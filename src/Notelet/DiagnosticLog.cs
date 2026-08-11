using System.Text;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace Notelet;

/// <summary>
/// 排錯用的檔案紀錄,預設關閉。
///
/// 為什麼不是 <c>Debug.WriteLine</c>:它掛著 <c>[Conditional("DEBUG")]</c>,
/// Release 建置會整個編掉,而日常安裝的就是 Release。擴展又跑在自己的 COM 進程裡,
/// 沒有主控台可看 —— 要確認某段程式到底有沒有被執行到,只剩下寫檔這條路。
///
/// 開啟方式:在筆記資料夾旁邊的設定資料夾裡建一個空檔 <c>diagnostic.on</c>,
/// 重載擴展即可。位置就是下面 <see cref="Folder"/> 印出來的那個。
/// 沒有那個檔案時,這裡的每次呼叫只是一個布林判斷。
/// </summary>
internal static class DiagnosticLog
{
    private static readonly Lock Gate = new();

    private static readonly string Folder = Utilities.BaseSettingsPath("Notelet");

    private static readonly bool Enabled = File.Exists(Path.Combine(Folder, "diagnostic.on"));

    private static readonly string LogPath = Path.Combine(Folder, "diagnostic.log");

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
