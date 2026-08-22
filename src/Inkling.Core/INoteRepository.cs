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

    /// <summary>
    /// 上一次掃描中因為讀不到而略過的檔案數(被別的程式鎖住、編碼壞掉)。
    ///
    /// **放在介面上是刻意的**:清單頁要拿它去多加一列。少了那一列,一個讀不出來的
    /// <c>.md</c> 就從畫面上靜靜消失,而使用者沒有任何線索 —— 這個專案在別的地方
    /// 都不允許那種靜默(清單被截斷時就會明講「還有幾則」)。
    /// 這個數字算完卻沒有任何消費者的狀態維持了很久。
    /// </summary>
    int SkippedFileCount { get; }

    /// <summary>取得全部筆記,依最後更新時間遞減排序。</summary>
    IReadOnlyList<Note> GetAll();

    /// <summary>
    /// 用**檔案路徑**重新取一則筆記的最新內容,找不到就回 null。
    ///
    /// 這是「我手上有一份快照,給我磁碟上現在的樣子」唯一的入口。
    /// <b>刻意不是用 id 查</b> —— id 在磁碟上不保證唯一(雲端硬碟的衝突副本是整檔複製),
    /// 用 id 查會拿到「同一個 id 的第一筆」,而那不一定是使用者選中的那一份。
    /// 理由與踩過的坑寫在 <see cref="Note.Id"/> 上。
    /// </summary>
    Note? GetByPath(string filePath);

    /// <summary>新增一則筆記並立刻寫檔。</summary>
    Note Create(string title, string body);

    /// <summary>
    /// 就地更新既有筆記。id、created 與不認得的 front matter 欄位都會保留。
    ///
    /// 吃的是 <see cref="Note"/> 而不是 id:目標檔案由 <see cref="Note.FilePath"/> 決定
    /// (見 <see cref="GetByPath"/>)。內容仍會重新從磁碟讀一次,所以傳一份舊快照進來也安全。
    /// 檔案已經不在了就丟 <see cref="NoteNotFoundException"/>。
    /// </summary>
    Note Update(Note note, string title, string body);

    /// <summary>
    /// 刪除一則筆記。檔案已經不在了就丟 <see cref="NoteNotFoundException"/>。
    ///
    /// 跟 <see cref="Update"/> 同一個理由吃 <see cref="Note"/> 而不是 id。
    /// 檔案怎麼消失的由 <see cref="IFileDeleter"/> 決定 —— 正式跑起來是送進資源回收筒,
    /// 測試裡則是直接刪掉。這一層只管快取要失效。
    /// </summary>
    void Delete(Note note);

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
