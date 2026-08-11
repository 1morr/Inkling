using System.Diagnostics;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Notelet.Core;

namespace Notelet.Commands;

/// <summary>
/// 把一行字直接存成一則新筆記。這是整個擴展存在的理由:叫出 Command Palette、
/// 打字、Enter,不進任何頁面。
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

    /// <summary>要記下的文字。由 fallback item 在使用者每次輸入時更新。</summary>
    public string Text { get; set; } = string.Empty;

    public override CommandResult Invoke()
    {
        var text = Text.Trim();

        if (text.Length == 0)
        {
            return CommandResult.KeepOpen();
        }

        try
        {
            var note = _repository.Create(text, string.Empty);
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
