using Inkling.Commands;
using Inkling.Properties;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Xunit;

namespace Inkling.Tests;

/// <summary>
/// 複製內文的回饋:**一則 toast,而且面板留著**。
///
/// 這一組釘的是一個**靜默**的失敗模式。`CommandResult.ShowToast("訊息")` 那個字串簡寫
/// 吃的是 <see cref="ToastArgs"/> 的預設 <c>Result</c>,而那個預設是 <c>Dismiss</c> ——
/// 也就是說「發個提示」的最順手寫法會**附帶把面板收掉**,而且編譯、測試、部署全部不會有
/// 任何訊號,只有在真機上按下去才看得到整個面板消失。toolkit 的 <see cref="CopyTextCommand"/>
/// 預設就是這一種,這個類別當初包一層的理由之一就是它。
///
/// 這個 repo 為此繞過一大圈:曾經有一條硬規則寫著「想留在畫面上就一個 toast 都不能發」,
/// 理由是「toast 會搶焦點,主視窗一失焦就自我隱藏」。**那條規則是假的**
/// (2026-08-23 量掉:toast 視窗是 <c>WS_EX_TOOLWINDOW | WS_DISABLED</c>,拿不到前景),
/// 但它長出過清單頁的 <c>FlashTag</c> 標籤機制,以及兩個 <c>ContentPage</c> **完全靜默**的
/// 成功路徑。考證見 <c>docs/design-notes.md</c>〈toast 不會把面板關掉〉。
///
/// 所以這裡比對的不是「有沒有發提示」,是 <c>ToastArgs.Result.Kind</c> 那一個列舉值。
/// </summary>
public class CopyFeedbackTests
{
    [Fact]
    public void EmptyBodyReportsWithoutClosingThePalette()
    {
        // 空內文這條路**在碰剪貼簿之前就返回**,所以測得到而不必動到跑測試那台機器的剪貼簿
        // (CI 上也沒有剪貼簿可用)。成功那條路的收尾走同一個私有 helper,
        // 這裡釘住的 Result 對兩條都成立。
        var command = new CopyNoteBodyCommand(string.Empty, "標題");

        var result = Assert.IsType<CommandResult>(command.Invoke());

        Assert.Equal(CommandResultKind.ShowToast, result.Kind);

        var args = Assert.IsType<ToastArgs>(result.Args);

        // **這兩行是整組測試的重點。** 漏掉 Result 就是 Dismiss,而畫面上的差別是
        // 「複製一次面板關一次」—— 沒有任何自動化訊號會告訴你。
        Assert.NotNull(args.Result);
        Assert.Equal(CommandResultKind.KeepOpen, args.Result.Kind);

        // 沒東西進剪貼簿,所以這條路**不提標題**:「已複製:X」講的是「X 進了剪貼簿」。
        Assert.Equal(Resources.CopyNoBody, args.Message);
        Assert.DoesNotContain("標題", args.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSuccessMessageCarriesTheNoteTitle()
    {
        // toast 沒有位置感 —— 清單頁以前靠「標籤掛在哪一列」講「複製到的是哪一則」,
        // 換成 toast 之後那個資訊只能寫在訊息裡。標題掉了,三個畫面就都只剩「已複製」,
        // 而使用者手上有兩百則筆記。
        Assert.Contains("{0}", Resources.CopyDone, StringComparison.Ordinal);

        var message = Strings.Format(Resources.CopyDone, "會議記錄");

        Assert.Contains("會議記錄", message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTitleCanBeSwappedAfterConstruction()
    {
        // 預覽頁與記下並預覽頁把同一個實例留著重複用,每次取內容都重查一次筆記
        // (NotePreviewContent.Reload)。使用者剛在編輯頁改過標題的話,只換 Text
        // 會讓 toast 講出舊標題 —— 那比不講更糟,它主動說了一句假話。
        var command = new CopyNoteBodyCommand("內文", "舊標題")
        {
            Text = "新內文",
            NoteTitle = "新標題",
        };

        Assert.Equal("新內文", command.Text);
        Assert.Equal("新標題", command.NoteTitle);
    }
}
