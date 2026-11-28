using System.Collections.Immutable;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Compiler.Syntax;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.CodeSync;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Inspector;
using FreexComp.Designer.Model;
using FreexComp.Designer.Surface;
using FreexComp.Ide.Core.Designer.CodeSync;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;

namespace FreexComp.Ide.Core.Designer;

/// <summary>What a <see cref="PropertyGridViewModel"/> edits: the selected objects and where their changes go.</summary>
public interface IPropertyGridTarget
{
    ComponentCatalog Catalog { get; }

    /// <summary>The edited objects; the last is the primary.</summary>
    IReadOnlyList<FormObject> Selection { get; }

    bool IsReadOnly { get; }

    /// <summary>Applies the changes as one unit; false when they were refused (<see cref="FailureMessage"/> then says why).</summary>
    bool Apply(IReadOnlyList<FormChange> changes);

    string? FailureMessage { get; }

    /// <summary>The names a component-reference row may take (object-inspector.md §5.1), by name.</summary>
    IReadOnlyList<string> References(PropertyInfo info);

    /// <summary>
    /// The designer of components of a form (the Object Inspector, the component editors): enables the <c>Name</c> row,
    /// the Events page, the collection editor and the TabOrder renumbering. Null for the values of a collection item.
    /// </summary>
    FormDesignerViewModel? Designer => null;

    /// <summary>DSG-OI-035: the message of an entry refused at focus loss (the Object Inspector shows it in the status bar).</summary>
    void ReportError(string message)
    {
    }
}

/// <summary>
/// The one property grid of object-inspector.md §2–§9 (D-110): the Object Inspector runs on it, the collection editor
/// (item mode, DSG-OI-113) and the Menu Designer (DSG-SUR-197) embed it. Rows from <see cref="PropertyGridModel"/>, the
/// current row, expansion, the text edit lifecycle with the value converters' validation (DSG-OI-032 … 035, 092), the
/// <c>Name</c> row (DSG-OI-070, 071), reset to default (DSG-OI-038), Boolean/set toggles (DSG-OI-050), value cycling, the
/// strings and collection editors (DSG-OI-055, 110), and the Events page (§8) over an <see cref="IPropertyGridTarget"/>.
/// </summary>
public sealed class PropertyGridViewModel : ObservableObject
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    private readonly IPropertyGridTarget _target;
    private readonly IDialogService? _dialogs;
    private readonly bool _itemMode;
    private PropertyGridModel _model;
    private ComponentCatalog _modelCatalog;
    private InspectorPage _page;
    private string? _currentPath;
    private string? _currentEvent;
    private string _editText = "";
    private string? _editError;
    private bool _isEditing;
    private string? _original;
    private string _filter = "";

    public PropertyGridViewModel(IPropertyGridTarget target, IDialogService? dialogs = null, bool itemMode = false)
    {
        ArgumentNullException.ThrowIfNull(target);
        _target = target;
        _dialogs = dialogs;
        _itemMode = itemMode;
        _modelCatalog = target.Catalog;
        _model = new PropertyGridModel(_modelCatalog) { ItemMode = itemMode, PreviewBounds = PreviewBounds };
    }

    /// <summary>DSG-OI-095: the surface's preview bounds of a control while a drag shows one.</summary>
    private FreexComp.Designer.Rect? PreviewBounds(FormObject obj) =>
        Designer?.Surface is { HasLayoutPreview: true } surface && surface.Layout.HasBounds(obj) ? surface.Layout.GetBounds(obj) : null;

    /// <summary>
    /// Sets the arrangement, collapsed categories and expanded rows of the model before each read (the Object Inspector's
    /// settings and per-class session state, DSG-OI-010, 013); null: the grid's own state.
    /// </summary>
    public Action<PropertyGridModel>? Configure { get; set; }

    /// <summary>The filter text (DSG-OI-020) of both pages.</summary>
    public string Filter
    {
        get => _filter;
        set => _filter = (value ?? "").Trim();
    }

    /// <summary>The page shown (the Events page exists for a designer target only).</summary>
    public InspectorPage Page
    {
        get => _page;
        set
        {
            if (SetProperty(ref _page, value))
            {
                Cancel();
                EnsureCurrentEvent();
                OnPropertyChanged(nameof(Rows));
                OnPropertyChanged(nameof(EventRows));
            }
        }
    }

    /// <summary>The visible rows of the Properties page (re-read from the objects on every access, DSG-OI-094).</summary>
    public IReadOnlyList<PropertyRow> Rows
    {
        get
        {
            var model = Model;
            model.SetSelection(_target.Selection);
            Configure?.Invoke(model);
            model.Filter = _filter;
            return model.Rows;
        }
    }

    /// <summary>The path of the current row (DSG-OI-031; null: none).</summary>
    public string? CurrentPath
    {
        get => _currentPath;
        set => SetProperty(ref _currentPath, value);
    }

    /// <summary>The current row of the Events page (the event name).</summary>
    public string? CurrentEvent
    {
        get => _currentEvent;
        set => SetProperty(ref _currentEvent, value);
    }

    /// <summary>The text of the value editor while <see cref="IsEditing"/>.</summary>
    public string EditText
    {
        get => _editText;
        set => SetProperty(ref _editText, value ?? "");
    }

    /// <summary>The inline error of a refused entry (DSG-OI-035, UI-DSG-109); null without one.</summary>
    public string? EditError
    {
        get => _editError;
        set => SetProperty(ref _editError, value);
    }

    public bool IsEditing
    {
        get => _isEditing;
        private set => SetProperty(ref _isEditing, value);
    }

    /// <summary>The current property row of the Properties page; null without one.</summary>
    public PropertyRow? CurrentRow => Rows.FirstOrDefault(r => r.Kind == PropertyRowKind.Property && Names.Equals(r.Path, CurrentPath));

    /// <summary>The model of the target's catalog (a new one after a metadata reload, DSG-TP-008).</summary>
    private PropertyGridModel Model
    {
        get
        {
            if (!ReferenceEquals(_target.Catalog, _modelCatalog))
            {
                var old = _model;
                _modelCatalog = _target.Catalog;
                _model = new PropertyGridModel(_modelCatalog)
                {
                    ItemMode = _itemMode, Arrangement = old.Arrangement, ExpandedPaths = old.ExpandedPaths,
                    CollapsedCategories = old.CollapsedCategories, PreviewBounds = PreviewBounds,
                };
            }

            return _model;
        }
    }

    private FormDesignerViewModel? Designer => _target.Designer;

    private IReadOnlyList<FormObject> Selection => _target.Selection;

    private ComponentCatalog Catalog => _target.Catalog;

    private bool CanEdit => Selection.Count > 0 && !_target.IsReadOnly;

    /// <summary>The target's objects or values changed: the rows are re-read (a current row no longer shown is cleared).</summary>
    public void Refresh()
    {
        if (CurrentPath is { } path && !Rows.Any(r => Names.Equals(r.Key, path)))
        {
            CurrentPath = null;
        }

        RaiseRowsChanged();
    }

    /// <summary>
    /// The selection changed (DSG-OI-031): an edit ends without a change, the current row keeps its path when the new
    /// selection has it, else the first property row; the Events page likewise.
    /// </summary>
    public void SelectionChanged()
    {
        Cancel();
        var rows = Rows;
        if (CurrentPath is null || !rows.Any(r => r.Kind == PropertyRowKind.Property && Names.Equals(r.Path, CurrentPath)))
        {
            CurrentPath = rows.FirstOrDefault(r => r.Kind == PropertyRowKind.Property)?.Path;
        }

        EnsureCurrentEvent();
        RaiseRowsChanged();
    }

    // ---- the current row -------------------------------------------------------------------------------------------

    /// <summary>The paths (Properties page) or event names (Events page) the current row moves over.</summary>
    private IReadOnlyList<string> NavigableRows =>
        Page == InspectorPage.Events
            ? [.. EventRows.Select(r => r.Name)]
            : [.. Rows.Where(r => r.Kind == PropertyRowKind.Property).Select(r => r.Path)];

    private string? Current => Page == InspectorPage.Events ? CurrentEvent : CurrentPath;

    /// <summary>Makes <paramref name="path"/> the current row of the shown page (a click); a running edit is committed first.</summary>
    public void SetCurrent(string? path)
    {
        if (IsEditing)
        {
            Commit(InspectorCommitCause.FocusLost);
        }

        if (Page == InspectorPage.Events)
        {
            CurrentEvent = path;
            return;
        }

        CurrentPath = path is not null && Rows.FirstOrDefault(r => Names.Equals(r.Key, path)) is { } row ? row.Key : null; // UI-TW-304
    }

    /// <summary>DSG-OI-030: Up/Down (plus or minus 1), PageUp/PageDown (a page) move the current row; it stops at the ends.</summary>
    public void MoveCurrent(int delta)
    {
        var rows = NavigableRows;
        if (rows.Count == 0)
        {
            return;
        }

        var index = Current is { } current ? IndexOf(rows, current) : -1;
        SetCurrent(rows[index < 0 ? (delta < 0 ? rows.Count - 1 : 0) : Math.Clamp(index + delta, 0, rows.Count - 1)]);
    }

    /// <summary>Home / End.</summary>
    public void MoveCurrentToEdge(bool end)
    {
        var rows = NavigableRows;
        if (rows.Count > 0)
        {
            SetCurrent(end ? rows[^1] : rows[0]);
        }
    }

    private static int IndexOf(IReadOnlyList<string> rows, string path)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (Names.Equals(rows[i], path))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Raised after an expansion or a category changed (the Object Inspector's session state, DSG-OI-013).</summary>
    public event EventHandler? ExpansionChanged;

    /// <summary>Expands an object or set row (DSG-OI-013).</summary>
    public void Expand(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (Expanded().Add(path))
        {
            ExpansionChanged?.Invoke(this, EventArgs.Empty);
        }

        RaiseRowsChanged();
    }

    public void Collapse(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (Expanded().Remove(path))
        {
            ExpansionChanged?.Invoke(this, EventArgs.Empty);
        }

        RaiseRowsChanged();
    }

    /// <summary>A click on a category row collapses or expands it (DSG-OI-013, UI-DSG-108).</summary>
    public void ToggleCategory(string category)
    {
        ArgumentNullException.ThrowIfNull(category);
        var collapsed = Configured().CollapsedCategories;
        if (!collapsed.Remove(category))
        {
            collapsed.Add(category);
        }

        ExpansionChanged?.Invoke(this, EventArgs.Empty);
        RaiseRowsChanged();
    }

    /// <summary>DSG-OI-030: Right expands the current object/set row, Left collapses it or moves to its parent row.</summary>
    public void ExpandCurrent(bool expand)
    {
        if (Page != InspectorPage.Properties || CurrentRow is not { } row)
        {
            return;
        }

        if (expand && row.IsExpandable && !row.IsExpanded)
        {
            Expand(row.Path);
        }
        else if (!expand && row.IsExpandable && row.IsExpanded)
        {
            Collapse(row.Path);
        }
        else if (!expand && row.Path.LastIndexOf('.') is var dot and > 0)
        {
            CurrentPath = row.Path[..dot];
        }
    }

    /// <summary><c>objectInspector.expandAll</c> / <c>collapseAll</c>: every expandable row of the selection.</summary>
    public void ExpandAll(bool expand)
    {
        var expanded = Expanded();
        if (!expand)
        {
            expanded.Clear();
        }
        else
        {
            // Expanding shows sub-rows that may expand in turn: repeat until nothing changes.
            for (var added = true; added;)
            {
                added = false;
                foreach (var row in Rows.Where(r => r.IsExpandable && !r.IsExpanded))
                {
                    added |= expanded.Add(row.Path);
                }
            }
        }

        ExpansionChanged?.Invoke(this, EventArgs.Empty);
        RaiseRowsChanged();
    }

    private PropertyGridModel Configured()
    {
        var model = Model;
        model.SetSelection(Selection);
        Configure?.Invoke(model);
        return model;
    }

    private ISet<string> Expanded() => Configured().ExpandedPaths;

    // ---- the edit lifecycle (§4) ------------------------------------------------------------------------------------

    /// <summary>Enters edit mode on the current row with its display text; false for rows that are not edited as text.</summary>
    public bool BeginEdit() => BeginEdit(null);

    /// <summary>
    /// DSG-OI-032: enters edit mode on the current row; <paramref name="entry"/> (a typed character, pasted text) replaces
    /// the displayed value. On the Events page the handler name is edited (DSG-OI-084).
    /// </summary>
    public bool BeginEdit(string? entry)
    {
        if (EditDisplay() is not { } display)
        {
            return false;
        }

        _original = display;
        EditText = entry ?? display;
        EditError = null;
        IsEditing = true;
        return true;
    }

    /// <summary>The text an edit of the current row starts from; null when the row is not edited as text.</summary>
    public string? EditDisplay()
    {
        if (Page == InspectorPage.Events)
        {
            return IsEventsReadOnly || CurrentEvent is not { } name || AllEvents().FirstOrDefault(r => Names.Equals(r.Name, name)) is not { } ev
                ? null
                : ev.Value;
        }

        return !CanEdit || CurrentRow is not { } row || !IsTextEditable(row) ? null : row.Display;
    }

    /// <summary>
    /// Commits the edit (§4): unchanged text ends the edit without a unit (DSG-OI-034); an entry refused for any object
    /// applies nothing (DSG-OI-092) — Enter keeps the editor open with the message, focus loss cancels and reports it
    /// (DSG-OI-035). True when the edit ended without an error.
    /// </summary>
    public bool Commit(InspectorCommitCause cause = InspectorCommitCause.Enter)
    {
        if (!IsEditing)
        {
            return true;
        }

        if (string.Equals(EditText, _original, StringComparison.Ordinal))
        {
            Cancel();
            return true;
        }

        string? error;
        if (Page == InspectorPage.Events)
        {
            if (CurrentEvent is not { } name)
            {
                Cancel();
                return true;
            }

            error = CommitEvent(name, EditText) ? null : EditError ?? "";
        }
        else if (CurrentRow is not { } row)
        {
            Cancel();
            return true;
        }
        else
        {
            error = row.Source == PropertyRowSource.Name ? CommitName(EditText) : CommitValue(row, EditText);
        }

        if (error is null)
        {
            Cancel();
            Refresh();
            return true;
        }

        if (cause == InspectorCommitCause.FocusLost)
        {
            Cancel();
            _target.ReportError(error);
            Refresh();
        }
        else
        {
            EditError = error;
        }

        return false;
    }

    /// <summary>Escape: the edit ends without a change.</summary>
    public void Cancel()
    {
        IsEditing = false;
        EditError = null;
        _original = null;
    }

    /// <summary>
    /// DSG-OI-038: a stored value of a writable row (not Name, not the always-written geometry) can be removed; DSG-OI-118:
    /// an override of an inherited object can be removed, its geometry included.
    /// </summary>
    public bool CanResetToDefault =>
        CanEdit && CurrentRow is { Source: PropertyRowSource.Metadata, IsReadOnly: false, SetMember: null, Info: { } info } row
        && Selection.Any(o => ResetPaths(o, row.Path, info).Any());

    /// <summary>
    /// DSG-OI-119, 124: the tooltip of the read-only Name row of an inherited object — <c>Msg.Designer.FrameChildFixed</c> in
    /// an instance the form creates, <c>Msg.Designer.InheritedComponentFixed</c> for a component of a descendant's ancestor
    /// (DSG-SUR-238); null otherwise.
    /// </summary>
    public string? FixedNameTip => Selection.FirstOrDefault(o => o.IsInherited) is { } inherited
        && DesignSurfaceController.FixedMessage(inherited) is var (messageId, className)
        ? DesignerMessages.Format(messageId, className)
        : null;

    /// <summary>
    /// DSG-OI-118, 123: every selected component is inherited, an instance or an inherited root: the reset reads "Revert to
    /// Inherited".
    /// </summary>
    public bool IsRevertToInherited => Selection.Count > 0 && Selection.All(o => o.IsInherited || o.IsFrameInstance || o.IsInheritedRoot);

    private IEnumerable<string> ResetPaths(FormObject obj, string path, PropertyInfo info) =>
        _itemMode || obj.IsInherited || obj.IsInheritedRoot || !IsAlwaysWritten(path) ? StoredPaths(obj, path, info) : [];

    /// <summary>DSG-OI-038: removes the stored value (an object row: all its stored sub-properties) of every object.</summary>
    public void ResetToDefault()
    {
        if (!CanResetToDefault || CurrentRow is not { Info: { } info } row)
        {
            return;
        }

        _target.Apply([.. Selection.SelectMany(o => ResetPaths(o, row.Path, info).Select(p => (FormChange)new RemoveProperty(o.QualifiedName, p)))]);
        Refresh();
    }

    /// <summary>DSG-OI-050: Space / double-click on a Boolean row or a set member row toggles and commits at once.</summary>
    public void ToggleBoolean()
    {
        if (!CanEdit || Page != InspectorPage.Properties || CurrentRow is not { IsReadOnly: false, Info: { } info } row)
        {
            return;
        }

        var changes = new List<FormChange>();
        if (row.SetMember is { } member)
        {
            var path = row.Path[..row.Path.LastIndexOf('.')];
            var members = SetMembers(info);
            foreach (var obj in Selection)
            {
                var chosen = (Effective(obj, path, info) is SetValue s ? s.Elements : []).Where(e => !Names.Equals(e, member)).ToList();
                if (row.Display != "True")
                {
                    chosen.Add(member);
                }

                changes.Add(new SetProperty(obj.QualifiedName, path, new SetValue([.. members.Where(m => chosen.Contains(m, Names))])));
            }
        }
        else if (info.Type.Equals("Boolean", StringComparison.OrdinalIgnoreCase))
        {
            var value = new IdentValue(row.Display == "True" ? "False" : "True");
            changes.AddRange(Selection.Select(o => (FormChange)new SetProperty(o.QualifiedName, row.Path, value)));
        }
        else
        {
            return;
        }

        _target.Apply(changes);
        Refresh();
    }

    /// <summary>
    /// The drop-down list of a row (§5.1): a component reference lists <c>(none)</c> and the target's references; an enum
    /// its members; a Boolean False and True.
    /// </summary>
    public IReadOnlyList<string> ValueList(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (Selection.Count == 0 || Selection[^1].Info is not { } info || Catalog.FindPropertyPath(info, path) is not { } property)
        {
            return [];
        }

        return property.Kind switch
        {
            PropertyKind.Component => ["(none)", .. _target.References(property)],
            PropertyKind.Enum => [.. Catalog.FindType(property.Type)?.Values ?? []],
            _ when property.Type.Equals("Boolean", StringComparison.OrdinalIgnoreCase) => ["False", "True"],
            _ when IsShortCut(property) => ShortCutChoices,
            _ => [],
        };
    }

    /// <summary>Object-inspector.md §5.1: the drop-down of a <c>TShortCut</c> row — <c>(None)</c>, Ctrl+A … Ctrl+Z, F1 … F12.</summary>
    public static IReadOnlyList<string> ShortCutChoices { get; } =
    [
        ShortCutText.None,
        .. Enumerable.Range('A', 26).Select(c => "Ctrl+" + (char)c),
        .. Enumerable.Range(1, 12).Select(n => "F" + n.ToString(CultureInfo.InvariantCulture)),
    ];

    /// <summary>A <c>TShortCut</c> property (<c>editor: "shortcut"</c>).</summary>
    public static bool IsShortCut(PropertyInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return string.Equals(info.Editor, "shortcut", StringComparison.OrdinalIgnoreCase) || info.Type.Equals("TShortCut", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The drop-down list of the current row: enum members, Booleans, references, compatible handlers; empty for none.</summary>
    public IReadOnlyList<string> CurrentValueList()
    {
        if (EditDisplay() is null)
        {
            return [];
        }

        return Page == InspectorPage.Events ? EventMethods(CurrentEvent!) : ValueList(CurrentPath!);
    }

    /// <summary>A value chosen from the list is entered and committed; false when the entry was refused.</summary>
    public bool ChooseValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return BeginEdit(value) && Commit();
    }

    /// <summary>The value text of the current row (Events page: the handler name); null without a current row.</summary>
    public string? CurrentValueText =>
        Page == InspectorPage.Events
            ? CurrentEvent is { } name ? EventRows.FirstOrDefault(r => Names.Equals(r.Name, name))?.Value : null
            : CurrentRow?.Display;

    /// <summary>
    /// DSG-OI-032 (D-128): the entry the opened value list starts at — the current value text, or for a component reference
    /// written qualified with the header name of the file that stores it (<c>FooterFrame.Note</c>, RTL-1104) its target
    /// (<c>Note</c>).
    /// </summary>
    public string? CurrentValueListEntry =>
        Page == InspectorPage.Properties && CurrentRow is { Info.Kind: PropertyKind.Component } row && Selection is [var obj]
        && PropertyAccess.EffectiveLine(obj, row.Path)?.Line.Value is QualifiedIdentValue
        && PropertyAccess.ReferencedName(obj, row.Path) is { } name
            ? name
            : CurrentValueText;

    /// <summary><c>objectInspector.copyPropertyName</c>: the current row's path; null without one.</summary>
    public string? CurrentPropertyName => Page == InspectorPage.Events ? CurrentEvent : CurrentRow?.Path;

    /// <summary>
    /// DSG-OI-032: double-click on the value cell of an enum or Boolean row cycles to the next value (wrapping) and
    /// commits. False when the row has no such list.
    /// </summary>
    public bool CycleValue()
    {
        if (Page != InspectorPage.Properties || !CanEdit || CurrentRow is not { IsReadOnly: false, Info: { } info } row)
        {
            return false;
        }

        if (row.SetMember is not null || info.Type.Equals("Boolean", StringComparison.OrdinalIgnoreCase))
        {
            ToggleBoolean();
            return true;
        }

        if (info.Kind != PropertyKind.Enum || ValueList(row.Path) is not { Count: > 0 } list)
        {
            return false;
        }

        var index = list.ToList().FindIndex(v => Names.Equals(v, row.Display));
        if (!BeginEdit())
        {
            return false;
        }

        EditText = list[(index + 1) % list.Count];
        return Commit();
    }

    // ---- the dialog editors (§5.5, §5.7) ---------------------------------------------------------------------------

    /// <summary>A <c>TStrings</c> row (<c>editor: "strings"</c>): read-only text with the button (object-inspector.md §5.1).</summary>
    public static bool IsStringsRow(PropertyRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row is { Kind: PropertyRowKind.Property, Source: PropertyRowSource.Metadata, Info: { } info }
            && (info.Type.Equals("TStrings", StringComparison.OrdinalIgnoreCase) || string.Equals(info.Editor, "strings", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A <c>"kind": "collection"</c> row: read-only text with the <c>…</c> button (object-inspector.md §5.1).</summary>
    public static bool IsCollectionRow(PropertyRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row is { Kind: PropertyRowKind.Property, Source: PropertyRowSource.Metadata, Info.Kind: PropertyKind.Collection };
    }

    /// <summary>A row whose value is edited in a dialog opened by its <c>…</c> button, Enter or double-click.</summary>
    public static bool HasEditorButton(PropertyRow row) => IsStringsRow(row) || IsCollectionRow(row);

    /// <summary>
    /// The file picker of the <c>editor: "filename"</c> rows (object-inspector.md §5.1); null: such a row is plain text
    /// without the button (the embedded grids of the component editors).
    /// </summary>
    public Dialogs.FilePrompts? FilePrompts { get; set; }

    /// <summary>
    /// An <c>editor: "filename"</c> row (object-inspector.md §5.1): text that can always be typed, plus the <c>…</c> button
    /// that opens the file picker. Not a dialog-only row (<see cref="HasEditorButton"/>).
    /// </summary>
    public static bool IsFileNameRow(PropertyRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row is { Kind: PropertyRowKind.Property, Source: PropertyRowSource.Metadata, Info: { } info }
            && string.Equals(info.Editor, "filename", StringComparison.OrdinalIgnoreCase)
            && info.Type.Equals("string", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The row shows the <c>…</c> button: a dialog row, or a file-name row when the picker is available.</summary>
    public bool ShowsButton(PropertyRow row) => HasEditorButton(row) || (FilePrompts is not null && IsFileNameRow(row));

    /// <summary>
    /// What a picked file is stored as (component-metadata.md §3 <c>filename</c>): the path relative to the project
    /// directory when <paramref name="relativeToProject"/> and the file is inside it, otherwise the absolute path.
    /// </summary>
    public static string StoredFileName(string chosen, string? projectDirectory, bool relativeToProject)
    {
        ArgumentNullException.ThrowIfNull(chosen);
        if (!relativeToProject || string.IsNullOrEmpty(projectDirectory))
        {
            return chosen;
        }

        var root = Path.TrimEndingDirectorySeparator(projectDirectory) + Path.DirectorySeparatorChar;
        return chosen.Length > root.Length && chosen.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? chosen[root.Length..] : chosen;
    }

    /// <summary>
    /// The <c>…</c> button of a file-name row: the open-file picker with the filter of <c>editorOptions.filter</c>, starting
    /// in the folder of the stored file (else the project directory); the picked path is set on every selected object as
    /// one unit (<see cref="StoredFileName"/>). Cancel changes nothing.
    /// </summary>
    public async Task EditFileNameAsync()
    {
        if (FilePrompts is not { } prompts || !CanEdit || CurrentRow is not { IsReadOnly: false, Info: { } info } row || !IsFileNameRow(row))
        {
            return;
        }

        Cancel();
        var primary = Selection[^1];
        var project = prompts.ProjectDirectory;
        var options = info.EditorOptions;
        var filter = options is not null && options.TryGetValue("filter", out var f) ? f as string : null;
        var relative = options is not null && options.TryGetValue("relativeToProject", out var r) && r is true;
        var chosen = await prompts.PropertyFileAsync(filter, FolderOf(FileName(primary, row.Path, info), project));
        if (chosen is null || Selection.Count == 0 || !ReferenceEquals(Selection[^1], primary))
        {
            return;
        }

        var stored = StoredFileName(chosen, project, relative);
        var changes = Selection.Where(o => !string.Equals(FileName(o, row.Path, info), stored, StringComparison.Ordinal))
            .Select(o => (FormChange)new SetProperty(o.QualifiedName, row.Path, new StringValue(stored)))
            .ToList();
        if (changes.Count > 0)
        {
            _target.Apply(changes);
            Refresh();
        }
    }

    private string FileName(FormObject obj, string path, PropertyInfo info) => Effective(obj, path, info) is StringValue { Value: var s } ? s : "";

    /// <summary>The folder of a stored file name (a relative one is taken from the project directory); null when it has none.</summary>
    private static string? FolderOf(string fileName, string? projectDirectory)
    {
        try
        {
            var full = Path.IsPathFullyQualified(fileName) ? fileName
                : fileName.Length > 0 && !string.IsNullOrEmpty(projectDirectory) ? Path.Combine(projectDirectory, fileName) : null;
            return full is null ? null : Path.GetDirectoryName(Path.GetFullPath(full));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null; // any text can be typed into the row (":memory:")
        }
    }

    /// <summary>
    /// DSG-OI-055: opens <c>Dialog.StringsEditor</c> on the current <c>TStrings</c> row (initial text from the primary);
    /// OK with a changed text sets the list on every object (an empty list removes the property, it is the default) as one
    /// unit; unchanged text or Cancel changes nothing. A tab control's <c>Tabs</c> brings its <c>TabIndex</c> change into the
    /// same unit (DSG-RND-024, <see cref="TabControlRules.TabIndexAfterTabsEdit"/>). With <see cref="FilePrompts"/> the
    /// dialog has <c>Load…</c> and <c>Save…</c> of a text file (DSG-OI-130 … 134).
    /// </summary>
    public async Task EditStringsAsync()
    {
        if (_dialogs is null || !CanEdit || CurrentRow is not { IsReadOnly: false, Info: { } info } row || !IsStringsRow(row))
        {
            return;
        }

        var selection = Selection;
        var primary = selection[^1];
        var component = selection.Count == 1
            ? primary.Name
            : string.Format(CultureInfo.CurrentCulture, Strings.StringsEditor_Components, selection.Count);
        var result = await _dialogs.ShowAsync(new Dialogs.StringsEditorViewModel(row.Name, component, Lines(primary, row.Path, info), FilePrompts, _dialogs,
            Dialogs.StringsEditorViewModel.SuggestedFileName(row.Name, selection.Count == 1 ? primary.Name : null)));
        if (result is null || Selection.Count == 0 || !ReferenceEquals(Selection[^1], primary))
        {
            return;
        }

        var changes = new List<FormChange>();
        foreach (var o in Selection.Where(o => !Lines(o, row.Path, info).SequenceEqual(result, StringComparer.Ordinal)))
        {
            var before = Lines(o, row.Path, info);
            changes.Add(result.Count == 0
                ? new RemoveProperty(o.QualifiedName, row.Path)
                : new SetProperty(o.QualifiedName, row.Path, new StringListValue([.. result])));
            if (row.Path.Equals("Tabs", StringComparison.OrdinalIgnoreCase)
                && TabControlRules.TabIndexAfterTabsEdit(o, before, result) is { } tabIndex)
            {
                changes.Add(tabIndex); // DSG-RND-024: in the same unit
            }
        }

        if (changes.Count > 0)
        {
            _target.Apply(changes);
            Refresh();
        }
    }

    /// <summary>DSG-OI-110, §5.1: the collection editor needs exactly one selected, non-preserved component of a writable form.</summary>
    public bool IsSingleEditableSelection => Selection is [{ IsPreserved: false }];

    /// <summary>The collection editor can open on the current row (see <see cref="IsSingleEditableSelection"/>).</summary>
    public bool CanEditCollection =>
        _dialogs is not null && Designer?.Model is not null && CanEdit && Selection is [{ IsPreserved: false }]
        && CurrentRow is { IsReadOnly: false } row && IsCollectionRow(row);

    /// <summary>
    /// DSG-OI-110 … 114: opens <c>Dialog.CollectionEditor</c> on the current collection row; its OK result (when the
    /// working copy changed) is applied as one unit "Property"; Cancel or no change applies nothing.
    /// </summary>
    public async Task EditCollectionAsync()
    {
        if (!CanEditCollection || CurrentRow is not { } row)
        {
            return;
        }

        var component = Selection[0];
        var change = await _dialogs!.ShowAsync(new Dialogs.CollectionEditorViewModel(Catalog, Designer!.Model!, component, row.Path, _dialogs));
        if (change is not null && Selection is [var still] && ReferenceEquals(still, component))
        {
            _target.Apply([change]);
            Refresh();
        }
    }

    /// <summary>The <c>…</c> button of the current row (<see cref="ShowsButton"/>): the strings or collection editor, or the file picker.</summary>
    public Task EditInDialogAsync() =>
        CurrentRow is not { } row ? Task.CompletedTask
        : IsCollectionRow(row) ? EditCollectionAsync()
        : IsFileNameRow(row) ? EditFileNameAsync()
        : EditStringsAsync();

    /// <summary>
    /// Enter on a row: the Events page creates or shows the handler (DSG-OI-083), a dialog row opens its editor, other rows
    /// enter edit mode (DSG-OI-032).
    /// </summary>
    public async Task ActivateCurrentAsync()
    {
        if (Page == InspectorPage.Events)
        {
            if (CurrentEvent is { } name)
            {
                ActivateEvent(name);
            }

            return;
        }

        if (CurrentRow is { } row && HasEditorButton(row))
        {
            await EditInDialogAsync();
            return;
        }

        BeginEdit();
    }

    // ---- the Events page (§8) --------------------------------------------------------------------------------------

    /// <summary>DSG-OI-080, 086: the events every selected component has (same name and signature), by name; the filter applies (DSG-OI-020).</summary>
    public IReadOnlyList<EventRow> EventRows => [.. AllEvents().Where(e => _filter.Length == 0 || e.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase))];

    private IReadOnlyList<EventRow> AllEvents()
    {
        var selection = Selection;
        if (Designer is null || selection.Count == 0 || selection.Any(o => o.Info is null || o.IsPreserved))
        {
            return [];
        }

        var primary = selection[^1].Info!;
        return [.. primary.Events
            .Where(e => selection.All(o => o.Info!.FindEvent(e.Name) is { } other && Names.Equals(other.Signature, e.Signature)))
            .OrderBy(e => e.Name, Names)
            .Select(EventRowOf)];
    }

    /// <summary>DSG-OI-120: the own handler; with none on every selected component, the one the frame assigns.</summary>
    private EventRow EventRowOf(EventInfo e)
    {
        var own = CommonHandler(e.Name);
        return own is "" && InheritedHandler(e.Name) is { } inherited
            ? new EventRow(e.Name, e.Signature, inherited.Method) { IsInherited = true }
            : new EventRow(e.Name, e.Signature, own ?? "");
    }

    /// <summary>The handler a frame assigns to the event, when it is the same for every selected component; null otherwise.</summary>
    private (AncestorDefinition Frame, string Method)? InheritedHandler(string eventName)
    {
        var handlers = Selection.Select(o => o.InheritedHandler(eventName)).ToList();
        return handlers.Count > 0 && handlers[0] is { } first
            && handlers.All(h => h is { } x && ReferenceEquals(x.Frame, first.Frame) && Names.Equals(x.Method, first.Method))
            ? first
            : null;
    }

    /// <summary>
    /// DSG-OI-125, DSG-CS-120: the ancestor form's handler of the event when it is the same for every selected component
    /// (inherited root or inherited components outside frame instances, none with a line of its own); null otherwise.
    /// </summary>
    private string? AncestorHandler(string eventName)
    {
        if (Designer?.Model is not { } model)
        {
            return null;
        }

        var handlers = Selection.Select(o => DesignSurfaceController.AncestorFormHandler(model, o, eventName)).ToList();
        return handlers.Count > 0 && handlers[0] is { } first && handlers.All(h => Names.Equals(h, first)) ? first : null;
    }

    /// <summary>
    /// DSG-OI-125: <c>Show Inherited Handler</c> is enabled — the row shows an inherited handler (no own line, the same
    /// inherited handler on every selected component).
    /// </summary>
    public bool CanShowInheritedHandler(string eventName)
    {
        ArgumentNullException.ThrowIfNull(eventName);
        return FindEvent(eventName) is { } ev && CommonHandler(ev.Name) is "" && InheritedHandler(ev.Name) is not null;
    }

    /// <summary>
    /// DSG-OI-125: <c>Show Inherited Handler</c> — the definition storing the inherited handler (an ancestor form or a frame,
    /// DSG-FD-135) is opened at the method by the IDE (<see cref="FormDesignerViewModel.InheritedHandlerRequested"/>).
    /// </summary>
    public void ShowInheritedHandler(string eventName)
    {
        ArgumentNullException.ThrowIfNull(eventName);
        if (CanShowInheritedHandler(eventName) && InheritedHandler(FindEvent(eventName)!.Name) is { } inherited)
        {
            Designer!.RequestInheritedHandler(inherited.Frame, inherited.Method);
        }
    }

    /// <summary>DSG-OI-087: the selection has no events.</summary>
    public bool HasNoEvents => Designer is not null && Selection.Count > 0 && AllEvents().Count == 0;

    /// <summary>The Events page cannot change anything: no unit (code-sync.md §8 <c>NoUnit</c>) or a read-only form.</summary>
    public bool IsEventsReadOnly => Designer is not { HasUnit: true } d || _target.IsReadOnly || d.CodeSync.State != CodeSyncState.Ok;

    /// <summary>DSG-OI-081: the methods of the form class compatible with the event, by name.</summary>
    public IReadOnlyList<string> EventMethods(string eventName) =>
        FindEvent(eventName) is { } ev ? Designer!.CodeSync.GetCompatibleMethods(ev.Signature) : [];

    /// <summary>
    /// DSG-OI-082: <c>&lt;Component&gt;&lt;Event without On&gt;</c> (the form root: <c>Form…</c>) of the primary component;
    /// while a member of that name exists that is not a compatible method, or a component has it, 2, 3, … are appended;
    /// DSG-CS-122: in a descendant no handler name of the ancestor chain is taken.
    /// </summary>
    public string DefaultHandlerName(string eventName)
    {
        ArgumentNullException.ThrowIfNull(eventName);
        var ev = FindEvent(eventName) ?? throw new ArgumentException("Unknown event " + eventName, nameof(eventName));
        var designer = Designer!;
        var obj = Selection[^1];
        var part = ev.Name.StartsWith("On", StringComparison.OrdinalIgnoreCase) ? ev.Name[2..] : ev.Name;
        var stem = DesignSurfaceController.HandlerComponentPart(obj) + part;
        var compatible = designer.CodeSync.GetCompatibleMethods(ev.Signature);
        var taken = DesignSurfaceController.AncestorHandlerNames(designer.Model!); // DSG-CS-122
        bool Usable(string name) => !taken.Contains(name) && (compatible.Contains(name, Names)
            || (designer.CodeSync.LookupMember(name) == MemberLookup.None && designer.Model!.FindByName(name) is null));

        var candidate = stem;
        for (var n = 2; !Usable(candidate); n++)
        {
            candidate = stem + n;
        }

        return candidate;
    }

    /// <summary>
    /// DSG-OI-083: double-click on an event row. A row with a value navigates to the method (<paramref name="navigate"/>).
    /// An empty row links the default name (a compatible method of that name is reused, else the handler is created) for
    /// all selected components in one "Event" unit, then shows it in the Code view unless <paramref name="navigate"/> is
    /// false (a component editor stays open, DSG-SUR-197).
    /// </summary>
    public void ActivateEvent(string eventName, bool navigate = true)
    {
        ArgumentNullException.ThrowIfNull(eventName);
        if (FindEvent(eventName) is not { } ev)
        {
            return;
        }

        // DSG-OI-125, DSG-CS-120: an ancestor form's handler M — the descendant's method M is opened, or created first as the
        // hiding stub (one "Event" unit, no form line).
        if (CommonHandler(ev.Name) is "" && AncestorHandler(ev.Name) is { } hidden)
        {
            var hiding = Designer!.CodeSync;
            if (hiding.LookupMember(hidden) == MemberLookup.Method)
            {
                if (navigate)
                {
                    hiding.NavigateToHandler(hidden);
                }
            }
            else if (!IsEventsReadOnly && Designer.Surface!.Commit(UndoTitles.Event, [], () => hiding.CreateInheritedHandler(hidden, SignatureText(ev)),
                         [new CreateInheritedHandlerOp(hidden, SignatureText(ev))]) && navigate)
            {
                hiding.NavigateToHandler(hidden);
            }

            RaiseRowsChanged();
            return;
        }

        // DSG-OI-120: a handler the frame assigns is shown in the frame's unit; nothing changes.
        if (CommonHandler(ev.Name) is "" && InheritedHandler(ev.Name) is { } inherited)
        {
            if (navigate)
            {
                Designer!.RequestInheritedHandler(inherited.Frame, inherited.Method);
            }

            return;
        }

        if (IsEventsReadOnly)
        {
            // DSG-OI-087: on a read-only form an assigned event still navigates to its method; nothing is created.
            if (navigate && Designer is { HasUnit: true } readOnly && CommonHandler(ev.Name) is { Length: > 0 } assigned)
            {
                readOnly.CodeSync.NavigateToHandler(assigned);
            }

            return;
        }

        var code = Designer!.CodeSync;
        if (CommonHandler(ev.Name) is { Length: > 0 } existing)
        {
            if (navigate)
            {
                code.NavigateToHandler(existing);
            }

            return;
        }

        var name = DefaultHandlerName(ev.Name);
        var linked = code.GetCompatibleMethods(ev.Signature).FirstOrDefault(m => Names.Equals(m, name));
        if ((linked is not null ? Assign(ev, linked) : Create(ev, name)) && navigate)
        {
            code.NavigateToHandler(linked ?? name);
        }

        RaiseRowsChanged();
    }

    /// <summary>
    /// DSG-OI-084, 085: a typed handler name <paramref name="text"/> for the event (the decision table of §8.3). False with
    /// <see cref="EditError"/> when it is refused.
    /// </summary>
    public bool CommitEvent(string eventName, string text)
    {
        ArgumentNullException.ThrowIfNull(eventName);
        ArgumentNullException.ThrowIfNull(text);
        EditError = null;
        if (IsEventsReadOnly || FindEvent(eventName) is not { } ev)
        {
            return false;
        }

        var designer = Designer!;
        var surface = designer.Surface!;
        var code = designer.CodeSync;
        var t = text.Trim();
        var v = CommonHandler(ev.Name) ?? "";
        var selection = Selection;
        if (t.Length == 0)
        {
            // Rows 1 and 3: clear the assigned values; the methods stay and become removal candidates (DSG-CS-071).
            var assigned = selection.Where(o => o.FindProperty(ev.Name) is not null).ToList();
            if (assigned.Count == 0)
            {
                return true;
            }

            foreach (var name in assigned.Select(o => o.FindProperty(ev.Name)!.Value).OfType<IdentValue>().Select(i => i.Name))
            {
                code.NoteHandlerCandidate(name);
            }

            return surface.Commit(UndoTitles.Event, [.. assigned.Select(o => (FormChange)new RemoveProperty(o.QualifiedName, ev.Name))]);
        }

        if (Names.Equals(t, v) || (v.Length == 0 && InheritedHandler(ev.Name) is { } inherited && Names.Equals(t, inherited.Method)))
        {
            return true; // row 2 (DSG-OI-120: the frame's handler as shown)
        }

        if (!IsIdentifier(t) || SyntaxFacts.IsReservedWord(t))
        {
            return Fail(Strings.Msg_Designer_HandlerNameInvalid); // row 4
        }

        if (code.GetCompatibleMethods(ev.Signature).FirstOrDefault(m => Names.Equals(m, t)) is { } method)
        {
            return Assign(ev, method); // row 5
        }

        switch (code.LookupMember(t))
        {
            case MemberLookup.Method when v.Length == 0: // row 6
                return Fail(DesignerMessages.Format("Msg.Designer.HandlerSignatureMismatch", t, ev.Name, SignatureText(ev)));
            case MemberLookup.Method: // row 10
                return Fail(DesignerMessages.Format("Msg.Designer.HandlerRenameConflict", designer.Unit.FormTypeName, t));
            case not MemberLookup.None:
                return Fail(DesignerMessages.Format("Msg.Designer.NameConflictsWithMember", designer.Unit.FormTypeName, t));
        }

        if (designer.Model!.FindByName(t) is not null)
        {
            return Fail(DesignerMessages.Format("Msg.Designer.NameConflictsWithMember", designer.Unit.FormTypeName, t));
        }

        // Row 8: one component, V referenced by this event only and declared: the method is renamed.
        if (v.Length > 0 && selection.Count == 1 && References(v) == 1 && code.LookupMember(v) == MemberLookup.Method)
        {
            var obj = selection[0];
            return surface.Commit(UndoTitles.Event, [new SetProperty(obj.QualifiedName, ev.Name, new IdentValue(t))],
                () => code.RenameHandler(v, t), [new RenameHandlerOp(v, t)]) || Fail(null);
        }

        // Rows 7 and 9: a new handler for this event (V, if any, becomes a removal candidate).
        if (v.Length > 0)
        {
            code.NoteHandlerCandidate(v);
        }

        return Create(ev, t) || Fail(null);
    }

    /// <summary>The Events page's current row stays when it is still listed, else the first event (DSG-OI-031).</summary>
    public void EnsureCurrentEvent()
    {
        var events = EventRows;
        if (CurrentEvent is null || !events.Any(r => Names.Equals(r.Name, CurrentEvent)))
        {
            CurrentEvent = events.FirstOrDefault()?.Name;
        }
    }

    /// <summary>The event of the primary component every selected component has; null otherwise.</summary>
    public EventInfo? FindEvent(string eventName) =>
        AllEvents().Any(r => Names.Equals(r.Name, eventName)) ? Selection[^1].Info!.FindEvent(eventName) : null;

    /// <summary>The signature text of an event (its declaration, else the type name).</summary>
    public string SignatureText(EventInfo ev)
    {
        ArgumentNullException.ThrowIfNull(ev);
        return Catalog.FindEventSignature(ev.Signature) ?? ev.Signature;
    }

    private bool Fail(string? message)
    {
        EditError = message ?? _target.FailureMessage;
        return false;
    }

    private bool Assign(EventInfo ev, string method)
    {
        var old = Selection.Select(o => o.FindProperty(ev.Name)?.Value).OfType<IdentValue>().Select(i => i.Name)
            .Where(n => !Names.Equals(n, method)).ToList();
        var changes = Selection.Select(o => (FormChange)new SetProperty(o.QualifiedName, ev.Name, new IdentValue(method))).ToList();
        if (!Designer!.Surface!.Commit(UndoTitles.Event, changes))
        {
            return false;
        }

        foreach (var name in old)
        {
            Designer.CodeSync.NoteHandlerCandidate(name);
        }

        return true;
    }

    private bool Create(EventInfo ev, string name)
    {
        var signature = SignatureText(ev);
        var changes = Selection.Select(o => (FormChange)new SetProperty(o.QualifiedName, ev.Name, new IdentValue(name))).ToList();
        var code = Designer!.CodeSync;
        return Designer.Surface!.Commit(UndoTitles.Event, changes, () => code.CreateHandler(name, signature), [new CreateHandlerOp(name, signature)]);
    }

    /// <summary>How many event properties of the form name <paramref name="method"/>.</summary>
    private int References(string method) =>
        Designer!.Model!.AllObjects().SelectMany(o => o.Properties)
            .Count(p => p.IsEvent && p.Value is IdentValue { Name: var n } && Names.Equals(n, method));

    /// <summary>The handler all selected components have for the event; "" when none has one, null when they differ.</summary>
    private string? CommonHandler(string eventName)
    {
        var values = Selection.Select(o => o.FindProperty(eventName)?.Value is IdentValue { Name: var n } ? n : "").Distinct(Names).ToList();
        return values.Count == 1 ? values[0] : null;
    }

    // ---- commits ---------------------------------------------------------------------------------------------------

    /// <summary>The header name of the root of <paramref name="obj"/>'s file when that file is a frame document (DSG-FD-117); else null.</summary>
    private static string? FrameHeaderName(FormObject obj)
    {
        var root = obj;
        while (root.Parent is { } parent)
        {
            root = parent;
        }

        return PropertyAccess.IsFrameRoot(root) ? root.Name : null;
    }

    /// <summary>A value row (§5): parsed per object (relative edits per object, DSG-OI-041), with the special effects of §9.1.</summary>
    private string? CommitValue(PropertyRow row, string text)
    {
        var converter = new ValueConverter(Catalog);
        var changes = new List<FormChange>();
        foreach (var obj in Selection)
        {
            var info = (obj.Info is { } component ? Catalog.FindPropertyPath(component, row.Path) : null) ?? row.Info!;
            var result = info.Kind == PropertyKind.Component
                ? converter.Parse(info, text, Effective(obj, row.Path, info), _target.References(info), FrameHeaderName(obj))
                : converter.Parse(info, text, Effective(obj, row.Path, info));
            if (!result.Succeeded)
            {
                return DesignerMessages.Format(result.MessageId!, [.. result.Args]);
            }

            changes.Add(new SetProperty(obj.QualifiedName, row.Path, result.Value!));
        }

        if (Designer is not null && Names.Equals(row.Path, "TabOrder"))
        {
            changes = TabOrderChanges(changes);
        }

        return _target.Apply(changes) ? null : _target.FailureMessage ?? "";
    }

    /// <summary>
    /// DSG-SUR-096: a TabOrder entry is clamped to 0 … n−1 and the siblings are renumbered in the same unit; DSG-SUR-240
    /// (D-133): an inherited sibling keeps its value (no override line), only an inherited control that was entered stores one.
    /// </summary>
    private List<FormChange> TabOrderChanges(List<FormChange> requested)
    {
        var sequences = new Dictionary<FormObject, List<FormObject>>(ReferenceEqualityComparer.Instance);
        var entered = new HashSet<FormObject>(ReferenceEqualityComparer.Instance);
        foreach (var change in requested.Cast<SetProperty>())
        {
            var obj = Designer!.Model!.FindByQualifiedName(change.ObjectName)!;
            if (!TabOrderRules.IsFocusable(obj) || change.Value is not IntegerValue { Value: var value })
            {
                continue;
            }

            var parent = obj.Parent!;
            if (!sequences.TryGetValue(parent, out var sequence))
            {
                sequence = [.. TabOrderRules.Sequence(parent)];
                sequences[parent] = sequence;
            }

            sequence.Remove(obj);
            sequence.Insert((int)Math.Clamp(value, 0, sequence.Count), obj);
            entered.Add(obj);
        }

        return [.. sequences.Values.SelectMany(s => TabOrderRules.Number(s)).Where(c =>
            Designer!.Model!.FindByQualifiedName(((SetProperty)c).ObjectName) is { } o && (!o.IsInherited || entered.Contains(o)))];
    }

    /// <summary>DSG-OI-070 (validation order), 071 (effects): the rename of the primary component (one "Rename" unit).</summary>
    private string? CommitName(string text)
    {
        if (Designer is not { } designer)
        {
            return null;
        }

        var obj = Selection[^1];
        var name = text.Trim();
        var old = obj.Name;
        if (name.Length == 0 || name.Length > ObjectInspectorViewModel.MaxNameLength || !IsIdentifier(name))
        {
            return Strings.Msg_Designer_NameInvalid;
        }

        if (SyntaxFacts.IsReservedWord(name))
        {
            return DesignerMessages.Format("Msg.Designer.NameReserved", name);
        }

        if ((designer.Model!.FindByName(name) is { } other && !ReferenceEquals(other, obj))
            || designer.AncestorRename?.IsNameTaken(designer, obj, name) == true) // D-137: a descendant has the name
        {
            return DesignerMessages.Format("Msg.Designer.NameDuplicate", name);
        }

        var code = designer.CodeSync;
        if (!Names.Equals(name, old) && code.LookupMember(name) != MemberLookup.None)
        {
            return DesignerMessages.Format("Msg.Designer.NameConflictsWithMember", designer.Unit.FormTypeName, name);
        }

        if (name == old)
        {
            return null;
        }

        if (designer.ComponentRename is { } rename)
        {
            // DSG-CS-130, 131: the caller gets "accepted" while the code references are prepared; the row shows the
            // present name until the commit changes the model (a rename that completed at once answers as before).
            if (rename.IsPreparing(designer))
            {
                return null; // DSG-CS-131: a further rename is not started; the row shows the present name
            }

            return rename.Rename(designer, obj, name, [], UndoTitles.Rename) != false
                ? null
                : designer.AncestorRename?.LastFailure ?? _target.FailureMessage ?? "";
        }

        return RenameUnit(designer, obj, name, [], UndoTitles.Rename) ? null : designer.AncestorRename?.LastFailure ?? _target.FailureMessage ?? "";
    }

    /// <summary>
    /// DSG-OI-071: renames <paramref name="obj"/> to <paramref name="name"/> (validated) with its field (the form root: the
    /// form class and variable) through code sync, as one unit <paramref name="titleKey"/>; a Caption or Text equal to the
    /// old name follows; <paramref name="before"/> changes are applied first in the same unit (DSG-SUR-197: the committed
    /// caption whose text gives the name). D-137: the project's descendants of the form follow in the same unit; refused
    /// (false) when one of them has a component of that name or a descendant file cannot be written.
    /// </summary>
    internal static bool RenameUnit(FormDesignerViewModel designer, FormObject obj, string name, IReadOnlyList<FormChange> before, string titleKey) =>
        RenameUnit(designer, obj, name, before, titleKey, null, out _);

    /// <summary>
    /// <see cref="RenameUnit(FormDesignerViewModel, FormObject, string, IReadOnlyList{FormChange}, string)"/> with
    /// <paramref name="codeReferences"/> (DSG-CS-132) joining the unit after the descendants of D-137; when the commit
    /// fails because one of the two could not be applied, <paramref name="failure"/> says why.
    /// </summary>
    internal static bool RenameUnit(FormDesignerViewModel designer, FormObject obj, string name, IReadOnlyList<FormChange> before, string titleKey,
        ILinkedEdits? codeReferences, out (string MessageId, object[] Args)? failure)
    {
        failure = null;
        var old = obj.Name;
        if (designer.AncestorRename?.IsNameTaken(designer, obj, name) == true)
        {
            return false;
        }

        var linked = LinkedPair.Of(designer.AncestorRename?.Prepare(designer, obj, name), codeReferences);
        var code = designer.CodeSync;
        var withCode = designer.HasUnit && code.State == CodeSyncState.Ok;
        string? newClass = null;
        Func<IReadOnlyList<TextEditRecord>>? edits = null;
        IReadOnlyList<CodeSyncOp> ops = [];
        if (withCode && obj.Parent is null)
        {
            edits = () =>
            {
                var result = code.RenameFormClassAndVariable(old, name);
                newClass = result.NewClassName;
                return result.Edits;
            };
            ops = [new RenameFormClassAndVariableOp(old, name)];
        }
        else if (withCode)
        {
            edits = () => code.RenameField(old, name).Edits;
            ops = [new RenameFieldOp(old, name)];
        }

        // The form part is built after the code part ran (the new class name comes from it).
        var changes = new DeferredChanges(() =>
        {
            List<FormChange> list = [.. before, new RenameObject(old, name)];
            if (newClass is not null)
            {
                list.Add(new RetypeObject(name, newClass));
            }

            foreach (var follow in (string[])["Caption", "Text"])
            {
                var value = before.OfType<SetProperty>().LastOrDefault(p => p.ObjectName == old && Names.Equals(p.Path, follow))?.Value
                    ?? obj.FindProperty(follow)?.Value;
                if (value is StringValue { Value: var text } && text == old)
                {
                    list.Add(new SetProperty(name, follow, new StringValue(name)));
                }
            }

            return list;
        });
        if (designer.Surface!.Commit(titleKey, changes, edits, ops, linked: linked))
        {
            return true;
        }

        failure = linked?.Failure;
        return false;
    }

    private FormValue Effective(FormObject obj, string path, PropertyInfo info) =>
        obj.FindProperty(path)?.Value ?? obj.InheritedValue(path) ?? PropertyAccess.DefaultValue(Catalog, info);

    private IReadOnlyList<string> Lines(FormObject obj, string path, PropertyInfo info) =>
        Effective(obj, path, info) is StringListValue list ? list.Lines : [];

    private ImmutableArray<string> SetMembers(PropertyInfo set) =>
        Catalog.FindType(set.Type) is { Kind: TypeKind.Set, ElementType: { } element } && Catalog.FindType(element) is { } members
            ? members.Values
            : [];

    /// <summary>The stored paths a reset removes: the path itself, or every stored sub-property of an object row.</summary>
    private static IEnumerable<string> StoredPaths(FormObject obj, string path, PropertyInfo info) =>
        info.Kind == PropertyKind.Object
            ? obj.Properties.Where(p => p.Path.StartsWith(path + ".", StringComparison.OrdinalIgnoreCase)).Select(p => p.Path).ToList()
            : obj.FindProperty(path) is { } stored ? [stored.Path] : [];

    /// <summary>DSG-FD-044: the geometry every component writes (no reset).</summary>
    private static bool IsAlwaysWritten(string path) =>
        path.ToLowerInvariant() is "left" or "top" or "width" or "height" or "clientwidth" or "clientheight";

    private bool IsTextEditable(PropertyRow row) =>
        !row.IsReadOnly && row.SetMember is null
        && ((row.Source == PropertyRowSource.Name && Designer is not null)
            || (row.Source == PropertyRowSource.Metadata && row.Info is { } info && info.Kind is not (PropertyKind.Object or PropertyKind.Collection)
                && !info.Type.Equals("TStrings", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(info.Editor, "picture", StringComparison.OrdinalIgnoreCase)));

    /// <summary>DSG-OI-070 (1): a letter or underscore, then letters, digits and underscores (ASCII).</summary>
    internal static bool IsIdentifier(string name) =>
        name.Length > 0 && (char.IsAsciiLetter(name[0]) || name[0] == '_') && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    private void RaiseRowsChanged()
    {
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(EventRows));
    }

    /// <summary>A change list computed when the commit applies it (after the code part ran).</summary>
    private sealed class DeferredChanges(Func<List<FormChange>> build) : IReadOnlyList<FormChange>
    {
        private List<FormChange>? _changes;

        private List<FormChange> Changes => _changes ??= build();

        public FormChange this[int index] => Changes[index];

        public int Count => Changes.Count;

        public IEnumerator<FormChange> GetEnumerator() => Changes.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

/// <summary>
/// The grid target of components of a form document's Design view: commits through the surface as one "Property" unit
/// (object-inspector.md §9), references exclude the edited components, a refused entry at focus loss goes to the status bar.
/// </summary>
public sealed class DesignerGridTarget(Func<FormDesignerViewModel?> designer, Func<IReadOnlyList<FormObject>> selection, IStatusService? status)
    : IPropertyGridTarget
{
    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(5); // UI-MSG-003 "Status"

    public FormDesignerViewModel? Designer => designer() is { Surface: not null } d ? d : null;

    public ComponentCatalog Catalog => designer()?.Catalog ?? ComponentCatalog.Empty;

    public IReadOnlyList<FormObject> Selection => Designer is null ? [] : selection();

    public bool IsReadOnly => Designer?.Surface!.IsReadOnly ?? true;

    public string? FailureMessage => status?.Message;

    public bool Apply(IReadOnlyList<FormChange> changes) => Designer?.Surface!.Commit(UndoTitles.Property, changes) ?? false;

    public IReadOnlyList<string> References(PropertyInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (Designer?.Model is not { } model)
        {
            return [];
        }

        var selected = new HashSet<FormObject>(Selection, ReferenceEqualityComparer.Instance);
        bool Candidate(FormObject o) => !selected.Contains(o) && o.Info is { } c && Catalog.IsAssignable(c.ClassName, info.Type);

        // D-126 (RTL-1104): in a frame document the frame itself can be named (by its header name), offered first.
        IEnumerable<string> root = PropertyAccess.IsFrameRoot(model.Root) && Candidate(model.Root) ? [model.Root.Name] : [];
        return [.. root, .. model.AllObjects()
            .Where(o => o.Parent is not null && Candidate(o))
            .Select(o => o.Name)
            .Order(StringComparer.OrdinalIgnoreCase)];
    }

    public void ReportError(string message) => status?.ShowMessage(message, StatusDuration);
}
