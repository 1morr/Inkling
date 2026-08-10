using Microsoft.CommandPalette.Extensions.Toolkit;
using Notelet.Core;

namespace Notelet;

/// <summary>
/// Notelet 的擴展設定,存在 CmdPal 為每個擴展準備的 settings.json 裡。
/// 這一層負責把使用者設定翻譯成 <see cref="NoteletOptions"/>,Core 層完全不知道設定存在哪。
/// </summary>
internal sealed partial class SettingsManager : JsonSettingsManager
{
    private const string SettingsNamespace = "Notelet";

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
        "打了這個前綴才會出現快速新增,例如「n 買咖啡機」。以字母或數字結尾時會自動補一個空白。",
        "n ");

    public SettingsManager()
    {
        FilePath = SettingsJsonPath();

        Settings.Add(_notesDirectory);
        Settings.Add(_quickCaptureEnabled);
        Settings.Add(_quickCapturePrefix);

        LoadSettings();

        Settings.SettingsChanged += (_, _) => SaveSettings();
    }

    public string NotesDirectory => _notesDirectory.Value ?? NoteletOptions.DefaultNotesDirectory();

    public bool QuickCaptureEnabled => _quickCaptureEnabled.Value;

    public string QuickCapturePrefix => _quickCapturePrefix.Value ?? "n ";

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
