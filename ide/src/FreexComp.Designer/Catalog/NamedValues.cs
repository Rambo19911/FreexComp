namespace FreexComp.Designer.Catalog;

/// <summary>
/// The fixed named values of <c>TColor</c> and <c>TCursor</c> (component-metadata.md §6). Colour values are the signed
/// 32-bit patterns the parser produces for <c>TColor</c> (DSG-FD-023); system colours are <c>$FF000000 or Index</c>.
/// </summary>
public static class NamedValues
{
    private static readonly Dictionary<string, long> Colors = BuildColors();

    private static readonly Dictionary<string, long> Cursors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["crDefault"] = 0, ["crNone"] = -1, ["crArrow"] = -2, ["crCross"] = -3, ["crIBeam"] = -4, ["crSizeNESW"] = -6,
        ["crSizeNS"] = -7, ["crSizeNWSE"] = -8, ["crSizeWE"] = -9, ["crUpArrow"] = -10, ["crHourGlass"] = -11,
        ["crDrag"] = -12, ["crNoDrop"] = -13, ["crHSplit"] = -14, ["crVSplit"] = -15, ["crNo"] = -18, ["crAppStart"] = -19,
        ["crHelp"] = -20, ["crHandPoint"] = -21, ["crSizeAll"] = -22,
    };

    /// <summary>The colour names that are written (the table without the read-only aliases), in table order.</summary>
    public static IReadOnlyList<string> ColorNames { get; } =
        [.. Colors.Keys.Where(n => !n.Equals("clLtGray", StringComparison.Ordinal) && !n.Equals("clDkGray", StringComparison.Ordinal))];

    /// <summary>The cursor names in table order (<c>crDefault</c> … <c>crSizeAll</c>).</summary>
    public static IReadOnlyList<string> CursorNames { get; } = [.. Cursors.Keys];

    private static readonly Dictionary<uint, string> ColorByValue =
        ColorNames.ToDictionary(n => unchecked((uint)Colors[n]), n => n);

    private static readonly Dictionary<long, string> CursorByValue = CursorNames.ToDictionary(n => Cursors[n], n => n);

    /// <summary>The written name of a colour value (compared as a 32-bit pattern), or null.</summary>
    public static string? ColorName(long value) => ColorByValue.GetValueOrDefault(unchecked((uint)value));

    public static string? CursorName(long value) => CursorByValue.GetValueOrDefault(value);

    /// <summary>The written name for a colour name as typed or read (declared casing; an alias gives its target), or null.</summary>
    public static string? CanonicalColorName(string name) => TryGetColor(name, out var v) ? ColorName(v) : null;

    /// <summary>The declared casing of a cursor name, or null.</summary>
    public static string? CanonicalCursorName(string name) => TryGetCursor(name, out var v) ? CursorName(v) : null;

    /// <summary>The value of a colour constant (also the read-only aliases <c>clLtGray</c>, <c>clDkGray</c>).</summary>
    public static bool TryGetColor(string name, out long value) => Colors.TryGetValue(name, out value);

    public static bool TryGetCursor(string name, out long value) => Cursors.TryGetValue(name, out value);

    /// <summary>The value of a named constant of <paramref name="type"/> (<c>TColor</c> or <c>TCursor</c>).</summary>
    public static bool TryGet(string type, string name, out long value)
    {
        if (string.Equals(type, "TColor", StringComparison.OrdinalIgnoreCase))
        {
            return TryGetColor(name, out value);
        }

        if (string.Equals(type, "TCursor", StringComparison.OrdinalIgnoreCase))
        {
            return TryGetCursor(name, out value);
        }

        value = 0;
        return false;
    }

    private static Dictionary<string, long> BuildColors()
    {
        var colors = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["clBlack"] = 0x000000, ["clMaroon"] = 0x000080, ["clGreen"] = 0x008000, ["clOlive"] = 0x008080,
            ["clNavy"] = 0x800000, ["clPurple"] = 0x800080, ["clTeal"] = 0x808000, ["clGray"] = 0x808080,
            ["clSilver"] = 0xC0C0C0, ["clRed"] = 0x0000FF, ["clLime"] = 0x00FF00, ["clYellow"] = 0x00FFFF,
            ["clBlue"] = 0xFF0000, ["clFuchsia"] = 0xFF00FF, ["clAqua"] = 0xFFFF00, ["clWhite"] = 0xFFFFFF,
            ["clMoneyGreen"] = 0xC0DCC0, ["clSkyBlue"] = 0xF0CAA6, ["clCream"] = 0xF0FBFF, ["clMedGray"] = 0xA4A0A0,
            ["clNone"] = 0x1FFFFFFF, ["clDefault"] = 0x20000000, ["clLtGray"] = 0xC0C0C0, ["clDkGray"] = 0x808080,
        };
        string[] system =
        [
            "clScrollBar", "clBackground", "clActiveCaption", "clInactiveCaption", "clMenu", "clWindow", "clWindowFrame",
            "clMenuText", "clWindowText", "clCaptionText", "clActiveBorder", "clInactiveBorder", "clAppWorkSpace",
            "clHighlight", "clHighlightText", "clBtnFace", "clBtnShadow", "clGrayText", "clBtnText",
            "clInactiveCaptionText", "clBtnHighlight", "cl3DDkShadow", "cl3DLight", "clInfoText", "clInfoBk", "",
            "clHotLight", "clGradientActiveCaption", "clGradientInactiveCaption", "clMenuHighlight", "clMenuBar",
        ];
        for (var index = 0; index < system.Length; index++)
        {
            if (system[index].Length > 0)
            {
                colors[system[index]] = unchecked((int)0xFF000000) | index;
            }
        }

        return colors;
    }
}
