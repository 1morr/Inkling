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

    /// <summary>
    /// 設定頁,整個 provider 生命週期只有這一個。
    ///
    /// 不能跟著 <see cref="ProviderState"/> 重建:CmdPal 在 provider 剛連上時就把
    /// <see cref="CommandProvider.Settings"/> 讀走存成自己的 viewmodel,之後不再過問。
    /// 換了實例它也不知道,只會繼續用手上那個 —— 而 Ctrl+D 要通知的正是它。
    /// 這一頁本來也不依賴 repository,跟著資料夾重建沒有意義。
    /// </summary>
    private readonly NoteletSettingsPage _settingsPage;

    private ProviderState _state;

    public NoteletCommandsProvider()
    {
        Id = CommandIds.Provider;
        DisplayName = "Notelet";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");

        _settingsPage = new NoteletSettingsPage(_settingsManager);

        // 包一層自己的 ICommandSettings,理由見 NoteletCommandSettings。
        Settings = new NoteletCommandSettings(_settingsPage);

        // 清單頁按 Ctrl+D 改寬度時,設定頁的下拉選單要跟著變。
        _settingsManager.DetailsWidthChanged += (_, _) => _settingsPage.Refresh();

        _state = BuildState();

        // 改了資料夾路徑之後,整組命令要跟著換掉 —— 舊的 repository 還盯著舊資料夾。
        // (只有資料夾會觸發重建,理由見 OnSettingsApplied。)
        _settingsManager.Applied += OnSettingsApplied;
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
        var capturePage = new QuickCapturePage(repository, _settingsManager, _settingsManager);
        var deletePage = new DeleteAllNotesPage(repository, options, _settingsManager);

        ICommandItem[] commands = [
            new CommandItem(listPage)
            {
                Title = DisplayName,
                Subtitle = "瀏覽與搜索筆記",

                // 跟 CmdPal 設定裡那一頁是同一個實例,兩邊看到的永遠一致。
                MoreCommands = [new CommandContextItem(_settingsPage)],
            },
            new CommandItem(capturePage)
            {
                Title = capturePage.Title,
                // 這裡刻意不寫「分號」:分隔符是可以改的設定,而這一列的副標沒有跟著它更新
                // (頂層命令陣列只在資料夾變了才重建)。頁面裡的提示才會照使用者設的那個顯示。
                Subtitle = "打字直接存成筆記,分隔符後面接內文",
                Icon = Icons.Capture,
            },
            new CommandItem(new NewNotePage(repository))
            {
                Title = "Notelet:新增筆記",
                Subtitle = "開表單寫比較長的內容",
                Icon = Icons.Add,
            },
            new CommandItem(deletePage)
            {
                Title = deletePage.Title,

                // 副標講的是「按下去會發生什麼」:進去只是看,不是當場刪。
                // 舊的版本寫「整個資料夾清空」,那句話兩頭都不準 ——
                // 清的只有 .md,而它清的又不只是 Notelet 自己建的那些。
                Subtitle = "先列出會刪掉哪些檔案,確認後才動手",
                Icon = Icons.Delete,
            },
        ];

        return new ProviderState(options.NotesDirectory, repository, listPage, capturePage, deletePage, commands);
    }

    /// <summary>
    /// 設定頁送出了表單。
    ///
    /// **只有資料夾變了才整組重建** —— 那時 repository 非換不可,它還盯著舊資料夾。
    /// 其他設定(寬度)刻意不重建,因為重建對它們根本沒用:CmdPal 手上握著的是使用者
    /// 當下開著的那個頁面實例,新建的頁面它不會去拿(實測 log:<c>BuildState</c> 之後
    /// 一次 <c>GetItems</c> 都沒有,直到 Reload)。硬重建反而更糟 —— 會把還在被使用的
    /// repository 給 Dispose 掉,連 FileSystemWatcher 一起收走。
    /// 寬度改走 <see cref="IDetailsWidthStore.DetailsWidthChanged"/>,由頁面自己更新。
    /// </summary>
    private void OnSettingsApplied(object? sender, EventArgs e)
    {
        // **送出表單之後一律叫設定頁重讀,而且要排在下面那個 early return 前面。**
        //
        // 設定頁的卡片是建構時就把值烤進 DataJson 的,而「設定 → Extensions → Notelet」
        // 那個入口 CmdPal 只初始化一次 —— 導覽進去不會重新呼叫 GetContent()
        // (見 NoteletSettingsPage 上的說明)。少了這一句,存完檔之後那張卡片就一直停在
        // 舊值:實際踩到的是分隔符改成 ## 存好了、設定頁卻永遠顯示 ;;。
        //
        // 而且比顯示錯更糟 —— 卡片上壓著的過期值會在下一次送出時**被當成使用者的輸入寫回去**,
        // 只改資料夾按儲存就足以把分隔符默默還原。所以這裡不分欄位、不比對新舊,一律重讀。
        //
        // 寬度那條線(DetailsWidthChanged → Refresh)要留著:Ctrl+D 是從設定頁外面改值,
        // 根本不會走到這裡。兩邊都命中時會多重讀一次,無害。
        _settingsPage.Refresh();

        var directory = _settingsManager.ToOptions().NotesDirectory;
        ProviderState previous;

        lock (_gate)
        {
            if (string.Equals(_state.NotesDirectory, directory, StringComparison.OrdinalIgnoreCase))
            {
                DiagnosticLog.Write("SettingsApplied: 資料夾沒變,不重建");
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
        _settingsManager.Applied -= OnSettingsApplied;
        _state.Dispose();

        base.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 一組「跟著目前資料夾走」的東西。資料夾一改就整組換掉(其他設定不會,
    /// 見 <see cref="OnSettingsApplied"/>),舊的那組要確實釋放,
    /// 否則 FileSystemWatcher 與事件訂閱會一直累積。
    /// </summary>
    /// 標成 partial 是 CsWinRT 的要求:任何實作了 WinRT 投影介面(這裡是 IDisposable)
    /// 的型別都得讓來源產生器有地方掛程式碼,否則 trimming/AOT 下會出問題。
    private sealed partial record ProviderState(
        string NotesDirectory,
        FileSystemNoteRepository Repository,
        NoteListPage ListPage,
        QuickCapturePage CapturePage,
        DeleteAllNotesPage DeletePage,
        ICommandItem[] Commands) : IDisposable
    {
        public void Dispose()
        {
            ListPage.Dispose();
            CapturePage.Dispose();
            DeletePage.Dispose();
            Repository.Dispose();
        }
    }
}
