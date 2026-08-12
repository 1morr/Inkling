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
/// 所以這裡自己套一層外殼,把「什麼時候該重拿」的控制權拿回來。
/// 表單也是自己的(<see cref="NoteletSettingsForm"/>),理由見那邊。
/// </summary>
internal sealed partial class NoteletSettingsPage : ContentPage
{
    /// <summary>
    /// 表單底下那行註腳。
    ///
    /// **它的存在不只是為了說明 —— 拿掉它焦點就會被搶走。**
    /// CmdPal 的 <c>ContentFormControl</c> 在載入後會自動聚焦第一個可輸入的欄位,
    /// 但只在自己是「頁面上唯一的控件」時才做(<c>OnFrameworkElementLoaded</c> 裡的
    /// <c>OnlyControlOnPage</c> 判斷,而那個旗標就是 <c>ContentPageViewModel</c>
    /// 依內容數量算出來的)。
    ///
    /// 我們每按一次 Ctrl+D 就得叫 CmdPal 重讀表單,而重讀等於整個控件重建、
    /// 再觸發一次 Loaded。設定視窗要是開在背景,那一下就會把焦點從主視窗搶過去。
    /// 多這一塊內容,<c>OnlyControlOnPage</c> 就是 false,重建也不會搶焦點。
    ///
    /// 代價:打開設定頁時游標不會自動落在第一個欄位,要點一下或按 Tab。
    /// 對「偶爾來改一次」的設定頁來說,這比背景視窗亂跳好得多。
    ///
    /// 擺在表單**後面**:markdown 那一塊是用頁面的字級渲染的,比卡片裡的字大一號,
    /// 放在最上面等於用一段旁白當標題,設定項反而被擠到下面去。當註腳就順眼多了。
    /// 內容也避開每個設定項自己的說明(那些就印在欄位底下),不然同一句話會出現兩次。
    /// </summary>
    private readonly MarkdownContent _footnote = new(
        "換資料夾不會搬動已經寫好的筆記,只是改成去讀新的位置。");

    private readonly SettingsManager _settings;

    public NoteletSettingsPage(SettingsManager settings)
    {
        _settings = settings;

        Name = "設定";
        Title = "Notelet 設定";
        Icon = Icons.Settings;
    }

    public override IContent[] GetContent()
    {
        DiagnosticLog.Write("SettingsPage.GetContent: 重新產生表單");

        // 每次都給新的表單物件:值是建構時就烤進卡片的,重用等於永遠顯示第一次的值。
        // 註腳看起來可有可無,但它必須留著 —— 見 _footnote 上的說明。
        return [new NoteletSettingsForm(_settings, Refresh), _footnote];
    }

    /// <summary>值被頁面以外的地方改掉了(目前只有清單頁的 Ctrl+D),叫 CmdPal 重拿表單。</summary>
    public void Refresh()
    {
        DiagnosticLog.Write("SettingsPage.Refresh: 發出 ItemsChanged");
        RaiseItemsChanged();
    }
}
