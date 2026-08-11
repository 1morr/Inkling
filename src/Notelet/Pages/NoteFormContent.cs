using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Notelet.Core;

namespace Notelet.Pages;

/// <summary>
/// 新增與編輯共用的表單,用 Adaptive Cards 描述。
///
/// 需要注意的一點:筆記內容一律經由 <see cref="FormContent.DataJson"/> 帶進去,
/// 絕不直接拼進 TemplateJson。內文裡本來就可能出現 <c>${...}</c>(寫程式筆記時很常見),
/// 拼進模板的話會被樣板引擎當成佔位符解讀,輕則亂碼重則內容遺失。
/// </summary>
internal sealed partial class NoteFormContent : FormContent
{
    /// <summary>
    /// 新增時內文框預填的空行數。
    ///
    /// 為什麼要預填:Adaptive Cards 的渲染器對多行輸入只設 AcceptsReturn 與 TextWrapping,
    /// 完全不碰高度,所以空的內文框就是一行高 —— 看起來像只能寫一行。卡片這邊沒有
    /// 「幾行高」這種屬性,唯一撐得開它的就是內容本身。
    ///
    /// (Container 的 minHeight 配上 height: stretch 這條路試過了,不成立:
    ///  輸入框連同它的標籤會被包進一個 StackPanel,StackPanel 只給子元素自己要的高度,
    ///  多出來的空間留在容器裡,框還是一行。)
    ///
    /// 代價有兩個:框裡有東西了,placeholder 就不顯示;空行本身可能被存進檔案,
    /// 所以新增的存檔路徑會把前後空白去掉。
    /// </summary>
    private const int BlankBodyLines = 5;

    private const string TitleField = """
        {
            "type": "Input.Text",
            "id": "title",
            "label": "標題",
            "value": "${title}",
            "isRequired": true,
            "errorMessage": "標題不能空白",
            "placeholder": "一句話講完這個想法"
        }
        """;

    private const string BodyField = """
        {
            "type": "Input.Text",
            "id": "body",
            "label": "內文(Markdown)",
            "value": "${body}",
            "isMultiline": true,
            "placeholder": "細節、連結、待辦…"
        }
        """;

    private readonly INoteRepository _repository;
    private readonly string? _noteId;
    private readonly Action? _onSaved;

    /// <param name="note">null 代表新增;有值代表編輯既有筆記。</param>
    /// <param name="onSaved">存檔成功後的回呼,讓呼叫端有機會刷新自己的畫面。</param>
    public NoteFormContent(INoteRepository repository, Note? note, Action? onSaved = null)
    {
        _repository = repository;
        _noteId = note?.Id;
        _onSaved = onSaved;

        var editing = note is not null;

        TemplateJson = BuildTemplate(bodyFirst: editing);
        DataJson = new JsonObject
        {
            ["title"] = note?.Title ?? string.Empty,
            ["body"] = note?.Body ?? new string('\n', BlankBodyLines - 1),
        }.ToJsonString();
    }

    /// <summary>存檔成功後要往哪走。新增回首頁,編輯回上一頁(也就是預覽)。</summary>
    private CommandResult AfterSave => _noteId is null ? CommandResult.GoHome() : CommandResult.GoBack();

    public override CommandResult SubmitForm(string inputs)
    {
        var form = JsonNode.Parse(inputs)?.AsObject();

        if (form is null)
        {
            return CommandResult.KeepOpen();
        }

        var title = form["title"]?.ToString()?.Trim() ?? string.Empty;
        var body = form["body"]?.ToString() ?? string.Empty;

        if (title.Length == 0)
        {
            // Adaptive Cards 的 isRequired 已經會擋一次,這裡是防呆的第二道。
            new ToastStatusMessage("標題不能空白").Show();
            return CommandResult.KeepOpen();
        }

        try
        {
            if (_noteId is null)
            {
                // 預填的空行不該變成筆記內容。編輯時不做這件事 —— 那些空行是使用者自己的排版。
                _repository.Create(title, body.Trim());
                new ToastStatusMessage($"已新增:{title}").Show();
            }
            else
            {
                _repository.Update(_noteId, title, body);
                new ToastStatusMessage($"已儲存:{title}").Show();
            }

            // 要在回上一頁之前通知,否則上一頁會顯示存檔前的內容。
            _onSaved?.Invoke();

            return AfterSave;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NoteNotFoundException)
        {
            // 存檔失敗絕對不能無聲無息 —— 使用者會以為東西存起來了然後把視窗關掉。
            Debug.WriteLine($"[Notelet] 存檔失敗:{ex}");
            new ToastStatusMessage($"存檔失敗:{ex.Message}").Show();
            return CommandResult.KeepOpen();
        }
    }

    /// <summary>
    /// 欄位順序決定游標落在哪一格。
    ///
    /// CmdPal 進表單頁後會聚焦卡片裡第一個可聚焦的控件
    /// (<c>ContentFormControl.FindFirstFocusableElement</c>),而 Adaptive Cards
    /// 既沒有 autofocus 也沒有 tabIndex —— 想讓游標落在內文,就只能讓內文排第一。
    ///
    /// 所以編輯時內文在上、標題在下:進來就是要改內容,而標題頁首已經寫著了。
    /// 新增時反過來,先想標題。
    /// </summary>
    private static string BuildTemplate(bool bodyFirst) => $$"""
        {
            "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
            "type": "AdaptiveCard",
            "version": "1.6",
            "body": [
                {{(bodyFirst ? $"{BodyField},{TitleField}" : $"{TitleField},{BodyField}")}}
            ],
            "actions": [
                {
                    "type": "Action.Submit",
                    "title": "儲存",
                    "style": "positive"
                }
            ]
        }
        """;
}
