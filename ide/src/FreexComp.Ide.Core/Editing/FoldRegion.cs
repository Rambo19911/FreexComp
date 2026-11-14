using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Editing;

/// <summary>
/// What a fold region folds (editor-features.md IDE-ED-051/053); the golden <c>.folds</c> files write the lower-case
/// name. Pascal: routine (a), type (b), section (c), uses (d), block (e), comment / linecomments (f), region (g),
/// conditional (h). Form: object, item. JSON: object, array.
/// </summary>
public enum FoldKind
{
    Routine,
    Type,
    Section,
    Uses,
    Block,
    Comment,
    LineComments,
    Region,
    Conditional,
    Object,
    Item,
    Array,
}

/// <summary>
/// A foldable range (IDE-ED-050): from the end of its start line's last non-whitespace character to the end of its end
/// line; start line &lt; end line. Folding hides the lines after the start line and shows <see cref="Title"/> (the
/// <c>…</c> box text) after the start line's text.
/// </summary>
public sealed class FoldRegion
{
    /// <summary>The <c>…</c> box text of a region without its own title (UI-ED-034).</summary>
    public const string DefaultTitle = "…";

    private FoldRegion(int startOffset, int endOffset, int startLine, int endLine, FoldKind kind, string title, string startLineText)
    {
        StartOffset = startOffset;
        EndOffset = endOffset;
        StartLine = startLine;
        EndLine = endLine;
        Kind = kind;
        Title = title;
        StartLineText = startLineText;
    }

    public int StartOffset { get; }
    public int EndOffset { get; }
    public int StartLine { get; }
    public int EndLine { get; }
    public FoldKind Kind { get; }

    /// <summary><c>…</c>, or the name of a <c>{$REGION 'name'}</c>.</summary>
    public string Title { get; }

    /// <summary>The start line's text when the region was computed (IDE-ED-056).</summary>
    public string StartLineText { get; }

    public bool IsFolded { get; internal set; }

    /// <summary>Follows the start line's beginning while the region is folded (IDE-ED-056).</summary>
    internal ITextAnchor? Anchor { get; set; }

    /// <summary>The region from <paramref name="startLine"/> to <paramref name="endLine"/>; null unless start &lt; end.</summary>
    public static FoldRegion? Create(ITextBuffer buffer, int startLine, int endLine, FoldKind kind, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (startLine < 1 || endLine > buffer.LineCount || startLine >= endLine)
            return null;
        var (startLineOffset, startLength, _) = buffer.GetLine(startLine);
        var text = buffer.GetText(startLineOffset, startLength);
        var (endLineOffset, endLength, _) = buffer.GetLine(endLine);
        return new FoldRegion(startLineOffset + text.TrimEnd().Length, endLineOffset + endLength, startLine, endLine, kind,
            string.IsNullOrEmpty(title) ? DefaultTitle : title, text);
    }

    /// <summary>"start-end kind", the golden-file line (testing.md §4).</summary>
    public override string ToString() => $"{StartLine}-{EndLine} {Kind.ToString().ToLowerInvariant()}";
}

/// <summary>Computes the fold regions of a buffer (architecture.md §11, <c>ILanguageDefinition.CreateFoldingProvider</c>).</summary>
public interface IFoldingProvider
{
    /// <summary>All regions, sorted by start offset, outer before inner at the same start. Must never throw.</summary>
    IReadOnlyList<FoldRegion> GetRegions(ITextBuffer buffer);
}

/// <summary>Sorting and collection helper shared by the providers.</summary>
public static class FoldRegions
{
    /// <summary>Adds the region when it exists (start line &lt; end line).</summary>
    public static void Add(this List<FoldRegion> regions, ITextBuffer buffer, int startLine, int endLine, FoldKind kind, string? title = null)
    {
        if (FoldRegion.Create(buffer, startLine, endLine, kind, title) is { } region)
            regions.Add(region);
    }

    /// <summary>Sorted by start offset, then the longer first; exact duplicates (same lines and kind) removed.</summary>
    public static List<FoldRegion> Sorted(IEnumerable<FoldRegion> regions) =>
        [.. regions.DistinctBy(r => (r.StartLine, r.EndLine, r.Kind)).OrderBy(r => r.StartOffset).ThenByDescending(r => r.EndOffset).ThenBy(r => r.Kind)];
}
