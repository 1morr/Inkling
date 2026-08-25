using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Inkling.Commands;
using Inkling.Core;
using Inkling.Properties;

namespace Inkling.Pages;

/// <summary>
/// 編輯既有筆記的表單頁。
/// </summary>
internal sealed partial class NoteEditPage : ContentPage
{
    private readonly INoteRepository _repository;
    private readonly Note _note;
    private readonly Action? _onSaved;

    /// <param name="onSaved">
    /// 存檔成功後的回呼。呼叫端(預覽頁)用它讓自己重新取一次內容 ——
    /// 導覽回上一頁時 CmdPal 不會自動重新拿，沒有這個回呼畫面會停在存檔前的樣子。
    /// </param>
    public NoteEditPage(INoteRepository repository, Note note, Action? onSaved = null)
    {
        _repository = repository;
        _note = note;
        _onSaved = onSaved;

        Icon = Icons.Edit;
        Title = Strings.Format(Resources.EditPageTitle, note.Title);
        Name = Resources.CommandEdit;

        // **Commands[0] 坐 Enter,Commands[1] 坐 Ctrl+Enter**(見 NoteCommands),
        // 而這一頁的 Enter 特別危險:焦點在單行的標題欄時按 Enter 是很自然的「送出」手勢，
        // 而卡片上壓著使用者還沒儲存的修改。
        //
        // 這裡曾經只掛一個「在預設編輯器開啟」——於是 Enter 就是它:跳去外部編輯器、
        // 面板被 Dismiss 收掉，打過的字全部消失(實機驗過)。當時的結論寫著
        // 「Enter 本身收不回來」,**那句話是錯的**:同一個 repo 裡 ScratchpadPage 就把無害的
        // 「捨棄變更」放在 Commands[0]、把跳外部推到 Commands[1],NewNotePage 與
        // InklingSettingsPage 更是根本不設 Commands。Commands[0] 一直都是可控的。
        //
        // 所以現在第一顆是一顆什麼都不做的「繼續編輯」:誤按 Enter 的代價變成零。
        // 它**不能**是「儲存」—— 底部工具列走的是無參數的 ICommand.Invoke(),
        // 拿不到使用者剛打的字(同一件事 ScratchpadPage 已經記過)，放上去只會是假按鈕。
        // 真正的儲存只有卡片裡那顆 Action.Submit 一條路。
        Commands = [
            new CommandContextItem(KeepEditing())
            {
                Title = Resources.EditKeepEditingTitle,
                Subtitle = Resources.EditKeepEditingSubtitle,
                Icon = Icons.Edit,
            },

            // 走 OpenNoteFileCommand 而不是 toolkit 的 OpenUrlCommand，三件事:
            //
            // 1. **dismiss 是必要的，不是偏好。** 這一頁跟隨手草稿一樣，畫面上有一份
            //    使用者還能按儲存的副本 —— 卡片的值是 GetContent() 當下烤進 DataJson 的。
            //    面板留著的話，從外部編輯器改完回到 CmdPal 再按一次儲存，就把外部的修改
            //    整個蓋掉(CmdPal 不會因為視窗重新出現就重新 GetContent)。收起來之後
            //    下次進來才會重讀檔案。理由與形狀見 NoteCommands.OpenInEditor。
            // 2. 開不起來時會說話，而且**失敗時不收面板** —— 收掉的話那則訊息會跟著消失。
            // 3. Name 要自己給:底部工具列顯示的是命令的 Name(不是這裡的 Title),
            //    而 toolkit 的預設是它自己資源檔的 "Open" —— 實機截圖抓到過那顆英文按鈕。
            new CommandContextItem(new OpenNoteFileCommand(note.FilePath, dismissOnSuccess: true)
            {
                Name = Resources.EditOpenExternalTitle,
            })
            {
                Title = Resources.EditOpenExternalTitle,
                Subtitle = Resources.EditOpenExternalSubtitle,
                Icon = Icons.OpenExternal,

                // 排第二 = Ctrl+Enter，跟另外三頁的「再進一步編輯」同一個鍵位。
                // Ctrl+O 照樣綁著:跨頁同一個鍵做同一件事。
                RequestedShortcut = Shortcuts.OpenExternal,
            },
        ];
    }

    /// <summary>
    /// 底部工具列第一顆:什麼都不做，面板留著。
    ///
    /// 存在的唯一理由是把 <c>Enter</c> 從「跳去外部編輯器並收掉面板」那條路上擋開
    /// (見建構子)。名字要讀得像「按了不會發生事」——「繼續編輯」正是使用者按下去之後
    /// 看到的結果。
    /// </summary>
    private static AnonymousCommand KeepEditing() => new(() => { })
    {
        Name = Resources.EditKeepEditingTitle,
        Icon = Icons.Edit,
        Result = CommandResult.KeepOpen(),
    };

    public override IContent[] GetContent()
    {
        // 每次進頁面都重新查一次，拿到的才是最新內容(可能剛從別台機器同步下來)。
        // 認路徑不認 id —— 同一個 id 可能對到兩個檔案，見 Note.Id。
        var current = _repository.GetByPath(_note.FilePath) ?? _note;

        return [new NoteFormContent(_repository, current, _onSaved)];
    }
}
