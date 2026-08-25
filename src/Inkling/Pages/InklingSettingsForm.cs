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
/// 1. **它畫不出「瀏覽…」按鈕。** 設定項只能一格一格排下去，卡片上沒有地方放別的東西。
/// 2. **欄位名根本不會顯示。** 它把 <c>Label</c> 塞進 Adaptive Cards 的 <c>title</c>,
///    而 <c>Input.Text</c> 沒有那個屬性;真正會顯示的 <c>label</c> 它拿去放 <c>Description</c>。
///    結果就是每個欄位頭上頂著一整句說明，看不到「筆記資料夾」這種短名字。
/// 3. **送出之後它固定 <c>GoHome</c>**，而我們得按結果分兩種:存不成要留在原地讓使用者改，
///    按「瀏覽…」時也是。見 <see cref="SubmitForm(string, string)"/> 上那張表。
///
/// 代價是存檔那條路要自己接:值交給 <see cref="SettingsManager.Apply"/>，由它存檔與通知
/// (toolkit 的 <c>Settings.RaiseSettingsChanged()</c> 是 internal，擴展叫不動)。
/// 標籤、說明、選項仍然只有 <see cref="SettingsManager"/> 那一份，這裡只負責畫。
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
    /// <c>valueOff</c> **字串**，不是 JSON 的 <c>true</c> / <c>false</c>,
    /// 送出那一頭的比對得跟這裡對得上。
    /// </summary>
    private const string ToggleOn = "true";

    /// <inheritdoc cref="ToggleOn" />
    private const string ToggleOff = "false";

    /// <summary>Adaptive Cards 的樣板佔位符，值由 <see cref="FormContent.DataJson"/> 填。</summary>
    private const string DirectoryBinding = "${" + DirectoryField + "}";

    /// <inheritdoc cref="DirectoryBinding" />
    private const string SeparatorBinding = "${" + SeparatorField + "}";

    /// <summary>
    /// 挑完資料夾那則提示留多久。預設是 2500 毫秒(實際 new 一個出來讀到的),
    /// 對這條路太短 —— 見 <c>Browse</c> 裡的說明。
    /// </summary>
    private const int FolderPickedToastMs = 8000;

    /// <summary>按鈕靠 <c>Action.Submit</c> 的 data 表明自己是誰 —— 兩顆按鈕走的是同一個 SubmitForm。</summary>
    private const string ActionKey = "action";
    private const string BrowseAction = "browse";

    private readonly SettingsManager _settings;

    public InklingSettingsForm(SettingsManager settings)
    {
        _settings = settings;

        TemplateJson = BuildTemplate(settings);

        // 路徑與分隔符都是使用者輸入的，一律經由 DataJson 帶進去。直接拼進 TemplateJson 的話，
        // 裡面的 ${...} 會被樣板引擎當成佔位符解讀 —— 跟筆記內文同一個理由。
        // 分隔符尤其要小心:那個欄位本來就是拿來放標點的，`${` 打得出來。
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

        // Input.Toggle 回來的是 valueOn / valueOff 那兩個字串，不是 JSON 的 true/false。
        var preview = string.Equals(
            form[PreviewField]?.ToString(), ToggleOn, StringComparison.OrdinalIgnoreCase);

        if (ActionOf(data) == BrowseAction)
        {
            return Browse(directory, separator, preview);
        }

        var outcome = _settings.Apply(directory, separator, preview);

        // **分兩種:存成功就帶一則 toast 回主頁，存不成就留在原地讓使用者改。**
        //
        // 兩個通道不能亂配，而配錯的代價是「訊息根本不會出現」:
        //
        // - `ToastStatusMessage`(底部 InfoBar + 徽章)**綁在當下這一頁的 view model 上**,
        //   回傳 `GoHome()` 導覽走的時候會連同訊息一起拆掉。成功那兩條以前正是這個組合，
        //   所以「設定已儲存」與「已儲存 —— 這個資料夾還不存在…」**一個字都沒真的出現過**
        //   (2026-08-23 在這一頁上 A/B 過:填相對路徑那條回 `KeepOpen`,800 毫秒讀得到
        //   `StatusBar` 加徽章計數 1;正常存檔那條回 `GoHome`，整棵 UIA 樹一個字都沒有)。
        // - `CommandResult.ShowToast` 是**獨立視窗**，導覽拆不掉它，所以要跨頁活下來只有它。
        //
        // **而 toast 不會把面板關掉。** 那個 toast 視窗是 `WS_EX_TOOLWINDOW | WS_DISABLED`,
        // **它拿不到前景**;`Result` 才是決定面板去留的東西。完整的量測、三種 `Result`
        // 的對照表，以及硬規則 8 那條假前提是怎麼被推翻的，見 docs/design-notes.md
        // 〈toast 不會把面板關掉〉。**清單頁(複製、刪除)也量過了**，結論一樣。
        switch (outcome)
        {
            case SettingsManager.ApplyResult.RejectedRelativePath:
                // 整筆都沒存，表單留在原地:使用者打的東西還在卡片上，改完路徑再送一次就好。
                return Feedback.Stay(Resources.SettingsDirectoryRejected);

            case SettingsManager.ApplyResult.SaveFailed:
                // 值在這個工作階段生效了，但沒寫進 settings.json —— 重啟就還原。
                // 留在原地(KeepOpen):使用者打的東西還在卡片上，排掉問題再送一次就好，
                // 而 GoHome 會讓那句話跟著這一頁一起走掉。
                return Feedback.Stay(Resources.SettingsSaveFailed);

            case SettingsManager.ApplyResult.AppliedToMissingFolder:
                // 存是存了，但資料夾還不存在 —— 當場講，「打錯一個字就換了家」才不會無聲發生。
                // **這一則尤其不能丟**:它存在的唯一理由就是那個無聲的換家，而 toast 是
                // 唯一能跨過導覽活下來的通道。
                return Feedback.Home(Strings.Format(
                    Resources.SettingsDirectoryWillBeCreated, _settings.NotesDirectory));

            default:
                return Feedback.Home(Resources.SettingsSaved);
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
                // 選好就直接存，不等使用者再按一次「儲存」:對話框一拿到焦點，CmdPal 主視窗
                // 就會把自己藏起來(MainWindow 的 Deactivated → HideWindow，沒有開關可以關掉),
                // 這張表單跟著一起消失 —— 那時候還壓在表單裡的值，使用者既看不到也按不到。
                //
                // 其他欄位一起套用，因為那就是使用者按下「瀏覽…」當下卡片上顯示的值 ——
                // 表單既然會消失，壓在上面的改動就只有這一次機會存下來。
                // 傳回值不用看:挑選器回來的一定是存在的完整路徑，兩條拒絕/提醒的路都踩不到。
                // Apply 會同步發 Applied,provider 收到就叫設定頁重讀 —— 這條路以前
                // 在這裡又自己叫了一次，同一次挑選把卡片重建兩遍。要靠的是同一個機制:
                // 送出表單那條路本來就只有它，兩邊分開的話，哪天事件那頭壞了也只會
                // 壞掉一半，反而更難查。
                _settings.Apply(picked, separator, preview);

                // **這一則提示是盡力而為的，不是保證看得到。** 它畫在 CmdPal 的主面板上，
                // 而那個面板在對話框拿到焦點的當下就把自己藏起來了(第 7 條那個機制)——
                // 使用者挑完資料夾回到 CmdPal 時，預設的 2500 毫秒多半已經走完。
                // 撐長一點讓它有機會還在，但**真正可靠的確認是卡片本身**:Apply 之後
                // provider 會叫設定頁重讀，回到這一頁時資料夾欄位顯示的就是剛挑的那個。
                Feedback.Say(
                    Strings.Format(Resources.SettingsFolderPicked, picked), FolderPickedToastMs);
            },
            // 對話框開不起來也要講一聲，否則「瀏覽…」看起來像壞掉 —— 之前只有 DiagnosticLog
            // 留一行字，而它預設是關的。用 InfoBadge:不開視窗、不關面板，表單留在原地。
            failed: () => Feedback.Say(Resources.SettingsFolderPickerFailed));

        if (!opened)
        {
            return Feedback.Stay(Resources.SettingsPickerAlreadyOpen);
        }

        return CommandResult.KeepOpen();
    }

    /// <summary>
    /// 卡片的排版。
    ///
    /// 「瀏覽…」跟輸入框放在同一個 <c>ColumnSet</c> 裡，按鈕那一欄靠底對齊 ——
    /// 輸入框頭上還有一行 <c>label</c>，不對齊的話按鈕會浮在框的上緣。
    ///
    /// 分隔符那一格也包在 <c>ColumnSet</c> 裡，但目的相反:限寬。它只放得下兩三個字元，
    /// 一個佔滿整頁的輸入框會讓人以為該填一長串，設定視窗開大的時候特別難看。
    ///
    /// <b>三個設定項之間靠間距切開，不是線。</b> 這裡以前宣告了 <c>"separator": true</c>
    /// 配 <c>spacing: default</c>(8px)，而**那條線從來沒有被畫出來** ——
    /// 2026-08-22 實機截圖逐列掃過，淺色主題下也一樣沒有(所以不是「深色背景上看不見」)。
    /// CmdPal 沒有給擴展任何 hostConfig 的入口，線的粗細與顏色我們碰不到，查不出為什麼
    /// 沒渲染也改不動它。留著一個不生效的宣告，下一個人只會以為線是別的地方弄丟的。
    ///
    /// 拿掉線之後間距要自己撐:走 Adaptive Cards 的預設階梯
    /// (none 0 / small 3 / default 8 / medium 20 / large 30)，用 <c>medium</c>。
    /// <c>default</c> 是配合線才夠的 —— 少了線，上一項的說明會直接黏著下一項的標籤，
    /// 看不出哪句話屬於哪個欄位。
    ///
    /// 「記下後先看一眼」那個 <c>Input.Toggle</c> 不必包 <c>ColumnSet</c>:核取方塊的寬度
    /// 本來就只有方塊加標題那麼寬，撐不開版面。它的欄位名寫在 <c>title</c> 而不是
    /// <c>label</c> —— 那個控件的字本來就長在方塊旁邊，再加一個 label 會變成同一句話印兩次。
    ///
    /// <b>⚠ 代價是這個核取方塊在 UIA 上沒有名字，而且存過一次檔之後會撿到「瀏覽…」。</b>
    /// 渲染器是拿 <c>label</c> 去設 <c>AutomationProperties.Name</c> 的，沒有 label 就不設 ——
    /// 剛進頁面時 <c>Name</c> 是空的，而這一頁每次存檔都會重建卡片(<see cref="InklingSettingsPage.Refresh"/>),
    /// 重建之後它就繼承到上面那顆「瀏覽…」按鈕的名字。**三個選項 2026-08-23 都實機量過了，
    /// 不要再試一遍**:
    ///
    /// <list type="bullet">
    /// <item>只有 <c>title</c>(現在這樣)—— 畫面對，<c>Name</c> 空的 / 重建後是「瀏覽…」。</item>
    /// <item><c>label</c> + <c>title</c> —— <c>Name</c> 修好了，但同一句話在標題列與方塊旁邊各印一次。</item>
    /// <item><b>只有 <c>label</c> —— 整張卡片渲染不出來，設定頁一片全白。</b>不是排版走樣，是完全空白 ——
    ///   也就是說這個渲染器上 <c>Input.Toggle</c> 沒有 <c>title</c> 就不合法，而它不報錯也不退回預設樣子。</item>
    /// </list>
    ///
    /// 也就是說能修好名字的寫法都要動到畫面，那是設計決定不是修 bug，所以現在維持原樣。
    /// 考證與「什麼變了才該重新考慮」見 docs/design-notes.md〈評估過但沒有做〉。
    ///
    /// 說明文字擺在欄位**下面**當註腳，而不是像 toolkit 那樣頂在標籤的位置，
    /// 而且**每個欄位下面各一塊，沒有例外** —— 卡片最上面曾經另外有一行「整頁共通的提醒」,
    /// 但那句話講的只是筆記資料夾，結果那個欄位變成唯一上下都有說明的。
    /// 已經併進 <c>NotesDirectorySetting</c> 的說明裡，要加類似的話也照這條走，
    /// 不要在卡片頂上再開一塊。
    ///
    /// 卡片層級只留「儲存」一顆。這是防禦性設計，依據在 CmdPal 的 **main 分支**:
    /// 在單行輸入框裡按 Enter 時，它送出的是 <c>card.Actions</c> 的第一個
    /// (<c>ContentFormControl.OnFormKeyDown</c>)—— 打完路徑按 Enter 應該是存檔，
    /// 不是跳出「瀏覽」對話框。**0.11.11762.0 沒有那段程式碼**(byte-scan 掃不到，
    /// 見 docs/design-notes.md〈編輯表單〉)，使用者手上單行框按 Enter 不會送出，Tab 到「儲存」
    /// 是唯一的鍵盤路徑。只留一顆的安排兩個版本下都對，所以維持。
    /// </summary>
    /// <summary>
    /// 設定檔壞掉被搬走時，卡片最上面那塊警告(沒發生就是空字串，含後面那個逗號)。
    ///
    /// <b>這是卡片頂上唯一允許出現的一塊。</b> 上面那段說明寫著「不要在卡片頂上再開一塊」,
    /// 講的是**常駐**的說明文字 —— 那種東西會讓某個欄位變成唯一上下都有字的。
    /// 這一塊不一樣:它不是說明而是錯誤，絕大多數時候根本不存在，而且非放最上面不可
    /// —— 使用者會來這一頁，正是因為「筆記全部不見了」(資料夾被退回預設值),
    /// 那句解釋要在他看到資料夾欄位**之前**就讀到。
    ///
    /// <c>attention</c> 是 Adaptive Cards 內建的警示色，不必自己碰顏色。
    /// </summary>
    private static string CorruptSettingsWarning(SettingsManager settings)
    {
        if (settings.QuarantinedFile is not { } quarantined)
        {
            return string.Empty;
        }

        var text = Strings.Format(Resources.SettingsCorruptWarning, Path.GetFileName(quarantined));

        return $$"""
            {
                "type": "TextBlock",
                "text": {{CardText.Json(text)}},
                "wrap": true,
                "color": "attention",
                "weight": "bolder"
            },
            """;
    }

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
                {{CorruptSettingsWarning(settings)}}{
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
                    "spacing": "medium",
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
                    "spacing": "medium"
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
