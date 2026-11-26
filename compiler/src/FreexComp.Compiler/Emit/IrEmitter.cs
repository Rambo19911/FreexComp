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

    /// <summary>
    /// <c>attributes #3 = { noinline optnone … }</c>: the outlined <c>$fin</c> routines of CMP-392 stay opaque calls. When the
    /// optimiser sees a finally part that always raises, it infers <c>noreturn</c>, deletes the funclet's <c>cleanupret</c> and with
    /// it the unwind edge to an enclosing <c>catchswitch</c> of the same function, so the <c>__C_specific_handler</c> scope table
    /// loses the except entry and the new exception is not caught at <c>-O2</c> (C-091 finding; <c>optnone</c> stops the inference,
    /// <c>noinline</c> keeps the body out of the funclet).
    /// </summary>
    public const int FinallyGroup = 3;

    /// <summary>
    /// CMP-398: the module flag of a module with try regions. With it the scope tables cover what lies between the
    /// <c>llvm.seh.try.begin</c> / <c>llvm.seh.try.end</c> invokes of a region, so a hardware fault of an instruction there (not
    /// only of a call) reaches the region's handler; without it the back end drops the two intrinsics.
    /// </summary>
    public const string AsyncEhFlag = "!{i32 2, !\"eh-asynch\", i32 1}";

    /// <summary>
    /// <c>attributes #4 = { noinline … }</c>: ABI-125 — every routine of unit <c>System</c> whose name starts with <c>_</c> (the
    /// compiler helpers; <c>_Raise</c> is the debugger's raise hook) is never inlined (C-193).
    /// </summary>
    public const int SystemHelperGroup = 4;

    internal static int AttributeGroupOf(RoutineSymbol routine) =>
        IsOutlinedFinally(routine) ? FinallyGroup
        : MemoryRoutineNames.Contains(Mangler.Routine(routine).Text) ? NoBuiltinsGroup
        : IsSystemHelper(routine) ? SystemHelperGroup
        : RoutineGroup;

    private static bool IsSystemHelper(RoutineSymbol routine) =>
        routine.Name.StartsWith('_') && routine.ContainingSymbol is UnitSymbol { Name: var unit } && string.Equals(unit, "System", StringComparison.OrdinalIgnoreCase);

    /// <summary>A <c>$fin</c> routine of the Lowerer (CMP-392): the only routines whose name holds a <c>$</c>.</summary>
    internal static bool IsOutlinedFinally(RoutineSymbol routine) => routine.Name.Contains('$', StringComparison.Ordinal);

    /// <param name="unitTable">
    /// Program only: the <c>$units</c> entries in initialisation order (CMP-355); default = empty table.
    /// </param>
    /// <param name="debug">CMP-420…424: the debug metadata to emit (<c>-g</c>); null → none at all.</param>
    /// <returns>CMP-425: with <paramref name="debug"/> the unit's sidecar fragment (JSON text), else null.</returns>
    public static string? EmitUnit(LoweredUnit unit, IIrWriter writer, ImmutableArray<UnitTableEntry> unitTable = default, DebugInfoOptions? debug = null)
    {
        // CMP-1012: the one place where the target's exception model selects the lowering of handlers and cleanups
        switch (unit.Target.ExceptionModel)
        {
            case ExceptionModel.SehTable:
                break;                                                      // funclet pads and the filter routine (CMP-390…399): the code below
            default:
                throw new NotSupportedException($"Exception model {unit.Target.ExceptionModel} of target '{unit.Target.Id}' has no lowering.");
        }

        var path = unit.Unit.Locations.IsEmpty ? unit.Unit.Name + ".pas" : unit.Unit.Locations[0].Path;
        writer.BeginModule(unit.Unit.Name, Path.GetFileName(path));
        if (string.Equals(unit.Unit.Name, "System", StringComparison.OrdinalIgnoreCase))
        {
            // CMP-611 / CMP-1013: the target's stack probe, appended verbatim (ADR 001; never imported, never in System.pas)
            foreach (var line in unit.Target.StackProbeAssembly)
            {
                writer.AppendModuleAsm(line);
            }
        }

        var interfaceMembers = unit.Unit.InterfaceMembers.Select(m => m is ConstantSymbol { Storage: { } storage } ? storage : m).ToHashSet();
        // C-119: what the unit's generic bodies name is reached from instances in other modules (external, like the interface)
        interfaceMembers.UnionWith(Binding.GenericSupport.Of(unit.Unit).Select(Binding.GenericSupport.Linked).OfType<Symbol>());
        // C-116 (CMP-385): the generic instances the unit uses are emitted here too, linkonce_odr
        ImmutableArray<TypeSymbol> instances;
        lock (unit.Unit.UsedInstances)
        {
            // C-344 (LANG-775): the types nested in an instance belong to it and are emitted with it
            instances = [.. unit.Unit.UsedInstances.OfType<TypeSymbol>().SelectMany(TypeMembers.SelfAndNestedTypes)];
        }

        IrLinkage LinkageOf(TypeSymbol type) => TypeMembers.EnclosingInstance(type) is not null ? IrLinkage.LinkOnceOdr
            : interfaceMembers.Contains(TypeMembers.Outermost(type)) ? IrLinkage.External : IrLinkage.Internal;     // CMP-1391: nested types too
        var classes = Binding.DeclarationBinder.ClassesOf(unit.Unit).Concat(instances.OfType<ClassTypeSymbol>()).Distinct().ToImmutableArray();
        var module = new ModuleState(writer)
        {
            Target = unit.Target,
            UnitName = unit.Unit.Name,
            ExportLocations = unit.Unit.ExportLocations,
            OwnClasses = [.. classes],
            Debug = debug is null ? null : new DebugInfoEmitter(debug, unit.Target, unit.Unit.Name, path, unit.Routines.Concat(unit.Bodies).Select(b => b.Source).OfType<Syntax.SyntaxNode>()
                .Select(s => s.SyntaxTree.Text).FirstOrDefault(t => Text.PathUtil.Comparer.Equals(t.Path, path))),
        };
        foreach (var body in unit.Routines)
        {
            module.DefinedRoutines.Add(Mangler.Routine(body.Routine!).Text);
            module.LinkNameOwners[Mangler.Routine(body.Routine!).Text] = body.Routine!;
        }

        // globals are defined after the functions (CMP-301: string literals, found while emitting, come first)
        // typed constants are read-only globals (LANG-344); those of routines follow the unit's own
        var variables = unit.Unit.InterfaceMembers.Concat(unit.Unit.ImplementationMembers)
            .Select(m => m is ConstantSymbol { Storage: { } storage } ? storage : m).OfType<VariableSymbol>().Concat(unit.Statics)
            .Concat(Binding.DeclarationBinder.ClassVariablesOf(unit.Unit))                             // CMP-370: class vars are globals
            .Concat(instances.SelectMany(i => i switch { ClassTypeSymbol c => c.ClassVariables, RecordTypeSymbol r => r.ClassVariables, _ => [] }))
            .Concat(Lowerer.InstanceClassVariables(unit.Unit))                                        // C-222: the owner finalises them
            .Where(v => !v.IsThreadVar).Distinct().ToImmutableArray();                                            // threadvars: ABI-180/181
        foreach (var variable in variables)
        {
            module.Globals[variable] = new IrGlobal(Mangler.GlobalVariable(variable));
        }

        foreach (var (variable, name) in unit.BodyGlobals)
        {
            module.Globals[variable] = new IrGlobal(name);                      // unit-level handler variables (C-091)
        }

        module.BodyGlobals = unit.BodyGlobals;

        // CMP-345 frame record types, in definition order (CMP-301: named struct types first)
        foreach (var body in unit.Routines)
        {
            if (body.Frame is { } frame)
            {
                ImmutableArray<TypeLayout> fields = [TypeLayout.Pointer(unit.Target), .. frame.Variables.Select(v => FieldLayout(unit.Target, v))];
                var (structType, indexes) = TypeLayout.Struct(unit.Target, fields);
                var type = writer.DeclareStruct(Mangler.FrameType(body.Routine!), structType);
                module.Frames[body.Routine!] = new FrameRecord(type, frame.Variables, indexes)
                {
                    Offsets = TypeLayout.StructOffsets(fields).Offsets,
                    Alignment = fields.Max(f => Math.Min(f.Alignment, 8)),
                };
            }
        }

        // CMP-301: definition order, each nested routine directly after its parent
        foreach (var body in InEmissionOrder(unit.Routines))
        {
            var routine = body.Routine!;
            var linkage = routine is MethodSymbol { Owner: var owner } genericCandidate
                && (TypeMembers.EnclosingInstance(owner) is not null || genericCandidate.GenericMethodDefinition is not null) ? IrLinkage.LinkOnceOdr
                : routine.ExplicitLinkName is not null || (!routine.IsNested && interfaceMembers.Contains(routine))
                || routine is MethodSymbol method && interfaceMembers.Contains(TypeMembers.Outermost(method.Owner))
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

            if (unit.Unit.IsLibrary)
            {
                // CMP-1050: a library module has no _start; its entry routine, then the export thunks (last, in ordinal order)
                EmitLibraryEntry(module, units.Length);
                foreach (var export in unit.Unit.Exports)
                {
                    EmitExportThunk(module, export);
                }
            }
            else
            {
                EmitStart(module, units.Length);
            }
        }
        else
        {
            // CMP-350: every unit defines both, possibly empty; $init registers the threadvar descriptor first (ABI-180)
            var initialization = unit.Initialization ?? EmptyBody();
            if (unit.FormRegistration is { } forms)
            {
                initialization = initialization with { Body = new LBlock([forms, initialization.Body]) };          // CMP-350: after _TlsRegister
            }

            if (unit.TlsRegistration is { } register)
            {
                initialization = initialization with { Body = new LBlock([register, initialization.Body]) };
            }

            new FunctionEmitter(module, initialization, IrLinkage.External, Mangler.UnitInit(unit.Unit)).Emit();
            module.DeclarePending();
            new FunctionEmitter(module, unit.Finalization ?? EmptyBody(), IrLinkage.External, Mangler.UnitFinal(unit.Unit)).Emit();
            module.DeclarePending();
        }

        // CMP-366: the VMTs are built before the literals are written (class names are UnicodeString literals)
        var vmts = classes.Select(c => (Class: c, Constant: Vmt(module, c))).ToImmutableArray();
        module.DeclarePending();

        // CMP-415 (C-114): the {$FORM} blobs and their table (the names are UnicodeString literals)
        var formEntry = new StructIrType([IrType.Ptr, IrType.Ptr, module.NativeInt]);
        var formTable = unit.Forms.IsEmpty ? null : new IrArrayConstant(new ArrayIrType(unit.Forms.Length, formEntry), [.. unit.Forms.Select((f, i) =>
            (IrConstant)new IrStructConstant(formEntry, [module.StringLiteral(f.Name), new IrGlobal(Mangler.FormBlob(unit.Unit, i)), module.Native(f.Data.Length)]))]);

        // the globals' initialisers too: typed constants may hold string literals (C-203; PChar constants since C-063)
        var initializers = variables.Select(v => v.Initializer is { } value && v.Type.Size > 0
            ? StorageConstant(module, value, v.Type) : new IrZeroConstant(TypeLayout.Allocated(v.Type).StorageType)).ToImmutableArray();

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
            writer.DefineGlobal(new IrGlobalDefinition(name, IrLinkage.LinkOnceOdr, IsConstant: true, block, Alignment: module.BlockAlign));     // CMP-405, CMP-365
        }

        for (var i = 0; i < unit.Forms.Length; i++)
        {
            var data = unit.Forms[i].Data;
            var bytes = new ArrayIrType(data.Length, IrType.I8);
            writer.DefineGlobal(new IrGlobalDefinition(Mangler.FormBlob(unit.Unit, i), IrLinkage.Private, IsConstant: true,
                new IrArrayConstant(bytes, [.. data.Select(b => (IrConstant)new IrIntConstant(IrType.I8, b))]), Alignment: 1));
        }

        if (formTable is not null)
        {
            writer.DefineGlobal(new IrGlobalDefinition(Mangler.FormTable(unit.Unit), IrLinkage.Internal, IsConstant: true, formTable, Alignment: module.PtrAlign));
        }

        foreach (var (type, name, block) in module.ClassInfos)
        {
            // codegen.md C-112 decision: defined only by the class's unit, with the VMT's linkage
            writer.DefineGlobal(new IrGlobalDefinition(name, LinkageOf(type), IsConstant: true, block, Alignment: module.BlockAlign));
        }

        foreach (var intf in module.Guids)
        {
            writer.DefineGlobal(new IrGlobalDefinition(Mangler.Guid(intf), IrLinkage.LinkOnceOdr, IsConstant: true, GuidConstant(intf), Alignment: 4));   // CMP-373
        }

        foreach (var (type, constant) in vmts)
        {
            writer.DefineGlobal(new IrGlobalDefinition(Mangler.Vmt(type), LinkageOf(type), IsConstant: true, constant, Alignment: module.PtrAlign));
        }

        foreach (var type in classes)
        {
            EmitInterfaceTables(module, type, LinkageOf(type));    // C-098: IMTs after the VMTs
        }

        if (unit.Unit.IsProgram)
        {
            DefineUnitTable(module, units);
        }

        foreach (var (variable, initializer) in variables.Zip(initializers))
        {
            var layout = TypeLayout.Allocated(variable.Type);
            var name = Mangler.GlobalVariable(variable);
            var linkage = variable.ContainingSymbol is TypeSymbol owningType && TypeMembers.EnclosingInstance(owningType) is not null ? IrLinkage.LinkOnceOdr
                : interfaceMembers.Contains(variable) || variable.ExplicitLinkName is not null
                || variable.ContainingSymbol is (ClassTypeSymbol or RecordTypeSymbol or HelperTypeSymbol) and TypeSymbol owner && interfaceMembers.Contains(TypeMembers.Outermost(owner)) ? IrLinkage.External : IrLinkage.Internal;
            writer.DefineGlobal(new IrGlobalDefinition(name, linkage, IsConstant: variable.IsReadOnly, initializer, layout.Alignment)
            {
                DebugVariable = module.Debug?.GlobalVariable(variable, name.Text, isLocal: linkage != IrLinkage.External),       // DBG-140
            });
        }

        foreach (var (variable, name) in unit.BodyGlobals)
        {
            // handler variables (ptr) and the hidden with / for…in locals of unit-level bodies (any storage, C-109, C-111)
            var layout = TypeLayout.Allocated(variable.Type);
            IrConstant zero = layout.StorageType is PointerIrType ? new IrNullConstant() : new IrZeroConstant(layout.StorageType);
            writer.DefineGlobal(new IrGlobalDefinition(name, IrLinkage.Internal, IsConstant: false, zero, Alignment: layout.Alignment));
        }

        if (!unit.ThreadVars.IsEmpty)
        {
            // ABI-180 / CMP-414: { i64 Size, i64 Offset = -1 }; external when other units reach interface threadvars through it
            var size = (unit.Unit.ThreadVarSize + 15) / 16 * 16;
            writer.DefineGlobal(new IrGlobalDefinition(Mangler.TlsDescriptor(unit.Unit),
                unit.ThreadVars.Any(interfaceMembers.Contains) ? IrLinkage.External : IrLinkage.Internal, IsConstant: false,
                new IrStructConstant(module.TlsDescriptorType, [module.Native(size), module.Native(-1)]), Alignment: module.PtrAlign));
        }

        module.DeclarePending();                                        // routines whose address a typed constant holds
        foreach (var (name, type) in module.ExternalVariables)
        {
            var layout = TypeLayout.Of(type);
            writer.DeclareGlobal(name, layout.StorageType, layout.Alignment);
        }

        foreach (var name in module.ExternalVmts)
        {
            writer.DeclareGlobal(name, IrType.Ptr, module.PtrAlign);            // another unit's VMT: only its address is used
        }

        foreach (var name in module.ExternalClassInfos)
        {
            writer.DeclareGlobal(name, IrType.Ptr, module.BlockAlign);          // another unit's class RTTI block (C-112)
        }

        foreach (var name in module.ExternalTlsDescriptors)
        {
            writer.DeclareGlobal(name, module.TlsDescriptorType, module.PtrAlign);      // another unit's threadvar descriptor (CMP-414)
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

        if (unit.Routines.Any(r => IsOutlinedFinally(r.Routine!)))
        {
            writer.DefineAttributeGroup(FinallyGroup,
                [IrAttribute.Enum("noinline"), IrAttribute.Enum("optnone"), IrAttribute.Enum("uwtable"), IrAttribute.String("frame-pointer", "all")]);
        }

        if (unit.Routines.Any(r => AttributeGroupOf(r.Routine!) == SystemHelperGroup))
        {
            writer.DefineAttributeGroup(SystemHelperGroup, [IrAttribute.Enum("noinline"), IrAttribute.Enum("uwtable"), IrAttribute.String("frame-pointer", "all")]);
        }

        if (module.Debug is { } flagsOwner)
        {
            flagsOwner.Finish(writer, module.UsesAsyncEh);
        }
        else if (module.UsesAsyncEh)
        {
            writer.AppendMetadata("!llvm.module.flags = !{!0}");
            writer.AppendMetadata("!0 = " + AsyncEhFlag);
        }

        writer.EndModule();
        if (module.Debug is not { } debugInfo)
        {
            return null;
        }

        foreach (var @class in classes.Where(c => !c.IsGenericDefinition))
        {
            debugInfo.Sidecar.TypeId(@class);            // DBG-168: every class of the unit, for the dynamic type of an instance
        }

        foreach (var alias in unit.Unit.TypeAliases)
        {
            debugInfo.Sidecar.AliasId(alias, unit.Unit.Name);   // C-268: every plain alias the unit declares
        }

        return debugInfo.Sidecar.ToJson();
    }

    private static readonly LinkName UnitsName = new("$units", AlwaysQuote: true);
    private static readonly LinkName MainName = new("$main", AlwaysQuote: true);
    private static StructIrType UnitEntryType => AbiLayout.UnitEntry;

    private static LoweredBody EmptyBody() => new(null, [], new LBlock([]));

    /// <summary>
    /// CMP-355: <c>internal void $main()</c> = <c>_InitUnits</c>, then the main block. The main block's managed slots are
    /// zeroed (CMP-325 rule 1) before <c>_InitUnits</c>: an exception escaping a unit initialization runs <c>$main</c>'s
    /// cleanup, which must see <c>nil</c> slots (C-241).
    /// </summary>
    private static void EmitMain(ModuleState module, LoweredUnit unit)
    {
        var main = unit.Bodies.IsEmpty ? EmptyBody() : unit.Bodies[0];
        var zeroed = main.Body.Statements.TakeWhile(s => s is LZero).ToArray();
        var rest = new LBlock([.. main.Body.Statements.Skip(zeroed.Length)]);
        var initUnits = new LCallStatement(new LHelperCallee(new RuntimeHelper("_InitUnits", [], ReturnType: null, IsNoReturn: false)), []);
        LStatement[] statements = [.. zeroed, initUnits, .. unit.TlsRegistration is { } register ? [register] : Array.Empty<LStatement>(),   // ABI-180
            .. unit.FormRegistration is { } forms ? [forms] : Array.Empty<LStatement>(), rest];                                // ABI-150 (C-114)
        new FunctionEmitter(module, main with { Body = new LBlock([.. statements]) }, IrLinkage.Internal, MainName).Emit();
        module.DeclarePending();
    }

    /// <summary>
    /// CMP-355: <c>_Startup($units, n)</c>, <c>invoke $main()</c>, <c>_Halt0()</c>; the top-level handler (LANG-832/833, C-095)
    /// catches every exception escaping the unit initializations or the main block with the one filter, makes it current
    /// (<c>_BeginCatch</c>) and ends in <c>_UnhandledException</c> (ABI-124: report, exit code 217). The PE entry point
    /// (<c>/entry:_start</c>), called by the OS like an ordinary function.
    /// </summary>
    private static void EmitStart(ModuleState module, int unitCount)
    {
        var w = module.Writer;
        var startup = module.Function(new IrFunctionDeclaration(Mangler.RuntimeHelper("_Startup"), IrType.Void, [IrType.Ptr, module.NativeInt]));
        var halt0 = module.NoReturnHelper("_Halt0");
        var filter = module.Function(new IrFunctionDeclaration(Mangler.RuntimeHelper("_SehFilter"), IrType.I32, [IrType.Ptr, IrType.Ptr]));
        var beginCatch = module.Function(new IrFunctionDeclaration(Mangler.RuntimeHelper("_BeginCatch"), IrType.Ptr, []));
        var unhandled = module.NoReturnHelper("_UnhandledException");
        IrBlock done = new("done"), dispatch = new("catch.dispatch"), @catch = new("catch"), unhandledBlock = new("unhandled");
        w.BeginFunction(new IrFunctionDefinition(new LinkName(module.Target.EntrySymbol, AlwaysQuote: false), IrLinkage.External, IrType.Void, [], RoutineGroup));
        w.AppendBlock(new IrBlock("entry"));
        w.Call(null, IrType.Void, startup, [new IrGlobal(UnitsName), module.Native(unitCount)]);
        TryRegion(module, new IrGlobal(MainName), done, dispatch);
        w.AppendBlock(done);
        w.Call(null, IrType.Void, halt0, [], NoReturnGroup);
        w.Unreachable();
        w.AppendBlock(dispatch);
        var catchSwitch = w.CatchSwitch("cs", @catch, unwind: null);
        w.AppendBlock(@catch);
        w.CatchRet(w.CatchPad("cp", catchSwitch, [filter]), unhandledBlock);
        w.AppendBlock(unhandledBlock);
        w.Call("t1", IrType.Ptr, beginCatch, []);
        w.Call(null, IrType.Void, unhandled, [], NoReturnGroup);
        w.Unreachable();
        w.EndFunction();
        module.DeclarePending();
    }

    /// <summary>
    /// CMP-398: the region of an entry routine — <c>llvm.seh.try.begin</c>, the call of <paramref name="callee"/>,
    /// <c>llvm.seh.try.end</c>, each an invoke unwinding to <paramref name="pad"/>; continues at <paramref name="next"/>.
    /// </summary>
    private static void TryRegion(ModuleState module, IrValue callee, IrBlock next, IrBlock pad)
    {
        var w = module.Writer;
        IrBlock begun = new("try.begun"), called = new("try.called");
        module.UsesAsyncEh = true;
        w.Invoke(null, IrType.Void, module.Intrinsic("llvm.seh.try.begin", IrType.Void, []), [], begun, pad);
        w.AppendBlock(begun);
        w.Invoke(null, IrType.Void, callee, [], called, pad);
        w.AppendBlock(called);
        w.Invoke(null, IrType.Void, module.Intrinsic("llvm.seh.try.end", IrType.Void, []), [], next, pad);
    }

    /// <summary>
    /// CMP-1051: the entry routine of a library module (<see cref="Targets.TargetInfo.LibraryEntrySymbol"/>), called by the
    /// operating system with the module handle, the notification code and the reserved pointer. Every decision is the run-time
    /// library's (<c>_DllEntry</c>, ABI-190): a non-zero answer means "first process attach", then <c>$main</c> runs (unit
    /// initializations, library block) and <c>_DllInitDone</c> follows; an exception escaping it is caught with the one filter,
    /// made current (<c>_BeginCatch</c>) and turned into the result 0, "load failed", after <c>_DllInitFailed</c>.
    /// </summary>
    private static void EmitLibraryEntry(ModuleState module, int unitCount)
    {
        var w = module.Writer;
        var dllEntry = module.Function(new IrFunctionDeclaration(Mangler.RuntimeHelper("_DllEntry"), IrType.I32,
            [IrType.Ptr, IrType.I32, IrType.Ptr, IrType.Ptr, module.NativeInt]));
        var initDone = module.Function(new IrFunctionDeclaration(Mangler.RuntimeHelper("_DllInitDone"), IrType.Void, []));
        var initFailed = module.Function(new IrFunctionDeclaration(Mangler.RuntimeHelper("_DllInitFailed"), IrType.Void, []));
        var filter = module.Function(new IrFunctionDeclaration(Mangler.RuntimeHelper("_SehFilter"), IrType.I32, [IrType.Ptr, IrType.Ptr]));
        var beginCatch = module.Function(new IrFunctionDeclaration(Mangler.RuntimeHelper("_BeginCatch"), IrType.Ptr, []));
        IrBlock attach = new("attach"), attached = new("attached"), ok = new("ok"), dispatch = new("catch.dispatch"), @catch = new("catch"),
            failed = new("failed");
        var parameters = w.BeginFunction(new IrFunctionDefinition(new LinkName(module.Target.LibraryEntrySymbol, AlwaysQuote: false), IrLinkage.External,
            IrType.I32, [new IrParameter(IrType.Ptr, "hinst"), new IrParameter(IrType.I32, "reason"), new IrParameter(IrType.Ptr, "reserved")], RoutineGroup));
        w.AppendBlock(new IrBlock("entry"));
        var init = w.Call("init", IrType.I32, dllEntry, [parameters[0], parameters[1], parameters[2], new IrGlobal(UnitsName), module.Native(unitCount)])!;
        w.CondBr(w.ICmp("run", IrIntPredicate.Ne, init, IrIntConstant.I32(0)), attach, ok);
        w.AppendBlock(attach);
        TryRegion(module, new IrGlobal(MainName), attached, dispatch);
        w.AppendBlock(attached);
        w.Call(null, IrType.Void, initDone, []);
        w.Br(ok);
        w.AppendBlock(ok);
        w.Ret(IrIntConstant.I32(1));
        w.AppendBlock(dispatch);
        var catchSwitch = w.CatchSwitch("cs", @catch, unwind: null);
        w.AppendBlock(@catch);
        w.CatchRet(w.CatchPad("cp", catchSwitch, [filter]), failed);
        w.AppendBlock(failed);
        w.Call("t1", IrType.Ptr, beginCatch, []);
        w.Call(null, IrType.Void, initFailed, []);
        w.Ret(IrIntConstant.I32(0));
        w.EndFunction();
        module.DeclarePending();
    }

    /// <summary>
    /// CMP-1053, 1054: one function with external linkage whose link name is the export name and whose IR signature is the
    /// routine's: a call of the routine and a return. A routine with a hidden result pointer (ABI-081) gets a thunk that returns
    /// that pointer, as the platform convention requires of a callee. No thunk when the routine's own <c>public name</c> is the
    /// export name: its symbol is the export. Thunks carry no debug-info subprogram.
    /// </summary>
    private static void EmitExportThunk(ModuleState module, ExportInfo export)
    {
        var routine = export.RoutineSymbol;
        if (routine.ExplicitLinkName == export.Name)
        {
            module.Routine(routine);                                    // a used unit's routine: declared, so the reference is visible
            module.DeclarePending();
            return;
        }

        // C-293: the thunk is a definition of the symbol <export name>; the module must not already declare (an imported routine
        // the library file calls) or define (another routine's public name) a function of that name
        if (module.LinkNameOwners.TryGetValue(export.Name, out var owner))
        {
            throw new EmitDiagnosticException("E2218", module.ExportLocations[export.Name], export.Name, owner.Name);
        }

        var w = module.Writer;
        var hidden = HasHiddenResult(routine);
        var names = new HashSet<string>(StringComparer.Ordinal);
        string Unique(string name)
        {
            var candidate = name;
            for (var n = 1; !names.Add(candidate); n++)
            {
                candidate = name + "." + n;
            }

            return candidate;
        }

        // ABI-082 for a plain routine: [hidden Result], the declared parameters (an open array with its High companion)
        ImmutableArray<IrParameter> parameters =
            [.. (hidden ? [new IrParameter(IrType.Ptr, Unique("Result"))] : Array.Empty<IrParameter>()),
             .. WithHighCompanions(routine.Parameters).Select(p => new IrParameter(ParameterType(p), Unique(p.Name)))];
        var callConv = module.CallConv(routine);                        // CMP-1011
        var returnType = ReturnType(routine);
        var callee = module.Routine(routine);
        var values = w.BeginFunction(new IrFunctionDefinition(new LinkName(export.Name, AlwaysQuote: false), IrLinkage.External,
            hidden ? IrType.Ptr : returnType, parameters, RoutineGroup) { CallConv = callConv });
        w.AppendBlock(new IrBlock("entry"));
        var result = w.Call(returnType == IrType.Void ? null : Unique("r"), returnType, callee, [.. values], callConv: callConv);
        w.Ret(hidden ? values[0] : result);
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
            new IrArrayConstant(new ArrayIrType(units.Length, UnitEntryType), entries), Alignment: module.PtrAlign));
    }

    /// <summary>
    /// CMP-366 / rtl-abi.md §7.2: <c>{ Parent, InstanceSize, ClassName, TypeInfo, IntfTable, MsgTable, InitTable, Reserved, [N x ptr]
    /// slots }</c>; abstract slots point to <c>System._AbstractError</c>; TypeInfo is the class RTTI block (C-112).
    /// </summary>
    private static IrConstant Vmt(ModuleState module, ClassTypeSymbol type)
    {
        var managed = type.ManagedFields.ToImmutableArray();
        IrConstant initTable = new IrNullConstant();
        if (!managed.IsEmpty)
        {
            // ABI §7.2: { i64 Count, [Count x { ptr TypeInfo, i64 Offset }] }
            var entry = new StructIrType([IrType.Ptr, module.NativeInt]);
            var entries = new ArrayIrType(managed.Length, entry);
            var table = new IrStructConstant(new StructIrType([module.NativeInt, entries]), [module.Native(managed.Length),
                new IrArrayConstant(entries, [.. managed.Select(f => (IrConstant)new IrStructConstant(entry, [module.TypeInfo(f.Type), module.Native(f.Offset)]))])]);
            module.AggregateConstants.Add((Mangler.InitTable(type), table, module.PtrAlign));
            initTable = new IrGlobal(Mangler.InitTable(type));
        }

        IrConstant intfTable = new IrNullConstant();
        if (!type.Interfaces.IsEmpty)
        {
            // ABI-095: { i64 Count, [Count x { [16 x i8] Guid, ptr Imt, i64 Offset }] } — each listed interface, then its ancestors
            var entry = new StructIrType([GuidType, IrType.Ptr, module.NativeInt]);
            var rows = new List<(InterfaceTypeSymbol Interface, ImplementedInterface Listed)>();
            foreach (var listed in type.Interfaces)
            {
                foreach (var intf in listed.Interface.SelfAndAncestors().Where(i => !rows.Exists(r => ReferenceEquals(r.Interface, i))))
                {
                    rows.Add((intf, listed));
                }
            }

            var entries = new ArrayIrType(rows.Count, entry);
            var table = new IrStructConstant(new StructIrType([module.NativeInt, entries]), [module.Native(rows.Count),
                new IrArrayConstant(entries, [.. rows.Select(r => (IrConstant)new IrStructConstant(entry,
                    [GuidConstant(r.Interface), new IrGlobal(Mangler.Imt(type, r.Listed.Interface)), module.Native(r.Listed.Offset)]))])]);
            module.AggregateConstants.Add((Mangler.IntfTable(type), table, module.PtrAlign));
            intfTable = new IrGlobal(Mangler.IntfTable(type));
        }

        IrConstant msgTable = new IrNullConstant();
        var handlers = type.Methods.Where(m => m.MessageId is not null).OrderBy(m => m.MessageId).ToImmutableArray();
        if (!handlers.IsEmpty)
        {
            // ABI §7.2 / CMP-370 (C-113): { i64 Count, [Count x { i64 MsgId, ptr Code }] } sorted by MsgId
            var entry = new StructIrType([IrType.I64, IrType.Ptr]);
            var entries = new ArrayIrType(handlers.Length, entry);
            var table = new IrStructConstant(new StructIrType([module.NativeInt, entries]), [module.Native(handlers.Length),
                new IrArrayConstant(entries, [.. handlers.Select(m => (IrConstant)new IrStructConstant(entry, [IrIntConstant.I64(m.MessageId!.Value), module.Routine(m)]))])]);
            module.AggregateConstants.Add((Mangler.MsgTable(type), table, module.BlockAlign));
            msgTable = new IrGlobal(Mangler.MsgTable(type));
        }

        var slots = new ArrayIrType(type.VirtualSlots.Length, IrType.Ptr);
        IrConstant Slot(MethodSymbol method) => method.IsAbstract ? module.NoReturnHelper("_AbstractError") : module.Routine(method);
        var vmtType = new StructIrType([.. module.VmtHeaderType.Fields, slots]);
        return new IrStructConstant(vmtType,
        [
            type.Parent is { } parent ? module.Vmt(parent) : new IrNullConstant(),
            module.Native(type.InstanceSize),
            module.StringLiteral(type.Name),
            module.TypeInfo(type),                                          // CMP-405 (C-112): the class RTTI block
            intfTable,
            msgTable,
            initTable,
            new IrNullConstant(),
            type.VirtualSlots.IsEmpty ? new IrZeroConstant(slots) : new IrArrayConstant(slots, [.. type.VirtualSlots.Select(Slot)]),
        ]);
    }

    /// <summary>
    /// CMP-371/372 (C-098): per interface the class lists, one internal thunk per IMT slot — <c>Self</c> = the interface reference
    /// minus the hidden field's offset, then a <c>musttail</c> call of the implementing method (through its VMT slot when it is
    /// virtual, so overrides are honoured) with the same IR parameter list — and the IMT constant of the thunks.
    /// </summary>
    private static void EmitInterfaceTables(ModuleState module, ClassTypeSymbol type, IrLinkage linkage)
    {
        var w = module.Writer;
        foreach (var listed in type.Interfaces)
        {
            var thunks = ImmutableArray.CreateBuilder<IrConstant>();
            for (var k = 0; k < listed.Implementations.Length; k++)
            {
                var target = listed.Implementations[k];
                var name = Mangler.Thunk(type, listed.Interface, listed.Interface.AllMethods[k]);
                if (HasInterfaceAggregateResult(listed.Interface.AllMethods[k]))
                {
                    EmitAggregateResultThunk(module, name, listed.Interface.AllMethods[k], target, listed.Offset);  // CMP-1200
                    thunks.Add(new IrGlobal(name));
                    continue;
                }

                var returnType = ReturnType(target);
                var parameters = ParameterTypes(target).Select((t, i) => new IrParameter(t, i == 0 ? "iref" : "p" + i)).ToImmutableArray();
                // CMP-1011: a musttail call needs the thunk and the method to share one convention
                var callConv = module.CallConv(target);
                var values = w.BeginFunction(new IrFunctionDefinition(name, IrLinkage.Internal, returnType, parameters, RoutineGroup) { CallConv = callConv });
                w.AppendBlock(new IrBlock("entry"));
                var self = w.GetElementPtr("self", IrType.I8, values[0], [IrIntConstant.I64(-listed.Offset)]);
                IrValue callee;
                if (target.IsDispatched)
                {
                    var vmt = w.Load("vmt", IrType.Ptr, self, module.PtrAlign);
                    var slot = w.GetElementPtr("slot", IrType.I8, vmt, [IrIntConstant.I64(module.VmtSlotOffset(target.VmtSlot))]);
                    callee = w.Load("fn", IrType.Ptr, slot, module.PtrAlign);
                }
                else
                {
                    callee = module.Routine(target);
                }

                var result = w.Call(returnType is VoidIrType ? null : "r", returnType, callee, [self, .. values.Skip(1)], mustTail: true, callConv: callConv);
                w.Ret(result);
                w.EndFunction();
                module.DeclarePending();
                thunks.Add(new IrGlobal(name));
            }

            var imt = new ArrayIrType(thunks.Count, IrType.Ptr);
            w.DefineGlobal(new IrGlobalDefinition(Mangler.Imt(type, listed.Interface), linkage, IsConstant: true,
                thunks.Count == 0 ? new IrZeroConstant(imt) : new IrArrayConstant(imt, thunks.ToImmutable()), Alignment: module.PtrAlign));
        }
    }

    /// <summary>
    /// CMP-1200 (LANG-684, ABI-206): the method-table entry of an interface method with a non-managed record, static-array,
    /// <c>ShortString</c> or method-pointer result — <c>ptr (ptr iref, ptr result, …)</c>. It calls the implementing method, which keeps
    /// its ordinary signature (ABI-081: an <c>iN</c> value for sizes 1, 2, 4 and 8, which the thunk stores; else the hidden result
    /// pointer, passed on), and returns the result pointer.
    /// </summary>
    private static void EmitAggregateResultThunk(ModuleState module, LinkName name, MethodSymbol slot, MethodSymbol target, long offset)
    {
        var w = module.Writer;
        var parameters = ParameterTypes(slot).Select((t, i) => new IrParameter(t, i == 0 ? "iref" : i == 1 ? "result" : "p" + (i - 1))).ToImmutableArray();
        var callConv = module.CallConv(target);
        var values = w.BeginFunction(new IrFunctionDefinition(name, IrLinkage.Internal, IrType.Ptr, parameters, RoutineGroup) { CallConv = module.CallConv(slot) });
        w.AppendBlock(new IrBlock("entry"));
        var self = w.GetElementPtr("self", IrType.I8, values[0], [IrIntConstant.I64(-offset)]);
        IrValue callee;
        if (target.IsDispatched)
        {
            var vmt = w.Load("vmt", IrType.Ptr, self, module.PtrAlign);
            var entry = w.GetElementPtr("slot", IrType.I8, vmt, [IrIntConstant.I64(module.VmtSlotOffset(target.VmtSlot))]);
            callee = w.Load("fn", IrType.Ptr, entry, module.PtrAlign);
        }
        else
        {
            callee = module.Routine(target);
        }

        if (HasHiddenResult(target))
        {
            w.Call(null, IrType.Void, callee, [self, values[1], .. values.Skip(2)], callConv: callConv);
        }
        else
        {
            var value = w.Call("v", ReturnType(target), callee, [self, .. values.Skip(2)], callConv: callConv);
            w.Store(value!, values[1], 1);
        }

        w.Ret(values[1]);
        w.EndFunction();
        module.DeclarePending();
    }

    /// <summary>ABI-096: <c>TGUID</c> as <c>[16 x i8]</c>.</summary>
    internal static readonly ArrayIrType GuidType = new(16, IrType.I8);

    /// <summary>The interface's GUID bytes in <c>TGUID</c> memory order; all zero without a GUID (ABI-095).</summary>
    internal static IrConstant GuidConstant(InterfaceTypeSymbol intf) =>
        intf.Guid is { } guid ? new IrArrayConstant(GuidType, [.. guid.Select(b => (IrConstant)new IrIntConstant(IrType.I8, b))]) : new IrZeroConstant(GuidType);

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
         .. WithHighCompanions(routine.Parameters).Select(ParameterType),
         .. (routine is { IsSafecall: true, ReturnType: not null } ? [IrType.Ptr] : Array.Empty<IrType>())];        // CMP-1202: ptr Ret, last

    /// <summary>ABI-080/082: the declared parameters as the IR passes them — each open array followed by its <c>i64</c> High (C-106).</summary>
    internal static IEnumerable<ParameterSymbol> WithHighCompanions(IEnumerable<ParameterSymbol> parameters) =>
        parameters.SelectMany(p => p.HighParameter is { } high ? [p, high] : new[] { p });

    /// <summary>The IR type of one declared parameter (ABI-080).</summary>
    internal static IrType ParameterType(ParameterSymbol parameter) => IsPassedByAddress(parameter) ? IrType.Ptr : ScalarStorage(parameter.Type);

    /// <summary>ABI-081: the value, an <c>iN</c> for an aggregate of size 1, 2, 4 or 8, <c>void</c> with a hidden result.</summary>
    internal static IrType ReturnType(RoutineSymbol routine) =>
        routine.IsSafecall ? IrType.I32                                         // CMP-1202: the HResult
        : HasInterfaceAggregateResult(routine) ? IrType.Ptr                     // ABI-206: the entry returns the result pointer
        : routine.ReturnType is { } type && !HasHiddenResult(routine) ? ScalarStorage(type) : IrType.Void;

    /// <summary>ABI-080/081: an aggregate (record, static array, big set) of size 1, 2, 4 or 8 travels as an integer of its size.</summary>
    internal static bool IsPassedAsInteger(TypeSymbol type) => Lowerer.IsAggregate(type) && type.Size is 1 or 2 or 4 or 8;

    /// <summary>
    /// ABI-080: the parameter arrives as a <c>ptr</c> — <c>var</c>/<c>out</c>, and value/<c>const</c> aggregates of other sizes
    /// (value: the caller's copy, which the callee owns; <c>const</c>: the original).
    /// </summary>
    /// <remarks>An open array (any mode) arrives as its data pointer value, spilled like a scalar (C-106).</remarks>
    internal static bool IsPassedByAddress(ParameterSymbol parameter) =>
        parameter.Type is not OpenArrayTypeSymbol
        && (parameter.IsByReference || parameter.IsRef || Lowerer.IsAggregate(parameter.Type) && !IsPassedAsInteger(parameter.Type));   // C-231 [Ref]

    /// <summary>
    /// ABI-081: the result is an aggregate not of size 1, 2, 4 or 8, or of a managed type (ABI-032), written through a hidden
    /// <c>ptr Result</c> parameter.
    /// </summary>
    internal static bool HasHiddenResult(RoutineSymbol routine) =>
        !routine.IsSafecall                                                     // LANG-686: the result is the last argument, whatever its type
        && (routine.ReturnType is { } type && (type.IsManaged || Lowerer.IsAggregate(type) && !IsPassedAsInteger(type))
            || HasInterfaceAggregateResult(routine));

    /// <summary>
    /// LANG-684 / ABI-206 (C-300): a method of an interface (the method-table entry and every call through an interface reference)
    /// returns a non-managed record, static array, <c>ShortString</c> or method pointer through a hidden <c>ptr Result</c> after the
    /// reference, whatever its size, and returns that pointer — the platform's member-function rule. Sets, managed and scalar
    /// results keep ABI-081; the interfaces of anonymous-method references (ABI-097) are exempt.
    /// </summary>
    internal static bool HasInterfaceAggregateResult(RoutineSymbol routine) =>
        routine is MethodSymbol { IsSafecall: false, ContainingInterface.IsMethodReference: false, ReturnType: { IsManaged: false } type }
        && type is RecordTypeSymbol or ArrayTypeSymbol or StringTypeSymbol { Variant: StringVariant.Short } or MethodPointerTypeSymbol;

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
    internal static TypeLayout FieldLayout(TargetInfo target, Symbol variable) => variable switch
    {
        _ when HoldsAddress(variable) => TypeLayout.Pointer(target),
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
        (RealTypeSymbol { RealKind: RealKind.Currency }, BigInteger i) => new IrIntConstant(IrType.I64, i * 10000),             // C-218: scaled
        (RealTypeSymbol { RealKind: RealKind.Currency }, double d) => new IrIntConstant(IrType.I64, CurrencyScaled(d)),
        (RealTypeSymbol { RealKind: not RealKind.Currency }, BigInteger i) => new IrFloatConstant((FloatingIrType)ValueType(type), (double)i),
        (RealTypeSymbol { RealKind: not RealKind.Currency }, double d) => new IrFloatConstant((FloatingIrType)ValueType(type), d),
        (PointerTypeSymbol or ProceduralTypeSymbol or ClassTypeSymbol or ClassRefTypeSymbol or InterfaceTypeSymbol or DynamicArrayTypeSymbol, null) => new IrNullConstant(),   // C-209: nil dynamic array
        (PointerTypeSymbol, BigInteger address) => new IrIntToPtrConstant(address),                                         // C-185
        (IntegerTypeSymbol or CharTypeSymbol or EnumTypeSymbol, BigInteger i) => new IrIntConstant((IntegerIrType)ValueType(type), i),
        (SetTypeSymbol { IsSmall: true }, BigInteger mask) => new IrIntConstant((IntegerIrType)ValueType(type), mask),       // CMP-335
        (SetTypeSymbol, BigInteger mask) => BigSetConstant(mask, type.Size),                                                  // CMP-336
        _ => throw new NotSupportedException($"Constant {constant} of type '{type.Name}' is not emitted yet."),
    };

    /// <summary>C-218 (ABI §2): a Currency value as its Int64 representation, value × 10000 rounded to nearest (ties to even).</summary>
    internal static BigInteger CurrencyScaled(double value) => new(Math.Round(value * 10000, MidpointRounding.ToEven));

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
            case (StringTypeSymbol ansi, string text) when Strings.IsAnsi(ansi):
                return module.AnsiStringLiteral(text, ansi.CodePage);                             // C-203: the constant literal (ABI-020)
            case (StringTypeSymbol, string text):
                return module.StringLiteral(text);
            case (ArrayTypeSymbol array, ImmutableArray<ConstantValue?> elements):
                return new IrArrayConstant((ArrayIrType)TypeLayout.Of(array).StorageType, [.. elements.Select(e => Element(e, array.ElementType))]);
            case (RecordTypeSymbol { HasVariantPart: true } variant, ImmutableArray<ConstantValue?>):
            {
                // C-198: CMP-306 byte storage, the image of the given fields
                var bytes = ConstantBytes.Of(constant, variant) ?? throw new InvalidOperationException("A variant record constant without a byte image.");
                var storage = (ArrayIrType)TypeLayout.Of(variant).StorageType;
                return bytes.All(b => b == 0)
                    ? new IrZeroConstant(storage)
                    : new IrArrayConstant(storage, [.. bytes.Select(b => (IrConstant)new IrIntConstant(IrType.I8, (sbyte)b))]);
            }
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

    /// <summary>The byte offset of every field (field 0 = the static link), for the debug records of captured variables (CMP-422).</summary>
    public ImmutableArray<long> Offsets { get; init; } = [];

    public long OffsetOf(Symbol variable) => Offsets[Variables.IndexOf(variable) + 1];

    /// <summary>The alignment of the record's alloca: its strictest field (cap 8, like <see cref="TypeLayout.StructOffsets"/>); field 0 is an address.</summary>
    public required int Alignment { get; init; }
}

/// <summary>Module-wide state shared by the function emitters.</summary>
/// <summary>C-213: a construct the emitter does not handle yet, with the source location of the statement being emitted.</summary>
internal sealed class EmitNotSupportedException(string message, Text.Location location) : NotSupportedException(message)
{
    public Text.Location Location { get; } = location;
}

/// <summary>A user error that only the emitted module reveals (C-293): reported as the diagnostic <paramref name="code"/>.</summary>
internal sealed class EmitDiagnosticException(string code, Text.Location location, params object[] arguments) : Exception(code)
{
    public string Code { get; } = code;

    public Text.Location Location { get; } = location;

    public object[] Arguments { get; } = arguments;
}

internal sealed class ModuleState(IIrWriter writer)
{
    public IIrWriter Writer { get; } = writer;

    /// <summary>The compilation's target (CMP-1000): every machine fact of the module comes from it.</summary>
    public required TargetInfo Target { get; init; }

    /// <summary>Where each export item of a library file is written (by export name), for diagnostics of the thunks.</summary>
    public IReadOnlyDictionary<string, Text.Location> ExportLocations { get; init; } = new Dictionary<string, Text.Location>();

    /// <summary>Alignment of a stored code or data address.</summary>
    public int PtrAlign => Target.PointerAlign;

    /// <summary>Alignment of the run-time blocks of rtl-abi.md that also hold <c>Int64</c> fields (RTTI blocks and tables, the message table).</summary>
    public int BlockAlign => Math.Max(Target.PointerAlign, Target.Int64Align);

    /// <summary><c>NativeInt</c> as the run-time blocks store it: <c>i&lt;PointerSize*8&gt;</c> (CMP-1007).</summary>
    public IntegerIrType NativeInt => AbiLayout.NativeInt(Target);

    public IrIntConstant Native(long value) => new(NativeInt, value);

    /// <summary>rtl-abi.md §7.2: the fixed part of a VMT — Parent, InstanceSize, ClassName, TypeInfo, IntfTable, MsgTable, InitTable, Reserved.</summary>
    public StructIrType VmtHeaderType => AbiLayout.VmtHeader(Target);

    /// <summary>The byte offset of virtual slot <paramref name="slot"/> in a VMT: the header's size plus one address per slot (CMP-1007).</summary>
    public long VmtSlotOffset(int slot) => AbiLayout.VmtSlotOffset(Target, slot);

    /// <summary>The byte offset of slot <paramref name="slot"/> in an IMT (ABI-091): one address per slot.</summary>
    public long ImtSlotOffset(int slot) => AbiLayout.ImtSlotOffset(Target, slot);

    /// <summary>ABI-180: a unit's threadvar descriptor <c>{ NativeInt Size, NativeInt Offset }</c>.</summary>
    public StructIrType TlsDescriptorType => AbiLayout.TlsDescriptor(Target);

    /// <summary>The (negative) byte offset of a dynamic array's <c>Length</c> field from element 0 (ABI-041).</summary>
    public long DynArrayLengthOffset => AbiLayout.DynArrayLengthOffset(Target);

    /// <summary>CMP-420…424: the module's debug metadata with <c>-g</c>; null without.</summary>
    public DebugInfoEmitter? Debug { get; init; }

    public Dictionary<VariableSymbol, IrGlobal> Globals { get; } = [];

    /// <summary>The storage of the variables of the unit's bodies without frame record (<see cref="LoweredUnit.BodyGlobals"/>), for their debug scope (C-316).</summary>
    public ImmutableArray<(VariableSymbol Variable, LinkName Name)> BodyGlobals { get; set; } = [];

    public Dictionary<RoutineSymbol, FrameRecord> Frames { get; } = [];

    /// <summary>Link names of the routines defined in this module: helper calls in unit System use them directly.</summary>
    public HashSet<string> DefinedRoutines { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// C-293: the routine behind each function symbol of the module that a user declaration names — the routines defined here and
    /// the <c>external</c> routines declared so far (by link name).
    /// </summary>
    public Dictionary<string, RoutineSymbol> LinkNameOwners { get; } = new(StringComparer.Ordinal);

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
        if (routine.IsExternal)
        {
            name = Mangler.Decorated(name, routine, Target);                // CMP-1011

            // C-293: several declarations may bind the same import name (overloads over one entry point, an integer and a pointer
            // view of one parameter). The module declares the symbol once, as the first declaration used; every call site states
            // its own signature, so a later declaration needs none of its own.
            if (LinkNameOwners.TryGetValue(name.Text, out var first) && !ReferenceEquals(first, routine))
            {
                return new IrGlobal(name);
            }

            LinkNameOwners[name.Text] = routine;
        }

        return Declare(new IrFunctionDeclaration(name, IrEmitter.ReturnType(routine), IrEmitter.ParameterTypes(routine),
            routine.IsNoReturn ? IrEmitter.NoReturnGroup : null)
        {
            DllImport = routine.LibraryName is not null,
            CallConv = CallConv(routine),
        });
    }

    /// <summary>CMP-1011: the LLVM keyword of <paramref name="routine"/>'s calling-convention directive on this target ("" = not printed).</summary>
    public string CallConv(RoutineSymbol routine) => Target.CallingConventions[routine.CallingConvention].LlvmKeyword;

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

    private readonly SortedSet<LinkName> _externalTlsDescriptors = new(Comparer<LinkName>.Create((a, b) => string.CompareOrdinal(a.Text, b.Text)));

    /// <summary>Threadvar descriptors of other units referenced here (ABI-180), sorted by link name.</summary>
    public IEnumerable<LinkName> ExternalTlsDescriptors => _externalTlsDescriptors;

    /// <summary>The threadvar descriptor <c>@"U.$tls"</c> of <paramref name="unit"/>, declared external when another unit defines it.</summary>
    public IrGlobal TlsDescriptor(UnitSymbol unit)
    {
        var name = Mangler.TlsDescriptor(unit);
        if (!string.Equals(unit.Name, UnitName, StringComparison.OrdinalIgnoreCase))
        {
            _externalTlsDescriptors.Add(name);
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

    private readonly Dictionary<string, IrGlobal> _typeInfoByName = new(StringComparer.Ordinal);

    /// <summary>RTTI blocks (rtl-abi.md §10) in first-use order; nested element blocks come before the blocks referencing them.</summary>
    public List<(LinkName Name, IrStructConstant Block)> TypeInfos { get; } = [];

    private static readonly IrConstant Pad7 = new IrZeroConstant(new ArrayIrType(7, IrType.I8));

    /// <summary>
    /// CMP-405/407: the address of the RTTI block of <paramref name="type"/> (rtl-abi.md §10), built once per module as an anonymous
    /// struct with explicit padding (<c>linkonce_odr</c>, CMP-365), for every type kind (C-112). The reference is registered before
    /// the block is built, so a type reaching itself (a pointer to a class whose fields list the pointer) is fine; nested blocks
    /// still come before the blocks referencing them. Classes: <see cref="ClassInfo"/>.
    /// </summary>
    public IrGlobal TypeInfo(TypeSymbol type)
    {
        if (_typeInfoReferences.TryGetValue(type, out var global))
        {
            return global;
        }

        if (type is ClassTypeSymbol @class)
        {
            return ClassInfo(@class);
        }

        // C-190: anonymous types are named by their type text (CMP-362), so two symbols spelt alike share one block
        var name = TypeInfoName(type);
        if (_typeInfoByName.TryGetValue(name.Text, out global))
        {
            _typeInfoReferences.Add(type, global);
            return global;
        }

        global = new IrGlobal(name);
        _typeInfoReferences.Add(type, global);
        _typeInfoByName.Add(name.Text, global);
        var fields = TypeInfoFields(type, name);
        TypeInfos.Add((name, new IrStructConstant(new StructIrType([.. fields.Select(f => f.Type)]), [.. fields])));
        return global;
    }

    /// <summary>The fields of the block of a non-class type: Kind, 7 zero bytes, Name, then the kind's data (rtl-abi.md §10 table).</summary>
    private IrConstant[] TypeInfoFields(TypeSymbol type, LinkName name)
    {
        IrConstant Kind(int kind) => new IrIntConstant(IrType.I8, kind);
        IrConstant Name() => IsDeclared(type) || type.IsPredefined || type is StringTypeSymbol { Variant: not StringVariant.Short } ? StringLiteral(type.Name) : new IrNullConstant();
        IrConstant Byte(long value) => new IrIntConstant(IrType.I8, value);
        IrConstant Pad(int bytes) => new IrZeroConstant(new ArrayIrType(bytes, IrType.I8));
        IrConstant[] Ordinal(int kind, int ordType, BigInteger min, BigInteger max) =>
            [Kind(kind), Pad7, Name(), Byte(ordType), Pad7, new IrIntConstant(IrType.I64, min), new IrIntConstant(IrType.I64, max)];
        IrConstant[] Routine(int kind, RoutineSymbol signature) =>
            [Kind(kind), Pad7, Name(), Byte(signature.ReturnType is null ? 0 : 1), Byte(signature.Parameters.Length), Pad(6)];

        return type switch
        {
            StringTypeSymbol { Variant: StringVariant.Short } s => [Kind(11), Pad7, Name(), Byte(s.MaxLength), Pad7],
            StringTypeSymbol s => [Kind(s.Variant == StringVariant.Unicode ? 9 : 10), Pad7, Name(), new IrIntConstant(IrType.I16, (short)s.CodePage), Pad(6)],
            IntegerTypeSymbol i => Ordinal(i.Bits == 64 ? 16 : 1, OrdType(i.Bits, i.IsSigned), i.Min, i.Max),
            CharTypeSymbol c => Ordinal(c.IsWide ? 8 : 2, c.IsWide ? 3 : 1, c.MinValue, c.MaxValue),
            BooleanTypeSymbol b => Enumeration(type, name, OrdType(8 * (int)b.Size, isSigned: false), 0, 1, type, ["False", "True"]),
            EnumTypeSymbol e => Enumeration(type, name, OrdType(8 * (int)e.Size, e.Low < 0), e.Low, e.High, e.BaseType ?? e,
                [.. Enumerable.Range(0, (int)(e.High - e.Low + 1)).Select(k => e.EnumMembers.FirstOrDefault(m => m.Ordinal == e.Low + k)?.Name ?? "")]),
            RealTypeSymbol r => [Kind(4), Pad7, Name(), Byte(r.RealKind switch { RealKind.Single => 0, RealKind.Double => 1, _ => 2 }), Pad7],
            SetTypeSymbol set => [Kind(5), Pad7, Name(), IrIntConstant.I32(set.Size), Pad(4), set.ElementType is { } element ? TypeInfo(element) : new IrNullConstant()],
            DynamicArrayTypeSymbol array =>
            [
                Kind(14), Pad7, Name(), Native(TypeLayout.Of(array.ElementType).Size), TypeInfo(array.ElementType),
                array.ElementType.IsManaged ? TypeInfo(array.ElementType) : new IrNullConstant(),
            ],
            RecordTypeSymbol record => RecordFields(record, Name()),
            InterfaceTypeSymbol intf =>
            [
                // ABI §10 tkInterface (C-099): Parent, Guid, own method count
                Kind(15), Pad7, Name(), intf.Parent is { } parent ? TypeInfo(parent) : new IrNullConstant(),
                IrEmitter.GuidConstant(intf), Native(intf.Methods.Length),
            ],
            ArrayTypeSymbol array => [Kind(13), Pad7, Name(), Native(array.Size), Native(ElementCount(array)), TypeInfo(Innermost(array))],
            PointerTypeSymbol pointer => [Kind(17), Pad7, Name(), pointer.PointedType is { } pointed && HasTypeInfo(pointed) ? TypeInfo(pointed) : new IrNullConstant()],
            ClassRefTypeSymbol reference => [Kind(18), Pad7, Name(), TypeInfo(reference.Class)],
            MethodPointerTypeSymbol method => Routine(7, method.Signature),
            ProceduralTypeSymbol procedure => Routine(19, procedure.Signature),
            _ => [Kind(0), Pad7, Name()],                                                       // tkUnknown (Text)
        };
    }

    /// <summary>ABI §10 <c>OrdType</c>: 0 S8, 1 U8, 2 S16, 3 U16, 4 S32, 5 U32, 6 S64, 7 U64.</summary>
    private static int OrdType(int bits, bool isSigned) => (bits switch { 8 => 0, 16 => 2, 32 => 4, _ => 6 }) + (isSigned ? 0 : 1);

    /// <summary>
    /// <c>tkEnumeration</c>: OrdType, Min, Max, BaseType (the block itself for a base enumeration) and <c>@"…$names"</c>, the
    /// <c>Max − Min + 1</c> element names of the range (<c>Boolean</c>: <c>False</c>, <c>True</c>).
    /// </summary>
    private IrConstant[] Enumeration(TypeSymbol type, LinkName name, int ordType, BigInteger min, BigInteger max, TypeSymbol baseType, string[] names)
    {
        var text = name.Text.EndsWith(".$rtti", StringComparison.Ordinal) ? name.Text[..^".$rtti".Length] : name.Text;
        var namesName = new LinkName(text + ".$names", AlwaysQuote: true);
        AggregateConstants.Add((namesName, new IrArrayConstant(new ArrayIrType(names.Length, IrType.Ptr), [.. names.Select(StringLiteral)]), PtrAlign));
        IrConstant typeName = IsDeclared(type) || type.IsPredefined ? StringLiteral(type.Name) : new IrNullConstant();
        return [new IrIntConstant(IrType.I8, 3), Pad7, typeName, new IrIntConstant(IrType.I8, ordType), Pad7, new IrIntConstant(IrType.I64, min),
            new IrIntConstant(IrType.I64, max), TypeInfo(baseType), new IrGlobal(namesName)];
    }

    /// <summary><c>tkRecord</c>: Size, ManagedCount and <c>@"…$rtti.fields"</c> (or null) of <c>{ ptr TypeInfo, i64 Offset }</c> entries.</summary>
    private IrConstant[] RecordFields(RecordTypeSymbol record, IrConstant name)
    {
        var managed = record.Fields.Where(f => f.Type.IsManaged).ToImmutableArray();
        IrConstant table = new IrNullConstant();
        if (!managed.IsEmpty)
        {
            var entry = new StructIrType([IrType.Ptr, NativeInt]);
            var tableName = new LinkName(TypeInfoName(record).Text + ".fields", AlwaysQuote: true);
            AggregateConstants.Add((tableName, new IrArrayConstant(new ArrayIrType(managed.Length, entry),
                [.. managed.Select(f => (IrConstant)new IrStructConstant(entry, [TypeInfo(f.Type), Native(f.Offset)]))]), BlockAlign));
            table = new IrGlobal(tableName);
        }

        return [new IrIntConstant(IrType.I8, 12), Pad7, name, Native(record.Size), Native(managed.Length), table];
    }

    /// <summary>The block of an untyped pointer's target is never built: <c>Text</c> and untyped targets have none worth pointing at.</summary>
    private static bool HasTypeInfo(TypeSymbol type) => type is not (TextTypeSymbol or UntypedTypeSymbol or ErrorTypeSymbol);

    /// <summary>The classes whose class RTTI block this module defines (CMP-405; the VMT's linkage, codegen.md C-112 decision), in first-use order.</summary>
    public List<(ClassTypeSymbol Class, LinkName Name, IrStructConstant Block)> ClassInfos { get; } = [];

    private readonly SortedSet<LinkName> _externalClassInfos = new(Comparer<LinkName>.Create((a, b) => string.CompareOrdinal(a.Text, b.Text)));

    /// <summary>Class RTTI blocks of other units' classes referenced here (declared external), sorted by link name.</summary>
    public IEnumerable<LinkName> ExternalClassInfos => _externalClassInfos;

    /// <summary>
    /// rtl-abi.md §10.1 (CMP-405/406/408, ABI-143): <c>tkClass</c> with VMT, ParentInfo, UnitName and the property, method and field
    /// tables of the members in the class's <see cref="ClassTypeSymbol.RttiScope"/> (<c>private constant</c> arrays, null when empty),
    /// AttrCount 0 / Attrs null (M2.6). Defined only by the class's own unit; any other unit references it externally.
    /// </summary>
    private IrGlobal ClassInfo(ClassTypeSymbol type)
    {
        var name = new LinkName(type.QualifiedName + ".$rtti", AlwaysQuote: true);
        var global = new IrGlobal(name);
        _typeInfoReferences.Add(type, global);
        if (!OwnClasses.Contains(type))
        {
            _externalClassInfos.Add(name);
            return global;
        }

        var broad = type.RttiScope == RttiScope.Broad;
        bool InScope(Symbol member) => member.Visibility == Visibility.Published || broad && member.Visibility == Visibility.Public;
        var properties = type.Properties.Where(p => !p.IsArrayProperty && !p.IsClassProperty && InScope(p)).ToImmutableArray();
        var methods = type.Methods.Where(InScope).ToImmutableArray();
        var fields = type.Fields.Where(f => broad || f.Visibility == Visibility.Published).ToImmutableArray();

        var props = Table(name, ".props", PropInfoType, properties.Select(PropInfo));
        var methodTable = Table(name, ".methods", MethodInfoType, methods.Select(m => (IrConstant)new IrStructConstant(MethodInfoType,
            [StringLiteral(m.Name), m.IsAbstract ? NoReturnHelper("_AbstractError") : Routine(m), VisibilityByte(m), new IrIntConstant(IrType.I8, MethodInfoKind(m)),
             new IrZeroConstant(new ArrayIrType(6, IrType.I8))])));
        var fieldTable = Table(name, ".fields", FieldInfoType, fields.Select(f => (IrConstant)new IrStructConstant(FieldInfoType,
            [StringLiteral(f.Name), Native(f.Offset), TypeInfo(f.Type), VisibilityByte(f), Pad7])));
        var attributeTable = Table(name, ".attrs", AttrInfoType, type.Attributes.Select(a => (IrConstant)new IrStructConstant(AttrInfoType,
            [Vmt(a.Type), Routine(a.Thunk)])));                                                                    // C-126 (ABI §10.1)
        IrConstant[] block =
        [
            new IrIntConstant(IrType.I8, 6), Pad7, StringLiteral(type.Name), Vmt(type), type.Parent is { } parent ? TypeInfo(parent) : new IrNullConstant(),
            StringLiteral(UnitOf(type)), Native(properties.Length), props, Native(methods.Length), methodTable,
            Native(fields.Length), fieldTable, Native(type.Attributes.Length), attributeTable,
        ];
        ClassInfos.Add((type, name, new IrStructConstant(new StructIrType([.. block.Select(f => f.Type)]), [.. block])));
        return global;
    }

    private static readonly StructIrType AttrInfoType = new([IrType.Ptr, IrType.Ptr]);

    private StructIrType FieldInfoType => new([IrType.Ptr, NativeInt, IrType.Ptr, IrType.I8, new ArrayIrType(7, IrType.I8)]);

    private static readonly StructIrType MethodInfoType = new([IrType.Ptr, IrType.Ptr, IrType.I8, IrType.I8, new ArrayIrType(6, IrType.I8)]);

    private StructIrType PropInfoType => new([IrType.Ptr, IrType.Ptr, IrType.I8, IrType.I8, IrType.I8, IrType.I8, new ArrayIrType(4, IrType.I8),
        NativeInt, NativeInt, NativeInt, IrType.I32, IrType.I32, IrType.I64]);

    /// <summary>A table <c>@"U.T.$rtti.&lt;suffix&gt;"</c> of <paramref name="entries"/>, or <c>ptr null</c> when there are none (CMP-408).</summary>
    private IrConstant Table(LinkName info, string suffix, StructIrType entry, IEnumerable<IrConstant> entries)
    {
        var items = entries.ToImmutableArray();
        if (items.IsEmpty)
        {
            return new IrNullConstant();
        }

        var tableName = new LinkName(info.Text + suffix, AlwaysQuote: true);
        AggregateConstants.Add((tableName, new IrArrayConstant(new ArrayIrType(items.Length, entry), items), BlockAlign));
        return new IrGlobal(tableName);
    }

    /// <summary>
    /// ABI §10.1 <c>TPropInfo</c> (CMP-406): accessor kinds 1 field (Getter/Setter = the field path's offset), 2 static method (its code
    /// address, <c>ptrtoint</c>), 3 virtual method (VMT offset 64 + 8·slot); <c>Stored</c> constant 1, no <c>Default</c> (codegen.md C-112
    /// decision), <c>Index</c> or <c>$80000000</c>.
    /// </summary>
    private IrConstant PropInfo(PropertySymbol property)
    {
        (int Kind, IrConstant Value) Accessor(ImmutableArray<Symbol> path, MethodSymbol? method) =>
            !path.IsEmpty ? (1, Native(path.OfType<FieldSymbol>().Sum(f => f.Offset)))
            : method is null ? (0, Native(0))
            : method.IsDispatched ? (3, Native(VmtSlotOffset(method.VmtSlot)))
            : (2, new IrPtrToIntConstant(Routine(method), NativeInt));
        var (getKind, getter) = Accessor(property.ReadPath, property.ReadMethod);
        var (setKind, setter) = Accessor(property.WritePath, property.WriteMethod);
        // C-217 (LANG-644): StoredKind 0 constant, 1 field, 2 static method, 3 virtual method
        var (storedKind, stored) = property.StoredField is { } storedField ? (1, Native(storedField.Offset))
            : property.StoredMethod is { } storedMethod ? Accessor([], storedMethod)
            : (0, Native(property.StoredConstant ? 1 : 0));
        return new IrStructConstant(PropInfoType,
        [
            StringLiteral(property.Name), TypeInfo(property.Type), new IrIntConstant(IrType.I8, getKind), new IrIntConstant(IrType.I8, setKind),
            new IrIntConstant(IrType.I8, storedKind), VisibilityByte(property), new IrZeroConstant(new ArrayIrType(4, IrType.I8)), getter, setter, stored,
            IrIntConstant.I32(property.Index ?? int.MinValue), IrIntConstant.I32(0), IrIntConstant.I64(property.DefaultValue ?? long.MinValue),
        ]);
    }

    /// <summary>ABI §10.1 <c>Visibility</c>: 0 private (incl. strict), 1 protected (incl. strict), 2 public, 3 published.</summary>
    private static IrConstant VisibilityByte(Symbol member) => new IrIntConstant(IrType.I8, member.Visibility switch
    {
        Symbols.Visibility.StrictPrivate or Symbols.Visibility.Private => 0,
        Symbols.Visibility.StrictProtected or Symbols.Visibility.Protected => 1,
        Symbols.Visibility.Public => 2,
        _ => 3,
    });

    /// <summary>ABI §10.1 <c>TMethodInfo.Kind</c>: 0/1 procedure/function, 2 constructor, 3 destructor, 4/5 class, 6/7 static class.</summary>
    private static int MethodInfoKind(MethodSymbol method)
    {
        var function = method.ReturnType is null ? 0 : 1;
        return method.MethodKind switch
        {
            MethodKind.Constructor => 2,
            MethodKind.Destructor => 3,
            MethodKind.ClassMethod => 4 + function,
            MethodKind.ClassStatic => 6 + function,
            _ => function,
        };
    }

    private static string UnitOf(Symbol symbol)
    {
        for (var s = symbol.ContainingSymbol; s is not null; s = s.ContainingSymbol)
        {
            if (s is UnitSymbol unit)
            {
                return unit.Name;
            }
        }

        return "";
    }

    private static TypeSymbol Innermost(ArrayTypeSymbol array) => array.ElementType is ArrayTypeSymbol inner ? Innermost(inner) : array.ElementType;

    private static long ElementCount(ArrayTypeSymbol array) => array.Length * (array.ElementType is ArrayTypeSymbol inner ? ElementCount(inner) : 1);

    /// <summary>A type declaration (not a built-in, not an anonymous type constructor).</summary>
    private static bool IsDeclared(TypeSymbol type) =>
        !type.Locations.IsEmpty && type.ContainingSymbol is not null && type is not RecordTypeSymbol { IsAnonymous: true };

    /// <summary>
    /// rtl-abi.md §10 / CMP-362: <c>U.T.$rtti</c> for a declared type (a routine's type: its routine's link name first), the
    /// predeclared types (and the built-in strings) as <c>System.&lt;Name&gt;.$rtti</c>, anonymous types <c>U.$rtti.&lt;type text&gt;</c>
    /// of this module's unit.
    /// </summary>
    private LinkName TypeInfoName(TypeSymbol type)
    {
        if (!type.IsPredefined && IsDeclared(type))                     // before the strings: a distinct string type (C-262)
        {
            var prefix = type.ContainingSymbol is RoutineSymbol routine ? Mangler.Routine(routine).Text + "." + type.Name : type.QualifiedName;
            return new LinkName(prefix + ".$rtti", AlwaysQuote: true);
        }

        if (type is StringTypeSymbol { Variant: not StringVariant.Short } || type.IsPredefined)
        {
            return new LinkName("System." + type.Name + ".$rtti", AlwaysQuote: true);
        }

        return new LinkName(UnitName + ".$rtti." + type.Name, AlwaysQuote: true);
    }

    /// <summary>The GUID constants used in this module (CMP-373), first-use order.</summary>
    public List<InterfaceTypeSymbol> Guids { get; } = [];

    /// <summary>CMP-373: <c>@"U.IBar.$guid"</c>, a <c>linkonce_odr</c> constant (with its comdat) defined in every module that uses it.</summary>
    public IrGlobal Guid(InterfaceTypeSymbol intf)
    {
        if (!Guids.Contains(intf))
        {
            Guids.Add(intf);
        }

        return new IrGlobal(Mangler.Guid(intf));
    }

    private readonly Dictionary<string, IrGlobal> _dispDescriptors = new(StringComparer.Ordinal);

    /// <summary>
    /// CMP-1209 (rtl-abi.md ABI-205): the private constant <c>@"U.$disp.n"</c> of a dispatch call — the packed record
    /// <c>&lt;{ i32 DispId, i16 Flags, i16 ResultType, i16 ArgCount, [ArgCount x i16] ArgTypes }&gt;</c>, alignment 4 (no array member
    /// without arguments); <c>n</c> counts from 1 in first-use order and identical descriptors share one constant.
    /// </summary>
    public IrGlobal DispDescriptor(Lowering.LDispDescriptor descriptor)
    {
        var key = string.Join(",", new[] { descriptor.DispId, descriptor.Flags, descriptor.ResultCode }.Concat(descriptor.ArgumentCodes));
        if (_dispDescriptors.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var count = descriptor.ArgumentCodes.Length;
        var codes = new ArrayIrType(count, IrType.I16);
        ImmutableArray<IrType> fieldTypes = count == 0 ? [IrType.I32, IrType.I16, IrType.I16, IrType.I16] : [IrType.I32, IrType.I16, IrType.I16, IrType.I16, codes];
        ImmutableArray<IrConstant> fields =
        [
            new IrIntConstant(IrType.I32, descriptor.DispId),
            new IrIntConstant(IrType.I16, descriptor.Flags),
            new IrIntConstant(IrType.I16, descriptor.ResultCode),
            new IrIntConstant(IrType.I16, count),
        ];
        if (count > 0)
        {
            fields = fields.Add(new IrArrayConstant(codes, [.. descriptor.ArgumentCodes.Select(c => (IrConstant)new IrIntConstant(IrType.I16, c))]));
        }

        var name = Mangler.DispDescriptor(UnitName, _dispDescriptors.Count + 1);
        AggregateConstants.Add((name, new IrStructConstant(new StructIrType(fieldTypes, IsPacked: true), fields), 4));
        return _dispDescriptors[key] = new IrGlobal(name);
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

    /// <summary>CMP-398: some function of the module has a try region; the module gets <see cref="IrEmitter.AsyncEhFlag"/>.</summary>
    public bool UsesAsyncEh { get; set; }

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
    private static readonly IrBlock CleanupPadBlock = new("cleanup.pad");

    private readonly ModuleState _module;
    private readonly IIrWriter _w;

    /// <summary>Alignment of a stored code or data address on the module's target.</summary>
    private int PtrAlign => _module.PtrAlign;

    /// <summary>A <c>NativeInt</c> loaded from a run-time block as the <c>i64</c> the expressions compute with (no-op on 64-bit targets).</summary>
    private IrValue Widen(IrValue native) =>
        _module.NativeInt.Bits == 64 ? native : _w.Cast(NewTemp(), IrCastOperator.SExt, native, IrType.I64);

    private readonly LoweredBody _body;
    private readonly RoutineSymbol? _routine;
    /// <summary>Implicit parameters (a method's <c>Self</c> and flag, ABI-082) first, then the declared ones.</summary>
    private readonly ImmutableArray<ParameterSymbol> _parameters;
    private readonly int _implicitCount;
    private readonly LinkName _name;
    private readonly IrLinkage _linkage;
    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase) { Entry.Name, Exit.Name, CleanupBlock.Name, CleanupPadBlock.Name };

    /// <summary>Where <c>Exit</c> and the end of the body go: block <c>cleanup</c> when the body has a cleanup scope, else <c>exit</c>.</summary>
    private readonly IrBlock _returnTarget;
    private readonly Dictionary<LLocal, IrValue> _locals = [];
    private readonly Dictionary<ParameterSymbol, IrValue> _parameterAddresses = [];
    private readonly Dictionary<LTempValue, IrValue> _temps = [];
    private int _tempCount;
    private int _blockCount;
    private int _invokeCount;
    private readonly Dictionary<LLoopLabel, (IrBlock Break, IrBlock Continue)> _loops = [];

    /// <summary>The number of EH scopes open where each loop starts: a jump out of the loop leaves the scopes above.</summary>
    private readonly Dictionary<LLoopLabel, int> _loopScopes = [];

    /// <summary>
    /// The open EH scopes, innermost last (CMP-391…394): try parts, handler regions, the function-level cleanup scope. Inside
    /// any of them a call that may raise is an <c>invoke</c> unwinding to the innermost one's pad.
    /// </summary>
    private readonly List<EhScope> _scopes = [];

    /// <summary>The pad token of the cleanup funclet being emitted (calls carry <c>[ "funclet"(token) ]</c>), else null.</summary>
    private IrValue? _funclet;

    /// <summary>The scopes <c>Exit</c> does not leave (the function-level cleanup scope, CMP-393).</summary>
    private int _returnScopes;

    /// <summary>An EH scope: unwinding goes to <see cref="Pad"/>; a jump leaving it first runs <see cref="Leave"/> (if any).</summary>
    /// <param name="Key">What opened the scope: the try statement (for a handler region its handler); null for the function-level scope.</param>
    /// <param name="Region">CMP-398: a try part or handler region, opened by <see cref="TryBegin"/>: every exit closes it with <see cref="TryEnd"/>.</param>
    private sealed record EhScope(IrBlock Pad, Action? Leave, object? Key = null, bool Region = false);

    /// <summary>
    /// CMP-398: opens the region of <paramref name="scope"/> — pushes it and writes <c>invoke void @llvm.seh.try.begin()</c> unwinding
    /// to its pad, so the scope table covers every instruction up to the matching <see cref="TryEnd"/>, not only the invokes.
    /// </summary>
    private void TryBegin(EhScope scope)
    {
        _module.UsesAsyncEh = true;
        _scopes.Add(scope);
        EmitCall(null, IrType.Void, _module.Intrinsic("llvm.seh.try.begin", IrType.Void, []), []);
    }

    /// <summary>CMP-398: <c>invoke void @llvm.seh.try.end()</c> of the innermost scope (a region), which stays open for the caller to pop.</summary>
    private void TryEnd() => EmitCall(null, IrType.Void, _module.Intrinsic("llvm.seh.try.end", IrType.Void, []), []);

    /// <summary>
    /// CMP-398: whether the body has a try statement (a try…except, or a try…finally with an outlined finally part; not a
    /// declaration scope, whose cleanup reads only what the run-time library's helpers stored). Then every load and store of the
    /// function is volatile: the handler, the finally part and the code after the statement read the values the try part stored
    /// before a fault (no promotion to registers, no forwarding along the unwind edges, which leave only from the invokes), and an
    /// access that may fault is neither removed nor moved.
    /// </summary>
    private static bool HasTry(LStatement? statement) => statement switch
    {
        LTryExcept or LTryFinally { Cleanup: null } => true,
        LTryFinally scope => HasTry(scope.Body),
        LBlock block => block.Statements.Any(HasTry),
        LIf branch => HasTry(branch.Then) || HasTry(branch.Else),
        LLoop loop => HasTry(loop.Body) || HasTry(loop.Step),
        LSwitch choice => choice.Sections.Any(s => HasTry(s.Body)) || HasTry(choice.Default),
        _ => false,
    };

    /// <summary>LANG-380: the EH scopes around each label of the body, outermost first, by <see cref="EhScope.Key"/>.</summary>
    private readonly Dictionary<Binding.GotoLabel, ImmutableArray<object>> _labelScopes = [];

    private readonly Dictionary<Binding.GotoLabel, IrBlock> _labelBlocks = [];

    /// <summary>The statements that contain a label: emitted also after a jump, since a <c>goto</c> may reach them.</summary>
    private readonly HashSet<LStatement> _labelled = new(ReferenceEqualityComparer.Instance);

    /// <summary>Records the labels below <paramref name="statement"/> with the EH scopes that will be open there; whether there is one.</summary>
    private bool FindLabels(LStatement? statement, ImmutableArray<object> scopes)
    {
        var found = false;
        switch (statement)
        {
            case LLabel label:
                _labelScopes[label.Label] = scopes;
                found = true;
                break;
            case LBlock block:
                foreach (var inner in block.Statements)
                {
                    found |= FindLabels(inner, scopes);
                }

                break;
            case LIf branch:
                found = FindLabels(branch.Then, scopes) | FindLabels(branch.Else, scopes);
                break;
            case LLoop loop:
                found = FindLabels(loop.Body, scopes) | FindLabels(loop.Step, scopes);
                break;
            case LSwitch choice:
                foreach (var section in choice.Sections)
                {
                    found |= FindLabels(section.Body, scopes);
                }

                found |= FindLabels(choice.Default, scopes);
                break;
            case LTryExcept tryExcept:
                found = FindLabels(tryExcept.Body, scopes.Add(tryExcept)) | FindLabels(tryExcept.Handler, scopes.Add(tryExcept.Handler));
                break;
            case LTryFinally tryFinally:
                found = FindLabels(tryFinally.Body, scopes.Add(tryFinally));
                break;
        }

        if (found)
        {
            _labelled.Add(statement!);
        }

        return found;
    }

    private IrBlock LabelBlock(Binding.GotoLabel label)
    {
        if (!_labelBlocks.TryGetValue(label, out var block))
        {
            _labelBlocks.Add(label, block = Block("label", NewConstruct()));
        }

        return block;
    }

    /// <summary>
    /// LANG-380, CMP-1185: jumps to the label after leaving the EH scopes that do not enclose it, innermost first — the regions
    /// of declaration scopes (a <c>goto</c> never leaves a part of a <c>try</c> statement, E2072).
    /// </summary>
    private void Goto(Binding.GotoLabel label)
    {
        var scopes = _labelScopes[label];
        var shared = 0;
        while (shared < scopes.Length && _returnScopes + shared < _scopes.Count && ReferenceEquals(_scopes[_returnScopes + shared].Key, scopes[shared]))
        {
            shared++;
        }

        JumpLeaving(LabelBlock(label), _returnScopes + shared);
    }

    /// <summary>CMP-394: helpers that never raise, always called with <c>call</c>.</summary>
    private static readonly HashSet<string> NeverRaising = new(StringComparer.Ordinal)
    {
        "_UStrAddRef", "_AStrAddRef", "_DynArrayAddRef", "_BeginCatch", "_CurrentExcept", "_IsClass", "_ClassInherits", "_ObjSupports",
        "_UStrEq", "_UStrCmp", "_AStrCmp", "_SStrCmp", "_SetEq", "_SetLe", "_SetUnion", "_SetInter", "_SetDiff", "_SetSymDiff", "_FreeMem",
        "_TlsBase", "_TlsRegister", "_Frac", "_Int",
    };

    private IrBlock? UnwindTarget => _scopes.Count == 0 ? null : _scopes[^1].Pad;

    /// <summary>The pads some invoke unwinds to.</summary>
    private readonly HashSet<IrBlock> _unwindTargets = [];

    /// <summary>
    /// CMP-394: a call, or inside an EH scope (when <paramref name="mayRaise"/>) an <c>invoke</c> continuing in a new block
    /// <c>cont.N</c>; inside a cleanup funclet both carry its token.
    /// </summary>
    private IrLocal? EmitCall(string? name, IrType returnType, IrValue callee, ImmutableArray<IrValue> arguments, int? attributeGroup = null, bool mayRaise = true,
        string callConv = "")
    {
        if (!mayRaise || UnwindTarget is not { } pad)
        {
            return _w.Call(name, returnType, callee, arguments, attributeGroup, _funclet, callConv: callConv);
        }

        _unwindTargets.Add(pad);
        var next = Block("cont", ++_invokeCount);                         // own counter: construct numbers stay as without EH
        var result = _w.Invoke(name, returnType, callee, arguments, next, pad, attributeGroup, _funclet, callConv);
        Start(next);
        return result;
    }

    private IrLocal? CallHelper(string? name, IrType returnType, string helper, IrValue callee, ImmutableArray<IrValue> arguments, int? attributeGroup = null) =>
        EmitCall(name, returnType, callee, arguments, attributeGroup, mayRaise: !NeverRaising.Contains(helper));

    /// <summary>Jumps to <paramref name="target"/> after leaving the EH scopes above <paramref name="depth"/>, innermost first (CMP-391/392).</summary>
    private void JumpLeaving(IrBlock target, int depth)
    {
        var open = _scopes.ToList();
        for (var i = open.Count - 1; i >= depth; i--)
        {
            if (open[i].Region)
            {
                _scopes.RemoveRange(i + 1, _scopes.Count - i - 1);
                TryEnd();                               // CMP-398: the jump closes the region it leaves
            }

            _scopes.RemoveRange(i, _scopes.Count - i);
            open[i].Leave?.Invoke();                    // runs in the scope enclosing the one being left
        }

        _scopes.Clear();
        _scopes.AddRange(open);
        Jump(target);
    }

    private bool LeavesScopes(int depth) => _scopes.Skip(depth).Any(s => s.Leave is not null || s.Region);

    private IrBlock _current = Entry;
    private bool _terminated;

    // CMP-421/422 (-g): the subprogram, the location the next instructions carry, the last statement location emitted (an if
    // arm's jump to the end carries it, DBG-120) and the syntax of the innermost statement being emitted
    private DebugSubprogram? _subprogram;
    private int? _location;
    private int? _lastLocation;
    private Syntax.SyntaxNode? _statementSyntax;
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
        _parameters = _routine is null ? []
            : [.. _routine.ImplicitParameters, .. IrEmitter.WithHighCompanions(_routine.Parameters),
               .. (_routine is { IsSafecall: true, ReturnType: not null } ? [_routine.SafecallResultParameter!] : Array.Empty<ParameterSymbol>())];   // CMP-1202
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
        var debug = BeginSubprogram(link, hidden);
        var values = _w.BeginFunction(new IrFunctionDefinition(_name, _linkage, _routine is null ? IrType.Void : IrEmitter.ReturnType(_routine),
            all, _routine is null ? IrEmitter.RoutineGroup : IrEmitter.AttributeGroupOf(_routine))
        {
            Subprogram = _subprogram?.Node,
            CallConv = _routine is null ? "" : _module.CallConv(_routine),               // CMP-1011
        });
        _w.VolatileAccesses = HasTry(_body.Body);                       // CMP-398
        SetLocation(debug?.Begin);                                      // DBG-117: the prologue at the body's begin
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
            _frameAddress = _w.Alloca(Unique("$frame"), _frame.Type, _frame.Alignment);
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
            _w.Store(_link ?? new IrNullConstant(), _frameAddress!, PtrAlign);
            foreach (var variable in _frame.Variables)
            {
                var address = _w.GetElementPtr(Unique(variable.Name + ".addr"), _frame.Type, _frameAddress!,
                    [IrIntConstant.I32(0), IrIntConstant.I32(_frame.FieldOf(variable))]);
                switch (variable)
                {
                    case ParameterSymbol parameter when IrEmitter.HoldsAddress(parameter):
                        spills.Add((_parameterAddresses[parameter], address, PtrAlign));
                        break;
                    case VariableSymbol when IrEmitter.HoldsAddress(variable):
                        spills.Add((hiddenResult!, address, PtrAlign));
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

        if (debug is not null)
        {
            DeclareVariables(debug, values, hiddenResult, argumentBase: link is null ? 1 : 2);
        }

        if (_body.Cleanup is not null)
        {
            TryBegin(new EhScope(CleanupPadBlock, Leave: null, Region: true));     // CMP-393: the outermost EH scope
        }

        _returnScopes = _scopes.Count;
        FindLabels(_body.Body, []);
        Statement(_body.Body);
        SetLocation(debug?.End);                                        // DBG-118: the epilogue at the final end
        FallThrough(_returnTarget);
        if (_body.Cleanup is { } cleanup)
        {
            // CMP-325 rule 2: the function-level cleanup scope on the normal exit path (outside every EH scope: CMP-398, the
            // block first closes the scope's region)
            Start(CleanupBlock);
            TryEnd();
            _scopes.Clear();
            Statement(cleanup);
            FallThrough(Exit);
        }

        Start(Exit);
        if (_body.SafecallHResult is { } hr)
        {
            _w.Ret(_w.Load(NewTemp(), IrType.I32, _locals[hr], TypeLayout.Of(hr.Type).Alignment));     // CMP-1205: the HResult
        }
        else if (_routine?.ResultVariable is { } result && hiddenResult is null)
        {
            // ABI-081: a small aggregate is returned as the integer holding its bytes
            var local = _body.Locals.Single(l => ReferenceEquals(l.Variable, result));
            var layout = TypeLayout.Of(local.Type);
            _w.Ret(_w.Load(NewTemp(), IrEmitter.ReturnType(_routine), _locals[local], layout.Alignment));
        }
        else if (_routine is MethodSymbol { MethodKind: MethodKind.Constructor, SelfParameter: { } self })
        {
            _w.Ret(_w.Load(NewTemp(), IrType.Ptr, _parameterAddresses[self], PtrAlign));     // ABI-075 / CMP-368: Result := Self
        }
        else
        {
            _w.Ret(null);
        }

        _scopes.Clear();
        if (_body.Cleanup is { } unwindCleanup && _unwindTargets.Contains(CleanupPadBlock))
        {
            // CMP-393: the same finalisation on the unwind path, in a cleanup funclet that unwinds to the caller (the invoke that
            // opens the scope's region unwinds to it, CMP-398)
            Start(CleanupPadBlock);
            var token = _w.CleanupPad(Unique("cl"));
            _funclet = token;
            Statement(unwindCleanup);
            _funclet = null;
            _w.CleanupRet(token, unwind: null);
        }

        _w.EndFunction();
        if (_subprogram is not null)
        {
            _module.Debug!.EndSubprogram(_subprogram);
        }
    }

    // ------------------------------------------------------------------ debug information (CMP-421, 422; debug-info.md §2–§4)

    /// <summary>The locations of a body's begin and final end (DBG-117/118).</summary>
    private sealed record DebugBody(int? Begin, int? End);

    /// <summary>
    /// DBG-110…113: the <c>DISubprogram</c> of a body compiled from source (with <c>-g</c>); null for one without source (DBG-114:
    /// <c>_start</c>, thunks, synthesised bodies), whose instructions then carry no location.
    /// </summary>
    private DebugBody? BeginSubprogram(IrParameter? link, IrParameter? hidden)
    {
        if (_module.Debug is not { } di || _body.Source is not { } source)
        {
            return null;
        }

        var (begin, end) = BodyTokens(source);
        var heading = HeadingToken(source);
        var returnType = _routine is null || hidden is not null ? null
            : _routine.ReturnType ?? (_routine is MethodSymbol { MethodKind: MethodKind.Constructor } constructor ? constructor.Owner : null);
        var parameters = new List<DebugParameterType>();
        if (link is not null)
        {
            parameters.Add(new DebugParameterType(null, ByAddress: false));
        }

        parameters.AddRange(_parameters.Take(_implicitCount).Select(p => new DebugParameterType(p.Type, IrEmitter.IsPassedByAddress(p))));
        if (hidden is not null)
        {
            parameters.Add(new DebugParameterType(_routine!.ReturnType, ByAddress: true));
        }

        parameters.AddRange(_parameters.Skip(_implicitCount).Select(p => new DebugParameterType(p.Type, IrEmitter.IsPassedByAddress(p))));
        var self = _routine is MethodSymbol { SelfParameter: { } selfParameter } ? selfParameter.Type : null;
        _subprogram = di.BeginSubprogram(_routine, DisplayName(_routine, source), _name.Text, heading, begin, end, _linkage != IrLinkage.External, returnType, parameters, self);
        return new DebugBody(di.Location(begin, _subprogram), di.Location(end, _subprogram));
    }

    /// <summary>DBG-111: <c>Routine</c>, <c>TClass.Method</c>, <c>Outer.Nested</c>, the program name, <c>Unit.initialization</c>.</summary>
    private string DisplayName(RoutineSymbol? routine, Syntax.SyntaxNode source) => routine switch
    {
        null => source switch
        {
            Syntax.InitializationSectionSyntax => _module.UnitName + ".initialization",
            Syntax.FinalizationSectionSyntax => _module.UnitName + ".finalization",
            Syntax.TryFinallyStatementSyntax => _name.Text,
            _ => _module.UnitName,
        },
        MethodSymbol method => TypeMembers.DisplayName(method.Owner) + "." + method.Name,       // CMP-1393: TOuter.TInner.M
        { ContainingSymbol: RoutineSymbol parent } => DisplayName(parent, source) + "." + routine.Name,
        _ => routine.Name,
    };

    /// <summary>The body's <c>begin</c> (or section keyword, <c>finally</c>) and its final <c>end</c> (DBG-117, 118, 130).</summary>
    private static (Syntax.SyntaxToken Begin, Syntax.SyntaxToken End) BodyTokens(Syntax.SyntaxNode source) => source switch
    {
        Syntax.CompoundStatementSyntax compound => (compound.BeginKeyword, compound.EndKeyword),
        Syntax.BlockSyntax block => (block.Body.BeginKeyword, block.Body.EndKeyword),
        Syntax.InitializationSectionSyntax section => (section.Keyword, (section.Parent as Syntax.UnitSyntax)?.EndKeyword ?? section.LastToken()),
        Syntax.FinalizationSectionSyntax section => (section.FinalizationKeyword, (section.Parent as Syntax.UnitSyntax)?.EndKeyword ?? section.LastToken()),
        Syntax.TryFinallyStatementSyntax tryFinally => (tryFinally.FinallyKeyword, tryFinally.EndKeyword),
        _ => (source.FirstToken(), source.LastToken()),
    };

    /// <summary>DBG-112: the first token of the routine heading in the implementation (the program keyword, the section keyword).</summary>
    private static Syntax.SyntaxToken HeadingToken(Syntax.SyntaxNode source)
    {
        if (source is Syntax.TryFinallyStatementSyntax or Syntax.InitializationSectionSyntax or Syntax.FinalizationSectionSyntax)
        {
            return BodyTokens(source).Begin;
        }

        for (var node = source.Parent; node is not null; node = node.Parent)
        {
            switch (node)
            {
                case Syntax.RoutineDeclarationSyntax routine:
                    return routine.Heading.FirstToken();
                case Syntax.AnonymousMethodExpressionSyntax anonymous:
                    return anonymous.Keyword;
                case Syntax.ProgramSyntax program:
                    return program.ProgramKeyword ?? BodyTokens(source).Begin;
                case Syntax.LibrarySyntax library:
                    return library.LibraryKeyword;
            }
        }

        return BodyTokens(source).Begin;
    }

    /// <summary>
    /// DBG-136…139: one <c>#dbg_declare</c> per parameter and named local in the entry block (location: <c>scopeLine</c>). A
    /// variable living at a received address (by-address parameter, hidden <c>Result</c>) gets an alloca holding that pointer and
    /// <c>DW_OP_deref</c>; a captured one is described on the frame record (<c>DW_OP_plus_uconst</c>, CMP-422). CMP-1184 (C-309):
    /// an inline variable carries its line range to the sidecar, a later declaration of its name in the routine is <c>Name$2</c>…
    /// (DBG-142), and one captured by an anonymous method is described on the hidden local of its block frame: the instance
    /// that local points to (<c>DW_OP_deref</c>) plus the offset of its field. In a unit-level body (C-316, C-320) inline variables
    /// are internal globals and so is the hidden local of a block frame: each is declared on a slot holding the global's address.
    /// </summary>
    private void DeclareVariables(DebugBody debug, ImmutableArray<IrLocal> values, IrValue? hiddenResult, int argumentBase)
    {
        var di = _module.Debug!;
        var subprogram = _subprogram!;
        var location = debug.Begin ?? throw new InvalidOperationException("A subprogram without a begin location.");
        var headingLine = _body.Source is { } source ? DebugInfoEmitter.LineOf(HeadingToken(source)) : 0;
        var declarations = new List<(IrValue Address, int Variable, string Expression)>();
        var bodyEnd = _body.Source is { } bodySource ? BodyTokens(bodySource).End : default;

        // CMP-1183: the captured inline variables of the body's block frames, each with the frame's hidden local
        var blocks = (_routine is null ? _body.UnitClosure : _routine.Closure)?.Blocks ?? [];
        var inFrames = blocks.SelectMany(b => b.Fields.Select(f => (Variable: f.Key as VariableSymbol, Frame: b, Field: f.Value)))
            .Where(f => f.Variable?.InlineScope is not null).OrderBy(f => f.Variable!.InlineScope!.Value.From.Span.Start).ToList();

        // CMP-1184 (C-316): the inline and handler variables of a unit-level body (the main block, an initialization or
        // finalization part, with their finally parts) are internal globals; each is a variable of this subprogram all the same
        var inGlobals = _routine is null && _body.Source is { } unitBody and not Syntax.TryFinallyStatementSyntax
            ? _module.BodyGlobals.Select(g => g.Variable)
                .Where(v => v.InlineScope is { } scope && !v.Name.StartsWith('$') && unitBody.Span.Contains(scope.From.Span.Start))
                .OrderBy(v => v.InlineScope!.Value.From.Span.Start).ToList()
            : [];

        // DBG-142: the first declaration of a name keeps it, later ones (source order) are Name$2, Name$3, …
        var debugNames = new Dictionary<VariableSymbol, string>(ReferenceEqualityComparer.Instance);
        var declared = new Dictionary<string, int>(Syntax.IdentifierComparer.Instance);
        foreach (var inline in _body.Locals.Select(l => l.Variable).Concat(inFrames.Select(f => f.Variable)).Concat(inGlobals)
            .Where(v => v?.InlineScope is not null && !v.Name.StartsWith('$')).OrderBy(v => v!.InlineScope!.Value.From.Span.Start))
        {
            var count = declared[inline!.Name] = declared.GetValueOrDefault(inline.Name) + 1;
            if (count > 1)
            {
                debugNames[inline] = inline.Name + "$" + count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        static string PlusOffset(long offset) => offset == 0 ? "" : "DW_OP_plus_uconst, " + offset.ToString(System.Globalization.CultureInfo.InvariantCulture);
        static string Expression(params string[] parts) => string.Join(", ", parts.Where(e => e.Length > 0));

        int Node(Symbol variable, string name, int? arg, TypeSymbol type, string? flags, string kind)
        {
            var line = variable is ParameterSymbol || variable.Locations.IsEmpty ? headingLine : variable.Locations[0].LineSpan.Start.Line + 1;
            var scope = (variable as VariableSymbol)?.InlineScope;
            int? toLine = scope is { To: var to } && to.Kind != Syntax.SyntaxKind.None && !to.Equals(bodyEnd) ? DebugInfoEmitter.LineOf(to) : null;
            return di.LocalVariable(subprogram, variable is VariableSymbol v && debugNames.TryGetValue(v, out var numbered) ? numbered : name, arg, line, type,
                flags ?? (name.StartsWith('$') ? "DIFlagArtificial" : null), kind, scope is null ? null : DebugInfoEmitter.LineOf(scope.Value.From), toLine);
        }

        void Declare(Symbol variable, string name, int? arg, TypeSymbol type, IrValue? received, IrValue? slot, string? flags, string kind)
        {
            // DBG-137: the storage holds the variable's address (an untyped parameter is described as that pointer itself)
            var deref = received is not null && type is not UntypedTypeSymbol ? "DW_OP_deref" : "";
            var sidecarKind = kind == "param" && deref.Length > 0 ? "varParam" : kind;                     // debug-info-sidecar.md §5
            var node = Node(variable, name, arg, type, flags, sidecarKind);
            if (IsCaptured(variable))
            {
                declarations.Add((_frameAddress!, node, Expression(PlusOffset(_frame!.OffsetOf(variable)), deref)));
            }
            else if (received is not null)
            {
                var reference = _w.Alloca(Unique(name + ".ref"), IrType.Ptr, PtrAlign);
                _w.Store(received, reference, PtrAlign);
                declarations.Add((reference, node, deref));
            }
            else if (slot is not null)
            {
                declarations.Add((slot, node, ""));
            }
        }

        for (var i = 0; i < _parameters.Length; i++)
        {
            var parameter = _parameters[i];
            if (_routine is MethodSymbol { FlagParameter: { } flag } && ReferenceEquals(parameter, flag))
            {
                continue;                                               // the constructor/destructor flag: hidden, no variable
            }

            var arg = argumentBase + i + (hiddenResult is not null && i >= _implicitCount ? 1 : 0);
            var self = _routine is MethodSymbol { SelfParameter: { } selfParameter } && ReferenceEquals(parameter, selfParameter);
            var byAddress = IrEmitter.IsPassedByAddress(parameter);
            Declare(parameter, parameter.Name, arg, parameter.Type, byAddress ? values[i] : null, byAddress ? null : _parameterAddresses.GetValueOrDefault(parameter),
                self ? "DIFlagArtificial | DIFlagObjectPointer" : parameter.Name.Contains('$') ? "DIFlagArtificial" : null, self ? "self" : "param");
        }

        foreach (var local in _body.Locals)
        {
            if (local.Variable is not { } variable)
            {
                continue;                                               // DBG-139: other temporaries get no variable
            }

            var isHiddenResult = hiddenResult is not null && ReferenceEquals(variable, _routine!.ResultVariable);
            var kind = ReferenceEquals(variable, _routine?.ResultVariable) ? "result" : "local";
            Declare(variable, local.Name, arg: null, local.Type, isHiddenResult ? hiddenResult : null, isHiddenResult ? null : _locals.GetValueOrDefault(local), flags: null, kind);
        }

        foreach (var variable in inGlobals)
        {
            Declare(variable, variable.Name, arg: null, variable.Type, _module.Globals[variable], slot: null, flags: null, "local");   // a slot holding the global's address
        }

        // C-320: the hidden local of a unit-level body's block frame is an internal global; one slot per frame holds its address
        var frameGlobals = new Dictionary<Binding.ClosureInfo, IrValue>(ReferenceEqualityComparer.Instance);
        foreach (var (variable, frame, field) in inFrames)
        {
            if (_routine is null)
            {
                if (!_module.Globals.TryGetValue(frame.Local, out var global))
                {
                    continue;
                }

                if (!frameGlobals.TryGetValue(frame, out var reference))
                {
                    frameGlobals[frame] = reference = _w.Alloca(Unique(frame.Local.Name + ".ref"), IrType.Ptr, PtrAlign);
                    _w.Store(global, reference, PtrAlign);
                }

                // the global (DW_OP_deref), the instance it points to (DW_OP_deref), the variable's field
                declarations.Add((reference, Node(variable!, variable!.Name, arg: null, variable.Type, flags: null, "local"), Expression("DW_OP_deref", "DW_OP_deref", PlusOffset(field.Offset))));
                continue;
            }

            // the frame's hidden local: a slot of this body, or a field of its frame record when a nested routine uses it
            var holder = _body.Locals.FirstOrDefault(l => ReferenceEquals(l.Variable, frame.Local));
            var (address, at) = IsCaptured(frame.Local) ? (_frameAddress, PlusOffset(_frame!.OffsetOf(frame.Local)))
                : (holder is null ? null : _locals.GetValueOrDefault(holder), "");
            if (address is not null)
            {
                declarations.Add((address, Node(variable!, variable!.Name, arg: null, variable.Type, flags: null, "local"), Expression(at, "DW_OP_deref", PlusOffset(field.Offset))));
            }
        }

        foreach (var (address, variable, expression) in declarations)
        {
            _w.DebugDeclare(address, variable, expression, location);
        }
    }

    private void SetLocation(int? location)
    {
        _location = location;
        _w.SetDebugLocation(location);
    }

    /// <summary>DBG-116/128: the statement location token; none for compound statements, empty statements and labels.</summary>
    private static Syntax.SyntaxToken StatementToken(Syntax.SyntaxNode syntax) => syntax switch
    {
        Syntax.LabeledStatementSyntax labeled => StatementToken(labeled.Statement),
        Syntax.CompoundStatementSyntax or Syntax.EmptyStatementSyntax => default,
        Syntax.StatementSyntax statement => statement.FirstToken(),
        _ => default,
    };

    /// <summary>The location token of the first statement of <paramref name="statements"/> that has one (looking into compound statements).</summary>
    private static Syntax.SyntaxToken FirstStatementToken(IEnumerable<Syntax.StatementSyntax> statements)
    {
        foreach (var statement in statements)
        {
            var token = statement switch
            {
                Syntax.CompoundStatementSyntax compound => FirstStatementToken(compound.Statements),
                Syntax.LabeledStatementSyntax { Statement: Syntax.CompoundStatementSyntax compound } => FirstStatementToken(compound.Statements),
                _ => StatementToken(statement),
            };
            if (token.Kind != Syntax.SyntaxKind.None)
            {
                return token;
            }
        }

        return default;
    }

    /// <summary>The location of <paramref name="token"/> in this subprogram; the current one when there is none.</summary>
    private int? LocationOf(Syntax.SyntaxToken token) =>
        _subprogram is not null && _module.Debug!.Location(token, _subprogram) is { } location ? location : _location;

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
    /// <see cref="LBlock"/> is dead code: it is not emitted, unless it contains a label (LANG-380).
    /// </summary>
    private void Statement(LStatement statement)
    {
        var (savedLocation, savedSyntax) = (_location, _statementSyntax);
        if (_subprogram is not null && statement.SourceSyntax is { } syntax && StatementToken(syntax) is { Kind: not Syntax.SyntaxKind.None } token
            && _module.Debug!.Location(token, _subprogram) is { } here)
        {
            SetLocation(here);                                          // DBG-116: the statement's first token
            _lastLocation = here;
            _statementSyntax = syntax;
        }

        try
        {
            StatementCore(statement);
        }
        catch (NotSupportedException e) when (e is not EmitNotSupportedException && statement.Location is { } location)
        {
            throw new EmitNotSupportedException(e.Message, location);                     // C-213: the innermost statement's location
        }

        if (_location != savedLocation)
        {
            SetLocation(savedLocation);                                 // the enclosing construct's own code (DBG-121…125)
        }

        _statementSyntax = savedSyntax;
    }

    private void StatementCore(LStatement statement)
    {
        switch (statement)
        {
            case LBlock block:
                foreach (var inner in block.Statements)
                {
                    if (_terminated)
                    {
                        if (!_labelled.Contains(inner))
                        {
                            continue;                                   // dead code: no label, so nothing jumps into it
                        }

                        if (inner is not (LLabel or LBlock))
                        {
                            Start(Block("dead", NewConstruct()));       // reached only through the label inside
                        }
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
                _w.Store(new IrNullConstant(), Value(zero.Address), PtrAlign);       // CMP-325 rule 1: store ptr null
                break;
            case LZero zero:
            {
                var layout = TypeLayout.Of(zero.Type);
                var memset = _module.Intrinsic("llvm.memset.p0.i64", IrType.Void, [IrType.Ptr, IrType.I8, IrType.I64, IrType.I1]);
                _w.Call(null, IrType.Void, memset,
                    [new IrAlignedPointer(Value(zero.Address), layout.Alignment), new IrIntConstant(IrType.I8, 0), IrIntConstant.I64(layout.Size), IrIntConstant.I1(_w.VolatileAccesses)]);
                break;
            }

            case LMemSet fill:
            {
                // CMP-412: FillChar → llvm.memset (the variable's access alignment, CMP-309)
                var address = WrittenAddress(fill.Target);
                var count = Value(fill.Count);
                var value = Value(fill.Value);
                var memset = _module.Intrinsic("llvm.memset.p0.i64", IrType.Void, [IrType.Ptr, IrType.I8, IrType.I64, IrType.I1]);
                _w.Call(null, IrType.Void, memset, [new IrAlignedPointer(address, AccessAlignment(fill.Target)), value, count, IrIntConstant.I1(_w.VolatileAccesses)]);
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
                    [new IrAlignedPointer(destination, AccessAlignment(move.Destination)), new IrAlignedPointer(source, AccessAlignment(move.Source)), count, IrIntConstant.I1(_w.VolatileAccesses)]);
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
            case LBreak jump:
                JumpLeaving(_loops[jump.Label].Break, _loopScopes[jump.Label]);
                break;
            case LContinue jump:
                JumpLeaving(_loops[jump.Label].Continue, _loopScopes[jump.Label]);
                break;
            case LReturn:
                JumpLeaving(_returnTarget, _returnScopes);
                break;
            case LLabel label:
            {
                var block = LabelBlock(label.Label);
                FallThrough(block);
                Start(block);
                break;
            }

            case LGoto jump:
                Goto(jump.Label);
                break;
            case LTryExcept tryExcept:
                TryExcept(tryExcept);
                break;
            case LTryFinally tryFinally:
                TryFinally(tryFinally);
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
                CallHelper(null, returnType, helper.Helper.Name, callee, arguments, helper.Helper.IsNoReturn ? IrEmitter.NoReturnGroup : null);
                break;
            }

            case LCallStatement { Callee: LRoutineCallee callee } call:
                Call(named: false, callee, call.Arguments);
                break;
            case LCallStatement { Callee: LIntrinsicCallee intrinsic } call:
                CallIntrinsic(null, intrinsic, call.Arguments);
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
    /// <remarks>Null too for a jump that leaves an EH scope with work to do on the way (<c>_EndCatch</c>, a <c>$fin</c> call).</remarks>
    private IrBlock? JumpTarget(LStatement statement) => statement switch
    {
        LBreak jump when !LeavesScopes(_loopScopes[jump.Label]) => _loops[jump.Label].Break,
        LContinue jump when !LeavesScopes(_loopScopes[jump.Label]) => _loops[jump.Label].Continue,
        LReturn when !LeavesScopes(_returnScopes) => _returnTarget,
        LBlock { Statements: [var single] } => JumpTarget(single),
        _ => null,
    };

    // ------------------------------------------------------------------ exceptions (CMP-391…393)

    /// <summary>
    /// CMP-391: the try part unwinds to <c>ex.dispatch.N</c> (<c>catchswitch</c> → <c>ex.pad.N</c>: <c>catchpad</c> with the filter,
    /// <c>catchret</c> to <c>ex.body.N</c>); the handler runs as parent code after <c>_BeginCatch</c>, inside the scope whose pad
    /// <c>ex.cleanup.N</c> calls <c>_EndCatch</c> in a cleanup funclet (an invoke when an outer scope exists, C-188); normal
    /// completion calls <c>_EndCatch</c> and continues at <c>try.end.N</c>.
    /// </summary>
    private void TryExcept(LTryExcept statement)
    {
        var id = NewConstruct();
        IrBlock dispatch = Block("ex.dispatch", id), pad = Block("ex.pad", id), handler = Block("ex.body", id),
            cleanup = Block("ex.cleanup", id), end = Block("try.end", id);
        var outer = UnwindTarget;
        if (outer is not null)
        {
            _unwindTargets.Add(outer);                                  // the catchswitch / cleanupret unwind there
        }

        TryBegin(new EhScope(dispatch, Leave: null, statement, Region: true));
        Statement(statement.Body);
        if (!_terminated)
        {
            TryEnd();
        }

        _scopes.RemoveAt(_scopes.Count - 1);
        FallThrough(end);

        var location = _location;
        var syntax = _statementSyntax as Syntax.TryExceptStatementSyntax;
        if (syntax is not null)
        {
            SetLocation(LocationOf(syntax.ExceptKeyword));              // DBG-127: entering the handler at except
        }

        Start(dispatch);
        var catchSwitch = _w.CatchSwitch(Unique("cs." + id), pad, outer);
        Start(pad);
        var filter = _module.Function(new IrFunctionDeclaration(Mangler.RuntimeHelper("_SehFilter"), IrType.I32, [IrType.Ptr, IrType.Ptr]));
        _w.CatchRet(_w.CatchPad(Unique("cp." + id), catchSwitch, [filter]), handler);

        Start(handler);
        var beginCatch = _module.Function(new IrFunctionDeclaration(Mangler.RuntimeHelper("_BeginCatch"), IrType.Ptr, []));
        _w.Store(_w.Call(NewTemp(), IrType.Ptr, beginCatch, [])!, _locals[statement.Exception], PtrAlign);
        TryBegin(new EhScope(cleanup, EndCatch, statement.Handler, Region: true));
        Statement(statement.Handler);
        var completes = !_terminated;
        if (syntax is not null)
        {
            SetLocation(LocationOf(syntax.EndKeyword));                 // DBG-127: cleanup at the closing end
        }

        if (completes)
        {
            TryEnd();
        }

        _scopes.RemoveAt(_scopes.Count - 1);
        if (completes)
        {
            EndCatch();
            Jump(end);
        }

        Start(cleanup);
        Funclet(id, EndCatch, outer);
        SetLocation(location);
        Start(end);
    }

    /// <summary>
    /// CMP-392: the try part unwinds to <c>fin.pad.N</c>, a cleanup funclet calling the outlined <c>$fin</c> routine; normal
    /// completion and jumps out of the try part call it as well.
    /// </summary>
    private void TryFinally(LTryFinally statement)
    {
        var id = NewConstruct();
        IrBlock pad = Block("fin.pad", id), end = Block("try.end", id);
        var outer = UnwindTarget;
        if (outer is not null)
        {
            _unwindTargets.Add(outer);                                  // the catchswitch / cleanupret unwind there
        }

        void CallFinally()
        {
            if (statement.Cleanup is { } cleanup)
            {
                Statement(cleanup);                                     // CMP-1180: a declaration scope clears its variables in place
            }
            else
            {
                Call(named: false, new LRoutineCallee(statement.Finally!), []);
            }
        }

        TryBegin(new EhScope(pad, CallFinally, statement, Region: true));
        Statement(statement.Body);
        var completes = !_terminated;
        var location = _location;
        if (_statementSyntax is Syntax.TryFinallyStatementSyntax syntax)
        {
            SetLocation(LocationOf(syntax.FinallyKeyword));             // DBG-127: running the finally part at finally
        }

        if (completes)
        {
            TryEnd();
        }

        _scopes.RemoveAt(_scopes.Count - 1);
        if (completes)
        {
            CallFinally();
            Jump(end);
        }

        Start(pad);
        Funclet(id, CallFinally, outer);
        SetLocation(location);
        Start(end);
    }

    /// <summary>
    /// The current block becomes a cleanup funclet <c>%cl.N = cleanuppad within none []</c> running <paramref name="body"/> (its calls
    /// carry the token; with an enclosing scope they are invokes unwinding there), then <c>cleanupret … unwind</c> to the enclosing
    /// scope's pad or to the caller.
    /// </summary>
    private void Funclet(int id, Action body, IrBlock? outer)
    {
        var token = _w.CleanupPad(Unique("cl." + id));
        var enclosing = _funclet;
        _funclet = token;
        body();
        _funclet = enclosing;
        _w.CleanupRet(token, outer);
        _terminated = true;
    }

    /// <summary>ABI-122 <c>_EndCatch</c>: may raise (the object's destructor), so an invoke inside an enclosing scope (C-188).</summary>
    private void EndCatch() =>
        EmitCall(null, IrType.Void, _module.Function(new IrFunctionDeclaration(Mangler.RuntimeHelper("_EndCatch"), IrType.Void, [])), []);

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
            Arm(then, statement.Then, end, jumpAtLastStatement: true);
        }

        if (elseTarget is null)
        {
            Arm(@else, statement.Else!, end, jumpAtLastStatement: true);
        }

        Start(end);
    }

    /// <param name="jumpAtLastStatement">DBG-120: the jump to <paramref name="end"/> carries the arm's last statement location (no extra stop).</param>
    private void Arm(IrBlock block, LStatement body, IrBlock end, bool jumpAtLastStatement = false)
    {
        Start(block);
        _lastLocation = null;
        Statement(body);
        if (jumpAtLastStatement && !_terminated && _lastLocation is { } last && last != _location)
        {
            var location = _location;
            _w.SetDebugLocation(last);
            FallThrough(end);
            _w.SetDebugLocation(location);
            return;
        }

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
        _loopScopes[loop.Label] = _scopes.Count;
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
        var location = _location;
        if (loop.SourceSyntax is Syntax.RepeatStatementSyntax repeat && _subprogram is not null)
        {
            // DBG-124: repeat has no location (the jump into the loop is the first statement's); the condition and the jumps to it
            // are at until
            var until = LocationOf(repeat.UntilKeyword);
            SetLocation(FirstStatementToken(repeat.Statements) is { Kind: not Syntax.SyntaxKind.None } first ? LocationOf(first) : until);
            Jump(body);
            SetLocation(until);
        }
        else
        {
            Jump(body);
        }

        Arm(body, loop.Body, step ?? body);
        if (step is not null)
        {
            Arm(step, loop.Step!, body);
        }

        SetLocation(location);
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
            Arm(arms[k], statement.Sections[k].Body, end, jumpAtLastStatement: true);
        }

        if (statement.Default is { } body)
        {
            Arm(@default, body, end, jumpAtLastStatement: true);
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
                if (record.HasVariantPart)
                {
                    // CMP-307 byte form for a record with a variant part (C-108): getelementptr inbounds i8, ptr %r, i64 <offset>
                    return field.Field.Offset == 0 ? address : _w.GetElementPtr(NewTemp(), IrType.I8, address, [IrIntConstant.I64(field.Field.Offset)]);
                }

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
        CallHelper(null, IrType.Void, helper, unique, [stringAddress]);
        return StringElement(element, _w.Load(NewTemp(), IrType.Ptr, stringAddress, PtrAlign));
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
            var length = element.Length is { } count ? Value(count) : ArrayLength(array);         // an open array: High + 1 (C-106)
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
        var field = _w.GetElementPtr(NewTemp(), IrType.I8, array, [IrIntConstant.I64(_module.DynArrayLengthOffset)]);
        var length = Widen(_w.Load(NewTemp(), _module.NativeInt, field, TypeLayout.NativeInt(_module.Target).Alignment));
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
            [new IrAlignedPointer(destination, destinationAlignment), new IrAlignedPointer(source, sourceAlignment), IrIntConstant.I64(layout.Size), IrIntConstant.I1(_w.VolatileAccesses)]);
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
        return IrEmitter.HoldsAddress(outer.Variable) ? _w.Load(NewTemp(), IrType.Ptr, field, PtrAlign) : field;
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
            frame = _w.Load(NewTemp(), IrType.Ptr, frame, PtrAlign);
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
                // Self is the VMT itself for class methods and for an allocating constructor call (CMP-367: through a class reference)
                var vmt = method.IsClassMethod || method.MethodKind == MethodKind.Constructor && callee.Flag == 1 ? self : _w.Load(NewTemp(), IrType.Ptr, self, PtrAlign);
                // ABI-093: an interface method's code is slot k of the IMT the interface reference points to (CMP-373)
                var slot = _w.GetElementPtr(NewTemp(), IrType.I8, vmt, [IrIntConstant.I64(method.ContainingInterface is null ? _module.VmtSlotOffset(method.VmtSlot) : _module.ImtSlotOffset(method.VmtSlot))]);
                function = _w.Load(NewTemp(), IrType.Ptr, slot, PtrAlign);
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
            if (arguments[i] is LOpenArray open)
            {
                // ABI-080 (C-106): ptr to element 0, then i64 High (a dynamic array's from its length)
                var data = Value(open.Data);
                values.Add(data);
                if (open.High is { } high)
                {
                    values.Add(Value(high));
                }
                else
                {
                    var length = ArrayLength(data);
                    values.Add(_w.Binary(NewTemp(), IrBinaryOperator.Sub, length, IrIntConstant.I64(1)));
                }
                continue;
            }

            if (!IrEmitter.IsPassedByAddress(parameter) && IrEmitter.IsPassedAsInteger(parameter.Type))
            {
                var address = Address(arguments[i]);
                values.Add(_w.Load(NewTemp(), IrEmitter.ParameterType(parameter), address, AccessAlignment(arguments[i])));
                continue;
            }

            var value = Value(arguments[i]);
            values.Add(IrEmitter.IsPassedByAddress(parameter) ? value : ToStorage(value, parameter.Type));
        }

        if (callee.SafecallResult is { } safecallResult)
        {
            values.Add(Value(safecallResult));                                  // CMP-1203: ptr Ret, last
        }

        // CMP-1011: the convention of the routine, or of the procedural type's signature for a call through a value
        return EmitCall(named ? NewTemp() : null, IrEmitter.ReturnType(routine), function ?? _module.Routine(routine), values.ToImmutable(),
            callConv: _module.CallConv(routine));
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
            case LGuidRef guid:
                return _module.Guid(guid.Interface);                                // CMP-373: ptr @"U.IBar.$guid"
            case LDispDescriptor descriptor:
                return _module.DispDescriptor(descriptor);                          // CMP-1209: ptr @"U.$disp.n"
            case LFormTable table:
                return new IrGlobal(Mangler.FormTable(table.Unit));                    // CMP-415 (C-114)
            case LTlsDescriptor descriptor:
                return _module.TlsDescriptor(descriptor.Unit);                      // ABI-180
            case LThreadVarAddress threadVar:
            {
                // CMP-414: _TlsBase() + the unit's block offset (descriptor field 1) + the variable's record offset
                var tlsBase = CallHelper(NewTemp(), IrType.Ptr, "_TlsBase", _module.Function(
                    new IrFunctionDeclaration(Mangler.RuntimeHelper("_TlsBase"), IrType.Ptr, [])), [])!;
                var descriptor = _module.TlsDescriptor(threadVar.Variable.ContainingUnit!);
                var offset = Widen(_w.Load(NewTemp(), _module.NativeInt, new IrGepConstant(IrType.I8, descriptor, [IrIntConstant.I64(_module.Target.PointerSize)]),
                    TypeLayout.NativeInt(_module.Target).Alignment));
                var block = _w.GetElementPtr(NewTemp(), IrType.I8, tlsBase, [offset]);
                return _w.GetElementPtr(NewTemp(), IrType.I8, block, [IrIntConstant.I64(threadVar.Variable.ThreadVarOffset!.Value)]);
            }
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

            case LCall { Callee: LIntrinsicCallee intrinsic } call:
                return CallIntrinsic(NewTemp(), intrinsic, call.Arguments)!;
            case LCall { Callee: LHelperCallee helper } call:
            {
                var arguments = call.Arguments.Select((a, i) => ToStorage(Value(a), helper.Helper.ParameterTypes[i])).ToImmutableArray();
                var storage = TypeLayout.Of(call.Type).StorageType;
                var result = CallHelper(NewTemp(), storage, helper.Helper.Name, _module.Helper(helper.Helper), arguments)!;
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

    /// <summary>CMP-413 (C-173, C-174): a plain call of an LLVM intrinsic, never an invoke (intrinsics do not raise).</summary>
    private IrLocal? CallIntrinsic(string? name, LIntrinsicCallee intrinsic, ImmutableArray<LExpression> arguments)
    {
        var values = arguments.Select((a, i) => ToStorage(Value(a), intrinsic.ParameterTypes[i])).ToImmutableArray();
        var returnType = intrinsic.ReturnType is { } type ? TypeLayout.Of(type).StorageType : IrType.Void;
        var callee = _module.Intrinsic(intrinsic.Name, returnType, [.. intrinsic.ParameterTypes.Select(t => TypeLayout.Of(t).StorageType)]);
        return _w.Call(name, returnType, callee, values);
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
        CallHelper(null, IrType.Void, helper, _module.NoReturnHelper(helper), [], IrEmitter.NoReturnGroup);
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

    private static bool IsSigned(TypeSymbol type) => type is IntegerTypeSymbol { IsSigned: true } or RealTypeSymbol { RealKind: RealKind.Currency };

    private static bool IsCurrency(TypeSymbol type) => type is RealTypeSymbol { RealKind: RealKind.Currency };

    /// <summary>
    /// C-218: conversions to and from Currency (an <c>i64</c> holding value × 10000): an integer is extended to <c>i64</c> and
    /// multiplied by 10000; a real is multiplied by 10000.0 (as Double) and rounded by <c>llvm.llrint</c> (current rounding mode,
    /// nearest-even by default); Currency → real is <c>sitofp</c> divided by 10000.0.
    /// </summary>
    private IrValue CurrencyConvert(IrValue value, TypeSymbol from, TypeSymbol to)
    {
        var scale = new IrFloatConstant(IrType.Double, 10000);
        if (IsCurrency(to))
        {
            switch (value)
            {
                case IrIntConstant constant:
                    return new IrIntConstant(IrType.I64, constant.Value * 10000);
                case { Type: IntegerIrType source }:
                {
                    var wide = source.Bits < 64 ? _w.Cast(NewTemp(), IsSigned(from) ? IrCastOperator.SExt : IrCastOperator.ZExt, value, IrType.I64) : value;
                    return _w.Binary(NewTemp(), IrBinaryOperator.Mul, wide, new IrIntConstant(IrType.I64, 10000));
                }

                case { Type: FloatingIrType floating }:
                {
                    var real = floating.Kind == FloatingKind.Float ? _w.Cast(NewTemp(), IrCastOperator.FPExt, value, IrType.Double) : value;
                    var scaled = _w.Binary(NewTemp(), IrBinaryOperator.FMul, real, scale);
                    var llrint = _module.Intrinsic("llvm.llrint.i64.f64", IrType.I64, [IrType.Double]);
                    return _w.Call(NewTemp(), IrType.I64, llrint, [scaled])!;
                }
            }
        }
        else if (IrEmitter.ValueType(to) is FloatingIrType target)
        {
            IrValue real = value is IrIntConstant constant
                ? new IrFloatConstant(IrType.Double, (double)constant.Value / 10000)
                : _w.Binary(NewTemp(), IrBinaryOperator.FDiv, _w.Cast(NewTemp(), IrCastOperator.SIToFP, value, IrType.Double), scale);
            return target.Kind == FloatingKind.Float ? _w.Cast(NewTemp(), IrCastOperator.FPTrunc, real, target) : real;
        }

        throw new NotSupportedException($"Conversion from '{from.Name}' to '{to.Name}' is not emitted yet.");
    }

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
        if (IsCurrency(from) != IsCurrency(to))
        {
            return CurrencyConvert(value, from, to);
        }

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
