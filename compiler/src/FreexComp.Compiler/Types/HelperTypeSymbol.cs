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

    /// <summary><c>helper(TAncestorHelper)</c>: the helper whose members this one inherits (LANG-753); null otherwise.</summary>
    public HelperTypeSymbol? Parent { get; internal set; }

    public ImmutableArray<MethodSymbol> Methods { get; internal set; } = [];

    public ImmutableArray<PropertySymbol> Properties { get; internal set; } = [];

    public ImmutableArray<VariableSymbol> ClassVariables { get; internal set; } = [];

    /// <summary>The helper's constants and nested types (LANG-750, 770).</summary>
    public ImmutableArray<Symbol> NestedMembers { get; internal set; } = [];

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

    /// <summary><c>type TFooHelper = class helper for TFoo</c> (CMP-731).</summary>
    public override string ToDisplayString() => $"type {Name} = {(IsClassHelper ? "class" : "record")} helper for {ExtendedType.Name}";
}
