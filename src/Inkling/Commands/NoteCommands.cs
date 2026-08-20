using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Inkling.Core;
using Inkling.Pages;
using Inkling.Properties;

namespace Inkling.Commands;

/// <summary>
/// 同一則筆記的 <c>Ctrl+K</c> 選單項:編輯 / 複製內文 / 在編輯器開啟 / 開啟檔案位置。
///
/// 同一則筆記有三個畫面(清單頁、預覽頁、記下並預覽頁),選單與鍵位要一致 ——
/// 手勢跨頁通用,使用者不必記「這一頁的複製是哪個鍵」。這幾項曾經各頁各刻一份,
/// 而且已經漂移過:記下頁的複製用了 toolkit 原生 <c>CopyTextCommand</c>
/// (預設 ShowToast,toast 一搶焦點主視窗就自我隱藏,整頁消失)、編輯鍵就地寫死
/// 沒走 <see cref="Shortcuts"/>。收在這裡之後,加命令、改鍵、改圖示只有一個地方要動。
///
/// 各頁專屬的項(清單頁的切換原始文字與刪除、記下頁的「完成」)仍由各頁自己插,
/// 順序也由各頁決定 —— 第一項會被 CmdPal 當成次要命令放上底部工具列,那是有語意的位置。
/// </summary>
internal static class NoteCommands
{
    /// <summary>
    /// 編輯這則筆記(表單頁)。<paramref name="onSaved"/> 是存檔後的回呼,
    /// 頁面靠它重新取一次內容 —— 導覽回來時 CmdPal 不會主動重拿,見 <see cref="NoteEditPage"/>。
    /// </summary>
    public static CommandContextItem Edit(INoteRepository repository, Note note, Action? onSaved = null) =>
        new(new NoteEditPage(repository, note, onSaved))
        {
            Title = Resources.CommandEdit,
            Icon = Icons.Edit,
            RequestedShortcut = Shortcuts.Edit,
        };

    /// <summary>
    /// 複製內文。命令實例由呼叫端準備:要回饋的(清單頁,在那一列打標籤)在建構時傳
    /// <c>report</c>;會更新內容的(預覽頁、記下頁)把實例留著,重新取內容時改掉
    /// <see cref="CopyTextCommand.Text"/>。
    /// </summary>
    public static CommandContextItem CopyBody(CopyNoteBodyCommand command) =>
        new(command)
        {
            Title = Resources.CommandCopyBody,
            Subtitle = Resources.CommandCopyBodySubtitle,
            Icon = Icons.Copy,
            RequestedShortcut = Shortcuts.CopyBody,
        };

    /// <summary>用系統預設的程式開啟這個 <c>.md</c>。</summary>
    public static CommandContextItem OpenInEditor(Note note) =>
        new(new OpenUrlCommand(note.FilePath))
        {
            Title = Resources.CommandOpenInEditor,
            Icon = Icons.OpenExternal,
            RequestedShortcut = Shortcuts.OpenExternal,
        };

    /// <summary>
    /// 在檔案總管裡開啟所在資料夾,並選中這個檔案。
    ///
    /// 直接用 toolkit 現成的命令(它跑的是 <c>explorer.exe /select,"&lt;路徑&gt;"</c>),
    /// 自己寫一個只會多一份 Process.Start 的錯誤處理。Name 要自己換掉:
    /// toolkit 給的是它自己資源檔裡的字串,而它跟著 CmdPal 的語言走、不見得跟我們一致,
    /// 而這一項有機會出現在底部工具列上。
    /// </summary>
    public static CommandContextItem OpenFileLocation(Note note) =>
        new(new ShowFileInFolderCommand(note.FilePath)
        {
            Name = Resources.CommandOpenFileLocation,
        })
        {
            Title = Resources.CommandOpenFileLocation,
            Subtitle = Resources.CommandOpenFileLocationSubtitle,
            Icon = Icons.FileLocation,
            RequestedShortcut = Shortcuts.OpenFileLocation,
        };
}
