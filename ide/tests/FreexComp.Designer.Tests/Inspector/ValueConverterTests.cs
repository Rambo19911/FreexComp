using System.Globalization;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.Inspector;
using FreexComp.Designer.Model;

namespace FreexComp.Designer.Tests.Inspector;

/// <summary>The Object Inspector's value converters (object-inspector.md §5.1, §5.2, §5.6; D-058, D-059).</summary>
public sealed class ValueConverterTests
{
    private static readonly ValueConverter Converter = new(TestCatalog.Instance);

    /// <summary>One row: parse <see cref="Input"/> (null = only format <see cref="Value"/>), expect the value or the message, then the display text.</summary>
    public sealed record Case(string Type, string? Input, FormValue? Value, string? Message, string? Display = null, object[]? Args = null)
    {
        public override string ToString() => $"{Type}: {Input ?? "(format)"}";
    }

    private static IntegerValue Int(long v, bool hex = false) => new(v, hex);

    private static IdentValue Id(string name) => new(name);

    public static TheoryData<Case> EveryType => new((Case[])
    [
        // Integer: decimal display, metadata range, else the Integer range.
        new("Integer", " 42 ", Int(42), null, "42"),
        new("Integer", "-7", Int(-7), null, "-7"),
        new("Integer", "2147483647", Int(2147483647), null, "2147483647"),
        new("Integer", "2147483648", null, "Msg.Designer.ValueInvalidInteger", Args: [-2147483648L, 2147483647L]),
        new("Integer", "abc", null, "Msg.Designer.ValueInvalidExpression"),
        new("Integer", "", null, "Msg.Designer.ValueInvalidExpression"),
        new("Integer0..100", "101", null, "Msg.Designer.ValueInvalidInteger", Args: [0L, 100L]),
        new("Integer0..100", "100", Int(100), null, "100"),
        new("Integer", null, Int(255, hex: true), null, "255"),
        new("Int64", "9223372036854775807", Int(long.MaxValue), null, "9223372036854775807"),
        new("Int64", "-9223372036854775808", Int(long.MinValue), null, "-9223372036854775808"),

        // Double: shortest round trip with '.', '.' or ',' as the one decimal separator, no expressions.
        new("Double", "1.5", new FloatValue(1.5), null, "1.5"),
        new("Double", "1,25", new FloatValue(1.25), null, "1.25"),
        new("Double", "-2.5e3", new FloatValue(-2500), null, "-2500"),
        new("Double", "0.1", new FloatValue(0.1), null, "0.1"),
        new("Double", "1,000.5", null, "Msg.Designer.ValueInvalidFloat"),
        new("Double", "1+2", null, "Msg.Designer.ValueInvalidFloat"),
        new("Double", "NaN", null, "Msg.Designer.ValueInvalidFloat"),
        new("Double0..10", "10.5", null, "Msg.Designer.ValueInvalidInteger", Args: [0.0, 10.0]),

        // Boolean: True / False, typing t / f selects.
        new("Boolean", "true", Id("True"), null, "True"),
        new("Boolean", "F", Id("False"), null, "False"),
        new("Boolean", "yes", null, "Msg.Designer.ValueInvalidEnum", Args: ["yes"]),

        // string: any text, the first line of pasted text; control characters shown as '·'.
        new("string", "  Hello ", new StringValue("  Hello "), null, "  Hello "),
        new("string", "first\r\nsecond", new StringValue("first"), null, "first"),
        new("string", null, new StringValue("a\tb"), null, "a·b"),
        new("filename", "C:\\x.txt", new StringValue("C:\\x.txt"), null, "C:\\x.txt"),

        // Char: one character or #code; control characters shown as #n.
        new("Char", "x", new StringValue("x"), null, "x"),
        new("Char", " ", new StringValue(" "), null, " "),
        new("Char", "#65", new StringValue("A"), null, "A"),
        new("Char", "#9", new StringValue("\t"), null, "#9"),
        new("Char", "#", new StringValue("#"), null, "#"),
        new("Char", "ab", null, "Msg.Designer.ValueInvalidEnum", Args: ["ab"]),
        new("Char", "#70000", null, "Msg.Designer.ValueInvalidEnum", Args: ["#70000"]),

        // enum: member (case-insensitive, declared casing) or a unique prefix.
        new("TAlign", "ALCLIENT", Id("alClient"), null, "alClient"),
        new("TAlign", "alc", Id("alClient"), null, "alClient"),
        new("TAlign", "alT", Id("alTop"), null, "alTop"),
        new("TAlign", "al", null, "Msg.Designer.ValueInvalidEnum", Args: ["al"]),
        new("TAlign", "alMiddle", null, "Msg.Designer.ValueInvalidEnum", Args: ["alMiddle"]),

        // set: brackets optional, ',' or spaces, duplicates ignored, declaration order.
        new("TAnchors", "[akTop, akLeft]", new SetValue(["akLeft", "akTop"]), null, "[akLeft, akTop]"),
        new("TAnchors", "akbottom akleft akleft", new SetValue(["akLeft", "akBottom"]), null, "[akLeft, akBottom]"),
        new("TAnchors", "[]", new SetValue([]), null, "[]"),
        new("TAnchors", "[akLeft, akMiddle, akNone]", null, "Msg.Designer.ValueInvalidEnum", Args: ["akMiddle"]),

        // TColor (also Integer with editor "color"): see ColorNamesAndHex.
        new("TColor", "clRed", Id("clRed"), null, "clRed"),
        new("TColor", "$FF8000", Int(0xFF8000, hex: true), null, "$00FF8000"),
        new("TColor", "nonsense", null, "Msg.Designer.ValueInvalidColor"),
        new("IntegerColor", "#FF0000", Int(0x0000FF, hex: true), null, "clRed"),

        // TCursor: constant name or −32768 … 32767.
        new("TCursor", "crhandpoint", Id("crHandPoint"), null, "crHandPoint"),
        new("TCursor", "-21", Int(-21), null, "crHandPoint"),
        new("TCursor", "5", Int(5), null, "5"),
        new("TCursor", "40000", null, "Msg.Designer.ValueInvalidEnum", Args: ["40000"]),
        new("TCursor", "crFancy", null, "Msg.Designer.ValueInvalidEnum", Args: ["crFancy"]),

        // TShortCut: gesture text; (None) / empty = 0; Ctrl $4000, Shift $2000, Alt $8000.
        new("TShortCut", "Ctrl+S", Int(0x4000 + 'S'), null, "Ctrl+S"),
        new("TShortCut", "shift + ctrl + f5", Int(0x6000 + 0x74), null, "Ctrl+Shift+F5"),
        new("TShortCut", "Alt+F4", Int(32883), null, "Alt+F4"),
        new("TShortCut", "Ctrl+Num +", Int(0x4000 + 0x6B), null, "Ctrl+Num +"),
        new("TShortCut", "(None)", Int(0), null, "(None)"),
        new("TShortCut", "", Int(0), null, "(None)"),
        new("TShortCut", "Ctrl+Banana", null, "Msg.Designer.ValueInvalidEnum", Args: ["Ctrl+Banana"]),

        // Read-only display kinds.
        new("TStrings", null, new StringListValue(["a", "b"]), null, "(2 lines)"),
        new("TStrings", null, new StringListValue(["a"]), null, "(1 line)"),
        new("TStrings", null, new StringListValue([]), null, "(empty)"),
        new("TFont", null, null, null, "(TFont)"),
        new("TPicture", null, null, null, "(none)"),
        new("TPicture", null, new BinaryValue([1, 2]), null, "(picture)"),
        new("TStatusPanels", null, new CollectionValue([new CollectionItem([]), new CollectionItem([])]), null, "(2 items)"),
        new("TStatusPanels", null, new CollectionValue([]), null, "(empty)"),

        // Component references: (none) or a candidate name.
        new("TWinControl", "(none)", Id("nil"), null, "(none)"),
        new("TWinControl", "", Id("nil"), null, "(none)"),
        new("TWinControl", "panel1", Id("Panel1"), null, "Panel1"),
        new("TWinControl", "Panel9", null, "Msg.Designer.ReferenceNotFound", Args: ["Panel9"]),
    ]);

    [Theory]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-OI-057")]
    [Trait("Req", "DSG-OI-058")]
    [MemberData(nameof(EveryType))]
    public void ParsesAndFormatsEveryType(Case c)
    {
        var property = PropertyOf(c.Type);
        var value = c.Value;
        if (c.Input is { } input)
        {
            var result = Converter.Parse(property, input, references: ["Panel1", "Edit1"]);
            Assert.Equal(c.Message, result.MessageId);
            if (c.Message is not null)
            {
                Assert.Null(result.Value);
                if (c.Args is { } args)
                {
                    Assert.Equal(args, result.Args);
                }

                return;
            }

            Assert.Equal(c.Value, result.Value);
            value = result.Value;
        }

        Assert.Equal(c.Display, Converter.Format(property, value));
    }

    /// <summary>
    /// DSG-OI-057: the built-in tables equal the interface's (docs/interfaces/component-metadata.md §6) — names, values
    /// and table order (rows left to right); the read-only aliases are values but never names.
    /// </summary>
    [Fact]
    [Trait("Phase", "5")]
    [Trait("Category", "Spec")]
    [Trait("Req", "DSG-OI-057")]
    public void ColorAndCursorTablesMatchInterface()
    {
        var tables = InterfaceTables(Path.Combine(DocsRoot(), "interfaces", "component-metadata.md"), "6.");
        Assert.True(tables.Count == 3, $"expected 3 tables in component-metadata.md §6, found {tables.Count}");
        var (explicitColors, systemColors, cursors) = (tables[0], tables[1], tables[2]);

        var colors = explicitColors.Select(c => (c.Name, Value: (uint)ParseHex(c.Value)))
            .Concat(systemColors.Select(c => (c.Name, Value: 0xFF000000u | uint.Parse(c.Value, CultureInfo.InvariantCulture))))
            .ToList();
        Assert.Equal(colors.Select(c => c.Name), NamedValues.ColorNames);
        Assert.Equal(colors, NamedValues.ColorNames.Select(n => (n, NamedValues.TryGetColor(n, out var v) ? unchecked((uint)v) : uint.MaxValue)));

        var cursorRows = cursors.Select(c => (c.Name, Value: long.Parse(c.Value.Replace('\u2212', '-'), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)))
            .ToList();
        Assert.Equal(cursorRows.Select(c => c.Name), NamedValues.CursorNames);
        Assert.Equal(cursorRows, NamedValues.CursorNames.Select(n => (n, NamedValues.TryGetCursor(n, out var v) ? v : long.MinValue)));

        // "clLtGray (= clSilver) and clDkGray (= clGray) are accepted when reading and never written."
        Assert.True(NamedValues.TryGetColor("clLtGray", out var lt) && NamedValues.TryGetColor("clSilver", out var silver) && lt == silver);
        Assert.True(NamedValues.TryGetColor("clDkGray", out var dk) && NamedValues.TryGetColor("clGray", out var gray) && dk == gray);
        Assert.DoesNotContain("clLtGray", NamedValues.ColorNames);
        Assert.DoesNotContain("clDkGray", NamedValues.ColorNames);
    }

    private static long ParseHex(string text)
    {
        Assert.StartsWith("$", text, StringComparison.Ordinal);
        return long.Parse(text.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
    }

    /// <summary>The repository's <c>docs/</c> (found above the test output like the other spec-table tests).</summary>
    private static string DocsRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FreexComp.sln")))
            {
                var docs = Path.Combine(dir.FullName, "docs");
                Assert.True(Directory.Exists(docs), "docs/ not found next to FreexComp.sln: " + docs);
                return docs;
            }
        }

        throw new InvalidOperationException("FreexComp.sln not found above " + AppContext.BaseDirectory);
    }

    /// <summary>
    /// The Markdown tables of the section whose heading starts with <paramref name="section"/> (up to the next level-2
    /// heading), each as its (Name, Value) cell pairs read row by row, left to right; backticks removed, empty pairs skipped.
    /// </summary>
    private static List<List<(string Name, string Value)>> InterfaceTables(string path, string section)
    {
        var lines = File.ReadAllLines(path);
        var start = Array.FindIndex(lines, l => l.StartsWith("## ", StringComparison.Ordinal) && l[3..].StartsWith(section, StringComparison.Ordinal));
        Assert.True(start >= 0, $"section {section} not found in {path}");
        var tables = new List<List<(string, string)>>();
        List<(string, string)>? table = null;
        for (var i = start + 1; i < lines.Length && !lines[i].StartsWith("## ", StringComparison.Ordinal); i++)
        {
            if (!lines[i].StartsWith('|'))
            {
                table = null;
                continue;
            }

            if (table is null)
            {
                table = [];
                tables.Add(table);
                i++; // the header row is read; skip the separator row
                continue;
            }

            var cells = lines[i].Trim().Trim('|').Split('|').Select(c => c.Replace("`", "", StringComparison.Ordinal).Trim()).ToList();
            for (var c = 0; c + 1 < cells.Count; c += 2)
            {
                if (cells[c].Length > 0)
                {
                    table.Add((cells[c], cells[c + 1]));
                }
            }
        }

        return tables;
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-OI-057")]
    [Trait("Req", "DSG-OI-058")]
    public void ColorNamesAndHex()
    {
        var color = PropertyOf("TColor");

        // Names: case-insensitive, stored as IdentValue with the declared casing (DSG-OI-058); aliases are read, never written.
        Assert.Equal(Id("clBtnFace"), Converter.Parse(color, "CLBTNFACE").Value);
        Assert.Equal(Id("clMoneyGreen"), Converter.Parse(color, "clmoneygreen").Value);
        Assert.Equal(Id("clSilver"), Converter.Parse(color, "clLtGray").Value);
        Assert.Equal(Id("clGray"), Converter.Parse(color, "clDkGray").Value);

        // Numbers: $ + 1 … 8 hex digits, #RRGGBB → $00BBGGRR, decimal; stored as hex integers (signed 32-bit pattern).
        Assert.Equal(Int(0xF, hex: true), Converter.Parse(color, "$f").Value);
        Assert.Equal(Int(unchecked((int)0xFF00000F), hex: true), Converter.Parse(color, "$FF00000F").Value);
        Assert.Equal(Int(0x563412, hex: true), Converter.Parse(color, "#123456").Value);
        Assert.Equal(Int(65280, hex: true), Converter.Parse(color, "65280").Value);
        Assert.Equal(Int(-1, hex: true), Converter.Parse(color, "4294967295").Value);
        foreach (var bad in new[] { "$", "$123456789", "#12345", "#GG0000", "4294967296", "clReddish", "$-1" })
        {
            Assert.Equal("Msg.Designer.ValueInvalidColor", Converter.Parse(color, bad).MessageId);
        }

        // Display: the constant name when the value equals one, else $ + 8 hex digits.
        Assert.Equal("clBtnFace", Converter.Format(color, Int(unchecked((int)0xFF00000F), hex: true)));
        Assert.Equal("clBtnFace", Converter.Format(color, Id("clbtnface")));
        Assert.Equal("clSilver", Converter.Format(color, Id("clLtGray")));
        Assert.Equal("clNone", Converter.Format(color, Int(0x1FFFFFFF)));
        Assert.Equal("clDefault", Converter.Format(color, Int(0x20000000)));
        Assert.Equal("clBlack", Converter.Format(color, Int(0)));
        Assert.Equal("$00123456", Converter.Format(color, Int(0x123456)));
        Assert.Equal("$FF0000FF", Converter.Format(color, Int(unchecked((int)0xFF0000FF))));

        // Every name of the interface table (component-metadata.md §6) round-trips through parse and format.
        string[] names =
        [
            "clBlack", "clMaroon", "clGreen", "clOlive", "clNavy", "clPurple", "clTeal", "clGray", "clSilver", "clRed", "clLime",
            "clYellow", "clBlue", "clFuchsia", "clAqua", "clWhite", "clMoneyGreen", "clSkyBlue", "clCream", "clMedGray", "clNone",
            "clDefault", "clScrollBar", "clBackground", "clActiveCaption", "clInactiveCaption", "clMenu", "clWindow",
            "clWindowFrame", "clMenuText", "clWindowText", "clCaptionText", "clActiveBorder", "clInactiveBorder", "clAppWorkSpace",
            "clHighlight", "clHighlightText", "clBtnFace", "clBtnShadow", "clGrayText", "clBtnText", "clInactiveCaptionText",
            "clBtnHighlight", "cl3DDkShadow", "cl3DLight", "clInfoText", "clInfoBk", "clHotLight", "clGradientActiveCaption",
            "clGradientInactiveCaption", "clMenuHighlight", "clMenuBar",
        ];
        Assert.Equal(names.Order(StringComparer.Ordinal), ValueConverter.ColorNames.Order(StringComparer.Ordinal));
        foreach (var name in names)
        {
            Assert.Equal(name, Converter.Format(color, Converter.Parse(color, name.ToUpperInvariant()).Value));
            Assert.True(NamedValues.TryGetColor(name, out var v));
            Assert.Equal(name, Converter.Format(color, Int(v, hex: true)));
        }

        // The cursor table: 20 names (crDefault 0 … crSizeAll −22).
        Assert.Equal(20, ValueConverter.CursorNames.Count);
        Assert.Equal("crDefault", ValueConverter.CursorNames[0]);
        Assert.Equal("crSizeAll", ValueConverter.CursorNames[^1]);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-OI-040")]
    public void IntegerExpressions()
    {
        var integer = PropertyOf("Integer");
        long Eval(string text) => Converter.Parse(integer, text).Value is IntegerValue { Value: var v }
            ? v
            : throw new Xunit.Sdk.XunitException($"'{text}' did not evaluate: {Converter.Parse(integer, text).MessageId}");

        Assert.Equal(14, Eval("2 + 3 * 4"));
        Assert.Equal(20, Eval("(2 + 3) * 4"));
        Assert.Equal(3, Eval("7 / 2"));
        Assert.Equal(-3, Eval("-7 / 2"));
        Assert.Equal(-3, Eval("-7 DIV 2"));
        Assert.Equal(-1, Eval("-7 mod 2"));
        Assert.Equal(1, Eval("7 Mod -2"));
        Assert.Equal(255, Eval("$FF"));
        Assert.Equal(256, Eval("$ff+1"));
        Assert.Equal(5, Eval("--5"));
        Assert.Equal(-5, Eval("+-5"));
        Assert.Equal(6, Eval("2*-(-3)"));
        Assert.Equal(10, Eval("  1+2 +3+4 "));
        Assert.Equal(1, Eval("10 - 4 - 5"));
        Assert.Equal(2, Eval("12 div 3 div 2"));
        Assert.Equal(16, Eval("4div2*8"));
        Assert.Equal(1, Eval("7mod3"));

        // The result is range-checked against the property (here the Integer range).
        var large = Converter.Parse(integer, "65536 * 65536");
        Assert.Equal("Msg.Designer.ValueInvalidInteger", large.MessageId);

        foreach (var bad in new[] { "1 / 0", "5 mod 0", "9223372036854775807 + 1", "-9223372036854775808 div -1", "3037000500*3037000500",
                     "99999999999999999999", "(1 + 2", "1 + 2)", "1 +", "* 2", "2 ** 3", "1.5", "$", "$G", "x", "1 2", "di v", "()" })
        {
            Assert.Equal("Msg.Designer.ValueInvalidExpression", Converter.Parse(PropertyOf("Int64"), bad).MessageId);
        }

        // The evaluated number is what is stored and displayed (DSG-OI-042).
        Assert.Equal("14", Converter.Format(integer, Converter.Parse(integer, "2+3*4").Value));
        Assert.Equal(long.MinValue, ((IntegerValue)Converter.Parse(PropertyOf("Int64"), "-9223372036854775808").Value!).Value);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-OI-041")]
    public void RelativeEdits()
    {
        var integer = PropertyOf("Integer");
        var ranged = PropertyOf("Integer0..100");
        long? Apply(PropertyInfo p, string text, long current) =>
            (Converter.Parse(p, text, current: Int(current)).Value as IntegerValue)?.Value;

        Assert.Equal(108, Apply(integer, "+= 8", 100));
        Assert.Equal(94, Apply(integer, "-=2*3", 100));
        Assert.Equal(-200, Apply(integer, " *= -2 ", 100));
        Assert.Equal(33, Apply(integer, "/= 3", 100));
        Assert.Equal(-33, Apply(integer, "/= 3", -100));
        Assert.Equal(108, Apply(integer, "+= (4 + 4)", 100));

        // Per component: the same entry gives each its own result.
        Assert.Equal(18, Apply(integer, "+= 8", 10));
        Assert.Equal(58, Apply(integer, "+= 8", 50));

        // The result is range-checked; errors of the operand are expression errors.
        Assert.Equal("Msg.Designer.ValueInvalidInteger", Converter.Parse(ranged, "+= 10", current: Int(95)).MessageId);
        Assert.Equal(100, Apply(ranged, "+= 5", 95));
        Assert.Equal("Msg.Designer.ValueInvalidExpression", Converter.Parse(integer, "/= 0", current: Int(5)).MessageId);
        Assert.Equal("Msg.Designer.ValueInvalidExpression", Converter.Parse(integer, "+=", current: Int(5)).MessageId);
        Assert.Equal("Msg.Designer.ValueInvalidExpression", Converter.Parse(PropertyOf("Int64"), "*= 2", current: Int(long.MaxValue)).MessageId);

        // Only the exact forms are relative: "+ = 8" and "= 8" are not expressions; "+8" is an absolute value.
        Assert.Equal("Msg.Designer.ValueInvalidExpression", Converter.Parse(integer, "+ = 8", current: Int(5)).MessageId);
        Assert.Equal("Msg.Designer.ValueInvalidExpression", Converter.Parse(integer, "= 8", current: Int(5)).MessageId);
        Assert.Equal(8, Apply(integer, "+8", 100));

        // A relative edit needs the component's current value.
        Assert.True(ValueConverter.IsRelativeEdit("*= 3"));
        Assert.False(ValueConverter.IsRelativeEdit("3"));
        Assert.Equal("Msg.Designer.ValueInvalidExpression", Converter.Parse(integer, "+= 1").MessageId);

        // Relative edits are for integers only.
        Assert.Equal("Msg.Designer.ValueInvalidFloat", Converter.Parse(PropertyOf("Double"), "+= 1", current: new FloatValue(1)).MessageId);
    }

    /// <summary>A property of the named test type (metadata properties for enum and set; constructed ones otherwise).</summary>
    private static PropertyInfo PropertyOf(string type)
    {
        var catalog = TestCatalog.Instance;
        PropertyInfo Make(string t, PropertyKind kind = PropertyKind.Simple, string? editor = null, double? min = null, double? max = null,
            string? itemClass = null) =>
            new("P", t, kind, null, false, "Misc", null, false, false, editor, min, max, false, itemClass, null, "TTest", true, false, null);

        return type switch
        {
            "Integer0..100" => Make("Integer", min: 0, max: 100),
            "Double0..10" => Make("Double", min: 0, max: 10),
            "IntegerColor" => Make("Integer", editor: "color"),
            "TShortCut" => Make("TShortCut", editor: "shortcut"),
            "TStrings" => Make("TStrings", editor: "strings"),
            "filename" => Make("string", editor: "filename"),
            "TFont" => Make("TFont", PropertyKind.Object, editor: "font"),
            "TPicture" => Make("TPicture", PropertyKind.Object, editor: "picture"),
            "TStatusPanels" => Make("TStatusPanels", PropertyKind.Collection, editor: "collection", itemClass: "TStatusPanel"),
            "TWinControl" => Make("TWinControl", PropertyKind.Component),
            "TAlign" => catalog.Find("TButton")!.FindProperty("Align")!,
            "TAnchors" => catalog.Find("TButton")!.FindProperty("Anchors")!,
            _ => Make(type),
        };
    }
}
