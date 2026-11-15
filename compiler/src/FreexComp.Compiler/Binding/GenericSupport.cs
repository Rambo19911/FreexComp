using System.Collections.Immutable;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

/// <summary>
/// C-119 (lead decision 2026-11-15, CMP-162): the implementation-only routines, variables and constants of a unit that the method
/// implementations of its generics name. Instances are emitted in the modules that use them, so these get external linkage under
/// their mangled names, and the unit's <c>.pcu</c> records them (<c>genericSupport</c>) for instances bound against it.
/// A name counts when an identifier token of such an implementation spells it (a local of the same name over-approximates harmlessly).
/// </summary>
internal static class GenericSupport
{
    /// <summary>The symbols in implementation declaration order (each once; all overloads of a named routine).</summary>
    public static ImmutableArray<Symbol> Of(UnitSymbol unit)
    {
        var names = new HashSet<string>(IdentifierComparer.Instance);
        foreach (var implementation in Implementations(unit))
        {
            foreach (var token in implementation.Declaration.DescendantTokens())
            {
                if (token.Kind == SyntaxKind.IdentifierToken)
                {
                    names.Add(token.Value as string ?? token.Text);
                }
            }
        }

        if (names.Count == 0)
        {
            return [];
        }

        var visible = unit.InterfaceMembers.ToHashSet();
        return [.. unit.ImplementationMembers.Distinct()
            .Where(m => m is RoutineSymbol { IsNested: false } or VariableSymbol { IsGlobal: true } or ConstantSymbol && !visible.Contains(m) && names.Contains(m.Name))];
    }

    /// <summary>The method implementations of the unit's generic types and generic methods, in source order.</summary>
    public static IEnumerable<GenericMethodImplementation> Implementations(UnitSymbol unit)
    {
        var members = unit.InterfaceMembers.Concat(unit.ImplementationMembers).Distinct().ToList();
        var fromTypes = members.OfType<GenericTypeSymbol>().SelectMany(g => g.Implementations);
        var fromMethods = DeclarationBinder.OwnTypesOf(unit).SelectMany(t => (IEnumerable<GenericMethodSymbol>)DeclarationBinder.GenericMethodsOf(t))
            .Select(g => g.Implementation).OfType<GenericMethodImplementation>();
        return fromTypes.Concat(fromMethods).Distinct().OrderBy(i => i.Declaration.Span.Start);
    }

    /// <summary>The storage a support symbol links under (a typed constant's variable), or null (an untyped constant).</summary>
    public static Symbol? Linked(Symbol symbol) => symbol switch
    {
        ConstantSymbol { Storage: { } storage } => storage,
        ConstantSymbol => null,
        _ => symbol,
    };
}
