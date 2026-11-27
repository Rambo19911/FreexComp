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
/// A key gesture (shortcuts.md UI-KEY-001): <c>[Ctrl+][Shift+][Alt+]&lt;Key&gt;</c>, or a two-key chord of them written
/// <c>&lt;gesture&gt;, &lt;gesture&gt;</c> (UI-KEY-006: <c>Ctrl+K, Ctrl+F</c> — the first key, then the second).
/// Parsing is case-insensitive and accepts any modifier order; <see cref="ToString"/> is the canonical form. Own type:
/// Core has no Avalonia.
/// </summary>
/// <param name="Modifiers">Modifiers of the (first) key.</param>
/// <param name="Key">The (first) key.</param>
/// <param name="SecondModifiers">Modifiers of a chord's second key.</param>
/// <param name="SecondKey">A chord's second key; null for a single key.</param>
public readonly record struct KeyGesture(KeyModifiers Modifiers, string Key, KeyModifiers SecondModifiers = KeyModifiers.None, string? SecondKey = null)
{
    private static readonly string[] NamedKeys =
    [
        "Enter", "Escape", "Space", "Tab", "Backspace", "Delete", "Insert", "Home", "End", "PageUp", "PageDown",
        "Up", "Down", "Left", "Right", "Break", "Multiply", "Add", "Subtract",
    ];

    private static readonly Dictionary<string, string> Keys = BuildKeys();

    /// <summary>Every canonical key name of UI-KEY-001.</summary>
    public static IReadOnlyCollection<string> KeyNames => Keys.Values;

    /// <summary>A two-key chord (UI-KEY-006).</summary>
    public bool IsChord => SecondKey is not null;

    /// <summary>The first key of a chord; a single key is its own first key.</summary>
    public KeyGesture First => IsChord ? new(Modifiers, Key) : this;

    /// <summary>The second key of a chord; null for a single key.</summary>
    public KeyGesture? Second => SecondKey is null ? null : new KeyGesture(SecondModifiers, SecondKey);

    /// <summary>The chord <paramref name="first"/>, <paramref name="second"/>.</summary>
    /// <exception cref="ArgumentException">One of them is itself a chord.</exception>
    public static KeyGesture Chord(KeyGesture first, KeyGesture second) =>
        first.IsChord || second.IsChord
            ? throw new ArgumentException("A chord has two single keys.")
            : new(first.Modifiers, first.Key, second.Modifiers, second.Key);

    /// <summary>This is a chord whose first key is <paramref name="key"/>.</summary>
    public bool StartsWith(KeyGesture key) => IsChord && First == key;

    /// <summary>
    /// The two cannot both be bound in one scope level (UI-KEY-012): the same gesture, or a single key and a chord
    /// that starts with it. Two chords with the same first key do not overlap.
    /// </summary>
    public bool Overlaps(KeyGesture other) => this == other || StartsWith(other) || other.StartsWith(this);

    /// <summary>
    /// A list of gestures as text (UI-KEY-013): joined with <c>, </c>; with <c>; </c> when one of them is a chord, whose
    /// own text has the comma.
    /// </summary>
    public static string Join(IEnumerable<KeyGesture> gestures)
    {
        var list = gestures.ToList();
        return string.Join(list.Exists(g => g.IsChord) ? "; " : ", ", list);
    }

    /// <exception cref="FormatException">Not a gesture of UI-KEY-001 / UI-KEY-006.</exception>
    public static KeyGesture Parse(string text) =>
        TryParse(text, out var gesture) ? gesture : throw new FormatException($"'{text}' is not a key gesture.");

    public static bool TryParse(string? text, out KeyGesture gesture)
    {
        if (TryParseKey(text, out gesture))
        {
            return true;
        }

        // A chord: "<gesture>, <gesture>". The comma is also a key name ("Ctrl+,"), so every comma that is followed by
        // white space is tried as the separator.
        for (var i = text?.IndexOf(',', StringComparison.Ordinal) ?? -1; i >= 0; i = text!.IndexOf(',', i + 1))
        {
            if (i + 1 < text!.Length && char.IsWhiteSpace(text[i + 1])
                && TryParseKey(text[..i], out var first) && TryParseKey(text[(i + 1)..], out var second))
            {
                gesture = Chord(first, second);
                return true;
            }
        }

        gesture = default;
        return false;
    }

    private static bool TryParseKey(string? text, out KeyGesture gesture)
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
        Format(Modifiers, Key) + (SecondKey is null ? "" : ", " + Format(SecondModifiers, SecondKey));

    private static string Format(KeyModifiers modifiers, string key) =>
        (modifiers.HasFlag(KeyModifiers.Ctrl) ? "Ctrl+" : "")
        + (modifiers.HasFlag(KeyModifiers.Shift) ? "Shift+" : "")
        + (modifiers.HasFlag(KeyModifiers.Alt) ? "Alt+" : "")
        + key;

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
