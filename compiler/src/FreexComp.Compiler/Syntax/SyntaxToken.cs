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

    internal SyntaxToken(SyntaxKind kind, string text, object? value, int position,
                         ImmutableArray<SyntaxTrivia> leading, ImmutableArray<SyntaxTrivia> trailing,
                         DirectiveState? state = null)
    {
        Kind = kind;
        ContextualKind = kind;
        _text = text;
        Value = value;
        _position = position;
        _leading = leading;
        _trailing = trailing;
        _state = state;
    }

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

    public bool IsMissing => false;

    public ImmutableArray<SyntaxTrivia> LeadingTrivia => _leading.IsDefault ? [] : _leading;

    public ImmutableArray<SyntaxTrivia> TrailingTrivia => _trailing.IsDefault ? [] : _trailing;

    public bool Equals(SyntaxToken other) =>
        Kind == other.Kind && _position == other._position && Text == other.Text && FullSpan == other.FullSpan;

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
