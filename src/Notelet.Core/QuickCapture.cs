namespace Notelet.Core;

/// <summary>
/// 快速新增解析出來的一則筆記。
///
/// 刻意用 record class 而不是 struct:UI 層那個共用的命令物件會在使用者每按一個鍵時
/// 被改寫,而按下 Enter 是另一次跨進程呼叫,兩者不保證在同一個執行緒。
/// 參考型別的指派是原子的,標題與內文不會拆開來看到一半。
/// </summary>
public sealed record QuickCaptureDraft(string Title, string Body);

/// <summary>
/// 快速記下的內容切分。
///
/// 放在 Core 而不是 UI 層,是因為「什麼情況算一則筆記、什麼情況只是打到一半」
/// 是這個功能最容易出錯的地方,而 Command Palette 的 UI 沒辦法自動化測試。
///
/// 這裡沒有前綴判斷:唯一的入口是快速記下頁,使用者靠 alias 進到那一頁就已經
/// 表達過意圖了,打什麼就記什麼。曾經有一條走主搜尋框 fallback 的路,那條路
/// 得先用前綴確認意圖 —— 為什麼放棄,見 README〈快速記下為什麼是頁面,不是 fallback〉。
/// </summary>
public static class QuickCapture
{
    /// <summary>
    /// 算得上分隔符的字元。要**連續兩個**才會切(見 <see cref="IndexOfSeparator"/>)。
    ///
    /// 全形分號也算數:中文輸入法打出來的就是它,要人為了分隔符特地切回半形太荒謬。
    /// 半形全形還可以混著用(「;；」也切) —— 中英切換的當下打出哪一個並不受控,
    /// 為了這種事讓一則筆記存錯太不值得。
    /// </summary>
    private static readonly char[] SeparatorChars = [';', '；'];

    /// <summary>
    /// 從一段已經確定是「要記下來」的文字裡切出標題與內文,不做任何前綴判斷。
    /// 回傳 null 代表這段文字還構不成一則筆記(空白、或只有分號後面的內文)。
    /// </summary>
    public static QuickCaptureDraft? Split(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var separator = IndexOfSeparator(text);

        var title = (separator < 0 ? text : text[..separator]).Trim();

        // 分隔符是兩個字元,所以內文從 +2 開始。
        var body = separator < 0 ? string.Empty : text[(separator + 2)..].Trim();

        // 沒有標題就沒有筆記 —— 只打了分隔符跟內文(「;;內容」)也不觸發,
        // 那多半是還在打字的中間狀態,不是使用者的意圖。
        return title.Length == 0 ? null : new QuickCaptureDraft(title, body);
    }

    /// <summary>
    /// 找出第一組連續兩個分號的位置,沒有就回傳 -1。
    ///
    /// 為什麼要兩個而不是一個:單一個分號在筆記標題裡太常見了(程式碼、清單、
    /// 中文句子裡的頓隔),要求連打兩次才切,標題就能自由使用分號 ——
    /// 一個人不會無意間打出兩個相連的分號。
    /// </summary>
    private static int IndexOfSeparator(string text)
    {
        for (var i = 0; i + 1 < text.Length; i++)
        {
            if (Array.IndexOf(SeparatorChars, text[i]) >= 0
                && Array.IndexOf(SeparatorChars, text[i + 1]) >= 0)
            {
                return i;
            }
        }

        return -1;
    }
}
