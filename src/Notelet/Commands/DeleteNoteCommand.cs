using Microsoft.CommandPalette.Extensions.Toolkit;
using Notelet.Core;

namespace Notelet.Commands;

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

        Name = "刪除";
        Icon = Icons.Delete;
    }

    public override CommandResult Invoke()
    {
        try
        {
            _repository.Delete(_note.Id);
            DiagnosticLog.Write($"DeleteNote: 已刪除 '{_note.Title}'({_note.Id})");

            // 留在清單頁:刪完通常還想接著整理下一則。repository 的 Changed 會讓
            // 清單自己更新,不必離開再重進。
            return CommandResult.ShowToast(new ToastArgs
            {
                Message = $"已移到資源回收筒:{_note.Title}",
                Result = CommandResult.KeepOpen(),
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NoteNotFoundException)
        {
            // 檔案被別的程式鎖住、資料夾權限不對、或是別台機器已經先刪掉了 ——
            // 都不該讓擴展掛掉,但更不能假裝刪掉了。
            DiagnosticLog.Write($"DeleteNote 失敗:{ex}");

            return CommandResult.ShowToast($"刪除失敗:{ex.Message}");
        }
    }
}
