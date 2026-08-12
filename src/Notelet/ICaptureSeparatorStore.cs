namespace Notelet;

/// <summary>
/// 快速記下用的分隔符存放處。
///
/// 跟 <see cref="IDetailsWidthStore"/> 同一個形狀,理由也一樣:快速記下頁只需要這麼一個字串,
/// 不必認識整個設定管理員。
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
    /// 為什麼需要這條路,而不是讓 provider 整組重建就好:CmdPal 手上握著的是使用者當下
    /// 開著的那個頁面實例,重建出來的新頁面它根本不會去拿 ——
    /// 完整的說明見 <see cref="IDetailsWidthStore.DetailsWidthChanged"/>。
    ///
    /// 只有資料夾**沒變**的時候這條路才是唯一的更新管道;資料夾也一起變了的話,
    /// provider 會整組重建,新的頁面本來就會讀到新值。
    /// </summary>
    event EventHandler? CaptureSeparatorChanged;
}
