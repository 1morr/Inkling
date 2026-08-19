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
    /// 這個檔案不是 Notelet 建立的:front matter 裡沒有 id,<see cref="Id"/> 是從路徑推導出來的。
    ///
    /// 列清單時兩者一視同仁 —— 使用者自己丟進資料夾的 .md 本來就該看得到,那是資料格式的承諾。
    /// 但**批次刪除必須分得出來**:筆記資料夾要是被指到既有的 Obsidian vault 或某個
    /// 專案目錄,「刪除全部」掃到的就遠不只是 Notelet 寫過的東西。
    /// </summary>
    public bool IsExternal { get; init; }

    /// <summary>
    /// front matter 裡 Notelet 不認得的欄位,原始文字逐行保留。
    ///
    /// 存在的理由:這些筆記是純檔案,使用者隨時可能用 Obsidian 之類的工具加上自己的
    /// metadata。如果 Notelet 編輯一次就把不認得的欄位吃掉,那就是在破壞別人的資料。
    /// 寫回時這些行會原樣輸出。
    /// </summary>
    public IReadOnlyList<string> ExtraFrontMatter { get; init; } = [];

    /// <summary>
    /// 給清單頁用的一行摘要。取內文第一行有效文字(跳過程式碼圍欄、水平線與
    /// 表格分隔列),沒有內文就留空。
    ///
    /// 標題是從內文第一行推導出來的時候(沒有 front matter 的外來檔案),
    /// 那一行已經顯示在標題欄了 —— 摘要從它之後開始取,免得清單上同一句話出現兩次。
    /// </summary>
    public string Summary
    {
        get
        {
            var isFirstLine = true;

            foreach (var line in NoteBody.ContentLines(Body))
            {
                if (isFirstLine)
                {
                    isFirstLine = false;

                    // 推導標題有 120 字的截斷,比對時要套用同樣的截斷才對得上。
                    var comparable = line.Length > NoteBody.MaxLineLength ? line[..NoteBody.MaxLineLength] : line;
                    if (string.Equals(comparable, Title, StringComparison.Ordinal))
                    {
                        continue;
                    }
                }

                return line.Length > NoteBody.MaxLineLength
                    ? string.Concat(line.AsSpan(0, NoteBody.MaxLineLength), "…")
                    : line;
            }

            return string.Empty;
        }
    }
}
