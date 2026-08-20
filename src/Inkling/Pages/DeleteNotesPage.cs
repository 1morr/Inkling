using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Inkling.Commands;
using Inkling.Core;
using Inkling.Properties;

namespace Inkling.Pages;

/// <summary>
/// 刪除筆記:一則一則刪,或整個清空。**打開這一頁不刪任何東西。**
///
/// 為什麼是一整頁,而不是一個按下去就跳確認框的命令:批次刪除真正該回答的問題是
/// 「到底會刪掉哪些檔案」,而確認框只有一行標題與一行說明。掃描的範圍是筆記資料夾底下
/// (含子資料夾)所有的 <c>.md</c>,而且**不分辨檔案是不是 Inkling 寫的** —— 那是列清單時
/// 刻意的設計(外來的 .md 也要看得到,見 <see cref="Note.IsExternal"/>),但放到批次刪除上
/// 就變成一把沒有握把的刀:資料夾要是被指到既有的 Obsidian vault 或某個專案目錄,
/// 一次就全掃走了。
///
/// 頁面還換來一件命令的形狀下放不下的東西:有外來檔案時多一列「只刪 Inkling 建立的」。
///
/// <para><b>兩個鍵位,兩種心情。</b></para>
///
/// 每一則筆記上 <c>Enter</c> 是「刪除,但先問一次」,<c>Ctrl+Enter</c> 是「直接刪」。
/// 同一個動作給兩條路,是因為使用者進到這一頁時的狀態有兩種:一種是心裡有數要清掉哪幾則
/// (連著按 <c>Ctrl+Enter</c> 最快),另一種是邊看邊決定(每一則都想再確認一次)。
/// 底部工具列會把兩條路都寫出來,不必記。
///
/// **例外只有一個**:不是 Inkling 建立的檔案,兩條路都跳確認框。那是別的工具寫的、
/// 或使用者自己丟進資料夾的,誤刪的代價跟自己記的筆記不一樣,不給它「跳過確認」這個選項。
///
/// 刪完的回饋不靠 toast,靠那一列當場從清單上消失 —— 理由見
/// <see cref="DeleteNoteCommand"/>(簡單說:toast 會搶焦點,而主視窗一失焦就自我隱藏,
/// 發一個 toast 等於刪一則就把面板關掉一次)。
/// </summary>
internal sealed partial class DeleteNotesPage : ListPage, IDisposable
{
    private readonly INoteRepository _repository;
    private readonly InklingOptions _options;

    /// <summary>
    /// 原始文字模式(全域,見 <see cref="ISourceModeStore"/>)。這一頁跟著走,但**不給切換鍵**:
    /// 詳細窗格的內容只能靠換掉整批項目物件才會重讀(見 <c>NoteListPage.RefreshDetails</c>
    /// 講的那條斷掉的通知路),而重建清單會讓選中項跑掉 —— 這一頁的第一列是「刪除全部」,
    /// 不值得為了一個切換鍵多一條「焦點跳到那一列」的路。要切換請在清單頁按 <c>Ctrl+U</c>,
    /// 切完再進來就是新的模式(快取鍵帶著它)。
    /// </summary>
    private readonly ISourceModeStore _sourceMode;

    /// <summary>
    /// 項目快取。規則只有一條 —— 鍵要帶 Version 與所有影響內容的設定值 ——
    /// 「為什麼」寫在 <see cref="VersionedItemsCache{TKey}"/> 上,三個清單頁共用。
    /// </summary>
    private readonly VersionedItemsCache<(int Version, bool ShowSource)> _cache = new();

    private bool _disposed;

    public DeleteNotesPage(INoteRepository repository, InklingOptions options, ISourceModeStore sourceMode)
    {
        _repository = repository;
        _options = options;
        _sourceMode = sourceMode;

        Id = CommandIds.DeleteAll;
        Icon = Icons.Delete;
        Title = Resources.ProviderDeletePageTitle;

        // 「開啟」而不是「刪除」:這個名字是頂層清單上那一列的動作標籤,
        // 而按下去只是進到這一頁。寫「刪除」會讓人以為 Enter 當場就動手。
        Name = Resources.CommandOpen;
        PlaceholderText = Resources.DeletePagePlaceholder;
        ShowDetails = true;

        EmptyContent = new CommandItem(new NoOpCommand())
        {
            Title = Resources.DeletePageEmptyTitle,
            Subtitle = _options.NotesDirectory,
            Icon = Icons.Note,
        };

        // 刪完要當場看到清單少一列 —— 那是「真的刪掉了」最直接的證據。
        // 別台機器同步下來的變動也走這條路。
        _repository.Changed += OnRepositoryChanged;
    }

    public override IListItem[] GetItems()
    {
        // 這一頁不吃查詢字串(過濾交給 CmdPal),但刪完之後那份清單一定要重建,
        // 否則畫面上還留著剛剛刪掉的檔案 —— 所以鍵要帶 Version。原始文字模式也要帶:
        // 它決定右邊那塊詳細窗格是渲染結果還是原文,而它是在別的頁面上切的。
        var showSource = _sourceMode.ShowSource;

        return _cache.Get((_repository.Version, showSource), () => BuildItems(showSource));
    }

    private IListItem[] BuildItems(bool showSource)
    {
        var notes = _repository.GetAll();

        if (notes.Count == 0)
        {
            DiagnosticLog.Write("DeleteNotesPage.BuildItems: 沒有筆記,交給 EmptyContent");
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

        // 沒有外來檔案時就不必分兩區,用一個中性的標題。
        var section = external > 0 ? Resources.DeleteSectionMine : Resources.DeleteSectionAll;

        foreach (var note in ordered.Take(_options.MaxResults))
        {
            items.Add(CreateNoteItem(note, section, showSource));
        }

        // 列不完的時候一定要講,而且要講清楚「沒列出來不等於不會刪」 ——
        // 這一頁的用途就是讓人看見範圍,含糊的截斷反而製造新的誤會。
        if (notes.Count > _options.MaxResults)
        {
            items.Add(new ListItem(new NoOpCommand())
            {
                Title = Strings.Format(Resources.DeleteMoreNotes, notes.Count - _options.MaxResults),
                Subtitle = Resources.DeleteMoreNotesSubtitle,
                Icon = Icons.External,
                Section = section,
            });
        }

        DiagnosticLog.Write($"DeleteNotesPage.BuildItems: 共 {notes.Count} 則,其中外來 {external} 則");
        return [.. items];
    }

    /// <summary>
    /// 一則筆記那一列。<c>Enter</c> 先問一次,<c>Ctrl+Enter</c> 直接刪。
    ///
    /// 預覽降到選單第二項:這一頁 <c>ShowDetails</c> 是開的,右邊的詳細窗格本來就在顯示
    /// 標題與內文,預覽頁多出來的只有 Markdown 渲染 —— 不值得佔著前面那兩個鍵位。
    /// </summary>
    private ListItem CreateNoteItem(Note note, string section, bool showSource) => new(CreateConfirmedDelete(note))
    {
        Title = note.Title,
        Subtitle = Path.GetRelativePath(_options.NotesDirectory, note.FilePath),
        Icon = note.IsExternal ? Icons.External : Icons.Note,
        Section = note.IsExternal ? Resources.DeleteSectionExternal : section,
        Details = NoteDetails.For(note, showSource),
        MoreCommands = [
            // 第一個會被 CmdPal 當成次要命令放上底部工具列(Ctrl+Enter)。
            CreateQuickDeleteItem(note),
            new CommandContextItem(new NotePreviewPage(_repository, note, _sourceMode))
            {
                Title = Resources.CommandPreview,
                Icon = Icons.Preview,
            },
        ],
    };

    /// <summary>
    /// <c>Enter</c> 走的路:跳確認框,按下主要按鈕才真的刪。
    ///
    /// 確認不是自己畫的 —— 命令回傳 <see cref="CommandResult.Confirm"/>,CmdPal 就會替我們
    /// 跳一個對話框。<c>IsPrimaryCommandCritical</c> 分兩種:**Inkling 建立的刻意不設** ——
    /// 上游拿它做的事是把預設按鈕設成「取消」,而這條路的存在意義就是「看一眼再按 Enter」,
    /// 再多一次方向鍵就本末倒置了。**外來檔案刻意設** —— 那是別的工具寫的檔案,
    /// 值得多那一下方向鍵。(反正 0.11 安裝版根本沒有那條程式路徑,設不設畫面一模一樣,
    /// 見 docs/design-notes.md〈確認框的按鈕沒有顏色,也沒有「危險」樣式〉;現在這樣設是為了 CmdPal
    /// 更新上來時語意就對。)
    /// </summary>
    private AnonymousCommand CreateConfirmedDelete(Note note) => new(() => { })
    {
        Name = Resources.CommandDelete,
        Icon = Icons.Delete,
        Result = CommandResult.Confirm(new ConfirmationArgs
        {
            Title = note.IsExternal ? Resources.DeleteExternalConfirmTitle : Resources.DeleteConfirmTitle,
            Description = Strings.Format(
                note.IsExternal ? Resources.DeleteExternalConfirmDescription : Resources.DeleteConfirmDescription,
                note.Title),
            PrimaryCommand = new DeleteNoteCommand(_repository, note),
            IsPrimaryCommandCritical = note.IsExternal,
        }),
    };

    /// <summary>
    /// <c>Ctrl+Enter</c> 走的路:不問,直接送進資源回收筒。
    ///
    /// **外來檔案是唯一的例外** —— 它拿到的還是確認框那條路,因為那是別的工具寫的、
    /// 或使用者自己丟進資料夾的檔案,不給它「跳過確認」這個選項。所以那一列的
    /// <c>Ctrl+Enter</c> 跟 <c>Enter</c> 做同一件事,標題也照實寫成「刪除」而不是
    /// 「直接刪除」,副標講明為什麼。
    ///
    /// 兩條路都設 <c>IsCritical</c>,讓它們在 <c>Ctrl+K</c> 選單裡是紅的 ——
    /// 跟清單頁的「刪除」同一個樣子。**只有選單裡的那一列變得了色**:同一個命令
    /// 出現在底部工具列上(<c>Ctrl+Enter</c> 那顆按鈕)時還是預設樣式,
    /// 見 docs/design-notes.md〈刪除的紅色只有一個地方碰得到〉。
    ///
    /// 這裡刻意**不綁** <see cref="Shortcuts.Delete"/>:這一頁的 <c>Enter</c> 與
    /// <c>Ctrl+Enter</c> 本來就是刪除,再多一個鍵只是多一種說法,而且語意會打架 ——
    /// 清單頁的 <c>Ctrl+D</c> 是「會先問一次」,這一頁的這一列卻是「不問」。
    /// </summary>
    private CommandContextItem CreateQuickDeleteItem(Note note)
    {
        if (note.IsExternal)
        {
            return new CommandContextItem(CreateConfirmedDelete(note))
            {
                Title = Resources.CommandDelete,
                Subtitle = Resources.DeleteExternalAlwaysConfirmSubtitle,
                IsCritical = true,
            };
        }

        return new CommandContextItem(new DeleteNoteCommand(_repository, note)
        {
            Name = Resources.DeleteQuickTitle,
        })
        {
            Title = Resources.DeleteQuickTitle,
            Subtitle = Resources.DeleteQuickSubtitle,
            IsCritical = true,
        };
    }

    /// <summary>
    /// 「刪除全部」。這一頁破壞力最大的一列,而且**排在第一位**。
    ///
    /// 那個位置有代價,寫下來免得日後當成沒想過:進到這一頁時預設選中的就是它,而 0.11
    /// 刪掉一列之後焦點很可能也跳回第一列(沒有 <c>main</c> 那套 sticky selection,
    /// byte-scan 掃不到 <c>_stickySelectedItem</c>)—— 也就是說「想刪下一則而順手按 Enter」
    /// 有機會落在這一列上。三道防線:它一定會跳確認框、標題明著寫「刪除全部 N 則筆記?」、
    /// 刪掉的檔案進資源回收筒。**而連著按 <c>Ctrl+Enter</c> 清理的那條路完全踩不到它** ——
    /// 這一列沒有次要命令,焦點跳過來時 <c>Ctrl+Enter</c> 什麼都不會發生。
    /// </summary>
    private ListItem CreateDeleteEverythingItem(int total, int external)
    {
        var description = external > 0
            ? Strings.Format(
                Resources.DeleteAllConfirmDescriptionWithExternal, _options.NotesDirectory, external)
            : Strings.Format(Resources.DeleteAllConfirmDescription, _options.NotesDirectory);

        var command = new AnonymousCommand(() => { })
        {
            Name = Resources.DeleteAllName,
            Icon = Icons.Delete,
            Result = CommandResult.Confirm(new ConfirmationArgs
            {
                Title = Strings.Format(Resources.DeleteAllConfirmTitle, total),
                Description = description,
                PrimaryCommand = new ConfirmedDeleteAllNotesCommand(_repository, DeleteScope.Everything),

                // 這裡維持 critical:上游拿它做的事是把預設按鈕設成「取消」,要清空整個
                // 資料夾就該多花那一下。**0.11 安裝版還沒有那條路**,所以現在按下去跟單則
                // 刪除長得一模一樣 —— 那一頁本身列出會刪掉哪些檔案,防線在那裡,不在這個旗標。
                IsPrimaryCommandCritical = true,
            }),
        };

        return new ListItem(command)
        {
            Title = Strings.Format(Resources.DeleteAllItemTitle, total),
            Subtitle = Strings.Format(Resources.DeleteAllItemSubtitle, _options.NotesDirectory),
            Icon = Icons.Delete,
            Section = Resources.DeleteSectionAction,
            Details = BuildDetails(Strings.Format(Resources.DeleteAllScope, total)
                + (external > 0
                    ? Strings.Format(Resources.DeleteAllScopeExternalSuffix, external)
                    : string.Empty)),
        };
    }

    private ListItem CreateDeleteMineItem(int mine, int external)
    {
        var command = new AnonymousCommand(() => { })
        {
            Name = Resources.DeleteMineName,
            Icon = Icons.Delete,
            Result = CommandResult.Confirm(new ConfirmationArgs
            {
                Title = Strings.Format(Resources.DeleteMineConfirmTitle, mine),
                Description = Strings.Format(Resources.DeleteMineConfirmDescription, external),
                PrimaryCommand = new ConfirmedDeleteAllNotesCommand(_repository, DeleteScope.InklingCreatedOnly),
                IsPrimaryCommandCritical = true,
            }),
        };

        return new ListItem(command)
        {
            Title = Strings.Format(Resources.DeleteMineItemTitle, mine),
            Subtitle = Strings.Format(Resources.DeleteMineItemSubtitle, external),
            Icon = Icons.Note,
            Section = Resources.DeleteSectionAction,
            Details = BuildDetails(Strings.Format(Resources.DeleteMineScope, mine + external, mine, external)),
        };
    }

    /// <summary>
    /// 兩個「整批」動作的詳細內容。開頭的資料夾路徑與結尾的資源回收筒那段兩列共用,
    /// 中間那段各講各的範圍 —— 兩列講同一套數字的話,「只刪 Inkling 建立的」
    /// 看起來就像也要刪掉全部。
    /// </summary>
    private Details BuildDetails(string scope) => new()
    {
        Title = Resources.DeleteDetailsTitle,
        Body = Strings.Format(Resources.DeleteDetailsBody, _options.NotesDirectory, scope),

        // Size 不明著寫就是最窄那一檔 —— 理由與「為什麼固定最寬」都寫在 NoteDetails.For。
        Size = ContentSize.Large,
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
