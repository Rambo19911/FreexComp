using System.ComponentModel;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.Inspector;
using FreexComp.Designer.Model;
using FreexComp.Designer.Surface;
using FreexComp.Ide.Core.Commands;
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
public sealed record EventRow(string Name, string Signature, string Value)
{
    /// <summary>DSG-OI-120: <see cref="Value"/> is the handler the frame assigns (shown in <c>text.secondary</c>, not stored here).</summary>
    public bool IsInherited { get; init; }
}

/// <summary>
/// The Object Inspector (object-inspector.md): follows the selection of the active form document's Design view and shows
/// it in the shared property grid (<see cref="Grid"/>, D-110: the Properties and Events pages, the edit lifecycle, the
/// commits as one designer undo unit for all selected components, §9) with the inspector's own parts around it — the
/// instance selector, the filter, the arrangement, the description pane, the empty states and the session state. The
/// grid's operations are forwarded under their inspector names. No Avalonia types.
/// </summary>
public sealed partial class ObjectInspectorViewModel : ToolWindowViewModel, ICommandTarget
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    /// <summary>DSG-OI-070: the longest component name.</summary>
    public const int MaxNameLength = 127;

    /// <summary>The tool window's name (tool-windows.md, IDE-ARCH-100).</summary>
    public const string ToolName = "ObjectInspector";

    private readonly ISettingsService? _settings;
    private FormDesignerViewModel? _designer;
    private string _filter = "";

    // The last document whose Design view was bound (DSG-OI-021).
    private FormDesignerViewModel? _lastDesigner;
    private bool _byName;
    private bool _showDescription = true;

    /// <param name="settings">The <c>objectInspector.*</c> settings (arrangement, description pane); null: in-memory defaults.</param>
    /// <param name="dialogs">Opens <c>Dialog.StringsEditor</c> (DSG-OI-055) and the collection editor; null: they are unavailable.</param>
    /// <param name="filePrompts">The file picker of the <c>editor: "filename"</c> rows; null: they are plain text rows.</param>
    public ObjectInspectorViewModel(IStatusService status, ISettingsService? settings = null, IDialogService? dialogs = null,
        Dialogs.FilePrompts? filePrompts = null)
    {
        ArgumentNullException.ThrowIfNull(status);
        _settings = settings;
        Grid = new PropertyGridViewModel(new DesignerGridTarget(() => _designer, () => Selection, status), dialogs)
        {
            FilePrompts = filePrompts,
            Configure = model =>
            {
                model.CollapsedCategories = _collapsed;
                model.ExpandedPaths = ExpandedSet();
                model.Arrangement = IsArrangedByName ? PropertyArrangement.Name : PropertyArrangement.Category;
            },
        };
        Grid.PropertyChanged += OnGridPropertyChanged;
        Grid.ExpansionChanged += (_, _) => RaiseSessionStateChanged();
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

    /// <summary>D-110: the shared property grid the inspector runs on (the collection editor and the Menu Designer embed the same).</summary>
    public PropertyGridViewModel Grid { get; }

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

    /// <summary>The filter took effect (DSG-OI-020, 150 ms after the last keystroke).</summary>
    protected override void OnFilterApplied(string filter)
    {
        _filter = filter.Trim();
        Grid.Filter = _filter;
        Grid.EnsureCurrentEvent();
        RaiseRowsChanged();
    }

    /// <summary>Applies the filter text at once (tests, Escape in the filter box).</summary>
    public void ApplyFilterNow(string filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        FilterText = filter;
        OnFilterApplied(filter);
    }

    // ---- the grid's state under the inspector's names ----------------------------------------------------------------

    /// <summary>The page shown.</summary>
    public InspectorPage Page
    {
        get => Grid.Page;
        set => Grid.Page = value;
    }

    /// <summary>The path of the current row (DSG-OI-031).</summary>
    public string? CurrentPath
    {
        get => Grid.CurrentPath;
        set => Grid.CurrentPath = value;
    }

    /// <summary>The current row of the Events page (the event name).</summary>
    public string? CurrentEvent
    {
        get => Grid.CurrentEvent;
        set => Grid.CurrentEvent = value;
    }

    /// <summary>The text of the value editor while <see cref="IsEditing"/>.</summary>
    public string EditText
    {
        get => Grid.EditText;
        set => Grid.EditText = value;
    }

    /// <summary>The message of a refused entry (DSG-OI-035); null when there is none.</summary>
    public string? EditError
    {
        get => Grid.EditError;
        set => Grid.EditError = value;
    }

    public bool IsEditing => Grid.IsEditing;

    /// <summary>No Design view is active: the empty state with <see cref="EmptyText"/> (DSG-OI-001).</summary>
    public bool IsEmpty => _designer is not { IsDesignActive: true };

    public string EmptyText => Strings.Msg_Inspector_NoSelection;

    /// <summary>The selected components, the last being the primary.</summary>
    public IReadOnlyList<FormObject> Selection => IsEmpty ? [] : _designer!.Surface!.Selection.Items;

    public IReadOnlyList<string> SelectedNames => [.. Selection.Select(o => o.QualifiedName)];

    /// <summary>The rows of the Properties page (re-read from the form on every access, DSG-OI-094).</summary>
    public IReadOnlyList<PropertyRow> Rows => IsEmpty ? [] : Grid.Rows;

    private PropertyRow? CurrentRow => IsEmpty ? null : Grid.CurrentRow;

    private FormDesignerViewModel Designer => _designer!;

    private DesignSurfaceController Surface => _designer!.Surface!;

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
            old.SurfaceInvalidated -= OnSurfaceInvalidated;
        }

        _designer = designer;
        if (designer is not null)
        {
            // DSG-OI-021: the filter is kept across selection and view changes, cleared when another document is active.
            if (_lastDesigner is not null && !ReferenceEquals(_lastDesigner, designer) && FilterText.Length > 0)
            {
                ApplyFilterNow("");
            }

            _lastDesigner = designer;
            designer.SelectionChanged += OnSelectionChanged;
            designer.ActiveViewChanged += OnSelectionChanged;
            designer.ModelChanged += OnModelChanged;
            designer.SurfaceInvalidated += OnSurfaceInvalidated;
        }

        OnSelectionChanged(this, EventArgs.Empty);
    }

    /// <summary>Expands an object or set row (DSG-OI-013).</summary>
    public void Expand(string path) => Grid.Expand(path);

    public void Collapse(string path) => Grid.Collapse(path);

    /// <summary>A click on a category row collapses or expands it (DSG-OI-013, UI-DSG-108).</summary>
    public void ToggleCategory(string category) => Grid.ToggleCategory(category);

    // ---- the edit lifecycle (§4) ------------------------------------------------------------------------------------

    /// <summary>Enters edit mode on the current row with its display text; false for rows that are not edited as text.</summary>
    public bool BeginEdit() => BeginEdit(null);

    /// <summary>DSG-OI-032: enters edit mode on the current row; <paramref name="entry"/> replaces the displayed value.</summary>
    public bool BeginEdit(string? entry) => !IsEmpty && Grid.BeginEdit(entry);

    /// <summary>The text an edit of the current row starts from; null when the row is not edited as text.</summary>
    private string? EditDisplay() => IsEmpty ? null : Grid.EditDisplay();

    /// <summary>Commits the edit (§4, DSG-OI-034, 035, 092); true when the edit ended without an error.</summary>
    public bool Commit(InspectorCommitCause cause = InspectorCommitCause.Enter) => Grid.Commit(cause);

    /// <summary>Escape: the edit ends without a change.</summary>
    public void Cancel() => Grid.Cancel();

    /// <summary>DSG-OI-038: a stored value of a writable row (not Name, not the always-written geometry) can be removed.</summary>
    public bool CanResetToDefault => !IsEmpty && Grid.CanResetToDefault;

    /// <summary>DSG-OI-038: removes the stored value (an object row: all its stored sub-properties) of every selected component.</summary>
    public void ResetToDefault() => Grid.ResetToDefault();

    /// <summary>Space / double-click on a Boolean row or a set member row: the value toggles and is committed at once (DSG-OI-050).</summary>
    public void ToggleBoolean() => Grid.ToggleBoolean();

    /// <summary>The drop-down list of a row (§5.1).</summary>
    public IReadOnlyList<string> ValueList(string path) => IsEmpty ? [] : Grid.ValueList(path);

    // ---- the Events page (§8) --------------------------------------------------------------------------------------

    /// <summary>DSG-OI-080, 086: the events every selected component has (same name and signature), by name; the filter applies.</summary>
    public IReadOnlyList<EventRow> EventRows => IsEmpty ? [] : Grid.EventRows;

    /// <summary>DSG-OI-087: the selection has no events.</summary>
    public bool HasNoEvents => !IsEmpty && Grid.HasNoEvents;

    /// <summary>The Events page cannot change anything: no unit (code-sync.md §8 <c>NoUnit</c>) or a read-only form.</summary>
    public bool IsEventsReadOnly => IsEmpty || Grid.IsEventsReadOnly;

    /// <summary>DSG-OI-081: the methods of the form class compatible with the event, by name.</summary>
    public IReadOnlyList<string> EventMethods(string eventName) => Grid.EventMethods(eventName);

    /// <summary>DSG-OI-082: the default handler name of the event for the primary component.</summary>
    public string DefaultHandlerName(string eventName) => Grid.DefaultHandlerName(eventName);

    /// <summary>DSG-OI-083: double-click on an event row (navigate to, link or create the handler).</summary>
    public void ActivateEvent(string eventName) => Grid.ActivateEvent(eventName);

    /// <summary>DSG-OI-084, 085: a typed handler name for the event; false with <see cref="EditError"/> when it is refused.</summary>
    public bool CommitEvent(string eventName, string text) => Grid.CommitEvent(eventName, text);

    // ---- the view's operations (D-081) -------------------------------------------------------------------------------

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

            // DSG-OI-119: the inherited objects of an instance follow it, in tree order.
            return [model.Root, .. model.AllObjects().Where(o => o.Parent is not null && !o.IsInherited).OrderBy(o => o.Name, Names)
                .SelectMany(o => o.IsFrameInstance ? [o, .. Inherited(o)] : new[] { o })];
        }
    }

    private static IEnumerable<FormObject> Inherited(FormObject instance) =>
        instance.Children.Where(c => c.IsInherited).SelectMany(c => (IEnumerable<FormObject>)[c, .. Inherited(c)]);

    /// <summary>The instance selector's entries (UI-DSG-101).</summary>
    public IReadOnlyList<string> ComponentItems => [.. Components.Select(o => o.QualifiedName + ": " + o.TypeName)];

    /// <summary>UI-DSG-136: per entry of <see cref="ComponentItems"/>, the frame class an inherited object comes from, else null.</summary>
    public IReadOnlyList<string?> ComponentFrames => [.. Components.Select(o => o.InheritedFrame?.ClassName)];

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
    public void MoveCurrent(int delta) => Grid.MoveCurrent(delta);

    /// <summary>Home / End.</summary>
    public void MoveCurrentToEdge(bool end) => Grid.MoveCurrentToEdge(end);

    /// <summary>DSG-OI-030: Right expands the current object/set row, Left collapses it or moves to its parent row.</summary>
    public void ExpandCurrent(bool expand) => Grid.ExpandCurrent(expand);

    /// <summary><c>objectInspector.expandAll</c> / <c>collapseAll</c>: every expandable row of the selection.</summary>
    public void ExpandAll(bool expand)
    {
        if (!IsEmpty)
        {
            Grid.ExpandAll(expand);
        }
    }

    /// <summary><c>objectInspector.copyPropertyName</c>: the current row's path; null without one.</summary>
    public string? CurrentPropertyName => IsEmpty ? null : Grid.CurrentPropertyName;

    /// <summary>DSG-OI-032: double-click on the value cell of an enum or Boolean row cycles to the next value and commits.</summary>
    public bool CycleValue() => !IsEmpty && Grid.CycleValue();

    /// <summary>A <c>TStrings</c> row (<c>editor: "strings"</c>): read-only text with the button (object-inspector.md §5.1).</summary>
    public static bool IsStringsRow(PropertyRow row) => PropertyGridViewModel.IsStringsRow(row);

    /// <summary>DSG-OI-055: <c>Dialog.StringsEditor</c> on the current <c>TStrings</c> row.</summary>
    public Task EditStringsAsync() => IsEmpty ? Task.CompletedTask : Grid.EditStringsAsync();

    /// <summary>A <c>"kind": "collection"</c> row: read-only text with the <c>…</c> button (object-inspector.md §5.1).</summary>
    public static bool IsCollectionRow(PropertyRow row) => PropertyGridViewModel.IsCollectionRow(row);

    /// <summary>A row whose value is edited in a dialog opened by its <c>…</c> button, Enter or double-click.</summary>
    public static bool HasEditorButton(PropertyRow row) => PropertyGridViewModel.HasEditorButton(row);

    /// <summary>DSG-OI-110, §5.1: the collection editor needs exactly one selected, non-preserved component of a writable form.</summary>
    public bool CanEditCollection => !IsEmpty && Grid.CanEditCollection;

    /// <summary>DSG-OI-110 … 114: <c>Dialog.CollectionEditor</c> on the current collection row; OK with a change is one "Property" unit.</summary>
    public Task EditCollectionAsync() => IsEmpty ? Task.CompletedTask : Grid.EditCollectionAsync();

    /// <summary>The dialog of the current row (<see cref="HasEditorButton"/>): the strings or the collection editor.</summary>
    public Task EditInDialogAsync() => IsEmpty ? Task.CompletedTask : Grid.EditInDialogAsync();

    /// <summary>Enter on a row (DSG-OI-083, DSG-OI-032).</summary>
    public Task ActivateCurrentAsync() => IsEmpty ? Task.CompletedTask : Grid.ActivateCurrentAsync();

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
                return CurrentEvent is { } name && Grid.FindEvent(name) is { } ev ? Grid.SignatureText(ev) : "";
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

    private void RaiseDescriptionChanged()
    {
        OnPropertyChanged(nameof(DescriptionTitle));
        OnPropertyChanged(nameof(DescriptionText));
    }

    // ---- internals --------------------------------------------------------------------------------------------------

    /// <summary>The grid's changes under the inspector's property names (the view binds the inspector).</summary>
    private void OnGridPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PropertyGridViewModel.Page):
                OnPropertyChanged(nameof(Page));
                RaiseSessionStateChanged();
                RaiseRowsChanged();
                break;
            case nameof(PropertyGridViewModel.CurrentPath):
                OnPropertyChanged(nameof(CurrentPath));
                RaiseDescriptionChanged();
                break;
            case nameof(PropertyGridViewModel.CurrentEvent):
                OnPropertyChanged(nameof(CurrentEvent));
                RaiseDescriptionChanged();
                break;
            case nameof(PropertyGridViewModel.EditText) or nameof(PropertyGridViewModel.EditError) or nameof(PropertyGridViewModel.IsEditing):
                OnPropertyChanged(e.PropertyName);
                break;
            case nameof(PropertyGridViewModel.Rows):
                RaiseRowsChanged();
                break;
        }
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        Grid.SelectionChanged(); // DSG-OI-031; a reloaded catalog gets a new model (DSG-TP-008)
        RaiseRowsChanged();
    }

    private void OnModelChanged(object? sender, EventArgs e) => RaiseRowsChanged();

    /// <summary>DSG-OI-095: the live update interval during a drag on the surface.</summary>
    public static readonly TimeSpan LiveUpdateInterval = TimeSpan.FromMilliseconds(50);

    private IDisposable? _liveUpdate;

    /// <summary>
    /// DSG-OI-095: while a drag shows a preview layout, the rows are re-read at most every 50 ms (the first step of a
    /// window schedules one refresh at its end); the drop's model change refreshes at once.
    /// </summary>
    private void OnSurfaceInvalidated(object? sender, EventArgs e)
    {
        if (_liveUpdate is not null || Clock is not { } clock || _designer?.Surface is not { HasLayoutPreview: true })
        {
            return;
        }

        _liveUpdate = clock.Schedule(LiveUpdateInterval, () =>
        {
            _liveUpdate = null;
            if (_designer?.Surface is not null)
            {
                Grid.Refresh();
            }
        });
    }

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
}
