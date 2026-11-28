using System.Collections.Immutable;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Types;

/// <summary>
/// A class or record helper (LANG-750…754, C-124): adds methods, properties, constants and class vars to
/// <see cref="ExtendedType"/>. It is no value type (E2751 as the type of anything, LANG-754); the active helper of a type is
/// the nearest one in scope (LANG-753).
/// </summary>
public sealed class HelperTypeSymbol : TypeSymbol
{
    internal HelperTypeSymbol(string name, bool isClassHelper, TypeSymbol extendedType, Symbol? containingSymbol, ImmutableArray<Location> locations)
        : base(TypeKind.Helper, name, size: 0, alignment: 1, containingSymbol, locations)
    {
        IsClassHelper = isClassHelper;
        ExtendedType = extendedType;
    }

    public bool IsClassHelper { get; }

    /// <summary>The helped type.</summary>
    public TypeSymbol ExtendedType { get; }

    /// <summary>
    /// CMP-1490 (c), (e) (C-443): the mutating methods of this record helper are known — always for a helper bound from source;
    /// for one read from a <c>.pcu</c>, when its entry carries <c>classified</c> (an entry without it: every instance method
    /// counts as mutating, and the entry is written again as it was read).
    /// </summary>
    internal bool IsClassified { get; set; } = true;

    /// <summary>
    /// CMP-1490 (d), (e) (C-439): the class methods of this class helper without <c>static</c> take the class reference as <c>Self</c>
    /// (LANG-991 (a)) — always for a helper bound from source; for one read from a <c>.pcu</c>, when its entry carries
    /// <c>classSelf</c> (an entry without it: every class method is static, and the entry is written again as it was read).
    /// </summary>
    internal bool HasClassSelf { get; set; } = true;

    /// <summary><c>helper(TAncestorHelper)</c>: the helper whose members this one inherits (LANG-753); null otherwise.</summary>
    public HelperTypeSymbol? Parent { get; internal set; }

    public ImmutableArray<MethodSymbol> Methods { get; internal set; } = [];

    public ImmutableArray<PropertySymbol> Properties { get; internal set; } = [];

    public ImmutableArray<VariableSymbol> ClassVariables { get; internal set; } = [];

    /// <summary>The class constructor (LANG-995, C-441), or null; never a member: no lookup finds it (LANG-787).</summary>
    internal MethodSymbol? ClassConstructor { get; set; }

    /// <summary>The class destructor (LANG-995, C-441), or null; never a member: no lookup finds it (LANG-787).</summary>
    internal MethodSymbol? ClassDestructor { get; set; }

    /// <summary>The helper's constants and nested types (LANG-750, 770).</summary>
    public ImmutableArray<Symbol> NestedMembers { get; internal set; } = [];

    /// <summary>Nested type aliases (<c>type TA = TOther;</c>, LANG-770, C-311): the alias name, the type it names and its section's visibility (LANG-774, C-359).</summary>
    internal ImmutableArray<(string Name, TypeSymbol Target, Visibility Visibility)> NestedAliases { get; set; } = [];

    public override ImmutableArray<Symbol> Members => [.. ClassVariables, .. Methods, .. Properties, .. NestedMembers];

    public override TypeSymbol? BaseType => Parent;

    private protected override TypeSymbol? InheritedFrom => Parent;

    /// <summary>The helper and the helpers it inherits from, the helper first.</summary>
    public IEnumerable<HelperTypeSymbol> SelfAndAncestors()
    {
        for (var helper = this; helper != null; helper = helper.Parent)
        {
            yield return helper;
        }
    }

    /// <summary><c>type TFooHelper = class helper for TFoo</c>, with an ancestor <c>class helper(TBaseHelper) for TFoo</c> (CMP-731, CMP-1494).</summary>
    public override string ToDisplayString() =>
        $"type {Name} = {(IsClassHelper ? "class" : "record")} helper{(Parent is { } parent ? "(" + parent.Name + ")" : "")} for {ExtendedType.Name}";
}
