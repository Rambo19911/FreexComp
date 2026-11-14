using System.Collections.Immutable;
using FreexComp.Compiler.Symbols;

namespace FreexComp.Compiler.Types;

/// <summary>
/// The member lists shared by classes and records with methods (LANG-730, C-122): code that binds methods, properties and
/// class vars works on either through these.
/// </summary>
internal static class TypeMembers
{
    /// <summary>A class and its ancestors (the class first); any other type alone.</summary>
    public static IEnumerable<TypeSymbol> SelfAndAncestors(TypeSymbol type) => type switch
    {
        ClassTypeSymbol c => c.SelfAndAncestors(),
        HelperTypeSymbol h => h.SelfAndAncestors(),
        _ => [type],
    };

    public static ImmutableArray<FieldSymbol> Fields(TypeSymbol type) => type switch
    {
        ClassTypeSymbol c => c.Fields,
        RecordTypeSymbol r => r.Fields,
        _ => [],
    };

    public static ImmutableArray<MethodSymbol> Methods(TypeSymbol type) => type switch
    {
        ClassTypeSymbol c => c.Methods,
        RecordTypeSymbol r => r.Methods,
        HelperTypeSymbol h => h.Methods,
        _ => [],
    };

    public static ImmutableArray<VariableSymbol> ClassVariables(TypeSymbol type) => type switch
    {
        ClassTypeSymbol c => c.ClassVariables,
        RecordTypeSymbol r => r.ClassVariables,
        HelperTypeSymbol h => h.ClassVariables,
        _ => [],
    };

    public static ImmutableArray<Symbol> NestedMembers(TypeSymbol type) => type switch
    {
        ClassTypeSymbol c => c.NestedMembers,
        RecordTypeSymbol r => r.NestedMembers,
        HelperTypeSymbol h => h.NestedMembers,
        _ => [],
    };

    /// <summary>The class or record a nested type is declared in, innermost first (LANG-770).</summary>
    public static IEnumerable<TypeSymbol> EnclosingTypes(Symbol symbol)
    {
        for (var outer = symbol.ContainingSymbol; outer is ClassTypeSymbol or RecordTypeSymbol; outer = outer.ContainingSymbol)
        {
            yield return (TypeSymbol)outer;
        }
    }

    /// <summary>The unit-level type a (possibly nested) type belongs to.</summary>
    public static TypeSymbol Outermost(TypeSymbol type) => EnclosingTypes(type).LastOrDefault() ?? type;
}
