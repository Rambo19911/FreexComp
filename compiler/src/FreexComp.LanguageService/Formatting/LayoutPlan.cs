using FreexComp.Compiler.Syntax;

namespace FreexComp.LanguageService.Formatting;

/// <summary>How the gap before a token is laid out.</summary>
internal enum GapRule : byte
{
    /// <summary>The token keeps the line break (or not) it had; broken, it is indented at its level.</summary>
    Free,

    /// <summary>The token stays on the previous token's line.</summary>
    Join,

    /// <summary>The token is the first token of its line (CMP-1441).</summary>
    Line,
}

/// <summary>Blank lines directly before a token (CMP-1443).</summary>
internal enum BlankRule : byte
{
    /// <summary>As written, at most <see cref="FormatProfile.MaxBlankLines"/>.</summary>
    Keep,

    /// <summary>None (directly before <c>end</c>).</summary>
    None,

    /// <summary>Exactly one (between routine implementations, before <c>implementation</c> …).</summary>
    One,
}

/// <summary>
/// The layout decided for every token of the main file (formatter.md §2): the rule of the gap before it, the indentation level
/// of its line when it starts one, the level of comments on their own line before it, and the blank-line rules. Built by
/// <see cref="LayoutPlanner"/> top-down; the first decision for a token wins (a parent decides before its children).
/// </summary>
internal sealed class LayoutPlan
{
    private readonly Dictionary<int, int> _indexByStart;
    private readonly bool[] _ruleSet;
    private readonly bool[] _commentSet;
    private readonly bool[] _blankSet;

    public LayoutPlan(SyntaxToken[] tokens)
    {
        Tokens = tokens;
        var n = tokens.Length;
        Rules = new GapRule[n];
        Levels = new int[n];
        CommentLevels = new int[n];
        BlankBefore = new BlankRule[n];
        NoBlankAfter = new bool[n];
        _ruleSet = new bool[n];
        _commentSet = new bool[n];
        _blankSet = new bool[n];
        _indexByStart = new Dictionary<int, int>(n);
        for (var i = 0; i < n; i++)
        {
            _indexByStart[tokens[i].Span.Start] = i;
        }
    }

    public SyntaxToken[] Tokens { get; }

    public GapRule[] Rules { get; }

    public int[] Levels { get; }

    /// <summary>The level of a comment or directive that has its own line before the token.</summary>
    public int[] CommentLevels { get; }

    public BlankRule[] BlankBefore { get; }

    public bool[] NoBlankAfter { get; }

    public int IndexOf(SyntaxToken token) =>
        token.Kind != SyntaxKind.None && !token.IsMissing && _indexByStart.TryGetValue(token.Span.Start, out var i) && Tokens[i].Kind == token.Kind ? i : -1;

    public bool IsSet(SyntaxToken token) => IndexOf(token) is var i and >= 0 && _ruleSet[i];

    public void Set(SyntaxToken? token, GapRule rule, int level)
    {
        if (token is { } t && IndexOf(t) is var i and >= 0 && !_ruleSet[i])
        {
            _ruleSet[i] = true;
            Rules[i] = rule;
            Levels[i] = level;
        }
    }

    public void SetCommentLevel(SyntaxToken? token, int level)
    {
        if (token is { } t && IndexOf(t) is var i and >= 0 && !_commentSet[i])
        {
            _commentSet[i] = true;
            CommentLevels[i] = level;
        }
    }

    public void SetBlank(SyntaxToken? token, BlankRule rule)
    {
        if (token is { } t && IndexOf(t) is var i and >= 0 && !_blankSet[i])
        {
            _blankSet[i] = true;
            BlankBefore[i] = rule;
        }
    }

    public void SetNoBlankAfter(SyntaxToken? token)
    {
        if (token is { } t && IndexOf(t) is var i and >= 0)
        {
            NoBlankAfter[i] = true;
        }
    }

    /// <summary>Tokens without a comment level use their own level.</summary>
    public void Complete()
    {
        for (var i = 0; i < Tokens.Length; i++)
        {
            if (!_commentSet[i])
            {
                CommentLevels[i] = Levels[i];
            }
        }
    }
}
