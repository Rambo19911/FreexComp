using System.Globalization;

namespace FreexComp.Ide.Core.Themes;

/// <summary>WCAG 2 contrast ratio (UI-TOK-006; also shown on the Colors options page, dialogs.md §9.5).</summary>
public static class ColorContrast
{
    /// <summary>Ratio from 1 to 21 between two opaque colors written <c>#RRGGBB</c>; order does not matter.</summary>
    public static double Ratio(string colorA, string colorB)
    {
        var a = Luminance(colorA);
        var b = Luminance(colorB);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance(string hex)
    {
        if (hex.Length != 7 || hex[0] != '#')
        {
            throw new FormatException($"Expected #RRGGBB, got '{hex}'.");
        }

        var v = int.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return (0.2126 * Channel(v >> 16)) + (0.7152 * Channel(v >> 8)) + (0.0722 * Channel(v));
    }

    private static double Channel(int value)
    {
        var c = (value & 0xFF) / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
