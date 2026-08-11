namespace Notelet.Core;

/// <summary>
/// 把一則筆記變成要交給 Command Palette 渲染的 Markdown 字串。
///
/// 放在 Core 而不是 UI 層:這裡全是純字串處理,而且「哪些行可以動、哪些不能動」
/// 的規則細節不少,必須有測試釘住。
/// </summary>
public static class NotePreview
{
    /// <summary>Markdown 的硬換行寫法:行尾兩個空白。</summary>
    private const string HardBreak = "  ";

    private const char Backtick = '`';

    /// <summary>圍欄式程式碼區塊的最短長度,由 CommonMark 規定。</summary>
    private const int MinFenceLength = 3;

    public static string Render(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);

        var body = PreserveLineBreaks(note.Body);

        // 標題存在 front matter 裡,內文通常不含標題,所以預覽時要自己補上 H1。
        // 但外來的 Markdown 檔標題本來就是從內文第一個標題推導出來的 ——
        // 那種情況再補一次就會變成重複的標題。
        if (BodyStartsWithTitle(note.Title, note.Body))
        {
            return body;
        }

        return body.Length == 0
            ? $"# {note.Title}"
            : $"# {note.Title}\n\n{body}";
    }

    /// <summary>
    /// 把內文原封不動包成程式碼區塊,讓渲染器一個字都不要動它。
    ///
    /// 用途是「我要看到檔案裡真正的那幾個字」:標題的 <c>#</c>、粗體的 <c>**</c>、
    /// 連結的 <c>[](…)</c> 渲染完就消失了,但要複製走的往往正是這些符號本身。
    /// 包成程式碼區塊是唯一能保證原文一字不差的做法 —— 逐字逃脫也做得到,
    /// 但那樣複製出去的會是加了反斜線的版本,等於沒有解決問題。
    /// </summary>
    public static string RenderSource(string body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return string.Empty;
        }

        // 圍欄要比內文裡最長的一串反引號再多一個。內文本來就含程式碼區塊時,
        // 用固定的三個反引號會被它提前關掉,後半段就漏出去被渲染了。
        var fence = new string(Backtick, Math.Max(MinFenceLength, LongestBacktickRun(body) + 1));

        return $"{fence}\n{body}\n{fence}";
    }

    private static int LongestBacktickRun(string text)
    {
        var longest = 0;
        var current = 0;

        foreach (var c in text)
        {
            if (c != Backtick)
            {
                current = 0;
                continue;
            }

            current++;

            if (current > longest)
            {
                longest = current;
            }
        }

        return longest;
    }

    /// <summary>
    /// 把單一換行變成 Markdown 的硬換行。
    ///
    /// 為什麼要這樣做:標準 Markdown 裡單一換行等於空格,所以打三行會顯示成一行。
    /// 對一個隨手記想法的工具來說那不是使用者要的。
    ///
    /// 為什麼不在存檔時就轉:那會動到使用者的檔案內容,而且行尾空白很多編輯器會自動清掉。
    /// 這裡只動要拿去渲染的那份字串,磁碟上的 .md 一個字都不變。
    ///
    /// 動手時要避開所有「換行本來就有意義」的區塊:程式碼區塊、表格、縮排程式碼、
    /// setext 標題底線。這些地方硬加東西會把版面弄壞。
    /// </summary>
    public static string PreserveLineBreaks(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return markdown;
        }

        var lines = markdown.Split('\n');
        var result = new string[lines.Length];
        var insideFence = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (IsFenceDelimiter(line))
            {
                insideFence = !insideFence;
                result[i] = line;
                continue;
            }

            result[i] = insideFence || !NeedsHardBreak(lines, i)
                ? line
                : line + HardBreak;
        }

        return string.Join('\n', result);
    }

    private static bool NeedsHardBreak(string[] lines, int index)
    {
        // 最後一行後面沒東西要接,不需要換行標記。
        if (index >= lines.Length - 1)
        {
            return false;
        }

        var line = lines[index];
        var next = lines[index + 1];

        // 空行本身就是段落分隔,兩邊都不用處理。
        if (string.IsNullOrWhiteSpace(line) || string.IsNullOrWhiteSpace(next))
        {
            return false;
        }

        // 已經是硬換行了(行尾兩個空白,或反斜線)。
        if (line.EndsWith(HardBreak, StringComparison.Ordinal) || line.EndsWith('\\'))
        {
            return false;
        }

        // 縮排程式碼區塊:四個空白或一個 tab 起頭。
        if (line.StartsWith("    ", StringComparison.Ordinal) || line.StartsWith('\t'))
        {
            return false;
        }

        // 表格自己管每一列,不要插手。
        if (IsTableRow(line) || IsTableRow(next))
        {
            return false;
        }

        // setext 標題(下一行是一整排 = 或 -),動了上面那行就不成立了。
        if (IsSetextUnderline(next))
        {
            return false;
        }

        return true;
    }

    private static bool IsFenceDelimiter(string line)
    {
        var trimmed = line.TrimStart();

        return trimmed.StartsWith("```", StringComparison.Ordinal)
            || trimmed.StartsWith("~~~", StringComparison.Ordinal);
    }

    private static bool IsTableRow(string line) => line.TrimStart().StartsWith('|');

    private static bool IsSetextUnderline(string line)
    {
        var trimmed = line.Trim();

        if (trimmed.Length == 0)
        {
            return false;
        }

        return trimmed.All(c => c == '=') || trimmed.All(c => c == '-');
    }

    private static bool BodyStartsWithTitle(string title, string body)
    {
        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            return string.Equals(trimmed.TrimStart('#').Trim(), title, StringComparison.Ordinal);
        }

        return false;
    }
}
