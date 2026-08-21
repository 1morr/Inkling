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
/// <c>ClipboardHelper.SetText</c> 會先 <c>EmptyClipboard()</c> 再把字串寫進去,
/// 對一則沒有內文的筆記按下去,等於把使用者剪貼簿裡本來的東西清掉,還配一句「已複製」。
///
/// <para><b>二、複製完要留在原地,所以一個 toast 都不發。</b></para>
///
/// toolkit 預設回的是 <c>ShowToast</c>,而 <see cref="ToastArgs"/> 的預設收尾又是
/// <c>Dismiss</c> —— 兩件事疊起來,複製一次面板就關一次。就算把 <c>ToastArgs.Result</c>
/// 改成 <c>KeepOpen</c> 也救不回來:toast 是另一個會搶焦點的視窗,而 CmdPal 主視窗一失焦
/// 就自我隱藏(同一個機制見 <see cref="DeleteNoteCommand"/>)。**想留在畫面上就一個
/// toast 都不能發**,這條規則對複製跟對刪除一樣硬。
///
/// 所以回饋改由頁面自己給:<paramref name="report"/> 讓清單頁在那一列打一個標籤
/// (<c>ListItem.Tags</c> 改了畫面會即時更新,那條路在安裝版上是通的)。
/// 沒有傳 <c>report</c> 的呼叫端(預覽頁、記下並預覽頁,它們沒有清單列可以掛標籤)
/// **成功時**就是靜靜地複製 —— 那兩頁本來就整頁顯示著剛複製的內容。
///
/// <para><b>但「內文是空的」那條路不能沿用那個理由。</b></para>
///
/// 空內文時什麼都沒被複製,「整頁顯示著剛複製的內容」不成立 —— 實機驗過:那兩頁按下去
/// 畫面一點變化都沒有,跟快速鍵壞掉分不出來。所以那條路改走
/// <see cref="ToastStatusMessage"/>:它不開視窗、不搶焦點、不收面板,而面板此時就在前景。
/// (那個提示畫成一條橫跨底部的 InfoBar 加一個計數 InfoBadge,ListPage 與 ContentPage 都會出現 ——
/// 前提是 <c>ExtensionHost</c> 拿得到 host,見 <see cref="InklingCommandsProvider.InitializeWithHost"/>。)
/// 成功那條路維持靜默。
/// </summary>
internal sealed partial class CopyNoteBodyCommand : CopyTextCommand
{
    private readonly Action<string>? _report;

    /// <param name="body">要複製的內文。</param>
    /// <param name="report">複製完的回饋文字,由頁面決定怎麼顯示;沒有就不回報。</param>
    public CopyNoteBodyCommand(string body, Action<string>? report = null)
        : base(body)
    {
        _report = report;

        Name = Resources.CommandCopyBody;
        Icon = Icons.Copy;
        Result = CommandResult.KeepOpen();
    }

    public override ICommandResult Invoke()
    {
        if (Text.Length == 0)
        {
            // 照實講。剪貼簿看不見,不講的話按下去就是完全沒有反應,
            // 使用者只會以為快速鍵壞了。
            //
            // **沒有傳 report 的呼叫端也要講。** 類別註解給那兩頁靜默的理由是
            // 「那一頁本來就整頁顯示著剛複製的內容」—— 而空內文時什麼都沒被複製,
            // 那個理由在這條路上不成立(實機驗過:預覽頁按下去 UIA 樹前後一字不差)。
            // 回饋走哪一條交給 Announce 決定,兩條都不會把面板收掉。成功那條路維持靜默。
            Announce(Resources.CopyNoBody);
            return CommandResult.KeepOpen();
        }

        // base.Invoke 是同步的(ClipboardHelper 自己開一條 STA 執行緒再 Join),
        // 所以走到下一行時剪貼簿真的已經寫好了,回報不會比事實早。
        var result = base.Invoke();

        if (WroteToClipboard())
        {
            // 成功時沒有 report 的那兩頁維持靜默 —— 它們整頁顯示著剛複製的內容。
            _report?.Invoke(Resources.CopyDone);
        }
        else
        {
            Announce(Resources.CopyFailed);
        }

        return result;
    }

    /// <summary>
    /// 剪貼簿裡現在真的是這段文字嗎。
    ///
    /// <c>ClipboardHelper.SetText</c> 回 <c>void</c>,失敗在 toolkit 裡就被吞掉了 ——
    /// 讀回來比一次是唯一能確認的方式。剪貼簿是全機共用的資源,被別的進程鎖住時寫入
    /// 會失敗,而這裡本來**無條件**回報「已複製」:那比靜默更糟,它主動說了一句假話。
    ///
    /// 讀回來剛好相等但其實是別人寫的(或本來就一樣)也算成功 —— 使用者要的是
    /// 「剪貼簿裡是這段文字」,那個條件確實成立。
    /// </summary>
    private bool WroteToClipboard()
    {
        try
        {
            return string.Equals(ClipboardHelper.GetText(), Text, StringComparison.Ordinal);
        }
        catch (Exception)
        {
            // 讀不回來就是確認不了,當成沒成功。這裡攔全部:剪貼簿的失敗形狀跨版本
            // 不一致(COM、Win32、逾時),而漏接一種就等於讓一個唯讀的確認動作
            // 把整個命令弄爆。
            return false;
        }
    }

    /// <summary>
    /// 講一句話,而且**不管在哪一頁都要講得到**。清單頁在那一列打標籤
    /// (<c>ListItem.Tags</c>),沒有清單列的頁面走底部的狀態訊息。
    /// 兩條都不開視窗、不搶焦點、不收面板。
    /// </summary>
    private void Announce(string message)
    {
        if (_report is null)
        {
            new ToastStatusMessage(message).Show();
        }
        else
        {
            _report(message);
        }
    }
}
