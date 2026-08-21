using Microsoft.CommandPalette.Extensions.Toolkit;
using Inkling.Core;
using Inkling.Properties;

namespace Inkling.Commands;

/// <summary>
/// 把快速記下頁搜尋框裡的一句話存成一則新筆記。這是整個擴展存在的理由:
/// 叫出 Command Palette、alias、打字、Enter,中間不必碰任何表單。
///
/// (原本連那一次 alias 都不用 —— 走的是主搜尋框的 fallback。為什麼改掉,
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

            // 記完就離開快速記下頁,否則搜尋框還留著剛打的字,想記下一則得先自己清掉。
            // Toast 帶著後續動作一起送:ToastArgs.Result 就是 CmdPal 顯示完提示要做的事 ——
            // 分兩次回傳做不到,Invoke 只有一次回傳的機會。
            return CommandResult.ShowToast(new ToastArgs
            {
                Message = Strings.Format(Resources.CaptureSaved, note.Title),
                Result = CommandResult.GoHome(),
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 磁碟滿了、資料夾被移走、OneDrive 鎖住檔案 —— 這些都不該讓擴展整個掛掉,
            // 但也絕對不能無聲失敗,不然使用者會以為想法記下來了。
            //
            // 用 DiagnosticLog 而不是 Debug.WriteLine:後者掛著 [Conditional("DEBUG")],
            // Release 會整個編掉,而日常安裝的就是 Release —— 也就是說最需要留下痕跡的
            // 那條路,在正式版反而什麼都查不到。
            DiagnosticLog.Failure($"QuickCapture 失敗:{ex}");

            // **這條路一個 toast 都不能發,也不能 Dismiss。** 搜尋框裡那句話是使用者
            // 剛打的、還沒存下來的東西:toast 視窗一搶焦點主視窗就自我隱藏(第 8 條那個
            // 機制),Dismiss 又會主動收起 —— 兩條路疊起來,失敗當下那句話就跟著消失,
            // 只能憑記憶重打。對照 CapturedNotePage 的失敗處理(原文整段留在畫面上)。
            //
            // 所以走 InfoBadge(不開視窗、不關面板)+ KeepOpen:那句話留在搜尋框裡,
            // 修好問題之後直接再按一次 Enter 就是重試。
            new ToastStatusMessage(Strings.Format(Resources.SaveFailed, ex.Message)).Show();
            return CommandResult.KeepOpen();
        }
    }
}
