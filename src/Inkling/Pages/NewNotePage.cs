using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Inkling.Core;
using Inkling.Properties;

namespace Inkling.Pages;

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
