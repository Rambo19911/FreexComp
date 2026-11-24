namespace FreexComp.Ide.Core.Commands;

/// <summary>
/// One gesture → command binding. <paramref name="ToolWindow"/> narrows a <see cref="CommandScope.ToolList"/> binding
/// to one tool window (<c>Tool.&lt;Name&gt;</c> name, e.g. <c>Watches</c>); null = every tool window of the scope.
/// </summary>
/// <summary>One key binding; <see cref="Argument"/> is passed to the command (Ctrl+Tab → <c>window.nextDocument</c> <c>mru</c>).</summary>
public sealed record KeyBinding(KeyGesture Gesture, CommandScope Scope, string? ToolWindow, string CommandId, string? Argument = null);

/// <summary>Effective key bindings and their lookup (shortcuts.md UI-KEY-004, UI-KEY-005).</summary>
public sealed class Keymap
{
    private readonly Dictionary<string, List<KeyGesture>> _gesturesByCommand = new(StringComparer.Ordinal);

    /// <param name="bindings">The effective bindings.</param>
    /// <param name="displayed">Per command, gestures shown before the catalog's (a key map preset's own gestures).</param>
    public Keymap(IEnumerable<KeyBinding> bindings, IReadOnlyDictionary<string, IReadOnlyList<KeyGesture>>? displayed = null)
    {
        Bindings = [.. bindings];
        foreach (var binding in Bindings)
        {
            if (!_gesturesByCommand.TryGetValue(binding.CommandId, out var list))
            {
                _gesturesByCommand[binding.CommandId] = list = [];
            }

            if (!list.Contains(binding.Gesture))
            {
                list.Add(binding.Gesture);
            }
        }

        // UI-KEY-005: the first gesture is the displayed one; the catalog's DefaultGestures give that order.
        foreach (var (id, list) in _gesturesByCommand)
        {
            var preferred = CommandCatalog.All.FirstOrDefault(c => c.Id == id)?.DefaultGestures ?? [];
            var order = preferred.Select(KeyGesture.Parse).ToList();
            if (displayed is not null && displayed.TryGetValue(id, out var first))
            {
                order.InsertRange(0, first);
            }

            var sorted = list.OrderBy(g => Rank(order, g)).ToList(); // stable: table order for the rest
            list.Clear();
            list.AddRange(sorted);
        }
    }

    public IReadOnlyList<KeyBinding> Bindings { get; }

    /// <summary>Gestures of a command, the displayed one first; empty when unbound.</summary>
    public IReadOnlyList<KeyGesture> GetGestures(string commandId) =>
        _gesturesByCommand.TryGetValue(commandId, out var list) ? list : [];

    /// <summary>
    /// UI-KEY-004 step 3, innermost first: the focused tool window's own binding → the focus scope →
    /// <see cref="CommandScope.Focus"/> → <see cref="CommandScope.Global"/>. Null when nothing is bound.
    /// </summary>
    public string? FindCommand(KeyGesture gesture, CommandScope focusScope, string? toolWindow) =>
        FindBinding(gesture, focusScope, toolWindow)?.CommandId;

    /// <summary><see cref="FindCommand"/> returning the whole binding (with its argument).</summary>
    public KeyBinding? FindBinding(KeyGesture gesture, CommandScope focusScope, string? toolWindow)
    {
        if (focusScope != CommandScope.Global)
        {
            if (toolWindow is not null && Find(gesture, focusScope, toolWindow) is { } own)
            {
                return own;
            }

            if (Find(gesture, focusScope, null) is { } scoped)
            {
                return scoped;
            }
        }

        return Find(gesture, CommandScope.Focus, null) ?? Find(gesture, CommandScope.Global, null);
    }

    private static int Rank(List<KeyGesture> order, KeyGesture gesture)
    {
        var index = order.IndexOf(gesture);
        return index < 0 ? int.MaxValue : index;
    }

    private KeyBinding? Find(KeyGesture gesture, CommandScope scope, string? toolWindow) =>
        Bindings.FirstOrDefault(b => b.Gesture == gesture && b.Scope == scope
                                     && string.Equals(b.ToolWindow, toolWindow, StringComparison.Ordinal));
}
