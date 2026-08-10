using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Notelet.Core;
using Windows.System;

namespace Notelet.Pages;

/// <summary>
/// 筆記清單與搜索。
///
/// 用 <see cref="DynamicListPage"/> 而不是 <see cref="ListPage"/>,是為了自己掌控過濾邏輯:
/// 內建的過濾只看標題,而需求要的是標題與內文都能搜。
/// </summary>
internal sealed partial class NoteListPage : DynamicListPage, IDisposable
{
    private readonly INoteRepository _repository;
    private readonly NoteletOptions _options;

    private string _query = string.Empty;
    private IListItem[]? _items;
    private string? _itemsQuery;
    private int _itemsVersion = -1;
    private bool _disposed;

    public NoteListPage(INoteRepository repository, NoteletOptions options)
    {
        _repository = repository;
        _options = options;

        Icon = Icons.Note;
        Title = "Notelet";
        Name = "開啟";
        PlaceholderText = "搜索標題與內文…";
        ShowDetails = true;

        EmptyContent = new CommandItem(new NoOpCommand())
        {
            Title = "還沒有任何筆記",
            Subtitle = $"在 Command Palette 主搜尋框打「{options.QuickCapturePrefix}你的想法」就能記下第一則",
            Icon = Icons.Note,
        };

        // 別台機器經 OneDrive 同步下來、或使用者拿別的編輯器改了檔案時自動更新。
        _repository.Changed += OnRepositoryChanged;
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        if (string.Equals(oldSearch, newSearch, StringComparison.Ordinal))
        {
            return;
        }

        _query = newSearch;
        RaiseItemsChanged();
    }

    public override IListItem[] GetItems()
    {
        // GetItems 會被頻繁呼叫,同一個查詢字串不重建項目。
        //
        // 但快取的鍵一定要帶上 repository 的 Version:只看查詢字串的話,
        // 新增一則筆記之後再回到清單頁,拿到的還是舊的那份結果 ——
        // 表現出來就是「筆記明明存好了,清單卻說還沒有任何筆記」。
        var version = _repository.Version;

        if (_items is not null
            && _itemsVersion == version
            && string.Equals(_itemsQuery, _query, StringComparison.Ordinal))
        {
            return _items;
        }

        _items = BuildItems(_query);
        _itemsQuery = _query;
        _itemsVersion = version;
        return _items;
    }

    private IListItem[] BuildItems(string query)
    {
        var matches = NoteSearch.Filter(_repository.GetAll(), query);

        var items = new List<IListItem>(Math.Min(matches.Count, _options.MaxResults) + 1);

        foreach (var note in matches.Take(_options.MaxResults))
        {
            items.Add(CreateItem(note));
        }

        // 被截掉就明講,不要讓使用者以為筆記不見了。
        if (matches.Count > _options.MaxResults)
        {
            items.Add(new ListItem(new NoOpCommand())
            {
                Title = $"還有 {matches.Count - _options.MaxResults} 則沒顯示",
                Subtitle = "再多打幾個字縮小範圍",
                Icon = Icons.Note,
            });
        }

        return [.. items];
    }

    private ListItem CreateItem(Note note) => new(new NotePreviewPage(_repository, note))
    {
        Title = note.Title,
        Subtitle = note.Summary,
        Icon = Icons.Note,
        Details = new Details
        {
            Title = note.Title,
            // 詳細窗格也是渲染 Markdown,換行的處理要跟預覽頁一致,
            // 否則同一則筆記在兩個地方長得不一樣。
            Body = note.Body.Length > 0 ? NotePreview.PreserveLineBreaks(note.Body) : "_(沒有內文)_",
        },
        MoreCommands = [
            new CommandContextItem(new NoteEditPage(_repository, note))
            {
                Title = "編輯",
                Icon = Icons.Edit,
                RequestedShortcut = KeyChordHelpers.FromModifiers(
                    ctrl: true, alt: false, shift: false, win: false, vkey: VirtualKey.E, scanCode: 0),
            },
            new CommandContextItem(new OpenUrlCommand(note.FilePath))
            {
                Title = "在預設編輯器開啟",
                Icon = Icons.OpenExternal,
            },
        ],
    };

    private void OnRepositoryChanged(object? sender, EventArgs e)
    {
        // Version 已經讓下一次 GetItems 自己重建了;這裡的重點是主動通知 CmdPal
        // 立刻來拿新的清單,讓正開著的頁面即時更新。
        RaiseItemsChanged();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _repository.Changed -= OnRepositoryChanged;
    }
}
