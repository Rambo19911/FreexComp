using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.ToolWindows;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Debugging;

/// <summary>
/// The IDE around a debug session from Phase 6 on (I-133): the six debug tool windows, the <c>Debug</c> layout profile
/// with automatic switching (UI-WIN-012 (b), <c>layout.autoSwitch</c>) and <c>window.layout.debug</c>, and whether the
/// Debug toolbar has its context (UI-TB-004: a session exists or the Debug profile is active).
/// </summary>
public sealed class DebugShell
{
    /// <summary>
    /// Phase 6 commands of the debug tool windows whose behaviour arrives with their windows' content; until then they are
    /// registered unavailable, so the command check of the start-up (IDE-ARCH-040) sees every catalog id handled.
    /// </summary>
    public static readonly IReadOnlyList<string> PendingCommands =
    [
        "breakpoints.delete", "breakpoints.enableAll", "breakpoints.disableAll", "breakpoints.goToSource",
        "callStack.goToFrame", "callStack.copy",
        "watches.edit", "watches.delete", "watches.deleteAll",
        "locals.copyValue", "locals.modifyValue",
        "threads.switchTo", "eventLog.clear",
    ];

    private readonly IDebugService _debug;
    private readonly ISettingsService _settings;
    private readonly LayoutViewModel _layout;
    private readonly Func<bool> _isDesignActive;
    private bool _hadSession;
    private bool _hasToolBarContext;

    /// <param name="isDesignActive">A Design view is the active document view: the profile rule (a) selects <c>Design</c>.</param>
    public DebugShell(ICommandService commands, IDebugService debug, IBreakpointService breakpoints, ISettingsService settings, LayoutViewModel layout,
        Func<bool> isDesignActive)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(debug);
        ArgumentNullException.ThrowIfNull(layout);
        _debug = debug;
        _settings = settings;
        _layout = layout;
        _isDesignActive = isDesignActive;
        ToolWindows =
        [
            new BreakpointsViewModel(breakpoints).Definition, new CallStackViewModel(debug).Definition, new LocalsViewModel(debug).Definition,
            new WatchesViewModel(debug).Definition, new ThreadsViewModel(debug).Definition, new EventLogViewModel().Definition,
        ];
        commands.RegisterHandler("window.layout.debug", new ProfileHandler(layout));
        foreach (var id in PendingCommands.Where(id => !commands.IsHandled(id)))
            commands.RegisterHandler(id, PendingHandler.Instance);
        debug.SessionChanged += (_, _) => OnSessionChanged();
        layout.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LayoutViewModel.ActiveProfile))
                UpdateToolBarContext();
        };
        UpdateToolBarContext();
    }

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

    private sealed class PendingHandler : ICommandHandler
    {
        public static readonly PendingHandler Instance = new();

        public bool CanExecute(string? argument) => false;

        public Task ExecuteAsync(string? argument) => Task.CompletedTask;
    }
}
