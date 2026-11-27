namespace FreexComp.Ide.Core.Commands;

/// <summary>
/// Effective keymap = defaults − <c>removed</c> + <c>bindings</c> of <c>keymap.json</c> (settings.md §5, IDE-SET-066).
/// UI thread only.
/// </summary>
public interface IKeymapService
{
    Keymap Current { get; }

    /// <summary>The built-in key map the user's assignments are applied on top of (<see cref="KeymapPresets"/>).</summary>
    string Preset { get; }

    /// <summary>
    /// Changes the built-in key map (setting <c>keyboard.preset</c>; an unknown name means <c>default</c>). The user's
    /// assignments stay and win over the preset; raises <see cref="Changed"/> when the preset changed.
    /// </summary>
    void SetPreset(string preset);

    IReadOnlyList<KeyGesture> GetGestures(string commandId);

    string? FindCommand(KeyGesture gesture, CommandScope focusScope, string? toolWindow);

    /// <summary>The binding <see cref="FindCommand"/> picks, with its argument; null when nothing is bound.</summary>
    KeyBinding? FindBinding(KeyGesture gesture, CommandScope focusScope, string? toolWindow);

    /// <summary>
    /// <paramref name="key"/> is the first key of a chord where the focus is, and no single-key binding takes it first
    /// (<see cref="Keymap.IsChordPrefix"/>, UI-KEY-010, UI-KEY-012).
    /// </summary>
    bool IsChordPrefix(KeyGesture key, CommandScope focusScope, string? toolWindow);

    /// <summary>
    /// Makes <paramref name="gestures"/> the command's gestures. A gesture bound to another command at the same scope
    /// level moves here (UI-KEY-021); so does a key across a chord: a single key takes the chords that start with it,
    /// a chord takes the single-key binding of its first key (UI-KEY-012). Only differences from the defaults are stored.
    /// </summary>
    /// <exception cref="ArgumentException">A reserved gesture (UI-KEY-008), also as one key of a chord.</exception>
    void SetGestures(string commandId, IReadOnlyList<KeyGesture> gestures);

    void ResetAll();

    event EventHandler? Changed;

    Task FlushAsync();
}
