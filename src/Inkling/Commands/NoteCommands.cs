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
/// 各頁專屬的項(清單頁的新增筆記與刪除)仍由各頁自己插,順序也由各頁決定。
///
/// <para><b>順序有語意,而且兩種頁面的規則不一樣 —— 這裡踩過坑。</b></para>
///
/// 底部工具列有兩顆按鈕:主命令(<c>Enter</c>)與次命令(<c>Ctrl+Enter</c>),坐上去的是誰
/// **只看順序**,跟那個命令自己綁的 <c>RequestedShortcut</c> 無關(所以同一個命令可能同時
/// 有兩個鍵能觸發)。但「第幾個」的算法兩種頁面不同:
///
/// <list type="bullet">
/// <item><c>ListPage</c> 的一列:主命令是那一列自己的命令,<b><c>MoreCommands[0]</c> 才是次命令</b>。</item>
/// <item><c>ContentPage</c>:<b><c>Commands[0]</c> 是主命令,<c>Commands[1]</c> 是次命令</b>。</item>
/// </list>
///
/// 三個畫面刻意讓 <b><c>Ctrl+Enter</c> 一律是「編輯」</b>,所以兩個 <c>ContentPage</c>
/// (預覽頁、記下並預覽頁)的第一項都是 <see cref="Done"/>、第二項都是 <see cref="Edit"/>。
/// 加新項目時**不要插進前兩個位置**,那會把編輯從 <c>Ctrl+Enter</c> 上擠掉
/// (真的發生過:切換原始文字排到第二個,複製內文就被頂掉了)。
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

    /// <summary>
    /// 「完成」:看完了,收起整個 Command Palette。**兩個 <c>ContentPage</c> 的 <c>Enter</c>
    /// 都是它**(預覽頁、記下並預覽頁),為的是讓 <c>Ctrl+Enter</c> 空出來給編輯 ——
    /// 理由見 <see cref="Pages.NotePreviewPage"/> 上那段「兩個位置鍵」的說明。
    ///
    /// 回傳的是命令本身而不是選單項:記下並預覽頁在存檔失敗時要就地把它改成「返回」
    /// (換 <c>Name</c> 與 <c>Result</c>),所以呼叫端得拿得到實例。
    ///
    /// 收起而不是 <c>GoHome</c>:看完筆記的下一步是回去做原本的事,
    /// 留一個主搜尋框在畫面上只是多一次 <c>Esc</c>。
    /// </summary>
    public static AnonymousCommand Done() => new(() => { })
    {
        Name = Resources.CommandDone,
        Icon = Icons.Done,
        Result = CommandResult.Dismiss(),
    };
}
