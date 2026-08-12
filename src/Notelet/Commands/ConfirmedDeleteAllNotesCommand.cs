using Microsoft.CommandPalette.Extensions.Toolkit;
using Notelet.Core;

namespace Notelet.Commands;

/// <summary>批次刪除的範圍。</summary>
internal enum DeleteScope
{
    /// <summary>筆記資料夾(含子資料夾)底下所有的 .md,包括不是 Notelet 建立的。</summary>
    Everything,

    /// <summary>只刪 front matter 裡有 Notelet id 的那些,別的工具丟進來的檔案留著。</summary>
    NoteletCreatedOnly,
}

/// <summary>
/// 確認之後真正動手的那一步。
///
/// 這個命令本身**不問**要不要刪 —— 確認是包在外面的 <see cref="CommandResult.Confirm"/>
/// 負責的,CmdPal 會先跳確認框,使用者按下主要按鈕之後才輪到這裡跑。
/// 所以走到 Invoke 就代表已經確認過了。
/// </summary>
internal sealed partial class ConfirmedDeleteAllNotesCommand : InvokableCommand
{
    private readonly INoteRepository _repository;
    private readonly DeleteScope _scope;

    public ConfirmedDeleteAllNotesCommand(INoteRepository repository, DeleteScope scope)
    {
        _repository = repository;
        _scope = scope;

        Name = scope == DeleteScope.Everything ? "刪除全部" : "只刪 Notelet 建立的";
        Icon = Icons.Delete;
    }

    public override CommandResult Invoke()
    {
        try
        {
            // 刪的當下重新讀一次,不用頁面建清單時的那一份:中間可能有筆記從別台機器
            // 同步下來。使用者按的是「刪除全部」,那就該是按下去那一刻的全部。
            var all = _repository.GetAll();

            IReadOnlyList<Note> targets = _scope == DeleteScope.Everything
                ? all
                : all.Where(n => !n.IsExternal).ToList();

            if (targets.Count == 0)
            {
                return CommandResult.ShowToast(new ToastArgs
                {
                    Message = "沒有筆記可以刪除",
                    Result = CommandResult.KeepOpen(),
                });
            }

            var deleted = _repository.DeleteMany(targets);
            DiagnosticLog.Write($"DeleteAllNotes: scope={_scope} 刪掉 {deleted}/{targets.Count} 則");

            // 有漏網的就講清楚是幾則 —— 使用者按下刪除之後看到清單還剩東西,
            // 要能立刻知道那不是沒生效,是那幾個檔案刪不掉。
            var message = deleted == targets.Count
                ? $"已把 {deleted} 則筆記移到資源回收筒"
                : $"已刪除 {deleted} 則,{targets.Count - deleted} 則刪不掉(檔案可能被其他程式開著)";

            // 留在刪除頁:repository 的 Changed 會讓它自己刷新,使用者當場看到清單真的空了
            // (或只剩下不是 Notelet 建立的那幾則)。比直接回首頁多一份「確實刪掉了」的證據。
            return CommandResult.ShowToast(new ToastArgs
            {
                Message = message,
                Result = CommandResult.KeepOpen(),
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DiagnosticLog.Write($"DeleteAllNotes 失敗:{ex}");

            return CommandResult.ShowToast($"刪除失敗:{ex.Message}");
        }
    }
}
