using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Inkling.Commands;
using Inkling.Core;
using Inkling.Properties;

namespace Inkling.Pages;

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
    /// 原始文字模式,這一頁自己不用,是傳給它建出來的兩種頁面 ——
    /// 「記下並預覽」與底下那幾則相似筆記的預覽頁。全域共用,見 <see cref="ISourceModeStore"/>。
    ///
    /// 這一頁**不必**進快取鍵:它自己的每一列(記下、相似筆記)長什麼樣跟這個模式無關,
    /// 模式只影響那些頁面被打開之後顯示的內容,而那是頁面自己在 <c>GetContent</c> 裡讀的。
    /// </summary>
    private readonly ISourceModeStore _sourceMode;

    /// <summary>
    /// 還沒打字時那塊提示。留著參照是為了在分隔符改掉之後就地更新它的副標 ——
    /// <c>ICommandItem</c> 在 IDL 裡就繼承 <c>INotifyPropChanged</c>,CmdPal 對它無條件訂閱,
    /// 走這條一定收得到(<c>IDetails</c> 就不行,見 <c>NoteListPage.RefreshDetails</c>)。
    /// </summary>
    private readonly CommandItem _emptyContent;

    private string _query = string.Empty;

    /// <summary>
    /// 項目快取。規則只有一條 —— 鍵要帶 Version 與所有影響內容的設定值 ——
    /// 「為什麼」寫在 <see cref="VersionedItemsCache{TKey}"/> 上,三個清單頁共用。
    /// </summary>
    private readonly VersionedItemsCache<(int Version, string Query, string Separator, bool Preview, string? Clipboard)> _cache = new();

    private bool _disposed;

    public QuickCapturePage(
        INoteRepository repository,
        ICaptureSeparatorStore separatorStore,
        ICapturePreviewStore previewStore,
        ISourceModeStore sourceMode)
    {
        _repository = repository;
        _separatorStore = separatorStore;
        _previewStore = previewStore;
        _sourceMode = sourceMode;

        var separator = separatorStore.CaptureSeparator;

        Id = CommandIds.QuickCapturePage;

        // 頁面內用字形,不是頂層那一列的自訂圖示 —— 界線見 Icons.cs:頂層要品牌,
        // 頁面內要跟 CmdPal 其他畫面協調。這裡挑燈泡而不是加號,是為了跟「新增筆記」
        // 分開;進了頁面又變回加號的話,那個區隔就白做了。
        Icon = Icons.Capture;

        // 頁面標題(標題列 + 底部命令列左下角)不帶「Inkling:」—— 進到頁面裡的人已經
        // 知道自己在哪個擴展了,前綴只是重複佔字。品牌名是**頂層那一列**的需求
        // (在主搜尋框裡要跟別的擴展區分),所以那一列另外走 ProviderCapturePageTitle。
        // 別為了省一條字串把兩邊接回去:接回去等於讓頂層的需求決定頁面裡的樣子,
        // 而那正是這一頁的標題曾經跟「新增筆記」「隨手草稿」對不起來的原因。
        Title = Resources.CapturePageTitle;
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
        // 快取的規則跟清單頁一樣(見 VersionedItemsCache),而鍵除了 Version 與查詢,
        // 還要帶分隔符與「記下後先看一眼」:同一句話在換掉分隔符之後切出來的標題與內文
        // 完全不同,少了它會拿到用舊分隔符切出來的那一列;預覽開關換的是每一列上
        // Enter 掛哪一條命令,快取沒帶到就等於設定改了卻沒生效。
        var separator = _separatorStore.CaptureSeparator;
        var preview = _previewStore.ShowCapturePreview;

        // **剪貼簿也是鍵的一部分。** 這一頁比別的清單頁多一個外部輸入:「內文取自剪貼簿」
        // 那一列的內容完全來自它,而使用者去別的視窗複製東西不會動到 Version、查詢
        // 或任何設定 —— 少了它,回到這一頁看到的還是上一份剪貼簿,而那一列按下去
        // 會把過期的內容存成筆記。
        //
        // 讀一次就往下傳,不要讓 BuildItems 再讀第二次:兩次讀取之間剪貼簿可能已經變了,
        // 那會讓「鍵」與「內容」對不上 —— 快取最糟的失效方式,因為它會一直錯下去。
        var clipboard = TryGetClipboardText();

        return _cache.Get(
            (_repository.Version, _query, separator, preview, clipboard),
            () => BuildItems(_query, separator, preview, clipboard));
    }

    private IListItem[] BuildItems(string query, string separator, bool preview, string? clipboard)
    {
        // 沒有前綴判斷:能走到這一頁,使用者已經用 alias 表達過意圖了。
        var draft = QuickCapture.Split(query, separator);

        if (draft is null)
        {
            DiagnosticLog.Write($"QuickCapturePage.BuildItems: query='{query}' 還構不成筆記");
            return [];
        }

        var items = new List<IListItem>(MaxSimilarNotes + 2) { CreateCaptureItem(draft, preview) };

        if (CreateClipboardItem(draft, preview, clipboard) is { } fromClipboard)
        {
            items.Add(fromClipboard);
        }

        // 相似筆記從這一格開始。前面那幾列(記下、可能還有「內文取自剪貼簿」)不算,
        // 而剪貼簿那一列在不在是浮動的 —— 寫死扣 1 的話,log 上的數字時準時不準。
        var beforeSimilar = items.Count;

        // 用標題而不是整句原始輸入去比對:分號後面的內文可能很長,
        // 拿它一起去搜只會讓命中率降到零,提醒就失效了。
        foreach (var note in NoteSearch.Filter(_repository.GetAll(), draft.Title).Take(MaxSimilarNotes))
        {
            items.Add(CreateSimilarItem(note));
        }

        // **內文只記字數,不記內容。** 這一行原本是拿來確認切分位置對不對的,字數就夠;
        // 而 bug 範本會請使用者把整份 log 貼進**公開的** issue —— 那是這個 repo 裡唯一
        // 會主動導致外洩的路徑,而內文正是使用者剛打完、最私密的那一段字。
        // 標題與搜尋字串照樣記(少了它們就對不上是哪一則),範本那邊因此附了去識別化的提醒。
        DiagnosticLog.Write(
            $"QuickCapturePage.BuildItems: 標題='{draft.Title}' 內文 {draft.Body.Length} 字,"
                + $"相似 {items.Count - beforeSimilar} 則");

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
            ? new CapturedNotePage(_repository, draft, _sourceMode)
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
    private ListItem? CreateClipboardItem(QuickCaptureDraft draft, bool preview, string? clipboard)
    {
        if (clipboard is null)
        {
            return null;
        }

        // **先正規化再判斷是不是多行。** 直接拿原文找 '\n' 會漏掉只有裸 CR 的內容
        // (舊 Mac 行尾、部分終端機與試算表複製出來的就是這樣)—— 那一列會整個不出現,
        // 而使用者看到的只是「貼了多行卻沒有那一列」。ReplaceLineEndings 把 CR / CRLF /
        // LS / PS / NEL 全部收斂成 Environment.NewLine,判斷與計行數用的是同一份文字。
        //
        // TrimEnd 也排在判斷之前:結尾帶一個換行的單行文字(複製一整列時很常見)
        // 不該冒出一列「內文取自剪貼簿(1 行)」。
        var text = clipboard.ReplaceLineEndings().TrimEnd();

        // 只在真的多行時才出現。單行的話搜尋框自己貼得進去,多這一列只是噪音。
        if (!text.Contains(Environment.NewLine, StringComparison.Ordinal))
        {
            return null;
        }

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

    private ListItem CreateSimilarItem(Note note) => new(new NotePreviewPage(_repository, note, _sourceMode))
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
