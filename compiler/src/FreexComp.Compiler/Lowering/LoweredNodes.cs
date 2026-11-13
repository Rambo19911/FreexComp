using System.Collections.Immutable;
using System.Numerics;
using FreexComp.Compiler.Emit;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Lowering;

// The lowered tree (architecture.md §3.2): the closed set of "simple" nodes the IrEmitter handles. Nodes are added
// with the milestone that first needs them; every expression is typed, control flow is loops, ifs, switches and
// labelled break/continue, and every intrinsic is primitive operations or a runtime-helper call.

/// <summary>One lowered routine or unit-level body.</summary>
/// <param name="Routine">Null for the program block and unit initialization/finalization sections.</param>
/// <param name="Locals">Storage the body needs: <c>Result</c>, declared locals (scope order), then compiler temporaries.</param>
internal sealed record LoweredBody(RoutineSymbol? Routine, ImmutableArray<LLocal> Locals, LBlock Body);

/// <summary>The lowered bodies of one unit: <see cref="Routines"/> in binding order, then <see cref="Bodies"/>.</summary>
internal sealed record LoweredUnit(UnitSymbol Unit, ImmutableArray<LoweredBody> Routines, ImmutableArray<LoweredBody> Bodies);

/// <summary>
/// A local storage slot (an <c>alloca</c>): a declared local or <c>Result</c> (<see cref="Variable"/> set) or a
/// compiler temporary (<c>$tmpN</c>). Compared by identity.
/// </summary>
internal sealed class LLocal(string name, TypeSymbol type, VariableSymbol? variable)
{
    public string Name { get; } = name;

    public TypeSymbol Type { get; } = type;

    public VariableSymbol? Variable { get; } = variable;

    public override string ToString() => Name;
}

/// <summary>The target of <see cref="LBreak"/>/<see cref="LContinue"/>; <see cref="Kind"/> (<c>for</c>, <c>while</c>, <c>repeat</c>) names the blocks.</summary>
internal sealed class LLoopLabel(string kind, int id)
{
    public string Kind { get; } = kind;

    public int Id { get; } = id;

    public override string ToString() => Kind + "." + Id;
}

/// <summary>A value computed once by <see cref="LTemp"/> and read through <see cref="LTempRef"/> (an SSA value, no storage).</summary>
internal sealed class LTempValue(TypeSymbol type, int id)
{
    public TypeSymbol Type { get; } = type;

    public int Id { get; } = id;

    public override string ToString() => "$v" + Id;
}

/// <summary>A runtime helper of unit System (rtl-abi.md §9) called by lowered code.</summary>
internal sealed record RuntimeHelper(string Name, ImmutableArray<TypeSymbol> ParameterTypes, TypeSymbol? ReturnType, bool IsNoReturn)
{
    public LinkName LinkName => Mangler.RuntimeHelper(Name);
}

/// <summary>What an <see cref="LCall"/> / <see cref="LCallStatement"/> calls.</summary>
internal abstract record LCallee
{
    public abstract TypeSymbol? ReturnType { get; }

    public abstract bool IsNoReturn { get; }
}

internal sealed record LRoutineCallee(RoutineSymbol Routine) : LCallee
{
    public override TypeSymbol? ReturnType => Routine.ReturnType;

    public override bool IsNoReturn => Routine.IsNoReturn;
}

internal sealed record LHelperCallee(RuntimeHelper Helper) : LCallee
{
    public override TypeSymbol? ReturnType => Helper.ReturnType;

    public override bool IsNoReturn => Helper.IsNoReturn;
}

internal abstract class LNode;

// ---------------------------------------------------------------------- statements

internal abstract class LStatement : LNode;

internal sealed class LBlock(ImmutableArray<LStatement> statements) : LStatement
{
    public ImmutableArray<LStatement> Statements { get; } = statements;
}

/// <summary><c>Target := Value</c>; the target is a variable reference (<see cref="LLocalRef"/>, <see cref="LGlobalRef"/>, <see cref="LParamRef"/>).</summary>
internal sealed class LAssign(LExpression target, LExpression value) : LStatement
{
    public LExpression Target { get; } = target;

    public LExpression Value { get; } = value;
}

/// <summary>A call whose result (if any) is discarded.</summary>
internal sealed class LCallStatement(LCallee callee, ImmutableArray<LExpression> arguments) : LStatement
{
    public LCallee Callee { get; } = callee;

    public ImmutableArray<LExpression> Arguments { get; } = arguments;
}

internal sealed class LIf(LExpression condition, LStatement then, LStatement? @else) : LStatement
{
    public LExpression Condition { get; } = condition;

    public LStatement Then { get; } = then;

    public LStatement? Else { get; } = @else;
}

/// <summary>
/// An infinite loop: <see cref="Body"/>, then <see cref="Step"/>, then again. <see cref="LContinue"/> jumps to
/// <see cref="Step"/> (to the top when there is none), <see cref="LBreak"/> behind the loop.
/// </summary>
internal sealed class LLoop(LLoopLabel label, LStatement body, LStatement? step) : LStatement
{
    public LLoopLabel Label { get; } = label;

    public LStatement Body { get; } = body;

    public LStatement? Step { get; } = step;
}

internal sealed class LBreak(LLoopLabel label) : LStatement
{
    public LLoopLabel Label { get; } = label;
}

internal sealed class LContinue(LLoopLabel label) : LStatement
{
    public LLoopLabel Label { get; } = label;
}

/// <summary>
/// Integer switch (CMP-322). The selector (evaluated once) is in its computation type; each section lists single
/// values (ranges of at most 256 values expanded) and larger ranges, tested before the switch in section order.
/// </summary>
internal sealed class LSwitch(LExpression selector, ImmutableArray<LSwitchSection> sections, LStatement? @default) : LStatement
{
    public LExpression Selector { get; } = selector;

    public ImmutableArray<LSwitchSection> Sections { get; } = sections;

    public LStatement? Default { get; } = @default;
}

internal sealed record LSwitchSection(ImmutableArray<BigInteger> Values, ImmutableArray<(BigInteger Low, BigInteger High)> Ranges, LStatement Body);

/// <summary>Leaves the body (branch to its exit block, CMP-310).</summary>
internal sealed class LReturn : LStatement;

/// <summary>Control never gets here (after a noreturn call).</summary>
internal sealed class LUnreachable : LStatement;

// ---------------------------------------------------------------------- expressions

internal abstract class LExpression(TypeSymbol type) : LNode
{
    public TypeSymbol Type { get; } = type;
}

internal sealed class LConst(ConstantValue value, TypeSymbol type) : LExpression(type)
{
    public ConstantValue Value { get; } = value;
}

internal sealed class LLocalRef(LLocal local) : LExpression(local.Type)
{
    public LLocal Local { get; } = local;
}

internal sealed class LGlobalRef(VariableSymbol variable) : LExpression(variable.Type)
{
    public VariableSymbol Variable { get; } = variable;
}

/// <summary>A parameter of the current routine; for <c>var</c>/<c>out</c> parameters the variable it refers to.</summary>
internal sealed class LParamRef(ParameterSymbol parameter) : LExpression(parameter.Type)
{
    public ParameterSymbol Parameter { get; } = parameter;
}

/// <summary>The address of a variable reference (<c>var</c>/<c>out</c> arguments); type <c>Pointer</c>.</summary>
internal sealed class LAddrOf(LExpression variable, TypeSymbol pointerType) : LExpression(pointerType)
{
    public LExpression Variable { get; } = variable;
}

internal enum LUnaryOperator
{
    Negate,

    /// <summary>Bitwise complement of integers, logical negation of <c>Boolean</c>.</summary>
    Not,

    /// <summary>Real absolute value (<c>llvm.fabs</c>, CMP-412); integer <c>Abs</c> is lowered to a select.</summary>
    Abs,
}

internal sealed class LUnary(LUnaryOperator op, LExpression operand) : LExpression(operand.Type)
{
    public LUnaryOperator Operator { get; } = op;

    public LExpression Operand { get; } = operand;
}

internal enum LBinaryOperator { Add, Subtract, Multiply, Divide, IntegerDivide, Modulo, And, Or, Xor, ConditionalAnd, ConditionalOr, Shl, Shr }

/// <summary>A primitive operation in the type of its operands (both have <see cref="LExpression.Type"/>; a shift count too).</summary>
internal sealed class LBinary(LBinaryOperator op, LExpression left, LExpression right) : LExpression(left.Type)
{
    public LBinaryOperator Operator { get; } = op;

    public LExpression Left { get; } = left;

    public LExpression Right { get; } = right;
}

internal enum LCompareOperator { Equal, NotEqual, Less, LessOrEqual, Greater, GreaterOrEqual }

/// <summary>
/// A comparison (type <c>Boolean</c>); operands have the same type, except a signed with an unsigned 64-bit operand
/// (LANG-302), compared mathematically (CMP-315).
/// </summary>
internal sealed class LCompare(LCompareOperator op, LExpression left, LExpression right, TypeSymbol boolean) : LExpression(boolean)
{
    public LCompareOperator Operator { get; } = op;

    public LExpression Left { get; } = left;

    public LExpression Right { get; } = right;
}

/// <summary>A value conversion between scalar types (the IR instruction follows from the two types, CMP-305/313).</summary>
internal sealed class LConvert(LExpression operand, TypeSymbol type) : LExpression(type)
{
    public LExpression Operand { get; } = operand;
}

internal sealed class LSelect(LExpression condition, LExpression whenTrue, LExpression whenFalse) : LExpression(whenTrue.Type)
{
    public LExpression Condition { get; } = condition;

    public LExpression WhenTrue { get; } = whenTrue;

    public LExpression WhenFalse { get; } = whenFalse;
}

/// <summary>A call with a result.</summary>
internal sealed class LCall(LCallee callee, ImmutableArray<LExpression> arguments)
    : LExpression(callee.ReturnType ?? throw new ArgumentException("A procedure call is a statement.", nameof(callee)))
{
    public LCallee Callee { get; } = callee;

    public ImmutableArray<LExpression> Arguments { get; } = arguments;
}

/// <summary>Evaluates <see cref="Value"/> once, then <see cref="Body"/>, in which <see cref="LTempRef"/>s of <see cref="Temp"/> read it.</summary>
internal sealed class LTemp(LTempValue temp, LExpression value, LExpression body) : LExpression(body.Type)
{
    public LTempValue Temp { get; } = temp;

    public LExpression Value { get; } = value;

    public LExpression Body { get; } = body;
}

internal sealed class LTempRef(LTempValue temp) : LExpression(temp.Type)
{
    public LTempValue Temp { get; } = temp;
}
