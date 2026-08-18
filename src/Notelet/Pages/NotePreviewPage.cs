using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Notelet.Commands;
using Notelet.Core;
using Notelet.Properties;

namespace Notelet.Pages;

/// <summary>
/// 一則筆記的 Markdown 預覽。
///
/// 選單上的命令與鍵位跟清單頁刻意一致(見 <see cref="Shortcuts"/>):這一頁是從清單頁按
/// <c>Enter</c> 進來的,同一則筆記在兩個畫面上要能用同一組手勢。
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
            new CommandContextItem(new NoteEditPage(repository, note, Refresh))
            {
                Title = Resources.CommandEdit,
                Icon = Icons.Edit,
                RequestedShortcut = Shortcuts.Edit,
            },
            // 複製完留在這一頁,而且沒有回饋 —— 這一頁沒有清單列可以掛標籤
            // (清單頁的做法見 NoteListPage.FlashTag),而 toast 會把整個面板關掉。
            // 可以接受:使用者正看著的就是剛複製走的那段內容。
            new CommandContextItem(_copyBody)
            {
                Title = Resources.CommandCopyBody,
                Subtitle = Resources.CommandCopyBodySubtitle,
                Icon = Icons.Copy,
                RequestedShortcut = Shortcuts.CopyBody,
            },
            new CommandContextItem(new OpenUrlCommand(note.FilePath))
            {
                Title = Resources.CommandOpenInEditor,
                Icon = Icons.OpenExternal,
                RequestedShortcut = Shortcuts.OpenExternal,
            },
            new CommandContextItem(new ShowFileInFolderCommand(note.FilePath)
            {
                // 換掉 toolkit 自己資源檔裡的名字,理由見 NoteListPage 同一處。
                Name = Resources.CommandOpenFileLocation,
            })
            {
                Title = Resources.CommandOpenFileLocation,
                Subtitle = Resources.CommandOpenFileLocationSubtitle,
                Icon = Icons.FileLocation,
                RequestedShortcut = Shortcuts.OpenFileLocation,
            },
        ];
    }

    public override IContent[] GetContent()
    {
        // 重新查一次而不是直接用建構時的快照:使用者可能剛編輯完,
        // 或是別台機器的改動剛同步下來。查不到就沿用上一次的,至少還看得到東西。
        _note = _repository.GetById(_noteId) ?? _note;

        Title = _note.Title;
        _copyBody.Text = _note.Body;

        // 渲染規則(補標題、單換行變硬換行、避開程式碼區塊與表格)全在 Core,
        // 那一層有測試涵蓋;這裡只負責把字串交給 CmdPal。
        return [new MarkdownContent(NotePreview.Render(_note))];
    }

    /// <summary>
    /// 編輯存檔後由表單呼叫。CmdPal 不會因為導覽回來就重新取內容,
    /// 一定要主動發這個事件,否則畫面會停在存檔前的樣子。
    /// </summary>
    private void Refresh() => RaiseItemsChanged(1);
}
