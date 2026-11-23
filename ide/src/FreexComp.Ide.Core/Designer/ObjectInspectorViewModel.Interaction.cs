using FreexComp.Designer.Model;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Menus;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.Designer;

/// <summary>
/// The Object Inspector state kept in <c>session.json</c> (object-inspector.md DSG-OI-003, DSG-OI-013; ui/designer.md
/// UI-DSG-103): the page, the collapsed categories, the expanded object/set rows per class name and the name column ratio.
/// </summary>
public sealed record ObjectInspectorSessionState(InspectorPage Page, IReadOnlyList<string> CollapsedCategories,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ExpandedRows, double NameColumnRatio);

/// <summary>
/// D-109: row copy and paste (DSG-OI-037), the row context menu (DSG-OI-039), value lists (DSG-OI-032 Alt+Down), the
/// session state, the name/value divider (UI-DSG-103) and the narrow caption menu (UI-DSG-113).
/// </summary>
public sealed partial class ObjectInspectorViewModel
{
    /// <summary>UI-DSG-103: the default share of the name column.</summary>
    public const double DefaultNameColumnRatio = 0.45;

    /// <summary>UI-DSG-103: the narrowest a column gets by dragging the divider.</summary>
    public const double MinColumnWidth = 60;

    /// <summary>UI-DSG-113: below this width the arrangement buttons move into the caption's <c>⋯</c> menu.</summary>
    public const double NarrowWidth = 260;

    private readonly HashSet<string> _collapsed = new(Names);
    private readonly Dictionary<string, HashSet<string>> _expanded = new(Names);
    private ICommandService? _commands;
    private IClipboardService? _clipboard;
    private double _nameColumnRatio = DefaultNameColumnRatio;
    private bool _isNarrow;
    private bool _restoring;

    /// <summary>A value of <see cref="SessionState"/> changed (the session schedules a write).</summary>
    public event EventHandler? SessionStateChanged;

    /// <summary>The commands the context menu shows and the clipboard of Copy/Paste (set by <see cref="ObjectInspectorCommands.Register"/>).</summary>
    public void AttachCommands(ICommandService commands, IClipboardService? clipboard)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _clipboard = clipboard;
    }

    // ---- session state ---------------------------------------------------------------------------------------------

    /// <summary>UI-DSG-103: the name column's share of the grid width (the view keeps both columns at least 60 DIP).</summary>
    public double NameColumnRatio
    {
        get => _nameColumnRatio;
        set
        {
            var ratio = double.IsFinite(value) ? Math.Clamp(value, 0.05, 0.95) : DefaultNameColumnRatio;
            if (SetProperty(ref _nameColumnRatio, ratio))
            {
                RaiseSessionStateChanged();
            }
        }
    }

    /// <summary>The state written to <c>session.json</c> (<c>objectInspector.*</c>).</summary>
    public ObjectInspectorSessionState SessionState => new(
        Page,
        [.. _collapsed.Order(Names)],
        _expanded.Where(e => e.Value.Count > 0).OrderBy(e => e.Key, Names)
            .ToDictionary(e => e.Key, e => (IReadOnlyList<string>)[.. e.Value.Order(Names)], Names),
        NameColumnRatio);

    /// <summary>Restores the state read from <c>session.json</c> (IDE-ARCH-071).</summary>
    public void RestoreSessionState(ObjectInspectorSessionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _restoring = true;
        try
        {
            _collapsed.Clear();
            _collapsed.UnionWith(state.CollapsedCategories);
            _expanded.Clear();
            foreach (var (className, paths) in state.ExpandedRows)
            {
                _expanded[className] = new HashSet<string>(paths, Names);
            }

            NameColumnRatio = state.NameColumnRatio;
            Page = state.Page;
        }
        finally
        {
            _restoring = false;
        }

        RaiseRowsChanged();
    }

    /// <summary>DSG-OI-013: the expanded rows of the primary component's class.</summary>
    private HashSet<string> ExpandedSet()
    {
        var key = Selection.Count > 0 ? ClassKey(Selection[^1]) : "";
        if (!_expanded.TryGetValue(key, out var set))
        {
            _expanded[key] = set = new HashSet<string>(Names);
        }

        return set;
    }

    private static string ClassKey(FormObject obj) => obj.Info?.ClassName ?? obj.TypeName;

    private void RaiseSessionStateChanged()
    {
        if (!_restoring)
        {
            SessionStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // ---- copy and paste (DSG-OI-037) -------------------------------------------------------------------------------

    /// <summary>The value text of the current row (Events page: the handler name); null without a current row.</summary>
    public string? CurrentValueText =>
        IsEmpty ? null
        : Page == InspectorPage.Events ? CurrentEvent is { } name ? EventRows.FirstOrDefault(r => Names.Equals(r.Name, name))?.Value : null
        : CurrentRow?.Display;

    bool ICommandTarget.CanExecute(string commandId, string? argument) => commandId switch
    {
        "edit.copy" => _clipboard is not null && !IsEditing && CurrentValueText is not null,
        "edit.paste" => _clipboard is not null && !IsEditing && EditDisplay() is not null,
        _ => false,
    };

    async Task<bool> ICommandTarget.ExecuteAsync(string commandId, string? argument)
    {
        if (!((ICommandTarget)this).CanExecute(commandId, argument))
        {
            return false;
        }

        if (commandId == "edit.copy")
        {
            await _clipboard!.SetTextAsync(CurrentValueText!);
            return true;
        }

        // Ctrl+V: edit mode with the clipboard text as the entry, not yet committed.
        if (await _clipboard!.GetTextAsync() is { } text && !IsEditing)
        {
            BeginEdit(text);
        }

        return true;
    }

    // ---- menus -----------------------------------------------------------------------------------------------------

    /// <summary>DSG-OI-039: the row context menu.</summary>
    public IReadOnlyList<MenuItemModel> BuildContextMenu()
    {
        var commands = _commands ?? throw new InvalidOperationException("Commands are not attached.");
        MenuItemModel Command(string id, MenuToggleKind toggle = MenuToggleKind.None) =>
            MenuItemModel.ForCommand(commands.Get(id).Title, commands.Get(id), null, toggle);
        return
        [
            Command("objectInspector.resetToDefault"),
            MenuItemModel.ForCommand(Strings.Inspector_CopyValue, commands.Get("edit.copy"), null, MenuToggleKind.None),
            Command("objectInspector.copyPropertyName"),
            MenuItemModel.Separator(),
            .. ArrangementItems(),
            Command("objectInspector.toggleDescription", MenuToggleKind.Check),
            Command("objectInspector.expandAll"),
            Command("objectInspector.collapseAll"),
        ];
    }

    /// <summary>UI-DSG-113: the Object Inspector is narrower than <see cref="NarrowWidth"/> (set by the view).</summary>
    public bool IsNarrow
    {
        get => _isNarrow;
        set
        {
            if (SetProperty(ref _isNarrow, value))
            {
                OnPropertyChanged(nameof(HasCaptionMenu));
            }
        }
    }

    public override bool HasCaptionMenu => IsNarrow;

    /// <summary>UI-DSG-113: the arrangement buttons of a narrow inspector.</summary>
    public override IReadOnlyList<MenuItemModel> BuildCaptionMenu() => IsNarrow ? ArrangementItems() : [];

    private IReadOnlyList<MenuItemModel> ArrangementItems() =>
    [
        MenuItemModel.ForAction("objectInspector.arrangeByCategory", Strings.Inspector_ArrangeByCategory, () => SetArrangedByName(false), !IsArrangedByName),
        MenuItemModel.ForAction("objectInspector.arrangeByName", Strings.Inspector_ArrangeByName, () => SetArrangedByName(true), IsArrangedByName),
    ];

    /// <summary>DSG-OI-010, 011: one of the two arrangements.</summary>
    public void SetArrangedByName(bool byName)
    {
        if (IsArrangedByName != byName)
        {
            ToggleArrangement();
        }
    }

    // ---- value lists (DSG-OI-032 Alt+Down) -------------------------------------------------------------------------

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
}
