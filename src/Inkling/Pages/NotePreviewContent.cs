using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Inkling.Commands;
using Inkling.Core;
using Inkling.Properties;

namespace Inkling.Pages;

/// <summary>
/// 「重新查一次 → 更新標題與複製命令 → 渲染」這一段,<see cref="NotePreviewPage"/> 與
/// <see cref="CapturedNotePage"/> 共用 —— 兩頁顯示的是同一則筆記,曾經各刻一份而且逐字相同。
///
/// 配套的「編輯存檔後要主動 <c>RaiseItemsChanged(1)</c>」也寫在這裡講一次:
/// **CmdPal 不會因為導覽回來就重新取內容**,編輯頁存檔後必須靠 <c>onSaved</c> 回呼
/// 讓上一頁主動發這個事件,否則畫面會停在存檔前的樣子。這是這個專案的已知陷阱,
/// 兩頁的 <c>Refresh</c> 做的是同一件事。
/// </summary>
internal static class NotePreviewContent
{
    /// <summary>
    /// 重新從 repository 取 <paramref name="filePath"/> 的最新內容,同步複製命令的文字,
    /// 回傳這一頁要顯示的內容。頁面的 <c>Title</c> 由呼叫端自己設(那是頁面屬性,收不進來)。
    /// </summary>
    /// <param name="note">
    /// 手上這份快照;會被換成重新查到的。查不到(剛被刪掉)就沿用舊的,至少還看得到東西。
    /// </param>
    /// <param name="showSource">
    /// 原始文字模式(全域共用,見 <see cref="ISourceModeStore"/>)。
    /// <b>每次取內容都重讀一次</b>:這兩頁都是清單裡每個項目各建一個的短命物件,
    /// 不能訂閱那個長壽事件,狀態有可能是在別的畫面上被切掉的。
    /// </param>
    public static IContent Reload(
        INoteRepository repository,
        string filePath,
        ref Note note,
        CopyNoteBodyCommand copyBody,
        bool showSource)
    {
        // 重新查一次而不是直接用快照:使用者可能剛編輯完,或別台機器的改動剛同步下來。
        // 認路徑不認 id —— 同一個 id 可能對到兩個檔案(雲端硬碟的衝突副本),見 Note.Id。
        note = repository.GetByPath(filePath) ?? note;

        // **兩個都要換。** 只換 Text 的話,使用者剛在編輯頁改過標題時,
        // 複製後那則 toast 會講出舊標題 —— 那比不講更糟。
        copyBody.Text = note.Body;
        copyBody.NoteTitle = note.Title;

        if (showSource)
        {
            return Source(note);
        }

        // 渲染規則(補標題、單換行變硬換行、避開程式碼區塊與表格)全在 Core,
        // 那一層有測試涵蓋;這裡只負責把字串交給 CmdPal。兩頁走同一條,
        // 同一則筆記在兩個地方長得一樣。
        return new MarkdownContent(NotePreview.Render(note));
    }

    /// <summary>
    /// 原始文字模式:交給 CmdPal 的**純文字檢視器**,而不是 Markdown 渲染器。
    ///
    /// 這是這一頁跟清單頁詳細窗格唯一長得不一樣的地方,而且是刻意的。詳細窗格只吃 Markdown
    /// 字串(<c>IDetails.Body</c>),要顯示原文只能逐字逃脫,代價是行首縮排與連續空行
    /// 會被渲染器正規化(見 <c>NotePreview.RenderSource</c>)。整頁的預覽沒有這個限制 ——
    /// <c>IPlainTextContent</c> 是原封不動的字串,**縮排、連續空行、貼進來的 HTML / SVG
    /// 通通照原樣顯示**,而後者正是「渲染完整頁空白」的成因(渲染器把它們吃掉了)。
    ///
    /// <b>0.11.11762.0 安裝版支援這條路</b> —— byte-scan 對照過:
    /// <c>Microsoft.CmdPal.UI.exe</c> 裡有 <c>ContentPlainTextViewModel</c> /
    /// <c>PlainTextContentViewer</c> / <c>IPlainTextContent</c> / <c>get_WrapWords</c>
    /// (UTF-8),<c>resources.pri</c> 裡有 <c>PlainTextContentTemplate</c> 與那個檢視器的
    /// 右鍵選單字串(UTF-16)。不是照 <c>main</c> 的原始碼寫的。
    ///
    /// 兩個屬性的取捨:
    /// <list type="bullet">
    /// <item><b>等寬字</b> —— 看的是縮排對齊,比例字型會讓對齊失去意義。</item>
    /// <item><b>自動換行開著</b> —— 關掉的話長行要橫向捲動才看得到,而這個面板是鍵盤驅動的,
    /// 橫捲很難按。使用者要關可以在內容上按右鍵(檢視器自己的選單有換行、等寬與縮放,
    /// 那是 CmdPal 給的,我們不必也不能代勞)。</item>
    /// </list>
    ///
    /// 只給內文,不補標題:標題已經在頁面的標題列與底部命令列上,而這個模式的承諾是
    /// 「檔案裡的字元一個不多一個不少」,補一行 <c>#</c> 出來反而是騙人的。
    /// </summary>
    private static PlainTextContent Source(Note note) => new(
        note.Body.Length == 0 ? Resources.NoBodyPlain : note.Body)
    {
        FontFamily = FontFamily.Monospace,
        WrapWords = true,
    };
}
