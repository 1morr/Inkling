using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Notelet.Commands;
using Notelet.Core;

namespace Notelet.Pages;

/// <summary>
/// 刪除所有筆記。**打開這一頁不刪任何東西**,它只是先把即將被刪的檔案列出來。
///
/// 為什麼是一整頁,而不是一個按下去就跳確認框的命令:確認框只有一行標題與一行說明,
/// 但這個動作真正該回答的問題是「到底會刪掉哪些檔案」。範圍是筆記資料夾底下
/// (含子資料夾)所有的 <c>.md</c>,而且**不分辨檔案是不是 Notelet 寫的** ——
/// 那是列清單時刻意的設計(外來的 .md 也要看得到,見 <see cref="Note.IsExternal"/>),
/// 但放到批次刪除上就變成一把沒有握把的刀:資料夾要是被指到既有的 Obsidian vault
/// 或某個專案目錄,一次就全掃走了。
///
/// 頁面還換來一件命令的形狀下放不下的東西:有外來檔案時多一列「只刪 Notelet 建立的」。
///
/// 順帶解決一個小毛病:原本沒有筆記時只能回一個 toast,而 toast 的預設收尾是把整個
/// CmdPal 關掉 —— 使用者只看到面板一閃就沒了。清單頁的 <c>EmptyContent</c>
/// 本來就是講這件事的地方。
/// </summary>
internal sealed partial class DeleteAllNotesPage : ListPage, IDisposable
{
    private const string ActionSection = "動作";
    private const string ExternalSection = "不是 Notelet 建立的";
    private const string MineSection = "Notelet 筆記";

    /// <summary>沒有外來檔案時就不必分兩區,一個中性的標題就好。</summary>
    private const string AllSection = "將被刪除";

    private readonly INoteRepository _repository;
    private readonly NoteletOptions _options;
    private readonly IDetailsWidthStore _widthStore;

    private IListItem[]? _items;
    private int _itemsVersion = -1;
    private bool _disposed;

    public DeleteAllNotesPage(INoteRepository repository, NoteletOptions options, IDetailsWidthStore widthStore)
    {
        _repository = repository;
        _options = options;
        _widthStore = widthStore;

        Id = CommandIds.DeleteAll;
        Icon = Icons.Delete;
        Title = "Notelet:刪除所有筆記";

        // 「開啟」而不是「刪除」:這個名字是頂層清單上那一列的動作標籤,
        // 而按下去只是進到這一頁。寫「刪除」會讓人以為 Enter 當場就動手。
        Name = "開啟";
        PlaceholderText = "動手前先看看會刪掉哪些檔案…";
        ShowDetails = true;

        EmptyContent = new CommandItem(new NoOpCommand())
        {
            Title = "沒有筆記可以刪除",
            Subtitle = _options.NotesDirectory,
            Icon = Icons.Note,
        };

        // 刪完要當場看到清單變空 —— 那是「真的刪掉了」最直接的證據。
        // 別台機器同步下來的變動也走這條路。
        _repository.Changed += OnRepositoryChanged;
    }

    public override IListItem[] GetItems()
    {
        // 快取的鍵只有 Version:這一頁不吃查詢字串(過濾交給 CmdPal),
        // 但刪完之後那份清單一定要重建,否則畫面上還留著剛剛刪掉的檔案。
        var version = _repository.Version;

        if (_items is not null && _itemsVersion == version)
        {
            return _items;
        }

        _items = BuildItems();
        _itemsVersion = version;
        return _items;
    }

    private IListItem[] BuildItems()
    {
        var notes = _repository.GetAll();

        if (notes.Count == 0)
        {
            DiagnosticLog.Write("DeleteAllNotesPage.BuildItems: 沒有筆記,交給 EmptyContent");
            return [];
        }

        var external = notes.Count(n => n.IsExternal);

        // 動作在最上面 —— 使用者是為了它才進來的。
        var items = new List<IListItem>(Math.Min(notes.Count, _options.MaxResults) + 3)
        {
            CreateDeleteEverythingItem(notes.Count, external),
        };

        // 全部都是外來檔案時這條路等於什麼都不刪,不要放一列點下去沒反應的東西。
        if (external > 0 && external < notes.Count)
        {
            items.Add(CreateDeleteMineItem(notes.Count - external, external));
        }

        // 外來檔案排最前面:那正是使用者最需要先看到的一批。
        // 兩邊各自維持 GetAll 的排序(最後更新的在前)。
        var ordered = notes.Where(n => n.IsExternal).Concat(notes.Where(n => !n.IsExternal));
        var section = external > 0 ? MineSection : AllSection;

        foreach (var note in ordered.Take(_options.MaxResults))
        {
            items.Add(CreateNoteItem(note, section));
        }

        // 列不完的時候一定要講,而且要講清楚「沒列出來不等於不會刪」 ——
        // 這一頁的用途就是讓人看見範圍,含糊的截斷反而製造新的誤會。
        if (notes.Count > _options.MaxResults)
        {
            items.Add(new ListItem(new NoOpCommand())
            {
                Title = $"還有 {notes.Count - _options.MaxResults} 則沒列出",
                Subtitle = "刪除全部仍然會刪掉它們,這裡只是列不下",
                Icon = Icons.External,
                Section = section,
            });
        }

        DiagnosticLog.Write($"DeleteAllNotesPage.BuildItems: 共 {notes.Count} 則,其中外來 {external} 則");
        return [.. items];
    }

    private ListItem CreateDeleteEverythingItem(int total, int external)
    {
        var description = external > 0
            ? $"{_options.NotesDirectory} 底下(含子資料夾)所有的 .md 都會移到資源回收筒,其中 {external} 則不是 Notelet 建立的。"
            : $"{_options.NotesDirectory} 底下(含子資料夾)所有的 .md 都會移到資源回收筒。";

        var command = new AnonymousCommand(() => { })
        {
            Name = "刪除全部",
            Icon = Icons.Delete,
            Result = CommandResult.Confirm(new ConfirmationArgs
            {
                Title = $"刪除全部 {total} 則筆記?",
                Description = description,
                PrimaryCommand = new ConfirmedDeleteAllNotesCommand(_repository, DeleteScope.Everything),

                // 這裡維持 critical:CmdPal 拿它做的事是把預設按鈕設成「取消」
                // (見 NoteListPage 上的說明)。要清空整個資料夾就該多花那一下。
                IsPrimaryCommandCritical = true,
            }),
        };

        return new ListItem(command)
        {
            Title = $"刪除全部 {total} 則",
            Subtitle = $"{_options.NotesDirectory}(含子資料夾)",
            Icon = Icons.Delete,
            Section = ActionSection,
            Details = BuildDetails($"底下(含子資料夾)所有的 `.md`,目前 {total} 則,**全部都會刪掉**。"
                + (external > 0
                    ? $"\n\n其中 **{external} 則不是 Notelet 建立的** —— front matter 裡沒有 Notelet 的 id,"
                        + "是別的工具寫的、或是直接丟進這個資料夾的檔案。"
                    : string.Empty)),
        };
    }

    private ListItem CreateDeleteMineItem(int mine, int external)
    {
        var command = new AnonymousCommand(() => { })
        {
            Name = "只刪 Notelet 建立的",
            Icon = Icons.Delete,
            Result = CommandResult.Confirm(new ConfirmationArgs
            {
                Title = $"刪除 Notelet 建立的 {mine} 則筆記?",
                Description = $"移到資源回收筒。另外 {external} 則不是 Notelet 建立的,不會動到。",
                PrimaryCommand = new ConfirmedDeleteAllNotesCommand(_repository, DeleteScope.NoteletCreatedOnly),
                IsPrimaryCommandCritical = true,
            }),
        };

        return new ListItem(command)
        {
            Title = $"只刪 Notelet 建立的 {mine} 則",
            Subtitle = $"保留 {external} 則不是 Notelet 建立的",
            Icon = Icons.Note,
            Section = ActionSection,
            Details = BuildDetails(
                $"底下(含子資料夾)共 {mine + external} 則 `.md`,這個動作只刪其中"
                + $" **Notelet 建立的 {mine} 則**。"
                + $"\n\n另外 {external} 則 front matter 裡沒有 Notelet 的 id"
                + "(別的工具寫的、或是直接丟進這個資料夾的),留著不動。"),
        };
    }

    private ListItem CreateNoteItem(Note note, string section) => new(new NotePreviewPage(_repository, note))
    {
        Title = note.Title,
        Subtitle = Path.GetRelativePath(_options.NotesDirectory, note.FilePath),
        Icon = note.IsExternal ? Icons.External : Icons.Note,
        Section = note.IsExternal ? ExternalSection : section,
        Details = new Details
        {
            Title = note.Title,
            Body = note.Body.Length == 0 ? "_(沒有內文)_" : NotePreview.PreserveLineBreaks(note.Body),
            Size = _widthStore.DetailsWidth,
        },
    };

    /// <summary>
    /// 動作那兩列的詳細內容。開頭的資料夾路徑與結尾的資源回收筒那段兩列共用,
    /// 中間那段各講各的範圍 —— 兩列講同一套數字的話,「只刪 Notelet 建立的」
    /// 看起來就像也要刪掉全部。
    /// </summary>
    private Details BuildDetails(string scope) => new()
    {
        Title = "會刪掉什麼",
        Body = $"`{_options.NotesDirectory}`\n\n{scope}"
            + "\n\n刪掉的檔案會進資源回收筒。網路磁碟或沒有回收筒的裝置上則是直接消失,"
            + "那是 Windows 的行為,不是我們能選的。",

        // 寬度讀一次就定了,不像清單頁那樣跟著 Ctrl+D 更新 —— 這一頁沒有那個快速鍵,
        // 唯一能在它開著時改寬度的地方是設定頁,而那不是會發生的事。
        Size = _widthStore.DetailsWidth,
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
