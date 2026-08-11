using System.Runtime.InteropServices;
using Notelet.Core;

namespace Notelet;

/// <summary>
/// 把檔案送進 Windows 的資源回收筒,而不是直接抹掉。
///
/// 為什麼值得為這件事寫 P/Invoke:筆記是使用者手打的東西,誤刪一則就沒了。
/// .NET 沒有內建送資源回收筒的 API,而 WinRT 的 <c>StorageFile.DeleteAsync</c>
/// 雖然做得到,卻要走 MSIX 的檔案 broker —— 筆記資料夾在 OneDrive 底下,
/// 是使用者自己指定的任意路徑,那條路的權限跟我們現在用 <c>System.IO</c>
/// 讀寫這些檔案不是同一套,而且它是非同步的。<c>SHFileOperationW</c> 是同步的普通
/// Win32 呼叫,跟現有的檔案存取同一層,沒有這些變數。
/// </summary>
internal sealed partial class RecycleBinFileDeleter : IFileDeleter
{
    private const uint FO_DELETE = 0x0003;

    /// <summary>不要顯示進度框。</summary>
    private const ushort FOF_SILENT = 0x0004;

    /// <summary>不要問「確定刪除嗎」—— 我們自己已經問過一次了。</summary>
    private const ushort FOF_NOCONFIRMATION = 0x0010;

    /// <summary>這一個就是「送資源回收筒」的意思,少了它就是永久刪除。</summary>
    private const ushort FOF_ALLOWUNDO = 0x0040;

    /// <summary>錯誤不要跳系統對話框,交給呼叫端處理。</summary>
    private const ushort FOF_NOERRORUI = 0x0400;

    public void Delete(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        // pFrom 是一串以 \0 分隔、再以 \0 收尾的路徑清單(不是普通字串)。
        // 少了那第二個結尾,shell 會讀過頭到相鄰的記憶體去。
        var from = Marshal.StringToHGlobalUni(path + "\0\0");

        try
        {
            var operation = new ShFileOpStruct
            {
                Func = FO_DELETE,
                From = from,
                Flags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI,
            };

            var result = SHFileOperation(ref operation);

            if (result != 0)
            {
                // 這個回傳碼不是 Win32 error code(shell 有自己一套),所以不能丟
                // Win32Exception 讓它去查訊息 —— 那樣只會得到一句對不上的錯誤。
                throw new IOException($"送資源回收筒失敗(SHFileOperation 回傳 0x{result:X})。");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(from);
        }
    }

    /// <summary>
    /// 刻意全部用 blittable 型別(字串欄位是 <see cref="IntPtr"/>,自己 marshal):
    /// <c>LibraryImport</c> 的來源產生器不吃含有 <c>string</c> 欄位的結構,
    /// 而換回舊的 <c>DllImport</c> 會踩到 SYSLIB1054,這個 repo 是 warnings-as-errors。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ShFileOpStruct
    {
        public IntPtr Hwnd;
        public uint Func;
        public IntPtr From;
        public IntPtr To;
        public ushort Flags;
        public int AnyOperationsAborted;
        public IntPtr NameMappings;
        public IntPtr ProgressTitle;
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHFileOperationW")]
    private static partial int SHFileOperation(ref ShFileOpStruct fileOp);
}
