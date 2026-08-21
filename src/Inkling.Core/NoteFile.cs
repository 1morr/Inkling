using System.Globalization;
using System.Text;

namespace Inkling.Core;

/// <summary>
/// 從 Markdown 檔內容裡解析出來的原始資料。欄位是可空的 —— 檔案不見得是 Inkling 寫的,
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
    /// <summary>
    /// 這個檔案原本有沒有合法的 front matter。生產程式碼不讀它,**但它是單元測試
    /// 唯一看得到的觀察點** —— 「開頭是 `---` 但不是 front matter 就整檔當內文」
    /// 那條規則靠它釘住(見 NoteFileTests)。不要收成 internal,測試專案沒有
    /// InternalsVisibleTo。
    /// </summary>
    public bool HadFrontMatter { get; init; }
}

/// <summary>
/// YAML front matter 的讀寫。
///
/// 這裡刻意手寫而不是用 YamlDotNet:擴展的 Release build 開了 trimming 與 AOT,
/// 反射式的序列化器在那個組態下會出問題。而我們實際需要的只是純量與字串陣列,
/// 手寫的成本遠低於維護 trimming 白名單。
///
/// 讀取時盡量寬容(別人的編輯器寫什麼都不該讓 Inkling 壞掉),
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
            return new ParsedNoteFile { Body = StripTrailingNewline(Newlines.ToLf(content)), HadFrontMatter = false };
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
            return new ParsedNoteFile { Body = StripTrailingNewline(Newlines.ToLf(content)), HadFrontMatter = false };
        }

        var block = lines[1..closingIndex].Select(l => l.TrimEnd('\r')).ToArray();

        // **開頭是 `---` 不代表那是 front matter。** Markdown 的水平線也是 `---`,
        // 而「第一行就是一條線、後面某處還有一條」的文件並不罕見。把那種檔案當成
        // front matter 的後果是兩層的:前半段內容從清單與預覽裡消失,而且使用者在
        // Inkling 裡編輯一次之後,那幾行會被寫進 front matter 區塊 —— 它們沒有冒號,
        // Obsidian / Hugo 從此解析不了這個檔案。這正是「外來 .md 也要能列出來」
        // 那條資料格式承諾要防的事。
        //
        // 判準與「有開頭沒收尾」那條路一樣保守:認不出 key 就整檔當內文。
        // 兩個方向的代價不對稱 —— 認錯成 front matter 會吃掉內容,
        // 認錯成內文只是多顯示兩行 `---`。
        if (!LooksLikeFrontMatter(block))
        {
            return new ParsedNoteFile { Body = StripTrailingNewline(Newlines.ToLf(content)), HadFrontMatter = false };
        }

        var body = string.Join('\n', lines[(closingIndex + 1)..].Select(l => l.TrimEnd('\r')));

        // 收尾分隔線後習慣空一行,把它吃掉,免得每次 round-trip 都多長一行。
        if (body.StartsWith('\n'))
        {
            body = body[1..];
        }

        return ParseBlock(block, StripTrailingNewline(body));
    }

    /// <summary>
    /// 這個區塊看起來是不是 YAML front matter:至少要有一行是 <c>key: value</c>。
    ///
    /// 三道限制都是為了不把內文誤認成 key:冒號後面必須接空白或就是行尾
    /// (YAML 對應的規則,順帶擋掉 <c>https://example.com</c> 這種行)、
    /// key 不能有內部空白(<c>單元二:開場</c> 是句子不是 key)、
    /// key 不能以 <c>#</c> 開頭(<c># 標題: 副標</c> 是 Markdown 標題)。
    /// </summary>
    private static bool LooksLikeFrontMatter(string[] block)
    {
        foreach (var line in block)
        {
            // 空行與接續行不能當判準:它們要靠前面那個 key 才有意義。
            if (line.Length == 0
                || char.IsWhiteSpace(line[0])
                || line.StartsWith("- ", StringComparison.Ordinal))
            {
                continue;
            }

            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            if (separator + 1 < line.Length && line[separator + 1] != ' ')
            {
                continue;
            }

            var key = line[..separator].TrimEnd();
            if (key.Length > 0 && key[0] != '#' && !key.Any(char.IsWhiteSpace))
            {
                return true;
            }
        }

        return false;
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

        // 折疊/字面純量(`title: >`、`title: |`)的暫存。YAML 允許把值寫在後面的縮排行裡,
        // 而我們認得的四個欄位在寫回去時只能是單行純量。不處理的話 title 會變成一個 ">",
        // 續行則掉進 ExtraFrontMatter —— 而 Serialize 把 extra 寫在固定欄位後面,
        // 那幾行縮排就這樣排到 `updated:` 底下,把 updated 變成多行純量、日期壞掉。
        // 所以把續行收起來併成一行還給原本那個欄位:標題救回來了,檔案也還是合法的 YAML。
        string? blockKey = null;
        var blockLines = new List<string>();

        foreach (var line in block)
        {
            var isContinuation = line.Length > 0
                && (char.IsWhiteSpace(line[0]) || line.StartsWith("- ", StringComparison.Ordinal));

            if (isContinuation)
            {
                if (blockKey is not null)
                {
                    blockLines.Add(line.Trim());
                    continue;
                }

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

            // 不是接續行 = 上一個區塊純量到此為止。
            FlushBlockScalar();

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

            // 認得的單行欄位寫成區塊純量時,值在後面的縮排行裡,這一行本身沒有內容。
            if (IsBlockScalarIndicator(value) && currentKey is "id" or "title" or "created" or "updated")
            {
                blockKey = currentKey;
                continue;
            }

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
                    // `tags: >` 沒有意義,但真遇到時不能把它當 inline 清單 ——
                    // 那會產出一個叫 ">" 的標籤。當成空的,讓後面的 "- " 續行接手。
                    tags = IsBlockScalarIndicator(value) ? [] : ParseInlineTags(value);
                    break;
                default:
                    extra.Add(line);
                    break;
            }
        }

        // 區塊純量收在最後一行時,迴圈裡沒有機會沖掉它。
        FlushBlockScalar();

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

        void FlushBlockScalar()
        {
            if (blockKey is null)
            {
                return;
            }

            // 折疊成一行:這四個欄位在 Serialize 那邊本來就只能是單行純量(見 Quote)。
            // `>` 與 `|` 的差別(折疊 vs 保留換行)在這裡沒有意義,不分開處理。
            var value = string.Join(' ', blockLines.Where(l => l.Length > 0));

            switch (blockKey)
            {
                case "id":
                    id = value;
                    break;
                case "title":
                    title = value;
                    break;
                case "created":
                    created = ParseDate(value);
                    break;
                case "updated":
                    updated = ParseDate(value);
                    break;
            }

            blockKey = null;
            blockLines.Clear();
        }
    }

    /// <summary>
    /// 值是不是 YAML 的區塊純量標記:<c>&gt;</c> 或 <c>|</c>,後面可以接 chomping
    /// 的 <c>-</c> / <c>+</c> 與明確縮排的數字(<c>|2-</c>),再後面只能是註解。
    /// </summary>
    private static bool IsBlockScalarIndicator(string value)
    {
        if (value.Length == 0 || (value[0] != '>' && value[0] != '|'))
        {
            return false;
        }

        var rest = value[1..];
        var comment = rest.IndexOf('#', StringComparison.Ordinal);
        if (comment >= 0)
        {
            rest = rest[..comment];
        }

        return rest.Trim().All(c => c is '-' or '+' || char.IsAsciiDigit(c));
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

        // 引號感知的手動掃描:在雙/單引號內不切逗號,否則 Obsidian / Hugo 寫的
        // ["a, b", "c"] 會裂成帶殘引號的碎片。引號原樣留在 token 裡,由 Unquote 解逸出。
        var tags = new List<string>();
        var current = new StringBuilder();
        var inDouble = false;
        var inSingle = false;
        var escaped = false;

        foreach (var ch in inner)
        {
            if (escaped)
            {
                current.Append(ch);
                escaped = false;
                continue;
            }

            if (inDouble)
            {
                current.Append(ch);
                if (ch == '\\')
                {
                    escaped = true;
                }
                else if (ch == '"')
                {
                    inDouble = false;
                }

                continue;
            }

            if (inSingle)
            {
                current.Append(ch);

                // 單引號的逸出是連續兩個 '';第一個關、第二個開,token 原樣,Unquote 處理。
                if (ch == '\'')
                {
                    inSingle = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inDouble = true;
                current.Append(ch);
            }
            else if (ch == '\'')
            {
                inSingle = true;
                current.Append(ch);
            }
            else if (ch == ',')
            {
                AddTag(tags, current);
            }
            else
            {
                current.Append(ch);
            }
        }

        AddTag(tags, current);
        return tags;

        static void AddTag(List<string> tags, StringBuilder current)
        {
            var tag = Unquote(current.ToString().Trim());
            if (tag.Length > 0)
            {
                tags.Add(tag);
            }

            current.Clear();
        }
    }

    private static DateTimeOffset? ParseDate(string value)
    {
        var unquoted = Unquote(value);
        return DateTimeOffset.TryParse(unquoted, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }

    /// <summary>
    /// 去掉最外層的引號。
    ///
    /// **「開頭與結尾剛好都是引號」不等於「被一對引號包住」。** 只看頭尾兩個字元的話,
    /// <c>"賣點" 與 "痛點"</c> 會被剝成 <c>賣點" 與 "痛點</c>,而下一次 Serialize
    /// 又會把它整個包起來 —— 每編輯一輪就多一層殘骸,而且沒有任何地方會報錯。
    /// 所以中間出現沒有逸出的同種引號時就當它不是引號字串,原樣留著。
    /// </summary>
    private static string Unquote(string value)
    {
        if (value.Length < 2)
        {
            return value;
        }

        var quote = value[0];
        if (quote != value[^1] || (quote != '"' && quote != '\''))
        {
            return value;
        }

        var inner = value[1..^1];

        if (quote == '"')
        {
            return HasUnescapedDoubleQuote(inner)
                ? value
                : inner.Replace("\\\"", "\"", StringComparison.Ordinal)
                       .Replace("\\\\", "\\", StringComparison.Ordinal);
        }

        // 單引號的逸出是連續兩個 '' —— 落單的一個代表這對引號沒有包住整個值。
        return HasLoneSingleQuote(inner)
            ? value
            : inner.Replace("''", "'", StringComparison.Ordinal);
    }

    private static bool HasUnescapedDoubleQuote(string inner)
    {
        for (var i = 0; i < inner.Length; i++)
        {
            if (inner[i] == '\\')
            {
                // 被逸出的那個字元跳過,\\ 也因此不會被誤讀成逸出下一個字元。
                i++;
                continue;
            }

            if (inner[i] == '"')
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasLoneSingleQuote(string inner)
    {
        for (var i = 0; i < inner.Length;)
        {
            if (inner[i] != '\'')
            {
                i++;
                continue;
            }

            var start = i;
            while (i < inner.Length && inner[i] == '\'')
            {
                i++;
            }

            if ((i - start) % 2 != 0)
            {
                return true;
            }
        }

        return false;
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

        // 空的 tags 不寫。這幾行是使用者在手機的 OneDrive / Google Drive App 裡
        // 打開筆記時最先看到的東西(那些 App 不渲染 Markdown,front matter 就是純文字
        // 擋在最上面),而 tags 目前還沒有任何功能 —— 一個空陣列不值得佔那一行。
        // 有值時照樣寫,別的編輯器加的 tags 也因此原樣留著。
        if (note.Tags.Count > 0)
        {
            builder.Append("tags: [").AppendJoin(", ", note.Tags.Select(QuoteArrayItem)).Append("]\n");
        }

        // 別人加的欄位原樣寫回。
        foreach (var line in note.ExtraFrontMatter)
        {
            builder.Append(line).Append('\n');
        }

        builder.Append(Delimiter).Append('\n');
        builder.Append('\n');
        builder.Append(Newlines.ToLf(note.Body));

        var text = builder.ToString();

        // 檔尾固定一個換行,讓 diff 乾淨。
        if (!text.EndsWith('\n'))
        {
            text += "\n";
        }

        // Windows 上的編輯器對 CRLF 比較友善,統一輸出 CRLF。
        return Newlines.ToCrlf(text);
    }

    /// <summary>
    /// 檔尾那個換行是格式,不是內容。Serialize 一定會補上它,所以 Parse 一定要拿掉,
    /// 否則 note.Body 跟磁碟上的內容永遠對不起來,每編輯一次就多一個換行。
    /// </summary>
    private static string StripTrailingNewline(string text) =>
        text.EndsWith('\n') ? text[..^1] : text;

    /// <summary>
    /// 只在不加引號會讓 YAML 解讀錯誤時才加,不然人在編輯器裡看到滿滿的引號很煩。
    /// </summary>
    private static string Quote(string value)
    {
        if (value.Length == 0)
        {
            return "\"\"";
        }

        // 純量只能佔一行:含換行的值寫出去是多行純量,我們自己的 Parse 讀不回來
        // (後半會掉進 ExtraFrontMatter,之後每編輯一輪就把殘骸再寫回去一次)。
        // 標題與 tag 本來就該是單行,這裡直接收攏。
        value = SingleLine(value);

        var needsQuoting =
            char.IsWhiteSpace(value[0])
            || char.IsWhiteSpace(value[^1])
            || value.Contains(": ", StringComparison.Ordinal)
            || value.EndsWith(':')
            || value.Contains(" #", StringComparison.Ordinal)
            || "-?:,[]{}#&*!|>'\"%@`".Contains(value[0])
            || IsYamlKeyword(value);

        return needsQuoting ? ForceQuote(value) : value;
    }

    /// <summary>
    /// inline 陣列(<c>tags: […]</c>)裡的項目:逗號是分隔符、']' 會提前關閉陣列,
    /// 所以值中間含這兩種字元時一律加引號 —— 純量位置不需要這兩條,才獨立一個方法。
    /// </summary>
    private static string QuoteArrayItem(string value)
    {
        var quoted = Quote(value);

        // Quote 已經加過引號(或空值)就不用再判斷。
        if (quoted.Length == 0 || quoted[0] == '"')
        {
            return quoted;
        }

        return quoted.Contains(',') || quoted.Contains('[') || quoted.Contains(']')
            ? ForceQuote(quoted)
            : quoted;
    }

    private static string ForceQuote(string value)
    {
        var escaped = value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

        return $"\"{escaped}\"";
    }

    private static string SingleLine(string value) =>
        value.Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\n', ' ')
            .Replace('\r', ' ');

    /// <summary>不加引號會被 YAML 當成布林/空值/數字的字串。</summary>
    private static bool IsYamlKeyword(string value) =>
        value is "true" or "false" or "null" or "yes" or "no" or "on" or "off" or "~"
        || double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out _);
}
