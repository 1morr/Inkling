using Inkling.Core;
using Inkling.Pages;
using Inkling.Properties;
using Xunit;

namespace Inkling.Tests;

/// <summary>
/// 三個清單頁的項目快取。規則只有一條 —— **鍵要帶 repository 的 Version,
/// 以及每一個會影響內容的設定值** —— 而漏掉的症狀全部是「事件收到了、拿到的還是舊結果」,
/// 沒有例外、沒有錯誤訊息。最出名的一次是「筆記明明存好了，清單卻說還沒有」。
/// </summary>
public class ListPageCacheTests
{
    [Fact]
    public void ListPage_ReflectsANewNote()
    {
        var (repository, settings) = Fixture();
        using var page = ListPage(repository, settings);

        Assert.Single(page.GetItems());

        repository.Create("第二則", string.Empty);

        Assert.Equal(2, page.GetItems().Length);
    }

    [Fact]
    public void ListPage_TruncatesAtMaxResultsAndSaysSo()
    {
        // MaxResults 是 design-notes〈效能上的規矩〉列的承諾:每一項都要跨進程封送，
        // 而清單每按一個鍵就重建一次。被截掉時最後一列要明講還有幾則。
        var (repository, settings) = Fixture(notes: 0);
        for (var i = 0; i < 7; i++)
        {
            repository.Add($"筆記 {i}");
        }

        using var page = ListPage(repository, settings, maxResults: 5);

        var items = page.GetItems();

        Assert.Equal(6, items.Length);
        Assert.Equal(Strings.Format(Resources.ListPageMoreResults, 2), items[^1].Title);
    }

    [Fact]
    public void QuickCapturePage_RebuildsWhenTheSeparatorChanges()
    {
        // 同一句話換個分隔符，切出來的標題與內文完全不同 —— 少了它，設定改了等於沒改。
        var (repository, settings) = Fixture(notes: 0);
        using var page = new QuickCapturePage(repository, settings, settings, settings);
        page.UpdateSearchText(string.Empty, "標題;;內文");

        var before = page.GetItems()[0].Title;

        settings.CaptureSeparator = "##";

        Assert.NotEqual(before, page.GetItems()[0].Title);
    }

    [Fact]
    public void QuickCapturePage_RebuildsWhenTheRepositoryChanges()
    {
        // 底下那幾列是「標題相近的既有筆記」，它們跟著 repository 走。
        var (repository, settings) = Fixture(notes: 0);
        using var page = new QuickCapturePage(repository, settings, settings, settings);
        page.UpdateSearchText(string.Empty, "咖啡");

        var before = page.GetItems().Length;

        repository.Create("咖啡機的想法", string.Empty);

        Assert.True(
            page.GetItems().Length > before,
            "存了一則標題相近的筆記，快速記下頁底下卻沒有多一列 —— 快取鍵漏了 Version?");
    }

    [Fact]
    public void DeletePage_ReflectsADeletedNote()
    {
        var (repository, settings) = Fixture(notes: 3);
        using var page = new DeleteNotesPage(repository, Options(), settings);

        var before = page.GetItems().Length;

        repository.Delete(repository.GetAll()[0]);

        Assert.Equal(before - 1, page.GetItems().Length);
    }

    private static (FakeNoteRepository Repository, FakeSettings Settings) Fixture(int notes = 1)
    {
        var repository = new FakeNoteRepository();
        for (var i = 0; i < notes; i++)
        {
            repository.Add($"筆記 {i}");
        }

        return (repository, new FakeSettings());
    }

    private static InklingOptions Options(int maxResults = 200) =>
        new() { NotesDirectory = @"C:\notes", MaxResults = maxResults };

    private static NoteListPage ListPage(
        FakeNoteRepository repository,
        FakeSettings settings,
        int maxResults = 200) =>
        new(
            repository,
            Options(maxResults),
            new QuickCapturePage(repository, settings, settings, settings),
            new NewNotePage(repository),
            settings);
}
