using Microsoft.CommandPalette.Extensions.Toolkit;

namespace Inkling;

/// <summary>
/// Segoe Fluent Icons 的字符。
///
/// 碼位寫成數字而不是字面字元,也不是 C# 的 \u 逸出:
/// 這些碼位落在 Unicode 私用區,直接貼字元進原始碼的話,在編輯器、git diff、
/// code review 裡通通顯示成空白方塊,根本看不出改了什麼。
/// 而 \u 逸出雖然看得見,卻會被各種文字處理工具當成逸出序列展開 —— 用工具改這個檔案時
/// 碼位會無聲地變成一個私用區字元(實際踩過)。數字碼位兩邊的問題都沒有。
///
/// 對照表:https://learn.microsoft.com/windows/apps/design/style/segoe-fluent-icons-font
/// </summary>
internal static class Icons
{
    // ---------------------------------------------------------------------
    // 五個頂層命令的圖示。這一組是自己畫的 PNG,不是字形。
    //
    // 為什麼只有這幾個自訂:它們是使用者在 CmdPal 主搜尋框裡會看到的東西,
    // 要一眼看得出是同一個產品。Ctrl+K 選單裡的編輯 / 複製 / 開啟位置那些
    // 繼續用 Segoe Fluent —— 那裡跟 CmdPal 內建命令混在一起,字形反而更協調,
    // 而且 16/20px 有專業 hinting,手畫的比不上。
    //
    // 為什麼一個要兩張:字形是以文字繪製的,前景色自動跟主題走;PNG 不會。
    // 所以每個命令備了淺色主題(深色前景)與深色主題(白色前景)兩張,
    // 交給 FromRelativePaths 去挑。少了這一層,深色主題下圖示會整片看不見。
    //
    // PNG 由 tools\render-icons.ps1 從 assets\icon\inkling-cmd-*.svg 產生,
    // **不要手改**。它們靠 Inkling.csproj 那條 Assets\*.png 的萬用字元帶
    // CopyToOutputDirectory 進建置輸出 —— 少了它圖示全變成灰方塊。
    // ---------------------------------------------------------------------

    /// <summary>頂層命令「Inkling」(清單頁)。</summary>
    public static IconInfo TopLevelList => Family("List");

    /// <summary>頂層命令「Inkling:快速記下」。</summary>
    public static IconInfo TopLevelCapture => Family("Capture");

    /// <summary>頂層命令「Inkling:新增筆記」。</summary>
    public static IconInfo TopLevelNew => Family("New");

    /// <summary>
    /// 頂層命令「Inkling:刪除筆記」。
    ///
    /// 這一個是刻意付的代價:垃圾桶(<see cref="Delete"/> 的 0xE74D)比「筆畫＋叉」
    /// 一望即知,但頂層命令要是同一個家族。覺得誤刪風險比家族感重要的話,
    /// 把這一行改回 <c>Glyph(0xE74D)</c> 就好,其他都不用動。
    /// </summary>
    public static IconInfo TopLevelDelete => Family("Delete");

    /// <summary>頂層命令「Inkling:隨手草稿」。</summary>
    public static IconInfo TopLevelScratchpad => Family("Scratchpad");

    private static IconInfo Family(string name) => IconHelpers.FromRelativePaths(
        $"Assets\\Command{name}Light.png",
        $"Assets\\Command{name}Dark.png");

    // ---------------------------------------------------------------------
    // 以下是 Segoe Fluent 的字形,給頁面內與 Ctrl+K 選單用。
    // ---------------------------------------------------------------------

    /// <summary>QuickNote — 清單裡的單一筆記。</summary>
    public static IconInfo Note => Glyph(0xE70B);

    /// <summary>
    /// Lightbulb — 快速記下頁本身(頂層那一列走 <see cref="TopLevelCapture"/>)。
    ///
    /// 刻意跟 <see cref="Add"/> 分開:兩個都用 + 的話,「快速記下」與「新增筆記」
    /// 在畫面上長得一模一樣,只剩標題能分辨。燈泡也剛好對得上這個功能的用途 ——
    /// 記下隨時冒出來的想法。
    /// </summary>
    public static IconInfo Capture => Glyph(0xEA80);

    /// <summary>Add — 新增筆記(完整表單)。</summary>
    public static IconInfo Add => Glyph(0xE710);

    /// <summary>
    /// Cancel — 隨手草稿的「捨棄變更」。
    ///
    /// 不用 <see cref="Done"/> 的勾勾:那個符號說的是「成功了」,而按下去什麼都不會存。
    /// 也不用 <see cref="Delete"/> 的垃圾桶 —— 沒有東西被刪掉,檔案裡的草稿原封不動。
    /// </summary>
    public static IconInfo Discard => Glyph(0xE711);

    /// <summary>
    /// Document — 隨手草稿那一頁本身(頂層那一列走 <see cref="TopLevelScratchpad"/>)。
    ///
    /// 沒有沿用 <see cref="Note"/> 的 QuickNote:隨手草稿不是一則筆記,兩者在
    /// <c>Ctrl+K</c> 選單裡有機會並排,長一樣只會讓人以為點錯了。
    /// </summary>
    public static IconInfo Scratchpad => Glyph(0xE8A5);

    /// <summary>Edit — 編輯筆記。</summary>
    public static IconInfo Edit => Glyph(0xE70F);

    /// <summary>Page — Markdown 預覽。</summary>
    public static IconInfo Preview => Glyph(0xE7C3);

    /// <summary>OpenWith — 在外部編輯器開啟。</summary>
    public static IconInfo OpenExternal => Glyph(0xE7AC);

    /// <summary>
    /// FolderOpen — 在檔案總管裡選中這個檔案。
    ///
    /// 跟 CmdPal 內建的 <c>ShowFileInFolderCommand</c> 用同一個碼位(它寫死 0xE838),
    /// 檔案索引與書籤擴展的「開啟檔案位置」長的就是這個樣子。
    /// </summary>
    public static IconInfo FileLocation => Glyph(0xE838);

    /// <summary>Copy — 複製內文。</summary>
    public static IconInfo Copy => Glyph(0xE8C8);

    /// <summary>Delete — 把筆記送進資源回收筒。</summary>
    public static IconInfo Delete => Glyph(0xE74D);

    /// <summary>Paste — 內文取自剪貼簿。</summary>
    public static IconInfo Paste => Glyph(0xE77F);

    /// <summary>
    /// Warning — 不是 Inkling 建立的檔案。
    ///
    /// 用警告而不是另一種文件圖示:它只出現在刪除筆記那一頁,
    /// 而在那個情境下這件事就是個警告 —— 這個檔案是別人的。
    /// </summary>
    public static IconInfo External => Glyph(0xE7BA);

    /// <summary>Code — 詳細窗格切換成原始文字。</summary>
    public static IconInfo Source => Glyph(0xE943);

    /// <summary>Settings — 設定頁。跟 CmdPal 自己的設定圖示同一個碼位。</summary>
    public static IconInfo Settings => Glyph(0xE713);

    /// <summary>Accept — 看完了,收起 Command Palette。</summary>
    public static IconInfo Done => Glyph(0xE8FB);

    private static IconInfo Glyph(int codepoint) => new(char.ConvertFromUtf32(codepoint));
}
