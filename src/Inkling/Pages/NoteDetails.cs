using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Inkling.Core;
using Inkling.Properties;

namespace Inkling.Pages;

/// <summary>
/// 一則筆記的詳細窗格(<c>Details</c>)組裝,清單頁與刪除頁共用這一份 ——
/// 曾經各刻一份,而「渲染規則跟預覽頁一致」「Size 要明著寫」這種約定,
/// 分散手寫就有多處忘記的機會。
/// </summary>
internal static class NoteDetails
{
    /// <summary>
    /// 標題 + 內文,寬度固定最寬(清單:詳情 = 1:1),沒有設定項也沒有快速鍵。
    /// 清單那一邊只有標題與摘要,寬一點也不多給什麼資訊;右邊是筆記本文,
    /// 窄一檔就多折斷幾十行,看原始文字時特別有感。曾經做過一個三檔循環的
    /// Ctrl+D 加一個設定項,代價是設定頁與清單頁之間一整條雙向同步線,
    /// 而實際上永遠停在最寬 —— 移除的理由見 docs/design-notes.md〈詳細面板寬度固定在最寬〉。
    /// </summary>
    /// <param name="showSource">
    /// true 顯示原始 Markdown(清單頁的 Ctrl+U 切換),false 走跟預覽頁同一套渲染規則。
    /// </param>
    public static Details For(Note note, bool showSource = false) => new()
    {
        Title = note.Title,
        Body = BodyFor(note, showSource),

        // **Size 一定要明著寫**:ContentSize 的 0 是 Small,`new Details()` 不設就是
        // 最窄那一檔(實測過)。CmdPal 也只認 Small / Medium / Large,對應 3:1 / 2:1 / 1:1
        // (它的 DetailsSizeToGridLengthConverter),沒有無段調整 —— 整個介面裡連一個
        // GridSplitter 都沒有,所以「寬」就是能給的上限。
        Size = ContentSize.Large,
    };

    /// <summary>
    /// 內文部分:空的給佔位文字;其餘的換行處理要跟預覽頁一致,
    /// 否則同一則筆記在兩個地方長得不一樣。
    /// </summary>
    private static string BodyFor(Note note, bool showSource = false)
    {
        if (note.Body.Length == 0)
        {
            return Resources.NoBody;
        }

        return showSource
            ? NotePreview.RenderSource(note.Body)
            : NotePreview.PreserveLineBreaks(note.Body);
    }
}
