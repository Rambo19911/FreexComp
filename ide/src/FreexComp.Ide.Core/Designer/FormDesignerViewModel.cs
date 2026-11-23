using System.Globalization;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.CodeSync;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Layout;
using FreexComp.Designer.Model;
using FreexComp.Designer.Rendering;
using FreexComp.Designer.Surface;
using FreexComp.Designer.Text;
using FreexComp.Ide.Core.Designer.CodeSync;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Designer;

/// <summary>What a save of a form document writes after <see cref="FormDesignerViewModel.PrepareSave"/>.</summary>
/// <param name="FormText">The <c>.form</c> text to write; null when the form buffer is not modified (DSG-FD-051).</param>
/// <param name="RemovedHandlers">The number of empty handlers removed (DSG-CS-074 status message).</param>
public sealed record FormSavePlan(string? FormText, int RemovedHandlers);

/// <summary>
/// The form designer side of a form document (designer README §3, D-072): the two buffers — the unit's text buffer and
/// the form model with its <c>.form</c> text — the three views and the rules for switching between them, the form
/// buffer's modified flag and the code-sync steps of a save. It owns the design surface controller of the current model
/// (<see cref="Surface"/>, rebuilt on <see cref="ModelReplaced"/>) and its host (<see cref="Host"/>: status, Tool Palette
/// arming, clipboard, code sync). No Avalonia types.
/// </summary>
public sealed class FormDesignerViewModel
{
    /// <summary>The info bar of a form text that does not parse (designer.md §9 <c>InfoBar.FormSyntax</c>).</summary>
    public const string FormSyntaxInfoBarKind = "FormSyntax";

    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(5); // UI-MSG-003 "Status"

    private readonly TextDocumentViewModel _unit;
    private readonly TextDocumentViewModel _formText;

    // The form text the model was last read from or written to, and the model version at that moment: while both
    // still hold, the Text view and the model agree.
    private string _syncedText;
    private int _syncedVersion;
    private (long Version, bool Differs) _pending;
    private DocumentView _activeView;

    /// <param name="hasUnit">False: a <c>.form</c> opened without its unit (code-sync.md §8 <c>NoUnit</c>, DSG-CS-080) —
    /// the Design view without code sync and without a Code view; <paramref name="unit"/> is then an empty placeholder.</param>
    public FormDesignerViewModel(TextDocumentViewModel unit, TextDocumentViewModel formText, ComponentCatalog catalog, bool hasUnit = true)
    {
        _unit = unit ?? throw new ArgumentNullException(nameof(unit));
        _formText = formText ?? throw new ArgumentNullException(nameof(formText));
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        HasUnit = hasUnit;
        _activeView = hasUnit ? DocumentView.Code : DocumentView.Design;
        _syncedText = formText.Buffer.Text;
        _pending = (formText.Buffer.Version, false);
        Host = new DesignerHost(this);
        var parsed = Parse(_syncedText);
        Unit = new FormUnit(unit.Buffer, parsed.Document?.Root.TypeName ?? "", unit.FilePath ?? FormClassLocator.PlaceholderPath);
        CodeSync = new CodeSyncService(Unit, catalog, ShowCodeAt) { HasUnit = hasUnit, Form = () => Model };
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

    public ComponentCatalog Catalog { get; private set; }

    /// <summary>False in the <c>NoUnit</c> mode (DSG-CS-080).</summary>
    public bool HasUnit { get; }

    /// <summary>The form model; null while the form text has a syntax error and was never read.</summary>
    public FormDocument? Model { get; private set; }

    /// <summary>The editor over <see cref="Model"/>; replaced with it.</summary>
    public FormEditor? Editor { get; private set; }

    /// <summary>The design surface over <see cref="Model"/>; replaced with it (null while there is no model).</summary>
    public DesignSurfaceController? Surface { get; private set; }

    /// <summary>What the surface needs from the IDE: status, palette arming, clipboard, code sync.</summary>
    public DesignerHost Host { get; }

    /// <summary>The designer settings the surface reads (defaults until the IDE binds its settings service).</summary>
    public IDesignerSettings Settings { get; set; } = new DefaultDesignerSettings();

    /// <summary>Where <see cref="DesignerHost.SetStatus"/> messages go (set by the document service).</summary>
    public IStatusService? Status { get; set; }

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

    /// <summary><see cref="Model"/> (with <see cref="Editor"/> and <see cref="Surface"/>) was replaced.</summary>
    public event EventHandler? ModelReplaced;

    /// <summary>The selection of the current surface changed, or the surface was replaced.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>The form model changed (any applied change).</summary>
    public event EventHandler? ModelChanged;

    /// <summary>The form buffer's modified flag may have changed.</summary>
    public event EventHandler? ModifiedChanged;

    /// <summary>
    /// The form buffer is modified (DSG-FD-091): the designer undo stack is off its save point, or the Text view holds an
    /// edit the model does not have yet.
    /// </summary>
    public bool IsFormModified => !Undo.IsAtSavePoint || HasPendingText;

    /// <summary>The Design view is shown and has a model: the Object Inspector and the Tool Palette work on it.</summary>
    public bool IsDesignActive => _activeView == DocumentView.Design && Surface is not null;

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
    /// modified (DSG-CS-091 c). Without a unit there is no Code view (false). Leaving the Design view disarms the Tool
    /// Palette (DSG-TP-040).
    /// </summary>
    public bool ShowView(DocumentView view)
    {
        var previous = _activeView;
        if (view == previous)
        {
            return true;
        }

        if (view == DocumentView.Code && !HasUnit)
        {
            return false;
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

        if (previous == DocumentView.Design)
        {
            Host.Disarm();
        }

        _activeView = view;
        if (view == DocumentView.Design)
        {
            UpdateReadOnly();
        }

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

        if (_unit.IsModified || IsFormModified)
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

        return new FormSavePlan(IsFormModified ? CurrentFormText() : null, removed);
    }

    /// <summary>The <c>.form</c> text as it would be saved now (the Text view's text, or the model written).</summary>
    public string CurrentFormText() =>
        _activeView == DocumentView.Text || Model is null ? _formText.Buffer.Text : FormWriter.Write(Model);

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

    /// <summary>
    /// DSG-CS-022, IDE-PM-071: the form file follows the unit (Save As, rename): code sync names
    /// <paramref name="formFileName"/> from now on and the unit's <c>{$FORM}</c> directive is set to it (state <c>Ok</c>).
    /// </summary>
    public void RetargetForm(string formFileName)
    {
        ArgumentNullException.ThrowIfNull(formFileName);
        CodeSync.FormFileName = formFileName;
        if (CodeSync.State == CodeSyncState.Ok)
        {
            FormDirectiveEdits.EnsureFormDirective(Unit, formFileName);
        }
    }

    /// <summary>
    /// DSG-FD-100: the form buffer was reloaded from disk; its text replaces the model (selection kept by name, undo stack
    /// cleared, the reloaded text is the save point). A text with syntax errors switches to the Text view with
    /// <c>InfoBar.FormSyntax</c> (DSG-FD-101).
    /// </summary>
    public void ReloadForm()
    {
        var text = _formText.Buffer.Text;
        var parsed = Parse(text);
        if (parsed.Document is not { } document)
        {
            ShowSyntaxError(parsed.Diagnostics[0]);
            _syncedText = text;
            _pending = (_formText.Buffer.Version, false);
            if (_activeView != DocumentView.Text)
            {
                Host.Disarm();
                _activeView = DocumentView.Text;
                ActiveViewChanged?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        var names = SelectedNames();
        Undo.Clear();
        Undo.MarkSaved();
        SetModel(document, text);
        Surface!.Selection.Set(names);
        Replaced();
    }

    /// <summary>
    /// DSG-TP-008: the component metadata was reloaded. The current canonical text is read again against
    /// <paramref name="catalog"/>; the selection is kept by name, the undo stack only when the object and property
    /// structure is identical.
    /// </summary>
    public void Rebind(ComponentCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        Catalog = catalog;
        CodeSync.Catalog = catalog;
        if (Model is not { } old)
        {
            return;
        }

        var text = FormWriter.Write(old);
        if (Parse(text).Document is not { } document)
        {
            return;
        }

        var names = SelectedNames();
        var inSync = old.Version == _syncedVersion;
        if (!SameStructure(old, document))
        {
            var saved = Undo.IsAtSavePoint;
            Undo.Clear();
            if (saved)
            {
                Undo.MarkSaved();
            }
        }

        var synced = _syncedText;
        SetModel(document, synced, repair: false);
        if (!inSync)
        {
            _syncedVersion = -1; // the Text view is written again when it is shown
        }

        Surface!.Selection.Set(names);
        Replaced();
    }

    /// <summary>Shows a designer status message (<c>states-and-messages.md</c> id and arguments).</summary>
    internal void ShowStatus(string messageId, object[] args) => Status?.ShowMessage(DesignerMessages.Format(messageId, args), StatusDuration);

    private string[] SelectedNames() => Surface is { } surface ? [.. surface.Selection.Items.Select(o => o.Name)] : [];

    private void Replaced()
    {
        ModelReplaced?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        ModifiedChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>DSG-TP-008: the same objects (name, type, kind, preserved) with the same stored property paths, in order.</summary>
    private static bool SameStructure(FormDocument a, FormDocument b)
    {
        var left = a.AllObjects().ToList();
        var right = b.AllObjects().ToList();
        return left.Count == right.Count && left.Zip(right).All(pair =>
            pair.First.Name == pair.Second.Name && pair.First.TypeName == pair.Second.TypeName && pair.First.Kind == pair.Second.Kind
            && pair.First.IsPreserved == pair.Second.IsPreserved && (pair.First.Info is null) == (pair.Second.Info is null)
            && pair.First.Properties.Select(p => p.Path).SequenceEqual(pair.Second.Properties.Select(p => p.Path), StringComparer.OrdinalIgnoreCase));
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

    /// <summary>code-sync.md §8: a unit that is not <c>Ok</c> makes the form read-only (not in the <c>NoUnit</c> mode).</summary>
    private void UpdateReadOnly()
    {
        if (Surface is { } surface)
        {
            surface.IsReadOnly = HasUnit && CodeSync.State != CodeSyncState.Ok;
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

        var names = SelectedNames();
        Undo.Clear(); // DSG-FD-093: the save point is gone with the stack
        SetModel(document, text);
        Surface!.Selection.Set(names);
        Replaced();
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

    /// <param name="repair">DSG-CS-091 (a): the next mutating action repairs first (open, Text-view replacement, reload).</param>
    private void SetModel(FormDocument document, string text, bool repair = true)
    {
        if (Model is not null)
        {
            Model.Changed -= OnModelChanged;
        }

        if (Surface is not null)
        {
            Surface.Selection.Changed -= OnSelectionChanged;
        }

        Model = document;
        Editor = new FormEditor(document, Catalog);
        Surface = new DesignSurfaceController(document, Editor, Undo, Catalog, new LayoutEngine(Catalog, RendererRegistry.CreateDefault()),
            Settings, Host);
        Surface.Selection.Changed += OnSelectionChanged;
        document.Changed += OnModelChanged;
        _syncedText = text;
        _syncedVersion = document.Version;
        _pending = (_formText.Buffer.Version, false);
        Unit.FormTypeName = document.Root.TypeName;
        _formText.RemoveInfoBar(FormSyntaxInfoBarKind);
        LastReconcile = CodeSync.Reconcile(document, ReconcileMode.CheckOnly);
        if (repair)
        {
            CodeSync.RepairPending = true;
        }

        UpdateReadOnly();
    }

    private void OnSelectionChanged(object? sender, EventArgs e) => SelectionChanged?.Invoke(this, EventArgs.Empty);

    private void OnModelChanged(object? sender, FormChangedEventArgs e)
    {
        // DSG-CS-046: the class code sync looks for follows the root's type name (a form rename, its undo and redo).
        Unit.FormTypeName = Model!.Root.TypeName;
        ModelChanged?.Invoke(this, EventArgs.Empty);
        ModifiedChanged?.Invoke(this, EventArgs.Empty);
    }

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
    /// not know the form class itself (README lead notes D-001…D-008). Without a unit the form class binds to the
    /// metadata's form class when the catalog does not know it.
    /// </summary>
    private FormParseResult Parse(string text)
    {
        var path = _formText.FilePath ?? "Untitled.form";
        var first = FormParser.Parse(text, path, Catalog);
        if (first.Document is not { } document || Catalog.Find(document.Root.TypeName) is not null)
        {
            return first;
        }

        var ancestor = HasUnit
            ? FormClassLocator.Locate(_unit.Buffer.Text, document.Root.TypeName, _unit.FilePath ?? FormClassLocator.PlaceholderPath)
                .FormClass?.AncestorName
            : Catalog.All.FirstOrDefault(c => c.Kind == ComponentKind.Form)?.ClassName;
        return ancestor is null ? first : FormParser.Parse(text, path, Catalog, ancestor);
    }
}
