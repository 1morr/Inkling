namespace Inkling.Core;

/// <summary>
/// 快速新增解析出來的一則筆記。
///
/// 刻意用 record class 而不是 struct:UI 層那個共用的命令物件會在使用者每按一個鍵時
/// 被改寫，而按下 Enter 是另一次跨進程呼叫，兩者不保證在同一個執行緒。
/// 參考型別的指派是原子的，標題與內文不會拆開來看到一半。
/// </summary>
public sealed record QuickCaptureDraft(string Title, string Body);

/// <summary>
/// 快速記下的內容切分。
///
/// 放在 Core 而不是 UI 層，是因為「什麼情況算一則筆記、什麼情況只是打到一半」
/// 是這個功能最容易出錯的地方，而 Command Palette 的 UI 沒辦法自動化測試。
///
/// 這裡沒有前綴判斷:唯一的入口是快速記下頁，使用者靠 alias 進到那一頁就已經
/// 表達過意圖了，打什麼就記什麼。曾經有一條走主搜尋框 fallback 的路，那條路
/// 得先用前綴確認意圖 —— 為什麼放棄，見 docs/design-notes.md〈快速記下為什麼是頁面，不是 fallback〉。
/// </summary>
public static class QuickCapture
{
    /// <summary>
    /// 沒有設定時用的分隔符。
    ///
    /// 為什麼是兩個半形分號:`;` 在 home row 上，右手小指原位，不用按 Shift，連打兩下最快;
    /// 而**連續兩個**分號在自然語句裡幾乎不出現，所以標題可以自由使用單一個分號
    /// (`for (var i = 0; i < 10; i++)`、中文句子的頓隔)。要求連打兩次，誤觸的成本就沒了。
    ///
    /// 唯一真的會撞到的是 C 系的無限迴圈寫法 `for (;;)` —— 常寫那種筆記的人可以在設定裡
    /// 換成 `,,`(碰撞更少，鍵位一樣不用 Shift)。設定欄位的說明就是這樣寫的。
    /// </summary>
    public const string DefaultSeparator = ";;";

    /// <summary>
    /// 從一段已經確定是「要記下來」的文字裡切出標題與內文，不做任何前綴判斷。
    /// 回傳 null 代表這段文字還構不成一則筆記(空白、或只有分隔符後面的內文)。
    /// </summary>
    /// <param name="separator">
    /// 使用者自訂的分隔符;null / 空白代表用 <see cref="DefaultSeparator"/>。
    /// 比對時半形全形視為同一個字，見 <see cref="Fold"/>。
    /// </param>
    public static QuickCaptureDraft? Split(string? text, string? separator = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var marker = NormalizeSeparator(separator);
        var index = IndexOfSeparator(text, marker);

        var title = (index < 0 ? text : text[..index]).Trim();
        var body = index < 0 ? string.Empty : text[(index + marker.Length)..].Trim();

        // 沒有標題就沒有筆記 —— 只打了分隔符跟內文(「;;內容」)也不觸發，
        // 那多半是還在打字的中間狀態，不是使用者的意圖。
        return title.Length == 0 ? null : new QuickCaptureDraft(title, body);
    }

    /// <summary>
    /// 把設定裡讀到的分隔符整理成真正拿去比對的那一個。設定頁存檔前也走這裡，
    /// 所以輸入框裡顯示的永遠就是實際生效的值。
    ///
    /// 為什麼要 <c>Trim</c>:這個值是從 Adaptive Cards 的單行輸入框來的，而尾隨空白
    /// 在那種框裡**完全看不見**。複製貼上多帶一個空格，分隔符就從此再也切不動，
    /// 而使用者盯著設定頁只會看到一個長得完全正確的值 —— 這種無聲失效比「不支援
    /// 前後帶空白的分隔符」糟糕得多。真想要「前後有空格」的效果，標題與內文本來就會
    /// 各自 <c>Trim</c> 一次，加不加空白沒有差別。
    /// </summary>
    public static string NormalizeSeparator(string? separator)
    {
        var trimmed = separator?.Trim() ?? string.Empty;

        return trimmed.Length == 0 ? DefaultSeparator : trimmed;
    }

    /// <summary>找出第一組分隔符的位置，沒有就回傳 -1。</summary>
    private static int IndexOfSeparator(string text, string separator)
    {
        for (var i = 0; i + separator.Length <= text.Length; i++)
        {
            if (MatchesAt(text, i, separator))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool MatchesAt(string text, int start, string separator)
    {
        for (var offset = 0; offset < separator.Length; offset++)
        {
            if (Fold(text[start + offset]) != Fold(separator[offset]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 把全形 ASCII 折回半形，好讓 <c>;;</c> 與 <c>；；</c>、<c>;；</c> 算同一個分隔符。
    ///
    /// 中文輸入法打出來的就是全形，而中英切換的當下打出哪一個並不受控 ——
    /// 為了這種事讓一則筆記存錯太不值得。設定欄位那一頭也走同一個折算，
    /// 所以使用者在設定裡填 <c>；；</c>、打字時打 <c>;;</c> 一樣切得開。
    ///
    /// 這個對照是**逐字元、長度不變**的(全形 ASCII U+FF01–U+FF5E 與半形一一對應，
    /// 差值固定 0xFEE0)，所以折算後的索引可以直接拿去切原字串，不必另外換算位置。
    /// 沒有半形對應的中文標點(<c>、</c> <c>。</c>)不在範圍內，填那些就只認它自己。
    ///
    /// 碼位寫成數字而不是字面字元:全形標點在等寬字型裡跟半形難以分辨，而全形空白根本
    /// 看不見 —— 跟 <c>Icons.cs</c> 不用 <c>\uXXXX</c> 是同一類理由，那種字元經不起
    /// 文字處理工具轉手。
    /// </summary>
    private static char Fold(char c) => c switch
    {
        >= (char)0xFF01 and <= (char)0xFF5E => (char)(c - 0xFEE0),
        _ => c,
    };
}
