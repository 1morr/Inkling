using Xunit;

namespace Inkling.Core.Tests;

public class NoteSearchTests
{
    private static Note MakeNote(string title, string body = "", int hoursOld = 0) => new()
    {
        Id = title,
        Title = title,
        Body = body,
        Created = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero).AddHours(-hoursOld),
        Updated = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero).AddHours(-hoursOld),
        FilePath = title + ".md",
    };

    [Fact]
    public void EmptyQuery_ReturnsEverythingNewestFirst()
    {
        var notes = new[] { MakeNote("舊", hoursOld: 5), MakeNote("新", hoursOld: 0), MakeNote("中", hoursOld: 2) };

        var result = NoteSearch.Filter(notes, string.Empty);

        Assert.Equal(new[] { "新", "中", "舊" }, result.Select(n => n.Title));
    }

    [Fact]
    public void WhitespaceQuery_IsTreatedAsEmpty()
    {
        var notes = new[] { MakeNote("一"), MakeNote("二") };

        Assert.Equal(2, NoteSearch.Filter(notes, "   ").Count);
    }

    [Fact]
    public void MatchesTitleAndBody()
    {
        var notes = new[]
        {
            MakeNote("咖啡機"),
            MakeNote("完全無關", body: "內文裡提到咖啡機"),
            MakeNote("沒有關鍵字", body: "也沒有"),
        };

        var result = NoteSearch.Filter(notes, "咖啡機");

        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, n => n.Title == "沒有關鍵字");
    }

    [Fact]
    public void TitleMatchesOutrankBodyMatches()
    {
        var notes = new[]
        {
            MakeNote("內文命中", body: "咖啡"),
            MakeNote("咖啡在標題"),
        };

        var result = NoteSearch.Filter(notes, "咖啡");

        Assert.Equal("咖啡在標題", result[0].Title);
    }

    [Fact]
    public void TitlePrefixOutranksTitleContains()
    {
        var notes = new[]
        {
            MakeNote("想買咖啡機"),
            MakeNote("咖啡機比較"),
        };

        var result = NoteSearch.Filter(notes, "咖啡機");

        Assert.Equal("咖啡機比較", result[0].Title);
    }

    [Fact]
    public void MultipleTerms_AllMustMatch()
    {
        var notes = new[]
        {
            MakeNote("咖啡機推薦", body: "預算五千"),
            MakeNote("咖啡豆", body: "沒有提到價格"),
        };

        Assert.Single(NoteSearch.Filter(notes, "咖啡 預算"));
        Assert.Equal("咖啡機推薦", NoteSearch.Filter(notes, "咖啡 預算")[0].Title);
    }

    [Fact]
    public void MultipleTerms_CanMatchAcrossTitleAndBody()
    {
        var notes = new[] { MakeNote("咖啡機", body: "預算五千") };

        Assert.Single(NoteSearch.Filter(notes, "咖啡 五千"));
    }

    [Fact]
    public void IsCaseInsensitive()
    {
        var notes = new[] { MakeNote("Docker Compose 筆記") };

        Assert.Single(NoteSearch.Filter(notes, "docker"));
        Assert.Single(NoteSearch.Filter(notes, "COMPOSE"));
    }

    [Fact]
    public void SplitsOnIdeographicSpace()
    {
        // 中文輸入法常常打出全形空白,使用者不會意識到自己打的不是半形。
        var notes = new[]
        {
            MakeNote("咖啡機", body: "預算五千"),
            MakeNote("咖啡豆"),
        };

        Assert.Single(NoteSearch.Filter(notes, "咖啡　預算"));
    }

    [Fact]
    public void NoMatches_ReturnsEmpty()
    {
        var notes = new[] { MakeNote("一"), MakeNote("二") };

        Assert.Empty(NoteSearch.Filter(notes, "完全找不到的關鍵字"));
    }

    [Fact]
    public void EqualScores_FallBackToNewestFirst()
    {
        var notes = new[]
        {
            MakeNote("咖啡 A", hoursOld: 5),
            MakeNote("咖啡 B", hoursOld: 1),
        };

        var result = NoteSearch.Filter(notes, "咖啡");

        Assert.Equal("咖啡 B", result[0].Title);
    }
}
