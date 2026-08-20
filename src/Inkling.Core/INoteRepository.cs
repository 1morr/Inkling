namespace Inkling.Core;

/// <summary>
/// 筆記的讀寫。刻意不含任何 Command Palette 型別,UI 層才好抽換、測試才好寫。
/// </summary>
public interface INoteRepository
{
    /// <summary>
    /// 資料夾內容在 Inkling 之外被改動時觸發,已做去抖動。
    ///
    /// 這正是多端同步在 UI 上的體現:另一台機器記下的想法經 OneDrive 同步下來、
    /// 或是使用者拿別的編輯器改了檔案,清單頁都會自己更新,不必手動重新整理。
    /// </summary>
    event EventHandler? Changed;

    /// <summary>
    /// 每次內容可能變動就會改變的號碼。
    ///
    /// UI 層拿它判斷自己的項目快取還新不新。為什麼不能只靠 <see cref="Changed"/>:
    /// 那個事件來自 FileSystemWatcher,而 watcher 要等資料夾存在才掛得上去 ——
    /// 第一次用的時候筆記資料夾根本還沒被建出來。純推播會漏掉那一段,
    /// 結果就是筆記存好了、清單卻還顯示空的。這個號碼是拉取式的,不依賴事件送達時機。
    /// </summary>
    int Version { get; }

    /// <summary>取得全部筆記,依最後更新時間遞減排序。</summary>
    IReadOnlyList<Note> GetAll();

    Note? GetById(string id);

    /// <summary>新增一則筆記並立刻寫檔。</summary>
    Note Create(string title, string body);

    /// <summary>就地更新既有筆記。id、created 與不認得的 front matter 欄位都會保留。</summary>
    Note Update(string id, string title, string body);

    /// <summary>
    /// 刪除一則筆記。找不到 id 時丟 <see cref="NoteNotFoundException"/>。
    ///
    /// 檔案怎麼消失的由 <see cref="IFileDeleter"/> 決定 —— 正式跑起來是送進資源回收筒,
    /// 測試裡則是直接刪掉。這一層只管快取要失效。
    /// </summary>
    void Delete(string id);

    /// <summary>
    /// 刪掉指定的那些筆記,回傳實際刪掉幾則。
    ///
    /// 為什麼是「刪這些」而不是「全刪」:範圍是呼叫端的判斷。使用者可能只想清掉
    /// Inkling 自己建的那些,留下別的工具丟進資料夾的 .md ——
    /// 見 <see cref="Note.IsExternal"/>。這一層只負責刪。
    ///
    /// 個別檔案刪不掉(被別的程式鎖住、權限不對)不會中斷整批 —— 回傳值比總數少
    /// 就代表有漏網的,由呼叫端決定怎麼講。半途丟例外只會留下一個「刪一半」的狀態,
    /// 而且使用者不知道刪到哪裡。
    /// </summary>
    int DeleteMany(IEnumerable<Note> notes);
}
