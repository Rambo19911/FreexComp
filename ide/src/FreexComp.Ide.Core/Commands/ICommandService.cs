namespace FreexComp.Ide.Core.Commands;

/// <summary>Owner of a command's availability and action (architecture.md §6).</summary>
public interface ICommandHandler
{
    bool CanExecute(string? argument);

    Task ExecuteAsync(string? argument);

    bool IsChecked(string? argument) => false;

    /// <summary>Title shown instead of the catalog title (e.g. "Continue" for <c>run.start</c> while paused); null = catalog title.</summary>
    string? TitleOverride => null;

    /// <summary>Main-menu label shown instead of the menu's own (<c>&amp;Continue</c> for <c>run.start</c> while paused, menus.md §7); null = the menu's label.</summary>
    string? MenuLabelOverride => null;

    /// <summary>
    /// True when <see cref="CanExecute"/> is false only because of the IDE state (building, running, …). Its shortcut
    /// then shows <c>Msg.Status.CommandUnavailable</c> (UI-CMD-012); otherwise a disabled shortcut is silent.
    /// </summary>
    bool IsBlockedByIdeState(string? argument) => false;
}

/// <summary>Implemented by editor, designer and tool-window view-models for Focus routing (commands.md §3).</summary>
public interface ICommandTarget
{
    bool CanExecute(string commandId, string? argument);

    /// <summary>False = not handled.</summary>
    Task<bool> ExecuteAsync(string commandId, string? argument);

    /// <summary>The check mark of a toggle command routed to this target (e.g. <c>view.wordWrap</c>, IDE-ED-040).</summary>
    bool IsChecked(string commandId) => false;
}

/// <summary>A command is about to run: its <c>CanExecute</c> holds and its handler starts next (IDE-REFAC-054).</summary>
public sealed class CommandExecutingEventArgs(string commandId, string? argument) : EventArgs
{
    public string CommandId { get; } = commandId;

    public string? Argument { get; } = argument;
}

public sealed class CommandExecutedEventArgs(string commandId, string? argument, TimeSpan duration, Exception? exception) : EventArgs
{
    public string CommandId { get; } = commandId;

    public string? Argument { get; } = argument;

    public TimeSpan Duration { get; } = duration;

    public Exception? Exception { get; } = exception;
}

/// <summary>Command registry, execution and key routing (architecture.md §6). UI thread only.</summary>
public interface ICommandService
{
    IReadOnlyList<CommandDefinition> Definitions { get; }

    /// <summary>Exactly one handler per catalog id; an unknown or already handled id throws (IDE-ARCH-040).</summary>
    void RegisterHandler(string commandId, ICommandHandler handler);

    /// <summary>Registers a <c>CanExecute = false</c> stub for every unhandled command of a later phase (UI-CMD-004).</summary>
    void RegisterLaterPhaseStubs(int implementedPhase);

    /// <summary>Throws when a catalog id has no handler (stubs count).</summary>
    void VerifyAllHandled();

    /// <summary>True when <paramref name="commandId"/> has a handler (real or stub).</summary>
    bool IsHandled(string commandId);

    IdeCommand Get(string commandId);

    bool CanExecute(string commandId, string? argument = null);

    Task ExecuteAsync(string commandId, string? argument = null);

    /// <summary>
    /// Key routing for the focused element of <see cref="IFocusTracker"/> (IDE-ARCH-042/043): local keys and, in a
    /// text input, <c>Focus</c> commands are left to the control; otherwise as the overload below.
    /// </summary>
    bool HandleKey(KeyGesture gesture);

    /// <summary>
    /// UI-KEY-004 step 3 and UI-KEY-007; true when the key was consumed. UI-KEY-010: the first key of a chord is consumed
    /// and starts the wait (<see cref="IsChordPending"/>); the next key is then always consumed — it runs the chord's
    /// command, or cancels the wait when it completes no chord or is Escape.
    /// </summary>
    bool HandleKey(KeyGesture gesture, CommandScope focusScope, string? toolWindowName);

    /// <summary>The first key of a chord was pressed and the next key is taken as its second key (UI-KEY-010).</summary>
    bool IsChordPending { get; }

    /// <summary>
    /// UI-KEY-011: ends the wait for a chord's second key without a message — the view calls it for a mouse click, a focus
    /// change and a deactivated window (a changed <see cref="IFocusTracker"/> state cancels by itself). No-op when nothing waits.
    /// </summary>
    void CancelChord();

    /// <summary>Coalesced re-query of every command's <c>CanExecute</c> (IDE-ARCH-044).</summary>
    void InvalidateCanExecute();

    /// <summary>Raised before a command's handler runs (an inline rename session is cancelled first, IDE-REFAC-054).</summary>
    event EventHandler<CommandExecutingEventArgs>? Executing;

    event EventHandler<CommandExecutedEventArgs>? Executed;
}
