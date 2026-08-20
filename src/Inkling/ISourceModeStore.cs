namespace Inkling;

/// <summary>
/// 「顯示原始文字還是渲染結果」這個檢視狀態的存放處。
///
/// 跟 <see cref="ICaptureSeparatorStore"/>、<see cref="ICapturePreviewStore"/> 是同一個
/// 家族(頁面只認識自己需要的那一格設定,不必認識整個設定管理員),但**形狀不一樣**:
/// 那兩個是設定頁寫、頁面讀,這一個是**頁面自己寫**(按 <c>Ctrl+U</c>)、所有畫面一起讀。
/// 所以它是唯一有 setter 的。
///
/// <c>ICaptureSeparatorStore</c> 上留過一句「第三項出現時考慮收成泛型
/// <c>ISettingValue&lt;T&gt;</c>」—— 第三項就是這個,而它不適合收:泛型那版的重點是
/// 「唯讀值 + 變更事件」,這裡多一個寫入端,收進去等於讓另外兩個也長出 setter,
/// 那是設定頁專屬的權力。三個介面各留各的,等到出現第四個**唯讀**設定時再收。
/// </summary>
internal interface ISourceModeStore
{
    /// <summary>
    /// true 代表所有顯示筆記內文的地方都給原始文字:清單頁與刪除頁的詳細窗格給
    /// 逐字逃脫的 Markdown,預覽頁與記下並預覽頁給純文字內容(連縮排都不動)。
    ///
    /// <b>這個狀態是全域而且會存進 settings.json</b> —— 換一個畫面、關掉 CmdPal、
    /// 甚至 Reload 之後都還在。刻意的:會按 <c>Ctrl+U</c> 的人是在看檔案裡真正的字元
    /// (Markdown 符號、貼進來的 SVG / HTML —— 那些東西渲染完會整段消失),
    /// 而那件事通常不會只做一則筆記。設定頁上**沒有**這一項,因為切換鍵本身就是它的介面。
    ///
    /// 設定的值一樣時不寫檔、也不發事件。
    /// </summary>
    bool ShowSource { get; set; }

    /// <summary>
    /// 有人切換了原始文字模式(可能是別的頁面切的)。
    ///
    /// 為什麼需要這條路,而不是重建頁面:CmdPal 手上握著的是使用者當下開著的那個頁面實例,
    /// 重建出來的新頁面它根本不會去拿 —— 完整說明見
    /// <see cref="ICaptureSeparatorStore.CaptureSeparatorChanged"/>。
    ///
    /// <b>只有長壽的頁面該訂閱這個事件。</b>預覽頁與記下並預覽頁是清單裡每個項目各建一個的,
    /// 訂閱長壽事件會一路累積死掉的訂閱者(<see cref="Pages.NotePreviewPage"/> 對
    /// <c>repository.Changed</c> 也是同一個理由不訂閱);那兩頁改成在
    /// <c>GetContent()</c> 當下讀一次,反正 CmdPal 導覽過去就一定會取內容。
    /// </summary>
    event EventHandler? ShowSourceChanged;
}
