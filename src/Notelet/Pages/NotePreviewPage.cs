using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Notelet.Commands;
using Notelet.Core;
using Notelet.Properties;

namespace Notelet.Pages;

/// <summary>
/// 一則筆記的 Markdown 預覽。
///
/// 選單上的命令與鍵位跟清單頁刻意一致(<see cref="NoteCommands"/> 是同一份組裝,
/// 鍵位見 <see cref="Shortcuts"/>):這一頁是從清單頁按 <c>Enter</c> 進來的,
/// 同一則筆記在兩個畫面上要能用同一組手勢。
/// 少的只有「刪除」—— 刪掉正在看的東西沒有道理,而且刪完停在一個空的預覽頁上更奇怪。
/// </summary>
internal sealed partial class NotePreviewPage : ContentPage
{
    private readonly INoteRepository _repository;
    private readonly string _noteId;
    private readonly CopyNoteBodyCommand _copyBody;

    private Note _note;

    public NotePreviewPage(INoteRepository repository, Note note)
    {
        _repository = repository;
        _noteId = note.Id;
        _note = note;

        Icon = Icons.Preview;
        Title = note.Title;
        Name = Resources.CommandPreview;

        _copyBody = new CopyNoteBodyCommand(note.Body);

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
            NoteCommands.OpenInEditor(note),
            NoteCommands.OpenFileLocation(note),
        ];
    }

    public override IContent[] GetContent()
    {
        // 「重查 → 更新 → 渲染」與記下頁共用同一份,理由見 NotePreviewContent。
        var content = NotePreviewContent.Reload(_repository, _noteId, ref _note, _copyBody);

        Title = _note.Title;
        return [content];
    }

    /// <summary>
    /// 編輯存檔後由表單呼叫。為什麼一定要主動發這個事件,見 <see cref="NotePreviewContent"/>。
    /// </summary>
    private void Refresh() => RaiseItemsChanged(1);
}
