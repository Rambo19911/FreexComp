namespace FreexComp.Ide.Core.Models;

/// <summary>The global IDE state (phase3-build-run.md §2); the state machine that owns it arrives with Phase 3.</summary>
public enum IdeState
{
    Idle,
    Building,
    Running,
    DebugRunning,
    Paused,
}
