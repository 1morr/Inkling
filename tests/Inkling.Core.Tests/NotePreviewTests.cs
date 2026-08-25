using Xunit;

namespace Inkling.Core.Tests;

public class NotePreviewTests
{
    private static Note MakeNote(string title, string body) => new()
    {
        Id = "id",
        Title = title,
        Body = body,
        Created = DateTimeOffset.UnixEpoch,
        Updated = DateTimeOffset.UnixEpoch,
        FilePath = "x.md",
    };

    [Fact]
    public void SingleNewlines_BecomeHardBreaks()
    {
        // 標準 Markdown 會把這三行併成一行。對一個隨手記想法的工具來說那不是使用者要的。
        var result = NotePreview.PreserveLineBreaks("111\n222\n333");

        Assert.Equal("111  \n222  \n333", result);
    }

    [Fact]
    public void LastLine_GetsNoTrailingMarker()
    {
        Assert.Equal("只有一行", NotePreview.PreserveLineBreaks("只有一行"));
    }

    [Fact]
    public void BlankLines_AreLeftAlone()
    {
        // 空行本身就是段落分隔，不需要也不該加標記。
        var result = NotePreview.PreserveLineBreaks("第一段\n\n第二段");

        Assert.Equal("第一段\n\n第二段", result);
    }

    [Fact]
    public void ExistingHardBreaks_AreNotDoubled()
    {
        var result = NotePreview.PreserveLineBreaks("已經有了  \n下一行");

        Assert.Equal("已經有了  \n下一行", result);
    }

    [Fact]
    public void BackslashLineBreaks_AreLeftAlone()
    {
        var result = NotePreview.PreserveLineBreaks("反斜線換行\\\n下一行");

        Assert.Equal("反斜線換行\\\n下一行", result);
    }

    [Fact]
    public void FencedCodeBlocks_AreUntouched()
    {
        // 程式碼區塊裡加空白會改變程式碼內容，而且換行本來就已經是換行了。
        const string markdown = "說明文字\n\n```csharp\nvar x = 1;\nvar y = 2;\n```\n\n後面";

        var result = NotePreview.PreserveLineBreaks(markdown);

        Assert.Contains("var x = 1;\nvar y = 2;", result, StringComparison.Ordinal);
        Assert.DoesNotContain("var x = 1;  ", result, StringComparison.Ordinal);
    }

    [Fact]
    public void TildeFences_AreAlsoRecognised()
    {
        const string markdown = "~~~\n第一行\n第二行\n~~~";

        var result = NotePreview.PreserveLineBreaks(markdown);

        Assert.Contains("第一行\n第二行", result, StringComparison.Ordinal);
    }

    [Fact]
    public void TextAfterAClosedFence_IsProcessedAgain()
    {
        // 圍籬關掉之後要恢復正常處理，不能因為進過程式碼區塊就整份不管了。
        const string markdown = "```\ncode\n```\n\n甲\n乙";

        var result = NotePreview.PreserveLineBreaks(markdown);

        Assert.EndsWith("甲  \n乙", result, StringComparison.Ordinal);
        Assert.DoesNotContain("code  ", result, StringComparison.Ordinal);
    }

    [Fact]
    public void TableRows_AreUntouched()
    {
        const string markdown = "| 欄位 | 值 |\n|---|---|\n| a | 1 |";

        var result = NotePreview.PreserveLineBreaks(markdown);

        Assert.Equal(markdown, result);
    }

    [Fact]
    public void IndentedCodeBlocks_AreUntouched()
    {
        const string markdown = "說明\n\n    indented code\n    second line";

        var result = NotePreview.PreserveLineBreaks(markdown);

        Assert.Equal(markdown, result);
    }

    [Fact]
    public void SetextHeadingUnderlines_AreUntouched()
    {
        // 動了上面那行，setext 標題就不成立了。
        const string markdown = "標題\n====\n\n內文";

        var result = NotePreview.PreserveLineBreaks(markdown);

        Assert.StartsWith("標題\n====", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ListItems_StillRenderAsSeparateItems()
    {
        var result = NotePreview.PreserveLineBreaks("- 甲\n- 乙\n- 丙");

        // 清單項目本來就各自成行，加了標記也不會壞，但不能把 "- " 弄丟。
        Assert.Contains("- 甲", result, StringComparison.Ordinal);
        Assert.Contains("- 乙", result, StringComparison.Ordinal);
        Assert.Contains("- 丙", result, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyBody_DoesNotThrow()
    {
        Assert.Equal(string.Empty, NotePreview.PreserveLineBreaks(string.Empty));
    }

    [Fact]
    public void Render_PrependsTitleAsHeading()
    {
        var result = NotePreview.Render(MakeNote("我的標題", "內文"));

        Assert.Equal("# 我的標題\n\n內文", result);
    }

    [Fact]
    public void Render_EmptyBody_ShowsOnlyTheTitle()
    {
        Assert.Equal("# 我的標題", NotePreview.Render(MakeNote("我的標題", string.Empty)));
    }

    [Fact]
    public void Render_DoesNotDuplicateATitleThatIsAlreadyInTheBody()
    {
        // 外來的 Markdown 檔標題是從內文第一個標題推導出來的，再補一次就會出現兩個標題。
        var note = MakeNote("外面來的標題", "# 外面來的標題\n\n內文");

        var result = NotePreview.Render(note);

        Assert.Equal("# 外面來的標題\n\n內文", result);
    }

    [Fact]
    public void Render_AppliesLineBreaksToTheBody()
    {
        var result = NotePreview.Render(MakeNote("test", "111\n222\n333"));

        Assert.Equal("# test\n\n111  \n222  \n333", result);
    }

    [Fact]
    public void RenderSource_EscapesMarkdownSyntaxSoItShowsUpLiterally()
    {
        // 反斜線只存在於送給渲染器的字串裡;畫面上顯示的是 "# 標題",
        // 使用者選取複製走的也是 "# 標題"。
        Assert.Equal("\\# 標題  \n\\*\\*粗體\\*\\*", NotePreview.RenderSource("# 標題\n**粗體**"));
    }

    [Theory]
    [InlineData("```", "\\`\\`\\`")]
    [InlineData("- 清單", "\\- 清單")]
    [InlineData("| a | b |", "\\| a \\| b \\|")]
    [InlineData("[連結](url)", "\\[連結\\]\\(url\\)")]
    [InlineData("1. 項目", "1\\. 項目")]
    [InlineData("<div>", "\\<div\\>")]
    public void RenderSource_EscapesEveryConstructThatWouldOtherwiseRender(string body, string expected)
    {
        Assert.Equal(expected, NotePreview.RenderSource(body));
    }

    [Fact]
    public void RenderSource_LeavesNonAsciiAlone()
    {
        // 中文與全形標點都不在 CommonMark 的逃脫集合裡，補反斜線只會讓它顯示出來。
        // (半形標點就該逃脫，連 : 也在集合內 —— 這裡刻意避開，測的是非 ASCII。)
        const string Text = "買咖啡機、順便看看濾杯。「這個要記住」";

        Assert.Equal(Text, NotePreview.RenderSource(Text));
    }

    [Fact]
    public void RenderSource_BlankLineStaysAParagraphBreak()
    {
        // 空行前面不補硬換行 —— 空行本身就是段落分隔，補了只是多出看不見的空白。
        Assert.Equal("111\n\n222", NotePreview.RenderSource("111\n\n222"));
    }

    [Fact]
    public void RenderSource_DropsLeadingIndentation()
    {
        // 刻意接受的取捨。段落開頭的四個空白在 CommonMark 裡是縮排程式碼區塊，
        // 那會把我們正想避開的外框畫回來。
        Assert.Equal("縮排的行", NotePreview.RenderSource("    縮排的行"));
    }

    [Fact]
    public void RenderSource_EmptyBody_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, NotePreview.RenderSource(string.Empty));
    }
}
