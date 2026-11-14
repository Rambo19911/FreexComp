using System.Collections.Immutable;
using System.Numerics;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Lowering;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Emit;

/// <summary>
/// Lowered trees of one unit → IR through <see cref="IIrWriter"/> (architecture.md §3, CMP-125). This part covers the module
/// skeleton, scalar globals (CMP-341), routines with parameters and locals (CMP-310), integer, Boolean and real
/// expressions (CMP-312, 315…317), conversions (CMP-305, 313), the always-on division tests of CMP-312, the overflow and
/// range checks the <see cref="CheckRewriter"/> marked (CMP-312 <c>{$Q+}</c>, CMP-314), control flow
/// (CMP-318…322), calls (ABI-080…082), nested routines with frame records and static links (CMP-345), UnicodeString
/// literals (CMP-320), variables of other units (<c>System.Output</c>, CMP-410), unit <c>$init</c>/<c>$final</c> (CMP-350) and
/// the program's <c>$units</c>, <c>$main</c> and <c>_start</c> (CMP-355; plain calls until the EH milestone adds the handler).
/// </summary>
internal static class IrEmitter
{
    /// <summary><c>attributes #0</c>: every Pascal routine (CMP-302, the <c>-O0</c> form until options reach the emitter).</summary>
    public const int RoutineGroup = 0;

    /// <summary><c>attributes #1</c>: noreturn helpers, on declarations and call sites (CMP-302, 312).</summary>
    public const int NoReturnGroup = 1;

    /// <summary><c>attributes #2</c>: routines whose link name is a C memory routine (CMP-302, 610).</summary>
    public const int NoBuiltinsGroup = 2;

    /// <summary>
    /// CMP-610: link names that get <see cref="NoBuiltinsGroup"/> so the optimiser cannot turn their loops into calls to themselves;
    /// <c>strlen</c>/<c>wcslen</c> because loop-idiom recognition at <c>-O2</c> turns a PChar scan into them (C-056).
    /// </summary>
    private static readonly HashSet<string> MemoryRoutineNames = new(StringComparer.Ordinal) { "memcpy", "memmove", "memset", "memcmp", "strlen", "wcslen" };

    /// <summary>CMP-611: the x64 stack probe, appended verbatim to unit <c>System</c> (ADR 001; never imported, never in <c>System.pas</c>).</summary>
    private static readonly ImmutableArray<string> StackProbe =
    [
        "\t.text", "\t.globl\t__chkstk", "\t.p2align\t4", "__chkstk:", "\tpushq\t%rcx", "\tpushq\t%rax", "\tcmpq\t$4096, %rax",
        "\tleaq\t24(%rsp), %rcx", "\tjb\t1f", "2:", "\tsubq\t$4096, %rcx", "\ttestq\t%rcx, (%rcx)", "\tsubq\t$4096, %rax",
        "\tcmpq\t$4096, %rax", "\tja\t2b", "1:", "\tsubq\t%rax, %rcx", "\ttestq\t%rcx, (%rcx)", "\tpopq\t%rax", "\tpopq\t%rcx", "\tretq",
    ];

    internal static int AttributeGroupOf(RoutineSymbol routine) =>
        MemoryRoutineNames.Contains(Mangler.Routine(routine).Text) ? NoBuiltinsGroup : RoutineGroup;

    /// <param name="unitTable">
    /// Program only: the <c>$units</c> entries in initialisation order (CMP-355); default = empty table.
    /// </param>
    public static void EmitUnit(LoweredUnit unit, IIrWriter writer, ImmutableArray<UnitTableEntry> unitTable = default)
    {
        var path = unit.Unit.Locations.IsEmpty ? unit.Unit.Name + ".pas" : unit.Unit.Locations[0].Path;
        writer.BeginModule(unit.Unit.Name, Path.GetFileName(path));
        if (string.Equals(unit.Unit.Name, "System", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var line in StackProbe)
            {
                writer.AppendModuleAsm(line);
            }
        }

        var interfaceMembers = unit.Unit.InterfaceMembers.Select(m => m is ConstantSymbol { Storage: { } storage } ? storage : m).ToHashSet();
        var classes = Binding.DeclarationBinder.ClassesOf(unit.Unit).ToImmutableArray();
        var module = new ModuleState(writer) { UnitName = unit.Unit.Name, OwnClasses = [.. classes] };
        foreach (var body in unit.Routines)
        {
            module.DefinedRoutines.Add(Mangler.Routine(body.Routine!).Text);
        }

        // globals are defined after the functions (CMP-301: string literals, found while emitting, come first)
        // typed constants are read-only globals (LANG-344); those of routines follow the unit's own
        var variables = unit.Unit.InterfaceMembers.Concat(unit.Unit.ImplementationMembers)
            .Select(m => m is ConstantSymbol { Storage: { } storage } ? storage : m).OfType<VariableSymbol>().Concat(unit.Statics).ToImmutableArray();
        foreach (var variable in variables)
        {
            module.Globals[variable] = new IrGlobal(Mangler.GlobalVariable(variable));
        }

        // CMP-345 frame record types, in definition order (CMP-301: named struct types first)
        foreach (var body in unit.Routines)
        {
            if (body.Frame is { } frame)
            {
                var (structType, indexes) = TypeLayout.Struct([new TypeLayout(8, 8, IrType.Ptr), .. frame.Variables.Select(FieldLayout)]);
                var type = writer.DeclareStruct(Mangler.FrameType(body.Routine!), structType);
                module.Frames[body.Routine!] = new FrameRecord(type, frame.Variables, indexes);
            }
        }

        // CMP-301: definition order, each nested routine directly after its parent
        foreach (var body in InEmissionOrder(unit.Routines))
        {
            var routine = body.Routine!;
            var linkage = routine.ExplicitLinkName is not null || (!routine.IsNested && interfaceMembers.Contains(routine))
                || routine is MethodSymbol method && interfaceMembers.Contains(method.ContainingType)
                ? IrLinkage.External
                : IrLinkage.Internal;
            new FunctionEmitter(module, body, linkage).Emit();
            module.DeclarePending();
        }

        var units = unitTable.IsDefault ? [] : unitTable;
        if (unit.Unit.IsProgram)
        {
            EmitMain(module, unit);
            if (unit.Finalization is { } final)
            {
                // C-183: the program's managed globals, released through its own (last) unit table entry
                module.DefinedRoutines.Add(Mangler.UnitFinal(unit.Unit).Text);
                new FunctionEmitter(module, final, IrLinkage.Internal, Mangler.UnitFinal(unit.Unit)).Emit();
                module.DeclarePending();
            }

            EmitStart(module, units.Length);
        }
        else
        {
            // CMP-350: every unit defines both, possibly empty
            new FunctionEmitter(module, unit.Initialization ?? EmptyBody(), IrLinkage.External, Mangler.UnitInit(unit.Unit)).Emit();
            module.DeclarePending();
            new FunctionEmitter(module, unit.Finalization ?? EmptyBody(), IrLinkage.External, Mangler.UnitFinal(unit.Unit)).Emit();
            module.DeclarePending();
        }

        // CMP-366: the VMTs are built before the literals are written (class names are UnicodeString literals)
        var vmts = classes.Select(c => (Class: c, Constant: Vmt(module, c))).ToImmutableArray();
        module.DeclarePending();

        foreach (var (name, literal) in module.StringLiterals)
        {
            writer.DefineGlobal(new IrGlobalDefinition(name, IrLinkage.Private, IsConstant: true, literal, Alignment: 16, UnnamedAddr: true));
        }

        if (module.UsesEmptyTerminator)
        {
            // CMP-320: the shared empty PChar literal (also PAnsiChar's: two zero bytes)
            writer.DefineGlobal(new IrGlobalDefinition(ModuleState.EmptyTerminatorName, IrLinkage.Private, IsConstant: true,
                new IrZeroConstant(new ArrayIrType(1, IrType.I16)), Alignment: 2, UnnamedAddr: true));
        }

        foreach (var (name, constant, alignment) in module.AggregateConstants)
        {
            writer.DefineGlobal(new IrGlobalDefinition(name, IrLinkage.Private, IsConstant: true, constant, alignment, UnnamedAddr: true));
        }

        foreach (var (name, block) in module.TypeInfos)
        {
            writer.DefineGlobal(new IrGlobalDefinition(name, IrLinkage.LinkOnceOdr, IsConstant: true, block, Alignment: 8));     // CMP-405, CMP-365
        }

        foreach (var (type, constant) in vmts)
        {
            writer.DefineGlobal(new IrGlobalDefinition(Mangler.Vmt(type), interfaceMembers.Contains(type) ? IrLinkage.External : IrLinkage.Internal,
                IsConstant: true, constant, Alignment: 8));
        }

        if (unit.Unit.IsProgram)
        {
            DefineUnitTable(module, units);
        }

        foreach (var variable in variables)
        {
            var layout = TypeLayout.Allocated(variable.Type);
            var initializer = variable.Initializer is { } value && variable.Type.Size > 0 ? StorageConstant(module, value, variable.Type) : new IrZeroConstant(layout.StorageType);
            writer.DefineGlobal(new IrGlobalDefinition(Mangler.GlobalVariable(variable),
                interfaceMembers.Contains(variable) || variable.ExplicitLinkName is not null ? IrLinkage.External : IrLinkage.Internal,
                IsConstant: variable.IsReadOnly, initializer, layout.Alignment));
        }

        module.DeclarePending();                                        // routines whose address a typed constant holds
        foreach (var (name, type) in module.ExternalVariables)
        {
            var layout = TypeLayout.Of(type);
            writer.DeclareGlobal(name, layout.StorageType, layout.Alignment);
        }

        foreach (var name in module.ExternalVmts)
        {
            writer.DeclareGlobal(name, IrType.Ptr, 8);                          // another unit's VMT: only its address is used
        }

        writer.DefineAttributeGroup(RoutineGroup, [IrAttribute.Enum("uwtable"), IrAttribute.String("frame-pointer", "all")]);
        if (module.UsesNoReturn)
        {
            writer.DefineAttributeGroup(NoReturnGroup, [IrAttribute.Enum("noreturn"), IrAttribute.Enum("uwtable")]);
        }

        if (unit.Routines.Any(r => AttributeGroupOf(r.Routine!) == NoBuiltinsGroup))
        {
            writer.DefineAttributeGroup(NoBuiltinsGroup, [IrAttribute.Enum("uwtable"), IrAttribute.String("no-builtins", null)]);
        }

        writer.EndModule();
    }

    private static readonly LinkName UnitsName = new("$units", AlwaysQuote: true);
    private static readonly LinkName MainName = new("$main", AlwaysQuote: true);
    private static readonly LinkName StartName = new("_start", AlwaysQuote: false);
    private static readonly StructIrType UnitEntryType = new([IrType.Ptr, IrType.Ptr]);

    private static LoweredBody EmptyBody() => new(null, [], new LBlock([]));

    /// <summary>CMP-355: <c>internal void $main()</c> = <c>_InitUnits</c>, then the main block.</summary>
    private static void EmitMain(ModuleState module, LoweredUnit unit)
    {
        var main = unit.Bodies.IsEmpty ? EmptyBody() : unit.Bodies[0];
        var initUnits = new LCallStatement(new LHelperCallee(new RuntimeHelper("_InitUnits", [], ReturnType: null, IsNoReturn: false)), []);
        new FunctionEmitter(module, main with { Body = new LBlock([initUnits, main.Body]) }, IrLinkage.Internal, MainName).Emit();
        module.DeclarePending();
    }

    /// <summary>
    /// CMP-355 without the EH part (until C-079+): <c>_Startup($units, n)</c>, <c>$main()</c>, <c>_Halt0()</c>. The PE entry
    /// point (<c>/entry:_start</c>), called by the OS like an ordinary function.
    /// </summary>
    private static void EmitStart(ModuleState module, int unitCount)
    {
        var w = module.Writer;
        var startup = module.Function(new IrFunctionDeclaration(Mangler.RuntimeHelper("_Startup"), IrType.Void, [IrType.Ptr, IrType.I64]));
        var halt0 = module.NoReturnHelper("_Halt0");
        w.BeginFunction(new IrFunctionDefinition(StartName, IrLinkage.External, IrType.Void, [], RoutineGroup));
        w.AppendBlock(new IrBlock("entry"));
        w.Call(null, IrType.Void, startup, [new IrGlobal(UnitsName), IrIntConstant.I64(unitCount)]);
        w.Call(null, IrType.Void, new IrGlobal(MainName), []);
        w.Call(null, IrType.Void, halt0, [], NoReturnGroup);
        w.Unreachable();
        w.EndFunction();
        module.DeclarePending();
    }

    /// <summary>CMP-355: <c>@"$units" = internal constant [n x { ptr, ptr }]</c> of <c>{ $init, $final }</c> (or nulls), align 8.</summary>
    private static void DefineUnitTable(ModuleState module, ImmutableArray<UnitTableEntry> units)
    {
        var entries = units.Select(u => (IrConstant)new IrStructConstant(UnitEntryType,
            [u.HasInit ? module.Function(new IrFunctionDeclaration(Mangler.UnitInit(u.Unit), IrType.Void, [])) : new IrNullConstant(),
             u.HasFinal ? module.Function(new IrFunctionDeclaration(Mangler.UnitFinal(u.Unit), IrType.Void, [])) : new IrNullConstant()])).ToImmutableArray();
        module.DeclarePending();
        module.Writer.DefineGlobal(new IrGlobalDefinition(UnitsName, IrLinkage.Internal, IsConstant: true,
            new IrArrayConstant(new ArrayIrType(units.Length, UnitEntryType), entries), Alignment: 8));
    }

    /// <summary>
    /// CMP-366 / rtl-abi.md §7.2: <c>{ Parent, InstanceSize, ClassName, TypeInfo, IntfTable, MsgTable, InitTable, Reserved, [N x ptr]
    /// slots }</c>; abstract slots point to <c>System._AbstractError</c>. TypeInfo is null until class RTTI is emitted (M2.5).
    /// </summary>
    private static IrConstant Vmt(ModuleState module, ClassTypeSymbol type)
    {
        var managed = type.ManagedFields.ToImmutableArray();
        IrConstant initTable = new IrNullConstant();
        if (!managed.IsEmpty)
        {
            // ABI §7.2: { i64 Count, [Count x { ptr TypeInfo, i64 Offset }] }
            var entry = new StructIrType([IrType.Ptr, IrType.I64]);
            var entries = new ArrayIrType(managed.Length, entry);
            var table = new IrStructConstant(new StructIrType([IrType.I64, entries]), [IrIntConstant.I64(managed.Length),
                new IrArrayConstant(entries, [.. managed.Select(f => (IrConstant)new IrStructConstant(entry, [module.TypeInfo(f.Type), IrIntConstant.I64(f.Offset)]))])]);
            module.AggregateConstants.Add((Mangler.InitTable(type), table, 8));
            initTable = new IrGlobal(Mangler.InitTable(type));
        }

        var slots = new ArrayIrType(type.VirtualSlots.Length, IrType.Ptr);
        IrConstant Slot(MethodSymbol method) => method.IsAbstract ? module.NoReturnHelper("_AbstractError") : module.Routine(method);
        var vmtType = new StructIrType([IrType.Ptr, IrType.I64, IrType.Ptr, IrType.Ptr, IrType.Ptr, IrType.Ptr, IrType.Ptr, IrType.Ptr, slots]);
        return new IrStructConstant(vmtType,
        [
            type.Parent is { } parent ? module.Vmt(parent) : new IrNullConstant(),
            IrIntConstant.I64(type.InstanceSize),
            module.StringLiteral(type.Name),
            new IrNullConstant(),
            new IrNullConstant(),
            new IrNullConstant(),
            initTable,
            new IrNullConstant(),
            type.VirtualSlots.IsEmpty ? new IrZeroConstant(slots) : new IrArrayConstant(slots, [.. type.VirtualSlots.Select(Slot)]),
        ]);
    }

    private static IEnumerable<LoweredBody> InEmissionOrder(ImmutableArray<LoweredBody> routines)
    {
        IEnumerable<LoweredBody> WithNested(LoweredBody body) =>
            routines.Where(r => ReferenceEquals(r.Routine!.ContainingSymbol, body.Routine)).SelectMany(WithNested).Prepend(body);

        return routines.Where(r => !r.Routine!.IsNested).SelectMany(WithNested);
    }

    /// <summary>
    /// The IR parameter types of <paramref name="routine"/> (ABI-080, 082): static link, hidden <c>ptr Result</c> (ABI-081),
    /// then per parameter its value, an <c>iN</c> holding a small aggregate, or <c>ptr</c>.
    /// </summary>
    internal static ImmutableArray<IrType> ParameterTypes(RoutineSymbol routine) =>
        [.. (routine.IsNested ? [IrType.Ptr] : Array.Empty<IrType>()),
         .. routine.ImplicitParameters.Select(ParameterType),
         .. (HasHiddenResult(routine) ? [IrType.Ptr] : Array.Empty<IrType>()),
         .. routine.Parameters.Select(ParameterType)];

    /// <summary>The IR type of one declared parameter (ABI-080).</summary>
    internal static IrType ParameterType(ParameterSymbol parameter) => IsPassedByAddress(parameter) ? IrType.Ptr : ScalarStorage(parameter.Type);

    /// <summary>ABI-081: the value, an <c>iN</c> for an aggregate of size 1, 2, 4 or 8, <c>void</c> with a hidden result.</summary>
    internal static IrType ReturnType(RoutineSymbol routine) =>
        routine.ReturnType is { } type && !HasHiddenResult(routine) ? ScalarStorage(type) : IrType.Void;

    /// <summary>ABI-080/081: an aggregate (record, static array, big set) of size 1, 2, 4 or 8 travels as an integer of its size.</summary>
    internal static bool IsPassedAsInteger(TypeSymbol type) => Lowerer.IsAggregate(type) && type.Size is 1 or 2 or 4 or 8;

    /// <summary>
    /// ABI-080: the parameter arrives as a <c>ptr</c> — <c>var</c>/<c>out</c>, and value/<c>const</c> aggregates of other sizes
    /// (value: the caller's copy, which the callee owns; <c>const</c>: the original).
    /// </summary>
    internal static bool IsPassedByAddress(ParameterSymbol parameter) =>
        parameter.IsByReference || Lowerer.IsAggregate(parameter.Type) && !IsPassedAsInteger(parameter.Type);

    /// <summary>
    /// ABI-081: the result is an aggregate not of size 1, 2, 4 or 8, or of a managed type (ABI-032), written through a hidden
    /// <c>ptr Result</c> parameter.
    /// </summary>
    internal static bool HasHiddenResult(RoutineSymbol routine) =>
        routine.ReturnType is { } type && (type.IsManaged || Lowerer.IsAggregate(type) && !IsPassedAsInteger(type));

    /// <summary>
    /// The variable lives at an address the routine receives (a by-address parameter, the hidden <c>Result</c>): a frame
    /// record stores that pointer (CMP-345).
    /// </summary>
    internal static bool HoldsAddress(Symbol variable) => variable switch
    {
        ParameterSymbol parameter => IsPassedByAddress(parameter),
        VariableSymbol { ContainingSymbol: RoutineSymbol routine } local => ReferenceEquals(routine.ResultVariable, local) && HasHiddenResult(routine),
        _ => false,
    };

    /// <summary>The storage type of a value parameter or result; small aggregates as <c>iN</c> (ABI-080/081).</summary>
    private static IrType ScalarStorage(TypeSymbol type) =>
        IsPassedAsInteger(type) ? IrType.Int((int)type.Size * 8) : TypeLayout.Of(type).StorageType;

    /// <summary>Layout of a frame field: the variable's storage, or a <c>ptr</c> when it lives at a received address (CMP-345).</summary>
    internal static TypeLayout FieldLayout(Symbol variable) => variable switch
    {
        _ when HoldsAddress(variable) => new TypeLayout(8, 8, IrType.Ptr),
        ParameterSymbol parameter => TypeLayout.Allocated(parameter.Type),
        VariableSymbol local => TypeLayout.Allocated(local.Type),
        _ => throw new InvalidOperationException($"'{variable.Name}' cannot live in a frame record."),
    };

    /// <summary>The SSA form of a value of <paramref name="type"/>: <c>i1</c> for the Boolean family (CMP-305), else the storage type.</summary>
    internal static IrType ValueType(TypeSymbol type) => type is BooleanTypeSymbol ? IrType.I1 : TypeLayout.Of(type).StorageType;

    /// <summary>A constant in SSA form (<see cref="ValueType"/>).</summary>
    internal static IrConstant ValueConstant(ConstantValue constant, TypeSymbol type) => (type, constant.Value) switch
    {
        (BooleanTypeSymbol, bool b) => IrIntConstant.I1(b),
        (BooleanTypeSymbol, BigInteger i) => IrIntConstant.I1(!i.IsZero),
        (RealTypeSymbol { RealKind: not RealKind.Currency }, BigInteger i) => new IrFloatConstant((FloatingIrType)ValueType(type), (double)i),
        (RealTypeSymbol { RealKind: not RealKind.Currency }, double d) => new IrFloatConstant((FloatingIrType)ValueType(type), d),
        (PointerTypeSymbol or ProceduralTypeSymbol or ClassTypeSymbol, null) => new IrNullConstant(),
        (IntegerTypeSymbol or CharTypeSymbol or EnumTypeSymbol, BigInteger i) => new IrIntConstant((IntegerIrType)ValueType(type), i),
        (SetTypeSymbol { IsSmall: true }, BigInteger mask) => new IrIntConstant((IntegerIrType)ValueType(type), mask),       // CMP-335
        (SetTypeSymbol, BigInteger mask) => BigSetConstant(mask, type.Size),                                                  // CMP-336
        _ => throw new NotSupportedException($"Constant {constant} of type '{type.Name}' is not emitted yet."),
    };

    /// <summary>ABI-050: bit <c>k</c> is bit <c>k mod 8</c> of byte <c>k div 8</c>; all zero → <c>zeroinitializer</c>.</summary>
    private static IrConstant BigSetConstant(BigInteger mask, long size)
    {
        var type = new ArrayIrType(size, IrType.I8);
        return mask.IsZero
            ? new IrZeroConstant(type)
            : new IrArrayConstant(type, [.. Enumerable.Range(0, (int)size).Select(i => (IrConstant)new IrIntConstant(IrType.I8, (mask >> (8 * i)) & 0xFF))]);
    }

    /// <summary>
    /// A constant in memory form: Booleans as 0/1, C-style Booleans as 0/−1 (CMP-305); static arrays and records element by
    /// element (CMP-341), omitted record fields and padding as <c>zeroinitializer</c>.
    /// </summary>
    private static IrConstant StorageConstant(ModuleState module, ConstantValue constant, TypeSymbol type)
    {
        IrConstant Element(ConstantValue? value, TypeSymbol elementType) =>
            value is null ? new IrZeroConstant(TypeLayout.Of(elementType).StorageType) : StorageConstant(module, value, elementType);

        switch (type, constant.Value)
        {
            case (PointerTypeSymbol, AddressConstant { Target: VariableSymbol variable }):
                return module.Global(variable);                                                   // LANG-265 @X in a typed constant
            case (PointerTypeSymbol, AddressConstant { Target: RoutineSymbol routine }):
                return module.Routine(routine);
            case (PointerTypeSymbol pointer, string text):
                return module.CharPointerLiteral(text, pointer);                                  // LANG-245 (C-063)
            case (StringTypeSymbol { Variant: StringVariant.Short } shortString, string text):
                return ModuleState.ShortStringConstant(text, shortString);
            case (ArrayTypeSymbol array, ImmutableArray<ConstantValue?> elements):
                return new IrArrayConstant((ArrayIrType)TypeLayout.Of(array).StorageType, [.. elements.Select(e => Element(e, array.ElementType))]);
            case (RecordTypeSymbol record, ImmutableArray<ConstantValue?> fields):
            {
                var storage = TypeLayout.Of(record).StorageType;
                var body = storage as StructIrType ?? ((NamedStructIrType)storage).Body!;
                var values = body.Fields.Select(f => (IrConstant)new IrZeroConstant(f)).ToArray();          // padding
                var indexes = TypeLayout.FieldIndexes(record);
                for (var i = 0; i < fields.Length; i++)
                {
                    values[indexes[i]] = Element(fields[i], record.Fields[i].Type);
                }

                return new IrStructConstant(body, [.. values], storage);
            }
        }

        var value = ValueConstant(constant, type);
        return type is BooleanTypeSymbol boolean
            ? new IrIntConstant((IntegerIrType)TypeLayout.Of(type).StorageType, ((IrIntConstant)value).Value.IsZero ? 0 : boolean.IsCStyle ? -1 : 1)
            : value;
    }
}

/// <summary>
/// One <c>$units</c> entry (CMP-355): the unit's <c>$init</c> and <c>$final</c>, each null when absent. The program's own entry
/// (C-183) has only a <c>$final</c>.
/// </summary>
internal sealed record UnitTableEntry(UnitSymbol Unit, bool HasInit, bool HasFinal);

/// <summary>A routine's frame record type and its captured variables (struct field <c>Indexes[i + 1]</c> holds <c>Variables[i]</c>, CMP-345).</summary>
/// <remarks>Fields are padded like records when a variable's LLVM alignment is below its ABI alignment (<see cref="TypeLayout.Struct"/>).</remarks>
internal sealed record FrameRecord(NamedStructIrType Type, ImmutableArray<Symbol> Variables, ImmutableArray<int> Indexes)
{
    public int FieldOf(Symbol variable) => Variables.IndexOf(variable) is var i and >= 0 ? Indexes[i + 1] : -1;
}

/// <summary>Module-wide state shared by the function emitters.</summary>
internal sealed class ModuleState(IIrWriter writer)
{
    public IIrWriter Writer { get; } = writer;

    public Dictionary<VariableSymbol, IrGlobal> Globals { get; } = [];

    public Dictionary<RoutineSymbol, FrameRecord> Frames { get; } = [];

    /// <summary>Link names of the routines defined in this module: helper calls in unit System use them directly.</summary>
    public HashSet<string> DefinedRoutines { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The function to call: routines with a body in this module directly, all others (external routines, routines of other
    /// units such as System's, C-175) through a declaration. A noreturn routine is declared with <c>#1</c>, like the helpers.
    /// </summary>
    public IrGlobal Routine(RoutineSymbol routine)
    {
        var name = Mangler.Routine(routine);
        if (DefinedRoutines.Contains(name.Text))
        {
            return new IrGlobal(name);
        }

        UsesNoReturn |= routine.IsNoReturn;
        return Declare(new IrFunctionDeclaration(name, IrEmitter.ReturnType(routine), IrEmitter.ParameterTypes(routine),
            routine.IsNoReturn ? IrEmitter.NoReturnGroup : null)
        {
            DllImport = routine.LibraryName is not null,
        });
    }

    public bool UsesNoReturn { get; private set; }

    /// <summary>The classes this unit declares: their VMTs are defined here (CMP-366).</summary>
    public HashSet<ClassTypeSymbol> OwnClasses { get; init; } = [];

    private readonly SortedSet<LinkName> _externalVmts = new(Comparer<LinkName>.Create((a, b) => string.CompareOrdinal(a.Text, b.Text)));

    /// <summary>VMTs of other units' classes referenced here, sorted by link name.</summary>
    public IEnumerable<LinkName> ExternalVmts => _externalVmts;

    /// <summary>The address of a class's VMT (ABI-071: a class reference), declared external when another unit defines it.</summary>
    public IrGlobal Vmt(ClassTypeSymbol type)
    {
        var name = Mangler.Vmt(type);
        if (!OwnClasses.Contains(type))
        {
            _externalVmts.Add(name);
        }

        return new IrGlobal(name);
    }

    private readonly Dictionary<string, IrConstant> _literalReferences = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, (LinkName Name, TypeSymbol Type)> _externals = new(StringComparer.Ordinal);

    /// <summary>The literal blocks <c>@.str.N</c> in first-use order (CMP-301, 320).</summary>
    public List<(LinkName Name, IrStructConstant Block)> StringLiterals { get; } = [];

    /// <summary>Variables of other units (System) used but not defined in this unit, sorted by link name.</summary>
    public IEnumerable<(LinkName Name, TypeSymbol Type)> ExternalVariables => _externals.Values;

    /// <summary>
    /// CMP-320: the reference to the UnicodeString literal <paramref name="text"/> — a GEP to the payload of its
    /// <c>{ i32 -1, i32 Length, i16 1200, i16 2, i32 0, [Length+1 x i16] }</c> block, one block per distinct text — or
    /// <c>ptr null</c> for <c>''</c>.
    /// </summary>
    public IrConstant StringLiteral(string text)
    {
        if (text.Length == 0)
        {
            return new IrNullConstant();
        }

        if (!_literalReferences.TryGetValue(text, out var reference))
        {
            var payloadType = new ArrayIrType(text.Length + 1, IrType.I16);
            var type = new StructIrType([IrType.I32, IrType.I32, IrType.I16, IrType.I16, IrType.I32, payloadType]);
            var payload = new IrArrayConstant(payloadType, [.. text.Select(c => (IrConstant)new IrIntConstant(IrType.I16, c)), new IrIntConstant(IrType.I16, 0)]);
            var name = Mangler.StringLiteral(StringLiterals.Count);
            StringLiterals.Add((name, new IrStructConstant(type,
                [IrIntConstant.I32(-1), IrIntConstant.I32(text.Length), new IrIntConstant(IrType.I16, 1200), new IrIntConstant(IrType.I16, 2), IrIntConstant.I32(0), payload])));
            reference = new IrGepConstant(type, new IrGlobal(name), [IrIntConstant.I32(0), IrIntConstant.I32(5)]);
            _literalReferences.Add(text, reference);
        }

        return reference;
    }

    /// <summary>
    /// CMP-320 (C-063): an AnsiString-family literal — <c>{ i32 -1, i32 Length, i16 CodePage, i16 1, i32 0, [Length+1 x i8] }</c>
    /// with the UTF-8 bytes of <paramref name="text"/> — referenced by a GEP to its payload; <c>''</c> is <c>ptr null</c>.
    /// </summary>
    public IrConstant AnsiStringLiteral(string text, int codePage)
    {
        if (text.Length == 0)
        {
            return new IrNullConstant();
        }

        var key = "A" + codePage + ":" + text;
        if (!_literalReferences.TryGetValue(key, out var reference))
        {
            var bytes = Strings.Utf8(text);
            var payloadType = new ArrayIrType(bytes.Length + 1, IrType.I8);
            var type = new StructIrType([IrType.I32, IrType.I32, IrType.I16, IrType.I16, IrType.I32, payloadType]);
            var payload = new IrArrayConstant(payloadType, [.. bytes.Select(b => (IrConstant)new IrIntConstant(IrType.I8, (sbyte)b)), new IrIntConstant(IrType.I8, 0)]);
            var name = Mangler.StringLiteral(StringLiterals.Count);
            StringLiterals.Add((name, new IrStructConstant(type,
                [IrIntConstant.I32(-1), IrIntConstant.I32(bytes.Length), new IrIntConstant(IrType.I16, (short)codePage), new IrIntConstant(IrType.I16, 1), IrIntConstant.I32(0), payload])));
            reference = new IrGepConstant(type, new IrGlobal(name), [IrIntConstant.I32(0), IrIntConstant.I32(5)]);
            _literalReferences.Add(key, reference);
        }

        return reference;
    }

    public static readonly LinkName EmptyTerminatorName = new(".str.empty", AlwaysQuote: false);

    /// <summary>Whether <c>@.str.empty</c> is referenced (CMP-320).</summary>
    public bool UsesEmptyTerminator { get; private set; }

    /// <summary>
    /// LANG-245 / CMP-320: a string literal as a <c>PChar</c> (the UnicodeString literal's payload) or <c>PAnsiChar</c> (the AnsiString
    /// literal's payload); <c>''</c> points to the shared <c>@.str.empty</c>.
    /// </summary>
    public IrConstant CharPointerLiteral(string text, TypeSymbol pointerType)
    {
        if (text.Length == 0)
        {
            UsesEmptyTerminator = true;
            return new IrGlobal(EmptyTerminatorName);
        }

        return Strings.IsCharPointer(pointerType, wide: false) ? AnsiStringLiteral(text, 65001) : StringLiteral(text);
    }

    /// <summary>A short string constant: <c>[capacity+1 x i8]</c> with the length byte, the UTF-8 bytes (cut at a sequence boundary) and zeros.</summary>
    public static IrConstant ShortStringConstant(string text, StringTypeSymbol type)
    {
        var bytes = Strings.ShortBytes(text, type.MaxLength);
        var storage = new ArrayIrType(type.Size, IrType.I8);
        return new IrArrayConstant(storage, [new IrIntConstant(IrType.I8, bytes.Length),
            .. bytes.Select(b => (IrConstant)new IrIntConstant(IrType.I8, (sbyte)b)),
            .. Enumerable.Repeat<IrConstant>(new IrIntConstant(IrType.I8, 0), (int)type.Size - 1 - bytes.Length)]);
    }

    /// <summary>The unit this module belongs to (anonymous types' RTTI names, CMP-362).</summary>
    public string UnitName { get; init; } = "";

    private readonly Dictionary<TypeSymbol, IrGlobal> _typeInfoReferences = [];

    /// <summary>RTTI blocks (rtl-abi.md §10) in first-use order; nested element blocks come before the blocks referencing them.</summary>
    public List<(LinkName Name, IrStructConstant Block)> TypeInfos { get; } = [];

    private static readonly IrConstant Pad7 = new IrZeroConstant(new ArrayIrType(7, IrType.I8));

    /// <summary>
    /// CMP-405/407 (C-064): the address of the RTTI block of <paramref name="type"/>, built once per module (<c>linkonce_odr</c>,
    /// CMP-365) as an anonymous struct per rtl-abi.md §10: <c>tkUString</c>/<c>tkAString</c> (code page), <c>tkDynArray</c>
    /// (ElSize, ElType, ManagedElType), <c>tkRecord</c> (Size, managed fields), <c>tkArray</c> (Size, total ElCount, ElType). An element
    /// type gets a block only for these kinds (ElType null otherwise; tkInteger &amp; co. arrive with TypeInfo, M2.5).
    /// </summary>
    public IrGlobal TypeInfo(TypeSymbol type)
    {
        if (_typeInfoReferences.TryGetValue(type, out var global))
        {
            return global;
        }

        IrConstant Name() => IsDeclared(type) || type is StringTypeSymbol ? StringLiteral(type.Name) : new IrNullConstant();
        IrConstant Element(TypeSymbol element) => HasTypeInfo(element) ? TypeInfo(element) : new IrNullConstant();
        var fields = type switch
        {
            StringTypeSymbol { Variant: not StringVariant.Short } s => new IrConstant[]
            {
                new IrIntConstant(IrType.I8, s.Variant == StringVariant.Unicode ? 9 : 10), Pad7, Name(),
                new IrIntConstant(IrType.I16, (short)s.CodePage), new IrZeroConstant(new ArrayIrType(6, IrType.I8)),
            },
            DynamicArrayTypeSymbol array =>
            [
                new IrIntConstant(IrType.I8, 14), Pad7, Name(), IrIntConstant.I64(TypeLayout.Of(array.ElementType).Size), Element(array.ElementType),
                array.ElementType.IsManaged ? Element(array.ElementType) : new IrNullConstant(),
            ],
            RecordTypeSymbol record => RecordFields(record, Name()),
            ArrayTypeSymbol array =>
            [
                new IrIntConstant(IrType.I8, 13), Pad7, Name(), IrIntConstant.I64(array.Size), IrIntConstant.I64(ElementCount(array)), Element(Innermost(array)),
            ],
            _ => throw new NotSupportedException($"No type info for '{type.Name}' yet."),
        };
        var block = new IrStructConstant(new StructIrType([.. fields.Select(f => f.Type)]), [.. fields]);
        var name = TypeInfoName(type);
        global = new IrGlobal(name);
        _typeInfoReferences.Add(type, global);
        TypeInfos.Add((name, block));
        return global;
    }

    /// <summary><c>tkRecord</c>: Size, ManagedCount and <c>@"…$rtti.fields"</c> (or null) of <c>{ ptr TypeInfo, i64 Offset }</c> entries.</summary>
    private IrConstant[] RecordFields(RecordTypeSymbol record, IrConstant name)
    {
        var managed = record.Fields.Where(f => f.Type.IsManaged).ToImmutableArray();
        IrConstant table = new IrNullConstant();
        if (!managed.IsEmpty)
        {
            var entry = new StructIrType([IrType.Ptr, IrType.I64]);
            var tableName = new LinkName(TypeInfoName(record).Text + ".fields", AlwaysQuote: true);
            AggregateConstants.Add((tableName, new IrArrayConstant(new ArrayIrType(managed.Length, entry),
                [.. managed.Select(f => (IrConstant)new IrStructConstant(entry, [TypeInfo(f.Type), IrIntConstant.I64(f.Offset)]))]), 8));
            table = new IrGlobal(tableName);
        }

        return [new IrIntConstant(IrType.I8, 12), Pad7, name, IrIntConstant.I64(record.Size), IrIntConstant.I64(managed.Length), table];
    }

    private static bool HasTypeInfo(TypeSymbol type) =>
        type is StringTypeSymbol { Variant: not StringVariant.Short } or DynamicArrayTypeSymbol or RecordTypeSymbol or ArrayTypeSymbol;

    private static TypeSymbol Innermost(ArrayTypeSymbol array) => array.ElementType is ArrayTypeSymbol inner ? Innermost(inner) : array.ElementType;

    private static long ElementCount(ArrayTypeSymbol array) => array.Length * (array.ElementType is ArrayTypeSymbol inner ? ElementCount(inner) : 1);

    /// <summary>A type declaration (not a built-in, not an anonymous type constructor).</summary>
    private static bool IsDeclared(TypeSymbol type) =>
        !type.Locations.IsEmpty && type.ContainingSymbol is not null && type is not RecordTypeSymbol { IsAnonymous: true };

    /// <summary>
    /// rtl-abi.md §10 / CMP-362: <c>U.T.$rtti</c> for a declared type (a routine's type: its routine's link name first), the
    /// built-in strings as <c>System.string.$rtti</c>, anonymous types <c>U.$rtti.&lt;type text&gt;</c> of this module's unit.
    /// </summary>
    private LinkName TypeInfoName(TypeSymbol type)
    {
        if (type is StringTypeSymbol)
        {
            return new LinkName("System." + type.Name + ".$rtti", AlwaysQuote: true);
        }

        if (IsDeclared(type))
        {
            var prefix = type.ContainingSymbol is RoutineSymbol routine ? Mangler.Routine(routine).Text + "." + type.Name : type.QualifiedName;
            return new LinkName(prefix + ".$rtti", AlwaysQuote: true);
        }

        return new LinkName(UnitName + ".$rtti." + type.Name, AlwaysQuote: true);
    }

    private readonly Dictionary<string, IrGlobal> _aggregateReferences = new(StringComparer.Ordinal);

    /// <summary>Constant aggregates used as operands (<c>@.set.N</c>, CMP-336), first-use order, one per distinct value.</summary>
    public List<(LinkName Name, IrConstant Value, int Alignment)> AggregateConstants { get; } = [];

    /// <summary>
    /// The address of a private constant holding <paramref name="value"/> (emitted after the string literals);
    /// <paramref name="key"/> identifies the value.
    /// </summary>
    public IrGlobal AggregateConstant(string key, IrConstant value, int alignment, string prefix = ".set.")
    {
        if (!_aggregateReferences.TryGetValue(key, out var global))
        {
            var name = new LinkName(prefix + AggregateConstants.Count, AlwaysQuote: false);
            AggregateConstants.Add((name, value, alignment));
            global = new IrGlobal(name);
            _aggregateReferences.Add(key, global);
        }

        return global;
    }

    /// <summary>The address of a global variable: this unit's own, or one of another unit declared <c>external</c> (CMP-301).</summary>
    public IrGlobal Global(VariableSymbol variable) =>
        Globals.TryGetValue(variable, out var own) ? own : External(Mangler.GlobalVariable(variable), variable.Type);

    private IrGlobal External(LinkName name, TypeSymbol type)
    {
        _externals.TryAdd(name.Text, (name, type));
        return new IrGlobal(name);
    }

    /// <summary>Declarations requested inside functions; the writer takes them at module level (<see cref="DeclarePending"/>).</summary>
    private readonly List<IrFunctionDeclaration> _pending = [];

    /// <summary>A void runtime helper without parameters, noreturn (<c>_DivZeroError</c>).</summary>
    public IrGlobal NoReturnHelper(string name)
    {
        UsesNoReturn = true;
        return Declare(new IrFunctionDeclaration(Mangler.RuntimeHelper(name), IrType.Void, [], IrEmitter.NoReturnGroup));
    }

    /// <summary>A runtime helper called by lowered code (<c>_Halt</c>, <c>_RunError</c>); noreturn ones carry <c>#1</c>.</summary>
    public IrGlobal Helper(RuntimeHelper helper)
    {
        UsesNoReturn |= helper.IsNoReturn;
        var returnType = helper.ReturnType is { } type ? TypeLayout.Of(type).StorageType : IrType.Void;
        return Declare(new IrFunctionDeclaration(helper.LinkName, returnType,
            [.. helper.ParameterTypes.Select(p => TypeLayout.Of(p).StorageType)], helper.IsNoReturn ? IrEmitter.NoReturnGroup : null));
    }

    public IrGlobal Intrinsic(string name, IrType returnType, ImmutableArray<IrType> parameterTypes) =>
        Declare(new IrFunctionDeclaration(new LinkName(name, AlwaysQuote: false), returnType, parameterTypes));

    /// <summary>Hands the collected declarations to the writer (it sorts them and merges duplicates, CMP-301).</summary>
    public void DeclarePending()
    {
        foreach (var declaration in _pending)
        {
            Writer.DeclareFunction(declaration);
        }

        _pending.Clear();
    }

    /// <summary>A function of another module (<c>System._Startup</c>, <c>U.$init</c>), declared at module level.</summary>
    public IrGlobal Function(IrFunctionDeclaration declaration) => Declare(declaration);

    private IrGlobal Declare(IrFunctionDeclaration declaration)
    {
        if (DefinedRoutines.Contains(declaration.Name.Text))
        {
            return new IrGlobal(declaration.Name);
        }

        _pending.Add(declaration);
        return new IrGlobal(declaration.Name);
    }
}

/// <summary>Emits one routine (CMP-310): entry block with all allocas and parameter spills, the body, block <c>exit</c> with the only <c>ret</c>.</summary>
internal sealed class FunctionEmitter
{
    private static readonly IrBlock Entry = new("entry");
    private static readonly IrBlock Exit = new("exit");
    private static readonly IrBlock CleanupBlock = new("cleanup");

    private readonly ModuleState _module;
    private readonly IIrWriter _w;
    private readonly LoweredBody _body;
    private readonly RoutineSymbol? _routine;
    /// <summary>Implicit parameters (a method's <c>Self</c> and flag, ABI-082) first, then the declared ones.</summary>
    private readonly ImmutableArray<ParameterSymbol> _parameters;
    private readonly int _implicitCount;
    private readonly LinkName _name;
    private readonly IrLinkage _linkage;
    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase) { Entry.Name, Exit.Name, CleanupBlock.Name };

    /// <summary>Where <c>Exit</c> and the end of the body go: block <c>cleanup</c> when the body has a cleanup scope, else <c>exit</c>.</summary>
    private readonly IrBlock _returnTarget;
    private readonly Dictionary<LLocal, IrValue> _locals = [];
    private readonly Dictionary<ParameterSymbol, IrValue> _parameterAddresses = [];
    private readonly Dictionary<LTempValue, IrValue> _temps = [];
    private int _tempCount;
    private int _blockCount;
    private readonly Dictionary<LLoopLabel, (IrBlock Break, IrBlock Continue)> _loops = [];
    private IrBlock _current = Entry;
    private bool _terminated;
    private IrValue? _link;
    private FrameRecord? _frame;
    private IrValue? _frameAddress;

    /// <param name="name">
    /// The function's name for a body without routine (<c>$main</c>, <c>U.$init</c>, <c>U.$final</c>: <c>void()</c>, CMP-350,
    /// 355); null for a routine body (its mangled name).
    /// </param>
    public FunctionEmitter(ModuleState module, LoweredBody body, IrLinkage linkage, LinkName? name = null)
    {
        _module = module;
        _w = module.Writer;
        _body = body;
        _routine = body.Routine;
        _implicitCount = _routine?.ImplicitParameters.Length ?? 0;
        _parameters = _routine is null ? [] : [.. _routine.ImplicitParameters, .. _routine.Parameters];
        _name = name ?? Mangler.Routine(_routine ?? throw new ArgumentException("A body without routine needs a name.", nameof(name)));
        _linkage = linkage;
        _returnTarget = body.Cleanup is null ? Exit : CleanupBlock;
    }

    /// <summary>
    /// Entry block: the frame record <c>%$frame</c> (CMP-345) first, then the allocas of parameters and locals that are not
    /// captured; then field 0 := static link (or null), <c>%name.addr</c> = field address of every captured variable (so the
    /// body addresses them like allocas), and the parameter spills in parameter order — into the frame for captured ones
    /// (by-reference parameters store their pointer there and keep using the incoming <c>ptr</c>).
    /// </summary>
    public void Emit()
    {
        var link = _routine is { IsNested: true } ? new IrParameter(IrType.Ptr, Unique("$link")) : null;
        var hidden = _routine is not null && IrEmitter.HasHiddenResult(_routine) ? new IrParameter(IrType.Ptr, Unique("Result")) : null;
        var parameters = _parameters.Select(p => new IrParameter(IrEmitter.ParameterType(p), Unique(p.Name))).ToImmutableArray();
        // ABI-082: [static link], [Self], [flag], [hidden Result], declared parameters
        ImmutableArray<IrParameter> all = [.. new[] { link }.OfType<IrParameter>(), .. parameters[.._implicitCount],
            .. new[] { hidden }.OfType<IrParameter>(), .. parameters[_implicitCount..]];
        var values = _w.BeginFunction(new IrFunctionDefinition(_name, _linkage, _routine is null ? IrType.Void : IrEmitter.ReturnType(_routine),
            all, _routine is null ? IrEmitter.RoutineGroup : IrEmitter.AttributeGroupOf(_routine)));
        if (link is not null)
        {
            _link = values[0];
            values = values.RemoveAt(0);
        }

        IrValue? hiddenResult = null;
        if (hidden is not null)
        {
            hiddenResult = values[_implicitCount];
            values = values.RemoveAt(_implicitCount);
        }

        _w.AppendBlock(Entry);
        if (_routine is not null)
        {
            _module.Frames.TryGetValue(_routine, out _frame);
        }
        if (_frame is not null)
        {
            _frameAddress = _w.Alloca(Unique("$frame"), _frame.Type, 8);
        }

        var spills = new List<(IrValue Value, IrValue Address, int Alignment)>();
        for (var i = 0; i < _parameters.Length; i++)
        {
            var parameter = _parameters[i];
            if (IrEmitter.IsPassedByAddress(parameter))
            {
                _parameterAddresses[parameter] = values[i];
                continue;
            }

            if (!IsCaptured(parameter))
            {
                var layout = TypeLayout.Allocated(parameter.Type);
                _parameterAddresses[parameter] = _w.Alloca(Unique(values[i].Name + ".addr"), layout.StorageType, layout.Alignment);
            }
        }

        foreach (var local in _body.Locals)
        {
            if (hiddenResult is not null && ReferenceEquals(local.Variable, _routine!.ResultVariable))
            {
                _locals[local] = hiddenResult;                  // ABI-081: Result is the caller's storage
            }
            else if (!IsCaptured(local.Variable))
            {
                var layout = TypeLayout.Allocated(local.Type);
                _locals[local] = _w.Alloca(Unique(local.Name + ".addr"), layout.StorageType, layout.Alignment);
            }
        }

        if (_frame is not null)
        {
            _w.Store(_link ?? new IrNullConstant(), _frameAddress!, 8);
            foreach (var variable in _frame.Variables)
            {
                var address = _w.GetElementPtr(Unique(variable.Name + ".addr"), _frame.Type, _frameAddress!,
                    [IrIntConstant.I32(0), IrIntConstant.I32(_frame.FieldOf(variable))]);
                switch (variable)
                {
                    case ParameterSymbol parameter when IrEmitter.HoldsAddress(parameter):
                        spills.Add((_parameterAddresses[parameter], address, 8));
                        break;
                    case VariableSymbol when IrEmitter.HoldsAddress(variable):
                        spills.Add((hiddenResult!, address, 8));
                        break;
                    case ParameterSymbol parameter:
                        _parameterAddresses[parameter] = address;
                        break;
                    default:
                        _locals[_body.Locals.Single(l => ReferenceEquals(l.Variable, variable))] = address;
                        break;
                }
            }
        }

        for (var i = 0; i < _parameters.Length; i++)
        {
            var parameter = _parameters[i];
            if (!IrEmitter.IsPassedByAddress(parameter))
            {
                spills.Add((values[i], _parameterAddresses[parameter], TypeLayout.Of(parameter.Type).Alignment));
            }
        }

        foreach (var (value, address, alignment) in spills)
        {
            _w.Store(value, address, alignment);
        }

        Statement(_body.Body);
        FallThrough(_returnTarget);
        if (_body.Cleanup is { } cleanup)
        {
            // CMP-325 rule 2: the function-level cleanup scope on the normal exit path. EH milestone (CMP-393): the same
            // finalisation sequence is emitted again in a cleanuppad funclet, and calls in the body become invokes (CMP-394).
            Start(CleanupBlock);
            Statement(cleanup);
            FallThrough(Exit);
        }

        Start(Exit);
        if (_routine?.ResultVariable is { } result && hiddenResult is null)
        {
            // ABI-081: a small aggregate is returned as the integer holding its bytes
            var local = _body.Locals.Single(l => ReferenceEquals(l.Variable, result));
            var layout = TypeLayout.Of(local.Type);
            _w.Ret(_w.Load(NewTemp(), IrEmitter.ReturnType(_routine), _locals[local], layout.Alignment));
        }
        else if (_routine is MethodSymbol { MethodKind: MethodKind.Constructor, SelfParameter: { } self })
        {
            _w.Ret(_w.Load(NewTemp(), IrType.Ptr, _parameterAddresses[self], 8));     // ABI-075 / CMP-368: Result := Self
        }
        else
        {
            _w.Ret(null);
        }

        _w.EndFunction();
    }

    // ------------------------------------------------------------------ names (CMP-303)

    /// <summary><paramref name="name"/>, or with <c>.N</c> appended when it is taken (case-insensitively) or has the shape of a temporary.</summary>
    private string Unique(string name)
    {
        var candidate = name;
        for (var n = 1; IsTemporaryName(candidate) || !_names.Add(candidate); n++)
        {
            candidate = name + "." + n;
        }

        return candidate;
    }

    private static bool IsTemporaryName(string name) => name.Length > 1 && name[0] is 't' or 'T' && name.Skip(1).All(char.IsAsciiDigit);

    private string NewTemp()
    {
        string name;
        do
        {
            name = "t" + ++_tempCount;
        }
        while (!_names.Add(name));

        return name;
    }

    private int NewConstruct() => ++_blockCount;

    private IrBlock Block(string kind, int id) => new(kind + "." + id);

    private void Start(IrBlock block)
    {
        _w.AppendBlock(block);
        _current = block;
        _terminated = false;
    }

    // ------------------------------------------------------------------ statements

    /// <summary>
    /// Emits <paramref name="statement"/> into the current block. After a jump (<see cref="LBreak"/>, <see cref="LContinue"/>,
    /// <see cref="LReturn"/>, <see cref="LUnreachable"/>) the block is terminated and the rest of the enclosing
    /// <see cref="LBlock"/> is dead code: it is not emitted (the lowered tree has no labels, so nothing can jump into it).
    /// </summary>
    private void Statement(LStatement statement)
    {
        switch (statement)
        {
            case LBlock block:
                foreach (var inner in block.Statements)
                {
                    if (_terminated)
                    {
                        break;
                    }

                    Statement(inner);
                }

                break;
            case LAssign { Value: LCall { Callee: LRoutineCallee callee } call } assign
                when Lowerer.IsAggregate(assign.Target.Type) || IrEmitter.HasHiddenResult(callee.Routine):
            {
                // ABI-081: the lowering assigns aggregate results only to fresh temporaries, so the target cannot alias the arguments;
                // a managed result goes to a temporary or a local the arguments do not mention (CMP-325 rule 4)
                var target = Address(assign.Target);
                if (IrEmitter.HasHiddenResult(callee.Routine))
                {
                    Call(named: false, callee, call.Arguments, target);
                }
                else
                {
                    _w.Store(Call(named: true, callee, call.Arguments)!, target, AccessAlignment(assign.Target));
                }

                break;
            }

            case LAssign assign when Lowerer.IsAggregate(assign.Target.Type):
            {
                if (assign.Target.Type.IsManaged)
                {
                    throw new InvalidOperationException("A managed record or array assignment must be lowered to _CopyRecord (ManagedRewriter).");
                }

                var source = Address(assign.Value);
                Copy(Address(assign.Target), source, assign.Target.Type, AccessAlignment(assign.Target), AccessAlignment(assign.Value),
                    mayOverlap: SameArrayVariable(assign.Target, assign.Value));
                break;
            }

            case LAssign assign when assign.Target.Type.IsManaged:
                throw new InvalidOperationException("A managed assignment must be lowered to helper calls (ManagedRewriter).");
            case LAssign { Target: LView view } assign:
                Store(Value(assign.Value), Address(view), view.Type, AlignmentOf(view));
                break;
            case LAssign assign:
                Store(Value(assign.Value), WrittenAddress(assign.Target), assign.Target.Type, AccessAlignment(assign.Target));
                break;
            case LZero { Type.IsManaged: true } zero when !Lowerer.IsAggregate(zero.Type):
                _w.Store(new IrNullConstant(), Value(zero.Address), 8);              // CMP-325 rule 1: store ptr null
                break;
            case LZero zero:
            {
                var layout = TypeLayout.Of(zero.Type);
                var memset = _module.Intrinsic("llvm.memset.p0.i64", IrType.Void, [IrType.Ptr, IrType.I8, IrType.I64, IrType.I1]);
                _w.Call(null, IrType.Void, memset,
                    [new IrAlignedPointer(Value(zero.Address), layout.Alignment), new IrIntConstant(IrType.I8, 0), IrIntConstant.I64(layout.Size), IrIntConstant.I1(false)]);
                break;
            }

            case LMemSet fill:
            {
                // CMP-412: FillChar → llvm.memset (the variable's access alignment, CMP-309)
                var address = WrittenAddress(fill.Target);
                var count = Value(fill.Count);
                var value = Value(fill.Value);
                var memset = _module.Intrinsic("llvm.memset.p0.i64", IrType.Void, [IrType.Ptr, IrType.I8, IrType.I64, IrType.I1]);
                _w.Call(null, IrType.Void, memset, [new IrAlignedPointer(address, AccessAlignment(fill.Target)), value, count, IrIntConstant.I1(false)]);
                break;
            }

            case LMemMove move:
            {
                // CMP-412: Move → llvm.memmove (overlap allowed)
                var source = Address(move.Source);
                var destination = WrittenAddress(move.Destination);
                var count = Value(move.Count);
                var memmove = _module.Intrinsic("llvm.memmove.p0.p0.i64", IrType.Void, [IrType.Ptr, IrType.Ptr, IrType.I64, IrType.I1]);
                _w.Call(null, IrType.Void, memmove,
                    [new IrAlignedPointer(destination, AccessAlignment(move.Destination)), new IrAlignedPointer(source, AccessAlignment(move.Source)), count, IrIntConstant.I1(false)]);
                break;
            }

            case LSetBit setBit:
            {
                var (address, bit) = BitAddress(setBit.Address, setBit.Element);
                var bits = _w.Load(NewTemp(), IrType.I8, address, 1);
                var mask = Shl8(bit);
                var changed = setBit.Include
                    ? _w.Binary(NewTemp(), IrBinaryOperator.Or, bits, mask)
                    : _w.Binary(NewTemp(), IrBinaryOperator.And, bits, mask is IrIntConstant c
                        ? new IrIntConstant(IrType.I8, ~c.Value & 0xFF)
                        : _w.Binary(NewTemp(), IrBinaryOperator.Xor, mask, new IrIntConstant(IrType.I8, -1)));
                _w.Store(changed, address, 1);
                break;
            }
            case LIf ifStatement:
                If(ifStatement);
                break;
            case LLoop loop:
                Loop(loop);
                break;
            case LSwitch switchStatement:
                Switch(switchStatement);
                break;
            case LBreak or LContinue:
                Jump(JumpTarget(statement)!);
                break;
            case LReturn:
                Jump(_returnTarget);
                break;
            case LUnreachable:
                _w.Unreachable();
                _terminated = true;
                break;
            case LCallStatement { Callee: LHelperCallee helper } call:
            {
                var callee = _module.Helper(helper.Helper);
                var arguments = call.Arguments.Select((a, i) => ToStorage(Value(a), helper.Helper.ParameterTypes[i])).ToImmutableArray();
                var returnType = helper.Helper.ReturnType is { } type ? TypeLayout.Of(type).StorageType : IrType.Void;
                _w.Call(null, returnType, callee, arguments, helper.Helper.IsNoReturn ? IrEmitter.NoReturnGroup : null);
                break;
            }

            case LCallStatement { Callee: LRoutineCallee callee } call:
                Call(named: false, callee, call.Arguments);
                break;
            default:
                throw new InvalidOperationException($"Cannot emit {statement.GetType().Name}.");
        }
    }

    // ------------------------------------------------------------------ control flow (CMP-318…322)

    private void Jump(IrBlock target)
    {
        _w.Br(target);
        _terminated = true;
    }

    /// <summary>Branches to <paramref name="block"/> unless the current block already ended with a jump.</summary>
    private void FallThrough(IrBlock block)
    {
        if (!_terminated)
        {
            _w.Br(block);
        }
    }

    /// <summary>The block a bare <see cref="LBreak"/>/<see cref="LContinue"/>/<see cref="LReturn"/> jumps to; null for other statements.</summary>
    private IrBlock? JumpTarget(LStatement statement) => statement switch
    {
        LBreak jump => _loops[jump.Label].Break,
        LContinue jump => _loops[jump.Label].Continue,
        LReturn => _returnTarget,
        LBlock { Statements: [var single] } => JumpTarget(single),
        _ => null,
    };

    /// <summary>
    /// CMP-318: <c>if.then.N</c> / <c>if.else.N</c> / <c>if.end.N</c>. An arm that is only a jump (<c>if c then Break</c>) branches
    /// to its target directly instead of getting a block.
    /// </summary>
    private void If(LIf statement)
    {
        var condition = Value(statement.Condition);
        var id = NewConstruct();
        var end = Block("if.end", id);
        var thenTarget = JumpTarget(statement.Then);
        var elseTarget = statement.Else is null ? end : JumpTarget(statement.Else);
        var then = thenTarget ?? Block("if.then", id);
        var @else = elseTarget ?? Block("if.else", id);
        _w.CondBr(condition, then, @else);
        if (thenTarget is null)
        {
            Arm(then, statement.Then, end);
        }

        if (elseTarget is null)
        {
            Arm(@else, statement.Else!, end);
        }

        Start(end);
    }

    private void Arm(IrBlock block, LStatement body, IrBlock end)
    {
        Start(block);
        Statement(body);
        FallThrough(end);
    }

    /// <summary>
    /// CMP-319 with the label kind <c>K</c> (architecture.md, C-035 decision): <c>K.body.N</c>, then the step block
    /// (<c>for.inc.N</c>, <c>repeat.cond.N</c>, else <c>K.step.N</c>) back to the body, and <c>K.end.N</c> after the loop.
    /// A loop whose body is <c>if c then S else Break</c> without step (the <c>while</c> lowering) is emitted as
    /// <c>K.cond.N</c> → <c>K.body.N</c> → back to the condition. <c>Continue</c> jumps to the step, condition or body top.
    /// </summary>
    private void Loop(LLoop loop)
    {
        var kind = loop.Label.Kind;
        var id = NewConstruct();
        IrBlock body = Block(kind + ".body", id), end = Block(kind + ".end", id);
        if (loop is { Step: null, Body: LIf { Else: { } exit } test } && exit is LBreak { } leave && leave.Label == loop.Label)
        {
            var cond = Block(kind + ".cond", id);
            _loops[loop.Label] = (end, cond);
            Jump(cond);
            Start(cond);
            _w.CondBr(Value(test.Condition), body, end);
            _terminated = true;
            Arm(body, test.Then, cond);
            Start(end);
            return;
        }

        var step = loop.Step is null ? null : Block(kind switch { "for" => "for.inc", "repeat" => "repeat.cond", _ => kind + ".step" }, id);
        _loops[loop.Label] = (end, step ?? body);
        Jump(body);
        Arm(body, loop.Body, step ?? body);
        if (step is not null)
        {
            Arm(step, loop.Step!, body);
        }

        Start(end);
    }

    /// <summary>
    /// CMP-322: ranges first (<c>icmp ule (sub sel, lo), hi − lo</c>, section order, blocks <c>case.range.N.J</c>), then
    /// <c>switch</c> to <c>case.arm.N.K</c> (K = section, 1-based), default <c>case.else.N</c> or <c>case.end.N</c>.
    /// </summary>
    private void Switch(LSwitch statement)
    {
        var selector = Value(statement.Selector);
        var type = (IntegerIrType)selector.Type;
        var id = NewConstruct();
        var end = Block("case.end", id);
        var @default = statement.Default is null ? end : Block("case.else", id);
        var arms = statement.Sections.Select((_, k) => Block("case.arm." + id, k + 1)).ToImmutableArray();

        var rangeCount = 0;
        for (var k = 0; k < statement.Sections.Length; k++)
        {
            foreach (var (low, high) in statement.Sections[k].Ranges)
            {
                var offset = _w.Binary(NewTemp(), IrBinaryOperator.Sub, selector, new IrIntConstant(type, low));
                var inRange = _w.ICmp(NewTemp(), IrIntPredicate.Ule, offset, new IrIntConstant(type, high - low));
                var next = Block("case.range." + id, ++rangeCount);
                _w.CondBr(inRange, arms[k], next);
                Start(next);
            }
        }

        _w.Switch(selector, @default, [.. statement.Sections.SelectMany((section, k) => section.Values.Select(v => (new IrIntConstant(type, v), arms[k])))]);
        _terminated = true;
        for (var k = 0; k < statement.Sections.Length; k++)
        {
            Arm(arms[k], statement.Sections[k].Body, end);
        }

        if (statement.Default is { } body)
        {
            Arm(@default, body, end);
        }

        Start(end);
    }

    // ------------------------------------------------------------------ storage

    /// <summary>The address of a variable reference or of an aggregate operand (constant, <see cref="LSequence"/>, <see cref="LView"/>).</summary>
    private IrValue Address(LExpression variable)
    {
        switch (variable)
        {
            case LLocalRef local:
                return _locals[local.Local];
            case LGlobalRef global:
                return _module.Global(global.Variable);
            case LParamRef parameter:
                return _parameterAddresses[parameter.Parameter];
            case LOuterRef outer:
                return OuterAddress(outer);
            case LStringLiteral { Type: StringTypeSymbol { Variant: StringVariant.Short } type } literal:
                return _module.AggregateConstant("s" + type.Size + ":" + literal.Value, ModuleState.ShortStringConstant(literal.Value, type), 1, ".sstr.");
            case LConst constant when Lowerer.IsAggregate(constant.Type):
                return _module.AggregateConstant(constant.Type.Size + ":" + constant.Value.Value, IrEmitter.ValueConstant(constant.Value, constant.Type),
                    TypeLayout.Of(constant.Type).Alignment);
            case LSequence sequence:
                Statement(sequence.First);
                return Address(sequence.Value);
            case LView view:
                return Address(view.Aggregate);
            case LIndex index:
            {
                // CMP-308: getelementptr inbounds [N x T], ptr %a, i64 0, i64 %idx0
                var array = Address(index.Array);
                var offset = Value(index.Index);
                return _w.GetElementPtr(NewTemp(), TypeLayout.Of(index.Array.Type).StorageType, array, [IrIntConstant.I64(0), offset]);
            }

            case LDeref dereference:
                return Value(dereference.Pointer);                                  // CMP-340: p^ is the pointer value as an address

            case LStringIndex element:
                return StringElement(element, Value(element.String));               // ABI-024: read access

            case LArrayElement element:
                return ArrayElement(element);

            case LField field:
            {
                // CMP-307: getelementptr inbounds %"U.TRec", ptr %r, i32 0, i32 <fieldIndex>
                var record = (RecordTypeSymbol)field.Record.Type;
                var address = Address(field.Record);
                var position = TypeLayout.FieldIndexes(record)[record.Fields.IndexOf(field.Field)];
                return _w.GetElementPtr(NewTemp(), TypeLayout.Of(record).StorageType, address, [IrIntConstant.I32(0), IrIntConstant.I32(position)]);
            }

            default:
                throw new InvalidOperationException($"{variable.GetType().Name} is not a variable reference.");
        }
    }

    /// <summary>
    /// The address of a variable reference about to be written (assignment target, <c>var</c> argument, address taken): a string
    /// character first makes its string unique (LANG-241, CMP-324 <c>s[i] := c</c>: <c>_UStrUnique(@s)</c>, reload, GEP).
    /// </summary>
    private IrValue WrittenAddress(LExpression variable)
    {
        if (variable is not LStringIndex element)
        {
            return Address(variable);
        }

        var stringAddress = Address(element.String);
        var helper = Strings.IsAnsi(element.String.Type) ? "_AStrUnique" : "_UStrUnique";
        var unique = _module.Function(new IrFunctionDeclaration(Mangler.RuntimeHelper(helper), IrType.Void, [IrType.Ptr]));
        _w.Call(null, IrType.Void, unique, [stringAddress]);
        return StringElement(element, _w.Load(NewTemp(), IrType.Ptr, stringAddress, 8));
    }

    /// <summary>CMP-324/331: <c>getelementptr inbounds i16, ptr %s, i64 %i0</c>, under <c>{$R+}</c> after <c>%i0 &lt;u Length</c>.</summary>
    private IrValue StringElement(LStringIndex element, IrValue text)
    {
        var index = Value(element.Index);
        if (element.IsChecked)
        {
            var length32 = StringLength(text);
            var length = _w.Cast(NewTemp(), IrCastOperator.ZExt, length32, IrType.I64);
            var inRange = _w.ICmp(NewTemp(), IrIntPredicate.Ult, index, length);
            var id = NewConstruct();
            IrBlock error = Block("range.error", id), ok = Block("range.ok", id);
            _w.CondBr(inRange, ok, error);
            ErrorBlock(error, "_RangeError");
            Start(ok);
        }

        return _w.GetElementPtr(NewTemp(), TypeLayout.Of(element.Type).StorageType, text, [index]);
    }

    /// <summary>
    /// ABI-042 / CMP-330: <c>getelementptr inbounds T, ptr %a, i64 %i</c>; under <c>{$R+}</c> after <c>%i &lt;u Length</c> (CMP-331, the
    /// length of <c>nil</c> being 0).
    /// </summary>
    private IrValue ArrayElement(LArrayElement element)
    {
        var array = Value(element.Array);
        var index = Value(element.Index);
        if (element.IsChecked)
        {
            var length = ArrayLength(array);
            var inRange = _w.ICmp(NewTemp(), IrIntPredicate.Ult, index, length);
            var id = NewConstruct();
            IrBlock error = Block("range.error", id), ok = Block("range.ok", id);
            _w.CondBr(inRange, ok, error);
            ErrorBlock(error, "_RangeError");
            Start(ok);
        }

        return _w.GetElementPtr(NewTemp(), TypeLayout.Of(element.Type).StorageType, array, [index]);
    }

    /// <summary>ABI-041: the <c>i64</c> length of a dynamic array value (<c>nil</c> → 0 through a branch, like <see cref="StringLength"/>).</summary>
    private IrValue ArrayLength(IrValue array)
    {
        var isNil = _w.ICmp(NewTemp(), IrIntPredicate.Eq, array, new IrNullConstant());
        var from = _current;
        var id = NewConstruct();
        IrBlock load = Block("len.load", id), end = Block("len.end", id);
        _w.CondBr(isNil, end, load);
        Start(load);
        var field = _w.GetElementPtr(NewTemp(), IrType.I8, array, [IrIntConstant.I64(-8)]);
        var length = _w.Load(NewTemp(), IrType.I64, field, 8);
        _w.Br(end);
        Start(end);
        return _w.Phi(NewTemp(), IrType.I64, [(IrIntConstant.I64(0), from), (length, load)]);
    }

    /// <summary>
    /// ABI-023 / CMP-324: <c>Length</c> of a string value without loading from <c>nil</c>: <c>icmp eq ptr %s, null</c> → block
    /// <c>len.end.N</c> directly, else <c>len.load.N</c> loads the <c>i32</c> at <c>s − 12</c>; a <c>phi</c> joins them.
    /// </summary>
    private IrValue StringLength(IrValue text)
    {
        var isNil = _w.ICmp(NewTemp(), IrIntPredicate.Eq, text, new IrNullConstant());
        var from = _current;
        var id = NewConstruct();
        IrBlock load = Block("len.load", id), end = Block("len.end", id);
        _w.CondBr(isNil, end, load);
        Start(load);
        var field = _w.GetElementPtr(NewTemp(), IrType.I8, text, [IrIntConstant.I64(-12)]);
        var length = _w.Load(NewTemp(), IrType.I32, field, 4);
        _w.Br(end);
        Start(end);
        return _w.Phi(NewTemp(), IrType.I32, [(IrIntConstant.I32(0), from), (length, load)]);
    }

    /// <summary>
    /// A view of a variable's bytes is aligned no better than the variable's access (a small set inside a big set: 1; a variable
    /// typecast of a packed field: 1).
    /// </summary>
    private static int AlignmentOf(LView view) => Math.Min(TypeLayout.Of(view.Type).Alignment, AccessAlignment(view.Aggregate));

    /// <summary>CMP-326: both sides are elements of the same array variable (<c>a[i] := a[j]</c>), so they may overlap.</summary>
    private static bool SameArrayVariable(LExpression target, LExpression source)
    {
        static object? Root(LExpression e)
        {
            if (e is not LIndex)
            {
                return null;
            }

            while (e is LIndex or LField)
            {
                e = e is LIndex index ? index.Array : ((LField)e).Record;
            }

            return e switch
            {
                LLocalRef l => l.Local,
                LGlobalRef g => g.Variable,
                LParamRef p => p.Parameter,
                LOuterRef o => o.Variable,
                _ => null,
            };
        }

        return Root(target) is { } root && ReferenceEquals(root, Root(source));
    }

    /// <summary>
    /// CMP-326: an aggregate copy — <c>load</c>/<c>store iN</c> for sizes 1, 2, 4, 8, else <c>llvm.memcpy</c> (<c>llvm.memmove</c>
    /// when <paramref name="mayOverlap"/>) with the type's alignment on both pointers (the source is computed first).
    /// </summary>
    private void Copy(IrValue destination, IrValue source, TypeSymbol type, int destinationAlignment, int sourceAlignment, bool mayOverlap = false)
    {
        var layout = TypeLayout.Of(type);
        if (layout.Size is 1 or 2 or 4 or 8)
        {
            var integer = IrType.Int((int)layout.Size * 8);
            _w.Store(_w.Load(NewTemp(), integer, source, sourceAlignment), destination, destinationAlignment);
            return;
        }

        var memcpy = _module.Intrinsic(mayOverlap ? "llvm.memmove.p0.p0.i64" : "llvm.memcpy.p0.p0.i64", IrType.Void, [IrType.Ptr, IrType.Ptr, IrType.I64, IrType.I1]);
        _w.Call(null, IrType.Void, memcpy,
            [new IrAlignedPointer(destination, destinationAlignment), new IrAlignedPointer(source, sourceAlignment), IrIntConstant.I64(layout.Size), IrIntConstant.I1(false)]);
    }

    /// <summary>
    /// The alignment an access to <paramref name="variable"/> may assume (CMP-309): the type's alignment, reduced inside
    /// records to <c>min(field alignment, packing)</c> and to the alignment of the enclosing record or array storage
    /// (a field of a <c>packed record</c> is accessed with <c>align 1</c>).
    /// </summary>
    internal static int AccessAlignment(LExpression variable) => variable switch
    {
        LField field => Math.Min(Math.Min(field.Type.Alignment, ((RecordTypeSymbol)field.Record.Type).Packing), AccessAlignment(field.Record)),
        LIndex index => Math.Min(index.Type.Alignment, AccessAlignment(index.Array)),
        LView view => AlignmentOf(view),
        LSequence sequence => AccessAlignment(sequence.Value),
        _ => TypeLayout.Of(variable.Type).Alignment,
    };

    /// <summary>CMP-336: <c>getelementptr inbounds i8, ptr set, i64 (x lshr 3)</c> and the bit number <c>x and 7</c> as <c>i8</c>.</summary>
    private (IrValue Address, IrValue Bit) BitAddress(LExpression set, LExpression element)
    {
        var address = Value(set);
        var x = Value(element);
        if (x is IrIntConstant constant)
        {
            var byteAddress = constant.Value < 8 ? address : _w.GetElementPtr(NewTemp(), IrType.I8, address, [IrIntConstant.I64((long)(constant.Value >> 3))]);
            return (byteAddress, new IrIntConstant(IrType.I8, constant.Value & 7));
        }

        var type = (IntegerIrType)x.Type;
        IrValue index = _w.Binary(NewTemp(), IrBinaryOperator.LShr, x, new IrIntConstant(type, 3));
        if (type.Bits < 64)
        {
            index = _w.Cast(NewTemp(), IrCastOperator.ZExt, index, IrType.I64);
        }

        var byteOf = _w.GetElementPtr(NewTemp(), IrType.I8, address, [index]);
        IrValue bit = _w.Binary(NewTemp(), IrBinaryOperator.And, x, new IrIntConstant(type, 7));
        if (type.Bits > 8)
        {
            bit = _w.Cast(NewTemp(), IrCastOperator.Trunc, bit, IrType.I8);
        }

        return (byteOf, bit);
    }

    /// <summary><c>1 shl bit</c> in <c>i8</c> (folded for a constant bit).</summary>
    private IrValue Shl8(IrValue bit) => bit is IrIntConstant c
        ? new IrIntConstant(IrType.I8, BigInteger.One << (int)c.Value)
        : _w.Binary(NewTemp(), IrBinaryOperator.Shl, new IrIntConstant(IrType.I8, 1), bit);

    private bool IsCaptured(Symbol? variable) => variable is not null && _frame?.FieldOf(variable) > 0;

    /// <summary>
    /// CMP-345: field of the owner's frame record, reached from <c>%$link</c> (the parent's frame) by loading field 0 once
    /// per further level; a captured <c>var</c>/<c>out</c> parameter's field holds the pointer to the variable.
    /// </summary>
    private IrValue OuterAddress(LOuterRef outer)
    {
        var frame = _module.Frames[outer.Owner];
        var address = FrameOf(outer.Owner);
        var field = _w.GetElementPtr(NewTemp(), frame.Type, address, [IrIntConstant.I32(0), IrIntConstant.I32(frame.FieldOf(outer.Variable))]);
        return IrEmitter.HoldsAddress(outer.Variable) ? _w.Load(NewTemp(), IrType.Ptr, field, 8) : field;
    }

    /// <summary>The frame record of <paramref name="routine"/>, this routine or an enclosing one (null value when it has none).</summary>
    private IrValue FrameOf(RoutineSymbol routine)
    {
        if (ReferenceEquals(routine, _routine))
        {
            return _frameAddress ?? new IrNullConstant();
        }

        var frame = _link ?? throw new InvalidOperationException($"'{_routine?.Name ?? _name.Text}' is not nested in '{routine.Name}'.");
        for (var level = (RoutineSymbol)_routine!.ContainingSymbol!; !ReferenceEquals(level, routine); level = (RoutineSymbol)level.ContainingSymbol!)
        {
            frame = _w.Load(NewTemp(), IrType.Ptr, frame, 8);
        }

        return frame;
    }

    /// <summary>
    /// A direct call (ABI-082): the static link of a nested callee (its parent's frame) first, then value arguments in storage
    /// form and <c>var</c>/<c>out</c> arguments as addresses (<see cref="LAddrOf"/>). <paramref name="named"/> false discards the result.
    /// </summary>
    /// <remarks>
    /// ABI-080/081: <paramref name="resultAddress"/> is the hidden <c>Result</c> argument; a small aggregate argument is loaded as
    /// <c>iN</c> from its storage; a by-address aggregate argument is already an address (<see cref="LAddrOf"/>, from the lowering).
    /// </remarks>
    private IrLocal? Call(bool named, LRoutineCallee callee, ImmutableArray<LExpression> arguments, IrValue? resultAddress = null)
    {
        var routine = callee.Routine;
        var function = callee.Target is { } target ? Value(target) : null;          // CMP-378: the procedural value first
        var values = ImmutableArray.CreateBuilder<IrValue>();
        if (routine.IsNested)
        {
            values.Add(FrameOf((RoutineSymbol)routine.ContainingSymbol!));
        }

        if (callee.Self is { } selfExpression)
        {
            // CMP-367: Self (the instance, or the VMT of a class method), then the Alloc/Dealloc flag
            var self = Value(selfExpression);
            values.Add(self);
            if (callee.IsVirtual)
            {
                var method = (MethodSymbol)routine;
                var vmt = method.IsClassMethod ? self : _w.Load(NewTemp(), IrType.Ptr, self, 8);
                var slot = _w.GetElementPtr(NewTemp(), IrType.I8, vmt, [IrIntConstant.I64(64 + 8 * method.VmtSlot)]);
                function = _w.Load(NewTemp(), IrType.Ptr, slot, 8);
            }
        }

        if (callee.Flag is { } flag)
        {
            values.Add(new IrIntConstant(IrType.I8, flag));
        }

        if (IrEmitter.HasHiddenResult(routine))
        {
            values.Add(resultAddress ?? throw new InvalidOperationException($"'{routine.Name}' needs a result address."));
        }

        for (var i = 0; i < arguments.Length; i++)
        {
            var parameter = routine.Parameters[i];
            if (!IrEmitter.IsPassedByAddress(parameter) && IrEmitter.IsPassedAsInteger(parameter.Type))
            {
                var address = Address(arguments[i]);
                values.Add(_w.Load(NewTemp(), IrEmitter.ParameterType(parameter), address, AccessAlignment(arguments[i])));
                continue;
            }

            var value = Value(arguments[i]);
            values.Add(IrEmitter.IsPassedByAddress(parameter) ? value : ToStorage(value, parameter.Type));
        }

        return _w.Call(named ? NewTemp() : null, IrEmitter.ReturnType(routine), function ?? _module.Routine(routine), values.ToImmutable());
    }

    /// <summary>Loads a value in SSA form: Booleans are compared <c>ne 0</c> after the load (CMP-305).</summary>
    private IrValue Load(IrValue address, TypeSymbol type, int? alignment = null)
    {
        var layout = TypeLayout.Of(type);
        var loaded = _w.Load(NewTemp(), layout.StorageType, address, alignment ?? layout.Alignment);
        return type is BooleanTypeSymbol ? _w.ICmp(NewTemp(), IrIntPredicate.Ne, loaded, new IrIntConstant((IntegerIrType)layout.StorageType, 0)) : loaded;
    }

    /// <summary>Stores an SSA value: <c>zext</c> for <c>Boolean</c>, <c>sext</c> (all ones) for C-style Booleans (CMP-305).</summary>
    private void Store(IrValue value, IrValue address, TypeSymbol type, int? alignment = null) =>
        _w.Store(ToStorage(value, type), address, alignment ?? TypeLayout.Of(type).Alignment);

    /// <summary>An SSA value in memory/argument form: <c>zext</c> for <c>Boolean</c>, <c>sext</c> (all ones) for C-style Booleans.</summary>
    private IrValue ToStorage(IrValue value, TypeSymbol type)
    {
        if (type is not BooleanTypeSymbol boolean)
        {
            return value;
        }

        var storage = (IntegerIrType)TypeLayout.Of(type).StorageType;
        return value is IrIntConstant constant
            ? new IrIntConstant(storage, constant.Value.IsZero ? 0 : boolean.IsCStyle ? -1 : 1)
            : _w.Cast(NewTemp(), boolean.IsCStyle ? IrCastOperator.SExt : IrCastOperator.ZExt, value, storage);
    }

    // ------------------------------------------------------------------ expressions

    private IrValue Value(LExpression expression)
    {
        switch (expression)
        {
            case LConst constant:
                return IrEmitter.ValueConstant(constant.Value, constant.Type);
            case LLocalRef or LGlobalRef or LParamRef or LOuterRef or LView or LIndex or LField or LDeref or LArrayElement when Lowerer.IsAggregate(expression.Type):
                throw new InvalidOperationException("An aggregate has no SSA value; use its address.");
            case LView view:
                return Load(Address(view), view.Type, AlignmentOf(view));
            case LLocalRef or LGlobalRef or LParamRef or LOuterRef or LIndex or LField or LDeref or LStringIndex or LArrayElement:
                return Load(Address(expression), expression.Type, AccessAlignment(expression));
            case LPointerOffset offset:
            {
                // CMP-342: getelementptr inbounds T, ptr %p, i64 %n
                var pointer = Value(offset.Pointer);
                var index = Value(offset.Index);
                return _w.GetElementPtr(NewTemp(), TypeLayout.Of(offset.ElementType).StorageType, pointer, [index]);
            }

            case LRoutineAddress routine:
                return _module.Routine(routine.Routine);                            // CMP-340: ptr @"link.name"
            case LClassRef reference:
                return _module.Vmt(reference.Class);                                // CMP-367: ptr @"U.TFoo.$vmt"
            case LSequence sequence:
                Statement(sequence.First);
                return Value(sequence.Value);
            case LTestBit test:
            {
                var (address, bit) = BitAddress(test.Address, test.Element);
                var bits = _w.Load(NewTemp(), IrType.I8, address, 1);
                var shifted = bit is IrIntConstant { Value.IsZero: true } ? bits : _w.Binary(NewTemp(), IrBinaryOperator.LShr, bits, bit);
                var one = _w.Binary(NewTemp(), IrBinaryOperator.And, shifted, new IrIntConstant(IrType.I8, 1));
                return _w.ICmp(NewTemp(), IrIntPredicate.Ne, one, new IrIntConstant(IrType.I8, 0));
            }

            case LCall { Callee: LHelperCallee helper } call:
            {
                var arguments = call.Arguments.Select((a, i) => ToStorage(Value(a), helper.Helper.ParameterTypes[i])).ToImmutableArray();
                var storage = TypeLayout.Of(call.Type).StorageType;
                var result = _w.Call(NewTemp(), storage, _module.Helper(helper.Helper), arguments)!;
                return call.Type is BooleanTypeSymbol ? _w.ICmp(NewTemp(), IrIntPredicate.Ne, result, new IrIntConstant((IntegerIrType)storage, 0)) : result;
            }
            case LStringLiteral { Type: StringTypeSymbol { Variant: StringVariant.Unicode } } literal:
                return _module.StringLiteral(literal.Value);
            case LStringLiteral { Type: StringTypeSymbol { Variant: StringVariant.Ansi } ansi } literal:
                return _module.AnsiStringLiteral(literal.Value, ansi.CodePage);
            case LStringLiteral { Type: PointerTypeSymbol } literal:
                return _module.CharPointerLiteral(literal.Value, literal.Type);
            case LAddrOf addressOf:
                return WrittenAddress(addressOf.Variable);                         // a var argument or @x may be written through
            case LStringLength length:
                return StringLength(Value(length.String));
            case LArrayLength length:
                return _w.Cast(NewTemp(), IrCastOperator.Trunc, ArrayLength(Value(length.Array)), IrType.I32);    // ABI-041: as Integer
            case LTypeInfo typeInfo:
                return _module.TypeInfo(typeInfo.Described);
            case LAtomic atomic:
                return Atomic(atomic);
            case LTemp temp:
                _temps.Add(temp.Temp, Value(temp.Value));
                return Value(temp.Body);
            case LTempRef reference:
                return _temps[reference.Temp];
            case LUnary unary:
                return Unary(unary);
            case LBinary binary:
                return Binary(binary);
            case LCompare compare:
                return Compare(compare);
            case LConvert convert:
                return Convert(Value(convert.Operand), convert.Operand.Type, convert.Type);
            case LRangeCheck check:
                return RangeCheck(check);
            case LSelect select:
            {
                var condition = Value(select.Condition);
                var whenTrue = Value(select.WhenTrue);
                return _w.Select(NewTemp(), condition, whenTrue, Value(select.WhenFalse));
            }

            case LCall { Callee: LRoutineCallee callee } call:
            {
                var result = Call(named: true, callee, call.Arguments)!;
                return call.Type is BooleanTypeSymbol
                    ? _w.ICmp(NewTemp(), IrIntPredicate.Ne, result, new IrIntConstant((IntegerIrType)result.Type, 0))
                    : result;
            }

            default:
                throw new InvalidOperationException($"Cannot emit {expression.GetType().Name}.");
        }
    }

    /// <summary>
    /// CMP-412 / ABI-172: the target's address (written: a string character is made unique first), then the operands;
    /// <c>atomicrmw</c> returns the previous value, <c>cmpxchg</c> a pair whose field 0 is the previous value.
    /// </summary>
    private IrValue Atomic(LAtomic atomic)
    {
        var address = WrittenAddress(atomic.Target);
        var alignment = AccessAlignment(atomic.Target);
        var value = Value(atomic.Value);
        if (atomic.Operator == LAtomicOperator.CompareExchange)
        {
            var comparand = Value(atomic.Comparand!);
            var pair = _w.CmpXchg(NewTemp(), address, comparand, value, alignment);
            return _w.ExtractValue(NewTemp(), pair, 0);
        }

        var op = atomic.Operator switch
        {
            LAtomicOperator.Add => IrAtomicOperator.Add,
            LAtomicOperator.Subtract => IrAtomicOperator.Sub,
            _ => IrAtomicOperator.Xchg,
        };
        return _w.AtomicRmw(NewTemp(), op, address, value, alignment);
    }

    private IrValue Unary(LUnary unary)
    {
        var operand = Value(unary.Operand);
        switch (unary.Operator)
        {
            case LUnaryOperator.Negate when operand.Type is FloatingIrType:
                return _w.FNeg(NewTemp(), operand);
            case LUnaryOperator.Negate when unary.IsChecked:
                return Overflow("sub", IsSigned(unary.Type), new IrIntConstant((IntegerIrType)operand.Type, 0), operand);
            case LUnaryOperator.Negate:
                return _w.Binary(NewTemp(), IrBinaryOperator.Sub, new IrIntConstant((IntegerIrType)operand.Type, 0), operand);
            case LUnaryOperator.Not:
                return _w.Binary(NewTemp(), IrBinaryOperator.Xor, operand, new IrIntConstant((IntegerIrType)operand.Type, -1));
            case LUnaryOperator.Abs:
            {
                var name = operand.Type == IrType.Float ? "llvm.fabs.f32" : "llvm.fabs.f64";
                var fabs = _module.Intrinsic(name, operand.Type, [operand.Type]);
                return _w.Call(NewTemp(), operand.Type, fabs, [operand])!;
            }

            case LUnaryOperator.Sqrt:
            {
                var name = operand.Type == IrType.Float ? "llvm.sqrt.f32" : "llvm.sqrt.f64";
                var sqrt = _module.Intrinsic(name, operand.Type, [operand.Type]);
                return _w.Call(NewTemp(), operand.Type, sqrt, [operand])!;
            }

            default:
                throw new InvalidOperationException($"Unknown unary operator {unary.Operator}.");
        }
    }

    private IrValue Binary(LBinary binary)
    {
        switch (binary.Operator)
        {
            case LBinaryOperator.ConditionalAnd or LBinaryOperator.ConditionalOr:
                return ShortCircuit(binary);
            case LBinaryOperator.IntegerDivide or LBinaryOperator.Modulo:
                return Division(binary);
        }

        var left = Value(binary.Left);
        var right = Value(binary.Right);
        if (binary.IsChecked)
        {
            var kind = binary.Operator switch
            {
                LBinaryOperator.Add => "add",
                LBinaryOperator.Subtract => "sub",
                LBinaryOperator.Multiply => "mul",
                _ => throw new InvalidOperationException($"{binary.Operator} has no overflow check."),
            };
            return Overflow(kind, IsSigned(binary.Type), left, right);
        }

        var isReal = left.Type is FloatingIrType;
        IrBinaryOperator op;
        switch (binary.Operator)
        {
            case LBinaryOperator.Add:
                op = isReal ? IrBinaryOperator.FAdd : IrBinaryOperator.Add;
                break;
            case LBinaryOperator.Subtract:
                op = isReal ? IrBinaryOperator.FSub : IrBinaryOperator.Sub;
                break;
            case LBinaryOperator.Multiply:
                op = isReal ? IrBinaryOperator.FMul : IrBinaryOperator.Mul;
                break;
            case LBinaryOperator.Divide:
                op = IrBinaryOperator.FDiv;
                break;
            case LBinaryOperator.And:
                op = IrBinaryOperator.And;
                break;
            case LBinaryOperator.Or:
                op = IrBinaryOperator.Or;
                break;
            case LBinaryOperator.Xor:
                op = IrBinaryOperator.Xor;
                break;
            case LBinaryOperator.Shl or LBinaryOperator.Shr:
            {
                // CMP-312: the count is masked to the operand width
                var type = (IntegerIrType)left.Type;
                IrValue count = right is IrIntConstant constant
                    ? new IrIntConstant(type, constant.Value & (type.Bits - 1))
                    : _w.Binary(NewTemp(), IrBinaryOperator.And, right, new IrIntConstant(type, type.Bits - 1));
                return _w.Binary(NewTemp(), binary.Operator == LBinaryOperator.Shl ? IrBinaryOperator.Shl : IrBinaryOperator.LShr, left, count);
            }

            default:
                throw new InvalidOperationException($"Unknown binary operator {binary.Operator}.");
        }

        return _w.Binary(NewTemp(), op, left, right);
    }

    /// <summary>CMP-317: <c>and</c> → <c>and.rhs.N</c>/<c>and.end.N</c> with a phi; <c>or</c> symmetric.</summary>
    private IrValue ShortCircuit(LBinary binary)
    {
        var isAnd = binary.Operator == LBinaryOperator.ConditionalAnd;
        var kind = isAnd ? "and" : "or";
        var left = Value(binary.Left);
        var leftBlock = _current;
        var id = NewConstruct();
        IrBlock rhs = Block(kind + ".rhs", id), end = Block(kind + ".end", id);
        if (isAnd)
        {
            _w.CondBr(left, rhs, end);
        }
        else
        {
            _w.CondBr(left, end, rhs);
        }

        Start(rhs);
        var right = Value(binary.Right);
        var rightBlock = _current;
        _w.Br(end);

        Start(end);
        return _w.Phi(NewTemp(), IrType.I1, [(IrIntConstant.I1(!isAnd), leftBlock), (right, rightBlock)]);
    }

    /// <summary>CMP-312 division: zero test → <c>_DivZeroError</c>; signed −1 handled without <c>sdiv</c>; tests omitted for non-zero constants.</summary>
    private IrValue Division(LBinary binary)
    {
        var left = Value(binary.Left);
        var right = Value(binary.Right);
        var type = (IntegerIrType)left.Type;
        var isSigned = binary.Type is IntegerTypeSymbol { IsSigned: true };
        var isDiv = binary.Operator == LBinaryOperator.IntegerDivide;
        var op = (isDiv, isSigned) switch
        {
            (true, true) => IrBinaryOperator.SDiv,
            (true, false) => IrBinaryOperator.UDiv,
            (false, true) => IrBinaryOperator.SRem,
            _ => IrBinaryOperator.URem,
        };

        if (right is IrIntConstant { Value.IsZero: false } constant)
        {
            if (isSigned && constant.Value == BigInteger.MinusOne)
            {
                return isDiv ? _w.Binary(NewTemp(), IrBinaryOperator.Sub, new IrIntConstant(type, 0), left) : new IrIntConstant(type, 0);
            }

            return _w.Binary(NewTemp(), op, left, right);
        }

        var id = NewConstruct();
        IrBlock zero = Block("div.zero", id), ok = Block("div.ok", id);
        _w.CondBr(_w.ICmp(NewTemp(), IrIntPredicate.Eq, right, new IrIntConstant(type, 0)), zero, ok);
        ErrorBlock(zero, "_DivZeroError");
        Start(ok);
        if (!isSigned)
        {
            return _w.Binary(NewTemp(), op, left, right);
        }

        IrBlock negative = Block("div.neg", id), divide = Block("div.do", id), end = Block("div.end", id);
        _w.CondBr(_w.ICmp(NewTemp(), IrIntPredicate.Eq, right, new IrIntConstant(type, -1)), negative, divide);
        Start(negative);
        IrValue negated = isDiv ? _w.Binary(NewTemp(), IrBinaryOperator.Sub, new IrIntConstant(type, 0), left) : new IrIntConstant(type, 0);
        _w.Br(end);
        Start(divide);
        var quotient = _w.Binary(NewTemp(), op, left, right);
        _w.Br(end);
        Start(end);
        return _w.Phi(NewTemp(), type, [(negated, negative), (quotient, divide)]);
    }

    /// <summary>
    /// CMP-312 <c>{$Q+}</c>: <c>llvm.[s|u]&lt;kind&gt;.with.overflow.iN</c>, flag (field 1) → block <c>ovf.error.N</c> calling
    /// <c>_OverflowError</c>, else <c>ovf.ok.N</c> continuing with the result (field 0).
    /// </summary>
    private IrValue Overflow(string kind, bool signed, IrValue left, IrValue right)
    {
        var type = (IntegerIrType)left.Type;
        var pairType = new StructIrType([type, IrType.I1]);
        var intrinsic = _module.Intrinsic($"llvm.{(signed ? "s" : "u")}{kind}.with.overflow.i{type.Bits}", pairType, [type, type]);
        var pair = _w.Call(NewTemp(), pairType, intrinsic, [left, right])!;
        var overflowed = _w.ExtractValue(NewTemp(), pair, 1);
        var id = NewConstruct();
        IrBlock error = Block("ovf.error", id), ok = Block("ovf.ok", id);
        _w.CondBr(overflowed, error, ok);
        ErrorBlock(error, "_OverflowError");
        Start(ok);
        return _w.ExtractValue(NewTemp(), pair, 0);
    }

    /// <summary>
    /// CMP-314 <c>{$R+}</c>: <c>icmp ule (sub v, lo), hi − lo</c> (no <c>sub</c> when <c>lo = 0</c>) → <c>range.ok.N</c>, else block
    /// <c>range.error.N</c> calling <c>_RangeError</c>. The value itself is the result.
    /// </summary>
    private IrValue RangeCheck(LRangeCheck check)
    {
        var value = Value(check.Value);
        var type = (IntegerIrType)value.Type;
        var offset = check.Low.IsZero ? value : _w.Binary(NewTemp(), IrBinaryOperator.Sub, value, new IrIntConstant(type, check.Low));
        var inRange = _w.ICmp(NewTemp(), IrIntPredicate.Ule, offset, new IrIntConstant(type, check.High - check.Low));
        var id = NewConstruct();
        IrBlock error = Block("range.error", id), ok = Block("range.ok", id);
        _w.CondBr(inRange, ok, error);
        ErrorBlock(error, "_RangeError");
        Start(ok);
        return value;
    }

    /// <summary><paramref name="block"/>: <c>call void @"System.&lt;helper&gt;"() #1</c> + <c>unreachable</c>.</summary>
    private void ErrorBlock(IrBlock block, string helper)
    {
        Start(block);
        _w.Call(null, IrType.Void, _module.NoReturnHelper(helper), [], IrEmitter.NoReturnGroup);
        _w.Unreachable();
    }

    /// <summary>CMP-315: signed/unsigned predicates by type (chars and Booleans unsigned), reals ordered except <c>une</c>; signed vs <c>UInt64</c> mathematically.</summary>
    private IrValue Compare(LCompare compare)
    {
        var left = Value(compare.Left);
        var right = Value(compare.Right);
        var op = compare.Operator;
        if (left.Type is FloatingIrType)
        {
            var predicate = op switch
            {
                LCompareOperator.Equal => IrFloatPredicate.Oeq,
                LCompareOperator.NotEqual => IrFloatPredicate.Une,
                LCompareOperator.Less => IrFloatPredicate.Olt,
                LCompareOperator.LessOrEqual => IrFloatPredicate.Ole,
                LCompareOperator.Greater => IrFloatPredicate.Ogt,
                _ => IrFloatPredicate.Oge,
            };
            return _w.FCmp(NewTemp(), predicate, left, right);
        }

        var leftSigned = IsSigned(compare.Left.Type);
        var rightSigned = IsSigned(compare.Right.Type);
        if (leftSigned == rightSigned)
        {
            return _w.ICmp(NewTemp(), IntPredicate(op, leftSigned), left, right);
        }

        // LANG-302 / CMP-315: put the signed operand on the left; a negative one is below every unsigned value
        if (!leftSigned)
        {
            (left, right) = (right, left);
            op = Mirror(op);
        }

        var unsigned = _w.ICmp(NewTemp(), IntPredicate(op, signed: false), left, right);
        if (op is LCompareOperator.Less or LCompareOperator.LessOrEqual or LCompareOperator.NotEqual)
        {
            var negative = _w.ICmp(NewTemp(), IrIntPredicate.Slt, left, new IrIntConstant((IntegerIrType)left.Type, 0));
            return _w.Binary(NewTemp(), IrBinaryOperator.Or, negative, unsigned);
        }

        var nonNegative = _w.ICmp(NewTemp(), IrIntPredicate.Sge, left, new IrIntConstant((IntegerIrType)left.Type, 0));
        return _w.Binary(NewTemp(), IrBinaryOperator.And, nonNegative, unsigned);
    }

    private static bool IsSigned(TypeSymbol type) => type is IntegerTypeSymbol { IsSigned: true };

    private static LCompareOperator Mirror(LCompareOperator op) => op switch
    {
        LCompareOperator.Less => LCompareOperator.Greater,
        LCompareOperator.LessOrEqual => LCompareOperator.GreaterOrEqual,
        LCompareOperator.Greater => LCompareOperator.Less,
        LCompareOperator.GreaterOrEqual => LCompareOperator.LessOrEqual,
        _ => op,
    };

    private static IrIntPredicate IntPredicate(LCompareOperator op, bool signed) => op switch
    {
        LCompareOperator.Equal => IrIntPredicate.Eq,
        LCompareOperator.NotEqual => IrIntPredicate.Ne,
        LCompareOperator.Less => signed ? IrIntPredicate.Slt : IrIntPredicate.Ult,
        LCompareOperator.LessOrEqual => signed ? IrIntPredicate.Sle : IrIntPredicate.Ule,
        LCompareOperator.Greater => signed ? IrIntPredicate.Sgt : IrIntPredicate.Ugt,
        _ => signed ? IrIntPredicate.Sge : IrIntPredicate.Uge,
    };

    /// <summary>
    /// CMP-313 between scalar SSA values: integers and chars extend by the source's signedness (chars unsigned) or truncate;
    /// Booleans become 0/1 (<c>zext</c>) and an ordinal becomes a Boolean by <c>ne 0</c>; integer → real <c>sitofp</c>/<c>uitofp</c>;
    /// <c>fpext</c>/<c>fptrunc</c>; pointer ↔ integer <c>ptrtoint</c>/<c>inttoptr</c>.
    /// </summary>
    private IrValue Convert(IrValue value, TypeSymbol from, TypeSymbol to)
    {
        var target = IrEmitter.ValueType(to);
        if (value.Type == target && (from is BooleanTypeSymbol) == (to is BooleanTypeSymbol))
        {
            return value;
        }

        if (value is IrIntConstant constant && target is IntegerIrType integerTarget)
        {
            return to is BooleanTypeSymbol ? IrIntConstant.I1(!constant.Value.IsZero) : new IrIntConstant(integerTarget, constant.Value);
        }

        switch (value.Type, target)
        {
            case (IntegerIrType, IntegerIrType { Bits: 1 }):
                return _w.ICmp(NewTemp(), IrIntPredicate.Ne, value, new IrIntConstant((IntegerIrType)value.Type, 0));
            case (IntegerIrType source, IntegerIrType destination):
            {
                var op = destination.Bits < source.Bits ? IrCastOperator.Trunc
                    : IsSigned(from) ? IrCastOperator.SExt : IrCastOperator.ZExt;
                return _w.Cast(NewTemp(), op, value, target);
            }

            case (IntegerIrType, FloatingIrType):
                return _w.Cast(NewTemp(), IsSigned(from) ? IrCastOperator.SIToFP : IrCastOperator.UIToFP, value, target);
            case (FloatingIrType { Kind: FloatingKind.Float }, FloatingIrType { Kind: FloatingKind.Double }):
                return _w.Cast(NewTemp(), IrCastOperator.FPExt, value, target);
            case (FloatingIrType { Kind: FloatingKind.Double }, FloatingIrType { Kind: FloatingKind.Float }):
                return _w.Cast(NewTemp(), IrCastOperator.FPTrunc, value, target);
            case (PointerIrType, IntegerIrType):
                return _w.Cast(NewTemp(), IrCastOperator.PtrToInt, value, target);
            case (IntegerIrType, PointerIrType):
                return _w.Cast(NewTemp(), IrCastOperator.IntToPtr, value, target);
            default:
                throw new NotSupportedException($"Conversion from '{from.Name}' to '{to.Name}' is not emitted yet.");
        }
    }
}
