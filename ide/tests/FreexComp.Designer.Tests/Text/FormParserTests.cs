using FreexComp.Compiler;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.Model;
using FreexComp.Designer.Text;

namespace FreexComp.Designer.Tests.Text;

public sealed class FormParserTests
{
    private static readonly ComponentCatalog Catalog = TestCatalog.Instance;

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-001")]
    [Trait("Req", "DSG-FD-003")]
    public void ParsesExampleFromInterfaceSpec()
    {
        var path = TestPaths.Combine("forms/canonical/hello.form");
        var result = FormParser.Parse(File.ReadAllText(path), path, Catalog);

        Assert.False(result.HasSyntaxErrors);
        var document = result.Document!;
        Assert.Equal("\n", document.LineEnding);
        Assert.Equal(0, document.Version);

        var root = document.Root;
        Assert.Equal((FormObjectKind.Object, "MainForm", "TMainForm"), (root.Kind, root.Name, root.TypeName));
        Assert.Null(root.Parent);
        Assert.Equal(["Left", "Top", "Caption", "ClientWidth", "ClientHeight", "PixelsPerInch", "OnCreate"], root.Properties.Select(p => p.Path));
        Assert.Equal(new IntegerValue(200, false), root.FindProperty("Left")!.Value);
        Assert.Equal(new IntegerValue(120, false), root.FindProperty("Top")!.Value);
        Assert.Equal(new StringValue("Hello"), root.FindProperty("Caption")!.Value);
        Assert.Equal(new IntegerValue(400, false), root.FindProperty("ClientWidth")!.Value);
        Assert.Equal(new IntegerValue(240, false), root.FindProperty("ClientHeight")!.Value);
        Assert.Equal(new IntegerValue(96, false), root.FindProperty("PixelsPerInch")!.Value);
        Assert.Equal(new IdentValue("FormCreate"), root.FindProperty("OnCreate")!.Value);
        Assert.Equal("'Hello'", root.FindProperty("Caption")!.RawText);

        Assert.Equal(["NameEdit", "GreetButton"], root.Children.Select(c => c.Name));
        var edit = root.Children[0];
        Assert.Equal((FormObjectKind.Object, "TEdit"), (edit.Kind, edit.TypeName));
        Assert.Same(root, edit.Parent);
        Assert.Same(Catalog.Find("TEdit"), edit.Info);
        Assert.Equal(["Left", "Top", "Width", "Height", "TabOrder"], edit.Properties.Select(p => p.Path));
        Assert.Equal([16L, 16L, 200L, 23L, 0L], edit.Properties.Select(p => ((IntegerValue)p.Value).Value));
        Assert.Same(Catalog.Find("TEdit")!.FindProperty("TabOrder"), edit.FindProperty("TabOrder")!.Info);
        Assert.Empty(edit.Children);

        var button = root.Children[1];
        Assert.Equal("TButton", button.TypeName);
        Assert.Equal(new StringValue("&Greet"), button.FindProperty("Caption")!.Value);
        Assert.Equal(new IdentValue("GreetButtonClick"), button.FindProperty("OnClick")!.Value);
        Assert.True(button.FindProperty("OnClick")!.IsEvent);
        Assert.Same(Catalog.Find("TButton")!.FindEvent("OnClick"), button.FindProperty("OnClick")!.EventInfo);
        Assert.False(button.FindProperty("Caption")!.IsEvent);
        Assert.Equal(new IntegerValue(1, false), button.FindProperty("TabOrder")!.Value);

        Assert.Equal(["MainForm", "NameEdit", "GreetButton"], document.AllObjects().Select(o => o.Name));
        Assert.Same(button, document.FindByName("GreetButton"));
        Assert.Null(document.FindByName("Missing"));
        Assert.All(document.AllObjects(), o => Assert.False(o.IsPreserved));

        // Spans point into the text.
        var text = File.ReadAllText(path);
        Assert.Equal(0, root.SourceSpan.Start);
        Assert.Equal(text.Length, root.SourceSpan.Length);
        var caption = button.FindProperty("Caption")!.SourceSpan;
        Assert.Equal("Caption = '&Greet'", text.Substring(caption.Start, caption.Length));
        Assert.StartsWith("object GreetButton: TButton", text[button.SourceSpan.Start..].TrimStart(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-021")]
    [Trait("Req", "DSG-FD-002")]
    public void KeywordsAndNamesAreCaseInsensitive()
    {
        const string Text = """
            OBJECT Form1: TForm
              caption = 'x'
              Object Btn: tbutton
                CAPTION = 'Go'
                onclick = BtnClick
              END
              InLine Frame1: TFrame
              eNd
              inherited Base1: TPanel
              end
            End
            """;
        var document = FormParser.Parse(Text, "case.form", Catalog).Document!;

        Assert.Equal(FormObjectKind.Object, document.Root.Kind);
        Assert.Equal([FormObjectKind.Object, FormObjectKind.Inline, FormObjectKind.Inherited], document.Root.Children.Select(c => c.Kind));
        var button = document.FindByName("BTN")!;
        Assert.Equal(("Btn", "tbutton"), (button.Name, button.TypeName)); // declared casing is kept
        Assert.Same(Catalog.Find("TButton"), button.Info);
        Assert.Same(document.Root, document.FindByName("form1"));
        Assert.Equal("CAPTION", button.FindProperty("caption")!.Path);
        Assert.Equal(new StringValue("Go"), button.FindProperty("Caption")!.Value);
        Assert.Equal("Caption", button.FindProperty("CAPTION")!.Info!.Name); // bound to metadata regardless of case
        Assert.True(button.FindProperty("OnClick")!.IsEvent);

        // A keyword followed by "=" or "." is a property path, not a keyword.
        var withKeywordNames = FormParser.Parse("object F: TForm\n  End = 1\n  Object.Tag = 2\nend\n", "k.form", Catalog);
        Assert.Equal(["End", "Object.Tag"], withKeywordNames.Document!.Root.Properties.Select(p => p.Path));

        // A unit-qualified type name is a syntax error (DSG-FD-021).
        var qualified = FormParser.Parse("object F: Forms.TForm\nend\n", "q.form", Catalog);
        Assert.True(qualified.HasSyntaxErrors);
        var diagnostic = Assert.Single(qualified.Diagnostics);
        Assert.Equal(("F0001", DiagnosticSeverity.Error, 1, 16), (diagnostic.Code, diagnostic.Severity, diagnostic.Line, diagnostic.Col));
        Assert.Equal("Syntax error: expected end of line but found \".\"", diagnostic.Message);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-020")]
    public void AcceptsCrLfBomAndBlankLines()
    {
        var canonical = File.ReadAllText(TestPaths.Combine("forms/canonical/hello.form"));
        var expected = FormParser.Parse(canonical, "hello.form", Catalog).Document!;

        // BOM, CRLF, tabs and odd indentation, trailing whitespace, blank lines (also with spaces) between every item,
        // spaces around ':' and '=', blank lines before the root and after the final end.
        var messy = "﻿\r\n  \r\n" + string.Concat(canonical.Split('\n').Select((line, i) =>
            (i % 2 == 0 ? "\t" : "   ") + line.Trim().Replace(": ", " : ", StringComparison.Ordinal).Replace(" = ", "   =\t", StringComparison.Ordinal)
            + " \t\r\n" + (i % 3 == 0 ? "\r\n" : "  \t\r\n")));
        var result = FormParser.Parse(messy, "messy.form", Catalog);

        Assert.False(result.HasSyntaxErrors, string.Join("; ", result.Diagnostics));
        var document = result.Document!;
        Assert.Equal("\r\n", document.LineEnding);
        Assert.Equal(Shape(expected), Shape(document));
        Assert.Equal("'Hello'", document.Root.FindProperty("Caption")!.RawText); // trailing whitespace is not part of the value

        // LF without a final line end (end of file counts as a line end); a lone CR is not a line end.
        Assert.False(FormParser.Parse("object F: TForm\nend", "f.form", Catalog).HasSyntaxErrors);
        Assert.True(FormParser.Parse("object F: TForm\rend\r", "cr.form", Catalog).HasSyntaxErrors);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-022")]
    [Trait("Req", "DSG-FD-023")]
    public void ParsesSimpleValues()
    {
        const string Text = """
            object F: TForm
              Tag = -9223372036854775808
              Color = $FFFFFFFF
              object E: TEdit
                Tag = $FFFFFFFF
                Left = -5
                Color = $00C0FFEE
                Text = 'it''s'
                PopupMenu = Other.Menu1
                Align = alClient
                Font.Height = $FFFFFFF4
              end
              object U: TUnknown
                Weight = 1.5
                Big = 2.5E+20
                Exp = 3e2
                Small = -0.25
              end
            end
            """;
        var document = FormParser.Parse(Text, "values.form", Catalog).Document!;
        FormValue Value(string obj, string path) => document.FindByName(obj)!.FindProperty(path)!.Value;

        Assert.Equal(new IntegerValue(long.MinValue, false), Value("F", "Tag"));
        Assert.Equal(new IntegerValue(-1, true), Value("F", "Color"));            // TColor: 32-bit pattern
        Assert.Equal(new IntegerValue(0xFFFFFFFF, true), Value("E", "Tag"));      // Int64: unsigned
        Assert.Equal(new IntegerValue(-5, false), Value("E", "Left"));
        Assert.Equal(new IntegerValue(0x00C0FFEE, true), Value("E", "Color"));
        Assert.Equal(new IntegerValue(-12, true), Value("E", "Font.Height"));     // sub-property typed Integer
        Assert.Equal(new StringValue("it's"), Value("E", "Text"));
        Assert.Equal(new QualifiedIdentValue(["Other", "Menu1"]), Value("E", "PopupMenu"));
        Assert.Equal(new IdentValue("alClient"), Value("E", "Align"));
        Assert.Equal(new FloatValue(1.5), Value("U", "Weight"));
        Assert.Equal(new FloatValue(2.5E+20), Value("U", "Big"));
        Assert.Equal(new FloatValue(300), Value("U", "Exp"));
        Assert.Equal(new FloatValue(-0.25), Value("U", "Small"));
        Assert.Equal("$00C0FFEE", document.FindByName("E")!.FindProperty("Color")!.RawText);

        Assert.Equal("Syntax error: integer out of range",
            Assert.Single(FormParser.Parse("object F: TForm\n  Tag = 9223372036854775808\nend\n", "r.form", Catalog).Diagnostics).Message);
        Assert.Equal("Syntax error: integer out of range",
            Assert.Single(FormParser.Parse("object F: TForm\n  Tag = $10000000000000000\nend\n", "r.form", Catalog).Diagnostics).Message);
    }

    /// <summary>The object tree with every property path and value, for structural comparison.</summary>
    private static List<string> Shape(FormDocument document) =>
        [.. document.AllObjects().SelectMany(o => new[] { $"{o.Kind} {o.Name}: {o.TypeName} in {o.Parent?.Name}" }
            .Concat(o.Properties.Select(p => $"  {p.Path} = {p.Value}")))];
}
