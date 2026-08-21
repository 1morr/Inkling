using System.Globalization;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Inkling.Commands;
using Inkling.Core;
using Inkling.Pages;
using Inkling.Properties;

namespace Inkling;

public sealed partial class InklingCommandsProvider : CommandProvider
{
    private readonly SettingsManager _settingsManager = new();
    private readonly Lock _gate = new();

    /// <summary>
    /// 設定頁,整個 provider 生命週期只有這一個。
    ///
    /// 不能跟著 <see cref="ProviderState"/> 重建:CmdPal 在 provider 剛連上時就把
    /// <see cref="CommandProvider.Settings"/> 讀走存成自己的 viewmodel,之後不再過問。
    /// 換了實例它也不知道,只會繼續用手上那個 —— 而送出表單之後要叫去重讀的正是它。
    /// 這一頁本來也不依賴 repository,跟著資料夾重建沒有意義。
    /// </summary>
    private readonly InklingSettingsPage _settingsPage;

    private ProviderState _state;

    public InklingCommandsProvider()
    {
        Id = CommandIds.Provider;
        DisplayName = "Inkling";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");

        // 介面語言沒有設定項,跟著 Windows 的顯示語言走。留一行紀錄是因為
        // 「為什麼我的 Inkling 變英文了」查起來只有這一個入口:擴展是 CmdPal 用 COM
        // 拉起來的獨立進程,拿到什麼語言從外面看不出來。抽一條字串出來一起印,
        // 是為了同時證明附屬組件真的載到了 —— 語言對、字串卻是英文,代表
        // zh-Hant\Inkling.resources.dll 沒進套件(trimming 或佈局出了問題)。
        DiagnosticLog.Write(
            $"UI 語言:{CultureInfo.CurrentUICulture.Name} 抽樣='{Resources.SettingsPageName}'");

        _settingsPage = new InklingSettingsPage(_settingsManager);

        // 包一層自己的 ICommandSettings,理由見 InklingCommandSettings。
        Settings = new InklingCommandSettings(_settingsPage);

        _state = BuildState();

        // 改了資料夾路徑之後,整組命令要跟著換掉 —— 舊的 repository 還盯著舊資料夾。
        // (只有資料夾會觸發重建,理由見 OnSettingsApplied。)
        _settingsManager.Applied += OnSettingsApplied;
    }

    /// <summary>
    /// CmdPal 把它自己交給我們的地方。**這裡不接的話,<c>ToastStatusMessage</c> 整條路是死的。**
    ///
    /// toolkit 的 <c>ToastStatusMessage.Show()</c> 走的是靜態的
    /// <c>ExtensionHost.ShowStatus</c>,而那個靜態類要先拿到 host 才有對象可呼叫 ——
    /// 沒有的話它靜靜地什麼都不做,不丟例外也不留痕跡。也就是說失敗提示
    /// (開不起來的檔案、複製不到內文)一句都沒有真的送到畫面上,而文檔一直把
    /// 那條路寫成「少數提示看得見的地方」。
    /// </summary>
    public override void InitializeWithHost(IExtensionHost host)
    {
        base.InitializeWithHost(host);
        ExtensionHost.Initialize(host);
    }

    // CmdPal 一啟動就會呼叫這個方法,絕對不能碰磁碟 —— 只回傳事先建好的靜態命令項。
    // 真正的載入延後到使用者實際打開清單頁時。
    //
    // 沒有 FallbackCommands():快速記下走的是頁面 + 使用者自設的 alias。
    // 為什麼不是 fallback,見 docs/design-notes.md〈快速記下為什麼是頁面,不是 fallback〉。
    public override ICommandItem[] TopLevelCommands() => _state.Commands;

    private ProviderState BuildState()
    {
        var options = _settingsManager.ToOptions();
        DiagnosticLog.Write($"BuildState: 資料夾='{options.NotesDirectory}'");

        // Repository 整個擴展共用一個。它內部有快取與資料夾監看,
        // 每頁各建一個等於每頁都重掃一次磁碟,還會多掛好幾個 FileSystemWatcher。
        // 刪除走資源回收筒,不是直接抹掉 —— 筆記是手打的東西,誤刪要拿得回來。
        var repository = new FileSystemNoteRepository(options, fileDeleter: new RecycleBinFileDeleter());

        // 三個 _settingsManager 是同一個物件,各自從一個窄介面看它 ——
        // 具名參數是為了讓呼叫端看得出誰是誰(見 ICaptureSeparatorStore 那一族的說明)。
        var capturePage = new QuickCapturePage(
            repository,
            separatorStore: _settingsManager,
            previewStore: _settingsManager,
            sourceMode: _settingsManager);

        // 新增筆記頁同樣是兩個地方掛同一個實例:頂層命令那一列,以及清單頁的 Ctrl+N。
        var newNotePage = new NewNotePage(repository);

        // 隨手草稿不經過 repository —— 它寫的是筆記資料夾裡一個固定檔名的純文字檔,
        // 而 repository 那一邊會把它排除在清單與搜索之外(見 ScratchpadStore.IsScratchpad)。
        // 資料夾一變就整組重建,新的 store 自然指向新資料夾;舊草稿留在舊資料夾裡不搬。
        var scratchpadPage = new ScratchpadPage(new ScratchpadStore(options));

        // 清單頁的空狀態會拿 capturePage 當那一列的命令(按 Enter 直接導覽過去),
        // 所以要先建。兩個地方掛同一個實例 —— 跟設定頁同一個做法。
        var listPage = new NoteListPage(repository, options, capturePage, newNotePage, _settingsManager);
        var deletePage = new DeleteNotesPage(repository, options, _settingsManager);

        ICommandItem[] commands = [
            new CommandItem(listPage)
            {
                Title = DisplayName,
                Subtitle = Resources.ProviderListSubtitle,

                // 明確指定圖示,不要靠繼承 provider 的那張套件磚:
                // 頂層命令是同一個單色家族(見 Icons.cs),漏掉這一行的話
                // 只有這一列會變成彩色方磚,跟其他三列對不起來。
                Icon = Icons.TopLevelList,

                // 跟 CmdPal 設定裡那一頁是同一個實例,兩邊看到的永遠一致。
                MoreCommands = [new CommandContextItem(_settingsPage)],
            },
            new CommandItem(capturePage)
            {
                // 頂層的標題跟頁面的標題**是兩條字串**,不要改回 capturePage.Title:
                // 這裡需要「Inkling:」前綴(主搜尋框裡要跟別的擴展區分),頁面裡不需要。
                // 四列的形狀因此一致,見 QuickCapturePage 的註解。
                Title = Resources.ProviderCapturePageTitle,

                // 這裡刻意不寫「分號」:分隔符是可以改的設定,而這一列的副標沒有跟著它更新
                // (頂層命令陣列只在資料夾變了才重建)。頁面裡的提示才會照使用者設的那個顯示。
                Subtitle = Resources.ProviderCaptureSubtitle,
                Icon = Icons.TopLevelCapture,
            },
            new CommandItem(newNotePage)
            {
                Title = Resources.ProviderNewNoteTitle,
                Subtitle = Resources.ProviderNewNoteSubtitle,
                Icon = Icons.TopLevelNew,
            },
            new CommandItem(scratchpadPage)
            {
                Title = Resources.ProviderScratchpadTitle,

                // 排在「新增筆記」後面、「刪除筆記」前面:三個寫東西的入口排在一起,
                // 會刪東西的那個殿後。
                Subtitle = Resources.ProviderScratchpadSubtitle,
                Icon = Icons.TopLevelScratchpad,
            },
            new CommandItem(deletePage)
            {
                // 同上:帶前綴的是頂層這一列,頁面自己叫「刪除筆記」。
                Title = Resources.ProviderDeletePageTitle,

                // 副標講的是「按下去會發生什麼」:進去只是看,不是當場刪。
                // 舊的版本寫「整個資料夾清空」,那句話兩頭都不準 ——
                // 清的只有 .md,而它清的又不只是 Inkling 自己建的那些。
                Subtitle = Resources.ProviderDeleteSubtitle,
                Icon = Icons.TopLevelDelete,
            },
        ];

        return new ProviderState(options.NotesDirectory, repository, listPage, capturePage, deletePage, commands);
    }

    /// <summary>
    /// 設定頁送出了表單。
    ///
    /// **只有資料夾變了才整組重建** —— 那時 repository 非換不可,它還盯著舊資料夾。
    /// 其他設定(分隔符、記下後先看一眼)刻意不重建,因為重建對它們根本沒用:CmdPal
    /// 手上握著的是使用者當下開著的那個頁面實例,新建的頁面它不會去拿(實測 log:
    /// <c>BuildState</c> 之後一次 <c>GetItems</c> 都沒有,直到 Reload)。硬重建反而更糟 ——
    /// 會把還在被使用的 repository 給 Dispose 掉,連 FileSystemWatcher 一起收走。
    /// 那兩項改走 <see cref="ICaptureSeparatorStore.CaptureSeparatorChanged"/> 與
    /// <see cref="ICapturePreviewStore.CapturePreviewChanged"/>,由頁面自己更新。
    /// </summary>
    private void OnSettingsApplied(object? sender, EventArgs e)
    {
        // **送出表單之後一律叫設定頁重讀,而且要排在下面那個 early return 前面。**
        //
        // 設定頁的卡片是建構時就把值烤進 DataJson 的,而「設定 → Extensions → Inkling」
        // 那個入口 CmdPal 只初始化一次 —— 導覽進去不會重新呼叫 GetContent()
        // (見 InklingSettingsPage 上的說明)。少了這一句,存完檔之後那張卡片就一直停在
        // 舊值:實際踩到的是分隔符改成 ## 存好了、設定頁卻永遠顯示 ;;。
        //
        // 而且比顯示錯更糟 —— 卡片上壓著的過期值會在下一次送出時**被當成使用者的輸入寫回去**,
        // 只改資料夾按儲存就足以把分隔符默默還原。所以這裡不分欄位、不比對新舊,一律重讀。
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
        DeleteNotesPage DeletePage,
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
