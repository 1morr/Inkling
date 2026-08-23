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
            // **成功時一個 toast 都不發 —— 現在這是選擇,不再是被迫。**
            //
            // 這裡曾經回一個「已移到資源回收筒」的 toast 配 KeepOpen,而 2026-08-13 觀察到
            // 「刪一則面板就關一次」,當時歸因於「toast 搶焦點 → 主視窗自我隱藏」。
            // **2026-08-23 在同一條路上量過,那個歸因是錯的**:toast 視窗拿不到前景,
            // ShowToast 配 KeepOpen 面板穩穩停在清單頁(見 docs/design-notes.md
            // 〈toast 不會把面板關掉〉)。當年為什麼會關掉沒有查出來,CmdPal 版本沒變過。
            //
            // 不發的理由改成它本來就該有的那一個:**那一列當場從清單上消失,
            // 比什麼訊息都直接**,再疊一則 toast 只是重複講同一件事。
            return CommandResult.KeepOpen();
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
            return CommandResult.ShowToast(new ToastArgs
            {
                Message = Strings.Format(Resources.DeleteFailed, ex.Message),
                Result = CommandResult.KeepOpen(),
            });
        }
    }
}
