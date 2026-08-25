using Microsoft.CommandPalette.Extensions.Toolkit;
using Inkling.Core;
using Inkling.Pages;
using Xunit;

namespace Inkling.Tests;

/// <summary>
/// 清單項的**物件識別**。這件事在畫面上的名字是「刪掉一則之後,選取跳回最上面」。
///
/// CmdPal 用參考相等去查它自己的 view model 快取,再問一句「當下選中的那個還在不在
/// 新集合裡」——在就不動,不在就選第一列。所以每次重建清單都給一批全新的
/// <c>ListItem</c>,等於每次都宣告「整份清單換人了」。這裡釘住的就是
/// <see cref="NoteItemSlots"/> 的三條分配規則,以及兩個清單頁真的有走它。
///
/// 第三條(內容變了就給一列全新的)看起來像是把前兩條的好處丟掉,**但它是必要的**:
/// 就地改一個 CmdPal 已經建好 view model 的清單項,會把那一列渲染到使用者當下看的
/// 頁面上。見 <see cref="ChangedContent_GetsAFreshItem"/>。
///
/// 配套的另一半(<c>RaiseItemsChanged</c> 要帶 <see cref="CmdPalRefresh.KeepSelection"/>)
/// 這一層測不到 —— 那是 CmdPal 收到之後自己的判斷,只能在真機上驗,
/// 步驟寫在 docs/manual-test-checklist.md。
/// </summary>
public class ListItemIdentityTests
{
    [Fact]
    public void UnchangedList_KeepsEveryItemObject()
    {
        var slots = new NoteItemSlots();
        var notes = new[] { Note("A"), Note("B"), Note("C") };

        var first = slots.Assign(notes, Create, Apply);
        var second = slots.Assign(notes, Create, Apply);

        Assert.Same(first[0], second[0]);
        Assert.Same(first[1], second[1]);
        Assert.Same(first[2], second[2]);
    }

    [Fact]
    public void RemovingANote_HandsItsItemToTheNextOne()
    {
        // 這就是使用者按下 Ctrl+D 的那一刻:選取停在第二列上,而第二列被刪掉了。
        // 把 B 的項目物件交給 C,那個物件因此還在集合裡 —— CmdPal 的選取就留在原位置,
        // 顯示的是下一則。
        var slots = new NoteItemSlots();

        var first = slots.Assign([Note("A"), Note("B"), Note("C")], Create, Apply);
        var second = slots.Assign([Note("A"), Note("C")], Create, Apply);

        Assert.Same(first[0], second[0]);
        Assert.Same(first[1], second[1]);
        Assert.Equal("C", second[1].Title);
    }

    [Fact]
    public void RemovingTheLastNote_HandsItsItemBackwards()
    {
        // 刪掉最後一列時沒有「下一則」,選取該落在新的最後一列上。
        var slots = new NoteItemSlots();

        var first = slots.Assign([Note("A"), Note("B"), Note("C")], Create, Apply);
        var second = slots.Assign([Note("A"), Note("B")], Create, Apply);

        Assert.Same(first[0], second[0]);
        Assert.Same(first[2], second[1]);
        Assert.Equal("B", second[1].Title);
    }

    [Fact]
    public void AddingANoteAtTheTop_LeavesTheOthersOnTheirOwnItems()
    {
        // 別台機器同步下來一則、或使用者在別的地方新增一則。使用者正看著的那一列
        // **不可以**換人 —— 這是身分語意,跟刪除那條位置語意是分開的兩條規則。
        var slots = new NoteItemSlots();

        var first = slots.Assign([Note("B"), Note("C")], Create, Apply);
        var second = slots.Assign([Note("A"), Note("B"), Note("C")], Create, Apply);

        Assert.NotSame(first[0], second[0]);
        Assert.Same(first[0], second[1]);
        Assert.Same(first[1], second[2]);
    }

    [Fact]
    public void ReorderingNotes_KeepsEachNoteOnItsOwnItem()
    {
        // 編輯一則會把它推到最前面(清單按更新時間排序)。其餘每一則都該待在自己的物件上,
        // CmdPal 那邊就只是一次 Move。
        var slots = new NoteItemSlots();

        var first = slots.Assign([Note("A"), Note("B"), Note("C")], Create, Apply);
        var second = slots.Assign([Note("C"), Note("A"), Note("B")], Create, Apply);

        Assert.Same(first[2], second[0]);
        Assert.Same(first[0], second[1]);
        Assert.Same(first[1], second[2]);
    }

    [Fact]
    public void RemovingSeveralNotes_NeverGivesTwoRowsTheSameItem()
    {
        // 批次刪除與同步都會一次少掉好幾則。讓不出去的槽要當孤兒丟掉 ——
        // 同一個物件出現在兩列上,CmdPal 的 diff 會直接錯亂。
        var slots = new NoteItemSlots();

        slots.Assign([Note("A"), Note("B"), Note("C"), Note("D"), Note("E")], Create, Apply);
        var second = slots.Assign([Note("A"), Note("E")], Create, Apply);

        Assert.Equal(second.Length, second.Distinct().Count());
    }

    [Fact]
    public void RemovingSeveralNotes_DoesNotStealFromASurvivor()
    {
        // 往前讓槽會搶走一個**沒被刪**的項目的位置。只少一則時那是對的(被搶的是
        // 使用者剛刪掉那列的前一列),一次少好幾則就不成立 —— A 沒被刪,它必須留在自己的
        // 物件上,否則選著 A 的人會被丟回第一列。
        var slots = new NoteItemSlots();

        var first = slots.Assign([Note("A"), Note("B"), Note("C"), Note("D"), Note("E")], Create, Apply);
        var second = slots.Assign([Note("A"), Note("E")], Create, Apply);

        Assert.Same(first[0], second[0]);
    }

    [Fact]
    public void ChangedContent_GetsAFreshItem()
    {
        // **這一條是拿畫面壞掉換來的,別為了「省一次配置」把它改成就地更新。**
        // 就地改一個 CmdPal 已經建好 view model 的清單項,CmdPal 會立刻把那一列
        // 渲染出來 —— 而「內容變了」最常見的來源就是使用者正在編輯那一則,人不在清單頁上。
        // 實測畫面:編輯表單旁邊多出一塊筆記預覽,底部工具列變成清單那一列的「預覽 / 編輯」。
        var slots = new NoteItemSlots();

        var first = slots.Assign([Note("A"), Note("B")], Create, Apply);
        var second = slots.Assign([Note("A"), Note("B", body: "改過了")], Create, Apply);

        Assert.Same(first[0], second[0]);
        Assert.NotSame(first[1], second[1]);
    }

    [Fact]
    public void UnchangedNotes_AreNotReboundAtAll()
    {
        // 沿用的那一列連一個屬性都不該被設 —— 每一次設值都是一趟跨進程通知,
        // 而且正是上面那條會打壞畫面的路。
        var slots = new NoteItemSlots();
        var notes = new[] { Note("A"), Note("B") };
        var rebinds = 0;

        slots.Assign(notes, Create, Apply);
        slots.Assign(notes, Create, (item, note) =>
        {
            rebinds++;
            Apply(item, note);
        });

        Assert.Equal(0, rebinds);
    }

    [Fact]
    public void EmptyingTheList_ThenRefilling_StartsFresh()
    {
        var slots = new NoteItemSlots();

        var first = slots.Assign([Note("A"), Note("B")], Create, Apply);
        slots.Assign([], Create, Apply);
        var third = slots.Assign([Note("A"), Note("B")], Create, Apply);

        Assert.NotSame(first[0], third[0]);
        Assert.NotSame(first[1], third[1]);
    }

    [Fact]
    public void ListPage_KeepsTheItemObjectOfTheRowBelowADeletedNote()
    {
        // 頁面層級:清單頁真的有走 NoteItemSlots,而不是每次 new 一批。
        var repository = new FakeNoteRepository();
        for (var i = 0; i < 3; i++)
        {
            repository.Add($"筆記 {i}");
        }

        var settings = new FakeSettings();
        using var page = ListPage(repository, settings);

        var before = page.GetItems();

        repository.Delete(repository.GetAll()[1]);

        var after = page.GetItems();

        Assert.Equal(2, after.Length);
        Assert.Same(before[0], after[0]);
        Assert.Same(before[1], after[1]);
    }

    [Fact]
    public void ListPage_KeepsTheItemObjectsWhenANoteArrives()
    {
        var repository = new FakeNoteRepository();
        for (var i = 0; i < 2; i++)
        {
            repository.Add($"筆記 {i}");
        }

        var settings = new FakeSettings();
        using var page = ListPage(repository, settings);

        var before = page.GetItems();

        repository.Create("新來的", string.Empty);

        var after = page.GetItems();
        var carried = after.Where(item => ReferenceEquals(item, before[0]) || ReferenceEquals(item, before[1]));

        Assert.Equal(3, after.Length);
        Assert.Equal(2, carried.Count());
    }

    [Fact]
    public void DeletePage_KeepsTheItemObjectOfTheRowBelowADeletedNote()
    {
        // 這一頁最需要:使用者是進來一則一則刪的,而第一列是「刪除全部」。
        var repository = new FakeNoteRepository();
        for (var i = 0; i < 3; i++)
        {
            repository.Add($"筆記 {i}");
        }

        using var page = new DeleteNotesPage(repository, Options(), new FakeSettings());

        var before = page.GetItems();

        // [0] 是「刪除全部」,筆記從 [1] 開始。
        repository.Delete(repository.GetAll()[1]);

        var after = page.GetItems();

        Assert.Same(before[0], after[0]);
        Assert.Same(before[2], after[2]);
    }

    [Fact]
    public void DeletePage_KeepsTheDeleteEverythingRowAcrossRebuilds()
    {
        // 那一列的標題帶著數字,每刪一則都要改 —— 但**物件本身不能換**,
        // 否則選取停在它上面時會被踢走。
        var repository = new FakeNoteRepository();
        for (var i = 0; i < 3; i++)
        {
            repository.Add($"筆記 {i}");
        }

        using var page = new DeleteNotesPage(repository, Options(), new FakeSettings());

        var before = page.GetItems()[0];
        var title = before.Title;

        repository.Delete(repository.GetAll()[0]);

        var after = page.GetItems()[0];

        Assert.Same(before, after);
        Assert.NotEqual(title, after.Title);
    }

    private static Note Note(string name, string body = "") => new()
    {
        Id = name,
        Title = name,
        Body = body,
        Created = DateTimeOffset.UnixEpoch,
        Updated = DateTimeOffset.UnixEpoch,
        FilePath = $@"C:\notes\{name}.md",
    };

    private static ListItem Create(Note note) => new(new NoOpCommand()) { Title = note.Title };

    private static void Apply(ListItem item, Note note) => item.Title = note.Title;

    private static InklingOptions Options() => new() { NotesDirectory = @"C:\notes", MaxResults = 200 };

    private static NoteListPage ListPage(FakeNoteRepository repository, FakeSettings settings) =>
        new(
            repository,
            Options(),
            new QuickCapturePage(repository, settings, settings, settings),
            new NewNotePage(repository),
            settings);
}
