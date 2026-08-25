using Microsoft.CommandPalette.Extensions.Toolkit;
using Inkling.Core;
using Inkling.Properties;

namespace Inkling.Commands;

/// <summary>
/// 把快速記下頁搜尋框裡的一句話存成一則新筆記。這是整個擴展存在的理由:
/// 叫出 Command Palette、alias、打字、Enter，中間不必碰任何表單。
///
/// (原本連那一次 alias 都不用 —— 走的是主搜尋框的 fallback。為什麼改掉，
/// 見 <see cref="Inkling.Pages.QuickCapturePage"/>。)
/// </summary>
internal sealed partial class QuickCaptureCommand : InvokableCommand
{
    private readonly INoteRepository _repository;

    public QuickCaptureCommand(INoteRepository repository)
    {
        _repository = repository;

        Id = CommandIds.QuickCapture;
        Name = Resources.CommandCapture;
        Icon = Icons.Add;
    }

    /// <summary>要記下的內容。由快速記下頁在使用者每次輸入時整個換掉。</summary>
    public QuickCaptureDraft? Draft { get; set; }

    /// <summary>
    /// 存檔成功後的回呼。快速記下頁用它清掉搜尋框 —— 少了這一步，下一次回到那一頁
    /// 會帶著上一次的字，反射性的 Enter 就多存一則重複筆記。理由見
    /// <c>QuickCapturePage.ClearQuery</c>。
    /// </summary>
    public Action? OnCaptured { get; set; }

    public override CommandResult Invoke()
    {
        // 讀一次就固定下來:使用者按 Enter 與 CmdPal 更新查詢是兩次不同的跨進程呼叫。
        if (Draft is not { } draft)
        {
            return CommandResult.KeepOpen();
        }

        try
        {
            var note = _repository.Create(draft.Title, draft.Body);

            // 存好了才清搜尋框 —— 失敗那條路刻意保留使用者打的字(見下面的 catch)。
            OnCaptured?.Invoke();

            // Toast 帶著後續動作一起送:ToastArgs.Result 就是 CmdPal 顯示完提示要做的事 ——
            // 分兩次回傳做不到，Invoke 只有一次回傳的機會。
            //
            // **`Dismiss()` 而不是 `GoHome()`**:記完這則想法就是回去做原本的事，
            // 留一個主搜尋框在畫面上只是多一次 Esc(跟記下並預覽頁的「完成」、
            // 隨手草稿的存檔同一個判準，見設計考證〈記下之後要不要先看一眼〉)。
            //
            // 這裡以前是 `GoHome()`，註解寫著「否則搜尋框還留著剛打的字」——
            // **那句話把兩個機制講混了**。清空快速記下頁的搜尋框是上面那行
            // `OnCaptured`(接到 `QuickCapturePage.ClearQuery`)做的，跟回傳值無關。
            //
            // ⚠ 曾經量到「`Dismiss` 與 `GoHome` 在畫面上分不出來」，解釋是「toast 一搶焦點
            // 主視窗就自我隱藏」。**那個解釋 2026-08-23 被推翻了**(toast 拿不到前景，
            // 面板去留完全由 `Result` 決定，見 docs/design-notes.md〈toast 不會把面板關掉〉),
            // 而在設定頁上 `GoHome` 明確是「面板留著、切回主頁」。也就是說那個「分不出來」
            // 跟現在的模型對不起來，**還沒重測**。`Dismiss()` 本身不受影響:
            // 這條路要的就是把面板收掉，它是直接講出那件事的那一個。
            return Feedback.Done(Strings.Format(Resources.CaptureSaved, note.Title));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 磁碟滿了、資料夾被移走、OneDrive 鎖住檔案 —— 這些都不該讓擴展整個掛掉，
            // 但也絕對不能無聲失敗，不然使用者會以為想法記下來了。
            //
            // 用 DiagnosticLog 而不是 Debug.WriteLine:後者掛著 [Conditional("DEBUG")],
            // Release 會整個編掉，而日常安裝的就是 Release —— 也就是說最需要留下痕跡的
            // 那條路，在正式版反而什麼都查不到。
            DiagnosticLog.Failure($"QuickCapture failed ({ex.GetType().Name})", ex.ToString());

            // **這條路絕對不能收面板。** 搜尋框裡那句話是使用者剛打的、還沒存下來的東西，
            // 面板一收就跟著消失，只能憑記憶重打。對照 CapturedNotePage 的失敗處理
            // (原文整段留在畫面上)。所以回傳 KeepOpen，那句話留在搜尋框裡，
            // 修好問題之後直接再按一次 Enter 就是重試。
            //
            // 訊息走 InfoBadge。以前寫的理由是「toast 一搶焦點主視窗就自我隱藏，
            // 輸入跟著丟」—— **那是假的**(toast 拿不到前景，見 docs/design-notes.md
            // 〈toast 不會把面板關掉〉),`ShowToast` 配 `KeepOpen` 同樣留得住那些字。
            // 維持 InfoBadge 是因為訊息與那句還在搜尋框裡的字是同一件事，
            // 放在同一個面板裡讀比較連貫;真正非顧不可的是 `Result`，不是通道。
            return Feedback.Stay(Strings.Format(Resources.SaveFailed, ex.Message));
        }
    }
}
