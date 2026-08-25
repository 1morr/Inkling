using Microsoft.CommandPalette.Extensions.Toolkit;
using Inkling.Properties;

namespace Inkling.Commands;

/// <summary>
/// 「顯示原始文字 ↔ 顯示渲染後的預覽」那一項，清單頁、預覽頁、記下並預覽頁共用。
///
/// 收在這裡的理由跟 <see cref="NoteCommands"/> 一樣:同一個手勢(<c>Ctrl+U</c>)在三個
/// 畫面上要長得一樣、字也要一樣。而且這一項有一個容易漏的細節 ——
/// <b>選單上的字講的是「按下去之後會看到什麼」</b>，所以狀態一變就得換掉命令的
/// <see cref="Command.Name"/>(<c>CommandItem.Title</c> 沒明著設就會回落到它，
/// 而 <c>ICommandItem</c> 是無條件訂閱那條路，跨進程收得到)。
///
/// 一個實例配一個頁面:命令物件共用沒問題(清單頁本來就讓所有項目共用同一個),
/// 但 <see cref="CreateItem"/> 每次給新的 <see cref="CommandContextItem"/> ——
/// 每一列各自持有一個，跟改成共用同一個物件相比，少一個「CmdPal 拿項目物件當快取鍵」
/// 的未知數。
/// </summary>
internal sealed class SourceModeToggle
{
    private readonly ISourceModeStore _store;
    private readonly AnonymousCommand _command;

    /// <param name="onToggled">
    /// 切換之後要做的事，由頁面自己決定怎麼更新畫面。
    ///
    /// <b>長壽的頁面(清單頁)請傳 null，改去訂閱
    /// <see cref="ISourceModeStore.ShowSourceChanged"/></b> —— 那條路連「別的頁面切的」
    /// 也收得到;短命的頁面(預覽頁、記下並預覽頁)不能訂閱長壽事件(理由見那個事件的說明),
    /// 所以走這個回呼。
    /// </param>
    public SourceModeToggle(ISourceModeStore store, Action? onToggled = null)
    {
        _store = store;

        _command = new AnonymousCommand(() =>
        {
            store.ShowSource = !store.ShowSource;
            onToggled?.Invoke();
        })
        {
            Name = NameFor(store.ShowSource),
            Icon = Icons.Source,

            // 切換完**留在原地**。這一頁(或這一列)正是使用者想看的東西，
            // 預設的 Dismiss 會把整個面板收掉。
            Result = CommandResult.KeepOpen(),
        };
    }

    /// <summary>
    /// 依目前的狀態更新選單上的字。切換之後、以及頁面重新取內容時都要呼叫一次 ——
    /// 短命的頁面不訂閱事件，狀態可能是在別的畫面上被改掉的。
    /// </summary>
    public void Sync() => _command.Name = NameFor(_store.ShowSource);

    /// <summary>
    /// 給一列選單項。<b>刻意不設 <c>Title</c></b>:讓它回落到命令的
    /// <see cref="Command.Name"/>，切換之後選單上的字才會跟著變。
    /// </summary>
    /// <param name="subtitle">
    /// 副標各頁不同 —— 清單頁講的是「不進預覽頁也能看原文」，預覽頁講的是
    /// 「這裡的原文連縮排都不動」，那是兩件不一樣的事。
    /// </param>
    public CommandContextItem CreateItem(string subtitle) => new(_command)
    {
        Subtitle = subtitle,
        RequestedShortcut = Shortcuts.ToggleSource,
    };

    private static string NameFor(bool showSource) =>
        showSource ? Resources.ToggleSourceShowRendered : Resources.ToggleSourceShowRaw;
}
