using System.Diagnostics;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Notelet.Core;

namespace Notelet.Commands;

/// <summary>
/// 「刪除所有筆記」的第一步:數一數有幾則,然後跳確認框。
///
/// 為什麼要兩個命令而不是在建構時就把數量算好放進標題:<c>TopLevelCommands()</c>
/// 在 CmdPal 一啟動時就會被呼叫,那條路上**絕對不能碰磁碟**。數量只有等使用者
/// 真的按下這個命令時才去讀,那時候慢一點也無所謂。
/// </summary>
internal sealed partial class DeleteAllNotesCommand : InvokableCommand
{
    private readonly INoteRepository _repository;

    public DeleteAllNotesCommand(INoteRepository repository)
    {
        _repository = repository;

        Id = CommandIds.DeleteAll;
        Name = "刪除所有筆記";
        Icon = Icons.Delete;
    }

    public override CommandResult Invoke()
    {
        int count;

        try
        {
            count = _repository.GetAll().Count;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return CommandResult.ShowToast($"讀不到筆記資料夾:{ex.Message}");
        }

        if (count == 0)
        {
            return CommandResult.ShowToast("沒有筆記可以刪除");
        }

        return CommandResult.Confirm(new ConfirmationArgs
        {
            Title = $"刪除全部 {count} 則筆記?",
            Description = "全部移到資源回收筒,可以從那裡還原。",
            PrimaryCommand = new ConfirmedDeleteAllNotesCommand(_repository),

            // 這一個維持 critical:CmdPal 會把預設按鈕設成「取消」,
            // 要清空整個資料夾就該多花那一下。單則刪除刻意不設,見 NoteListPage。
            IsPrimaryCommandCritical = true,
        });
    }
}

/// <summary>確認之後真正動手的那一步。走到這裡就代表使用者已經確認過了。</summary>
internal sealed partial class ConfirmedDeleteAllNotesCommand : InvokableCommand
{
    private readonly INoteRepository _repository;

    public ConfirmedDeleteAllNotesCommand(INoteRepository repository)
    {
        _repository = repository;

        Name = "刪除全部";
        Icon = Icons.Delete;
    }

    public override CommandResult Invoke()
    {
        try
        {
            var total = _repository.GetAll().Count;
            var deleted = _repository.DeleteAll();

            DiagnosticLog.Write($"DeleteAllNotes: 刪掉 {deleted}/{total} 則");

            // 有漏網的就講清楚是幾則 —— 使用者按下「刪除全部」之後看到清單還剩東西,
            // 要能立刻知道那不是沒生效,是那幾個檔案刪不掉。
            var message = deleted == total
                ? $"已把 {deleted} 則筆記移到資源回收筒"
                : $"已刪除 {deleted} 則,{total - deleted} 則刪不掉(檔案可能被其他程式開著)";

            return CommandResult.ShowToast(new ToastArgs
            {
                Message = message,
                Result = CommandResult.GoHome(),
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[Notelet] 刪除全部失敗:{ex}");
            DiagnosticLog.Write($"DeleteAllNotes 失敗:{ex}");

            return CommandResult.ShowToast($"刪除失敗:{ex.Message}");
        }
    }
}
