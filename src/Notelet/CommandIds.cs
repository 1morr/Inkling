namespace Notelet;

/// <summary>
/// 每個會出現在 Command Palette 頂層的命令都要有一個固定的 Id。
///
/// 為什麼非有不可:CmdPal 把使用者對命令做的設定 —— alias、全域快速鍵、釘選、
/// fallback 的「Include in the Global result」與排序 —— 通通存在自己的 settings.json 裡,
/// 鍵就是這個 Id。而命令沒有設 Id 時,CmdPal 會現場算一個
/// (<c>TopLevelViewModel.GenerateId</c>:對 <c>ProviderId + DisplayTitle + Title + Subtitle</c>
/// 取 WyHash64)。也就是說,標題變一個字,那個命令對 CmdPal 來說就變成了另一個命令,
/// 使用者設過的東西全部對不上。
///
/// 對 fallback 更致命:它的標題本來就會跟著使用者打的字一直變。實際踩到過 —— CmdPal 的
/// settings.json 裡留下了兩個 Notelet 的 fallback 條目,其中一個算出來的雜湊
/// 正好對應標題「記下:你好」,也就是某次重新載入時使用者剛好打了那句話。
/// 結果就是:改一次設定,快速新增就「莫名其妙不會出現了」。
///
/// 這些字串跟資料格式一樣是對外承諾,改了等於把使用者的設定清掉,所以不要改。
/// </summary>
internal static class CommandIds
{
    /// <summary>命令提供者本身。</summary>
    public const string Provider = "Notelet";

    /// <summary>清單頁(頂層命令「Notelet」)。</summary>
    public const string List = "Notelet.List";

    /// <summary>完整表單的新增頁(頂層命令「Notelet:新增筆記」)。</summary>
    public const string NewNote = "Notelet.NewNote";

    /// <summary>主搜尋框的快速新增。命令與 fallback 項目共用同一個 Id。</summary>
    public const string QuickCapture = "Notelet.QuickCapture";
}
