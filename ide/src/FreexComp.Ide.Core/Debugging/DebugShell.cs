using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Layout;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.ToolWindows;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Debugging;

/// <summary>
/// The IDE around a debug session from Phase 6 on (I-133, I-142…I-144): the six debug tool windows with their
/// commands, the <c>Debug</c> layout profile with automatic switching (UI-WIN-012 (b), <c>layout.autoSwitch</c>) and
/// <c>window.layout.debug</c>, and whether the Debug toolbar has its context (UI-TB-004: a session exists or the Debug
/// profile is active).
/// </summary>
public sealed class DebugShell
{
    private readonly IDebugService _debug;
    private readonly ISettingsService _settings;
    private readonly LayoutViewModel _layout;
    private readonly Func<bool> _isDesignActive;
    private bool _hadSession;
    private bool _hasToolBarContext;

    /// <param name="isDesignActive">A Design view is the active document view: the profile rule (a) selects <c>Design</c>.</param>
    public DebugShell(ICommandService commands, DebugService debug, BreakpointService breakpoints, ISettingsService settings, ILayoutService layout,
        IDocumentService documents, INavigationService navigation, IClipboardService clipboard, IDialogService dialogs, Func<bool> isDesignActive)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(debug);
        ArgumentNullException.ThrowIfNull(breakpoints);
        ArgumentNullException.ThrowIfNull(layout);
        _debug = debug;
        _settings = settings;
        _layout = layout.Layout;
        _isDesignActive = isDesignActive;
        Breakpoints = new BreakpointsViewModel(commands, breakpoints, debug, documents, navigation);
        CallStack = new CallStackViewModel(commands, debug, clipboard);
        Locals = new LocalsViewModel(commands, debug, clipboard);
        Watches = new WatchesViewModel(commands, debug, clipboard, dialogs) { UiState = layout };
        Threads = new ThreadsViewModel(commands, debug, clipboard);
        EventLog = new EventLogViewModel(commands, debug, clipboard, documents, navigation);
        ToolWindows = [Breakpoints.Definition, CallStack.Definition, Locals.Definition, Watches.Definition, Threads.Definition, EventLog.Definition];

        commands.RegisterHandler("window.layout.debug", new ProfileHandler(_layout));
        Breakpoints.RegisterCommands();
        CallStack.RegisterCommands();
        Watches.RegisterCommands();
        Threads.RegisterCommands();
        EventLog.RegisterCommands();

        // tool-windows.md §12, §13: the value commands belong to Locals and Watches alike — the one with focus, else Locals.
        VariableTreeViewModel Values() => Watches.IsActive ? Watches : Locals;
        commands.RegisterHandler("locals.modifyValue", new Handler(() => Values().CanModifySelected, () =>
        {
            Values().BeginModifySelected();
            return Task.CompletedTask;
        }));
        commands.RegisterHandler("locals.copyValue", new Handler(() => Values().CanCopySelected, () => Values().CopySelectedValueAsync()));

        // The row commands of the windows that have focus act on their rows.
        breakpoints.ToolSelection = () => Breakpoints.IsActive ? Breakpoints.SelectedBreakpoints : null;
        debug.ToolExpression = () => Watches.IsActive ? Watches.SelectedExpression : Locals.IsActive ? Locals.SelectedExpression : null;
        debug.AddWatchInPlace = () =>
        {
            if (!Watches.IsActive)
                return false;
            Watches.BeginAdd();
            return true;
        };

        debug.SessionChanged += (_, _) => OnSessionChanged();
        debug.Stopped += (_, _) => OnStopped();
        _layout.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LayoutViewModel.ActiveProfile))
                UpdateToolBarContext();
        };
        UpdateToolBarContext();
    }

    public BreakpointsViewModel Breakpoints { get; }

    public CallStackViewModel CallStack { get; }

    public LocalsViewModel Locals { get; }

    public WatchesViewModel Watches { get; }

    public ThreadsViewModel Threads { get; }

    public EventLogViewModel EventLog { get; }

    /// <summary>The debug tool windows, to register with the tool-window service.</summary>
    public IReadOnlyList<ToolWindowDefinition> ToolWindows { get; }

    /// <summary>A debug session exists: it holds the <c>Debug</c> profile against rule (a) of UI-WIN-012.</summary>
    public bool HoldsProfile => _debug.Session is not null;

    /// <summary>UI-TB-004: a debug session exists or the Debug layout profile is active.</summary>
    public bool HasToolBarContext => _hasToolBarContext;

    public event EventHandler? HasToolBarContextChanged;

    private void OnSessionChanged()
    {
        var has = _debug.Session is not null;
        if (has != _hadSession)
        {
            _hadSession = has;
            if (_settings.Get(SettingsCatalog.LayoutAutoSwitch))
            {
                // UI-WIN-012 (b): Debug for the lifetime of the session, then what rule (a) selects for the active document.
                _layout.SwitchProfile(has ? LayoutProfiles.Debug : _isDesignActive() ? LayoutProfiles.Design : LayoutProfiles.Edit);
            }
        }

        UpdateToolBarContext();
    }

    /// <summary>I-141: a stop brings the Debug profile's windows back when the user left the profile during the session.</summary>
    private void OnStopped()
    {
        if (_settings.Get(SettingsCatalog.LayoutAutoSwitch) && _layout.ActiveProfile != LayoutProfiles.Debug)
            _layout.SwitchProfile(LayoutProfiles.Debug);
    }

    private void UpdateToolBarContext()
    {
        var context = _debug.Session is not null || _layout.ActiveProfile == LayoutProfiles.Debug;
        if (context == _hasToolBarContext)
            return;
        _hasToolBarContext = context;
        HasToolBarContextChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class ProfileHandler(LayoutViewModel layout) : ICommandHandler
    {
        public bool CanExecute(string? argument) => true;

        public bool IsChecked(string? argument) => layout.ActiveProfile == LayoutProfiles.Debug;

        public Task ExecuteAsync(string? argument)
        {
            layout.SwitchProfile(LayoutProfiles.Debug);
            return Task.CompletedTask;
        }
    }

    private sealed class Handler(Func<bool> canExecute, Func<Task> execute) : ICommandHandler
    {
        public bool CanExecute(string? argument) => canExecute();

        public Task ExecuteAsync(string? argument) => execute();
    }
}
