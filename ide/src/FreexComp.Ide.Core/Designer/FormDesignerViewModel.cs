using System.Globalization;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.CodeSync;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;
using FreexComp.Designer.Text;
using FreexComp.Ide.Core.Designer.CodeSync;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Designer;

/// <summary>What a save of a form document writes after <see cref="FormDesignerViewModel.PrepareSave"/>.</summary>
/// <param name="FormText">The <c>.form</c> text to write; null when the form buffer is not modified (DSG-FD-051).</param>
/// <param name="RemovedHandlers">The number of empty handlers removed (DSG-CS-074 status message).</param>
public sealed record FormSavePlan(string? FormText, int RemovedHandlers);

/// <summary>
/// The form designer side of a form document (designer README §3, D-072): the two buffers — the unit's text buffer and
/// the form model with its <c>.form</c> text — the three views and the rules for switching between them, the form
/// buffer's modified flag and the code-sync steps of a save. No Avalonia types; the design surface binds to
/// <see cref="Model"/>, <see cref="Editor"/> and <see cref="Undo"/> and rebinds on <see cref="ModelReplaced"/>.
/// </summary>
public sealed class FormDesignerViewModel
{
    /// <summary>The info bar of a form text that does not parse (designer.md §9 <c>InfoBar.FormSyntax</c>).</summary>
    public const string FormSyntaxInfoBarKind = "FormSyntax";

    private readonly TextDocumentViewModel _unit;
    private readonly TextDocumentViewModel _formText;

    // The form text the model was last read from or written to, and the model version at that moment: while both
    // still hold, the Text view and the model agree.
    private string _syncedText;
    private int _syncedVersion;
    private (long Version, bool Differs) _pending;
    private DocumentView _activeView;

    public FormDesignerViewModel(TextDocumentViewModel unit, TextDocumentViewModel formText, ComponentCatalog catalog)
    {
        _unit = unit ?? throw new ArgumentNullException(nameof(unit));
        _formText = formText ?? throw new ArgumentNullException(nameof(formText));
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _syncedText = formText.Buffer.Text;
        _pending = (formText.Buffer.Version, false);
        var parsed = Parse(_syncedText);
        Unit = new FormUnit(unit.Buffer, parsed.Document?.Root.TypeName ?? "", unit.FilePath ?? FormClassLocator.PlaceholderPath);
        CodeSync = new CodeSyncService(Unit, catalog, ShowCodeAt);
        formText.Buffer.Changed += (_, _) => ModifiedChanged?.Invoke(this, EventArgs.Empty);
        if (parsed.Document is { } document)
        {
            SetModel(document, _syncedText);
        }
        else
        {
            // DSG-FD-101: a form text with a syntax error opens in the Text view with InfoBar.FormSyntax.
            _activeView = DocumentView.Text;
            ShowSyntaxError(parsed.Diagnostics[0]);
        }
    }

    public ComponentCatalog Catalog { get; }

    /// <summary>The form model; null while the form text has a syntax error and was never read.</summary>
    public FormDocument? Model { get; private set; }

    /// <summary>The editor over <see cref="Model"/>; replaced with it.</summary>
    public FormEditor? Editor { get; private set; }

    /// <summary>The designer undo stack; cleared when the model is replaced (DSG-FD-093).</summary>
    public DesignerUndoStack Undo { get; } = new();

    /// <summary>The unit side for code sync.</summary>
    public FormUnit Unit { get; }

    public CodeSyncService CodeSync { get; }

    /// <summary>The last <c>CheckOnly</c> reconciliation (DSG-CS-090: on open and after a model replacement).</summary>
    public ReconcileResult? LastReconcile { get; private set; }

    public DocumentView ActiveView => _activeView;

    /// <summary>Raised after <see cref="ActiveView"/> changed.</summary>
    public event EventHandler? ActiveViewChanged;

    /// <summary>A Text-view edit replaced <see cref="Model"/> (and <see cref="Editor"/>).</summary>
    public event EventHandler? ModelReplaced;

    /// <summary>The form buffer's modified flag may have changed.</summary>
    public event EventHandler? ModifiedChanged;

    /// <summary>
    /// The form buffer is modified (DSG-FD-091): the designer undo stack is off its save point, or the Text view holds an
    /// edit the model does not have yet.
    /// </summary>
    public bool IsFormModified => !Undo.IsAtSavePoint || HasPendingText;

    private bool HasPendingText
    {
        get
        {
            var buffer = _formText.Buffer;
            if (_pending.Version != buffer.Version)
            {
                _pending = (buffer.Version, !string.Equals(buffer.Text, _syncedText, StringComparison.Ordinal));
            }

            return _pending.Differs;
        }
    }

    /// <summary>
    /// A view switch (designer README §3): leaving the Text view parses a changed text — a syntax error refuses the switch
    /// (false; <c>InfoBar.FormSyntax</c>, caret at the error), a valid change replaces the model; entering the Text view
    /// writes a changed model with <see cref="FormWriter"/>; Code → Design repairs the form class when the document is
    /// modified (DSG-CS-091 c).
    /// </summary>
    public bool ShowView(DocumentView view)
    {
        var previous = _activeView;
        if (view == previous)
        {
            return true;
        }

        if (previous == DocumentView.Text && !TryReadText())
        {
            return false;
        }

        if (view == DocumentView.Text)
        {
            WriteText();
        }
        else if (view == DocumentView.Design && previous == DocumentView.Code && _unit.IsModified)
        {
            Repair();
        }

        _activeView = view;
        ActiveViewChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary><c>view.toggleFormCode</c> (F7): Code ↔ Design; from the Text view to the Design view.</summary>
    public bool ToggleFormCode() => ShowView(_activeView == DocumentView.Design ? DocumentView.Code : DocumentView.Design);

    /// <summary><c>view.formAsText</c>.</summary>
    public bool ShowFormAsText() => ShowView(DocumentView.Text);

    /// <summary>
    /// The code-sync steps of a save, before anything is written: a Text-view edit is read first (when it parses); then
    /// <c>Repair</c> when either buffer is modified and the state is <c>Ok</c> (DSG-CS-091 b), then the removal of empty
    /// unreferenced handlers (DSG-CS-072), each one code-editor undo group. Returns what to write for the form.
    /// </summary>
    public FormSavePlan PrepareSave()
    {
        if (_activeView == DocumentView.Text && HasPendingText)
        {
            TryReadText();
        }

        if (_unit.IsModified)
        {
            Repair();
        }

        var removed = 0;
        if (Model is not null && CodeSync.State == CodeSyncState.Ok)
        {
            var candidates = CodeSync.HandlerCandidates.ToList();
            CodeSync.RemoveEmptyUnreferencedHandlers(Model);
            removed = candidates.Count(name => !CodeSync.HandlerCandidates.Contains(name));
        }

        string? text = null;
        if (IsFormModified)
        {
            text = _activeView == DocumentView.Text || Model is null ? _formText.Buffer.Text : FormWriter.Write(Model);
        }

        return new FormSavePlan(text, removed);
    }

    /// <summary>The form text <paramref name="written"/> (from <see cref="PrepareSave"/>) is on disk now: the new save point.</summary>
    public void MarkFormSaved(string written)
    {
        ArgumentNullException.ThrowIfNull(written);
        Undo.MarkSaved();
        if (string.Equals(written, _formText.Buffer.Text, StringComparison.Ordinal))
        {
            _syncedText = written;
            _pending = (_formText.Buffer.Version, false);
        }

        ModifiedChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The Code view with the caret at <paramref name="offset"/> (DSG-CS-063).</summary>
    private void ShowCodeAt(int offset)
    {
        if (ShowView(DocumentView.Code))
        {
            _unit.ViewState.Caret = _unit.Buffer.GetPosition(offset);
        }
    }

    /// <summary>DSG-CS-091: repairs the form class when there is a model and the state is <c>Ok</c>.</summary>
    private void Repair()
    {
        if (Model is null || CodeSync.State != CodeSyncState.Ok)
        {
            return;
        }

        try
        {
            CodeSync.Reconcile(Model, ReconcileMode.Repair);
        }
        catch (CodeSyncException)
        {
            // DSG-CS-004: rolled back; the class stays as it is and the next save tries again.
        }
    }

    /// <summary>Leaving the Text view: an unchanged text keeps the model; a changed one must parse and replaces it.</summary>
    private bool TryReadText()
    {
        var text = _formText.Buffer.Text;
        if (Model is not null && string.Equals(text, _syncedText, StringComparison.Ordinal))
        {
            _formText.RemoveInfoBar(FormSyntaxInfoBarKind);
            return true;
        }

        var parsed = Parse(text);
        if (parsed.Document is not { } document)
        {
            ShowSyntaxError(parsed.Diagnostics[0]);
            return false;
        }

        Undo.Clear(); // DSG-FD-093: the save point is gone with the stack
        SetModel(document, text);
        ModelReplaced?.Invoke(this, EventArgs.Empty);
        ModifiedChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Entering the Text view: a model changed since the last sync is written into the form buffer.</summary>
    private void WriteText()
    {
        if (Model is null || Model.Version == _syncedVersion)
        {
            return;
        }

        var text = FormWriter.Write(Model);
        var buffer = _formText.Buffer;
        if (!string.Equals(buffer.Text, text, StringComparison.Ordinal))
        {
            buffer.ReplaceAll(text, resetUndo: false);
        }

        _syncedText = text;
        _syncedVersion = Model.Version;
        _pending = (buffer.Version, false);
    }

    private void SetModel(FormDocument document, string text)
    {
        if (Model is not null)
        {
            Model.Changed -= OnModelChanged;
        }

        Model = document;
        Editor = new FormEditor(document, Catalog);
        document.Changed += OnModelChanged;
        _syncedText = text;
        _syncedVersion = document.Version;
        _pending = (_formText.Buffer.Version, false);
        Unit.FormTypeName = document.Root.TypeName;
        _formText.RemoveInfoBar(FormSyntaxInfoBarKind);
        LastReconcile = CodeSync.Reconcile(document, ReconcileMode.CheckOnly);
    }

    private void OnModelChanged(object? sender, FormChangedEventArgs e) => ModifiedChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>InfoBar.FormSyntax with <c>Msg.Designer.FormSyntaxError</c>; the caret goes to the error position.</summary>
    private void ShowSyntaxError(FormDiagnostic error)
    {
        _formText.ShowInfoBar(new InfoBarViewModel(FormSyntaxInfoBarKind, InfoBarSeverity.Error,
            string.Format(CultureInfo.CurrentCulture, Strings.Msg_Designer_FormSyntaxError, error.Message, error.Line, error.Col), [], null));
        var buffer = _formText.Buffer;
        var line = Math.Clamp(error.Line, 1, buffer.LineCount);
        _formText.ViewState.Caret = buffer.GetPosition(buffer.GetOffset(line, Math.Max(1, error.Col)));
    }

    /// <summary>
    /// Parses the form text; the root binds to the ancestor its unit declares (<c>class(TForm)</c>) when the catalog does
    /// not know the form class itself (README lead notes D-001…D-008).
    /// </summary>
    private FormParseResult Parse(string text)
    {
        var path = _formText.FilePath ?? "Untitled.form";
        var first = FormParser.Parse(text, path, Catalog);
        if (first.Document is not { } document || Catalog.Find(document.Root.TypeName) is not null)
        {
            return first;
        }

        var ancestor = FormClassLocator.Locate(_unit.Buffer.Text, document.Root.TypeName, _unit.FilePath ?? FormClassLocator.PlaceholderPath)
            .FormClass?.AncestorName;
        return ancestor is null ? first : FormParser.Parse(text, path, Catalog, ancestor);
    }
}
