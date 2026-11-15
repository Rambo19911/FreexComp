using System.Collections.Immutable;
using System.Numerics;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Emit;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Lowering;

/// <summary>
/// Bound trees of one unit → lowered trees (architecture.md §3.2, CMP-116). The input must be free of binder errors
/// (the lowerer reports nothing; it throws on error nodes). This skeleton translates the M2.1 statement and expression
/// set; the structured statements and intrinsics are delegated to <see cref="ForRewriter"/>, <see cref="CaseRewriter"/>,
/// <see cref="IntrinsicRewriter"/> and <see cref="WriteRewriter"/>. Variables of enclosing routines become <see cref="LOuterRef"/>;
/// their owner gets a <see cref="LoweredFrame"/> (CMP-345); range and overflow checks come from <see cref="CheckRewriter"/>.
/// String constants become <see cref="LStringLiteral"/> (CMP-320).
/// </summary>
internal static class Lowerer
{
    public static LoweredUnit Lower(BindingResult result, ImmutableArray<Forms.EmbeddedForm> forms = default)
    {
        // CMP-325 rule 7 / LANG-523: a unit's managed globals are released at the end of its $final (after the finalization
        // statements); a program's by a synthesised program $final, the last unit table entry, so they are released after the
        // ExitProc chain (ABI-104 steps 1–2, C-183)
        var managed = ManagedGlobals(result.Unit);
        var outlined = new List<LoweredBody>();                                // CMP-392 $fin routines
        var bodyGlobals = new List<(VariableSymbol, LinkName)>();
        var routines = result.Routines.Select(r => LowerRoutine(result, r, outlined)).ToImmutableArray();
        var bodies = result.Bodies.Select(b => (b.Syntax, Lowered: LowerBody(result, b,
            b.Syntax is FinalizationSectionSyntax ? managed : [], BodyName(b.Syntax), outlined, bodyGlobals))).ToImmutableArray();
        var finalization = bodies.FirstOrDefault(b => b.Syntax is FinalizationSectionSyntax).Lowered;
        if (finalization is null && !managed.IsEmpty)
        {
            finalization = LowerBody(result, block: null, managed, "$final", outlined, bodyGlobals);
        }

        return new(result.Unit, [.. routines, .. outlined], [.. bodies.Select(b => b.Lowered)])
        {
            BodyGlobals = [.. bodyGlobals],
            Initialization = bodies.FirstOrDefault(b => b.Syntax is InitializationSectionSyntax).Lowered,
            Finalization = finalization,
            Statics = [.. result.Routines.SelectMany(r => r.Scope.Symbols.OfType<ConstantSymbol>()).Select(c => c.Storage).OfType<VariableSymbol>()],
            ThreadVars = ThreadVars(result.Unit),
            TlsRegistration = ThreadVars(result.Unit).IsEmpty ? null
                : new LCallStatement(new LHelperCallee(new RuntimeHelper("_TlsRegister", [result.Types.Pointer], ReturnType: null, IsNoReturn: false)),
                    [new LTlsDescriptor(result.Unit, result.Types.Pointer)]),                                        // ABI-180
            Forms = forms.IsDefault ? [] : forms,
            FormRegistration = forms.IsDefaultOrEmpty ? null
                : new LCallStatement(new LHelperCallee(new RuntimeHelper("_RegisterResources", [result.Types.Pointer, result.Types.NativeInt], ReturnType: null, IsNoReturn: false)),
                    [new LFormTable(result.Unit, result.Types.Pointer), new LConst(new ConstantValue(new System.Numerics.BigInteger(forms.Length), result.Types.NativeInt), result.Types.NativeInt)]),   // ABI-150 (C-114)
        };
    }

    /// <summary>The unit's (or program's) threadvars in declaration order (interface first; LANG-345, ABI-180).</summary>
    public static ImmutableArray<VariableSymbol> ThreadVars(UnitSymbol unit) =>
        [.. unit.InterfaceMembers.Concat(unit.ImplementationMembers).OfType<VariableSymbol>().Where(v => v.IsThreadVar).Distinct()];

    /// <summary>The unit's (or program's) global variables of managed types in declaration order (interface first; LANG-523).</summary>
    public static ImmutableArray<VariableSymbol> ManagedGlobals(UnitSymbol unit) =>
        [.. unit.InterfaceMembers.Concat(unit.ImplementationMembers).OfType<VariableSymbol>()
            .Concat(DeclarationBinder.ClassVariablesOf(unit))                                              // then class vars (LANG-615, 730)
            .Concat(InstanceClassVariables(unit))
            .Where(v => v is { IsGlobal: true, IsReadOnly: false, Type.IsManaged: true })];

    /// <summary>
    /// C-222: the class vars of every instance (made anywhere in the compilation) of the generic types the unit declares, by instance
    /// link name. The declaring unit owns them: it is initialised before and finalised after every unit using an instance, so it
    /// releases their managed ones once (the other modules only define the <c>linkonce_odr</c> storage).
    /// </summary>
    public static IEnumerable<VariableSymbol> InstanceClassVariables(UnitSymbol unit) =>
        unit.InterfaceMembers.Concat(unit.ImplementationMembers).OfType<GenericTypeSymbol>().Distinct()
            .SelectMany(g => g.Instances.Values)
            .Where(i => i.TypeKind != TypeKind.Error).Distinct()
            .OrderBy(i => i.QualifiedName, StringComparer.Ordinal)
            .SelectMany(i => i switch { ClassTypeSymbol c => c.ClassVariables, RecordTypeSymbol r => r.ClassVariables, _ => [] });

    /// <param name="outlined">Receives the <c>$fin</c> routines outlined from the body (CMP-392).</param>
    public static LoweredBody LowerRoutine(BindingResult result, BoundRoutineBody body, List<LoweredBody> outlined)
    {
        var captured = result.GetCapturedVariables(body.Routine);
        var context = new LoweringContext(result.Types, body.Routine, body.Scope.Symbols.OfType<VariableSymbol>().Distinct())
        {
            Unit = result.Unit,
            Output = OutputOf(result),
            Input = SystemVariable(result, "Input"),
            CheckedSystem = CheckedSystemOf(result),
            Captured = [.. captured],
            AddressTaken = body.AddressTaken,
        };
        var lowered = context.Finish(body.Body);
        outlined.AddRange(context.Outlined);
        // CMP-392: the variables the $fin routines use live in the frame too (after those of nested routines)
        ImmutableArray<Symbol> frame = [.. captured, .. context.FinCaptured.Where(v => !captured.Contains(v))];
        // CMP-345: a frame when nested routines capture something of this routine, and for a nested routine with nested
        // routines of its own (field 0 continues the static link chain they walk)
        var needsFrame = !frame.IsEmpty || body.Routine.IsNested && (!body.NestedRoutines.IsEmpty || context.HasOutlined);
        return needsFrame ? lowered with { Frame = new LoweredFrame(frame) } : lowered;
    }

    /// <summary>
    /// The program block or a unit initialization/finalization section (globals only, no locals); <paramref name="finalised"/>
    /// globals are released, in reverse order, in its cleanup scope (so also after <c>Exit</c>). A null block is an empty one.
    /// <paramref name="name"/> (<c>$main</c>, <c>$init</c>, <c>$final</c>) prefixes the names of its <c>$fin</c> routines.
    /// </summary>
    /// <param name="bodyGlobals">Receives the storage of the body's handler variables (<see cref="LoweredUnit.BodyGlobals"/>).</param>
    public static LoweredBody LowerBody(BindingResult result, BoundBlock? block, ImmutableArray<VariableSymbol> finalised, string name, List<LoweredBody> outlined,
        List<(VariableSymbol, LinkName)> bodyGlobals)
    {
        var context = new LoweringContext(result.Types, routine: null, locals: [])
        {
            Unit = result.Unit,
            BodyName = name,
            BodyGlobals = bodyGlobals,
            Output = OutputOf(result),
            Input = SystemVariable(result, "Input"),
            CheckedSystem = CheckedSystemOf(result),
        };
        var lowered = context.Finish(block, finalised);
        outlined.AddRange(context.Outlined);
        return lowered;
    }

    private static string BodyName(SyntaxNode syntax) => syntax switch
    {
        InitializationSectionSyntax => "$init",
        FinalizationSectionSyntax => "$final",
        _ => "$main",
    };

    /// <summary>Types held in memory as <c>[N x T]</c> or structs (big sets, static arrays, records): never SSA values (see the aggregate nodes).</summary>
    public static bool IsAggregate(TypeSymbol type) =>
        type is SetTypeSymbol { IsSmall: false } or ArrayTypeSymbol or RecordTypeSymbol or StringTypeSymbol { Variant: StringVariant.Short } or MethodPointerTypeSymbol;

    /// <summary>The bound <c>System</c> whose text-file routines get <c>_IOCheck</c> (ABI-132); null while lowering System itself.</summary>
    private static UnitSymbol? CheckedSystemOf(BindingResult result) => ReferenceEquals(result.System, result.Unit) ? null : result.System;

    /// <summary>The variable <c>Output</c> of the bound <c>System</c> (default file of <c>Write</c>, LANG-910), or null without one.</summary>
    private static VariableSymbol? OutputOf(BindingResult result) => SystemVariable(result, "Output");

    /// <summary>The interface variable <paramref name="name"/> of the bound <c>System</c> (<c>Output</c>, <c>Input</c>), or null.</summary>
    private static VariableSymbol? SystemVariable(BindingResult result, string name) =>
        result.System?.InterfaceMembers.OfType<VariableSymbol>().FirstOrDefault(v => IdentifierComparer.Instance.Equals(v.Name, name));
}

/// <summary>Per-body lowering state (locals, temporaries, loop labels) and the translation of plain nodes.</summary>
internal sealed partial class LoweringContext
{
    private readonly Dictionary<VariableSymbol, LLocal> _variables = [];
    private readonly List<LLocal> _locals = [];
    private readonly Stack<LLoopLabel> _loops = new();
    private int _tempCount;
    private int _valueCount;
    private int _loopCount;

    public LoweringContext(BuiltInTypes types, RoutineSymbol? routine, IEnumerable<VariableSymbol> locals)
    {
        Types = types;
        Routine = routine;
        foreach (var variable in locals)
        {
            var local = new LLocal(variable.Name, variable.Type, variable);
            _variables.Add(variable, local);
            _locals.Add(local);
        }

        RunError = new RuntimeHelper("_RunError", [types.Integer], ReturnType: null, IsNoReturn: true);
        Halt = new RuntimeHelper("_Halt", [types.Integer], ReturnType: null, IsNoReturn: true);
    }

    public BuiltInTypes Types { get; }

    public RoutineSymbol? Routine { get; }

    /// <summary>The unit being lowered: the container of the <c>$fin</c> routines of a unit-level body (CMP-392).</summary>
    public UnitSymbol? Unit { get; init; }

    /// <summary>A unit-level body's function name (<c>$main</c>, <c>$init</c>, <c>$final</c>): the prefix of its <c>$fin</c> names.</summary>
    public string BodyName { get; init; } = "$main";

    /// <summary>
    /// Unit-level bodies and their <c>$fin</c> routines: the handler variables living in module globals (shared with the outlined
    /// routines, <see cref="LoweredUnit.BodyGlobals"/>); null inside routines, whose handler variables are locals.
    /// </summary>
    public List<(VariableSymbol Variable, LinkName Name)>? BodyGlobals { get; init; }

    /// <summary>The <c>$fin</c> routines outlined from this body and, after each, those outlined from it (CMP-392).</summary>
    public List<LoweredBody> Outlined { get; } = [];

    /// <summary>Whether this body called an outlined <c>$fin</c> routine (it is then their parent, CMP-345).</summary>
    public bool HasOutlined => _finCount > 0;

    /// <summary>Variables of enclosing routines referenced here or in the outlined routines (<see cref="LOuterRef"/>), first-use order.</summary>
    public IReadOnlyList<Symbol> OuterReferences => _outer;

    private readonly List<Symbol> _outer = [];
    private readonly List<VariableSymbol> _managedBodyGlobals = [];
    private readonly List<Symbol> _finCaptured = [];
    private int _finCount;

    /// <summary>This routine's variables, parameters and <c>Result</c> that its outlined <c>$fin</c> routines use (first-use order).</summary>
    public IReadOnlyList<Symbol> FinCaptured => _finCaptured.Distinct().ToList();

    private LOuterRef Outer(Symbol variable, TypeSymbol type)
    {
        NoteOuter(variable);
        return new LOuterRef(variable, type);
    }

    private void NoteOuter(Symbol variable)
    {
        if (!_outer.Contains(variable))
        {
            _outer.Add(variable);
        }
    }

    /// <summary>A local declared inside the body (LANG-821: the variable of an <c>on E: T do</c> handler).</summary>
    private LLocal DeclareLocal(VariableSymbol variable)
    {
        var local = new LLocal(variable.Name, variable.Type, variable);
        _variables.Add(variable, local);
        _locals.Add(local);
        return local;
    }

    /// <summary><c>System.Output</c> as bound (LANG-910); null when the unit was bound without <c>System</c> (white-box tests).</summary>
    public VariableSymbol? Output { get; init; }

    /// <summary><c>System.Input</c> as bound (default file of <c>Read</c>/<c>Eof</c>, LANG-940); null without <c>System</c>.</summary>
    public VariableSymbol? Input { get; init; }

    /// <summary>
    /// The unit <c>System</c> when lowering another unit: a call statement of one of its routines whose first parameter is
    /// <c>var … : Text</c> is followed by <c>_IOCheck</c> in <c>{$I+}</c> state (ABI-132, C-182). Null inside System, which
    /// handles <c>InOutRes</c> itself.
    /// </summary>
    public UnitSymbol? CheckedSystem { get; init; }

    /// <summary>This routine's variables, parameters and <c>Result</c> that nested routines reference (they live in the frame record).</summary>
    public ImmutableHashSet<Symbol> Captured { get; init; } = [];

    /// <summary>This routine's variables whose address is taken: never written directly as a hidden <c>Result</c> (C-062).</summary>
    public ImmutableHashSet<Symbol> AddressTaken { get; init; } = [];

    /// <summary><c>procedure _RunError(Code: Integer)</c>, noreturn (rtl-abi.md §9.1).</summary>
    public RuntimeHelper RunError { get; }

    /// <summary><c>procedure _Halt(Code: Integer)</c>, noreturn (CMP-356).</summary>
    public RuntimeHelper Halt { get; }

    public LoweredBody Finish(BoundBlock? body, ImmutableArray<VariableSymbol> finalisedGlobals = default)
    {
        // CMP-380 (C-120): a body with anonymous methods first creates its closure
        var closure = Routine is null ? body?.Closure : Routine.Closure;
        ImmutableArray<LStatement> prologue = closure is null ? [] : ClosurePrologue(closure);
        var block = Routine is MethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.Destructor } method && body is not null
            ? Protocol(method, body)
            : body is null ? new LBlock([]) : (LBlock)Lower(body);
        if (!prologue.IsEmpty)
        {
            block = new LBlock([.. prologue, .. block.Statements]);
        }
        if (Routine is MethodSymbol { IsRecordConstructor: true, ResultVariable: { } constructed })
        {
            // LANG-730 (C-122): the result is zero-filled before the body runs (a managed one released first: it may hold the
            // caller's old value, CMP-325 rule 4)
            var result = new LLocalRef(_variables[constructed]);
            LStatement[] zero = constructed.Type.IsManaged
                ? [ManagedRewriter.Clear(this, result), new LZero(new LAddrOf(result, Types.Pointer), constructed.Type)]
                : [new LZero(new LAddrOf(result, Types.Pointer), constructed.Type)];
            block = new LBlock([.. zero, .. block.Statements]);
        }

        var (copies, releases) = OpenArrayCopies();
        var lowered = ManagedRewriter.AddLifetimes(this, new LoweredBody(Routine, [.. _locals], copies.IsEmpty ? block : new LBlock([.. copies, .. block.Statements])));
        releases = [.. releases, .. _managedBodyGlobals.Select(v => ManagedRewriter.Clear(this, new LGlobalRef(v)))];
        if (!releases.IsEmpty)
        {
            lowered = lowered with { Cleanup = new LBlock([.. lowered.Cleanup?.Statements ?? [], .. releases]) };
        }

        if (finalisedGlobals.IsDefaultOrEmpty)
        {
            return lowered;
        }

        // CMP-325 rule 7: after the body's own temporaries, the globals in reverse declaration order (CMP-350)
        var clears = finalisedGlobals.Reverse().Select(v => ManagedRewriter.Clear(this, new LGlobalRef(v)));
        return lowered with { Cleanup = new LBlock([.. lowered.Cleanup?.Statements ?? [], .. clears]) };
    }

    /// <summary>The largest value open array copied onto the stack (LANG-432); larger ones go to the heap.</summary>
    private const int OpenArrayStackLimit = 4096;

    /// <summary>
    /// LANG-432 (C-106): each value open array parameter is copied in the prologue — into a 4096-byte stack buffer when its
    /// <c>(High + 1) × SizeOf(T)</c> bytes fit, else into <c>_GetMem</c> storage — and the parameter then points at the copy;
    /// managed elements get <c>_AddRefArray</c>. The cleanup scope releases them (<c>_FinalizeArray</c>, <c>_FreeMem</c> of a heap copy).
    /// </summary>
    private (ImmutableArray<LStatement> Prologue, ImmutableArray<LStatement> Cleanup) OpenArrayCopies()
    {
        var prologue = new List<LStatement>();
        var cleanup = new List<LStatement>();
        foreach (var parameter in Routine?.Parameters.Where(p => p is { Mode: ParameterMode.Value, Type: OpenArrayTypeSymbol }) ?? [])
        {
            var type = (OpenArrayTypeSymbol)parameter.Type;
            var data = new LParamRef(parameter);
            LExpression Count() => new LBinary(LBinaryOperator.Add, new LParamRef(parameter.HighParameter!), Constant(BigInteger.One, Types.Int64));
            var bytes = new LLocalRef(NewTemp(Types.Int64));
            var source = new LLocalRef(NewTemp(type));
            var byteIndex = new IntegerTypeSymbol($"0..{OpenArrayStackLimit - 1}", (IntegerTypeSymbol)Types.Integer, BigInteger.Zero, OpenArrayStackLimit - 1, containingSymbol: null, []);
            var buffer = new LLocalRef(NewTemp(new ArrayTypeSymbol($"array[0..{OpenArrayStackLimit - 1}] of Byte", byteIndex, Types.Byte, containingSymbol: null, [])));
            var fits = Compare(LCompareOperator.LessOrEqual, bytes, Constant(OpenArrayStackLimit, Types.Int64));
            var getMem = new LCall(new LHelperCallee(new RuntimeHelper("_GetMem", [Types.NativeInt], type, IsNoReturn: false)), [bytes]);
            prologue.Add(new LAssign(bytes, new LBinary(LBinaryOperator.Multiply, Count(), Constant(type.ElementType.Size, Types.Int64))));
            prologue.Add(new LAssign(source, data));
            prologue.Add(new LIf(fits, new LAssign(data, new LAddrOf(buffer, type)), new LAssign(data, getMem)));
            prologue.Add(new LMemMove(new LDeref(source, Types.Byte), new LDeref(data, Types.Byte), bytes));
            if (type.ElementType.IsManaged)
            {
                var info = new LTypeInfo(type.ElementType, Types.Pointer);
                prologue.Add(new LCallStatement(new LHelperCallee(new RuntimeHelper("_AddRefArray", [Types.Pointer, Types.Pointer, Types.NativeInt], ReturnType: null, IsNoReturn: false)),
                    [data, info, Count()]));
                cleanup.Add(new LCallStatement(new LHelperCallee(new RuntimeHelper("_FinalizeArray", [Types.Pointer, Types.Pointer, Types.NativeInt], ReturnType: null, IsNoReturn: false)),
                    [data, info, Count()]));
            }

            var freeMem = new LCallStatement(new LHelperCallee(new RuntimeHelper("_FreeMem", [Types.Pointer], ReturnType: null, IsNoReturn: false)), [data]);
            cleanup.Add(new LIf(Compare(LCompareOperator.Greater, bytes, Constant(OpenArrayStackLimit, Types.Int64)), freeMem, @else: null));
        }

        return ([.. prologue], [.. cleanup]);
    }

    /// <summary><c>procedure _ReRaise</c>, noreturn (ABI-122).</summary>
    private static readonly RuntimeHelper ReRaise = new("_ReRaise", [], ReturnType: null, IsNoReturn: true);

    /// <summary>The label <c>Exit</c> leaves in a constructor/destructor body, so that the epilogue still runs (CMP-368/369).</summary>
    private LLoopLabel? _bodyLabel;

    /// <summary>
    /// CMP-368: <c>if Alloc &lt;&gt; 0 then Self := NewInstance(Self)</c> (slot 3 of the class reference); <c>try</c> body; <c>if Alloc
    /// &lt;&gt; 0 then Self.AfterConstruction</c> (slot 1) <c>except if Alloc &lt;&gt; 0 then Self.Destroy(3)</c> (slot 0); <c>raise end</c>;
    /// the emitter returns <c>Self</c>. The wrapper is always emitted: <c>AfterConstruction</c> is a call inside it (C-094).
    /// CMP-369: <c>if (Dealloc and 3) = 1 then Self.BeforeDestruction</c> (slot 2); body; <c>if (Dealloc and 1) &lt;&gt; 0 then Self.FreeInstance</c> (slot 4). The body is a
    /// one-pass loop so that <c>Exit</c> reaches the epilogue. Without the <c>TObject</c> slots (no System) only the body runs.
    /// </summary>
    private LBlock Protocol(MethodSymbol method, BoundBlock body)
    {
        _bodyLabel = NewLoop("body");
        var user = new LLoop(_bodyLabel, new LBlock([Lower(body), new LBreak(_bodyLabel)]), step: null);
        _bodyLabel = null;
        var slots = method.ContainingType.VirtualSlots;
        if (slots.Length < 5)
        {
            return new LBlock([user]);
        }

        var self = new LParamRef(method.SelfParameter!);
        var flag = new LParamRef(method.FlagParameter!);
        var zero = Constant(BigInteger.Zero, Types.Byte);
        LStatement Virtual(int slot) => new LCallStatement(new LRoutineCallee(slots[slot], Self: self, IsVirtual: true), []);
        if (method.MethodKind == MethodKind.Constructor)
        {
            LCompare Allocating() => Compare(LCompareOperator.NotEqual, flag, zero);
            var allocate = new LAssign(self, new LCall(new LRoutineCallee(slots[3], Self: self, IsVirtual: true), []));
            // C-094 / ABI-075 / ABI-076: an exception out of the body or AfterConstruction destroys the new instance through its
            // virtual Destroy with Dealloc = 3 (FreeInstance, no BeforeDestruction), then propagates
            var destroy = new LCallStatement(new LRoutineCallee(slots[0], Self: self, Flag: 3, IsVirtual: true), []);
            var handler = new LBlock([new LIf(Allocating(), destroy, @else: null), CallNoReturn(ReRaise)]);
            var guarded = new LTryExcept(new LBlock([user, new LIf(Allocating(), Virtual(1), @else: null)]), NewTemp(Types.Pointer), handler);
            return new LBlock([new LIf(Allocating(), allocate, @else: null), guarded]);
        }

        var outermost = Compare(LCompareOperator.Equal, new LBinary(LBinaryOperator.And, flag, Constant(3, Types.Byte)), Constant(BigInteger.One, Types.Byte));
        var freeing = Compare(LCompareOperator.NotEqual, new LBinary(LBinaryOperator.And, flag, Constant(BigInteger.One, Types.Byte)), zero);
        return new LBlock([new LIf(outermost, Virtual(2), @else: null), user, new LIf(freeing, Virtual(4), @else: null)]);
    }

    // ------------------------------------------------------------------ helpers for the rewriters

    /// <summary>A new compiler temporary <c>$tmpN</c> (storage).</summary>
    public LLocal NewTemp(TypeSymbol type)
    {
        var local = new LLocal("$tmp" + ++_tempCount, type, variable: null);
        _locals.Add(local);
        return local;
    }

    /// <summary>A new once-evaluated value <c>$vN</c> for <see cref="LTemp"/>.</summary>
    public LTempValue NewValue(TypeSymbol type) => new(type, ++_valueCount);

    public LLoopLabel NewLoop(string kind) => new(kind, ++_loopCount);

    /// <summary>Lowers a loop body with <paramref name="label"/> as the target of its <c>Break</c>/<c>Continue</c>.</summary>
    public LStatement LowerLoopBody(LLoopLabel label, BoundStatement body)
    {
        _loops.Push(label);
        try
        {
            return Lower(body);
        }
        finally
        {
            _loops.Pop();
        }
    }

    /// <summary>The type ordinal arithmetic is done in (LANG-222): integers per CMP-312, chars and Booleans in <c>Integer</c>.</summary>
    public TypeSymbol ComputationType(TypeSymbol ordinal) => ordinal switch
    {
        IntegerTypeSymbol integer => ConstantFolder.ComputationType(integer) switch
        {
            (32, true) => Types.Integer,
            (32, false) => Types.Cardinal,
            (_, true) => Types.Int64,
            _ => Types.UInt64,
        },
        CharTypeSymbol or BooleanTypeSymbol or EnumTypeSymbol => Types.Integer,
        _ => throw new InvalidOperationException($"'{ordinal.Name}' is not an ordinal type."),
    };

    public LExpression Convert(LExpression value, TypeSymbol type) => ReferenceEquals(value.Type, type) ? value : new LConvert(value, type);

    public LConst Constant(BigInteger value, TypeSymbol type) => new(new ConstantValue(value, type), type);

    /// <summary>
    /// CMP-373 row 1 (LANG-665, C-098): the address of the hidden interface field at <paramref name="offset"/> inside the object,
    /// <c>nil</c> for <c>nil</c> (the object is evaluated once).
    /// </summary>
    public LExpression ObjectToInterface(LExpression instance, long offset, TypeSymbol type)
    {
        var value = NewValue(instance.Type);
        var isNil = Compare(LCompareOperator.Equal, Convert(new LTempRef(value), Types.Pointer), new LConst(new ConstantValue(null, Types.Nil), Types.Pointer));
        return new LTemp(value, instance, new LSelect(isNil, new LConst(new ConstantValue(null, Types.Nil), type),
            new LPointerOffset(new LTempRef(value), Constant(offset, Types.Int64), Types.Byte, type)));
    }

    public LCompare Compare(LCompareOperator op, LExpression left, LExpression right) => new(op, left, right, Types.Boolean);

    /// <summary>
    /// <c>x ± 1</c> (or <paramref name="step"/>) in the computation type, converted back to <paramref name="type"/>; with
    /// <paramref name="checks"/> the operation and the conversion back are checked per <see cref="CheckRewriter"/>.
    /// </summary>
    public LExpression Step(LExpression value, TypeSymbol type, LBinaryOperator op, LExpression? step = null, DirectiveState? checks = null)
    {
        var computation = ComputationType(type);
        var amount = step is null ? Constant(BigInteger.One, computation) : Convert(step, computation);
        return CheckRewriter.Convert(this, CheckRewriter.Binary(op, Convert(value, computation), amount, checks), type, checks);
    }

    public LStatement CallNoReturn(RuntimeHelper helper, params LExpression[] arguments) =>
        new LBlock([new LCallStatement(new LHelperCallee(helper), [.. arguments]), new LUnreachable()]);

    // ------------------------------------------------------------------ statements

    public LStatement Lower(BoundStatement statement)
    {
        // C-213: a lowered statement (and a lowering failure) carries its source statement's location for E2900
        LStatement lowered;
        try
        {
            lowered = LowerStatement(statement);
        }
        catch (NotSupportedException e) when (e is not Emit.EmitNotSupportedException && EmitNotSupportedLocation.Of(statement.Syntax) is { } location)
        {
            throw new Emit.EmitNotSupportedException(e.Message, location);
        }

        lowered.SourceSyntax ??= statement.Syntax;
        return lowered;
    }

    private LStatement LowerStatement(BoundStatement statement)
    {
        switch (statement)
        {
            case BoundBlock block:
                return new LBlock([.. block.Statements.Select(Lower)]);
            case BoundAssignmentStatement { Target.Type.IsManaged: true } assignment:
                return ManagedRewriter.Assign(this, Lower(assignment.Target), assignment.Value);
            case BoundAssignmentStatement { Target.Type: StringTypeSymbol { Variant: StringVariant.Short } } assignment:
                return ManagedRewriter.AssignShort(this, Lower(assignment.Target), assignment.Value);        // LANG-246 (C-063)
            case BoundAssignmentStatement assignment:
                return new LAssign(Lower(assignment.Target), Lower(assignment.Value));
            case BoundIfStatement ifStatement:
                return new LIf(Lower(ifStatement.Condition), Lower(ifStatement.Then), ifStatement.Else is null ? null : Lower(ifStatement.Else));
            case BoundWhileStatement loop:
            {
                var label = NewLoop("while");
                var condition = Lower(loop.Condition);
                return new LLoop(label, new LIf(condition, LowerLoopBody(label, loop.Body), new LBreak(label)), step: null);
            }

            case BoundRepeatStatement loop:
            {
                var label = NewLoop("repeat");
                var body = LowerLoopBody(label, loop.Body);
                return new LLoop(label, body, new LIf(Lower(loop.Condition), new LBreak(label), @else: null));
            }

            case BoundForStatement loop:
                return ForRewriter.Rewrite(this, loop);
            case BoundCaseStatement caseStatement:
                return CaseRewriter.Rewrite(this, caseStatement);
            case BoundBreakStatement:
                return new LBreak(_loops.Peek());
            case BoundContinueStatement:
                return new LContinue(_loops.Peek());
            case BoundExitStatement { Value: null }:
                return _bodyLabel is { } bodyLabel ? new LBreak(bodyLabel) : new LReturn();       // CMP-368/369: through the epilogue
            case BoundExitStatement { Value.Type.IsManaged: true } exit:
                return new LBlock([ManagedRewriter.Assign(this, new LLocalRef(_variables[Routine!.ResultVariable!]), exit.Value!), new LReturn()]);
            case BoundExitStatement { Value.Type: StringTypeSymbol { Variant: StringVariant.Short } } exit:
                return new LBlock([ManagedRewriter.AssignShort(this, new LLocalRef(_variables[Routine!.ResultVariable!]), exit.Value!), new LReturn()]);
            case BoundExitStatement exit:
                return new LBlock([new LAssign(new LLocalRef(_variables[Routine!.ResultVariable!]), Lower(exit.Value)), new LReturn()]);
            case BoundHaltStatement halt:
                return CallNoReturn(Halt, halt.ExitCode is null ? Constant(BigInteger.Zero, Types.Integer) : Lower(halt.ExitCode));
            case BoundCallStatement call:
                return IOChecked(call.Syntax, call.Routine, CallStatement(call.Routine, call.Arguments, call.Target, call.Receiver));
            case BoundExpressionStatement { Expression: BoundCallExpression call } expression:
                return IOChecked(expression.Syntax, call.Routine, CallStatement(call.Routine, call.Arguments, call.Target, call.Receiver));
            case BoundExpressionStatement { Expression: BoundIntrinsicCallExpression { Intrinsic.IsAtomic: true } atomic }:
                return new LAssign(new LLocalRef(NewTemp(atomic.Type)), Lower(atomic));     // C-168: a discarded result
            case BoundIntrinsicCallStatement intrinsic:
                return IntrinsicRewriter.Rewrite(this, intrinsic);
            case BoundWriteStatement write:
                return WriteRewriter.Rewrite(this, write);
            case BoundRaiseStatement { IsReraise: true }:
                return CallNoReturn(ReRaise);                                                                         // LANG-807
            case BoundRaiseStatement raise:
                // CMP-395: _Raise(Obj, Addr), Addr = nil for 'raise E' (ABI-123)
                return CallNoReturn(new RuntimeHelper("_Raise", [Types.Pointer, Types.Pointer], ReturnType: null, IsNoReturn: true),
                    Lower(raise.Exception!), raise.Address is null ? new LConst(new ConstantValue(null, Types.Nil), Types.Nil) : Lower(raise.Address));
            case BoundTryExceptStatement tryExcept:
                return TryExcept(tryExcept);
            case BoundTryFinallyStatement tryFinally:
                return TryFinally(tryFinally);
            case BoundWithStatement with:
            {
                // LANG-375 (C-109): the designator once into its hidden variable, then the body
                var storage = HandlerVariable(with.Variable);
                var store = with.Variable.Type.IsManaged ? ManagedRewriter.Assign(this, storage, with.Value) : new LAssign(storage, Lower(with.Value));
                return new LBlock([store, Lower(with.Body)]);
            }

            case BoundLocalsStatement locals:
                foreach (var local in locals.Locals)
                {
                    HandlerVariable(local);                                     // C-111: declared before the body uses them
                }

                return Lower(locals.Body);

            default:
                throw new InvalidOperationException($"Cannot lower {statement.Kind}.");
        }
    }

    /// <summary>
    /// CMP-391: a catch-all whose handler tests the handlers' classes in source order (<c>_IsClass</c>); the first match stores
    /// the object in its variable and runs; no match runs the <c>else</c> part, or re-raises (LANG-821).
    /// </summary>
    private LTryExcept TryExcept(BoundTryExceptStatement statement)
    {
        var body = Lower(statement.Body);
        var exception = NewTemp(Types.Pointer);
        LStatement handler = statement.Else is { } otherwise
            ? Lower(otherwise)
            : CallNoReturn(ReRaise);
        foreach (var on in statement.Handlers.Reverse())
        {
            var isClass = new LCall(new LHelperCallee(new RuntimeHelper("_IsClass", [Types.Pointer, Types.Pointer], Types.Boolean, IsNoReturn: false)),
                [new LLocalRef(exception), new LClassRef(on.Type, Types.Pointer)]);
            var store = on.Variable is { } variable
                ? [new LAssign(HandlerVariable(variable), new LLocalRef(exception))]                // both are a ptr
                : Array.Empty<LStatement>();
            handler = new LIf(isClass, new LBlock([.. store, Lower(on.Body)]), handler);
        }

        return new LTryExcept(body, exception, handler);
    }

    /// <summary>
    /// The storage of a handler variable: a local of the routine (in its frame when a <c>$fin</c> uses it); in a unit-level body
    /// an internal global <c>&lt;U&gt;.&lt;body&gt;.&lt;Name&gt;.&lt;n&gt;</c>, since those bodies have no frame record.
    /// </summary>
    private LExpression HandlerVariable(VariableSymbol variable)
    {
        if (Routine is not null || BodyGlobals is null)
        {
            return new LLocalRef(DeclareLocal(variable));
        }

        BodyGlobals.Add((variable, new LinkName($"{Unit!.Name}.{BodyName}.{variable.Name}.{BodyGlobals.Count + 1}", AlwaysQuote: true)));
        if (variable.Type.IsManaged)
        {
            _managedBodyGlobals.Add(variable);                              // a hidden for…in / with local: released with the body (C-111)
        }

        return new LGlobalRef(variable);
    }

    /// <summary>
    /// CMP-392: the finally part becomes the routine <c>&lt;routine&gt;.$fin&lt;n&gt;</c> nested in this one (for a unit-level body
    /// <c>&lt;U&gt;.&lt;body&gt;.$fin&lt;n&gt;</c> of the unit), lowered with its own context: the variables it uses are outer references,
    /// so they live in this routine's frame record (<see cref="OuterReferences"/>).
    /// </summary>
    private LTryFinally TryFinally(BoundTryFinallyStatement statement)
    {
        var number = ++_finCount;
        var fin = Routine is null
            ? new RoutineSymbol(BodyName + ".$fin" + number, Unit!, [], declaration: null!)
            : new RoutineSymbol("$fin" + number, Routine, [], declaration: null!);
        var body = Lower(statement.Body);
        var context = new LoweringContext(Types, fin, locals: [])
        {
            Unit = Unit,
            BodyGlobals = BodyGlobals,
            Output = Output,
            Input = Input,
            CheckedSystem = CheckedSystem,
        };
        var lowered = context.Finish(statement.Finally);
        Outlined.Add(fin.IsNested && context.HasOutlined ? lowered with { Frame = new LoweredFrame([]) } : lowered);
        Outlined.AddRange(context.Outlined);
        foreach (var variable in context.OuterReferences)
        {
            if (!ReferenceEquals(variable.ContainingSymbol, Routine))
            {
                NoteOuter(variable);                                    // a variable of a routine further out
            }
            else
            {
                _finCaptured.Add(variable);
            }
        }

        return new LTryFinally(body, fin);
    }

    // ------------------------------------------------------------------ expressions

    public LExpression Lower(BoundExpression expression)
    {
        if (expression.HasErrors)
        {
            throw new InvalidOperationException("Cannot lower an erroneous expression.");
        }

        if (expression.ConstantValue is not null && expression.Type is MethodPointerTypeSymbol
            || expression.ConstantValue?.Value is ImmutableArray<ConstantValue?> { IsEmpty: true } && expression.Type.IsManaged)
        {
            // C-116: Default(T) of a managed record or array is a zeroed temporary too (it holds no references to release)
            var empty = new LLocalRef(NewTemp(expression.Type));                    // nil method pointer: a zeroed temporary (C-103)
            return new LSequence(new LZero(new LAddrOf(empty, Types.Pointer), expression.Type), empty);
        }

        if (expression.ConstantValue is { } constant)
        {
            return constant.Value is string text ? new LStringLiteral(text, expression.Type) : new LConst(constant, expression.Type);
        }

        switch (expression)
        {
            case BoundVariableExpression { Variable: { IsThreadVar: true } threadVar }:
                return new LDeref(new LThreadVarAddress(threadVar, Types.Pointer), threadVar.Type);    // CMP-414
            case BoundVariableExpression { Variable: { IsGlobal: true } global }:
                return new LGlobalRef(global);
            case BoundVariableExpression variable when BodyGlobals?.Exists(g => ReferenceEquals(g.Variable, variable.Variable)) == true:
                return new LGlobalRef(variable.Variable);                       // a unit-level handler variable (C-091)
            case BoundVariableExpression variable when ClosureAccess(variable.Variable) is { } field:
                return field;                                                   // CMP-380: a variable in a closure (C-120)
            case BoundVariableExpression variable:
                return _variables.TryGetValue(variable.Variable, out var local) ? new LLocalRef(local)
                    : variable.Variable.ContainingSymbol is not RoutineSymbol ? new LGlobalRef(variable.Variable)  // a unit-level body's local, from an anonymous method
                    : Outer(variable.Variable, variable.Variable.Type);
            case BoundParameterExpression parameter when ClosureAccess(parameter.Parameter) is { } field:
                return field;
            case BoundParameterExpression parameter:
                return ReferenceEquals(parameter.Parameter.ContainingSymbol, Routine)
                    ? new LParamRef(parameter.Parameter)
                    : Outer(parameter.Parameter, parameter.Parameter.Type);
            case BoundConversionExpression { Type: PointerTypeSymbol, Operand.Type: MethodPointerTypeSymbol } conversion:
                return new LField(new LView(Lower(conversion.Operand), MethodParts), MethodParts.Fields[0]);      // Assigned / = nil: the Code
            case BoundConversionExpression { Type: MethodPointerTypeSymbol, Operand.Type: MethodPointerTypeSymbol } conversion:
                return new LView(Lower(conversion.Operand), conversion.Type);           // LANG-483: the same { Code, Data } (C-103)
            case BoundConversionExpression { Type: StringTypeSymbol } conversion when !ReferenceEquals(conversion.Type, conversion.Operand.Type):
                return ManagedRewriter.Value(this, conversion);                         // LANG-245 (CMP-324, C-063)
            case BoundConversionExpression { IsExplicit: true, Type: PointerTypeSymbol { PointedType: not null } pointer, Operand.Type: StringTypeSymbol } conversion:
                return StringConversions.DataPointer(this, Lower(conversion.Operand), pointer);    // LANG-290: PChar(s)
            case BoundBinaryExpression { OperatorKind: BoundBinaryOperatorKind.Add, Type: StringTypeSymbol } concatenation:
                return ManagedRewriter.Value(this, concatenation);                      // LANG-243 (CMP-324)
            case BoundArrayConstructor constructor:
                return ManagedRewriter.Value(this, constructor);                        // LANG-259: a new array in a temporary
            case BoundIndexExpression { IsStringElement: true } element:
                return StringElement(element);
            case BoundIndexExpression { Array.Type: OpenArrayTypeSymbol } element:
                // LANG-431 / CMP-331 (C-106): element i of the data, checked unsigned against High + 1 under {$R+}
                return new LArrayElement(Lower(element.Array), Convert(Lower(element.Index), Types.Int64), CheckRewriter.StateOf(element).RangeChecks, element.Type,
                    new LBinary(LBinaryOperator.Add, OpenArrayHigh(element.Array), Constant(BigInteger.One, Types.Int64)));
            case BoundIndexExpression { Array.Type: DynamicArrayTypeSymbol } element:
                // ABI-042 / CMP-330/331: element i of the block (no copy-on-write), checked unsigned against the length under {$R+}
                return new LArrayElement(Lower(element.Array), Convert(Lower(element.Index), Types.Int64), CheckRewriter.StateOf(element).RangeChecks, element.Type);
            case BoundConversionExpression { Type: SetTypeSymbol target, Operand.Type: SetTypeSymbol } conversion:
                return SetRewriter.Convert(this, Lower(conversion.Operand), target);
            case BoundConversionExpression { IsExplicit: true, Type: PointerTypeSymbol, Operand.Type: { IsOrdinal: true, Size: < 8 } source } conversion:
                // LANG-290: an ordinal is extended to 64 bits by its own signedness before it becomes a pointer
                return Convert(Convert(Lower(conversion.Operand), source is IntegerTypeSymbol { IsSigned: true } ? Types.Int64 : Types.UInt64), conversion.Type);
            case BoundConversionExpression { Type: InterfaceTypeSymbol target, Operand.Type: ClassTypeSymbol source } conversion:
                return ObjectToInterface(Lower(conversion.Operand), source.FindInterface(target)!.Offset, target);
            case BoundConversionExpression conversion:
                return conversion.IsExplicit
                    ? Convert(Lower(conversion.Operand), conversion.Type)
                    : CheckRewriter.Convert(this, Lower(conversion.Operand), conversion.Type, CheckRewriter.StateOf(conversion));
            case BoundVariableTypecastExpression typecast:
                return new LView(Lower(typecast.Operand), typecast.Type);              // LANG-290: the variable's storage as another type
            case BoundUnaryExpression { OperatorKind: BoundUnaryOperatorKind.Identity } unary:
                return Lower(unary.Operand);
            case BoundUnaryExpression { OperatorKind: BoundUnaryOperatorKind.Negate } unary:
                return CheckRewriter.Negate(Lower(unary.Operand), CheckRewriter.StateOf(unary));
            case BoundUnaryExpression unary:
                return new LUnary(LUnaryOperator.Not, Lower(unary.Operand));
            case BoundBinaryExpression binary:
                return Binary(binary);
            case BoundCallExpression call:
                return Call(call.Routine, call.Arguments, call.Target, call.Receiver);
            case BoundIntrinsicCallExpression { Intrinsic.Intrinsic: IntrinsicKind.Copy } copy:
                return ManagedRewriter.Value(this, copy);                               // C-062: a string value in a temporary
            case BoundIntrinsicCallExpression intrinsic:
                return IntrinsicRewriter.Rewrite(this, intrinsic);
            case BoundSetConstructor constructor:
                return SetRewriter.Constructor(this, constructor);
            case BoundIndexExpression index:
                return Index(index);
            case BoundFieldAccessExpression field:
                return field.Record.Type is ClassTypeSymbol
                    ? new LDeref(new LPointerOffset(Lower(field.Record), Constant(field.Field.Offset, Types.Int64), Types.Byte, Types.Pointer), field.Type)   // CMP-367
                    : new LField(Lower(field.Record), field.Field);                     // CMP-307
            case BoundClassReferenceExpression reference:
                return new LClassRef(reference.Class, reference.Type);                  // CMP-367 TFoo.Create
            case BoundMethodPointerExpression methodPointer:
                return MethodPointer(methodPointer);
            case BoundTypeTestExpression { Interface: not null, IsAs: true } test:
                return ManagedRewriter.Value(this, test);                               // CMP-373: written into a managed temporary
            case BoundTypeTestExpression { Interface: { } intf } test:
                return new LCall(new LHelperCallee(new RuntimeHelper("_ObjSupports", [Types.Pointer, Types.Pointer], Types.Boolean, IsNoReturn: false)),
                    [Lower(test.Operand), new LGuidRef(intf, Types.Pointer)]);          // CMP-373 o is I
            case BoundTypeTestExpression test:
            {
                // CMP-367: _IsClass(o, VMT) / _AsClass(o, VMT); for a class reference _ClassInherits(c, VMT) (ABI §9.6)
                var helper = test.IsAs ? "_AsClass" : test.Operand.Type is ClassRefTypeSymbol ? "_ClassInherits" : "_IsClass";
                return new LCall(new LHelperCallee(new RuntimeHelper(helper, [Types.Pointer, Types.Pointer], test.Type, IsNoReturn: false)),
                    [Lower(test.Operand), new LClassRef(test.Class, Types.Pointer)]);
            }

            case BoundAddressOfExpression { Routine: { } routine } addressOf:
                return new LRoutineAddress(routine, addressOf.Type);                    // CMP-340
            case BoundAddressOfExpression addressOf:
                return new LAddrOf(Lower(addressOf.Operand!), addressOf.Type);
            case BoundDereferenceExpression dereference:
                return new LDeref(Lower(dereference.Pointer), dereference.Type);
            case BoundPointerArithmeticExpression arithmetic:
                return PointerArithmetic(arithmetic);
            default:
                throw new InvalidOperationException($"Cannot lower {expression.Kind}.");
        }
    }

    /// <summary>
    /// CMP-308: element <c>index − Low</c> in <c>Int64</c> (sign- or zero-extended by the index type; folded for a constant). Under
    /// <c>{$R+}</c> the index is checked against the declared bounds in its computation type when its type can lie outside
    /// (CMP-331, CMP-314); constant indexes were checked by the binder (E2032).
    /// </summary>
    private LExpression Index(BoundIndexExpression index)
    {
        var array = index.ArrayType;
        var target = Lower(index.Array);
        var value = Lower(index.Index);
        if (value is LConst { Value.Value: var constant })
        {
            var ordinal = constant is bool b ? (b ? BigInteger.One : BigInteger.Zero) : (BigInteger)constant!;
            return new LIndex(target, Constant(ordinal - array.Low, Types.Int64), array.ElementType);
        }

        if (CheckRewriter.StateOf(index).RangeChecks && Ordinals.Bounds(value.Type) is { } source && (source.Low < array.Low || source.High > array.High))
        {
            var computation = ComputationType(value.Type);
            var bounds = Ordinals.Bounds(computation)!.Value;
            value = new LRangeCheck(Convert(value, computation), BigInteger.Max(array.Low, bounds.Low), BigInteger.Min(array.High, bounds.High));
        }

        var wide = Convert(value, Types.Int64);
        return new LIndex(target, array.Low.IsZero ? wide : new LBinary(LBinaryOperator.Subtract, wide, Constant(array.Low, Types.Int64)), array.ElementType);
    }

    /// <summary>
    /// ABI-024: character <c>i − 1</c> (in <c>Int64</c>) of the string; under <c>{$R+}</c> checked against the length (CMP-331).
    /// A constant index is folded.
    /// </summary>
    private LExpression StringElement(BoundIndexExpression element)
    {
        var target = Lower(element.Array);
        var index = Lower(element.Index);
        if (element.Array.Type is StringTypeSymbol { Variant: StringVariant.Short } shortString)
        {
            // C-063: byte i of the storage (0 = the length byte); under {$R+} checked against 0..capacity like a static array
            var bytes = StringConversions.ShortBytes(this, target, shortString);
            if (index is LConst { Value.Value: BigInteger position })
            {
                return new LIndex(bytes, Constant(position, Types.Int64), Types.AnsiChar);
            }

            if (CheckRewriter.StateOf(element).RangeChecks)
            {
                var computation = ComputationType(index.Type);
                index = new LRangeCheck(Convert(index, computation), BigInteger.Zero, shortString.MaxLength);
            }

            return new LIndex(bytes, Convert(index, Types.Int64), Types.AnsiChar);
        }

        LExpression zeroBased = index is LConst { Value.Value: BigInteger constant }
            ? Constant(constant - 1, Types.Int64)
            : new LBinary(LBinaryOperator.Subtract, Convert(index, Types.Int64), Constant(BigInteger.One, Types.Int64));
        return new LStringIndex(target, zeroBased, CheckRewriter.StateOf(element).RangeChecks, element.Type);
    }

    /// <summary>
    /// CMP-342: <c>p ± n</c> = GEP over the element type (bytes for the untyped <c>Pointer</c>); <c>p - q</c> = the byte distance
    /// (<c>ptrtoint</c>, <c>sub</c>) divided by the element size.
    /// </summary>
    private LExpression PointerArithmetic(BoundPointerArithmeticExpression arithmetic)
    {
        var pointer = Lower(arithmetic.Pointer);
        var offset = Lower(arithmetic.Offset);
        var size = Pointers.ElementSize(arithmetic.PointerType);
        if (arithmetic.Difference)
        {
            var bytes = new LBinary(LBinaryOperator.Subtract, Convert(pointer, Types.NativeInt), Convert(offset, Types.NativeInt));
            return size == 1 ? bytes : new LBinary(LBinaryOperator.IntegerDivide, bytes, Constant(size, Types.NativeInt));
        }

        return Offset(pointer, arithmetic.Subtract ? Negated(offset) : offset, arithmetic.PointerType, arithmetic.Type);
    }

    /// <summary><c>-x</c> without overflow check; a constant is negated at compile time.</summary>
    public LExpression Negated(LExpression value) =>
        value is LConst { Value.Value: BigInteger constant } ? Constant(-constant, value.Type) : new LUnary(LUnaryOperator.Negate, value);

    /// <summary><paramref name="pointer"/> moved by <paramref name="index"/> elements of <paramref name="pointerType"/> (CMP-342).</summary>
    public LExpression Offset(LExpression pointer, LExpression index, PointerTypeSymbol pointerType, TypeSymbol resultType) =>
        new LPointerOffset(pointer, index, pointerType.PointedType ?? Types.Byte, resultType);

    private LExpression Binary(BoundBinaryExpression binary)
    {
        if (binary.OperandType is SetTypeSymbol)
        {
            return SetRewriter.Binary(this, binary);
        }

        if (binary.OperandType is StringTypeSymbol)
        {
            return ManagedRewriter.Compare(this, binary);                               // LANG-244 (CMP-324)
        }

        if (binary.OperandType is MethodPointerTypeSymbol)
        {
            return MethodPointerCompare(binary);
        }

        var left = Lower(binary.Left);
        var right = Lower(binary.Right);
        LCompareOperator? comparison = binary.OperatorKind switch
        {
            BoundBinaryOperatorKind.Equal => LCompareOperator.Equal,
            BoundBinaryOperatorKind.NotEqual => LCompareOperator.NotEqual,
            BoundBinaryOperatorKind.Less => LCompareOperator.Less,
            BoundBinaryOperatorKind.LessOrEqual => LCompareOperator.LessOrEqual,
            BoundBinaryOperatorKind.Greater => LCompareOperator.Greater,
            BoundBinaryOperatorKind.GreaterOrEqual => LCompareOperator.GreaterOrEqual,
            _ => null,
        };
        if (comparison is { } op)
        {
            return Compare(op, left, right);
        }

        return CheckRewriter.Binary(binary.OperatorKind switch
        {
            BoundBinaryOperatorKind.Add => LBinaryOperator.Add,
            BoundBinaryOperatorKind.Subtract => LBinaryOperator.Subtract,
            BoundBinaryOperatorKind.Multiply => LBinaryOperator.Multiply,
            BoundBinaryOperatorKind.Divide => LBinaryOperator.Divide,
            BoundBinaryOperatorKind.IntegerDivide => LBinaryOperator.IntegerDivide,
            BoundBinaryOperatorKind.Modulo => LBinaryOperator.Modulo,
            BoundBinaryOperatorKind.And => LBinaryOperator.And,
            BoundBinaryOperatorKind.Or => LBinaryOperator.Or,
            BoundBinaryOperatorKind.Xor => LBinaryOperator.Xor,
            BoundBinaryOperatorKind.ConditionalAnd => LBinaryOperator.ConditionalAnd,
            BoundBinaryOperatorKind.ConditionalOr => LBinaryOperator.ConditionalOr,
            BoundBinaryOperatorKind.Shl => LBinaryOperator.Shl,
            BoundBinaryOperatorKind.Shr => LBinaryOperator.Shr,
            _ => throw new InvalidOperationException($"Unknown operator {binary.OperatorKind}."),
        }, left, right, CheckRewriter.StateOf(binary));
    }

    /// <summary>
    /// Value and <c>const</c> arguments as values; <c>var</c>/<c>out</c> arguments as the variable's address. ABI-080: an aggregate
    /// of size 1, 2, 4 or 8 stays an aggregate operand (the emitter loads it as <c>iN</c>); a larger value aggregate is copied
    /// into a fresh temporary whose address is passed, a larger <c>const</c> one passes the original's address (a copy when
    /// the original may be less aligned than its type, CMP-309). A <c>var</c>/<c>out</c> argument less aligned than its type (a
    /// field of a packed record) is passed as an aligned temporary copied in before and back after the call
    /// (<paramref name="writeBacks"/>), since the callee accesses it with the type's alignment (CMP-309).
    /// </summary>
    private ImmutableArray<LExpression> Arguments(RoutineSymbol routine, ImmutableArray<BoundExpression> arguments, List<LStatement> writeBacks) =>
        [.. arguments.Select((argument, i) => routine.Parameters[i].Type is OpenArrayTypeSymbol open
            ? OpenArrayArgument(argument, open)
            : Argument(routine.Parameters[i], Lower(argument), writeBacks))];

    /// <summary>
    /// LANG-433 / rtl-abi.md §9.10 (C-107): <c>[e1, …]</c> for an <c>array of const</c> — a zeroed temporary <c>array[0..n-1] of
    /// TVarRec</c>; element k gets its <c>VType</c> byte at offset 0 and its payload at offset 8: the value (integers as
    /// <c>Integer</c>, Booleans normalised, characters, pointers, string and object references, not counted), or for reals and
    /// 64-bit integers the address of a <c>Double</c>/<c>Int64</c> temporary, for a <c>ShortString</c> its address.
    /// </summary>
    private LOpenArray VarRecArgument(BoundArrayConstructor constructor, OpenArrayTypeSymbol type)
    {
        var n = constructor.Elements.Length;
        var index = new IntegerTypeSymbol($"0..{n - 1}", (IntegerTypeSymbol)Types.Integer, BigInteger.Zero, n - 1, containingSymbol: null, []);
        var built = new LLocalRef(NewTemp(new ArrayTypeSymbol($"array[0..{n - 1}] of {type.ElementType.Name}", index, type.ElementType, containingSymbol: null, [])));
        var statements = new List<LStatement> { new LZero(new LAddrOf(built, Types.Pointer), built.Type) };
        for (var k = 0; k < n; k++)
        {
            var value = constructor.Elements[k];
            var vType = VarRecs.VTypeOf(value.Type, value.ConstantValue)!.Value;
            var element = new LAddrOf(new LIndex(built, Constant(k, Types.Int64), type.ElementType), Types.Pointer);
            LExpression Slot(TypeSymbol slotType) => new LDeref(new LPointerOffset(element, Constant(8, Types.Int64), Types.Byte, Types.Pointer), slotType);
            statements.Add(new LAssign(new LDeref(element, Types.Byte), Constant(vType, Types.Byte)));
            switch (vType)
            {
                case VarRecs.VInteger:
                    statements.Add(new LAssign(Slot(Types.Integer), Convert(Lower(value), Types.Integer)));
                    break;
                case VarRecs.VBoolean:
                    statements.Add(new LAssign(Slot(Types.Boolean), Convert(Lower(value), Types.Boolean)));
                    break;
                case VarRecs.VChar or VarRecs.VWideChar:
                    statements.Add(new LAssign(Slot(value.Type), Lower(value)));
                    break;
                case VarRecs.VExtended or VarRecs.VInt64:
                {
                    var holder = new LLocalRef(NewTemp(vType == VarRecs.VExtended ? Types.Double : Types.Int64));
                    statements.Add(new LAssign(holder, Convert(Lower(value), holder.Type)));
                    statements.Add(new LAssign(Slot(Types.Pointer), new LAddrOf(holder, Types.Pointer)));
                    break;
                }

                case VarRecs.VString:
                    statements.Add(new LAssign(Slot(Types.Pointer), new LAddrOf(Lower(value), Types.Pointer)));
                    break;
                default:                                                            // references and pointers (ptr)
                    statements.Add(new LAssign(Slot(Types.Pointer), Lower(value)));
                    break;
            }
        }

        return new LOpenArray(new LSequence(new LBlock([.. statements]), new LAddrOf(built, Types.Pointer)), Constant(n - 1, Types.Int64), type);
    }

    /// <summary>The <c>Int64</c> High companion of an open array parameter expression (C-106; outer when a nested routine reads it).</summary>
    public LExpression OpenArrayHigh(BoundExpression openArray) => openArray is BoundParameterExpression { Parameter.HighParameter: { } high } parameter
        ? Lower(new BoundParameterExpression(parameter.Syntax, high))
        : throw new InvalidOperationException($"{openArray.Kind} is no open array parameter.");

    /// <summary>
    /// ABI-080 / LANG-430 (C-106): the data pointer and High of an open array argument — a static array's address and its constant
    /// High, a dynamic array's reference (High from its length), an open array parameter passed on, <c>Slice(a, n)</c> (a's data,
    /// High n − 1), or a constructor built into a temporary static array (managed elements counted, released at function exit).
    /// </summary>
    private LOpenArray OpenArrayArgument(BoundExpression argument, OpenArrayTypeSymbol type)
    {
        while (argument is BoundConversionExpression { Type: OpenArrayTypeSymbol } conversion)
        {
            argument = conversion.Operand;
        }

        switch (argument)
        {
            case BoundIntrinsicCallExpression { Intrinsic.Intrinsic: IntrinsicKind.Slice, Arguments: [var array, var count] }:
            {
                var high = new LBinary(LBinaryOperator.Subtract, Convert(Lower(count), Types.Int64), Constant(BigInteger.One, Types.Int64));
                return new LOpenArray(OpenArrayArgument(array, type).Data, high, type);
            }

            case BoundArrayConstructor { Elements.IsEmpty: true }:
                return new LOpenArray(new LConst(new ConstantValue(null, Types.Pointer), Types.Pointer), Constant(BigInteger.MinusOne, Types.Int64), type);
            case BoundArrayConstructor { Type: OpenArrayTypeSymbol { IsArrayOfConst: true } } constructor:
                return VarRecArgument(constructor, type);
            case BoundArrayConstructor constructor:
            {
                var elementType = type.ElementType;
                var n = constructor.Elements.Length;
                var index = new IntegerTypeSymbol($"0..{n - 1}", (IntegerTypeSymbol)Types.Integer, BigInteger.Zero, n - 1, containingSymbol: null, []);
                var built = new LLocalRef(NewTemp(new ArrayTypeSymbol($"array[0..{n - 1}] of {elementType.Name}", index, elementType, containingSymbol: null, [])));
                var stores = constructor.Elements.Select((value, k) =>
                {
                    var element = new LIndex(built, Constant(k, Types.Int64), elementType);
                    return elementType.IsManaged ? ManagedRewriter.Assign(this, element, value)
                        : Strings.IsShort(elementType) ? ManagedRewriter.AssignShort(this, element, value)
                        : new LAssign(element, Lower(value));
                });
                return new LOpenArray(new LSequence(new LBlock([.. stores]), new LAddrOf(built, Types.Pointer)), Constant(n - 1, Types.Int64), type);
            }

            case { Type: OpenArrayTypeSymbol }:
                return new LOpenArray(Lower(argument), OpenArrayHigh(argument), type);
            case { Type: ArrayTypeSymbol array }:
                return new LOpenArray(new LAddrOf(Lower(argument), Types.Pointer), Constant(array.Length - 1, Types.Int64), type);
            default:
                return new LOpenArray(Lower(argument), high: null, type);          // a dynamic array
        }
    }

    private LExpression Argument(ParameterSymbol parameter, LExpression argument, List<LStatement> writeBacks)
    {
        var aligned = FunctionEmitter.AccessAlignment(argument) >= TypeLayout.Of(parameter.Type).Alignment;
        if (parameter.IsByReference)
        {
            if (aligned)
            {
                return new LAddrOf(argument, Types.Pointer);
            }

            var temporary = new LLocalRef(NewTemp(parameter.Type));
            writeBacks.Add(Copy(argument, temporary));
            return new LSequence(Copy(temporary, argument), new LAddrOf(temporary, Types.Pointer));
        }

        if (!IrEmitter.IsPassedByAddress(parameter))
        {
            return argument;
        }

        if (parameter.Mode == ParameterMode.Const && aligned)
        {
            return new LAddrOf(argument, Types.Pointer);
        }

        // ABI-080 / CMP-325 rule 6: the caller's copy of a managed record or array holds its own references (finalised with the
        // caller's temporaries)
        var copy = new LLocalRef(NewTemp(parameter.Type));
        return new LSequence(Copy(copy, argument), new LAddrOf(copy, Types.Pointer));
    }

    /// <summary><c>target := value</c> between storage of one type: a plain copy, or the managed assignment helper.</summary>
    private LStatement Copy(LExpression target, LExpression value) =>
        target.Type.IsManaged ? ManagedRewriter.Assign(this, target, value) : new LAssign(target, value);

    /// <summary>
    /// A call with a result. ABI-081: an aggregate result is assigned to a fresh temporary (the emitter passes its address as
    /// the hidden <c>Result</c> or stores the returned <c>iN</c>), which is the value; so is any result when arguments are
    /// written back after the call.
    /// </summary>
    /// <summary>The routine, or a call through the procedural value <paramref name="target"/> (CMP-378).</summary>
    private LRoutineCallee Callee(RoutineSymbol routine, BoundExpression? target, BoundReceiver? receiver)
    {
        if (target is { Type: InterfaceTypeSymbol { IsMethodReference: true } })
        {
            return new(routine, null, Lower(target), null, true);              // ABI-097: an interface call of Invoke (IMT slot 3)
        }

        if (target is { Type: MethodPointerTypeSymbol })
        {
            // CMP-378 (C-103): call Code(Data, args) — the method pointer is evaluated once (Code is read first)
            var value = Lower(target);
            LExpression first = value, second = value;
            if (value is not (LLocalRef or LGlobalRef or LParamRef or LOuterRef or LDeref or LField or LIndex or LArrayElement))
            {
                var copy = new LLocalRef(NewTemp(target.Type));
                (first, second) = (new LSequence(new LAssign(copy, value), copy), copy);
            }

            return new LRoutineCallee(routine, new LField(new LView(first, MethodParts), MethodParts.Fields[0]),
                new LField(new LView(second, MethodParts), MethodParts.Fields[1]));
        }

        // ABI-084 (C-122): a record method takes the address of its receiver (a temporary for a non-variable, LANG-751)
        var self = receiver?.Self is { } selfValue
            ? routine is MethodSymbol { SelfParameter: { IsByReference: true } selfParameter } ? SelfAddress(Lower(selfValue), selfParameter.Type) : Lower(selfValue)
            : null;
        return new(routine, target is null ? null : Lower(target), self, receiver?.Flag, receiver?.IsVirtual ?? false);
    }

    /// <summary>The address of a record method's or record helper's receiver: the variable itself, else a temporary copy (LANG-751).</summary>
    private LExpression SelfAddress(LExpression value, TypeSymbol type)
    {
        if (value is LLocalRef or LGlobalRef or LParamRef or LOuterRef or LDeref or LField or LIndex or LArrayElement or LView or LSequence)
        {
            return new LAddrOf(value, Types.Pointer);
        }

        var temporary = new LLocalRef(NewTemp(type));
        return new LSequence(Copy(temporary, value), new LAddrOf(temporary, Types.Pointer));
    }

    /// <summary>
    /// C-207 (LANG-481): <c>a = b</c> between method pointers is <c>(a.Code = b.Code) and (a.Data = b.Data)</c>, <c>&lt;&gt;</c> its
    /// negation; each operand is evaluated once, left first (a temporary unless it is a plain location).
    /// </summary>
    private LExpression MethodPointerCompare(BoundBinaryExpression binary)
    {
        (LExpression First, LExpression Second) Once(BoundExpression operand)
        {
            var value = Lower(operand);
            if (value is LLocalRef or LGlobalRef or LParamRef or LOuterRef or LDeref or LField or LIndex or LArrayElement)
            {
                return (value, value);
            }

            var copy = new LLocalRef(NewTemp(operand.Type));
            return (new LSequence(new LAssign(copy, value), copy), copy);
        }

        LExpression Part(LExpression value, int index) => new LField(new LView(value, MethodParts), MethodParts.Fields[index]);
        var (leftFirst, leftSecond) = Once(binary.Left);
        var (rightFirst, rightSecond) = Once(binary.Right);
        var equal = binary.OperatorKind == BoundBinaryOperatorKind.Equal;
        var op = equal ? LCompareOperator.Equal : LCompareOperator.NotEqual;
        return new LBinary(equal ? LBinaryOperator.And : LBinaryOperator.Or,
            Compare(op, Part(leftFirst, 0), Part(rightFirst, 0)), Compare(op, Part(leftSecond, 1), Part(rightSecond, 1)));
    }

    private RecordTypeSymbol? _methodParts;

    /// <summary>A method pointer's storage as <c>record Code, Data: Pointer end</c> (C-103).</summary>
    public RecordTypeSymbol MethodParts => _methodParts ??= MethodPointerTypeSymbol.Parts(Types.Pointer);

    /// <summary>
    /// CMP-378 (C-103): a method pointer value in a temporary — Data := Self, then Code := the method's address, or the VMT slot
    /// read through Data (the instance's VMT, or the class reference itself for a class method).
    /// </summary>
    private LExpression MethodPointer(BoundMethodPointerExpression value)
    {
        var temporary = new LLocalRef(NewTemp(value.Type));
        var data = new LField(new LView(temporary, MethodParts), MethodParts.Fields[1]);
        var code = new LField(new LView(temporary, MethodParts), MethodParts.Fields[0]);
        LExpression address = new LRoutineAddress(value.Method, Types.Pointer);
        if (value.Method.ContainingInterface is not null)
        {
            // C-207: an interface method — Code is the IMT slot (ABI-093: slot k at offset 8k of the IMT the reference points to)
            address = new LDeref(new LPointerOffset(new LDeref(data, Types.Pointer), Constant(8 * value.Method.VmtSlot, Types.Int64), Types.Byte, Types.Pointer), Types.Pointer);
        }
        else if (value.IsVirtual)
        {
            var vmt = value.Method.IsClassMethod ? (LExpression)data : new LDeref(data, Types.Pointer);
            address = new LDeref(new LPointerOffset(vmt, Constant(64 + 8 * value.Method.VmtSlot, Types.Int64), Types.Byte, Types.Pointer), Types.Pointer);
        }

        return new LSequence(new LBlock([new LAssign(data, Convert(Lower(value.Self), Types.Pointer)), new LAssign(code, address)]), temporary);
    }

    private LExpression Call(RoutineSymbol routine, ImmutableArray<BoundExpression> arguments, BoundExpression? target = null, BoundReceiver? receiver = null)
    {
        var writeBacks = new List<LStatement>();
        var call = new LCall(Callee(routine, target, receiver), Arguments(routine, arguments, writeBacks));
        if (!Lowerer.IsAggregate(call.Type) && !call.Type.IsManaged && writeBacks.Count == 0)
        {
            return call;
        }

        // a managed result goes to a managed temporary (CMP-325 rule 4/5), zeroed in the entry block and finalised at exit
        var result = new LLocalRef(NewTemp(call.Type));
        return new LSequence(new LBlock([new LAssign(result, call), .. writeBacks]), result);
    }

    /// <summary>
    /// CMP-325 rule 4: <c>target := F(args)</c> for a managed result. The target's address is the hidden <c>Result</c> only when
    /// <see cref="ReceivesResultDirectly"/>; otherwise a temporary receives it and is assigned with <c>_UStrAsg</c>.
    /// </summary>
    public LStatement CallInto(LExpression target, RoutineSymbol routine, ImmutableArray<BoundExpression> arguments, BoundExpression? procedural = null,
        BoundReceiver? receiver = null)
    {
        var writeBacks = new List<LStatement>();
        var call = new LCall(Callee(routine, procedural, receiver), Arguments(routine, arguments, writeBacks));
        if (ReceivesResultDirectly(target, call))
        {
            return new LBlock([new LAssign(target, call), .. writeBacks]);
        }

        var result = new LLocalRef(NewTemp(call.Type));
        return new LBlock([new LAssign(result, call), .. writeBacks, ManagedRewriter.Assign(this, target, result)]);
    }

    /// <summary>
    /// CMP-325 rule 4: a declared local of this body (not <c>Result</c>, which is the caller's storage) that no nested routine
    /// captures, whose address is never taken (the callee could reach it through a pointer, C-062) and that the call's
    /// arguments do not mention.
    /// </summary>
    private bool ReceivesResultDirectly(LExpression target, LCall call) =>
        target is LLocalRef { Local: { Variable: { } variable } local }
        && !ReferenceEquals(variable, Routine?.ResultVariable)
        && !Captured.Contains(variable)
        && !AddressTaken.Contains(variable)
        && !ManagedRewriter.References(call, local);

    /// <summary>ABI-132: <paramref name="call"/> then <c>_IOCheck</c> when the callee is a System text-file routine under <c>{$I+}</c>.</summary>
    private LStatement IOChecked(SyntaxNode syntax, RoutineSymbol routine, LStatement call) =>
        CheckedSystem is not null
        && ReferenceEquals(routine.ContainingSymbol, CheckedSystem)
        && routine.Parameters is [{ Mode: ParameterMode.Var, Type: TextTypeSymbol }, ..]
        && syntax.FirstToken().DirectiveState.IoChecks
            ? new LBlock([call, new LCallStatement(new LHelperCallee(new RuntimeHelper("_IOCheck", [], ReturnType: null, IsNoReturn: false)), [])])
            : call;

    /// <summary>A call statement; a discarded aggregate or managed result still needs storage (ABI-081).</summary>
    private LStatement CallStatement(RoutineSymbol routine, ImmutableArray<BoundExpression> arguments, BoundExpression? target, BoundReceiver? receiver = null)
    {
        if (routine.ReturnType is { } type && (Lowerer.IsAggregate(type) || type.IsManaged))
        {
            return ((LSequence)Call(routine, arguments, target, receiver)).First;
        }

        var writeBacks = new List<LStatement>();
        var call = new LCallStatement(Callee(routine, target, receiver), Arguments(routine, arguments, writeBacks));
        return writeBacks.Count == 0 ? call : new LBlock([call, .. writeBacks]);
    }
}