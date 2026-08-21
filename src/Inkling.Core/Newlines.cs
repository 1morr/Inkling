namespace Inkling.Core;

/// <summary>
/// 換行符的正規化,寫進磁碟的每一份文字都走這裡。
///
/// 只有一份實作是刻意的:規則本身很簡單,但「在記憶體裡一律 LF、寫出去一律 CRLF」
/// 這個約定一旦有兩份就會漂移,而漂移的症狀很難看 —— Adaptive Cards 的多行輸入框
/// 送回來的換行是<b>裸 CR</b>(底下那個 WinUI <c>TextBox</c> 的行為),原樣落到磁碟上,
/// 使用者用外部編輯器打開就是擠成一行的一大塊字。
/// </summary>
internal static class Newlines
{
    /// <summary>
    /// 一律折成 LF。CRLF 與裸 CR 都算一個換行 ——
    /// 裸 CR 是 Adaptive Cards 那一頭送回來的形狀,不是使用者打錯。
    /// </summary>
    public static string ToLf(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

    /// <summary>
    /// 寫出去之前折成 CRLF。Windows 上的編輯器對它比較友善。
    /// 傳進來的必須是已經過 <see cref="ToLf"/> 的文字。
    /// </summary>
    public static string ToCrlf(string lfText) =>
        lfText.Replace("\n", "\r\n", StringComparison.Ordinal);
}
