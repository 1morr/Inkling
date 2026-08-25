using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Inkling.Core;
using Inkling.Properties;

namespace Inkling;

/// <summary>
/// Inkling 的擴展設定，存在 CmdPal 為每個擴展準備的 settings.json 裡。
/// 這一層負責把使用者設定翻譯成 <see cref="InklingOptions"/>,Core 層完全不知道設定存在哪。
///
/// 檔案位置是 <c>%LOCALAPPDATA%\Packages\{套件家族名}\LocalState\settings.json</c>
/// (<c>Utilities.BaseSettingsPath</c> 會走 MSIX 的重導向)，每個擴展各存各的，
/// 跟其他擴展的做法一致。CmdPal 自己那份設定(啟用與否、alias、快速鍵、fallback 的顯示規則)
/// 則存在 CmdPal 的套件底下，由 CmdPal 管理，擴展碰不到。
/// </summary>
internal sealed partial class SettingsManager
    : JsonSettingsManager, ICaptureSeparatorStore, ICapturePreviewStore, ISourceModeStore
{
    private const string SettingsNamespace = "Inkling";

    // 標籤與說明都來自資源檔 —— 這幾個欄位的字是使用者每次打開設定都要讀一次的東西，
    // 跟清單上的命令一樣要跟著 Windows 的顯示語言走。
    //
    // 說明那幾段會原樣進 Adaptive Cards 的 TextBlock，而它只認得粗體 / 斜體 / 清單 / 連結
    // 那幾種 markdown —— 反引號會照字面印出來，所以資源檔裡的引用一律用引號，不要用 `。
    private readonly TextSetting _notesDirectory = new(
        Namespaced(nameof(NotesDirectory)),
        Resources.SettingNotesDirectoryLabel,
        Resources.SettingNotesDirectoryDescription,
        InklingOptions.DefaultNotesDirectory());

    private readonly TextSetting _captureSeparator = new(
        Namespaced(nameof(CaptureSeparator)),
        Resources.SettingSeparatorLabel,
        Resources.SettingSeparatorDescription,
        QuickCapture.DefaultSeparator);

    private readonly ToggleSetting _capturePreview = new(
        Namespaced(nameof(ShowCapturePreview)),
        Resources.SettingPreviewLabel,

        // 說明只留「按下去會發生什麼」。取捨的理由(為什麼預設開、為什麼沒有第二條路)
        // 屬於 docs/design-notes.md，不是設定頁 —— 那段字每次打開設定都要看一次。
        Resources.SettingPreviewDescription,
        true);

    /// <summary>
    /// 原始文字模式。**這一項不在設定頁上** —— 它的介面是 <c>Ctrl+U</c> 那個切換鍵，
    /// 存在這裡只是為了記住上一次的選擇(見 <see cref="ISourceModeStore.ShowSource"/>)。
    ///
    /// 標籤與說明還是給了資源字串:這個型別的建構子要求要有，而萬一哪天它真的被畫出來，
    /// 借用切換鍵那兩條字串至少讀得通。
    /// </summary>
    private readonly ToggleSetting _showSource = new(
        Namespaced(nameof(ShowSource)),
        Resources.ToggleSourceShowRaw,
        Resources.ToggleSourceSubtitle,
        false);

    public SettingsManager()
    {
        FilePath = SettingsJsonPath();

        // **這個順序就是載入順序，而載入是「一項爆掉、後面全部不載入」。**
        //
        // toolkit 的 Settings.Update 是一個沒有逐項 try/catch 的 foreach，例外一路拋到
        // JsonSettingsManager.LoadSettings 的 catch —— 排在後面的設定項連碰都碰不到，
        // 靜靜地退回預設值(實際踩過:把這個開關手動寫成 JSON 的 true，結果排在它後面的
        // 那一項就這樣退回預設，而且沒有任何錯誤訊息)。
        //
        // 所以 _capturePreview 刻意排最後:它是唯一的布林項，而 ToggleSetting 存的是
        // **字串** "true" / "false"(見它的 ToState;Adaptive Cards 的 Input.Toggle
        // 回傳的就是字串)。人手去改 settings.json 時最容易在這一項寫成 JSON 的 true ——
        // 排最後，寫錯就只影響它自己。
        //
        // 我們自己畫設定卡片，所以這個順序不影響畫面上的欄位順序(那個在 InklingSettingsForm)。
        //
        // 兩個布林項的先後也是照這條規則排的:**壞掉的代價小的排後面**。
        // _capturePreview 是設定頁上看得到、使用者可能手改的那一個，所以它排在
        // 兩個字串項後面;_showSource 排最後 —— 它只是檢視狀態，再按一次 Ctrl+U 就回來，
        // 是這三項裡唯一丟了也不痛的。
        Settings.Add(_notesDirectory);
        Settings.Add(_captureSeparator);
        Settings.Add(_capturePreview);
        Settings.Add(_showSource);

        // **一定要排在 LoadSettings 前面。** 檔案壞掉的話這一步先把它搬走，
        // 後面的載入與存檔才回得到可用狀態，見 QuarantineCorruptSettings。
        QuarantineCorruptSettings();

        LoadSettings();
        DiagnosticLog.Write(
            $"SettingsManager: loaded {FilePath} separator='{CaptureSeparator}' "
                + $"capturePreview={ShowCapturePreview} showSource={ShowSource}");
    }

    /// <inheritdoc />
    public event EventHandler? CaptureSeparatorChanged;

    /// <inheritdoc />
    public event EventHandler? CapturePreviewChanged;

    /// <inheritdoc />
    public event EventHandler? ShowSourceChanged;

    /// <summary>
    /// <see cref="Apply"/> 對資料夾欄位的處理結果，設定頁照它決定要跟使用者講哪句話。
    /// </summary>
    public enum ApplyResult
    {
        /// <summary>全部存好了。</summary>
        Applied,

        /// <summary>
        /// 存好了，但資料夾還不存在(第一次存檔時才會由 repository 建立)。
        /// 要跟使用者講一聲:打錯一個字就靜靜換了資料夾，看起來會像「舊筆記全部消失」。
        /// </summary>
        AppliedToMissingFolder,

        /// <summary>拒絕:不是完整路徑。整筆都沒存(分隔符與預覽開關也一樣)。</summary>
        RejectedRelativePath,

        /// <summary>
        /// 值在這個工作階段生效了，但 <c>settings.json</c> 寫不進去 —— 重啟之後會還原。
        ///
        /// 這條路以前完全偵測不到:<see cref="Save"/> 把例外記進 DiagnosticLog 就算了，
        /// 沒有回傳給呼叫端，於是磁碟滿了、LocalState 權限壞掉的時候，使用者看到的仍然是
        /// 「設定已儲存」，下次打開卻是舊值。而 diagnostic.log 預設是關的，
        /// 也就是說那個失敗對使用者等於不存在。
        /// </summary>
        SaveFailed,
    }

    /// <summary>
    /// 設定頁送出表單之後發出(不管值有沒有真的變)。
    ///
    /// 為什麼不是 toolkit 的 <c>Settings.SettingsChanged</c>:那個事件只有 toolkit 自己的
    /// <c>SettingsForm</c> 發得出來(<c>RaiseSettingsChanged()</c> 是 internal)，而設定頁的
    /// 表單已經換成我們自己的了 —— 理由見 <see cref="Pages.InklingSettingsForm"/>。
    /// </summary>
    public event EventHandler? Applied;

    /// <summary>
    /// 上一次啟動時 <c>settings.json</c> 壞掉，被搬到這個路徑;null = 沒發生過。
    ///
    /// 設定頁會把它講出來 —— 沒有這一句的話，使用者只會看到「我的筆記全部不見了」
    /// (資料夾退回預設值)，而完全沒有線索。見 <see cref="QuarantineCorruptSettings"/>。
    /// </summary>
    public string? QuarantinedFile { get; private set; }

    /// <summary>
    /// 設定頁的表單照這幾個定義畫 —— 標籤與說明只有這一份。
    /// </summary>
    public TextSetting NotesDirectorySetting => _notesDirectory;

    /// <inheritdoc cref="NotesDirectorySetting" />
    public TextSetting CaptureSeparatorSetting => _captureSeparator;

    /// <inheritdoc cref="NotesDirectorySetting" />
    public ToggleSetting CapturePreviewSetting => _capturePreview;

    /// <summary>
    /// 表單送出(按「儲存」，或選完資料夾)之後的唯一入口:寫值、存檔、通知。
    /// </summary>
    /// <param name="notesDirectory">使用者填的路徑;空白代表回到預設資料夾。</param>
    /// <param name="captureSeparator">快速記下的分隔符;空白代表回到預設值。</param>
    /// <param name="showCapturePreview">記下之後要不要停在預覽頁。</param>
    /// <returns>資料夾欄位的處理結果;相對路徑會整筆拒絕，什麼都不存。</returns>
    public ApplyResult Apply(
        string notesDirectory,
        string captureSeparator,
        bool showCapturePreview)
    {
        var directory = NormalizeDirectory(notesDirectory);

        // 擋相對路徑(含「C:foo」這種磁碟機相對):它會對著擴展 COM server 進程的 CWD
        // 解析，筆記落在使用者意想不到的位置 —— 看起來就像「舊筆記全部消失」。
        // 整筆退回，分隔符與預覽開關也不存:表單留在原地，使用者改完路徑再送一次就好，
        // 部分儲存只會讓「到底哪些生效了」變得難猜。
        if (!Path.IsPathFullyQualified(directory))
        {
            DiagnosticLog.Failure("Apply: rejected a path that is not fully qualified, nothing was saved", directory);
            return ApplyResult.RejectedRelativePath;
        }

        // 存回去的是**整理過**的值(去空白、空的話退回預設)，而不是使用者原本打的那串。
        // 這樣設定頁下次打開時顯示的就是實際生效的分隔符，不會出現「看起來設了、其實沒生效」。
        var separator = QuickCapture.NormalizeSeparator(captureSeparator);
        var separatorChanged = !string.Equals(CaptureSeparator, separator, StringComparison.Ordinal);

        var previewChanged = ShowCapturePreview != showCapturePreview;

        _notesDirectory.Value = directory;
        _captureSeparator.Value = separator;
        _capturePreview.Value = showCapturePreview;

        var saved = Save("Apply");
        DiagnosticLog.Write(
            $"Apply: directory='{directory}' separator='{separator}' capturePreview={showCapturePreview}");

        // 資料夾變了就得換掉整組 repository，那是 provider 的事 —— 它自己比對舊值。
        Applied?.Invoke(this, EventArgs.Empty);

        // 剩下這兩個都是「頁面自己響應」的路 —— 見 ICaptureSeparatorStore 上的說明。
        // 排在 Applied 後面沒有關係:資料夾真的變了的話，provider 已經把舊頁面
        // 連同它的訂閱一起釋放，新頁面本來就是拿新值建的。
        if (separatorChanged)
        {
            CaptureSeparatorChanged?.Invoke(this, EventArgs.Empty);
        }

        if (previewChanged)
        {
            CapturePreviewChanged?.Invoke(this, EventArgs.Empty);
        }

        // 寫不進磁碟優先講:那一句涵蓋的問題比「資料夾還不存在」嚴重
        // (值在這個工作階段還是生效的，但重啟就沒了)。
        if (!saved)
        {
            return ApplyResult.SaveFailed;
        }

        // 不存在的完整路徑不擋 —— repository 本來就會在第一次存檔時把它建出來。
        // 但回給呼叫端講一聲，讓「打錯一個字就靜靜換了家」當場可見，而不是下次才發現。
        return Directory.Exists(directory)
            ? ApplyResult.Applied
            : ApplyResult.AppliedToMissingFolder;
    }

    /// <summary>
    /// 存檔並留下痕跡。
    ///
    /// toolkit 的 <see cref="JsonSettingsManager.SaveSettings"/> 自己把例外吞掉，
    /// 只往 CmdPal 的 log 丟一行字。設定存不起來的時候使用者看到的是「按了 Save 什麼都沒發生」,
    /// 查不出原因 —— 實際被這件事咬過一次，所以這裡自己記一筆。
    ///
    /// <para><b>而且要驗證。</b></para>
    ///
    /// 光靠「沒有丟例外」判斷不了成功:<c>SaveSettings</c> 內部會先 <c>JsonNode.Parse</c>
    /// 讀舊內容再合併，舊內容不是合法 JSON 時它走另一條分支 —— **完全不寫檔，也不丟例外**。
    /// 於是這個方法回 true、設定頁走成功路徑、檔案一個位元組都沒變，而
    /// <see cref="ApplyResult.SaveFailed"/> 那條路永遠到不了。
    /// (啟動時的 <see cref="QuarantineCorruptSettings"/> 擋掉了大部分，但檔案也可能是
    /// 在執行期間被外部改壞的。)所以寫完讀回來對一次。
    /// </summary>
    /// <returns>有沒有真的寫進磁碟。<b>回傳值一定要往上傳</b> —— 只記進 diagnostic.log
    /// 的話，對使用者等於沒發生:那個 log 預設是關的，而設定頁照樣說「設定已儲存」。</returns>
    private bool Save(string reason)
    {
        try
        {
            SaveSettings();
        }
        catch (Exception ex)
        {
            // 設定存不起來不該讓整個擴展掛掉，但也不能無聲無息。
            DiagnosticLog.Failure($"SaveSettings({reason}) failed ({ex.GetType().Name})", ex.ToString());
            return false;
        }

        if (!Persisted())
        {
            DiagnosticLog.Failure(
                $"SaveSettings({reason}): the file on disk does not hold what we just wrote",
                FilePath);
            return false;
        }

        DiagnosticLog.Write($"SaveSettings({reason}): wrote {FilePath}");
        return true;
    }

    /// <summary>
    /// 磁碟上那份真的是我們剛寫的嗎。
    ///
    /// 只對筆記資料夾那一項 —— 它是這四項裡唯一「錯了會讓使用者以為筆記全部不見」的，
    /// 而任何一種寫入失敗都會讓它對不上。逐項比對沒有更多資訊，只是多幾行。
    /// </summary>
    private bool Persisted()
    {
        try
        {
            var saved = JsonNode.Parse(File.ReadAllText(FilePath));

            return string.Equals(
                saved?[Namespaced(nameof(NotesDirectory))]?.ToString(),
                _notesDirectory.Value,
                StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// <c>settings.json</c> 不是合法 JSON 的話，把它搬成
    /// <c>settings.json.corrupt-&lt;時間戳&gt;</c>。
    ///
    /// <para><b>不搬走的話這個擴展的設定會永久性、而且無聲地卡住。</b></para>
    ///
    /// 讀:toolkit 的 <c>LoadSettings</c> 把例外吞掉，四項設定全部退回預設 ——
    /// **筆記資料夾變回 <c>%OneDrive%\Inkling</c>**，使用者的清單換成別的內容。
    /// 寫:<c>SaveSettings</c> 也要先解析舊內容，失敗就整個不寫(見 <see cref="Save"/>)。
    /// 兩邊加起來就是「設定頁怎麼改都沒有用，重啟又還原」，而使用者在 app 裡修不好它 ——
    /// 唯一的解是手動去刪那個檔案，而他不會知道要去做。
    ///
    /// 觸發不需要使用者手改:toolkit 走的是 <c>File.WriteAllText</c>,**不是** atomic write
    /// (我們自己寫筆記時走 <c>AtomicFile</c>，設定檔沒有這個保護)，寫到一半斷電或當機
    /// 就會留下半個檔案。
    ///
    /// 搬走而不是刪掉:那裡面是使用者設過的東西，壞的可能只有一個字元，手工救得回來。
    /// 路徑記在 <see cref="QuarantinedFile"/> 上，設定頁會把它講出來。
    /// </summary>
    private void QuarantineCorruptSettings()
    {
        string content;

        try
        {
            if (!File.Exists(FilePath))
            {
                return;
            }

            content = File.ReadAllText(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 讀不到就別動它 —— 可能只是同步軟體暫時鎖住，搬走反而製造問題。
            return;
        }

        try
        {
            JsonNode.Parse(content);
            return;
        }
        catch (JsonException)
        {
            // 往下走，搬走它。
        }

        var quarantine = FormattableString.Invariant(
            $"{FilePath}.corrupt-{DateTimeOffset.Now:yyyyMMdd-HHmmss}");

        try
        {
            File.Move(FilePath, quarantine, overwrite: true);
            QuarantinedFile = quarantine;

            // 這一條要進共用通道:它是「使用者的設定莫名其妙全部還原」唯一的線索，
            // 而 diagnostic.log 預設是關的。摘要不帶路徑(那是使用者名字)，細節走本機那份。
            DiagnosticLog.Failure("settings.json was not valid JSON; moved it aside and started from defaults", quarantine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DiagnosticLog.Failure(
                $"settings.json is corrupt and could not be moved aside ({ex.GetType().Name})",
                ex.ToString());
        }
    }

    public string NotesDirectory => NormalizeDirectory(_notesDirectory.Value);

    /// <inheritdoc />
    /// <remarks>
    /// 每次都重新整理一遍，不快取:舊版的 settings.json 裡根本沒有這個鍵，而使用者也可能
    /// 直接拿編輯器去改那個檔案。讓讀取端永遠拿到可用的值，比在載入時修一次可靠。
    /// </remarks>
    public string CaptureSeparator => QuickCapture.NormalizeSeparator(_captureSeparator.Value);

    /// <inheritdoc />
    public bool ShowCapturePreview => _capturePreview.Value;

    /// <inheritdoc />
    /// <remarks>
    /// **這一項不走 <see cref="Apply"/>** —— 那是設定表單的入口，而這個值是使用者在
    /// 清單頁或預覽頁按 <c>Ctrl+U</c> 當場改的。兩邊各自寫回同一個 <c>settings.json</c>
    /// 沒有問題:<c>SaveSettings</c> 每次都把整份設定寫出去，而表單那邊的欄位值
    /// 也是送出當下才從卡片讀的。
    ///
    /// 值一樣就整個跳過:不寫檔(切換鍵按得很兇)，也不發事件(否則每個頁面都會白重整一次)。
    /// </remarks>
    public bool ShowSource
    {
        get => _showSource.Value;
        set
        {
            if (_showSource.Value == value)
            {
                return;
            }

            _showSource.Value = value;
            Save("ShowSource");
            DiagnosticLog.Write($"ShowSource: toggled to {value}");

            ShowSourceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public InklingOptions ToOptions() => new() { NotesDirectory = NotesDirectory };

    /// <summary>
    /// 資料夾欄位的正規化只有這一份:空白(含沒設過)退回預設資料夾，其餘去頭尾空白。
    /// 使用者可能把欄位清空，與其讓擴展壞掉，不如退回預設值。
    /// </summary>
    private static string NormalizeDirectory(string? value) =>
        string.IsNullOrWhiteSpace(value) ? InklingOptions.DefaultNotesDirectory() : value.Trim();

    private static string Namespaced(string propertyName) => $"{SettingsNamespace}.{propertyName}";

    private static string SettingsJsonPath()
    {
        var directory = Utilities.BaseSettingsPath(SettingsNamespace);
        Directory.CreateDirectory(directory);

        return Path.Combine(directory, "settings.json");
    }
}
