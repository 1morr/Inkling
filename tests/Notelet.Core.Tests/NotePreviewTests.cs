using Xunit;

namespace Notelet.Core.Tests;

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
        // 空行本身就是段落分隔,不需要也不該加標記。
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
        // 程式碼區塊裡加空白會改變程式碼內容,而且換行本來就已經是換行了。
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
        // 圍籬關掉之後要恢復正常處理,不能因為進過程式碼區塊就整份不管了。
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
        // 動了上面那行,setext 標題就不成立了。
        const string markdown = "標題\n====\n\n內文";

        var result = NotePreview.PreserveLineBreaks(markdown);

        Assert.StartsWith("標題\n====", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ListItems_StillRenderAsSeparateItems()
    {
        var result = NotePreview.PreserveLineBreaks("- 甲\n- 乙\n- 丙");

        // 清單項目本來就各自成行,加了標記也不會壞,但不能把 "- " 弄丟。
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
        // 外來的 Markdown 檔標題是從內文第一個標題推導出來的,再補一次就會出現兩個標題。
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
    public void RenderSource_WrapsTheBodyInACodeFence()
    {
        Assert.Equal("```\n# 標題\n**粗體**\n```", NotePreview.RenderSource("# 標題\n**粗體**"));
    }

    [Fact]
    public void RenderSource_DoesNotTouchTheContent()
    {
        // 這是原始文字模式存在的理由:一個字都不能改,包括不補硬換行。
        const string Body = "111\n222\n  尾巴有空白  ";

        Assert.Equal($"```\n{Body}\n```", NotePreview.RenderSource(Body));
    }

    [Theory]
    [InlineData("```\ncode\n```", "````")]
    [InlineData("~~~\ncode\n~~~", "```")]
    [InlineData("行內 `code` 而已", "```")]
    [InlineData("````\ncode\n````", "`````")]
    public void RenderSource_UsesAFenceLongerThanAnythingInTheBody(string body, string expectedFence)
    {
        // 內文自己的程式碼區塊會把長度相同的外層圍欄提前關掉,
        // 後半段就漏出去被渲染 —— 那正是要避免的事。
        var result = NotePreview.RenderSource(body);

        Assert.StartsWith(expectedFence + "\n", result, StringComparison.Ordinal);
        Assert.EndsWith("\n" + expectedFence, result, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderSource_EmptyBody_ReturnsEmpty()
    {
        // 空的程式碼區塊只會畫出一個空灰框,不如交給呼叫端顯示自己的提示。
        Assert.Equal(string.Empty, NotePreview.RenderSource(string.Empty));
    }
}
