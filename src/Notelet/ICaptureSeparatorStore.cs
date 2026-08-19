namespace Notelet;

/// <summary>
/// 快速記下用的分隔符存放處。
///
/// 快速記下頁只需要這麼一個字串,不必認識整個設定管理員 —— 這樣它的相依關係看一眼就清楚,
/// 測試(如果哪天 UI 層也能測)也不必生出一份 settings.json。
/// <see cref="ICapturePreviewStore"/> 是同一個形狀。
///
/// 目前只有兩項設定走這個模式,一項一個窄介面還划算(各自的事件說明有地方掛)。
/// **第三項出現時請考慮收成泛型 <c>ISettingValue&lt;T&gt;</c>(Value + Changed)** ——
/// 「為什麼不能靠重建頁面生效」那份說明到時候搬到泛型介面上講一次就好。
/// </summary>
internal interface ICaptureSeparatorStore
{
    /// <summary>
    /// 已經整理過的分隔符(空白會被去掉,空值會退回預設值),拿到手就能直接用。
    /// </summary>
    string CaptureSeparator { get; }

    /// <summary>
    /// 使用者在設定頁改了分隔符。
    ///
    /// 為什麼需要這條路,而不是讓 provider 整組重建就好:CmdPal 手上握著的是使用者
    /// 當下開著的那個頁面實例,重建出來的新頁面它根本不會去拿。實測 log 顯示
    /// <c>BuildState</c> 跑完之後一次 <c>GetItems</c> 都沒有,舊實例的項目快取
    /// (查詢字串與 Version 都沒變)就這樣把舊值一路留到 Reload 為止。
    /// 硬重建反而更糟 —— 會把還在被使用的 repository 給 Dispose 掉。
    ///
    /// 所以這類設定一律讓**現有頁面自己響應**。頁面上快取項目的地方,
    /// 快取鍵也要帶上那個設定值,否則事件收到了、拿到的還是舊結果。
    ///
    /// 只有資料夾**沒變**的時候這條路才是唯一的更新管道;資料夾也一起變了的話,
    /// provider 會整組重建,新的頁面本來就會讀到新值。
    /// </summary>
    event EventHandler? CaptureSeparatorChanged;
}
