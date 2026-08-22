namespace Inkling.Core;

/// <summary>
/// 一則筆記,對應磁碟上的一個 Markdown 檔。
/// </summary>
public sealed record Note
{
    /// <summary>
    /// 筆記的身分,格式 <c>yyyyMMdd-HHmmss-xxxx</c>。
    /// 檔名只是給人看的,改標題不會改檔名,所以 front matter 認這個而不是檔名。
    ///
    /// <b>但「解析成哪個檔案」不看它,看 <see cref="FilePath"/>。</b> 這個 id 在磁碟上
    /// 並不保證唯一 —— OneDrive 的衝突副本是**整檔複製**,front matter 一模一樣,
    /// 同一個 id 就這樣出現在兩個檔案上。以前 <c>Update</c> / <c>Delete</c> 都經由
    /// 「用 id 查第一筆」解析目標,結果是清單列出兩列、兩列都指向同一份檔案:
    /// 選第二列按編輯,標題寫著乙、欄位帶出甲的內容,存檔寫進甲,乙一個位元組都沒動。
    /// 現在那兩個入口直接吃 <see cref="Note"/>,用它的路徑定位。
    /// </summary>
    public required string Id { get; init; }

    public required string Title { get; init; }

    /// <summary>front matter 之後的內文,不含標題。</summary>
    public required string Body { get; init; }

    public required DateTimeOffset Created { get; init; }

    public required DateTimeOffset Updated { get; init; }

    /// <summary>
    /// <c>created:</c> 原本那一行的文字,**只有在解析不出日期時才有值**;
    /// <see cref="NoteFile.Serialize"/> 會原樣寫回去,取代我們自己產的那一行。
    ///
    /// 存在的理由跟 <see cref="ExtraFrontMatter"/> 是同一條承諾:不認得的東西不要默默改掉。
    /// 沒有這個欄位的話,<c>created: 2024-01-05 (approx)</c> 在 Inkling 裡編輯一次就
    /// **永久**變成檔案系統時間,而原字串連 <see cref="ExtraFrontMatter"/> 都進不去
    /// —— 它在認得的 switch 分支裡就被消化掉了。
    /// </summary>
    public string? CreatedRaw { get; init; }

    /// <summary>
    /// <c>updated:</c> 的同一件事,見 <see cref="CreatedRaw"/>。
    ///
    /// <b>編輯存檔時這一個會被清掉</b>(<see cref="FileSystemNoteRepository.Update"/>):
    /// <c>updated</c> 的語意就是「最後改動時間」,而我們正在改它。
    /// <see cref="CreatedRaw"/> 相反,永遠留著。
    /// </summary>
    public string? UpdatedRaw { get; init; }

    /// <summary>目前不在 MVP 範圍,但格式先留著,免得日後要遷移既有檔案。</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>檔案的絕對路徑。</summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// 這個檔案不是 Inkling 建立的。
    ///
    /// 列清單時兩者一視同仁 —— 使用者自己丟進資料夾的 .md 本來就該看得到,那是資料格式的承諾。
    /// 但**批次刪除必須分得出來**:筆記資料夾要是被指到既有的 Obsidian vault 或某個
    /// 專案目錄,「刪除全部」掃到的就遠不只是 Inkling 寫過的東西。
    ///
    /// <b>判準是「id 是不是我們產的形狀」,不是「有沒有 id」</b>
    /// (<see cref="NoteFileName.IsGeneratedId"/>)。後者是原本的寫法,而 <c>id:</c> 在
    /// Obsidian / Zettelkasten / Hugo 生態裡極常見 —— 一個 <c>id: 202401051200</c> 的
    /// zettel 會被算成「Inkling 建立的」,於是「只刪 Inkling 建立的」那顆按鈕反而刪掉
    /// 使用者自己的東西,而畫面上那句「保留 N 則不是 Inkling 建立的」是假的。
    /// </summary>
    public bool IsExternal { get; init; }

    /// <summary>
    /// 資料夾裡有另一個檔案帶著同一個 <see cref="Id"/>。
    ///
    /// 幾乎只有一個成因:**雲端硬碟的衝突副本**。多台機器同時改同一則筆記時,OneDrive
    /// 會產生 <c>&lt;檔名&gt;-&lt;電腦名&gt;.md</c>,那是整檔複製,front matter 一模一樣。
    /// 編輯與刪除認路徑,所以兩份各自獨立(見 <see cref="Id"/>);但畫面上那兩列
    /// 標題一樣、副標可能也一樣,**不講出來使用者根本不會發現發生過什麼** ——
    /// 清單頁靠這個旗標打一個標籤。
    /// </summary>
    public bool HasDuplicateId { get; init; }

    /// <summary>
    /// front matter 裡 Inkling 不認得的欄位,原始文字逐行保留。
    ///
    /// 存在的理由:這些筆記是純檔案,使用者隨時可能用 Obsidian 之類的工具加上自己的
    /// metadata。如果 Inkling 編輯一次就把不認得的欄位吃掉,那就是在破壞別人的資料。
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
                    if (string.Equals(NoteBody.Truncate(line), Title, StringComparison.Ordinal))
                    {
                        continue;
                    }
                }

                var truncated = NoteBody.Truncate(line);

                return truncated.Length == line.Length ? line : truncated + "…";
            }

            return string.Empty;
        }
    }
}
