using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Notelet.Commands;
using Notelet.Core;

namespace Notelet.Pages;

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
    /// 重新從 repository 取 <paramref name="noteId"/> 的最新內容,同步複製命令的文字,
    /// 回傳渲染好的內容。頁面的 <c>Title</c> 由呼叫端自己設(那是頁面屬性,收不進來)。
    /// </summary>
    /// <param name="note">
    /// 手上這份快照;會被換成重新查到的。查不到(剛被刪掉)就沿用舊的,至少還看得到東西。
    /// </param>
    public static MarkdownContent Reload(
        INoteRepository repository,
        string noteId,
        ref Note note,
        CopyNoteBodyCommand copyBody)
    {
        // 重新查一次而不是直接用快照:使用者可能剛編輯完,或別台機器的改動剛同步下來。
        note = repository.GetById(noteId) ?? note;

        copyBody.Text = note.Body;

        // 渲染規則(補標題、單換行變硬換行、避開程式碼區塊與表格)全在 Core,
        // 那一層有測試涵蓋;這裡只負責把字串交給 CmdPal。兩頁走同一條,
        // 同一則筆記在兩個地方長得一樣。
        return new MarkdownContent(NotePreview.Render(note));
    }
}
