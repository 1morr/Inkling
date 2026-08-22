using Microsoft.CommandPalette.Extensions.Toolkit;
using Inkling.Properties;

namespace Inkling.Commands;

/// <summary>
/// 在檔案總管裡開啟筆記所在的資料夾,並選中那個檔案。
///
/// 底下仍然是 toolkit 的 <see cref="ShowFileInFolderCommand"/>(它跑的是
/// <c>explorer.exe /select,"&lt;路徑&gt;"</c>,那一段自己寫沒有意義),包一層換掉兩件事:
///
/// <para><b>一、路徑不存在時它靜靜什麼都不做。</b></para>
///
/// toolkit 那邊是 <c>if (Path.Exists(_path))</c> —— 不成立就整段跳過,直接回傳
/// <c>Result</c>。筆記檔在 Inkling 以外被改名或移走之後按 <c>Ctrl+L</c>,使用者看到的是
/// 「面板關掉了,檔案總管沒開」,而且沒有任何訊息可以解釋。
///
/// 這裡改成先自己檢查,失敗發一則 <see cref="ToastStatusMessage"/> 並留在原地 ——
/// 那是底部命令列的 InfoBadge,不開視窗、不搶焦點(對照 <c>CommandResult.ShowToast</c>,
/// 見 <see cref="OpenNoteFileCommand"/> 上那段說明)。**失敗時面板沒有失焦**,所以這則
/// 訊息真的看得到。
///
/// <para><b>二、<c>Result</c> 顯式指定成 <c>KeepOpen</c>。</b></para>
///
/// <see cref="ShowFileInFolderCommand"/> 的預設是 <c>Dismiss</c>,而隔壁的
/// <see cref="OpenUrlCommand"/> 預設是 <c>KeepOpen</c> —— 同一個 <c>Ctrl+K</c> 選單裡
/// 兩個「跳出去」的鍵行為相反,而那不是誰決定的,是沒去覆寫的結果。
///
/// 兩者的差別實機量過(考證見 <see href="../../../docs/design-notes.md">設計考證</see>
/// 〈跳出去之後回得到哪一頁〉):外部視窗搶走焦點造成的自我隱藏**不動導覽堆疊**,
/// 面板叫回來還停在原本那一頁;<c>Dismiss</c> 則會回到主頁而且清空搜尋框。
/// 按 <c>Ctrl+L</c> 的人是去檔案總管做事,回來多半還想著同一則筆記 —— 留著那一頁,
/// 省掉重新搜尋一次。
/// </summary>
internal sealed partial class ShowNoteInFolderCommand : ShowFileInFolderCommand
{
    private readonly string _filePath;

    public ShowNoteInFolderCommand(string filePath)
        : base(filePath)
    {
        _filePath = filePath;

        // Name 要自己換掉,理由同 OpenNoteFileCommand:toolkit 給的字串跟著 CmdPal 的
        // 語言走,而這一項有機會坐上底部工具列。
        Name = Resources.CommandOpenFileLocation;
        Result = CommandResult.KeepOpen();
    }

    public override CommandResult Invoke()
    {
        if (!File.Exists(_filePath))
        {
            // 留痕跡,理由同 OpenNoteFileCommand:畫面上那條訊息 2.5 秒就收掉了。
            DiagnosticLog.Failure("ShowNoteInFolder: the file no longer exists", _filePath);
            new ToastStatusMessage(Resources.OpenFileMissing).Show();
            return CommandResult.KeepOpen();
        }

        return base.Invoke();
    }
}
