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

        Commands = [
            // 編輯頁存檔後會回呼 Refresh。
            //
            // 這裡刻意用回呼而不是訂閱 repository 的 Changed 事件:預覽頁是清單裡
            // 每個項目各建一個的,而清單每次搜索就重建一次。長壽事件抓著這些短命物件
            // 會一路累積訂閱,不只是記憶體洩漏,一次改動還會打出上百個刷新。
            NoteCommands.Edit(repository, note, Refresh),
            // 複製完留在這一頁,而且沒有回饋 —— 這一頁沒有清單列可以掛標籤
            // (清單頁的做法見 NoteListPage.FlashTag),而 toast 會把整個面板關掉。
            // 可以接受:使用者正看著的就是剛複製走的那段內容。
            NoteCommands.CopyBody(_copyBody),

            // **切換原始文字排在複製後面,跟清單頁的選單順序不一樣,是刻意的。**
            // 這一頁是 ContentPage:第一個命令是底部工具列的主按鈕,**第二個掛 Ctrl+Enter**
            // (清單頁那邊的第一個才是 Ctrl+Enter)。排到第二個就等於把複製內文從
            // Ctrl+Enter 上擠掉 —— 那是使用者早就在用的鍵位,而這一項本來就有 Ctrl+U。
            _toggleSource.CreateItem(Resources.ToggleSourcePageSubtitle),
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
