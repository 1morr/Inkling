namespace Inkling.Core;

/// <summary>
/// 「內文的第一行有效文字」的唯一實作。
///
/// 這個概念有三個消費者:<see cref="Note.Summary"/>(清單副標)、
/// <see cref="FileSystemNoteRepository"/> 為外來檔案推導標題、<see cref="NotePreview"/>
/// 判斷內文是否已含標題。曾經各寫一份，字元集與截斷策略已經開始漂移(例如 '#' 之外
/// 要不要也去掉 '>')，而且三份都不認得程式碼圍欄 —— 以 ``` 開頭的筆記，
/// 標題與副標直接顯示成三個反引號。抽在這裡，改規則只動一處。
/// </summary>
internal static class NoteBody
{
    /// <summary>摘要與推導標題共用的字元上限。</summary>
    internal const int MaxLineLength = 120;

    /// <summary>
    /// 摘要與推導標題共用的截斷。
    ///
    /// **不能裸切。** 上限算的是 UTF-16 字元數，而第 <see cref="MaxLineLength"/> 個位置
    /// 正好落在代理對中間時(emoji、擴充區漢字 —— 人名用字與異體字大量落在那裡),
    /// 尾端會留下一個落單的 high surrogate，畫面上就是一個 �。
    /// 檔名那條路早就有這個保護(<see cref="NoteFileName.Slug"/>，而且有測試釘著),
    /// 摘要這條漏了 —— 同一個 bug 的兩份實作，只修過一份。
    /// </summary>
    internal static string Truncate(string line)
    {
        if (line.Length <= MaxLineLength)
        {
            return line;
        }

        var cut = MaxLineLength;
        if (char.IsHighSurrogate(line[cut - 1]))
        {
            cut--;
        }

        return line[..cut];
    }

    /// <summary>
    /// 逐行回傳內文裡的有效文字:去掉一行**外圍**的 Markdown 裝飾
    /// (#、&gt;、清單記號、成對的強調記號)，跳過空行、程式碼圍欄行(``` 與 ~~~)、
    /// 水平線與表格分隔列。
    /// 圍欄**內**的行算內容 —— 以程式碼片段開頭的筆記，圍欄裡的第一行才是摘要。
    /// </summary>
    internal static IEnumerable<string> ContentLines(string body)
    {
        var insideFence = false;

        foreach (var rawLine in body.Split('\n'))
        {
            var trimmed = rawLine.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (IsFenceDelimiter(trimmed))
            {
                insideFence = !insideFence;
                continue;
            }

            if (!insideFence && (IsHorizontalRule(trimmed) || IsTableSeparator(trimmed)))
            {
                continue;
            }

            var content = StripDecoration(trimmed);
            if (content.Length > 0)
            {
                yield return content;
            }
        }
    }

    internal static string? FirstContentLine(string body) => ContentLines(body).FirstOrDefault();

    /// <summary>
    /// 剝掉一行外圍的 Markdown 裝飾。
    ///
    /// **兩邊都要剝。** 以前只剝前綴(一組字元集 <c>TrimStart</c>)，於是
    /// <c>**賣點**</c> 的摘要顯示成 <c>賣點**</c> —— 前面的星號被吃掉、後面的原樣留著，
    /// 比兩邊都留還難看;<c>## 標題 ##</c> 也留著結尾那兩個井號。
    ///
    /// 剝的規則刻意保守，寧可少剝:
    ///
    /// <list type="bullet">
    /// <item>清單記號 <c>- * +</c> **後面要有空白**才算 —— 否則 <c>*強調*</c>
    /// 會被當成清單項吃掉半邊，那正是舊版的字元集做的事。</item>
    /// <item>ATX 標題最多六個 <c>#</c> 而且後面要有空白，所以 <c>#標籤</c> 留著。
    /// 收尾的井號前面也要有空白，所以「談談 C#」不會被剝成「談談 C」。</item>
    /// <item>強調記號只在**整行剛好被同一組記號包住**時才剝:
    /// <c>**甲** 與 **乙**</c> 的內側還有 <c>**</c>，那不是包住整行，原樣留著。</item>
    /// </list>
    ///
    /// 一行可能疊好幾層(<c>&gt; - **重點**</c>)，所以剝到不能再剝為止。
    /// </summary>
    private static string StripDecoration(string trimmedLine)
    {
        var line = trimmedLine;
        string previous;

        do
        {
            previous = line;
            line = StripBlockPrefix(line).Trim();
            line = StripWrappingEmphasis(line).Trim();
        }
        while (!string.Equals(line, previous, StringComparison.Ordinal));

        return line;
    }

    /// <summary>引用、ATX 標題、清單記號 —— 一次剝一層。</summary>
    private static string StripBlockPrefix(string line)
    {
        if (line.StartsWith('>'))
        {
            return line[1..];
        }

        if (line.StartsWith('#'))
        {
            var hashes = 0;
            while (hashes < line.Length && line[hashes] == '#')
            {
                hashes++;
            }

            if (hashes <= 6 && (hashes == line.Length || line[hashes] is ' ' or '\t'))
            {
                return StripAtxClosing(line[hashes..].Trim());
            }
        }

        if (line.Length >= 2 && line[0] is '-' or '*' or '+' && line[1] is ' ' or '\t')
        {
            return line[2..];
        }

        return line;
    }

    /// <summary><c>## 標題 ##</c> 的收尾井號。前面沒有空白就不是收尾(「談談 C#」)。</summary>
    private static string StripAtxClosing(string line)
    {
        var end = line.Length;
        while (end > 0 && line[end - 1] == '#')
        {
            end--;
        }

        return end != line.Length && end > 0 && line[end - 1] is ' ' or '\t'
            ? line[..end]
            : line;
    }

    /// <summary>成對的強調 / 行內程式碼記號，長的先試(<c>**</c> 要贏過 <c>*</c>)。</summary>
    private static readonly string[] WrappingMarkers = ["***", "___", "**", "__", "~~", "*", "_", "`"];

    /// <summary>整行剛好被同一組記號包住時，把那組記號剝掉。</summary>
    private static string StripWrappingEmphasis(string line)
    {
        foreach (var marker in WrappingMarkers)
        {
            if (line.Length <= marker.Length * 2
                || !line.StartsWith(marker, StringComparison.Ordinal)
                || !line.EndsWith(marker, StringComparison.Ordinal))
            {
                continue;
            }

            var inner = line[marker.Length..^marker.Length];

            // 內側還有同一組記號，代表它包的不是整行(`**甲** 與 **乙**`)—— 不動它，
            // 剝了只會變成 `甲** 與 **乙`，比原樣更糟。
            if (!inner.Contains(marker, StringComparison.Ordinal))
            {
                return inner;
            }
        }

        return line;
    }

    /// <summary>已去除行首空白的圍欄行判斷(``` 或 ~~~ 開頭)。</summary>
    internal static bool IsFenceDelimiter(string trimmedLine) =>
        trimmedLine.StartsWith("```", StringComparison.Ordinal)
        || trimmedLine.StartsWith("~~~", StringComparison.Ordinal);

    /// <summary>水平線與 setext 底線:整行都是同一種標點字元。</summary>
    private static bool IsHorizontalRule(string trimmedLine) =>
        trimmedLine.All(c => c is '-' or '*' or '_' or '=');

    /// <summary>表格分隔列，例如 <c>|---|:---:|</c>。表頭列仍是內容，不濾。</summary>
    private static bool IsTableSeparator(string trimmedLine) =>
        trimmedLine.StartsWith('|')
        && trimmedLine.All(c => c is '|' or '-' or ':' or ' ');
}
