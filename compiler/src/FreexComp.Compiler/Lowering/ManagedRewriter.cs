using System.Collections.Immutable;
using System.Numerics;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Emit;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Lowering;

/// <summary>
/// CMP-324/325 for UnicodeString (the first managed type; dynamic arrays, interfaces and managed records plug in here with
/// their helper families): assignments become <c>_UStrAsg</c>/<c>_UStrCat</c>/<c>_UStrCatN</c>/<c>_UStrFromWChar</c> calls or a
/// hidden-Result call into the target (rule 4); string-valued operations outside an assignment write into managed
/// temporaries (rule 5: one alloca per lowering site, so a temporary inside a loop is reused each iteration, and finalised at
/// function exit); <see cref="AddLifetimes"/> adds rule 1 (zero-initialisation), rule 3 (<c>_UStrAddRef</c> of value parameters)
/// and the rule 2 cleanup scope. Globals are finalised by their unit's <c>$final</c> (rule 7, C-071).
/// </summary>
internal static class ManagedRewriter
{
    // ------------------------------------------------------------------ helpers (rtl-abi.md §9.3)

    private static RuntimeHelper Helper(string name, params TypeSymbol[] parameters) =>
        new(name, [.. parameters], ReturnType: null, IsNoReturn: false);

    private static LCallStatement Call(RuntimeHelper helper, params LExpression[] arguments) => new(new LHelperCallee(helper), [.. arguments]);

    private static LAddrOf AddressOf(LoweringContext context, LExpression variable) => new(variable, context.Types.Pointer);

    /// <summary>The helper family of a managed type (rtl-abi.md §9.3/§9.4/§9.6): <c>UStr</c>, <c>AStr</c> or <c>Intf</c> (C-099).</summary>
    private static string Family(TypeSymbol type) => type is InterfaceTypeSymbol ? "Intf" : Strings.IsAnsi(type) ? "AStr" : "UStr";

    /// <summary>The type info argument of the <c>_DynArray*</c> helpers (ABI-044).</summary>
    private static LTypeInfo TypeInfo(LoweringContext context, TypeSymbol type) => new(type, context.Types.Pointer);

    /// <summary>A managed record or static array (C-065): handled through its type info by <c>_Finalize</c>/<c>_AddRef</c>/<c>_CopyRecord</c>.</summary>
    public static bool IsManagedAggregate(TypeSymbol type) => type.IsManaged && type is RecordTypeSymbol or ArrayTypeSymbol;

    /// <summary>
    /// <c>_UStrClr(var S)</c> / <c>_AStrClr(var S)</c> / <c>_DynArrayClr(var A, TI)</c>: release, <c>S := nil</c>; a managed record or
    /// static array: <c>_Finalize(@v, TI)</c> (rtl-abi.md §9.5).
    /// </summary>
    public static LStatement Clear(LoweringContext context, LExpression variable) =>
        IsManagedAggregate(variable.Type)
            ? Call(Helper("_Finalize", context.Types.Pointer, context.Types.Pointer), AddressOf(context, variable), TypeInfo(context, variable.Type))
            : variable.Type is DynamicArrayTypeSymbol
                ? Call(Helper("_DynArrayClr", context.Types.Pointer, context.Types.Pointer), AddressOf(context, variable), TypeInfo(context, variable.Type))
                : Call(Helper("_" + Family(variable.Type) + "Clr", context.Types.Pointer), AddressOf(context, variable));

    /// <summary>
    /// <c>_UStrAddRef(S)</c> / <c>_AStrAddRef(S)</c> / <c>_DynArrayAddRef(A)</c> (ABI-031); a managed record or static array:
    /// <c>_AddRef(@v, TI)</c>.
    /// </summary>
    private static LStatement AddRef(LoweringContext context, LExpression value) =>
        IsManagedAggregate(value.Type)
            ? Call(Helper("_AddRef", context.Types.Pointer, context.Types.Pointer), AddressOf(context, value), TypeInfo(context, value.Type))
            : Call(Helper(value.Type is DynamicArrayTypeSymbol ? "_DynArrayAddRef" : "_" + Family(value.Type) + "AddRef", context.Types.Pointer), value);

    // ------------------------------------------------------------------ assignments (CMP-324, CMP-325 rule 4)

    /// <summary>
    /// <c>target := value</c> for a managed <paramref name="target"/> (a lowered variable reference): a call returning the
    /// string writes into the target directly when <see cref="LoweringContext.ReceivesResultDirectly"/> allows it, else into a
    /// temporary assigned with <c>_UStrAsg</c>; a concatenation is built into the target (<c>_UStrCat(@s, a, b)</c>, CMP-324),
    /// a conversion by its helper (LANG-245, C-063); any other value is assigned with <c>_UStrAsg</c>/<c>_AStrAsg(@s, value)</c>.
    /// </summary>
    public static LStatement Assign(LoweringContext context, LExpression target, BoundExpression value)
    {
        if (value.ConstantValue is null && Fill(context, target, value) is { } fill)
        {
            return fill;
        }

        if (target.Type is DynamicArrayTypeSymbol or InterfaceTypeSymbol && value.ConstantValue is { Value: null })
        {
            return Clear(context, target);                                  // CMP-330: a := nil; CMP-373: i := nil
        }

        return Assign(context, target, context.Lower(value));
    }

    /// <summary>
    /// <c>_UStrAsg(@target, value)</c> / <c>_AStrAsg</c> / <c>_DynArrayAsg(@target, value, TI)</c>: add a reference to the value,
    /// release the old one. A managed record or static array: <c>_CopyRecord(@target, @value, TI)</c> (CMP-325 rule 6).
    /// </summary>
    public static LStatement Assign(LoweringContext context, LExpression target, LExpression value) =>
        IsManagedAggregate(target.Type)
            ? Call(Helper("_CopyRecord", context.Types.Pointer, context.Types.Pointer, context.Types.Pointer),
                AddressOf(context, target), AddressOf(context, value), TypeInfo(context, target.Type))
        : target.Type is DynamicArrayTypeSymbol
            ? Call(Helper("_DynArrayAsg", context.Types.Pointer, context.Types.Pointer, context.Types.Pointer), AddressOf(context, target), value, TypeInfo(context, target.Type))
            : Call(Helper("_" + Family(target.Type) + "Asg", context.Types.Pointer, context.Types.Pointer), AddressOf(context, target), value);

    /// <summary>The value written into <paramref name="target"/> by a helper, or null when it is an ordinary value to assign.</summary>
    private static LStatement? Fill(LoweringContext context, LExpression target, BoundExpression value)
    {
        switch (value)
        {
            case BoundCallExpression call:
                return context.CallInto(target, call.Routine, call.Arguments, call.Target, call.Receiver);
            case BoundBinaryExpression { OperatorKind: BoundBinaryOperatorKind.Add, Type: StringTypeSymbol } concatenation:
                return Concatenate(context, target, concatenation);
            case BoundBinaryExpression { OperatorKind: BoundBinaryOperatorKind.Add, Type: DynamicArrayTypeSymbol } arrays:
                return ConcatenateArrays(context, target, arrays);
            case BoundIntrinsicCallExpression { Intrinsic.Intrinsic: IntrinsicKind.Copy } copy:
                return StringRewriter.CopyInto(context, AddressOf(context, target), copy);     // C-062: Dest may be the source
            case BoundConversionExpression { Type: StringTypeSymbol } conversion when !ReferenceEquals(conversion.Operand.Type, conversion.Type):
                return StringConversions.Convert(context, target, conversion);
            case BoundArrayConstructor constructor:
                return Construct(context, target, constructor);
            case BoundTypeTestExpression { Interface: { } intf, IsAs: true } test:
                // CMP-373 (C-100): _ObjToIntf(@dest, o, @guid) / _IntfCast(@dest, i, @guid); they release the old value
                return Call(Helper(test.Operand.Type is InterfaceTypeSymbol ? "_IntfCast" : "_ObjToIntf", context.Types.Pointer, context.Types.Pointer, context.Types.Pointer),
                    AddressOf(context, target), context.Lower(test.Operand), new LGuidRef(intf, context.Types.Pointer));
            default:
                return null;
        }
    }

    /// <summary>
    /// codegen.md §dynamic arrays (LANG-259, C-067): <c>[e1, …, en]</c> → <c>_DynArraySetLen</c> of a fresh temporary to <c>n</c>,
    /// the element stores (managed elements by their assignment helper), then <c>_DynArrayAsg(@target, tmp, TI)</c> — so the
    /// elements may read the target.
    /// </summary>
    private static LStatement Construct(LoweringContext context, LExpression target, BoundArrayConstructor constructor)
    {
        var built = new LLocalRef(context.NewTemp(constructor.Type));
        var statements = Build(context, built, constructor);
        statements.Add(Assign(context, target, built));
        return new LBlock([.. statements]);
    }

    /// <summary>
    /// The statements that make the variable <paramref name="built"/> (nil or a unique array, read by no element) the array of
    /// <paramref name="constructor"/>: <c>_DynArraySetLen</c> to <c>n</c>, then the element stores.
    /// </summary>
    private static List<LStatement> Build(LoweringContext context, LExpression built, BoundArrayConstructor constructor)
    {
        var types = context.Types;
        var array = (DynamicArrayTypeSymbol)constructor.Type;
        var index = new IntegerTypeSymbol("0..0", (IntegerTypeSymbol)types.Integer, BigInteger.Zero, BigInteger.Zero, containingSymbol: null, []);
        var dims = new LLocalRef(context.NewTemp(new ArrayTypeSymbol("array[0..0] of NativeInt", index, types.NativeInt, containingSymbol: null, [])));
        var statements = new List<LStatement>
        {
            new LAssign(new LIndex(dims, context.Constant(BigInteger.Zero, types.Int64), types.NativeInt), context.Constant(constructor.Elements.Length, types.NativeInt)),
            Call(Helper("_DynArraySetLen", types.Pointer, types.Pointer, types.NativeInt, types.Pointer),
                AddressOf(context, built), TypeInfo(context, array), context.Constant(1, types.NativeInt), AddressOf(context, dims)),
        };
        for (var k = 0; k < constructor.Elements.Length; k++)
        {
            var element = new LArrayElement(built, context.Constant(k, types.Int64), isChecked: false, array.ElementType);
            var value = constructor.Elements[k];
            statements.Add(array.ElementType.IsManaged ? Assign(context, element, value)
                : Strings.IsShort(array.ElementType) ? AssignShort(context, element, value)
                : new LAssign(element, context.Lower(value)));
        }

        return statements;
    }

    /// <summary>
    /// <c>target := value</c> for a short-string <paramref name="target"/> (LANG-246, C-063): the same type (or a constant, which
    /// carries the target's type) is a plain copy, another short string <c>_SStrAsg(@t, max, @s)</c>, anything else a conversion.
    /// </summary>
    public static LStatement AssignShort(LoweringContext context, LExpression target, BoundExpression value) =>
        value is BoundConversionExpression { ConstantValue: null } conversion && !ReferenceEquals(conversion.Operand.Type, conversion.Type)
            ? StringConversions.Convert(context, target, conversion)
            : ReferenceEquals(value.Type, target.Type) || value.ConstantValue is not null
                ? new LAssign(target, context.Lower(value))
                : StringConversions.ShortFromShort(context, target, context.Lower(value));

    // ------------------------------------------------------------------ values in temporaries (CMP-325 rule 5)

    /// <summary>A string-valued expression that needs storage, evaluated into a fresh temporary (the temporary is the value).</summary>
    public static LExpression Value(LoweringContext context, BoundExpression value)
    {
        var temporary = new LLocalRef(context.NewTemp(value.Type));
        var fill = Strings.IsShort(value.Type) ? AssignShort(context, temporary, value)
            : Fill(context, temporary, value) ?? throw new InvalidOperationException($"{value.Kind} needs no managed temporary.");
        return new LSequence(fill, temporary);
    }

    /// <summary>CMP-324: <c>_UStrFromWChar(@dest, c)</c>; an <c>AnsiChar</c> is zero-extended first (LANG-245).</summary>
    internal static LStatement FromChar(LoweringContext context, LExpression destination, LExpression character) =>
        Call(Helper("_UStrFromWChar", context.Types.Pointer, context.Types.Char), destination, context.Convert(character, context.Types.Char));

    /// <summary>
    /// CMP-324: the operands of a chain of <c>+</c> (left to right, each a string value: literal, variable, or temporary) →
    /// <c>_UStrCat(@dest, a, b)</c> for two, else a stack array <c>[n x ptr]</c> of the parts and <c>_UStrCatN(@dest, @parts, n)</c>.
    /// AnsiStrings (no <c>CatN</c> helper, rtl-abi.md §9.4): <c>_AStrCat(@dest, a, b)</c>; for more operands
    /// <c>_AStrCat(@tmp, a, b)</c>, <c>_AStrCat(@tmp, tmp, c)</c>, … into a temporary, then <c>_AStrAsg(@dest, tmp)</c>.
    /// </summary>
    private static LStatement Concatenate(LoweringContext context, LExpression target, BoundBinaryExpression concatenation)
    {
        var types = context.Types;
        var parts = new List<BoundExpression>();
        Flatten(concatenation, parts);
        var values = parts.Select(context.Lower).ToImmutableArray();
        if (Strings.IsAnsi(concatenation.Type))
        {
            var cat = Helper("_AStrCat", types.Pointer, types.Pointer, types.Pointer);
            if (values.Length == 2)
            {
                return Call(cat, AddressOf(context, target), values[0], values[1]);
            }

            var built = new LLocalRef(context.NewTemp(concatenation.Type));
            var steps = new List<LStatement> { Call(cat, AddressOf(context, built), values[0], values[1]) };
            steps.AddRange(values.Skip(2).Select(v => (LStatement)Call(cat, AddressOf(context, built), built, v)));
            steps.Add(Assign(context, target, built));
            return new LBlock([.. steps]);
        }

        var destination = AddressOf(context, target);
        if (values.Length == 2)
        {
            return Call(Helper("_UStrCat", types.Pointer, types.Pointer, types.Pointer), destination, values[0], values[1]);
        }

        var index = new IntegerTypeSymbol($"0..{values.Length - 1}", (IntegerTypeSymbol)types.Integer, BigInteger.Zero, values.Length - 1, containingSymbol: null, []);
        var array = new LLocalRef(context.NewTemp(new ArrayTypeSymbol($"array[0..{values.Length - 1}] of Pointer", index, types.Pointer, containingSymbol: null, [])));
        var statements = values.Select((v, k) => (LStatement)new LAssign(new LIndex(array, context.Constant(k, types.Int64), types.Pointer), v)).ToList();
        statements.Add(Call(Helper("_UStrCatN", types.Pointer, types.Pointer, types.Integer),
            destination, AddressOf(context, array), context.Constant(values.Length, types.Integer)));
        return new LBlock([.. statements]);
    }

    private static void Flatten(BoundExpression value, List<BoundExpression> parts)
    {
        if (value is BoundBinaryExpression { OperatorKind: BoundBinaryOperatorKind.Add, Type: StringTypeSymbol, ConstantValue: null } binary)
        {
            Flatten(binary.Left, parts);
            Flatten(binary.Right, parts);
        }
        else
        {
            parts.Add(value);
        }
    }

    /// <summary>
    /// CMP-1417 (C-390): a chain <c>E1 + … + En</c> of dynamic arrays → one <c>_DynArrayCat(@target, TI, n, @sources)</c>. The address
    /// of a target other than a plain variable is taken once, before the operands; the operands are evaluated left to right into
    /// <c>sources</c> (a stack <c>[n x ptr]</c>): a variable reference by its value, <c>nil</c>/<c>[]</c> as <c>null</c>, anything else
    /// (constructors, calls) into a temporary that is released after the call. <c>A := A + …</c> passes <c>A</c> itself as
    /// <c>sources[0]</c>, so the run time can append in place (RTL-088).
    /// </summary>
    private static LStatement ConcatenateArrays(LoweringContext context, LExpression target, BoundBinaryExpression concatenation)
    {
        var types = context.Types;
        var parts = new List<BoundExpression>();
        FlattenArrays(concatenation, parts);
        var statements = new List<LStatement>();
        LExpression destination = AddressOf(context, target);
        if (target is not (LLocalRef or LGlobalRef or LParamRef))
        {
            var slot = new LLocalRef(context.NewTemp(types.Pointer));
            statements.Add(new LAssign(slot, destination));
            destination = slot;
        }

        var n = parts.Count;
        var index = new IntegerTypeSymbol($"0..{n - 1}", (IntegerTypeSymbol)types.Integer, BigInteger.Zero, n - 1, containingSymbol: null, []);
        var sources = new LLocalRef(context.NewTemp(new ArrayTypeSymbol($"array[0..{n - 1}] of Pointer", index, types.Pointer, containingSymbol: null, [])));
        var temporaries = new List<LExpression>();
        for (var k = 0; k < n; k++)
        {
            var part = parts[k];
            LExpression value;
            if (part.ConstantValue is { Value: null })
            {
                value = new LConst(new ConstantValue(null, types.Pointer), types.Pointer);
            }
            else if (ExpressionBinder.IsAddressable(part))
            {
                value = context.Lower(part);
            }
            else
            {
                var temporary = new LLocalRef(context.NewTemp(part.Type));
                statements.Add(part is BoundArrayConstructor constructor ? new LBlock([.. Build(context, temporary, constructor)]) : Assign(context, temporary, part));
                temporaries.Add(temporary);
                value = temporary;
            }

            statements.Add(new LAssign(new LIndex(sources, context.Constant(k, types.Int64), types.Pointer), value));
        }

        statements.Add(Call(Helper("_DynArrayCat", types.Pointer, types.Pointer, types.NativeInt, types.Pointer),
            destination, TypeInfo(context, concatenation.Type), context.Constant(n, types.NativeInt), AddressOf(context, sources)));
        statements.AddRange(temporaries.Select(t => Clear(context, t)));
        return new LBlock([.. statements]);
    }

    private static void FlattenArrays(BoundExpression value, List<BoundExpression> parts)
    {
        if (value is BoundBinaryExpression { OperatorKind: BoundBinaryOperatorKind.Add, Type: DynamicArrayTypeSymbol } binary)
        {
            FlattenArrays(binary.Left, parts);
            FlattenArrays(binary.Right, parts);
        }
        else
        {
            parts.Add(value);
        }
    }

    // ------------------------------------------------------------------ comparisons (LANG-244, CMP-324)

    /// <summary>
    /// CMP-324: <c>a = b</c> / <c>a &lt;&gt; b</c> → <c>_UStrEq</c> (negated for <c>&lt;&gt;</c>), against <c>''</c> a <c>nil</c> test
    /// (ABI-021) without a call; the ordering operators compare <c>_UStrCmp(a, b)</c> with 0 (ordinal UTF-16 code units).
    /// </summary>
    public static LExpression Compare(LoweringContext context, BoundBinaryExpression comparison)
    {
        var types = context.Types;
        var left = context.Lower(comparison.Left);
        var right = context.Lower(comparison.Right);
        var kind = comparison.OperandType is StringTypeSymbol { Variant: var variant } ? variant : StringVariant.Unicode;
        var op = comparison.OperatorKind switch
        {
            BoundBinaryOperatorKind.Equal => LCompareOperator.Equal,
            BoundBinaryOperatorKind.NotEqual => LCompareOperator.NotEqual,
            BoundBinaryOperatorKind.Less => LCompareOperator.Less,
            BoundBinaryOperatorKind.LessOrEqual => LCompareOperator.LessOrEqual,
            BoundBinaryOperatorKind.Greater => LCompareOperator.Greater,
            BoundBinaryOperatorKind.GreaterOrEqual => LCompareOperator.GreaterOrEqual,
            _ => throw new InvalidOperationException($"{comparison.OperatorKind} is no string comparison."),
        };

        if (kind == StringVariant.Short)
        {
            // C-063: _SStrCmp(@a, @b) of two short strings
            var shortOrder = new LCall(new LHelperCallee(new RuntimeHelper("_SStrCmp", [types.Pointer, types.Pointer], types.Integer, IsNoReturn: false)),
                [AddressOf(context, left), AddressOf(context, right)]);
            return context.Compare(op, shortOrder, context.Constant(BigInteger.Zero, types.Integer));
        }

        if (op is LCompareOperator.Equal or LCompareOperator.NotEqual && kind == StringVariant.Ansi && (IsEmpty(left) || IsEmpty(right)))
        {
            return context.Compare(op, IsEmpty(left) ? right : left, new LStringLiteral("", types.String));       // ABI-021
        }

        if (kind == StringVariant.Ansi)
        {
            // C-063: _AStrCmp (bytewise) for every operator; there is no _AStrEq (rtl-abi.md §9.4)
            var bytewise = new LCall(new LHelperCallee(new RuntimeHelper("_AStrCmp", [types.Pointer, types.Pointer], types.Integer, IsNoReturn: false)), [left, right]);
            return context.Compare(op, bytewise, context.Constant(BigInteger.Zero, types.Integer));
        }

        if (op is LCompareOperator.Equal or LCompareOperator.NotEqual)
        {
            if (IsEmpty(left) || IsEmpty(right))
            {
                return context.Compare(op, IsEmpty(left) ? right : left, new LStringLiteral("", types.String));
            }

            var equal = new LCall(new LHelperCallee(new RuntimeHelper("_UStrEq", [types.Pointer, types.Pointer], types.Boolean, IsNoReturn: false)), [left, right]);
            return op == LCompareOperator.Equal ? equal : new LUnary(LUnaryOperator.Not, equal);
        }

        var order = new LCall(new LHelperCallee(new RuntimeHelper("_UStrCmp", [types.Pointer, types.Pointer], types.Integer, IsNoReturn: false)), [left, right]);
        return context.Compare(op, order, context.Constant(BigInteger.Zero, types.Integer));
    }

    private static bool IsEmpty(LExpression value) => value is LStringLiteral { Value.Length: 0 };

    // ------------------------------------------------------------------ lifetimes (CMP-325 rules 1–3)

    /// <summary>
    /// Rule 1: every managed local and temporary (not a hidden <c>Result</c>, which is the caller's initialised storage, ABI-032)
    /// is set to <c>nil</c> first; rule 3: <c>_UStrAddRef</c> of each managed value parameter (ABI-031) and <c>_UStrClr</c> of each
    /// managed <c>out</c> parameter (ABI-030); rule 2: when the body has any managed value parameter, local or temporary, the
    /// cleanup scope finalises them in declaration order (parameters, locals, temporaries).
    /// </summary>
    public static LoweredBody AddLifetimes(LoweringContext context, LoweredBody body)
    {
        var routine = body.Routine;
        var locals = body.Locals.Where(l => l.Type.IsManaged && !(routine is not null && ReferenceEquals(l.Variable, routine.ResultVariable) && IrEmitter.HasHiddenResult(routine)))
            .ToImmutableArray();
        var parameters = routine?.Parameters.Where(p => p.Type.IsManaged).ToImmutableArray() ?? [];
        // ABI-080 / CMP-325 rule 6: a managed record or array passed by address is the caller's copy, which the caller
        // references and finalises; the callee takes its own reference only to values it receives in its own storage
        var valueParameters = parameters.Where(p => p.Mode == ParameterMode.Value && !IrEmitter.IsPassedByAddress(p)).ToImmutableArray();
        if (locals.IsEmpty && valueParameters.IsEmpty && !parameters.Any(p => p.Mode == ParameterMode.Out))
        {
            return body;
        }

        var prologue = new List<LStatement>();
        prologue.AddRange(locals.Select(l => new LZero(AddressOf(context, new LLocalRef(l)), l.Type)));
        prologue.AddRange(valueParameters.Select(p => AddRef(context, new LParamRef(p))));
        prologue.AddRange(parameters.Where(p => p.Mode == ParameterMode.Out).Select(p => Clear(context, new LParamRef(p))));
        // CMP-1180: a variable its declaration scope finalises is only zeroed here
        var finalised = valueParameters.Select(p => (LExpression)new LParamRef(p))
            .Concat(locals.Where(l => !context.IsScopeFinalised(l.Variable)).Select(l => new LLocalRef(l))).ToImmutableArray();
        return body with
        {
            Body = new LBlock([.. prologue, .. body.Body.Statements]),
            Cleanup = finalised.IsEmpty ? null : new LBlock([.. finalised.Select(v => Clear(context, v))]),
        };
    }

    // ------------------------------------------------------------------ references (rule 4)

    /// <summary>Whether <paramref name="node"/> mentions <paramref name="local"/> anywhere (read, written or address taken).</summary>
    public static bool References(LNode node, LLocal local) => node switch
    {
        LLocalRef reference => ReferenceEquals(reference.Local, local),
        LConst or LGlobalRef or LStringLiteral or LParamRef or LOuterRef or LTempRef or LRoutineAddress or LClassRef
            or LThreadVarAddress or LTlsDescriptor or LReturn or LUnreachable or LBreak or LContinue => false,
        LAddrOf a => References(a.Variable, local),
        LUnary u => References(u.Operand, local),
        LBinary b => References(b.Left, local) || References(b.Right, local),
        LCompare c => References(c.Left, local) || References(c.Right, local),
        LConvert c => References(c.Operand, local),
        LRangeCheck r => References(r.Value, local),
        LSelect s => References(s.Condition, local) || References(s.WhenTrue, local) || References(s.WhenFalse, local),
        LCall c => References(c.Callee, local) || c.Arguments.Any(a => References(a, local)),
        LTemp t => References(t.Value, local) || References(t.Body, local),
        LSequence s => References(s.First, local) || References(s.Value, local),
        LView v => References(v.Aggregate, local),
        LIndex i => References(i.Array, local) || References(i.Index, local),
        LField f => References(f.Record, local),
        LTestBit t => References(t.Address, local) || References(t.Element, local),
        LDeref d => References(d.Pointer, local),
        LPointerOffset p => References(p.Pointer, local) || References(p.Index, local),
        LStringLength l => References(l.String, local),
        LArrayLength l => References(l.Array, local),
        LArrayElement e => References(e.Array, local) || References(e.Index, local) || (e.Length is { } n && References(n, local)),
        LOpenArray o => References(o.Data, local) || (o.High is { } h && References(h, local)),
        LTypeInfo or LGuidRef or LDispDescriptor => false,
        LStringIndex i => References(i.String, local) || References(i.Index, local),
        LAtomic a => References(a.Target, local) || References(a.Value, local) || (a.Comparand is { } c && References(c, local)),
        LBlock b => b.Statements.Any(s => References(s, local)),
        LAssign a => References(a.Target, local) || References(a.Value, local),
        LCallStatement c => References(c.Callee, local) || c.Arguments.Any(a => References(a, local)),
        LIf i => References(i.Condition, local) || References(i.Then, local) || (i.Else is { } e && References(e, local)),
        LLoop l => References(l.Body, local) || (l.Step is { } s && References(s, local)),
        LSwitch s => References(s.Selector, local) || s.Sections.Any(c => References(c.Body, local)) || (s.Default is { } d && References(d, local)),
        LZero z => References(z.Address, local),
        LSetBit s => References(s.Address, local) || References(s.Element, local),
        LMemSet m => References(m.Target, local) || References(m.Count, local) || References(m.Value, local),
        LMemMove m => References(m.Source, local) || References(m.Destination, local) || References(m.Count, local),
        LTryExcept t => ReferenceEquals(t.Exception, local) || References(t.Body, local) || References(t.Handler, local),
        LTryFinally t => References(t.Body, local) || (t.Cleanup is { } c && References(c, local)),
        _ => throw new InvalidOperationException($"Unknown lowered node {node.GetType().Name}."),
    };

    /// <summary>The procedural value and the <c>Self</c> a call evaluates before its arguments.</summary>
    private static bool References(LCallee callee, LLocal local) =>
        callee is LRoutineCallee routine && (routine.Target is { } t && References(t, local) || routine.Self is { } s && References(s, local)
            || routine.SafecallResult is { } r && References(r, local));
}
