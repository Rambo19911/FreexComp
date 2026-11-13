using System.Collections.Immutable;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Symbols;

public enum ParameterMode { Value, Const, Var, Out }

/// <summary>A formal parameter of a routine (routines.md §3).</summary>
public sealed class ParameterSymbol : Symbol
{
    internal ParameterSymbol(string name, TypeSymbol type, ParameterMode mode, int ordinal, Symbol containingSymbol, ImmutableArray<Location> locations)
        : base(SymbolKind.Parameter, name, containingSymbol, locations)
    {
        Type = type;
        Mode = mode;
        Ordinal = ordinal;
    }

    public TypeSymbol Type { get; }

    public ParameterMode Mode { get; }

    /// <summary>Open array parameters arrive with M2.5.</summary>
    public bool IsOpenArray => false;

    /// <summary>Default values arrive with M2.5 (LANG-422).</summary>
    public ConstantValue? DefaultValue => null;

    /// <summary>0-based position in the parameter list.</summary>
    public int Ordinal { get; }

    internal bool IsByReference => Mode is ParameterMode.Var or ParameterMode.Out;

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

    public ImmutableArray<ParameterSymbol> Parameters { get; internal set; } = [];

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
    public string LinkName
    {
        get
        {
            if (ExplicitLinkName != null)
            {
                return ExplicitLinkName;
            }

            var name = (ContainingSymbol is RoutineSymbol outer ? outer.LinkName : ContainingSymbol?.QualifiedName) is { } prefix ? prefix + "." + Name : Name;
            return IsOverload
                ? name + "$" + string.Join(",", Parameters.Select(p => SignaturePrefix(p.Mode) + p.Type.Name))
                : name;
        }
    }

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

    private static string SignaturePrefix(ParameterMode mode) => mode switch
    {
        ParameterMode.Const => "c:",
        ParameterMode.Var => "v:",
        ParameterMode.Out => "o:",
        _ => "",
    };
}

/// <summary>A type parameter of a generic type or routine (M2.6); declared now so the API shape of §5 holds.</summary>
public sealed class TypeParameterSymbol : Symbol
{
    internal TypeParameterSymbol(string name, Symbol containingSymbol, ImmutableArray<Location> locations)
        : base(SymbolKind.TypeParameter, name, containingSymbol, locations)
    {
    }
}
