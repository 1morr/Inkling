using System.Text;

namespace Inkling.Core;

/// <summary>
/// 讀筆記與隨手草稿共用的嚴格 UTF-8 解碼器:遇到無效位元組**丟例外**，不要默默換成
/// U+FFFD。
///
/// 本來只有 <see cref="FileSystemNoteRepository"/> 有這一份 —— <see cref="ScratchpadStore"/>
/// 以前用 <c>File.ReadAllText</c> 的預設(寬鬆)解碼器讀草稿檔，外部編輯器用舊 code page
/// (Big5 / GBK / Latin-1)把檔案存回去，Inkling 下一次存檔就會把讀到的一串 U+FFFD 寫回去 ——
/// 原始位元組永久消失，沒有備份、沒有提示、資源回收筒裡什麼都沒有。兩邊要認的是同一顆
/// 解碼器，因此抽出來共用，而不是各刻一份然後漂移。
///
/// 有 BOM 的檔案不受影響:<c>StreamReader</c> 仍然會先照 BOM 判編碼
/// (UTF-8 / UTF-16 LE / BE 都認得)，這個編碼只是「沒有 BOM 時的假設」。
/// </summary>
internal static class StrictUtf8
{
    public static readonly UTF8Encoding Encoding = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
}
