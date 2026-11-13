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
}

/// <summary>
/// A binary operator. Both operands are converted to <see cref="OperandType"/>, the type the operation is done in
/// — except a comparison of a signed with an unsigned 64-bit operand (LANG-302), where the signed side is
/// <c>Int64</c> and the unsigned side <c>UInt64</c> and the comparison is mathematical. For a shift the count is
/// converted to the left operand's type.
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
