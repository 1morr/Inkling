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

    /// <summary>
    /// 找不到的那個 id。**生產程式碼目前沒有人讀它**,留著是因為它是這個例外唯一帶得出
    /// 現場的東西:兩個丟出點(Update / Delete)都在「使用者按了鍵、而那則筆記剛好被
    /// 別的程式移走」的競態上,而 UI 只把 Message 包進「刪除失敗:{0}」。真的要查的時候
    /// 沒有它就只剩一句話。不要因為「沒人用」把它拿掉。
    /// </summary>
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
