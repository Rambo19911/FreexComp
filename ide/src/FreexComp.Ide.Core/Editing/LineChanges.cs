namespace FreexComp.Ide.Core.Editing;

/// <summary>A line's change-bar state (UI-ED-040).</summary>
public enum LineChangeState
{
    None,
    Modified,
    Saved,
}

/// <summary>Per-line change state drawn by the change bar (UI-ED-040/041); the tracker is <c>LineChangeTracker</c> (I-046).</summary>
public interface ILineChangeSource
{
    /// <summary>The state of the 1-based <paramref name="line"/>.</summary>
    LineChangeState StateOf(int line);

    event EventHandler? Changed;
}

/// <summary>No line has a mark: the source of a document until the line-change tracker is plugged in.</summary>
public sealed class NoLineChanges : ILineChangeSource
{
    public static NoLineChanges Instance { get; } = new();

    private NoLineChanges()
    {
    }

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    public LineChangeState StateOf(int line) => LineChangeState.None;
}
