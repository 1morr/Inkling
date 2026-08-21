using System.Collections.Concurrent;
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

    /// <summary>
    /// 自己剛寫過的檔案,在這段時間內收到的 watcher 事件當成自己的回音忽略掉。
    /// 抓得比實際延遲寬鬆(實測是幾毫秒),但要明顯短於使用者可能在外部編輯器
    /// 改完同一個檔案再存回來的時間。
    /// </summary>
    private const int SelfWriteEchoWindowMs = 500;

    private readonly string _directory;
    private readonly TimeProvider _timeProvider;
    private readonly IFileDeleter _fileDeleter;
    private readonly Func<DateTimeOffset, string> _idGenerator;
    private readonly Lock _gate = new();
    private readonly System.Threading.Timer _changeDebounce;

    /// <summary>
    /// Inkling 自己剛寫過(或剛刪掉)的路徑 → 忽略到什麼時候(<see cref="Environment.TickCount64"/>)。
    ///
    /// 自己的寫入在當下就 <see cref="Invalidate"/> 過了 —— 丟快取、進版本、發 Changed 一次做完。
    /// 但 watcher 幾毫秒後會為同一個檔案再發一次事件,於是同一次存檔讓正開著的頁面
    /// 重建兩遍,第二遍還晚 250 ms(去抖動)才到,畫面會多閃一下。
    ///
    /// **按路徑記,不是按時間段全域關掉** —— 同一段時間裡別的檔案被外部工具改了照樣要收到。
    /// 同一個檔案在這 500 ms 內真的被外部改動則會漏掉一次通知,那是刻意的取捨:
    /// 事件本身分不出是誰寫的,而快取已經丟掉了,下一次 GetAll 讀到的仍是磁碟上的最新內容。
    ///
    /// 用 ConcurrentDictionary 而不是 _gate:這個字典會在 watcher 執行緒上讀,
    /// 而 _gate 在整個資料夾掃描期間都被持有 —— 讓 watcher 的回呼卡在那個鎖上,
    /// 它的事件緩衝區會溢位(那正是 OnWatcherError 要處理的災難)。
    /// </summary>
    private readonly ConcurrentDictionary<string, long> _selfWrites = new(StringComparer.OrdinalIgnoreCase);

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
                if (_disposed)
                {
                    return;
                }

                try
                {
                    Changed?.Invoke(this, EventArgs.Empty);
                }
                catch (Exception)
                {
                    // **執行緒池上沒接住的例外會直接終止整個擴展進程。** 同一條規則這個檔案
                    // 自己在 OnFileSystemChanged 的 ObjectDisposedException 上寫過,
                    // 但只套用在那一條路。訂閱者是 UI 層的頁面,它們會呼叫 RaiseItemsChanged ——
                    // 那是跨 COM 邊界的呼叫,CmdPal 那頭走掉之後 proxy 就死了。
                    // 使用者看到的會是「Inkling 突然整個不見了」。
                    //
                    // 這裡不記 log:Core 不引用 UI 層的 DiagnosticLog(架構界線)。
                    // 要留痕跡的話該由訂閱端自己包一層,那一層才有 log 可用。
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

    /// <inheritdoc />
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

            // 正規化要在**回傳的物件上**做,不能只在寫檔那一頭做:Serialize 會 ToLf,
            // 所以磁碟上永遠是 LF,再讀回來也是 LF。這裡不做的話,剛存好那一則
            // 在記憶體裡帶著呼叫端給的 CRLF(Adaptive Cards 甚至是裸 CR),
            // 跟從磁碟讀回來的同一則不相等 —— 預覽頁比對「內文是否已含標題」、
            // 快取比對這類地方就會得到莫名其妙的結果。
            Body = Newlines.ToLf(body),
            Created = now,
            Updated = now,
            FilePath = NoteFileName.CreateUniquePath(_directory, now, title),
        };

        NoteSelfWrite(note.FilePath);
        AtomicFile.Write(note.FilePath, NoteFile.Serialize(note));
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

            // 與 Create 同一個理由:回傳的物件要跟磁碟上的那一份對得起來。
            Body = Newlines.ToLf(body),
            Updated = _timeProvider.GetLocalNow(),
        };

        NoteSelfWrite(updated.FilePath);
        AtomicFile.Write(updated.FilePath, NoteFile.Serialize(updated));
        Invalidate();

        return updated;
    }

    public void Delete(string id)
    {
        var existing = GetById(id)
            ?? throw NoteNotFoundException.ForId(id);

        NoteSelfWrite(existing.FilePath);
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
                NoteSelfWrite(note.FilePath);
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
                // 隨手草稿的檔案不是筆記(沒有標題也沒有 id),列進來只會讓清單永遠多一列
                // 標題在跳動的半成品。**刻意不計入 SkippedFileCount** —— 那個數字講的是
                // 「有幾個檔案壞到讀不出來」,而這一個是我們自己決定不列的。
                if (ScratchpadStore.IsScratchpad(_directory, path))
                {
                    continue;
                }

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

        // **Updated 只到秒。** 快速記下連打兩則、或別台機器一次同步下來一批,
        // 時間戳就是相等的 —— 而 List<T>.Sort 是不穩定排序,相等元素的先後由實作決定,
        // 同一個資料夾在不同機器上可能排出不同順序。用 id 當第二鍵讓順序完全確定
        // (id 本身也帶著時間與亂數後綴,所以次序仍然合理)。
        return [.. notes.OrderByDescending(n => n.Updated).ThenBy(n => n.Id, StringComparer.Ordinal)];
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
                // 最後一輪不睡。這段迴圈跑在持有 _gate 的掃描裡,那一次白睡是所有
                // 等著拿清單的呼叫端一起付的 —— 而睡完只會直接 return null。
                if (attempt < ReadRetries - 1)
                {
                    Thread.Sleep(ReadRetryDelayMs);
                }
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
    /// 監看資料夾,任何變動就讓快取失效。
    ///
    /// 用「延遲失效」而不是「立即重載」有個好處:OneDrive 同步下來時是一陣爆發式的寫入,
    /// 立即重載會被打成篩子,而失效旗標天然就把它們併成一次。
    /// </summary>
    private void EnsureWatcher()
    {
        // **Dispose 之後不准再掛。** 換筆記資料夾時 provider 會釋放舊的 repository,
        // 但舊頁面可能還活著(CmdPal 手上那個實例不會因為我們重建就換掉)。
        // 它一呼叫 GetAll,這裡就會生出一個新的 FileSystemWatcher,而已經沒有人
        // 會再去 Dispose 它了 —— 換幾次資料夾就漏幾個,每個都還盯著舊目錄發事件。
        if (_disposed || _watcher is not null || !Directory.Exists(_directory))
        {
            return;
        }

        try
        {
            // **刻意不設 Filter,副檔名改在 OnFileSystemChanged 自己判。**
            // 以前是 new FileSystemWatcher(_directory, "*.md"),而那個過濾器**連資料夾事件
            // 一起濾掉了** —— 也就是下面 NotifyFilters.DirectoryName 設了等於沒設。
            // 實測(同一組組態、獨立重現):在檔案總管把裝著筆記的子資料夾改名
            // (Directory.Move)**一個事件都沒有**,清單因此不會更新;拿掉 Filter 之後
            // 收得到 Renamed。代價是事件量變大(資料夾裡任何檔案都會發),
            // 但 handler 只是設一個失效旗標 + 去抖動,而且 AffectsNotes 會先擋掉。
            var watcher = new FileSystemWatcher(_directory)
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

    /// <summary>
    /// 這個事件值不值得讓快取失效。watcher 收所有檔案(見 <see cref="EnsureWatcher"/>),
    /// 過濾在這裡做。
    /// </summary>
    private static bool AffectsNotes(FileSystemEventArgs e)
    {
        if (IsNoteFile(e.FullPath))
        {
            return true;
        }

        // 改名要看兩邊:把 note.md 改成 note.txt 之後新路徑不是筆記,
        // 但那一則確實從清單裡消失了。
        if (e is RenamedEventArgs renamed && IsNoteFile(renamed.OldFullPath))
        {
            return true;
        }

        // 沒有副檔名的幾乎一定是資料夾。**只認 Created / Deleted / Renamed,不認 Changed:**
        //
        //  - 要的是資料夾被改名或刪掉 —— 那不會替裡面每個 .md 各發一次事件
        //    (實測 Directory.Move 在舊的 Filter="*.md" 之下一個事件都沒有,那就是這條的成因)。
        //  - 不要的是資料夾的 Changed:在子資料夾裡動一個檔案會順帶讓那個資料夾的
        //    LastWrite 變動,而那個事件的路徑是**資料夾**、不是我們剛寫的檔案 ——
        //    它會繞過自寫回音的抑制(見 <see cref="_selfWrites"/>),讓一次存檔又變成
        //    重掃兩次。裡面的檔案事件本來就會通知,資料夾那一則沒有帶來新資訊。
        //
        // 副作用是名字裡帶點的資料夾(my.notes)會被當成檔案濾掉。代價只是少一次自動更新,
        // 跟修正前一樣;反過來把有副檔名的東西全放行,等於整個過濾形同虛設
        // (原子寫入的 .md.tmp 每次都會多打一次事件)。
        return e.ChangeType != WatcherChangeTypes.Changed
            && Path.GetExtension(e.FullPath.AsSpan()).IsEmpty;
    }

    private static bool IsNoteFile(string path) =>
        Path.GetExtension(path.AsSpan()).Equals(NoteFileName.Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 記下「這個路徑等一下會發事件,那是我們自己造成的」。
    /// **要在動手寫之前叫** —— 事件有可能在 File.Move 還沒返回時就送到 watcher 執行緒。
    /// </summary>
    private void NoteSelfWrite(string path) =>
        _selfWrites[path] = Environment.TickCount64 + SelfWriteEchoWindowMs;

    private bool IsSelfWriteEcho(string path)
    {
        var now = Environment.TickCount64;

        // 一次寫入常常發不只一個事件(Created 之後還有 Changed),所以命中之後
        // **不移除**,讓它自己過期 —— 移除的話第二個事件照樣會穿過去。
        // 過期的順手清掉:這個字典只在寫入路徑上長,但不該無限長。
        foreach (var entry in _selfWrites)
        {
            if (entry.Value <= now)
            {
                _selfWrites.TryRemove(entry.Key, out _);
            }
        }

        return _selfWrites.TryGetValue(path, out var until) && until > now;
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        // 隨手草稿的檔案不在清單裡,它的寫入不該讓每一個開著的清單頁白重掃一遍 ——
        // 隨手草稿每按一次儲存就寫一次檔,而一次重掃要讀完整個資料夾。
        // 忽略它不會漏掉什麼:隨手草稿頁面每次 GetContent() 都自己重讀檔案,不靠這條路,
        // 所以連「使用者用外部編輯器改了草稿」也照樣看得到。
        //
        // e 要先擋 null —— OnWatcherError 是拿 null! 呼叫進來的。
        if (e is not null && ScratchpadStore.IsScratchpad(_directory, e.FullPath))
        {
            return;
        }

        // 自己剛寫的那個檔案的回音。理由與取捨見 _selfWrites。
        if (e is not null && IsSelfWriteEcho(e.FullPath))
        {
            return;
        }

        // watcher 沒有設 Filter(理由見 EnsureWatcher),副檔名在這裡判。
        if (e is not null && !AffectsNotes(e))
        {
            return;
        }

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
