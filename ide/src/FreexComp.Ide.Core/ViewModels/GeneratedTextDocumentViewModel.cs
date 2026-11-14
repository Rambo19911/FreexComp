using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>
/// A read-only, generated plain-text document that is not a file (architecture.md §5), e.g. the Keyboard Shortcuts
/// document (UI-MENU-030). <see cref="Kind"/> identifies it: showing the same kind again reuses the tab.
/// </summary>
public sealed class GeneratedTextDocumentViewModel : DocumentViewModel
{
    public GeneratedTextDocumentViewModel(string kind, string title, string text)
        : this(kind, title, text, new StringTextBufferFactory())
    {
    }

    /// <param name="buffers">Creates the buffer of <see cref="Content"/> (the editor view binds to it).</param>
    public GeneratedTextDocumentViewModel(string kind, string title, string text, ITextBufferFactory buffers)
    {
        ArgumentNullException.ThrowIfNull(buffers);
        Kind = kind;
        Title = title;
        var buffer = buffers.Create(text, LineEndingKind.CrLf);
        buffer.IsReadOnly = true;
        Content = new TextDocumentViewModel(buffer, null, title, new PlainTextLanguage(), FileEncoding.Utf8, LineEndingKind.CrLf);
    }

    public string Kind { get; }

    public override string Title { get; }

    public override string? FilePath => null;

    public override bool IsModified => false;

    public override bool IsReadOnly => true;

    public override ICommandTarget? CommandTarget => null;

    /// <summary>
    /// The text as a read-only, never-saved text document: its buffer, caret and selection, so the find panel works here
    /// too (I-058, <c>FindService.For(Content)</c>). It is not in the document list.
    /// </summary>
    public TextDocumentViewModel Content { get; }

    public string Text
    {
        get => Content.Buffer.Text;
        internal set
        {
            if (Content.Buffer.Text == value)
                return;
            Content.Reload(value);
            OnPropertyChanged();
        }
    }
}
