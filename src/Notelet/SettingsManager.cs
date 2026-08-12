using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Notelet.Core;

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
internal sealed partial class SettingsManager : JsonSettingsManager, IDetailsWidthStore
{
    private const string SettingsNamespace = "Notelet";

    private const string NarrowWidth = "small";
    private const string MediumWidth = "medium";
    private const string WideWidth = "large";

    private readonly TextSetting _notesDirectory = new(
        Namespaced(nameof(NotesDirectory)),
        "筆記資料夾",
        "放在 OneDrive 之類的雲端硬碟底下,多端同步就交給它處理 —— Notelet 自己不做同步。",
        NoteletOptions.DefaultNotesDirectory());

    private readonly ChoiceSetSetting _detailsWidth = new(
        Namespaced(nameof(DetailsWidth)),
        "詳細面板寬度",
        "清單頁右邊那塊佔多寬。清單頁按 Ctrl+D 可以直接循環,選好的檔位存回這裡。",
        [
            new ChoiceSetSetting.Choice("窄(清單:詳情 = 3:1)", NarrowWidth),
            new ChoiceSetSetting.Choice("中(2:1)", MediumWidth),
            new ChoiceSetSetting.Choice("寬(1:1)", WideWidth),
        ]);

    public SettingsManager()
    {
        FilePath = SettingsJsonPath();

        Settings.Add(_notesDirectory);
        Settings.Add(_detailsWidth);

        LoadSettings();
        DiagnosticLog.Write($"SettingsManager: 載入 {FilePath} 寬度={_detailsWidth.Value}");
    }

    /// <inheritdoc />
    public event EventHandler? DetailsWidthChanged;

    /// <summary>
    /// 設定頁送出表單之後發出(不管值有沒有真的變)。
    ///
    /// 為什麼不是 toolkit 的 <c>Settings.SettingsChanged</c>:那個事件只有 toolkit 自己的
    /// <c>SettingsForm</c> 發得出來(<c>RaiseSettingsChanged()</c> 是 internal),而設定頁的
    /// 表單已經換成我們自己的了 —— 理由見 <see cref="Pages.NoteletSettingsForm"/>。
    /// </summary>
    public event EventHandler? Applied;

    /// <summary>
    /// 設定頁的表單照這兩個定義畫 —— 標籤、說明、選項只有這一份。
    /// </summary>
    public TextSetting NotesDirectorySetting => _notesDirectory;

    /// <inheritdoc cref="NotesDirectorySetting" />
    public ChoiceSetSetting DetailsWidthSetting => _detailsWidth;

    /// <summary>
    /// 表單送出(按「儲存」,或選完資料夾)之後的唯一入口:寫值、存檔、通知。
    /// </summary>
    /// <param name="notesDirectory">使用者填的路徑;空白代表回到預設資料夾。</param>
    /// <param name="detailsWidth">下拉選單的值;不認得就當作沒改。</param>
    public void Apply(string notesDirectory, string detailsWidth)
    {
        var directory = string.IsNullOrWhiteSpace(notesDirectory)
            ? NoteletOptions.DefaultNotesDirectory()
            : notesDirectory.Trim();

        var widthChanged = _detailsWidth.Choices.Any(choice => choice.Value == detailsWidth)
            && !string.Equals(_detailsWidth.Value, detailsWidth, StringComparison.Ordinal);

        _notesDirectory.Value = directory;

        if (widthChanged)
        {
            _detailsWidth.Value = detailsWidth;
        }

        Save("Apply");
        DiagnosticLog.Write($"Apply: 資料夾='{directory}' 寬度={_detailsWidth.Value}");

        // 資料夾變了就得換掉整組 repository,那是 provider 的事 —— 它自己比對舊值。
        Applied?.Invoke(this, EventArgs.Empty);

        // 清單頁開著的話,詳細面板要當場跟著變 —— 見 IDetailsWidthStore 上的說明。
        if (widthChanged)
        {
            DetailsWidthChanged?.Invoke(this, EventArgs.Empty);
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

    /// <summary>
    /// 詳細窗格的寬度。清單頁按 Ctrl+D 走的是這條。
    ///
    /// 這裡刻意**不**發 <see cref="Applied"/>:那個事件的意思是「使用者在設定頁送出了表單」,
    /// 發它等於謊報,而且會讓 provider 白跑一次資料夾比對。
    /// 只發 <see cref="DetailsWidthChanged"/> —— 設定頁靠它把下拉選單更新成新值,
    /// 否則按完 Ctrl+D 再打開設定頁,看到的還是舊的那一檔。
    /// </summary>
    public ContentSize DetailsWidth
    {
        get => _detailsWidth.Value switch
        {
            MediumWidth => ContentSize.Medium,
            WideWidth => ContentSize.Large,
            _ => ContentSize.Small,
        };

        set
        {
            _detailsWidth.Value = value switch
            {
                ContentSize.Medium => MediumWidth,
                ContentSize.Large => WideWidth,
                _ => NarrowWidth,
            };

            Save("DetailsWidth");

            DiagnosticLog.Write($"DetailsWidth setter: 改成 {_detailsWidth.Value},發出 DetailsWidthChanged");
            DetailsWidthChanged?.Invoke(this, EventArgs.Empty);
        }
    }

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
