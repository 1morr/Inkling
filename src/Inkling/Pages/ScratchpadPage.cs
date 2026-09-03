using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Inkling.Commands;
using Inkling.Core;
using Inkling.Properties;

namespace Inkling.Pages;

/// <summary>
/// 隨手草稿:一塊永久的便條紙。打開就是上次留下的東西，不必取標題、不必挑檔案。
///
/// 這是快速記下與正式筆記之外的第三種東西 —— <b>還沒成形、不值得變成一則筆記的想法</b>。
/// 因此它刻意沒有清單、沒有預覽、沒有「另存成筆記」:那些都會逼使用者當場決定
/// 「這到底算不算一則筆記」，而隨手草稿存在的意義正是不必決定。
///
/// 存檔是明著的動作(<c>Tab</c> → <c>Enter</c>)，原因見
/// <see cref="ScratchpadFormContent"/> 上那段「為什麼沒有自動儲存」。
/// </summary>
internal sealed partial class ScratchpadPage : ContentPage
{
    private readonly ScratchpadStore _store;

    public ScratchpadPage(ScratchpadStore store)
    {
        _store = store;

        Id = CommandIds.Scratchpad;
        Icon = Icons.Scratchpad;
        Title = Resources.ScratchpadPageTitle;
        Name = Resources.CommandOpen;

        // **只有一顆命令，而且刻意不加守衛。** 編輯頁的 Commands[0] 是一顆什麼都不做的
        // 「繼續編輯」，用來擋掉誤按的 Enter;這一頁不需要 —— 卡片上只有一個**多行**
        // 文字框，而 2026-09-03 的量測證實位置鍵(Enter / Ctrl+Enter)在多行輸入框裡
        // 會被輸入框吃掉，鍵盤永遠打不到底部工具列。加一顆無害命令在這裡只會是
        // 一顆按不到也不做事的按鈕。滑鼠仍然點得到這一顆，但它做的就是字面上那件事。
        // 量測表與機制見 docs/design-notes.md〈位置鍵打不打得到工具列〉。
        //
        // Commands[0] **不能**是「儲存」，不管多想這麼做:底部工具列的按鈕走的是
        // ICommand.Invoke()，沒有參數，拿不到使用者剛打的字 —— 存檔只有卡片裡那顆
        // Action.Submit 一條路(見 ScratchpadFormContent)。放上去只會是一顆假按鈕。
        //
        // 「捨棄變更」2026-09-03 移除:`Esc` 就是不存離開(退出去再進來，上次**存下**的
        // 內容原封不動)，再按一次 `Esc` 就收面板 —— 跟那一顆完全等價。同一件事不留兩個出口。
        Commands = [NoteCommands.OpenInEditor(_store.FilePath, dismiss: true)];
    }

    public override IContent[] GetContent()
    {
        // 讓檔案先存在:Ctrl+O 拿的是一個固定路徑，而使用者可能一個字都還沒存過。
        // 已經有內容的話這一步什麼都不做。
        _store.EnsureFile();

        // **每次都建一張新卡片。** 卡片的值是建構時烤進 DataJson 的，而 CmdPal 導覽進
        // 這一頁就一定會呼叫 GetContent() —— 「打開就接著上次寫」靠的就是這一步。
        // (同一個形狀見 InklingSettingsPage。)
        return [new ScratchpadFormContent(_store)];
    }
}
