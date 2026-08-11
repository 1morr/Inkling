using System.Globalization;

namespace Notelet.Core;

/// <summary>
/// 把筆記存成資料夾裡的 Markdown 檔。同步完全交給雲端硬碟客戶端(OneDrive)處理,
/// 這一層對「同步」一無所知,只管檔案。
/// </summary>
public sealed class FileSystemNoteRepository : INoteRepository, IDisposable
{
    private const int ReadRetries = 3;
    private const int ReadRetryDelayMs = 20;

    /// <summary>
    /// OneDrive 同步下來時是一陣爆發式的寫入,一個檔案可能連續觸發好幾個事件。
    /// 通知 UI 前先靜置這麼久,把整批併成一次。
    /// </summary>
    private const int ChangeDebounceMs = 250;

    private readonly string _directory;
    private readonly TimeProvider _timeProvider;
    private readonly IFileDeleter _fileDeleter;
    private readonly Lock _gate = new();
    private readonly System.Threading.Timer _changeDebounce;

    private List<Note>? _cache;
    private FileSystemWatcher? _watcher;
    private bool _disposed;
    private int _version;

    public FileSystemNoteRepository(
        NoteletOptions options,
        TimeProvider? timeProvider = null,
        IFileDeleter? fileDeleter = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _directory = options.NotesDirectory;
        _timeProvider = timeProvider ?? TimeProvider.System;

        // 預設是直接刪。UI 層會換成送資源回收筒的那一個 —— 見 IFileDeleter 上的說明。
        _fileDeleter = fileDeleter ?? new PermanentFileDeleter();

        // 先建起來但不啟動;每次檔案異動就往後推遲觸發時間。
        _changeDebounce = new System.Threading.Timer(
            _ => Changed?.Invoke(this, EventArgs.Empty),
            null,
            Timeout.Infinite,
            Timeout.Infinite);
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public int Version => Volatile.Read(ref _version);

    /// <summary>
    /// 這次掃描中因為讀不到而略過的檔案數。給手動驗證與日後的狀態訊息用,
    /// 免得檔案默默消失卻查不出原因。
    /// </summary>
    public int SkippedFileCount { get; private set; }

    public IReadOnlyList<Note> GetAll()
    {
        lock (_gate)
        {
            if (_cache is not null)
            {
                return _cache;
            }

            var loaded = Load();
            EnsureWatcher();

            // 資料夾還不存在時不要快取空結果:第一次用 Notelet 的時候它本來就不存在,
            // 而 watcher 也還掛不上去,沒有任何東西會來通知我們它出現了。
            // 快取住的話,資料夾之後被建出來(第一次記筆記、或別台機器同步下來)
            // 就再也讀不到內容。每次多一個 Directory.Exists 的成本可以忽略。
            if (!Directory.Exists(_directory))
            {
                return loaded;
            }

            _cache = loaded;
            return _cache;
        }
    }

    public Note? GetById(string id) =>
        GetAll().FirstOrDefault(n => string.Equals(n.Id, id, StringComparison.Ordinal));

    public Note Create(string title, string body)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(body);

        var now = _timeProvider.GetLocalNow();
        Directory.CreateDirectory(_directory);

        var note = new Note
        {
            Id = NoteFileName.CreateId(now),
            Title = title.Trim(),
            Body = body,
            Created = now,
            Updated = now,
            FilePath = NoteFileName.CreateUniquePath(_directory, now, title),
        };

        WriteAtomic(note.FilePath, NoteFile.Serialize(note));
        Invalidate();

        return note;
    }

    public Note Update(string id, string title, string body)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(body);

        var existing = GetById(id)
            ?? throw NoteNotFoundException.ForId(id);

        // 改標題不重新命名檔案 —— 身分是 id,而在同步資料夾裡 rename 會製造衝突檔。
        var updated = existing with
        {
            Title = title.Trim(),
            Body = body,
            Updated = _timeProvider.GetLocalNow(),
        };

        WriteAtomic(updated.FilePath, NoteFile.Serialize(updated));
        Invalidate();

        return updated;
    }

    public void Delete(string id)
    {
        var existing = GetById(id)
            ?? throw NoteNotFoundException.ForId(id);

        _fileDeleter.Delete(existing.FilePath);
        Invalidate();
    }

    public int DeleteAll()
    {
        var notes = GetAll();
        var deleted = 0;

        foreach (var note in notes)
        {
            try
            {
                _fileDeleter.Delete(note.FilePath);
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 一個檔案刪不掉就放棄整批,只會留下一個「刪到一半」而且說不清楚的狀態。
                // 繼續刪其他的,漏掉幾則由回傳值反映。
            }
        }

        // 一次就好。每刪一則就 Invalidate 的話,清單會在整批刪除的過程中被重建 N 次。
        Invalidate();

        return deleted;
    }

    public void Invalidate()
    {
        // 由 Notelet 自己的寫入(Create / Update)觸發,一次操作就一個事件,
        // 不需要去抖動,而且要立刻通知 —— 使用者剛按下儲存,畫面就該跟上。
        InvalidateCore(notifyImmediately: true);
    }

    private void InvalidateCore(bool notifyImmediately)
    {
        lock (_gate)
        {
            _cache = null;
        }

        Interlocked.Increment(ref _version);

        // 事件在鎖外面發,handler 幾乎一定會回頭呼叫 GetAll()。
        if (notifyImmediately)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private List<Note> Load()
    {
        SkippedFileCount = 0;

        if (!Directory.Exists(_directory))
        {
            return [];
        }

        var notes = new List<Note>();

        // 連子資料夾一起掃:使用者用檔案總管把筆記分門別類是很自然的事,
        // 只掃頂層的話那些筆記會無聲消失,查都查不出來。新筆記一律寫在根目錄。
        foreach (var path in Directory.EnumerateFiles(_directory, "*" + NoteFileName.Extension, SearchOption.AllDirectories))
        {
            var note = TryReadNote(path);
            if (note is not null)
            {
                notes.Add(note);
            }
            else
            {
                SkippedFileCount++;
            }
        }

        notes.Sort((a, b) => b.Updated.CompareTo(a.Updated));
        return notes;
    }

    private Note? TryReadNote(string path)
    {
        var content = TryReadAllText(path);
        if (content is null)
        {
            return null;
        }

        var parsed = NoteFile.Parse(content);

        // 缺的欄位用檔案本身的資訊補齊。這讓「使用者自己丟進來的普通 .md」
        // 也能正常出現在清單裡,而不是被當成壞檔案跳過。
        var created = parsed.Created ?? new DateTimeOffset(File.GetCreationTime(path));
        var updated = parsed.Updated ?? new DateTimeOffset(File.GetLastWriteTime(path));

        return new Note
        {
            Id = parsed.Id ?? DeriveId(path),
            Title = parsed.Title ?? DeriveTitle(parsed.Body, path),
            Body = parsed.Body,
            Created = created,
            Updated = updated,
            Tags = parsed.Tags,
            ExtraFrontMatter = parsed.ExtraFrontMatter,
            FilePath = path,
        };
    }

    /// <summary>
    /// 讀檔並在短暫的 IO 衝突時重試 —— OneDrive 與其他編輯器都可能正好在寫同一個檔。
    /// </summary>
    private static string? TryReadAllText(string path)
    {
        for (var attempt = 0; attempt < ReadRetries; attempt++)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (IOException)
            {
                Thread.Sleep(ReadRetryDelayMs);
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// 給沒有 id 的外來檔案一個穩定身分。用路徑做 FNV-1a,
    /// 不能用 string.GetHashCode —— 那個值每次跑程式都不一樣。
    /// </summary>
    private string DeriveId(string path)
    {
        var relative = Path.GetRelativePath(_directory, path);

        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        var hash = offsetBasis;
        foreach (var ch in relative)
        {
            hash = (hash ^ ch) * prime;
        }

        return "file-" + hash.ToString("x8", CultureInfo.InvariantCulture);
    }

    private static string DeriveTitle(string body, string path)
    {
        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.Trim().TrimStart('#').Trim();
            if (trimmed.Length > 0)
            {
                return trimmed.Length > 120 ? trimmed[..120] : trimmed;
            }
        }

        return Path.GetFileNameWithoutExtension(path);
    }

    /// <summary>
    /// 先寫暫存檔再換上去。直接覆寫的話,寫到一半中斷就等於毀掉一則既有筆記。
    /// 暫存檔用 .tmp 副檔名,不會被 *.md 的掃描撿到。
    /// </summary>
    private static void WriteAtomic(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + ".tmp";
        File.WriteAllText(temp, content);

        if (File.Exists(path))
        {
            File.Move(temp, path, overwrite: true);
        }
        else
        {
            File.Move(temp, path);
        }
    }

    /// <summary>
    /// 監看資料夾,任何變動就讓快取失效。
    ///
    /// 用「延遲失效」而不是「立即重載」有個好處:OneDrive 同步下來時是一陣爆發式的寫入,
    /// 立即重載會被打成篩子,而失效旗標天然就把它們併成一次。
    /// </summary>
    private void EnsureWatcher()
    {
        if (_watcher is not null || !Directory.Exists(_directory))
        {
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(_directory, "*" + NoteFileName.Extension)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName,
                IncludeSubdirectories = true,
            };

            watcher.Changed += OnFileSystemChanged;
            watcher.Created += OnFileSystemChanged;
            watcher.Deleted += OnFileSystemChanged;
            watcher.Renamed += OnFileSystemChanged;

            // 事件緩衝區溢位時前面的事件會遺失,唯一安全的反應就是整個重掃。
            watcher.Error += (_, _) => OnFileSystemChanged(this, null!);

            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // 監看不到就退化成「每次都重掃」,功能不受影響,只是少了自動更新。
            _watcher = null;
        }
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        // 外部異動走去抖動:OneDrive 同步下來時是一陣爆發式的寫入,
        // 每個檔案都立刻通知一次的話,清單頁會在同步期間被重建幾十次。
        InvalidateCore(notifyImmediately: false);

        if (!_disposed)
        {
            // 每來一個事件就把觸發時間往後推,連續寫入結束後才真的通知一次。
            _changeDebounce.Change(ChangeDebounceMs, Timeout.Infinite);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _watcher?.Dispose();
        _watcher = null;
        _changeDebounce.Dispose();
    }
}

public sealed class NoteNotFoundException : Exception
{
    public NoteNotFoundException()
    {
    }

    public NoteNotFoundException(string message)
        : base(message)
    {
    }

    public NoteNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public string? Id { get; private init; }

    public static NoteNotFoundException ForId(string id) =>
        new($"找不到 id 為「{id}」的筆記。") { Id = id };
}
