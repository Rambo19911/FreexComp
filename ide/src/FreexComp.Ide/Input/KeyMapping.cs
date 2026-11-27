using Avalonia.Input;
using CoreGesture = FreexComp.Ide.Core.Commands.KeyGesture;
using CoreModifiers = FreexComp.Ide.Core.Commands.KeyModifiers;

namespace FreexComp.Ide.Input;

/// <summary>
/// Maps the key names of <see cref="CoreGesture"/> (UI-KEY-001) to Avalonia keys and back, from one table: used by
/// key routing (IDE-ARCH-043) and by the test harness to press gestures.
/// </summary>
public static class KeyMapping
{
    private static readonly Dictionary<string, Key> ToKey = BuildTable();
    private static readonly HashSet<string> Punctuation = new([",", ".", "/", ";", "'", "[", "]", "\\", "-", "=", "`"], StringComparer.Ordinal);
    private static readonly Dictionary<Key, string> ToName = ToKey.ToDictionary(p => p.Value, p => p.Key);

    /// <summary>The toolkit key of a single key; of a chord, its first key (press <c>Second</c> after it).</summary>
    public static (Key Key, KeyModifiers Modifiers) ToAvalonia(CoreGesture gesture) =>
        (ToKey[gesture.Key], ToAvalonia(gesture.Modifiers));

    /// <summary>The gesture of a key event, or null for keys without a UI-KEY-001 name (modifier keys, …).</summary>
    public static CoreGesture? FromAvalonia(Key key, KeyModifiers modifiers)
    {
        if (!ToName.TryGetValue(key, out var name))
        {
            return null;
        }

        var core = CoreModifiers.None;
        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            core |= CoreModifiers.Ctrl;
        }

        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            core |= CoreModifiers.Shift;
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            core |= CoreModifiers.Alt;
        }

        return new CoreGesture(core, name);
    }

    /// <summary>
    /// The gesture of a key event (UI-KEY-002): punctuation is matched by the character the key types
    /// (<paramref name="keySymbol"/>) when that is one of the UI-KEY-001 punctuation names, else by the key.
    /// </summary>
    public static CoreGesture? FromKeyEvent(Key key, KeyModifiers modifiers, string? keySymbol)
    {
        if (keySymbol is { Length: 1 } && Punctuation.Contains(keySymbol) && FromAvalonia(ToKey[keySymbol], modifiers) is { } bySymbol)
        {
            return bySymbol;
        }

        return FromAvalonia(key, modifiers);
    }

    private static KeyModifiers ToAvalonia(CoreModifiers modifiers) =>
        (modifiers.HasFlag(CoreModifiers.Ctrl) ? KeyModifiers.Control : KeyModifiers.None)
        | (modifiers.HasFlag(CoreModifiers.Shift) ? KeyModifiers.Shift : KeyModifiers.None)
        | (modifiers.HasFlag(CoreModifiers.Alt) ? KeyModifiers.Alt : KeyModifiers.None);

    private static Dictionary<string, Key> BuildTable()
    {
        var table = new Dictionary<string, Key>(StringComparer.OrdinalIgnoreCase)
        {
            ["Enter"] = Key.Enter, ["Escape"] = Key.Escape, ["Space"] = Key.Space, ["Tab"] = Key.Tab,
            ["Backspace"] = Key.Back, ["Delete"] = Key.Delete, ["Insert"] = Key.Insert, ["Home"] = Key.Home,
            ["End"] = Key.End, ["PageUp"] = Key.PageUp, ["PageDown"] = Key.PageDown, ["Up"] = Key.Up,
            ["Down"] = Key.Down, ["Left"] = Key.Left, ["Right"] = Key.Right, ["Break"] = Key.Pause,
            ["Multiply"] = Key.Multiply, ["Add"] = Key.Add, ["Subtract"] = Key.Subtract,
            [","] = Key.OemComma, ["."] = Key.OemPeriod, ["/"] = Key.OemQuestion, [";"] = Key.OemSemicolon,
            ["'"] = Key.OemQuotes, ["["] = Key.OemOpenBrackets, ["]"] = Key.OemCloseBrackets, ["\\"] = Key.OemPipe,
            ["-"] = Key.OemMinus, ["="] = Key.OemPlus, ["`"] = Key.OemTilde,
        };
        for (var c = 'A'; c <= 'Z'; c++)
        {
            table[c.ToString()] = Enum.Parse<Key>(c.ToString());
        }

        for (var d = 0; d <= 9; d++)
        {
            table[d.ToString(System.Globalization.CultureInfo.InvariantCulture)] = Key.D0 + d;
        }

        for (var f = 1; f <= 12; f++)
        {
            table["F" + f.ToString(System.Globalization.CultureInfo.InvariantCulture)] = Key.F1 + (f - 1);
        }

        return table;
    }
}
