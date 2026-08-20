using Microsoft.CommandPalette.Extensions.Toolkit;
using Inkling.Core;
using Inkling.Properties;

namespace Inkling.Commands;

/// <summary>批次刪除的範圍。</summary>
internal enum DeleteScope
{
    /// <summary>筆記資料夾(含子資料夾)底下所有的 .md,包括不是 Inkling 建立的。</summary>
    Everything,

    /// <summary>只刪 front matter 裡有 Inkling id 的那些,別的工具丟進來的檔案留著。</summary>
    InklingCreatedOnly,
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

        Name = scope == DeleteScope.Everything ? Resources.DeleteAllName : Resources.DeleteMineName;
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
                // 這條路只有在按下確認的那一瞬間別人剛好把資料夾清空了才會走到,
                // 而頁面本身的 EmptyContent 馬上就會講同一件事,不必再發 toast。
                return CommandResult.KeepOpen();
            }

            var deleted = _repository.DeleteMany(targets);
            DiagnosticLog.Write($"DeleteAllNotes: scope={_scope} 刪掉 {deleted}/{targets.Count} 則");

            // **成功時一個 toast 都不發。** 這裡曾經回一個「已把 N 則移到資源回收筒」的
            // toast 配 KeepOpen,註釋還寫著「使用者當場看到清單真的空了」—— 但 toast 是
            // 另一個會搶焦點的視窗,主視窗一失焦就自我隱藏,實際上使用者什麼都沒看到,
            // 面板直接消失(同一個機制見 docs/design-notes.md〈記下之後要不要先看一眼〉)。
            // 清單當場變成「沒有筆記可以刪除」本來就是最好的回饋。
            if (deleted == targets.Count)
            {
                return CommandResult.KeepOpen();
            }

            // 有漏網的就非講不可 —— 使用者按下刪除之後看到清單還剩東西,
            // 要能立刻知道那不是沒生效,是那幾個檔案刪不掉。這是例外路徑,
            // 面板被 toast 關掉也比默默少刪好。
            return CommandResult.ShowToast(
                Strings.Format(Resources.DeletePartialFailure, deleted, targets.Count - deleted));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DiagnosticLog.Write($"DeleteAllNotes 失敗:{ex}");

            return CommandResult.ShowToast(Strings.Format(Resources.DeleteFailed, ex.Message));
        }
    }
}
