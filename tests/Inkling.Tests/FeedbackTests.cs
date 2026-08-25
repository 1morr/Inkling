using Inkling.Commands;
using Inkling.Properties;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Xunit;

namespace Inkling.Tests;

/// <summary>
/// <see cref="Feedback"/> 那條規則的守門:**通道由面板去留決定**。
///
/// 這一組釘的是兩個**靜默**的失敗模式，兩個都真的發生過而且撐了很久沒被發現:
///
/// <list type="number">
/// <item><b>InfoBar 配導覽 = 訊息一個字都不會出現。</b> 那個 InfoBar 綁在當下這一頁的
/// view model 上，<c>GoHome</c> / <c>Dismiss</c> 會連同它一起拆掉。檔案存好了、
/// 程式碼跑到了、沒有例外 —— 看起來就像「本來就沒有提示」
/// (新增筆記表單、設定頁存檔各中過一次)。</item>
/// <item><b><c>CommandResult.ShowToast("字串")</c> 附帶收面板。</b> 它吃的是
/// <see cref="ToastArgs"/> 的預設 <c>Result</c>，而那個預設是 <c>Dismiss</c>。
/// 「只想發個提示」的最順手寫法會把使用者踢出去(刪除失敗三條路中過)。</item>
/// </list>
///
/// 兩個都編譯得過、跑得動、不丟例外 —— 沒有任何自動化訊號會告訴你。所以規則寫成三個
/// 方法，把「訊息」與「收尾」綁成同一個呼叫，那些組合就建構不出來;這裡比對的是
/// 每個方法實際回傳的 <c>Kind</c>。
/// </summary>
public class FeedbackTests
{
    [Fact]
    public void StayKeepsThePageAndDoesNotOpenAToast()
    {
        // 留在原地就**不能**是 ShowToast:toast 活得比頁面久，那是它的用處，
        // 但也代表它跟「我還在這一頁」講的不是同一件事。
        var result = Feedback.Stay("訊息");

        Assert.Equal(CommandResultKind.KeepOpen, result.Kind);
    }

    [Fact]
    public void DoneSendsAToastThatOutlivesThePalette()
    {
        var result = Feedback.Done("訊息");

        Assert.Equal(CommandResultKind.ShowToast, result.Kind);

        var args = Assert.IsType<ToastArgs>(result.Args);

        Assert.Equal("訊息", args.Message);
        Assert.NotNull(args.Result);

        // 漏掉 Result 也會是 Dismiss(那是預設值)，所以這一條單看過不了關 ——
        // 真正的守門是下面 HomeGoesHome:它證明 Result 是**明著給**的，不是撿到預設值。
        Assert.Equal(CommandResultKind.Dismiss, args.Result.Kind);
    }

    [Fact]
    public void HomeSendsAToastAndNavigatesInstead()
    {
        var result = Feedback.Home("訊息");

        Assert.Equal(CommandResultKind.ShowToast, result.Kind);

        var args = Assert.IsType<ToastArgs>(result.Args);

        Assert.NotNull(args.Result);

        // GoHome 不是任何東西的預設值，所以這一條同時證明了 Toast() 真的有在指派 Result。
        Assert.Equal(CommandResultKind.GoHome, args.Result.Kind);
    }

    /// <summary>
    /// 複製內文是「留在原地」那一類 —— 三條路(成功 / 空內文 / 失敗)都不能收面板。
    ///
    /// 這一頁的歷史值得記著:它先後是「靜靜地複製」、「那一列閃一個標籤」、
    /// 「一則 toast」，最後回到 <see cref="Feedback.Stay"/>。前兩種都是為了繞開
    /// 一條後來證實是假的規則(以為 toast 會搶焦點、面板必關)，第三種則是規則倒了之後
    /// 過頭的那一步 —— toast 是給離開用的。
    /// </summary>
    [Fact]
    public void CopyingAnEmptyBodyStaysOnThePage()
    {
        // 空內文這條路**在碰剪貼簿之前就返回**，所以測得到而不必動到跑測試那台機器的
        // 剪貼簿(CI 上也沒有剪貼簿可用)。另外兩條的收尾走同一個 Feedback.Stay。
        var command = new CopyNoteBodyCommand(string.Empty, "標題");

        var result = Assert.IsType<CommandResult>(command.Invoke());

        Assert.Equal(CommandResultKind.KeepOpen, result.Kind);
    }

    [Fact]
    public void TheSuccessMessageCarriesTheNoteTitle()
    {
        // 三個畫面共用同一則訊息，而清單頁上一次可能有兩百列 ——
        // 「複製到的是哪一則」只能靠訊息本身講。
        Assert.Contains("{0}", Resources.CopyDone, StringComparison.Ordinal);

        var message = Strings.Format(Resources.CopyDone, "會議記錄");

        Assert.Contains("會議記錄", message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEmptyBodyMessageDoesNotPretendSomethingWasCopied()
    {
        // 沒東西進剪貼簿，所以不提標題:「已複製:X」講的是「X 進了剪貼簿」。
        Assert.DoesNotContain("{0}", Resources.CopyNoBody, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTitleCanBeSwappedAfterConstruction()
    {
        // 預覽頁與記下並預覽頁把同一個實例留著重複用，每次取內容都重查一次筆記
        // (NotePreviewContent.Reload)。使用者剛在編輯頁改過標題的話，只換 Text
        // 會讓訊息講出舊標題 —— 那比不講更糟，它主動說了一句假話。
        var command = new CopyNoteBodyCommand("內文", "舊標題")
        {
            Text = "新內文",
            NoteTitle = "新標題",
        };

        Assert.Equal("新內文", command.Text);
        Assert.Equal("新標題", command.NoteTitle);
    }
}
