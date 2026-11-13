namespace FreexComp.Ide.Core.Commands;

/// <summary>
/// Where keyboard focus is (IDE-ARCH-042): the innermost command scope, the tool window, the
/// <see cref="ICommandTarget"/> of that scope, and whether a plain text input has focus.
/// </summary>
public sealed record FocusState(CommandScope Scope, string? ToolWindowName, ICommandTarget? Target, bool IsTextInput)
{
    public static readonly FocusState None = new(CommandScope.Global, null, null, false);
}

/// <summary>
/// Focus as seen by commands (IDE-ARCH-042/043). The view observes the toolkit's focus and reports it through
/// <see cref="Update"/>; popups declare the keys they handle themselves through <see cref="SetLocalKeys"/>.
/// UI thread only.
/// </summary>
public interface IFocusTracker
{
    FocusState Current { get; }

    /// <summary>
    /// The target of the active document (the "active document" of UI-CMD-011): set by the document service on
    /// activation, and by a document view (Editor or Designer scope) that receives focus.
    /// </summary>
    ICommandTarget? ActiveDocumentTarget { get; }

    /// <summary>Keys the focused control handles itself while a popup is open; key routing leaves them alone.</summary>
    IReadOnlyCollection<KeyGesture> LocalKeys { get; }

    event EventHandler? Changed;

    void Update(FocusState state);

    void SetLocalKeys(IReadOnlyCollection<KeyGesture> keys);

    /// <summary>The document service activated a document; null when none is open.</summary>
    void SetActiveDocumentTarget(ICommandTarget? target);
}

/// <inheritdoc />
public sealed class FocusTracker : IFocusTracker
{
    private HashSet<KeyGesture> _localKeys = [];

    public FocusState Current { get; private set; } = FocusState.None;

    public ICommandTarget? ActiveDocumentTarget { get; private set; }

    public IReadOnlyCollection<KeyGesture> LocalKeys => _localKeys;

    public event EventHandler? Changed;

    public void Update(FocusState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Scope is CommandScope.Editor or CommandScope.Designer && state.Target is not null)
        {
            ActiveDocumentTarget = state.Target;
        }

        if (state != Current)
        {
            Current = state;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetLocalKeys(IReadOnlyCollection<KeyGesture> keys) => _localKeys = [.. keys];

    public void SetActiveDocumentTarget(ICommandTarget? target) => ActiveDocumentTarget = target;
}
