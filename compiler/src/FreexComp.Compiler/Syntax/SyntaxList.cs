using System.Collections;
using System.Collections.Immutable;

namespace FreexComp.Compiler.Syntax;

/// <summary>A sequence of child nodes of one kind (e.g. the declarations of a block). <c>default</c> is empty.</summary>
public readonly struct SyntaxList<T> : IReadOnlyList<T>
    where T : SyntaxNode
{
    private readonly ImmutableArray<T> _nodes;

    internal SyntaxList(ImmutableArray<T> nodes)
    {
        _nodes = nodes;
    }

    private ImmutableArray<T> Nodes => _nodes.IsDefault ? [] : _nodes;

    public int Count => Nodes.Length;

    public T this[int index] => Nodes[index];

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Nodes).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Nodes separated by tokens (<c>A, B, C</c>): node, separator, node, … in source order. A trailing separator is
/// allowed (e.g. an optional final <c>;</c>). <c>default</c> is empty.
/// </summary>
public readonly struct SeparatedSyntaxList<T> : IReadOnlyList<T>
    where T : SyntaxNode
{
    private readonly ImmutableArray<SyntaxNodeOrToken> _items;

    internal SeparatedSyntaxList(ImmutableArray<SyntaxNodeOrToken> nodesAndSeparators)
    {
        _items = nodesAndSeparators;
    }

    private ImmutableArray<SyntaxNodeOrToken> Items => _items.IsDefault ? [] : _items;

    /// <summary>Number of nodes (separators not counted).</summary>
    public int Count => (Items.Length + 1) / 2;

    public T this[int index] => (T)Items[index * 2].AsNode()!;

    /// <summary>The separator tokens in source order.</summary>
    public IEnumerable<SyntaxToken> Separators
    {
        get
        {
            var items = Items;
            for (var i = 1; i < items.Length; i += 2)
            {
                yield return items[i].AsToken();
            }
        }
    }

    public ImmutableArray<SyntaxNodeOrToken> GetWithSeparators() => Items;

    public IEnumerator<T> GetEnumerator()
    {
        var items = Items;
        for (var i = 0; i < items.Length; i += 2)
        {
            yield return (T)items[i].AsNode()!;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
