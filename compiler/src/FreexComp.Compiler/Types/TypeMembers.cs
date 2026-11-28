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

    /// <summary>The class constructor and class destructor of a class, record or helper, those it has (LANG-786, 995; never members, LANG-787).</summary>
    public static IEnumerable<MethodSymbol> ClassConstructors(TypeSymbol type) => type switch
    {
        ClassTypeSymbol c => new[] { c.ClassConstructor, c.ClassDestructor }.OfType<MethodSymbol>(),
        RecordTypeSymbol r => new[] { r.ClassConstructor, r.ClassDestructor }.OfType<MethodSymbol>(),
        HelperTypeSymbol h => new[] { h.ClassConstructor, h.ClassDestructor }.OfType<MethodSymbol>(),  // LANG-995 (C-441)
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

    public static ImmutableArray<(string Name, TypeSymbol Target, Visibility Visibility)> NestedAliases(TypeSymbol type) => type switch
    {
        ClassTypeSymbol c => c.NestedAliases,
        RecordTypeSymbol r => r.NestedAliases,
        HelperTypeSymbol h => h.NestedAliases,
        _ => [],
    };

    /// <summary>The nested type <paramref name="name"/> of <paramref name="type"/>: declared there, or a nested alias (C-311).</summary>
    public static TypeSymbol? NestedType(TypeSymbol type, string name) => NestedTypeEntry(type, name)?.Type;

    /// <summary>
    /// LANG-774 (C-359): the nested type or alias <paramref name="name"/> of <paramref name="type"/> with its declared name, the type that declares
    /// it and the visibility of its section (an alias has its own; it is not the visibility of the type it names).
    /// </summary>
    public static (TypeSymbol Type, string Name, TypeSymbol Declaring, Visibility Visibility)? NestedTypeEntry(TypeSymbol type, string name)
    {
        if (NestedMembers(type).FirstOrDefault(m => m is not GenericTypeSymbol && IdentifierComparer.Instance.Equals(m.Name, name)) is { } member)
        {
            // C-361: a helper's nested type has its section's visibility too (LANG-774); one declared elsewhere has none
            return member is not TypeSymbol nested ? null
                : EnclosingTypes(nested).FirstOrDefault() is { } enclosing ? (nested, nested.Name, enclosing, nested.NestedVisibility)
                : (nested, nested.Name, type, ReferenceEquals(nested.ContainingSymbol, type) ? nested.NestedVisibility : Visibility.Public);
        }

        var alias = NestedAliases(type).FirstOrDefault(a => IdentifierComparer.Instance.Equals(a.Name, name));
        return alias.Target is null ? null : (alias.Target, alias.Name, type, alias.Visibility);                 // C-361: a helper's too
    }

    /// <summary>LANG-778 (C-345): the generic types named <paramref name="name"/> nested in <paramref name="type"/> (any arity).</summary>
    public static IEnumerable<GenericTypeSymbol> NestedGenerics(TypeSymbol type, string name) =>
        NestedMembers(type).OfType<GenericTypeSymbol>().Where(g => IdentifierComparer.Instance.Equals(g.Name, name));

    /// <summary>
    /// LANG-780 amendment (C-354): the nested type <paramref name="name"/> in the type member scope of <paramref name="type"/> — its
    /// own, else the nearest ancestor's (a class's own nested type hides a same-named inherited one).
    /// </summary>
    public static TypeSymbol? MemberNestedType(TypeSymbol type, string name) => MemberNestedTypeEntry(type, name)?.Type;

    /// <summary><see cref="MemberNestedType"/> with its declared name, the type that declares it and its section's visibility (LANG-774, C-359).</summary>
    public static (TypeSymbol Type, string Name, TypeSymbol Declaring, Visibility Visibility)? MemberNestedTypeEntry(TypeSymbol type, string name) =>
        SelfAndAncestors(type).Select(t => NestedTypeEntry(t, name)).FirstOrDefault(e => e is not null);

    /// <summary>LANG-780 amendment (C-354): the generic types <paramref name="name"/> of the nearest of <paramref name="type"/> and its ancestors that declares any.</summary>
    public static IEnumerable<GenericTypeSymbol> MemberNestedGenerics(TypeSymbol type, string name) =>
        SelfAndAncestors(type).Select(t => NestedGenerics(t, name).ToList()).FirstOrDefault(g => g.Count > 0) ?? [];

    /// <summary>LANG-780 amendment (C-354): the nested member (type or constant) <paramref name="name"/> of <paramref name="type"/> or its nearest ancestor.</summary>
    public static Symbol? MemberNestedMember(TypeSymbol type, string name) =>
        SelfAndAncestors(type).Select(t => NestedMembers(t).FirstOrDefault(m => IdentifierComparer.Instance.Equals(m.Name, name))).FirstOrDefault(m => m is not null);

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

    /// <summary>
    /// LANG-775 (C-344): the display name of a type — for a nested type its enclosing types' names first, <c>TOuter.TInner</c>; a
    /// helper that declares it counts as an enclosing type (LANG-990 (c), C-441: <c>THelper.TInner</c>).
    /// </summary>
    public static string DisplayName(TypeSymbol type) =>
        string.Join(".", DeclaringTypes(type).Reverse().Select(t => t.Name).Append(type.Name));

    /// <summary>The classes, records and helpers a nested type is declared in, innermost first (LANG-770, LANG-990 (c)).</summary>
    private static IEnumerable<TypeSymbol> DeclaringTypes(Symbol symbol)
    {
        for (var outer = symbol.ContainingSymbol; outer is ClassTypeSymbol or RecordTypeSymbol or HelperTypeSymbol; outer = outer.ContainingSymbol)
        {
            yield return (TypeSymbol)outer;
        }
    }

    /// <summary>
    /// CMP-1391 (C-345): the generic instance a type is part of — itself or its innermost enclosing instance (<c>TList&lt;Integer&gt;</c>
    /// for <c>TList&lt;Integer&gt;.TEnumerator</c>; <c>TOuter.TCell&lt;string&gt;</c> itself); null for a type of no instance.
    /// </summary>
    public static TypeSymbol? EnclosingInstance(TypeSymbol type) => new[] { type }.Concat(EnclosingTypes(type)).FirstOrDefault(t => t.IsGenericInstance);

    /// <summary>The unit-level type a (possibly nested) type belongs to — through a helper that declares it too (LANG-990 (c), C-441).</summary>
    public static TypeSymbol Outermost(TypeSymbol type) => DeclaringTypes(type).LastOrDefault() ?? type;
}
