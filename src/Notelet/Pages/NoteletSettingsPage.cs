using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace Notelet.Pages;

/// <summary>
/// 設定頁。
///
/// 為什麼不直接用 toolkit 的 <c>Settings.SettingsPage</c>:那個頁面只在
/// <c>SettingsChanged</c> 事件來的時候才 <c>RaiseItemsChanged()</c>,而**擴展發不出那個事件**
/// —— <c>Settings.RaiseSettingsChanged()</c> 是 internal,唯一的呼叫者是使用者在設定頁
/// 按下 Save 時走的 <c>SettingsForm.SubmitForm</c>。
///
/// 結果就是:清單頁按 Ctrl+D 改了寬度,設定檔也存了,可是設定頁再打開時顯示的還是舊值 ——
/// CmdPal 不會因為導覽進頁面就重新呼叫 <c>GetContent()</c>,得有人叫它重拿。
///
/// 所以這裡自己套一層外殼,內容還是用公開的 <c>Settings.ToContent()</c>(表單本身
/// 連同 Save 的處理都是 toolkit 的),只是把「什麼時候該重拿」的控制權拿回來。
/// </summary>
internal sealed partial class NoteletSettingsPage : ContentPage
{
    private readonly Settings _settings;

    public NoteletSettingsPage(Settings settings)
    {
        _settings = settings;

        Name = "設定";
        Title = "Notelet 設定";
        Icon = Icons.Settings;
    }

    public override IContent[] GetContent()
    {
        // 這一行是診斷「Ctrl+D 之後設定頁還是舊值」用的。CmdPal 只在頁面**開著**的時候
        // 聽 ItemsChanged(ContentPageViewModel 在離開頁面時就退訂了),所以問題是
        // 「打開頁面時它到底有沒有重新來拿」—— 有這行 log 就分得出來:
        // 打開設定頁時有出現 → CmdPal 有重拿,值不對就是別的原因;
        // 沒出現 → CmdPal 用的是快取的 ViewModel,得換一個新的頁面實例才會重建。
        DiagnosticLog.Write("SettingsPage.GetContent: CmdPal 來拿表單了");

        return _settings.ToContent();
    }

    /// <summary>值被頁面以外的地方改掉了(目前只有清單頁的 Ctrl+D),叫 CmdPal 重拿表單。</summary>
    public void Refresh()
    {
        DiagnosticLog.Write("SettingsPage.Refresh: 發出 ItemsChanged");
        RaiseItemsChanged();
    }
}
