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

    /// <summary><c>_UStrClr(var S)</c>: release, <c>S := nil</c>.</summary>
    public static LStatement Clear(LoweringContext context, LExpression variable) =>
        Call(Helper("_UStrClr", context.Types.Pointer), AddressOf(context, variable));

    // ------------------------------------------------------------------ assignments (CMP-324, CMP-325 rule 4)

    /// <summary>
    /// <c>target := value</c> for a managed <paramref name="target"/> (a lowered variable reference): a call returning the
    /// string writes into the target directly when <see cref="LoweringContext.ReceivesResultDirectly"/> allows it, else into a
    /// temporary assigned with <c>_UStrAsg</c>; a concatenation is built into the target (<c>_UStrCat(@s, a, b)</c>, CMP-324),
    /// a character converted by <c>_UStrFromWChar</c>; any other value is assigned with <c>_UStrAsg(@s, value)</c>.
    /// </summary>
    public static LStatement Assign(LoweringContext context, LExpression target, BoundExpression value)
    {
        if (value.ConstantValue is null)
        {
            switch (value)
            {
                case BoundCallExpression call:
                    return context.CallInto(target, call.Routine, call.Arguments);
                case BoundBinaryExpression { OperatorKind: BoundBinaryOperatorKind.Add, Type: StringTypeSymbol } concatenation:
                    return Concatenate(context, AddressOf(context, target), concatenation);
                case BoundConversionExpression { Type: StringTypeSymbol, Operand.Type: CharTypeSymbol } conversion:
                    return FromChar(context, AddressOf(context, target), context.Lower(conversion.Operand));
                case BoundIntrinsicCallExpression { Intrinsic.Intrinsic: IntrinsicKind.Copy } copy:
                    return StringRewriter.CopyInto(context, AddressOf(context, target), copy);     // C-062: Dest may be the source
            }
        }

        return Assign(context, target, context.Lower(value));
    }

    /// <summary><c>_UStrAsg(@target, value)</c>: add a reference to the value, release the old one.</summary>
    public static LStatement Assign(LoweringContext context, LExpression target, LExpression value) =>
        Call(Helper("_UStrAsg", context.Types.Pointer, context.Types.Pointer), AddressOf(context, target), value);

    // ------------------------------------------------------------------ values in temporaries (CMP-325 rule 5)

    /// <summary>A string-valued expression that needs storage, evaluated into a fresh managed temporary (the temporary is the value).</summary>
    public static LExpression Value(LoweringContext context, BoundExpression value)
    {
        var temporary = new LLocalRef(context.NewTemp(value.Type));
        var fill = value switch
        {
            BoundBinaryExpression concatenation => Concatenate(context, AddressOf(context, temporary), concatenation),
            BoundConversionExpression conversion => FromChar(context, AddressOf(context, temporary), context.Lower(conversion.Operand)),
            BoundIntrinsicCallExpression { Intrinsic.Intrinsic: IntrinsicKind.Copy } copy => StringRewriter.CopyInto(context, AddressOf(context, temporary), copy),
            _ => throw new InvalidOperationException($"{value.Kind} needs no managed temporary."),
        };
        return new LSequence(fill, temporary);
    }

    /// <summary>CMP-324: <c>_UStrFromWChar(@dest, c)</c>; an <c>AnsiChar</c> is zero-extended first (LANG-245).</summary>
    private static LStatement FromChar(LoweringContext context, LExpression destination, LExpression character) =>
        Call(Helper("_UStrFromWChar", context.Types.Pointer, context.Types.Char), destination, context.Convert(character, context.Types.Char));

    /// <summary>
    /// CMP-324: the operands of a chain of <c>+</c> (left to right, each a string value: literal, variable, or temporary) →
    /// <c>_UStrCat(@dest, a, b)</c> for two, else a stack array <c>[n x ptr]</c> of the parts and <c>_UStrCatN(@dest, @parts, n)</c>.
    /// </summary>
    private static LStatement Concatenate(LoweringContext context, LExpression destination, BoundBinaryExpression concatenation)
    {
        var types = context.Types;
        var parts = new List<BoundExpression>();
        Flatten(concatenation, parts);
        var values = parts.Select(context.Lower).ToImmutableArray();
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
        var valueParameters = parameters.Where(p => p.Mode == ParameterMode.Value).ToImmutableArray();
        if (locals.IsEmpty && parameters.IsEmpty)
        {
            return body;
        }

        var prologue = new List<LStatement>();
        prologue.AddRange(locals.Select(l => new LZero(AddressOf(context, new LLocalRef(l)), l.Type)));
        prologue.AddRange(valueParameters.Select(p => Call(Helper("_UStrAddRef", context.Types.Pointer), new LParamRef(p))));
        prologue.AddRange(parameters.Where(p => p.Mode == ParameterMode.Out).Select(p => Clear(context, new LParamRef(p))));
        var finalised = valueParameters.Select(p => (LExpression)new LParamRef(p)).Concat(locals.Select(l => new LLocalRef(l))).ToImmutableArray();
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
        LConst or LGlobalRef or LRuntimeVariableRef or LStringLiteral or LParamRef or LOuterRef or LTempRef or LRoutineAddress
            or LReturn or LUnreachable or LBreak or LContinue => false,
        LAddrOf a => References(a.Variable, local),
        LUnary u => References(u.Operand, local),
        LBinary b => References(b.Left, local) || References(b.Right, local),
        LCompare c => References(c.Left, local) || References(c.Right, local),
        LConvert c => References(c.Operand, local),
        LRangeCheck r => References(r.Value, local),
        LSelect s => References(s.Condition, local) || References(s.WhenTrue, local) || References(s.WhenFalse, local),
        LCall c => c.Arguments.Any(a => References(a, local)),
        LTemp t => References(t.Value, local) || References(t.Body, local),
        LSequence s => References(s.First, local) || References(s.Value, local),
        LView v => References(v.Aggregate, local),
        LIndex i => References(i.Array, local) || References(i.Index, local),
        LField f => References(f.Record, local),
        LTestBit t => References(t.Address, local) || References(t.Element, local),
        LDeref d => References(d.Pointer, local),
        LPointerOffset p => References(p.Pointer, local) || References(p.Index, local),
        LStringLength l => References(l.String, local),
        LStringIndex i => References(i.String, local) || References(i.Index, local),
        LAtomic a => References(a.Target, local) || References(a.Value, local) || (a.Comparand is { } c && References(c, local)),
        LBlock b => b.Statements.Any(s => References(s, local)),
        LAssign a => References(a.Target, local) || References(a.Value, local),
        LCallStatement c => c.Arguments.Any(a => References(a, local)),
        LIf i => References(i.Condition, local) || References(i.Then, local) || (i.Else is { } e && References(e, local)),
        LLoop l => References(l.Body, local) || (l.Step is { } s && References(s, local)),
        LSwitch s => References(s.Selector, local) || s.Sections.Any(c => References(c.Body, local)) || (s.Default is { } d && References(d, local)),
        LZero z => References(z.Address, local),
        LSetBit s => References(s.Address, local) || References(s.Element, local),
        LMemSet m => References(m.Target, local) || References(m.Count, local) || References(m.Value, local),
        LMemMove m => References(m.Source, local) || References(m.Destination, local) || References(m.Count, local),
        _ => throw new InvalidOperationException($"Unknown lowered node {node.GetType().Name}."),
    };
}
