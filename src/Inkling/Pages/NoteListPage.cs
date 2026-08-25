using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Inkling.Commands;
using Inkling.Core;
using Inkling.Properties;

namespace Inkling.Pages;

/// <summary>
/// 筆記清單與搜索。
///
/// 用 <see cref="DynamicListPage"/> 而不是 <see cref="ListPage"/>，是為了自己掌控過濾邏輯:
/// 內建的過濾只看標題，而需求要的是標題與內文都能搜。
/// </summary>
internal sealed partial class NoteListPage : DynamicListPage, IDisposable
{
    private readonly INoteRepository _repository;
    private readonly InklingOptions _options;
    private readonly ISourceModeStore _sourceMode;

    /// <summary>
    /// 「新增筆記」那一頁，跟頂層命令同一個實例(<see cref="InklingCommandsProvider"/> 建的)。
    ///
    /// 掛在這裡是為了 <c>Ctrl+N</c>:CmdPal 的快速鍵只能掛在**當下選中那一列**的命令上
    /// (<c>CommandBarViewModel.CheckKeybinding</c>)，頁面層級沒有掛鍵的地方，
    /// 所以這一項會出現在每一則筆記的 <c>Ctrl+K</c> 選單裡。
    /// </summary>
    private readonly NewNotePage _newNotePage;

    /// <summary>
    /// 詳細窗格的「渲染 / 原始文字」切換鈕。
    ///
    /// 全部項目共用同一個命令實例，所以狀態一變，每個項目選單上的字就一起變 ——
    /// <c>CommandItem.Title</c> 沒有明確設定時會回落到命令的名字，而且它有訂閱
    /// 命令的屬性變更。狀態本身是全域的，見 <see cref="ISourceModeStore"/>。
    /// </summary>
    private readonly SourceModeToggle _toggleSource;

    private string _query = string.Empty;

    /// <summary>
    /// 項目快取。規則只有一條 —— 鍵要帶 Version 與所有影響內容的設定值 ——
    /// 「為什麼」寫在 <see cref="VersionedItemsCache{TKey}"/> 上，三個清單頁共用。
    /// </summary>
    private readonly VersionedItemsCache<(int Version, string Query, bool ShowSource)> _cache = new();

    /// <summary>
    /// 目前列出來的每一則筆記，連同它的清單項目物件。
    ///
    /// 整個陣列一次換掉而不是就地增刪:建清單跟按下切換鍵是兩個不同的
    /// 跨進程呼叫，可能落在不同執行緒上，邊列舉邊改 List 會直接炸。
    /// </summary>
    private (Note Note, ListItem Item)[] _shown = [];

    /// <summary>
    /// 清單項物件的分配。**每次重建清單都給一批全新的 <see cref="ListItem"/> 的話，
    /// 選取每一次都會被推回第一列** —— CmdPal 認的是物件識別，不是我們的筆記身分。
    /// 規則與踩過的坑寫在 <see cref="NoteItemSlots"/>。
    /// </summary>
    private readonly NoteItemSlots _slots = new();

    /// <summary>
    /// 尾端那兩列提示。**跟筆記那幾列一樣要是長壽物件**:每次重建都新做一個的話，
    /// CmdPal 眼中那一列就是「被移除又插回來」，選取正好停在上面時會被踢走。
    /// 副標是固定的資源字串，只有標題帶著數字，所以只有標題要就地更新。
    /// </summary>
    private readonly ListItem _truncatedNotice = new(new NoOpCommand())
    {
        Subtitle = Resources.ListPageMoreResultsSubtitle,
        Icon = Icons.Note,
    };

    private readonly ListItem _skippedNotice = new(new NoOpCommand())
    {
        Subtitle = Resources.ListPageSkippedFilesSubtitle,
        Icon = Icons.Note,
    };

    /// <summary>
    /// 空白狀態那一列。留著參照是為了依查詢就地換文案(見 UpdateEmptyContent)——
    /// <c>ICommandItem</c> 在 IDL 裡就繼承 <c>INotifyPropChanged</c>,CmdPal 對它無條件訂閱，
    /// 走這條一定收得到(<c>IDetails</c> 就不行，見 RefreshDetails)。
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

        // 切換的回呼傳 null:這一頁活得跟擴展進程一樣久，直接訂閱事件就好 ——
        // 那條路連「在預覽頁上切的」也收得到，回呼只收得到自己按的那一次。
        _toggleSource = new SourceModeToggle(_sourceMode);
        _sourceMode.ShowSourceChanged += OnShowSourceChanged;

        Id = CommandIds.List;
        Icon = Icons.Note;
        Title = "Inkling";
        Name = Resources.CommandOpen;
        PlaceholderText = Resources.ListPagePlaceholder;
        ShowDetails = true;

        // 命令直接掛快速記下頁(跟頂層命令同一個實例):清單項的命令是 IPage 時 CmdPal
        // 會導覽過去，所以這一列的 Enter 真的能帶使用者去記下第一則 ——
        // 而不是給了指示(「用『快速記下』…」)卻按下去沒反應。
        _emptyContent = new CommandItem(capturePage)
        {
            Title = Resources.ListPageEmptyTitle,

            // 引用的是快速記下那個頂層命令的標題，所以拿同一條資源去填 ——
            // 寫死的話翻譯改了一邊，這句話就會指向一個畫面上不存在的命令。
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

        // 這裡**刻意**用預設值(也就是「更新完順便選第一列」)。使用者剛改了搜尋字，
        // 結果換了一批，選取本來就該回到最上面 —— 對照 OnRepositoryChanged。
        RaiseItemsChanged();
    }

    public override IListItem[] GetItems()
    {
        // 鍵帶上 Version 的理由見 VersionedItemsCache —— 只看查詢字串的話，
        // 表現出來就是「筆記明明存好了，清單卻說還沒有任何筆記」。
        //
        // 原始文字模式也要帶:它決定每一列的 Details 長什麼樣，而且可能是在別的頁面
        // (預覽頁的 Ctrl+U)切掉的。切換本身不會重建清單 —— 見 RefreshDetails ——
        // 這裡是為了下一次真的重建時拿到的是新模式。
        return _cache.Get(
            (_repository.Version, _query, _sourceMode.ShowSource), () => BuildItems(_query));
    }

    private IListItem[] BuildItems(string query)
    {
        var matches = NoteSearch.Filter(_repository.GetAll(), query);

        UpdateEmptyContent(query, matches.Count);

        // 先定案要列哪幾則，再交給 NoteItemSlots 決定每一則坐哪一個既有的項目物件。
        var listed = matches.Take(_options.MaxResults).ToArray();
        var slots = _slots.Assign(listed, CreateItem, ApplyNote);

        var items = new List<IListItem>(slots.Length + 2);
        var shown = new (Note, ListItem)[slots.Length];

        for (var i = 0; i < slots.Length; i++)
        {
            items.Add(slots[i]);
            shown[i] = (listed[i], slots[i]);
        }

        _shown = shown;
        DiagnosticLog.Write($"BuildItems: query='{query}' matched {matches.Count}, listed {slots.Length}");

        // 被截掉就明講，不要讓使用者以為筆記不見了。
        if (matches.Count > _options.MaxResults)
        {
            _truncatedNotice.Title =
                Strings.Format(Resources.ListPageMoreResults, matches.Count - _options.MaxResults);

            items.Add(_truncatedNotice);
        }

        // 讀不出來的檔案同理，而且更需要講:那一則是真的存在、只是這次讀不到
        // (被別的程式鎖住、編碼壞掉)，使用者不會知道它為什麼從清單上消失了。
        // repository 一直有在數，只是以前沒有任何人讀那個數字。
        //
        // **不受查詢字影響**:讀不出來就不知道它的標題，篩不了，所以永遠掛在最後一列。
        if (_repository.SkippedFileCount > 0)
        {
            _skippedNotice.Title =
                Strings.Format(Resources.ListPageSkippedFiles, _repository.SkippedFileCount);

            items.Add(_skippedNotice);
        }

        return [.. items];
    }

    /// <summary>
    /// 空白提示有兩種，要分開講:**真的沒有筆記**(引導去快速記下)與**查詢沒有命中**。
    ///
    /// CmdPal 的 <c>ShowEmptyContent</c> 只看篩完的項目數是不是零，不看搜尋框裡有沒有字
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

    /// <summary>
    /// <see cref="ListItem"/> 的建構子非給一個命令不可，而真正的命令要到
    /// <see cref="ApplyNote"/> 才裝得起來。這個實例只活到那一行為止。
    /// </summary>
    private static readonly NoOpCommand Placeholder = new();

    /// <summary>
    /// 新做一列。圖示在這裡設而不在 <see cref="ApplyNote"/> 裡 —— 這一頁每一列都是同一個，
    /// 放進去只會讓每次重整都多發一次跨進程通知。(刪除頁不一樣，那裡圖示分內外。)
    /// </summary>
    private ListItem CreateItem(Note note)
    {
        var item = new ListItem(Placeholder) { Icon = Icons.Note };

        ApplyNote(item, note);
        return item;
    }

    /// <summary>
    /// 把一則筆記的內容寫進某一列 —— **可能是剛做好的，也可能是上一輪留下來、
    /// 這一輪換人坐的槽**(見 <see cref="NoteItemSlots"/>)。所以「這一列長什麼樣、
    /// 按下去做什麼」**每一項都要設一遍**，不能假設誰沒變:漏掉一項的症狀是
    /// 那一列顯示甲、命令卻還綁著乙，而且靜悄悄的。
    ///
    /// **同一則筆記的內容變了不會走到這裡** —— <see cref="NoteItemSlots"/> 會給它一列
    /// 全新的。就地改一個 CmdPal 已經建好 view model 的清單項會打壞使用者當下看的畫面，
    /// 而「內容變了」最常見的來源正是他在編輯那一則;理由與實測寫在那個類別上。
    /// </summary>
    private void ApplyNote(ListItem item, Note note)
    {
        item.Command = new NotePreviewPage(_repository, note, _sourceMode);
        item.Title = note.Title;
        item.Subtitle = note.Summary;
        item.Details = BuildDetails(note);
        item.MoreCommands = BuildCommands(note);

        // 同一個 id 出現在兩個檔案上 = 雲端硬碟的衝突副本(見 Note.HasDuplicateId)。
        // 兩列的標題與內文可能一模一樣，不標的話使用者根本不會發現多了一份。
        // 走 Tags 而不是改副標:副標是摘要，那是使用者要讀的內容。**這條路跨進程是通的**
        // —— 跟 ListItem.Details 相反(見 RefreshDetails):ICommandItem 在 IDL 裡就繼承
        // INotifyPropChanged,CmdPal 對它無條件訂閱，而且安裝版的 UpdateTags /
        // VisibleTags / TagViewModel 都掃得到。
        item.Tags = BaseTags(note);
    }

    /// <summary>
    /// 一列平常掛著的標籤。目前只有「衝突副本」一種。
    ///
    /// 這裡曾經還有第二種:複製內文之後在那一列閃一個「已複製」(<c>FlashTag</c>),
    /// 連同一個計時器與兩個欄位。**2026-08-23 整組移除** —— 它存在的唯一理由是
    /// 「複製完要留在畫面上，所以一個 toast 都不能發」，而那個前提量過之後是假的。
    /// 現在三個畫面共用同一則帶標題的訊息(底部的 <c>InfoBar</c>，面板留在原地),
    /// 見 <see cref="Commands.CopyNoteBodyCommand"/>。
    /// </summary>
    private static ITag[] BaseTags(Note note) =>
        note.HasDuplicateId ? [new Tag(Resources.ListPageConflictTag)] : [];

    /// <summary>
    /// 一則筆記的 <c>Ctrl+K</c> 選單。編輯 / 複製 / 開啟那幾項跟預覽頁、記下頁共用
    /// <see cref="NoteCommands"/> 那一份組裝，鍵位全部來自 <see cref="Shortcuts"/>,
    /// 挑鍵的理由寫在那裡。
    ///
    /// 順序有意義:**第一項會被 CmdPal 當成次要命令放上底部工具列**(<c>Ctrl+Enter</c>),
    /// 所以編輯一定排第一;其餘按「看 → 拿 → 出去 → 刪掉」由輕到重排，
    /// 刪除排最後 —— 它是這裡唯一不可逆的動作。
    /// </summary>
    private IContextItem[] BuildCommands(Note note) =>
    [
        NoteCommands.Edit(_repository, note),
        _toggleSource.CreateItem(Resources.ToggleSourceSubtitle),
        // 複製完留在清單頁，回饋走一則帶標題的 toast(它拿不到前景，收不掉面板)。
        NoteCommands.CopyBody(new CopyNoteBodyCommand(note.Body, note.Title)),
        NoteCommands.OpenInEditor(note),
        NoteCommands.OpenFileLocation(note),

        // **這一項跟選中的那一則筆記無關**，但只能掛在這裡:CmdPal 的快速鍵是拿
        // 當下選中項的命令去比對的(CommandBarViewModel.CheckKeybinding),
        // 頁面層級沒有掛鍵的地方。排在筆記自己的動作後面、刪除前面 ——
        // 前面那幾項講的是「這一則」，它講的是「下一則」，而刪除永遠留在最後。
        //
        // 命令是一個 IPage,CmdPal 對頁面的處理是導覽，所以按下去真的會開表單。
        // 清單是空的時候按不到(那時沒有選中項)，但那個情境的 Enter 本來就會帶去
        // 快速記下頁，見 _emptyContent。
        new CommandContextItem(_newNotePage)
        {
            Title = Resources.CommandNewNote,
            Subtitle = Resources.ProviderNewNoteSubtitle,
            Icon = Icons.Add,
            RequestedShortcut = Shortcuts.NewNote,
        },

        // **刪除的鍵位是 `Ctrl+D`，不是 `Delete` 也不是 `Ctrl+Delete`。**
        // 後兩個是搜尋框的標準編輯鍵(刪右邊一個字 / 刪右邊一個詞)，而快速鍵比 TextBox
        // 先收到鍵(tunneling 階段的 `ShellPage_OnPreviewKeyDown`)，綁走等於把它們從
        // 搜尋框拿掉。`Ctrl+D` 在文字框裡沒有標準語意，CmdPal 自己也沒佔用。
        //
        // 它好按，所以也容易誤按 —— 那正是這個鍵位上一次被整個拿掉的理由。
        // 防線是這一列一定會跳確認框，而且刪掉的檔案進資源回收筒，兩道都還在。
        //
        // `IsCritical` 讓這一項在選單裡變紅(圖示、標題、鍵位都套
        // `SystemFillColorCriticalBrush`)—— SDK 的 IDL 對這個屬性的註解就是
        // 「make this red」。這是擴展唯一碰得到的紅色:底部工具列的按鈕與確認框的按鈕
        // 都沒有對應的樣式開口，見 docs/design-notes.md〈刪除的紅色只有一個地方碰得到〉。
        new CommandContextItem(CreateDeleteCommand(note))
        {
            Title = Resources.CommandDelete,
            Subtitle = Resources.CommandDeleteSubtitle,
            RequestedShortcut = Shortcuts.Delete,
            IsCritical = true,
        },
    ];

    /// <summary>
    /// 刪除鍵按下去先跳確認框，確認了才真的刪。
    ///
    /// 確認不是自己畫的:命令回傳 <see cref="CommandResult.Confirm"/>,CmdPal 就會替我們
    /// 跳一個對話框，按下主要按鈕之後才去跑 <see cref="ConfirmationArgs.PrimaryCommand"/>。
    ///
    /// <c>IsPrimaryCommandCritical</c> 這裡刻意**不設**。它聽起來只是「把按鈕標成危險」,
    /// 但上游拿它做的事是 <c>dialog.DefaultButton = ContentDialogButton.Close</c> ——
    /// 也就是把預設按鈕設成「取消」,Enter 下去等於放棄。單則刪除有資源回收筒兜底，
    /// 不值得為此讓每次刪除都多按一次方向鍵。刪除全部那一頁上的兩個批次刪除維持 critical。
    ///
    /// **這個旗標在 0.11.11762.0 安裝版上是真的有作用的，所以不設是一個實質決定，不是空手勢。**
    /// 2026-08-22 實機驗過:設 true 的三個確認框焦點落在「取消」，沒設的兩個落在「刪除」。
    /// 這裡不設，按下 <c>Ctrl+D</c> 再按 Enter 就刪掉了 —— 那正是想要的。
    ///
    /// (這段以前寫著「安裝版掃不到 <c>set_DefaultButton</c>，所以設不設畫面完全一樣」。
    /// **那是錯的**:<c>Microsoft.CmdPal.UI.exe</c> 是 NativeAOT 影像，byte-scan
    /// 可以證實、不能證否。詳見 docs/design-notes.md〈確認框的按鈕沒有顏色，也沒有「危險」樣式〉。)
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
    /// 有人切了原始文字模式 —— 可能是這一頁的 <c>Ctrl+U</c>，也可能是預覽頁的。
    ///
    /// 狀態是全域的(<see cref="ISourceModeStore"/>)，所以兩種來源在這裡沒有分別:
    /// 更新選單上那一項的字，再把已經顯示的項目換上新的詳細窗格。
    /// </summary>
    private void OnShowSourceChanged(object? sender, EventArgs e)
    {
        _toggleSource.Sync();

        RefreshDetails();
        DiagnosticLog.Write($"OnShowSourceChanged: showSource={_sourceMode.ShowSource}");
    }

    /// <summary>
    /// 把每一則已顯示筆記的 <see cref="ListItem.Details"/> 整個換掉，讓 CmdPal 重讀。
    ///
    /// 這裡不呼叫 <c>RaiseItemsChanged</c>:那會讓 CmdPal 重新拿一次清單，
    /// 而它是用 <c>IListItem</c> 的物件識別去快取 viewmodel 的 —— 想讓它重讀詳細內容
    /// 就得換掉整批項目物件，而整份清單被翻新一次，選中項就有機會跑掉。按下 Ctrl+U 的當下
    /// 正在看某一則筆記，跳走的話這個功能就沒有意義了。
    ///
    /// 為什麼是換掉整個 Details 而不是就地改它的屬性(那樣更省):
    /// 因為那條路在跨進程時是斷的。CmdPal 的 <c>DetailsViewModel</c> 是全專案唯一
    /// 用執行期型別測試(<c>model is INotifyPropChanged</c>)決定要不要訂閱的 ——
    /// 因為 SDK 的 <c>IDetails</c> 沒有宣告成可觀察介面。那個 QI 跨不過 out-of-process
    /// 邊界，而 <c>BaseObservable.OnPropertyChanged</c> 又把例外整個吞掉，
    /// 結果就是改了值、通知石沉大海、畫面要重進頁面才會更新(實測過)。
    ///
    /// <c>ICommandItem</c> 則是在 IDL 裡就繼承 <c>INotifyPropChanged</c>,
    /// <c>CommandItemViewModel</c> 對它是無條件訂閱，所以走這條一定收得到。
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

    private void OnRepositoryChanged(object? sender, EventArgs e)
    {
        // Version 已經讓下一次 GetItems 自己重建了;這裡的重點是主動通知 CmdPal
        // 立刻來拿新的清單，讓正開著的頁面即時更新。
        //
        // **參數不能省。** 預設值會讓 CmdPal 順手把選取推回第一列 —— 刪掉一則、
        // 或別台機器同步下來一則，使用者正看著的那一列就這樣沒了。見 CmdPalRefresh。
        RaiseItemsChanged(CmdPalRefresh.KeepSelection);
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
    }
}
