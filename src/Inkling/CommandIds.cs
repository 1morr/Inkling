namespace Inkling;

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
/// settings.json 裡留下了兩個這個擴展的 fallback 條目,其中一個算出來的雜湊
/// 正好對應標題「記下:你好」,也就是某次重新載入時使用者剛好打了那句話。
/// 結果就是:改一次設定,快速新增就「莫名其妙不會出現了」。
///
/// 這些字串跟資料格式一樣是對外承諾,改了等於把使用者的設定清掉,所以不要改。
///
/// **為什麼它們還叫 <c>Notelet.*</c>:那是這個擴展改名前的名字,故意不跟著改。**
/// 實測過 CmdPal 的 settings.json:<c>Aliases</c> 的鍵是**純命令 Id**(條目裡沒有 PFN、
/// 也沒有 provider 參照),只有 <c>ProviderSettings</c> 與 <c>PinnedCommands</c> 帶
/// <c>&lt;PFN&gt;!App!&lt;ProviderId&gt;</c>。所以改名時只要這幾個字串不動,使用者設過的
/// alias 就跟著新名字走;動了它們,alias 當場全部失效,而換來的只是「看起來一致」——
/// 使用者永遠看不到這些字串。新增命令時用新的 Id,不要為了整齊回頭改這些。
/// </summary>
internal static class CommandIds
{
    /// <summary>命令提供者本身。</summary>
    public const string Provider = "Notelet";

    /// <summary>清單頁(頂層命令「Inkling」)。</summary>
    public const string List = "Notelet.List";

    /// <summary>完整表單的新增頁(頂層命令「Inkling:新增筆記」)。</summary>
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
    /// 快速記下頁(頂層命令「Inkling:快速記下」)。
    ///
    /// 刻意跟 <see cref="QuickCapture"/> 分開:CmdPal 是拿 Id 當鍵去存 alias、快速鍵、
    /// 釘選與 fallback 規則的,同一個 Id 掛著一個頂層命令又掛著一個 fallback,
    /// 那些設定會互相蓋掉。而 <see cref="QuickCapture"/> 已經寫進使用者的 settings.json
    /// (fallback 的「Include in the Global result」),不能拿去給頁面用。
    /// </summary>
    public const string QuickCapturePage = "Notelet.QuickCapturePage";

    /// <summary>
    /// 刪除筆記那一頁(頂層命令「Inkling:刪除筆記」)。
    ///
    /// 這個 Id 一路沿用下來:它原本掛在一個按下去就跳確認框的「刪除所有筆記」命令上,
    /// 後來換成頁面(多選曾經做過、又整個移除,見 docs/design-notes.md〈為什麼沒有多選〉)。
    /// **每一次都刻意沿用同一個字串** ——
    /// 對 CmdPal 來說那只是「同一個命令換了行為」,使用者設過的 alias 與釘選都還對得上。
    /// 標題可以改(Id 有設就不會去算雜湊),Id 不行。
    /// </summary>
    public const string DeleteAll = "Notelet.DeleteAll";

    /// <summary>
    /// 隨手草稿那一頁(頂層命令「Inkling:隨手草稿」)。
    ///
    /// 新命令給新 Id。前綴照舊是 <c>Notelet.</c> —— 那是改名前的名字,現在的作用只剩
    /// 「跟其他幾個 Id 長得一樣」,而使用者永遠看不到這些字串(見這個類別上面的說明)。
    /// </summary>
    public const string Scratchpad = "Notelet.Scratchpad";
}
