using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Inkling.Commands;
using Inkling.Core;
using Inkling.Properties;

namespace Inkling.Pages;

/// <summary>
/// 隨手草稿:一塊永久的便條紙。打開就是上次留下的東西,不必取標題、不必挑檔案。
///
/// 這是快速記下與正式筆記之外的第三種東西 —— <b>還沒成形、不值得變成一則筆記的想法</b>。
/// 因此它刻意沒有清單、沒有預覽、沒有「另存成筆記」:那些都會逼使用者當場決定
/// 「這到底算不算一則筆記」,而隨手草稿存在的意義正是不必決定。
///
/// 存檔是明著的動作(<c>Tab</c> → <c>Enter</c>),原因見
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

        // **前兩項的位置有語意**(見 NoteCommands):Commands[0] 坐 Enter、Commands[1] 坐
        // Ctrl+Enter,跟命令自己的 RequestedShortcut 無關。
        //
        // 這一頁沒有「編輯」可放 —— 它本身就是編輯狀態。Ctrl+Enter 因此給「跳到外部編輯器」,
        // 那正是這一頁語意上的「再進一步編輯」,跟另外兩個 ContentPage 對得起來。
        // **不要在前兩個位置插新東西。**
        //
        // Commands[0] 也**不能**是「儲存」,不管多想這麼做:底部工具列的按鈕走的是
        // ICommand.Invoke(),沒有參數,拿不到使用者剛打的字 —— 存檔只有卡片裡那顆
        // Action.Submit 一條路(見 ScratchpadFormContent)。放上去只會是一顆假按鈕。
        Commands = [
            new CommandContextItem(Discard()),
            NoteCommands.OpenInEditor(_store.FilePath, dismiss: true),
        ];
    }

    /// <summary>
    /// 「捨棄變更」:收起面板,這一趟打的字不存。
    ///
    /// 沒有沿用 <see cref="NoteCommands.Done"/> 的「完成」,雖然行為完全一樣 ——
    /// 那個字在別的頁面上是「看完了,收工」,在這一頁會被讀成「存檔並結束」,
    /// 而它一個字都不會存。**存檔成功本來就會自己關掉面板**(見
    /// <see cref="ScratchpadFormContent.SubmitForm"/>),所以還會走到這一顆的,
    /// 就只有「不想存」那一種情形,名字要照那個講。
    ///
    /// 講「變更」而不是「草稿」是刻意的:被丟掉的是**這一次的編輯**,
    /// 不是已經存在檔案裡的草稿 —— 那份東西誰都不會動它。
    ///
    /// 實務上很難誤按:焦點在文字框裡時 <c>Enter</c> 是換行,碰不到底部工具列,
    /// 而 <c>Tab</c> 的第一站是「儲存」。
    /// </summary>
    private static AnonymousCommand Discard() => new(() => { })
    {
        Name = Resources.ScratchpadDiscard,
        Icon = Icons.Discard,

        // 帶一句話再收工。這一顆最容易被誤讀成「存檔並結束」,而它一個字都不會存 ——
        // 名字只在按下去**之前**看得到,真正需要確認的是按下去**之後**「剛才那些字沒進檔案」。
        //
        // 訊息刻意只有一行:曾經在後面接「—— 存著的草稿沒有動」,實機看過就知道太長,
        // toast 是一瞥的東西。要保住的區分由「這次的」帶(丟掉的是這一次的編輯,
        // 不是檔案裡的草稿)。
        //
        // 面板本來就要關,所以下面明著回 `Dismiss()`,而 toast 在面板收掉之後還留得住
        // (同一個判斷見 ScratchpadFormContent 的存檔路徑與 CapturedNotePage 的「完成」)。
        Result = CommandResult.ShowToast(new ToastArgs
        {
            Message = Resources.ScratchpadDiscarded,
            Result = CommandResult.Dismiss(),
        }),
    };

    public override IContent[] GetContent()
    {
        // 讓檔案先存在:Ctrl+O 拿的是一個固定路徑,而使用者可能一個字都還沒存過。
        // 已經有內容的話這一步什麼都不做。
        _store.EnsureFile();

        // **每次都建一張新卡片。** 卡片的值是建構時烤進 DataJson 的,而 CmdPal 導覽進
        // 這一頁就一定會呼叫 GetContent() —— 「打開就接著上次寫」靠的就是這一步。
        // (同一個形狀見 InklingSettingsPage。)
        return [new ScratchpadFormContent(_store)];
    }
}
