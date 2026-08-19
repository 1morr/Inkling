namespace Notelet;

/// <summary>
/// 「記下之後要不要先看一眼」這個開關的存放處。
///
/// 跟 <see cref="ICaptureSeparatorStore"/> 同一個形狀,理由也一樣:快速記下頁只需要
/// 這麼一個布林值,不必認識整個設定管理員。
/// </summary>
internal interface ICapturePreviewStore
{
    /// <summary>
    /// true 代表 Enter 記下之後停在筆記的預覽頁,再按一次 Enter 才收起;
    /// false 是記完直接收起(toast + 關掉)。
    ///
    /// 同一時間**只有一條路**在:另一條不會掛到 Ctrl+Enter,也不會出現在選單裡。
    /// 預設是開(見 SettingsManager 的建構,與 docs/design-notes.md〈記下之後要不要先看一眼〉的
    /// 「為什麼預設是看一眼」)—— 「叫出來、打字、Enter」一次到底是**關掉**這個開關
    /// 的理由,寫在這裡免得被當成預設值的依據。
    /// </summary>
    bool ShowCapturePreview { get; }

    /// <summary>
    /// 使用者在設定頁改了這個開關。
    ///
    /// 為什麼需要這條路,而不是讓 provider 整組重建就好:CmdPal 手上握著的是使用者當下
    /// 開著的那個頁面實例,重建出來的新頁面它根本不會去拿 ——
    /// 完整的說明見 <see cref="ICaptureSeparatorStore.CaptureSeparatorChanged"/>。
    /// </summary>
    event EventHandler? CapturePreviewChanged;
}
