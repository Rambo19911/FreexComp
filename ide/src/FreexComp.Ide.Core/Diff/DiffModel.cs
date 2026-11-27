using System.Collections.Immutable;
using FreexComp.Compiler.Text;

namespace FreexComp.Ide.Core.Diff;

/// <summary>IDE-DIFF-003: the line-matching algorithm.</summary>
public enum DiffAlgorithm
{
    Myers,
    Patience,
}

/// <summary>IDE-DIFF-002/003/004: how lines are compared and how much context a hunk carries.</summary>
public sealed record DiffOptions(DiffAlgorithm Algorithm = DiffAlgorithm.Myers, bool IgnoreWhitespace = false,
                                 bool IgnoreCase = false, int ContextLines = 3);

/// <summary>One side of a diff: the decoded text and, optionally, the name of its encoding (IDE-DIFF-006).</summary>
public sealed record DiffInput(string Text, string? EncodingName = null);

public enum DiffLineKind
{
    Context,
    Removed,
    Added,
}

/// <summary>
/// A line of a hunk. <see cref="LeftLine"/> / <see cref="RightLine"/> are 0-based (absent for an added / removed line);
/// <see cref="Text"/> is the original text without its line break — for a context line the left text (IDE-DIFF-002: with
/// <c>IgnoreWhitespace</c> / <c>IgnoreCase</c> the right line may differ); <see cref="Changes"/> are the intra-line spans
/// within <see cref="Text"/> (IDE-DIFF-005).
/// </summary>
public sealed record DiffLine(DiffLineKind Kind, int? LeftLine, int? RightLine, string Text,
                              ImmutableArray<TextSpan> Changes);

/// <summary>IDE-DIFF-004: change blocks with their context; 0-based starts (of an empty range: the index the lines would have).</summary>
public sealed record DiffHunk(int LeftStart, int LeftCount, int RightStart, int RightCount,
                              ImmutableArray<DiffLine> Lines);

public sealed record DiffResult(ImmutableArray<DiffHunk> Hunks, ImmutableArray<string> Notes, bool Identical,
                                int ChangeCount)
{
    /// <summary>What <see cref="DiffService.Apply"/> and <see cref="DiffService.ToUnifiedPatch"/> need beyond the hunks; set by <see cref="DiffService.Compute"/>.</summary>
    internal DiffEnds? Ends { get; init; }
}

/// <summary>Line counts, whether each last line ends with a line break, and the right text's most frequent line break.</summary>
internal sealed record DiffEnds(int LeftLines, int RightLines, bool LeftFinalBreak, bool RightFinalBreak, string? RightBreak);
