using System.Runtime.InteropServices;
using System.Text;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace Inkling;

/// <summary>
/// 排錯用的檔案紀錄,預設關閉。
///
/// 為什麼不是 <c>Debug.WriteLine</c>:它掛著 <c>[Conditional("DEBUG")]</c>,
/// Release 建置會整個編掉,而日常安裝的就是 Release。擴展又跑在自己的 COM 進程裡,
/// 沒有主控台可看 —— 要確認某段程式到底有沒有被執行到,只剩下寫檔這條路。
///
/// 開啟方式:在筆記資料夾旁邊的設定資料夾裡建一個空檔 <c>diagnostic.on</c>,
/// 重載擴展即可。位置就是下面 <see cref="Folder"/> 印出來的那個。
/// 沒有那個檔案時,<see cref="Write"/> 的每次呼叫只是一個布林判斷。
///
/// **失敗要用 <see cref="Failure"/>,不要用 <see cref="Write"/>。** 這裡預設是關的,
/// 也就是說使用者回報問題時,失敗現場多半根本沒有被記下來 —— 而要他先建一個空檔、
/// 重載、重現一次,門檻高到大部分人不會做。<see cref="Failure"/> 另外送一份給
/// CmdPal 自己的 log(<c>%LOCALAPPDATA%\Microsoft\PowerToys\CmdPal\Logs\&lt;版本&gt;\</c>),
/// 那份**永遠開著**,而且 PowerToys 的問題回報本來就會收集它。
/// </summary>
internal static class DiagnosticLog
{
    private static readonly Lock Gate = new();

    private static readonly string Folder = Utilities.BaseSettingsPath("Inkling");

    private static readonly bool Enabled = File.Exists(Path.Combine(Folder, "diagnostic.on"));

    private static readonly string LogPath = Path.Combine(Folder, "diagnostic.log");

    /// <summary>
    /// 記一筆失敗。同時進 CmdPal 自己的 log(永遠開著)與本地的 <c>diagnostic.log</c>
    /// (預設關著)。
    ///
    /// **只給真的失敗用。** CmdPal 那份 log 是所有擴展共用的,把每次 <c>GetItems</c>
    /// 都寫進去只會把別人的線索淹掉 —— 追蹤性質的訊息走 <see cref="Write"/>。
    ///
    /// 前綴帶擴展名字:那份 log 裡混著所有擴展的訊息,沒有前綴就不知道是誰寫的。
    /// </summary>
    public static void Failure(string message)
    {
        try
        {
            // 沒接到 host 時 toolkit 自己就靜靜跳過(那條路曾經整個是死的,
            // 見 InklingCommandsProvider.InitializeWithHost)。
            ExtensionHost.LogMessage($"[Inkling] {message}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            // 跨進程呼叫,CmdPal 走掉之後 proxy 就死了。排錯工具自己壞掉不該影響功能。
            Write($"DiagnosticLog.Failure: 送不出去({ex.GetType().Name})");
        }

        Write(message);
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
}
