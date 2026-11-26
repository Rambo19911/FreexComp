using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Models;

namespace FreexComp.Ide.Core.Build;

/// <summary>What a <see cref="IdeState.Building"/> state is doing (phase3-build-run.md §2).</summary>
public enum BuildKind
{
    Build,
    Rebuild,
    Clean,
    CheckSyntax,
}

/// <summary>The one global IDE state with its details (phase3-build-run.md §2).</summary>
public sealed record IdeStateInfo(IdeState State, BuildKind? BuildKind, bool CancelRequested, string? ProgramPath, int? ProcessId)
{
    public static IdeStateInfo Idle { get; } = new(IdeState.Idle, null, false, null, null);

    /// <summary>"Debugging" in the UI (IDE-P3-001).</summary>
    public bool IsDebugging => State is IdeState.DebugRunning or IdeState.Paused;

    /// <summary>Paused: the source file of the stop location; null when it has no source (<c>Msg.State.PausedNoSource</c>).</summary>
    public string? PausedFile { get; init; }

    /// <summary>Paused: the 1-based line of the stop location.</summary>
    public int PausedLine { get; init; }
}

/// <summary>Read side of the IDE state: <see cref="Current"/> and <see cref="Changed"/> (UI thread).</summary>
public interface IIdeState
{
    IdeStateInfo Current { get; }

    event EventHandler? Changed;
}

/// <summary>
/// Owner of the IDE state (IDE-P3-001): only the transitions of the phase3-build-run.md §2 diagram are legal; any other
/// is refused with a logged error. UI thread only.
/// </summary>
public sealed class IdeStateMachine(ILog log) : IIdeState
{
    private const string Category = "state";

    private static readonly HashSet<(IdeState From, IdeState To)> Legal =
    [
        (IdeState.Idle, IdeState.Building),
        (IdeState.Building, IdeState.Idle),
        (IdeState.Building, IdeState.Running),      // IDE-P3-002: a run request goes on without passing Idle
        (IdeState.Building, IdeState.DebugRunning),
        (IdeState.Running, IdeState.Idle),
        (IdeState.Idle, IdeState.DebugRunning),     // P6: start with the debugger
        (IdeState.DebugRunning, IdeState.Paused),
        (IdeState.Paused, IdeState.DebugRunning),
        (IdeState.DebugRunning, IdeState.Idle),
        (IdeState.Paused, IdeState.Idle),
    ];

    public IdeStateInfo Current { get; private set; } = IdeStateInfo.Idle;

    public event EventHandler? Changed;

    public static bool IsLegal(IdeState from, IdeState to) => Legal.Contains((from, to));

    /// <summary>Moves to <paramref name="next"/>; false (and an error in the log) when the drawn diagram has no such arrow.</summary>
    public bool TryTransition(IdeStateInfo next)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (!IsLegal(Current.State, next.State))
        {
            log.Error(Category, $"Illegal state transition {Current.State} -> {next.State}.");
            return false;
        }

        Current = next;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>IDE-P3-050: a cancel request while Building keeps the state and sets <see cref="IdeStateInfo.CancelRequested"/>.</summary>
    public bool RequestCancel()
    {
        if (Current.State != IdeState.Building || Current.CancelRequested)
            return false;
        Current = Current with { CancelRequested = true };
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Forces <see cref="IdeState.Idle"/> from any state (IDE-P3-051/091: a hung build or a failed kill).</summary>
    public void ForceIdle()
    {
        if (Current == IdeStateInfo.Idle)
            return;
        Current = IdeStateInfo.Idle;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// IDE-P3-003: command availability by IDE state, on top of each handler's own conditions. Commands not listed are
/// available in every state (editing, saving, documents, find, navigation).
/// </summary>
public static class IdeCommandAvailability
{
    private static readonly IdeState[] IdleOnly = [IdeState.Idle];
    private static readonly IdeState[] AllButBuilding = [IdeState.Idle, IdeState.Running, IdeState.DebugRunning, IdeState.Paused];

    private static readonly Dictionary<string, IdeState[]> Rules = new(StringComparer.Ordinal)
    {
        ["project.build"] = IdleOnly,
        ["project.buildAll"] = IdleOnly,
        ["project.rebuildAll"] = IdleOnly, // IDE-PM-171
        ["project.cleanAll"] = IdleOnly,
        ["project.rebuild"] = IdleOnly,
        ["project.clean"] = IdleOnly,
        ["project.checkSyntax"] = IdleOnly,
        ["project.options"] = IdleOnly,
        ["project.setConfiguration"] = IdleOnly,
        ["project.setPlatform"] = IdleOnly, // IDE-PLAT-003
        ["project.addPlatform"] = IdleOnly, // IDE-PLAT-013
        ["project.removePlatform"] = IdleOnly, // IDE-PLAT-014
        ["project.configurationManager"] = IdleOnly,
        ["project.setActive"] = IdleOnly, // IDE-PM-160
        ["project.dependencies"] = IdleOnly, // IDE-PM-175
        ["project.addExistingProject"] = IdleOnly, // IDE-PM-164…168
        ["project.addNewProject"] = IdleOnly,
        ["project.removeFromGroup"] = IdleOnly,
        ["project.moveProjectUp"] = IdleOnly,
        ["project.moveProjectDown"] = IdleOnly,
        ["run.parameters"] = IdleOnly,
        ["project.cancelBuild"] = [IdeState.Building],
        // IDE-P3-004: available while Running too, where they ask Msg.Run.AlreadyRunning (Restart = run.restart).
        ["run.start"] = [IdeState.Idle, IdeState.Running, IdeState.Paused],
        ["run.startWithoutDebugging"] = [IdeState.Idle, IdeState.Running],
        ["run.stop"] = [IdeState.Running, IdeState.DebugRunning, IdeState.Paused],
        ["run.restart"] = [IdeState.Running, IdeState.DebugRunning, IdeState.Paused],
        // File operations on the project (IDE-PM-032); opening/closing a project asks to stop a running program first.
        ["project.addNewUnit"] = IdleOnly,
        ["project.addNewForm"] = IdleOnly,
        ["project.addNewFrame"] = IdleOnly,
        ["project.addNewInheritedForm"] = IdleOnly,
        ["project.addExistingFile"] = IdleOnly,
        ["project.newFolder"] = IdleOnly,
        ["project.addNewFile"] = IdleOnly,
        ["project.moveFile"] = IdleOnly,
        ["project.removeFile"] = IdleOnly,
        ["project.renameFile"] = IdleOnly,
        ["project.deleteFile"] = IdleOnly,
        ["file.newProject"] = IdleOnly,
        ["file.openProject"] = AllButBuilding,
        ["file.openRecentProject"] = AllButBuilding,
        ["file.closeProject"] = AllButBuilding,
    };

    /// <summary>True when the IDE state does not rule <paramref name="commandId"/> out.</summary>
    public static bool IsAvailable(string commandId, IdeState state) =>
        !Rules.TryGetValue(commandId, out var states) || states.Contains(state);
}
