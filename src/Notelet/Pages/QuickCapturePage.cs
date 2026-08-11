using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Notelet.Commands;
using Notelet.Core;

namespace Notelet.Pages;

/// <summary>
/// 快速記下:進到這一頁,打字,Enter 存檔。
///
/// 為什麼有了 <see cref="QuickCaptureFallbackItem"/> 還要這一頁 —— fallback 那條路
/// 拿得到主搜尋框的字,不必跳頁,但代價是它得跟所有命令與應用程式擠在同一個清單裡。
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

    private const string CaptureSection = "記下";
    private const string SimilarSection = "已經記過的";

    private readonly INoteRepository _repository;

    private string _query = string.Empty;
    private IListItem[]? _items;
    private string? _itemsQuery;
    private int _itemsVersion = -1;
    private bool _disposed;

    public QuickCapturePage(INoteRepository repository)
    {
        _repository = repository;

        Id = CommandIds.QuickCapturePage;
        Icon = Icons.Add;
        Title = "Notelet:快速記下";
        Name = "開啟";
        PlaceholderText = "打字記下想法,分號後面接內文…";

        EmptyContent = new CommandItem(new NoOpCommand())
        {
            Title = "打字就記下",
            Subtitle = "「買咖啡機;比較過幾台」—— 分號前面是標題,後面是內文",
            Icon = Icons.Add,
        };

        // 別台機器經 OneDrive 同步下來、或使用者拿別的編輯器改了檔案時,
        // 底下那份「已經記過的」要跟著更新,否則提醒的是過期的內容。
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
        // 快取的規則跟清單頁一樣:GetItems 會被頻繁呼叫,但鍵一定要帶上 repository 的
        // Version —— 只看查詢字串的話,剛記下的那則不會出現在底下的「已經記過的」裡,
        // 使用者會以為存檔沒生效,然後再記一次。
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

    private IListItem[] BuildItems(string query)
    {
        // 沒有前綴判斷:能走到這一頁,使用者已經用 alias 表達過意圖了。
        var draft = QuickCapture.Split(query);

        if (draft is null)
        {
            DiagnosticLog.Write($"QuickCapturePage.BuildItems: query='{query}' 還構不成筆記");
            return [];
        }

        var items = new List<IListItem>(MaxSimilarNotes + 1) { CreateCaptureItem(draft) };

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

    private ListItem CreateCaptureItem(QuickCaptureDraft draft)
    {
        // 每次重建一個新的命令實例,Draft 在建構時就固定下來。
        // fallback 那邊共用一個實例、每次輸入改寫 Draft,是因為 CmdPal 只讓它有一列;
        // 這裡沒有那個限制,不共用可變狀態就少一個「按 Enter 時 Draft 已經被改掉」的風險。
        var command = new QuickCaptureCommand(_repository, goHomeAfterSave: true)
        {
            Draft = draft,
        };

        return new ListItem(command)
        {
            Title = $"記下:{draft.Title}",
            Subtitle = draft.Body.Length == 0 ? "存成新筆記" : $"內文:{draft.Body}",
            Icon = Icons.Add,
            Section = CaptureSection,
        };
    }

    private ListItem CreateSimilarItem(Note note) => new(new NotePreviewPage(_repository, note))
    {
        Title = note.Title,
        Subtitle = note.Summary,
        Icon = Icons.Note,
        Section = SimilarSection,
    };

    private void OnRepositoryChanged(object? sender, EventArgs e)
    {
        // Version 已經讓下一次 GetItems 自己重建了;這裡是主動通知 CmdPal 立刻來拿。
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
    }
}
