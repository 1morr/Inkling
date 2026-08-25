using System.Text;

namespace Inkling.Core;

/// <summary>
/// 把一則筆記變成要交給 Command Palette 渲染的 Markdown 字串。
///
/// 放在 Core 而不是 UI 層:這裡全是純字串處理，而且「哪些行可以動、哪些不能動」
/// 的規則細節不少，必須有測試釘住。
/// </summary>
public static class NotePreview
{
    /// <summary>Markdown 的硬換行寫法:行尾兩個空白。</summary>
    private const string HardBreak = "  ";

    /// <summary>
    /// CommonMark 允許用反斜線逃脫的字元，就是這些 ASCII 標點，一個不多一個不少。
    /// 全部逃脫掉，整行就只剩字面意義。
    /// </summary>
    private const string Escapable = "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~";

    public static string Render(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);

        var body = PreserveLineBreaks(note.Body);

        // 標題存在 front matter 裡，內文通常不含標題，所以預覽時要自己補上 H1。
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
    /// 把內文逐字逃脫，讓每個 Markdown 符號都以字面顯示出來。
    ///
    /// 用途是「我要看到檔案裡真正的那幾個字」:標題的 <c>#</c>、粗體的 <c>**</c>、
    /// 連結的 <c>[](…)</c> 渲染完就消失了，但要複製走的往往正是這些符號本身。
    ///
    /// 逃脫不影響複製:反斜線只存在於送給渲染器的字串裡，畫面上顯示的、
    /// 使用者選取複製走的，都是還原後的原文。
    ///
    /// 為什麼不包成程式碼區塊(那樣連空白都能一字不差):CmdPal 會替程式碼區塊
    /// 畫上外框與底色，樣式寫在它自己的資源裡，擴展改不動，在窄窄的詳細窗格裡很搶版面。
    ///
    /// 代價是行首縮排與連續空行會被渲染器正規化 —— 這是刻意接受的取捨。
    /// </summary>
    public static string RenderSource(string body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return string.Empty;
        }

        var lines = body.Split('\n');
        var result = new StringBuilder(body.Length * 2);

        for (var i = 0; i < lines.Length; i++)
        {
            // 行首空白一定要去掉:段落開頭的四個空白在 CommonMark 裡就是縮排程式碼區塊，
            // 那正好把我們想避開的外框畫回來。段落中間的行首空白反正也會被解析器吃掉，
            // 統一去掉至少行為一致。
            var line = lines[i].Trim();

            if (line.Length > 0)
            {
                Escape(line, result);

                // 下一行還有字才需要硬換行;下一行是空的話，空行本身就是段落分隔。
                if (i + 1 < lines.Length && lines[i + 1].Trim().Length > 0)
                {
                    result.Append(HardBreak);
                }
            }

            if (i + 1 < lines.Length)
            {
                result.Append('\n');
            }
        }

        return result.ToString();
    }

    private static void Escape(string line, StringBuilder builder)
    {
        foreach (var c in line)
        {
            if (Escapable.Contains(c, StringComparison.Ordinal))
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }
    }

    /// <summary>
    /// 把單一換行變成 Markdown 的硬換行。
    ///
    /// 為什麼要這樣做:標準 Markdown 裡單一換行等於空格，所以打三行會顯示成一行。
    /// 對一個隨手記想法的工具來說那不是使用者要的。
    ///
    /// 為什麼不在存檔時就轉:那會動到使用者的檔案內容，而且行尾空白很多編輯器會自動清掉。
    /// 這裡只動要拿去渲染的那份字串，磁碟上的 .md 一個字都不變。
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
        // 最後一行後面沒東西要接，不需要換行標記。
        if (index >= lines.Length - 1)
        {
            return false;
        }

        var line = lines[index];
        var next = lines[index + 1];

        // 空行本身就是段落分隔，兩邊都不用處理。
        if (string.IsNullOrWhiteSpace(line) || string.IsNullOrWhiteSpace(next))
        {
            return false;
        }

        // 已經是硬換行了(行尾兩個空白，或反斜線)。
        if (line.EndsWith(HardBreak, StringComparison.Ordinal) || line.EndsWith('\\'))
        {
            return false;
        }

        // 縮排程式碼區塊:四個空白或一個 tab 起頭。
        if (line.StartsWith("    ", StringComparison.Ordinal) || line.StartsWith('\t'))
        {
            return false;
        }

        // 表格自己管每一列，不要插手。
        if (IsTableRow(line) || IsTableRow(next))
        {
            return false;
        }

        // setext 標題(下一行是一整排 = 或 -)，動了上面那行就不成立了。
        if (IsSetextUnderline(next))
        {
            return false;
        }

        return true;
    }

    private static bool IsFenceDelimiter(string line) => NoteBody.IsFenceDelimiter(line.TrimStart());

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
        var first = NoteBody.FirstContentLine(body);

        if (first is null)
        {
            return false;
        }

        // 外來檔案的標題推導有 120 字截斷，完整與截斷後兩種長度都要比。
        var truncated = first.Length > NoteBody.MaxLineLength ? first[..NoteBody.MaxLineLength] : first;
        return string.Equals(first, title, StringComparison.Ordinal)
            || string.Equals(truncated, title, StringComparison.Ordinal);
    }
}
