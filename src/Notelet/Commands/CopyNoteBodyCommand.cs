using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Notelet.Properties;

namespace Notelet.Commands;

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
/// 沒有傳 <c>report</c> 的呼叫端(預覽頁,它沒有清單列可以掛標籤)就是靜靜地複製 ——
/// 那一頁本來就整頁顯示著剛複製的內容。
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
            _report?.Invoke(Resources.CopyNoBody);
            return CommandResult.KeepOpen();
        }

        // base.Invoke 是同步的(ClipboardHelper 自己開一條 STA 執行緒再 Join),
        // 所以走到下一行時剪貼簿真的已經寫好了,回報不會比事實早。
        var result = base.Invoke();
        _report?.Invoke(Resources.CopyDone);

        return result;
    }
}
