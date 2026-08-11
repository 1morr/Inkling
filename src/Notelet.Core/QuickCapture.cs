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
/// 快速新增的觸發判斷與內容切分。
///
/// 放在 Core 而不是 UI 層,是因為這裡的規則(前綴、要不要吃空白、什麼情況不該觸發)
/// 是整個功能最容易出錯的地方,而 Command Palette 的 UI 沒辦法自動化測試。
/// </summary>
public static class QuickCapture
{
    /// <summary>
    /// 標題與內文的分隔符。
    ///
    /// 全形分號也算數:中文輸入法打出來的就是它,要人為了分隔符特地切回半形太荒謬。
    /// 代價是標題裡不能出現分號 —— 需要分號的標題請走完整表單。
    /// </summary>
    private static readonly char[] Separators = [';', '；'];

    /// <summary>
    /// 從使用者在主搜尋框打的字裡抽出要記下的內容。
    /// 回傳 null 代表這句查詢不是要快速新增,fallback 項目應該把自己藏起來。
    /// </summary>
    public static QuickCaptureDraft? Parse(string? query, NoteletOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.QuickCaptureEnabled || string.IsNullOrEmpty(query))
        {
            return null;
        }

        var prefix = options.QuickCapturePrefix;

        // 沒有前綴就等於每一次搜索都要冒出來,太吵,一律不觸發。
        if (prefix.Length == 0 || !query.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var text = query[prefix.Length..];
        var separator = text.IndexOfAny(Separators);

        var title = (separator < 0 ? text : text[..separator]).Trim();
        var body = separator < 0 ? string.Empty : text[(separator + 1)..].Trim();

        // 沒有標題就沒有筆記 —— 只打了分號跟內文(「n ;內容」)也不觸發,
        // 那多半是還在打字的中間狀態,不是使用者的意圖。
        return title.Length == 0 ? null : new QuickCaptureDraft(title, body);
    }
}
