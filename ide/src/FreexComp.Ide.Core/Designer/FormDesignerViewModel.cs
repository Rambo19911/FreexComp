using System.Collections.ObjectModel;
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
using FreexComp.Ide.Core.Menus;
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
    private readonly LiveSettings _liveSettings;
    private InteractionState? _shownState;
    private FieldMismatch? _mismatch;
    private readonly HashSet<(string Kind, string Text)> _dismissed = [];

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
        _liveSettings = new LiveSettings(this);
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

    /// <summary>
    /// The designer settings the surface reads, live (defaults until the IDE binds its settings service); a change of a
    /// shown value is announced with <see cref="RefreshDisplay"/>.
    /// </summary>
    public IDesignerSettings Settings { get; set; } = new DefaultDesignerSettings();

    /// <summary>The items of the design-surface context menu (designer.md §8); null: no menu (set by the IDE's designer wiring).</summary>
    public Func<IReadOnlyList<MenuItemModel>>? ContextMenu { get; set; }

    /// <summary>
    /// The info bars of the Design view (designer.md §9) in stack order, at most <see cref="DocumentViewModel.MaxVisibleInfoBars"/>:
    /// <c>InfoBar.UnitErrors</c>, <c>InfoBar.FormClassNotFound</c> (the form is read-only, code-sync.md §8) and
    /// <c>InfoBar.TabOrder</c>. Re-evaluated when the Design view is entered, the model is replaced or the tab-order mode changes.
    /// </summary>
    public ObservableCollection<InfoBarViewModel> DesignInfoBars { get; } = [];

    /// <summary>Something the surface draws from outside the model changed (a designer setting): the view repaints.</summary>
    public event EventHandler? DisplayChanged;

    /// <summary>Raises <see cref="DisplayChanged"/>.</summary>
    public void RefreshDisplay() => DisplayChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// UI-DSG-130: the status-bar text of the selection — <c>Msg.Designer.Selection</c> (name, class, left, top, width,
    /// height; the form: its position and client size) or <c>Msg.Designer.SelectionMany</c>; null without a surface.
    /// </summary>
    public string? SelectionStatusText
    {
        get
        {
            if (Surface is not { } surface)
            {
                return null;
            }

            var items = surface.Selection.Items;
            if (items.Count > 1)
            {
                return string.Format(CultureInfo.CurrentCulture, Strings.Msg_Designer_SelectionMany, items.Count);
            }

            var obj = surface.Selection.Primary;
            var bounds = obj.Parent is not null && surface.Layout.HasBounds(obj) ? surface.Layout.GetBounds(obj) : PropertyAccess.GetBounds(obj);
            var (left, top) = obj.Parent is null || !surface.Layout.HasBounds(obj)
                ? (PropertyAccess.GetInt(obj, "Left"), PropertyAccess.GetInt(obj, "Top"))
                : (bounds.Left, bounds.Top);
            return string.Format(CultureInfo.CurrentCulture, Strings.Msg_Designer_Selection, obj.Name, obj.TypeName, left, top, bounds.Width, bounds.Height);
        }
    }

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

    /// <summary>
    /// code-sync.md §8: a unit that is not <c>Ok</c> makes the form read-only (not in the <c>NoUnit</c> mode); so does a
    /// preserved root (DSG-FD-027). The field-type check of DSG-CS-035 runs again (CheckOnly, no edits).
    /// </summary>
    private void UpdateReadOnly()
    {
        if (Surface is { } surface)
        {
            surface.IsReadOnly = (HasUnit && CodeSync.State != CodeSyncState.Ok) || Model!.Root.IsPreserved;
        }

        _mismatch = HasUnit && Model is { } model && CodeSync.State == CodeSyncState.Ok
            ? CodeSync.Reconcile(model, ReconcileMode.CheckOnly).Issues.FirstOrDefault(i => i.Kind == ReconcileIssueKind.FieldTypeMismatch)?.Mismatch
            : null;
        UpdateInfoBars();
    }

    /// <summary>
    /// The form problems <c>InfoBar.FormErrors</c> counts and <c>Show Problems</c> lists (DSG-FD-071): the model's parse and
    /// validation diagnostics of severity warning or error (the <c>F0107</c> hints have <c>InfoBar.FormReadOnlyKind</c>).
    /// </summary>
    public IReadOnlyList<FormDiagnostic> Problems =>
        Model is { } model ? [.. model.Diagnostics.Where(d => d.Severity is FreexComp.Compiler.DiagnosticSeverity.Error or FreexComp.Compiler.DiagnosticSeverity.Warning)] : [];

    /// <summary><c>Show Problems</c> of <c>InfoBar.FormErrors</c>: the IDE lists the problems in the Messages tool window.</summary>
    public Action<IReadOnlyList<FormDiagnostic>>? ShowProblems { get; set; }

    /// <summary>A bar the user dismissed (× or <c>Ignore</c>) stays hidden while its text is the same, until the model is replaced.</summary>
    private void Dismiss(InfoBarViewModel bar)
    {
        _dismissed.Add((bar.Kind, bar.Text));
        UpdateInfoBars();
    }

    /// <summary><c>Fix Declaration</c> (DSG-CS-035): the field's declared type becomes the form's type (one code undo group).</summary>
    private Task FixFieldTypeAsync(FieldMismatch mismatch)
    {
        try
        {
            CodeSync.FixFieldType(mismatch.Name, mismatch.ExpectedType);
        }
        catch (CodeSyncException)
        {
            // DSG-CS-004: rolled back; the bar stays.
        }

        UpdateReadOnly();
        return Task.CompletedTask;
    }

    /// <summary>The tab-order mode started or ended: <c>InfoBar.TabOrder</c> follows.</summary>
    private void OnSurfaceInvalidated(object? sender, EventArgs e)
    {
        if (Surface is { } surface && surface.State != _shownState
            && (surface.State == InteractionState.TabOrder || _shownState == InteractionState.TabOrder))
        {
            UpdateInfoBars();
        }
    }

    /// <summary>designer.md §9 (UI-DSG-091): the bars that apply, in table order, at most three.</summary>
    private void UpdateInfoBars()
    {
        _shownState = Surface?.State;
        var bars = new List<InfoBarViewModel>();
        if (Surface is not null && Problems.Count is var problems and > 0)
        {
            bars.Add(new InfoBarViewModel("FormErrors", InfoBarSeverity.Warning,
                problems == 1 ? Strings.Msg_Designer_FormParseErrorsOne : string.Format(CultureInfo.CurrentCulture, Strings.Msg_Designer_FormParseErrorsMany, problems),
            [
                new InfoBarAction("ShowProblems", Strings.Button_ShowProblems, () => Run(() => ShowProblems?.Invoke(Problems))),
                new InfoBarAction("ViewAsText", Strings.Button_ViewAsText, () => Run(ShowFormAsText)),
            ], Dismiss));
        }

        if (HasUnit && Surface is not null)
        {
            switch (CodeSync.State)
            {
                case CodeSyncState.UnitHasErrors:
                    bars.Add(new InfoBarViewModel("UnitErrors", InfoBarSeverity.Warning, Strings.Msg_Designer_UnitHasErrors,
                        [new InfoBarAction("GoToFirstError", Strings.Button_GoToFirstError, GoToFirstErrorAsync)], null));
                    break;
                case CodeSyncState.FormClassNotFound:
                    bars.Add(new InfoBarViewModel("FormClassNotFound", InfoBarSeverity.Warning,
                        string.Format(CultureInfo.CurrentCulture, Strings.Msg_Designer_FormClassNotFound, Unit.FormTypeName),
                        [new InfoBarAction("ViewCode", Strings.Button_ViewCode, () => Run(() => ShowView(DocumentView.Code)))], null));
                    break;
            }
        }

        if (Surface is not null && _mismatch is { } mismatch)
        {
            bars.Add(new InfoBarViewModel("FieldTypeMismatch", InfoBarSeverity.Warning,
                string.Format(CultureInfo.CurrentCulture, Strings.Msg_Designer_FieldTypeMismatch, mismatch.Name, mismatch.DeclaredType, mismatch.ExpectedType),
            [
                new InfoBarAction("FixDeclaration", Strings.Button_FixDeclaration, () => FixFieldTypeAsync(mismatch)),
                new InfoBarAction("Ignore", Strings.Button_Ignore, () => Run(() => Dismiss(DesignInfoBars.First(b => b.Kind == "FieldTypeMismatch")))),
            ], Dismiss));
        }

        if (Model is { } model && Surface is not null && model.Diagnostics.Any(d => d.Code == "F0107"))
        {
            bars.Add(new InfoBarViewModel("FormReadOnlyKind", InfoBarSeverity.Info, Strings.Msg_Designer_InheritedNotSupported,
                [new InfoBarAction("ViewAsText", Strings.Button_ViewAsText, () => Run(ShowFormAsText))], null));
        }

        if (Surface is { State: InteractionState.TabOrder } tabOrder)
        {
            bars.Add(new InfoBarViewModel("TabOrder", InfoBarSeverity.Info, Strings.Msg_Designer_TabOrderMode,
            [
                new InfoBarAction("Done", Strings.Button_Done, () => Run(tabOrder.ExitTabOrderMode)),
                new InfoBarAction("RestartNumbering", Strings.Button_RestartNumbering, () => Run(tabOrder.RestartTabOrderNumbering)),
            ], null));
        }

        bars = [.. bars.Where(b => !_dismissed.Contains((b.Kind, b.Text))).Take(DocumentViewModel.MaxVisibleInfoBars)];
        if (bars.Select(b => (b.Kind, b.Text)).SequenceEqual(DesignInfoBars.Select(b => (b.Kind, b.Text))))
        {
            return;
        }

        DesignInfoBars.Clear();
        foreach (var bar in bars)
        {
            DesignInfoBars.Add(bar);
        }
    }

    private static Task Run(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    private static Task Run(Func<bool> action)
    {
        action();
        return Task.CompletedTask;
    }

    /// <summary><c>Go to First Error</c>: the Code view with the caret at the unit's first syntax error.</summary>
    private Task GoToFirstErrorAsync()
    {
        if (Unit.Locate().FirstError is { Location.HasPosition: true } error)
        {
            ShowCodeAt(error.Location.Span.Start);
        }
        else
        {
            ShowView(DocumentView.Code);
        }

        return Task.CompletedTask;
    }

    /// <summary>The settings the surface reads: always those of <see cref="Settings"/> at the moment of reading.</summary>
    private sealed class LiveSettings(FormDesignerViewModel owner) : IDesignerSettings
    {
        public bool ShowComponentCaptions => owner.Settings.ShowComponentCaptions;

        public int HandleSize => owner.Settings.HandleSize;

        public bool ShowGrid => owner.Settings.ShowGrid;

        public bool SnapToGrid => owner.Settings.SnapToGrid;

        public int GridSizeX => owner.Settings.GridSizeX;

        public int GridSizeY => owner.Settings.GridSizeY;

        public bool UseGuidelines => owner.Settings.UseGuidelines;

        public int GuidelineSnapDistance => owner.Settings.GuidelineSnapDistance;

        public int GuidelineMargin => owner.Settings.GuidelineMargin;

        public bool LockControls => owner.Settings.LockControls;
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
            Surface.Invalidated -= OnSurfaceInvalidated;
        }

        Model = document;
        Editor = new FormEditor(document, Catalog);
        Surface = new DesignSurfaceController(document, Editor, Undo, Catalog, new LayoutEngine(Catalog, RendererRegistry.CreateDefault()),
            _liveSettings, Host);
        Surface.Selection.Changed += OnSelectionChanged;
        Surface.Invalidated += OnSurfaceInvalidated;
        _shownState = null;
        _dismissed.Clear();
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
        UpdateInfoBars();
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
