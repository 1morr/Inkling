using System.Runtime.InteropServices;
using System.Text;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace Inkling;

/// <summary>
/// 排錯用的檔案紀錄，預設關閉。
///
/// 為什麼不是 <c>Debug.WriteLine</c>:它掛著 <c>[Conditional("DEBUG")]</c>,
/// Release 建置會整個編掉，而日常安裝的就是 Release。擴展又跑在自己的 COM 進程裡，
/// 沒有主控台可看 —— 要確認某段程式到底有沒有被執行到，只剩下寫檔這條路。
///
/// 開啟方式:在筆記資料夾旁邊的設定資料夾裡建一個空檔 <c>diagnostic.on</c>,
/// 重載擴展即可。位置就是下面 <see cref="Folder"/> 印出來的那個。
/// 沒有那個檔案時，<see cref="Write"/> 的每次呼叫只是一個布林判斷。
///
/// <para><b>兩個通道，兩種隱私等級 —— 這是這個型別最重要的一件事。</b></para>
///
/// <list type="bullet">
/// <item><see cref="Write"/> → 只寫本機的 <c>diagnostic.log</c>，而且**預設是關的**。
/// 使用者要自己建一個檔案才會開始記，所以這裡放什麼都是他自己開的。</item>
/// <item><see cref="Failure"/> → 另外送一份給 CmdPal 自己的 log
/// (<c>%LOCALAPPDATA%\Microsoft\PowerToys\CmdPal\Logs\&lt;版本&gt;\</c>)。那份**永遠開著**、
/// **所有擴展共用**，而且 PowerToys 的 Bug Report Tool 會把整個資料夾打包 ——
/// 使用者拿去貼在 <c>microsoft/PowerToys</c> 的**公開** issue 上，完全不會經過我們自己的
/// issue 範本與那裡的遮蔽提醒。</item>
/// </list>
///
/// 所以 <see cref="Failure"/> 的第一個參數是**去識別化的摘要**(失敗種類),
/// 完整路徑、例外全文這類東西一律走第二個參數 <c>detail</c>，只進本機那一份。
/// 筆記的檔案路徑同時帶著**筆記標題**與(經 <c>%OneDrive%</c> / <c>Documents</c>)
/// **Windows 使用者名字**，那不該進一個公開通道。
///
/// 訊息一律**英文**:這是 log(見 CLAUDE.md〈慣例〉)，而共用那一份會被 PowerToys
/// 的維護者拿去 triage 別人的 bug —— <c>[Inkling]</c> 前綴認得出是誰寫的，
/// 訊息本身除了我們沒人讀得懂就白寫了。
/// </summary>
internal static class DiagnosticLog
{
    /// <summary>
    /// <c>diagnostic.log</c> 的大小上限。超過就把現有的搬成 <c>.1</c> 重新開始。
    ///
    /// 為什麼需要:清單頁每次重建(≈每個按鍵)就寫一行，而使用者照排錯指示建了
    /// <c>diagnostic.on</c> 之後多半不會記得刪。沒有上限的話那個檔案會一直長，
    /// 而它裡面有使用者打過的每一個查詢字串 —— 附進 bug report 等於交出搜尋歷史。
    ///
    /// 留一代(<c>.1</c>)而不是直接砍掉:失敗現場前面那幾行往往才是線索，
    /// 而剛好在輪替點失敗的話，直接砍等於把它扔了。
    /// </summary>
    private const long MaxLogBytes = 2 * 1024 * 1024;

    private static readonly Lock Gate = new();

    private static readonly string Folder = Utilities.BaseSettingsPath("Inkling");

    private static readonly bool Enabled = File.Exists(Path.Combine(Folder, "diagnostic.on"));

    private static readonly string LogPath = Path.Combine(Folder, "diagnostic.log");

    private static readonly string PreviousLogPath = LogPath + ".1";

    /// <summary>
    /// 記一筆失敗。<paramref name="summary"/> 進兩個通道，<paramref name="detail"/> 只進本機那一份。
    ///
    /// **只給真的失敗用。** CmdPal 那份 log 是所有擴展共用的，把每次 <c>GetItems</c>
    /// 都寫進去只會把別人的線索淹掉 —— 追蹤性質的訊息走 <see cref="Write"/>。
    /// </summary>
    /// <param name="summary">
    /// 去識別化的失敗描述:**不要**帶檔案路徑、筆記標題、使用者打的字或例外全文。
    /// 例外只放型別名(<c>ex.GetType().Name</c>)—— 那已經足以分辨是哪一類問題。
    /// </param>
    /// <param name="detail">路徑、例外全文這些查起來才有用、但不能公開的東西。</param>
    public static void Failure(string summary, string? detail = null)
    {
        try
        {
            // 沒接到 host 時 toolkit 自己就靜靜跳過(那條路曾經整個是死的，
            // 見 InklingCommandsProvider.InitializeWithHost)。
            ExtensionHost.LogMessage($"[Inkling] {summary}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            // 跨進程呼叫，CmdPal 走掉之後 proxy 就死了。排錯工具自己壞掉不該影響功能。
            Write($"DiagnosticLog.Failure: could not reach the host ({ex.GetType().Name})");
        }

        Write(detail is null ? summary : $"{summary} — {detail}");
    }

    public static void Write(string message)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                Rotate();
                File.AppendAllText(
                    LogPath,
                    FormattableString.Invariant($"{DateTimeOffset.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}"),
                    Encoding.UTF8);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 排錯工具自己壞掉不該影響功能。
        }
    }

    /// <summary>超過上限就搬成 <c>.1</c>。呼叫端已經持有 <see cref="Gate"/>。</summary>
    private static void Rotate()
    {
        var info = new FileInfo(LogPath);

        if (!info.Exists || info.Length < MaxLogBytes)
        {
            return;
        }

        // Move 的 overwrite 會把上一代蓋掉，所以永遠只有兩個檔案。
        File.Move(LogPath, PreviousLogPath, overwrite: true);
    }
}
