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
        var page = HelpPageFormatter.Format("a\r\n| H |\r\n| - |\r\n| x |\nb\rc");
        Assert.Equal("a\r\nH\r\nx\r\nb\r\nc", page.Text);
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
    [InlineData("a | b\n--|--")]                          // the lines do not start with '|'
    [InlineData(" | a |\n |---|")]
    [InlineData("x := a or b; // |\n|---| is no row here")]
    public void BarLinesThatAreNoTableAreKept(string markdown)
    {
        var page = HelpPageFormatter.Format(markdown);
        Assert.Equal(markdown.Replace("\n", "\r\n", StringComparison.Ordinal), page.Text);
        Assert.All(page.Lines, k => Assert.Equal(HelpLineKind.Text, k));
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

    /// <summary>Every page of the repository: no delimiter row is left, the other lines are the page's, in order.</summary>
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
            Assert.Equal(source.Where(l => !l.StartsWith('|')), shown.Where((_, i) => page.Lines[i] is HelpLineKind.Text or HelpLineKind.Heading));
        }

        Assert.True(tables >= 3, "the repository's help pages have tables");
    }
}
