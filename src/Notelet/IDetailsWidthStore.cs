using Microsoft.CommandPalette.Extensions;

namespace Notelet;

/// <summary>
/// 詳細窗格寬度的存放處。
///
/// 清單頁只需要這麼一個屬性,不必認識整個設定管理員 —— 這樣它的相依關係看一眼就清楚,
/// 測試(如果哪天 UI 層也能測)也不必生出一份 settings.json。
/// </summary>
internal interface IDetailsWidthStore
{
    /// <summary>設下去就會立刻存檔,下次啟動照這個值。</summary>
    ContentSize DetailsWidth { get; set; }

    /// <summary>
    /// 寬度變了,**不管是誰改的** —— 設定頁按 Save,或清單頁按 Ctrl+D,兩條路都會發。
    ///
    /// 為什麼需要這條路,而不是讓 provider 整組重建就好:CmdPal 手上握著的是使用者
    /// 當下開著的那個頁面實例,重建出來的新頁面它根本不會去拿。實測 log 顯示
    /// <c>BuildState</c> 跑完之後一次 <c>GetItems</c> 都沒有,舊實例的項目快取
    /// (查詢字串與 Version 都沒變)就這樣把舊寬度一路留到 Reload 為止。
    ///
    /// 兩邊都要收得到,因為兩邊都顯示這個值:清單頁是窗格本身的寬度,設定頁是那個下拉選單。
    /// 訂閱者自己比對新舊值,發現是自己剛改的就別再動一次(見 <c>NoteListPage</c>)。
    /// </summary>
    event EventHandler? DetailsWidthChanged;
}
