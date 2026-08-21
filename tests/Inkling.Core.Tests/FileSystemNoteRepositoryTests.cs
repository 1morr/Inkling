using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Xunit;

namespace Inkling.Core.Tests;

public class FileSystemNoteRepositoryTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    private static FileSystemNoteRepository CreateRepository(TempDirectory temp, out FixedTimeProvider clock)
    {
        clock = new FixedTimeProvider(Noon);
        return new FileSystemNoteRepository(temp.Options, clock);
    }

    [Fact]
    public void GetAll_ExternalFileStartingWithCodeFence_TitleComesFromCodeContent()
    {
        // 使用者現有的筆記就有以 ``` 開頭的;標題變成三個反引號、副標顯示圍欄行
        // 是實機確認過的缺陷。圍欄內的第一行內容才是有意義的標題與摘要。
        using var temp = new TempDirectory();
        temp.WriteFile("external-codefence.md", "```python\nimport os\n```");

        using var repository = CreateRepository(temp, out _);
        var note = Assert.Single(repository.GetAll());

        Assert.Equal("import os", note.Title);

        // 唯一一行內容已經被拿去當標題,副標就該留空,而不是重複一次。
        Assert.Equal(string.Empty, note.Summary);
    }

    [Fact]
    public void GetAll_ExternalFile_TitleAndSummaryAreNotTheSameLine()
    {
        // DeriveTitle 取第一行當標題,Summary 若也取第一行,清單上同一句話會出現兩次。
        using var temp = new TempDirectory();
        temp.WriteFile("external-plain.md", "# 外來標題\n\n這是別的工具寫的檔案。");

        using var repository = CreateRepository(temp, out _);
        var note = Assert.Single(repository.GetAll());

        Assert.Equal("外來標題", note.Title);
        Assert.Equal("這是別的工具寫的檔案。", note.Summary);
    }

    [Fact]
    [SupportedOSPlatform("windows")] // ACL 是 Windows-only API;這個 repo 本來就只跑 Windows
    public void GetAll_InaccessibleSubdirectory_StillReturnsOtherNotes()
    {
        // 筆記資料夾指到 Documents 之類的位置時,底下很可能有進不去的子目錄
        // (deny-read 的 junction、權限不對的資料夾)。一個進不去不該讓整份清單全滅。
        using var temp = new TempDirectory();
        temp.WriteFile("正常.md", "---\nid: ok\ntitle: 正常筆記\n---\n\n內文");
        var hidden = temp.WriteFile(
            Path.Combine("進不去", "藏起來.md"),
            "---\nid: hidden\ntitle: 藏起來\n---\n\n內文");

        // 只 deny 列目錄(ReadData),留著 ReadAttributes,Directory.Exists 才還是 true,
        // 這樣走的是「枚舉途中被拒」那條路,而不是「目錄不存在」。
        var subdir = new DirectoryInfo(Path.GetDirectoryName(hidden)!);
        var acl = subdir.GetAccessControl();
        using var identity = WindowsIdentity.GetCurrent();
        var deny = new FileSystemAccessRule(identity.Name, FileSystemRights.ReadData, AccessControlType.Deny);
        acl.AddAccessRule(deny);
        subdir.SetAccessControl(acl);

        try
        {
            using var repository = CreateRepository(temp, out _);
            var note = Assert.Single(repository.GetAll());

            Assert.Equal("正常筆記", note.Title);
        }
        finally
        {
            // 不把 Deny 拿掉的話,TempDirectory 收尾會刪不掉這個子目錄。
            acl.RemoveAccessRuleSpecific(deny);
            subdir.SetAccessControl(acl);
        }
    }

    [Fact]
    [SupportedOSPlatform("windows")] // ACL 是 Windows-only API;這個 repo 本來就只跑 Windows
    public void GetAll_UnreadableNotesDirectory_ReturnsEmptyInsteadOfThrowing()
    {
        // 設定頁允許填任意路徑,「整個資料夾列不出來」是使用者自己製造得出來的狀態。
        // 那該退化成空清單,而不是讓例外一路穿出頁面的 GetItems。
        using var temp = new TempDirectory();

        var dir = new DirectoryInfo(temp.Path);
        var acl = dir.GetAccessControl();
        using var identity = WindowsIdentity.GetCurrent();
        var deny = new FileSystemAccessRule(identity.Name, FileSystemRights.ReadData, AccessControlType.Deny);
        acl.AddAccessRule(deny);
        dir.SetAccessControl(acl);

        try
        {
            using var repository = CreateRepository(temp, out _);
            Assert.Empty(repository.GetAll());
        }
        finally
        {
            acl.RemoveAccessRuleSpecific(deny);
            dir.SetAccessControl(acl);
        }
    }

    [Fact]
    public async Task Changed_FiresAgain_AfterDirectoryIsDeletedAndRecreated()
    {
        // OneDrive 重新佈建、或使用者自己砍掉再建資料夾之後,舊 watcher 監看的是
        // 已經消失的目錄,永遠不會再發事件 —— 不拆掉的話,之後所有外部異動都無聲消失。
        using var temp = new TempDirectory();
        using var repository = CreateRepository(temp, out _);

        repository.GetAll(); // watcher 在這裡掛上

        Directory.Delete(temp.Path, recursive: true);
        repository.Invalidate();
        Assert.Empty(repository.GetAll()); // 目錄不在:回傳空,同時該把死掉的 watcher 拆掉

        Directory.CreateDirectory(temp.Path);
        repository.GetAll(); // 目錄回來了,這裡要重新掛上 watcher

        // 刪除那陣事件的去抖動通知可能還在排隊(去抖動是 250ms),等它過去再訂閱,
        // 免得把舊事件誤當成後面那個檔案觸發的。
        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        // 上限不能設 1。watcher 對同一次異動常常發不只一個事件,第二次 Release()
        // 會丟 SemaphoreFullException —— 那個例外在事件的回呼執行緒上,
        // 直接讓整個測試進程收掉,而不是讓這一條測試紅。
        using var fired = new SemaphoreSlim(0);
        repository.Changed += (_, _) => fired.Release();

        temp.WriteFile("later.md", "---\nid: later\ntitle: 後來新增\n---\n\n內文");

        Assert.True(
            await fired.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken),
            "資料夾刪除再重建之後,外部新增檔案沒有收到 Changed 事件");
    }

    [Fact]
    public void Create_IdAlreadyTaken_PicksAnotherId()
    {
        // 16-bit 後綴同一秒內有 1/65536 的碰撞率;撞了兩則筆記共用一個 id,
        // Update / Delete 會作用在錯的那則上。撞了就要重抽。
        using var temp = new TempDirectory();
        temp.WriteFile("既有.md", "---\nid: taken-id\ntitle: 既有\n---\n\n內文");

        var attempts = 0;
        string Generate(DateTimeOffset _)
        {
            attempts++;
            return attempts == 1 ? "taken-id" : "fresh-id";
        }

        using var repository = new FileSystemNoteRepository(
            temp.Options, new FixedTimeProvider(Noon), idGenerator: Generate);

        var note = repository.Create("新筆記", string.Empty);

        Assert.Equal(2, attempts);
        Assert.Equal("fresh-id", note.Id);
        Assert.Equal(2, repository.GetAll().Count);
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
        // 錨點是 front matter 的收尾分隔線(開頭那條前面沒有換行,不會誤中)。
        var onDisk = File.ReadAllText(original.FilePath)
            .Replace("\r\n---\r\n", "\r\ncssclass: reading\r\naliases:\r\n  - 別名\r\n---\r\n", StringComparison.Ordinal);
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
    public void Delete_RemovesTheFileAndTheNote()
    {
        using var temp = new TempDirectory();
        using var repository = CreateRepository(temp, out _);

        var note = repository.Create("要刪掉的", "內文");
        var other = repository.Create("留下來的", string.Empty);

        repository.Delete(note.Id);

        Assert.False(File.Exists(note.FilePath));
        Assert.Null(repository.GetById(note.Id));
        Assert.Equal([other.Id], repository.GetAll().Select(n => n.Id));
    }

    [Fact]
    public void Delete_UnknownId_Throws()
    {
        using var temp = new TempDirectory();
        using var repository = CreateRepository(temp, out _);

        Assert.Throws<NoteNotFoundException>(() => repository.Delete("不存在"));
    }

    [Fact]
    public void Delete_BumpsVersionSoCachesRefresh()
    {
        // 清單頁的項目快取以 Version 為鍵。刪完不動 Version 的話,畫面上那一則會留著,
        // 點下去才發現檔案已經不在了。
        using var temp = new TempDirectory();
        using var repository = CreateRepository(temp, out _);

        var note = repository.Create("要刪掉的", string.Empty);
        var before = repository.Version;

        repository.Delete(note.Id);

        Assert.NotEqual(before, repository.Version);
    }

    [Fact]
    public void Delete_GoesThroughTheInjectedDeleter()
    {
        // 正式跑起來這裡換成送資源回收筒的實作(UI 層的 RecycleBinFileDeleter),
        // 所以 repository 絕對不能自己呼叫 File.Delete。
        using var temp = new TempDirectory();

        var deleter = new RecordingFileDeleter();
        using var repository = new FileSystemNoteRepository(
            temp.Options, new FixedTimeProvider(DateTimeOffset.UtcNow), deleter);

        var note = repository.Create("要刪掉的", string.Empty);
        repository.Delete(note.Id);

        Assert.Equal([note.FilePath], deleter.Deleted);

        // 這個 deleter 什麼都沒做,檔案應該還在 —— 證明刪除確實只走它那條路。
        Assert.True(File.Exists(note.FilePath));
    }

    [Fact]
    public void DeleteMany_RemovesEverythingAndReportsTheCount()
    {
        using var temp = new TempDirectory();
        using var repository = CreateRepository(temp, out _);

        repository.Create("一", "a");
        repository.Create("二", "b");
        repository.Create("三", "c");

        var deleted = repository.DeleteMany(repository.GetAll());

        Assert.Equal(3, deleted);
        Assert.Empty(repository.GetAll());
        Assert.Empty(Directory.GetFiles(temp.Path, "*.md"));
    }

    [Fact]
    public void DeleteMany_OnlyTouchesTheNotesItWasGiven()
    {
        // 「只刪 Inkling 建立的」走的就是這條路:外來的 .md 一個都不能少。
        using var temp = new TempDirectory();
        temp.WriteFile("別人的/筆記.md", "# 不是 Inkling 寫的");

        using var repository = CreateRepository(temp, out _);
        repository.Create("Inkling 的一", string.Empty);
        repository.Create("Inkling 的二", string.Empty);

        var deleted = repository.DeleteMany(repository.GetAll().Where(n => !n.IsExternal));

        Assert.Equal(2, deleted);
        var left = Assert.Single(repository.GetAll());
        Assert.True(left.IsExternal);
        Assert.Equal("不是 Inkling 寫的", left.Title);
    }

    [Fact]
    public void DeleteMany_OnAnEmptyFolderIsANoOp()
    {
        using var temp = new TempDirectory();
        using var repository = CreateRepository(temp, out _);

        Assert.Equal(0, repository.DeleteMany(repository.GetAll()));
    }

    [Fact]
    public void DeleteMany_DeletingNothingDoesNotBumpVersion()
    {
        // 版本一動,正開著的頁面就會重建一次項目。什麼都沒刪掉還讓它重建是白做工。
        using var temp = new TempDirectory();
        using var repository = CreateRepository(temp, out _);

        repository.Create("留著", string.Empty);
        var before = repository.Version;

        Assert.Equal(0, repository.DeleteMany([]));
        Assert.Equal(before, repository.Version);
    }

    [Fact]
    public void DeleteMany_KeepsGoingWhenOneFileCannotBeDeleted()
    {
        // 一個檔案被鎖住不該讓其餘的留在原地 —— 使用者按下「刪除全部」就是要清空,
        // 半途中止只會留下一個說不清楚的狀態。
        using var temp = new TempDirectory();

        var deleter = new RecordingFileDeleter { FailOnPathContaining = "壞掉的" };
        using var repository = new FileSystemNoteRepository(
            temp.Options, new FixedTimeProvider(DateTimeOffset.UtcNow), deleter);

        repository.Create("好的一", string.Empty);
        repository.Create("壞掉的", string.Empty);
        repository.Create("好的二", string.Empty);

        var deleted = repository.DeleteMany(repository.GetAll());

        // 三則都試過,回報的是真正成功的那兩則。
        Assert.Equal(2, deleted);
        Assert.Equal(3, deleter.Attempted.Count);
    }

    private sealed class RecordingFileDeleter : IFileDeleter
    {
        public List<string> Deleted { get; } = [];

        public List<string> Attempted { get; } = [];

        /// <summary>路徑含有這段字的就丟 IOException,用來模擬「檔案被鎖住」。</summary>
        public string? FailOnPathContaining { get; init; }

        public void Delete(string path)
        {
            Attempted.Add(path);

            if (FailOnPathContaining is { } marker && path.Contains(marker, StringComparison.Ordinal))
            {
                throw new IOException($"模擬失敗:{path}");
            }

            Deleted.Add(path);
        }
    }

    [Fact]
    public void GetAll_MissingDirectory_ReturnsEmpty()
    {
        var options = new InklingOptions { NotesDirectory = Path.Combine(Path.GetTempPath(), "inkling-does-not-exist-" + Guid.NewGuid().ToString("n")) };
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
    public void GetAll_CountsFilesItCouldNotRead()
    {
        // 讀不出來的檔案會從清單上消失,而使用者不會知道為什麼 —— 清單頁靠這個數字
        // 多掛一列說明。以前這個數字算完沒有任何消費者,也沒有測試碰過遞增那條路。
        using var temp = new TempDirectory();
        temp.WriteFile("讀得到.md", "---\nid: ok-1\ntitle: 讀得到\n---\n\n內文");
        var locked = temp.WriteFile("被鎖住.md", "---\nid: locked-1\ntitle: 被鎖住\n---\n\n內文");

        // FileShare.None:別的程序連讀都不行,正是「被編輯器或同步程式佔著」的形狀。
        using (File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            using var repository = CreateRepository(temp, out _);

            var only = Assert.Single(repository.GetAll());
            Assert.Equal("讀得到", only.Title);
            Assert.Equal(1, repository.SkippedFileCount);
        }
    }

    [Fact]
    public void GetAll_MarksFilesNotWrittenByInklingAsExternal()
    {
        // 批次刪除靠這個旗標決定範圍,認錯就是刪掉別人的檔案。
        using var temp = new TempDirectory();
        temp.WriteFile("沒有 front matter.md", "# 外面來的");
        temp.WriteFile("有別人的 front matter.md", "---\ntitle: Obsidian 寫的\ntags: [a]\n---\n\n內文");

        using var repository = CreateRepository(temp, out _);
        repository.Create("Inkling 自己建的", string.Empty);

        var notes = repository.GetAll();

        Assert.Equal(3, notes.Count);
        Assert.False(Assert.Single(notes, n => n.Title == "Inkling 自己建的").IsExternal);
        Assert.True(Assert.Single(notes, n => n.Title == "外面來的").IsExternal);

        // front matter 有沒有不是重點,有沒有 Inkling 的 id 才是 —— 別的工具也會寫 front matter。
        Assert.True(Assert.Single(notes, n => n.Title == "Obsidian 寫的").IsExternal);
    }

    [Fact]
    public void Update_MakesAnExternalNoteOurs()
    {
        // 編輯外來檔案會替它補上 Inkling 的 front matter(含 id),那之後它就不算外來的了。
        // 這條記下來是因為它決定「只刪 Inkling 建立的」會不會把它掃進去 ——
        // 會,而且合理:那個檔案確實是我們寫過的。
        using var temp = new TempDirectory();
        temp.WriteFile("外來.md", "# 外面來的");

        using var repository = CreateRepository(temp, out _);
        var external = Assert.Single(repository.GetAll());
        Assert.True(external.IsExternal);

        repository.Update(external.Id, "改過標題", "改過內文");

        Assert.False(Assert.Single(repository.GetAll()).IsExternal);
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
    public void GetAll_ExcludesTheScratchpad()
    {
        // 隨手草稿存在筆記資料夾裡(才跟得上雲端同步),但它不是筆記 —— 沒有標題也沒有 id,
        // 列進來清單就會永遠多一列標題在跳動的半成品。
        using var temp = new TempDirectory();
        temp.WriteFile(ScratchpadStore.FileName, "還沒想清楚的東西");
        temp.WriteFile("真的筆記.md", "---\nid: n-1\ntitle: 真的筆記\n---\n\n內文");

        using var repository = CreateRepository(temp, out _);
        var note = Assert.Single(repository.GetAll());

        Assert.Equal("真的筆記", note.Title);

        // 它不是「壞到讀不出來的檔案」,是我們自己決定不列的 —— 混進這個數字會讓
        // 清單頁跳出「有幾個檔案讀不出來」的提示,而使用者根本沒有壞檔。
        Assert.Equal(0, repository.SkippedFileCount);
    }

    [Fact]
    public void GetAll_ScratchpadInASubdirectoryIsStillANote()
    {
        // 排除規則只認最上層那一個。子資料夾裡剛好同名的檔案是使用者自己的筆記,
        // 照常列出來 —— 規則要講得出口,不然就成了無聲吃掉檔案的黑魔法。
        using var temp = new TempDirectory();
        temp.WriteFile("專案A/" + ScratchpadStore.FileName, "# 這是一則筆記\n\n內文");

        using var repository = CreateRepository(temp, out _);
        var note = Assert.Single(repository.GetAll());

        Assert.Equal("這是一則筆記", note.Title);
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

        // 上限不能設 1。watcher 對同一次異動常常發不只一個事件,第二次 Release()
        // 會丟 SemaphoreFullException —— 那個例外在事件的回呼執行緒上,
        // 直接讓整個測試進程收掉,而不是讓這一條測試紅。
        using var fired = new SemaphoreSlim(0);
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
        // 第一次用 Inkling 的實際情境:筆記資料夾要等第一則筆記才會被建出來。
        // 先讀一次(空的)再新增,第二次讀必須看得到那則筆記。
        var directory = Path.Combine(Path.GetTempPath(), "inkling-tests", Guid.NewGuid().ToString("n"));
        var options = new InklingOptions { NotesDirectory = directory };

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
        var directory = Path.Combine(Path.GetTempPath(), "inkling-tests", Guid.NewGuid().ToString("n"));
        var options = new InklingOptions { NotesDirectory = directory };

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
    public async Task Version_DoesNotMoveASecondTimeFromOurOwnWrite()
    {
        // 自己的寫入在當下就 Invalidate 過了(丟快取、進版本、發 Changed)。
        // watcher 幾毫秒後為同一個檔案再發一次事件的話,同一次存檔會讓正開著的頁面
        // 重建兩遍,而第二遍還晚 250 ms(去抖動)才到 —— 畫面會多閃一下。
        // 這一條也是兩條 Version 測試偶發紅掉的成因。
        using var temp = new TempDirectory();
        using var repository = CreateRepository(temp, out _);

        // 先讀一次,資料夾監看是在這時候才掛上去的。
        repository.GetAll();

        // **一次存十則,不是一則。** 實測回音只有三成左右的機率出現(同一支測試在
        // 修正前跑八次紅三次)—— 只存一則的話這條測試有六成機率在壞掉的程式碼上變綠,
        // 那種守門等於沒有。十則把漏網率壓到 1% 以下,而且只等一次。
        var before = repository.Version;

        for (var i = 0; i < 10; i++)
        {
            repository.Create($"標題 {i}", "內文");
        }

        // 比去抖動的 250 ms 長,回音真的存在的話這段時間內一定到了。
        await Task.Delay(TimeSpan.FromMilliseconds(800), TestContext.Current.CancellationToken);

        Assert.Equal(before + 10, repository.Version);
    }

    [Fact]
    public async Task Version_StillMovesWhenTheSameFileIsTouchedFromOutsideLater()
    {
        // 上一條的反面:抑制是有時效的。過了那扇窗,同一個檔案被外部工具改動照樣要收到 ——
        // 否則「別台機器同步下來的修改」會在剛存過的那則筆記上永久失聯。
        using var temp = new TempDirectory();
        using var repository = CreateRepository(temp, out _);
        repository.GetAll();

        var note = repository.Create("標題", "內文");

        using var fired = new SemaphoreSlim(0);

        // 等抑制過期之後才開始聽,免得收到的是自己那一次的回音。
        await Task.Delay(TimeSpan.FromMilliseconds(900), TestContext.Current.CancellationToken);
        repository.Changed += (_, _) => fired.Release();

        File.WriteAllText(note.FilePath, "---\nid: outside-1\ntitle: 別人改的\n---\n\n內文");

        Assert.True(
            await fired.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken),
            "抑制過期之後,同一個檔案的外部異動仍然沒有觸發 Changed");
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
