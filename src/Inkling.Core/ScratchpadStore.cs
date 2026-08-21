namespace Inkling.Core;

/// <summary>
/// 隨手草稿的內容:筆記資料夾根目錄底下一個固定檔名的純文字檔。
///
/// <para><b>為什麼不是一則筆記。</b></para>
///
/// 草稿沒有標題、沒有 id,而且會被反覆整段覆寫 —— 那三件事正好是 <see cref="Note"/>
/// 的全部意義。硬把它塞成筆記的話,清單裡會永遠多一列標題在跳動的東西,搜索結果也會
/// 一直撈到半成品。所以它就是一個檔案,<b>而且刻意不寫 front matter</b>:
/// 使用者按 <c>Ctrl+O</c> 用外部編輯器打開時,看到的應該是自己寫的字,不是一段中繼資料。
///
/// <para><b>放在筆記資料夾裡,而不是擴展自己的 LocalState。</b></para>
///
/// 那個資料夾多半掛在 OneDrive 之類的同步碟上,草稿因此跟著換機器走,也能被別的編輯器
/// 直接打開。代價是它會落在 <see cref="FileSystemNoteRepository"/> 的掃描範圍裡,
/// 所以那一邊要認得它並跳過 —— 見 <see cref="IsScratchpad"/>。
/// </summary>
public sealed class ScratchpadStore
{
    /// <summary>
    /// 草稿檔的檔名。<b>這是對使用者的承諾</b>:改了等於讓所有人的草稿憑空消失
    /// (舊檔還在磁碟上,但誰都不會再打開它)。
    /// </summary>
    public const string FileName = "scratchpad.md";

    private readonly string _directory;

    public ScratchpadStore(InklingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _directory = options.NotesDirectory;
        FilePath = Path.Combine(_directory, FileName);
    }

    /// <summary>草稿檔的完整路徑。檔案不一定存在 —— 要保證存在請先叫 <see cref="EnsureFile"/>。</summary>
    public string FilePath { get; }

    /// <summary>
    /// 讀出草稿內容,換行一律折成 LF(記憶體裡的約定,跟 <see cref="NoteFile"/> 一致)。
    /// 檔尾那個換行是格式不是內容,跟 <see cref="Write"/> 對稱地拿掉 ——
    /// 不然每存一次就多長一行。
    ///
    /// <b>讀不到一律回空字串,不丟例外</b> —— 沒寫過、資料夾被搬走、OneDrive 正好鎖著檔案,
    /// 對使用者來說都只是「隨手草稿現在是空的」,而讓例外從 <c>GetContent()</c> 穿出去
    /// 會把整頁變成擴展錯誤。
    /// </summary>
    public string Read()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return string.Empty;
            }

            var text = Newlines.ToLf(File.ReadAllText(FilePath));

            return text.EndsWith('\n') ? text[..^1] : text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 整份覆寫草稿。資料夾不存在就先建出來(跟第一次存筆記一樣的行為)。
    /// 失敗時例外往外丟 —— 存檔失敗必須讓使用者看見,不然他會以為東西存起來了然後把視窗關掉。
    ///
    /// <b>換行一定要正規化過再落地。</b>Adaptive Cards 的多行輸入框送回來的換行是
    /// <b>裸 CR</b>(底下那個 WinUI <c>TextBox</c> 的行為),原樣寫進檔案的話,
    /// 使用者按 <c>Ctrl+O</c> 用外部編輯器打開會看到擠成一行的一大塊字 ——
    /// 而「跳到外部編輯器」正是這個功能拿來替代自動儲存的那條路,不能壞。
    /// 筆記走 <see cref="NoteFile.Serialize"/> 時做的是同一件事。
    /// </summary>
    public void Write(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lf = Newlines.ToLf(text);

        // 檔尾固定一個換行(空草稿除外),讓 diff 與別的編輯器都乾淨。
        if (lf.Length > 0 && !lf.EndsWith('\n'))
        {
            lf += "\n";
        }

        AtomicFile.Write(FilePath, Newlines.ToCrlf(lf));
    }

    /// <summary>
    /// 檔案不存在就建一個空的,<b>已經存在的話絕不覆寫</b>。
    ///
    /// 存在的理由只有一個:「用外部編輯器打開」那個命令拿的是一個固定路徑,
    /// 而使用者可能一個字都還沒存過 —— 沒有這一步,第一次按 <c>Ctrl+O</c> 會開失敗。
    /// </summary>
    public void EnsureFile()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return;
            }

            Directory.CreateDirectory(_directory);
            File.WriteAllText(FilePath, string.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 建不出來就算了:隨手草稿本身照樣打得開(內容是空的),只有 Ctrl+O 會開失敗。
            // 為了這件事讓整頁變成錯誤畫面不划算。
        }
    }

    /// <summary>
    /// 這個路徑是不是草稿檔。<see cref="FileSystemNoteRepository"/> 用它把草稿排除在
    /// 清單與搜索之外。
    ///
    /// <b>只認根目錄的那一個。</b>子資料夾裡剛好也叫 <c>scratchpad.md</c> 的檔案是使用者
    /// 自己的筆記,照常列出來 —— 「Inkling 的隨手草稿」只有一個,而規則講得出口
    /// (「筆記資料夾最上層那個 scratchpad.md」)才不會變成無聲吃掉檔案的黑魔法。
    /// </summary>
    public static bool IsScratchpad(string notesDirectory, string path)
    {
        ArgumentNullException.ThrowIfNull(notesDirectory);
        ArgumentNullException.ThrowIfNull(path);

        string relative;
        try
        {
            relative = Path.GetRelativePath(notesDirectory, path);
        }
        catch (ArgumentException)
        {
            // 路徑裡有非法字元之類的:當它不是草稿檔,讓原本的流程去處理。
            return false;
        }

        // 比對整段相對路徑而不是只比檔名,「有沒有夾在子資料夾裡」才分得出來。
        // Windows 的檔名不分大小寫,所以 OrdinalIgnoreCase。
        return string.Equals(relative, FileName, StringComparison.OrdinalIgnoreCase);
    }
}
