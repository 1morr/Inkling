using System.Text.Json.Nodes;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Inkling.Core;
using Inkling.Properties;

namespace Inkling.Pages;

/// <summary>
/// 隨手草稿的那一塊文字框。
///
/// 跟 <see cref="NoteFormContent"/> 同一個形狀(Adaptive Cards、內容一律走
/// <see cref="FormContent.DataJson"/>)，差別只在它沒有標題欄位、存的是一個固定檔案。
///
/// <para><b>為什麼沒有自動儲存 —— 這條查過了，不要再試。</b></para>
///
/// 表單的輸入值只活在 CmdPal 進程裡的 <c>RenderedAdaptiveCard.UserInputs</c>，擴展唯一的
/// 取值管道是 CmdPal 反過來呼叫 <see cref="SubmitForm(string)"/>。沒有 keystroke 事件、
/// 沒有失焦事件、也沒有 <c>Ctrl+S</c>(把鍵綁到擴展的命令上一樣拿不到使用者剛打的字)。
/// 整個 CmdPal 裡唯一收得到「正在打的字」的地方是搜尋框
/// (<c>DynamicListPage.UpdateSearchText</c>)，而那是單行的 —— 隨手草稿不能換行就沒有意義。
/// 所以存檔是一個明著的動作，而使用者要真正的自動儲存時走 <c>Ctrl+O</c> 跳到外部編輯器。
/// 完整考證見 docs/design-notes.md〈隨手草稿為什麼沒有自動儲存〉。
///
/// <b>同一條限制也決定了底部工具列放不了「儲存」。</b>那兩顆按鈕走的是
/// <c>ICommand.Invoke()</c>，沒有參數 —— 放上去會是一顆存不了東西的假按鈕。
/// 能做的是把路徑縮到最短:<c>Tab</c> → <c>Enter</c>，然後面板自己關掉。
/// </summary>
internal sealed partial class ScratchpadFormContent : FormContent
{
    /// <summary>
    /// 文字框至少要有這麼多行高。
    ///
    /// 跟 <see cref="NoteFormContent"/> 是同一個限制:渲染器對多行輸入只設 AcceptsReturn 與
    /// TextWrapping，完全不碰高度，卡片也沒有「幾行高」這種屬性 —— <b>唯一撐得開它的就是
    /// 內容本身</b>。隨手草稿整頁就只有這一個框，而且是拿來想事情的，所以給得比新增筆記
    /// (5 行)大得多。
    ///
    /// 補進去的空行不會累積:存檔路徑會 <c>TrimEnd</c>。
    /// </summary>
    private const int MinDisplayLines = 12;

    private readonly ScratchpadStore _store;

    public ScratchpadFormContent(ScratchpadStore store)
    {
        _store = store;

        TemplateJson = Template;

        // 草稿內容只能走 DataJson。拼進 TemplateJson 的話，內容裡的 ${...}
        // 會被樣板引擎當成佔位符吃掉 —— 而隨手草稿正是最可能出現那種字串的地方。
        DataJson = new JsonObject
        {
            ["text"] = PadToMinLines(store.Read()),
        }.ToJsonString();
    }

    public override CommandResult SubmitForm(string inputs)
    {
        var form = JsonNode.Parse(inputs)?.AsObject();

        if (form is null)
        {
            return CommandResult.KeepOpen();
        }

        var text = form["text"]?.ToString() ?? string.Empty;

        try
        {
            // TrimEnd 去掉的是我們自己補進去撐高度的空行(見 MinDisplayLines)。
            // 開頭不動 —— 那可能是使用者自己的縮排。
            _store.Write(text.TrimEnd());

            // **存完就把面板收掉。** 寫下來的東西通常就是那一趟的全部目的，
            // 留一個面板在畫面上只是多一次 Esc。`Tab` → `Enter` 兩鍵結束。
            //
            // `Done` 一手包辦「收面板」與「訊息走得到面板外面」:InfoBar 畫在面板上，
            // 面板都收了就看不見，發了只是白費(這裡曾經因為這個理由什麼都不發，
            // 而「面板消失」單獨拿來當回饋，說不出存進去的是什麼)。
            //
            // 存檔失敗相反，見下面那個 catch:那條路留在原地，所以走 `Stay`。
            return Feedback.Done(Resources.ScratchpadSaved);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 存檔失敗絕對不能無聲無息 —— 使用者會以為存起來了然後把視窗關掉。
            DiagnosticLog.Failure($"ScratchpadFormContent save failed ({ex.GetType().Name})", ex.ToString());
            return Feedback.Stay(Strings.Format(Resources.SaveFailed, ex.Message));
        }
    }

    /// <summary>
    /// 不足 <see cref="MinDisplayLines"/> 行就在尾端補空行。CRLF 也算得對
    /// (每個 <c>\r\n</c> 只有一個 <c>\n</c>)。
    /// </summary>
    private static string PadToMinLines(string text)
    {
        var lines = text.Count(c => c == '\n') + 1;

        return lines >= MinDisplayLines ? text : text + new string('\n', MinDisplayLines - lines);
    }

    /// <summary>
    /// 卡片上就一個輸入框跟一顆按鈕，別的都沒有。
    ///
    /// 只有一個可聚焦的控件，所以焦點一定落在文字框
    /// (<c>ContentFormControl.FindFirstFocusableElement</c> 挑第一個可聚焦的控件)。
    ///
    /// <b>沒有 label、沒有 placeholder、也沒有說明文字。</b>頁面標題已經寫著這是什麼;
    /// placeholder 永遠不會顯示(框裡固定有那些撐高度的空行);而底部工具列本來就把
    /// 「在預設編輯器開啟」跟它的鍵位印在畫面上，再寫一行字說明只是把寫字的地方變小。
    /// 「沒有自動儲存」這件事一度寫在卡片底部，拿掉了 —— 存完面板就自己收掉，
    /// 使用者實際感覺到的是「打完按兩下就結束」，不需要一段免責聲明。考證留在
    /// docs/design-notes.md〈隨手草稿為什麼沒有自動儲存〉。
    /// </summary>
    private static string Template => $$"""
        {
            "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
            "type": "AdaptiveCard",
            "version": "1.6",
            "body": [
                {
                    "type": "Input.Text",
                    "id": "text",
                    "value": "${text}",
                    "isMultiline": true
                }
            ],
            "actions": [
                {
                    "type": "Action.Submit",
                    "title": {{CardText.Json(Resources.FormSave)}},
                    "style": "positive"
                }
            ]
        }
        """;
}
