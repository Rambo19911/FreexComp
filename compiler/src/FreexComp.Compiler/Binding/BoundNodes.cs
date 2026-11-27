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
    ClassReferenceExpression,
    PropertyAccessExpression,
    TypeTestExpression,
    MethodPointerExpression,
    InterfaceGuidExpression,

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
    RaiseStatement,
    TryExceptStatement,
    TryFinallyStatement,
    WithStatement,
    LocalsStatement,
    LabelStatement,
    GotoStatement,
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

    /// <summary>The class property read through this <c>class var</c> accessor (LANG-647; not a variable, LANG-645).</summary>
    public PropertySymbol? Property { get; init; }
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
/// the constant <c>nil</c>. As an argument for a value/<c>const</c> open array parameter (C-106) its type is the
/// <see cref="OpenArrayTypeSymbol"/>: the elements in a caller temporary (never a constant, also when empty).
/// </summary>
internal sealed class BoundArrayConstructor : BoundExpression
{
    public BoundArrayConstructor(SyntaxNode syntax, ImmutableArray<BoundExpression> elements, TypeSymbol type)
        : base(syntax, type, elements.IsEmpty && type is DynamicArrayTypeSymbol ? new ConstantValue(null, type) : null)
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
                case BoundFieldAccessExpression { Record.Type: ClassTypeSymbol } classField:
                    return classField;                                  // an instance field lives behind the reference, like p^.F (C-080)
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

    /// <summary>The property read through this field accessor (LANG-641/645: not a variable for <c>var</c>, <c>@</c>, <c>Inc</c>).</summary>
    public PropertySymbol? Property { get; init; }
}

/// <summary>
/// <c>o is T</c> (Boolean; <c>_IsClass</c>, or <c>_ClassInherits</c> when <see cref="Operand"/> is a class reference) and
/// <c>o as T</c> (typed <c>T</c>; <c>_AsClass</c>) for a class <see cref="Target"/> (LANG-650/651, CMP-367, ABI §9.6); for an
/// interface <see cref="Target"/> (C-100, CMP-373) <c>o is I</c> = <c>_ObjSupports</c>, <c>o as I</c> = <c>_ObjToIntf</c>,
/// <c>i as I</c> = <c>_IntfCast</c> (a managed value written into its destination).
/// </summary>
internal sealed class BoundTypeTestExpression : BoundExpression
{
    public BoundTypeTestExpression(SyntaxNode syntax, BoundExpression operand, TypeSymbol target, bool isAs, TypeSymbol type)
        : base(syntax, type, constantValue: null)
    {
        Operand = operand;
        Target = target;
        IsAs = isAs;
    }

    public override BoundKind Kind => BoundKind.TypeTestExpression;

    public BoundExpression Operand { get; }

    /// <summary>The class or interface tested for.</summary>
    public TypeSymbol Target { get; }

    public ClassTypeSymbol Class => (ClassTypeSymbol)Target;

    public InterfaceTypeSymbol? Interface => Target as InterfaceTypeSymbol;

    public bool IsAs { get; }
}

/// <summary>
/// A property as the whole target of an assignment (LANG-641): the receiver and converted index arguments, before the write is
/// resolved to a field store or a setter call. Produced and consumed by the body binder only.
/// </summary>
internal sealed class BoundPropertyAccessExpression : BoundExpression
{
    public BoundPropertyAccessExpression(SyntaxNode syntax, PropertySymbol property, BoundExpression? self, ImmutableArray<BoundExpression> indexes)
        : base(syntax, property.Type, constantValue: null)
    {
        Property = property;
        Self = self;
        Indexes = indexes;
    }

    public override BoundKind Kind => BoundKind.PropertyAccessExpression;

    public PropertySymbol Property { get; }

    /// <summary>The instance, the class reference of a class property; null for static accessors.</summary>
    public BoundExpression? Self { get; }

    public ImmutableArray<BoundExpression> Indexes { get; }

    /// <summary>Named through <c>inherited</c> (LANG-624, C-270): the setter is called statically.</summary>
    public bool IsInherited { get; init; }
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

    /// <summary>The closure of a unit-level body with anonymous methods (CMP-380, C-120); null otherwise.</summary>
    public ClosureInfo? Closure { get; init; }
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
    public BoundErrorStatement(SyntaxNode syntax, BoundExpression? expression = null)
        : base(syntax)
    {
        Expression = expression;
    }

    /// <summary>C-401: what was bound of the statement's expression, for the language service only (flow analysis and code ignore it).</summary>
    public BoundExpression? Expression { get; }

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

/// <summary>A label of a body's <c>label</c> section (LANG-380); the identity of the jump target through lowering and emission.</summary>
internal sealed class GotoLabel(Symbols.LabelSymbol symbol)
{
    public string Name => Symbol.Name;

    /// <summary>The label as a symbol of the semantic model (C-318).</summary>
    public Symbols.LabelSymbol Symbol { get; } = symbol;
}

/// <summary><c>Label: Statement</c> (LANG-380): the definition of <see cref="Label"/>.</summary>
internal sealed class BoundLabelStatement : BoundStatement
{
    public BoundLabelStatement(SyntaxNode syntax, GotoLabel label, BoundStatement statement)
        : base(syntax)
    {
        Label = label;
        Statement = statement;
    }

    public override BoundKind Kind => BoundKind.LabelStatement;

    public GotoLabel Label { get; }

    public BoundStatement Statement { get; }
}

/// <summary><c>goto Label</c> (LANG-380, LANG-571): the label is declared in the same body.</summary>
internal sealed class BoundGotoStatement : BoundStatement
{
    public BoundGotoStatement(SyntaxNode syntax, GotoLabel label)
        : base(syntax)
    {
        Label = label;
    }

    public override BoundKind Kind => BoundKind.GotoStatement;

    public GotoLabel Label { get; }
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

/// <summary>
/// <c>raise Exception [at Address]</c> (LANG-805, 806): <see cref="Exception"/> is of a class type (or <c>nil</c>),
/// <see cref="Address"/> converted to <c>Pointer</c>; <c>raise;</c> (LANG-807) has neither.
/// </summary>
internal sealed class BoundRaiseStatement : BoundStatement
{
    public BoundRaiseStatement(SyntaxNode syntax, BoundExpression? exception, BoundExpression? address)
        : base(syntax)
    {
        Exception = exception;
        Address = address;
    }

    public override BoundKind Kind => BoundKind.RaiseStatement;

    public BoundExpression? Exception { get; }

    public BoundExpression? Address { get; }

    /// <summary><c>raise;</c>: re-raises the exception of the innermost handler.</summary>
    public bool IsReraise => Exception is null;
}

/// <summary><c>on [Variable:] Type do Body</c> (LANG-821); <see cref="Variable"/> is a read-only local scoped to <see cref="Body"/>.</summary>
internal sealed class BoundExceptionHandler
{
    public BoundExceptionHandler(SyntaxNode syntax, VariableSymbol? variable, ClassTypeSymbol type, BoundStatement body)
    {
        Syntax = syntax;
        Variable = variable;
        Type = type;
        Body = body;
    }

    public SyntaxNode Syntax { get; }

    public VariableSymbol? Variable { get; }

    public ClassTypeSymbol Type { get; }

    public BoundStatement Body { get; }
}

/// <summary>
/// <c>try Body except handlers else Else end</c> (LANG-820, 821). The plain form has no <see cref="Handlers"/> and its
/// except block as <see cref="Else"/> (it runs for any exception). No handler matching and no <see cref="Else"/>: the
/// exception propagates.
/// </summary>
internal sealed class BoundTryExceptStatement : BoundStatement
{
    public BoundTryExceptStatement(SyntaxNode syntax, BoundBlock body, ImmutableArray<BoundExceptionHandler> handlers, BoundBlock? @else)
        : base(syntax)
    {
        Body = body;
        Handlers = handlers;
        Else = @else;
    }

    public override BoundKind Kind => BoundKind.TryExceptStatement;

    public BoundBlock Body { get; }

    public ImmutableArray<BoundExceptionHandler> Handlers { get; }

    public BoundBlock? Else { get; }
}

/// <summary><c>try Body finally Finally end</c> (LANG-810).</summary>
internal sealed class BoundTryFinallyStatement : BoundStatement
{
    public BoundTryFinallyStatement(SyntaxNode syntax, BoundBlock body, BoundBlock @finally)
        : base(syntax)
    {
        Body = body;
        Finally = @finally;
    }

    public override BoundKind Kind => BoundKind.TryFinallyStatement;

    public BoundBlock Body { get; }

    public BoundBlock Finally { get; }
}

/// <summary>
/// LANG-375 (C-109): one designator of <c>with d do s</c>. <see cref="Value"/> (the address of an addressable record, else the
/// record value or the object reference) is stored once in the hidden local <see cref="Variable"/> (<c>$withN</c>); the members
/// named in <see cref="Body"/> are bound through it.
/// </summary>
internal sealed class BoundWithStatement : BoundStatement
{
    public BoundWithStatement(SyntaxNode syntax, VariableSymbol variable, BoundExpression value, BoundStatement body)
        : base(syntax)
    {
        Variable = variable;
        Value = value;
        Body = body;
    }

    public override BoundKind Kind => BoundKind.WithStatement;

    public VariableSymbol Variable { get; }

    public BoundExpression Value { get; }

    public BoundStatement Body { get; }
}

/// <summary>What the locals of a <see cref="BoundLocalsStatement"/> are (inline-variables.md LANG-560…565).</summary>
internal enum LocalsScopeKind
{
    /// <summary>Compiler temporaries of a statement (the collection copy and index of a <c>for…in</c> loop): routine lifetime.</summary>
    Hidden,

    /// <summary>An inline <c>var</c>/<c>const</c> of a statement list: its scope is the rest of that list (LANG-560).</summary>
    List,

    /// <summary>The variable of a <c>for var</c> loop or the hidden enumerator of a <c>for…in</c> loop: its scope is the loop (LANG-564).</summary>
    Loop,
}

/// <summary>
/// Locals declared by a statement (<see cref="Locals"/>) and used by <see cref="Body"/>: the hidden collection and index of a
/// <c>for…in</c> loop (LANG-362, C-111, in no scope), an inline declaration with the rest of its statement list (LANG-385), a
/// <c>for var</c> variable or an enumerator with its loop. <see cref="ScopeKind"/> decides when managed ones are finalised (C-306).
/// </summary>
internal sealed class BoundLocalsStatement : BoundStatement
{
    public BoundLocalsStatement(SyntaxNode syntax, ImmutableArray<VariableSymbol> locals, BoundStatement body, LocalsScopeKind scopeKind = LocalsScopeKind.Hidden)
        : base(syntax)
    {
        Locals = locals;
        Body = body;
        ScopeKind = scopeKind;
    }

    public override BoundKind Kind => BoundKind.LocalsStatement;

    public ImmutableArray<VariableSymbol> Locals { get; }

    public LocalsScopeKind ScopeKind { get; }

    public BoundStatement Body { get; }

    /// <summary>C-308: the declaration of a statement list as the binder noted it (its block frame, CMP-1183); null for other locals.</summary>
    public InlineScope? Inline { get; init; }

    /// <summary>The scope that declares the names of an inline declaration or a <c>for var</c> loop (LANG-560); null for hidden locals.</summary>
    public Scope? DeclarationScope { get; init; }

    /// <summary>The source positions (both inclusive) at which <see cref="DeclarationScope"/> is the innermost scope (LANG-572).</summary>
    public (int Start, int End) ScopeRange { get; init; }
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
    public BoundCallExpression(SyntaxNode syntax, RoutineSymbol routine, ImmutableArray<BoundExpression> arguments, BoundExpression? target = null,
        TypeSymbol? type = null)
        : base(syntax, type ?? routine.ReturnType!, constantValue: null)
    {
        Routine = routine;
        Arguments = arguments;
        Target = target;
    }

    public override BoundKind Kind => BoundKind.CallExpression;

    /// <summary>The callee; for a call through a procedural value the <see cref="ProceduralTypeSymbol.Signature"/> of its type.</summary>
    public RoutineSymbol Routine { get; }

    public ImmutableArray<BoundExpression> Arguments { get; }

    /// <summary>The procedural value called (LANG-484, evaluated before the arguments); null for a direct call.</summary>
    public BoundExpression? Target { get; }

    /// <summary><c>Self</c> and dispatch of a method call (CMP-367); null for a routine.</summary>
    public BoundReceiver? Receiver { get; init; }
}

/// <summary>
/// The receiver of a method call (CMP-367, ABI-075/076/084): <see cref="Self"/> (evaluated before the arguments), whether the
/// callee is taken from the VMT slot of <see cref="Self"/>'s class (<see cref="IsVirtual"/>), and the constructor/destructor
/// flag (<c>Alloc</c> 1 for a call on a class name, 0 for <c>inherited</c> and calls on an instance; <c>Dealloc</c> 1 for
/// <c>Obj.Destroy</c>, 0 for <c>inherited Destroy</c>).
/// </summary>
internal sealed record BoundReceiver(BoundExpression? Self, bool IsVirtual, int? Flag)
{
    /// <summary>A <c>class … static</c> method (LANG-620): no <c>Self</c>, a direct call.</summary>
    public static readonly BoundReceiver Static = new(null, IsVirtual: false, Flag: null);
}

/// <summary>A class name as a constructor's <c>Self</c>: the address of its VMT (ABI-071), type <c>Pointer</c>.</summary>
internal sealed class BoundClassReferenceExpression : BoundExpression
{
    public BoundClassReferenceExpression(SyntaxNode syntax, ClassTypeSymbol type, TypeSymbol pointer)
        : base(syntax, pointer, constantValue: null)
    {
        Class = type;
    }

    public override BoundKind Kind => BoundKind.ClassReferenceExpression;

    public ClassTypeSymbol Class { get; }
}

/// <summary>
/// LANG-680 (C-299): an interface type identifier where a <c>TGUID</c> is required — the GUID constant of <see cref="Interface"/>
/// (CMP-373 <c>@"U.I.$guid"</c>), a read-only value of type <c>TGUID</c>.
/// </summary>
internal sealed class BoundInterfaceGuidExpression : BoundExpression
{
    public BoundInterfaceGuidExpression(SyntaxNode syntax, InterfaceTypeSymbol intf, TypeSymbol guidType)
        : base(syntax, guidType, constantValue: null)
    {
        Interface = intf;
    }

    public override BoundKind Kind => BoundKind.InterfaceGuidExpression;

    public InterfaceTypeSymbol Interface { get; }
}

/// <summary>
/// LANG-481 / CMP-378 (C-103): the method pointer <c>{ Code, Data }</c> of <see cref="Method"/> on <see cref="Self"/> (an instance,
/// or a class reference for a class method): Code is the method's address, or the VMT slot read from Self when
/// <see cref="IsVirtual"/>.
/// </summary>
internal sealed class BoundMethodPointerExpression : BoundExpression
{
    public BoundMethodPointerExpression(SyntaxNode syntax, MethodSymbol method, BoundExpression self, bool isVirtual, TypeSymbol type)
        : base(syntax, type, constantValue: null)
    {
        Method = method;
        Self = self;
        IsVirtual = isVirtual;
    }

    public override BoundKind Kind => BoundKind.MethodPointerExpression;

    public MethodSymbol Method { get; }

    public BoundExpression Self { get; }

    public bool IsVirtual { get; }
}

/// <summary>A procedure call, or a function call whose result is discarded (LANG-307, 352); arguments as in <see cref="BoundCallExpression"/>.</summary>
internal sealed class BoundCallStatement : BoundStatement
{
    public BoundCallStatement(SyntaxNode syntax, RoutineSymbol routine, ImmutableArray<BoundExpression> arguments, BoundExpression? target = null)
        : base(syntax)
    {
        Routine = routine;
        Arguments = arguments;
        Target = target;
    }

    public override BoundKind Kind => BoundKind.CallStatement;

    public RoutineSymbol Routine { get; }

    public ImmutableArray<BoundExpression> Arguments { get; }

    /// <summary>As <see cref="BoundCallExpression.Target"/>.</summary>
    public BoundExpression? Target { get; }

    /// <summary>As <see cref="BoundCallExpression.Receiver"/>.</summary>
    public BoundReceiver? Receiver { get; init; }
}

/// <summary>
/// A function-like intrinsic (intrinsics.md §1: <c>Succ</c> … <c>Swap</c>). Folded to <see cref="BoundExpression.ConstantValue"/>
/// when its argument is constant, and always for <c>Low</c>/<c>High</c>/<c>SizeOf</c> of a type or of an ordinal value
/// (<see cref="Arguments"/> is then empty for a type argument; a variable argument of <c>SizeOf</c> is never evaluated,
/// LANG-903). <c>Abs</c>/<c>Sqr</c> of an integer take their operand converted to its computation type (LANG-222).
/// </summary>
internal sealed class BoundIntrinsicCallExpression : BoundExpression
{
    public BoundIntrinsicCallExpression(SyntaxNode syntax, IntrinsicSymbol intrinsic, ImmutableArray<BoundExpression> arguments, TypeSymbol type, ConstantValue? constantValue,
        TypeSymbol? typeOperand = null)
        : base(syntax, type, constantValue)
    {
        Intrinsic = intrinsic;
        Arguments = arguments;
        TypeOperand = typeOperand;
    }

    /// <summary>The type operand of <c>TypeInfo(T)</c> (C-112); null for the other intrinsics.</summary>
    public TypeSymbol? TypeOperand { get; }

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
