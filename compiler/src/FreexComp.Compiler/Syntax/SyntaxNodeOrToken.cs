using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Syntax;

/// <summary>A child of a node: either a node or a token (api.md §4).</summary>
public readonly struct SyntaxNodeOrToken : IEquatable<SyntaxNodeOrToken>
{
    private readonly SyntaxNode? _node;
    private readonly SyntaxToken _token;

    public SyntaxNodeOrToken(SyntaxNode node)
    {
        _node = node ?? throw new ArgumentNullException(nameof(node));
        _token = default;
    }

    public SyntaxNodeOrToken(SyntaxToken token)
    {
        _node = null;
        _token = token;
    }

    public bool IsNode => _node is not null;

    public bool IsToken => _node is null;

    public SyntaxNode? AsNode() => _node;

    /// <summary>The token, or <c>default</c> if this is a node.</summary>
    public SyntaxToken AsToken() => _node is null ? _token : default;

    public SyntaxKind Kind => _node?.Kind ?? _token.Kind;

    public SyntaxNode? Parent => _node is null ? _token.Parent : _node.Parent;

    public TextSpan Span => _node?.Span ?? _token.Span;

    public TextSpan FullSpan => _node?.FullSpan ?? _token.FullSpan;

    public static implicit operator SyntaxNodeOrToken(SyntaxNode node) => new(node);

    public static implicit operator SyntaxNodeOrToken(SyntaxToken token) => new(token);

    public bool Equals(SyntaxNodeOrToken other) => ReferenceEquals(_node, other._node) && _token.Equals(other._token);

    public override bool Equals(object? obj) => obj is SyntaxNodeOrToken other && Equals(other);

    public override int GetHashCode() => _node is null ? _token.GetHashCode() : _node.GetHashCode();

    public override string ToString() => _node?.ToString() ?? _token.Text;

    public static bool operator ==(SyntaxNodeOrToken left, SyntaxNodeOrToken right) => left.Equals(right);

    public static bool operator !=(SyntaxNodeOrToken left, SyntaxNodeOrToken right) => !left.Equals(right);
}
