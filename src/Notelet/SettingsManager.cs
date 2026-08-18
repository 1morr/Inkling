using Microsoft.CommandPalette.Extensions.Toolkit;
using Notelet.Core;
using Notelet.Properties;

namespace Notelet;

/// <summary>
/// Notelet 的擴展設定,存在 CmdPal 為每個擴展準備的 settings.json 裡。
/// 這一層負責把使用者設定翻譯成 <see cref="NoteletOptions"/>,Core 層完全不知道設定存在哪。
///
/// 檔案位置是 <c>%LOCALAPPDATA%\Packages\{套件家族名}\LocalState\settings.json</c>
/// (<c>Utilities.BaseSettingsPath</c> 會走 MSIX 的重導向),每個擴展各存各的,
/// 跟其他擴展的做法一致。CmdPal 自己那份設定(啟用與否、alias、快速鍵、fallback 的顯示規則)
/// 則存在 CmdPal 的套件底下,由 CmdPal 管理,擴展碰不到。
/// </summary>
internal sealed partial class SettingsManager
    : JsonSettingsManager, ICaptureSeparatorStore, ICapturePreviewStore
{
    private const string SettingsNamespace = "Notelet";

    // 標籤與說明都來自資源檔 —— 這幾個欄位的字是使用者每次打開設定都要讀一次的東西,
    // 跟清單上的命令一樣要跟著 Windows 的顯示語言走。
    //
    // 說明那幾段會原樣進 Adaptive Cards 的 TextBlock,而它只認得粗體 / 斜體 / 清單 / 連結
    // 那幾種 markdown —— 反引號會照字面印出來,所以資源檔裡的引用一律用引號,不要用 `。
    private readonly TextSetting _notesDirectory = new(
        Namespaced(nameof(NotesDirectory)),
        Resources.SettingNotesDirectoryLabel,
        Resources.SettingNotesDirectoryDescription,
        NoteletOptions.DefaultNotesDirectory());

    private readonly TextSetting _captureSeparator = new(
        Namespaced(nameof(CaptureSeparator)),
        Resources.SettingSeparatorLabel,
        Resources.SettingSeparatorDescription,
        QuickCapture.DefaultSeparator);

    private readonly ToggleSetting _capturePreview = new(
        Namespaced(nameof(ShowCapturePreview)),
        Resources.SettingPreviewLabel,

        // 說明只留「按下去會發生什麼」。取捨的理由(為什麼預設開、為什麼沒有第二條路)
        // 屬於 README,不是設定頁 —— 那段字每次打開設定都要看一次。
        Resources.SettingPreviewDescription,
        true);

    public SettingsManager()
    {
        FilePath = SettingsJsonPath();

        // **這個順序就是載入順序,而載入是「一項爆掉、後面全部不載入」。**
        //
        // toolkit 的 Settings.Update 是一個沒有逐項 try/catch 的 foreach,例外一路拋到
        // JsonSettingsManager.LoadSettings 的 catch —— 排在後面的設定項連碰都碰不到,
        // 靜靜地退回預設值(實際踩過:把這個開關手動寫成 JSON 的 true,結果排在它後面的
        // 那一項就這樣退回預設,而且沒有任何錯誤訊息)。
        //
        // 所以 _capturePreview 刻意排最後:它是唯一的布林項,而 ToggleSetting 存的是
        // **字串** "true" / "false"(見它的 ToState;Adaptive Cards 的 Input.Toggle
        // 回傳的就是字串)。人手去改 settings.json 時最容易在這一項寫成 JSON 的 true ——
        // 排最後,寫錯就只影響它自己。
        //
        // 我們自己畫設定卡片,所以這個順序不影響畫面上的欄位順序(那個在 NoteletSettingsForm)。
        Settings.Add(_notesDirectory);
        Settings.Add(_captureSeparator);
        Settings.Add(_capturePreview);

        LoadSettings();
        DiagnosticLog.Write(
            $"SettingsManager: 載入 {FilePath} 分隔符='{CaptureSeparator}' "
                + $"記下後預覽={ShowCapturePreview}");
    }

    /// <inheritdoc />
    public event EventHandler? CaptureSeparatorChanged;

    /// <inheritdoc />
    public event EventHandler? CapturePreviewChanged;

    /// <summary>
    /// 設定頁送出表單之後發出(不管值有沒有真的變)。
    ///
    /// 為什麼不是 toolkit 的 <c>Settings.SettingsChanged</c>:那個事件只有 toolkit 自己的
    /// <c>SettingsForm</c> 發得出來(<c>RaiseSettingsChanged()</c> 是 internal),而設定頁的
    /// 表單已經換成我們自己的了 —— 理由見 <see cref="Pages.NoteletSettingsForm"/>。
    /// </summary>
    public event EventHandler? Applied;

    /// <summary>
    /// 設定頁的表單照這幾個定義畫 —— 標籤與說明只有這一份。
    /// </summary>
    public TextSetting NotesDirectorySetting => _notesDirectory;

    /// <inheritdoc cref="NotesDirectorySetting" />
    public TextSetting CaptureSeparatorSetting => _captureSeparator;

    /// <inheritdoc cref="NotesDirectorySetting" />
    public ToggleSetting CapturePreviewSetting => _capturePreview;

    /// <summary>
    /// 表單送出(按「儲存」,或選完資料夾)之後的唯一入口:寫值、存檔、通知。
    /// </summary>
    /// <param name="notesDirectory">使用者填的路徑;空白代表回到預設資料夾。</param>
    /// <param name="captureSeparator">快速記下的分隔符;空白代表回到預設值。</param>
    /// <param name="showCapturePreview">記下之後要不要停在預覽頁。</param>
    public void Apply(
        string notesDirectory,
        string captureSeparator,
        bool showCapturePreview)
    {
        var directory = string.IsNullOrWhiteSpace(notesDirectory)
            ? NoteletOptions.DefaultNotesDirectory()
            : notesDirectory.Trim();

        // 存回去的是**整理過**的值(去空白、空的話退回預設),而不是使用者原本打的那串。
        // 這樣設定頁下次打開時顯示的就是實際生效的分隔符,不會出現「看起來設了、其實沒生效」。
        var separator = QuickCapture.NormalizeSeparator(captureSeparator);
        var separatorChanged = !string.Equals(CaptureSeparator, separator, StringComparison.Ordinal);

        var previewChanged = ShowCapturePreview != showCapturePreview;

        _notesDirectory.Value = directory;
        _captureSeparator.Value = separator;
        _capturePreview.Value = showCapturePreview;

        Save("Apply");
        DiagnosticLog.Write(
            $"Apply: 資料夾='{directory}' 分隔符='{separator}' 記下後預覽={showCapturePreview}");

        // 資料夾變了就得換掉整組 repository,那是 provider 的事 —— 它自己比對舊值。
        Applied?.Invoke(this, EventArgs.Empty);

        // 剩下這兩個都是「頁面自己響應」的路 —— 見 ICaptureSeparatorStore 上的說明。
        // 排在 Applied 後面沒有關係:資料夾真的變了的話,provider 已經把舊頁面
        // 連同它的訂閱一起釋放,新頁面本來就是拿新值建的。
        if (separatorChanged)
        {
            CaptureSeparatorChanged?.Invoke(this, EventArgs.Empty);
        }

        if (previewChanged)
        {
            CapturePreviewChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 存檔並留下痕跡。
    ///
    /// toolkit 的 <see cref="JsonSettingsManager.SaveSettings"/> 自己把例外吞掉,
    /// 只往 CmdPal 的 log 丟一行字。設定存不起來的時候使用者看到的是「按了 Save 什麼都沒發生」,
    /// 查不出原因 —— 實際被這件事咬過一次,所以這裡自己記一筆。
    /// </summary>
    private void Save(string reason)
    {
        try
        {
            SaveSettings();
            DiagnosticLog.Write($"SaveSettings({reason}): 已寫入 {FilePath}");
        }
        catch (Exception ex)
        {
            // 設定存不起來不該讓整個擴展掛掉,但也不能無聲無息。
            DiagnosticLog.Write($"SaveSettings({reason}) 失敗:{ex}");
        }
    }

    public string NotesDirectory => _notesDirectory.Value ?? NoteletOptions.DefaultNotesDirectory();

    /// <inheritdoc />
    /// <remarks>
    /// 每次都重新整理一遍,不快取:舊版的 settings.json 裡根本沒有這個鍵,而使用者也可能
    /// 直接拿編輯器去改那個檔案。讓讀取端永遠拿到可用的值,比在載入時修一次可靠。
    /// </remarks>
    public string CaptureSeparator => QuickCapture.NormalizeSeparator(_captureSeparator.Value);

    /// <inheritdoc />
    public bool ShowCapturePreview => _capturePreview.Value;

    public NoteletOptions ToOptions()
    {
        // 使用者可能把資料夾欄位清空。與其讓擴展壞掉,不如退回預設值。
        var directory = string.IsNullOrWhiteSpace(NotesDirectory)
            ? NoteletOptions.DefaultNotesDirectory()
            : NotesDirectory.Trim();

        return new NoteletOptions { NotesDirectory = directory };
    }

    private static string Namespaced(string propertyName) => $"{SettingsNamespace}.{propertyName}";

    private static string SettingsJsonPath()
    {
        var directory = Utilities.BaseSettingsPath(SettingsNamespace);
        Directory.CreateDirectory(directory);

        return Path.Combine(directory, "settings.json");
    }
}
