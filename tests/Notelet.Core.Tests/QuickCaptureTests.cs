using Xunit;

namespace Notelet.Core.Tests;

public class QuickCaptureTests
{
    private static NoteletOptions Options(string prefix = "n ", bool enabled = true) => new()
    {
        NotesDirectory = @"C:\notes",
        QuickCapturePrefix = prefix,
        QuickCaptureEnabled = enabled,
    };

    [Fact]
    public void ExtractsTextAfterThePrefix()
    {
        var draft = QuickCapture.Parse("n 買咖啡機的想法", Options());

        Assert.NotNull(draft);
        Assert.Equal("買咖啡機的想法", draft.Title);
        Assert.Equal(string.Empty, draft.Body);
    }

    [Fact]
    public void TrimsSurroundingWhitespace()
    {
        Assert.Equal("想法", QuickCapture.Parse("n    想法   ", Options())?.Title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("buy coffee")]
    [InlineData("notepad")]
    [InlineData("note about something")]
    [InlineData("nginx 設定")]
    public void DoesNotTriggerOnOrdinaryQueries(string query)
    {
        // 這是整個功能最容易惹人厭的失敗模式:前綴太鬆,結果每次搜索都跳出來,
        // 而且會把第一個字母吃掉(「note about x」變成記下「ote about x」)。
        Assert.Null(QuickCapture.Parse(query, Options()));
    }

    [Theory]
    [InlineData("n")]
    [InlineData("n ")]
    [InlineData("n     ")]
    public void DoesNotTriggerWhenThereIsNothingToRecord(string query)
    {
        Assert.Null(QuickCapture.Parse(query, Options()));
    }

    [Fact]
    public void PrefixWithoutTrailingSpaceStillBehavesCorrectly()
    {
        // 使用者在設定裡打 "n" 而不是 "n " 是很自然的事,不該因此就壞掉。
        var options = Options("n");

        Assert.Equal("一個想法", QuickCapture.Parse("n 一個想法", options)?.Title);
        Assert.Null(QuickCapture.Parse("note about something", options));
    }

    [Fact]
    public void SymbolPrefixDoesNotRequireASpace()
    {
        var options = Options(",");

        Assert.Equal("一個想法", QuickCapture.Parse(",一個想法", options)?.Title);
        Assert.Equal("一個想法", QuickCapture.Parse(", 一個想法", options)?.Title);
    }

    [Fact]
    public void IsCaseInsensitive()
    {
        Assert.Equal("想法", QuickCapture.Parse("N 想法", Options())?.Title);
    }

    [Fact]
    public void ReturnsNullWhenDisabled()
    {
        Assert.Null(QuickCapture.Parse("n 想法", Options(enabled: false)));
    }

    [Fact]
    public void EmptyPrefixNeverTriggers()
    {
        // 空前綴等於每一次搜索都要冒出來。與其吵死使用者,不如乾脆不觸發。
        Assert.Null(QuickCapture.Parse("隨便打的東西", Options(prefix: "")));
    }

    [Theory]
    [InlineData("n 買咖啡機;比較過 Breville 跟 Sage")]
    [InlineData("n 買咖啡機；比較過 Breville 跟 Sage")]
    [InlineData("n 買咖啡機 ;  比較過 Breville 跟 Sage  ")]
    public void SplitsTitleAndBodyOnTheSeparator(string query)
    {
        // 全形分號也要認:中文輸入法打出來的就是全形。
        var draft = QuickCapture.Parse(query, Options());

        Assert.NotNull(draft);
        Assert.Equal("買咖啡機", draft.Title);
        Assert.Equal("比較過 Breville 跟 Sage", draft.Body);
    }

    [Fact]
    public void SplitsOnTheFirstSeparatorOnly()
    {
        // 後面的分號是內文的一部分,不再切 —— 內文本來就可能有分號(程式碼、清單)。
        var draft = QuickCapture.Parse("n 標題;第一段;第二段", Options());

        Assert.NotNull(draft);
        Assert.Equal("標題", draft.Title);
        Assert.Equal("第一段;第二段", draft.Body);
    }

    [Fact]
    public void SeparatorWithoutBodyIsJustATitle()
    {
        // 正在打字的中間狀態:分號打了、內文還沒打。這時候該存的就是標題。
        var draft = QuickCapture.Parse("n 買咖啡機;", Options());

        Assert.NotNull(draft);
        Assert.Equal("買咖啡機", draft.Title);
        Assert.Equal(string.Empty, draft.Body);
    }

    [Fact]
    public void SeparatorWithoutTitleDoesNotTrigger()
    {
        // 沒有標題就沒有筆記。
        Assert.Null(QuickCapture.Parse("n ;只有內文", Options()));
    }
}
