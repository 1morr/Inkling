using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Inkling.Properties;

namespace Inkling.Pages;

/// <summary>
/// 設定頁。
///
/// 為什麼不直接用 toolkit 的 <c>Settings.SettingsPage</c>:那個頁面只在
/// <c>SettingsChanged</c> 事件來的時候才 <c>RaiseItemsChanged()</c>，而**擴展發不出那個事件**
/// —— <c>Settings.RaiseSettingsChanged()</c> 是 internal，唯一的呼叫者是使用者在設定頁
/// 按下 Save 時走的 <c>SettingsForm.SubmitForm</c>。
///
/// 結果就是:表單送出了、設定檔也存了，可是「設定 → Extensions → Inkling」那個入口
/// 顯示的還是舊值 —— CmdPal 不會因為導覽進頁面就重新呼叫 <c>GetContent()</c>,
/// 得有人叫它重拿。
///
/// 所以這裡自己套一層外殼，把「什麼時候該重拿」的控制權拿回來。
/// 表單也是自己的(<see cref="InklingSettingsForm"/>)，理由見那邊。
/// </summary>
internal sealed partial class InklingSettingsPage : ContentPage
{
    private readonly SettingsManager _settings;

    public InklingSettingsPage(SettingsManager settings)
    {
        _settings = settings;

        Name = Resources.SettingsPageName;
        Title = Resources.SettingsPageTitle;
        Icon = Icons.Settings;
    }

    /// <remarks>
    /// **這裡曾經多掛一塊空的 <c>MarkdownContent</c>，現在拿掉了。**
    ///
    /// 那塊空白是為了擋「背景的設定視窗被拉到前面」:`ContentFormControl` 載入後會自動
    /// 聚焦第一個輸入欄位，而我們每次送出表單都得叫 CmdPal 重讀(見 <see cref="Refresh"/>),
    /// 重讀等於控件重建 + 再觸發一次 Loaded。當年的理由是 CmdPal 只在「頁面上唯一的控件」
    /// 時才聚焦(<c>OnlyControlOnPage</c>)，湊滿兩塊就不聚焦。
    ///
    /// 拿掉的兩個理由:
    ///
    /// 1. **會觸發的情境沒了。** 當初每按一次 <c>Ctrl+D</c>(那時詳細面板寬度是可調的)
    ///    就重讀一次表單，人卻在主視窗翻筆記 —— 背景視窗因此一直跳。現在
    ///    <see cref="Refresh"/> 只有一個呼叫點(provider 收到 <c>Applied</c> 之後),
    ///    而那一定源自使用者在設定表單上的操作(按儲存、或按「瀏覽…」選完資料夾),
    ///    人本來就在設定頁上。
    /// 2. **那個判斷在安裝版裡根本不存在。** byte-scan 過
    ///    <c>Microsoft.CmdPal.UI.exe</c>(0.11.11762.0):同一條路上的
    ///    <c>ContentFormControl</c> / <c>OnFrameworkElementLoaded</c> /
    ///    <c>FindFirstFocusableElement</c> 全都在，只有 <c>OnlyControlOnPage</c> 沒有，
    ///    <c>OnlyControl</c> / <c>SoleControl</c> / <c>SingleControl</c> 各種變體也都沒有。
    ///    那是 CmdPal <c>main</c> 才有的判斷 —— 也就是說這塊空白在安裝版上八成從來沒生效過。
    ///
    /// 換回來的是:打開設定頁時游標會自動落在第一個欄位，不必先點一下或按 Tab。
    ///
    /// **萬一背景設定視窗又開始搶焦點**，原因就在這裡，補救方式是讓 <c>GetContent</c>
    /// 多回傳一塊內容 —— 但先確認安裝版到底有沒有那道判斷，不要照 <c>main</c> 的原始碼寫。
    /// </remarks>
    public override IContent[] GetContent()
    {
        DiagnosticLog.Write("SettingsPage.GetContent: rebuilding the form");

        // 每次都給新的表單物件:值是建構時就烤進卡片的，重用等於永遠顯示第一次的值。
        return [new InklingSettingsForm(_settings)];
    }

    /// <summary>
    /// 值變了(送出表單，或是在表單裡按「瀏覽…」選完資料夾)，叫 CmdPal 重拿表單 ——
    /// 卡片的值是建構時就烤進 <c>DataJson</c> 的，不重拿它永遠停在舊值。
    ///
    /// 唯一的呼叫者是 <c>InklingCommandsProvider.OnSettingsApplied</c>，兩條路
    /// (儲存、瀏覽…)都經過 <c>SettingsManager.Apply</c>，所以都會走到這裡。
    /// </summary>
    public void Refresh()
    {
        DiagnosticLog.Write("SettingsPage.Refresh: raising ItemsChanged");
        RaiseItemsChanged();
    }
}
