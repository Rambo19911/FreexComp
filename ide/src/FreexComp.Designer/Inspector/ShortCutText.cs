using System.Globalization;

namespace FreexComp.Designer.Inspector;

/// <summary>
/// <c>TShortCut</c> values and their gesture text (object-inspector.md §5.1; encoding and key names as the framework's
/// <c>ShortCutToText</c>/<c>TextToShortCut</c>, ui-framework actions-menus.md UIF-930 … 933): low byte = virtual-key
/// code, <c>$2000</c> Shift, <c>$4000</c> Ctrl, <c>$8000</c> Alt.
/// </summary>
public static class ShortCutText
{
    public const int Shift = 0x2000;
    public const int Ctrl = 0x4000;
    public const int Alt = 0x8000;

    /// <summary>The text of the value 0.</summary>
    public const string None = "(None)";

    private static readonly Dictionary<int, string> Names = BuildNames();

    private static readonly Dictionary<string, int> Keys =
        Names.ToDictionary(e => e.Value, e => e.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The gesture text: <c>(None)</c> for 0; modifiers in the order <c>Ctrl+</c>, <c>Alt+</c>, <c>Shift+</c>, then the key
    /// name; a value whose key has no fixed name is shown as its decimal number.
    /// </summary>
    public static string Format(long value)
    {
        if (value == 0)
        {
            return None;
        }

        if (value is < 0 or > 0xFFFF || !Names.TryGetValue((int)(value & 0xFF), out var key) || (value & 0x1F00) != 0)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        return ((value & Ctrl) != 0 ? "Ctrl+" : "") + ((value & Alt) != 0 ? "Alt+" : "") + ((value & Shift) != 0 ? "Shift+" : "") + key;
    }

    /// <summary>
    /// Parses gesture text: empty or <c>(None)</c> = 0; otherwise modifiers <c>Ctrl</c>, <c>Shift</c>, <c>Alt</c> in any
    /// order, each followed by <c>+</c> (spaces around <c>+</c> optional, case-insensitive), then exactly one fixed key name.
    /// </summary>
    public static bool TryParse(string text, out long value)
    {
        value = 0;
        var rest = text.Trim();
        if (rest.Length == 0 || rest.Equals(None, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var modifiers = 0;
        while (true)
        {
            var found = false;
            foreach (var (name, flag) in new[] { ("Ctrl", Ctrl), ("Shift", Shift), ("Alt", Alt) })
            {
                if (rest.StartsWith(name, StringComparison.OrdinalIgnoreCase) && rest[name.Length..].TrimStart() is ['+', .. var after]
                    && after.Trim().Length > 0 && (modifiers & flag) == 0)
                {
                    modifiers |= flag;
                    rest = after.Trim();
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                break;
            }
        }

        if (!Keys.TryGetValue(rest, out var key))
        {
            return false;
        }

        value = modifiers | key;
        return true;
    }

    /// <summary>UIF-932: the fixed (English) key names.</summary>
    private static Dictionary<int, string> BuildNames()
    {
        var names = new Dictionary<int, string>
        {
            [8] = "BkSp", [9] = "Tab", [13] = "Enter", [27] = "Esc", [32] = "Space", [33] = "PgUp", [34] = "PgDn", [35] = "End",
            [36] = "Home", [37] = "Left", [38] = "Up", [39] = "Right", [40] = "Down", [45] = "Ins", [46] = "Del",
            [0x6A] = "Num *", [0x6B] = "Num +", [0x6D] = "Num -", [0x6E] = "Num .", [0x6F] = "Num /",
        };
        for (var c = 'A'; c <= 'Z'; c++)
        {
            names[c] = c.ToString();
        }

        for (var d = 0; d <= 9; d++)
        {
            names['0' + d] = d.ToString(CultureInfo.InvariantCulture);
            names[0x60 + d] = "Num " + d.ToString(CultureInfo.InvariantCulture);
        }

        for (var f = 1; f <= 24; f++)
        {
            names[0x6F + f] = "F" + f.ToString(CultureInfo.InvariantCulture);
        }

        return names;
    }
}
