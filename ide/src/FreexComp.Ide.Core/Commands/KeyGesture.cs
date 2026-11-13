namespace FreexComp.Ide.Core.Commands;

[Flags]
public enum KeyModifiers
{
    None = 0,
    Ctrl = 1,
    Shift = 2,
    Alt = 4,
}

/// <summary>
/// A key gesture (shortcuts.md UI-KEY-001): <c>[Ctrl+][Shift+][Alt+]&lt;Key&gt;</c>. Parsing is case-insensitive
/// and accepts any modifier order; <see cref="ToString"/> is the canonical form. Own type: Core has no Avalonia.
/// </summary>
public readonly record struct KeyGesture(KeyModifiers Modifiers, string Key)
{
    private static readonly string[] NamedKeys =
    [
        "Enter", "Escape", "Space", "Tab", "Backspace", "Delete", "Insert", "Home", "End", "PageUp", "PageDown",
        "Up", "Down", "Left", "Right", "Break", "Multiply", "Add", "Subtract",
    ];

    private static readonly Dictionary<string, string> Keys = BuildKeys();

    /// <summary>Every canonical key name of UI-KEY-001.</summary>
    public static IReadOnlyCollection<string> KeyNames => Keys.Values;

    /// <exception cref="FormatException">Not a gesture of UI-KEY-001.</exception>
    public static KeyGesture Parse(string text) =>
        TryParse(text, out var gesture) ? gesture : throw new FormatException($"'{text}' is not a key gesture.");

    public static bool TryParse(string? text, out KeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('+').Select(p => p.Trim()).ToArray();
        var modifiers = KeyModifiers.None;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var modifier = parts[i].ToUpperInvariant() switch
            {
                "CTRL" => KeyModifiers.Ctrl,
                "SHIFT" => KeyModifiers.Shift,
                "ALT" => KeyModifiers.Alt,
                _ => KeyModifiers.None,
            };
            if (modifier == KeyModifiers.None || modifiers.HasFlag(modifier))
            {
                return false;
            }

            modifiers |= modifier;
        }

        if (!Keys.TryGetValue(parts[^1], out var key))
        {
            return false;
        }

        gesture = new KeyGesture(modifiers, key);
        return true;
    }

    public override string ToString() =>
        (Modifiers.HasFlag(KeyModifiers.Ctrl) ? "Ctrl+" : "")
        + (Modifiers.HasFlag(KeyModifiers.Shift) ? "Shift+" : "")
        + (Modifiers.HasFlag(KeyModifiers.Alt) ? "Alt+" : "")
        + Key;

    private static Dictionary<string, string> BuildKeys()
    {
        var names = new List<string>();
        names.AddRange(Enumerable.Range('A', 26).Select(c => ((char)c).ToString()));
        names.AddRange(Enumerable.Range(0, 10).Select(d => d.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        names.AddRange(Enumerable.Range(1, 12).Select(n => "F" + n.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        names.AddRange(NamedKeys);
        names.AddRange([",", ".", "/", ";", "'", "[", "]", "\\", "-", "=", "`"]);
        return names.ToDictionary(n => n, n => n, StringComparer.OrdinalIgnoreCase);
    }
}
