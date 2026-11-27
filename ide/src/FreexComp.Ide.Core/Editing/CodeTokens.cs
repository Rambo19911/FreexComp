using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Editing;

/// <summary>What a <see cref="CodeToken"/> is to bracket matching and word highlight (editor-features.md §7).</summary>
public enum CodeTokenKind
{
    /// <summary>An opening bracket or a block keyword (<c>begin</c>, <c>repeat</c>, …).</summary>
    Open,

    /// <summary>A closing bracket or a block-closing keyword (<c>end</c>, <c>until</c>).</summary>
    Close,

    /// <summary>An identifier (IDE-ED-074).</summary>
    Identifier,

    /// <summary>A compiler directive of a conditional group or region pair (IDE-ED-075; in <see cref="CodeTokens.Directives"/> only).</summary>
    Directive,
}

/// <summary>
/// One lexer token that pairing or word highlight looks at; <see cref="LexIndex"/> is its index in the language's whole
/// token stream (the 20 000-token search limit of IDE-ED-070 counts all tokens).
/// </summary>
public readonly record struct CodeToken(int Offset, int Length, CodeTokenKind Kind, int LexIndex, bool IsKeyword = false)
{
    public int End => Offset + Length;
}

/// <summary>
/// The bracket, keyword or directive at the caret and its partner (null: none, IDE-ED-070). For a directive group
/// (IDE-ED-075) <see cref="Group"/> holds every member in document order and <see cref="Partner"/> is the next one
/// (IDE-ED-076).
/// </summary>
public readonly record struct BracketMatch(CodeToken Bracket, CodeToken? Partner, IReadOnlyList<CodeToken>? Group = null)
{
    /// <summary>The tokens to highlight, in document order.</summary>
    public IReadOnlyList<CodeToken> Highlighted =>
        Group ?? (Partner is not { } p ? [Bracket] : p.Offset < Bracket.Offset ? [p, Bracket] : [Bracket, p]);
}

/// <summary>A guide drawn with <c>editor.indentGuide.active</c>: <see cref="Column"/> (0-based visual) over lines <see cref="FirstLine"/>…<see cref="LastLine"/>.</summary>
public readonly record struct ActiveGuide(int Column, int FirstLine, int LastLine);

/// <summary>
/// The pairing-relevant tokens of one text snapshot, sorted by offset, with their partners (IDE-ED-070…072). Built by
/// <see cref="Languages.ILanguageDefinition.ScanCodeTokens"/> from the language's lexer, so brackets and keywords inside
/// strings and comments never appear. Immutable.
/// </summary>
public sealed class CodeTokens
{
    /// <summary>IDE-ED-070: a partner further than this many tokens away is not searched.</summary>
    public const int SearchLimit = 20_000;

    private readonly int[] _partners;
    private readonly int[] _nextInGroup;

    /// <param name="partners">Per token the index of its partner in <paramref name="tokens"/>, or -1.</param>
    /// <param name="directives">IDE-ED-075: the directives of groups, sorted by offset (kind <see cref="CodeTokenKind.Directive"/>).</param>
    /// <param name="nextInGroup">Per directive the index of the next member of its group in <paramref name="directives"/>
    /// (the last member points back to the first), or -1 for a directive without partner.</param>
    public CodeTokens(string text, IReadOnlyList<CodeToken> tokens, IReadOnlyList<int> partners, bool ignoreCase, long version = -1,
        IReadOnlyList<CodeToken>? directives = null, IReadOnlyList<int>? nextInGroup = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(partners);
        if (tokens.Count != partners.Count)
            throw new ArgumentException("One partner per token is required.", nameof(partners));
        directives ??= [];
        nextInGroup ??= [];
        if (directives.Count != nextInGroup.Count)
            throw new ArgumentException("One group link per directive is required.", nameof(nextInGroup));
        Text = text;
        Tokens = tokens;
        IgnoreCase = ignoreCase;
        Version = version;
        Directives = directives;
        _partners = [.. partners];
        _nextInGroup = [.. nextInGroup];
    }

    public static CodeTokens Empty { get; } = new("", [], [], ignoreCase: false);

    /// <summary>The text the tokens were scanned from.</summary>
    public string Text { get; }

    public IReadOnlyList<CodeToken> Tokens { get; }

    /// <summary>IDE-ED-075: the directives of conditional groups and region pairs, sorted by offset.</summary>
    public IReadOnlyList<CodeToken> Directives { get; }

    /// <summary>Identifiers compare case-insensitively (Object Pascal, IDE-ED-074).</summary>
    public bool IgnoreCase { get; }

    /// <summary>The buffer version of <see cref="Text"/> (-1 when built outside a buffer).</summary>
    public long Version { get; }

    internal CodeTokens WithVersion(long version) => new(Text, Tokens, _partners, IgnoreCase, version, Directives, _nextInGroup);

    /// <summary>The partner of the token at <paramref name="index"/> within <see cref="SearchLimit"/> tokens, or null.</summary>
    public CodeToken? PartnerOf(int index)
    {
        var p = _partners[index];
        return p >= 0 && Math.Abs(Tokens[p].LexIndex - Tokens[index].LexIndex) <= SearchLimit ? Tokens[p] : null;
    }

    /// <summary>
    /// UI-ED-053 / IDE-ED-070/071/075: a bracket right before the caret, else one right after it, else a block keyword
    /// the caret is inside of (or at either end of), else a directive of a group likewise; with its partner.
    /// </summary>
    public BracketMatch? MatchAt(int caret) =>
        IndexAt(caret) is { } i ? new BracketMatch(Tokens[i], PartnerOf(i)) : DirectiveAt(caret) is { } d ? GroupOf(d) : null;

    /// <summary>The directive the caret is inside of or at either end of (the first one when two touch), or null.</summary>
    private int? DirectiveAt(int caret)
    {
        int low = 0, high = Directives.Count;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (Directives[mid].End >= caret) high = mid;
            else low = mid + 1;
        }

        return low < Directives.Count && Directives[low].Offset <= caret ? low : null;
    }

    /// <summary>
    /// IDE-ED-075/076: the group of the directive at <paramref name="index"/>, limited to the members within
    /// <see cref="SearchLimit"/> tokens of it; alone (no partner) when no other member is left. The partner is the next
    /// member in document order, after the last one the first.
    /// </summary>
    private BracketMatch GroupOf(int index)
    {
        var at = Directives[index];
        var others = new List<CodeToken>();
        for (int i = _nextInGroup[index]; i >= 0 && i != index; i = _nextInGroup[i])
        {
            if (Math.Abs(Directives[i].LexIndex - at.LexIndex) <= SearchLimit)
                others.Add(Directives[i]);
        }

        if (others.Count == 0)
            return new BracketMatch(at, null);
        return new BracketMatch(at, others[0], [.. others.Append(at).OrderBy(t => t.Offset)]);
    }

    private int? IndexAt(int caret)
    {
        int? before = null, after = null, keyword = null;
        for (int i = FirstEndingAtOrAfter(caret); i < Tokens.Count && Tokens[i].Offset <= caret; i++)
        {
            var t = Tokens[i];
            if (t.Kind == CodeTokenKind.Identifier)
                continue;
            if (t.IsKeyword)
            {
                if (t.Offset <= caret && caret <= t.End)
                    keyword ??= i;
            }
            else if (t.End == caret)
                before = i;
            else if (t.Offset == caret)
                after = i;
        }

        return before ?? after ?? keyword;
    }

    /// <summary>The index of the first token whose end is at least <paramref name="offset"/> (tokens do not overlap).</summary>
    private int FirstEndingAtOrAfter(int offset)
    {
        int low = 0, high = Tokens.Count;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (Tokens[mid].End >= offset) high = mid;
            else low = mid + 1;
        }

        return low;
    }

    /// <summary>The index of the last token starting before <paramref name="offset"/>, or -1.</summary>
    private int LastStartingBefore(int offset)
    {
        int low = 0, high = Tokens.Count;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (Tokens[mid].Offset >= offset) high = mid;
            else low = mid + 1;
        }

        return low - 1;
    }

    /// <summary>
    /// IDE-ED-072/076: where <c>search.goToMatchingBracket</c> puts the caret — the partner of the bracket at the caret
    /// (just before an opener, just after a closer, the start of a keyword or of the next directive of a group), else
    /// the nearest enclosing opener; null when there is none (or the bracket or directive at the caret has no partner).
    /// </summary>
    public int? MatchingBracketTarget(int caret)
    {
        if (MatchAt(caret) is { } match)
        {
            if (match.Partner is not { } partner)
                return null;
            return partner.Kind == CodeTokenKind.Close && !partner.IsKeyword ? partner.End : partner.Offset;
        }

        return EnclosingOpener(caret) is { } opener ? Tokens[opener].Offset : null;
    }

    /// <summary>The index of the innermost opener before <paramref name="caret"/> whose block (or bracket) is still open there.</summary>
    private int? EnclosingOpener(int caret)
    {
        for (int i = LastStartingBefore(caret); i >= 0; i--)
        {
            if (Tokens[i].Kind != CodeTokenKind.Open)
                continue;
            if (PartnerOf(i) is not { } partner || partner.Offset >= caret)
                return i;
        }

        return null;
    }

    /// <summary>
    /// The innermost keyword pair (IDE-ED-052) containing <paramref name="caret"/>, including the keywords themselves:
    /// the opener and its closer (IDE-ED-073).
    /// </summary>
    public (CodeToken Opener, CodeToken Closer)? BlockAt(int caret)
    {
        if (IndexAt(caret) is { } at && Tokens[at].IsKeyword && PartnerOf(at) is { } partner)
            return Tokens[at].Kind == CodeTokenKind.Open ? (Tokens[at], partner) : (partner, Tokens[at]);
        for (int i = LastStartingBefore(caret); i >= 0; i--)
        {
            var t = Tokens[i];
            if (t.Kind == CodeTokenKind.Open && t.IsKeyword && PartnerOf(i) is { } closer && closer.Offset >= caret)
                return (t, closer);
        }

        return null;
    }

    /// <summary>
    /// IDE-ED-074 / UI-ED-054: with the caret on an identifier (inside or at either end) and no selection, or a selection
    /// that is exactly one identifier, the other identifiers with the same text (<see cref="IgnoreCase"/>); else none.
    /// </summary>
    public IReadOnlyList<CodeToken> Occurrences(int caret, TextSelection selection)
    {
        int? at = null;
        for (int i = FirstEndingAtOrAfter(caret); i < Tokens.Count && Tokens[i].Offset <= caret; i++)
        {
            if (Tokens[i].Kind == CodeTokenKind.Identifier)
                at ??= i;
        }

        if (at is not { } index)
            return [];
        var word = Tokens[index];
        if (!selection.IsEmpty && (selection.Start != word.Offset || selection.End != word.End))
            return [];
        var comparison = IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var result = new List<CodeToken>();
        for (int i = 0; i < Tokens.Count; i++)
        {
            var t = Tokens[i];
            if (i != index && t.Kind == CodeTokenKind.Identifier && t.Length == word.Length
                && string.Compare(Text, t.Offset, Text, word.Offset, word.Length, comparison) == 0)
                result.Add(t);
        }

        return result;
    }

    /// <summary>
    /// Pairs brackets: <paramref name="isOpen"/>/<paramref name="pairKey"/> tell each bracket token's direction and pair
    /// (one stack per pair, so <c>( ]</c> does not break the parentheses). Returns the partner array.
    /// </summary>
    public static int[] PairBrackets(IReadOnlyList<CodeToken> tokens, Func<int, char> pairKey)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(pairKey);
        var partners = new int[tokens.Count];
        Array.Fill(partners, -1);
        var stacks = new Dictionary<char, Stack<int>>();
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.IsKeyword || t.Kind == CodeTokenKind.Identifier)
                continue;
            var key = pairKey(i);
            if (!stacks.TryGetValue(key, out var stack))
                stacks[key] = stack = new Stack<int>();
            if (t.Kind == CodeTokenKind.Open)
                stack.Push(i);
            else if (stack.Count > 0)
            {
                var open = stack.Pop();
                partners[open] = i;
                partners[i] = open;
            }
        }

        return partners;
    }
}
