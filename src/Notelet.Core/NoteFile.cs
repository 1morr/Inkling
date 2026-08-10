using System.Globalization;
using System.Text;

namespace Notelet.Core;

/// <summary>
/// 從 Markdown 檔內容裡解析出來的原始資料。欄位是可空的 —— 檔案不見得是 Notelet 寫的,
/// 也可能是使用者自己丟進資料夾的普通 .md。缺的欄位由 <see cref="FileSystemNoteRepository"/>
/// 用檔案本身的資訊補齊。
/// </summary>
public sealed record ParsedNoteFile
{
    public string? Id { get; init; }

    public string? Title { get; init; }

    public DateTimeOffset? Created { get; init; }

    public DateTimeOffset? Updated { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = [];

    public IReadOnlyList<string> ExtraFrontMatter { get; init; } = [];

    public string Body { get; init; } = string.Empty;

    /// <summary>檔案裡到底有沒有 front matter 區塊。</summary>
    public bool HadFrontMatter { get; init; }
}

/// <summary>
/// YAML front matter 的讀寫。
///
/// 這裡刻意手寫而不是用 YamlDotNet:擴展的 Release build 開了 trimming 與 AOT,
/// 反射式的序列化器在那個組態下會出問題。而我們實際需要的只是純量與字串陣列,
/// 手寫的成本遠低於維護 trimming 白名單。
///
/// 讀取時盡量寬容(別人的編輯器寫什麼都不該讓 Notelet 壞掉),
/// 寫入時只產出一種固定樣式。
/// </summary>
public static class NoteFile
{
    private const string Delimiter = "---";
    private const string DateFormat = "yyyy-MM-ddTHH:mm:sszzz";
    private const char ByteOrderMark = (char)0xFEFF;

    public static ParsedNoteFile Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        // File.ReadAllText 通常已經吃掉 BOM,但 Parse 也可能拿到別處來的字串。
        if (content.Length > 0 && content[0] == ByteOrderMark)
        {
            content = content[1..];
        }

        var lines = content.Split('\n');

        if (lines.Length == 0 || lines[0].TrimEnd('\r').Trim() != Delimiter)
        {
            // 沒有 front matter —— 整個檔案都是內文。這是使用者用別的工具
            // 丟進資料夾的普通 Markdown,照樣要能顯示。
            return new ParsedNoteFile { Body = StripTrailingNewline(NormalizeNewlines(content)), HadFrontMatter = false };
        }

        var closingIndex = -1;
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].TrimEnd('\r').Trim() == Delimiter)
            {
                closingIndex = i;
                break;
            }
        }

        if (closingIndex < 0)
        {
            // 開頭有 "---" 卻沒有收尾。當成沒有 front matter,總比把整個檔案吞掉好。
            return new ParsedNoteFile { Body = StripTrailingNewline(NormalizeNewlines(content)), HadFrontMatter = false };
        }

        var block = lines[1..closingIndex].Select(l => l.TrimEnd('\r')).ToArray();
        var body = string.Join('\n', lines[(closingIndex + 1)..].Select(l => l.TrimEnd('\r')));

        // 收尾分隔線後習慣空一行,把它吃掉,免得每次 round-trip 都多長一行。
        if (body.StartsWith('\n'))
        {
            body = body[1..];
        }

        return ParseBlock(block, StripTrailingNewline(body));
    }

    private static ParsedNoteFile ParseBlock(string[] block, string body)
    {
        string? id = null;
        string? title = null;
        DateTimeOffset? created = null;
        DateTimeOffset? updated = null;
        List<string> tags = [];
        List<string> extra = [];

        // 邊掃邊記住目前所屬的 key,接續行(縮排行、"- " 清單項)才能歸到正確的欄位。
        // 不認得的 key 要連同它的接續行整段原樣留著,否則巢狀結構會被拆爛。
        var currentKey = string.Empty;

        foreach (var line in block)
        {
            var isContinuation = line.Length > 0
                && (char.IsWhiteSpace(line[0]) || line.StartsWith("- ", StringComparison.Ordinal));

            if (isContinuation)
            {
                if (currentKey == "tags")
                {
                    var item = line.TrimStart();
                    if (item.StartsWith("- ", StringComparison.Ordinal))
                    {
                        var tag = Unquote(item[2..].Trim());
                        if (tag.Length > 0)
                        {
                            tags.Add(tag);
                        }
                    }
                }
                else if (currentKey.Length > 0)
                {
                    // 屬於某個不認得的 key,原樣保留。
                    extra.Add(line);
                }

                continue;
            }

            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
            {
                // 不是 "key: value" 的形狀(註解、殘缺行)。原樣留著。
                if (line.Trim().Length > 0)
                {
                    extra.Add(line);
                }

                currentKey = string.Empty;
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            currentKey = key.ToLowerInvariant();

            switch (currentKey)
            {
                case "id":
                    id = Unquote(value);
                    break;
                case "title":
                    title = Unquote(value);
                    break;
                case "created":
                    created = ParseDate(value);
                    break;
                case "updated":
                    updated = ParseDate(value);
                    break;
                case "tags":
                    tags = ParseInlineTags(value);
                    break;
                default:
                    extra.Add(line);
                    break;
            }
        }

        return new ParsedNoteFile
        {
            Id = string.IsNullOrWhiteSpace(id) ? null : id,
            Title = string.IsNullOrWhiteSpace(title) ? null : title,
            Created = created,
            Updated = updated,
            Tags = tags,
            ExtraFrontMatter = extra,
            Body = body,
            HadFrontMatter = true,
        };
    }

    private static List<string> ParseInlineTags(string value)
    {
        // 空值代表是區塊式清單,標籤在後面的接續行裡。
        if (value.Length == 0)
        {
            return [];
        }

        var inner = value.StartsWith('[') && value.EndsWith(']')
            ? value[1..^1]
            : value;

        return [.. inner
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Unquote)
            .Where(t => t.Length > 0)];
    }

    private static DateTimeOffset? ParseDate(string value)
    {
        var unquoted = Unquote(value);
        return DateTimeOffset.TryParse(unquoted, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2
            && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            var inner = value[1..^1];
            return value[0] == '"'
                ? inner.Replace("\\\"", "\"", StringComparison.Ordinal)
                       .Replace("\\\\", "\\", StringComparison.Ordinal)
                : inner.Replace("''", "'", StringComparison.Ordinal);
        }

        return value;
    }

    public static string Serialize(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);

        var builder = new StringBuilder();
        builder.Append(Delimiter).Append('\n');
        builder.Append("id: ").Append(Quote(note.Id)).Append('\n');
        builder.Append("title: ").Append(Quote(note.Title)).Append('\n');
        builder.Append("created: ").Append(note.Created.ToString(DateFormat, CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("updated: ").Append(note.Updated.ToString(DateFormat, CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("tags: [").AppendJoin(", ", note.Tags.Select(Quote)).Append("]\n");

        // 別人加的欄位原樣寫回。
        foreach (var line in note.ExtraFrontMatter)
        {
            builder.Append(line).Append('\n');
        }

        builder.Append(Delimiter).Append('\n');
        builder.Append('\n');
        builder.Append(NormalizeNewlines(note.Body));

        var text = builder.ToString();

        // 檔尾固定一個換行,讓 diff 乾淨。
        if (!text.EndsWith('\n'))
        {
            text += "\n";
        }

        // Windows 上的編輯器對 CRLF 比較友善,統一輸出 CRLF。
        return text.Replace("\n", "\r\n", StringComparison.Ordinal);
    }

    /// <summary>
    /// 檔尾那個換行是格式,不是內容。Serialize 一定會補上它,所以 Parse 一定要拿掉,
    /// 否則 note.Body 跟磁碟上的內容永遠對不起來,每編輯一次就多一個換行。
    /// </summary>
    private static string StripTrailingNewline(string text) =>
        text.EndsWith('\n') ? text[..^1] : text;

    private static string NormalizeNewlines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

    /// <summary>
    /// 只在不加引號會讓 YAML 解讀錯誤時才加,不然人在編輯器裡看到滿滿的引號很煩。
    /// </summary>
    private static string Quote(string value)
    {
        if (value.Length == 0)
        {
            return "\"\"";
        }

        var needsQuoting =
            char.IsWhiteSpace(value[0])
            || char.IsWhiteSpace(value[^1])
            || value.Contains(": ", StringComparison.Ordinal)
            || value.EndsWith(':')
            || value.Contains(" #", StringComparison.Ordinal)
            || value.Contains('\n')
            || "-?:,[]{}#&*!|>'\"%@`".Contains(value[0])
            || IsYamlKeyword(value);

        if (!needsQuoting)
        {
            return value;
        }

        var escaped = value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

        return $"\"{escaped}\"";
    }

    /// <summary>不加引號會被 YAML 當成布林/空值/數字的字串。</summary>
    private static bool IsYamlKeyword(string value) =>
        value is "true" or "false" or "null" or "yes" or "no" or "on" or "off" or "~"
        || double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out _);
}
