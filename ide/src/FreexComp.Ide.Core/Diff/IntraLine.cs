using System.Collections.Immutable;
using FreexComp.Compiler.Text;

namespace FreexComp.Ide.Core.Diff;

/// <summary>
/// IDE-DIFF-005: changed spans within a removed / added line pair. Tokens are identifier/number runs (letters, digits,
/// <c>_</c>), whitespace runs and any other single character (a surrogate pair is one token); the token sequences are
/// diffed with Myers (ordinal token text) and runs of changed tokens become one span each. When the unchanged characters
/// are less than half of the longer line, neither side gets spans.
/// </summary>
internal static class IntraLine
{
    public static (ImmutableArray<TextSpan> Left, ImmutableArray<TextSpan> Right) Spans(string left, string right, DiffSteps steps)
    {
        var leftTokens = Tokens(left);
        var rightTokens = Tokens(right);
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        var a = Keys(left, leftTokens, ids);
        var b = Keys(right, rightTokens, ids);
        var removed = new bool[a.Length];
        var added = new bool[b.Length];
        SequenceDiff.Myers(a, 0, a.Length, b, 0, b.Length, removed, added, steps);

        var unchanged = 0;
        for (var i = 0; i < leftTokens.Count; i++)
        {
            if (!removed[i])
                unchanged += leftTokens[i].Length;
        }

        if (unchanged * 2 < Math.Max(left.Length, right.Length))
            return ([], []);
        return (Merge(leftTokens, removed), Merge(rightTokens, added));
    }

    private static List<TextSpan> Tokens(string text)
    {
        var tokens = new List<TextSpan>();
        for (var i = 0; i < text.Length;)
        {
            var start = i;
            var c = text[i];
            if (IsWord(c))
            {
                while (i < text.Length && IsWord(text[i]))
                    i++;
            }
            else if (char.IsWhiteSpace(c))
            {
                while (i < text.Length && char.IsWhiteSpace(text[i]))
                    i++;
            }
            else
                i += char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? 2 : 1;

            tokens.Add(TextSpan.FromBounds(start, i));
        }

        return tokens;
    }

    private static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static int[] Keys(string text, List<TextSpan> tokens, Dictionary<string, int> ids)
    {
        var keys = new int[tokens.Count];
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = text.Substring(tokens[i].Start, tokens[i].Length);
            if (!ids.TryGetValue(token, out var id))
                ids.Add(token, id = ids.Count);
            keys[i] = id;
        }

        return keys;
    }

    private static ImmutableArray<TextSpan> Merge(List<TextSpan> tokens, bool[] changed)
    {
        var spans = ImmutableArray.CreateBuilder<TextSpan>();
        for (var i = 0; i < tokens.Count;)
        {
            if (!changed[i])
            {
                i++;
                continue;
            }

            var start = tokens[i].Start;
            while (i < tokens.Count && changed[i])
                i++;
            spans.Add(TextSpan.FromBounds(start, tokens[i - 1].End));
        }

        return spans.ToImmutable();
    }
}
