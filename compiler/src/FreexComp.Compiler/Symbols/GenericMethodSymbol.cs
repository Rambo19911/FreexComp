using System.Collections.Immutable;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Symbols;

/// <summary>
/// A generic method <c>function Max&lt;T&gt;(A, B: T): T;</c> of a class or record (LANG-701, C-117). Each distinct argument list gets
/// an ordinary <see cref="MethodSymbol"/> (<see cref="Instances"/>) bound from <see cref="Declaration"/> with the type parameters
/// declared as the arguments; its link name carries the arguments (<c>U.TUtil.Max&lt;Integer&gt;</c>, CMP-360).
/// </summary>
public sealed class GenericMethodSymbol : Symbol
{
    internal GenericMethodSymbol(string name, TypeSymbol owner, MethodDeclarationSyntax declaration, Visibility visibility, ImmutableArray<string> parameterNames,
        Scope declaringScope, Symbol declaringContainer, ImmutableArray<Location> locations)
        : base(SymbolKind.Method, name, owner, locations, visibility)
    {
        Declaration = declaration;
        ParameterNames = parameterNames;
        DeclaringScope = declaringScope;
        DeclaringContainer = declaringContainer;
    }

    public TypeSymbol Owner => (TypeSymbol)ContainingSymbol!;

    public ImmutableArray<string> ParameterNames { get; }

    /// <summary>LANG-702 (C-118): the constraints of each type parameter.</summary>
    internal ImmutableArray<TypeParameterConstraints> Constraints { get; set; } = [];

    internal MethodDeclarationSyntax Declaration { get; }

    /// <summary>The scope the owner's members were bound in; instances bind their heading below it.</summary>
    internal Scope DeclaringScope { get; }

    internal Symbol DeclaringContainer { get; }

    /// <summary><c>function TUtil.Max&lt;T&gt;(…)</c>, once found (LANG-704).</summary>
    internal GenericMethodImplementation? Implementation { get; set; }

    internal Dictionary<ImmutableArray<TypeSymbol>, MethodSymbol> Instances { get; } = new(GenericTypeSymbol.ArgumentsComparer.Instance);

    public override string ToDisplayString() => $"{Owner.Name}.{Name}<{string.Join(", ", ParameterNames)}>";
}