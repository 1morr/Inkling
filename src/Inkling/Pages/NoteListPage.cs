using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Inkling.Commands;
using Inkling.Core;
using Inkling.Properties;

namespace Inkling.Pages;

/// <summary>
/// 筆記清單與搜索。
///
/// 用 <see cref="DynamicListPage"/> 而不是 <see cref="ListPage"/>,是為了自己掌控過濾邏輯:
/// 內建的過濾只看標題,而需求要的是標題與內文都能搜。
/// </summary>
internal sealed partial class NoteListPage : DynamicListPage, IDisposable
{
    /// <summary>「已複製」那個標籤留多久。跟 CmdPal 自己的 toast 一樣長。</summary>
    private static readonly TimeSpan TagDuration = TimeSpan.FromMilliseconds(2500);

    private readonly INoteRepository _repository;
    private readonly InklingOptions _options;
    private readonly ISourceModeStore _sourceMode;

    /// <summary>
    /// 「新增筆記」那一頁,跟頂層命令同一個實例(<see cref="InklingCommandsProvider"/> 建的)。
    ///
    /// 掛在這裡是為了 <c>Ctrl+N</c>:CmdPal 的快速鍵只能掛在**當下選中那一列**的命令上
    /// (<c>CommandBarViewModel.CheckKeybinding</c>),頁面層級沒有掛鍵的地方,
    /// 所以這一項會出現在每一則筆記的 <c>Ctrl+K</c> 選單裡。
    /// </summary>
    private readonly NewNotePage _newNotePage;

    /// <summary>
    /// 詳細窗格的「渲染 / 原始文字」切換鈕。
    ///
    /// 全部項目共用同一個命令實例,所以狀態一變,每個項目選單上的字就一起變 ——
    /// <c>CommandItem.Title</c> 沒有明確設定時會回落到命令的名字,而且它有訂閱
    /// 命令的屬性變更。狀態本身是全域的,見 <see cref="ISourceModeStore"/>。
    /// </summary>
    private readonly SourceModeToggle _toggleSource;

    private string _query = string.Empty;

    /// <summary>
    /// 項目快取。規則只有一條 —— 鍵要帶 Version 與所有影響內容的設定值 ——
    /// 「為什麼」寫在 <see cref="VersionedItemsCache{TKey}"/> 上,三個清單頁共用。
    /// </summary>
    private readonly VersionedItemsCache<(int Version, string Query, bool ShowSource)> _cache = new();

    /// <summary>
    /// 目前列出來的每一則筆記,連同它的清單項目物件。
    ///
    /// 整個陣列一次換掉而不是就地增刪:建清單跟按下切換鍵是兩個不同的
    /// 跨進程呼叫,可能落在不同執行緒上,邊列舉邊改 List 會直接炸。
    /// </summary>
    private (Note Note, ListItem Item)[] _shown = [];

    /// <summary>
    /// 「已複製」那個標籤的計時器,時間到自己把標籤收掉。
    ///
    /// 跟 CmdPal 自己的 toast 一樣是 2.5 秒(<c>ToastWindow.VisibleDuration</c>),
    /// 讓兩種回饋的節奏一致。
    /// </summary>
    private readonly System.Threading.Timer _tagTimer;

    /// <summary>目前掛著標籤的那一列,清的時候只碰它一個。</summary>
    private ListItem? _taggedItem;

    /// <summary>
    /// 那一列在「已複製」蓋上去之前本來掛著的標籤(見 <see cref="BaseTags"/>)。
    /// 收標籤時要回到這一份,不是清成空的 —— 否則複製一次就把衝突標記弄丟了。
    /// </summary>
    private ITag[] _taggedBase = [];

    /// <summary>
    /// 空白狀態那一列。留著參照是為了依查詢就地換文案(見 UpdateEmptyContent)——
    /// <c>ICommandItem</c> 在 IDL 裡就繼承 <c>INotifyPropChanged</c>,CmdPal 對它無條件訂閱,
    /// 走這條一定收得到(<c>IDetails</c> 就不行,見 RefreshDetails)。
    /// </summary>
    private readonly CommandItem _emptyContent;

    private bool _disposed;

    public NoteListPage(
        INoteRepository repository,
        InklingOptions options,
        QuickCapturePage capturePage,
        NewNotePage newNotePage,
        ISourceModeStore sourceMode)
    {
        _repository = repository;
        _options = options;
        _newNotePage = newNotePage;
        _sourceMode = sourceMode;

        _tagTimer = new System.Threading.Timer(_ => ClearTag(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        // 切換的回呼傳 null:這一頁活得跟擴展進程一樣久,直接訂閱事件就好 ——
        // 那條路連「在預覽頁上切的」也收得到,回呼只收得到自己按的那一次。
        _toggleSource = new SourceModeToggle(_sourceMode);
        _sourceMode.ShowSourceChanged += OnShowSourceChanged;

        Id = CommandIds.List;
        Icon = Icons.Note;
        Title = "Inkling";
        Name = Resources.CommandOpen;
        PlaceholderText = Resources.ListPagePlaceholder;
        ShowDetails = true;

        // 命令直接掛快速記下頁(跟頂層命令同一個實例):清單項的命令是 IPage 時 CmdPal
        // 會導覽過去,所以這一列的 Enter 真的能帶使用者去記下第一則 ——
        // 而不是給了指示(「用『快速記下』…」)卻按下去沒反應。
        _emptyContent = new CommandItem(capturePage)
        {
            Title = Resources.ListPageEmptyTitle,

            // 引用的是快速記下那個頂層命令的標題,所以拿同一條資源去填 ——
            // 寫死的話翻譯改了一邊,這句話就會指向一個畫面上不存在的命令。
            Subtitle = Strings.Format(Resources.ListPageEmptySubtitle, Resources.ProviderCapturePageTitle),
            Icon = Icons.Note,
        };

        EmptyContent = _emptyContent;

        // 別台機器經 OneDrive 同步下來、或使用者拿別的編輯器改了檔案時自動更新。
        _repository.Changed += OnRepositoryChanged;
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        if (string.Equals(oldSearch, newSearch, StringComparison.Ordinal))
        {
            return;
        }

        _query = newSearch;
        RaiseItemsChanged();
    }

    public override IListItem[] GetItems()
    {
        // 鍵帶上 Version 的理由見 VersionedItemsCache —— 只看查詢字串的話,
        // 表現出來就是「筆記明明存好了,清單卻說還沒有任何筆記」。
        //
        // 原始文字模式也要帶:它決定每一列的 Details 長什麼樣,而且可能是在別的頁面
        // (預覽頁的 Ctrl+U)切掉的。切換本身不會重建清單 —— 見 RefreshDetails ——
        // 這裡是為了下一次真的重建時拿到的是新模式。
        return _cache.Get(
            (_repository.Version, _query, _sourceMode.ShowSource), () => BuildItems(_query));
    }

    private IListItem[] BuildItems(string query)
    {
        var matches = NoteSearch.Filter(_repository.GetAll(), query);

        UpdateEmptyContent(query, matches.Count);

        var items = new List<IListItem>(Math.Min(matches.Count, _options.MaxResults) + 1);
        var shown = new List<(Note, ListItem)>(items.Capacity);

        foreach (var note in matches.Take(_options.MaxResults))
        {
            var item = CreateItem(note);

            items.Add(item);
            shown.Add((note, item));
        }

        // 標籤屬於上一份清單那些項目物件,整批換掉之後就沒有意義了。
        ClearTag();

        _shown = [.. shown];
        DiagnosticLog.Write($"BuildItems: query='{query}' matched {matches.Count}, listed {shown.Count}");

        // 被截掉就明講,不要讓使用者以為筆記不見了。
        if (matches.Count > _options.MaxResults)
        {
            items.Add(new ListItem(new NoOpCommand())
            {
                Title = Strings.Format(Resources.ListPageMoreResults, matches.Count - _options.MaxResults),
                Subtitle = Resources.ListPageMoreResultsSubtitle,
                Icon = Icons.Note,
            });
        }

        // 讀不出來的檔案同理,而且更需要講:那一則是真的存在、只是這次讀不到
        // (被別的程式鎖住、編碼壞掉),使用者不會知道它為什麼從清單上消失了。
        // repository 一直有在數,只是以前沒有任何人讀那個數字。
        //
        // **不受查詢字影響**:讀不出來就不知道它的標題,篩不了,所以永遠掛在最後一列。
        if (_repository.SkippedFileCount > 0)
        {
            items.Add(new ListItem(new NoOpCommand())
            {
                Title = Strings.Format(Resources.ListPageSkippedFiles, _repository.SkippedFileCount),
                Subtitle = Resources.ListPageSkippedFilesSubtitle,
                Icon = Icons.Note,
            });
        }

        return [.. items];
    }

    /// <summary>
    /// 空白提示有兩種,要分開講:**真的沒有筆記**(引導去快速記下)與**查詢沒有命中**。
    ///
    /// CmdPal 的 <c>ShowEmptyContent</c> 只看篩完的項目數是不是零,不看搜尋框裡有沒有字
    /// (<c>ListViewModel</c>:IsInitialized、FilteredItems.Count 為零、不在載入中)。
    /// 所以「資料夾裡有幾百則筆記、打一個搜不到的字」也會走到空白提示 ——
    /// 那時候說「還沒有任何筆記」會讓人以為筆記不見了(真機重現過)。
    ///
    /// 就地改 Title/Subtitle 即時生效:<c>ICommandItem</c> 是無條件訂閱的
    /// (跟 QuickCapturePage 更新提示同一條路;<c>IDetails</c> 才是斷的)。
    /// </summary>
    private void UpdateEmptyContent(string query, int matchCount)
    {
        var noMatch = matchCount == 0 && !string.IsNullOrWhiteSpace(query);

        _emptyContent.Title = noMatch ? Resources.ListPageNoMatchTitle : Resources.ListPageEmptyTitle;
        _emptyContent.Subtitle = noMatch
            ? Resources.ListPageNoMatchSubtitle
            : Strings.Format(Resources.ListPageEmptySubtitle, Resources.ProviderCapturePageTitle);
    }

    private ListItem CreateItem(Note note) => new(new NotePreviewPage(_repository, note, _sourceMode))
    {
        Title = note.Title,
        Subtitle = note.Summary,
        Icon = Icons.Note,
        Details = BuildDetails(note),
        MoreCommands = BuildCommands(note),

        // 同一個 id 出現在兩個檔案上 = 雲端硬碟的衝突副本(見 Note.HasDuplicateId)。
        // 兩列的標題與內文可能一模一樣,不標的話使用者根本不會發現多了一份。
        // 走 Tags 而不是改副標:副標是摘要,那是使用者要讀的內容;而 Tags 這條路
        // 跨進程是通的(見 FlashTag)。
        Tags = BaseTags(note),
    };

    /// <summary>
    /// 一列平常掛著的標籤。目前只有「衝突副本」一種。
    ///
    /// 抽出來是因為 <see cref="FlashTag"/> 會暫時再掛一個「已複製」上去,
    /// 收回來的時候要回到這一份而不是空陣列 —— 否則複製一次,衝突標記就消失了。
    /// </summary>
    private static ITag[] BaseTags(Note note) =>
        note.HasDuplicateId ? [new Tag(Resources.ListPageConflictTag)] : [];

    /// <summary>
    /// 一則筆記的 <c>Ctrl+K</c> 選單。編輯 / 複製 / 開啟那幾項跟預覽頁、記下頁共用
    /// <see cref="NoteCommands"/> 那一份組裝,鍵位全部來自 <see cref="Shortcuts"/>,
    /// 挑鍵的理由寫在那裡。
    ///
    /// 順序有意義:**第一項會被 CmdPal 當成次要命令放上底部工具列**(<c>Ctrl+Enter</c>),
    /// 所以編輯一定排第一;其餘按「看 → 拿 → 出去 → 刪掉」由輕到重排,
    /// 刪除排最後 —— 它是這裡唯一不可逆的動作。
    /// </summary>
    private IContextItem[] BuildCommands(Note note) =>
    [
        NoteCommands.Edit(_repository, note),
        _toggleSource.CreateItem(Resources.ToggleSourceSubtitle),
        // 複製完**留在清單頁**,所以不發 toast(toast 會搶焦點,主視窗一失焦就自我隱藏)。
        // 回饋改成在那一列打一個標籤,見 FlashTag。
        NoteCommands.CopyBody(new CopyNoteBodyCommand(note.Body, message => FlashTag(note.FilePath, message))),
        NoteCommands.OpenInEditor(note),
        NoteCommands.OpenFileLocation(note),

        // **這一項跟選中的那一則筆記無關**,但只能掛在這裡:CmdPal 的快速鍵是拿
        // 當下選中項的命令去比對的(CommandBarViewModel.CheckKeybinding),
        // 頁面層級沒有掛鍵的地方。排在筆記自己的動作後面、刪除前面 ——
        // 前面那幾項講的是「這一則」,它講的是「下一則」,而刪除永遠留在最後。
        //
        // 命令是一個 IPage,CmdPal 對頁面的處理是導覽,所以按下去真的會開表單。
        // 清單是空的時候按不到(那時沒有選中項),但那個情境的 Enter 本來就會帶去
        // 快速記下頁,見 _emptyContent。
        new CommandContextItem(_newNotePage)
        {
            Title = Resources.CommandNewNote,
            Subtitle = Resources.ProviderNewNoteSubtitle,
            Icon = Icons.Add,
            RequestedShortcut = Shortcuts.NewNote,
        },

        // **刪除的鍵位是 `Ctrl+D`,不是 `Delete` 也不是 `Ctrl+Delete`。**
        // 後兩個是搜尋框的標準編輯鍵(刪右邊一個字 / 刪右邊一個詞),而快速鍵比 TextBox
        // 先收到鍵(tunneling 階段的 `ShellPage_OnPreviewKeyDown`),綁走等於把它們從
        // 搜尋框拿掉。`Ctrl+D` 在文字框裡沒有標準語意,CmdPal 自己也沒佔用。
        //
        // 它好按,所以也容易誤按 —— 那正是這個鍵位上一次被整個拿掉的理由。
        // 防線是這一列一定會跳確認框,而且刪掉的檔案進資源回收筒,兩道都還在。
        //
        // `IsCritical` 讓這一項在選單裡變紅(圖示、標題、鍵位都套
        // `SystemFillColorCriticalBrush`)—— SDK 的 IDL 對這個屬性的註解就是
        // 「make this red」。這是擴展唯一碰得到的紅色:底部工具列的按鈕與確認框的按鈕
        // 都沒有對應的樣式開口,見 docs/design-notes.md〈刪除的紅色只有一個地方碰得到〉。
        new CommandContextItem(CreateDeleteCommand(note))
        {
            Title = Resources.CommandDelete,
            Subtitle = Resources.CommandDeleteSubtitle,
            RequestedShortcut = Shortcuts.Delete,
            IsCritical = true,
        },
    ];

    /// <summary>
    /// 刪除鍵按下去先跳確認框,確認了才真的刪。
    ///
    /// 確認不是自己畫的:命令回傳 <see cref="CommandResult.Confirm"/>,CmdPal 就會替我們
    /// 跳一個對話框,按下主要按鈕之後才去跑 <see cref="ConfirmationArgs.PrimaryCommand"/>。
    ///
    /// <c>IsPrimaryCommandCritical</c> 這裡刻意**不設**。它聽起來只是「把按鈕標成危險」,
    /// 但上游拿它做的事是 <c>dialog.DefaultButton = ContentDialogButton.Close</c> ——
    /// 也就是把預設按鈕設成「取消」,Enter 下去等於放棄。單則刪除有資源回收筒兜底,
    /// 不值得為此讓每次刪除都多按一次方向鍵。刪除全部那一頁上的兩個批次刪除維持 critical。
    ///
    /// **這個旗標在 0.11.11762.0 安裝版上是真的有作用的,所以不設是一個實質決定,不是空手勢。**
    /// 2026-08-22 實機驗過:設 true 的三個確認框焦點落在「取消」,沒設的兩個落在「刪除」。
    /// 這裡不設,按下 <c>Ctrl+D</c> 再按 Enter 就刪掉了 —— 那正是想要的。
    ///
    /// (這段以前寫著「安裝版掃不到 <c>set_DefaultButton</c>,所以設不設畫面完全一樣」。
    /// **那是錯的**:<c>Microsoft.CmdPal.UI.exe</c> 是 NativeAOT 影像,byte-scan
    /// 可以證實、不能證否。詳見 docs/design-notes.md〈確認框的按鈕沒有顏色,也沒有「危險」樣式〉。)
    /// </summary>
    private AnonymousCommand CreateDeleteCommand(Note note) => new(() => { })
    {
        Name = Resources.CommandDelete,
        Icon = Icons.Delete,
        Result = CommandResult.Confirm(new ConfirmationArgs
        {
            Title = Resources.DeleteConfirmTitle,
            Description = Strings.Format(Resources.DeleteConfirmDescription, note.Title),
            PrimaryCommand = new DeleteNoteCommand(_repository, note),
        }),
    };

    private Details BuildDetails(Note note) => NoteDetails.For(note, _sourceMode.ShowSource);

    /// <summary>
    /// 有人切了原始文字模式 —— 可能是這一頁的 <c>Ctrl+U</c>,也可能是預覽頁的。
    ///
    /// 狀態是全域的(<see cref="ISourceModeStore"/>),所以兩種來源在這裡沒有分別:
    /// 更新選單上那一項的字,再把已經顯示的項目換上新的詳細窗格。
    /// </summary>
    private void OnShowSourceChanged(object? sender, EventArgs e)
    {
        _toggleSource.Sync();

        RefreshDetails();
        DiagnosticLog.Write($"OnShowSourceChanged: showSource={_sourceMode.ShowSource}");
    }

    /// <summary>
    /// 把每一則已顯示筆記的 <see cref="ListItem.Details"/> 整個換掉,讓 CmdPal 重讀。
    ///
    /// 這裡不呼叫 <c>RaiseItemsChanged</c>:那會讓 CmdPal 重新拿一次清單,
    /// 而它是用 <c>IListItem</c> 的物件識別去快取 viewmodel 的 —— 想讓它重讀詳細內容
    /// 就得換掉整批項目物件,而整份清單被翻新一次,選中項就有機會跑掉。按下 Ctrl+U 的當下
    /// 正在看某一則筆記,跳走的話這個功能就沒有意義了。
    ///
    /// 為什麼是換掉整個 Details 而不是就地改它的屬性(那樣更省):
    /// 因為那條路在跨進程時是斷的。CmdPal 的 <c>DetailsViewModel</c> 是全專案唯一
    /// 用執行期型別測試(<c>model is INotifyPropChanged</c>)決定要不要訂閱的 ——
    /// 因為 SDK 的 <c>IDetails</c> 沒有宣告成可觀察介面。那個 QI 跨不過 out-of-process
    /// 邊界,而 <c>BaseObservable.OnPropertyChanged</c> 又把例外整個吞掉,
    /// 結果就是改了值、通知石沉大海、畫面要重進頁面才會更新(實測過)。
    ///
    /// <c>ICommandItem</c> 則是在 IDL 裡就繼承 <c>INotifyPropChanged</c>,
    /// <c>CommandItemViewModel</c> 對它是無條件訂閱,所以走這條一定收得到。
    /// </summary>
    private void RefreshDetails()
    {
        var shown = _shown;

        foreach (var (note, item) in shown)
        {
            item.Details = BuildDetails(note);
        }

        DiagnosticLog.Write($"RefreshDetails: replaced Details on {shown.Length} items");
    }

    /// <summary>
    /// 在某一列右邊打一個短暫的標籤,<see cref="_tagTimer"/> 到時自己收掉。
    ///
    /// 這是「複製完不關面板」換來的問題的解:**不能用 toast**(它是另一個會搶焦點的視窗,
    /// 主視窗一失焦就自我隱藏,見 <see cref="Commands.DeleteNoteCommand"/>),
    /// 但剪貼簿看不見,完全沒有回饋又會讓人以為快速鍵壞了。
    ///
    /// 用 <see cref="ListItem.Tags"/> 是因為**這條路跨進程是通的** —— 跟
    /// <see cref="ListItem.Details"/> 相反(見 <see cref="RefreshDetails"/>):
    /// <c>ICommandItem</c> 在 IDL 裡就繼承 <c>INotifyPropChanged</c>,CmdPal 對它無條件訂閱,
    /// 而且安裝版的 <c>UpdateTags</c> / <c>VisibleTags</c> / <c>TagViewModel</c> 都掃得到。
    /// 這裡也不呼叫 <c>RaiseItemsChanged</c>:整份清單翻新一次,選中項就有機會跑掉,
    /// 而複製完的下一秒使用者通常還想留在同一列上。
    ///
    /// 計時器回呼跑在執行緒集區上,所以只碰 <see cref="_taggedItem"/> 這一個參考
    /// (用 <see cref="Interlocked"/> 換走),不去走 <see cref="_shown"/> 那個會整個被換掉的陣列。
    /// </summary>
    /// <param name="filePath">
    /// 要掛在哪一列。**認路徑不認 id** —— 同一個 id 可能對到兩個檔案(衝突副本),
    /// 用 id 找會把標籤掛到上面那一列去,而使用者複製的是下面那一列。
    /// </param>
    private void FlashTag(string filePath, string text)
    {
        ClearTag();

        foreach (var (note, item) in _shown)
        {
            if (!string.Equals(note.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // 疊在那一列本來就有的標籤上面,不是換掉 —— 衝突標記得留著。
            _taggedBase = BaseTags(note);
            item.Tags = [.. _taggedBase, new Tag(text)];
            _taggedItem = item;
            _tagTimer.Change(TagDuration, Timeout.InfiniteTimeSpan);

            DiagnosticLog.Write($"FlashTag: '{text}' attached to {note.Id}");
            return;
        }
    }

    /// <summary>
    /// 把「已複製」收掉,回到那一列平常的標籤。時間到、換一列複製、或整份清單重建時都會走到。
    /// </summary>
    private void ClearTag()
    {
        var item = Interlocked.Exchange(ref _taggedItem, null);

        if (item is not null)
        {
            item.Tags = _taggedBase;
        }

        _taggedBase = [];
    }

    private void OnRepositoryChanged(object? sender, EventArgs e)
    {
        // Version 已經讓下一次 GetItems 自己重建了;這裡的重點是主動通知 CmdPal
        // 立刻來拿新的清單,讓正開著的頁面即時更新。
        RaiseItemsChanged();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _repository.Changed -= OnRepositoryChanged;
        _sourceMode.ShowSourceChanged -= OnShowSourceChanged;
        _tagTimer.Dispose();
    }
}
