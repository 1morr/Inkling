using System.Globalization;

namespace Inkling.Core;

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
    private readonly Func<DateTimeOffset, string> _idGenerator;
    private readonly Lock _gate = new();
    private readonly System.Threading.Timer _changeDebounce;

    private List<Note>? _cache;
    private FileSystemWatcher? _watcher;
    private volatile bool _disposed;
    private int _version;

    /// <param name="idGenerator">
    /// 產生筆記 id 的方法,預設是 <see cref="NoteFileName.CreateId"/>。
    /// 是測試用的接縫:碰撞重抽的迴圈要有辦法確定性地製造碰撞才測得到。
    /// </param>
    public FileSystemNoteRepository(
        InklingOptions options,
        TimeProvider? timeProvider = null,
        IFileDeleter? fileDeleter = null,
        Func<DateTimeOffset, string>? idGenerator = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _directory = options.NotesDirectory;
        _timeProvider = timeProvider ?? TimeProvider.System;

        // 預設是直接刪。UI 層會換成送資源回收筒的那一個 —— 見 IFileDeleter 上的說明。
        _fileDeleter = fileDeleter ?? new PermanentFileDeleter();

        _idGenerator = idGenerator ?? NoteFileName.CreateId;

        // 先建起來但不啟動;每次檔案異動就往後推遲觸發時間。
        _changeDebounce = new System.Threading.Timer(
            _ =>
            {
                // 回呼在執行緒池上跑,Dispose 之後仍可能被叫到 —— 那時訂閱者多半已死。
                if (!_disposed)
                {
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            },
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

            // 先掛 watcher 再掃磁碟:反過來的話,掃描完成到 watcher 啟用之間進來的
            // 檔案兩邊都漏掉,之後沒有任何東西會讓快取失效,那個檔案一直隱形。
            // 掃描期間收到事件反而安全 —— 失效旗標會讓快取重來一次。
            EnsureWatcher();
            var loaded = Load();

            if (!Directory.Exists(_directory))
            {
                // watcher 監看的目錄已經消失,它永遠不會再發事件。拆掉,
                // 目錄之後重建(OneDrive 重新佈建、使用者自己建回來)時這裡才掛得上新的。
                DisposeWatcherUnlocked();

                // 資料夾還不存在時不要快取空結果:第一次用 Inkling 的時候它本來就不存在,
                // 而 watcher 也還掛不上去,沒有任何東西會來通知我們它出現了。
                // 快取住的話,資料夾之後被建出來(第一次記筆記、或別台機器同步下來)
                // 就再也讀不到內容。每次多一個 Directory.Exists 的成本可以忽略。
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
            Id = GenerateUniqueId(now),
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

    /// <summary>
    /// id 的後綴只有 16-bit,同一秒內兩次 Create 各有 1/65536 的碰撞率;撞了兩則筆記
    /// 共用一個 id,GetById 只回第一筆,Update / Delete 會作用在錯的那則上。
    /// 撞了就重抽 —— 這也順便擋住「別台機器同一秒同步下來同名 id」的情境。
    /// </summary>
    private string GenerateUniqueId(DateTimeOffset now)
    {
        var id = _idGenerator(now);
        while (GetById(id) is not null)
        {
            id = _idGenerator(now);
        }

        return id;
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

    public int DeleteMany(IEnumerable<Note> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);

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
        // 一則都沒刪掉時連這一次都不必 —— 磁碟上什麼都沒變,發個 Changed 只是叫
        // 正開著的頁面白重建一遍。
        if (deleted > 0)
        {
            Invalidate();
        }

        return deleted;
    }

    /// <summary>
    /// 丟掉快取,下次讀取時重新掃描資料夾,並立刻通知訂閱者。
    ///
    /// 刻意不在 <see cref="INoteRepository"/> 上:介面上的外部變動通知已由
    /// <see cref="INoteRepository.Changed"/> 與 <see cref="INoteRepository.Version"/> 涵蓋,
    /// UI 層沒有任何透過介面呼叫它的需求 —— 留著只會逼每個未來的替代實作
    /// 替它發明一個語意。目前只有這個類自己的寫入路徑與測試在用。
    ///
    /// 由 Inkling 自己的寫入(Create / Update)觸發,一次操作就一個事件,
    /// 不需要去抖動,而且要立刻通知 —— 使用者剛按下儲存,畫面就該跟上。
    /// </summary>
    public void Invalidate()
    {
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

        try
        {
            // 連子資料夾一起掃:使用者用檔案總管把筆記分門別類是很自然的事,
            // 只掃頂層的話那些筆記會無聲消失,查都查不出來。新筆記一律寫在根目錄。
            //
            // IgnoreInaccessible:進不去的子目錄(Documents 底下 deny-read 的 junction、
            // 權限不對的資料夾)靜靜跳過 —— 一個壞子目錄不該讓整份清單列不出來。
            var enumeration = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
            };

            foreach (var path in Directory.EnumerateFiles(_directory, "*" + NoteFileName.Extension, enumeration))
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
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 目錄本身枚舉不了:Exists 之後被刪、根目錄沒有權限、OneDrive 把同步根抽掉。
            // 回傳已掃到的部分,總比讓例外穿出頁面的 GetItems、整頁變成擴展錯誤好。
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

            // front matter 裡沒有 id,就代表這個檔案不是 Inkling 寫的 —— 上面那個 id 是我們推的。
            IsExternal = parsed.Id is null,
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
        var first = NoteBody.FirstContentLine(body);

        return first is not null
            ? (first.Length > NoteBody.MaxLineLength ? first[..NoteBody.MaxLineLength] : first)
            : Path.GetFileNameWithoutExtension(path);
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

            // 事件緩衝區溢位時前面的事件會遺失,而且這個 watcher 的狀態已不可信 ——
            // 拆掉讓下一次 GetAll 重掃並重新掛上,而不是繼續信它。
            watcher.Error += OnWatcherError;

            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // 監看不到就退化成「每次都重掃」,功能不受影響,只是少了自動更新。
            _watcher = null;
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        OnFileSystemChanged(sender, null!);

        FileSystemWatcher? stale;
        lock (_gate)
        {
            stale = _watcher;
            _watcher = null;
        }

        // FileSystemWatcher 從自己的事件回呼裡同步 Dispose 有死結回報,丟給執行緒池收。
        if (stale is not null)
        {
            ThreadPool.QueueUserWorkItem(static w => ((FileSystemWatcher)w!).Dispose(), stale);
        }
    }

    /// <summary>呼叫端必須已持有 <see cref="_gate"/>。</summary>
    private void DisposeWatcherUnlocked()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        // 外部異動走去抖動:OneDrive 同步下來時是一陣爆發式的寫入,
        // 每個檔案都立刻通知一次的話,清單頁會在同步期間被重建幾十次。
        InvalidateCore(notifyImmediately: false);

        if (_disposed)
        {
            return;
        }

        try
        {
            // 每來一個事件就把觸發時間往後推,連續寫入結束後才真的通知一次。
            _changeDebounce.Change(ChangeDebounceMs, Timeout.Infinite);
        }
        catch (ObjectDisposedException)
        {
            // watcher 事件在執行緒池上跑:上面的檢查通過之後、Change 之前,
            // Dispose 可能已經把 Timer 收掉。吞掉 —— 物件正在消失,通知已無意義,
            // 而執行緒池上沒接住的例外會直接終止整個擴展進程。
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        lock (_gate)
        {
            DisposeWatcherUnlocked();
        }

        _changeDebounce.Dispose();
    }
}
