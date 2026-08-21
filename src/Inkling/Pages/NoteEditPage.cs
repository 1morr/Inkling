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
    /// 導覽回上一頁時 CmdPal 不會自動重新拿,沒有這個回呼畫面會停在存檔前的樣子。
    /// </param>
    public NoteEditPage(INoteRepository repository, Note note, Action? onSaved = null)
    {
        _repository = repository;
        _note = note;
        _onSaved = onSaved;

        Icon = Icons.Edit;
        Title = Strings.Format(Resources.EditPageTitle, note.Title);
        Name = Resources.CommandEdit;

        Commands = [
            // 走 OpenNoteFileCommand 而不是 toolkit 的 OpenUrlCommand,三件事:
            //
            // 1. **dismiss 是必要的,不是偏好。** 這一頁跟隨手草稿一樣,畫面上有一份
            //    使用者還能按儲存的副本 —— 卡片的值是 GetContent() 當下烤進 DataJson 的。
            //    面板留著的話,從外部編輯器改完回到 CmdPal 再按一次儲存,就把外部的修改
            //    整個蓋掉(CmdPal 不會因為視窗重新出現就重新 GetContent)。收起來之後
            //    下次進來才會重讀檔案。理由與形狀見 NoteCommands.OpenInEditor。
            // 2. 開不起來時會說話,而且**失敗時不收面板** —— 收掉的話那則訊息會跟著消失。
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

                // 這一頁只有一個命令,所以它同時是底部工具列的主命令 —— **Enter 就是它**
                // (那兩顆按鈕坐的是誰只看順序,見 NoteCommands)。焦點在單行的標題欄時
                // 按 Enter 是很自然的「送出」手勢,結果卻是跳去外部編輯器並收掉面板,
                // 卡片上未儲存的修改跟著消失(實機驗過)。Enter 本身收不回來,
                // 至少把 Ctrl+O 補上:跨頁同一個鍵做同一件事,而且副標講明了代價。
                RequestedShortcut = Shortcuts.OpenExternal,
            },
        ];
    }

    public override IContent[] GetContent()
    {
        // 每次進頁面都重新查一次,拿到的才是最新內容(可能剛從別台機器同步下來)。
        var current = _repository.GetById(_note.Id) ?? _note;

        return [new NoteFormContent(_repository, current, _onSaved)];
    }
}
