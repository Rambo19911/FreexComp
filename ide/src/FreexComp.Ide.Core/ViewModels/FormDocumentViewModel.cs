using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>The views of a form document (designer.md §1, designer README §3).</summary>
public enum DocumentView { Code, Design, Text }

/// <summary>
/// A unit with a companion <c>.form</c> (architecture.md §5, main-window.md UI-WIN-020): one document, one tab, three
/// views. The Code view edits the unit (this document's own buffer), the Text view shows the <c>.form</c> text
/// (<see cref="FormText"/>), the Design view hosts the designer. Switching rules beyond selecting a view (parsing the
/// text, refusing on syntax errors, reconciliation) and saving the form come with the form designer view-model.
/// </summary>
public sealed class FormDocumentViewModel : TextDocumentViewModel
{
    private DocumentView _activeView;

    public FormDocumentViewModel(ITextBuffer buffer, string filePath, ILanguageDefinition language, FileEncoding encoding,
        LineEndingKind defaultLineEnding, TextDocumentViewModel formText)
        : base(buffer, filePath, null, language, encoding, defaultLineEnding)
    {
        ArgumentNullException.ThrowIfNull(formText);
        FormText = formText;
    }

    /// <summary>The <c>.form</c> text as a document of its own (not in the document list): what the Text view edits.</summary>
    public TextDocumentViewModel FormText { get; }

    /// <summary>The text buffer of the <c>.form</c> file (architecture.md §5 <c>FormBuffer</c>).</summary>
    public ITextBuffer FormBuffer => FormText.Buffer;

    /// <summary>The shown view; a new document shows its Code view.</summary>
    public DocumentView ActiveView
    {
        get => _activeView;
        private set
        {
            if (SetProperty(ref _activeView, value))
            {
                OnPropertyChanged(nameof(CommandTarget));
                ActiveViewChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>Raised after <see cref="ActiveView"/> changed (the document service re-evaluates command states).</summary>
    public event EventHandler? ActiveViewChanged;

    /// <summary>Editing commands go to the text the active view shows; the Design view has its own targets.</summary>
    public override ICommandTarget? CommandTarget => ActiveView switch
    {
        DocumentView.Code => this,
        DocumentView.Text => FormText,
        _ => null,
    };

    /// <summary>A tab of the view switcher (UI-DSG-001).</summary>
    public void ShowView(DocumentView view) => ActiveView = view;

    /// <summary><c>view.toggleFormCode</c> (F7): Code ↔ Design; from the Text view to the Design view.</summary>
    public void ToggleFormCode() => ActiveView = ActiveView == DocumentView.Design ? DocumentView.Code : DocumentView.Design;

    /// <summary><c>view.formAsText</c>.</summary>
    public void ShowFormAsText() => ActiveView = DocumentView.Text;
}
