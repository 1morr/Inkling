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

    /// <summary>要記下的內容。由 fallback item 在使用者每次輸入時整個換掉。</summary>
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
            return CommandResult.ShowToast($"已記下:{note.Title}");
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
