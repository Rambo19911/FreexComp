using System.Collections.Immutable;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Symbols;

/// <summary>
/// A label of a block's <c>label</c> section (LANG-380; api.md §5): declared there, defined by one labelled statement and named by
/// the <c>goto</c> statements of the block. Labels are no scope symbols — a name lookup never finds one.
/// </summary>
public sealed class LabelSymbol : Symbol
{
    internal LabelSymbol(string name, Symbol? containingSymbol, ImmutableArray<Location> locations)
        : base(SymbolKind.Label, name, containingSymbol, locations)
    {
    }

    public override string ToDisplayString() => "label " + Name;
}
