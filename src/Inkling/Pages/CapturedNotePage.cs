using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Inkling.Commands;
using Inkling.Core;
using Inkling.Properties;

namespace Inkling.Pages;

/// <summary>
/// 「記下並預覽」:存檔,然後把剛存下來的那則筆記整篇顯示出來,再按一次 Enter 收起。
///
/// **這一頁自己就是那個副作用** —— 它掛在快速記下頁那一列上當命令,而 CmdPal 對
/// 「命令是一個頁面」的處理是導覽過去,不是 <c>Invoke</c>。也就是說:寫檔發生在
/// <see cref="GetContent"/> 裡,不是在某個 <c>InvokableCommand</c> 裡。
///
/// 為什麼非得這樣不可:<c>CommandResult.GoToPage</c> 是個空殼。SDK 有那個型別,
/// 但 CmdPal 的 <c>ShellViewModel.UnsafeHandleCommandResult</c> 那個 switch 裡
/// 根本沒有 <c>GoToPage</c> 這個 case(0.11.11762.0 沒有,連 main 都沒有)——
/// 所以「存完之後叫 CmdPal 跳到某一頁」用回傳值做不到,唯一還通的路就是讓那一列的
/// 命令本身是一個頁面。
///
/// 「打字打到一半就存檔了」不會發生:清單項的 <c>CommandViewModel.InitializeProperties</c>
/// 只讀 Id / Name / Icon,不碰 <c>GetContent</c>(查過原始碼)。內容是使用者真的按下
/// Enter、CmdPal 建出 <c>ContentPageViewModel</c> 時才取的。
///
/// <para><b>toast 的規矩在這一頁分成兩段,別把它讀成「完全不能發」。</b></para>
///
/// toast 視窗會搶焦點,而 CmdPal 主視窗一失焦就把自己藏起來(<c>MainWindow_Activated</c>
/// → <c>EndSession("LostFocus")</c>,沒有開關)—— 那正是「記下之後 CmdPal 整個消失」的成因。
/// 所以**停留期間一個 toast 都不能發**:進頁、存檔那一刻、存檔失敗、複製內文,全部不行,
/// 這一頁的存在意義就是讓使用者看一眼。
///
/// **但按下「完成」時相反。** 那一下的語意就是收工,面板本來就要關掉 ——
/// toast 搶焦點造成的隱藏剛好是我們要的結果,所以它是免費的。而這裡非發不可:
/// 關掉「記下後先看一眼」走的是 <see cref="Commands.QuickCaptureCommand"/>,那條路存完會
/// 跳「已記下:標題」;開著設定卻什麼都沒有,同一個動作換個設定就少了結尾確認。
/// 兩條路共用 <c>Resources.CaptureSaved</c>,文案因此不會漂移。見 <see cref="Capture"/>。
/// </summary>
internal sealed partial class CapturedNotePage : ContentPage
{
    private readonly INoteRepository _repository;
    private readonly ISourceModeStore _sourceMode;
    private readonly QuickCaptureDraft _draft;

    /// <summary>
    /// Enter 那一顆。存檔成功是「完成 → 收起 CmdPal」,失敗則改成回上一頁。
    ///
    /// 換的是 <see cref="AnonymousCommand.Result"/> 而不是整個命令物件:那個屬性是
    /// <c>Invoke</c> 當下才讀的,所以在 <see cref="GetContent"/> 裡改一定來得及,
    /// 也不必指望任何跨進程的變更通知。
    /// </summary>
    private readonly AnonymousCommand _done;

    /// <summary>
    /// 複製內文。刻意用 <see cref="CopyNoteBodyCommand"/> 而不是 toolkit 原生
    /// <see cref="CopyTextCommand"/>:後者預設回 ShowToast,而這一頁一個 toast 都不能發
    /// (見上面的型別註解)。實例留著,重新取內容時才能改掉 <c>Text</c>。
    /// </summary>
    private readonly CopyNoteBodyCommand _copyBody;

    /// <summary>
    /// 「顯示原始文字 ↔ 顯示渲染後的預覽」,跟預覽頁、清單頁共用同一個組裝與鍵位。
    /// 這一頁同樣是短命物件,所以不訂閱 <see cref="ISourceModeStore.ShowSourceChanged"/>,
    /// 狀態在 <see cref="GetContent"/> 當下讀。
    /// </summary>
    private readonly SourceModeToggle _toggleSource;

    private Note? _note;
    private string? _error;
    private bool _captured;

    public CapturedNotePage(INoteRepository repository, QuickCaptureDraft draft, ISourceModeStore sourceMode)
    {
        _repository = repository;
        _sourceMode = sourceMode;
        _draft = draft;
        _toggleSource = new SourceModeToggle(sourceMode, Refresh);

        Icon = Icons.Capture;

        // 標題不必等存檔 —— 使用者打的就是它。
        Title = draft.Title;
        Name = Resources.CapturedPageName;

        // 跟預覽頁共用同一份組裝(收起面板,不是 GoHome —— 理由寫在 NoteCommands.Done)。
        // 存檔失敗時這一顆會就地被改成「返回」,所以留著實例,見 Capture()。
        _done = NoteCommands.Done();

        _copyBody = new CopyNoteBodyCommand(draft.Body);

        // 存檔前只有這一顆:其餘幾個都要拿到存好的 Note 才建得出來(檔案路徑、id)。
        // 補齊的時機見 Capture()。
        Commands = [new CommandContextItem(_done)];
    }

    public override IContent[] GetContent()
    {
        Capture();

        // 選單上那一項的字講的是「按下去會看到什麼」,狀態可能是在別的畫面上切的。
        _toggleSource.Sync();

        if (_note is not { } captured)
        {
            // 存檔失敗。原文照樣顯示出來,讓使用者至少能把打過的字複製走,
            // 而不是連同錯誤訊息一起消失。
            return [new MarkdownContent(
                Strings.Format(Resources.CaptureFailedContent, _error, _draft.Title, _draft.Body))];
        }

        // 「重查 → 更新 → 渲染」與預覽頁共用同一份,理由見 NotePreviewContent。
        // 重新查而不是用存檔當下的快照:使用者可能剛從這一頁按 Ctrl+E 編輯完回來。
        var note = captured;
        var content = NotePreviewContent.Reload(
            _repository, note.Id, ref note, _copyBody, _sourceMode.ShowSource);

        _note = note;
        Title = note.Title;
        return [content];
    }

    /// <summary>
    /// 真正寫檔的地方,成功之後就不再跑。
    ///
    /// <see cref="GetContent"/> 不保證只被呼叫一次(編輯完回來、<c>RaiseItemsChanged</c>
    /// 都會再要一次內容),少了這道旗標,同一則想法會被存成好幾個檔案。
    ///
    /// <para><b>但失敗時旗標要放掉,否則「重試」是假的。</b></para>
    ///
    /// 失敗那條路把 Enter 改成「回上一步」,設計意圖是「剛打的那句話還在搜尋框裡,
    /// 退回去就能再試一次」。可是退回去之後查詢字沒變、repository 的 Version 也沒變
    /// (根本沒寫成功),快速記下頁的項目快取因此原封不動地回傳**同一個頁面實例** ——
    /// 旗標還立著,再按一次 Enter 只是把同一則錯誤訊息再畫一遍,連寫檔都沒有嘗試。
    /// 實機驗過:連按三次 Enter,診斷日誌裡只有一筆 Capture 失敗。
    /// 失敗時放掉旗標之後,那條唯一的重試路徑才真的會重試。
    /// </summary>
    private void Capture()
    {
        if (_captured)
        {
            return;
        }

        // 先立起來再寫。順序是刻意的:寫檔中途又被要一次內容時,寧可漏掉一次重試,
        // 也不要把同一則想法存成兩個檔案 —— 漏掉的那次使用者再按一次就有,
        // 多出來的那個檔案使用者得自己去刪。
        _captured = true;

        try
        {
            var note = _repository.Create(_draft.Title, _draft.Body);

            _note = note;
            _copyBody.Text = note.Body;

            // 「完成」帶著記下的確認一起收工 —— 跟關掉「記下後先看一眼」那條路
            // (QuickCaptureCommand)講同一句話,用的也是同一個字串。
            //
            // 這是這一頁**唯一**可以發 toast 的時機:按下去的語意就是「收工」,面板本來
            // 就要關,所以 toast 搶焦點造成的隱藏不是代價而是目的。停留期間為什麼一個都
            // 不能發,見型別註解。
            //
            // ToastArgs.Result 是 CmdPal 顯示完提示之後要做的事,維持 Dismiss ——
            // 不是 GoHome:記完這則想法就要回去做原本的事,留一個主搜尋框在畫面上
            // 只是多一次 Esc(同一個取捨見 NoteCommands.Done)。
            _done.Result = CommandResult.ShowToast(new ToastArgs
            {
                Message = Strings.Format(Resources.CaptureSaved, note.Title),
                Result = CommandResult.Dismiss(),
            });

            // 前一次嘗試失敗過的話,這一顆的字還停在「回上一步」。改回來 ——
            // 底部按鈕寫著「回上一步」卻收掉整個面板,比什麼都不寫更糟。
            _done.Name = Resources.CommandDone;
            _error = null;

            // 這裡才補齊命令列。CmdPal 讀 Commands 的時機比 GetContent 早
            // (ContentPageViewModel.InitializeProperties:先 BuildCommandViewModels,
            // 後 FetchContent),所以只能靠換掉整個陣列發出的 PropChanged 讓它重讀 ——
            // IContentPage 走的是無條件訂閱那條路(IDetails 才是斷的,見 NoteListPage)。
            Commands = BuildCommands(note);

            DiagnosticLog.Write($"CapturedNotePage.Capture: 已存 id={note.Id} 標題='{note.Title}'");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 磁碟滿了、資料夾被移走、OneDrive 鎖住檔案。這一頁沒有 toast 可用,
            // 錯誤就直接畫在頁面上 —— 絕對不能讓使用者以為想法記下來了。
            _error = ex.Message;

            // 放掉旗標,下一次進來才會真的再寫一次檔(理由見方法註解)。
            _captured = false;

            // Enter 改成回快速記下頁:剛打的那句話還在搜尋框裡,可以直接重試。
            _done.Name = Resources.CommandGoBack;
            _done.Result = CommandResult.GoBack();

            DiagnosticLog.Write($"CapturedNotePage.Capture 失敗:{ex}");
        }
    }

    /// <summary>
    /// 存檔成功後的命令列。**前兩項的位置是有語意的,不要插隊**:第一項掛 <c>Enter</c>
    /// (完成,收起面板),第二項掛 <c>Ctrl+Enter</c>(編輯)—— 這一頁是剛打完字回頭看一眼,
    /// 下一步是收工,所以主命令給「完成」。<see cref="NotePreviewPage"/> 的前兩項**剛好相反**
    /// (那一頁是在清單裡找到某一則才進來的),算法與理由見 <see cref="NoteCommands"/>。
    ///
    /// 其餘幾項與預覽頁、清單頁共用同一份組裝(<see cref="NoteCommands"/>),
    /// 鍵位因此自動一致 —— 這一頁是第三個顯示同一則筆記的畫面,手勢要跨頁通用。
    /// </summary>
    private IContextItem[] BuildCommands(Note note) => [
        new CommandContextItem(_done),
        NoteCommands.Edit(_repository, note, Refresh),
        _toggleSource.CreateItem(Resources.ToggleSourcePageSubtitle),
        NoteCommands.CopyBody(_copyBody),
        NoteCommands.OpenInEditor(note),
        NoteCommands.OpenFileLocation(note),
    ];

    /// <summary>
    /// 編輯存檔後由表單呼叫,切換原始文字模式之後也走這裡。
    /// 為什麼一定要主動發這個事件,見 <see cref="NotePreviewContent"/>。
    /// </summary>
    private void Refresh() => RaiseItemsChanged(1);
}
