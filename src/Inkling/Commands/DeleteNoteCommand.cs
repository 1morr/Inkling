using Microsoft.CommandPalette.Extensions.Toolkit;
using Inkling.Core;
using Inkling.Properties;

namespace Inkling.Commands;

/// <summary>
/// 把一則筆記送進資源回收筒。
///
/// 這個命令本身**不問**要不要刪 —— 確認是由包在外面的
/// <see cref="CommandResult.Confirm"/> 負責的,CmdPal 會先跳確認框,
/// 使用者按下主要按鈕之後才輪到這裡跑。所以走到 Invoke 就代表已經確認過了。
/// </summary>
internal sealed partial class DeleteNoteCommand : InvokableCommand
{
    private readonly INoteRepository _repository;
    private readonly Note _note;

    public DeleteNoteCommand(INoteRepository repository, Note note)
    {
        _repository = repository;
        _note = note;

        Name = Resources.CommandDelete;
        Icon = Icons.Delete;
    }

    public override CommandResult Invoke()
    {
        try
        {
            _repository.Delete(_note);
            DiagnosticLog.Write($"DeleteNote: deleted '{_note.Title}' ({_note.Id})");

            // 留在原來那一頁:刪完通常還想接著整理下一則。repository 的 Changed 會讓
            // 清單自己更新,不必離開再重進。
            //
            // **講一句話,留在原地。**
            //
            // 這條路曾經整整十天是靜默的,理由是「那一列當場從清單上消失,比什麼訊息都直接」——
            // 而那句話是在「發訊息會把面板關掉」那條假規則底下寫的:當年沒得選,
            // 於是把「沒有回饋」合理化成優點。規則倒了之後(見 docs/design-notes.md
            // 〈toast 不會把面板關掉〉)它就站不住了:
            //
            //  - **刪除是唯一不可逆的動作,卻是唯一不出聲的成功路徑。** 複製、存檔、
            //    快速記下現在都會講,只有它沉默。
            //  - **刪完焦點會跳走** —— 視覺錨點沒了,「剛才刪掉的是哪一則」只剩訊息講得出來。
            //    (焦點現在落在**下一則**而不是第一列,見 docs/design-notes.md
            //    〈刪掉一則之後,選取落在哪〉。**但這不構成把訊息拿掉的理由**:
            //    選取落在下一則說不出被刪那一則的標題。)
            //  - 清單頁的 `Ctrl+D` 更弱:那裡連則數都不會變,只有一列悄悄消失。
            //
            // **措辭刻意不提資源回收筒**:網路磁碟與沒有回收筒的裝置上 Windows 是直接刪除,
            // 那句話會變成假的。刪除頁的詳細窗格已經把這個差別講清楚了。
            return Feedback.Stay(Strings.Format(Resources.DeleteDone, _note.Title));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NoteNotFoundException)
        {
            // 檔案被別的程式鎖住、資料夾權限不對、或是別台機器已經先刪掉了 ——
            // 都不該讓擴展掛掉,但更不能假裝刪掉了。
            DiagnosticLog.Failure($"DeleteNote failed ({ex.GetType().Name})", ex.ToString());

            // **`Result` 一定要明著給。** `CommandResult.ShowToast(字串)` 那個簡寫用的是
            // `ToastArgs` 的預設收尾,而那個預設是 `Dismiss`(把它 new 一個出來讀到的)——
            // 於是「刪不掉」的提示會順手把面板收掉,跟「刪成功了」在畫面上分不出來。
            //
            // 這裡以前就是那個簡寫,而註解把它合理化成「面板被 toast 關掉可以接受」。
            // 那個「可以接受」建立在一條假規則上(以為 toast 會搶焦點、面板必關,見
            // docs/design-notes.md〈toast 不會把面板關掉〉)—— 既然是自由選擇,
            // 就沒有理由選「失敗時把證據收走」:那一則筆記還在清單上,留著面板才看得到。
            return Feedback.Stay(Strings.Format(Resources.DeleteFailed, ex.Message));
        }
    }
}
