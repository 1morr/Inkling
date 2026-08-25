---
name: add-adaptive-card-form
description: >-
  Create form-based UI for your Command Palette extension using Adaptive Cards.
  Use when asked to add forms, user input fields, toggle switches, text inputs,
  dropdown menus, data entry, surveys, configuration dialogs, or interactive
  content pages. Supports the Adaptive Cards Designer for visual form building.
---

> **這份是 CmdPal 官方擴展模板附的 skill，正文原封不動搬進來。**
>
> **本專案踩過的坑，它沒寫:**
>
> 1. **能調的極少。** 欄位順序決定游標落在哪一格(沒有 autofocus / tabIndex);
>    **游標在那一格裡的位置完全指定不了**(CmdPal 只做 `Focus(FocusState.Programmatic)`,
>    `Input.Text` 沒有 caret / selection 屬性)—— 編輯頁「游標放到內文最後」查過做不到，
>    現在是在卡片底部提示按 `Ctrl+End`;多行輸入框的高度也不可控(只能靠預填內容撐開);
>    沒有 `Ctrl+S`。[設計考證〈編輯表單〉](../../../docs/design-notes.md#edit-form)。
> 2. **卡片裡的字串一律走資源檔，而且一定要經過 `CardText.Json` 跳脫** ——
>    翻譯裡一個雙引號就能讓整張卡片變成不合法的 JSON。
> 3. **使用者內容絕不直接拼進 `TemplateJson`**，一律經 `DataJson` 帶進去:內文裡本來就可能
>    出現 `${...}`(寫程式筆記時很常見)，拼進模板會被樣板引擎當成佔位符解讀。
> 4. **卡片層級的第一顆 action 就是 Enter 送出的那顆。** 機制名
>    `ContentFormControl.OnFormKeyDown` 來自 `main` 分支 —— 0.11.11762.0 安裝版掃不到它
>    (見 [設計考證〈編輯表單〉](../../../docs/design-notes.md#edit-form));「第一顆 action = Enter 送出」在本機是實測成立的結論，
>    機制歸因以設計考證的版本註記為準。
> 5. **送出後導頁不能靠回傳值** —— `CommandResult.GoToPage` 是空殼，CmdPal 的 switch 裡沒有
>    那個 case。唯一還通的路是讓那一列的命令**本身就是一個 `IPage`，副作用寫在 `GetContent()`
>    裡並自己上一次性旗標(`CapturedNotePage` 就是這個形狀)。[設計考證〈記下之後要不要先看一眼〉](../../../docs/design-notes.md#capture-preview)。
> 6. `TextBlock` 不是 `Control`，加說明文字不會把焦點從輸入框搶走。

# Add Forms with Adaptive Cards

Create interactive forms in your Command Palette extension using Adaptive Cards. Forms allow you to collect user input through text fields, toggles, dropdowns, and other controls.

## When to Use This Skill

- Adding a form to collect user input (name, settings, feedback)
- Creating interactive configuration dialogs
- Building data entry interfaces
- Adding toggle switches or dropdown menus
- Displaying complex layouts beyond simple lists

## Prerequisites

- Familiarity with [Adaptive Cards](https://adaptivecards.io/)
- Optional: Use the [Adaptive Card Designer](https://adaptivecards.io/designer/) to visually build your form

## Quick Start

### Step 1: Create a ContentPage with FormContent

Create a new file in your `Pages/` directory:

```csharp
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System.Text.Json.Nodes;

namespace YourExtension;

internal sealed partial class MyFormPage : ContentPage
{
    private readonly MyForm _form = new();

    public MyFormPage()
    {
        Name = "Open";
        Title = "My Form";
        Icon = new IconInfo("\uECA5");
    }

    public override IContent[] GetContent() => [_form];
}

internal sealed partial class MyForm : FormContent
{
    public MyForm()
    {
        TemplateJson = """
        {
            "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
            "type": "AdaptiveCard",
            "version": "1.6",
            "body": [
                {
                    "type": "Input.Text",
                    "label": "Name",
                    "id": "Name",
                    "isRequired": true,
                    "errorMessage": "Name is required",
                    "placeholder": "Enter your name"
                }
            ],
            "actions": [
                {
                    "type": "Action.Submit",
                    "title": "Submit"
                }
            ]
        }
        """;
    }

    public override CommandResult SubmitForm(string payload)
    {
        var formInput = JsonNode.Parse(payload)?.AsObject();
        if (formInput == null)
        {
            return CommandResult.GoHome();
        }

        var name = formInput["Name"]?.ToString() ?? "Unknown";
        return CommandResult.ShowToast($"Hello, {name}!");
    }
}
```

### Step 2: Register the Page

In your `CommandsProvider`, add the form page:

```csharp
_commands = [
    new CommandItem(new MyFormPage()) { Title = "My Form" },
];
```

### Step 3: Deploy and Test

1. Deploy your extension
2. In Command Palette, run `Reload`
3. Navigate to your form and submit it

## Key Concepts

### TemplateJson
The JSON layout of your form (from Adaptive Cards schema). Design it at https://adaptivecards.io/designer/

### DataJson (Optional)
Dynamic data binding using `${...}` placeholders in your TemplateJson:
```csharp
TemplateJson = """{ "body": [{ "type": "TextBlock", "text": "${title}" }] }""";
DataJson = """{ "title": "Dynamic Title" }""";
```

### SubmitForm
Called when the user submits. Parse `payload` as JSON to read input values by their `id`.

### Mixing Content Types
You can combine forms with markdown on the same page:
```csharp
public override IContent[] GetContent() => [
    new MarkdownContent("# Instructions\nFill out the form below."),
    _form,
];
```

## Common Form Patterns

See [form-patterns.md](references/form-patterns.md) for template JSON for common form types.

## Documentation

- [Get user input with forms](https://learn.microsoft.com/windows/powertoys/command-palette/using-form-pages)
- [Adaptive Card Designer](https://adaptivecards.io/designer/)
- [Adaptive Cards Schema](https://adaptivecards.io/explorer/)
