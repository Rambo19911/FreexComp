using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Menus;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.ToolWindows;
using FreexComp.Ide.Core.Layout;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>
/// Base of every tool window's view-model (tool-windows.md §1): caption title, focused state, the optional tool bar and
/// filter box. The frame (caption, tool bar, filter) is drawn by the shell; the content view is found by the view
/// locator from the concrete type. A view-model implementing <see cref="ICommandTarget"/> receives the Focus commands
/// of its scope. Attached by <see cref="ToolWindowService"/> when first created.
/// </summary>
public abstract class ToolWindowViewModel : ObservableObject
{
    /// <summary>UI-TW-009: filtering is applied this long after the last keystroke.</summary>
    public static readonly TimeSpan FilterDelay = TimeSpan.FromMilliseconds(150);

    private IToolWindowService? _service;
    private ILayoutService? _layout;
    private IClock? _clock;
    private IDisposable? _pendingFilter;
    private IdeCommand? _hideCommand;
    private string _filterText = "";
    private bool _isActive;

    public string Name { get; private set; } = "";

    public string Title { get; private set; } = "";

    /// <summary>UI-TW-002: the caption shows the title upper-case.</summary>
    public string Caption => Title.ToUpper(CultureInfo.CurrentCulture);

    public CommandScope Scope { get; private set; } = CommandScope.ToolList;

    /// <summary>The tool window contains keyboard focus (UI-TW-002 caption state).</summary>
    public bool IsActive
    {
        get => _isActive;
        internal set => SetProperty(ref _isActive, value);
    }

    /// <summary>Commands of the tool bar, left to right; none = no tool bar (UI-TW-001).</summary>
    public virtual IReadOnlyList<ToolWindowToolBarItem> ToolBarCommands => [];

    /// <summary>The tool-bar buttons, built from <see cref="ToolBarCommands"/>.</summary>
    public IReadOnlyList<ToolBarButtonModel> ToolBar { get; private set; } = [];

    /// <summary>True when the tool bar has a filter box (UI-TW-008).</summary>
    public virtual bool HasFilter => false;

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value ?? ""))
            {
                _pendingFilter?.Dispose();
                _pendingFilter = _clock?.Schedule(FilterDelay, () => OnFilterApplied(_filterText));
            }
        }
    }

    /// <summary>The view moves keyboard focus to the primary control (UI-TW-007).</summary>
    public event EventHandler? FocusRequested;

    /// <summary>The Hide button of the caption (UI-TW-002).</summary>
    public void Hide() => _service?.Hide(Name);

    /// <summary>UI-A11Y-004: Escape without a local use in the tool window moves focus to the active document.</summary>
    public void ReturnFocusToDocument() => _layout?.Layout.FocusPane(Models.Pane.Documents);

    /// <summary>UI-TW-012: the persisted width of a grid column of this tool window; null = the column's default.</summary>
    public double? ColumnWidth(string columnId) => _layout?.GetColumnWidth(Name, columnId);

    /// <summary>The user resized a grid column (UI-TW-011); the width is persisted in the global UI state.</summary>
    public void SetColumnWidth(string columnId, double width) => _layout?.SetColumnWidth(Name, columnId, width);

    /// <summary>UI-TW-002: "Hide (Shift+Escape)", the shortcut of <c>window.closeToolWindow</c>.</summary>
    public string HideTooltip => _hideCommand is { GestureText.Length: > 0 } command
        ? string.Format(CultureInfo.CurrentCulture, Strings.Format_TitleWithShortcut, Strings.ToolWindow_Hide, command.GestureText)
        : Strings.ToolWindow_Hide;

    /// <summary>The filter text took effect (UI-TW-009); case-insensitive substring matching is the window's own.</summary>
    protected virtual void OnFilterApplied(string filter)
    {
    }

    internal void Attach(ToolWindowDefinition definition, IToolWindowService service, ILayoutService layout, ICommandService commands, IClock clock)
    {
        _layout = layout;
        Name = definition.Name;
        Scope = definition.Scope;
        Title = Strings.ResourceManager.GetString(definition.TitleKey, CultureInfo.CurrentCulture) ?? definition.Name;
        ToolBar = [.. ToolBarCommands.Select(item => new ToolBarButtonModel(commands.Get(item.CommandId), item.IconName))];
        _service = service;
        _clock = clock;
        _hideCommand = commands.Get("window.closeToolWindow");
        _hideCommand.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IdeCommand.GestureText))
            {
                OnPropertyChanged(nameof(HideTooltip));
            }
        };
    }

    internal void RequestFocus() => FocusRequested?.Invoke(this, EventArgs.Empty);
}
