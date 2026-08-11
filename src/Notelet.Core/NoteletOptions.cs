namespace Notelet.Core;

/// <summary>
/// Notelet 的執行期設定。UI 層負責從擴展設定讀出值後建構這個物件,
/// Core 層本身不知道設定存在哪裡。
/// </summary>
public sealed class NoteletOptions
{
    /// <summary>存放筆記 Markdown 檔的資料夾。</summary>
    public required string NotesDirectory { get; init; }

    private readonly string _quickCapturePrefix = "n ";

    /// <summary>
    /// 觸發快速新增的前綴,例如 "n "。設定值會經過正規化,見 <see cref="NormalizePrefix"/>。
    /// </summary>
    public string QuickCapturePrefix
    {
        get => _quickCapturePrefix;
        init => _quickCapturePrefix = NormalizePrefix(value);
    }

    /// <summary>
    /// 主搜尋框的快速新增(fallback)是否啟用。預設關閉。
    ///
    /// 關的理由不是它不好用,而是它在目前的 CmdPal 上藏不乾淨:沒命中前綴時我們只能
    /// 把標題設成空字串,而 0.11.11762.0 在「Include in the Global result」那條路上
    /// 沒有把空標題的項目濾掉,結果每一次搜索都多出一個點不動的空列。
    /// 快速記下頁不受這個開關影響 —— 那一頁是使用者自己叫出來的。
    /// </summary>
    public bool QuickCaptureEnabled { get; init; }

    /// <summary>
    /// 前綴以字母或數字結尾時補一個空白。
    ///
    /// 不補的話,前綴 "n" 會讓「note 開頭的任何一句話」都被當成快速新增,
    /// 而且會把第一個字母吃掉("note about x" 變成記下 "ote about x")。
    /// 符號結尾的前綴(例如 ",")則維持原樣,那種寫法本來就不需要空白。
    /// </summary>
    public static string NormalizePrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return string.Empty;
        }

        var trimmed = prefix.TrimEnd();

        return char.IsLetterOrDigit(trimmed[^1]) ? trimmed + " " : trimmed;
    }

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

    public static NoteletOptions Default() => new() { NotesDirectory = DefaultNotesDirectory() };
}
