namespace Inkling.Core;

/// <summary>
/// Inkling 的執行期設定。UI 層負責從擴展設定讀出值後建構這個物件,
/// Core 層本身不知道設定存在哪裡。
/// </summary>
public sealed class InklingOptions
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

    /// <summary>資料夾名。身分的一部分,換掉等於讓使用者的舊筆記從清單上消失。</summary>
    private const string FolderName = "Inkling";

    /// <summary>
    /// 預設把筆記放在 OneDrive 底下,同步完全交給 OneDrive 客戶端處理。
    /// 沒有 OneDrive 時退回使用者的 Documents。
    /// </summary>
    public static string DefaultNotesDirectory()
    {
        var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
        var hasOneDrive = !string.IsNullOrWhiteSpace(oneDrive) && Directory.Exists(oneDrive);

        return DefaultNotesDirectoryUnder(
            hasOneDrive ? oneDrive : null,
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
    }

    /// <summary>
    /// 上面那個決策本身,把「這台機器長什麼樣」當參數傳進來。
    ///
    /// **分出來是為了驗得到。** 無參數那個版本問的是這台機器的環境變數,
    /// 於是「有 OneDrive 就用它」那條分支在 CI 上永遠走不到 —— windows-latest 沒有
    /// OneDrive,測試看起來在驗兩條路,實際上只碰得到 fallback 那一條。
    /// </summary>
    /// <param name="oneDriveRoot">OneDrive 的根目錄;沒有(或那個路徑不存在)就傳 null。</param>
    /// <param name="documentsRoot">沒有 OneDrive 時的退路。</param>
    public static string DefaultNotesDirectoryUnder(string? oneDriveRoot, string documentsRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentsRoot);

        var root = string.IsNullOrWhiteSpace(oneDriveRoot) ? documentsRoot : oneDriveRoot;

        return Path.Combine(root, FolderName);
    }
}
