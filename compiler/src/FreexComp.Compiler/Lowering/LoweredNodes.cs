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
/// <param name="Frame">The routine's frame record (CMP-345), or null when it needs none.</param>
internal sealed record LoweredBody(RoutineSymbol? Routine, ImmutableArray<LLocal> Locals, LBlock Body, LoweredFrame? Frame = null);

/// <summary>
/// A frame record (CMP-345): field 0 is the routine's static link (null for a top-level routine), then
/// <see cref="Variables"/> — the routine's variables, parameters and <c>Result</c> referenced by nested routines
/// (<see cref="Binding.BindingResult.GetCapturedVariables"/>, scope order). They live only in the frame.
/// </summary>
internal sealed record LoweredFrame(ImmutableArray<Symbol> Variables);

/// <summary>
/// The lowered bodies of one unit: <see cref="Routines"/> in binding order, then <see cref="Bodies"/> (the program block, or
/// a unit's initialization then finalization section, those present). <see cref="Initialization"/> and
/// <see cref="Finalization"/> name a unit's sections among <see cref="Bodies"/> (CMP-350).
/// </summary>
internal sealed record LoweredUnit(UnitSymbol Unit, ImmutableArray<LoweredBody> Routines, ImmutableArray<LoweredBody> Bodies)
{
    /// <summary>The static storage of typed constants declared in routines (LANG-344), in routine binding order.</summary>
    public ImmutableArray<VariableSymbol> Statics { get; init; } = [];

    public LoweredBody? Initialization { get; init; }

    public LoweredBody? Finalization { get; init; }
}

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

/// <summary>A variable of unit System referenced by lowered code (<c>Output</c>, CMP-410); declared <c>external</c> outside System.</summary>
internal sealed record RuntimeVariable(string Name, TypeSymbol Type)
{
    public LinkName LinkName => Mangler.SystemVariable(Name);
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

/// <summary><c>llvm.memset</c> of <see cref="Size"/> zero bytes at <see cref="Address"/> (a <c>Pointer</c> value, CMP-336).</summary>
internal sealed class LZero(LExpression address, TypeSymbol type) : LStatement
{
    public LExpression Address { get; } = address;

    /// <summary>The type of the storage being cleared (size and alignment).</summary>
    public TypeSymbol Type { get; } = type;
}

/// <summary>
/// Sets (<see cref="Include"/>) or clears bit <see cref="Element"/> of the big set at <see cref="Address"/> (CMP-336: byte
/// <c>x lshr 3</c>, bit <c>x and 7</c>). The element is an integer in 0..255; the lowering guards or checks it.
/// </summary>
internal sealed class LSetBit(LExpression address, LExpression element, bool include) : LStatement
{
    public LExpression Address { get; } = address;

    public LExpression Element { get; } = element;

    public bool Include { get; } = include;
}

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

/// <summary>A variable of unit System (<see cref="RuntimeVariable"/>), a variable reference like <see cref="LGlobalRef"/>.</summary>
internal sealed class LRuntimeVariableRef(RuntimeVariable variable) : LExpression(variable.Type)
{
    public RuntimeVariable Variable { get; } = variable;
}

/// <summary>A string constant (CMP-320): a reference to the module's literal block, or <c>nil</c> for <c>''</c>.</summary>
internal sealed class LStringLiteral(string value, TypeSymbol type) : LExpression(type)
{
    public string Value { get; } = value;
}

/// <summary>A parameter of the current routine; for <c>var</c>/<c>out</c> parameters the variable it refers to.</summary>
/// <summary>
/// A variable, parameter or <c>Result</c> of an enclosing routine (CMP-345), reached through the static link; for
/// <c>var</c>/<c>out</c> parameters the variable it refers to.
/// </summary>
internal sealed class LOuterRef(Symbol variable, TypeSymbol type) : LExpression(type)
{
    public Symbol Variable { get; } = variable;

    /// <summary>The routine owning <see cref="Variable"/>.</summary>
    public RoutineSymbol Owner => (RoutineSymbol)Variable.ContainingSymbol!;
}

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

/// <summary>A unary operation; <see cref="IsChecked"/> = integer negation under <c>{$Q+}</c> (CMP-312, set by <see cref="CheckRewriter"/>).</summary>
internal sealed class LUnary(LUnaryOperator op, LExpression operand, bool isChecked = false) : LExpression(operand.Type)
{
    public LUnaryOperator Operator { get; } = op;

    public LExpression Operand { get; } = operand;

    public bool IsChecked { get; } = isChecked;
}

internal enum LBinaryOperator { Add, Subtract, Multiply, Divide, IntegerDivide, Modulo, And, Or, Xor, ConditionalAnd, ConditionalOr, Shl, Shr }

/// <summary>
/// A primitive operation in the type of its operands (both have <see cref="LExpression.Type"/>; a shift count too).
/// <see cref="IsChecked"/>: integer <c>+ - *</c> under <c>{$Q+}</c>, overflow → <c>_OverflowError</c> (CMP-312, set by <see cref="CheckRewriter"/>).
/// </summary>
internal sealed class LBinary(LBinaryOperator op, LExpression left, LExpression right, bool isChecked = false) : LExpression(left.Type)
{
    public LBinaryOperator Operator { get; } = op;

    public LExpression Left { get; } = left;

    public LExpression Right { get; } = right;

    public bool IsChecked { get; } = isChecked;
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

/// <summary>
/// CMP-314: <see cref="Value"/> (an integer or char) unchanged when <c>Low ≤ Value ≤ High</c>, else <c>_RangeError</c>. The bounds
/// lie within the value's own type (<see cref="CheckRewriter"/> intersects them with it).
/// </summary>
internal sealed class LRangeCheck(LExpression value, BigInteger low, BigInteger high) : LExpression(value.Type)
{
    public LExpression Value { get; } = value;

    public BigInteger Low { get; } = low;

    public BigInteger High { get; } = high;
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

/// <summary>
/// Evaluates <see cref="Value"/> once, then <see cref="Body"/>, in which <see cref="LTempRef"/>s of <see cref="Temp"/> read it.
/// As the condition of an <see cref="LIf"/> its value may also be read in the guarded branch (the condition dominates it).
/// </summary>
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

// ---------------------------------------------------------------------- aggregates (big sets, static arrays)
// An expression of an aggregate type (storage [N x T], TypeLayout) is never an SSA value: it is a variable reference, an
// aggregate constant (LConst), LSequence or LView/LIndex of one, used through its address (LAddrOf, LAssign copies).

/// <summary>Executes <see cref="First"/>, then yields <see cref="Value"/> (e.g. a temporary filled by helper calls).</summary>
internal sealed class LSequence(LStatement first, LExpression value) : LExpression(value.Type)
{
    public LStatement First { get; } = first;

    public LExpression Value { get; } = value;
}

/// <summary>
/// A variable reference to the first <c>SizeOf(Type)</c> bytes of <see cref="Aggregate"/>'s storage read or written as
/// <see cref="LExpression.Type"/> (a small set inside a big set's bytes: ABI-050 bit order is the integer's).
/// </summary>
internal sealed class LView(LExpression aggregate, TypeSymbol type) : LExpression(type)
{
    public LExpression Aggregate { get; } = aggregate;
}

/// <summary>
/// Element <see cref="Index"/> (zero-based, <c>Int64</c>: index − Low, CMP-308) of the static array <see cref="Array"/>, a
/// variable reference of the element type.
/// </summary>
internal sealed class LIndex(LExpression array, LExpression index, TypeSymbol elementType) : LExpression(elementType)
{
    public LExpression Array { get; } = array;

    public LExpression Index { get; } = index;
}

/// <summary>Field <see cref="Field"/> of the record <see cref="Record"/> (CMP-307), a variable reference of the field type.</summary>
internal sealed class LField(LExpression record, FieldSymbol field) : LExpression(field.Type)
{
    public LExpression Record { get; } = record;

    public FieldSymbol Field { get; } = field;
}

/// <summary>Bit <see cref="Element"/> (an integer in 0..255) of the big set at <see cref="Address"/> (CMP-336); type <c>Boolean</c>.</summary>
internal sealed class LTestBit(LExpression address, LExpression element, TypeSymbol boolean) : LExpression(boolean)
{
    public LExpression Address { get; } = address;

    public LExpression Element { get; } = element;
}
