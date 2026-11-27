using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Core.Layout;

namespace FreexComp.Ide.Core.ToolWindows;

/// <summary>
/// Tool-window registry, visibility and focus (architecture.md §4, tool-windows.md §1); the arrangement itself belongs to
/// <see cref="ILayoutService"/>. UI thread only.
/// </summary>
public interface IToolWindowService
{
    IReadOnlyList<ToolWindowDefinition> Registered { get; }

    /// <summary>
    /// Adds a tool window (IDE-ARCH-100). One whose phase is later than the implemented phase is not registered
    /// (UI-TW-006) and false is returned. A name registered twice throws.
    /// </summary>
    bool Register(ToolWindowDefinition definition);

    bool IsRegistered(string name);

    /// <summary>The view-model of a registered tool window (created on first use), else null.</summary>
    ToolWindowViewModel? Get(string name);

    /// <summary>
    /// Shows the tool window and selects its tab; with <paramref name="focus"/> keyboard focus moves to its primary
    /// control (<c>view.*</c>, UI-TW-007), without it focus stays where it is (programmatic activation, UI-WIN-016).
    /// False when it is not registered.
    /// </summary>
    bool Show(string name, bool focus);

    /// <summary>Hides the tool window (UI-TW-005); false when it is not registered or not shown.</summary>
    bool Hide(string name);
}

/// <inheritdoc />
public sealed class ToolWindowService : IToolWindowService
{
    /// <summary>The <c>view.*</c> command of each tool window (commands.md §2.4).</summary>
    public static readonly IReadOnlyDictionary<string, string> ShowCommands = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ProjectManager"] = "view.projectManager",
        ["Structure"] = "view.structure",
        ["ObjectInspector"] = "view.objectInspector",
        ["ToolPalette"] = "view.toolPalette",
        ["Messages"] = "view.messages",
        ["Output"] = "view.output",
        ["SearchResults"] = "view.searchResults",
        ["ToDo"] = "view.todo",
        ["Breakpoints"] = "view.breakpoints",
        ["CallStack"] = "view.callStack",
        ["Locals"] = "view.locals",
        ["Watches"] = "view.watches",
        ["Threads"] = "view.threads",
        ["EventLog"] = "view.eventLog",
        ["DataExplorer"] = "view.dataExplorer",
        ["Bookmarks"] = "view.bookmarks",
        ["Templates"] = "view.templates",
        ["History"] = "view.history",
    };

    private readonly ICommandService _commands;
    private readonly IFocusTracker _focus;
    private readonly IClock _clock;
    private readonly ILayoutService _layoutService;
    private readonly int _implementedPhase;
    private readonly Dictionary<string, ToolWindowDefinition> _definitions = new(StringComparer.Ordinal);
    private readonly List<ToolWindowDefinition> _registered = [];
    private readonly Dictionary<string, ToolWindowViewModel> _viewModels = new(StringComparer.Ordinal);
    private string? _lastFocused;

    public ToolWindowService(ICommandService commands, IFocusTracker focus, IClock clock, ILayoutService layout, int implementedPhase)
    {
        _commands = commands;
        _focus = focus;
        _clock = clock;
        _layoutService = layout;
        _implementedPhase = implementedPhase;
        layout.AttachToolWindows(Get);
        foreach (var (name, commandId) in ShowCommands)
        {
            commands.RegisterHandler(commandId, new ShowHandler(this, name));
        }

        commands.RegisterHandler("window.closeToolWindow", new CloseActiveHandler(this));
        focus.Changed += (_, _) => OnFocusChanged();
    }

    private LayoutViewModel Layout => _layoutService.Layout;

    public IReadOnlyList<ToolWindowDefinition> Registered => _registered;

    public bool Register(ToolWindowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!Composition.Phase8Features.IsAvailable("toolwindow:" + definition.Name, definition.Phase, _implementedPhase))
        {
            return false;
        }

        if (!_definitions.TryAdd(definition.Name, definition))
        {
            throw new InvalidOperationException($"Tool window '{definition.Name}' is already registered.");
        }

        _registered.Add(definition);
        Layout.Refresh();
        _commands.InvalidateCanExecute();
        return true;
    }

    public bool IsRegistered(string name) => _definitions.ContainsKey(name);

    public ToolWindowViewModel? Get(string name)
    {
        if (!_definitions.TryGetValue(name, out var definition))
        {
            return null;
        }

        if (!_viewModels.TryGetValue(name, out var viewModel))
        {
            viewModel = definition.Create();
            viewModel.Attach(definition, this, _layoutService, _commands, _clock);
            _viewModels[name] = viewModel;
        }

        return viewModel;
    }

    public bool Show(string name, bool focus)
    {
        if (!_definitions.TryGetValue(name, out var definition))
        {
            return false;
        }

        if (!Layout.SetVisible(name, true))
        {
            Layout.Place(name, definition.HomeDock); // UI-WIN-013: its home dock in the current profile
        }

        if (focus)
        {
            Get(name)!.RequestFocus();
        }

        _commands.InvalidateCanExecute();
        return true;
    }

    public bool Hide(string name)
    {
        if (!IsRegistered(name) || !IsShown(name))
        {
            return false;
        }

        Layout.SetVisible(name, false);
        _commands.InvalidateCanExecute();
        return true;
    }

    private bool IsShown(string name) => Layout.Groups.Any(g => g.Tools.Contains(name, StringComparer.Ordinal));

    private void OnFocusChanged()
    {
        var focused = _focus.Current.ToolWindowName;
        if (focused is not null && IsRegistered(focused))
        {
            _lastFocused = focused;
        }

        foreach (var viewModel in _viewModels.Values)
        {
            viewModel.IsActive = viewModel.Name == focused;
        }
    }

    private sealed class ShowHandler(ToolWindowService owner, string name) : ICommandHandler
    {
        public bool CanExecute(string? argument) => owner.IsRegistered(name);

        /// <summary>UI-A11Y-005: the shortcut of the tool window that has focus returns focus to the active document.</summary>
        public Task ExecuteAsync(string? argument)
        {
            if (owner._focus.Current.ToolWindowName == name)
            {
                owner.Layout.FocusPane(Pane.Documents);
            }
            else
            {
                owner.Show(name, focus: true);
            }

            return Task.CompletedTask;
        }
    }

    /// <summary><c>window.closeToolWindow</c>: hides the tool window that had keyboard focus last, while it is shown.</summary>
    private sealed class CloseActiveHandler(ToolWindowService owner) : ICommandHandler
    {
        public bool CanExecute(string? argument) => owner._lastFocused is { } name && owner.IsShown(name);

        public Task ExecuteAsync(string? argument)
        {
            owner.Hide(owner._lastFocused!);
            return Task.CompletedTask;
        }
    }
}
