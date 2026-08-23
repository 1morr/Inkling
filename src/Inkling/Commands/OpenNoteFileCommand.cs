using Microsoft.CommandPalette.Extensions.Toolkit;
using Inkling.Properties;

namespace Inkling.Commands;

/// <summary>
/// 用系統預設的程式開啟一個 <c>.md</c>。
///
/// <para><b>為什麼不直接用 toolkit 的 <c>OpenUrlCommand</c>。</b></para>
///
/// 它呼叫的 <see cref="ShellHelpers.OpenInShell(string?, string?, string?, ShellHelpers.ShellRunAsType, bool)"/>
/// 會回一個 <c>bool</c> 說到底開起來了沒(裡面 <c>catch (Win32Exception) { return false; }</c>),
/// 但 <c>OpenUrlCommand.Invoke</c> **把那個值丟掉**,無論成敗都回傳它的 <c>Result</c> ——
/// 表現出來就是「按下去什麼都沒發生」,連一行訊息都沒有。
///
/// 實機重現過(把筆記檔在 Inkling 以外改名,再回到預覽頁按 <c>Ctrl+O</c>):
/// 改之前是面板原封不動、`toast 視窗:可見=False`、沒有任何程式起來、也沒有任何訊息 ——
/// 使用者按下去,什麼都沒有發生。<c>Ctrl+L</c> 當時更糟,它還走著 toolkit 預設的
/// <c>Dismiss</c>,所以是「面板關掉了、檔案總管沒開」,看起來跟成功幾乎一樣。
///
/// <b>「沒有可以開啟 <c>.md</c> 的程式」那條路沒有在真機上重現過</b>,只從原始碼確認。
/// 順帶記一個查證上的坑:<c>assoc .md</c> 回 "File association not found" **不代表**
/// 開不起來 —— 那個舊命令只看 <c>HKCR\.md</c> 的預設值,而 <c>OpenWithProgids</c> 裡
/// 還有候選程式,<c>ShellExecute</c> 照樣開得起來(實測:`assoc` 說沒有,VS Code 照開)。
///
/// <para><b>失敗時為什麼可以發提示。</b></para>
///
/// 用的是 <see cref="ToastStatusMessage"/>(底部命令列的 InfoBadge)。
/// <c>CommandResult.ShowToast</c> 配 <c>KeepOpen</c> 在這裡**也行得通**
/// (toast 拿不到前景、收不掉面板,見<see href="../../../docs/design-notes.md">設計考證</see>
/// 〈toast 不會把面板關掉〉;這一段以前寫著「後者會開一個搶焦點的視窗」,那是錯的)——
/// 維持 InfoBadge 只是因為這條路的訊息屬於「面板裡發生的事」,而面板此時就在前景。
///
/// 而這條路正好是整個擴展裡少數「提示看得到」的地方:**失敗的定義就是沒有任何外部視窗
/// 跳出來**,面板因此還在前景,InfoBadge 看得見也留得住。成功那條路相反 —— 編輯器一起來
/// 面板就被蓋掉了,那時發什麼都是白費,所以成功時一個字都不說,跳出來的那個視窗本身
/// 就是最好的回饋。
/// </summary>
internal sealed partial class OpenNoteFileCommand : InvokableCommand
{
    private readonly string _filePath;
    private readonly CommandResult _onSuccess;

    /// <param name="filePath">要開啟的 <c>.md</c>。</param>
    /// <param name="dismissOnSuccess">
    /// 開起來之後要不要順手把面板收掉。<b>隨手草稿一定要傳 true</b>,理由見
    /// <see cref="NoteCommands.OpenInEditor(string, bool)"/>。
    ///
    /// **只管成功那條路。** 開不起來時一律 <c>KeepOpen</c>:面板收掉的話,那則訊息
    /// 連同「什麼都沒發生」會一起消失,使用者只會以為編輯器在背景開好了。
    /// </param>
    public OpenNoteFileCommand(string filePath, bool dismissOnSuccess = false)
    {
        _filePath = filePath;
        _onSuccess = dismissOnSuccess ? CommandResult.Dismiss() : CommandResult.KeepOpen();

        // Name 一定要自己給。底部工具列那兩顆按鈕顯示的是命令的 Name,而 toolkit 自己
        // 資源檔裡的字串跟著 CmdPal 的語言走,不見得跟我們一致(實機驗證時抓到過一顆
        // 英文的 "Open")。
        Name = Resources.CommandOpenInEditor;
        Icon = Icons.OpenExternal;
    }

    public override CommandResult Invoke()
    {
        // 先擋不存在的檔案。`OpenInShell` 對它也會失敗,但分成兩句話講得清楚得多 ——
        // 「檔案不在了」跟「沒有程式能開」的下一步完全不同。
        if (!File.Exists(_filePath))
        {
            // 留痕跡:這條路唯一看得見的東西是一個 2.5 秒就收掉的 InfoBadge,
            // 事後回頭查「按了沒反應」的時候,log 是唯一還在的證據。
            DiagnosticLog.Failure("OpenNoteFile: the file no longer exists", _filePath);
            new ToastStatusMessage(Resources.OpenFileMissing).Show();
            return CommandResult.KeepOpen();
        }

        if (!ShellHelpers.OpenInShell(_filePath))
        {
            DiagnosticLog.Failure("OpenNoteFile: OpenInShell refused to launch the file", _filePath);
            new ToastStatusMessage(Resources.OpenInEditorFailed).Show();
            return CommandResult.KeepOpen();
        }

        DiagnosticLog.Write($"OpenNoteFile: handed to the shell '{_filePath}'");

        return _onSuccess;
    }
}
