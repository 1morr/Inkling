namespace Notelet.Core;

/// <summary>
/// 快速新增的觸發判斷。
///
/// 放在 Core 而不是 UI 層,是因為這裡的規則(前綴、要不要吃空白、什麼情況不該觸發)
/// 是整個功能最容易出錯的地方,而 Command Palette 的 UI 沒辦法自動化測試。
/// </summary>
public static class QuickCapture
{
    /// <summary>
    /// 從使用者在主搜尋框打的字裡抽出要記下的內容。
    /// 回傳 null 代表這句查詢不是要快速新增,fallback 項目應該把自己藏起來。
    /// </summary>
    public static string? ExtractText(string? query, NoteletOptions options)
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

        var text = query[prefix.Length..].Trim();

        return text.Length == 0 ? null : text;
    }
}
