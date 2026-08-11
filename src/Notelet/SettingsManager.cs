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
        "存放 Markdown 檔的資料夾。放在 OneDrive 之類的雲端硬碟底下,多端同步就完全交給它處理,Notelet 本身不做同步。",
        NoteletOptions.DefaultNotesDirectory());

    private readonly ToggleSetting _quickCaptureEnabled = new(
        Namespaced(nameof(QuickCaptureEnabled)),
        "啟用快速新增",
        "在 Command Palette 主搜尋框直接輸入就能記下想法,不必先進 Notelet。",
        true);

    private readonly TextSetting _quickCapturePrefix = new(
        Namespaced(nameof(QuickCapturePrefix)),
        "快速新增前綴",
        "打了這個前綴才會出現快速新增,例如「n 買咖啡機」。以字母或數字結尾時會自動補一個空白。標題後面打分號可以接內文:「n 買咖啡機;比較過幾台」。",
        "n ");

    private readonly ChoiceSetSetting _detailsWidth = new(
        Namespaced(nameof(DetailsWidth)),
        "詳細面板寬度",
        "清單頁右邊那塊佔多寬。清單頁按 Ctrl+D 可以直接循環,選好的檔位會存回這裡。",
        [
            new ChoiceSetSetting.Choice("窄(清單:詳情 = 3:1)", NarrowWidth),
            new ChoiceSetSetting.Choice("中(2:1)", MediumWidth),
            new ChoiceSetSetting.Choice("寬(1:1)", WideWidth),
        ]);

    public SettingsManager()
    {
        FilePath = SettingsJsonPath();

        Settings.Add(_notesDirectory);
        Settings.Add(_quickCaptureEnabled);
        Settings.Add(_quickCapturePrefix);
        Settings.Add(_detailsWidth);

        LoadSettings();

        Settings.SettingsChanged += (_, _) => SaveSettings();
    }

    public string NotesDirectory => _notesDirectory.Value ?? NoteletOptions.DefaultNotesDirectory();

    public bool QuickCaptureEnabled => _quickCaptureEnabled.Value;

    public string QuickCapturePrefix => _quickCapturePrefix.Value ?? "n ";

    /// <summary>
    /// 詳細窗格的寬度。
    ///
    /// 這裡刻意只呼叫 <see cref="JsonSettingsManager.SaveSettings"/>,不發
    /// <c>SettingsChanged</c>:那個事件會讓整個 provider 重建(換掉 repository 與清單頁),
    /// 而按 Ctrl+D 的當下人正看著某一則筆記,清單被翻新一次選中項就跑掉了 ——
    /// 那正好毀掉這個功能存在的意義。設定頁那條路本來就會發事件,不受影響。
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

            SaveSettings();
        }
    }

    public NoteletOptions ToOptions()
    {
        // 使用者可能把資料夾欄位清空。與其讓擴展壞掉,不如退回預設值。
        var directory = string.IsNullOrWhiteSpace(NotesDirectory)
            ? NoteletOptions.DefaultNotesDirectory()
            : NotesDirectory.Trim();

        return new NoteletOptions
        {
            NotesDirectory = directory,
            QuickCaptureEnabled = QuickCaptureEnabled,
            QuickCapturePrefix = QuickCapturePrefix,
        };
    }

    private static string Namespaced(string propertyName) => $"{SettingsNamespace}.{propertyName}";

    private static string SettingsJsonPath()
    {
        var directory = Utilities.BaseSettingsPath(SettingsNamespace);
        Directory.CreateDirectory(directory);

        return Path.Combine(directory, "settings.json");
    }
}
