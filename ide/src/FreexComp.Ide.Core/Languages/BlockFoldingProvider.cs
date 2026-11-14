using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Languages;

/// <summary>
/// IDE-ED-053: fold regions of the <c>.form</c> and JSON languages from their classifiers' spans. Each opener span
/// pushes, a closer pops the innermost opener it closes; a block spanning at least two lines is a region.
/// Unbalanced closers are ignored; unclosed openers produce no region.
/// </summary>
internal sealed class BlockFoldingProvider(Func<ITokenClassifier> createClassifier, Func<string, string, FoldKind?> opener, Func<string, string, FoldKind?, bool> closes)
    : IFoldingProvider
{
    public IReadOnlyList<FoldRegion> GetRegions(ITextBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var regions = new List<FoldRegion>();
        var stack = new List<(int Line, FoldKind Kind)>();
        foreach (var span in createClassifier().Classify(buffer, new LineRange(1, buffer.LineCount)))
        {
            var text = buffer.GetText(span.Offset, span.Length);
            int line = buffer.GetPosition(span.Offset).Line;
            if (opener(text, span.TokenClass) is { } kind)
                stack.Add((line, kind));
            else if (stack.Count > 0 && closes(text, span.TokenClass, stack[^1].Kind))
            {
                var (start, openKind) = stack[^1];
                stack.RemoveAt(stack.Count - 1);
                regions.Add(buffer, start, line, openKind);
            }
        }

        return FoldRegions.Sorted(regions);
    }

    /// <summary><c>object</c> / <c>inherited</c> / <c>inline</c> … <c>end</c> and <c>item</c> … <c>end</c> (keywords only start lines).</summary>
    public static IFoldingProvider ForForm(Func<ITokenClassifier> createClassifier) => new BlockFoldingProvider(createClassifier,
        (text, token) => token != ColorTokens.Keyword ? null
            : text.Equals("item", StringComparison.OrdinalIgnoreCase) ? FoldKind.Item
            : text.Equals("end", StringComparison.OrdinalIgnoreCase) ? null
            : FoldKind.Object,
        (text, token, _) => token == ColorTokens.Keyword && text.Equals("end", StringComparison.OrdinalIgnoreCase));

    /// <summary>Objects <c>{…}</c> and arrays <c>[…]</c>.</summary>
    public static IFoldingProvider ForJson(Func<ITokenClassifier> createClassifier) => new BlockFoldingProvider(createClassifier,
        (text, token) => token != ColorTokens.Symbol ? null : text == "{" ? FoldKind.Object : text == "[" ? FoldKind.Array : null,
        (text, token, open) => token == ColorTokens.Symbol && ((text == "}" && open == FoldKind.Object) || (text == "]" && open == FoldKind.Array)));
}
