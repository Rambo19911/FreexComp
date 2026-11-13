namespace FreexComp.Ide.Core.Commands;

/// <summary>
/// Effective keymap = defaults − <c>removed</c> + <c>bindings</c> of <c>keymap.json</c> (settings.md §5, IDE-SET-066).
/// UI thread only.
/// </summary>
public interface IKeymapService
{
    Keymap Current { get; }

    IReadOnlyList<KeyGesture> GetGestures(string commandId);

    string? FindCommand(KeyGesture gesture, CommandScope focusScope, string? toolWindow);

    /// <summary>
    /// Makes <paramref name="gestures"/> the command's gestures. A gesture bound to another command at the same scope
    /// level moves here (UI-KEY-021). Only differences from the defaults are stored.
    /// </summary>
    /// <exception cref="ArgumentException">A reserved gesture (UI-KEY-008).</exception>
    void SetGestures(string commandId, IReadOnlyList<KeyGesture> gestures);

    void ResetAll();

    event EventHandler? Changed;

    Task FlushAsync();
}
