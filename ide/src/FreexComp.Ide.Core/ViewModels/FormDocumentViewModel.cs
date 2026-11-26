using FreexComp.Designer.Catalog;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>The views of a form document (designer.md §1, designer README §3).</summary>
public enum DocumentView { Code, Design, Text }

/// <summary>
/// A unit with a companion <c>.form</c> (architecture.md §5, main-window.md UI-WIN-020): one document, one tab, three
/// views. The Code view edits the unit (this document's own buffer), the Text view edits the <c>.form</c> text
/// (<see cref="FormText"/>), the Design view hosts the designer. The switching rules, the form model and the form's
/// modified flag are the <see cref="Designer"/>'s (D-072); the document is modified when either buffer is.
/// </summary>
public sealed class FormDocumentViewModel : TextDocumentViewModel
{
    private readonly FormDesignerViewModel? _designer;

    public FormDocumentViewModel(ITextBuffer buffer, string filePath, ILanguageDefinition language, FileEncoding encoding,
        LineEndingKind defaultLineEnding, TextDocumentViewModel formText, ComponentCatalog catalog, bool hasUnit = true,
        FreexComp.Designer.Model.IFrameCatalog? frames = null, FreexComp.Designer.Model.IFrameCatalog? ancestors = null)
        : base(buffer, filePath, null, language, encoding, defaultLineEnding)
    {
        ArgumentNullException.ThrowIfNull(formText);
        FormText = formText;
        _designer = new FormDesignerViewModel(this, formText, catalog, hasUnit, frames, ancestors);
        _designer.ActiveViewChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ActiveView));
            OnPropertyChanged(nameof(CommandTarget));
            ActiveViewChanged?.Invoke(this, EventArgs.Empty);
        };
        _designer.ModifiedChanged += (_, _) => OnPropertyChanged(nameof(IsModified));
    }

    /// <summary>False: a <c>.form</c> opened without its unit (DSG-CS-080); this document's own buffer is an empty placeholder.</summary>
    public bool HasUnit => Designer.HasUnit;

    /// <summary>The <c>.form</c> text as a document of its own (not in the document list): what the Text view edits.</summary>
    public TextDocumentViewModel FormText { get; }

    /// <summary>The text buffer of the <c>.form</c> file (architecture.md §5 <c>FormBuffer</c>).</summary>
    public ITextBuffer FormBuffer => FormText.Buffer;

    /// <summary>The form designer side: model, undo stack, code sync, view switching (D-072).</summary>
    public FormDesignerViewModel Designer => _designer!;

    /// <summary>The shown view; a document opened from its unit shows its Code view.</summary>
    public DocumentView ActiveView => Designer.ActiveView;

    /// <summary>Raised after <see cref="ActiveView"/> changed (the document service re-evaluates command states).</summary>
    public event EventHandler? ActiveViewChanged;

    /// <summary>The unit or the form buffer is modified (designer README §3).</summary>
    public override bool IsModified => base.IsModified || _designer is { IsFormModified: true };

    /// <summary>The unit buffer alone is modified (the <c>.pas</c> needs writing).</summary>
    public bool IsUnitModified => base.IsModified;

    /// <summary>Editing commands go to the text the active view shows; the Design view has its own targets.</summary>
    public override ICommandTarget? CommandTarget => ActiveView switch
    {
        DocumentView.Code => this,
        DocumentView.Text => FormText,
        _ => null,
    };

    /// <summary>A tab of the view switcher (UI-DSG-001); false when the switch was refused (form text syntax error).</summary>
    public bool ShowView(DocumentView view) => Designer.ShowView(view);

    /// <summary><c>view.toggleFormCode</c> (F7): Code ↔ Design; from the Text view to the Design view.</summary>
    public void ToggleFormCode() => Designer.ToggleFormCode();

    /// <summary><c>view.formAsText</c>.</summary>
    public void ShowFormAsText() => Designer.ShowFormAsText();
}
