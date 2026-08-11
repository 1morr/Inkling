using Microsoft.CommandPalette.Extensions.Toolkit;
using Notelet.Commands;
using Notelet.Core;

namespace Notelet;

/// <summary>
/// 讓「記下這句話」直接出現在 Command Palette 的主搜尋框裡,不必先進 Notelet 頁面。
///
/// 為什麼是 fallback 而不是一個普通的頂層命令:只有 fallback 拿得到使用者正在打的字
/// (<c>IFallbackHandler.UpdateQuery</c>)。頂層命令被叫起來時,搜尋框已經被清空了,
/// 換句話說「打字→Enter 就存檔」這件事非 fallback 不可。
///
/// Fallback 的規則是:使用者打的字沒有命中任何命令時,CmdPal 就會來問各個擴展。
/// 問題是這樣每一次搜索都會多出一列 Notelet,很吵;而且跟其他 fallback 擠在一起時,
/// 我們不見得排得到第一個,那「打字→Enter」就不成立了。
///
/// 內建的 fallback(計算機、Run、開網址)不需要前綴,是因為它們看得出查詢的形狀 ——
/// 算不算得出來、是不是一個可執行檔、像不像網址。筆記沒有這種形狀:任何一句話都是合法的筆記。
/// 所以這裡改用前綴(預設 "n ")當作意圖判斷:有前綴時它幾乎是唯一的結果,Enter 直接就是存檔;
/// 沒前綴時把 Title 設成空字串把自己藏起來 —— 這是 CmdPal 內建擴展慣用的隱藏手法
/// (空標題的項目會被 <c>MainListPage.GetSearchViewItems</c> 濾掉)。
/// </summary>
internal sealed partial class QuickCaptureFallbackItem : FallbackCommandItem
{
    private readonly QuickCaptureCommand _command;
    private readonly NoteletOptions _options;

    public QuickCaptureFallbackItem(QuickCaptureCommand command, NoteletOptions options)
        : base(command, "記下想法", CommandIds.QuickCapture)
    {
        _command = command;
        _options = options;

        Title = string.Empty;
        Icon = Icons.Add;
    }

    public override void UpdateQuery(string query)
    {
        // 判斷規則放在 Core.QuickCapture,那裡有單元測試涵蓋;這裡只負責把結果變成 UI。
        var draft = QuickCapture.Parse(query, _options);

        if (draft is null)
        {
            Hide();
            return;
        }

        _command.Draft = draft;
        Title = $"記下:{draft.Title}";

        // 有分號時把切出來的內文顯示出來,使用者才看得到分隔真的生效了。
        Subtitle = draft.Body.Length == 0 ? "存成新筆記" : $"內文:{draft.Body}";
    }

    private void Hide()
    {
        _command.Draft = null;
        Title = string.Empty;
        Subtitle = string.Empty;
    }
}
