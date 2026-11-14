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
/// <param name="Cleanup">
/// CMP-325 rule 2: the function-level cleanup scope — finalisation of the managed value parameters, locals and temporaries in
/// declaration order — or null when the body has none. The emitter routes every exit through block <c>cleanup</c> holding it.
/// EH milestone hook (CMP-393/394): the same statements become the <c>cleanuppad</c> funclet, calls inside the body become
/// <c>invoke</c>s unwinding to it.
/// </param>
internal sealed record LoweredBody(RoutineSymbol? Routine, ImmutableArray<LLocal> Locals, LBlock Body, LoweredFrame? Frame = null, LBlock? Cleanup = null);

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

    /// <summary>
    /// The handler variables (LANG-821) of the unit-level bodies, kept in internal module globals (link name given) so that the
    /// outlined <c>$fin</c> routines of those bodies, which have no frame record, reach them like any global (C-091).
    /// </summary>
    public ImmutableArray<(VariableSymbol Variable, LinkName Name)> BodyGlobals { get; init; } = [];

    public LoweredBody? Initialization { get; init; }

    /// <summary>The unit's threadvars (ABI-180), declaration order; they have no module global.</summary>
    public ImmutableArray<VariableSymbol> ThreadVars { get; init; } = [];

    /// <summary><c>_TlsRegister(@"U.$tls")</c> when the unit declares threadvars (first in <c>$init</c>; a program's after <c>_InitUnits</c>).</summary>
    public LStatement? TlsRegistration { get; init; }

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

/// <summary>What an <see cref="LCall"/> / <see cref="LCallStatement"/> calls.</summary>
internal abstract record LCallee
{
    public abstract TypeSymbol? ReturnType { get; }

    public abstract bool IsNoReturn { get; }
}

/// <summary>A routine; with <paramref name="Target"/> a call through that procedural value whose signature is <paramref name="Routine"/> (CMP-378).</summary>
/// <remarks>A method call (CMP-367) passes <paramref name="Self"/> and the constructor/destructor <paramref name="Flag"/> first (ABI-082); with
/// <paramref name="IsVirtual"/> the code address is read from the VMT slot of <c>Self</c>'s class (<c>Self</c> itself is the VMT for class methods).</remarks>
internal sealed record LRoutineCallee(RoutineSymbol Routine, LExpression? Target = null, LExpression? Self = null, int? Flag = null, bool IsVirtual = false) : LCallee
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

/// <summary>
/// CMP-391 <c>try Body except Handler end</c> as a catch-all: an exception leaving <see cref="Body"/> is caught (filter
/// <c>System._SehFilter</c>), its object (<c>_BeginCatch</c>) stored in <see cref="Exception"/>, then <see cref="Handler"/> runs —
/// ordinary code whose class tests, <c>else</c> part and re-raise (<c>_ReRaise</c>) are explicit. The handler region is an EH
/// scope whose cleanup calls <c>_EndCatch</c>; normal completion and leaving it by a jump call <c>_EndCatch</c> too.
/// </summary>
internal sealed class LTryExcept(LStatement body, LLocal exception, LStatement handler) : LStatement
{
    public LStatement Body { get; } = body;

    public LLocal Exception { get; } = exception;

    public LStatement Handler { get; } = handler;
}

/// <summary>
/// CMP-392 <c>try Body finally F end</c>: <see cref="Finally"/> is the outlined <c>$fin</c> routine of F (a routine nested in the
/// current one, no parameters, reaching the variables through the frame record), called after normal completion, before a
/// jump that leaves <see cref="Body"/>, and from a cleanup funclet on the exception path.
/// </summary>
internal sealed class LTryFinally(LStatement body, RoutineSymbol @finally) : LStatement
{
    public LStatement Body { get; } = body;

    public RoutineSymbol Finally { get; } = @finally;
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

    /// <summary>Square root of a <c>Double</c> (<c>llvm.sqrt.f64</c>, CMP-412; negative → NaN).</summary>
    Sqrt,
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
/// <see cref="LExpression.Type"/> (a small set inside a big set's bytes: ABI-050 bit order is the integer's; a variable typecast,
/// LANG-290, of any variable reference of the same size).
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

// ---------------------------------------------------------------------- UnicodeString (C-060, C-061)
// A string value is its payload pointer (ptr, nil = ''). Managed assignments, temporaries and lifetimes are explicit
// helper calls made by ManagedRewriter: an LAssign to a managed target exists only as "the call writes its result into
// the target" (a hidden-Result call, CMP-325 rule 4).

/// <summary>ABI-023 / CMP-324: <c>Length(s)</c> of the string value <see cref="String"/> (<c>Integer</c>), 0 for <c>nil</c> without loading.</summary>
internal sealed class LStringLength(LExpression @string, TypeSymbol integer) : LExpression(integer)
{
    public LExpression String { get; } = @string;
}

/// <summary>ABI-041 / CMP-330: <c>Length</c> of the dynamic array value <see cref="Array"/> (nil → 0, else the Int64 at A − 8), as an <c>Integer</c>.</summary>
internal sealed class LArrayLength(LExpression array, TypeSymbol integer) : LExpression(integer)
{
    public LExpression Array { get; } = array;
}

/// <summary>
/// ABI-042 / CMP-330: element <see cref="Index"/> (zero-based, <c>Int64</c>) of the dynamic array value <see cref="Array"/>, a variable
/// reference of the element type (no copy-on-write). <see cref="IsChecked"/>: <c>{$R+}</c> test <c>Index &lt;u Length</c> (CMP-331).
/// An open array's element (C-106): <see cref="Array"/> is its data pointer and <see cref="Length"/> (<c>Int64</c>, High + 1) the
/// count the check uses.
/// </summary>
internal sealed class LArrayElement(LExpression array, LExpression index, bool isChecked, TypeSymbol elementType, LExpression? length = null) : LExpression(elementType)
{
    public LExpression Array { get; } = array;

    public LExpression Index { get; } = index;

    public bool IsChecked { get; } = isChecked;

    public LExpression? Length { get; } = length;
}

/// <summary>
/// ABI-080 (C-106): the argument of an open array parameter — two IR arguments, the <c>ptr</c> <see cref="Data"/> (element 0, or
/// null) then the <c>i64</c> <see cref="High"/>. A null <see cref="High"/> means a dynamic array value: its length − 1, read from
/// <see cref="Data"/> (evaluated once).
/// </summary>
internal sealed class LOpenArray(LExpression data, LExpression? high, TypeSymbol type) : LExpression(type)
{
    public LExpression Data { get; } = data;

    public LExpression? High { get; } = high;
}

/// <summary>CMP-407: the address of the RTTI block of <see cref="Described"/> (rtl-abi.md §10), a <c>Pointer</c>.</summary>
internal sealed class LTypeInfo(TypeSymbol described, TypeSymbol pointer) : LExpression(pointer)
{
    public TypeSymbol Described { get; } = described;
}

/// <summary>
/// ABI-024 / CMP-324: character <see cref="Index"/> (zero-based, <c>Int64</c>: i − 1) of the string <see cref="String"/>, a
/// variable reference of type <c>Char</c>. Read: the string value plus <c>2·Index</c>. Written (assignment target, address taken):
/// <see cref="String"/> is a variable reference made unique first (<c>_UStrUnique</c>, LANG-241), then reloaded.
/// <see cref="IsChecked"/>: <c>{$R+}</c> test <c>Index &lt;u Length</c> (CMP-331) → <c>_RangeError</c>.
/// </summary>
internal sealed class LStringIndex(LExpression @string, LExpression index, bool isChecked, TypeSymbol @char) : LExpression(@char)
{
    public LExpression String { get; } = @string;

    public LExpression Index { get; } = index;

    public bool IsChecked { get; } = isChecked;
}

// ---------------------------------------------------------------------- pointers (C-056)

/// <summary><c>p^</c> (CMP-340): the variable at the address <see cref="Pointer"/> (a <c>ptr</c> value), a variable reference of its type.</summary>
internal sealed class LDeref(LExpression pointer, TypeSymbol type) : LExpression(type)
{
    public LExpression Pointer { get; } = pointer;
}

/// <summary>
/// CMP-342 <c>p + n</c>: <c>getelementptr inbounds T, ptr p, i64 n</c> with <see cref="ElementType"/> <c>T</c>; <see cref="Index"/> is an
/// <c>Int64</c>-sized integer (already negated for <c>p - n</c>).
/// </summary>
internal sealed class LPointerOffset(LExpression pointer, LExpression index, TypeSymbol elementType, TypeSymbol type) : LExpression(type)
{
    public LExpression Pointer { get; } = pointer;

    public LExpression Index { get; } = index;

    public TypeSymbol ElementType { get; } = elementType;
}

/// <summary><c>@Routine</c> (CMP-340): the routine's code address <c>ptr @"link.name"</c>.</summary>
internal sealed class LRoutineAddress(RoutineSymbol routine, TypeSymbol pointerType) : LExpression(pointerType)
{
    public RoutineSymbol Routine { get; } = routine;
}

/// <summary>
/// CMP-414: the calling thread's address of the threadvar <see cref="Variable"/> (<c>_TlsBase() + U.$tls.Offset + k</c>,
/// ABI-181); computed at every use, never cached across statements.
/// </summary>
internal sealed class LThreadVarAddress(VariableSymbol variable, TypeSymbol pointerType) : LExpression(pointerType)
{
    public VariableSymbol Variable { get; } = variable;
}

/// <summary>The threadvar descriptor <c>@"U.$tls"</c> of <see cref="Unit"/> (ABI-180); type <c>Pointer</c>.</summary>
internal sealed class LTlsDescriptor(UnitSymbol unit, TypeSymbol pointerType) : LExpression(pointerType)
{
    public UnitSymbol Unit { get; } = unit;
}

/// <summary>The VMT of <see cref="Class"/> (ABI-071 class reference, CMP-366 <c>@"U.T.$vmt"</c>); type <c>Pointer</c>.</summary>
internal sealed class LClassRef(ClassTypeSymbol type, TypeSymbol pointerType) : LExpression(pointerType)
{
    public ClassTypeSymbol Class { get; } = type;
}

/// <summary>
/// <c>FillChar</c> (CMP-412): <c>llvm.memset</c> of <see cref="Count"/> bytes (<c>Int64</c>) of <see cref="Value"/> (a <c>Byte</c>) at the
/// address of the variable reference <see cref="Target"/> (its access alignment, CMP-309).
/// </summary>
internal sealed class LMemSet(LExpression target, LExpression count, LExpression value) : LStatement
{
    public LExpression Target { get; } = target;

    public LExpression Count { get; } = count;

    public LExpression Value { get; } = value;
}

/// <summary><c>Move</c> (CMP-412): <c>llvm.memmove</c> of <see cref="Count"/> bytes from the variable reference <see cref="Source"/> to <see cref="Destination"/>.</summary>
internal sealed class LMemMove(LExpression source, LExpression destination, LExpression count) : LStatement
{
    public LExpression Source { get; } = source;

    public LExpression Destination { get; } = destination;

    public LExpression Count { get; } = count;
}
// ---------------------------------------------------------------------- atomics (C-168)

internal enum LAtomicOperator { Add, Subtract, Exchange, CompareExchange }

/// <summary>
/// CMP-412 / ABI-172: one sequentially consistent atomic operation on the variable reference <see cref="Target"/> (a 32-/64-bit
/// integer or a pointer; add/subtract work on a pointer through its <c>NativeInt</c> <see cref="LView"/>): <c>atomicrmw add|sub|xchg</c>
/// with <see cref="Value"/>, or <c>cmpxchg</c> storing <see cref="Value"/> when the target equals <see cref="Comparand"/>. The value
/// is the target's <b>previous</b> content. Evaluation: the target's address, then <see cref="Value"/>, then <see cref="Comparand"/>.
/// </summary>
internal sealed class LAtomic(LAtomicOperator op, LExpression target, LExpression value, LExpression? comparand) : LExpression(target.Type)
{
    public LAtomicOperator Operator { get; } = op;

    public LExpression Target { get; } = target;

    public LExpression Value { get; } = value;

    public LExpression? Comparand { get; } = comparand;
}
