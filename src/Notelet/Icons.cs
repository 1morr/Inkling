using Microsoft.CommandPalette.Extensions.Toolkit;

namespace Notelet;

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
    /// <summary>QuickNote — 擴展與清單頁。</summary>
    public static IconInfo Note => Glyph(0xE70B);

    /// <summary>
    /// Lightbulb — 快速記下。
    ///
    /// 刻意跟 <see cref="Add"/> 分開:兩個都用 + 的話,「快速記下」與「新增筆記」
    /// 在頂層清單裡長得一模一樣,只剩標題能分辨。燈泡也剛好對得上這個功能的用途 ——
    /// 記下隨時冒出來的想法。
    /// </summary>
    public static IconInfo Capture => Glyph(0xEA80);

    /// <summary>Add — 新增筆記(完整表單)。</summary>
    public static IconInfo Add => Glyph(0xE710);

    /// <summary>Edit — 編輯筆記。</summary>
    public static IconInfo Edit => Glyph(0xE70F);

    /// <summary>Page — Markdown 預覽。</summary>
    public static IconInfo Preview => Glyph(0xE7C3);

    /// <summary>OpenWith — 在外部編輯器開啟。</summary>
    public static IconInfo OpenExternal => Glyph(0xE7AC);

    /// <summary>Copy — 複製內文。</summary>
    public static IconInfo Copy => Glyph(0xE8C8);

    /// <summary>Code — 詳細窗格切換成原始文字。</summary>
    public static IconInfo Source => Glyph(0xE943);

    /// <summary>DockRight — 詳細窗格的寬度。</summary>
    public static IconInfo DetailsWidth => Glyph(0xE90D);

    private static IconInfo Glyph(int codepoint) => new(char.ConvertFromUtf32(codepoint));
}
