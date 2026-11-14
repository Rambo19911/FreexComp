using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Tests.Editing;

/// <summary>Indentation guides (IDE-ED-073, UI-ED-056).</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Unit")]
public sealed class IndentGuideTests
{
    private const string Code =
        "begin\r\n" +            // 1
        "  if X then\r\n" +      // 2
        "  begin\r\n" +          // 3
        "    A;\r\n" +           // 4
        "\r\n" +                 // 5: below 2, above 4
        "  end;\r\n" +           // 6
        "   \r\n" +              // 7: whitespace only, below 0
        "end;\r\n" +             // 8
        "\tB;\r\n" +             // 9: a tab = tabWidth columns
        "   C;";                 // 10: 3 columns

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-073")]
    [Trait("Req", "UI-ED-056")]
    public void GuidesFollowIndentationAndBlankLines()
    {
        var buffer = new StringTextBuffer(Code);
        var guides = IndentGuides.Compute(buffer, 1, buffer.LineCount, tabWidth: 2);
        string Show(int line) => string.Join(",", guides[line - 1]);
        Assert.Equal(["", "0", "0", "0,2", "0", "0", "", "", "0", "0,2"], Enumerable.Range(1, 10).Select(Show));

        // A range in the middle still looks at the neighbours outside it.
        Assert.Equal("0", string.Join(",", IndentGuides.Compute(buffer, 5, 5, tabWidth: 2)[0]));
        Assert.Equal("0", string.Join(",", IndentGuides.Compute(buffer, 9, 9, tabWidth: 4)[0]));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-073")]
    public void ActiveGuideIsTheInnermostBlockAtTheCaret()
    {
        var buffer = new StringTextBuffer(Code);
        var tokens = new PascalLanguage().ScanCodeTokens(Code);
        int At(int line, int column) => buffer.GetOffset(line, column);

        Assert.Equal(new ActiveGuide(2, 4, 5), IndentGuides.Active(buffer, tokens, At(4, 5), tabWidth: 2));
        Assert.Equal(new ActiveGuide(0, 2, 7), IndentGuides.Active(buffer, tokens, At(2, 3), tabWidth: 2));
        Assert.Null(IndentGuides.Active(buffer, tokens, At(10, 4), tabWidth: 2)); // outside every block
    }
}
