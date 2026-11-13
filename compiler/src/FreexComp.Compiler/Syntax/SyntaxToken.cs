using System.Collections.Immutable;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Syntax;

/// <summary>A token with its trivia (api.md §4). Immutable; <c>default</c> is a <see cref="SyntaxKind.None"/> token.</summary>
public readonly struct SyntaxToken : IEquatable<SyntaxToken>
{
    private readonly string? _text;
    private readonly ImmutableArray<SyntaxTrivia> _leading;
    private readonly ImmutableArray<SyntaxTrivia> _trailing;
    private readonly int _position;
    private readonly DirectiveState? _state;
    private readonly SyntaxNode? _parent;
    private readonly int _index;          // slot in the parent's child list
    private readonly bool _missing;

    internal SyntaxToken(SyntaxKind kind, string text, object? value, int position,
                         ImmutableArray<SyntaxTrivia> leading, ImmutableArray<SyntaxTrivia> trailing,
                         DirectiveState? state = null, SourceText? file = null)
    {
        Kind = kind;
        ContextualKind = kind;
        _text = text;
        Value = value;
        _position = position;
        _leading = leading;
        _trailing = trailing;
        _state = state;
        File = file;
    }

    private SyntaxToken(SyntaxToken token, SyntaxNode? parent, int index, bool missing)
    {
        this = token;
        _parent = parent;
        _index = index;
        _missing = missing;
    }

    /// <summary>A zero-width token inserted by the parser for a required but absent token (LANG-181).</summary>
    internal static SyntaxToken Missing(SyntaxKind kind, int position, DirectiveState? state = null, SourceText? file = null) =>
        new(new SyntaxToken(kind, "", null, position, [], [], state, file), null, 0, missing: true);

    private SyntaxToken(SyntaxToken token, SyntaxKind contextualKind, ImmutableArray<SyntaxTrivia> leading)
    {
        this = token;
        ContextualKind = contextualKind;
        _leading = leading;
    }

    /// <summary>The parser's decision that this identifier is used as a contextual keyword (CMP-222).</summary>
    internal SyntaxToken WithContextualKind(SyntaxKind kind) => new(this, kind, _leading);

    /// <summary>The same token with other leading trivia (the parser prepends skipped tokens, LANG-182).</summary>
    internal SyntaxToken WithLeadingTrivia(ImmutableArray<SyntaxTrivia> leading) => new(this, ContextualKind, leading);

    /// <summary>This token attached to <paramref name="parent"/> at child slot <paramref name="index"/>.</summary>
    internal SyntaxToken WithParent(SyntaxNode parent, int index) => new(this, parent, index, _missing);

    internal int IndexInParent => _index;

    /// <summary>The file this token was read from (the main file or an include file, LANG-876); spans are relative to it.</summary>
    internal SourceText? File { get; }

    /// <summary>Switch state at this token (CMP-111); public access is <c>SemanticModel.GetDirectiveState</c>.</summary>
    internal DirectiveState DirectiveState => _state ?? DirectiveState.Default;

    /// <summary><see cref="SyntaxKind.IdentifierToken"/>, a literal, symbol or keyword kind, or EndOfFileToken.</summary>
    public SyntaxKind Kind { get; }

    /// <summary>Equals <see cref="Kind"/> for lexer tokens (CMP-222); the parser sets it for directives.</summary>
    public SyntaxKind ContextualKind { get; }

    /// <summary>The token text as written (without trivia).</summary>
    public string Text => _text ?? "";

    /// <summary>long/ulong/double/string for literals; the identifier name without '&amp;'; null otherwise.</summary>
    public object? Value { get; }

    public TextSpan Span => new(_position, Text.Length);

    public TextSpan FullSpan
    {
        get
        {
            var start = LeadingTrivia.IsEmpty ? _position : LeadingTrivia[0].Span.Start;
            var end = TrailingTrivia.IsEmpty ? Span.End : TrailingTrivia[^1].Span.End;
            return TextSpan.FromBounds(start, end);
        }
    }

    /// <summary>True for a zero-width token the parser inserted for a required but absent token (LANG-181).</summary>
    public bool IsMissing => _missing;

    /// <summary>The node owning this token; null for tokens from <see cref="Lexer.Tokenize"/>.</summary>
    public SyntaxNode? Parent => _parent;

    public ImmutableArray<SyntaxTrivia> LeadingTrivia => _leading.IsDefault ? [] : _leading;

    public ImmutableArray<SyntaxTrivia> TrailingTrivia => _trailing.IsDefault ? [] : _trailing;

    /// <summary>The next token of the tree in source order (missing tokens included); <c>default</c> at the end or without a parent.</summary>
    public SyntaxToken GetNextToken() => _parent is null ? default : SyntaxNode.TokenAfter(_parent, _index);

    /// <summary>The previous token of the tree in source order; <c>default</c> at the start or without a parent.</summary>
    public SyntaxToken GetPreviousToken() => _parent is null ? default : SyntaxNode.TokenBefore(_parent, _index);

    /// <summary>Leading trivia, text and trailing trivia.</summary>
    public string ToFullString()
    {
        var builder = new System.Text.StringBuilder();
        WriteTo(builder);
        return builder.ToString();
    }

    internal void WriteTo(System.Text.StringBuilder builder, bool leading = true, bool trailing = true)
    {
        if (leading)
        {
            foreach (var trivia in LeadingTrivia)
            {
                builder.Append(trivia.Text);
            }
        }

        builder.Append(Text);
        if (trailing)
        {
            foreach (var trivia in TrailingTrivia)
            {
                builder.Append(trivia.Text);
            }
        }
    }

    public bool Equals(SyntaxToken other) =>
        Kind == other.Kind && _position == other._position && Text == other.Text && FullSpan == other.FullSpan
        && ReferenceEquals(_parent, other._parent) && _index == other._index && _missing == other._missing;

    public override bool Equals(object? obj) => obj is SyntaxToken other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, _position, Text);

    public override string ToString() => Text;

    public static bool operator ==(SyntaxToken left, SyntaxToken right) => left.Equals(right);

    public static bool operator !=(SyntaxToken left, SyntaxToken right) => !left.Equals(right);
}

/// <summary>Whitespace, a line break, a comment, a directive, disabled or skipped text (api.md §4).</summary>
public readonly struct SyntaxTrivia : IEquatable<SyntaxTrivia>
{
    private readonly string? _text;
    private readonly int _position;

    internal SyntaxTrivia(SyntaxKind kind, string text, int position, DirectiveTriviaInfo? directive = null)
    {
        Kind = kind;
        _text = text;
        _position = position;
        Directive = directive;
    }

    public SyntaxKind Kind { get; }

    public string Text => _text ?? "";

    public TextSpan Span => new(_position, Text.Length);

    /// <summary>For <see cref="SyntaxKind.DirectiveTrivia"/>.</summary>
    public DirectiveTriviaInfo? Directive { get; }

    public bool Equals(SyntaxTrivia other) => Kind == other.Kind && _position == other._position && Text == other.Text;

    public override bool Equals(object? obj) => obj is SyntaxTrivia other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, _position, Text);

    public override string ToString() => Text;

    public static bool operator ==(SyntaxTrivia left, SyntaxTrivia right) => left.Equals(right);

    public static bool operator !=(SyntaxTrivia left, SyntaxTrivia right) => !left.Equals(right);
}

public sealed record DirectiveTriviaInfo(string Name, string Argument, TextSpan Span, bool IsActive);
