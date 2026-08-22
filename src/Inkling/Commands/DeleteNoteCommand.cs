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
            // **成功時一個 toast 都不發。** 這裡曾經回一個「已移到資源回收筒」的 toast 配
            // KeepOpen,但那兩件事湊不到一起:toast 是另一個會搶焦點的視窗,而 CmdPal 主視窗
            // 一失焦就自我隱藏(同一個機制見 docs/design-notes.md〈記下之後要不要先看一眼〉)——
            // 寫著「留在清單頁」的程式碼,實際效果是刪一則就把整個面板關掉一次。
            // 回饋本來就不需要 toast:那一列當場從清單上消失,比什麼訊息都直接。
            return CommandResult.KeepOpen();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NoteNotFoundException)
        {
            // 檔案被別的程式鎖住、資料夾權限不對、或是別台機器已經先刪掉了 ——
            // 都不該讓擴展掛掉,但更不能假裝刪掉了。
            DiagnosticLog.Failure($"DeleteNote failed ({ex.GetType().Name})", ex.ToString());

            // 失敗路形狀的準則(跟 QuickCaptureCommand、ConfirmedDeleteAllNotesCommand 對照著看):
            // **畫面上還壓著使用者沒存下的輸入時**(存檔類),一個 toast 都不能發 ——
            // 它一搶焦點主視窗就自我隱藏,輸入跟著丟,所以那些路走 InfoBadge + KeepOpen。
            // 刪除沒有這種東西:打的是現成的筆記,刪不掉它還在原處,使用者的下一步
            // 本來就是去查資料夾,面板被這個 toast 關掉可以接受
            // (ConfirmedDeleteAllNotesCommand 的部分失敗是同一個取捨)。
            return CommandResult.ShowToast(Strings.Format(Resources.DeleteFailed, ex.Message));
        }
    }
}
