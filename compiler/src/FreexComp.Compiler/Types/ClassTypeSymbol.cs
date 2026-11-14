using System.Collections.Immutable;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Types;

/// <summary>
/// A class type (LANG-600…604, rtl-abi.md §7). A value is a reference: 8 bytes, the address of the instance (ABI-071), so
/// <see cref="TypeSymbol.Size"/> is 8. The instance has the VMT pointer at offset 0 and the fields of the root-most ancestor
/// first (ABI §7.1); <see cref="InstanceSize"/> follows ABI-070. A forward declaration (<c>TFoo = class;</c>, LANG-601) creates
/// the symbol with <see cref="IsForward"/> set; the full declaration in the same type section completes the same symbol.
/// </summary>
public sealed class ClassTypeSymbol : TypeSymbol
{
    internal ClassTypeSymbol(string name, Symbol? containingSymbol, ImmutableArray<Location> locations)
        : base(TypeKind.Class, name, size: 8, alignment: 8, containingSymbol, locations)
    {
    }

    /// <summary>The parent class; null for the root class (<c>TObject</c>, or any class bound without a <c>System</c>).</summary>
    public ClassTypeSymbol? Parent { get; internal set; }

    /// <summary>True until the full declaration has been bound (LANG-601).</summary>
    public bool IsForward { get; internal set; } = true;

    public bool IsAbstract { get; internal set; }

    public bool IsSealed { get; internal set; }

    /// <summary>The class's own instance fields in declaration order (offsets per ABI §7.1).</summary>
    public ImmutableArray<FieldSymbol> Fields { get; internal set; } = [];

    /// <summary>The class's own methods in declaration order.</summary>
    public ImmutableArray<MethodSymbol> Methods { get; internal set; } = [];

    /// <summary>ABI-070: end of the last field rounded up to 8; 8 for a class without fields (the VMT pointer).</summary>
    public long InstanceSize { get; internal set; } = 8;

    /// <summary>
    /// The virtual method table slots (ABI-073/074): the parent's slots with this class's overrides replacing them, then one
    /// slot per method this class introduces with <c>virtual</c>/<c>dynamic</c>.
    /// </summary>
    public ImmutableArray<MethodSymbol> VirtualSlots { get; internal set; } = [];

    public int VmtSlotCount => VirtualSlots.Length;

    /// <summary>
    /// The class's own <c>class var</c> fields (LANG-615, CMP-370): globals of the declaring unit named <c>U.TClass.Name</c>, shared
    /// with descendants.
    /// </summary>
    public ImmutableArray<VariableSymbol> ClassVariables { get; internal set; } = [];

    /// <summary>The class's own properties in declaration order (LANG-641…647).</summary>
    public ImmutableArray<PropertySymbol> Properties { get; internal set; } = [];

    /// <summary>The nearest default array property of the class or an ancestor (LANG-642); null when there is none.</summary>
    public PropertySymbol? DefaultProperty => SelfAndAncestors().SelectMany(c => c.Properties).FirstOrDefault(p => p.IsDefault);

    public override ImmutableArray<Symbol> Members => [.. Fields, .. ClassVariables, .. Methods, .. Properties];

    public override TypeSymbol? BaseType => Parent;

    private protected override TypeSymbol? InheritedFrom => Parent;

    /// <summary>The class and its ancestors, the class first.</summary>
    public IEnumerable<ClassTypeSymbol> SelfAndAncestors()
    {
        for (var type = this; type != null; type = type.Parent)
        {
            yield return type;
        }
    }

    /// <summary>ABI §7.2 <c>InitTable</c>: the managed fields declared in this class (finalised by <c>FreeInstance</c>, ABI-072).</summary>
    public IEnumerable<FieldSymbol> ManagedFields => Fields.Where(f => f.Type.IsManaged);

    /// <summary><c>type TFoo = class(TBar)</c> (CMP-731).</summary>
    public override string ToDisplayString() => Parent is null ? $"type {Name} = class" : $"type {Name} = class({Parent.Name})";

    private ClassRefTypeSymbol? _reference;

    /// <summary>The anonymous <c>class of</c> this class: the type of the class name used as a value and of <c>Self</c> in class methods (LANG-652).</summary>
    public ClassRefTypeSymbol Reference => _reference ??= new ClassRefTypeSymbol("class of " + Name, this, containingSymbol: null, []);
}

/// <summary>
/// <c>class of TFoo</c> (LANG-652): a value is the address of the VMT of <c>TFoo</c> or of a descendant (ABI-071), 8 bytes, <c>ptr</c>.
/// </summary>
public sealed class ClassRefTypeSymbol : TypeSymbol
{
    internal ClassRefTypeSymbol(string name, ClassTypeSymbol @class, Symbol? containingSymbol, ImmutableArray<Location> locations)
        : base(TypeKind.ClassRef, name, size: 8, alignment: 8, containingSymbol, locations)
    {
        Class = @class;
    }

    /// <summary>The class referred to (may still be forward-declared while the type section is bound).</summary>
    public ClassTypeSymbol Class { get; }

    /// <summary><c>type TFooClass = class of TFoo</c> (CMP-731).</summary>
    public override string ToDisplayString() => $"type {Name} = class of {Class.Name}";
}
