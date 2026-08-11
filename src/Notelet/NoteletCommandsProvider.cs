using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Notelet.Commands;
using Notelet.Core;
using Notelet.Pages;

namespace Notelet;

public sealed partial class NoteletCommandsProvider : CommandProvider
{
    private readonly SettingsManager _settingsManager = new();
    private readonly Lock _gate = new();

    private ProviderState _state;

    public NoteletCommandsProvider()
    {
        Id = CommandIds.Provider;
        DisplayName = "Notelet";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");

        Settings = _settingsManager.Settings;

        _state = BuildState();

        // 改了資料夾路徑或前綴之後,整組命令要跟著換掉 —— 舊的 repository 還盯著舊資料夾。
        _settingsManager.Settings.SettingsChanged += OnSettingsChanged;
    }

    // CmdPal 一啟動就會呼叫這個方法,絕對不能碰磁碟 —— 只回傳事先建好的靜態命令項。
    // 真正的載入延後到使用者實際打開清單頁時。
    //
    // 沒有 FallbackCommands():快速記下走的是頁面 + 使用者自設的 alias。
    // 為什麼不是 fallback,見 README〈快速記下為什麼是頁面,不是 fallback〉。
    public override ICommandItem[] TopLevelCommands() => _state.Commands;

    private ProviderState BuildState()
    {
        var options = _settingsManager.ToOptions();
        DiagnosticLog.Write($"BuildState: 資料夾='{options.NotesDirectory}'");

        // Repository 整個擴展共用一個。它內部有快取與資料夾監看,
        // 每頁各建一個等於每頁都重掃一次磁碟,還會多掛好幾個 FileSystemWatcher。
        // 刪除走資源回收筒,不是直接抹掉 —— 筆記是手打的東西,誤刪要拿得回來。
        var repository = new FileSystemNoteRepository(options, fileDeleter: new RecycleBinFileDeleter());
        var listPage = new NoteListPage(repository, options, _settingsManager);
        var capturePage = new QuickCapturePage(repository);

        // 自己的設定頁外殼,不用 toolkit 的 Settings.SettingsPage —— 理由見 NoteletSettingsPage。
        var settingsPage = new NoteletSettingsPage(_settingsManager.Settings);
        _settingsManager.DetailsWidthChanged += (_, _) => settingsPage.Refresh();

        ICommandItem[] commands = [
            new CommandItem(listPage)
            {
                Title = DisplayName,
                Subtitle = "瀏覽與搜索筆記",
                MoreCommands = [new CommandContextItem(settingsPage)],
            },
            new CommandItem(capturePage)
            {
                Title = capturePage.Title,
                Subtitle = "打字直接存成筆記,分號後面接內文",
                Icon = Icons.Capture,
            },
            new CommandItem(new NewNotePage(repository))
            {
                Title = "Notelet:新增筆記",
                Subtitle = "開表單寫比較長的內容",
                Icon = Icons.Add,
            },
            new CommandItem(new DeleteAllNotesCommand(repository))
            {
                Title = "Notelet:刪除所有筆記",
                Subtitle = "整個資料夾清空,全部移到資源回收筒",
                Icon = Icons.Delete,
            },
        ];

        return new ProviderState(options.NotesDirectory, repository, listPage, capturePage, commands);
    }

    /// <summary>
    /// 設定變更。
    ///
    /// **只有資料夾變了才整組重建** —— 那時 repository 非換不可,它還盯著舊資料夾。
    /// 其他設定(寬度)刻意不重建,因為重建對它們根本沒用:CmdPal 手上握著的是使用者
    /// 當下開著的那個頁面實例,新建的頁面它不會去拿(實測 log:<c>BuildState</c> 之後
    /// 一次 <c>GetItems</c> 都沒有,直到 Reload)。硬重建反而更糟 —— 會把還在被使用的
    /// repository 給 Dispose 掉,連 FileSystemWatcher 一起收走。
    /// 寬度改走 <see cref="IDetailsWidthStore.DetailsWidthChanged"/>,由頁面自己更新。
    /// </summary>
    private void OnSettingsChanged(object? sender, Settings e)
    {
        var directory = _settingsManager.ToOptions().NotesDirectory;
        ProviderState previous;

        lock (_gate)
        {
            if (string.Equals(_state.NotesDirectory, directory, StringComparison.OrdinalIgnoreCase))
            {
                DiagnosticLog.Write("SettingsChanged: 資料夾沒變,不重建");
                return;
            }

            previous = _state;
            _state = BuildState();
        }

        previous.Dispose();
        RaiseItemsChanged();
    }

    public override void Dispose()
    {
        _settingsManager.Settings.SettingsChanged -= OnSettingsChanged;
        _state.Dispose();

        base.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 一組「跟著目前資料夾走」的東西。資料夾一改就整組換掉(其他設定不會,
    /// 見 <see cref="OnSettingsChanged"/>),舊的那組要確實釋放,
    /// 否則 FileSystemWatcher 與事件訂閱會一直累積。
    /// </summary>
    /// 標成 partial 是 CsWinRT 的要求:任何實作了 WinRT 投影介面(這裡是 IDisposable)
    /// 的型別都得讓來源產生器有地方掛程式碼,否則 trimming/AOT 下會出問題。
    private sealed partial record ProviderState(
        string NotesDirectory,
        FileSystemNoteRepository Repository,
        NoteListPage ListPage,
        QuickCapturePage CapturePage,
        ICommandItem[] Commands) : IDisposable
    {
        public void Dispose()
        {
            ListPage.Dispose();
            CapturePage.Dispose();
            Repository.Dispose();
        }
    }
}
