using FreexComp.Compiler;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.Model;
using FreexComp.Designer.Text;

namespace FreexComp.Designer.Tests.Text;

/// <summary>Value kinds, strings and syntax errors (D-009, D-010).</summary>
public sealed partial class FormParserTests
{
    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-022")]
    [Trait("Req", "DSG-FD-055")]
    public void ParsesEveryValueKind()
    {
        var path = TestPaths.Combine("forms/canonical/all-value-kinds.form");
        var result = FormParser.Parse(File.ReadAllText(path), path, Catalog);
        Assert.False(result.HasSyntaxErrors, string.Join("; ", result.Diagnostics));
        var document = result.Document!;
        FormValue Value(string obj, string property) => document.FindByName(obj)!.FindProperty(property)!.Value;

        // integer (decimal, negative, hex), float (plain, exponent, negative), string, identifier, qualified identifier
        Assert.Equal(new IntegerValue(long.MinValue, false), Value("ValuesForm", "Tag"));
        Assert.Equal(new IntegerValue(-5, false), Value("Memo1", "Left"));
        Assert.Equal(new IntegerValue(0x00C0FFEE, true), Value("ValuesForm", "Color"));
        Assert.Equal(new FloatValue(1.5), Value("Image1", "Scale"));
        Assert.Equal(new FloatValue(1.5E+20), Value("Image1", "Huge"));
        Assert.Equal(new FloatValue(-2.5E-7), Value("Image1", "Tiny"));
        Assert.Equal(new FloatValue(-0.25), Value("Image1", "Ratio"));
        Assert.Equal(new StringValue("Café ünicode ✓ 漢字 \U0001F600"), Value("ValuesForm", "Font.Name"));
        Assert.Equal(new StringValue("'quoted' and #hash"), Value("Edit1", "Text"));
        Assert.Equal(new IdentValue("poScreenCenter"), Value("ValuesForm", "Position"));
        Assert.Equal(new IdentValue("True"), Value("ValuesForm", "KeyPreview"));
        Assert.Equal(new IdentValue("clInfoBk"), Value("Edit1", "Color"));
        Assert.Equal(new IdentValue("MainMenu1"), Value("ValuesForm", "Menu"));
        Assert.Equal(new QualifiedIdentValue(["OtherForm", "PopupMenu1"]), Value("Memo1", "PopupMenu"));

        // set (order as read), string list, binary, collection
        Assert.Equal(new SetValue(["akLeft", "akTop", "akRight"]), Value("Edit1", "Anchors"));
        Assert.Equal(new SetValue(["fsBold", "fsItalic"]), Value("ValuesForm", "Font.Style"));
        Assert.Equal(new SetValue([]), Single("object F: TForm\n  BorderIcons = [ ]\nend\n").Root.Properties[0].Value);
        Assert.Equal(new SetValue(["biMinimize", "biSystemMenu"]), Single("object F: TForm\n  BorderIcons = [ biMinimize ,biSystemMenu ]\nend\n").Root.Properties[0].Value);
        Assert.Equal(new StringListValue(["first line", "it's the second", "\ttabbed\0", "",
            "A string list item that is longer than sixty-four characters, so it is continued."]), Value("Memo1", "Lines"));
        var bytes = Enumerable.Range(0, 32).Select(i => (byte)i).Concat([(byte)0xFF, (byte)0xFE]);
        Assert.Equal(new BinaryValue([.. bytes]), Value("Image1", "Picture.Data"));
        Assert.Equal(new BinaryValue([0xAB, 0xCD]), Single("object F: TForm\n  X = {\n    abCD\n  }\nend\n").Root.Properties[0].Value);
        var panels = Assert.IsType<CollectionValue>(Value("StatusBar1", "Panels"));
        Assert.Equal(2, panels.Items.Length);
        Assert.Equal([("Text", (FormValue)new StringValue("Ready"), (string?)"'Ready'"), ("Width", new IntegerValue(120, false), "120")],
            panels.Items[0].Properties.Select(p => (p.Path, p.Value, p.RawText)));
        Assert.Equal(["Alignment", "Bevel"], panels.Items[1].Properties.Select(p => p.Path));
        Assert.Equal(new IdentValue("pbRaised"), panels.Items[1].Properties[1].Value);
        Assert.Equal(new CollectionValue([]),
            Single("object F: TForm\n  object S: TStatusBar\n    Panels = <\n    >\n  end\nend\n").FindByName("S")!.FindProperty("Panels")!.Value);

        // Bound metadata: a collection property; the tray position of a non-visual component.
        Assert.Equal(PropertyKind.Collection, document.FindByName("StatusBar1")!.FindProperty("Panels")!.Info!.Kind);
        Assert.Same(ComponentCatalog.DesignLeftProperty, document.FindByName("MainMenu1")!.FindProperty("DesignLeft")!.Info);

        // Raw text of a multi-line value: all lines as read (LF).
        Assert.Equal("{\n      000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F\n      FFFE\n    }",
            document.FindByName("Image1")!.FindProperty("Picture.Data")!.RawText);

        // ")" may also end the last item line (DSG-FD-055); blank lines between items are accepted.
        Assert.Equal(new StringListValue(["a", "b"]),
            Single("object F: TForm\n  object M: TMemo\n    Lines = (\n\n      'a'\n\n      'b')\n  end\nend\n").FindByName("M")!.FindProperty("Lines")!.Value);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-024")]
    public void StringConcatenationAndControlChars()
    {
        const string Text = """
            object F: TForm
              Caption = 'Line 1'#13#10'Line 2'
              Hint = 'first part ' +
                'second part' +

                ' and the end'
              object E: TEdit
                Text = #$41#66'c'#$263a''''
                TextHint = #0
              end
              object M: TMemo
                Lines = (
                  'one' +
                    ' two'
                  #9
                )
              end
            end
            """;
        var document = Single(Text);
        FormValue Value(string obj, string property) => document.FindByName(obj)!.FindProperty(property)!.Value;

        Assert.Equal(new StringValue("Line 1\r\nLine 2"), Value("F", "Caption"));
        Assert.Equal(new StringValue("first part second part and the end"), Value("F", "Hint"));
        Assert.Equal(new StringValue("ABc☺'"), Value("E", "Text"));
        Assert.Equal(new StringValue("\0"), Value("E", "TextHint"));
        Assert.Equal(new StringListValue(["one two", "\t"]), Value("M", "Lines"));
        Assert.Equal("'first part ' +\n    'second part' +\n\n    ' and the end'", document.Root.FindProperty("Hint")!.RawText);

        // Any other "+" is a syntax error; "#n" must fit one UTF-16 code unit.
        Assert.Equal("Syntax error: expected end of line but found \"+\"", SyntaxError("object F: TForm\n  Caption = 'a' + 'b'\nend\n"));
        Assert.Equal("Syntax error: expected end but found \"+\"", SyntaxError("object F: TForm\n  Caption = 'a'\n  + 'b'\nend\n"));
        Assert.Equal("Syntax error: expected string but found \"Hint\"", SyntaxError("object F: TForm\n  Caption = 'a' +\n  Hint = 'b'\nend\n"));
        Assert.Equal("Syntax error: expected character code 0..65535 but found \"#65536\"", SyntaxError("object F: TForm\n  Caption = #65536\nend\n"));
        Assert.Equal(new StringValue("\uFFFF"), Single("object F: TForm\n  Caption = #65535\nend\n").Root.Properties[0].Value);
    }

    public static TheoryData<string> InvalidFiles() => [.. CorpusFiles("invalid")];

    [Theory]
    [MemberData(nameof(InvalidFiles))]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-025")]
    [Trait("Req", "DSG-FD-030")]
    public void SyntaxErrorStopsWithSingleF0001(string file)
    {
        var path = TestPaths.Combine("forms/invalid/" + file);
        var result = FormParser.Parse(File.ReadAllText(path), file, Catalog);

        Assert.True(result.HasSyntaxErrors);
        Assert.Null(result.Document);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(("F0001", DiagnosticSeverity.Error), (diagnostic.Code, diagnostic.Severity));
        Assert.StartsWith("Syntax error: ", diagnostic.Message, StringComparison.Ordinal);
        Golden.Check(Path.ChangeExtension(path, ".diag.txt"), diagnostic + "\n");
    }

    /// <summary>The <c>.form</c> files of a corpus folder, without expected-output files.</summary>
    internal static IEnumerable<string> CorpusFiles(string folder) =>
        Directory.EnumerateFiles(Path.Combine(TestPaths.Forms, folder), "*.form")
            .Select(f => Path.GetFileName(f))
            .Where(f => !f.EndsWith(".expected.form", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

    private static FormDocument Single(string text)
    {
        var result = FormParser.Parse(text, "inline.form", Catalog);
        Assert.False(result.HasSyntaxErrors, string.Join("; ", result.Diagnostics));
        return result.Document!;
    }

    private static string SyntaxError(string text) => Assert.Single(FormParser.Parse(text, "inline.form", Catalog).Diagnostics).Message;
}
