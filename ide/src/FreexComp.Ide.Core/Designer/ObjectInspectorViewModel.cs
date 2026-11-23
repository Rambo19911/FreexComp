using System.Collections.Immutable;
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
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.ToolWindows;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Designer;

/// <summary>The two pages of the Object Inspector (object-inspector.md §1).</summary>
public enum InspectorPage { Properties, Events }

/// <summary>Why an edit is committed: Enter keeps a failed edit open, focus loss cancels it (DSG-OI-035).</summary>
public enum InspectorCommitCause { Enter, FocusLost }

/// <summary>One row of the Events page: event name, signature type name and the handler name (blank when it differs).</summary>
public sealed record EventRow(string Name, string Signature, string Value);

/// <summary>
/// The Object Inspector (object-inspector.md): follows the selection of the active form document's Design view, shows
/// the Properties page (rows of <see cref="PropertyGridModel"/>) and the Events page, and commits edits as one designer
/// undo unit for all selected components (§9) through <see cref="DesignSurfaceController.Commit"/>. No Avalonia types.
/// </summary>
public sealed partial class ObjectInspectorViewModel : ToolWindowViewModel
{
    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(5); // UI-MSG-003 "Status"
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    /// <summary>DSG-OI-070: the longest component name.</summary>
    public const int MaxNameLength = 127;

    /// <summary>The tool window's name (tool-windows.md, IDE-ARCH-100).</summary>
    public const string ToolName = "ObjectInspector";

    private readonly IStatusService _status;
    private readonly ISettingsService? _settings;
    private readonly IDialogService? _dialogs;
    private FormDesignerViewModel? _designer;
    private PropertyGridModel? _grid;
    private ComponentCatalog? _gridCatalog;
    private string? _original;
    private string _filter = "";
    private bool _byName;
    private bool _showDescription = true;

    /// <param name="settings">The <c>objectInspector.*</c> settings (arrangement, description pane); null: in-memory defaults.</param>
    /// <param name="dialogs">Opens <c>Dialog.StringsEditor</c> (DSG-OI-055); null: the editor is unavailable.</param>
    public ObjectInspectorViewModel(IStatusService status, ISettingsService? settings = null, IDialogService? dialogs = null)
    {
        _status = status ?? throw new ArgumentNullException(nameof(status));
        _settings = settings;
        _dialogs = dialogs;
        if (settings is not null)
        {
            settings.Changed += (_, e) =>
            {
                if (e.Key.StartsWith("objectInspector.", StringComparison.Ordinal))
                {
                    OnPropertyChanged(nameof(IsArrangedByName));
                    OnPropertyChanged(nameof(ShowDescription));
                    RaiseRowsChanged();
                }
            };
        }
    }

    /// <summary>The Object Inspector tool window in the right upper group from Phase 5 (UI-WIN-011).</summary>
    public ToolWindowDefinition Definition => new(ToolName, "Cmd_view_objectInspector", ToolGroupId.RightUpper, 5, () => this);

    /// <summary>DSG-OI-010, 011: <c>objectInspector.arrangement</c> is <c>"name"</c>.</summary>
    public bool IsArrangedByName => _settings is { } s ? s.Get(SettingsCatalog.ObjectInspectorArrangement) == "name" : _byName;

    /// <summary>DSG-OI-012: <c>objectInspector.toggleCategorized</c> switches the arrangement and persists it.</summary>
    public void ToggleArrangement()
    {
        if (_settings is { } s)
        {
            s.Set(SettingsCatalog.ObjectInspectorArrangement, IsArrangedByName ? "category" : "name");
            return;
        }

        _byName = !_byName;
        OnPropertyChanged(nameof(IsArrangedByName));
        RaiseRowsChanged();
    }

    /// <summary>UI-DSG-110: <c>objectInspector.showDescription</c>.</summary>
    public bool ShowDescription => _settings is { } s ? s.Get(SettingsCatalog.ObjectInspectorShowDescription) : _showDescription;

    /// <summary><c>objectInspector.toggleDescription</c>.</summary>
    public void ToggleDescription()
    {
        if (_settings is { } s)
        {
            s.Set(SettingsCatalog.ObjectInspectorShowDescription, !ShowDescription);
            return;
        }

        _showDescription = !_showDescription;
        OnPropertyChanged(nameof(ShowDescription));
    }

    /// <summary>DSG-OI-003: <c>objectInspector.togglePage</c>.</summary>
    public void TogglePage() => Page = Page == InspectorPage.Properties ? InspectorPage.Events : InspectorPage.Properties;

    partial void OnPageChanged(InspectorPage value)
    {
        Cancel();
        EnsureCurrentEvent();
        RaiseRowsChanged();
    }

    /// <summary>The filter took effect (DSG-OI-020, 150 ms after the last keystroke).</summary>
    protected override void OnFilterApplied(string filter)
    {
        _filter = filter.Trim();
        EnsureCurrentEvent();
        RaiseRowsChanged();
    }

    /// <summary>Applies the filter text at once (tests, Escape in the filter box).</summary>
    public void ApplyFilterNow(string filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        FilterText = filter;
        OnFilterApplied(filter);
    }

    /// <summary>The page shown.</summary>
    [ObservableProperty]
    private InspectorPage _page;

    /// <summary>The path of the current row (DSG-OI-031).</summary>
    [ObservableProperty]
    private string? _currentPath;

    /// <summary>The text of the value editor while <see cref="IsEditing"/>.</summary>
    [ObservableProperty]
    private string _editText = "";

    /// <summary>The message of a refused entry (DSG-OI-035); null when there is none.</summary>
    [ObservableProperty]
    private string? _editError;

    [ObservableProperty]
    private bool _isEditing;

    /// <summary>No Design view is active: the empty state with <see cref="EmptyText"/> (DSG-OI-001).</summary>
    public bool IsEmpty => _designer is not { IsDesignActive: true };

    public string EmptyText => Strings.Msg_Inspector_NoSelection;

    /// <summary>The selected components, the last being the primary.</summary>
    public IReadOnlyList<FormObject> Selection => IsEmpty ? [] : _designer!.Surface!.Selection.Items;

    public IReadOnlyList<string> SelectedNames => [.. Selection.Select(o => o.Name)];

    /// <summary>The rows of the Properties page (re-read from the form on every access, DSG-OI-094).</summary>
    public IReadOnlyList<PropertyRow> Rows
    {
        get
        {
            if (IsEmpty || _grid is null)
            {
                return [];
            }

            _grid.SetSelection(Selection);
            _grid.Arrangement = IsArrangedByName ? PropertyArrangement.Name : PropertyArrangement.Category;
            _grid.Filter = _filter;
            return _grid.Rows;
        }
    }

    private PropertyRow? CurrentRow => Rows.FirstOrDefault(r => r.Kind == PropertyRowKind.Property && Names.Equals(r.Path, CurrentPath));

    private FormDesignerViewModel Designer => _designer!;

    private DesignSurfaceController Surface => _designer!.Surface!;

    private ComponentCatalog Catalog => _designer!.Catalog;

    /// <summary>The document whose Design view the inspector follows (null: none).</summary>
    public void Bind(FormDesignerViewModel? designer)
    {
        if (ReferenceEquals(designer, _designer))
        {
            return;
        }

        if (_designer is { } old)
        {
            old.SelectionChanged -= OnSelectionChanged;
            old.ActiveViewChanged -= OnSelectionChanged;
            old.ModelChanged -= OnModelChanged;
        }

        _designer = designer;
        _grid = null;
        _gridCatalog = null;
        if (designer is not null)
        {
            designer.SelectionChanged += OnSelectionChanged;
            designer.ActiveViewChanged += OnSelectionChanged;
            designer.ModelChanged += OnModelChanged;
        }

        OnSelectionChanged(this, EventArgs.Empty);
    }

    /// <summary>Expands an object or set row (DSG-OI-013).</summary>
    public void Expand(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        _grid?.ExpandedPaths.Add(path);
        RaiseRowsChanged();
    }

    public void Collapse(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        _grid?.ExpandedPaths.Remove(path);
        RaiseRowsChanged();
    }

    /// <summary>A click on a category row collapses or expands it (DSG-OI-013, UI-DSG-108).</summary>
    public void ToggleCategory(string category)
    {
        ArgumentNullException.ThrowIfNull(category);
        if (_grid is { } grid && !grid.CollapsedCategories.Remove(category))
        {
            grid.CollapsedCategories.Add(category);
        }

        RaiseRowsChanged();
    }

    // ---- the edit lifecycle (§4) ------------------------------------------------------------------------------------

    /// <summary>Enters edit mode on the current row with its display text; false for rows that are not edited as text.</summary>
    public bool BeginEdit() => BeginEdit(null);

    /// <summary>
    /// DSG-OI-032: enters edit mode on the current row; <paramref name="entry"/> (a typed character, pasted text) replaces
    /// the displayed value. On the Events page the handler name is edited (DSG-OI-084).
    /// </summary>
    public bool BeginEdit(string? entry)
    {
        string display;
        if (Page == InspectorPage.Events)
        {
            if (IsEventsReadOnly || CurrentEvent is not { } name || AllEvents().FirstOrDefault(r => Names.Equals(r.Name, name)) is not { } ev)
            {
                return false;
            }

            display = ev.Value;
        }
        else if (IsEmpty || Surface.IsReadOnly || CurrentRow is not { } row || !IsTextEditable(row))
        {
            return false;
        }
        else
        {
            display = row.Display;
        }

        _original = display;
        EditText = entry ?? display;
        EditError = null;
        IsEditing = true;
        return true;
    }

    /// <summary>
    /// Commits the edit (§4): unchanged text ends the edit without a unit (DSG-OI-034); an entry refused for any selected
    /// component applies nothing (DSG-OI-092) — Enter keeps the editor open with the message, focus loss cancels and shows
    /// it in the status bar (DSG-OI-035). True when the edit ended without an error.
    /// </summary>
    public bool Commit(InspectorCommitCause cause = InspectorCommitCause.Enter)
    {
        if (!IsEditing)
        {
            return true;
        }

        string? error;
        if (string.Equals(EditText, _original, StringComparison.Ordinal))
        {
            Cancel();
            return true;
        }

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
            RaiseRowsChanged();
            return true;
        }

        if (cause == InspectorCommitCause.FocusLost)
        {
            Cancel();
            _status.ShowMessage(error, StatusDuration);
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

    /// <summary>DSG-OI-038: a stored value of a writable row (not Name, not the always-written geometry) can be removed.</summary>
    public bool CanResetToDefault =>
        !IsEmpty && !Surface.IsReadOnly && CurrentRow is { Source: PropertyRowSource.Metadata, IsReadOnly: false, SetMember: null, Info: { } info } row
        && !IsAlwaysWritten(row.Path) && Selection.Any(o => StoredPaths(o, row.Path, info).Any());

    /// <summary>DSG-OI-038: removes the stored value (an object row: all its stored sub-properties) of every selected component.</summary>
    public void ResetToDefault()
    {
        if (!CanResetToDefault || CurrentRow is not { Info: { } info } row)
        {
            return;
        }

        var changes = Selection.SelectMany(o => StoredPaths(o, row.Path, info).Select(p => (FormChange)new RemoveProperty(o.Name, p))).ToList();
        Surface.Commit(UndoTitles.Property, changes);
        RaiseRowsChanged();
    }

    /// <summary>Space / double-click on a Boolean row or a set member row: the value toggles and is committed at once (DSG-OI-050).</summary>
    public void ToggleBoolean()
    {
        if (IsEmpty || Surface.IsReadOnly || CurrentRow is not { IsReadOnly: false, Info: { } info } row)
        {
            return;
        }

        List<FormChange> changes = [];
        if (row.SetMember is { } member)
        {
            var path = row.Path[..row.Path.LastIndexOf('.')];
            var add = row.Display != "True";
            foreach (var obj in Selection)
            {
                var set = Effective(obj, path, info) is SetValue s ? s.Elements : [];
                var members = SetMembers(info);
                var chosen = set.Where(e => !Names.Equals(e, member)).ToList();
                if (add)
                {
                    chosen.Add(member);
                }

                changes.Add(new SetProperty(obj.Name, path, new SetValue([.. members.Where(m => chosen.Contains(m, Names))])));
            }
        }
        else if (info.Type.Equals("Boolean", StringComparison.OrdinalIgnoreCase))
        {
            var value = new IdentValue(row.Display == "True" ? "False" : "True");
            changes.AddRange(Selection.Select(o => (FormChange)new SetProperty(o.Name, row.Path, value)));
        }
        else
        {
            return;
        }

        Surface.Commit(UndoTitles.Property, changes);
        RaiseRowsChanged();
    }

    /// <summary>
    /// The drop-down list of a row (§5.1): a component reference lists <c>(none)</c> and the form's components assignable
    /// to the property's class, without the selected ones, by name; an enum its members; a Boolean False and True.
    /// </summary>
    public IReadOnlyList<string> ValueList(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (IsEmpty || Selection[^1].Info is not { } component || Catalog.FindPropertyPath(component, path) is not { } info)
        {
            return [];
        }

        return info.Kind switch
        {
            PropertyKind.Component => ["(none)", .. References(info)],
            PropertyKind.Enum => [.. Catalog.FindType(info.Type)?.Values ?? []],
            _ when info.Type.Equals("Boolean", StringComparison.OrdinalIgnoreCase) => ["False", "True"],
            _ => [],
        };
    }

    // ---- the Events page (§8) --------------------------------------------------------------------------------------

    /// <summary>DSG-OI-080, 086: the events every selected component has (same name and signature), by name; the filter applies (DSG-OI-020).</summary>
    public IReadOnlyList<EventRow> EventRows => [.. AllEvents().Where(e => _filter.Length == 0 || e.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase))];

    private IReadOnlyList<EventRow> AllEvents()
    {
        var selection = Selection;
        if (selection.Count == 0 || selection.Any(o => o.Info is null || o.IsPreserved))
        {
            return [];
        }

        var primary = selection[^1].Info!;
        return [.. primary.Events
            .Where(e => selection.All(o => o.Info!.FindEvent(e.Name) is { } other && Names.Equals(other.Signature, e.Signature)))
            .OrderBy(e => e.Name, Names)
            .Select(e => new EventRow(e.Name, e.Signature, CommonHandler(e.Name) ?? ""))];
    }

    /// <summary>DSG-OI-087: the selection has no events.</summary>
    public bool HasNoEvents => !IsEmpty && AllEvents().Count == 0;

    /// <summary>The Events page cannot change anything: no unit (code-sync.md §8 <c>NoUnit</c>) or a read-only form.</summary>
    public bool IsEventsReadOnly => IsEmpty || !Designer.HasUnit || Surface.IsReadOnly || Designer.CodeSync.State != CodeSyncState.Ok;

    /// <summary>DSG-OI-081: the methods of the form class compatible with the event, by name.</summary>
    public IReadOnlyList<string> EventMethods(string eventName) =>
        FindEvent(eventName) is { } ev ? Designer.CodeSync.GetCompatibleMethods(ev.Signature) : [];

    /// <summary>
    /// DSG-OI-082: <c>&lt;Component&gt;&lt;Event without On&gt;</c> (the form root: <c>Form…</c>) of the primary component;
    /// while a member of that name exists that is not a compatible method, or a component has it, 2, 3, … are appended.
    /// </summary>
    public string DefaultHandlerName(string eventName)
    {
        ArgumentNullException.ThrowIfNull(eventName);
        var ev = FindEvent(eventName) ?? throw new ArgumentException("Unknown event " + eventName, nameof(eventName));
        var obj = Selection[^1];
        var part = ev.Name.StartsWith("On", StringComparison.OrdinalIgnoreCase) ? ev.Name[2..] : ev.Name;
        var stem = (obj.Parent is null ? "Form" : obj.Name) + part;
        var compatible = Designer.CodeSync.GetCompatibleMethods(ev.Signature);
        bool Usable(string name) => compatible.Contains(name, Names)
            || (Designer.CodeSync.LookupMember(name) == MemberLookup.None && Designer.Model!.FindByName(name) is null);

        var candidate = stem;
        for (var n = 2; !Usable(candidate); n++)
        {
            candidate = stem + n;
        }

        return candidate;
    }

    /// <summary>
    /// DSG-OI-083: double-click on an event row. A row with a value navigates to the method. An empty row links the default
    /// name (a compatible method of that name is reused, else the handler is created) for all selected components in one
    /// "Event" unit, then shows it in the Code view.
    /// </summary>
    public void ActivateEvent(string eventName)
    {
        ArgumentNullException.ThrowIfNull(eventName);
        if (IsEventsReadOnly || FindEvent(eventName) is not { } ev)
        {
            return;
        }

        if (CommonHandler(ev.Name) is { Length: > 0 } existing)
        {
            Designer.CodeSync.NavigateToHandler(existing);
            return;
        }

        var name = DefaultHandlerName(ev.Name);
        var compatible = Designer.CodeSync.GetCompatibleMethods(ev.Signature);
        var linked = compatible.FirstOrDefault(m => Names.Equals(m, name));
        if (linked is not null ? Assign(ev, linked) : Create(ev, name))
        {
            Designer.CodeSync.NavigateToHandler(linked ?? name);
        }
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

        var code = Designer.CodeSync;
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

            return Surface.Commit(UndoTitles.Event, [.. assigned.Select(o => (FormChange)new RemoveProperty(o.Name, ev.Name))]);
        }

        if (Names.Equals(t, v))
        {
            return true; // row 2
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
                return Fail(DesignerMessages.Format("Msg.Designer.HandlerRenameConflict", Designer.Unit.FormTypeName, t));
            case not MemberLookup.None:
                return Fail(DesignerMessages.Format("Msg.Designer.NameConflictsWithMember", Designer.Unit.FormTypeName, t));
        }

        if (Designer.Model!.FindByName(t) is not null)
        {
            return Fail(DesignerMessages.Format("Msg.Designer.NameConflictsWithMember", Designer.Unit.FormTypeName, t));
        }

        // Row 8: one component, V referenced by this event only and declared: the method is renamed.
        if (v.Length > 0 && selection.Count == 1 && References(v) == 1 && code.LookupMember(v) == MemberLookup.Method)
        {
            var obj = selection[0];
            return Surface.Commit(UndoTitles.Event, [new SetProperty(obj.Name, ev.Name, new IdentValue(t))],
                () => code.RenameHandler(v, t), [new RenameHandlerOp(v, t)]) || Fail(null);
        }

        // Rows 7 and 9: a new handler for this event (V, if any, becomes a removal candidate).
        if (v.Length > 0)
        {
            code.NoteHandlerCandidate(v);
        }

        return Create(ev, t) || Fail(null);
    }

    // ---- the view's operations (D-081) -------------------------------------------------------------------------------

    /// <summary>The current row of the Events page (the event name).</summary>
    [ObservableProperty]
    private string? _currentEvent;

    partial void OnCurrentPathChanged(string? value) => RaiseDescriptionChanged();

    partial void OnCurrentEventChanged(string? value) => RaiseDescriptionChanged();

    /// <summary>
    /// DSG-OI-002: the components of the form, the form first, then the others by name; item text <c>Name: TClassName</c>
    /// (UI-DSG-101).
    /// </summary>
    public IReadOnlyList<FormObject> Components
    {
        get
        {
            if (IsEmpty || Designer.Model is not { } model)
            {
                return [];
            }

            return [model.Root, .. model.AllObjects().Where(o => o.Parent is not null).OrderBy(o => o.Name, Names)];
        }
    }

    /// <summary>The instance selector's entries (UI-DSG-101).</summary>
    public IReadOnlyList<string> ComponentItems => [.. Components.Select(o => o.Name + ": " + o.TypeName)];

    /// <summary>The entry of the single selected component; -1 for none or several.</summary>
    public int SelectedComponentIndex
    {
        get
        {
            var selection = Selection;
            if (selection.Count != 1)
            {
                return -1;
            }

            var components = Components;
            for (var i = 0; i < components.Count; i++)
            {
                if (ReferenceEquals(components[i], selection[0]))
                {
                    return i;
                }
            }

            return -1;
        }
    }

    /// <summary>UI-DSG-101: the closed selector's text with several components selected; null otherwise.</summary>
    public string? MultiSelectionText =>
        Selection.Count > 1 ? DesignerMessages.Format("Msg.Designer.SelectionMany", Selection.Count) : null;

    /// <summary>DSG-OI-002: choosing an entry selects that component on the surface.</summary>
    public void SelectComponent(int index)
    {
        var components = Components;
        if (index < 0 || index >= components.Count || SelectedComponentIndex == index)
        {
            return;
        }

        Surface.Selection.Select(components[index]);
    }

    /// <summary>The paths (Properties page) or event names (Events page) the current row moves over.</summary>
    private IReadOnlyList<string> NavigableRows =>
        Page == InspectorPage.Events
            ? [.. EventRows.Select(r => r.Name)]
            : [.. Rows.Where(r => r.Kind == PropertyRowKind.Property).Select(r => r.Path)];

    private string? Current => Page == InspectorPage.Events ? CurrentEvent : CurrentPath;

    /// <summary>Makes <paramref name="path"/> the current row of the shown page (a click, the end of an edit).</summary>
    public void SetCurrent(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (Page == InspectorPage.Events)
        {
            CurrentEvent = path;
        }
        else
        {
            CurrentPath = path;
        }
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
        var next = index < 0 ? (delta < 0 ? rows.Count - 1 : 0) : Math.Clamp(index + delta, 0, rows.Count - 1);
        SetCurrent(rows[next]);
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
        if (_grid is null)
        {
            return;
        }

        if (!expand)
        {
            _grid.ExpandedPaths.Clear();
        }
        else
        {
            // Expanding shows sub-rows that may expand in turn: repeat until nothing changes.
            for (var added = true; added;)
            {
                added = false;
                foreach (var row in Rows.Where(r => r.IsExpandable && !r.IsExpanded))
                {
                    added |= _grid.ExpandedPaths.Add(row.Path);
                }
            }
        }

        RaiseRowsChanged();
    }

    /// <summary><c>objectInspector.copyPropertyName</c>: the current row's path; null without one.</summary>
    public string? CurrentPropertyName => Page == InspectorPage.Events ? CurrentEvent : CurrentRow?.Path;

    /// <summary>
    /// DSG-OI-032: double-click on the value cell of an enum or Boolean row cycles to the next value (wrapping) and
    /// commits. False when the row has no such list.
    /// </summary>
    public bool CycleValue()
    {
        if (Page != InspectorPage.Properties || IsEmpty || Surface.IsReadOnly || CurrentRow is not { IsReadOnly: false, Info: { } info } row)
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

        var index = -1;
        for (var i = 0; i < list.Count; i++)
        {
            if (Names.Equals(list[i], row.Display))
            {
                index = i;
            }
        }

        if (!BeginEdit())
        {
            return false;
        }

        EditText = list[(index + 1) % list.Count];
        return Commit();
    }

    /// <summary>A <c>TStrings</c> row (<c>editor: "strings"</c>): read-only text with the button (object-inspector.md §5.1).</summary>
    public static bool IsStringsRow(PropertyRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row is { Kind: PropertyRowKind.Property, Source: PropertyRowSource.Metadata, Info: { } info }
            && (info.Type.Equals("TStrings", StringComparison.OrdinalIgnoreCase) || string.Equals(info.Editor, "strings", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// DSG-OI-055: opens <c>Dialog.StringsEditor</c> on the current <c>TStrings</c> row (initial text from the primary);
    /// OK with a changed text sets the list on every selected component (an empty list removes the property, it is the
    /// default) as one undo unit; unchanged text or Cancel changes nothing.
    /// </summary>
    public async Task EditStringsAsync()
    {
        if (_dialogs is null || IsEmpty || Surface.IsReadOnly || CurrentRow is not { IsReadOnly: false, Info: { } info } row || !IsStringsRow(row))
        {
            return;
        }

        var selection = Selection;
        var primary = selection[^1];
        var lines = Lines(primary, row.Path, info);
        var component = selection.Count == 1
            ? primary.Name
            : string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.StringsEditor_Components, selection.Count);
        var result = await _dialogs.ShowAsync(new Dialogs.StringsEditorViewModel(row.Name, component, lines));
        if (result is null || IsEmpty || !ReferenceEquals(Selection[^1], primary))
        {
            return;
        }

        var changes = new List<FormChange>();
        foreach (var obj in Selection)
        {
            if (Lines(obj, row.Path, info).SequenceEqual(result, StringComparer.Ordinal))
            {
                continue;
            }

            changes.Add(result.Count == 0
                ? new RemoveProperty(obj.Name, row.Path)
                : new SetProperty(obj.Name, row.Path, new StringListValue([.. result])));
        }

        if (changes.Count > 0)
        {
            Surface.Commit(UndoTitles.Property, changes);
            RaiseRowsChanged();
        }
    }

    private IReadOnlyList<string> Lines(FormObject obj, string path, PropertyInfo info) =>
        Effective(obj, path, info) is StringListValue list ? list.Lines : [];

    /// <summary>
    /// Enter on a row: the Events page creates or shows the handler (DSG-OI-083), a <c>TStrings</c> row opens its editor,
    /// other rows enter edit mode (DSG-OI-032).
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

        if (CurrentRow is { } row && IsStringsRow(row))
        {
            await EditStringsAsync();
            return;
        }

        BeginEdit();
    }

    /// <summary>DSG-OI-100: the current row's name (<c>type.bodyStrong</c>), empty without a current row.</summary>
    public string DescriptionTitle => IsEmpty ? "" : Page == InspectorPage.Events ? CurrentEvent ?? "" : CurrentRow?.Name ?? "";

    /// <summary>DSG-OI-100: the metadata description, else <c>Name: TypeName</c>; for events the signature text.</summary>
    public string DescriptionText
    {
        get
        {
            if (IsEmpty)
            {
                return "";
            }

            if (Page == InspectorPage.Events)
            {
                return CurrentEvent is { } name && FindEvent(name) is { } ev ? SignatureText(ev) : "";
            }

            return CurrentRow switch
            {
                { Info.Description: { Length: > 0 } description } => description,
                { Info: { } info } row => row.Name + ": " + info.Type,
                { } row => row.Name,
                null => "",
            };
        }
    }

    /// <summary>
    /// UI-DSG-112: the empty state of the grid area: <c>Msg.Inspector.NoSelection</c> without a Design view (DSG-OI-001),
    /// <c>Msg.Inspector.NoEvents</c>, <c>Msg.Inspector.FilterEmpty</c>; null when rows are shown.
    /// </summary>
    public string? EmptyStateText
    {
        get
        {
            if (IsEmpty)
            {
                return Strings.Msg_Inspector_NoSelection;
            }

            if (Page == InspectorPage.Events)
            {
                return HasNoEvents ? Strings.Msg_Inspector_NoEvents
                    : EventRows.Count == 0 ? DesignerMessages.Format("Msg.Inspector.FilterEmpty", _filter) : null;
            }

            return _filter.Length > 0 && !Rows.Any(r => r.Kind == PropertyRowKind.Property)
                ? DesignerMessages.Format("Msg.Inspector.FilterEmpty", _filter)
                : null;
        }
    }

    /// <summary>The secondary line under <see cref="EmptyStateText"/> (states-and-messages.md §7), or null.</summary>
    public string? EmptyStateSecondary => IsEmpty ? Strings.Msg_Inspector_NoSelection_Secondary : null;

    /// <summary>The Events page's current row stays when it is still listed, else the first event (DSG-OI-031).</summary>
    private void EnsureCurrentEvent()
    {
        var events = EventRows;
        if (CurrentEvent is null || !events.Any(r => Names.Equals(r.Name, CurrentEvent)))
        {
            CurrentEvent = events.FirstOrDefault()?.Name;
        }
    }

    private void RaiseDescriptionChanged()
    {
        OnPropertyChanged(nameof(DescriptionTitle));
        OnPropertyChanged(nameof(DescriptionText));
    }

    // ---- internals --------------------------------------------------------------------------------------------------

    private bool Fail(string? message)
    {
        EditError = message ?? _status.Message;
        return false;
    }

    private bool Assign(EventInfo ev, string method)
    {
        var old = Selection.Select(o => o.FindProperty(ev.Name)?.Value).OfType<IdentValue>().Select(i => i.Name)
            .Where(n => !Names.Equals(n, method)).ToList();
        var changes = Selection.Select(o => (FormChange)new SetProperty(o.Name, ev.Name, new IdentValue(method))).ToList();
        if (!Surface.Commit(UndoTitles.Event, changes))
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
        var changes = Selection.Select(o => (FormChange)new SetProperty(o.Name, ev.Name, new IdentValue(name))).ToList();
        return Surface.Commit(UndoTitles.Event, changes, () => Designer.CodeSync.CreateHandler(name, signature),
            [new CreateHandlerOp(name, signature)]);
    }

    private string SignatureText(EventInfo ev) => Catalog.FindEventSignature(ev.Signature) ?? ev.Signature;

    /// <summary>How many event properties of the form name <paramref name="method"/>.</summary>
    private int References(string method) =>
        Designer.Model!.AllObjects().SelectMany(o => o.Properties)
            .Count(p => p.IsEvent && p.Value is IdentValue { Name: var n } && Names.Equals(n, method));

    private EventInfo? FindEvent(string eventName) =>
        IsEmpty ? null : AllEvents().Any(r => Names.Equals(r.Name, eventName)) ? Selection[^1].Info!.FindEvent(eventName) : null;

    /// <summary>The handler all selected components have for the event; "" when none has one, null when they differ.</summary>
    private string? CommonHandler(string eventName)
    {
        var values = Selection.Select(o => o.FindProperty(eventName)?.Value is IdentValue { Name: var n } ? n : "").Distinct(Names).ToList();
        return values.Count == 1 ? values[0] : null;
    }

    /// <summary>A value row (§5): parsed per component (relative edits per component, DSG-OI-041), with the special effects of §9.1.</summary>
    private string? CommitValue(PropertyRow row, string text)
    {
        var path = row.Path;
        var changes = new List<FormChange>();
        foreach (var obj in Selection)
        {
            var info = Catalog.FindPropertyPath(obj.Info!, path) ?? row.Info!;
            var result = Converter.Parse(info, text, Effective(obj, path, info), info.Kind == PropertyKind.Component ? References(info) : null);
            if (!result.Succeeded)
            {
                return DesignerMessages.Format(result.MessageId!, [.. result.Args]);
            }

            changes.Add(new SetProperty(obj.Name, path, result.Value!));
        }

        if (Names.Equals(path, "TabOrder"))
        {
            changes = TabOrderChanges(changes);
        }

        return Surface.Commit(UndoTitles.Property, changes) ? null : _status.Message ?? "";
    }

    /// <summary>DSG-SUR-096: a TabOrder entry is clamped to 0 … n−1 and the siblings are renumbered in the same unit.</summary>
    private List<FormChange> TabOrderChanges(List<FormChange> requested)
    {
        var sequences = new Dictionary<FormObject, List<FormObject>>(ReferenceEqualityComparer.Instance);
        foreach (var change in requested.Cast<SetProperty>())
        {
            var obj = Designer.Model!.FindByName(change.ObjectName)!;
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
        }

        return [.. sequences.Values.SelectMany(TabOrderRules.Number)];
    }

    /// <summary>DSG-OI-070 (validation order), 071 (effects): the rename of the single selected component.</summary>
    private string? CommitName(string text)
    {
        var obj = Selection[^1];
        var name = text.Trim();
        var old = obj.Name;
        if (name.Length == 0 || name.Length > MaxNameLength || !IsIdentifier(name))
        {
            return Strings.Msg_Designer_NameInvalid;
        }

        if (SyntaxFacts.IsReservedWord(name))
        {
            return DesignerMessages.Format("Msg.Designer.NameReserved", name);
        }

        if (Designer.Model!.FindByName(name) is { } other && !ReferenceEquals(other, obj))
        {
            return DesignerMessages.Format("Msg.Designer.NameDuplicate", name);
        }

        var code = Designer.CodeSync;
        if (!Names.Equals(name, old) && code.LookupMember(name) != MemberLookup.None)
        {
            return DesignerMessages.Format("Msg.Designer.NameConflictsWithMember", Designer.Unit.FormTypeName, name);
        }

        if (name == old)
        {
            return null;
        }

        var withCode = Designer.HasUnit && code.State == CodeSyncState.Ok;
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
            List<FormChange> list = [new RenameObject(old, name)];
            if (newClass is not null)
            {
                list.Add(new RetypeObject(name, newClass));
            }

            foreach (var follow in (string[])["Caption", "Text"])
            {
                if (obj.FindProperty(follow) is { Value: StringValue { Value: var value } } && value == old)
                {
                    list.Add(new SetProperty(name, follow, new StringValue(name)));
                }
            }

            return list;
        });
        return Surface.Commit(UndoTitles.Rename, changes, edits, ops) ? null : _status.Message ?? "";
    }

    private ValueConverter Converter => new(Catalog);

    private IReadOnlyList<string> References(PropertyInfo info)
    {
        var selected = new HashSet<FormObject>(Selection, ReferenceEqualityComparer.Instance);
        return [.. Designer.Model!.AllObjects()
            .Where(o => o.Parent is not null && !selected.Contains(o) && o.Info is { } c && Catalog.IsAssignable(c.ClassName, info.Type))
            .Select(o => o.Name)
            .Order(Names)];
    }

    private FormValue Effective(FormObject obj, string path, PropertyInfo info) =>
        obj.FindProperty(path)?.Value ?? PropertyAccess.DefaultValue(Catalog, info);

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

    private static bool IsTextEditable(PropertyRow row) =>
        !row.IsReadOnly && row.SetMember is null
        && (row.Source == PropertyRowSource.Name
            || (row.Source == PropertyRowSource.Metadata && row.Info is { } info && info.Kind is not (PropertyKind.Object or PropertyKind.Collection)
                && !info.Type.Equals("TStrings", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(info.Editor, "picture", StringComparison.OrdinalIgnoreCase)));

    /// <summary>DSG-OI-070 (1): a letter or underscore, then letters, digits and underscores (ASCII).</summary>
    private static bool IsIdentifier(string name) =>
        name.Length > 0 && (char.IsAsciiLetter(name[0]) || name[0] == '_') && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        Cancel();
        if (_designer is { } designer && !ReferenceEquals(designer.Catalog, _gridCatalog))
        {
            NewGrid(designer.Catalog); // DSG-TP-008: the metadata was reloaded
        }

        // DSG-OI-031: the current row keeps its path when the new selection has it, else the first property row.
        var rows = Rows;
        if (CurrentPath is null || !rows.Any(r => r.Kind == PropertyRowKind.Property && Names.Equals(r.Path, CurrentPath)))
        {
            CurrentPath = rows.FirstOrDefault(r => r.Kind == PropertyRowKind.Property)?.Path;
        }

        EnsureCurrentEvent();
        RaiseRowsChanged();
    }

    private void NewGrid(ComponentCatalog catalog)
    {
        var grid = new PropertyGridModel(catalog);
        if (_grid is { } old)
        {
            grid.ExpandedPaths.UnionWith(old.ExpandedPaths);
            grid.CollapsedCategories.UnionWith(old.CollapsedCategories);
            grid.Arrangement = old.Arrangement;
            grid.Filter = old.Filter;
        }

        _grid = grid;
        _gridCatalog = catalog;
    }

    private void OnModelChanged(object? sender, EventArgs e) => RaiseRowsChanged();

    private void RaiseRowsChanged()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(EventRows));
        OnPropertyChanged(nameof(SelectedNames));
        OnPropertyChanged(nameof(ComponentItems));
        OnPropertyChanged(nameof(SelectedComponentIndex));
        OnPropertyChanged(nameof(MultiSelectionText));
        OnPropertyChanged(nameof(EmptyStateText));
        RaiseDescriptionChanged();
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
