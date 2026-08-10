using System.Diagnostics;
using Xunit;

namespace Notelet.Core.Tests;

/// <summary>
/// 效能的防退化警戒線,不是 benchmark。
///
/// 存在的理由是需求裡那條「擴展不能拖慢 Command Palette」。搜索是每按一個鍵就跑一次的
/// 路徑,一旦有人在裡面塞了正規表示式或每次都重讀磁碟,這裡會先叫。
/// 門檻刻意抓得寬鬆,免得在慢一點的機器上變成隨機失敗的測試。
/// </summary>
public class PerformanceTests
{
    private const int NoteCount = 1000;

    [Fact]
    public void Search_OverOneThousandNotes_StaysWellUnderAKeystroke()
    {
        var notes = Enumerable.Range(0, NoteCount)
            .Select(i => new Note
            {
                Id = i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Title = $"筆記 {i} 的標題",
                Body = string.Join('\n', Enumerable.Repeat($"這是第 {i} 則筆記的內文,寫得長一點才有意義。", 20)),
                Created = DateTimeOffset.UnixEpoch.AddMinutes(i),
                Updated = DateTimeOffset.UnixEpoch.AddMinutes(i),
                FilePath = $"{i}.md",
            })
            .ToArray();

        // 先跑一次暖身,不要把 JIT 的時間算進去。
        NoteSearch.Filter(notes, "暖身");

        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < 10; i++)
        {
            NoteSearch.Filter(notes, "筆記 內文");
        }

        stopwatch.Stop();

        var perQuery = stopwatch.Elapsed.TotalMilliseconds / 10;
        Assert.True(perQuery < 50, $"{NoteCount} 則筆記的一次搜索花了 {perQuery:F1} ms,超過 50 ms 的警戒線");
    }

    [Fact]
    public void Load_OneThousandNotes_CompletesInReasonableTime()
    {
        using var temp = new TempDirectory();

        for (var i = 0; i < NoteCount; i++)
        {
            temp.WriteFile(
                $"note-{i}.md",
                $"---\nid: id-{i}\ntitle: 筆記 {i}\ncreated: 2026-08-10T12:00:00+00:00\nupdated: 2026-08-10T12:00:00+00:00\ntags: []\n---\n\n內文 {i}\n");
        }

        using var repository = new FileSystemNoteRepository(temp.Options);

        var stopwatch = Stopwatch.StartNew();
        var notes = repository.GetAll();
        stopwatch.Stop();

        Assert.Equal(NoteCount, notes.Count);
        Assert.True(
            stopwatch.Elapsed.TotalMilliseconds < 3000,
            $"載入 {NoteCount} 則筆記花了 {stopwatch.Elapsed.TotalMilliseconds:F0} ms,超過 3000 ms 的警戒線");
    }

    [Fact]
    public void GetAll_IsCachedAfterFirstLoad()
    {
        // 清單頁每按一個鍵就會問一次筆記,每次都重掃磁碟是絕對不行的。
        using var temp = new TempDirectory();

        for (var i = 0; i < 200; i++)
        {
            temp.WriteFile($"note-{i}.md", $"---\nid: id-{i}\ntitle: 筆記 {i}\n---\n\n內文");
        }

        using var repository = new FileSystemNoteRepository(temp.Options);
        repository.GetAll();

        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < 100; i++)
        {
            repository.GetAll();
        }

        stopwatch.Stop();

        Assert.True(
            stopwatch.Elapsed.TotalMilliseconds < 50,
            $"100 次快取讀取花了 {stopwatch.Elapsed.TotalMilliseconds:F0} ms —— 快取大概沒生效");
    }
}
