namespace Notelet.Core.Tests;

/// <summary>
/// 可控制的時鐘。筆記的 id、檔名與時間戳都取決於「現在幾點」,
/// 沒有這個就沒辦法對它們寫確定性的斷言。
/// </summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;

    // 固定用 UTC,免得測試結果隨著跑測試那台機器的時區而變。
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

/// <summary>
/// 一次性的暫存資料夾,測完自動刪掉。
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "notelet-tests",
            Guid.NewGuid().ToString("n"));

        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public NoteletOptions Options => new() { NotesDirectory = Path };

    public string WriteFile(string relativePath, string content)
    {
        var full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // 測試收尾清不掉不該讓測試失敗。
        }
    }
}
