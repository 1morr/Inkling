using Xunit;

namespace Notelet.Core.Tests;

public class NoteTests
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
    public void Summary_SkipsCodeFenceDelimiters()
    {
        // 以程式碼片段開頭的筆記很常見;副標顯示三個反引號等於沒有副標。
        var note = MakeNote("標題", "```python\nprint(1)\n```");

        Assert.Equal("print(1)", note.Summary);
    }

    [Fact]
    public void Summary_SkipsTildeFenceDelimiters()
    {
        var note = MakeNote("標題", "~~~\nsome code\n~~~");

        Assert.Equal("some code", note.Summary);
    }

    [Fact]
    public void Summary_SkipsHorizontalRules()
    {
        var note = MakeNote("標題", "---\n\n真正的內容");

        Assert.Equal("真正的內容", note.Summary);
    }

    [Fact]
    public void Summary_SkipsTableSeparatorRows()
    {
        var note = MakeNote("標題", "|---|---|\n真正的內容");

        Assert.Equal("真正的內容", note.Summary);
    }

    [Fact]
    public void Summary_StripsListAndQuoteMarkers()
    {
        Assert.Equal("引文", MakeNote("t", "> 引文").Summary);
        Assert.Equal("項目", MakeNote("t", "- 項目").Summary);
        Assert.Equal("井號標題", MakeNote("t", "# 井號標題").Summary);
    }

    [Fact]
    public void Summary_TruncatesLongLinesWithEllipsis()
    {
        var longLine = new string('字', 130);
        var summary = MakeNote("t", longLine).Summary;

        Assert.Equal(121, summary.Length);
        Assert.EndsWith("…", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Summary_SkipsTheLineThatBecameTheTitle()
    {
        // 外來檔案的標題是從內文第一行推導出來的;副標再顯示同一句話只是重複,
        // 該從標題那一行之後開始取。
        var note = MakeNote("外來標題", "# 外來標題\n\n這是別的工具寫的檔案。");

        Assert.Equal("這是別的工具寫的檔案。", note.Summary);
    }

    [Fact]
    public void Summary_IsEmptyWhenTheOnlyContentIsTheTitle()
    {
        // 沒有下一行有效文字就留空,讓副標乾脆不顯示。
        var note = MakeNote("唯一一行", "# 唯一一行");

        Assert.Equal(string.Empty, note.Summary);
    }

    [Fact]
    public void Summary_DoesNotSkipWhenTheFirstLineIsNotTheTitle()
    {
        var note = MakeNote("標題", "內文第一行\n第二行");

        Assert.Equal("內文第一行", note.Summary);
    }

    [Fact]
    public void Summary_EmptyBody_IsEmpty()
    {
        Assert.Equal(string.Empty, MakeNote("t", string.Empty).Summary);
    }
}
