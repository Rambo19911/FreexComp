using System.Text;
using FreexComp.Designer.Text;

namespace FreexComp.Designer.Tests.Text;

public sealed partial class FormParserTests
{
    /// <summary>A form whose objects nest <paramref name="depth"/> levels (the root is level 1), unindented.</summary>
    private static string NestedObjects(int depth)
    {
        var text = new StringBuilder("object MainForm: TForm\n");
        for (var i = 2; i <= depth; i++)
        {
            text.Append("object Panel").Append(i).Append(": TPanel\n");
        }

        for (var i = 1; i <= depth; i++)
        {
            text.Append("end\n");
        }

        return text.ToString();
    }

    /// <summary>A form with one collection property whose items nest collections <paramref name="depth"/> levels.</summary>
    private static string NestedCollections(int depth)
    {
        var text = new StringBuilder("object MainForm: TForm\n  Items = ");
        for (var i = 1; i < depth; i++)
        {
            text.Append("<\nitem\nItems = ");
        }

        text.Append("<\n>");
        for (var i = 1; i < depth; i++)
        {
            text.Append("\nend\n>");
        }

        return text.Append("\nend\n").ToString();
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-025")]
    public void DeepNestingIsADiagnostic()
    {
        // Objects: MaxNesting levels parse; one more is one F0001 and no document.
        var ok = FormParser.Parse(NestedObjects(FormParser.MaxNesting), "deep.form", Catalog);
        Assert.False(ok.HasSyntaxErrors, string.Join("\n", ok.Diagnostics));
        var tooDeep = FormParser.Parse(NestedObjects(FormParser.MaxNesting + 1), "deep.form", Catalog);
        Assert.Null(tooDeep.Document);
        var error = Assert.Single(tooDeep.Diagnostics);
        Assert.Equal("F0001", error.Code);
        Assert.Contains("nest", error.Message, StringComparison.Ordinal);
        Assert.Equal(FormParser.MaxNesting + 1, error.Line);

        // Collections nest under the same limit.
        Assert.False(FormParser.Parse(NestedCollections(FormParser.MaxNesting - 1), "deep.form", Catalog).HasSyntaxErrors);
        Assert.Equal("F0001", Assert.Single(FormParser.Parse(NestedCollections(FormParser.MaxNesting + 1), "deep.form", Catalog).Diagnostics).Code);

        // Far deeper than the stack allows: still one diagnostic, no stack overflow.
        Assert.Equal("F0001", Assert.Single(FormParser.Parse(NestedObjects(200_000), "deep.form", Catalog).Diagnostics).Code);
        Assert.Equal("F0001", Assert.Single(FormParser.Parse(NestedCollections(200_000), "deep.form", Catalog).Diagnostics).Code);
    }
}
