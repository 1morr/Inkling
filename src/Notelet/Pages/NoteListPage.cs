using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Notelet.Commands;
using Notelet.Core;

namespace Notelet.Pages;

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
    private readonly NoteletOptions _options;

    /// <summary>
    /// 詳細窗格的「渲染 / 原始文字」切換鈕。
    ///
    /// 全部項目共用同一個實例,所以改一次 <see cref="Command.Name"/>,
    /// 每個項目選單上的字就一起變 —— <c>CommandItem.Title</c> 沒有明確設定時
    /// 會回落到命令的名字,而且它有訂閱命令的屬性變更。
    /// </summary>
    private readonly AnonymousCommand _toggleSource;

    private string _query = string.Empty;
    private IListItem[]? _items;
    private string? _itemsQuery;
    private int _itemsVersion = -1;

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

    private bool _showSource;
    private bool _disposed;

    public NoteListPage(INoteRepository repository, NoteletOptions options)
    {
        _repository = repository;
        _options = options;

        _tagTimer = new System.Threading.Timer(_ => ClearTag(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        _toggleSource = new AnonymousCommand(ToggleSource)
        {
            Name = ToggleSourceName,
            Icon = Icons.Source,
            Result = CommandResult.KeepOpen(),
        };

        Id = CommandIds.List;
        Icon = Icons.Note;
        Title = "Notelet";
        Name = "開啟";
        PlaceholderText = "搜索標題與內文…";
        ShowDetails = true;

        EmptyContent = new CommandItem(new NoOpCommand())
        {
            Title = "還沒有任何筆記",
            Subtitle = "用「Notelet:快速記下」記下第一則",
            Icon = Icons.Note,
        };

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
        // GetItems 會被頻繁呼叫,同一個查詢字串不重建項目。
        //
        // 但快取的鍵一定要帶上 repository 的 Version:只看查詢字串的話,
        // 新增一則筆記之後再回到清單頁,拿到的還是舊的那份結果 ——
        // 表現出來就是「筆記明明存好了,清單卻說還沒有任何筆記」。
        var version = _repository.Version;

        if (_items is not null
            && _itemsVersion == version
            && string.Equals(_itemsQuery, _query, StringComparison.Ordinal))
        {
            return _items;
        }

        _items = BuildItems(_query);
        _itemsQuery = _query;
        _itemsVersion = version;
        return _items;
    }

    /// <summary>選單上顯示的字,講的是「按下去之後會看到什麼」。</summary>
    private string ToggleSourceName => _showSource ? "顯示渲染後的預覽" : "顯示原始文字";

    private IListItem[] BuildItems(string query)
    {
        var matches = NoteSearch.Filter(_repository.GetAll(), query);

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
        DiagnosticLog.Write($"BuildItems: query='{query}' 命中 {matches.Count} 則,列出 {shown.Count} 則");

        // 被截掉就明講,不要讓使用者以為筆記不見了。
        if (matches.Count > _options.MaxResults)
        {
            items.Add(new ListItem(new NoOpCommand())
            {
                Title = $"還有 {matches.Count - _options.MaxResults} 則沒顯示",
                Subtitle = "再多打幾個字縮小範圍",
                Icon = Icons.Note,
            });
        }

        return [.. items];
    }

    private ListItem CreateItem(Note note) => new(new NotePreviewPage(_repository, note))
    {
        Title = note.Title,
        Subtitle = note.Summary,
        Icon = Icons.Note,
        Details = BuildDetails(note),
        MoreCommands = BuildCommands(note),
    };

    /// <summary>
    /// 一則筆記的 <c>Ctrl+K</c> 選單。鍵位全部來自 <see cref="Shortcuts"/>,挑鍵的理由寫在那裡。
    ///
    /// 順序有意義:**第一項會被 CmdPal 當成次要命令放上底部工具列**(<c>Ctrl+Enter</c>),
    /// 所以編輯一定排第一;其餘按「看 → 拿 → 出去 → 刪掉」由輕到重排,
    /// 刪除排最後 —— 它是這裡唯一不可逆的動作。
    /// </summary>
    private IContextItem[] BuildCommands(Note note) =>
    [
        new CommandContextItem(new NoteEditPage(_repository, note))
        {
            Title = "編輯",
            Icon = Icons.Edit,
            RequestedShortcut = Shortcuts.Edit,
        },
        new CommandContextItem(_toggleSource)
        {
            // 這裡刻意不設 Title:讓它回落到 _toggleSource.Name,
            // 切換之後選單上的字才會跟著從「顯示原始文字」變成「顯示渲染後的預覽」。
            Subtitle = "不進預覽頁也能選取、複製原始 Markdown",
            RequestedShortcut = Shortcuts.ToggleSource,
        },
        // 複製完**留在清單頁**,所以不發 toast(toast 會搶焦點,主視窗一失焦就自我隱藏)。
        // 回饋改成在那一列打一個標籤,見 FlashTag。
        new CommandContextItem(new CopyNoteBodyCommand(note.Body, message => FlashTag(note.Id, message)))
        {
            Title = "複製內文",
            Subtitle = "不含 front matter",
            Icon = Icons.Copy,
            RequestedShortcut = Shortcuts.CopyBody,
        },
        new CommandContextItem(new OpenUrlCommand(note.FilePath))
        {
            Title = "在預設編輯器開啟",
            Icon = Icons.OpenExternal,
            RequestedShortcut = Shortcuts.OpenExternal,
        },

        // 「開啟檔案位置」直接用 toolkit 現成的命令(它跑的是 `explorer.exe /select,"<路徑>"`),
        // 自己寫一個只會多一份 Process.Start 的錯誤處理。Name 要換成中文:
        // toolkit 給的是它自己資源檔裡的英文字串,而這一項有機會出現在底部工具列上。
        new CommandContextItem(new ShowFileInFolderCommand(note.FilePath) { Name = "開啟檔案位置" })
        {
            Title = "開啟檔案位置",
            Subtitle = "在檔案總管裡選中這個檔案",
            Icon = Icons.FileLocation,
            RequestedShortcut = Shortcuts.OpenFileLocation,
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
        // 都沒有對應的樣式開口,見 README〈刪除的紅色只有一個地方碰得到〉。
        new CommandContextItem(CreateDeleteCommand(note))
        {
            Title = "刪除",
            Subtitle = "移到資源回收筒",
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
    /// **注意 0.11 安裝版根本沒有那條路**:整個套件掃不到 <c>set_DefaultButton</c>
    /// (同一段程式碼的 <c>set_PrimaryButtonText</c> / <c>set_CloseButtonText</c> 都掃得到,
    /// 所以不是掃描失準)。也就是說現在這個旗標設不設**畫面上完全一樣**,
    /// 兩邊都是 <c>DefaultButton.None</c> + 焦點落在主要按鈕。維持現在的用法是為了
    /// 之後 CmdPal 更新上來時語意正確。詳見 README〈確認框的預設按鈕是反過來的〉。
    /// </summary>
    private AnonymousCommand CreateDeleteCommand(Note note) => new(() => { })
    {
        Name = "刪除",
        Icon = Icons.Delete,
        Result = CommandResult.Confirm(new ConfirmationArgs
        {
            Title = "刪除這則筆記?",
            Description = $"「{note.Title}」會被移到資源回收筒。",
            PrimaryCommand = new DeleteNoteCommand(_repository, note),
        }),
    };

    private Details BuildDetails(Note note) => new()
    {
        Title = note.Title,
        Body = BuildDetailsBody(note),

        // 寬度固定最寬(清單:詳情 = 1:1),沒有設定項也沒有快速鍵。清單那一邊只有標題與
        // 時間,寬一點也不多給什麼資訊;右邊是筆記本文,窄一檔就多折斷幾十行,看原始文字時
        // 特別有感。曾經做過一個三檔循環的 Ctrl+D 加一個設定項,代價是設定頁與清單頁之間
        // 一整條雙向同步線,而實際上永遠停在最寬 —— 移除的理由見 README〈詳細面板寬度固定在最寬〉。
        //
        // **一定要明著寫**:ContentSize 的 0 是 Small,`new Details()` 不設就是最窄那一檔
        // (實測過)。CmdPal 也只認 Small / Medium / Large,對應 3:1 / 2:1 / 1:1
        // (它的 DetailsSizeToGridLengthConverter),沒有無段調整 —— 整個介面裡連一個
        // GridSplitter 都沒有,所以「寬」就是能給的上限。
        Size = ContentSize.Large,
    };

    private string BuildDetailsBody(Note note)
    {
        if (note.Body.Length == 0)
        {
            return "_(沒有內文)_";
        }

        // 渲染模式的換行處理要跟預覽頁一致,否則同一則筆記在兩個地方長得不一樣。
        return _showSource
            ? NotePreview.RenderSource(note.Body)
            : NotePreview.PreserveLineBreaks(note.Body);
    }

    /// <summary>詳細窗格在「渲染」與「原始文字」之間切換。</summary>
    private void ToggleSource()
    {
        _showSource = !_showSource;
        _toggleSource.Name = ToggleSourceName;

        RefreshDetails();
        DiagnosticLog.Write($"ToggleSource: showSource={_showSource}");
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

        DiagnosticLog.Write($"RefreshDetails: 換掉 {shown.Length} 則的 Details");
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
    private void FlashTag(string noteId, string text)
    {
        ClearTag();

        foreach (var (note, item) in _shown)
        {
            if (!string.Equals(note.Id, noteId, StringComparison.Ordinal))
            {
                continue;
            }

            item.Tags = [new Tag(text)];
            _taggedItem = item;
            _tagTimer.Change(TagDuration, Timeout.InfiniteTimeSpan);

            DiagnosticLog.Write($"FlashTag: '{text}' 掛在 {noteId} 上");
            return;
        }
    }

    /// <summary>把標籤收掉。時間到、換一列複製、或整份清單重建時都會走到。</summary>
    private void ClearTag()
    {
        var item = Interlocked.Exchange(ref _taggedItem, null);

        if (item is not null)
        {
            item.Tags = [];
        }
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
        _tagTimer.Dispose();
    }
}
