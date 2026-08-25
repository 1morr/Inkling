using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Inkling;

/// <summary>
/// 系統的「選擇資料夾」對話框(shell 的 <c>IFileDialog</c> 加上 <c>FOS_PICKFOLDERS</c>)。
///
/// 為什麼要自己 P/Invoke:擴展是個**沒有視窗**的 out-of-process COM server,
/// 手上沒有 WinUI / WinForms 那一套現成的對話框可用;WinRT 的 <c>FolderPicker</c> 也不行 ——
/// 它要 <c>IInitializeWithWindow</c> 給一個屬於自己的 HWND 才初始化得起來。
/// <c>IFileDialog</c> 是普通的 COM 呼叫，跟 <see cref="RecycleBinFileDeleter"/> 同一層，
/// 沒有這些前提。
///
/// 用 source-generated COM(<c>[GeneratedComInterface]</c>)而不是舊的 <c>[ComImport]</c>:
/// 舊的那套靠執行期產生封送程式碼，trimming / AOT 分析器會直接報 IL2050 —— 這個 repo
/// 是 warnings-as-errors，而且 Release 是 trimmed 的。
/// </summary>
internal static partial class FolderPicker
{
    /// <summary>選資料夾，不是選檔案。</summary>
    private const uint FosPickFolders = 0x0020;

    /// <summary>只接受真的有檔案系統路徑的項目 ——「這台電腦」那種虛擬節點選了也拿不到路徑。</summary>
    private const uint FosForceFileSystem = 0x0040;

    private const uint FosPathMustExist = 0x0800;

    /// <summary>要 <c>IShellItem</c> 給出 <c>C:\…</c> 這種真路徑，而不是顯示用的名字。</summary>
    private const uint SigdnFileSysPath = 0x80058000;

    /// <summary>使用者按了取消。這是正常結果，不是故障。</summary>
    private const int ErrorCancelled = unchecked((int)0x800704C7);

    private const uint ClsctxInprocServer = 0x1;

    /// <summary>對話框最多等幾毫秒才放棄把它拉到前景 —— 見 <see cref="PullToFrontWhenItAppears"/>。</summary>
    private const int PullToFrontTimeoutMs = 5000;

    private static readonly Guid ClsidFileOpenDialog = new("dc1c5a9c-e88a-4dde-a5a1-60f82a20aef7");
    private static readonly Guid IidFileDialog = new("42f85136-db7e-439c-85f1-e4075d135fc8");
    private static readonly Guid IidShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    /// <summary>
    /// 把 COM 指標包成 .NET 物件的那一套。
    ///
    /// 用 <c>StrategyBasedComWrappers</c> 是因為它認得 <c>[GeneratedComInterface]</c> 產生的
    /// 型別資訊 —— 包出來的物件實作 <c>IDynamicInterfaceCastable</c>，轉型成
    /// <see cref="IFileDialog"/> 時才有對應的封送程式碼可用。
    /// </summary>
    private static readonly StrategyBasedComWrappers ComWrappers = new();

    /// <summary>同時只開一個對話框:表單上的按鈕連按兩下不該疊出兩個。</summary>
    private static int _open;

    /// <summary>
    /// 開對話框，選好之後用 <paramref name="picked"/> 把路徑送回來(**在對話框自己的執行緒上**呼叫)。
    /// 使用者按取消就完全不回呼。
    ///
    /// 這個方法立刻返回:<c>IFileDialog.Show</c> 會一路擋到使用者關掉對話框，而呼叫端是
    /// CmdPal 跨進程呼叫進來的表單送出(<c>ContentFormViewModel.HandleSubmit</c> 裡的
    /// <c>Task.Run</c>)—— 那條執行緒是 CmdPal 的，不該讓它在我們的對話框上等好幾分鐘。
    /// 對話框又規定要跑在 STA 上，所以這裡自己開一條。
    /// </summary>
    /// <param name="failed">
    /// 對話框**開不起來**時叫(也在對話框那條執行緒上):CoCreateInstance 失敗、
    /// Show 回傳錯誤、或執行緒上拋了例外。不給這條路的話，那些失敗只有 DiagnosticLog
    /// 留一行字，而它預設是關的 —— 使用者按「瀏覽…」的體驗就是「什麼都沒發生」,
    /// 跟「絕對不能無聲失敗」的原則打架。取消不算失敗，不會走到這裡。
    /// </param>
    /// <returns>已經有對話框開著時回傳 false。</returns>
    public static bool TryShow(string title, string? initialDirectory, Action<string> picked, Action? failed = null)
    {
        if (Interlocked.CompareExchange(ref _open, 1, 0) != 0)
        {
            DiagnosticLog.Write("FolderPicker: a dialog is already open, ignoring this request");
            return false;
        }

        var thread = new Thread(() =>
        {
            try
            {
                var path = Pick(title, initialDirectory, failed);

                if (path is not null)
                {
                    picked(path);
                }
            }
            catch (Exception ex)
            {
                // 選資料夾失敗不該把整個擴展帶走，但也不能無聲無息。
                DiagnosticLog.Failure($"FolderPicker failed ({ex.GetType().Name})", ex.ToString());
                failed?.Invoke();
            }
            finally
            {
                Volatile.Write(ref _open, 0);
            }
        })
        {
            // 對話框還開著時 CmdPal 若要收掉擴展，不該被我們卡住。
            IsBackground = true,
            Name = "Inkling folder picker",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return true;
    }

    private static string? Pick(string title, string? initialDirectory, Action? failed)
    {
        var hr = CoCreateInstance(in ClsidFileOpenDialog, IntPtr.Zero, ClsctxInprocServer, in IidFileDialog, out var native);

        if (hr < 0 || native == IntPtr.Zero)
        {
            DiagnosticLog.Failure($"FolderPicker: CoCreateInstance failed 0x{hr:X}");
            failed?.Invoke();
            return null;
        }

        IFileDialog dialog;

        try
        {
            // GetOrCreateObjectForComInstance 會自己 AddRef，所以 CoCreateInstance 那一份要還回去。
            dialog = (IFileDialog)ComWrappers.GetOrCreateObjectForComInstance(native, CreateObjectFlags.None);
        }
        finally
        {
            _ = Marshal.Release(native);
        }

        try
        {
            dialog.GetOptions(out var options);
            dialog.SetOptions(options | FosPickFolders | FosForceFileSystem | FosPathMustExist);
            dialog.SetTitle(title);
            SetInitialFolder(dialog, initialDirectory);

            PullToFrontWhenItAppears();

            // owner 刻意傳 0(無主視窗)，不傳 CmdPal 的 HWND。
            // CmdPal 主視窗一失焦就會把自己藏起來(MainWindow 的 Deactivated → HideWindow,
            // 沒有開關可以關掉)，而 IFileDialog 會 EnableWindow(owner, FALSE) 做 modal ——
            // 把一個馬上要消失的視窗設成 owner，對話框的下場只能靠運氣。
            //
            // 代價是這個對話框會拿到自己的工作列按鈕(圖示是套件的 Square44x44Logo),
            // 但那是**刻意留著的退路**:萬一 PullToFrontWhenItAppears 沒把它拉上來，
            // 使用者至少還點得到它。
            var shown = dialog.Show(IntPtr.Zero);

            if (shown == ErrorCancelled)
            {
                DiagnosticLog.Write("FolderPicker: cancelled by the user");
                return null;
            }

            if (shown < 0)
            {
                DiagnosticLog.Failure($"FolderPicker: Show failed 0x{shown:X}");
                failed?.Invoke();
                return null;
            }

            dialog.GetResult(out var item);

            return FileSystemPath(item);
        }
        finally
        {
            // COM 物件不會等 GC 才放掉 —— 這個進程活得跟 CmdPal 一樣久。
            (dialog as IDisposable)?.Dispose();
        }
    }

    /// <summary>對話框一開就停在使用者目前設定的資料夾，而不是上次別的程式用過的位置。</summary>
    private static void SetInitialFolder(IFileDialog dialog, string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        if (SHCreateItemFromParsingName(directory, IntPtr.Zero, in IidShellItem, out var item) < 0 || item == IntPtr.Zero)
        {
            return;
        }

        try
        {
            // SetFolder 而不是 SetDefaultFolder:後者只在「使用者沒動過」時才生效。
            dialog.SetFolder(item);
        }
        finally
        {
            _ = Marshal.Release(item);
        }
    }

    private static string? FileSystemPath(IShellItem item)
    {
        try
        {
            item.GetDisplayName(SigdnFileSysPath, out var buffer);

            try
            {
                return Marshal.PtrToStringUni(buffer);
            }
            finally
            {
                // GetDisplayName 給的字串是 shell 用 CoTaskMemAlloc 配的，要由我們釋放。
                Marshal.FreeCoTaskMem(buffer);
            }
        }
        finally
        {
            (item as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// 把對話框拉到前面來。
    ///
    /// 為什麼需要這一段:Windows 只讓「前景進程」自己開的視窗搶焦點，而我們這個 COM server
    /// 從頭到尾沒收過使用者的輸入 —— 前景是 CmdPal。不管的話，對話框會開在 CmdPal 後面，
    /// 使用者只看到工作列閃一下，以為按鈕沒反應。
    ///
    /// 對話框的 HWND 只有它自己知道，所以這裡用輪詢的:這個進程平常一個可見的頂層視窗都沒有，
    /// 所以「屬於我們、而且看得見」的那個就是它。
    ///
    /// (更正規的做法是實作 <c>IFileDialogEvents</c> 等 <c>OnFolderChange</c> 再去問
    ///  <c>IOleWindow</c> 要 HWND。多兩個 COM 介面，換到的是同一個 HWND。)
    /// </summary>
    private static void PullToFrontWhenItAppears()
    {
        var thread = new Thread(() =>
        {
            for (var waited = 0; waited < PullToFrontTimeoutMs; waited += 50)
            {
                var dialog = FindOwnVisibleWindow();

                if (dialog != IntPtr.Zero)
                {
                    PullToFront(dialog);
                    return;
                }

                Thread.Sleep(50);
            }

            DiagnosticLog.Write("FolderPicker: no dialog window found, giving up on foregrounding");
        })
        {
            IsBackground = true,
            Name = "Inkling folder picker foreground",
        };

        thread.Start();
    }

    private static IntPtr FindOwnVisibleWindow()
    {
        var self = (uint)Environment.ProcessId;
        var window = IntPtr.Zero;

        // parent 傳 0 時 FindWindowEx 走的是頂層視窗清單，等於一次 EnumWindows,
        // 但不必為了回呼委派去開 unsafe 的函式指標。
        while ((window = FindWindowEx(IntPtr.Zero, window, null, null)) != IntPtr.Zero)
        {
            if (IsWindowVisible(window) == 0)
            {
                continue;
            }

            _ = GetWindowThreadProcessId(window, out var owner);

            if (owner == self)
            {
                return window;
            }
        }

        return IntPtr.Zero;
    }

    private static void PullToFront(IntPtr window)
    {
        if (GetForegroundWindow() == window)
        {
            return;
        }

        _ = SetForegroundWindow(window);

        if (GetForegroundWindow() == window)
        {
            return;
        }

        // 前景權限拿不到(SetForegroundWindow 對非前景進程通常就是這個下場)。
        // 剩下的手段只能盡力:先擺到 z-order 最上面，再借 Alt+Tab 那條路切過去。
        // SwitchToThisWindow 官方註明「不是給一般用途用的」，所以擺在最後，失敗也就算了 ——
        // 使用者還是能從工作列點開那個閃爍的對話框。
        _ = BringWindowToTop(window);
        SwitchToThisWindow(window, true);

        DiagnosticLog.Write("FolderPicker: SetForegroundWindow did not take, falling back to BringWindowToTop / SwitchToThisWindow");
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(
        in Guid rclsid, IntPtr outer, uint context, in Guid riid, out IntPtr instance);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHCreateItemFromParsingName(
        string path, IntPtr bindContext, in Guid riid, out IntPtr item);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [LibraryImport("user32.dll")]
    private static partial int IsWindowVisible(IntPtr window);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial int SetForegroundWindow(IntPtr window);

    [LibraryImport("user32.dll")]
    private static partial int BringWindowToTop(IntPtr window);

    [LibraryImport("user32.dll")]
    private static partial void SwitchToThisWindow(IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool altTab);
}

/// <summary>
/// <c>IModalWindow</c>。<c>IFileDialog</c> 的基底，vtable 的第一格就是 <c>Show</c>。
/// </summary>
[GeneratedComInterface]
[Guid("b4db1657-70d7-485e-8e3e-6fcb5a5c1802")]
internal partial interface IModalWindow
{
    /// <summary>
    /// 取消也是走 HRESULT 回來的(<c>HRESULT_FROM_WIN32(ERROR_CANCELLED)</c>),
    /// 所以這一個要 <c>PreserveSig</c> —— 讓產生器丟例外的話，按取消就變成擲例外。
    /// </summary>
    [PreserveSig]
    int Show(IntPtr owner);
}

/// <summary>
/// <c>IFileDialog</c>。
///
/// **方法的宣告順序就是 vtable 的順序**，順序錯了會呼叫到別的函式(而且不會有任何編譯錯誤)。
/// 這裡照 Windows SDK <c>ShObjIdl_core.h</c> 的 <c>IFileDialogVtbl</c> 一格一格排，
/// 用不到的也得留著佔位。
/// </summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("42f85136-db7e-439c-85f1-e4075d135fc8")]
internal partial interface IFileDialog : IModalWindow
{
    void SetFileTypes(uint count, IntPtr filterSpec);

    void SetFileTypeIndex(uint fileType);

    void GetFileTypeIndex(out uint fileType);

    void Advise(IntPtr events, out uint cookie);

    void Unadvise(uint cookie);

    void SetOptions(uint options);

    void GetOptions(out uint options);

    void SetDefaultFolder(IntPtr folder);

    void SetFolder(IntPtr folder);

    void GetFolder(out IntPtr folder);

    void GetCurrentSelection(out IntPtr item);

    void SetFileName(string name);

    void GetFileName(out IntPtr name);

    void SetTitle(string title);

    void SetOkButtonLabel(string text);

    void SetFileNameLabel(string label);

    void GetResult(out IShellItem item);

    void AddPlace(IntPtr item, int place);

    void SetDefaultExtension(string extension);

    void Close(int result);

    void SetClientGuid(in Guid client);

    void ClearClientData();

    void SetFilter(IntPtr filter);
}

/// <summary>
/// <c>IShellItem</c>。同樣照 SDK 標頭的 vtable 順序排，我們只用得到 <c>GetDisplayName</c>。
/// </summary>
[GeneratedComInterface]
[Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
internal partial interface IShellItem
{
    void BindToHandler(IntPtr bindContext, in Guid handler, in Guid riid, out IntPtr result);

    void GetParent(out IShellItem parent);

    /// <param name="name">shell 用 CoTaskMemAlloc 配的字串，呼叫端負責釋放。</param>
    void GetDisplayName(uint kind, out IntPtr name);

    void GetAttributes(uint mask, out uint attributes);

    void Compare(IntPtr other, uint hint, out int order);
}
