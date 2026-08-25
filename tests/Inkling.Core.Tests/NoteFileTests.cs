using Xunit;

namespace Inkling.Core.Tests;

public class NoteFileTests
{
    private static Note SampleNote(string title = "買咖啡機的想法", string body = "先查一下手沖跟義式的差別。") => new()
    {
        Id = "20260810-143052-a7f3",
        Title = title,
        Body = body,
        Created = new DateTimeOffset(2026, 8, 10, 14, 30, 52, TimeSpan.FromHours(8)),
        Updated = new DateTimeOffset(2026, 8, 11, 9, 15, 0, TimeSpan.FromHours(8)),
        FilePath = @"C:\notes\x.md",
    };

    [Fact]
    public void RoundTrip_PreservesAllKnownFields()
    {
        var original = SampleNote() with { Tags = ["idea", "咖啡"] };

        var parsed = NoteFile.Parse(NoteFile.Serialize(original));

        Assert.True(parsed.HadFrontMatter);
        Assert.Equal(original.Id, parsed.Id);
        Assert.Equal(original.Title, parsed.Title);
        Assert.Equal(original.Body, parsed.Body);
        Assert.Equal(original.Created, parsed.Created);
        Assert.Equal(original.Updated, parsed.Updated);
        Assert.Equal(original.Tags, parsed.Tags);
    }

    [Fact]
    public void RoundTrip_PreservesUnknownFrontMatterFields()
    {
        // 這是整個檔案格式最重要的一條:別的編輯器(例如 Obsidian)加的 metadata,
        // 經過 Inkling 編輯一輪之後必須原封不動。
        var content = """
            ---
            id: 20260810-143052-a7f3
            title: 有別人加的欄位
            created: 2026-08-10T14:30:52+08:00
            updated: 2026-08-10T14:30:52+08:00
            tags: []
            aliases:
              - 別名一
              - 別名二
            cssclass: my-custom-class
            publish: true
            ---

            內文
            """;

        var parsed = NoteFile.Parse(content);

        Assert.Contains("cssclass: my-custom-class", parsed.ExtraFrontMatter);
        Assert.Contains("publish: true", parsed.ExtraFrontMatter);
        Assert.Contains("aliases:", parsed.ExtraFrontMatter);
        Assert.Contains("  - 別名一", parsed.ExtraFrontMatter);
        Assert.Contains("  - 別名二", parsed.ExtraFrontMatter);

        // 再寫回去，那些欄位還要在。
        var note = SampleNote() with
        {
            Title = parsed.Title!,
            Body = parsed.Body,
            ExtraFrontMatter = parsed.ExtraFrontMatter,
        };

        var reparsed = NoteFile.Parse(NoteFile.Serialize(note));

        Assert.Equal(parsed.ExtraFrontMatter, reparsed.ExtraFrontMatter);
    }

    [Theory]
    [InlineData("標題裡有: 冒號")]
    [InlineData("#開頭是井號")]
    [InlineData("- 開頭是連字號")]
    [InlineData("true")]
    [InlineData("42")]
    [InlineData("結尾有空白 ")]
    [InlineData("有\"雙引號\"")]
    public void RoundTrip_HandlesTitlesThatNeedQuoting(string title)
    {
        var parsed = NoteFile.Parse(NoteFile.Serialize(SampleNote(title)));

        Assert.Equal(title, parsed.Title);
    }

    [Fact]
    public void Serialize_OmitsEmptyTags()
    {
        // front matter 是使用者在手機的雲端硬碟 App 裡最先看到的東西(那些 App
        // 不渲染 Markdown)，而 tags 還沒有功能 —— 空的就別佔那一行。
        var text = NoteFile.Serialize(SampleNote());

        Assert.DoesNotContain("tags:", text, StringComparison.Ordinal);

        // 少一行不能讓其他欄位跟著遺失。
        var parsed = NoteFile.Parse(text);
        Assert.True(parsed.HadFrontMatter);
        Assert.Equal("20260810-143052-a7f3", parsed.Id);
        Assert.Empty(parsed.Tags);
        Assert.Empty(parsed.ExtraFrontMatter);
    }

    [Fact]
    public void Serialize_WritesTagsWhenPresent()
    {
        // 省略只針對空值。別的編輯器加上的 tags 經過 Inkling 一輪之後必須還在。
        var text = NoteFile.Serialize(SampleNote() with { Tags = ["idea", "咖啡"] });

        Assert.Contains("tags: [idea, 咖啡]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RoundTrip_TagContainingComma_StaysOneTag()
    {
        // inline 陣列裡逗號是分隔符;含逗號的 tag 不加引號寫回去，讀回來就裂成兩個 ——
        // 正好違反「別人的 metadata 經過 Inkling 一輪必須原封不動」的承諾。
        var parsed = NoteFile.Parse(NoteFile.Serialize(SampleNote() with { Tags = ["a, b", "c"] }));

        Assert.Equal(new[] { "a, b", "c" }, parsed.Tags);
    }

    [Fact]
    public void Parse_InlineTags_DoesNotSplitInsideDoubleQuotes()
    {
        // Obsidian / Hugo 會寫出帶引號的逗號 tag;不看引號直接 Split 會裂出帶殘引號的碎片。
        var parsed = NoteFile.Parse("---\ntags: [\"a, b\", \"c\"]\n---\n\nbody");

        Assert.Equal(new[] { "a, b", "c" }, parsed.Tags);
    }

    [Fact]
    public void Parse_InlineTags_DoesNotSplitInsideSingleQuotes()
    {
        var parsed = NoteFile.Parse("---\ntags: ['x, y']\n---\n\nbody");

        Assert.Equal(new[] { "x, y" }, parsed.Tags);
    }

    [Fact]
    public void Parse_InlineTags_HandlesEscapedQuotes()
    {
        var parsed = NoteFile.Parse("---\ntags: [\"say \\\"hi\\\", ok\", plain]\n---\n\nbody");

        Assert.Equal(new[] { "say \"hi\", ok", "plain" }, parsed.Tags);
    }

    [Fact]
    public void RoundTrip_TitleContainingNewline_IsCollapsedToSingleLine()
    {
        // 多行純量我們自己的 Parse 讀不回來:後半會掉進 ExtraFrontMatter，之後每編輯
        // 一輪就把殘骸當別人的欄位再寫回去。標題本來就該是單行，序列化時收攏。
        var parsed = NoteFile.Parse(NoteFile.Serialize(SampleNote("第一行\n第二行")));

        Assert.Equal("第一行 第二行", parsed.Title);
        Assert.Empty(parsed.ExtraFrontMatter);
    }

    [Fact]
    public void Parse_StillReadsFilesWithEmptyTags()
    {
        // Inkling 不再寫這一行，但既有的檔案裡到處都是，照樣要讀得懂 ——
        // 而且 tags 不能掉進 ExtraFrontMatter，否則寫回去會多出一行空陣列。
        var parsed = NoteFile.Parse("---\nid: abc\ntitle: 舊檔案\ntags: []\n---\n\n內文");

        Assert.Empty(parsed.Tags);
        Assert.Empty(parsed.ExtraFrontMatter);
    }

    [Fact]
    public void Parse_ReadsBlockStyleTags()
    {
        var content = """
            ---
            id: abc
            title: 區塊式標籤
            tags:
              - idea
              - 咖啡
            ---

            內文
            """;

        var parsed = NoteFile.Parse(content);

        Assert.Equal(new[] { "idea", "咖啡" }, parsed.Tags);
        Assert.Empty(parsed.ExtraFrontMatter);
    }

    [Fact]
    public void Parse_ReadsInlineTags()
    {
        var parsed = NoteFile.Parse("---\ntags: [a, \"b c\"]\n---\n\nbody");

        Assert.Equal(new[] { "a", "b c" }, parsed.Tags);
    }

    [Fact]
    public void Parse_FileWithoutFrontMatter_TreatsEverythingAsBody()
    {
        // 使用者直接丟進資料夾的普通 Markdown。不該被當成壞檔案。
        const string content = "# 一般的 Markdown\n\n沒有 front matter。";

        var parsed = NoteFile.Parse(content);

        Assert.False(parsed.HadFrontMatter);
        Assert.Equal(content, parsed.Body);
        Assert.Null(parsed.Id);
        Assert.Null(parsed.Title);
    }

    [Fact]
    public void Parse_UnterminatedFrontMatter_FallsBackToBody()
    {
        // 開頭有 --- 卻沒收尾。寧可整份當內文，也不能把內容吞掉。
        const string content = "---\nid: abc\ntitle: 沒有收尾\n\n內文";

        var parsed = NoteFile.Parse(content);

        Assert.False(parsed.HadFrontMatter);
        Assert.Equal(content, parsed.Body);
    }

    [Fact]
    public void Parse_EmptyFile_DoesNotThrow()
    {
        var parsed = NoteFile.Parse(string.Empty);

        Assert.False(parsed.HadFrontMatter);
        Assert.Equal(string.Empty, parsed.Body);
    }

    [Fact]
    public void Parse_StripsByteOrderMark()
    {
        var parsed = NoteFile.Parse("\uFEFF---\nid: abc\ntitle: 有 BOM\n---\n\n內文");

        Assert.True(parsed.HadFrontMatter);
        Assert.Equal("abc", parsed.Id);
    }

    [Fact]
    public void Parse_HandlesCrLfLineEndings()
    {
        var parsed = NoteFile.Parse("---\r\nid: abc\r\ntitle: CRLF\r\n---\r\n\r\n第一行\r\n第二行");

        Assert.Equal("abc", parsed.Id);
        Assert.Equal("CRLF", parsed.Title);
        Assert.Equal("第一行\n第二行", parsed.Body);
    }

    [Fact]
    public void Serialize_AlwaysEndsWithSingleNewline()
    {
        var text = NoteFile.Serialize(SampleNote(body: "沒有換行結尾"));

        Assert.EndsWith("沒有換行結尾\r\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_DoesNotDoubleUpCarriageReturns()
    {
        // 表單送回來的內文可能已經是 CRLF，不能再被轉一次變成 \r\r\n。
        var text = NoteFile.Serialize(SampleNote(body: "第一行\r\n第二行"));

        Assert.DoesNotContain("\r\r", text, StringComparison.Ordinal);
        Assert.Equal("第一行\n第二行", NoteFile.Parse(text).Body);
    }

    [Theory]
    [InlineData("單行內文")]
    [InlineData("第一行\n第二行")]
    [InlineData("")]
    [InlineData("結尾有空行\n")]
    [InlineData("# 標題\n\n- 清單\n- 項目\n\n```\ncode\n```")]
    public void SerializeParse_IsIdempotent(string body)
    {
        // 每編輯一次就在檔尾多長一個換行，是這種手寫序列化最典型的 bug。
        // 這裡直接釘死:序列化兩輪之後的文字必須一模一樣。
        var note = SampleNote(body: body);

        var once = NoteFile.Serialize(note);
        var reparsed = NoteFile.Parse(once);
        var twice = NoteFile.Serialize(note with { Body = reparsed.Body });

        Assert.Equal(once, twice);
    }

    [Fact]
    public void RoundTrip_EmptyBody()
    {
        // 快速新增只有標題、沒有內文，這是最常見的一種筆記。
        var parsed = NoteFile.Parse(NoteFile.Serialize(SampleNote(body: string.Empty)));

        Assert.Equal(string.Empty, parsed.Body);
        Assert.Equal("買咖啡機的想法", parsed.Title);
    }

    [Fact]
    public void Parse_HorizontalRuleAtTopOfFile_IsNotFrontMatter()
    {
        // Markdown 的水平線也是 ---。以前這種檔案的第一段會被當成 front matter 吞掉，
        // 使用者在 Inkling 裡編輯一次之後那幾行還會被寫進 front matter 區塊 ——
        // 它們沒有冒號，別的工具從此解析不了這個檔案。
        const string content = "---\n\n# 我的標題\n\n第一段內容\n\n---\n\n第二段內容";

        var parsed = NoteFile.Parse(content);

        Assert.False(parsed.HadFrontMatter);
        Assert.Equal(content, parsed.Body);
        Assert.Null(parsed.Title);
        Assert.Empty(parsed.ExtraFrontMatter);
    }

    [Fact]
    public void Parse_BlockThatOnlyLooksLikeKeysBecauseOfAUrl_IsNotFrontMatter()
    {
        // https://… 有冒號但後面沒有空白。YAML 的對應規則要求冒號後接空白或就是行尾，
        // 少了這一條，任何以水平線開頭又貼了網址的筆記都會被當成 front matter。
        const string content = "---\n\nhttps://example.com\n\n---\n\n內文";

        var parsed = NoteFile.Parse(content);

        Assert.False(parsed.HadFrontMatter);
        Assert.Equal(content, parsed.Body);
    }

    [Fact]
    public void Parse_MarkdownHeadingWithColon_IsNotMistakenForAKey()
    {
        const string content = "---\n\n# 單元二: 開場\n\n---\n\n內文";

        var parsed = NoteFile.Parse(content);

        Assert.False(parsed.HadFrontMatter);
        Assert.Equal(content, parsed.Body);
    }

    [Fact]
    public void Parse_RealFrontMatterWithOnlyOneKey_IsStillFrontMatter()
    {
        // 上面三條的反面:只要有一行是真的 key，就照樣當 front matter,
        // 不能因為收緊判準而把正常的檔案擋掉。
        const string content = "---\ntitle: 只有標題\n---\n\n內文";

        var parsed = NoteFile.Parse(content);

        Assert.True(parsed.HadFrontMatter);
        Assert.Equal("只有標題", parsed.Title);
        Assert.Equal("內文", parsed.Body);
    }

    [Theory]
    [InlineData(">")]
    [InlineData("|")]
    [InlineData(">-")]
    [InlineData("|+")]
    [InlineData("|2")]
    public void Parse_FoldedTitle_KeepsTheTitleAndDoesNotOrphanItsLines(string indicator)
    {
        // 別的工具會把長標題寫成區塊純量。以前 Title 會變成一個 ">"，續行掉進
        // ExtraFrontMatter —— 而 extra 是寫在固定欄位後面的，那幾行縮排於是排到
        // updated: 底下，把 updated 變成多行純量、日期就此壞掉。
        var content = $"---\nid: abc\ntitle: {indicator}\n  這是一個很長的標題\n  被折成兩行\naliases:\n  - foo\n---\n\n內文";

        var parsed = NoteFile.Parse(content);

        Assert.Equal("這是一個很長的標題 被折成兩行", parsed.Title);
        Assert.Equal(["aliases:", "  - foo"], parsed.ExtraFrontMatter);
    }

    [Fact]
    public void Parse_FoldedTitle_SurvivesRoundTripAsASingleLineScalar()
    {
        const string content = "---\nid: abc\ntitle: >\n  折疊的標題\ncreated: 2026-01-01T00:00:00+08:00\nupdated: 2026-01-02T00:00:00+08:00\n---\n\n內文";

        var parsed = NoteFile.Parse(content);
        var note = new Note
        {
            Id = parsed.Id!,
            Title = parsed.Title!,
            Body = parsed.Body,
            Created = parsed.Created!.Value,
            Updated = parsed.Updated!.Value,
            ExtraFrontMatter = parsed.ExtraFrontMatter,
            FilePath = @"C:\notes\x.md",
        };

        var again = NoteFile.Parse(NoteFile.Serialize(note));

        Assert.Equal("折疊的標題", again.Title);
        // 這一條才是重點:updated 沒有被縮排行黏成多行純量。
        Assert.Equal(parsed.Updated, again.Updated);
    }

    [Fact]
    public void Parse_BlockStyleTagsWithAFoldedIndicator_DoesNotProduceAGreaterThanTag()
    {
        const string content = "---\ntitle: t\ntags: >\n  - foo\n  - bar\n---\n\n內文";

        var parsed = NoteFile.Parse(content);

        Assert.Equal(["foo", "bar"], parsed.Tags);
    }

    [Theory]
    [InlineData("\"賣點\" 與 \"痛點\"")]
    [InlineData("'單引號' 開頭也 '單引號' 結尾")]
    public void Parse_TitleThatMerelyStartsAndEndsWithAQuote_IsNotStripped(string title)
    {
        // 只看頭尾兩個字元的話會剝掉一層，而下一次 Serialize 又整個包起來 ——
        // 每編輯一輪就多一層殘骸，沒有任何地方會報錯。
        var content = $"---\ntitle: {title}\n---\n\n內文";

        var parsed = NoteFile.Parse(content);

        Assert.Equal(title, parsed.Title);
    }

    [Fact]
    public void Parse_ProperlyQuotedTitle_IsStillUnquoted()
    {
        // 上一條的反面:真的被一對引號包住(中間的引號有逸出)時照樣要剝掉。
        const string content = "---\ntitle: \"他說 \\\"好\\\" 之後就走了\"\n---\n\n內文";

        var parsed = NoteFile.Parse(content);

        Assert.Equal("他說 \"好\" 之後就走了", parsed.Title);
    }
}
