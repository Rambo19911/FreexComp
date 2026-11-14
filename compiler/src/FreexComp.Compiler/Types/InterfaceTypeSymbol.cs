using System.Collections.Immutable;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Types;

/// <summary>
/// An interface type (LANG-660, rtl-abi.md §7.3). A value is an interface reference: the address of the hidden interface field
/// inside the implementing object (ABI-090), 8 bytes, managed (LANG-662). The method table (IMT, ABI-091) holds the methods of
/// the root-most ancestor first: <see cref="AllMethods"/>; a method's IMT slot is its <see cref="MethodSymbol.VmtSlot"/>.
/// </summary>
public sealed class InterfaceTypeSymbol : TypeSymbol
{
    internal InterfaceTypeSymbol(string name, Symbol? containingSymbol, ImmutableArray<Location> locations)
        : base(TypeKind.Interface, name, size: 8, alignment: 8, containingSymbol, locations)
    {
    }

    /// <summary>The ancestor interface; null for the root (<c>IInterface</c>, or any interface bound without a <c>System</c>).</summary>
    public InterfaceTypeSymbol? Parent { get; internal set; }

    /// <summary>True until the full declaration has been bound (<c>IFoo = interface;</c>).</summary>
    public bool IsForward { get; internal set; } = true;

    /// <summary>The GUID in <c>TGUID</c> memory order (ABI-096), 16 bytes; null when the declaration has none.</summary>
    public ImmutableArray<byte>? Guid { get; internal set; }

    /// <summary>The interface's own methods in declaration order.</summary>
    public ImmutableArray<MethodSymbol> Methods { get; internal set; } = [];

    /// <summary>The interface's own properties (LANG-660: accessors are methods).</summary>
    public ImmutableArray<PropertySymbol> Properties { get; internal set; } = [];

    /// <summary>ABI-091: the IMT order — the ancestors' methods (root first), then the own ones.</summary>
    public ImmutableArray<MethodSymbol> AllMethods => Parent is null ? Methods : [.. Parent.AllMethods, .. Methods];

    /// <summary>The nearest default array property of the interface or an ancestor (LANG-642, C-101); null when there is none.</summary>
    public PropertySymbol? DefaultProperty => SelfAndAncestors().SelectMany(i => i.Properties).FirstOrDefault(p => p.IsDefault);

    public override bool IsManaged => true;

    public override ImmutableArray<Symbol> Members => [.. Methods, .. Properties];

    public override TypeSymbol? BaseType => Parent;

    private protected override TypeSymbol? InheritedFrom => Parent;

    /// <summary>The interface and its ancestors, the interface first.</summary>
    public IEnumerable<InterfaceTypeSymbol> SelfAndAncestors()
    {
        for (var type = this; type != null; type = type.Parent)
        {
            yield return type;
        }
    }

    /// <summary><c>type IFoo = interface(IBar)</c> (CMP-731).</summary>
    public override string ToDisplayString() => Parent is null ? $"type {Name} = interface" : $"type {Name} = interface({Parent.Name})";
}

/// <summary>
/// One interface listed in a class declaration (ABI §7.1): its hidden field at <see cref="Offset"/> and, per IMT slot
/// (<see cref="InterfaceTypeSymbol.AllMethods"/>), the class method implementing it (LANG-661).
/// </summary>
public sealed record ImplementedInterface(InterfaceTypeSymbol Interface, long Offset, ImmutableArray<MethodSymbol> Implementations);
