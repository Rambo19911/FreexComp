using System.Text.RegularExpressions;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;
using FreexComp.Designer.Text;

namespace FreexComp.Designer.Tests.Text;

/// <summary>The written line formats and property order of form-document.md §2 / §4 (D-087).</summary>
[Trait("Phase", "5")]
public sealed partial class FormFormatRulesTests
{
    [GeneratedRegex(@"^(?<indent>(?:  )*)(?<kind>object|inherited|inline) (?<name>[A-Za-z_][A-Za-z0-9_]*): (?<type>[A-Za-z_][A-Za-z0-9_.]*)$")]
    private static partial Regex Header();

    [GeneratedRegex(@"^(?:  )+[A-Za-z_][A-Za-z0-9_.]* = \S")]
    private static partial Regex PropertyLine();

    private static FormDocument Parse(string text, bool allowDiagnostics = false)
    {
        var document = FormParser.Parse(text.Replace("\r\n", "\n"), "rules.form", TestCatalog.Instance).Document!;
        Assert.True(allowDiagnostics || document.Diagnostics.Count == 0, string.Join("\n", document.Diagnostics));
        return document;
    }

    [Fact]
    [Trait("Req", "DSG-FD-041")]
    [Trait("Req", "DSG-FD-045")]
    public void HeadersAndPropertyLinesHaveSingleSpaces()
    {
        // Any keyword case and spacing on input; on output the header is "<kind> <Name>: <TypeName>" with single spaces
        // and the keyword in lower case, and a property line is "<indent><Path> = <value>".
        const string Input = """
            OBJECT   F :  TForm
              Caption   =   'x'
              ClientWidth=300
              ClientHeight = 200
              Object  P:TPanel
                Left = 8
                Top = 8
                Width = 100
                Height = 50
                Caption='p'
                TabOrder = 0
                Object  L :TLabel
                  Left = 8
                  Top = 8
                  Width = 6
                  Height = 15
                  Caption = 'L'
                eND
              END
            End
            """;
        var written = FormWriter.Write(Parse(Input));
        var lines = written.TrimEnd('\n').Split('\n');
        var headers = lines.Select(l => Header().Match(l)).Where(m => m.Success).ToList();
        Assert.Equal(["object F: TForm", "object P: TPanel", "object L: TLabel"],
            headers.Select(m => m.Value.TrimStart()));
        Assert.All(lines.Where(l => l.TrimStart() is not "end" && !Header().IsMatch(l)), line => Assert.Matches(PropertyLine(), line));
        Assert.Contains("  Caption = 'x'", lines);
        Assert.Contains("  ClientWidth = 300", lines);
        Assert.Contains("    Caption = 'p'", lines);
    }

    [Fact]
    [Trait("Req", "DSG-FD-005")]
    public void SubPropertiesAreFlatInMetadataOrder()
    {
        using var h = Support.DesignerHarness.FromText("""
            object F: TForm
              ClientWidth = 300
              ClientHeight = 200
              object P: TPanel
                Left = 8
                Top = 8
                Width = 100
                Height = 50
                Caption = 'p'
                TabOrder = 0
              end
            end
            """);

        // Set out of order: Font.Style before Font.Color, Padding and Color after Font (a Font change clears ParentFont).
        h.Editor.Apply([
            new SetProperty("P", "Font.Style", new SetValue(["fsBold"])),
            new SetProperty("P", "Font.Color", new IdentValue("clRed")),
            new SetProperty("P", "Padding.Left", new IntegerValue(4, false)),
            new SetProperty("P", "Color", new IdentValue("clWindow")),
        ]);

        // Stored flat with their dotted path; parent property position in metadata (Caption, Color, Font, Padding, TabOrder),
        // then the sub-properties in the object class's declaration order (Color before Style).
        var panel = h.Text.Split('\n').SkipWhile(l => l != "  object P: TPanel").Skip(1).TakeWhile(l => l != "  end").ToList();
        Assert.Equal(
        [
            "    Left = 8", "    Top = 8", "    Width = 100", "    Height = 50", "    Caption = 'p'", "    Color = clWindow",
            "    Font.Color = clRed", "    Font.Style = [fsBold]", "    Padding.Left = 4", "    ParentFont = False", "    TabOrder = 0",
        ], panel);
    }

    [Fact]
    [Trait("Req", "DSG-FD-011")]
    public void NameIsTheHeaderNotAProperty()
    {
        using var h = Support.DesignerHarness.FromFile("forms/canonical/hello.form");

        // Renaming changes the object header; no "Name =" line is ever written.
        h.Editor.Apply(new RenameObject("GreetButton", "HelloButton"));
        Assert.Contains("  object HelloButton: TButton\n", h.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Name =", h.Text, StringComparison.Ordinal);
        Assert.Null(h.Object("HelloButton").FindProperty("Name"));

        // "Name" is not a stored property path: setting it is refused.
        Assert.ThrowsAny<Exception>(() => h.Editor.Apply(new SetProperty("HelloButton", "Name", new StringValue("Other"))));
        Assert.Equal("HelloButton", h.Object("HelloButton").Name);
    }
}
