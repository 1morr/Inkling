using Xunit;

namespace Notelet.Core.Tests;

public class NoteletOptionsTests
{
    [Theory]
    [InlineData("n", "n ")]
    [InlineData("n ", "n ")]
    [InlineData("note", "note ")]
    [InlineData("note   ", "note ")]
    [InlineData(",", ",")]
    [InlineData(">", ">")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void QuickCapturePrefix_IsNormalized(string input, string expected)
    {
        var options = new NoteletOptions { NotesDirectory = @"C:\notes", QuickCapturePrefix = input };

        Assert.Equal(expected, options.QuickCapturePrefix);
    }

    [Fact]
    public void DefaultNotesDirectory_PrefersOneDrive()
    {
        // 這台機器有 OneDrive,預設就該落在 OneDrive 底下 —— 那是同步方案的全部。
        var path = NoteletOptions.DefaultNotesDirectory();

        Assert.EndsWith("Notelet", path, StringComparison.Ordinal);

        var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
        if (!string.IsNullOrWhiteSpace(oneDrive) && Directory.Exists(oneDrive))
        {
            Assert.StartsWith(oneDrive, path, StringComparison.OrdinalIgnoreCase);
        }
    }
}
