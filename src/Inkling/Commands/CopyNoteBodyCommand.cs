using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Inkling.Properties;

namespace Inkling.Commands;

/// <summary>
/// 複製筆記內文(不含 front matter)。
///
/// 包一層 toolkit 的 <see cref="CopyTextCommand"/> 換掉它的兩個預設行為:
///
/// <para><b>一、內文空的時候不要碰剪貼簿。</b></para>
///
/// <c>ClipboardHelper.SetText</c> 會先 <c>EmptyClipboard()</c> 再把字串寫進去，
/// 對一則沒有內文的筆記按下去，等於把使用者剪貼簿裡本來的東西清掉，還配一句「已複製」。
///
/// <para><b>二、三條路都講一句話，而且都留在原地。</b></para>
///
/// toolkit 預設回的是 <c>ShowToast</c>，而 <see cref="ToastArgs"/> 的預設收尾是
/// <c>Dismiss</c>(把它 new 一個出來讀到的)—— 兩件事疊起來，複製一次面板就關一次。
/// 所以三條路(空內文、成功、失敗)一律回 <see cref="Feedback.Stay"/>:面板留在原地，
/// 訊息走底部的 <c>InfoBar</c>。
///
/// ⚠ **這一段 2026-08-23 整個改寫過，前一版的理由是錯的。** 以前寫著「就算把
/// <c>ToastArgs.Result</c> 改成 <c>KeepOpen</c> 也救不回來:toast 是另一個會搶焦點的視窗，
/// 而 CmdPal 主視窗一失焦就自我隱藏」，結論是「想留在畫面上就一個 toast 都不能發」。
/// **量過之後不成立** —— toast 視窗是 <c>WS_EX_TOOLWINDOW | WS_DISABLED</c> 的，
/// 它拿不到前景，面板去留完全由 <c>ToastArgs.Result</c> 決定。實測數字與推翻的經過見
/// [設計考證〈toast 不會把面板關掉〉](../../../docs/design-notes.md#toast-does-not-steal-focus)。
///
/// 那條假規則的代價不是清單頁(它靠 <c>ListItem.Tags</c> 在那一列打標籤繞過去了),
/// 而是**預覽頁與記下並預覽頁的成功路徑整個是靜默的** —— 理由寫著「那兩頁本來就整頁
/// 顯示著剛複製的內容」，但頁面顯示什麼跟剪貼簿有沒有寫成功無關，按下去畫面一個像素
/// 都不變，跟快速鍵壞掉分不出來。那正是當初修「空內文」那條時用的判準，只是成功路徑被漏掉了。
///
/// 現在三個畫面走同一條:「已複製:&lt;筆記標題&gt;」。標題不是裝飾 ——
/// 清單頁以前靠「標籤掛在哪一列」講「複製到的是哪一則」，底部那條訊息沒有位置感，
/// 那個資訊只能寫進訊息裡。
///
/// **通道是 <c>InfoBar</c> 而不是 toast**，雖然推翻之後兩個都可用:<see cref="Feedback"/>
/// 的分工只看面板去留，留在原地就是 <see cref="Feedback.Stay"/>，沒有例外可以挑。
/// 中間一度改成 toast 配 <c>KeepOpen</c>，一天之內就收回來了 —— 那讓「留在原地 +
/// 說一句話」同時有兩種寫法，分界線就講不出來。見
/// [設計考證〈通道的分工〉](../../../docs/design-notes.md#feedback-channels)。
/// </summary>
internal sealed partial class CopyNoteBodyCommand : CopyTextCommand
{
    /// <summary>
    /// 要寫進提示的筆記標題。
    ///
    /// **跟 <see cref="CopyTextCommand.Text"/> 一樣是可變的，而且要一起換。**
    /// 預覽頁與記下並預覽頁把這個實例留著重複用，每次取內容都重新查一次筆記
    /// (見 <see cref="Pages.NotePreviewContent.Reload"/>)—— 使用者剛在編輯頁改過標題的話，
    /// 只換 <c>Text</c> 會讓提示講出舊標題，而那比不講更糟。
    /// </summary>
    public string NoteTitle { get; set; }

    /// <param name="body">要複製的內文。</param>
    /// <param name="noteTitle">寫進提示的標題，見 <see cref="NoteTitle"/>。</param>
    public CopyNoteBodyCommand(string body, string noteTitle)
        : base(body)
    {
        NoteTitle = noteTitle;

        Name = Resources.CommandCopyBody;
        Icon = Icons.Copy;

        // 這個屬性是 toolkit 在沒有覆寫 Invoke 時用的收尾。我們每一條路都明著回傳，
        // 所以它其實走不到 —— 留著是為了讓「這個命令不收面板」在建構時就看得出來，
        // 而 toolkit 的預設值(ShowToast 配 Dismiss)剛好相反。
        Result = CommandResult.KeepOpen();
    }

    public override ICommandResult Invoke()
    {
        if (Text.Length == 0)
        {
            // 照實講。剪貼簿看不見，不講的話按下去就是完全沒有反應，
            // 使用者只會以為快速鍵壞了。這條路**沒有動到剪貼簿**，所以不提標題:
            // 「已複製:X」講的是「X 進了剪貼簿」，這裡什麼都沒進去。
            return Feedback.Stay(Resources.CopyNoBody);
        }

        // base.Invoke 是同步的(ClipboardHelper 自己開一條 STA 執行緒再 Join),
        // 所以走到下一行時剪貼簿真的已經寫好了，回報不會比事實早。
        //
        // **回傳值刻意丟掉。** toolkit 那一版回的是 ShowToast 配預設的 Dismiss,
        // 拿來當結果等於複製一次面板關一次。要的只有它的副作用。
        base.Invoke();

        return WroteToClipboard()
            ? Feedback.Stay(Strings.Format(Resources.CopyDone, NoteTitle))
            : Feedback.Stay(Resources.CopyFailed);
    }

    /// <summary>
    /// 剪貼簿裡現在真的是這段文字嗎。
    ///
    /// <c>ClipboardHelper.SetText</c> 回 <c>void</c>，失敗在 toolkit 裡就被吞掉了 ——
    /// 讀回來比一次是唯一能確認的方式。剪貼簿是全機共用的資源，被別的進程鎖住時寫入
    /// 會失敗，而這裡本來**無條件**回報「已複製」:那比靜默更糟，它主動說了一句假話。
    ///
    /// 讀回來剛好相等但其實是別人寫的(或本來就一樣)也算成功 —— 使用者要的是
    /// 「剪貼簿裡是這段文字」，那個條件確實成立。
    /// </summary>
    private bool WroteToClipboard()
    {
        try
        {
            return string.Equals(ClipboardHelper.GetText(), Text, StringComparison.Ordinal);
        }
        catch (Exception)
        {
            // 讀不回來就是確認不了，當成沒成功。這裡攔全部:剪貼簿的失敗形狀跨版本
            // 不一致(COM、Win32、逾時)，而漏接一種就等於讓一個唯讀的確認動作
            // 把整個命令弄爆。
            return false;
        }
    }
}
