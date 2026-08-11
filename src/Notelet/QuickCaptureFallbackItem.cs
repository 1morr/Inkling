using Microsoft.CommandPalette.Extensions.Toolkit;
using Notelet.Commands;
using Notelet.Core;

namespace Notelet;

/// <summary>
/// 讓「記下這句話」直接出現在 Command Palette 的主搜尋框裡,不必先進 Notelet 頁面。
///
/// Fallback item 的規則是:使用者打的字沒有命中任何命令時,CmdPal 就會來問各個擴展。
/// 問題是這樣每一次搜索都會多出一列 Notelet,很吵;而且跟其他 fallback 擠在一起時,
/// 我們不見得排得到第一個,那「打字→Enter」就不成立了。
///
/// 解法是要求一個前綴(預設 "n ")。有前綴時它幾乎是唯一的結果,Enter 直接就是存檔;
/// 沒前綴時把 Title 設成空字串把自己藏起來 —— 這是 CmdPal 內建擴展慣用的隱藏手法。
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
        var text = QuickCapture.ExtractText(query, _options);

        if (text is null)
        {
            Hide();
            return;
        }

        _command.Text = text;
        Title = $"記下:{text}";
        Subtitle = "存成新筆記";
    }

    private void Hide()
    {
        _command.Text = string.Empty;
        Title = string.Empty;
        Subtitle = string.Empty;
    }
}
