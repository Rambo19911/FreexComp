using System.Diagnostics;
using System.Text;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Syntax;

/// <summary>
/// Base of every syntax node (api.md §4). Nodes are immutable once constructed. Tokens keep the absolute positions
/// the lexer gave them, so spans need no offset arithmetic; a node's constructor attaches its children in source
/// order (<c>Add</c>), which sets every child's parent link (CMP-221: parent and spans O(1) / amortised O(1)).
/// Every node owns at least one token (a missing one, if nothing else), except <c>EmptyStatementSyntax</c>, which
/// is zero-width after the previous token.
/// </summary>
public abstract class SyntaxNode
{
    private readonly List<SyntaxNodeOrToken> _children = [];
    private SyntaxTree? _tree;
    private int _index;                 // slot in the parent's child list
    private SpanCache? _spans;

    internal SyntaxNode(SyntaxKind kind)
    {
        Kind = kind;
    }

    public SyntaxKind Kind { get; }

    public SyntaxNode? Parent { get; private set; }

    /// <summary>The tree this node belongs to. Nodes are only created by the parser, always inside a tree.</summary>
    public SyntaxTree SyntaxTree
    {
        get
        {
            if (_tree is null)
            {
                var tree = Parent?.SyntaxTree ?? throw new InvalidOperationException("The node is not part of a syntax tree.");
                _tree = tree;
            }

            return _tree;
        }
    }

    /// <summary>From the start of the first token to the end of the last token, without leading/trailing trivia.</summary>
    public TextSpan Span => Spans.Span;

    /// <summary>Including the leading trivia of the first token and the trailing trivia of the last.</summary>
    public TextSpan FullSpan => Spans.FullSpan;

    // computed once, published by one reference write (trees are shared between threads)
    private SpanCache Spans
    {
        get
        {
            var file = TryGetTree()?.Text;
            if (_spans is null && FirstOwnToken(file).Kind == SyntaxKind.None)
            {
                // a node without tokens of the tree's file (EmptyStatementSyntax, or a fragment that came entirely
                // from an include file): zero width after the previous token's trivia
                var previous = Parent is null ? default : TokenBefore(Parent, _index);
                while (previous.Kind != SyntaxKind.None && !IsOwn(previous, file))
                {
                    previous = previous.GetPreviousToken();
                }

                var empty = new TextSpan(previous.Kind == SyntaxKind.None ? 0 : previous.FullSpan.End, 0);
                _spans = new SpanCache(empty, empty);
            }

            if (_spans is null)
            {
                var first = FirstOwnToken(file);
                var last = LastOwnToken(file);
                _spans = new SpanCache(
                    TextSpan.FromBounds(first.Span.Start, Math.Max(first.Span.Start, last.Span.End)),
                    TextSpan.FromBounds(first.FullSpan.Start, Math.Max(first.FullSpan.Start, last.FullSpan.End)));
            }

            return _spans;
        }
    }

    private sealed record SpanCache(TextSpan Span, TextSpan FullSpan);

    /// <summary>
    /// Tokens from <c>{$I}</c> files are ordinary children but keep their own file and file-relative positions
    /// (api.md addendum, include option (a)); text and span queries of a tree use only the tree's own file's tokens.
    /// </summary>
    internal static bool IsOwn(SyntaxToken token, SourceText? file) =>
        file is null || token.File is null || ReferenceEquals(token.File, file);

    private SyntaxToken FirstOwnToken(SourceText? file)
    {
        foreach (var child in _children)
        {
            var token = child.AsNode() is { } node ? node.FirstOwnToken(file) : child.AsToken();
            if (token.Kind != SyntaxKind.None && IsOwn(token, file))
            {
                return token;
            }
        }

        return default;
    }

    private SyntaxToken LastOwnToken(SourceText? file)
    {
        for (var i = _children.Count - 1; i >= 0; i--)
        {
            var child = _children[i];
            var token = child.AsNode() is { } node ? node.LastOwnToken(file) : child.AsToken();
            if (token.Kind != SyntaxKind.None && IsOwn(token, file))
            {
                return token;
            }
        }

        return default;
    }

    /// <summary>The descendant tokens of the tree's own file (all tokens when the node is not in a tree).</summary>
    internal IEnumerable<SyntaxToken> OwnTokens()
    {
        var file = TryGetTree()?.Text;
        return DescendantTokens().Where(t => IsOwn(t, file));
    }

    /// <summary>True if a diagnostic of the tree lies within <see cref="FullSpan"/>.</summary>
    public bool ContainsDiagnostics
    {
        get
        {
            var tree = TryGetTree();
            if (tree is null)
            {
                return false;
            }

            var full = FullSpan;
            foreach (var diagnostic in tree.Diagnostics)
            {
                var location = diagnostic.Location;
                if (location.HasPosition && PathUtil.AreEqual(location.Path, tree.Text.Path)
                    && location.Span.Start >= full.Start && location.Span.End <= full.End)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>True if the node has tokens and every one of them is missing.</summary>
    public bool IsMissing => FirstToken().Kind != SyntaxKind.None && DescendantTokens().All(t => t.IsMissing);

    public IEnumerable<SyntaxNodeOrToken> ChildNodesAndTokens() => _children;

    public IEnumerable<SyntaxNode> ChildNodes()
    {
        foreach (var child in _children)
        {
            if (child.AsNode() is { } node)
            {
                yield return node;
            }
        }
    }

    /// <summary>All descendant nodes in pre-order (not this node); <paramref name="descendInto"/> false stops below a node.</summary>
    public IEnumerable<SyntaxNode> DescendantNodes(Func<SyntaxNode, bool>? descendInto = null)
    {
        // an explicit stack of child positions, no enumerator per node (C-244)
        var stack = new Stack<(SyntaxNode Node, int Index)>();
        var parent = this;
        var index = 0;
        while (true)
        {
            if (index < parent._children.Count)
            {
                if (parent._children[index++].AsNode() is not { } node)
                {
                    continue;
                }

                yield return node;
                if (descendInto is null || descendInto(node))
                {
                    stack.Push((parent, index));
                    parent = node;
                    index = 0;
                }
            }
            else if (stack.Count > 0)
            {
                (parent, index) = stack.Pop();
            }
            else
            {
                yield break;
            }
        }
    }

    /// <summary>All tokens of the node in source order, missing tokens included.</summary>
    public IEnumerable<SyntaxToken> DescendantTokens()
    {
        // an explicit stack: nested iterators would cost O(depth) per token (C-244)
        var stack = new Stack<(SyntaxNode Node, int Index)>();
        var node = this;
        var index = 0;
        while (true)
        {
            if (index < node._children.Count)
            {
                var child = node._children[index++];
                if (child.AsNode() is { } inner)
                {
                    stack.Push((node, index));
                    node = inner;
                    index = 0;
                }
                else
                {
                    yield return child.AsToken();
                }
            }
            else if (stack.Count > 0)
            {
                (node, index) = stack.Pop();
            }
            else
            {
                yield break;
            }
        }
    }

    public IEnumerable<SyntaxNode> AncestorsAndSelf()
    {
        for (var node = this; node is not null; node = node.Parent)
        {
            yield return node;
        }
    }

    public T? FirstAncestorOrSelf<T>()
        where T : SyntaxNode
    {
        for (var node = this; node is not null; node = node.Parent)
        {
            if (node is T match)
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>The first token of the node (possibly missing); <c>default</c> only for a node without tokens.</summary>
    public SyntaxToken FirstToken()
    {
        foreach (var child in _children)
        {
            var token = child.AsNode() is { } node ? node.FirstToken() : child.AsToken();
            if (token.Kind != SyntaxKind.None)
            {
                return token;
            }
        }

        return default;
    }

    /// <summary>The last token of the node (possibly missing); <c>default</c> only for a node without tokens.</summary>
    public SyntaxToken LastToken()
    {
        for (var i = _children.Count - 1; i >= 0; i--)
        {
            var child = _children[i];
            var token = child.AsNode() is { } node ? node.LastToken() : child.AsToken();
            if (token.Kind != SyntaxKind.None)
            {
                return token;
            }
        }

        return default;
    }

    /// <summary>The exact source text of the node including all trivia (CMP-220); tokens of include files are not part of it.</summary>
    public string ToFullString()
    {
        var builder = new StringBuilder();
        foreach (var token in OwnTokens())
        {
            token.WriteTo(builder);
        }

        return builder.ToString();
    }

    /// <summary>The text of <see cref="Span"/>: without the first token's leading and the last token's trailing trivia.</summary>
    public override string ToString()
    {
        var builder = new StringBuilder();
        var tokens = OwnTokens().ToList();
        for (var i = 0; i < tokens.Count; i++)
        {
            tokens[i].WriteTo(builder, leading: i > 0, trailing: i < tokens.Count - 1);
        }

        return builder.ToString();
    }

    public abstract TResult Accept<TResult>(SyntaxVisitor<TResult> visitor);

    // ------------------------------------------------------------------ construction (parser only)

    internal SyntaxToken Add(SyntaxToken token)
    {
        Debug.Assert(token.Parent is null, "A token can belong to one node only.");
        var attached = token.WithParent(this, _children.Count);
        _children.Add(attached);
        Invalidate();
        return attached;
    }

    internal SyntaxToken? Add(SyntaxToken? token) => token is { } t ? Add(t) : null;

    internal T Add<T>(T node)
        where T : SyntaxNode
    {
        ArgumentNullException.ThrowIfNull(node);
        Debug.Assert(node.Parent is null && node._tree is null, "A node can belong to one parent only.");
        node.Parent = this;
        node._index = _children.Count;
        node._spans = null;
        _children.Add(node);
        Invalidate();
        return node;
    }

    internal T? AddOptional<T>(T? node)
        where T : SyntaxNode => node is null ? null : Add(node);

    internal void Add(SyntaxNodeOrToken child)
    {
        if (child.AsNode() is { } node)
        {
            Add(node);
        }
        else
        {
            Add(child.AsToken());
        }
    }

    internal SyntaxList<T> Add<T>(SyntaxList<T> list)
        where T : SyntaxNode
    {
        foreach (var node in list)
        {
            Add(node);
        }

        return list;
    }

    internal SeparatedSyntaxList<T> Add<T>(SeparatedSyntaxList<T> list)
        where T : SyntaxNode
    {
        var attached = new List<SyntaxNodeOrToken>(list.Count * 2);
        foreach (var item in list.GetWithSeparators())
        {
            attached.Add(item.AsNode() is { } node ? Add(node) : Add(item.AsToken()));
        }

        return new SeparatedSyntaxList<T>([.. attached]);
    }

    /// <summary>Called by <see cref="SyntaxTree"/> for its root.</summary>
    internal void AttachTree(SyntaxTree tree)
    {
        Debug.Assert(Parent is null && _tree is null, "Only a parentless node can become a root.");
        _tree = tree;
    }

    private SyntaxTree? TryGetTree()
    {
        var root = this;
        while (root.Parent is not null)
        {
            root = root.Parent;
        }

        return root._tree;
    }

    private void Invalidate() => _spans = null;

    // ------------------------------------------------------------------ token navigation (SyntaxToken.GetNext/PreviousToken)

    internal static SyntaxToken TokenAfter(SyntaxNode parent, int index)
    {
        for (var node = parent; node is not null; index = node._index, node = node.Parent)
        {
            for (var i = index + 1; i < node._children.Count; i++)
            {
                var child = node._children[i];
                var token = child.AsNode() is { } n ? n.FirstToken() : child.AsToken();
                if (token.Kind != SyntaxKind.None)
                {
                    return token;
                }
            }
        }

        return default;
    }

    internal static SyntaxToken TokenBefore(SyntaxNode parent, int index)
    {
        for (var node = parent; node is not null; index = node._index, node = node.Parent)
        {
            for (var i = index - 1; i >= 0; i--)
            {
                var child = node._children[i];
                var token = child.AsNode() is { } n ? n.LastToken() : child.AsToken();
                if (token.Kind != SyntaxKind.None)
                {
                    return token;
                }
            }
        }

        return default;
    }

    // ------------------------------------------------------------------ position queries (SyntaxTree.FindToken/FindNode)

    internal SyntaxToken FindToken(int position)
    {
        var file = TryGetTree()?.Text;
        var node = this;
        while (true)
        {
            SyntaxNode? next = null;
            foreach (var child in node._children)
            {
                if (!child.FullSpan.Contains(position) || (child.IsToken && !IsOwn(child.AsToken(), file)))
                {
                    continue;
                }

                if (child.AsNode() is { } n)
                {
                    next = n;
                    break;
                }

                return child.AsToken();
            }

            if (next is null)
            {
                return node.LastOwnToken(file);
            }

            node = next;
        }
    }

    internal SyntaxNode FindNode(TextSpan span)
    {
        var node = this;
        while (true)
        {
            SyntaxNode? next = null;
            foreach (var child in node.ChildNodes())
            {
                var full = child.FullSpan;
                if (full.Start <= span.Start && span.End <= full.End && (full.Length > 0 || span.Length == 0))
                {
                    next = child;
                    break;
                }
            }

            if (next is null)
            {
                return node;
            }

            node = next;
        }
    }
}
