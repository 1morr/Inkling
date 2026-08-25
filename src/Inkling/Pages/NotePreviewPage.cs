using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Inkling.Commands;
using Inkling.Core;
using Inkling.Properties;

namespace Inkling.Pages;

/// <summary>
/// 一則筆記的預覽:渲染後的 Markdown，或原始文字(<c>Ctrl+U</c> 切換)。
///
/// 選單上的命令與鍵位跟清單頁刻意一致(<see cref="NoteCommands"/> 是同一份組裝，
/// 鍵位見 <see cref="Shortcuts"/>):這一頁是從清單頁按 <c>Enter</c> 進來的，
/// 同一則筆記在兩個畫面上要能用同一組手勢。
/// 少的只有「刪除」—— 刪掉正在看的東西沒有道理，而且刪完停在一個空的預覽頁上更奇怪。
///
/// <para><b>兩個位置鍵:<c>Enter</c> 是「編輯」,<c>Ctrl+Enter</c> 是「完成」。</b></para>
///
/// 那兩顆按鈕坐的是誰只看順序，不看命令自己綁的鍵(算法見 <see cref="NoteCommands"/>)。
/// **這一頁跟記下並預覽頁剛好對調，是刻意的** —— 兩頁的動線不一樣:
/// 這一頁是使用者在清單裡**找到了某一則**才進來的，下一步多半是改它;記下並預覽頁是
/// 剛打完字回頭看一眼，下一步是收工。所以主命令各自給那條動線，另一個讓給 <c>Ctrl+Enter</c>。
///
/// 曾經兩頁都是「<c>Enter</c> 完成 / <c>Ctrl+Enter</c> 編輯」，為的是讓 <c>Ctrl+Enter</c>
/// 三頁同義;代價是在這一頁按 <c>Enter</c> 會把面板整個收掉，而使用者剛剛才在清單裡搜到它。
/// 那個代價比「<c>Ctrl+Enter</c> 得記兩套」大，所以換回來 ——
/// 更早之前的那一版是 <c>Ctrl+Enter</c> 順位落在複製內文上，那才是真的沒道理的形狀，
/// 現在它坐的是「完成」，兩顆按鈕仍然是同一組動作的兩個入口。
/// 編輯還是三條路都到得了(<c>Enter</c> / <c>Ctrl+E</c> / 選單),
/// 複製內文一律走自己的 <c>Ctrl+Shift+C</c>。
/// </summary>
internal sealed partial class NotePreviewPage : ContentPage
{
    private readonly INoteRepository _repository;
    private readonly ISourceModeStore _sourceMode;
    /// <summary>
    /// 這一則的檔案路徑。**身分認路徑不認 id** —— 同一個 id 可能對到兩個檔案
    /// (雲端硬碟的衝突副本)，見 <see cref="Note.Id"/>。留路徑而不是留整個 Note,
    /// 是因為 <c>_note</c> 每次重新取內容都會被換掉，而定位用的鍵必須固定。
    /// </summary>
    private readonly string _filePath;
    private readonly CopyNoteBodyCommand _copyBody;

    /// <summary>
    /// 「顯示原始文字 ↔ 顯示渲染後的預覽」。跟清單頁共用同一個組裝與鍵位，
    /// 狀態則是全域的(見 <see cref="ISourceModeStore"/>)。
    /// </summary>
    private readonly SourceModeToggle _toggleSource;

    private Note _note;

    public NotePreviewPage(INoteRepository repository, Note note, ISourceModeStore sourceMode)
    {
        _repository = repository;
        _sourceMode = sourceMode;
        _filePath = note.FilePath;
        _note = note;

        Icon = Icons.Preview;
        Title = note.Title;
        Name = Resources.CommandPreview;

        _copyBody = new CopyNoteBodyCommand(note.Body, note.Title);

        // 切換之後自己重新取一次內容。**這裡刻意不去訂閱 ShowSourceChanged** ——
        // 預覽頁是清單裡每個項目各建一個的短命物件，訂閱長壽事件會一路累積死掉的訂閱者
        // (跟下面不訂閱 repository.Changed 是同一個理由)。別的頁面切掉狀態的情況
        // 靠 GetContent 每次重讀來收(導覽過去一定會取內容)。
        _toggleSource = new SourceModeToggle(_sourceMode, Refresh);

        // **前兩項的位置是有語意的，不要插隊**:第一項掛 Enter、第二項掛 Ctrl+Enter
        // (見類別註解與 NoteCommands)。第三項之後才是純選單。
        Commands = [
            // 編輯頁存檔後會回呼 Refresh。
            //
            // 這裡刻意用回呼而不是訂閱 repository 的 Changed 事件:預覽頁是清單裡
            // 每個項目各建一個的，而清單每次搜索就重建一次。長壽事件抓著這些短命物件
            // 會一路累積訂閱，不只是記憶體洩漏，一次改動還會打出上百個刷新。
            NoteCommands.Edit(repository, note, Refresh),

            new CommandContextItem(NoteCommands.Done()),

            _toggleSource.CreateItem(Resources.ToggleSourcePageSubtitle),

            // 複製完留在這一頁，並發一則帶標題的 toast。**這裡以前是完全靜默的**,
            // 理由寫著「使用者正看著的就是剛複製走的那段內容」—— 但頁面顯示什麼跟剪貼簿
            // 有沒有寫成功無關，按下去畫面一個像素都不變。真正的成因是當時以為
            // 「發 toast 就會把面板關掉」，見 CopyNoteBodyCommand 的型別註解。
            NoteCommands.CopyBody(_copyBody),
            NoteCommands.OpenInEditor(note),
            NoteCommands.OpenFileLocation(note),
        ];
    }

    public override IContent[] GetContent()
    {
        var showSource = _sourceMode.ShowSource;

        // 選單上那一項的字講的是「按下去會看到什麼」，所以每次取內容都對一次狀態 ——
        // 這個實例可能是在原始文字模式打開之前就建好的。
        _toggleSource.Sync();

        // 「重查 → 更新 → 渲染」與記下頁共用同一份，理由見 NotePreviewContent。
        var content = NotePreviewContent.Reload(_repository, _filePath, ref _note, _copyBody, showSource);

        Title = _note.Title;
        return [content];
    }

    /// <summary>
    /// 編輯存檔後由表單呼叫，切換原始文字模式之後也走這裡。
    /// 為什麼一定要主動發這個事件，見 <see cref="NotePreviewContent"/>。
    /// </summary>
    private void Refresh() => RaiseItemsChanged(1);
}
