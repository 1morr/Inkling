using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Notelet.Core;
using Notelet.Properties;

namespace Notelet.Pages;

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
            new CommandContextItem(new OpenUrlCommand(note.FilePath))
            {
                Title = Resources.EditOpenExternalTitle,
                Subtitle = Resources.EditOpenExternalSubtitle,
                Icon = Icons.OpenExternal,
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

/// <summary>
/// 新增筆記的表單頁。快速新增只能記一行字,這裡給的是「一開始就想寫長一點」的入口。
/// </summary>
internal sealed partial class NewNotePage : ContentPage
{
    private readonly INoteRepository _repository;

    public NewNotePage(INoteRepository repository)
    {
        _repository = repository;

        Id = CommandIds.NewNote;
        Icon = Icons.Add;
        Title = Resources.NewNotePageTitle;
        Name = Resources.CommandNew;
    }

    public override IContent[] GetContent() => [new NoteFormContent(_repository, null)];
}
