using System.Globalization;
using System.Text;
using FreexComp.Ide.Core.Diff;

namespace FreexComp.Ide.Core.Tests.Diff;

/// <summary>I-329: properties of <see cref="DiffService"/> (diff-viewer.md §1, §3).</summary>
[Trait("Phase", "8")]
public sealed class DiffPropertyTests
{
    private static readonly string[] Alphabet = ["a", "b", "c", "d", "e", ""];

    /// <summary>
    /// IDE-DIFF-007 (with the IDE-DIFF-003/004/005 invariants): 1 000 seeded pairs of random line sequences over a small
    /// alphabet (many equal lines), each diffed with Myers and Patience: <c>Apply</c> reproduces the right text exactly;
    /// hunk counts, line numbers, block order (Removed before Added), context bounds, hunk merging, <c>ChangeCount</c> /
    /// <c>Identical</c> and span bounds hold; Myers is minimal (changed lines = N + M − 2·LCS).
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-DIFF-003")]
    [Trait("Req", "IDE-DIFF-004")]
    [Trait("Req", "IDE-DIFF-005")]
    [Trait("Req", "IDE-DIFF-007")]
    public void ApplyReproducesRight1000RandomPairs()
    {
        var random = new Random(329);
        for (var pair = 0; pair < 1000; pair++)
        {
            var leftLines = RandomLines(random, random.Next(0, 26));
            var rightLines = random.Next(4) == 0 ? RandomLines(random, random.Next(0, 26)) : Edit(random, leftLines);
            var left = Join(leftLines, random.Next(2) == 0);
            var right = Join(rightLines, random.Next(2) == 0);
            var context = random.Next(0, 5);
            foreach (var algorithm in new[] { DiffAlgorithm.Myers, DiffAlgorithm.Patience })
            {
                var options = new DiffOptions(algorithm, ContextLines: context);
                var result = DiffService.Compute(new DiffInput(left), new DiffInput(right), options, TestContext.Current.CancellationToken);
                var label = $"pair {pair} {algorithm} context {context}: {Escape(left)} -> {Escape(right)}";

                Assert.True(DiffService.Apply(left, result) == right, label);
                CheckInvariants(result, leftLines, rightLines, context, label);
                if (algorithm == DiffAlgorithm.Myers)
                {
                    var changed = result.Hunks.Sum(h => h.Lines.Count(l => l.Kind != DiffLineKind.Context));
                    Assert.True(leftLines.Count + rightLines.Count - 2 * Lcs(leftLines, rightLines) == changed, label + " (not minimal)");
                }
            }
        }
    }

    /// <summary>IDE-DIFF-008: headers, 1-based starts, count omitted when 1, empty ranges, prefixes, the no-newline marker, LF.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-DIFF-008")]
    public void UnifiedPatchFormat()
    {
        Assert.Equal(
            "--- left.pas\n+++ right.pas\n@@ -1,4 +1,5 @@\n a\n-b\n+B\n c\n d\n+e\n\\ No newline at end of file\n",
            Patch("a\r\nb\r\nc\r\nd\r\n", "a\nB\nc\nd\ne", 1));
        Assert.Equal("--- left.pas\n+++ right.pas\n@@ -1,0 +2 @@\n+X\n", Patch("a\nb\nc\n", "a\nX\nb\nc\n", 0));
        Assert.Equal("--- left.pas\n+++ right.pas\n@@ -2 +1,0 @@\n-b\n", Patch("a\nb\n", "a\n", 0));
        Assert.Equal("--- left.pas\n+++ right.pas\n@@ -0,0 +1,2 @@\n+a\n+b\n", Patch("", "a\nb\n", 3));
        // The left's last line gains a line break: shown as removed (with the marker) and added again.
        Assert.Equal("--- left.pas\n+++ right.pas\n@@ -1,2 +1,3 @@\n a\n-b\n\\ No newline at end of file\n+b\n+c\n",
            Patch("a\nb", "a\nb\nc\n", 3));
        // Both last lines lack the break: one context line with the marker.
        Assert.Equal("--- left.pas\n+++ right.pas\n@@ -1,2 +1,2 @@\n-a\n+A\n b\n\\ No newline at end of file\n", Patch("a\nb", "A\nb", 3));
        Assert.Equal("--- left.pas\n+++ right.pas\n", Patch("same\n", "same\n", 3));
    }

    /// <summary>IDE-DIFF-009: a cancelled token stops <c>Compute</c> — before it starts and while it runs (a diff that would take minutes).</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-DIFF-009")]
    public async Task CancellationStopsCompute()
    {
        var left = new DiffInput(string.Join("\n", Enumerable.Range(0, 200_000).Select(i => "L" + i.ToString(CultureInfo.InvariantCulture))));
        var right = new DiffInput(string.Join("\n", Enumerable.Range(0, 200_000).Select(i => "R" + i.ToString(CultureInfo.InvariantCulture))));
        foreach (var algorithm in new[] { DiffAlgorithm.Myers, DiffAlgorithm.Patience })
        {
            Assert.ThrowsAny<OperationCanceledException>(() => DiffService.Compute(left, right, new DiffOptions(algorithm), new CancellationToken(true)));

            using var cts = new CancellationTokenSource();
            var running = Task.Run(() => DiffService.Compute(left, right, new DiffOptions(algorithm), cts.Token), TestContext.Current.CancellationToken);
            await cts.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        }
    }

    private static string Patch(string left, string right, int context)
    {
        var result = DiffService.Compute(new DiffInput(left), new DiffInput(right), new DiffOptions(ContextLines: context), TestContext.Current.CancellationToken);
        return DiffService.ToUnifiedPatch(result, "left.pas", "right.pas");
    }

    private static void CheckInvariants(DiffResult result, List<string> left, List<string> right, int context, string label)
    {
        var blocks = 0;
        int? previousLeftEnd = null, previousTrailing = null;
        foreach (var hunk in result.Hunks)
        {
            var lines = hunk.Lines;
            Assert.True(lines.Count(l => l.Kind != DiffLineKind.Added) == hunk.LeftCount, label + " LeftCount");
            Assert.True(lines.Count(l => l.Kind != DiffLineKind.Removed) == hunk.RightCount, label + " RightCount");
            Assert.True(lines.Where(l => l.LeftLine is not null).Select(l => l.LeftLine!.Value).SequenceEqual(Enumerable.Range(hunk.LeftStart, hunk.LeftCount)), label + " left numbers");
            Assert.True(lines.Where(l => l.RightLine is not null).Select(l => l.RightLine!.Value).SequenceEqual(Enumerable.Range(hunk.RightStart, hunk.RightCount)), label + " right numbers");

            var runs = Runs(lines);
            Assert.True(runs.Count > 0 && runs.Any(r => !r.Context), label + " empty hunk");
            for (var i = 0; i < runs.Count; i++)
            {
                var (isContext, start, length) = runs[i];
                if (!isContext)
                {
                    blocks++;
                    var kinds = lines.Skip(start).Take(length).Select(l => l.Kind).ToList();
                    Assert.True(kinds.SkipWhile(k => k == DiffLineKind.Removed).All(k => k == DiffLineKind.Added), label + " Removed before Added");
                }
                else if (i == 0 || i == runs.Count - 1)
                    Assert.True(length <= context, label + " outer context");
                else
                    Assert.True(length <= 2 * context, label + " inner context");
            }

            var leading = runs[0].Context ? runs[0].Length : 0;
            if (previousLeftEnd is { } end && previousTrailing is { } trailing)
                Assert.True(trailing + (hunk.LeftStart - end) + leading > 2 * context, label + " hunks not merged");
            previousLeftEnd = hunk.LeftStart + hunk.LeftCount;
            previousTrailing = runs[^1].Context ? runs[^1].Length : 0;

            foreach (var line in lines)
            {
                if (line.Kind == DiffLineKind.Context)
                    Assert.True(left[line.LeftLine!.Value] == right[line.RightLine!.Value] && line.Text == left[line.LeftLine.Value], label + " context text");
                else
                    Assert.True(line.Text == (line.Kind == DiffLineKind.Removed ? left[line.LeftLine!.Value] : right[line.RightLine!.Value]), label + " text");
                Assert.All(line.Changes, s => Assert.True(s.Length > 0 && s.End <= line.Text.Length, label + " span"));
            }
        }

        Assert.True(result.ChangeCount == blocks, label + " ChangeCount");
        Assert.True(result.Identical == (blocks == 0), label + " Identical");
    }

    private static List<(bool Context, int Start, int Length)> Runs(IReadOnlyList<DiffLine> lines)
    {
        var runs = new List<(bool, int, int)>();
        for (var i = 0; i < lines.Count;)
        {
            var context = lines[i].Kind == DiffLineKind.Context;
            var start = i;
            while (i < lines.Count && (lines[i].Kind == DiffLineKind.Context) == context)
                i++;
            runs.Add((context, start, i - start));
        }

        return runs;
    }

    private static List<string> RandomLines(Random random, int count) =>
        Enumerable.Range(0, count).Select(_ => Alphabet[random.Next(Alphabet.Length)]).ToList();

    private static List<string> Edit(Random random, List<string> lines)
    {
        var result = lines.ToList();
        for (var edits = random.Next(0, 6); edits > 0; edits--)
        {
            var at = random.Next(result.Count + 1);
            switch (random.Next(3))
            {
                case 0:
                    result.Insert(at, Alphabet[random.Next(Alphabet.Length)]);
                    break;
                case 1 when at < result.Count:
                    result.RemoveAt(at);
                    break;
                default:
                    if (at < result.Count)
                        result[at] = Alphabet[random.Next(Alphabet.Length)];
                    break;
            }
        }

        return result;
    }

    private static string Join(List<string> lines, bool finalBreak)
    {
        if (lines.Count == 0)
            return "";
        var s = new StringBuilder(string.Join("\n", lines));
        if (finalBreak || lines[^1].Length == 0) // IDE-DIFF-001: an empty last line exists only before a line break
            s.Append('\n');
        return s.ToString();
    }

    private static int Lcs(List<string> a, List<string> b)
    {
        var table = new int[a.Count + 1, b.Count + 1];
        for (var i = 1; i <= a.Count; i++)
        {
            for (var j = 1; j <= b.Count; j++)
                table[i, j] = a[i - 1] == b[j - 1] ? table[i - 1, j - 1] + 1 : Math.Max(table[i - 1, j], table[i, j - 1]);
        }

        return table[a.Count, b.Count];
    }

    private static string Escape(string text) => "\"" + text.Replace("\n", "\\n", StringComparison.Ordinal) + "\"";
}
