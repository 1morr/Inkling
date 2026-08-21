using Xunit;

namespace Inkling.Core.Tests;

/// <summary>
/// 隨手草稿的檔案讀寫。這一層的規矩只有三條,但每一條壞掉都會讓使用者靜靜地掉資料:
/// 讀不到要當成「空的」而不是爆掉、寫入要是原子的、EnsureFile 絕不能覆寫既有內容。
/// </summary>
public class ScratchpadStoreTests
{
    [Fact]
    public void Read_WhenFileDoesNotExist_ReturnsEmpty()
    {
        using var temp = new TempDirectory();
        var store = new ScratchpadStore(temp.Options);

        // 不能丟例外:第一次打開隨手草稿時檔案本來就不存在,而這個回傳值會直接進 UI。
        Assert.Equal(string.Empty, store.Read());
    }

    [Fact]
    public void Read_WhenDirectoryDoesNotExist_ReturnsEmpty()
    {
        using var temp = new TempDirectory();
        var options = new InklingOptions
        {
            NotesDirectory = Path.Combine(temp.Path, "還沒建出來"),
        };

        Assert.Equal(string.Empty, new ScratchpadStore(options).Read());
    }

    /// <remarks>
    /// 記憶體裡的約定是「LF、檔尾不帶換行」,所以這裡的輸入都照那個形狀寫。
    /// CR / CRLF 進來會被折平,那是 <see cref="Write_NormalizesEveryFlavourOfNewlineToCrlfOnDisk"/> 的事。
    /// </remarks>
    [Theory]
    [InlineData("一行")]
    [InlineData("第一行\n第二行\n第三行")]
    [InlineData("中間有\n\n空行也要留著")]
    [InlineData("  前導空白是使用者自己的縮排,不能吃掉")]
    [InlineData("emoji 🐈 與全形標點,。「」都要活著")]
    [InlineData("${foo} 與 \"雙引號\" 不該被任何人解讀")]
    public void WriteThenRead_RoundTrips(string text)
    {
        using var temp = new TempDirectory();
        var store = new ScratchpadStore(temp.Options);

        store.Write(text);

        Assert.Equal(text, store.Read());
    }

    [Fact]
    public void Write_OverwritesPreviousContentEntirely()
    {
        using var temp = new TempDirectory();
        var store = new ScratchpadStore(temp.Options);

        store.Write("很長很長的第一版內容");
        store.Write("短");

        // 草稿是整份覆寫的,不是追加 —— 舊內容的殘骸不能留在後面。
        Assert.Equal("短", store.Read());
    }

    [Theory]
    [InlineData("裸 CR\r也是換行\r第三行")]
    [InlineData("CRLF\r\n混\nLF\r裸CR")]
    public void Write_NormalizesEveryFlavourOfNewlineToCrlfOnDisk(string typed)
    {
        // Adaptive Cards 的多行輸入框送回來的換行是**裸 CR**(底下那個 WinUI TextBox 的行為)。
        // 原樣落地的話,Ctrl+O 用外部編輯器打開會看到擠成一行的一大塊字 ——
        // 而那條路正是這個功能拿來替代自動儲存的,不能壞。
        using var temp = new TempDirectory();
        var store = new ScratchpadStore(temp.Options);

        store.Write(typed);

        var raw = File.ReadAllText(store.FilePath);

        Assert.DoesNotContain('\r', raw.Replace("\r\n", string.Empty, StringComparison.Ordinal));
        Assert.DoesNotContain('\n', raw.Replace("\r\n", string.Empty, StringComparison.Ordinal));

        // 讀回來一律是 LF,而且行的內容一個字都沒少。
        Assert.Equal(
            typed.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'),
            store.Read());
    }

    [Fact]
    public void Write_EndsTheFileWithExactlyOneNewline()
    {
        using var temp = new TempDirectory();
        var store = new ScratchpadStore(temp.Options);

        store.Write("一行");

        Assert.Equal("一行\r\n", File.ReadAllText(store.FilePath));
    }

    [Fact]
    public void WriteThenRead_DoesNotGrowATrailingBlankLineEachRound()
    {
        // 檔尾那個換行是格式不是內容。Read 不拿掉的話,每存一次就多長一行 ——
        // NoteFile.StripTrailingNewline 存在的理由一模一樣。
        using var temp = new TempDirectory();
        var store = new ScratchpadStore(temp.Options);

        const string text = "第一行\n第二行";
        store.Write(text);

        for (var round = 0; round < 5; round++)
        {
            store.Write(store.Read());
        }

        Assert.Equal(text, store.Read());
    }

    [Fact]
    public void Write_EmptyTextLeavesAnEmptyFile()
    {
        using var temp = new TempDirectory();
        var store = new ScratchpadStore(temp.Options);

        store.Write(string.Empty);

        // 清空草稿之後不該留下一個孤零零的換行。
        Assert.Equal(string.Empty, File.ReadAllText(store.FilePath));
        Assert.Equal(string.Empty, store.Read());
    }

    [Fact]
    public void Write_LeavesNoTempFilesBehind()
    {
        using var temp = new TempDirectory();
        var store = new ScratchpadStore(temp.Options);

        store.Write("內容");
        store.Write("再一次");

        // .tmp 留在筆記資料夾裡會跟著 OneDrive 同步到別台機器上。
        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp"));
    }

    [Fact]
    public void Write_CreatesTheNotesDirectoryWhenMissing()
    {
        using var temp = new TempDirectory();
        var directory = Path.Combine(temp.Path, "還沒建出來");
        var store = new ScratchpadStore(new InklingOptions { NotesDirectory = directory });

        store.Write("第一次就要存得起來");

        Assert.True(Directory.Exists(directory));
        Assert.Equal("第一次就要存得起來", store.Read());
    }

    [Fact]
    public void EnsureFile_CreatesAnEmptyFile()
    {
        using var temp = new TempDirectory();
        var store = new ScratchpadStore(temp.Options);

        store.EnsureFile();

        // Ctrl+O 要開得起來,檔案就得真的存在。
        Assert.True(File.Exists(store.FilePath));
        Assert.Equal(string.Empty, store.Read());
    }

    [Fact]
    public void EnsureFile_NeverOverwritesExistingContent()
    {
        using var temp = new TempDirectory();
        var store = new ScratchpadStore(temp.Options);
        store.Write("使用者辛苦寫的草稿");

        store.EnsureFile();
        store.EnsureFile();

        // 每次打開隨手草稿都會叫一次 EnsureFile —— 這裡寫錯等於「打開就清空」。
        Assert.Equal("使用者辛苦寫的草稿", store.Read());
    }

    [Fact]
    public void FilePath_IsTheFixedNameInTheNotesRoot()
    {
        using var temp = new TempDirectory();

        Assert.Equal(
            Path.Combine(temp.Path, ScratchpadStore.FileName),
            new ScratchpadStore(temp.Options).FilePath);
    }

    [Theory]
    [InlineData("scratchpad.md", true)]
    [InlineData("Scratchpad.MD", true)]
    [InlineData("scratchpad.md.md", false)]
    [InlineData("我的筆記.md", false)]
    [InlineData("子資料夾/scratchpad.md", false)]
    [InlineData("子資料夾/更深/scratchpad.md", false)]
    public void IsScratchpad_OnlyMatchesTheOneInTheRoot(string relativePath, bool expected)
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, relativePath.Replace('/', Path.DirectorySeparatorChar));

        Assert.Equal(expected, ScratchpadStore.IsScratchpad(temp.Path, path));
    }
}
