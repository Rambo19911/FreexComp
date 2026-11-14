using System.Collections.Immutable;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Symbols;

public enum ParameterMode { Value, Const, Var, Out }

/// <summary>A formal parameter of a routine (routines.md §3).</summary>
public sealed class ParameterSymbol : Symbol
{
    /// <param name="highType">The built-in <c>Int64</c>: required for an open array parameter (its High companion).</param>
    internal ParameterSymbol(string name, TypeSymbol type, ParameterMode mode, int ordinal, Symbol containingSymbol, ImmutableArray<Location> locations,
        TypeSymbol? highType = null)
        : base(SymbolKind.Parameter, name, containingSymbol, locations)
    {
        Type = type;
        Mode = mode;
        Ordinal = ordinal;
        if (type is OpenArrayTypeSymbol)
        {
            // ABI-080: the High companion travels right after the data pointer (C-106); debug name "A$high" (DBG-138)
            HighParameter = new ParameterSymbol(name + "$high", highType ?? throw new ArgumentNullException(nameof(highType)),
                ParameterMode.Value, ordinal, containingSymbol, []);
        }
    }

    public TypeSymbol Type { get; }

    public ParameterMode Mode { get; }

    /// <summary>An open array parameter (LANG-430, C-106).</summary>
    public bool IsOpenArray => Type is OpenArrayTypeSymbol;

    /// <summary>The hidden <c>Int64</c> High companion of an open array parameter (ABI-080); null for other parameters.</summary>
    internal ParameterSymbol? HighParameter { get; }

    /// <summary>LANG-422: the default value, converted to <see cref="Type"/>; null when the parameter has none (C-105).</summary>
    public ConstantValue? DefaultValue { get; internal set; }

    /// <summary>0-based position in the parameter list.</summary>
    public int Ordinal { get; }

    /// <summary>Passed as the address of the caller's variable: <c>var</c>, <c>out</c>, and untyped <c>const</c> (C-202).</summary>
    internal bool IsByReference => Mode is ParameterMode.Var or ParameterMode.Out || IsUntyped;

    /// <summary>An untyped <c>var</c>/<c>const</c>/<c>out</c> parameter (routines.md §3, C-202).</summary>
    public bool IsUntyped => Type is UntypedTypeSymbol;

    /// <summary><c>param [const|var|out] Name: Type</c> (CMP-731).</summary>
    public override string ToDisplayString() => $"param {Prefix(Mode)}{Name}: {Type.Name}";

    internal static string Prefix(ParameterMode mode) => mode switch
    {
        ParameterMode.Const => "const ",
        ParameterMode.Var => "var ",
        ParameterMode.Out => "out ",
        _ => "",
    };
}

/// <summary>
/// A procedure or function (routines.md). One symbol per routine: the definition of a <c>forward</c> or interface
/// declaration completes the same symbol (its location is added).
/// </summary>
public class RoutineSymbol : Symbol
{
    internal RoutineSymbol(string name, Symbol containingSymbol, ImmutableArray<Location> locations, RoutineDeclarationSyntax declaration)
        : base(SymbolKind.Routine, name, containingSymbol, locations)
    {
        Declaration = declaration;
    }

    /// <summary>A routine of a unit imported from its <c>.pcu</c> (C-074): no syntax, no body in this compilation.</summary>
    internal RoutineSymbol(string name, UnitSymbol unit, ImmutableArray<Location> locations)
        : base(SymbolKind.Routine, name, unit, locations)
    {
        Declaration = null!;
    }

    /// <summary>The signature of a procedural or method pointer type (LANG-480/481): declared in no scope, without declaration or body.</summary>
    internal RoutineSymbol(string name, TypeSymbol type)
        : base(SymbolKind.Routine, name, type, type.Locations)
    {
        Declaration = null!;
    }

    /// <summary>A method (<see cref="MethodSymbol"/>): declared in a class body, no <see cref="Declaration"/>.</summary>
    private protected RoutineSymbol(SymbolKind kind, string name, Symbol containingSymbol, ImmutableArray<Location> locations, Visibility visibility)
        : base(kind, name, containingSymbol, locations, visibility)
    {
        Declaration = null!;
    }

    public ImmutableArray<ParameterSymbol> Parameters { get; internal set; } = [];

    /// <summary>Parameters the IR passes before the hidden <c>Result</c> and <see cref="Parameters"/> (ABI-082): a method's <c>Self</c> and flag.</summary>
    internal virtual ImmutableArray<ParameterSymbol> ImplicitParameters => [];

    /// <summary>Null for procedures.</summary>
    public TypeSymbol? ReturnType { get; internal set; }

    /// <summary>Generic routines arrive with M2.6.</summary>
    public ImmutableArray<TypeParameterSymbol> TypeParameters => [];

    public bool IsOverload { get; internal set; }

    public bool IsExternal { get; internal set; }

    public bool IsInline { get; internal set; }

    /// <summary>Declared in another routine's declaration part (LANG-450).</summary>
    public bool IsNested => ContainingSymbol is RoutineSymbol;

    /// <summary>CMP-360.</summary>
    public string LinkName => Emit.Mangler.RoutineText(this);

    /// <summary>The library of an <c>external 'lib'</c> routine (LANG-463); null otherwise.</summary>
    public string? LibraryName { get; internal set; }

    /// <summary>Import name of an external routine or the <c>public name</c> (LANG-463, 470).</summary>
    internal string? ExplicitLinkName { get; set; }

    internal bool IsNoReturn { get; set; }

    /// <summary>The first declaration (heading, directives).</summary>
    internal RoutineDeclarationSyntax Declaration { get; }

    /// <summary>The declaration with the body; null while a forward/interface routine is undefined, and for externals.</summary>
    internal RoutineDeclarationSyntax? Definition { get; set; }

    /// <summary>The scope the definition appears in: parent of the routine's own scope.</summary>
    internal Scope? DefinitionScope { get; set; }

    /// <summary>Declarations of <see cref="DefinitionScope"/> made up to and including the definition (LANG-402).</summary>
    internal int DefinitionScopeVisibleCount { get; set; }

    /// <summary>The implicit <c>Result</c> variable of a function (LANG-410); null for procedures.</summary>
    internal VariableSymbol? ResultVariable { get; set; }

    /// <summary>Declared without a body (<c>forward</c> or in an interface section) and not yet defined (E2100).</summary>
    internal bool AwaitsDefinition => Definition is null && !IsExternal;

    /// <summary><c>function U.Name(A: Integer; const B: string): Boolean</c> (CMP-731).</summary>
    public override string ToDisplayString()
    {
        var parameters = Parameters.IsEmpty ? "" : "(" + string.Join("; ", Parameters.Select(p => $"{ParameterSymbol.Prefix(p.Mode)}{p.Name}: {p.Type.Name}")) + ")";
        return ReturnType is null ? $"procedure {QualifiedName}{parameters}" : $"function {QualifiedName}{parameters}: {ReturnType.Name}";
    }
}

/// <summary>A type parameter of a generic type or routine (M2.6); declared now so the API shape of §5 holds.</summary>
public sealed class TypeParameterSymbol : Symbol
{
    internal TypeParameterSymbol(string name, Symbol containingSymbol, ImmutableArray<Location> locations)
        : base(SymbolKind.TypeParameter, name, containingSymbol, locations)
    {
    }
}
