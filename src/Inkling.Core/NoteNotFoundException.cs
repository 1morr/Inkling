namespace Inkling.Core;

/// <summary>
/// 指定的 id 找不到對應的筆記。
/// </summary>
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

    /// <remarks>
    /// 訊息是英文,而且刻意不走資源檔:Core 這一層不認得介面語言(它連 CmdPal 都不認得),
    /// 而這句話最後會被 UI 包進「刪除失敗:{0}」那類字串裡顯示 —— 同一個位置平常裝的是
    /// .NET 自己丟的例外訊息,在這個套件裡固定是英文(附屬組件沒有進 MSIX 佈局,驗過),
    /// 所以跟著英文才不會一句中文一句英文。
    /// </remarks>
    public static NoteNotFoundException ForId(string id) =>
        new($"No note with id '{id}'.") { Id = id };
}
