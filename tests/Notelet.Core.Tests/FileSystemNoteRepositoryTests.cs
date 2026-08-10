using Xunit;

namespace Notelet.Core.Tests;

public class FileSystemNoteRepositoryTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    private static FileSystemNoteRepository CreateRepository(TempDirectory temp, out FixedTimeProvider clock)
    {
        clock = new FixedTimeProvider(Noon);
        return new FileSystemNoteRepository(temp.Options, clock);
    }

    [Fact]
    public void Create_WritesFileAndReturnsNote()
    {
        using var temp = new TempDirectory();
        using var repository = CreateRepository(temp, out _);

        var note = repository.Create("買咖啡機的想法", "先查手沖跟義式的差別。");

        Assert.True(File.Exists(note.FilePath));
        Assert.Equal("買咖啡機的想法", note.Title);
        Assert.Equal(Noon, note.Created);
        Assert.Equal(Noon, note.Updated);
        Assert.Equal("20260810-120000-買咖啡機的想法.md", Path.GetFileName(note.FilePath));
    }

    [Fact]
    public void Create_ThenGetAll_RoundTripsThroughDisk()
    {
        using var temp = new TempDirectory();
        using var repository = CreateRepository(temp, out _);

        var created = repository.Create("標題", "內文");
        var loaded = Assert.Single(repository.GetAll());

        Assert.Equal(created.Id, loaded.Id);
        Assert.Equal(created.Title, loaded.Title);
        Assert.Equal(created.Body, loaded.Body);
        Assert.Equal(created.Created, loaded.Created);
    }

    [Fact]
    public void Create_QuickCaptureWithEmptyBody()
    {
        // 最常見的一種筆記:只有一句話,沒有內文。
        using var temp = new TempDirectory();
        using var repository = CreateRepository(temp, out _);

        repository.Create("突然想到的事", string.Empty);
        var loaded = Assert.Single(repository.GetAll());

        Assert.Equal("突然想到的事", loaded.Title);
        Assert.Equal(string.Empty, loaded.Body);
    }

    [Fact]
    public void Update_PreservesIdCreatedAndUnknownFrontMatter()
    {
        using var temp = new TempDirectory();
        using var repository = CreateRepository(temp, out var clock);

        var original = repository.Create("原標題", "原內文");

        // 模擬使用者在別的編輯器(例如 Obsidian)手動加了欄位。
        var onDisk = File.ReadAllText(original.FilePath)
            .Replace("tags: []", "tags: []\r\ncssclass: reading\r\naliases:\r\n  - 別名", StringComparison.Ordinal);
        File.WriteAllText(original.FilePath, onDisk);
        repository.Invalidate();

        clock.Now = Noon.AddHours(3);
        var updated = repository.Update(original.Id, "新標題", "新內文");

        Assert.Equal(original.Id, updated.Id);
        Assert.Equal(original.Created, updated.Created);
        Assert.Equal(Noon.AddHours(3), updated.Updated);

        var reloaded = Assert.Single(repository.GetAll());
        Assert.Equal("新標題", reloaded.Title);
        Assert.Equal("新內文", reloaded.Body);
        Assert.Contains("cssclass: reading", reloaded.ExtraFrontMatter);
        Assert.Contains("aliases:", reloaded.ExtraFrontMatter);
        Assert.Contains("  - 別名", reloaded.ExtraFrontMatter);
    }

    [Fact]
    public void Update_DoesNotRenameTheFile()
    {
        // 改標題就重新命名檔案,在雲端同步資料夾裡是製造重複檔與衝突檔的頭號原因。
        using var temp = new TempDirectory();
        using var repository = CreateRepository(temp, out _);

        var original = repository.Create("原標題", string.Empty);
        var updated = repository.Update(original.Id, "完全不一樣的標題", string.Empty);

        Assert.Equal(original.FilePath, updated.FilePath);
        Assert.True(File.Exists(original.FilePath));
        Assert.Single(Directory.GetFiles(temp.Path, "*.md"));
    }

    [Fact]
    public void Update_UnknownId_Throws()
    {
        using var temp = new TempDirectory();
        using var repository = CreateRepository(temp, out _);

        Assert.Throws<NoteNotFoundException>(() => repository.Update("不存在", "t", "b"));
    }

    [Fact]
    public void GetAll_MissingDirectory_ReturnsEmpty()
    {
        var options = new NoteletOptions { NotesDirectory = Path.Combine(Path.GetTempPath(), "notelet-does-not-exist-" + Guid.NewGuid().ToString("n")) };
        using var repository = new FileSystemNoteRepository(options);

        Assert.Empty(repository.GetAll());
    }

    [Fact]
    public void GetAll_SortsByUpdatedDescending()
    {
        using var temp = new TempDirectory();
        using var repository = CreateRepository(temp, out var clock);

        repository.Create("最舊", string.Empty);
        clock.Now = Noon.AddHours(1);
        repository.Create("中間", string.Empty);
        clock.Now = Noon.AddHours(2);
        repository.Create("最新", string.Empty);

        var titles = repository.GetAll().Select(n => n.Title).ToArray();

        Assert.Equal(new[] { "最新", "中間", "最舊" }, titles);
    }

    [Fact]
    public void GetAll_IncludesPlainMarkdownDroppedInByOtherTools()
    {
        // 使用者把既有的 .md 丟進資料夾。它沒有 front matter,但照樣要出現在清單裡,
        // 而不是無聲消失。
        using var temp = new TempDirectory();
        temp.WriteFile("外來筆記.md", "# 外面來的標題\n\n一些內文。");

        using var repository = CreateRepository(temp, out _);
        var note = Assert.Single(repository.GetAll());

        Assert.Equal("外面來的標題", note.Title);
        Assert.NotEmpty(note.Id);
        Assert.Equal(0, repository.SkippedFileCount);
    }

    [Fact]
    public void GetAll_DerivedIdIsStableAcrossReloads()
    {
        // 外來檔案的 id 是從路徑推導出來的。不穩定的話,預覽頁與編輯頁
        // 在重新載入之後就會找不到同一則筆記。
        using var temp = new TempDirectory();
        temp.WriteFile("外來筆記.md", "沒有 front matter");

        using var repository = CreateRepository(temp, out _);
        var first = Assert.Single(repository.GetAll()).Id;

        repository.Invalidate();
        var second = Assert.Single(repository.GetAll()).Id;

        Assert.Equal(first, second);
    }

    [Fact]
    public void GetAll_FindsNotesInSubdirectories()
    {
        // 使用者用檔案總管把筆記分門別類是很自然的事。只掃頂層的話那些筆記會無聲消失。
        using var temp = new TempDirectory();
        temp.WriteFile("專案A/想法.md", "---\nid: sub-1\ntitle: 子資料夾裡的筆記\n---\n\n內文");

        using var repository = CreateRepository(temp, out _);
        var note = Assert.Single(repository.GetAll());

        Assert.Equal("子資料夾裡的筆記", note.Title);
    }

    [Fact]
    public void GetAll_IgnoresNonMarkdownFiles()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("readme.txt", "不是筆記");
        temp.WriteFile("圖片.png", "也不是筆記");
        temp.WriteFile("真的筆記.md", "---\nid: a\ntitle: 真的筆記\n---\n\n");

        using var repository = CreateRepository(temp, out _);

        Assert.Single(repository.GetAll());
    }

    [Fact]
    public void GetAll_TolerateGarbledFile()
    {
        // 半寫入或亂碼的檔案不該讓整個清單掛掉。
        using var temp = new TempDirectory();
        temp.WriteFile("壞掉.md", "---\n這行不是 key value\n沒有收尾的 front matter");
        temp.WriteFile("正常.md", "---\nid: ok\ntitle: 正常\n---\n\n內文");

        using var repository = CreateRepository(temp, out _);
        var notes = repository.GetAll();

        Assert.Equal(2, notes.Count);
        Assert.Contains(notes, n => n.Title == "正常");
    }

    [Fact]
    public void Invalidate_PicksUpExternalEdits()
    {
        // 這是「別台機器經 OneDrive 同步下來」與「使用者用別的編輯器改了檔案」
        // 這兩種情況的模擬。
        using var temp = new TempDirectory();
        using var repository = CreateRepository(temp, out _);

        var note = repository.Create("原標題", string.Empty);
        Assert.Equal("原標題", Assert.Single(repository.GetAll()).Title);

        File.WriteAllText(note.FilePath, File.ReadAllText(note.FilePath).Replace("原標題", "外部改過的標題", StringComparison.Ordinal));
        repository.Invalidate();

        Assert.Equal("外部改過的標題", Assert.Single(repository.GetAll()).Title);
    }

    [Fact]
    public void Create_TwiceInTheSameSecond_ProducesTwoFiles()
    {
        using var temp = new TempDirectory();
        using var repository = CreateRepository(temp, out _);

        repository.Create("同名", string.Empty);
        repository.Create("同名", string.Empty);

        Assert.Equal(2, Directory.GetFiles(temp.Path, "*.md").Length);
        Assert.Equal(2, repository.GetAll().Count);
    }

    [Fact]
    public async Task Changed_FiresWhenAFileAppearsFromOutside()
    {
        // 這條就是多端同步在 UI 上的體現:別台機器記下的想法經 OneDrive 同步到這個
        // 資料夾時,清單頁要能自己更新,不必手動重新整理。
        using var temp = new TempDirectory();
        using var repository = CreateRepository(temp, out _);

        // 先讀一次,資料夾監看是在這時候才掛上去的。
        repository.GetAll();

        using var fired = new SemaphoreSlim(0, 1);
        repository.Changed += (_, _) => fired.Release();

        temp.WriteFile("從別台機器同步過來.md", "---\nid: remote-1\ntitle: 遠端筆記\n---\n\n內文");

        Assert.True(
            await fired.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken),
            "檔案從外部新增之後 5 秒內沒有收到 Changed 事件");

        Assert.Contains(repository.GetAll(), n => n.Title == "遠端筆記");
    }

    [Fact]
    public void Changed_FiresSynchronouslyOnOurOwnWrites()
    {
        // 使用者剛按下儲存,畫面就該跟上 —— 這一條不能走去抖動的 250 毫秒。
        // (去抖動是給 OneDrive 那種爆發式外部寫入用的。)
        using var temp = new TempDirectory();
        using var repository = CreateRepository(temp, out _);
        repository.GetAll();

        var created = 0;
        repository.Changed += (_, _) => Interlocked.Increment(ref created);

        var note = repository.Create("標題", string.Empty);
        Assert.True(created > 0, "Create 沒有立刻觸發 Changed");

        var beforeUpdate = created;
        repository.Update(note.Id, "新標題", string.Empty);
        Assert.True(created > beforeUpdate, "Update 沒有立刻觸發 Changed");
    }

    [Fact]
    public async Task Changed_IsDebouncedAcrossABurstOfWrites()
    {
        // OneDrive 同步是一陣爆發式的寫入。每個檔案都通知一次的話,
        // 清單頁會在同步期間被重建幾十次。
        using var temp = new TempDirectory();
        using var repository = CreateRepository(temp, out _);
        repository.GetAll();

        var count = 0;
        repository.Changed += (_, _) => Interlocked.Increment(ref count);

        for (var i = 0; i < 20; i++)
        {
            temp.WriteFile($"burst-{i}.md", $"---\nid: burst-{i}\ntitle: 第 {i} 則\n---\n\n");
        }

        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.True(count is > 0 and <= 3, $"20 次寫入觸發了 {count} 次 Changed,去抖動沒有生效");
        Assert.Equal(20, repository.GetAll().Count);
    }

    [Fact]
    public void GetAll_ReflectsCreate_WhenDirectoryDidNotExistInitially()
    {
        // 第一次用 Notelet 的實際情境:筆記資料夾要等第一則筆記才會被建出來。
        // 先讀一次(空的)再新增,第二次讀必須看得到那則筆記。
        var directory = Path.Combine(Path.GetTempPath(), "notelet-tests", Guid.NewGuid().ToString("n"));
        var options = new NoteletOptions { NotesDirectory = directory };

        try
        {
            using var repository = new FileSystemNoteRepository(options, new FixedTimeProvider(Noon));

            Assert.Empty(repository.GetAll());

            repository.Create("第一則想法", string.Empty);

            Assert.Single(repository.GetAll());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void GetAll_DoesNotCacheEmptyResult_WhileDirectoryIsMissing()
    {
        // 資料夾不存在時把空結果快取起來,之後資料夾出現(別台機器同步下來、
        // 或使用者自己建的)就永遠不會被發現 —— 而那時候還沒有 watcher 能通知我們。
        var directory = Path.Combine(Path.GetTempPath(), "notelet-tests", Guid.NewGuid().ToString("n"));
        var options = new NoteletOptions { NotesDirectory = directory };

        try
        {
            using var repository = new FileSystemNoteRepository(options);

            Assert.Empty(repository.GetAll());

            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "外面建的.md"), "---\nid: x\ntitle: 外面建的\n---\n\n");

            Assert.Single(repository.GetAll());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Version_ChangesWheneverContentMayHaveChanged()
    {
        // UI 層靠這個號碼判斷自己的項目快取還新不新。少了它,清單頁會一直拿著
        // 舊的結果 —— 這正是「筆記存好了但清單顯示還沒有任何筆記」的成因。
        using var temp = new TempDirectory();
        using var repository = CreateRepository(temp, out _);

        var initial = repository.Version;

        var note = repository.Create("標題", string.Empty);
        var afterCreate = repository.Version;
        Assert.NotEqual(initial, afterCreate);

        repository.Update(note.Id, "新標題", string.Empty);
        var afterUpdate = repository.Version;
        Assert.NotEqual(afterCreate, afterUpdate);

        repository.Invalidate();
        Assert.NotEqual(afterUpdate, repository.Version);
    }

    [Fact]
    public void Version_IsStableWhenNothingChanges()
    {
        using var temp = new TempDirectory();
        using var repository = CreateRepository(temp, out _);

        repository.Create("標題", string.Empty);
        repository.GetAll();

        var version = repository.Version;

        repository.GetAll();
        repository.GetAll();

        Assert.Equal(version, repository.Version);
    }

    [Fact]
    public void WriteAtomic_LeavesNoTempFilesBehind()
    {
        using var temp = new TempDirectory();
        using var repository = CreateRepository(temp, out _);

        var note = repository.Create("標題", "內文");
        repository.Update(note.Id, "新標題", "新內文");

        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp"));
    }
}
