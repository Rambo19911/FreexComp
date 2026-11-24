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
    internal InterfaceTypeSymbol(TargetInfo target, string name, Symbol? containingSymbol, ImmutableArray<Location> locations)
        : base(TypeKind.Interface, name, size: target.PointerSize, alignment: target.PointerAlign, containingSymbol, locations)
    {
        Target = target;
    }

    /// <summary>The compilation's target (reference size; CMP-1007).</summary>
    internal TargetInfo Target { get; }

    /// <summary>The ancestor interface; null for the root (<c>IInterface</c>, or any interface bound without a <c>System</c>).</summary>
    public InterfaceTypeSymbol? Parent { get; internal set; }

    /// <summary>True until the full declaration has been bound (<c>IFoo = interface;</c>).</summary>
    public bool IsForward { get; internal set; } = true;

    /// <summary>
    /// A dispatch interface (<c>dispinterface</c>, com-interfaces.md LANG-693): its members are called through
    /// <c>IDispatch.Invoke</c> by number; it has no method table of its own (<see cref="Methods"/> have no slot).
    /// </summary>
    public bool IsDispatchInterface { get; internal set; }

    /// <summary>
    /// LANG-696: a reference to an <c>IDispatch</c> — a dispatch interface, <c>System.IDispatch</c> or a descendant of it; such a
    /// value converts to every dispatch-interface type.
    /// </summary>
    internal bool IsDispatchReference => SelfAndAncestors().Any(i => i.IsDispatchInterface
        || string.Equals(i.Name, "IDispatch", StringComparison.OrdinalIgnoreCase)
            && i.ContainingSymbol is UnitSymbol { IsProgram: false } unit && string.Equals(unit.Name, "System", StringComparison.OrdinalIgnoreCase));

    /// <summary>The GUID in <c>TGUID</c> memory order (ABI-096), 16 bytes; null when the declaration has none.</summary>
    public ImmutableArray<byte>? Guid { get; internal set; }

    /// <summary>
    /// A <c>reference to procedure/function</c> type (LANG-720, ABI-097): a compiler-made interface whose only own method is
    /// <see cref="Invoke"/>; values are called with call syntax (LANG-724).
    /// </summary>
    public bool IsMethodReference { get; internal set; }

    /// <summary>
    /// The interface of one anonymous method expression (CMP-380): its value converts to every method reference type with a
    /// compatible signature (LANG-723).
    /// </summary>
    public bool IsAnonymousMethod { get; internal set; }

    /// <summary>
    /// CMP-1183 (C-308): the block frame that implements the interface of an anonymous method written inside a declaration scope
    /// with captured inline variables; null when the closure of the enclosing body does.
    /// </summary>
    internal Binding.ClosureInfo? BlockFrame { get; set; }

    /// <summary>The <c>Invoke</c> method of a method reference type (IMT slot 3, ABI-097).</summary>
    public MethodSymbol Invoke => Methods[0];

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
    public override string ToDisplayString() => IsDispatchInterface ? $"type {Name} = dispinterface"
        : Parent is null ? $"type {Name} = interface" : $"type {Name} = interface({Parent.Name})";
}

/// <summary>
/// One interface listed in a class declaration (ABI §7.1): its hidden field at <see cref="Offset"/> and, per IMT slot
/// (<see cref="InterfaceTypeSymbol.AllMethods"/>), the class method implementing it (LANG-661).
/// </summary>
public sealed record ImplementedInterface(InterfaceTypeSymbol Interface, long Offset, ImmutableArray<MethodSymbol> Implementations);
