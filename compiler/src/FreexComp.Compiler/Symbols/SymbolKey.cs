using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Symbols;

/// <summary>
/// C-242 (CMP-130): the identity of a symbol across snapshots. A unit taken over from an earlier snapshot keeps the symbols of the
/// units it used as they were then, while an edited unit is bound anew; both denote the same declaration when kind, qualified name
/// (case-insensitive) and first declaration site (file and offset) agree. A body-only edit keeps every interface declaration site.
/// </summary>
public readonly struct SymbolKey : IEquatable<SymbolKey>
{
    private SymbolKey(SymbolKind kind, string qualifiedName, string path, int position)
    {
        Kind = kind;
        QualifiedName = qualifiedName;
        Path = path;
        Position = position;
    }

    public SymbolKind Kind { get; }

    public string QualifiedName { get; }

    /// <summary>The file of the first declaration site; empty for built-ins.</summary>
    public string Path { get; }

    /// <summary>The offset of the first declaration site; -1 for built-ins.</summary>
    public int Position { get; }

    public static SymbolKey Create(Symbol symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        return symbol.Locations.IsEmpty
            ? new SymbolKey(symbol.Kind, symbol.QualifiedName, "", -1)
            : new SymbolKey(symbol.Kind, symbol.QualifiedName, PathUtil.Normalize(symbol.Locations[0].Path), symbol.Locations[0].Span.Start);
    }

    /// <summary>Same object, or the same declaration seen through two snapshots.</summary>
    public static bool AreSame(Symbol? a, Symbol? b) =>
        ReferenceEquals(a, b) || a is not null && b is not null && Create(a).Equals(Create(b));

    public bool Equals(SymbolKey other) =>
        Kind == other.Kind && Position == other.Position && IdentifierComparer.Instance.Equals(QualifiedName ?? "", other.QualifiedName ?? "")
        && PathUtil.Comparer.Equals(Path ?? "", other.Path ?? "");

    public override bool Equals(object? obj) => obj is SymbolKey other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Kind, Position, IdentifierComparer.Instance.GetHashCode(QualifiedName ?? ""), PathUtil.Comparer.GetHashCode(Path ?? ""));

    public static bool operator ==(SymbolKey left, SymbolKey right) => left.Equals(right);

    public static bool operator !=(SymbolKey left, SymbolKey right) => !left.Equals(right);

    public override string ToString() => $"{Kind}:{QualifiedName}@{Path}:{Position}";
}
