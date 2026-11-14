using FreexComp.Ide.Core.Commands;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>
/// A read-only, generated plain-text document that is not a file (architecture.md §5), e.g. the Keyboard Shortcuts
/// document (UI-MENU-030). <see cref="Kind"/> identifies it: showing the same kind again reuses the tab.
/// </summary>
public sealed class GeneratedTextDocumentViewModel(string kind, string title, string text) : DocumentViewModel
{
    private string _text = text;

    public string Kind { get; } = kind;

    public override string Title { get; } = title;

    public override string? FilePath => null;

    public override bool IsModified => false;

    public override bool IsReadOnly => true;

    public override ICommandTarget? CommandTarget => null;

    public string Text
    {
        get => _text;
        internal set => SetProperty(ref _text, value);
    }
}
