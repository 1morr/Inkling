namespace Inkling.Core;

/// <summary>
/// 「讓一個檔案消失」這件事怎麼做。
///
/// 抽出來是因為刪除的**去向**是個平台決定，而 Core 這一層刻意不知道自己跑在哪:
/// 正式跑起來要送進 Windows 的資源回收筒(誤刪拿得回來)，那需要 shell32,
/// 只有 UI 層那個 Windows-only 的專案適合放。測試則用預設的
/// <see cref="PermanentFileDeleter"/> 直接刪 —— 跑一次測試就往使用者的資源回收筒
/// 塞一堆垃圾，那才是真的討厭。
/// </summary>
public interface IFileDeleter
{
    /// <summary>刪掉指定路徑的檔案。檔案本來就不在時視為成功。</summary>
    void Delete(string path);
}

/// <summary>
/// 直接從磁碟抹掉，不經過資源回收筒。Core 的預設，也是測試用的那一個。
/// </summary>
public sealed class PermanentFileDeleter : IFileDeleter
{
    public void Delete(string path) => File.Delete(path);
}
