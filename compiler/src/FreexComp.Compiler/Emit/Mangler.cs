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

    /// <summary>The text of <see cref="Routine"/> (also <see cref="RoutineSymbol.LinkName"/>).</summary>
    public static string RoutineText(RoutineSymbol routine)
    {
        if (routine.ExplicitLinkName is { } explicitName)
        {
            return explicitName;
        }

        var prefix = routine.ContainingSymbol is RoutineSymbol outer ? RoutineText(outer) : routine.ContainingSymbol?.QualifiedName;
        var name = prefix is null ? routine.Name : prefix + "." + routine.Name;
        return routine.IsOverload ? name + "$" + Signature(routine.Parameters) : name;
    }

    /// <summary>Global variable or typed constant: <c>U.Name</c>; a typed constant of a routine: <c>&lt;routine link name&gt;.Name</c>.</summary>
    public static LinkName GlobalVariable(VariableSymbol variable) =>
        variable.ExplicitLinkName is { } explicitName ? new LinkName(explicitName, AlwaysQuote: false)
        : variable.ContainingSymbol is RoutineSymbol routine ? new(RoutineText(routine) + "." + variable.Name, AlwaysQuote: true)
        : new(variable.QualifiedName, AlwaysQuote: true);

    public static LinkName UnitInit(UnitSymbol unit) => new(unit.Name + ".$init", AlwaysQuote: true);

    public static LinkName UnitFinal(UnitSymbol unit) => new(unit.Name + ".$final", AlwaysQuote: true);

    /// <summary>Name of the frame record struct type of <paramref name="routine"/> (CMP-345).</summary>
    public static LinkName FrameType(RoutineSymbol routine) => new(RoutineText(routine) + ".$frame", AlwaysQuote: true);

    /// <summary>
    /// Struct type of a declared record (CMP-306): <c>U.TRec</c>; a record declared in a routine is qualified by the routine's
    /// link name (<c>U.Proc.TRec</c>).
    /// </summary>
    public static LinkName RecordType(RecordTypeSymbol record) => new(
        record.ContainingSymbol is RoutineSymbol routine ? RoutineText(routine) + "." + record.Name : record.QualifiedName, AlwaysQuote: true);

    /// <summary>The VMT constant of a class (rtl-abi.md §7.2, CMP-360): <c>U.TFoo.$vmt</c>.</summary>
    public static LinkName Vmt(ClassTypeSymbol type) => new(type.QualifiedName + ".$vmt", AlwaysQuote: true);

    /// <summary>The <c>InitTable</c> of a class (ABI §7.2 VMT+48): <c>U.TFoo.$inittable</c>.</summary>
    public static LinkName InitTable(ClassTypeSymbol type) => new(type.QualifiedName + ".$inittable", AlwaysQuote: true);

    /// <summary>A runtime helper of unit System (rtl-abi.md §9): <c>System._Name</c>.</summary>
    public static LinkName RuntimeHelper(string name) => new("System." + name, AlwaysQuote: true);

    public static LinkName StringLiteral(int index) => new(".str." + index, AlwaysQuote: false);

    /// <summary>
    /// Parameter type names joined with <c>,</c>, each prefixed per mode: <c>c:</c>, <c>v:</c>, <c>o:</c>; open arrays add
    /// <c>a</c> (<c>a:</c>, <c>ca:</c>, <c>va:</c>, <c>oa:</c>).
    /// </summary>
    public static string Signature(ImmutableArray<ParameterSymbol> parameters) =>
        string.Join(",", parameters.Select(p => ModePrefix(p.Mode, p.IsOpenArray) + TypeName(p.Type)));

    /// <summary>
    /// CMP-361: built-ins by canonical name (aliases are the same symbol, so its own name is canonical); declared types as
    /// <c>Unit.Name</c>. Anonymous types (structural names) arrive with their type constructors.
    /// </summary>
    public static string TypeName(TypeSymbol type)
    {
        if (type.ContainingSymbol is null)
        {
            return type.Name;
        }

        if (type.Name.Length == 0)
        {
            throw new NotSupportedException("Structural names of anonymous types (CMP-361) are not implemented yet.");
        }

        return type.QualifiedName;
    }

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
