using FreexComp.Designer.Catalog;
using FreexComp.Designer.Model;
using FreexComp.Designer.Text;

namespace FreexComp.Designer.Tests.Text;

public sealed class FormWriterTests
{
    private static readonly ComponentCatalog Catalog = TestCatalog.Instance;

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-043")]
    public void WritesOnlyNonDefaultProperties()
    {
        const string Input = """
            object F: TForm
              Left = 10
              Top = 20
              BorderStyle = bsSizeable
              Caption = ''
              ClientWidth = 300
              ClientHeight = 200
              Color = clBtnFace
              Font.Height = -12
              Font.Name = 'Consolas'
              PixelsPerInch = 96
              Scaled = true
              object L: TListBox
                Left = 1
                Top = 2
                Width = 3
                Height = 4
                Anchors = [akTop, akLeft]
                Color = $FF000005
                ItemHeight = 15
                Items = (
                )
                MultiSelect = False
                PopupMenu = nil
                TabOrder = -1
                Tag = 0
              end
              object S: TStatusBar
                Left = 0
                Top = 0
                Width = 300
                Height = 19
                Align = alBottom
                Panels = <
                >
                SizeGrip = False
              end
            end
            """;

        // Every line equal to its default disappears, whatever its spelling (TColor by value, set order and case,
        // empty list and collection, nil); "stored": false lines read from the file are kept (DSG-FD-043).
        Assert.Equal("""
            object F: TForm
              Left = 10
              Top = 20
              ClientWidth = 300
              ClientHeight = 200
              Font.Name = 'Consolas'
              PixelsPerInch = 96
              object L: TListBox
                Left = 1
                Top = 2
                Width = 3
                Height = 4
                ItemHeight = 15
              end
              object S: TStatusBar
                Left = 0
                Top = 0
                Width = 300
                Height = 19
                SizeGrip = False
              end
            end

            """.ReplaceLineEndings("\n"), Write(Input));
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-044")]
    [Trait("Req", "DSG-FD-012")]
    public void AlwaysWritesGeometry()
    {
        const string Input = """
            object F: TForm
              Caption = 'No position'
              ClientWidth = 300
              ClientHeight = 200
              object B: TButton
                Caption = 'No bounds'
              end
              object Zero: TLabel
                Left = 0
                Top = 0
                Width = 0
                Height = 0
              end
              object T: TTimer
                object Nested: TTimer
                  Interval = 5
                end
              end
              object Child: TForm
              end
            end
            """;

        // Root: Left/Top of the new-form template, client size and PixelsPerInch = 96; controls: their bounds even when
        // zero, a control without a stored size the class's default size (I-288); a non-visual child of the root: its tray position, a nested one none; "stored": false wins (TForm Width/Height).
        Assert.Equal("""
            object F: TForm
              Left = 200
              Top = 120
              Caption = 'No position'
              ClientWidth = 300
              ClientHeight = 200
              PixelsPerInch = 96
              object B: TButton
                Left = 0
                Top = 0
                Width = 75
                Height = 25
                Caption = 'No bounds'
              end
              object Zero: TLabel
                Left = 0
                Top = 0
                Width = 0
                Height = 0
              end
              object T: TTimer
                DesignLeft = 0
                DesignTop = 0
                object Nested: TTimer
                  Interval = 5
                end
              end
              object Child: TForm
                Left = 0
                Top = 0
              end
            end

            """.ReplaceLineEndings("\n"), Write(Input));

        // DSG-FD-012: a legacy root with Width/Height keeps them and gets no client size until it is edited.
        Assert.Equal("object F: TForm\n  Left = 1\n  Top = 2\n  Width = 416\n  Height = 339\n  PixelsPerInch = 96\nend\n",
            Write("object F: TForm\n  Height = 339\n  Width = 416\n  Top = 2\n  Left = 1\nend\n"));
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-042")]
    public void OrdersPropertiesByMetadataThenPreservedThenEvents()
    {
        const string Input = """
            object F: TForm
              OnKeyPress = FormKeyPress
              Zebra = 1
              PixelsPerInch = 96
              OnCreate = FormCreate
              Font.Style = [fsBold]
              Apple = 2
              Font.Height = -16
              Caption = 'x'
              ClientHeight = 200
              Left = 1
              Tag = 7
              OnClick = FormClick
              Top = 2
              ClientWidth = 300
              Font.Color = clRed
              object E: TEdit
                OnKeyPress = EKeyPress
                Text = 'x'
                Align = alMiddle
                OnChange = EChange
                TabOrder = 0
                Anchors = [akRight]
                Height = 23
                Width = 121
                Top = 8
                Left = 8
              end
              object T: TTimer
                DesignTop = 4
                OnTimer = TTimer1
                DesignLeft = 3
                Interval = 10
                Tag = 1
              end
            end
            """;

        // Known properties ancestor-first (Tag from TComponent first; Font.* at Font's position in TFont's order), then
        // DesignLeft/DesignTop, then preserved properties in their original order, then events in metadata order.
        Assert.Equal("""
            object F: TForm
              Tag = 7
              Left = 1
              Top = 2
              Caption = 'x'
              ClientWidth = 300
              ClientHeight = 200
              Font.Color = clRed
              Font.Height = -16
              Font.Style = [fsBold]
              PixelsPerInch = 96
              Zebra = 1
              Apple = 2
              OnClick = FormClick
              OnCreate = FormCreate
              OnKeyPress = FormKeyPress
              object E: TEdit
                Left = 8
                Top = 8
                Width = 121
                Height = 23
                Anchors = [akRight]
                TabOrder = 0
                Text = 'x'
                Align = alMiddle
                OnChange = EChange
                OnKeyPress = EKeyPress
              end
              object T: TTimer
                Tag = 1
                Interval = 10
                DesignLeft = 3
                DesignTop = 4
                OnTimer = TTimer1
              end
            end

            """.ReplaceLineEndings("\n"), Write(Input));
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-047")]
    public void FormatsIntegersFloatsSetsListsBinaryCollections()
    {
        // Integers: decimal; hex as 8 digits of the 32-bit pattern, 16 outside the 32-bit range.
        Assert.Equal("42", Format(new IntegerValue(42, false)));
        Assert.Equal("-7", Format(new IntegerValue(-7, false)));
        Assert.Equal("-9223372036854775808", Format(new IntegerValue(long.MinValue, false)));
        Assert.Equal("$FFFFFFFF", Format(new IntegerValue(-1, true)));
        Assert.Equal("$00C0FFEE", Format(new IntegerValue(0x00C0FFEE, true)));
        Assert.Equal("$FFFFFFFF", Format(new IntegerValue(0xFFFFFFFF, true)));
        Assert.Equal("$0000000100000000", Format(new IntegerValue(0x1_0000_0000, true)));
        Assert.Equal("$FFFFFFFF7FFFFFFF", Format(new IntegerValue(int.MinValue - 1L, true)));

        // Floats: shortest round trip, always with ".", exponent form outside [1e-5, 1e15).
        Assert.Equal("1.0", FormWriter.FormatFloat(1));
        Assert.Equal("0.0", FormWriter.FormatFloat(0));
        Assert.Equal("1.5", FormWriter.FormatFloat(1.5));
        Assert.Equal("-0.25", FormWriter.FormatFloat(-0.25));
        Assert.Equal("0.1", FormWriter.FormatFloat(0.1));
        Assert.Equal("100.0", FormWriter.FormatFloat(100));
        Assert.Equal("123456789012345.0", FormWriter.FormatFloat(123456789012345));
        Assert.Equal("1.0E+15", FormWriter.FormatFloat(1e15));
        Assert.Equal("1.5E+20", FormWriter.FormatFloat(1.5e20));
        Assert.Equal("-1.7976931348623157E+308", FormWriter.FormatFloat(-double.MaxValue));
        Assert.Equal("0.00001", FormWriter.FormatFloat(1e-5));
        Assert.Equal("2.5E-7", FormWriter.FormatFloat(2.5e-7));
        Assert.Equal("5.0E-324", FormWriter.FormatFloat(double.Epsilon)); // shortest round trip
        Assert.Throws<ArgumentOutOfRangeException>(() => FormWriter.FormatFloat(double.NaN));
        foreach (var x in new[] { 0.1, 1.0 / 3, 2.5e-7, 1.5e20, 123.456, -98765.4321 })
        {
            var text = FormWriter.FormatFloat(x);
            Assert.Equal(new FloatValue(x), FormParser.Parse($"object F: TForm\n  X = {text}\nend\n", "f.form", Catalog).Document!.Root.Properties[0].Value);
        }

        // Identifiers: True/False/nil in canonical casing; qualified references joined.
        Assert.Equal("True", Format(new IdentValue("TRUE")));
        Assert.Equal("nil", Format(new IdentValue("Nil")));
        Assert.Equal("alClient", Format(new IdentValue("alClient")));
        Assert.Equal("Other.Menu1", Format(new QualifiedIdentValue(["Other", "Menu1"])));

        // Sets in enum declaration order; unknown elements last in their order.
        var anchors = Catalog.Find("TEdit")!.FindProperty("Anchors");
        Assert.Equal("[akLeft, akTop, akBottom]", Format(new SetValue(["akBottom", "akLeft", "akTop"]), anchors));
        Assert.Equal("[akTop, akZ, akA]", Format(new SetValue(["akZ", "akTop", "akA"]), anchors));
        Assert.Equal("[]", Format(new SetValue([]), anchors));

        // String list, binary, collection: one item per line, closer on its own line at the property indent.
        Assert.Equal([(0, "("), (1, "'first line'"), (1, "'it''s the second'"), (1, "''"), (0, ")")],
            FormWriter.FormatValue(new StringListValue(["first line", "it's the second", ""]), null, Catalog));
        var bytes = Enumerable.Range(0, 34).Select(i => (byte)(i * 7)).ToArray();
        Assert.Equal([(0, "{"), (1, Convert.ToHexString(bytes, 0, 32)), (1, "E0E7"), (0, "}")],
            FormWriter.FormatValue(new BinaryValue([.. bytes]), null, Catalog));
        var panels = Catalog.Find("TStatusBar")!.FindProperty("Panels");
        var collection = new CollectionValue(
        [
            new CollectionItem([("Width", new IntegerValue(120, false), null), ("Bevel", new IdentValue("pbLowered"), null), ("Text", new StringValue("Ready"), null)]),
            new CollectionItem([("Custom", new FloatValue(2), null), ("Alignment", new IdentValue("taCenter"), null)]),
        ]);
        Assert.Equal(
            [(0, "<"), (1, "item"), (2, "Text = 'Ready'"), (2, "Width = 120"), (1, "end"), (1, "item"), (2, "Alignment = taCenter"), (2, "Custom = 2.0"), (1, "end"), (0, ">")],
            FormWriter.FormatValue(collection, panels, Catalog));

        // A designer-set value in a whole file: lines re-indented under the property.
        var document = FormParser.Parse("object F: TForm\n  object M: TMemo\n    Left = 1\n    Top = 2\n    Width = 3\n    Height = 4\n  end\nend\n", "m.form", Catalog).Document!;
        var memo = document.FindByName("M")!;
        memo.AddProperty(new FormProperty("Lines", new StringListValue(["a", new string('x', 70)]), Catalog.Find("TMemo")!.FindProperty("Lines"), null, null, default));
        Assert.Equal("""
            object F: TForm
              Left = 200
              Top = 120
              ClientWidth = 0
              ClientHeight = 0
              PixelsPerInch = 96
              object M: TMemo
                Left = 1
                Top = 2
                Width = 3
                Height = 4
                Lines = (
                  'a'
                  'xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx' +
                    'xxxxxx'
                )
              end
            end

            """.ReplaceLineEndings("\n"), FormWriter.Write(document));
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-062")]
    [Trait("Req", "DSG-FD-063")]
    public void SplitsLongStringsAt64()
    {
        // The example of form-format.md §2.5.
        Assert.Equal(["'This caption is exactly seventy characters long, so it must be s' +", "'plit!!'"],
            FormWriter.FormatString("This caption is exactly seventy characters long, so it must be split!!"));
        Assert.Equal([new string('a', 64).Quoted()], FormWriter.FormatString(new string('a', 64)));
        Assert.Equal([new string('a', 64).Quoted() + " +", "'b'"], FormWriter.FormatString(new string('a', 64) + "b"));
        Assert.Equal([new string('a', 64).Quoted() + " +", new string('b', 64).Quoted() + " +", "'c'"],
            FormWriter.FormatString(new string('a', 64) + new string('b', 64) + "c"));

        // A #n code and a doubled quote count as one character; a chunk of control codes only has no quotes.
        Assert.Equal([new string('a', 63).Quoted() + "#13 +", "#10"], FormWriter.FormatString(new string('a', 63) + "\r\n"));
        Assert.Equal([(new string('a', 63) + "''").Quoted() + " +", "'b'"], FormWriter.FormatString(new string('a', 63) + "'b"));

        // A surrogate pair counts as one character and is never split.
        var smiles = string.Concat(Enumerable.Repeat("\U0001F600", 65));
        var lines = FormWriter.FormatString(smiles);
        Assert.Equal(2, lines.Count);
        Assert.Equal(string.Concat(Enumerable.Repeat("\U0001F600", 64)).Quoted() + " +", lines[0]);
        Assert.Equal("\U0001F600".Quoted(), lines[1]);

        // Continuation lines are indented by property indent + 2, and the text parses back to the same string.
        var document = FormParser.Parse("object F: TForm\nend\n", "s.form", Catalog).Document!;
        var text = new string('x', 100) + "\r\n" + new string('y', 30);
        document.Root.AddProperty(new FormProperty("Caption", new StringValue(text), Catalog.Find("TForm")!.FindProperty("Caption"), null, null, default));
        var written = FormWriter.Write(document);
        Assert.Contains("  Caption = '" + new string('x', 64) + "' +\n    '" + new string('x', 36) + "'#13#10'" + new string('y', 26) + "' +\n    '" + new string('y', 4) + "'\n", written, StringComparison.Ordinal);
        Assert.Equal(new StringValue(text), FormParser.Parse(written, "s.form", Catalog).Document!.Root.FindProperty("Caption")!.Value);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-060")]
    [Trait("Req", "DSG-FD-061")]
    public void EscapesQuotesAndControlCharacters()
    {
        Assert.Equal(["''"], FormWriter.FormatString(""));
        Assert.Equal(["'it''s'"], FormWriter.FormatString("it's"));
        Assert.Equal(["''''"], FormWriter.FormatString("'"));
        Assert.Equal(["'Line 1'#13#10'Line 2'"], FormWriter.FormatString("Line 1\r\nLine 2"));
        Assert.Equal(["#9"], FormWriter.FormatString("\t"));
        Assert.Equal(["#0'abc'"], FormWriter.FormatString("\0abc"));
        Assert.Equal(["'~'#127#133#159'\u00A0é漢'"], FormWriter.FormatString("~\u007F\u0085\u009F\u00A0é漢"));
        Assert.Equal(["'a'#8232'b'#8233"], FormWriter.FormatString("a\u2028b\u2029"));
        Assert.Equal(["'\U0001F600'#55357'x'#56832"], FormWriter.FormatString("\U0001F600\uD83Dx\uDE00")); // unpaired surrogates as codes
        Assert.Equal(["' #hash '"], FormWriter.FormatString(" #hash "));

        // Everything parses back to the same string.
        foreach (var s in new[] { "it's", "\0abc", "~\u007F\u0085\u00A0", "a\u2028b", "\U0001F600\uD83Dx\uDE00", "''" })
        {
            var text = $"object F: TForm\n  Caption = {string.Join(" +\n    ", FormWriter.FormatString(s))}\nend\n";
            Assert.Equal(new StringValue(s), FormParser.Parse(text, "e.form", Catalog).Document!.Root.Properties[0].Value);
        }
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-046")]
    public void KeepsRawTextOfUntouchedValues()
    {
        const string Input = """
            object F: TForm
              Tag = $FF
              Left = 200
              Top = 120
              Hint = 'ab' +
                       'cd' +
              'ef'
              BorderIcons = [biMinimize,biSystemMenu]
              ClientWidth = 300
              ClientHeight = 200
              Color = $000000FF
              KeyPreview = true
              PixelsPerInch = 96
              object M: TMemo
                Left = 8
                Top = 8
                Width = 185
                Height = 89
                Lines = (
                        'one'#9 +
                  'two'
                  'three')
                TabOrder = 0
              end
              object S: TStatusBar
                Left = 0
                Top = 181
                Width = 300
                Height = 19
                Panels = <
                item
                Text = 'a' +
                'b'
                Width = $10
                end
                  >
              end
            end
            """;

        // The original spelling of every untouched value survives (hex, split points, set order, casing), re-indented.
        Assert.Equal("""
            object F: TForm
              Tag = $FF
              Left = 200
              Top = 120
              Hint = 'ab' +
                'cd' +
                'ef'
              BorderIcons = [biMinimize,biSystemMenu]
              ClientWidth = 300
              ClientHeight = 200
              Color = $000000FF
              KeyPreview = true
              PixelsPerInch = 96
              object M: TMemo
                Left = 8
                Top = 8
                Width = 185
                Height = 89
                Lines = (
                  'one'#9 +
                    'two'
                  'three')
                TabOrder = 0
              end
              object S: TStatusBar
                Left = 0
                Top = 181
                Width = 300
                Height = 19
                Panels = <
                  item
                    Text = 'a' +
                      'b'
                    Width = $10
                  end
                >
              end
            end

            """.ReplaceLineEndings("\n"), Write(Input));
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-048")]
    public void WritesPreservedObjectsVerbatimReindented()
    {
        const string Input = """
            object F: TForm
              Left = 200
              Top = 120
              ClientWidth = 300
              ClientHeight = 200
              PixelsPerInch = 96
              object P: TPanel
                Left = 0
                Top = 0
                Width = 300
                Height = 41
                TabOrder = 0
                     object Chart1: TChart
                       Zeta = 1
                         Alpha = 2
                       object Series1: TBarSeries
                           Values = (
                       '1'
                           )
                       end
                     end
                inline Frame1: TFrame
                end
              end
            end
            """;

        // The header goes to the canonical level, the relative indentation inside the block is kept as read; the
        // property order inside the block is not touched.
        Assert.Equal("""
            object F: TForm
              Left = 200
              Top = 120
              ClientWidth = 300
              ClientHeight = 200
              PixelsPerInch = 96
              object P: TPanel
                Left = 0
                Top = 0
                Width = 300
                Height = 41
                TabOrder = 0
                object Chart1: TChart
                  Zeta = 1
                    Alpha = 2
                  object Series1: TBarSeries
                      Values = (
                  '1'
                      )
                  end
                end
                inline Frame1: TFrame
                end
              end
            end

            """.ReplaceLineEndings("\n"), Write(Input));

        // A clipboard fragment starts at level 0.
        var document = FormParser.Parse(Input, "p.form", Catalog).Document!;
        Assert.Equal("object Chart1: TChart\n  Zeta = 1\n    Alpha = 2\n  object Series1: TBarSeries\n      Values = (\n  '1'\n      )\n  end\nend\n",
            FormWriter.WriteObjects([document.FindByName("Chart1")!]));
    }

    /// <summary>
    /// DSG-FD-043, DSG-FD-044: <c>"stored": false</c> (tab-sheet and tool-button geometry of the framework metadata) wins
    /// over every writing rule: never written for designer values, never always-written, sub-properties of a non-stored
    /// object property included; a line read from the file is kept as read until the object is edited (D-095).
    /// </summary>
    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-043")]
    [Trait("Req", "DSG-FD-044")]
    public void NeverWritesNonStoredProperties()
    {
        var catalog = Layout.LayoutHarness.Catalog;
        const string Input = """
            object F: TForm
              Left = 0
              Top = 0
              ClientWidth = 300
              ClientHeight = 200
              PixelsPerInch = 96
              object P: TPageControl
                Left = 0
                Top = 0
                Width = 289
                Height = 193
                object S1: TTabSheet
                  Left = 4
                  Top = 27
                  Width = 281
                  Height = 162
                  Caption = 'One'
                end
                object S2: TTabSheet
                  Caption = 'Two'
                end
              end
              object B: TToolBar
                Left = 0
                Top = 0
                Width = 300
                Height = 29
                object T: TToolButton
                  Width = 23
                end
              end
            end

            """;
        var document = FormParser.Parse(Input.ReplaceLineEndings("\n"), "s.form", catalog).Document!;
        Assert.Empty(document.Diagnostics);

        // As read: the sheet's lines stay; nothing is added for sheets or tool buttons (no always-written geometry).
        Assert.Equal(Input.ReplaceLineEndings("\n"), FormWriter.Write(document));

        var editor = new FreexComp.Designer.Editing.FormEditor(document, catalog);
        editor.Apply(
        [
            new FreexComp.Designer.Editing.SetProperty("S1", "Caption", new StringValue("First")),
            new FreexComp.Designer.Editing.SetProperty("S2", "Left", new IntegerValue(9, false)),
            new FreexComp.Designer.Editing.SetProperty("S2", "Margins.Left", new IntegerValue(7, false)),
            new FreexComp.Designer.Editing.SetProperty("T", "Top", new IntegerValue(5, false)),
            new FreexComp.Designer.Editing.SetProperty("T", "Caption", new StringValue("Go")),
        ]);

        // Editing S1 drops its read geometry; designer values of non-stored properties are never written.
        Assert.Equal("""
            object F: TForm
              Left = 0
              Top = 0
              ClientWidth = 300
              ClientHeight = 200
              PixelsPerInch = 96
              object P: TPageControl
                Left = 0
                Top = 0
                Width = 289
                Height = 193
                object S1: TTabSheet
                  Caption = 'First'
                end
                object S2: TTabSheet
                  Caption = 'Two'
                end
              end
              object B: TToolBar
                Left = 0
                Top = 0
                Width = 300
                Height = 29
                object T: TToolButton
                  Width = 23
                  Caption = 'Go'
                end
              end
            end

            """.ReplaceLineEndings("\n"), FormWriter.Write(document));
    }

    private static string Write(string input)
    {
        var result = FormParser.Parse(input, "w.form", Catalog);
        Assert.False(result.HasSyntaxErrors, string.Join("; ", result.Diagnostics));
        return FormWriter.Write(result.Document!);
    }

    private static string Format(FormValue value, PropertyInfo? info = null) =>
        Assert.Single(FormWriter.FormatValue(value, info, Catalog)).Text;
}

internal static class StringTestExtensions
{
    public static string Quoted(this string s) => "'" + s + "'";
}
