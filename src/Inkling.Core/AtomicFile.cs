namespace Inkling.Core;

/// <summary>
/// 「先寫暫存檔再換上去」的寫檔方式,筆記與隨手草稿共用。
///
/// 本來長在 <see cref="FileSystemNoteRepository"/> 裡,<see cref="ScratchpadStore"/> 出現時
/// 抽出來 —— 兩邊寫的都是使用者的資料,而且都放在同一個(多半有雲端同步的)資料夾裡,
/// 「寫到一半中斷」的後果一模一樣。
/// </summary>
internal static class AtomicFile
{
    /// <summary>
    /// 先寫暫存檔再換上去。直接覆寫的話,寫到一半中斷就等於毀掉使用者既有的內容。
    /// 暫存檔用 .tmp 副檔名,不會被 *.md 的掃描撿到。
    /// </summary>
    public static void Write(string path, string content)
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
}
