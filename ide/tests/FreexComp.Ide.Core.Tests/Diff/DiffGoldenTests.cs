using System.Globalization;
using System.Text;
using FreexComp.Ide.Core.Diff;

namespace FreexComp.Ide.Core.Tests.Diff;

/// <summary>
/// I-329, IDE-DIFF-001…006: golden cases <c>Diff/Corpus/&lt;name&gt;.{left,right,hunks}</c> (copied to the output).
/// <para>
/// <c>.left</c> / <c>.right</c> are the two texts with their exact bytes (the folder is <c>-text</c> in git): a UTF-16 LE
/// byte-order mark gives <c>EncodingName</c> <c>UTF-16 LE</c>, anything else is UTF-8 (<c>UTF-8</c>).
/// </para>
/// <para>
/// <c>.hunks</c> format (LF; one item per line):
/// <code>
/// options: algorithm=Myers ignoreWhitespace=false ignoreCase=false context=3   (input: the DiffOptions; missing keys = defaults)
/// identical: true|false                                                     (DiffResult.Identical)
/// changes: N                                                                (DiffResult.ChangeCount)
/// note: &lt;text&gt;                                                             (one per DiffResult.Notes entry, in order)
/// @@ left S,C right S,C @@                                                  (a hunk: 0-based starts and counts)
/// K L R [spans] |text                                                       (a line of the hunk)
/// </code>
/// <c>K</c> is <c>=</c> Context, <c>-</c> Removed, <c>+</c> Added; <c>L</c> / <c>R</c> are the 0-based left / right line
/// numbers or <c>.</c> when absent; <c>spans</c> (only when <c>Changes</c> is not empty) is a comma-separated list of
/// <c>start+length</c> within the text; everything after <c>|</c> is <c>DiffLine.Text</c> verbatim.
/// </para>
/// </summary>
[Trait("Phase", "8")]
public sealed class DiffGoldenTests
{
    private static string CorpusDirectory => Path.Combine(AppContext.BaseDirectory, "Diff", "Corpus");

    public static TheoryData<string> Cases()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(CorpusDirectory, "*.hunks").Order(StringComparer.Ordinal))
            data.Add(Path.GetFileNameWithoutExtension(file));
        return data;
    }

    [Theory(Timeout = 30000)]
    [MemberData(nameof(Cases))]
    [Trait("Req", "IDE-DIFF-001")]
    [Trait("Req", "IDE-DIFF-002")]
    [Trait("Req", "IDE-DIFF-003")]
    [Trait("Req", "IDE-DIFF-004")]
    [Trait("Req", "IDE-DIFF-005")]
    [Trait("Req", "IDE-DIFF-006")]
    public void Case(string name)
    {
        var expected = File.ReadAllText(Path.Combine(CorpusDirectory, name + ".hunks")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var options = ParseOptions(expected.Split('\n')[0]);
        var left = ReadInput(Path.Combine(CorpusDirectory, name + ".left"));
        var right = ReadInput(Path.Combine(CorpusDirectory, name + ".right"));

        var result = DiffService.Compute(left, right, options, TestContext.Current.CancellationToken);

        Assert.Equal(expected, Render(options, result));
    }

    /// <summary>The corpus holds every case named in diff-viewer.md §3.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-DIFF-003")]
    public void CorpusHasRequiredCases()
    {
        string[] required =
        [
            "insert", "delete", "replace", "move", "whitespace", "whitespace-ignored", "case-only", "empty-left", "empty-right",
            "no-final-newline", "line-ending-only", "encoding-note", "reordered-myers", "reordered-patience", "intra-line",
            "intra-line-below-half",
        ];
        var cases = Cases().Select(row => row.Data).ToHashSet(StringComparer.Ordinal);

        Assert.All(required, name => Assert.Contains(name, cases));
        Assert.All(cases, name =>
        {
            Assert.True(File.Exists(Path.Combine(CorpusDirectory, name + ".left")), name + ".left");
            Assert.True(File.Exists(Path.Combine(CorpusDirectory, name + ".right")), name + ".right");
        });
    }

    private static DiffInput ReadInput(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes is [0xFF, 0xFE, ..])
            return new DiffInput(Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), "UTF-16 LE");
        var skip = bytes is [0xEF, 0xBB, 0xBF, ..] ? 3 : 0;
        return new DiffInput(new UTF8Encoding(false, true).GetString(bytes, skip, bytes.Length - skip), "UTF-8");
    }

    private static DiffOptions ParseOptions(string line)
    {
        Assert.StartsWith("options:", line, StringComparison.Ordinal);
        var options = new DiffOptions();
        foreach (var pair in line["options:".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var (key, value) = (pair[..pair.IndexOf('=', StringComparison.Ordinal)], pair[(pair.IndexOf('=', StringComparison.Ordinal) + 1)..]);
            options = key switch
            {
                "algorithm" => options with { Algorithm = Enum.Parse<DiffAlgorithm>(value) },
                "ignoreWhitespace" => options with { IgnoreWhitespace = bool.Parse(value) },
                "ignoreCase" => options with { IgnoreCase = bool.Parse(value) },
                "context" => options with { ContextLines = int.Parse(value, CultureInfo.InvariantCulture) },
                _ => throw new InvalidDataException("unknown option " + key),
            };
        }

        return options;
    }

    internal static string Render(DiffOptions options, DiffResult result)
    {
        var s = new StringBuilder();
        s.Append(CultureInfo.InvariantCulture, $"options: algorithm={options.Algorithm} ignoreWhitespace={Lower(options.IgnoreWhitespace)} ")
            .Append(CultureInfo.InvariantCulture, $"ignoreCase={Lower(options.IgnoreCase)} context={options.ContextLines}\n")
            .Append(CultureInfo.InvariantCulture, $"identical: {Lower(result.Identical)}\n")
            .Append(CultureInfo.InvariantCulture, $"changes: {result.ChangeCount}\n");
        foreach (var note in result.Notes)
            s.Append("note: ").Append(note).Append('\n');
        foreach (var hunk in result.Hunks)
        {
            s.Append(CultureInfo.InvariantCulture, $"@@ left {hunk.LeftStart},{hunk.LeftCount} right {hunk.RightStart},{hunk.RightCount} @@\n");
            foreach (var line in hunk.Lines)
            {
                var kind = line.Kind switch { DiffLineKind.Context => '=', DiffLineKind.Removed => '-', _ => '+' };
                s.Append(kind).Append(' ').Append(Number(line.LeftLine)).Append(' ').Append(Number(line.RightLine)).Append(' ');
                if (!line.Changes.IsEmpty)
                    s.Append(string.Join(",", line.Changes.Select(c => $"{c.Start}+{c.Length}"))).Append(' ');
                s.Append('|').Append(line.Text).Append('\n');
            }
        }

        return s.ToString();
    }

    private static string Lower(bool value) => value ? "true" : "false";

    private static string Number(int? line) => line is { } n ? n.ToString(CultureInfo.InvariantCulture) : ".";
}
