using Inkling.Core;
using Inkling.Pages;
using Inkling.Properties;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Xunit;

namespace Inkling.Tests;

/// <summary>
/// 底部工具列那兩顆按鈕是**位置鍵**:主命令(<c>Enter</c>)與次要命令
/// (<c>Ctrl+Enter</c>)坐的是誰只看命令排序，跟命令自己的 <c>RequestedShortcut</c> 無關。
/// 也就是說「在陣列裡插一項」就足以換掉 Enter 的意思，而且不會有任何編譯或執行期訊號。
///
/// 踩過兩次:切換原始文字排到第二個把複製內文擠掉;預覽頁的 Ctrl+Enter 一度落在
/// 複製內文上，跟另外兩頁不同義。這一組測試把順序釘住。
/// </summary>
public class PageCommandOrderTests
{
    [Fact]
    public void PreviewPage_LeadsWithEditThenDone()
    {
        // 在清單裡找到某一則才進得來，下一步多半是改它 —— 所以 Enter 是編輯。
        var (repository, settings) = Fixture();
        var note = repository.Add("標題", "內文");

        var page = new NotePreviewPage(repository, note, settings);

        Assert.Equal(Resources.CommandEdit, TitleOf(page.Commands[0]));
        Assert.Equal(Resources.CommandDone, TitleOf(page.Commands[1]));
    }

    [Fact]
    public void CapturedNotePage_LeadsWithDoneThenEdit()
    {
        // 剛打完字看一眼，下一步是收工 —— 跟預覽頁**刻意相反**。
        // 考證見 docs/design-notes.md〈兩個位置鍵〉。
        var (repository, settings) = Fixture();
        var page = new CapturedNotePage(repository, new QuickCaptureDraft("標題", "內文"), settings);

        page.GetContent();

        Assert.Equal(Resources.CommandDone, TitleOf(page.Commands[0]));
        Assert.Equal(Resources.CommandEdit, TitleOf(page.Commands[1]));
    }

    [Fact]
    public void TheTwoContentPagesAreDeliberatelyOpposite()
    {
        // 兩頁前兩項相反是刻意的，不是漂移。這一條把「刻意」寫成可執行的斷言:
        // 哪天有人「順手統一」，這裡會紅，而不是等使用者發現 Enter 變成關面板。
        var (repository, settings) = Fixture();
        var note = repository.Add("標題", "內文");

        var preview = new NotePreviewPage(repository, note, settings);
        var captured = new CapturedNotePage(repository, new QuickCaptureDraft("另一則", "內文"), settings);
        captured.GetContent();

        Assert.Equal(TitleOf(preview.Commands[0]), TitleOf(captured.Commands[1]));
        Assert.Equal(TitleOf(preview.Commands[1]), TitleOf(captured.Commands[0]));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NoPageBindsTheSameShortcutTwice(bool captured)
    {
        // 同一個選單裡撞鍵**不會報錯** —— CmdPal 用 TryAdd，第二個被靜靜丟掉，
        // 只在它自己的 log 留一行 warning。所以只能靠這裡擋。
        var (repository, settings) = Fixture();
        var note = repository.Add("標題", "內文");

        IContextItem[] commands;
        if (captured)
        {
            var page = new CapturedNotePage(repository, new QuickCaptureDraft("標題", "內文"), settings);
            page.GetContent();
            commands = page.Commands;
        }
        else
        {
            var page = new NotePreviewPage(repository, note, settings);
            commands = page.Commands;
        }

        var chords = commands
            .OfType<CommandContextItem>()
            .Select(c => c.RequestedShortcut)
            .Where(k => k.Vkey != 0)
            .Select(Describe)
            .ToList();

        Assert.Equal(chords.Count, chords.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EditPage_BindsOpenExternalToCtrlO()
    {
        // 這個動作原本只坐在 Enter 上(焦點在單行標題欄時很容易誤觸，而卡片上
        // 未儲存的修改會靜靜消失)，連個快速鍵都沒有。
        var (repository, _) = Fixture();
        var note = repository.Add("標題", "內文");

        var page = new NoteEditPage(repository, note);

        var external = page.Commands
            .OfType<CommandContextItem>()
            .Single(c => c.Title == Resources.EditOpenExternalTitle);

        Assert.Equal(Describe(Shortcuts.OpenExternal), Describe(external.RequestedShortcut));
    }

    [Fact]
    public void EditPage_EnterDoesNotLeaveThePage()
    {
        // **這一頁的 Enter 特別危險:卡片上壓著使用者還沒儲存的修改。**
        // 焦點在單行的標題欄時按 Enter 是很自然的「送出」手勢，而這裡曾經只掛一個
        // 「在預設編輯器開啟」—— 於是 Enter 就是它:跳去外部編輯器、面板被 Dismiss 收掉，
        // 打過的字全部消失(實機驗過)。
        //
        // 釘住的是「Commands[0] 是無害的那一顆」。誰都可以在後面加東西，
        // 但第一個位置一動，Enter 的意思就變了，而那不會有任何編譯或執行期訊號。
        var (repository, _) = Fixture();
        var note = repository.Add("標題", "內文");

        var page = new NoteEditPage(repository, note);

        Assert.Equal(Resources.EditKeepEditingTitle, TitleOf(page.Commands[0]));

        // 而且它真的什麼都不做:回傳 KeepOpen，面板留著。
        var command = Assert.IsType<CommandContextItem>(page.Commands[0]).Command;
        var result = Assert.IsAssignableFrom<IInvokableCommand>(command).Invoke(null!);

        Assert.Equal(CommandResultKind.KeepOpen, result.Kind);
    }

    private static (FakeNoteRepository Repository, FakeSettings Settings) Fixture() =>
        (new FakeNoteRepository(), new FakeSettings());

    private static string? TitleOf(IContextItem item) =>
        item is CommandContextItem command ? command.Title : null;

    /// <summary>攤成字串再比，失敗訊息才看得懂是哪一組鍵。</summary>
    private static string Describe(KeyChord chord) =>
        chord.Vkey == 0
            ? "(none)"
            : FormattableString.Invariant($"{chord.Modifiers}+{chord.Vkey}");
}
