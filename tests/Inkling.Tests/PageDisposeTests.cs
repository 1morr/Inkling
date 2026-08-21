using Inkling.Core;
using Inkling.Pages;
using Xunit;

namespace Inkling.Tests;

/// <summary>
/// 長壽頁面訂閱了誰,<c>Dispose</c> 就要退掉誰。
///
/// 換筆記資料夾時 provider 會整組重建並釋放舊的一組。退訂漏掉的話,同一個事件之後會有
/// 好幾個**已經沒有人在看**的頁面在聽 —— 改幾次資料夾就累積幾份,每一份收到通知都會
/// 回頭呼叫跨進程的 CmdPal。這件事從畫面上完全看不出來,只能從這裡擋。
/// </summary>
public class PageDisposeTests
{
    [Fact]
    public void ListPage_UnsubscribesEverything()
    {
        var repository = new FakeNoteRepository();
        var settings = new FakeSettings();

        var page = new NoteListPage(
            repository,
            Options,
            new QuickCapturePage(repository, settings, settings, settings),
            new NewNotePage(repository),
            settings);

        Assert.True(repository.ChangedSubscriberCount > 0, "清單頁根本沒有訂閱 repository.Changed?");
        var showSourceBefore = settings.ShowSourceSubscriberCount;

        page.Dispose();

        Assert.Equal(0, repository.ChangedSubscriberCount - CapturePageSubscriptions);
        Assert.Equal(showSourceBefore - 1, settings.ShowSourceSubscriberCount);
    }

    [Fact]
    public void QuickCapturePage_UnsubscribesEverything()
    {
        var repository = new FakeNoteRepository();
        var settings = new FakeSettings();

        var page = new QuickCapturePage(repository, settings, settings, settings);

        Assert.Equal(1, repository.ChangedSubscriberCount);
        Assert.Equal(1, settings.SeparatorSubscriberCount);
        Assert.Equal(1, settings.PreviewSubscriberCount);

        page.Dispose();

        Assert.Equal(0, repository.ChangedSubscriberCount);
        Assert.Equal(0, settings.SeparatorSubscriberCount);
        Assert.Equal(0, settings.PreviewSubscriberCount);
    }

    [Fact]
    public void DeletePage_UnsubscribesEverything()
    {
        var repository = new FakeNoteRepository();
        var settings = new FakeSettings();

        var page = new DeleteNotesPage(repository, Options, settings);

        Assert.Equal(1, repository.ChangedSubscriberCount);

        page.Dispose();

        Assert.Equal(0, repository.ChangedSubscriberCount);
        Assert.Equal(0, settings.ShowSourceSubscriberCount);
    }

    [Fact]
    public void ShortLivedPagesDoNotSubscribeAtAll()
    {
        // 預覽頁與記下並預覽頁是清單裡**每個項目各建一個**的短命物件。它們訂閱等於
        // 一路累積死掉的訂閱者,所以那兩頁改成在 GetContent() 當下讀一次設定。
        // 這一條擋的是「哪天有人順手把它們也接上事件」。
        var repository = new FakeNoteRepository();
        var settings = new FakeSettings();
        var note = repository.Add("標題", "內文");

        _ = new NotePreviewPage(repository, note, settings);
        _ = new CapturedNotePage(repository, new QuickCaptureDraft("標題", "內文"), settings);

        Assert.Equal(0, repository.ChangedSubscriberCount);
        Assert.Equal(0, settings.ShowSourceSubscriberCount);
    }

    /// <summary>清單頁自己會建一個快速記下頁當 EmptyContent,那一份也訂了 repository。</summary>
    private const int CapturePageSubscriptions = 1;

    private static InklingOptions Options => new() { NotesDirectory = @"C:\notes" };
}
