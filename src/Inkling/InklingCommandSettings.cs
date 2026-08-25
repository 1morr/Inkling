using Microsoft.CommandPalette.Extensions;

namespace Inkling;

/// <summary>
/// 交給 CmdPal 的設定入口。整個介面只有 <see cref="SettingsPage"/> 一個成員。
///
/// 為什麼不直接用 toolkit 的 <c>Settings</c>(它本身就實作了這個介面):因為那樣
/// **CmdPal 設定 → Extensions → Inkling 那一頁永遠停在啟動時的值**。
///
/// 那條路上 CmdPal 是這樣走的:<c>ProviderSettingsViewModel</c> 用
/// <c>_initializeSettingsTask ??=</c> 只初始化一次，之後每次都回傳同一個
/// <c>ContentPageViewModel</c>;而那個 viewmodel 只有在頁面發出 <c>ItemsChanged</c>
/// 時才會重新 <c>GetContent()</c>。toolkit 的設定頁確實會轉發 —— 但它轉發的來源是
/// <c>Settings.SettingsChanged</c>，而那個事件**擴展發不出來**
/// (<c>RaiseSettingsChanged()</c> 是 internal，唯一的呼叫者是使用者按下 Save)。
///
/// 於是設定送出去了、檔也存了，那一頁卻毫不知情，卡片一直停在啟動時的值 ——
/// 而下一次從那裡按 Save，那個過期值會被當成使用者的輸入寫回設定
/// (完整的坑見 <see cref="InklingCommandsProvider"/> 的 <c>OnSettingsApplied</c>)。
/// 換成自己的 <see cref="Pages.InklingSettingsPage"/> 就拿回了發 <c>ItemsChanged</c> 的權力。
/// </summary>
internal sealed partial class InklingCommandSettings : ICommandSettings
{
    public InklingCommandSettings(IContentPage settingsPage) => SettingsPage = settingsPage;

    public IContentPage SettingsPage { get; }
}
