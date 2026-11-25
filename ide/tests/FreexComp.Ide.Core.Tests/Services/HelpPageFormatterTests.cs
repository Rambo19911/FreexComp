using FreexComp.Ide.Core.Services;

namespace FreexComp.Ide.Core.Tests.Services;

/// <summary>UI-MENU-032: the text of a help document derived from the page's Markdown (tables aligned, the rest kept).</summary>
[Trait("Phase", "8")]
[Trait("Req", "UI-MENU-032")]
public sealed class HelpPageFormatterTests
{
    private static string[] Lines(HelpPageText page) => page.Text.Split("\r\n");

    [Fact(Timeout = 30000)]
    public void TableIsAlignedWithoutItsDelimiterRow()
    {
        var page = HelpPageFormatter.Format("# Title\n\nSome text.\n\n| System | Editions | Status |\n|---|:-:|--:|\n| Windows 11 | x64 | Supported |\n|  Windows 10, version 1809 or later  | x64 | Supported |\n\nAfter.\n");

        Assert.Equal(
        [
            "# Title",
            "",
            "Some text.",
            "",
            "System                             Editions  Status",
            "Windows 11                         x64       Supported",
            "Windows 10, version 1809 or later  x64       Supported",
            "",
            "After.",
            "",
        ], Lines(page));
        Assert.Equal(
        [
            HelpLineKind.Heading, HelpLineKind.Text, HelpLineKind.Text, HelpLineKind.Text,
            HelpLineKind.TableHeader, HelpLineKind.TableRow, HelpLineKind.TableRow,
            HelpLineKind.Text, HelpLineKind.Text, HelpLineKind.Text,
        ], page.Lines);
    }

    [Fact(Timeout = 30000)]
    public void LinesEndInCrLfWhateverTheSourceUses()
    {
        var page = HelpPageFormatter.Format("a\r\n| H |\r\n| - |\r\n| x |\n# b\rc"); // I-266: "b" and "c" as plain lines would be one paragraph
        Assert.Equal("a\r\nH\r\nx\r\n# b\r\nc", page.Text);
        Assert.Equal(Lines(page).Length, page.Lines.Count);
    }

    [Fact(Timeout = 30000)]
    public void EscapedBarIsACellCharacter()
    {
        var page = HelpPageFormatter.Format("| Operator | Meaning |\n|---|---|\n| a \\| b | either |\n| `x \\| y` | in code |");
        Assert.Equal(["Operator  Meaning", "a | b     either", "`x | y`   in code"], Lines(page));
    }

    [Fact(Timeout = 30000)]
    public void InlineCodeMarksAreKeptAndCounted()
    {
        var page = HelpPageFormatter.Format("| Option | Meaning |\n|---|---|\n| `-Keep` | keep `obj` |\n| `-Home <directory>` | another directory |");
        Assert.Equal(
        [
            "Option               Meaning",
            "`-Keep`              keep `obj`",
            "`-Home <directory>`  another directory",
        ], Lines(page));
    }

    [Fact(Timeout = 30000)]
    public void RaggedRowsGetEmptyCells()
    {
        // A short row, a row with more cells than the header, a row without its closing bar, an empty cell in the middle.
        var page = HelpPageFormatter.Format("| A | B | C |\n|---|---|---|\n| 1 |\n| 1 | 2 | 3 | 4 |\n| 1 | 2 | 3\n| 1 || 3 |");
        Assert.Equal(
        [
            "A  B  C",
            "1",
            "1  2  3  4",
            "1  2  3",
            "1     3",
        ], Lines(page));
        Assert.Equal(HelpLineKind.TableHeader, page.Lines[0]);
        Assert.All(page.Lines.Skip(1), k => Assert.Equal(HelpLineKind.TableRow, k));
    }

    [Fact(Timeout = 30000)]
    public void TableAtTheEndOfTheFile()
    {
        Assert.Equal(["Text", "", "A   B", "10  20"], Lines(HelpPageFormatter.Format("Text\n\n| A | B |\n|---|---|\n| 10 | 20 |")));
        Assert.Equal(["A   B", "10  20", ""], Lines(HelpPageFormatter.Format("| A | B |\n|---|---|\n| 10 | 20 |\n")));

        // Only a header and its delimiter row.
        var header = HelpPageFormatter.Format("| A | B |\n| --- | --- |");
        Assert.Equal(["A  B"], Lines(header));
        Assert.Equal([HelpLineKind.TableHeader], header.Lines);
    }

    [Theory(Timeout = 30000)]
    [InlineData("| not a table")]
    [InlineData("| a | b |\n| c | d |")]                  // no delimiter row
    [InlineData("| a | b |\n\n|---|---|")]                // the delimiter row is not the second line
    [InlineData("|---|---|")]                             // a delimiter row alone
    [InlineData("| a | b |\n| === | === |")]
    [InlineData("| a | b |\n|   |   |")]                  // no '-'
    [InlineData("x := a or b; // |\n|---| is no row here")]
    public void BarLinesThatAreNoTableAreKept(string markdown)
    {
        var page = HelpPageFormatter.Format(markdown);
        Assert.Equal(markdown.Replace("\n", "\r\n", StringComparison.Ordinal), page.Text);
        Assert.All(page.Lines, k => Assert.Equal(HelpLineKind.Text, k));
    }

    /// <summary>Lines that do not start with a bar in column 1 are plain text: no table, one paragraph (I-266).</summary>
    [Theory(Timeout = 30000)]
    [InlineData("a | b\n--|--", "a | b --|--")]
    [InlineData(" | a |\n |---|", " | a | |---|")]
    public void BarLinesNotInColumnOneAreParagraphText(string markdown, string shown)
    {
        var page = HelpPageFormatter.Format(markdown);
        Assert.Equal(shown, page.Text);
        Assert.Equal([HelpLineKind.Text], page.Lines);
    }

    [Fact(Timeout = 30000)]
    public void TwoTablesAreAlignedOnTheirOwn()
    {
        var page = HelpPageFormatter.Format("| A | B |\n|---|---|\n| long cell | 2 |\ntext\n| C | D |\n|:--|--:|\n| 3 | 4 |");
        Assert.Equal(["A          B", "long cell  2", "text", "C  D", "3  4"], Lines(page));
        Assert.Equal(
            [HelpLineKind.TableHeader, HelpLineKind.TableRow, HelpLineKind.Text, HelpLineKind.TableHeader, HelpLineKind.TableRow],
            page.Lines);
    }

    [Theory(Timeout = 30000)]
    [InlineData("# Title", true)]
    [InlineData("###### Six", true)]
    [InlineData("####### Seven", false)]
    [InlineData("#NoSpace", false)]
    [InlineData(" # Indented", false)]
    [InlineData("", false)]
    [InlineData("Text # not", false)]
    public void HeadingLines(string line, bool heading)
    {
        Assert.Equal(heading, HelpPageFormatter.IsHeading(line));
        Assert.Equal(heading ? HelpLineKind.Heading : HelpLineKind.Text, Assert.Single(HelpPageFormatter.Format(line).Lines));
    }

    /// <summary>I-266: the plain lines of a paragraph are one line, joined with one space; blank lines, headings and bar lines end it.</summary>
    [Fact(Timeout = 30000)]
    public void ParagraphLinesAreJoined()
    {
        var page = HelpPageFormatter.Format(
            "# Title\nfirst line\nsecond line  \n  third line\n\nNext paragraph,\nits end.\n## Heading\nafter the heading\n| a bar line\nafter the bar line\n"
            + "| A |\n|---|\n| 1 |\nafter the table\nand on\n \nlast");

        Assert.Equal(
        [
            "# Title",
            "first line second line third line",
            "",
            "Next paragraph, its end.",
            "## Heading",
            "after the heading",
            "| a bar line",
            "after the bar line",
            "A",
            "1",
            "after the table and on",
            " ",
            "last",
        ], Lines(page));
        Assert.Equal(
        [
            HelpLineKind.Heading, HelpLineKind.Text, HelpLineKind.Text, HelpLineKind.Text, HelpLineKind.Heading, HelpLineKind.Text,
            HelpLineKind.Text, HelpLineKind.Text, HelpLineKind.TableHeader, HelpLineKind.TableRow, HelpLineKind.Text, HelpLineKind.Text,
            HelpLineKind.Text,
        ], page.Lines);
    }

    /// <summary>I-266: a list item starts a paragraph of its own; the lines after it join it, indented or not.</summary>
    [Fact(Timeout = 30000)]
    public void ListItemContinuationJoinsItsItem()
    {
        var page = HelpPageFormatter.Format(
            "Intro\n- first item\n  continues here\n  and here\n- second item\n* star item\nlazy continuation\n+ plus item\n"
            + "12. numbered item\n    kept: four spaces\n3) third\n   more of it\n  - nested item\n    kept as well\n\n-not an item\nplain");

        Assert.Equal(
        [
            "Intro",
            "- first item continues here and here",
            "- second item",
            "* star item lazy continuation",
            "+ plus item",
            "12. numbered item",
            "    kept: four spaces",
            "3) third more of it",
            "  - nested item",
            "    kept as well",
            "",
            "-not an item plain",
        ], Lines(page));
        Assert.Equal(
        [
            HelpLineKind.Text, HelpLineKind.ListItem, HelpLineKind.ListItem, HelpLineKind.ListItem, HelpLineKind.ListItem,
            HelpLineKind.ListItem, HelpLineKind.Text, HelpLineKind.ListItem, HelpLineKind.ListItem, HelpLineKind.Text,
            HelpLineKind.Text, HelpLineKind.Text,
        ], page.Lines);
    }

    /// <summary>I-266: where the text of a list item starts (a wrapped item continues there); 0 for any other line.</summary>
    [Theory(Timeout = 30000)]
    [InlineData("- item", 2)]
    [InlineData("*   item", 4)]
    [InlineData("+ item", 2)]
    [InlineData("1. item", 3)]
    [InlineData("12) item", 4)]
    [InlineData("   - item", 5)]
    [InlineData("    - code", 0)]
    [InlineData("-item", 0)]
    [InlineData("- ", 0)]
    [InlineData("1.item", 0)]
    [InlineData("1234567890. item", 0)]
    [InlineData("**strong** text", 0)]
    [InlineData("text - not", 0)]
    [InlineData("", 0)]
    public void ListItemTextStarts(string line, int column) => Assert.Equal(column, HelpPageFormatter.ListItemTextStart(line));

    /// <summary>I-266: a line indented by four or more spaces (or a tab) is kept as it is and joins nothing.</summary>
    [Fact(Timeout = 30000)]
    public void IndentedCodeLinesAreKept()
    {
        var page = HelpPageFormatter.Format("Run it:\n    opc main.pas  \n    pbuild  app.pproj\n\tTabbed := True;\n   three spaces join\nthe text\n     five\nend");

        Assert.Equal(
        [
            "Run it:",
            "    opc main.pas  ",
            "    pbuild  app.pproj",
            "\tTabbed := True;",
            "   three spaces join the text",
            "     five",
            "end",
        ], Lines(page));
        Assert.All(page.Lines, k => Assert.Equal(HelpLineKind.Text, k));
    }

    /// <summary>Every page of the repository: no delimiter row is left, the other text is the page's, in order.</summary>
    [Fact(Timeout = 30000)]
    public void RepositoryPagesKeepEveryLineButDelimiterRows()
    {
        var root = Path.Combine(Infrastructure.SpecTables.RepoRootPath(), "help");
        var tables = 0;
        foreach (var file in Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file).ReplaceLineEndings("\n").Split('\n');
            var page = HelpPageFormatter.Format(File.ReadAllText(file));
            var shown = Lines(page);
            Assert.Equal(shown.Length, page.Lines.Count);
            Assert.DoesNotContain(shown, l => l.StartsWith('|'));
            tables += page.Lines.Count(k => k == HelpLineKind.TableHeader);
            Assert.Equal(source.Count(l => l.StartsWith("|-", StringComparison.Ordinal)), page.Lines.Count(k => k == HelpLineKind.TableHeader));

            // I-266: the lines of a paragraph are joined, so the words outside tables are the page's, in order.
            static IEnumerable<string> Words(IEnumerable<string> lines) => lines.SelectMany(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            Assert.Equal(
                Words(source.Where(l => !l.StartsWith('|'))),
                Words(shown.Where((_, i) => page.Lines[i] is HelpLineKind.Text or HelpLineKind.Heading or HelpLineKind.ListItem)));
            Assert.Equal(source.Count(HelpPageFormatter.IsHeading), page.Lines.Count(k => k == HelpLineKind.Heading));
            Assert.Equal(source.Count(l => l.Trim().Length == 0), shown.Count(l => l.Length == 0));
        }

        Assert.True(tables >= 3, "the repository's help pages have tables");
    }
}
