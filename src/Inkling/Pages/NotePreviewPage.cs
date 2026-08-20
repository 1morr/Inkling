using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Inkling.Commands;
using Inkling.Core;
using Inkling.Properties;

namespace Inkling.Pages;

/// <summary>
/// 一則筆記的預覽:渲染後的 Markdown,或原始文字(<c>Ctrl+U</c> 切換)。
///
/// 選單上的命令與鍵位跟清單頁刻意一致(<see cref="NoteCommands"/> 是同一份組裝,
/// 鍵位見 <see cref="Shortcuts"/>):這一頁是從清單頁按 <c>Enter</c> 進來的,
/// 同一則筆記在兩個畫面上要能用同一組手勢。
/// 少的只有「刪除」—— 刪掉正在看的東西沒有道理,而且刪完停在一個空的預覽頁上更奇怪。
///
/// <para><b>兩個位置鍵:<c>Enter</c> 是「完成」,<c>Ctrl+Enter</c> 是「編輯」。</b></para>
///
/// 那兩顆按鈕坐的是誰只看順序,不看命令自己綁的鍵(算法見 <see cref="NoteCommands"/>)。
/// 這一頁的 <c>Enter</c> 曾經是編輯,而 <c>Ctrl+Enter</c> 就順位落在複製內文上 ——
/// 於是同一個 <c>Ctrl+Enter</c> 在清單頁是編輯、在這一頁是複製,使用者得記兩套。
/// 現在三個畫面**一律** <c>Ctrl+Enter</c> = 編輯,代價是這一頁的 <c>Enter</c> 讓給
/// <see cref="NoteCommands.Done"/>(收起面板,跟記下並預覽頁同形):看完了就收工,
/// 要改的話 <c>Ctrl+E</c> 或 <c>Ctrl+Enter</c> 都到得了。
/// 複製內文因此只剩自己的 <c>Ctrl+Shift+C</c> —— 那個鍵本來就跨三頁一致。
/// </summary>
internal sealed partial class NotePreviewPage : ContentPage
{
    private readonly INoteRepository _repository;
    private readonly ISourceModeStore _sourceMode;
    private readonly string _noteId;
    private readonly CopyNoteBodyCommand _copyBody;

    /// <summary>
    /// 「顯示原始文字 ↔ 顯示渲染後的預覽」。跟清單頁共用同一個組裝與鍵位,
    /// 狀態則是全域的(見 <see cref="ISourceModeStore"/>)。
    /// </summary>
    private readonly SourceModeToggle _toggleSource;

    private Note _note;

    public NotePreviewPage(INoteRepository repository, Note note, ISourceModeStore sourceMode)
    {
        _repository = repository;
        _sourceMode = sourceMode;
        _noteId = note.Id;
        _note = note;

        Icon = Icons.Preview;
        Title = note.Title;
        Name = Resources.CommandPreview;

        _copyBody = new CopyNoteBodyCommand(note.Body);

        // 切換之後自己重新取一次內容。**這裡刻意不去訂閱 ShowSourceChanged** ——
        // 預覽頁是清單裡每個項目各建一個的短命物件,訂閱長壽事件會一路累積死掉的訂閱者
        // (跟下面不訂閱 repository.Changed 是同一個理由)。別的頁面切掉狀態的情況
        // 靠 GetContent 每次重讀來收(導覽過去一定會取內容)。
        _toggleSource = new SourceModeToggle(_sourceMode, Refresh);

        // **前兩項的位置是有語意的,不要插隊**:第一項掛 Enter、第二項掛 Ctrl+Enter
        // (見類別註解與 NoteCommands)。第三項之後才是純選單。
        Commands = [
            new CommandContextItem(NoteCommands.Done()),

            // 編輯頁存檔後會回呼 Refresh。
            //
            // 這裡刻意用回呼而不是訂閱 repository 的 Changed 事件:預覽頁是清單裡
            // 每個項目各建一個的,而清單每次搜索就重建一次。長壽事件抓著這些短命物件
            // 會一路累積訂閱,不只是記憶體洩漏,一次改動還會打出上百個刷新。
            NoteCommands.Edit(repository, note, Refresh),

            _toggleSource.CreateItem(Resources.ToggleSourcePageSubtitle),

            // 複製完留在這一頁,而且沒有回饋 —— 這一頁沒有清單列可以掛標籤
            // (清單頁的做法見 NoteListPage.FlashTag),而 toast 會把整個面板關掉。
            // 可以接受:使用者正看著的就是剛複製走的那段內容。
            NoteCommands.CopyBody(_copyBody),
            NoteCommands.OpenInEditor(note),
            NoteCommands.OpenFileLocation(note),
        ];
    }

    public override IContent[] GetContent()
    {
        var showSource = _sourceMode.ShowSource;

        // 選單上那一項的字講的是「按下去會看到什麼」,所以每次取內容都對一次狀態 ——
        // 這個實例可能是在原始文字模式打開之前就建好的。
        _toggleSource.Sync();

        // 「重查 → 更新 → 渲染」與記下頁共用同一份,理由見 NotePreviewContent。
        var content = NotePreviewContent.Reload(_repository, _noteId, ref _note, _copyBody, showSource);

        Title = _note.Title;
        return [content];
    }

    /// <summary>
    /// 編輯存檔後由表單呼叫,切換原始文字模式之後也走這裡。
    /// 為什麼一定要主動發這個事件,見 <see cref="NotePreviewContent"/>。
    /// </summary>
    private void Refresh() => RaiseItemsChanged(1);
}
