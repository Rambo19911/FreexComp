using System.Collections.Immutable;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;

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

    public static ImmutableArray<(string Name, TypeSymbol Target)> NestedAliases(TypeSymbol type) => type switch
    {
        ClassTypeSymbol c => c.NestedAliases,
        RecordTypeSymbol r => r.NestedAliases,
        HelperTypeSymbol h => h.NestedAliases,
        _ => [],
    };

    /// <summary>The nested type <paramref name="name"/> of <paramref name="type"/>: declared there, or a nested alias (C-311).</summary>
    public static TypeSymbol? NestedType(TypeSymbol type, string name)
    {
        if (NestedMembers(type).FirstOrDefault(m => m is not GenericTypeSymbol && IdentifierComparer.Instance.Equals(m.Name, name)) is { } member)
        {
            return member as TypeSymbol;
        }

        var alias = NestedAliases(type).FirstOrDefault(a => IdentifierComparer.Instance.Equals(a.Name, name));
        return alias.Target;
    }

    /// <summary>LANG-778 (C-345): the generic types named <paramref name="name"/> nested in <paramref name="type"/> (any arity).</summary>
    public static IEnumerable<GenericTypeSymbol> NestedGenerics(TypeSymbol type, string name) =>
        NestedMembers(type).OfType<GenericTypeSymbol>().Where(g => IdentifierComparer.Instance.Equals(g.Name, name));

    /// <summary>The class or record a nested type is declared in, innermost first (LANG-770).</summary>
    public static IEnumerable<TypeSymbol> EnclosingTypes(Symbol symbol)
    {
        for (var outer = symbol.ContainingSymbol; outer is ClassTypeSymbol or RecordTypeSymbol; outer = outer.ContainingSymbol)
        {
            yield return (TypeSymbol)outer;
        }
    }

    /// <summary>C-344: <paramref name="type"/> and, depth first, the class and record types nested in it (LANG-771), each declared there.</summary>
    public static IEnumerable<TypeSymbol> SelfAndNestedTypes(TypeSymbol type) =>
        new[] { type }.Concat(NestedMembers(type).OfType<TypeSymbol>().Where(t => t is ClassTypeSymbol or RecordTypeSymbol && ReferenceEquals(t.ContainingSymbol, type))
            .SelectMany(SelfAndNestedTypes));

    /// <summary>LANG-775 (C-344): the display name of a type — for a nested type its enclosing types' names first, <c>TOuter.TInner</c>.</summary>
    public static string DisplayName(TypeSymbol type) =>
        string.Join(".", EnclosingTypes(type).Reverse().Select(t => t.Name).Append(type.Name));

    /// <summary>
    /// CMP-1391 (C-345): the generic instance a type is part of — itself or its innermost enclosing instance (<c>TList&lt;Integer&gt;</c>
    /// for <c>TList&lt;Integer&gt;.TEnumerator</c>; <c>TOuter.TCell&lt;string&gt;</c> itself); null for a type of no instance.
    /// </summary>
    public static TypeSymbol? EnclosingInstance(TypeSymbol type) => new[] { type }.Concat(EnclosingTypes(type)).FirstOrDefault(t => t.IsGenericInstance);

    /// <summary>The unit-level type a (possibly nested) type belongs to.</summary>
    public static TypeSymbol Outermost(TypeSymbol type) => EnclosingTypes(type).LastOrDefault() ?? type;
}
