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
        Assert.Equal("買咖啡機的想法", QuickCapture.ExtractText("n 買咖啡機的想法", Options()));
    }

    [Fact]
    public void TrimsSurroundingWhitespace()
    {
        Assert.Equal("想法", QuickCapture.ExtractText("n    想法   ", Options()));
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
        Assert.Null(QuickCapture.ExtractText(query, Options()));
    }

    [Theory]
    [InlineData("n")]
    [InlineData("n ")]
    [InlineData("n     ")]
    public void DoesNotTriggerWhenThereIsNothingToRecord(string query)
    {
        Assert.Null(QuickCapture.ExtractText(query, Options()));
    }

    [Fact]
    public void PrefixWithoutTrailingSpaceStillBehavesCorrectly()
    {
        // 使用者在設定裡打 "n" 而不是 "n " 是很自然的事,不該因此就壞掉。
        var options = Options("n");

        Assert.Equal("一個想法", QuickCapture.ExtractText("n 一個想法", options));
        Assert.Null(QuickCapture.ExtractText("note about something", options));
    }

    [Fact]
    public void SymbolPrefixDoesNotRequireASpace()
    {
        var options = Options(",");

        Assert.Equal("一個想法", QuickCapture.ExtractText(",一個想法", options));
        Assert.Equal("一個想法", QuickCapture.ExtractText(", 一個想法", options));
    }

    [Fact]
    public void IsCaseInsensitive()
    {
        Assert.Equal("想法", QuickCapture.ExtractText("N 想法", Options()));
    }

    [Fact]
    public void ReturnsNullWhenDisabled()
    {
        Assert.Null(QuickCapture.ExtractText("n 想法", Options(enabled: false)));
    }

    [Fact]
    public void EmptyPrefixNeverTriggers()
    {
        // 空前綴等於每一次搜索都要冒出來。與其吵死使用者,不如乾脆不觸發。
        Assert.Null(QuickCapture.ExtractText("隨便打的東西", Options(prefix: "")));
    }
}
