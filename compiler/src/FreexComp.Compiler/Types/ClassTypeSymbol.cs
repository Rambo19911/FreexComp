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

    public override ImmutableArray<Symbol> Members => [.. Fields, .. Methods];

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
}
