using Inkling.Core;

namespace Inkling.Tests;

/// <summary>
/// 記憶體版的筆記倉庫。這一層的測試在乎的是「頁面把結果翻譯成什麼」,
/// 不是磁碟 —— 真正的檔案行為在 Inkling.Core.Tests 那一邊已經測透了。
/// </summary>
internal sealed class FakeNoteRepository : INoteRepository
{
    private readonly List<Note> _notes = [];
    private int _version;

    public event EventHandler? Changed;

    public int Version => _version;

    public int SkippedFileCount { get; set; }

    public IReadOnlyList<Note> GetAll() => _notes;

    public Note? GetByPath(string filePath) =>
        _notes.FirstOrDefault(n => string.Equals(n.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

    public Note Create(string title, string body)
    {
        var note = Add(title, body);
        Bump();
        return note;
    }

    public Note Update(Note note, string title, string body)
    {
        var index = _notes.FindIndex(n => n.FilePath == note.FilePath);
        if (index < 0)
        {
            throw NoteNotFoundException.ForPath(note.FilePath);
        }

        _notes[index] = _notes[index] with { Title = title, Body = body };
        Bump();
        return _notes[index];
    }

    public void Delete(Note note)
    {
        if (_notes.RemoveAll(n => n.FilePath == note.FilePath) == 0)
        {
            throw NoteNotFoundException.ForPath(note.FilePath);
        }

        Bump();
    }

    public int DeleteMany(IEnumerable<Note> notes)
    {
        var removed = notes.Count(n => _notes.RemoveAll(x => x.FilePath == n.FilePath) > 0);
        if (removed > 0)
        {
            Bump();
        }

        return removed;
    }

    /// <summary>不進版本號的直接塞入，給「先鋪好資料再建頁面」的測試用。</summary>
    public Note Add(string title, string body = "")
    {
        var id = $"id-{_notes.Count + 1}";
        var note = new Note
        {
            Id = id,
            Title = title,
            Body = body,
            Created = DateTimeOffset.UnixEpoch.AddMinutes(_notes.Count),
            Updated = DateTimeOffset.UnixEpoch.AddMinutes(_notes.Count),
            FilePath = $@"C:\notes\{id}.md",
        };

        _notes.Add(note);
        return note;
    }

    /// <summary>模擬外部異動:版本前進並發事件，頁面的快取應該因此失效。</summary>
    public void Bump()
    {
        _version++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public int ChangedSubscriberCount => Changed?.GetInvocationList().Length ?? 0;
}

internal sealed class FakeSettings : ICaptureSeparatorStore, ICapturePreviewStore, ISourceModeStore
{
    private string _separator = ";;";
    private bool _preview;
    private bool _showSource;

    public event EventHandler? CaptureSeparatorChanged;

    public event EventHandler? CapturePreviewChanged;

    public event EventHandler? ShowSourceChanged;

    public string CaptureSeparator
    {
        get => _separator;
        set
        {
            _separator = value;
            CaptureSeparatorChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool ShowCapturePreview
    {
        get => _preview;
        set
        {
            _preview = value;
            CapturePreviewChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool ShowSource
    {
        get => _showSource;
        set
        {
            _showSource = value;
            ShowSourceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public int SeparatorSubscriberCount => CaptureSeparatorChanged?.GetInvocationList().Length ?? 0;

    public int PreviewSubscriberCount => CapturePreviewChanged?.GetInvocationList().Length ?? 0;

    public int ShowSourceSubscriberCount => ShowSourceChanged?.GetInvocationList().Length ?? 0;
}
