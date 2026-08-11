using System.Diagnostics;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Notelet.Core;

namespace Notelet.Commands;

/// <summary>
/// 把主搜尋框裡的一句話直接存成一則新筆記。這是整個擴展存在的理由:
/// 叫出 Command Palette、打字、Enter,不進任何頁面。
/// </summary>
internal sealed partial class QuickCaptureCommand : InvokableCommand
{
    private readonly INoteRepository _repository;

    public QuickCaptureCommand(INoteRepository repository)
    {
        _repository = repository;

        Id = CommandIds.QuickCapture;
        Name = "記下";
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
                Message = $"已記下:{note.Title}",
                Result = CommandResult.GoHome(),
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 磁碟滿了、資料夾被移走、OneDrive 鎖住檔案 —— 這些都不該讓擴展整個掛掉,
            // 但也絕對不能無聲失敗,不然使用者會以為想法記下來了。
            Debug.WriteLine($"[Notelet] 快速新增失敗:{ex}");
            return CommandResult.ShowToast($"存檔失敗:{ex.Message}");
        }
    }
}
