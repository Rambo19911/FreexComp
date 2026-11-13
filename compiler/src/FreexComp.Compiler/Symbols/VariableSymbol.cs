using System.Collections.Immutable;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Symbols;

/// <summary>
/// A global (<see cref="SymbolKind.Variable"/>) or routine-local (<see cref="SymbolKind.Local"/>) variable; class
/// variables follow with classes.
/// </summary>
public sealed class VariableSymbol : Symbol
{
    internal VariableSymbol(
        string name,
        TypeSymbol type,
        bool isGlobal,
        Symbol? containingSymbol,
        ImmutableArray<Location> locations,
        ConstantValue? initializer = null)
        : base(isGlobal ? SymbolKind.Variable : SymbolKind.Local, name, containingSymbol, locations)
    {
        Type = type;
        IsGlobal = isGlobal;
        Initializer = initializer;
    }

    public TypeSymbol Type { get; }

    public bool IsGlobal { get; }

    /// <summary>Folded value of a global initialiser <c>var X: T = c;</c> (LANG-522); null when there is none.</summary>
    internal ConstantValue? Initializer { get; }

    /// <summary><c>var Name: Type</c> (CMP-731).</summary>
    public override string ToDisplayString() => $"var {Name}: {Type.Name}";
}
