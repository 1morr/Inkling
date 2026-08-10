namespace Notelet.Core;

/// <summary>
/// 一則筆記,對應磁碟上的一個 Markdown 檔。
/// </summary>
public sealed record Note
{
    /// <summary>
    /// 筆記的身分,格式 <c>yyyyMMdd-HHmmss-xxxx</c>。
    /// 檔名只是給人看的,改標題不會改檔名,所以身分認這個而不是檔名。
    /// </summary>
    public required string Id { get; init; }

    public required string Title { get; init; }

    /// <summary>front matter 之後的內文,不含標題。</summary>
    public required string Body { get; init; }

    public required DateTimeOffset Created { get; init; }

    public required DateTimeOffset Updated { get; init; }

    /// <summary>目前不在 MVP 範圍,但格式先留著,免得日後要遷移既有檔案。</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>檔案的絕對路徑。</summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// front matter 裡 Notelet 不認得的欄位,原始文字逐行保留。
    ///
    /// 存在的理由:這些筆記是純檔案,使用者隨時可能用 Obsidian 之類的工具加上自己的
    /// metadata。如果 Notelet 編輯一次就把不認得的欄位吃掉,那就是在破壞別人的資料。
    /// 寫回時這些行會原樣輸出。
    /// </summary>
    public IReadOnlyList<string> ExtraFrontMatter { get; init; } = [];

    /// <summary>
    /// 給清單頁用的一行摘要。優先取內文第一行有內容的文字,沒有內文就留空。
    /// </summary>
    public string Summary
    {
        get
        {
            foreach (var line in Body.Split('\n'))
            {
                var trimmed = line.Trim().TrimStart('#', '>', '-', '*', ' ').Trim();
                if (trimmed.Length > 0)
                {
                    return trimmed.Length > 120 ? string.Concat(trimmed.AsSpan(0, 120), "…") : trimmed;
                }
            }

            return string.Empty;
        }
    }
}
