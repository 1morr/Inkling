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
            DiagnosticLog.Write($"DeleteAllNotes: scope={_scope}, deleted {deleted}/{targets.Count}");

            // **成功時一個 toast 都不發 —— 現在這是選擇,不再是被迫。**
            // 舊註解把「面板消失」歸因於 toast 搶焦點,那個歸因 2026-08-23 量過是錯的
            // (見 DeleteNoteCommand 同一段與 docs/design-notes.md〈toast 不會把面板關掉〉)。
            // 不發的理由是清單當場變成「沒有筆記可以刪除」,那本來就是最好的回饋。
            if (deleted == targets.Count)
            {
                return CommandResult.KeepOpen();
            }

            // 有漏網的就非講不可 —— 使用者按下刪除之後看到清單還剩東西,
            // 要能立刻知道那不是沒生效,是那幾個檔案刪不掉。
            //
            // **所以 `Result` 非 `KeepOpen` 不可,而這裡以前是反的。** 這條路以前用
            // `ShowToast(字串)` 那個簡寫,而它的預設收尾是 `Dismiss` —— 面板關掉,
            // 上面那句「要能立刻知道清單還剩東西」就自相矛盾了:看不到清單。
            // 當時以為 toast 必然關面板(假規則,見 docs/design-notes.md
            // 〈toast 不會把面板關掉〉),所以沒發現這個矛盾。
            return Feedback.Stay(
                Strings.Format(Resources.DeletePartialFailure, deleted, targets.Count - deleted));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DiagnosticLog.Failure($"DeleteAllNotes failed ({ex.GetType().Name})", ex.ToString());

            return Feedback.Stay(Strings.Format(Resources.DeleteFailed, ex.Message));
        }
    }
}
