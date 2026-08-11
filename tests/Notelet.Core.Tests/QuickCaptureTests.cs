using Xunit;

namespace Notelet.Core.Tests;

public class QuickCaptureTests
{
    [Fact]
    public void TakesTheTextAsIs()
    {
        var draft = QuickCapture.Split("買咖啡機的想法");

        Assert.NotNull(draft);
        Assert.Equal("買咖啡機的想法", draft.Title);
        Assert.Equal(string.Empty, draft.Body);
    }

    [Fact]
    public void TrimsSurroundingWhitespace()
    {
        Assert.Equal("想法", QuickCapture.Split("   想法   ")?.Title);
    }

    [Fact]
    public void DoesNotSecondGuessTheUsersIntent()
    {
        // 沒有前綴判斷,也不該有:進得了快速記下頁就代表意圖明確。
        // 這裡刻意用一句看起來像普通搜索的話 —— 它一樣是合法的筆記標題。
        Assert.Equal("note about something", QuickCapture.Split("note about something")?.Title);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ReturnsNullWhenThereIsNothingToRecord(string? text)
    {
        // 頁面剛打開、或使用者還沒打字 —— 這時候不該出現「記下」那一列。
        Assert.Null(QuickCapture.Split(text));
    }

    [Theory]
    [InlineData("買咖啡機;;比較過 Breville 跟 Sage")]
    [InlineData("買咖啡機；；比較過 Breville 跟 Sage")]
    [InlineData("買咖啡機;；比較過 Breville 跟 Sage")]
    [InlineData("買咖啡機；;比較過 Breville 跟 Sage")]
    [InlineData("  買咖啡機 ;;  比較過 Breville 跟 Sage  ")]
    public void SplitsTitleAndBodyOnTheSeparator(string text)
    {
        // 全形分號也要認(中文輸入法打出來的就是全形),而且半形全形混著打也算 ——
        // 中英切換的當下打出哪一個並不受控。
        var draft = QuickCapture.Split(text);

        Assert.NotNull(draft);
        Assert.Equal("買咖啡機", draft.Title);
        Assert.Equal("比較過 Breville 跟 Sage", draft.Body);
    }

    [Theory]
    [InlineData("買咖啡機;比較過幾台")]
    [InlineData("買咖啡機；比較過幾台")]
    [InlineData("for (var i = 0; i < 10; i++)")]
    public void SingleSeparatorIsOrdinaryText(string text)
    {
        // 要連續兩個才切。單一個分號在標題裡太常見了,不能拿來當觸發條件。
        var draft = QuickCapture.Split(text);

        Assert.NotNull(draft);
        Assert.Equal(text, draft.Title);
        Assert.Equal(string.Empty, draft.Body);
    }

    [Fact]
    public void SplitsOnTheFirstSeparatorOnly()
    {
        // 後面的分隔符是內文的一部分,不再切。
        var draft = QuickCapture.Split("標題;;第一段;;第二段");

        Assert.NotNull(draft);
        Assert.Equal("標題", draft.Title);
        Assert.Equal("第一段;;第二段", draft.Body);
    }

    [Fact]
    public void SeparatorWithoutBodyIsJustATitle()
    {
        // 正在打字的中間狀態:分隔符打了、內文還沒打。這時候該存的就是標題。
        var draft = QuickCapture.Split("買咖啡機;;");

        Assert.NotNull(draft);
        Assert.Equal("買咖啡機", draft.Title);
        Assert.Equal(string.Empty, draft.Body);
    }

    [Fact]
    public void ThirdSeparatorCharBelongsToTheBody()
    {
        // 「;;;」切在前兩個,第三個是內文的第一個字。
        var draft = QuickCapture.Split("標題;;;內文");

        Assert.NotNull(draft);
        Assert.Equal("標題", draft.Title);
        Assert.Equal(";內文", draft.Body);
    }

    [Fact]
    public void SeparatorWithoutTitleDoesNotTrigger()
    {
        // 沒有標題就沒有筆記。
        Assert.Null(QuickCapture.Split(";;只有內文"));
    }
}
