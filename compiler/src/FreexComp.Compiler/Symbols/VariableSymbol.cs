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
        ConstantValue? initializer = null,
        Visibility visibility = Visibility.Public)
        : base(isGlobal ? SymbolKind.Variable : SymbolKind.Local, name, containingSymbol, locations, visibility)
    {
        Type = type;
        IsGlobal = isGlobal;
        Initializer = initializer;
    }

    public TypeSymbol Type { get; }

    public bool IsGlobal { get; }

    /// <summary>Folded value of a global initialiser <c>var X: T = c;</c> (LANG-522); null when there is none.</summary>
    internal ConstantValue? Initializer { get; }

    /// <summary>The static storage of a typed constant (LANG-344): assignments and <c>var</c> arguments are rejected.</summary>
    internal bool IsReadOnly { get; init; }

    /// <summary>The typed constant whose static storage this is (LANG-344); null for other variables.</summary>
    internal ConstantSymbol? TypedConstant { get; set; }

    /// <summary>The <c>public name</c> of a global variable (LANG-470); null otherwise.</summary>
    internal string? ExplicitLinkName { get; set; }

    /// <summary>
    /// A <c>threadvar</c> (LANG-345): its offset in the threadvar record of its unit (ABI-180); null for every other variable.
    /// </summary>
    internal long? ThreadVarOffset { get; init; }

    public bool IsThreadVar => ThreadVarOffset is not null;

    /// <summary>
    /// A local of a unit-level body (program block, initialization, finalization) that an anonymous method uses: unless it
    /// moves to a block frame (<see cref="BlockFrame"/>) it keeps the body's lifetime instead of its declaration scope's — a
    /// <c>for var</c> variable (LANG-721).
    /// </summary>
    internal bool IsCapturedBodyLocal { get; set; }

    /// <summary>
    /// The block frame a captured inline variable lives in (inline-variables.md LANG-566, CMP-1183; C-308): one instance per
    /// execution of its declaration scope. Null for every other variable.
    /// </summary>
    internal Binding.ClosureInfo? BlockFrame { get; set; }

    /// <summary>
    /// The debug scope of an inline variable or typed constant (inline-variables.md CMP-1184, C-309): the first token of its
    /// declaration statement and the token that closes its statement list (the last token of a <c>repeat</c> or <c>for var</c>
    /// statement; none when the list ends a unit section). Null for every other variable.
    /// </summary>
    internal (Syntax.SyntaxToken From, Syntax.SyntaxToken To)? InlineScope { get; set; }

    /// <summary><c>var Name: Type</c> / <c>threadvar Name: Type</c> (CMP-731).</summary>
    public override string ToDisplayString() => $"{(IsThreadVar ? "threadvar" : "var")} {Name}: {Type.Name}";
}
