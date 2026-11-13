using System.Collections.Immutable;
using System.Numerics;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Lowering;

/// <summary>
/// Bound trees of one unit → lowered trees (architecture.md §3.2, CMP-116). The input must be free of binder errors
/// (the lowerer reports nothing; it throws on error nodes). This skeleton translates the M2.1 statement and expression
/// set; the structured statements and intrinsics are delegated to <see cref="ForRewriter"/>, <see cref="CaseRewriter"/>
/// and <see cref="IntrinsicRewriter"/>. Variables of enclosing routines become <see cref="LOuterRef"/>; their owner gets a
/// <see cref="LoweredFrame"/> (CMP-345). Not yet: <c>Write</c>/<c>Writeln</c> (C-040, throws <see cref="NotSupportedException"/>).
/// </summary>
internal static class Lowerer
{
    public static LoweredUnit Lower(BindingResult result) =>
        new(result.Unit, [.. result.Routines.Select(r => LowerRoutine(result, r))], [.. result.Bodies.Select(b => LowerBody(result, b))]);

    public static LoweredBody LowerRoutine(BindingResult result, BoundRoutineBody body)
    {
        var context = new LoweringContext(result.Types, body.Routine, body.Scope.Symbols.OfType<VariableSymbol>());
        var lowered = context.Finish(body.Body);
        // CMP-345: a frame when nested routines capture something of this routine, and for a nested routine with nested
        // routines of its own (field 0 continues the static link chain they walk)
        var captured = result.GetCapturedVariables(body.Routine);
        var needsFrame = !captured.IsEmpty || body.Routine.IsNested && !body.NestedRoutines.IsEmpty;
        return needsFrame ? lowered with { Frame = new LoweredFrame(captured) } : lowered;
    }

    /// <summary>The program block or a unit initialization/finalization section (globals only, no locals).</summary>
    public static LoweredBody LowerBody(BindingResult result, BoundBlock block) =>
        new LoweringContext(result.Types, routine: null, locals: []).Finish(block);
}

/// <summary>Per-body lowering state (locals, temporaries, loop labels) and the translation of plain nodes.</summary>
internal sealed class LoweringContext
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

    /// <summary><c>procedure _RunError(Code: Integer)</c>, noreturn (rtl-abi.md §9.1).</summary>
    public RuntimeHelper RunError { get; }

    /// <summary><c>procedure _Halt(Code: Integer)</c>, noreturn (CMP-356).</summary>
    public RuntimeHelper Halt { get; }

    public LoweredBody Finish(BoundBlock body)
    {
        var block = (LBlock)Lower(body);
        return new LoweredBody(Routine, [.. _locals], block);
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
        CharTypeSymbol or BooleanTypeSymbol => Types.Integer,
        _ => throw new InvalidOperationException($"'{ordinal.Name}' is not an ordinal type."),
    };

    public LExpression Convert(LExpression value, TypeSymbol type) => ReferenceEquals(value.Type, type) ? value : new LConvert(value, type);

    public LConst Constant(BigInteger value, TypeSymbol type) => new(new ConstantValue(value, type), type);

    public LCompare Compare(LCompareOperator op, LExpression left, LExpression right) => new(op, left, right, Types.Boolean);

    /// <summary><c>x ± 1</c> (or <paramref name="step"/>) in the computation type, converted back to <paramref name="type"/>.</summary>
    public LExpression Step(LExpression value, TypeSymbol type, LBinaryOperator op, LExpression? step = null)
    {
        var computation = ComputationType(type);
        var amount = step is null ? Constant(BigInteger.One, computation) : Convert(step, computation);
        return Convert(new LBinary(op, Convert(value, computation), amount), type);
    }

    public LStatement CallNoReturn(RuntimeHelper helper, LExpression argument) =>
        new LBlock([new LCallStatement(new LHelperCallee(helper), [argument]), new LUnreachable()]);

    // ------------------------------------------------------------------ statements

    public LStatement Lower(BoundStatement statement)
    {
        switch (statement)
        {
            case BoundBlock block:
                return new LBlock([.. block.Statements.Select(Lower)]);
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
                return new LReturn();
            case BoundExitStatement exit:
                return new LBlock([new LAssign(new LLocalRef(_variables[Routine!.ResultVariable!]), Lower(exit.Value)), new LReturn()]);
            case BoundHaltStatement halt:
                return CallNoReturn(Halt, halt.ExitCode is null ? Constant(BigInteger.Zero, Types.Integer) : Lower(halt.ExitCode));
            case BoundCallStatement call:
                return new LCallStatement(new LRoutineCallee(call.Routine), Arguments(call.Routine, call.Arguments));
            case BoundExpressionStatement { Expression: BoundCallExpression call }:
                return new LCallStatement(new LRoutineCallee(call.Routine), Arguments(call.Routine, call.Arguments));
            case BoundIntrinsicCallStatement intrinsic:
                return IntrinsicRewriter.Rewrite(this, intrinsic);
            case BoundWriteStatement:
                throw new NotSupportedException("Write/Writeln lowering arrives with C-040.");
            default:
                throw new InvalidOperationException($"Cannot lower {statement.Kind}.");
        }
    }

    // ------------------------------------------------------------------ expressions

    public LExpression Lower(BoundExpression expression)
    {
        if (expression.HasErrors)
        {
            throw new InvalidOperationException("Cannot lower an erroneous expression.");
        }

        if (expression.ConstantValue is { } constant)
        {
            return new LConst(constant, expression.Type);
        }

        switch (expression)
        {
            case BoundVariableExpression { Variable: { IsGlobal: true } global }:
                return new LGlobalRef(global);
            case BoundVariableExpression variable:
                return _variables.TryGetValue(variable.Variable, out var local)
                    ? new LLocalRef(local)
                    : new LOuterRef(variable.Variable, variable.Variable.Type);
            case BoundParameterExpression parameter:
                return ReferenceEquals(parameter.Parameter.ContainingSymbol, Routine)
                    ? new LParamRef(parameter.Parameter)
                    : new LOuterRef(parameter.Parameter, parameter.Parameter.Type);
            case BoundConversionExpression conversion:
                return Convert(Lower(conversion.Operand), conversion.Type);
            case BoundUnaryExpression { OperatorKind: BoundUnaryOperatorKind.Identity } unary:
                return Lower(unary.Operand);
            case BoundUnaryExpression unary:
                return new LUnary(unary.OperatorKind == BoundUnaryOperatorKind.Negate ? LUnaryOperator.Negate : LUnaryOperator.Not, Lower(unary.Operand));
            case BoundBinaryExpression binary:
                return Binary(binary);
            case BoundCallExpression call:
                return new LCall(new LRoutineCallee(call.Routine), Arguments(call.Routine, call.Arguments));
            case BoundIntrinsicCallExpression intrinsic:
                return IntrinsicRewriter.Rewrite(this, intrinsic);
            default:
                throw new InvalidOperationException($"Cannot lower {expression.Kind}.");
        }
    }

    private LExpression Binary(BoundBinaryExpression binary)
    {
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

        return new LBinary(binary.OperatorKind switch
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
        }, left, right);
    }

    /// <summary>Value and <c>const</c> arguments as values; <c>var</c>/<c>out</c> arguments as the variable's address.</summary>
    private ImmutableArray<LExpression> Arguments(RoutineSymbol routine, ImmutableArray<BoundExpression> arguments) =>
        [.. arguments.Select((argument, i) => routine.Parameters[i].IsByReference
            ? new LAddrOf(Lower(argument), Types.Pointer)
            : Lower(argument))];
}
