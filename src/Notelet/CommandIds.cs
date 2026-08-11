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

    /// <summary>
    /// 「記下」這個動作本身(快速記下頁裡的第一列)。
    ///
    /// 這個 Id 曾經同時掛在已經移除的 fallback 項目上,所以它八成還躺在使用者的
    /// CmdPal settings.json 裡。**別拿它去給別的東西用** —— 舊設定會直接套到新命令上。
    /// 頁面用的是底下那個分開的 <see cref="QuickCapturePage"/>。
    /// </summary>
    public const string QuickCapture = "Notelet.QuickCapture";

    /// <summary>
    /// 快速記下頁(頂層命令「Notelet:快速記下」)。
    ///
    /// 刻意跟 <see cref="QuickCapture"/> 分開:CmdPal 是拿 Id 當鍵去存 alias、快速鍵、
    /// 釘選與 fallback 規則的,同一個 Id 掛著一個頂層命令又掛著一個 fallback,
    /// 那些設定會互相蓋掉。而 <see cref="QuickCapture"/> 已經寫進使用者的 settings.json
    /// (fallback 的「Include in the Global result」),不能拿去給頁面用。
    /// </summary>
    public const string QuickCapturePage = "Notelet.QuickCapturePage";

    /// <summary>
    /// 刪除所有筆記那一頁(頂層命令「Notelet:刪除所有筆記」)。
    ///
    /// 這個 Id 原本掛在一個按下去就跳確認框的命令上,現在掛在頁面上。
    /// **刻意沿用同一個字串**:對 CmdPal 來說那只是「同一個命令換了行為」,
    /// 使用者設過的 alias 與釘選都還對得上。
    /// </summary>
    public const string DeleteAll = "Notelet.DeleteAll";
}
