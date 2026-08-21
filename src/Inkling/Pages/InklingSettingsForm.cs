using System.Text.Json.Nodes;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Inkling.Core;
using Inkling.Properties;

namespace Inkling.Pages;

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
internal sealed partial class InklingSettingsForm : FormContent
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

    /// <summary>按鈕靠 <c>Action.Submit</c> 的 data 表明自己是誰 —— 兩顆按鈕走的是同一個 SubmitForm。</summary>
    private const string ActionKey = "action";
    private const string BrowseAction = "browse";

    private readonly SettingsManager _settings;
    private readonly Action _refreshPage;

    /// <param name="refreshPage">選完資料夾之後叫設定頁重畫,否則輸入框裡還是舊路徑。</param>
    public InklingSettingsForm(SettingsManager settings, Action refreshPage)
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

        var outcome = _settings.Apply(directory, separator, preview);

        switch (outcome)
        {
            case SettingsManager.ApplyResult.RejectedRelativePath:
                // 整筆都沒存,表單留在原地:使用者打的東西還在卡片上,改完路徑再送一次就好。
                new ToastStatusMessage(Resources.SettingsDirectoryRejected).Show();
                return CommandResult.KeepOpen();

            case SettingsManager.ApplyResult.SaveFailed:
                // 值在這個工作階段生效了,但沒寫進 settings.json —— 重啟就還原。
                // 留在原地(KeepOpen):使用者打的東西還在卡片上,排掉問題再送一次就好,
                // 而 GoHome 會讓那句話跟著這一頁一起走掉。
                new ToastStatusMessage(Resources.SettingsSaveFailed).Show();
                return CommandResult.KeepOpen();

            case SettingsManager.ApplyResult.AppliedToMissingFolder:
                // 存是存了,但資料夾還不存在 —— 當場講,「打錯一個字就換了家」才不會無聲發生。
                new ToastStatusMessage(
                    Strings.Format(Resources.SettingsDirectoryWillBeCreated, _settings.NotesDirectory)).Show();
                return CommandResult.GoHome();

            default:
                new ToastStatusMessage(Resources.SettingsSaved).Show();
                return CommandResult.GoHome();
        }
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
            Resources.SettingsFolderPickerTitle,
            string.IsNullOrWhiteSpace(directory) ? _settings.NotesDirectory : directory.Trim(),
            picked =>
            {
                // 選好就直接存,不等使用者再按一次「儲存」:對話框一拿到焦點,CmdPal 主視窗
                // 就會把自己藏起來(MainWindow 的 Deactivated → HideWindow,沒有開關可以關掉),
                // 這張表單跟著一起消失 —— 那時候還壓在表單裡的值,使用者既看不到也按不到。
                //
                // 其他欄位一起套用,因為那就是使用者按下「瀏覽…」當下卡片上顯示的值 ——
                // 表單既然會消失,壓在上面的改動就只有這一次機會存下來。
                // 傳回值不用看:挑選器回來的一定是存在的完整路徑,兩條拒絕/提醒的路都踩不到。
                _settings.Apply(picked, separator, preview);
                new ToastStatusMessage(Strings.Format(Resources.SettingsFolderPicked, picked)).Show();

                _refreshPage();
            },
            // 對話框開不起來也要講一聲,否則「瀏覽…」看起來像壞掉 —— 之前只有 DiagnosticLog
            // 留一行字,而它預設是關的。用 InfoBadge:不開視窗、不關面板,表單留在原地。
            failed: () => new ToastStatusMessage(Resources.SettingsFolderPickerFailed).Show());

        if (!opened)
        {
            new ToastStatusMessage(Resources.SettingsPickerAlreadyOpen).Show();
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
    /// 分隔線把三個設定項切開,間距用 <c>default</c>(8px)。CmdPal 沒有自帶 hostConfig,
    /// 走的就是 Adaptive Cards 的預設階梯(none 0 / small 3 / default 8 / medium 20 /
    /// large 30),而 <c>large</c>、<c>medium</c> 撐出來的空白在這張只有三項的卡片上都太散 ——
    /// 每個欄位下面本來就有一塊說明文字,那本身已經是視覺上的呼吸空間。
    /// 線本身不能拿掉:少了線的話上一項的說明會直接黏著下一項的標籤,
    /// 看不出哪句話屬於哪個欄位,而間距一收更是如此。
    ///
    /// 「記下後先看一眼」那個 <c>Input.Toggle</c> 不必包 <c>ColumnSet</c>:核取方塊的寬度
    /// 本來就只有方塊加標題那麼寬,撐不開版面。它的欄位名寫在 <c>title</c> 而不是
    /// <c>label</c> —— 那個控件的字本來就長在方塊旁邊,再加一個 label 會變成同一句話印兩次。
    ///
    /// 說明文字擺在欄位**下面**當註腳,而不是像 toolkit 那樣頂在標籤的位置,
    /// 而且**每個欄位下面各一塊,沒有例外** —— 卡片最上面曾經另外有一行「整頁共通的提醒」,
    /// 但那句話講的只是筆記資料夾,結果那個欄位變成唯一上下都有說明的。
    /// 已經併進 <c>NotesDirectorySetting</c> 的說明裡,要加類似的話也照這條走,
    /// 不要在卡片頂上再開一塊。
    ///
    /// 卡片層級只留「儲存」一顆。這是防禦性設計,依據在 CmdPal 的 **main 分支**:
    /// 在單行輸入框裡按 Enter 時,它送出的是 <c>card.Actions</c> 的第一個
    /// (<c>ContentFormControl.OnFormKeyDown</c>)—— 打完路徑按 Enter 應該是存檔,
    /// 不是跳出「瀏覽」對話框。**0.11.11762.0 沒有那段程式碼**(byte-scan 掃不到,
    /// 見 docs/design-notes.md〈編輯表單〉),使用者手上單行框按 Enter 不會送出,Tab 到「儲存」
    /// 是唯一的鍵盤路徑。只留一顆的安排兩個版本下都對,所以維持。
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
                    "type": "ColumnSet",
                    "columns": [
                        {
                            "type": "Column",
                            "width": "stretch",
                            "items": [
                                {
                                    "type": "Input.Text",
                                    "id": "{{DirectoryField}}",
                                    "label": {{CardText.Json(directory.Label)}},
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
                                            "title": {{CardText.Json(Resources.SettingsBrowse)}},
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
                    "text": {{CardText.Json(directory.Description)}},
                    "wrap": true,
                    "isSubtle": true,
                    "size": "small",
                    "spacing": "small"
                },
                {
                    "type": "ColumnSet",
                    "separator": true,
                    "spacing": "default",
                    "columns": [
                        {
                            "type": "Column",
                            "width": "180px",
                            "items": [
                                {
                                    "type": "Input.Text",
                                    "id": "{{SeparatorField}}",
                                    "label": {{CardText.Json(separator.Label)}},
                                    "value": "{{SeparatorBinding}}",
                                    "placeholder": {{CardText.Json(QuickCapture.DefaultSeparator)}}
                                }
                            ]
                        }
                    ]
                },
                {
                    "type": "TextBlock",
                    "text": {{CardText.Json(separator.Description)}},
                    "wrap": true,
                    "isSubtle": true,
                    "size": "small",
                    "spacing": "small"
                },
                {
                    "type": "Input.Toggle",
                    "id": "{{PreviewField}}",
                    "title": {{CardText.Json(preview.Label)}},
                    "value": "{{(preview.Value ? ToggleOn : ToggleOff)}}",
                    "valueOn": "{{ToggleOn}}",
                    "valueOff": "{{ToggleOff}}",
                    "separator": true,
                    "spacing": "default"
                },
                {
                    "type": "TextBlock",
                    "text": {{CardText.Json(preview.Description)}},
                    "wrap": true,
                    "isSubtle": true,
                    "size": "small",
                    "spacing": "small"
                }
            ],
            "actions": [
                {
                    "type": "Action.Submit",
                    "title": {{CardText.Json(Resources.FormSave)}},
                    "style": "positive",
                    "data": { "{{ActionKey}}": "save" }
                }
            ]
        }
        """;
    }
}
