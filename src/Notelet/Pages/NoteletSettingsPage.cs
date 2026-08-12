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
/// 結果就是:表單送出了、設定檔也存了,可是「設定 → Extensions → Notelet」那個入口
/// 顯示的還是舊值 —— CmdPal 不會因為導覽進頁面就重新呼叫 <c>GetContent()</c>,
/// 得有人叫它重拿。
///
/// 所以這裡自己套一層外殼,把「什麼時候該重拿」的控制權拿回來。
/// 表單也是自己的(<see cref="NoteletSettingsForm"/>),理由見那邊。
/// </summary>
internal sealed partial class NoteletSettingsPage : ContentPage
{
    /// <summary>
    /// 一塊空白內容。看起來毫無作用,**拿掉它焦點就會被搶走**。
    ///
    /// CmdPal 的 <c>ContentFormControl</c> 在載入後會自動聚焦第一個可輸入的欄位,
    /// 但只在自己是「頁面上唯一的控件」時才做(<c>OnFrameworkElementLoaded</c> 裡的
    /// <c>OnlyControlOnPage</c> 判斷,而那個旗標就是 <c>ContentPageViewModel</c>
    /// 依內容數量算出來的:<c>newContent.Count == 1</c>)。
    ///
    /// 而我們每次送出表單都得叫 CmdPal 重讀(見 <see cref="Refresh"/>),重讀等於整個控件
    /// 重建、再觸發一次 Loaded。設定頁有兩個入口而它們共用同一個實例,所以從清單頁那個
    /// 入口存檔時,背景那個「設定 → Extensions → Notelet」視窗會跟著重建 ——
    /// 那一下就把焦點從主視窗搶過去。
    /// 湊滿兩塊內容,<c>OnlyControlOnPage</c> 就是 false,重建也不會搶焦點。
    ///
    /// 代價:打開設定頁時游標不會自動落在第一個欄位,要點一下或按 Tab。
    /// 對「偶爾來改一次」的設定頁來說,這比背景視窗亂跳好得多。
    ///
    /// 為什麼是空的:內容區塊之間有大約 32px 收不掉的間距(<c>ItemsRepeater</c> 的
    /// <c>StackLayout Spacing=8</c>,加上每塊自己的 <c>Margin="0,4,4,4"</c> 與
    /// <c>Padding="12,8,8,8"</c>)。原本這裡放一句說明,擺前面是一段跟表單斷開的旁白,
    /// 擺後面更像掉在半空 —— 兩種都試過。那句話已經搬進卡片裡當淡色提示
    /// (<c>isSubtle</c>,markdown 這條路沒有淡色可用),這裡就只剩「湊數」這個作用。
    /// 排在最後,那 32px 就落在儲存按鈕底下,看不出來。
    ///
    /// 內容是空字串,不是空白字元:一個空白也是一行文字,會再多撐出約 20px。
    /// 剩下的 32px 是 CmdPal 的版面寫死的,只能接受 —— 少了這一塊就要拿焦點去換。
    ///
    /// 這塊的存在完全依賴「CmdPal 不過濾空內容」
    /// (<c>CommandPaletteContentPageViewModel.ViewModelFromContent</c> 只看型別)。
    /// 哪天它加了一道 <c>IsNullOrEmpty</c>,這塊會**無聲**消失、焦點又開始亂跳 ——
    /// 手動驗證清單裡「焦點不會被搶」那一項就是為了接住這種回歸。
    /// </summary>
    private readonly MarkdownContent _focusGuard = new(string.Empty);

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
        // 後面那塊空白不是忘了刪 —— 見 _focusGuard 上的說明。
        return [new NoteletSettingsForm(_settings, Refresh), _focusGuard];
    }

    /// <summary>
    /// 值變了(送出表單,或是在表單裡按「瀏覽…」選完資料夾),叫 CmdPal 重拿表單 ——
    /// 卡片的值是建構時就烤進 <c>DataJson</c> 的,不重拿它永遠停在舊值。
    /// </summary>
    public void Refresh()
    {
        DiagnosticLog.Write("SettingsPage.Refresh: 發出 ItemsChanged");
        RaiseItemsChanged();
    }
}
