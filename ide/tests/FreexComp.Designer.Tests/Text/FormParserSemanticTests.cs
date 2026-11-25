using FreexComp.Compiler;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.Model;
using FreexComp.Designer.Text;

namespace FreexComp.Designer.Tests.Text;

/// <summary>Semantic binding to the catalog and preserved items (D-011; codes of form-format.md §5).</summary>
public sealed partial class FormParserTests
{
    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-026")]
    public void UnknownClassIsPreservedWithF0101()
    {
        var (document, diagnostics) = ParseSemantic("unknown-class");

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(("F0101", DiagnosticSeverity.Warning, "Class TChart not found"), (diagnostic.Code, diagnostic.Severity, diagnostic.Message));
        Assert.Equal((7, 18, 7, 24, "Chart1"), (diagnostic.Line, diagnostic.Col, diagnostic.EndLine, diagnostic.EndCol, diagnostic.ObjectName));

        var chart = document.FindByName("Chart1")!;
        Assert.Null(chart.Info);
        Assert.True(chart.IsPreserved);
        Assert.True(document.FindByName("Series1")!.IsPreserved); // the whole subtree
        Assert.Null(document.FindByName("Series1")!.PreservedText); // the outermost preserved object holds the text
        Assert.Equal(
            "object Chart1: TChart\n  Left = 8\n  Top = 8\n  Width = 300\n  Height = 200\n  Title.Text = (\n    'Sales'\n  )\n"
            + "  object Series1: TBarSeries\n    Marks.Visible = False\n  end\nend\n",
            chart.PreservedText);
        Assert.False(document.FindByName("OkButton")!.IsPreserved);
        Assert.False(document.Root.IsPreserved);

        // The root binds to the first form class, or to the given ancestor, and never raises F0101 (lead decision D-011).
        Assert.Same(Catalog.Find("TForm"), document.Root.Info);
        var withAncestor = FormParser.Parse("object F: TMyForm\nend\n", "a.form", Catalog, rootAncestorClass: "TPanel");
        Assert.Same(Catalog.Find("TPanel"), withAncestor.Document!.Root.Info);
        Assert.Empty(withAncestor.Diagnostics);
        var noFormClass = FormParser.Parse("object F: TMyForm\nend\n", "n.form", ComponentCatalog.Empty);
        Assert.Equal("F0101", Assert.Single(noFormClass.Diagnostics).Code);
        Assert.True(noFormClass.Document!.Root.IsPreserved);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-026")]
    [Trait("Req", "DSG-FD-056")]
    public void UnknownPropertyIsPreservedWithF0102()
    {
        var (document, diagnostics) = ParseSemantic("unknown-property");

        Assert.Equal(
        [
            "unknown-property.form(7,3): warning F0102: Property Name does not exist on TMainForm",
            "unknown-property.form(16,5): warning F0102: Property Flat does not exist on TButton",
            "unknown-property.form(17,5): warning F0102: Property Font.Nope does not exist on TButton",
            "unknown-property.form(18,5): warning F0102: Property Caption.Size does not exist on TButton",
            "unknown-property.form(27,5): warning F0102: Property Lines.Strings does not exist on TMemo",
        ], diagnostics.Select(d => d.ToString()));
        Assert.Equal(("OkButton", "Font.Nope", 17, 14), (diagnostics[2].ObjectName, diagnostics[2].PropertyPath, diagnostics[2].EndLine, diagnostics[2].EndCol));

        var button = document.FindByName("OkButton")!;
        var flat = button.FindProperty("Flat")!;
        Assert.True(flat.IsPreserved);
        Assert.Null(flat.Info);
        Assert.Equal(new IdentValue("True"), flat.Value); // a parsable value is kept as parsed, with its raw text
        Assert.Equal("True", flat.RawText);
        Assert.False(button.IsPreserved);
        Assert.False(button.FindProperty("Caption")!.IsPreserved);
        Assert.True(button.FindProperty("OnClick")!.IsEvent);
        Assert.Equal(new StringListValue(["old style"]), document.FindByName("Memo1")!.FindProperty("Lines.Strings")!.Value);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-026")]
    public void TypeMismatchIsPreservedWithF0103()
    {
        var (document, diagnostics) = ParseSemantic("type-mismatch");

        Assert.Equal(
        [
            "type-mismatch.form(13,11): warning F0103: Invalid value for property Tag: value kind does not fit type Int64",
            "type-mismatch.form(14,13): warning F0103: Invalid value for property Align: 'alMiddle' is not a member of TAlign",
            "type-mismatch.form(15,15): warning F0103: Invalid value for property Anchors: 'akNowhere' is not a member of TAnchors",
            "type-mismatch.form(16,12): warning F0103: Invalid value for property Font: value kind does not fit type TFont",
            "type-mismatch.form(17,13): warning F0103: Invalid value for property Color: value kind does not fit type TColor",
            "type-mismatch.form(18,15): warning F0103: Invalid value for property Visible: value kind does not fit type Boolean",
            "type-mismatch.form(19,17): warning F0103: Invalid value for property PopupMenu: value kind does not fit type TPopupMenu",
            "type-mismatch.form(20,20): warning F0103: Invalid value for property PasswordChar: value kind does not fit type Char",
            "type-mismatch.form(21,12): warning F0103: Invalid value for property Text: value kind does not fit type string",
            "type-mismatch.form(22,16): warning F0103: Invalid value for property OnChange: value kind does not fit type TNotifyEvent",
        ], diagnostics.Select(d => d.ToString()));
        Assert.All(diagnostics, d => Assert.Equal("Edit1", d.ObjectName));

        var edit = document.FindByName("Edit1")!;
        var align = edit.FindProperty("Align")!;
        Assert.True(align.IsPreserved);
        Assert.Equal(new RawValue("alMiddle"), align.Value);
        Assert.Equal("alMiddle", align.RawText);
        Assert.NotNull(align.Info); // still bound: the property exists, its value does not fit
        Assert.False(edit.FindProperty("TabOrder")!.IsPreserved);
        Assert.False(edit.IsPreserved);

        // Every accepted kind of the §3.1 table raises nothing.
        const string Fits = """
            object F: TForm
              Tag = 5
              Color = $FF
              Cursor = crHandPoint
              BorderIcons = [BIMINIMIZE]
              Position = POSCREENCENTER
              KeyPreview = true
              ActiveControl = Other.Edit1
              object E: TEdit
                PasswordChar = '*'
                Color = clSomethingNew
                Cursor = -21
                OnChange = EChange
              end
              object S: TStatusBar
                Panels = <
                  item
                    Whatever = 1
                  end
                >
              end
              object M: TMemo
                Lines = (
                  'x'
                )
              end
              object T: TTimer
                DesignLeft = 8
              end
            end
            """;
        Assert.Empty(FormParser.Parse(Fits, "fits.form", Catalog).Diagnostics);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-026")]
    [Trait("Req", "DSG-FD-003")]
    public void DuplicateNameReportsF0106()
    {
        var (document, diagnostics) = ParseSemantic("duplicate-name");

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("duplicate-name.form(21,12): error F0106: A component named button1 already exists", diagnostic.ToString());

        var first = document.FindByName("BUTTON1")!;
        Assert.Same(document.Root, first.Parent); // the first object owns the name
        Assert.False(first.IsPreserved);
        var second = document.FindByName("Panel1")!.Children.Single();
        Assert.Equal("button1", second.Name);
        Assert.True(second.IsPreserved);
        Assert.Same(Catalog.Find("TButton"), second.Info);
        Assert.StartsWith("object button1: TButton\n  Left = 8\n", second.PreservedText, StringComparison.Ordinal);
        Assert.True(document.FindByName("Inner1")!.IsPreserved);

        // Names are unique across the whole form, preserved objects included.
        var nested = FormParser.Parse("object F: TForm\n  object X: TUnknown\n    object B: TButton\n    end\n  end\n  object b: TButton\n  end\nend\n", "d.form", Catalog);
        Assert.Equal(["F0101", "F0106"], nested.Diagnostics.Select(d => d.Code));
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-026")]
    [Trait("Req", "DSG-FD-027")]
    public void InheritedAndInlineArePreservedWithF0107()
    {
        var (document, diagnostics) = ParseSemantic("inherited-inline");

        Assert.Equal(
        [
            "inherited-inline.form(7,3): hint F0107: \"inline\" objects are not supported: shown read-only",
            "inherited-inline.form(17,3): hint F0107: \"inherited\" objects are not supported: shown read-only",
        ], diagnostics.Select(d => d.ToString()));
        var frame = document.FindByName("Frame1")!;
        Assert.Equal((FormObjectKind.Inline, true), (frame.Kind, frame.IsPreserved));
        // nothing more is reported inside a preserved subtree; D-118 (DSG-FD-108, form-format.md §2.1): the blocks of an
        // `inline` object name components of the frame, so InnerButton is not a name of the file scope
        Assert.Null(document.FindByName("InnerButton"));
        Assert.True(Assert.Single(frame.Children, c => c.Name == "InnerButton").IsPreserved);
        var base1 = document.FindByName("Base1")!;
        Assert.Equal((FormObjectKind.Inherited, true), (base1.Kind, base1.IsPreserved));
        Assert.Same(Catalog.Find("TButton"), base1.Info);
        Assert.False(document.FindByName("OkButton")!.IsPreserved);

        // DSG-FD-027: an inherited root preserves the whole form (it opens read-only).
        var (root, rootDiagnostics) = ParseSemantic("inherited-root");
        Assert.Equal("inherited-root.form(1,1): hint F0107: \"inherited\" objects are not supported: shown read-only", Assert.Single(rootDiagnostics).ToString());
        Assert.All(root.AllObjects(), o => Assert.True(o.IsPreserved));
        Assert.Equal(File.ReadAllText(TestPaths.Combine("forms/semantic/inherited-root.form")), root.Root.PreservedText);
    }

    private static (FormDocument Document, IReadOnlyList<FormDiagnostic> Diagnostics) ParseSemantic(string name)
    {
        var file = name + ".form";
        var result = FormParser.Parse(File.ReadAllText(TestPaths.Combine("forms/semantic/" + file)), file, Catalog);
        Assert.False(result.HasSyntaxErrors, string.Join("; ", result.Diagnostics));
        Assert.Equal(result.Diagnostics, result.Document!.Diagnostics);
        return (result.Document, result.Diagnostics);
    }
}
