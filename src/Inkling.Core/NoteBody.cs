namespace Inkling.Core;

/// <summary>
/// 「內文的第一行有效文字」的唯一實作。
///
/// 這個概念有三個消費者:<see cref="Note.Summary"/>(清單副標)、
/// <see cref="FileSystemNoteRepository"/> 為外來檔案推導標題、<see cref="NotePreview"/>
/// 判斷內文是否已含標題。曾經各寫一份,字元集與截斷策略已經開始漂移(例如 '#' 之外
/// 要不要也去掉 '>'),而且三份都不認得程式碼圍欄 —— 以 ``` 開頭的筆記,
/// 標題與副標直接顯示成三個反引號。抽在這裡,改規則只動一處。
/// </summary>
internal static class NoteBody
{
    /// <summary>摘要與推導標題共用的字元上限。</summary>
    internal const int MaxLineLength = 120;

    /// <summary>
    /// 逐行回傳內文裡的有效文字:去掉 Markdown 的裝飾前綴(#、&gt;、清單記號),
    /// 跳過空行、程式碼圍欄行(``` 與 ~~~)、水平線與表格分隔列。
    /// 圍欄**內**的行算內容 —— 以程式碼片段開頭的筆記,圍欄裡的第一行才是摘要。
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

            var content = trimmed.TrimStart('#', '>', '-', '*', ' ').Trim();
            if (content.Length > 0)
            {
                yield return content;
            }
        }
    }

    internal static string? FirstContentLine(string body) => ContentLines(body).FirstOrDefault();

    /// <summary>已去除行首空白的圍欄行判斷(``` 或 ~~~ 開頭)。</summary>
    internal static bool IsFenceDelimiter(string trimmedLine) =>
        trimmedLine.StartsWith("```", StringComparison.Ordinal)
        || trimmedLine.StartsWith("~~~", StringComparison.Ordinal);

    /// <summary>水平線與 setext 底線:整行都是同一種標點字元。</summary>
    private static bool IsHorizontalRule(string trimmedLine) =>
        trimmedLine.All(c => c is '-' or '*' or '_' or '=');

    /// <summary>表格分隔列,例如 <c>|---|:---:|</c>。表頭列仍是內容,不濾。</summary>
    private static bool IsTableSeparator(string trimmedLine) =>
        trimmedLine.StartsWith('|')
        && trimmedLine.All(c => c is '|' or '-' or ':' or ' ');
}
