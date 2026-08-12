namespace Notelet.Core;

/// <summary>
/// Notelet 的執行期設定。UI 層負責從擴展設定讀出值後建構這個物件,
/// Core 層本身不知道設定存在哪裡。
/// </summary>
public sealed class NoteletOptions
{
    /// <summary>存放筆記 Markdown 檔的資料夾。</summary>
    public required string NotesDirectory { get; init; }

    /// <summary>
    /// 清單一次最多送幾則給 Command Palette。
    ///
    /// 每個項目都要跨進程 COM 封送,而清單是每按一個鍵就重建一次的。筆記累積到幾千則時,
    /// 無上限地全部送過去就會拖慢整個 Command Palette —— 那正是需求裡明確禁止的事。
    /// 被截掉時清單最後會明講還有幾則,不會默默少東西。
    /// </summary>
    public int MaxResults { get; init; } = 200;

    /// <summary>
    /// 預設把筆記放在 OneDrive 底下,同步完全交給 OneDrive 客戶端處理。
    /// 沒有 OneDrive 時退回使用者的 Documents。
    /// </summary>
    public static string DefaultNotesDirectory()
    {
        var oneDrive = Environment.GetEnvironmentVariable("OneDrive");

        var root = !string.IsNullOrWhiteSpace(oneDrive) && Directory.Exists(oneDrive)
            ? oneDrive
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        return Path.Combine(root, "Notelet");
    }
}
