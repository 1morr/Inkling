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

        try
        {
            // overwrite: true 對「目標還不存在」也是合法的,不必先 Exists 分兩條路。
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            // 換上去失敗 —— 防毒軟體正在掃那個檔、雲端同步佔著、磁碟滿了。
            // 暫存檔留著的話會躺在使用者的筆記資料夾裡,而掃描只看 *.md,
            // 所以沒有任何畫面會提到它,它就這樣一直在那裡。
            // 清不掉就算了:真正要往外傳的是原本那個失敗,不是清理的失敗。
            try
            {
                File.Delete(temp);
            }
            catch (IOException)
            {
                // 刪不掉也只能留著。
            }
            catch (UnauthorizedAccessException)
            {
                // 同上。
            }

            throw;
        }
    }
}
