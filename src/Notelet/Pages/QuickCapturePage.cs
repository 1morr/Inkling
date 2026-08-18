using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Notelet.Commands;
using Notelet.Core;
using Notelet.Properties;

namespace Notelet.Pages;

/// <summary>
/// 快速記下:進到這一頁,打字,Enter 存檔。
///
/// 這是快速記下**唯一**的入口。曾經有一條走主搜尋框 fallback 的路(型別叫
/// <c>QuickCaptureFallbackItem</c>,已經整個移除,<c>git log --diff-filter=D</c> 找得到) ——
/// 那條路拿得到主搜尋框的字,不必跳頁,但代價是它得跟所有命令與應用程式擠在同一個清單裡。
/// 沒有前綴命中時我們只能把 <c>Title</c> 設成空字串把自己藏起來,而那招要成立,
/// 得靠 CmdPal 端把空標題的項目濾掉 —— 0.11.11762.0 在「Include in the Global result」
/// 這條路上沒有確實做到,結果就是每一次搜索都多出一個點不動的空列。
///
/// 這一頁把入口換成使用者自己設的 alias(例如 <c>n</c>)。按鍵數完全一樣
/// (<c>n</c> 空白 想法 Enter),差別只在中間會跳一次頁,換來的是主搜尋框從此完全乾淨,
/// 而且不再受 CmdPal 版本行為影響。
///
/// 跳頁還順便換來一件 fallback 結構上做不到的事:fallback 只有一列,這裡有一整個清單,
/// 所以「記下」底下可以直接列出標題相近的既有筆記,避免同一件事記兩遍。
/// </summary>
internal sealed partial class QuickCapturePage : DynamicListPage, IDisposable
{
    /// <summary>
    /// 底下最多列幾則既有筆記。
    ///
    /// 這一頁的主角是「記下」那一列,既有筆記只是拿來提醒「這件事你記過了」。
    /// 列太多會把畫面變成搜索結果,反而模糊掉這一頁的用途 —— 真要翻筆記請走清單頁。
    /// </summary>
    private const int MaxSimilarNotes = 5;

    private readonly INoteRepository _repository;
    private readonly ICaptureSeparatorStore _separatorStore;
    private readonly ICapturePreviewStore _previewStore;

    /// <summary>
    /// 還沒打字時那塊提示。留著參照是為了在分隔符改掉之後就地更新它的副標 ——
    /// <c>ICommandItem</c> 在 IDL 裡就繼承 <c>INotifyPropChanged</c>,CmdPal 對它無條件訂閱,
    /// 走這條一定收得到(<c>IDetails</c> 就不行,見 <c>NoteListPage.RefreshDetails</c>)。
    /// </summary>
    private readonly CommandItem _emptyContent;

    private string _query = string.Empty;
    private IListItem[]? _items;
    private string? _itemsQuery;
    private string? _itemsSeparator;
    private bool _itemsPreview;
    private int _itemsVersion = -1;
    private bool _disposed;

    public QuickCapturePage(
        INoteRepository repository,
        ICaptureSeparatorStore separatorStore,
        ICapturePreviewStore previewStore)
    {
        _repository = repository;
        _separatorStore = separatorStore;
        _previewStore = previewStore;

        var separator = separatorStore.CaptureSeparator;

        Id = CommandIds.QuickCapturePage;
        Icon = Icons.Add;
        Title = Resources.ProviderCapturePageTitle;
        Name = Resources.CommandOpen;
        PlaceholderText = PlaceholderFor(separator);

        _emptyContent = new CommandItem(new NoOpCommand())
        {
            Title = Resources.QuickCaptureEmptyTitle,
            Subtitle = HintFor(separator),
            Icon = Icons.Add,
        };

        EmptyContent = _emptyContent;

        // 別台機器經 OneDrive 同步下來、或使用者拿別的編輯器改了檔案時,
        // 底下那份「已經記過的」要跟著更新,否則提醒的是過期的內容。
        _repository.Changed += OnRepositoryChanged;

        // 設定頁改了分隔符,要更新的是使用者當下開著的這一個頁面實例 ——
        // 見 ICaptureSeparatorStore.CaptureSeparatorChanged 上的說明。
        _separatorStore.CaptureSeparatorChanged += OnCaptureSeparatorChanged;

        // 「記下後先看一眼」同理:它決定的是每一列上 Enter 與 Ctrl+Enter 各掛哪一條命令。
        _previewStore.CapturePreviewChanged += OnCapturePreviewChanged;
    }

    /// <summary>
    /// 提示裡的分隔符一律照使用者設的那個寫,不要寫死「分號」——
    /// 換成 <c>,,</c> 之後還教人打分號,那比沒有提示更糟。
    /// </summary>
    private static string PlaceholderFor(string separator) =>
        Strings.Format(Resources.QuickCapturePlaceholder, separator);

    /// <inheritdoc cref="PlaceholderFor" />
    private static string HintFor(string separator) =>
        Strings.Format(Resources.QuickCaptureHint, separator);

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
        // 快取的規則跟清單頁一樣:GetItems 會被頻繁呼叫,但鍵一定要帶上 repository 的
        // Version —— 只看查詢字串的話,剛記下的那則不會出現在底下的「已經記過的」裡,
        // 使用者會以為存檔沒生效,然後再記一次。
        //
        // 分隔符也在鍵裡面:同一句話在換掉分隔符之後切出來的標題與內文完全不同,
        // 少了它會拿到用舊分隔符切出來的那一列。「記下後先看一眼」同理 ——
        // 它換的是每一列上 Enter 掛哪一條命令,快取沒帶到就等於設定改了卻沒生效。
        var version = _repository.Version;
        var separator = _separatorStore.CaptureSeparator;
        var preview = _previewStore.ShowCapturePreview;

        if (_items is not null
            && _itemsVersion == version
            && string.Equals(_itemsQuery, _query, StringComparison.Ordinal)
            && string.Equals(_itemsSeparator, separator, StringComparison.Ordinal)
            && _itemsPreview == preview)
        {
            return _items;
        }

        _items = BuildItems(_query, separator, preview);
        _itemsQuery = _query;
        _itemsSeparator = separator;
        _itemsPreview = preview;
        _itemsVersion = version;
        return _items;
    }

    private IListItem[] BuildItems(string query, string separator, bool preview)
    {
        // 沒有前綴判斷:能走到這一頁,使用者已經用 alias 表達過意圖了。
        var draft = QuickCapture.Split(query, separator);

        if (draft is null)
        {
            DiagnosticLog.Write($"QuickCapturePage.BuildItems: query='{query}' 還構不成筆記");
            return [];
        }

        var items = new List<IListItem>(MaxSimilarNotes + 2) { CreateCaptureItem(draft, preview) };

        if (CreateClipboardItem(draft, preview) is { } fromClipboard)
        {
            items.Add(fromClipboard);
        }

        // 用標題而不是整句原始輸入去比對:分號後面的內文可能很長,
        // 拿它一起去搜只會讓命中率降到零,提醒就失效了。
        foreach (var note in NoteSearch.Filter(_repository.GetAll(), draft.Title).Take(MaxSimilarNotes))
        {
            items.Add(CreateSimilarItem(note));
        }

        DiagnosticLog.Write(
            $"QuickCapturePage.BuildItems: 標題='{draft.Title}' 內文='{draft.Body}' 相似 {items.Count - 1} 則");

        return [.. items];
    }

    private ListItem CreateCaptureItem(QuickCaptureDraft draft, bool preview) => CreateCaptureItem(
        draft,
        preview,
        draft.Body.Length == 0
            ? Resources.QuickCaptureNewNoteSubtitle
            : Strings.Format(Resources.QuickCaptureBodySubtitle, draft.Body),
        Icons.Add);

    /// <summary>
    /// 一列「記下」。存檔的路有兩條,但同一時間**只掛一條** —— 設定決定是哪一條,
    /// 另一條不會出現在 Ctrl+Enter 或選單上。
    ///
    /// 曾經做成「兩條都在,設定只決定哪一條掛 Enter」,拿掉了:使用者不會為了看一眼
    /// 特地去按 Ctrl+Enter,那一列留著只是讓選單多一項要讀的東西。設定就是設定。
    ///
    /// 命令實例每次重建,Draft 在建構時就固定下來 —— 不共用可變狀態,就少一個
    /// 「按下 Enter 時 Draft 已經被下一次輸入改掉」的競態。
    /// (按 Enter 與更新查詢是兩次不同的跨進程呼叫,不保證在同一個執行緒。)
    /// </summary>
    private ListItem CreateCaptureItem(QuickCaptureDraft draft, bool preview, string subtitle, IconInfo icon)
    {
        ICommand command = preview
            ? new CapturedNotePage(_repository, draft)
            : new QuickCaptureCommand(_repository) { Draft = draft };

        return new ListItem(command)
        {
            Title = Strings.Format(Resources.QuickCaptureItemTitle, draft.Title),
            Subtitle = subtitle,
            Icon = icon,
            Section = Resources.CommandCapture,
        };
    }

    /// <summary>
    /// 「內文取自剪貼簿」那一列。剪貼簿不是多行文字時回傳 null,不佔位子。
    ///
    /// 為什麼需要它:CmdPal 的搜尋框是單行 <c>TextBox</c>(<c>SearchBar.xaml</c> 沒有
    /// <c>AcceptsReturn</c>),往裡面貼一段多行的 Markdown,**只有第一行進得來**,
    /// 其餘的無聲消失。那是 CmdPal 的控件,我們改不了。
    ///
    /// 但剪貼簿本身是完整的 —— 繞過搜尋框直接讀它就行:標題還是用打的,
    /// 內文取原文,換行、縮排、程式碼區塊通通留著。
    /// </summary>
    private ListItem? CreateClipboardItem(QuickCaptureDraft draft, bool preview)
    {
        var clipboard = TryGetClipboardText();

        // 只在真的多行時才出現。單行的話搜尋框自己貼得進去,多這一列只是噪音。
        if (clipboard is null || !clipboard.Contains('\n', StringComparison.Ordinal))
        {
            return null;
        }

        var text = clipboard.ReplaceLineEndings().TrimEnd();
        var lineCount = text.Split(Environment.NewLine).Length;

        return CreateCaptureItem(
            draft with { Body = text },
            preview,
            Strings.Format(Resources.QuickCaptureClipboardSubtitle, lineCount),
            Icons.Paste);
    }

    /// <summary>
    /// 讀剪貼簿。讀不到就當作沒有 —— 剪貼簿隨時可能被別的程式佔住,
    /// 那不是使用者做錯什麼,不該讓整個頁面炸掉或跳錯誤。
    /// </summary>
    private static string? TryGetClipboardText()
    {
        try
        {
            var text = ClipboardHelper.GetText();

            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"TryGetClipboardText 失敗:{ex.Message}");
            return null;
        }
    }

    private ListItem CreateSimilarItem(Note note) => new(new NotePreviewPage(_repository, note))
    {
        Title = note.Title,
        Subtitle = note.Summary,
        Icon = Icons.Note,
        Section = Resources.QuickCaptureSectionSimilar,
    };

    private void OnRepositoryChanged(object? sender, EventArgs e)
    {
        // Version 已經讓下一次 GetItems 自己重建了;這裡是主動通知 CmdPal 立刻來拿。
        RaiseItemsChanged();
    }

    /// <summary>
    /// 設定頁改了分隔符。項目那邊靠快取鍵自己會重建(見 <see cref="GetItems"/>),
    /// 這裡負責的是兩塊寫死在屬性上的提示文字。
    /// </summary>
    private void OnCaptureSeparatorChanged(object? sender, EventArgs e)
    {
        var separator = _separatorStore.CaptureSeparator;

        PlaceholderText = PlaceholderFor(separator);
        _emptyContent.Subtitle = HintFor(separator);

        RaiseItemsChanged();
        DiagnosticLog.Write($"QuickCapturePage.OnCaptureSeparatorChanged: 分隔符='{separator}'");
    }

    /// <summary>
    /// 設定頁改了「記下後先看一眼」。項目那邊靠快取鍵自己會重建(見 <see cref="GetItems"/>),
    /// 這裡只負責催 CmdPal 立刻來拿 —— 使用者剛按完儲存,不該還要退出去再進來一次。
    /// </summary>
    private void OnCapturePreviewChanged(object? sender, EventArgs e)
    {
        RaiseItemsChanged();
        DiagnosticLog.Write(
            $"QuickCapturePage.OnCapturePreviewChanged: 記下後預覽={_previewStore.ShowCapturePreview}");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _repository.Changed -= OnRepositoryChanged;
        _separatorStore.CaptureSeparatorChanged -= OnCaptureSeparatorChanged;
        _previewStore.CapturePreviewChanged -= OnCapturePreviewChanged;
    }
}
