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
/// 兩個 <c>ContentPage</c> 的前兩項是「編輯」與「完成」,**順序刻意相反**:
///
/// <list type="bullet">
/// <item><see cref="Pages.NotePreviewPage"/>(從清單找到某一則進來的)—— <see cref="Edit"/> 在前:下一步多半是改它。</item>
/// <item><see cref="Pages.CapturedNotePage"/>(剛記完看一眼)—— <see cref="Done"/> 在前:下一步是收工。</item>
/// </list>
///
/// 也就是說 <c>Enter</c> 各自給那一頁真正的下一步,另一個動作退到 <c>Ctrl+Enter</c>。
/// 曾經兩頁都是「完成 / 編輯」好讓 <c>Ctrl+Enter</c> 跨頁同義,但預覽頁的 <c>Enter</c>
/// 因此變成「把面板收掉」,而使用者剛剛才搜到那一則 —— 代價比不一致大,考證見
/// <see cref="Pages.NotePreviewPage"/>。
///
/// 加新項目時**不要插進前兩個位置**,那會把這兩顆按鈕擠掉
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
    public static CommandContextItem OpenInEditor(Note note) => OpenInEditor(note.FilePath);

    /// <summary>
    /// 同上,但直接吃路徑 —— 隨手草稿不是一則 <see cref="Note"/>(它沒有標題也沒有 id),
    /// 卻要用同一個鍵位、同一個圖示、同一句話跳到外部編輯器。
    /// </summary>
    /// <param name="dismiss">
    /// 開完要不要順手把面板收起來。<b>隨手草稿一定要傳 true。</b>
    ///
    /// 它是這一族裡唯一「畫面上有一份使用者還能按儲存的副本」的頁面:面板留著的話,
    /// 使用者在外部編輯器改完回到 CmdPal,那張卡片還停在跳出去之前的舊值,再按一次儲存
    /// 就把外部的修改整個蓋掉。收起來之後下次打開會重新 <c>GetContent()</c> 讀檔,
    /// 看到的才是編輯器存下的那一版。
    ///
    /// 筆記那三頁不傳(維持 <c>KeepOpen</c>):它們顯示的是唯讀的預覽,沒有這個問題。
    /// </param>
    public static CommandContextItem OpenInEditor(string filePath, bool dismiss = false) =>
        new(new OpenNoteFileCommand(filePath, dismiss))
        {
            Title = Resources.CommandOpenInEditor,
            Icon = Icons.OpenExternal,
            RequestedShortcut = Shortcuts.OpenExternal,
        };

    /// <summary>
    /// 在檔案總管裡開啟所在資料夾,並選中這個檔案。
    ///
    /// 底下還是 toolkit 的命令,但包了一層 —— 它對不存在的路徑是靜默的,而且預設的
    /// <c>Result</c> 跟隔壁的 <c>Ctrl+O</c> 相反。兩件事都在
    /// <see cref="ShowNoteInFolderCommand"/> 上說明。
    /// </summary>
    public static CommandContextItem OpenFileLocation(Note note) =>
        new(new ShowNoteInFolderCommand(note.FilePath))
        {
            Title = Resources.CommandOpenFileLocation,
            Subtitle = Resources.CommandOpenFileLocationSubtitle,
            Icon = Icons.FileLocation,
            RequestedShortcut = Shortcuts.OpenFileLocation,
        };

    /// <summary>
    /// 「完成」:看完了,收起整個 Command Palette。記下並預覽頁的 <c>Enter</c> 是它
    /// (剛記完,下一步就是收工);預覽頁把 <c>Enter</c> 讓給編輯,它退到 <c>Ctrl+Enter</c> ——
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
