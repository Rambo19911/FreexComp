using System.Collections.Immutable;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

/// <summary>
/// Kinds of bound nodes (architecture.md §3: the body binder's output, input of flow analysis and lowering). The
/// set grows with the binder tasks; every node records the syntax it was bound from.
/// </summary>
internal enum BoundKind
{
    // expressions
    ErrorExpression,
    Literal,
    ConstantExpression,
    VariableExpression,
    ConversionExpression,
    UnaryExpression,
    BinaryExpression,
    ParameterExpression,
    CallExpression,
    IntrinsicCallExpression,
    SetConstructor,
    IndexExpression,
    FieldAccessExpression,
    AddressOfExpression,
    DereferenceExpression,
    PointerArithmeticExpression,
    VariableTypecastExpression,
    ArrayConstructor,

    // statements
    Block,
    AssignmentStatement,
    ExpressionStatement,
    ErrorStatement,
    IfStatement,
    WhileStatement,
    RepeatStatement,
    ForStatement,
    CaseStatement,
    BreakStatement,
    ContinueStatement,
    ExitStatement,
    HaltStatement,
    CallStatement,
    IntrinsicCallStatement,
    WriteStatement,
}

/// <summary>A node of the bound tree: immutable, typed, symbols resolved.</summary>
internal abstract class BoundNode
{
    private protected BoundNode(SyntaxNode syntax)
    {
        Syntax = syntax;
    }

    public abstract BoundKind Kind { get; }

    public SyntaxNode Syntax { get; }
}

/// <summary>An expression; <see cref="ConstantValue"/> is set when it is a constant expression (LANG-340).</summary>
internal abstract class BoundExpression : BoundNode
{
    private protected BoundExpression(SyntaxNode syntax, TypeSymbol type, ConstantValue? constantValue)
        : base(syntax)
    {
        Type = type;
        ConstantValue = constantValue;
    }

    public TypeSymbol Type { get; }

    public ConstantValue? ConstantValue { get; }

    public bool HasErrors => Type.TypeKind == TypeKind.Error;
}

/// <summary>
/// An expression that could not be bound (CMP-135): error type, never reported on again. <see cref="Children"/> keeps
/// whatever was bound below it for the language service.
/// </summary>
internal sealed class BoundErrorExpression : BoundExpression
{
    public BoundErrorExpression(SyntaxNode syntax, TypeSymbol errorType, ImmutableArray<BoundExpression> children = default)
        : base(syntax, errorType, constantValue: null)
    {
        Children = children.IsDefault ? [] : children;
    }

    public override BoundKind Kind => BoundKind.ErrorExpression;

    public ImmutableArray<BoundExpression> Children { get; }
}

/// <summary>A literal token: integer, real, string or character, <c>nil</c>.</summary>
internal sealed class BoundLiteral : BoundExpression
{
    public BoundLiteral(SyntaxNode syntax, ConstantValue value)
        : base(syntax, value.Type, value)
    {
    }

    public override BoundKind Kind => BoundKind.Literal;
}

/// <summary>A reference to a true constant (LANG-343): its value is the expression's value.</summary>
internal sealed class BoundConstantExpression : BoundExpression
{
    public BoundConstantExpression(SyntaxNode syntax, ConstantSymbol constant, ConstantValue value)
        : base(syntax, value.Type, value)
    {
        Constant = constant;
    }

    public override BoundKind Kind => BoundKind.ConstantExpression;

    public ConstantSymbol Constant { get; }
}

/// <summary>A reference to a global or local variable.</summary>
internal sealed class BoundVariableExpression : BoundExpression
{
    public BoundVariableExpression(SyntaxNode syntax, VariableSymbol variable)
        : base(syntax, variable.Type, constantValue: null)
    {
        Variable = variable;
    }

    public override BoundKind Kind => BoundKind.VariableExpression;

    public VariableSymbol Variable { get; }
}

/// <summary>
/// A value conversion: an implicit one inserted by the binder (operand widening LANG-220, real promotion LANG-230,
/// Boolean normalisation LANG-215) or an explicit typecast <c>T(x)</c> (LANG-290, <see cref="IsExplicit"/>).
/// The conversion of a constant is a constant of the target type.
/// </summary>
internal sealed class BoundConversionExpression : BoundExpression
{
    public BoundConversionExpression(SyntaxNode syntax, BoundExpression operand, TypeSymbol type, ConversionKind conversionKind,
        bool isExplicit, ConstantValue? constantValue)
        : base(syntax, type, constantValue)
    {
        Operand = operand;
        ConversionKind = conversionKind;
        IsExplicit = isExplicit;
    }

    public override BoundKind Kind => BoundKind.ConversionExpression;

    public BoundExpression Operand { get; }

    public ConversionKind ConversionKind { get; }

    public bool IsExplicit { get; }
}

internal enum BoundUnaryOperatorKind
{
    /// <summary>Unary <c>+</c>.</summary>
    Identity,
    Negate,

    /// <summary>Logical negation (Boolean family) or bitwise complement (integers, LANG-227).</summary>
    Not,
}

/// <summary>A unary operator; the operand is already converted to the type the operation is done in (= <see cref="BoundExpression.Type"/>).</summary>
internal sealed class BoundUnaryExpression : BoundExpression
{
    public BoundUnaryExpression(SyntaxNode syntax, BoundUnaryOperatorKind operatorKind, BoundExpression operand, TypeSymbol type,
        ConstantValue? constantValue)
        : base(syntax, type, constantValue)
    {
        OperatorKind = operatorKind;
        Operand = operand;
    }

    public override BoundKind Kind => BoundKind.UnaryExpression;

    public BoundUnaryOperatorKind OperatorKind { get; }

    public BoundExpression Operand { get; }
}

internal enum BoundBinaryOperatorKind
{
    Add,
    Subtract,
    Multiply,

    /// <summary><c>/</c>: real division (LANG-226).</summary>
    Divide,
    IntegerDivide,
    Modulo,

    /// <summary>Bitwise on integers; Boolean with complete evaluation (<c>{$B+}</c>).</summary>
    And,
    Or,
    Xor,

    /// <summary>Boolean <c>and</c>/<c>or</c> with short-circuit evaluation (<c>{$B-}</c>, LANG-305).</summary>
    ConditionalAnd,
    ConditionalOr,
    Shl,
    Shr,
    Equal,
    NotEqual,
    Less,
    Greater,
    LessOrEqual,
    GreaterOrEqual,

    /// <summary><c>x in S</c> (LANG-252): the element keeps its own type, <see cref="BoundBinaryExpression.OperandType"/> is the set type.</summary>
    In,
}

/// <summary>
/// A binary operator. Both operands are converted to <see cref="OperandType"/>, the type the operation is done in
/// — except a comparison of a signed with an unsigned 64-bit operand (LANG-302), where the signed side is
/// <c>Int64</c> and the unsigned side <c>UInt64</c> and the comparison is mathematical. For a shift the count is
/// converted to the left operand's type. Set operators (LANG-252) have a set <see cref="OperandType"/>; for
/// <see cref="BoundBinaryOperatorKind.In"/> only the set operand has it.
/// </summary>
internal sealed class BoundBinaryExpression : BoundExpression
{
    public BoundBinaryExpression(SyntaxNode syntax, BoundBinaryOperatorKind operatorKind, BoundExpression left, BoundExpression right,
        TypeSymbol operandType, TypeSymbol type, ConstantValue? constantValue)
        : base(syntax, type, constantValue)
    {
        OperatorKind = operatorKind;
        Left = left;
        Right = right;
        OperandType = operandType;
    }

    public override BoundKind Kind => BoundKind.BinaryExpression;

    public BoundBinaryOperatorKind OperatorKind { get; }

    public BoundExpression Left { get; }

    public BoundExpression Right { get; }

    public TypeSymbol OperandType { get; }
}

/// <summary>
/// A set constructor <c>[a, b..c]</c> (LANG-251) of set type <see cref="BoundExpression.Type"/>. Element expressions keep
/// their own (ordinal) type: out-of-range elements contribute nothing (CMP-335) or fail the <c>{$R+}</c> check against the
/// element type, which the lowerer applies. A constructor of constants is a constant (a bit mask).
/// </summary>
internal sealed class BoundSetConstructor : BoundExpression
{
    public BoundSetConstructor(SyntaxNode syntax, ImmutableArray<BoundSetElement> elements, SetTypeSymbol type, ConstantValue? constantValue)
        : base(syntax, type, constantValue)
    {
        Elements = elements;
    }

    public override BoundKind Kind => BoundKind.SetConstructor;

    public ImmutableArray<BoundSetElement> Elements { get; }

    public SetTypeSymbol SetType => (SetTypeSymbol)Type;
}

/// <summary>
/// LANG-259 (C-067): <c>[e1, e2, …]</c> assigned to a dynamic array (variable, value/<c>const</c> parameter, <c>Result</c>):
/// a new array of <see cref="BoundExpression.Type"/> holding the elements, each converted to the element type. <c>[]</c> is
/// the constant <c>nil</c>.
/// </summary>
internal sealed class BoundArrayConstructor : BoundExpression
{
    public BoundArrayConstructor(SyntaxNode syntax, ImmutableArray<BoundExpression> elements, DynamicArrayTypeSymbol type)
        : base(syntax, type, elements.IsEmpty ? new ConstantValue(null, type) : null)
    {
        Elements = elements;
    }

    public override BoundKind Kind => BoundKind.ArrayConstructor;

    public ImmutableArray<BoundExpression> Elements { get; }
}

/// <summary>
/// <c>a[i]</c> of a static array (LANG-255; <c>a[i, j]</c> = <c>a[i][j]</c>) or <c>s[i]</c> of a UnicodeString (LANG-241,
/// <see cref="IsStringElement"/>, a <c>Char</c>, 1-based): a variable when <see cref="Array"/> is one. <see cref="Index"/> keeps
/// its own type (ordinal, compatible with the index type; an integer for strings); range checks are the lowerer's (CMP-331).
/// </summary>
internal sealed class BoundIndexExpression : BoundExpression
{
    public BoundIndexExpression(SyntaxNode syntax, BoundExpression array, BoundExpression index, TypeSymbol elementType)
        : base(syntax, elementType, constantValue: null)
    {
        Array = array;
        Index = index;
    }

    public override BoundKind Kind => BoundKind.IndexExpression;

    public BoundExpression Array { get; }

    public BoundExpression Index { get; }

    public ArrayTypeSymbol ArrayType => (ArrayTypeSymbol)Array.Type;

    public bool IsStringElement => Array.Type is StringTypeSymbol;

    /// <summary>The variable or parameter an element/field access or variable typecast chain starts from (the expression itself otherwise).</summary>
    public static BoundExpression RootOf(BoundExpression expression)
    {
        while (true)
        {
            switch (expression)
            {
                case BoundIndexExpression index:
                    expression = index.Array;
                    break;
                case BoundFieldAccessExpression field:
                    expression = field.Record;
                    break;
                case BoundVariableTypecastExpression typecast:
                    expression = typecast.Operand;
                    break;
                default:
                    return expression;
            }
        }
    }
}

/// <summary>
/// A variable typecast <c>T(v)</c> (LANG-290 last row): the storage of the variable reference <see cref="Operand"/> read or
/// written as type <see cref="BoundExpression.Type"/> of the same size; a variable when the operand is one.
/// </summary>
internal sealed class BoundVariableTypecastExpression : BoundExpression
{
    public BoundVariableTypecastExpression(SyntaxNode syntax, BoundExpression operand, TypeSymbol type)
        : base(syntax, type, constantValue: null)
    {
        Operand = operand;
    }

    public override BoundKind Kind => BoundKind.VariableTypecastExpression;

    public BoundExpression Operand { get; }
}

/// <summary><c>r.F</c> of a record (LANG-260): a variable when <see cref="Record"/> is one.</summary>
internal sealed class BoundFieldAccessExpression : BoundExpression
{
    public BoundFieldAccessExpression(SyntaxNode syntax, BoundExpression record, FieldSymbol field)
        : base(syntax, field.Type, constantValue: null)
    {
        Record = record;
        Field = field;
    }

    public override BoundKind Kind => BoundKind.FieldAccessExpression;

    public BoundExpression Record { get; }

    public FieldSymbol Field { get; }
}

/// <summary>
/// <c>@x</c> / <c>Addr(x)</c> (LANG-265): the address of the variable reference <see cref="Operand"/>, or the code address of
/// <see cref="Routine"/>; type <c>Pointer</c>. A typed constant may hold one (<see cref="AddressConstant"/>).
/// </summary>
internal sealed class BoundAddressOfExpression : BoundExpression
{
    public BoundAddressOfExpression(SyntaxNode syntax, BoundExpression? operand, RoutineSymbol? routine, TypeSymbol pointer)
        : base(syntax, pointer, constantValue: null)
    {
        Operand = operand;
        Routine = routine;
    }

    public override BoundKind Kind => BoundKind.AddressOfExpression;

    public BoundExpression? Operand { get; }

    public RoutineSymbol? Routine { get; }
}

/// <summary><c>p^</c> (LANG-265): the variable <see cref="Pointer"/> points to, of the pointed type (a byte for an untyped pointer in an untyped argument).</summary>
internal sealed class BoundDereferenceExpression : BoundExpression
{
    public BoundDereferenceExpression(SyntaxNode syntax, BoundExpression pointer, TypeSymbol type)
        : base(syntax, type, constantValue: null)
    {
        Pointer = pointer;
    }

    public override BoundKind Kind => BoundKind.DereferenceExpression;

    public BoundExpression Pointer { get; }
}

/// <summary>
/// LANG-247 pointer arithmetic (CMP-342): <see cref="Pointer"/> ± <see cref="Offset"/> elements (<see cref="Difference"/> false; the
/// pointer's type), or the element count <see cref="Pointer"/> − <see cref="Offset"/> of two pointers (<c>NativeInt</c>).
/// An integer offset is already converted to <c>NativeInt</c>.
/// </summary>
internal sealed class BoundPointerArithmeticExpression : BoundExpression
{
    public BoundPointerArithmeticExpression(SyntaxNode syntax, BoundExpression pointer, BoundExpression offset, bool subtract, bool difference, TypeSymbol type)
        : base(syntax, type, constantValue: null)
    {
        Pointer = pointer;
        Offset = offset;
        Subtract = subtract;
        Difference = difference;
    }

    public override BoundKind Kind => BoundKind.PointerArithmeticExpression;

    public BoundExpression Pointer { get; }

    public BoundExpression Offset { get; }

    public bool Subtract { get; }

    public bool Difference { get; }

    public PointerTypeSymbol PointerType => (PointerTypeSymbol)Pointer.Type;
}

/// <summary>The value of a typed constant <c>@X</c> (LANG-265, 344): the address of a global variable or routine.</summary>
internal sealed record AddressConstant(Symbol Target)
{
    public override string ToString() => "@" + Target.Name;
}

/// <summary>One element <c>a</c> (<see cref="High"/> null) or range <c>a..b</c> of a set constructor.</summary>
internal sealed record BoundSetElement(BoundExpression Low, BoundExpression? High);

/// <summary>A statement of a routine body, program block or initialization/finalization section.</summary>
internal abstract class BoundStatement : BoundNode
{
    private protected BoundStatement(SyntaxNode syntax)
        : base(syntax)
    {
    }
}

/// <summary>A statement list (<c>begin … end</c>, a body); empty statements are left out (LANG-353).</summary>
internal sealed class BoundBlock : BoundStatement
{
    public BoundBlock(SyntaxNode syntax, ImmutableArray<BoundStatement> statements)
        : base(syntax)
    {
        Statements = statements;
    }

    public override BoundKind Kind => BoundKind.Block;

    public ImmutableArray<BoundStatement> Statements { get; }
}

/// <summary>
/// <c>target := value</c> (LANG-350): <see cref="Value"/> is converted to the target's type (LANG-351), unless one
/// side has errors.
/// </summary>
internal sealed class BoundAssignmentStatement : BoundStatement
{
    public BoundAssignmentStatement(SyntaxNode syntax, BoundExpression target, BoundExpression value)
        : base(syntax)
    {
        Target = target;
        Value = value;
    }

    public override BoundKind Kind => BoundKind.AssignmentStatement;

    public BoundExpression Target { get; }

    public BoundExpression Value { get; }
}

/// <summary>A call used as a statement (LANG-352; the result, if any, is discarded, LANG-307).</summary>
internal sealed class BoundExpressionStatement : BoundStatement
{
    public BoundExpressionStatement(SyntaxNode syntax, BoundExpression expression)
        : base(syntax)
    {
        Expression = expression;
    }

    public override BoundKind Kind => BoundKind.ExpressionStatement;

    public BoundExpression Expression { get; }
}

/// <summary>A statement that could not be bound (already reported).</summary>
internal sealed class BoundErrorStatement : BoundStatement
{
    public BoundErrorStatement(SyntaxNode syntax)
        : base(syntax)
    {
    }

    public override BoundKind Kind => BoundKind.ErrorStatement;
}

/// <summary><c>if Condition then Then else Else</c> (LANG-354); the condition is converted to <c>Boolean</c>.</summary>
internal sealed class BoundIfStatement : BoundStatement
{
    public BoundIfStatement(SyntaxNode syntax, BoundExpression condition, BoundStatement then, BoundStatement? @else)
        : base(syntax)
    {
        Condition = condition;
        Then = then;
        Else = @else;
    }

    public override BoundKind Kind => BoundKind.IfStatement;

    public BoundExpression Condition { get; }

    public BoundStatement Then { get; }

    public BoundStatement? Else { get; }
}

/// <summary><c>while Condition do Body</c> (LANG-360).</summary>
internal sealed class BoundWhileStatement : BoundStatement
{
    public BoundWhileStatement(SyntaxNode syntax, BoundExpression condition, BoundStatement body)
        : base(syntax)
    {
        Condition = condition;
        Body = body;
    }

    public override BoundKind Kind => BoundKind.WhileStatement;

    public BoundExpression Condition { get; }

    public BoundStatement Body { get; }
}

/// <summary><c>repeat Body until Condition</c> (LANG-360); the loop ends when the condition is True.</summary>
internal sealed class BoundRepeatStatement : BoundStatement
{
    public BoundRepeatStatement(SyntaxNode syntax, BoundBlock body, BoundExpression condition)
        : base(syntax)
    {
        Body = body;
        Condition = condition;
    }

    public override BoundKind Kind => BoundKind.RepeatStatement;

    public BoundBlock Body { get; }

    public BoundExpression Condition { get; }
}

/// <summary>
/// <c>for Variable := Start to|downto Finish do Body</c> (LANG-361). <see cref="Start"/> and <see cref="Finish"/>
/// are converted to the variable's type; <see cref="Variable"/> is a <see cref="BoundVariableExpression"/> unless the
/// statement has errors.
/// </summary>
internal sealed class BoundForStatement : BoundStatement
{
    public BoundForStatement(SyntaxNode syntax, BoundExpression variable, BoundExpression start, BoundExpression finish, bool isDownTo,
        BoundStatement body)
        : base(syntax)
    {
        Variable = variable;
        Start = start;
        Finish = finish;
        IsDownTo = isDownTo;
        Body = body;
    }

    public override BoundKind Kind => BoundKind.ForStatement;

    public BoundExpression Variable { get; }

    public BoundExpression Start { get; }

    public BoundExpression Finish { get; }

    public bool IsDownTo { get; }

    public BoundStatement Body { get; }
}

/// <summary>
/// <c>case Selector of arms else Else end</c> (LANG-355…357). Labels of a valid statement are constants of the
/// selector's type, ranges have <c>Low ≤ High</c> and no two labels overlap.
/// </summary>
internal sealed class BoundCaseStatement : BoundStatement
{
    public BoundCaseStatement(SyntaxNode syntax, BoundExpression selector, ImmutableArray<BoundCaseArm> arms, BoundBlock? @else)
        : base(syntax)
    {
        Selector = selector;
        Arms = arms;
        Else = @else;
    }

    public override BoundKind Kind => BoundKind.CaseStatement;

    public BoundExpression Selector { get; }

    public ImmutableArray<BoundCaseArm> Arms { get; }

    /// <summary>The <c>else</c> statement list; null when there is none.</summary>
    public BoundBlock? Else { get; }
}

/// <summary>One arm of a case statement (not a statement itself).</summary>
internal sealed class BoundCaseArm
{
    public BoundCaseArm(SyntaxNode syntax, ImmutableArray<BoundCaseLabel> labels, BoundStatement body)
    {
        Syntax = syntax;
        Labels = labels;
        Body = body;
    }

    public SyntaxNode Syntax { get; }

    public ImmutableArray<BoundCaseLabel> Labels { get; }

    public BoundStatement Body { get; }
}

/// <summary>A case label <c>Low</c> or range <c>Low..High</c>.</summary>
internal sealed class BoundCaseLabel
{
    public BoundCaseLabel(SyntaxNode syntax, BoundExpression low, BoundExpression? high)
    {
        Syntax = syntax;
        Low = low;
        High = high;
    }

    public SyntaxNode Syntax { get; }

    public BoundExpression Low { get; }

    /// <summary>Null for a single value.</summary>
    public BoundExpression? High { get; }
}

/// <summary><c>Break</c>: leaves the innermost enclosing loop (LANG-363).</summary>
internal sealed class BoundBreakStatement : BoundStatement
{
    public BoundBreakStatement(SyntaxNode syntax)
        : base(syntax)
    {
    }

    public override BoundKind Kind => BoundKind.BreakStatement;
}

/// <summary><c>Continue</c>: next iteration of the innermost enclosing loop (LANG-363).</summary>
internal sealed class BoundContinueStatement : BoundStatement
{
    public BoundContinueStatement(SyntaxNode syntax)
        : base(syntax)
    {
    }

    public override BoundKind Kind => BoundKind.ContinueStatement;
}

/// <summary><c>Exit</c> / <c>Exit(Value)</c>: leaves the current routine or unit-level body (LANG-363).</summary>
internal sealed class BoundExitStatement : BoundStatement
{
    public BoundExitStatement(SyntaxNode syntax, BoundExpression? value)
        : base(syntax)
    {
        Value = value;
    }

    public override BoundKind Kind => BoundKind.ExitStatement;

    /// <summary>The function result assigned first (converted to the result type); null for a plain <c>Exit</c>.</summary>
    public BoundExpression? Value { get; }
}

/// <summary><c>Halt</c> / <c>Halt(ExitCode)</c>: ends the program after the finalization sections (LANG-364).</summary>
internal sealed class BoundHaltStatement : BoundStatement
{
    public BoundHaltStatement(SyntaxNode syntax, BoundExpression? exitCode)
        : base(syntax)
    {
        ExitCode = exitCode;
    }

    public override BoundKind Kind => BoundKind.HaltStatement;

    /// <summary>Converted to <c>Integer</c>; null means exit code 0.</summary>
    public BoundExpression? ExitCode { get; }
}

/// <summary>A reference to a parameter of the current (or, from C-028, an enclosing) routine.</summary>
internal sealed class BoundParameterExpression : BoundExpression
{
    public BoundParameterExpression(SyntaxNode syntax, ParameterSymbol parameter)
        : base(syntax, parameter.Type, constantValue: null)
    {
        Parameter = parameter;
    }

    public override BoundKind Kind => BoundKind.ParameterExpression;

    public ParameterSymbol Parameter { get; }
}

/// <summary>
/// A function call in an expression (LANG-420). <see cref="Arguments"/> are positional: value and <c>const</c>
/// arguments converted to the parameter type, <c>var</c>/<c>out</c> arguments the variable reference itself.
/// </summary>
internal sealed class BoundCallExpression : BoundExpression
{
    public BoundCallExpression(SyntaxNode syntax, RoutineSymbol routine, ImmutableArray<BoundExpression> arguments)
        : base(syntax, routine.ReturnType!, constantValue: null)
    {
        Routine = routine;
        Arguments = arguments;
    }

    public override BoundKind Kind => BoundKind.CallExpression;

    public RoutineSymbol Routine { get; }

    public ImmutableArray<BoundExpression> Arguments { get; }
}

/// <summary>A procedure call, or a function call whose result is discarded (LANG-307, 352); arguments as in <see cref="BoundCallExpression"/>.</summary>
internal sealed class BoundCallStatement : BoundStatement
{
    public BoundCallStatement(SyntaxNode syntax, RoutineSymbol routine, ImmutableArray<BoundExpression> arguments)
        : base(syntax)
    {
        Routine = routine;
        Arguments = arguments;
    }

    public override BoundKind Kind => BoundKind.CallStatement;

    public RoutineSymbol Routine { get; }

    public ImmutableArray<BoundExpression> Arguments { get; }
}

/// <summary>
/// A function-like intrinsic (intrinsics.md §1: <c>Succ</c> … <c>Swap</c>). Folded to <see cref="BoundExpression.ConstantValue"/>
/// when its argument is constant, and always for <c>Low</c>/<c>High</c>/<c>SizeOf</c> of a type or of an ordinal value
/// (<see cref="Arguments"/> is then empty for a type argument; a variable argument of <c>SizeOf</c> is never evaluated,
/// LANG-903). <c>Abs</c>/<c>Sqr</c> of an integer take their operand converted to its computation type (LANG-222).
/// </summary>
internal sealed class BoundIntrinsicCallExpression : BoundExpression
{
    public BoundIntrinsicCallExpression(SyntaxNode syntax, IntrinsicSymbol intrinsic, ImmutableArray<BoundExpression> arguments, TypeSymbol type, ConstantValue? constantValue)
        : base(syntax, type, constantValue)
    {
        Intrinsic = intrinsic;
        Arguments = arguments;
    }

    public override BoundKind Kind => BoundKind.IntrinsicCallExpression;

    public IntrinsicSymbol Intrinsic { get; }

    public ImmutableArray<BoundExpression> Arguments { get; }
}

/// <summary>
/// A procedure-like intrinsic: <c>Inc</c>/<c>Dec</c> (variable reference, optional integer step, unconverted) and
/// <c>RunError</c> (optional code converted to <c>Integer</c>; absent = 0).
/// </summary>
internal sealed class BoundIntrinsicCallStatement : BoundStatement
{
    public BoundIntrinsicCallStatement(SyntaxNode syntax, IntrinsicSymbol intrinsic, ImmutableArray<BoundExpression> arguments)
        : base(syntax)
    {
        Intrinsic = intrinsic;
        Arguments = arguments;
    }

    public override BoundKind Kind => BoundKind.IntrinsicCallStatement;

    public IntrinsicSymbol Intrinsic { get; }

    public ImmutableArray<BoundExpression> Arguments { get; }
}

/// <summary>
/// <c>Write</c>/<c>Writeln</c> (LANG-910…922): <see cref="File"/> is the <c>Text</c> variable of the first argument, null
/// for <c>Output</c>; arguments in source order.
/// </summary>
internal sealed class BoundWriteStatement : BoundStatement
{
    public BoundWriteStatement(SyntaxNode syntax, IntrinsicSymbol intrinsic, BoundExpression? file, ImmutableArray<BoundWriteArgument> arguments)
        : base(syntax)
    {
        Intrinsic = intrinsic;
        File = file;
        Arguments = arguments;
    }

    public override BoundKind Kind => BoundKind.WriteStatement;

    public IntrinsicSymbol Intrinsic { get; }

    public bool IsWriteln => Intrinsic.Intrinsic == IntrinsicKind.Writeln;

    public BoundExpression? File { get; }

    public ImmutableArray<BoundWriteArgument> Arguments { get; }
}

/// <summary>One <c>e[:w[:d]]</c> of a <c>Write</c>: the value unconverted, width and decimals converted to <c>Integer</c>.</summary>
internal sealed class BoundWriteArgument
{
    public BoundWriteArgument(ArgumentSyntax syntax, BoundExpression value, BoundExpression? width, BoundExpression? decimals)
    {
        Syntax = syntax;
        Value = value;
        Width = width;
        Decimals = decimals;
    }

    public ArgumentSyntax Syntax { get; }

    public BoundExpression Value { get; }

    public BoundExpression? Width { get; }

    public BoundExpression? Decimals { get; }
}
