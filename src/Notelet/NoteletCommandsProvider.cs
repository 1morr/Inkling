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
    public override ICommandItem[] TopLevelCommands() => _state.Commands;

    public override IFallbackCommandItem[] FallbackCommands() => _state.Fallbacks;

    private ProviderState BuildState()
    {
        var options = _settingsManager.ToOptions();
        DiagnosticLog.Write(
            $"BuildState: prefix='{options.QuickCapturePrefix}' 快速新增={options.QuickCaptureEnabled} 資料夾='{options.NotesDirectory}'");

        // Repository 整個擴展共用一個。它內部有快取與資料夾監看,
        // 每頁各建一個等於每頁都重掃一次磁碟,還會多掛好幾個 FileSystemWatcher。
        var repository = new FileSystemNoteRepository(options);
        var listPage = new NoteListPage(repository, options, _settingsManager);
        var capturePage = new QuickCapturePage(repository);

        ICommandItem[] commands = [
            new CommandItem(listPage)
            {
                Title = DisplayName,
                Subtitle = "瀏覽與搜索筆記",
                MoreCommands = [new CommandContextItem(_settingsManager.Settings.SettingsPage)],
            },
            new CommandItem(capturePage)
            {
                Title = capturePage.Title,
                Subtitle = "打字記下想法,建議設一個 alias(例如 n)直接叫它",
                Icon = Icons.Add,
            },
            new CommandItem(new NewNotePage(repository))
            {
                Title = "Notelet:新增筆記",
                Subtitle = "開表單寫比較長的內容",
                Icon = Icons.Add,
            },
        ];

        // fallback 預設是關的,見 SettingsManager 那邊的說明 —— 它在目前的 CmdPal 上
        // 藏不乾淨。快速記下頁不受這個開關影響,那是使用者自己叫出來的。
        IFallbackCommandItem[] fallbacks = options.QuickCaptureEnabled
            ? [new QuickCaptureFallbackItem(new QuickCaptureCommand(repository), options)]
            : [];

        return new ProviderState(repository, listPage, capturePage, commands, fallbacks);
    }

    private void OnSettingsChanged(object? sender, Settings e)
    {
        ProviderState previous;

        lock (_gate)
        {
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
    /// 一組「跟著目前設定走」的東西。設定一改就整組換掉,舊的那組要確實釋放,
    /// 否則 FileSystemWatcher 與事件訂閱會一直累積。
    /// </summary>
    /// 標成 partial 是 CsWinRT 的要求:任何實作了 WinRT 投影介面(這裡是 IDisposable)
    /// 的型別都得讓來源產生器有地方掛程式碼,否則 trimming/AOT 下會出問題。
    private sealed partial record ProviderState(
        FileSystemNoteRepository Repository,
        NoteListPage ListPage,
        QuickCapturePage CapturePage,
        ICommandItem[] Commands,
        IFallbackCommandItem[] Fallbacks) : IDisposable
    {
        public void Dispose()
        {
            ListPage.Dispose();
            CapturePage.Dispose();
            Repository.Dispose();
        }
    }
}
