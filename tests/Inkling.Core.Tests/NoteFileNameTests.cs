using Xunit;

namespace Inkling.Core.Tests;

public class NoteFileNameTests
{
    [Theory]
    [InlineData("買咖啡機的想法", "買咖啡機的想法")]
    [InlineData("Hello World", "Hello-World")]
    [InlineData("a//b\\c:d*e?f", "a-b-c-d-e-f")]
    [InlineData("  前後有空白  ", "前後有空白")]
    [InlineData("多----重---連字號", "多-重-連字號")]
    [InlineData("中英mixed混合123", "中英mixed混合123")]
    public void Slug_KeepsLettersAndDigits_ReplacesEverythingElse(string title, string expected)
    {
        Assert.Equal(expected, NoteFileName.Slug(title));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!???")]
    [InlineData("///")]
    public void Slug_FallsBackWhenNothingUsableRemains(string title)
    {
        Assert.Equal("note", NoteFileName.Slug(title));
    }

    [Fact]
    public void Slug_KeepsCjkOutsideTheBasicPlane()
    {
        // 擴充區 B 的漢字(U+20005 / U+2000B)在 UTF-16 裡是代理對,而
        // char.IsLetterOrDigit 對單一代理字元一律回 false —— 逐 char 的舊實作
        // 會把它們整串換成連字號,slug 只剩 "abc"。人名用字與異體字大量落在這一區,
        // 那正是「中日韓照樣留著」那句註解宣稱要保住的東西。
        //
        // 碼位寫成數字,不要貼字元、也不要用 UTF-16 逸出:文字處理工具會靜靜把它換掉,
        // 而輸入與預期值一起被換掉的話,這條測試照樣是綠的(CLAUDE.md 記過同一個坑)。
        var cjkB = char.ConvertFromUtf32(0x20005) + char.ConvertFromUtf32(0x2000B);

        Assert.Equal(cjkB + "abc", NoteFileName.Slug(cjkB + "abc"));
    }

    [Fact]
    public void Slug_TruncatesLongTitles()
    {
        var slug = NoteFileName.Slug(new string('a', 200));

        Assert.Equal(40, slug.Length);
    }

    [Fact]
    public void Slug_DoesNotSplitSurrogatePairs()
    {
        // 從代理對中間切開會留下落單的 high surrogate,那是無效的 UTF-16,
        // 轉碼時會變成 U+FFFD。
        //
        // 用擴充區 B 的**漢字**,不要用 emoji:emoji 不是字母,整串會先被換成連字號,
        // 截斷那一段根本走不到 —— 這條測試以前就是那樣,綠得毫無意義。
        //
        // 前面墊一個 ASCII 字元讓代理對錯位,截斷點才會正好落在某個字中間 ——
        // 這正是要驗的那條路徑。
        var title = "a" + string.Concat(Enumerable.Repeat(char.ConvertFromUtf32(0x20005), 40));

        var slug = NoteFileName.Slug(title);

        Assert.True(slug.Length <= 40);
        Assert.False(char.IsHighSurrogate(slug[^1]), "slug 結尾留下了落單的 high surrogate");

        // 最直接的判準:能不能無損地轉成 UTF-8 再轉回來。
        var roundTripped = System.Text.Encoding.UTF8.GetString(System.Text.Encoding.UTF8.GetBytes(slug));
        Assert.Equal(slug, roundTripped);
    }

    [Fact]
    public void CreateId_HasExpectedShape()
    {
        var id = NoteFileName.CreateId(new DateTimeOffset(2026, 8, 10, 14, 30, 52, TimeSpan.Zero));

        Assert.StartsWith("20260810-143052-", id, StringComparison.Ordinal);
        Assert.Equal("20260810-143052-".Length + 4, id.Length);
    }

    [Fact]
    public void CreateId_IsUniqueWithinTheSameSecond()
    {
        // 同一秒內連續記兩則想法一點都不罕見,光靠時間戳會撞。
        var timestamp = new DateTimeOffset(2026, 8, 10, 14, 30, 52, TimeSpan.Zero);

        var ids = Enumerable.Range(0, 200).Select(_ => NoteFileName.CreateId(timestamp)).ToHashSet(StringComparer.Ordinal);

        // 4 位十六進位有 65536 種可能,200 次抽樣撞到超過幾次就代表隨機性有問題。
        Assert.True(ids.Count >= 195, $"200 次只產生了 {ids.Count} 個相異 id");
    }

    [Fact]
    public void CreateUniquePath_AppendsCounterOnCollision()
    {
        using var temp = new TempDirectory();
        var timestamp = new DateTimeOffset(2026, 8, 10, 14, 30, 52, TimeSpan.Zero);

        var first = NoteFileName.CreateUniquePath(temp.Path, timestamp, "撞名");
        File.WriteAllText(first, "x");

        var second = NoteFileName.CreateUniquePath(temp.Path, timestamp, "撞名");

        Assert.Equal("20260810-143052-撞名.md", Path.GetFileName(first));
        Assert.Equal("20260810-143052-撞名-2.md", Path.GetFileName(second));
    }
}
