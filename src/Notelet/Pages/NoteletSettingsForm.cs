using System.Text.Json.Nodes;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Notelet.Core;

namespace Notelet.Pages;

/// <summary>
/// 設定頁的表單。
///
/// 為什麼不用 toolkit 現成的 <c>Settings.ToContent()</c>:
///
/// 1. **它畫不出「瀏覽…」按鈕。** 設定項只能一格一格排下去,卡片上沒有地方放別的東西。
/// 2. **欄位名根本不會顯示。** 它把 <c>Label</c> 塞進 Adaptive Cards 的 <c>title</c>,
///    而 <c>Input.Text</c> 沒有那個屬性;真正會顯示的 <c>label</c> 它拿去放 <c>Description</c>。
///    結果就是每個欄位頭上頂著一整句說明,看不到「筆記資料夾」這種短名字。
/// 3. **送出之後它固定 <c>GoHome</c>**,而按「瀏覽…」時我們得留在原地。
///
/// 代價是存檔那條路要自己接:值交給 <see cref="SettingsManager.Apply"/>,由它存檔與通知
/// (toolkit 的 <c>Settings.RaiseSettingsChanged()</c> 是 internal,擴展叫不動)。
/// 標籤、說明、選項仍然只有 <see cref="SettingsManager"/> 那一份,這裡只負責畫。
/// </summary>
internal sealed partial class NoteletSettingsForm : FormContent
{
    private const string DirectoryField = "directory";
    private const string SeparatorField = "separator";
    private const string PreviewField = "preview";

    /// <summary>
    /// <c>Input.Toggle</c> 的開 / 關值。
    ///
    /// 明著寫出來而不是靠 Adaptive Cards 的預設:那個控件回傳的是 <c>valueOn</c> /
    /// <c>valueOff</c> **字串**,不是 JSON 的 <c>true</c> / <c>false</c>,
    /// 送出那一頭的比對得跟這裡對得上。
    /// </summary>
    private const string ToggleOn = "true";

    /// <inheritdoc cref="ToggleOn" />
    private const string ToggleOff = "false";

    /// <summary>Adaptive Cards 的樣板佔位符,值由 <see cref="FormContent.DataJson"/> 填。</summary>
    private const string DirectoryBinding = "${" + DirectoryField + "}";

    /// <inheritdoc cref="DirectoryBinding" />
    private const string SeparatorBinding = "${" + SeparatorField + "}";

    /// <summary>
    /// 整頁共通的一句提醒。
    ///
    /// 放在卡片**裡面**,而不是頁面上另外一塊 markdown:CmdPal 的內容區塊之間有大約 32px
    /// 收不掉的間距(<c>ItemsRepeater</c> 的 <c>StackLayout Spacing=8</c> 加上每塊自己的
    /// <c>Margin/Padding</c>),而且 markdown 那條路沒有淡色可用 —— 它的
    /// <c>MarkdownThemes</c> 只設定了字級與 inline code 的樣式。
    /// 進到卡片裡才有 <c>isSubtle</c> + <c>size: small</c>,也才貼得住底下的欄位。
    /// </summary>
    private const string Hint = "換資料夾不會搬動已經寫好的筆記,只是改成去讀新的位置。";

    /// <summary>按鈕靠 <c>Action.Submit</c> 的 data 表明自己是誰 —— 兩顆按鈕走的是同一個 SubmitForm。</summary>
    private const string ActionKey = "action";
    private const string BrowseAction = "browse";

    private readonly SettingsManager _settings;
    private readonly Action _refreshPage;

    /// <param name="refreshPage">選完資料夾之後叫設定頁重畫,否則輸入框裡還是舊路徑。</param>
    public NoteletSettingsForm(SettingsManager settings, Action refreshPage)
    {
        _settings = settings;
        _refreshPage = refreshPage;

        TemplateJson = BuildTemplate(settings);

        // 路徑與分隔符都是使用者輸入的,一律經由 DataJson 帶進去。直接拼進 TemplateJson 的話,
        // 裡面的 ${...} 會被樣板引擎當成佔位符解讀 —— 跟筆記內文同一個理由。
        // 分隔符尤其要小心:那個欄位本來就是拿來放標點的,`${` 打得出來。
        DataJson = new JsonObject
        {
            [DirectoryField] = settings.NotesDirectory,
            [SeparatorField] = settings.CaptureSeparator,
        }.ToJsonString();
    }

    public override CommandResult SubmitForm(string inputs, string data)
    {
        var form = JsonNode.Parse(inputs)?.AsObject();

        if (form is null)
        {
            return CommandResult.KeepOpen();
        }

        var directory = form[DirectoryField]?.ToString() ?? string.Empty;
        var separator = form[SeparatorField]?.ToString() ?? string.Empty;

        // Input.Toggle 回來的是 valueOn / valueOff 那兩個字串,不是 JSON 的 true/false。
        var preview = string.Equals(
            form[PreviewField]?.ToString(), ToggleOn, StringComparison.OrdinalIgnoreCase);

        if (ActionOf(data) == BrowseAction)
        {
            return Browse(directory, separator, preview);
        }

        _settings.Apply(directory, separator, preview);
        new ToastStatusMessage("設定已儲存").Show();

        return CommandResult.GoHome();
    }

    /// <summary>Action.Submit 的 data;空字串代表這張卡片沒帶 data(理論上不會發生)。</summary>
    private static string? ActionOf(string data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(data)?[ActionKey]?.ToString();
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private CommandResult Browse(string directory, string separator, bool preview)
    {
        var opened = FolderPicker.TryShow(
            "選擇筆記資料夾",
            string.IsNullOrWhiteSpace(directory) ? _settings.NotesDirectory : directory.Trim(),
            picked =>
            {
                // 選好就直接存,不等使用者再按一次「儲存」:對話框一拿到焦點,CmdPal 主視窗
                // 就會把自己藏起來(MainWindow 的 Deactivated → HideWindow,沒有開關可以關掉),
                // 這張表單跟著一起消失 —— 那時候還壓在表單裡的值,使用者既看不到也按不到。
                //
                // 其他欄位一起套用,因為那就是使用者按下「瀏覽…」當下卡片上顯示的值 ——
                // 表單既然會消失,壓在上面的改動就只有這一次機會存下來。
                _settings.Apply(picked, separator, preview);
                new ToastStatusMessage($"筆記資料夾:{picked}").Show();

                _refreshPage();
            });

        if (!opened)
        {
            new ToastStatusMessage("已經有一個「選擇資料夾」的視窗開著了").Show();
        }

        return CommandResult.KeepOpen();
    }

    /// <summary>
    /// 卡片的排版。
    ///
    /// 「瀏覽…」跟輸入框放在同一個 <c>ColumnSet</c> 裡,按鈕那一欄靠底對齊 ——
    /// 輸入框頭上還有一行 <c>label</c>,不對齊的話按鈕會浮在框的上緣。
    ///
    /// 分隔符那一格也包在 <c>ColumnSet</c> 裡,但目的相反:限寬。它只放得下兩三個字元,
    /// 一個佔滿整頁的輸入框會讓人以為該填一長串,設定視窗開大的時候特別難看。
    /// 分隔線把三個設定項切開。
    ///
    /// 「記下後先看一眼」那個 <c>Input.Toggle</c> 不必包 <c>ColumnSet</c>:核取方塊的寬度
    /// 本來就只有方塊加標題那麼寬,撐不開版面。它的欄位名寫在 <c>title</c> 而不是
    /// <c>label</c> —— 那個控件的字本來就長在方塊旁邊,再加一個 label 會變成同一句話印兩次。
    ///
    /// 說明文字擺在欄位**下面**當註腳,而不是像 toolkit 那樣頂在標籤的位置。
    /// 卡片層級只留「儲存」一顆:在單行輸入框裡按 Enter 時,CmdPal 送出的是
    /// <c>card.Actions</c> 的第一個(<c>ContentFormControl.OnFormKeyDown</c>)——
    /// 打完路徑按 Enter 應該是存檔,不是跳出對話框。
    /// </summary>
    private static string BuildTemplate(SettingsManager settings)
    {
        var directory = settings.NotesDirectorySetting;
        var separator = settings.CaptureSeparatorSetting;
        var preview = settings.CapturePreviewSetting;

        return $$"""
        {
            "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
            "type": "AdaptiveCard",
            "version": "1.6",
            "body": [
                {
                    "type": "TextBlock",
                    "text": {{Json(Hint)}},
                    "wrap": true,
                    "isSubtle": true,
                    "size": "small"
                },
                {
                    "type": "ColumnSet",
                    "spacing": "medium",
                    "columns": [
                        {
                            "type": "Column",
                            "width": "stretch",
                            "items": [
                                {
                                    "type": "Input.Text",
                                    "id": "{{DirectoryField}}",
                                    "label": {{Json(directory.Label)}},
                                    "value": "{{DirectoryBinding}}"
                                }
                            ]
                        },
                        {
                            "type": "Column",
                            "width": "auto",
                            "verticalContentAlignment": "bottom",
                            "items": [
                                {
                                    "type": "ActionSet",
                                    "actions": [
                                        {
                                            "type": "Action.Submit",
                                            "title": "瀏覽…",
                                            "data": { "{{ActionKey}}": "{{BrowseAction}}" }
                                        }
                                    ]
                                }
                            ]
                        }
                    ]
                },
                {
                    "type": "TextBlock",
                    "text": {{Json(directory.Description)}},
                    "wrap": true,
                    "isSubtle": true,
                    "size": "small",
                    "spacing": "small"
                },
                {
                    "type": "ColumnSet",
                    "separator": true,
                    "spacing": "large",
                    "columns": [
                        {
                            "type": "Column",
                            "width": "180px",
                            "items": [
                                {
                                    "type": "Input.Text",
                                    "id": "{{SeparatorField}}",
                                    "label": {{Json(separator.Label)}},
                                    "value": "{{SeparatorBinding}}",
                                    "placeholder": {{Json(QuickCapture.DefaultSeparator)}}
                                }
                            ]
                        }
                    ]
                },
                {
                    "type": "TextBlock",
                    "text": {{Json(separator.Description)}},
                    "wrap": true,
                    "isSubtle": true,
                    "size": "small",
                    "spacing": "small"
                },
                {
                    "type": "Input.Toggle",
                    "id": "{{PreviewField}}",
                    "title": {{Json(preview.Label)}},
                    "value": "{{(preview.Value ? ToggleOn : ToggleOff)}}",
                    "valueOn": "{{ToggleOn}}",
                    "valueOff": "{{ToggleOff}}",
                    "separator": true,
                    "spacing": "large"
                },
                {
                    "type": "TextBlock",
                    "text": {{Json(preview.Description)}},
                    "wrap": true,
                    "isSubtle": true,
                    "size": "small",
                    "spacing": "small"
                }
            ],
            "actions": [
                {
                    "type": "Action.Submit",
                    "title": "儲存",
                    "style": "positive",
                    "data": { "{{ActionKey}}": "save" }
                }
            ]
        }
        """;
    }

    /// <summary>把字串變成帶引號的 JSON 字面值,連跳脫一起處理。</summary>
    private static string Json(string text) => JsonValue.Create(text)!.ToJsonString();
}
