---
name: add-fallback-commands
description: >-
  Add fallback commands to your Command Palette extension for catch-all search behavior.
  Use when asked to add search functionality, query matching, direct input handling,
  calculator-style evaluation, URL opening, command execution, or results that appear
  when no other extension matches. Used by 14 of 20 built-in extensions.
---

> **這份是 CmdPal 官方擴展模板附的 skill,正文原封不動搬進來當參考。**
>
> **本專案刻意不用 fallback。** 這條路完整做完過,最後整個移除
> (型別叫 `QuickCaptureFallbackItem`,`git log --diff-filter=D` 找得到)。
>
> 原因:只有 fallback 拿得到使用者正在打的字(`UpdateQuery`),但沒命中前綴時我們只能把
> `Title` 設成空字串,而 0.11.11762.0 **只在底部 fallback 區塊那條路濾空標題** ——
> 勾了「Include in the Global result」走的 `_scoredFallbackItems` 沒濾,每次搜索都多一個
> 點不動的空列;不勾又會排在所有結果後面、失去意義。**這不是我們能修的。**
> 查證過程見 README〈快速記下為什麼是頁面,不是 fallback〉。
>
> 現在的入口是 `QuickCapturePage` + 使用者自設的 alias,按鍵數一樣。
>
> **要重新考慮之前先讀那一節**,而且注意 `Notelet.QuickCapture` 這個 Id 曾經掛在 fallback 上、
> 八成還躺在使用者的 CmdPal `settings.json` 裡,**不要拿它去給別的東西用**。

# Add Fallback Commands

Fallback commands are shown in Command Palette when no other results match the user's query. They enable your extension to act as a catch-all handler — perfect for calculators, web search, command execution, file path opening, and more.

## When to Use This Skill

- Adding search functionality that responds to any user input
- Creating a calculator that evaluates expressions as the user types
- Building a web search that triggers on unmatched queries
- Opening files or URLs typed directly into the palette
- Executing shell commands from the search bar

## How Fallback Commands Work

1. User types a query in Command Palette
2. If no top-level commands match, CmdPal asks extensions for fallback results
3. Your extension's `FallbackCommands()` provides items that respond to the query
4. The fallback items can be static (always shown) or dynamic (filtered by query)

## Quick Start: Static Fallback

Override `FallbackCommands()` in your `CommandProvider`:

```csharp
public partial class MyCommandsProvider : CommandProvider
{
    private readonly ICommandItem[] _commands;
    private readonly FallbackCommandItem[] _fallbacks;

    public MyCommandsProvider()
    {
        DisplayName = "Web Search";
        Icon = new IconInfo("\uE721"); // Search icon

        var searchPage = new WebSearchPage();
        _commands = [new CommandItem(searchPage) { Title = DisplayName }];
        _fallbacks = [new FallbackCommandItem(searchPage) { Title = "Search the web" }];
    }

    public override ICommandItem[] TopLevelCommands() => _commands;
    public override IFallbackCommandItem[] FallbackCommands() => _fallbacks;
}
```

## Dynamic Fallback with DynamicListPage

For fallbacks that filter results based on the query, use `DynamicListPage`:

```csharp
internal sealed partial class WebSearchPage : DynamicListPage
{
    private string _query = string.Empty;

    public WebSearchPage()
    {
        Icon = new IconInfo("\uE721");
        Title = "Web Search";
        Name = "Search";
        PlaceholderText = "Type to search...";
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        _query = newSearch;
        RaiseItemsChanged();
    }

    public override IListItem[] GetItems()
    {
        if (string.IsNullOrWhiteSpace(_query))
            return [];

        return [
            new ListItem(new OpenUrlCommand($"https://www.google.com/search?q={Uri.EscapeDataString(_query)}"))
            {
                Title = $"Search Google for \"{_query}\"",
                Icon = new IconInfo("\uE721"),
            },
            new ListItem(new OpenUrlCommand($"https://www.bing.com/search?q={Uri.EscapeDataString(_query)}"))
            {
                Title = $"Search Bing for \"{_query}\"",
                Icon = new IconInfo("\uE721"),
            },
        ];
    }
}
```

## Responsive Fallback with Cancellation

For expensive operations (API calls, file searches), use cancellation to stay responsive:

```csharp
internal sealed partial class SmartSearchPage : DynamicListPage
{
    private CancellationTokenSource? _cts;
    private IListItem[] _results = [];

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        // Cancel any in-flight search
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _ = Task.Run(async () =>
        {
            // Debounce: wait for user to stop typing
            await Task.Delay(300, token);
            if (token.IsCancellationRequested) return;

            // Perform search
            _results = await SearchAsync(newSearch, token);
            RaiseItemsChanged();
        }, token);
    }

    public override IListItem[] GetItems() => _results;

    private async Task<IListItem[]> SearchAsync(string query, CancellationToken token)
    {
        // Your search logic here
        // Check token.IsCancellationRequested periodically
        return [];
    }
}
```

## Real-World Examples (from built-in extensions)

| Extension | Fallback Behavior |
|-----------|------------------|
| **Apps** | Search installed applications by name |
| **Calc** | Evaluate mathematical expressions directly |
| **Shell** | Execute command-line commands |
| **WebSearch** | Search the web with configured engine |
| **Indexer** | Open files by path |
| **TimeDate** | Parse time/date queries |
| **WindowsSettings** | Jump to Windows Settings pages |
| **WinGet** | Search WinGet packages |
| **WindowWalker** | Find and switch to open windows |

## Key Points

- `FallbackCommands()` returns `IFallbackCommandItem[]` (not `ICommandItem[]`)
- Use `FallbackCommandItem` wrapper (not `CommandItem`)
- Wrap a `DynamicListPage` for query-reactive results
- Cancel previous searches when new input arrives
- Keep fallback responses fast — users expect instant results
- Use `PlaceholderText` on your page to guide users

## Documentation

- [Extension samples](https://learn.microsoft.com/windows/powertoys/command-palette/samples)
- [Extensibility overview](https://learn.microsoft.com/windows/powertoys/command-palette/extensibility-overview)
