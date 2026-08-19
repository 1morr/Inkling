---
name: cmdpal-extension-api
description: >-
  Command Palette 擴展 API 的速查:頁面型別(ListPage / DynamicListPage /
  ContentPage)、內容型別(Markdown / Form / PlainText / Image / Tree)、命令與
  CommandResult、ListItem 的屬性、Sections 與 Grid 版面、圖示、動態更新、狀態訊息。
  問到「CmdPal 的某個型別怎麼用」「有哪些 CommandResult」「Details 有哪些欄位」時看這份。
  Use when working with Command Palette SDK types, pages, content, or commands.
---

> **這份是 CmdPal 官方擴展模板附的文檔,正文原封不動搬進來當 API 速查。**
> 來源:`TemplateExtension/.github/instructions/cmdpal-extension.instructions.md`。
>
> **它有幾處跟本專案實測到的行為衝突,照做會壞掉。以這一段為準:**
>
> 1. **下面〈Status Messages and Toasts〉那兩行是兩種完全不同的東西,只有一種會關掉面板。**
>
>    - **`CommandResult.ShowToast(...)`(以及 `CopyTextCommand` 的預設 `Result`)會讓面板消失。**
>      它送的是 `ShowToastMessage`,由 CmdPal 開一個獨立的 `ToastWindow` —— 那個視窗會搶焦點,
>      而主視窗一失焦就自我隱藏。`ToastArgs.Result = KeepOpen` 救不回來,搶焦點的是視窗本身。
>      [設計考證〈刪除成功時一個 toast 都不發〉](../../../docs/design-notes.md#delete-no-toast)。
>    - **`new ToastStatusMessage(...).Show()` 不會。** 它名字裡有 Toast,但根本沒開視窗:
>      呼叫的是 `IExtensionHost.ShowStatus`,由 CmdPal 加進 `StatusMessages`,顯示成底部命令列
>      左邊那個 `InfoBadge`(點開是 flyout 裡的 `InfoBar`),2.5 秒後自己收掉。
>      擴展跑在自己的進程裡,本來就開不了 CmdPal 的視窗 —— 它能做的只有呼叫 host。
>      本專案的存檔提示走的就是這條(`NoteFormContent`、`NoteletSettingsForm`)。
>
>    要「做完之後留在原地」而且**不想離開清單**,還有第三條路:`ListItem.Tags`
>    就地改一列的狀態(見 `NoteListPage.FlashTag`)。
> 2. **`ListItem.Details` 只能整個換掉,不能就地改屬性。** 下面把它寫成一般屬性,但 `IDetails`
>    在 SDK IDL 裡沒有宣告成可觀察介面,通知跨不過 out-of-process 邊界 —— 值改了畫面不動。
>    `Details.Size` 更只在初始化時讀一次,不明著寫就是最窄那一檔。
> 3. **每個頂層命令都要有固定 `Id`**(`src/Notelet/CommandIds.cs`)。下面的範例全都沒設。
>    沒設的話 CmdPal 拿 `ProviderId + DisplayTitle + Title + Subtitle` 算雜湊當身分,
>    標題改一個字,使用者的 alias / 快速鍵 / 釘選就全部對不上。
> 4. **`Debug.Write()` 在這個專案沒用。** 它掛著 `[Conditional("DEBUG")]`,而日常安裝的是 Release。
>    用 `DiagnosticLog`,見 README〈排錯:讓擴展自己說話(DiagnosticLog)〉。
> 5. **〈Build & Debug〉那節是 Visual Studio 流程,這台機器沒有 VS。** 走 `tools\deploy.ps1`。
> 6. **圖示碼位不要寫成 `\uXXXX`。** 本專案的 `Icons.cs` 用 `Glyph(0xE70B)` —— `\u` 逸出
>    被文字處理工具碰到會**無聲地**變成一個私用區字元,檔案看起來還是好的,圖示卻全部消失。
> 7. **介面字串不准寫在程式碼裡**,一律走 `Properties/Resources.resx`(英文 / 繁中 / 簡中三份),
>    見 CLAUDE.md〈慣例〉。下面的範例為了簡潔都是寫死的字串。
> 8. `CommandResult.Confirm` 的 `IsPrimaryCommandCritical` 在 0.11.11762.0 **完全沒有效果**
>    (整個套件掃不到 `set_DefaultButton`),確認框的按鈕也碰不到任何顏色。
>    見 [設計考證〈確認框的按鈕沒有顏色,也沒有「危險」樣式〉](../../../docs/design-notes.md#confirm-dialog-colors)。
> 9. **正文 `KeyChordHelpers.FromModifiers` 的範例是舊的 4 參數簽章,照抄編譯不過。**
>    現行 SDK 是 6 參數:`(ctrl, alt, shift, win, vkey, scanCode)`(另有 `VirtualKey` 多載),
>    `dotnet run --project tools\ApiDump -- KeyChordHelpers` 可複驗;本專案的用法見
>    `src/Notelet/Shortcuts.cs`(6 參數具名呼叫)。

# Command Palette Extension Development

Complete reference for building Command Palette (CmdPal) extensions. Extensions run out-of-process as MSIX-packaged COM servers.

## Extension Architecture

### IExtension Interface

The root class implements `IExtension` and `IDisposable`:

```csharp
[Guid("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF")]
public sealed partial class MyExtension : IExtension, IDisposable
{
    private readonly ManualResetEvent _extensionDisposedEvent;
    private readonly MyCommandsProvider _provider = new();

    public MyExtension(ManualResetEvent extensionDisposedEvent)
    {
        _extensionDisposedEvent = extensionDisposedEvent;
    }

    public object? GetProvider(ProviderType providerType) => providerType switch
    {
        ProviderType.Commands => _provider,
        _ => null,
    };

    public void Dispose() => _extensionDisposedEvent.Set();
}
```

- Only `ProviderType.Commands` is currently supported
- The `[Guid]` must match the CLSID in `Package.appxmanifest`

### CommandProvider

Override `TopLevelCommands()` to register main commands. Optionally override `FallbackCommands()` and `GetDockBands()`:

```csharp
public partial class MyCommandsProvider : CommandProvider
{
    public MyCommandsProvider()
    {
        DisplayName = "My Extension";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
    }

    public override ICommandItem[] TopLevelCommands() => [
        new CommandItem(new MyPage()) { Title = DisplayName },
    ];
}
```

### COM Server (Program.cs)

`Program.cs` hosts the COM server. Do not change this pattern:

```csharp
public class Program
{
    [MTAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "-RegisterProcessAsComServer")
        {
            global::Shmuelie.WinRTServer.ComServer server = new();
            ManualResetEvent extensionDisposedEvent = new(false);
            var extensionInstance = new MyExtension(extensionDisposedEvent);
            server.RegisterClass<MyExtension, IExtension>(() => extensionInstance);
            server.Start();
            extensionDisposedEvent.WaitOne();
            server.Stop();
            server.UnsafeDispose();
        }
    }
}
```

### Package.appxmanifest

Two critical extension registrations must be present:

1. **COM server** — `com:ComServer` with matching CLSID and `-RegisterProcessAsComServer` args
2. **App extension** — `uap3:AppExtension` with `Name="com.microsoft.commandpalette"` and `CreateInstance ClassId` matching the GUID

The CLSID must be identical in three places: the `[Guid]` attribute, the `com:Class Id`, and the `CreateInstance ClassId`.

## Page Types

### ListPage (Most Common)

Displays a searchable list of items:

```csharp
internal sealed partial class MyPage : ListPage
{
    public MyPage()
    {
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        Title = "My page";
        Name = "Open";
    }

    public override IListItem[] GetItems() => [
        new ListItem(new OpenUrlCommand("https://example.com")) { Title = "Example" },
    ];
}
```

### DynamicListPage (Search-Reactive)

Responds to search text changes for filtering or live queries:

```csharp
internal sealed partial class MyDynamicPage : DynamicListPage
{
    private IListItem[] _filteredItems = [];

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        _filteredItems = _allItems
            .Where(i => i.Title.Contains(newSearch, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        RaiseItemsChanged();
    }

    public override IListItem[] GetItems() => _filteredItems;
}
```

- Supports `Filters` property for category filtering
- Call `RaiseItemsChanged()` after updating items to notify the UI

### ContentPage (Rich Content)

Displays rich content like markdown, forms, or images:

```csharp
internal sealed partial class MyContentPage : ContentPage
{
    public override IContent[] GetContent() => [
        new MarkdownContent("# Hello\nThis is **markdown**."),
    ];
}
```

- Can return multiple `IContent` items (mix markdown, forms, images, etc.)
- Supports `Commands` property for context menu items via `CommandContextItem`

## Content Types

| Type | Description |
|------|-------------|
| `MarkdownContent(string)` | Renders markdown with headers, links, code blocks, tables, images |
| `FormContent` | Adaptive Cards forms with `TemplateJson`, optional `DataJson`, and `SubmitForm()` |
| `PlainTextContent(string)` | Plain text; optional `FontFamily.Monospace` and `WrapWords` |
| `ImageContent` | Images with `MaxWidth`/`MaxHeight` constraints |
| `TreeContent` | Hierarchical nested content; override `GetChildren()` for child `IContent[]` |

### MarkdownContent Images

Supports `file:`, `data:` (base64), and `https:` URLs. Image hints control rendering:

```markdown
![alt](https://example.com/img.png?--x-cmdpal-fit=fit&--x-cmdpal-maxwidth=400)
```

### FormContent (Adaptive Cards)

```csharp
internal sealed partial class MyForm : FormContent
{
    public MyForm()
    {
        TemplateJson = """{ "type": "AdaptiveCard", ... }""";
        DataJson = """{ "name": "default" }""";
    }

    public override CommandResult SubmitForm(string payload)
    {
        var data = JsonSerializer.Deserialize<MyFormData>(payload);
        return CommandResult.Dismiss();
    }
}
```

- Design cards visually at [adaptivecards.io/designer](https://adaptivecards.io/designer)
- Use `${...}` placeholders in `TemplateJson` bound to `DataJson` properties

## Commands

### InvokableCommand

Actions that do something when activated:

```csharp
internal sealed partial class MyCommand : InvokableCommand
{
    public override string Name => "Do it";
    public override IconInfo Icon => new("\uE945");

    public override CommandResult Invoke()
    {
        // Do work here
        return CommandResult.Dismiss();
    }
}
```

### Built-in Command Helpers

| Helper | Purpose |
|--------|---------|
| `OpenUrlCommand(string url)` | Open URL in default browser |
| `CopyTextCommand(string text)` | Copy to clipboard with toast |
| `NoOpCommand()` | Does nothing (placeholder) |
| `AnonymousCommand(Action? action)` | Lambda command; set `Result` property for navigation |

### CommandResult Types

| Result | Behavior |
|--------|----------|
| `CommandResult.Dismiss()` | Hide palette, go home |
| `CommandResult.KeepOpen()` | Stay on current page |
| `CommandResult.Hide()` | Hide palette, keep page state |
| `CommandResult.GoBack()` | Navigate back one page |
| `CommandResult.GoHome()` | Navigate to home page |
| `CommandResult.ShowToast("msg")` | Show toast notification, then dismiss |
| `CommandResult.Confirm(args)` | Show confirmation dialog before proceeding |

## ListItem Properties

```csharp
new ListItem(command)
{
    Title = "Display name",
    Subtitle = "Secondary text",
    Icon = new IconInfo("\uE8A7"),
    Tags = [new Tag("label") { Foreground = ColorHelpers.FromRgb(255, 0, 0) }],
    Details = new Details
    {
        Title = "Detail panel",
        Body = "**Markdown** body",
        HeroImage = IconHelpers.FromRelativePath("Assets\\hero.png"),
        Size = ContentSize.Medium,
        Metadata = [
            new DetailsLink("URL", "https://example.com"),
            new DetailsSeparator(),
        ],
    },
    MoreCommands = [
        new CommandContextItem(deleteCommand)
        {
            RequestedShortcut = KeyChordHelpers.FromModifiers(
                true, false, false, (int)VirtualKey.Delete),
        },
    ],
}
```

## Sections and Grid Layouts

### Sections

Group items under section headers:

```csharp
public override ISection[] GetSections() => [
    new Section { Title = "Group A", Items = itemsA },
    new Section { Title = "Group B", Items = itemsB },
];
```

### Grid Layouts

Set `GridProperties` on a `ListPage`:

| Layout | Description |
|--------|-------------|
| `GalleryGridLayout()` | Large tiles with title + subtitle |
| `SmallGridLayout()` | Compact grid |
| `MediumGridLayout()` | Medium tiles with title |

## Icons

```csharp
// Segoe Fluent UI icons (most common)
new IconInfo("\uE8A5")                                    // Document
new IconInfo("\uE945")                                    // Lightning bolt

// Emoji
new IconInfo("📂")

// Image from package assets
IconHelpers.FromRelativePath("Assets\\StoreLogo.png")

// Remote URL or SVG
new IconInfo("https://example.com/icon.svg")

// From exe/dll resource
new IconInfo("%systemroot%\\system32\\shell32.dll,3")
```

## Dynamic Updates

- Call `RaiseItemsChanged()` on any page to trigger a UI refresh of its items
- Call `RaisePropertyChanged(propertyName)` for individual property updates (e.g., title)
- For top-level command changes, call `RaiseItemsChanged()` on the `CommandProvider`
- Use `System.Timers.Timer` for periodic background updates

## Status Messages and Toasts

```csharp
// Inline status message (e.g., loading indicator)
var msg = new StatusMessage
{
    Message = "Loading...",
    State = MessageState.Info,
    Progress = new ProgressState { IsIndeterminate = true },
};
ExtensionHost.ShowStatus(msg, StatusContext.Page);
ExtensionHost.HideStatus(msg);

// Transient toast notification
new ToastStatusMessage("Copied to clipboard").Show();
```

## Build & Debug

1. Select **Debug** configuration
2. **Deploy** via Build > Deploy (not just Build) — this registers the MSIX package
3. Press **F5** to launch with debugger attached
4. Use `Debug.Write()` / `Debug.WriteLine()` for diagnostic output
5. Check Output window (**Ctrl+Alt+O**) set to "Debug"
6. In Command Palette, run `Reload` → "Reload Command Palette extensions"

Use the `(Package)` launch profile, not `(Unpackaged)`.

## Common Mistakes

| Mistake | Fix |
|---------|-----|
| Building without deploying | Use Build > Deploy so the MSIX package is updated |
| Running "(Unpackaged)" profile | Select the "(Package)" launch profile |
| Forgetting to reload extensions | Run `Reload` in Command Palette after deploying |
| CLSID mismatch | Ensure `[Guid]` in .cs matches `ClassId` in Package.appxmanifest (both places) |
| Logging in hot paths | `GetItems()` is called frequently — avoid expensive work or logging here |
