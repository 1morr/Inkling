using System.Text;
using Xunit;

namespace Inkling.Core.Tests;

/// <summary>
/// 首次公開發佈前那一輪總體檢裡，在真機上重現過的資料完整性缺陷。
///
/// 每一條都對應一個實際會弄丟使用者資料的情境，所以測試釘的是**行為**而不是實作:
/// 「編輯乙那一列不會寫進甲的檔案」「Big5 的檔案不會被改寫成亂碼」這種。
/// </summary>
[Collection(DiskBoundTests.Name)]
public class DataIntegrityTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    private static FileSystemNoteRepository CreateRepository(TempDirectory temp) =>
        new(temp.Options, new FixedTimeProvider(Noon));

    /// <summary>
    /// OneDrive 的衝突副本是**整檔複製**,front matter 一模一樣 —— 同一個 id 因此會
    /// 出現在兩個檔案上。以前 Update / Delete 都用 id 解析目標，兩列都指向同一份檔案:
    /// 選第二列按編輯，存檔寫進第一份，第二份一個位元組都沒動。
    /// </summary>
    [Fact]
    public void Update_TwoFilesShareAnId_WritesToTheOneYouPicked()
    {
        using var temp = new TempDirectory();

        temp.WriteFile("會議記錄.md", FrontMatter("20260822-120000-abcd", "本機版") + "我是本機寫的");
        temp.WriteFile(
            "會議記錄-DESKTOP-A1B2C3.md",
            FrontMatter("20260822-120000-abcd", "衝突副本") + "我是別台機器寫的");

        using var repository = CreateRepository(temp);

        var copy = repository.GetAll().Single(n => n.Title == "衝突副本");
        repository.Update(copy, "改過的衝突副本", "改過的內文");

        var reloaded = repository.GetAll();

        Assert.Equal("本機版", reloaded.Single(n => n.FilePath.EndsWith("會議記錄.md", StringComparison.Ordinal)).Title);
        Assert.Equal("我是本機寫的", reloaded.Single(n => n.FilePath.EndsWith("會議記錄.md", StringComparison.Ordinal)).Body);
        Assert.Equal("改過的衝突副本", reloaded.Single(n => n.FilePath.Contains("DESKTOP", StringComparison.Ordinal)).Title);
    }

    /// <inheritdoc cref="Update_TwoFilesShareAnId_WritesToTheOneYouPicked" />
    [Fact]
    public void Delete_TwoFilesShareAnId_RemovesTheOneYouPicked()
    {
        using var temp = new TempDirectory();

        var mine = temp.WriteFile("筆記.md", FrontMatter("20260822-120000-abcd", "本機版") + "甲");
        var conflict = temp.WriteFile("筆記-DESKTOP.md", FrontMatter("20260822-120000-abcd", "衝突副本") + "乙");

        using var repository = CreateRepository(temp);

        repository.Delete(repository.GetAll().Single(n => n.Title == "衝突副本"));

        Assert.True(File.Exists(mine));
        Assert.False(File.Exists(conflict));
    }

    /// <summary>
    /// 兩列長得幾乎一樣，不標出來使用者根本不會發現多了一份 —— 清單頁靠這個旗標打標籤。
    /// </summary>
    [Fact]
    public void GetAll_DuplicateId_BothSidesAreFlagged()
    {
        using var temp = new TempDirectory();

        temp.WriteFile("a.md", FrontMatter("20260822-120000-abcd", "甲") + "內文甲");
        temp.WriteFile("b.md", FrontMatter("20260822-120000-abcd", "乙") + "內文乙");
        temp.WriteFile("c.md", FrontMatter("20260822-130000-ef01", "丙") + "內文丙");

        using var repository = CreateRepository(temp);
        var notes = repository.GetAll();

        Assert.True(notes.Single(n => n.Title == "甲").HasDuplicateId);
        Assert.True(notes.Single(n => n.Title == "乙").HasDuplicateId);
        Assert.False(notes.Single(n => n.Title == "丙").HasDuplicateId);
    }

    /// <summary>
    /// 非 UTF-8 的外來檔案以前會被讀成一串 U+FFFD，而使用者一旦在 Inkling 裡編輯它，
    /// 那些 � 就被寫回檔案 —— 原始位元組永久消失，沒有備份、沒有提示。
    /// 現在整個檔案讀不出來，改由「有 N 個檔案讀不出來」那一列講。
    /// </summary>
    [Fact]
    public void GetAll_NonUtf8File_IsSkippedInsteadOfMangled()
    {
        using var temp = new TempDirectory();

        var path = Path.Combine(temp.Path, "big5.md");

        // 「# 」加上 Big5 的「好」(0xA6 0x6E)。直接寫位元組而不是用 Encoding.GetEncoding(950):
        // 那個編碼要另外掛 System.Text.Encoding.CodePages，而這裡要的只是
        // 「一段不是合法 UTF-8 的位元組」。0xA6 當開頭在 UTF-8 裡是非法的續行位元組。
        var before = new byte[] { 0x23, 0x20, 0xA6, 0x6E };
        File.WriteAllBytes(path, before);

        using var repository = CreateRepository(temp);

        Assert.Empty(repository.GetAll());
        Assert.Equal(1, repository.SkippedFileCount);

        // 最重要的一條:檔案原封不動留在資料夾裡。
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    /// <summary>
    /// 有 BOM 的檔案照樣讀得到 —— 嚴格解碼只是「沒有 BOM 時的假設」,
    /// <c>StreamReader</c> 仍然先照 BOM 判編碼。這條擋的是「順手把 UTF-16 使用者也弄丟」。
    /// </summary>
    [Theory]
    [InlineData("utf8-bom.md", true, false)]
    [InlineData("utf16.md", false, true)]
    public void GetAll_FilesWithABom_StillLoad(string name, bool utf8, bool unicode)
    {
        using var temp = new TempDirectory();

        var encoding = utf8
            ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
            : unicode ? new UnicodeEncoding(bigEndian: false, byteOrderMark: true) : (Encoding)Encoding.UTF8;

        File.WriteAllText(Path.Combine(temp.Path, name), "# 有 BOM 的筆記", encoding);

        using var repository = CreateRepository(temp);

        Assert.Equal("有 BOM 的筆記", Assert.Single(repository.GetAll()).Title);
        Assert.Equal(0, repository.SkippedFileCount);
    }

    /// <summary>
    /// <c>id:</c> 在 Obsidian / Zettelkasten / Hugo 生態裡極常見。判準要是「有沒有 id」,
    /// 「只刪 Inkling 建立的」那顆按鈕就會刪掉使用者自己的 vault 檔，
    /// 而畫面上那句「保留 N 則不是 Inkling 建立的」是假的。
    /// </summary>
    [Theory]
    [InlineData("202401051200", true)]
    [InlineData("my-post-slug", true)]
    [InlineData("20260822-120000-ABCD", true)]
    [InlineData("20260822-120000-abcd", false)]
    public void GetAll_ForeignFrontMatterId_CountsAsExternal(string id, bool external)
    {
        using var temp = new TempDirectory();
        temp.WriteFile("note.md", FrontMatter(id, "標題") + "內文");

        using var repository = CreateRepository(temp);

        Assert.Equal(external, Assert.Single(repository.GetAll()).IsExternal);
    }

    /// <summary>
    /// 讀不懂的日期以前會被靜靜丟掉、改用檔案系統時間，而且下一次編輯就把原字串永久覆蓋。
    /// 原始那一行既不在認得的欄位裡，也進不了 <see cref="Note.ExtraFrontMatter"/> ——
    /// 它在 switch 分支裡就被消化掉了。
    /// </summary>
    [Theory]
    [InlineData("2024-01-05 (approx)")]
    [InlineData("05/01/2024")]
    [InlineData("去年冬天")]
    public void Update_UnparseableCreated_KeepsTheOriginalLine(string original)
    {
        using var temp = new TempDirectory();

        var path = temp.WriteFile(
            "note.md",
            $"---\nid: 20260822-120000-abcd\ntitle: 標題\ncreated: {original}\nupdated: 2026-08-22T12:00:00+00:00\n---\n\n內文\n");

        using var repository = CreateRepository(temp);
        repository.Update(Assert.Single(repository.GetAll()), "新標題", "新內文");

        Assert.Contains($"created: {original}", File.ReadAllText(path), StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>05/01/2024</c> 在 InvariantCulture 下會被讀成 **5 月 1 日**，而那串字在多數
    /// 非美式工具裡是 1 月 5 日。猜錯不會有任何徵兆，所以乾脆不猜。
    /// </summary>
    [Fact]
    public void Parse_AmbiguousDate_IsNotGuessed()
    {
        var parsed = NoteFile.Parse("---\ncreated: 05/01/2024\n---\n\n內文\n");

        Assert.Null(parsed.Created);
        Assert.Equal("created: 05/01/2024", parsed.CreatedRaw);
    }

    /// <summary>ISO 8601 起手式的照樣讀得懂，這條擋的是「保守過頭連自己寫的都不認」。</summary>
    [Theory]
    [InlineData("2024-01-05")]
    [InlineData("2024-01-05T10:30:00+08:00")]
    [InlineData("2024-01-05T10:30:00Z")]
    public void Parse_Iso8601Date_IsUnderstood(string value)
    {
        var parsed = NoteFile.Parse($"---\ncreated: {value}\n---\n\n內文\n");

        Assert.NotNull(parsed.Created);
        Assert.Null(parsed.CreatedRaw);
    }

    /// <summary>
    /// 摘要與推導標題的 120 字截斷是裸索引切割，第 120 個位置落在代理對中間時
    /// (emoji、擴充區漢字)尾端會留下落單的 high surrogate，畫面上是 �。
    /// 檔名那條路早就修過而且有測試，摘要這條漏了。
    /// </summary>
    [Fact]
    public void Summary_TruncationDoesNotSplitSurrogatePairs()
    {
        // 第 120 個 UTF-16 字元剛好是「🙂」的前半:119 個 ASCII + 一個代理對。
        var line = new string('a', 119) + "🙂尾巴";

        var note = NoteWithBody("標題", line);

        Assert.DoesNotContain('\uFFFD', note.Summary);
        Assert.False(char.IsSurrogate(note.Summary[^2]), "截斷處留下了落單的代理字元");
        Assert.EndsWith("…", note.Summary, StringComparison.Ordinal);
    }

    /// <inheritdoc cref="Summary_TruncationDoesNotSplitSurrogatePairs" />
    [Fact]
    public void DerivedTitle_TruncationDoesNotSplitSurrogatePairs()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("外來.md", new string('a', 119) + "🙂尾巴");

        using var repository = CreateRepository(temp);
        var title = Assert.Single(repository.GetAll()).Title;

        Assert.False(char.IsSurrogate(title[^1]), "截斷處留下了落單的代理字元");
    }

    /// <summary>
    /// <b>同一個截斷的第三個消費者曾經自己裸切一次。</b>
    ///
    /// 推導標題走 <c>NoteBody.Truncate</c>(代理對會退一格),而預覽判斷「內文是不是
    /// 已經以標題開頭」卻自己寫了 <c>first[..120]</c> —— 兩邊對同一行字算出不同的結果，
    /// 於是外來檔案的預覽在內文上面又補了一個一模一樣的 H1。
    ///
    /// 這正是 <c>NoteBody</c> 這個型別存在的理由(三個消費者只留一份實作),
    /// 而漂掉的是最晚加進來的那一個。
    /// </summary>
    [Fact]
    public void Preview_DoesNotRepeatADerivedTitleThatWasTruncatedAtASurrogatePair()
    {
        using var temp = new TempDirectory();

        // 跟上面兩條同一個形狀:第 120 個 UTF-16 字元剛好是「🙂」的前半。
        temp.WriteFile("外來.md", new string('a', 119) + "🙂尾巴");

        using var repository = CreateRepository(temp);
        var note = Assert.Single(repository.GetAll());

        // 標題就是內文的開頭(截斷過),所以預覽不該再補一行 H1。
        Assert.DoesNotContain("# ", NotePreview.Render(note), StringComparison.Ordinal);
    }

    private static Note NoteWithBody(string title, string body) => new()
    {
        Id = "20260822-120000-abcd",
        Title = title,
        Body = body,
        Created = Noon,
        Updated = Noon,
        FilePath = @"C:\notes\x.md",
    };

    private static string FrontMatter(string id, string title) =>
        $"---\nid: {id}\ntitle: {title}\ncreated: 2026-08-22T12:00:00+00:00\nupdated: 2026-08-22T12:00:00+00:00\n---\n\n";
}
