using Xunit;

namespace Inkling.Core.Tests;

public class InklingOptionsTests
{
    [Fact]
    public void DefaultNotesDirectory_PrefersOneDrive()
    {
        // 有 OneDrive 就用它 —— 那是這個專案全部的同步方案。
        Assert.Equal(
            Path.Combine(@"D:\OneDrive", "Inkling"),
            InklingOptions.DefaultNotesDirectoryUnder(@"D:\OneDrive", @"C:\Users\me\Documents"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DefaultNotesDirectory_FallsBackToDocumentsWithoutOneDrive(string? oneDrive)
    {
        // 這一條在作者機器上碰不到真實情境(這台有 OneDrive),而 CI 上碰不到的是上一條
        // —— 兩邊各只驗得到一半,正是把決策抽出來的理由。
        Assert.Equal(
            Path.Combine(@"C:\Users\me\Documents", "Inkling"),
            InklingOptions.DefaultNotesDirectoryUnder(oneDrive, @"C:\Users\me\Documents"));
    }

    [Fact]
    public void DefaultNotesDirectory_UsesThisMachinesEnvironment()
    {
        // 無參數那個版本仍然要接得起來:上面兩條驗的是決策,這一條驗的是接線。
        var path = InklingOptions.DefaultNotesDirectory();

        Assert.EndsWith("Inkling", path, StringComparison.Ordinal);
        Assert.True(Path.IsPathFullyQualified(path), path);

        var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
        var expectedRoot = !string.IsNullOrWhiteSpace(oneDrive) && Directory.Exists(oneDrive)
            ? oneDrive
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        Assert.StartsWith(expectedRoot, path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MaxResults_DefaultsTo200()
    {
        // design-notes〈效能上的規矩〉把這個數字列為承諾,README 也講了截斷提示。
        // 截斷本身在清單頁與刪除頁(UI 層,那一層另有測試),這裡守的是預設值。
        Assert.Equal(200, new InklingOptions { NotesDirectory = @"C:\notes" }.MaxResults);
    }
}
