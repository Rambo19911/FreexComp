using System.Collections.Immutable;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Emit;

/// <summary>
/// A global LLVM name. Names derived from Pascal declarations are always written as quoted identifiers (CMP-360);
/// import names, <c>public name</c>s and compiler-internal names (<c>.str.N</c>) are quoted only when they are not
/// valid plain LLVM identifiers. Quoting itself is the writer's business.
/// </summary>
internal readonly record struct LinkName(string Text, bool AlwaysQuote);

/// <summary>Link names of codegen.md §12 (CMP-360…363). Always the declared casing (CMP-363).</summary>
internal static class Mangler
{
    public static LinkName Routine(RoutineSymbol routine) =>
        routine.ExplicitLinkName is { } explicitName ? new LinkName(explicitName, AlwaysQuote: false) : new LinkName(RoutineText(routine), AlwaysQuote: true);

    /// <summary>
    /// CMP-1011: the link name of an <c>external</c> import (or an export) under the decoration the target gives the routine's
    /// calling convention. <see cref="SymbolDecoration.None"/> is the name itself; the decorated forms belong to the targets that
    /// use them and are defined with those targets.
    /// </summary>
    public static LinkName Decorated(LinkName name, RoutineSymbol routine, TargetInfo target) =>
        target.CallingConventions[routine.CallingConvention].Decoration switch
        {
            SymbolDecoration.None => name,
            var decoration => throw new NotSupportedException($"Symbol decoration {decoration} is not defined for target '{target.Id}'."),
        };

    /// <summary>The text of <see cref="Routine"/> (also <see cref="RoutineSymbol.LinkName"/>).</summary>
    public static string RoutineText(RoutineSymbol routine)
    {
        if (routine.ExplicitLinkName is { } explicitName)
        {
            return explicitName;
        }

        var prefix = routine.ContainingSymbol is { } container ? Qualified(container) : null;     // a nested routine: its parent's link name
        if (routine is MethodSymbol { MethodKind: MethodKind.ClassConstructor or MethodKind.ClassDestructor } classConstructor)
        {
            // CMP-1395 (C-374): fixed suffixes, independent of the declared name (an ordinary Create keeps U.TA.Create)
            return prefix + (classConstructor.MethodKind == MethodKind.ClassConstructor ? ".$cctor" : ".$cdtor");
        }

        var name = (prefix is null ? routine.Name : prefix + "." + routine.Name) + routine.GenericSuffix;
        // C-123: conversion operators may differ in their result only (Implicit to Integer and to Double), so operators add it
        return routine is MethodSymbol { OperatorName: not null, ReturnType: { } result } ? name + "$" + Signature(routine.Parameters) + "$" + TypeName(result)
            : routine.IsOverload ? name + "$" + Signature(routine.Parameters) : name;
    }

    /// <summary>Global variable or typed constant: <c>U.Name</c>; a typed constant of a routine: <c>&lt;routine link name&gt;.Name</c>.</summary>
    public static LinkName GlobalVariable(VariableSymbol variable) =>
        variable.ExplicitLinkName is { } explicitName ? new LinkName(explicitName, AlwaysQuote: false)
        : new(Qualified(variable), AlwaysQuote: true);

    /// <summary>CMP-1209: call descriptor number <paramref name="number"/> (from 1) of a module: <c>U.$disp.n</c>.</summary>
    public static LinkName DispDescriptor(string unitName, int number) =>
        new(unitName + ".$disp." + number.ToString(System.Globalization.CultureInfo.InvariantCulture), AlwaysQuote: true);

    public static LinkName UnitInit(UnitSymbol unit) => new(unit.Name + ".$init", AlwaysQuote: true);

    public static LinkName UnitFinal(UnitSymbol unit) => new(unit.Name + ".$final", AlwaysQuote: true);

    /// <summary>The threadvar descriptor of a unit or program (ABI-180): <c>U.$tls</c>.</summary>
    public static LinkName TlsDescriptor(UnitSymbol unit) => new(unit.Name + ".$tls", AlwaysQuote: true);

    /// <summary>CMP-1397 (C-374): the done flags of the module's class constructor items, <c>U.$ccdone</c> (<c>[n x i8]</c>).</summary>
    public static LinkName ClassConstructorsDone(string unitName) => new(unitName + ".$ccdone", AlwaysQuote: true);

    /// <summary>CMP-415: the unit's <c>{$FORM}</c> table and its blob number <paramref name="index"/> (0-based, directive order).</summary>
    public static LinkName FormTable(UnitSymbol unit) => new(unit.Name + ".$forms", AlwaysQuote: true);

    public static LinkName FormBlob(UnitSymbol unit, int index) => new(unit.Name + ".$form." + index, AlwaysQuote: true);

    /// <summary>Name of the frame record struct type of <paramref name="routine"/> (CMP-345).</summary>
    public static LinkName FrameType(RoutineSymbol routine) => new(RoutineText(routine) + ".$frame", AlwaysQuote: true);

    /// <summary>
    /// Struct type of a declared record (CMP-306): <c>U.TRec</c>; a record declared in a routine is qualified by the routine's
    /// link name (<c>U.Proc.TRec</c>, <c>U.P$Integer.TRec</c> in an overload), also when nested in a routine's type (C-368).
    /// </summary>
    public static LinkName RecordType(RecordTypeSymbol record) => new(Qualified(record), AlwaysQuote: true);

    /// <summary>The VMT constant of a class (rtl-abi.md §7.2, CMP-360): <c>U.TFoo.$vmt</c>.</summary>
    public static LinkName Vmt(ClassTypeSymbol type) => new(Qualified(type) + ".$vmt", AlwaysQuote: true);

    /// <summary>The <c>InitTable</c> of a class (ABI §7.2 VMT+48): <c>U.TFoo.$inittable</c>.</summary>
    public static LinkName InitTable(ClassTypeSymbol type) => new(Qualified(type) + ".$inittable", AlwaysQuote: true);

    /// <summary>The IMT of a class for one listed interface (ABI-091, CMP-371): <c>U.TFoo.$imt.IU.IBar</c>.</summary>
    public static LinkName Imt(ClassTypeSymbol type, InterfaceTypeSymbol intf) => new(Qualified(type) + ".$imt." + Qualified(intf), AlwaysQuote: true);

    /// <summary>
    /// The thunk of IMT slot <paramref name="slot"/> (CMP-372, codegen.md §12): <c>U.TFoo.$thunk.IU.IBar.Method</c>; an overloaded
    /// interface method adds <c>$</c> and its parameter signature like a routine (C-098).
    /// </summary>
    public static LinkName Thunk(ClassTypeSymbol type, InterfaceTypeSymbol intf, MethodSymbol method) =>
        new(Qualified(type) + ".$thunk." + Qualified(intf) + "." + method.Name + (method.IsOverload ? "$" + Signature(method.Parameters) : ""), AlwaysQuote: true);

    /// <summary>The <c>MsgTable</c> of a class (ABI §7.2, VMT+40, C-113): <c>U.TFoo.$msgtable</c>.</summary>
    public static LinkName MsgTable(ClassTypeSymbol type) => new(Qualified(type) + ".$msgtable", AlwaysQuote: true);

    /// <summary>The <c>IntfTable</c> of a class (ABI-095, VMT+32): <c>U.TFoo.$intftable</c>.</summary>
    public static LinkName IntfTable(ClassTypeSymbol type) => new(Qualified(type) + ".$intftable", AlwaysQuote: true);

    /// <summary>The GUID constant of an interface (CMP-373): <c>U.IBar.$guid</c>.</summary>
    public static LinkName Guid(InterfaceTypeSymbol intf) => new(Qualified(intf) + ".$guid", AlwaysQuote: true);

    /// <summary>A runtime helper of unit System (rtl-abi.md §9): <c>System._Name</c>.</summary>
    public static LinkName RuntimeHelper(string name) => new("System." + name, AlwaysQuote: true);

    public static LinkName StringLiteral(int index) => new(".str." + index, AlwaysQuote: false);

    /// <summary>
    /// Parameter type names joined with <c>,</c>, each prefixed per mode: <c>c:</c>, <c>v:</c>, <c>o:</c>; open arrays add
    /// <c>a</c> (<c>a:</c>, <c>ca:</c>, <c>va:</c>, <c>oa:</c>).
    /// </summary>
    public static string Signature(ImmutableArray<ParameterSymbol> parameters) =>
        string.Join(",", parameters.Select(p => ModePrefix(p.Mode, p.IsOpenArray) + p.Type switch
        {
            OpenArrayTypeSymbol { IsArrayOfConst: true } => "const",                    // C-107: ca:const
            OpenArrayTypeSymbol open => TypeName(open.ElementType),                     // CMP-346: va:Byte
            var type => TypeName(type),
        }));

    /// <summary>
    /// CMP-361: built-ins by canonical name (aliases are the same symbol, so its own name is canonical); declared types as
    /// <c>Unit.Name</c>. Anonymous types (structural names) arrive with their type constructors. C-367: a named pointer has no
    /// containing symbol, so it is named after the unit, routine or type that declared it (<c>U.PFoo</c>, <c>U.TOuter.PItem</c>).
    /// </summary>
    public static string TypeName(TypeSymbol type)
    {
        if (type is PointerTypeSymbol { DeclaredIn: { } declaredIn } && type.Name.Length > 0 && !type.Name.StartsWith('^'))
        {
            return Qualified(declaredIn) + "." + type.Name;
        }

        if (type.ContainingSymbol is null)
        {
            return type.Name;
        }

        if (type.Name.Length == 0)
        {
            throw new NotSupportedException("Structural names of anonymous types (CMP-361) are not implemented yet.");
        }

        return Qualified(type);
    }

    /// <summary>
    /// <see cref="Symbol.QualifiedName"/> for link names: a routine is its link name, so a symbol declared in a routine is qualified
    /// by the routine's link name (C-368: overload-distinct, <c>U.P$Integer.TLocal</c>), and so is everything nested in it.
    /// </summary>
    public static string Qualified(Symbol symbol) =>
        symbol is RoutineSymbol routine ? RoutineText(routine)
        : symbol.QualifiedNameOverride ?? (symbol.ContainingSymbol is { } container ? Qualified(container) + "." + symbol.Name : symbol.Name);

    private static string ModePrefix(ParameterMode mode, bool isOpenArray)
    {
        var letters = mode switch
        {
            ParameterMode.Const => "c",
            ParameterMode.Var => "v",
            ParameterMode.Out => "o",
            _ => "",
        } + (isOpenArray ? "a" : "");
        return letters.Length == 0 ? "" : letters + ":";
    }
}
