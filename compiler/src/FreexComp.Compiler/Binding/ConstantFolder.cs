using System.Numerics;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

/// <summary>
/// Evaluates constant expressions (LANG-340…343). Integers fold in unbounded precision and are then typed as
/// literals are (LANG-126/341); reals fold in <see cref="double"/>. Returns null when the expression is not
/// constant or erroneous: the error is then already reported (syntax errors are the parser's, nothing more is said).
/// </summary>
internal sealed class ConstantFolder
{
    private readonly BuiltInTypes _types;
    private readonly Scope _scope;
    private readonly DiagnosticBag _diagnostics;

    public ConstantFolder(BuiltInTypes types, Scope scope, DiagnosticBag diagnostics)
    {
        _types = types;
        _scope = scope;
        _diagnostics = diagnostics;
    }

    public ConstantValue? Fold(ExpressionSyntax expression)
    {
        if (expression.IsMissing)
        {
            return null;
        }

        switch (expression)
        {
            case LiteralExpressionSyntax literal:
                return FoldLiteral(literal);
            case IdentifierNameSyntax name:
                return FoldName(name);
            case ParenthesizedExpressionSyntax parenthesized:
                return Fold(parenthesized.Expression);
            case UnaryExpressionSyntax unary:
                return FoldUnary(unary);
            case BinaryExpressionSyntax binary:
                return FoldBinary(binary);
            case InvocationExpressionSyntax invocation:
                return FoldInvocation(invocation);
            case SetConstructorSyntax:
                _diagnostics.Report(expression, "E2900", "set constant");
                return null;
            default:
                if (!expression.ContainsDiagnostics)
                {
                    _diagnostics.Report(expression, "E2050");
                }

                return null;
        }
    }

    // ------------------------------------------------------------------ factors

    internal ConstantValue? FoldLiteral(LiteralExpressionSyntax literal)
    {
        var token = literal.Token;
        switch (token.Kind)
        {
            case SyntaxKind.IntegerLiteralToken:
                var value = token.Value switch { ulong u => new BigInteger(u), long l => new BigInteger(l), _ => BigInteger.Zero };
                // LANG-126: a hex literal $80000000…$FFFFFFFF is Cardinal
                if (token.Text.StartsWith('$') && value >= 0x8000_0000L && value <= 0xFFFF_FFFFL)
                {
                    return new ConstantValue(value, _types.Cardinal);
                }

                return new ConstantValue(value, IntegerTypeFor(value)!);
            case SyntaxKind.RealLiteralToken:
                return new ConstantValue(token.Value is double d ? d : 0.0, _types.Double);
            case SyntaxKind.StringLiteralToken:
                // LANG-133: one code unit is a character literal
                var text = token.Value as string ?? "";
                return text.Length == 1 ? new ConstantValue(new BigInteger(text[0]), _types.Char) : new ConstantValue(text, _types.String);
            case SyntaxKind.NilKeyword:
                _diagnostics.Report(literal, "E2900", "nil constant");
                return null;
            default:
                return null;
        }
    }

    private ConstantValue? FoldName(IdentifierNameSyntax name)
    {
        var symbol = LookupName(name);
        switch (symbol)
        {
            case null:
                return null;
            case ConstantSymbol { IsTyped: false } constant:
                return constant.Value;
            case TypeSymbol type:
                _diagnostics.Report(name, "E2009", type.Name);
                return null;
            default:
                _diagnostics.Report(name, "E2050");
                return null;
        }
    }

    /// <summary>The symbol a name binds to; E2003 (and null) when undeclared, null for a parser-inserted name.</summary>
    private Symbol? LookupName(IdentifierNameSyntax name)
    {
        var token = name.Identifier;
        if (token.IsMissing)
        {
            return null;
        }

        var text = token.Value as string ?? token.Text;
        var symbol = _scope.Lookup(text);
        if (symbol is null)
        {
            _diagnostics.ReportUndeclared(name, text);
        }

        return symbol;
    }

    /// <summary>A typecast <c>T(v)</c> between ordinal types (LANG-340); calls of intrinsics fold from C-029 on.</summary>
    private ConstantValue? FoldInvocation(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not IdentifierNameSyntax name)
        {
            return NotConstant(invocation);
        }

        var symbol = LookupName(name);
        if (symbol is null)
        {
            return null;
        }

        var arguments = invocation.ArgumentList.Arguments;
        if (symbol is not TypeSymbol target || arguments.Count != 1 || arguments[0].WidthColonToken != null)
        {
            return NotConstant(invocation);
        }

        var operand = Fold(arguments[0].Expression);
        return operand is null ? null : FoldTypecast(invocation, target, operand);
    }

    /// <summary>An ordinal typecast of a constant (LANG-290 row 1); E2040 at <paramref name="node"/> otherwise.</summary>
    internal ConstantValue? FoldTypecast(SyntaxNode node, TypeSymbol target, ConstantValue operand)
    {
        if (OrdinalOf(operand) is not { } ordinal || !target.IsOrdinal)
        {
            _diagnostics.Report(node, "E2040", operand.Type.Name, target.Name);
            return null;
        }

        return target switch
        {
            IntegerTypeSymbol integer => new ConstantValue(Wrap(ordinal, integer.Bits, integer.IsSigned), target),
            CharTypeSymbol => new ConstantValue(Wrap(ordinal, (int)target.Size * 8, isSigned: false), target),
            _ => new ConstantValue(!ordinal.IsZero, target),
        };
    }

    private ConstantValue? NotConstant(ExpressionSyntax expression)
    {
        if (!expression.ContainsDiagnostics)
        {
            _diagnostics.Report(expression, "E2050");
        }

        return null;
    }

    // ------------------------------------------------------------------ operators

    private ConstantValue? FoldUnary(UnaryExpressionSyntax unary)
    {
        var operand = Fold(unary.Operand);
        return operand is null ? null : FoldUnary(unary.OperatorToken, operand);
    }

    /// <summary>A unary operator on a constant; diagnostics (E2011, E2051) at <paramref name="op"/>.</summary>
    internal ConstantValue? FoldUnary(SyntaxToken op, ConstantValue operand)
    {
        switch (op.Kind, operand.Value)
        {
            case (SyntaxKind.MinusToken, BigInteger i) when IsInteger(operand):
                return Integer(-i, op);
            case (SyntaxKind.PlusToken, BigInteger i) when IsInteger(operand):
                return Integer(i, op);
            case (SyntaxKind.MinusToken, double d):
                return new ConstantValue(-d, _types.Double);
            case (SyntaxKind.PlusToken, double d):
                return new ConstantValue(d, _types.Double);
            case (SyntaxKind.NotKeyword, bool b):
                return new ConstantValue(!b, operand.Type);
            case (SyntaxKind.NotKeyword, BigInteger i) when operand.Type is IntegerTypeSymbol type:
                // LANG-227: complement in the operand's own type for 8/16-bit types, else in its computation type
                var (bits, signed) = type.Bits < 32 ? (type.Bits, type.IsSigned) : ComputationType(type);
                return Integer(Wrap(~i, bits, signed), op);
            default:
                _diagnostics.Report(op, "E2011", op.Text, $"'{operand.Type.Name}'");
                return null;
        }
    }

    private ConstantValue? FoldBinary(BinaryExpressionSyntax binary)
    {
        var left = Fold(binary.Left);
        var right = Fold(binary.Right);
        return left is null || right is null ? null : FoldBinary(binary.OperatorToken, left, right);
    }

    /// <summary>A binary operator on constants; diagnostics (E2011, E2051, E2052) at <paramref name="op"/>.</summary>
    internal ConstantValue? FoldBinary(SyntaxToken op, ConstantValue left, ConstantValue right)
    {
        var result = op.Kind switch
        {
            SyntaxKind.PlusToken or SyntaxKind.MinusToken or SyntaxKind.AsteriskToken => Arithmetic(op, left, right),
            SyntaxKind.SlashToken => Divide(op, left, right),
            SyntaxKind.DivKeyword or SyntaxKind.ModKeyword => IntegerDivide(op, left, right),
            SyntaxKind.AndKeyword or SyntaxKind.OrKeyword or SyntaxKind.XorKeyword => Logical(op, left, right),
            SyntaxKind.ShlKeyword or SyntaxKind.ShrKeyword => Shift(op, left, right),
            SyntaxKind.EqualsToken or SyntaxKind.LessThanGreaterThanToken or SyntaxKind.LessThanToken
                or SyntaxKind.GreaterThanToken or SyntaxKind.LessThanEqualsToken or SyntaxKind.GreaterThanEqualsToken
                => Relational(op, left, right),
            _ => Inapplicable(op, left, right),
        };
        return result.Error ? null : result.Value ?? Inapplicable(op, left, right).Value;
    }

    /// <summary>Value = null and Error = false means "operands not applicable" (the caller reports E2011).</summary>
    private readonly record struct Folded(ConstantValue? Value, bool Error)
    {
        public static readonly Folded NotApplicable = new(null, false);
        public static readonly Folded Failed = new(null, true);

        public static implicit operator Folded(ConstantValue? value) => value is null ? Failed : new(value, false);
    }

    private Folded Arithmetic(SyntaxToken op, ConstantValue left, ConstantValue right)
    {
        if (left.Value is BigInteger l && right.Value is BigInteger r && IsInteger(left) && IsInteger(right))
        {
            return Integer(op.Kind switch { SyntaxKind.PlusToken => l + r, SyntaxKind.MinusToken => l - r, _ => l * r }, op);
        }

        if (RealOf(left) is { } a && RealOf(right) is { } b && (left.Value is double || right.Value is double))
        {
            return Real(op.Kind switch { SyntaxKind.PlusToken => a + b, SyntaxKind.MinusToken => a - b, _ => a * b }, op);
        }

        if (op.Kind == SyntaxKind.PlusToken && StringOf(left) is { } s && StringOf(right) is { } t)
        {
            return new ConstantValue(s + t, _types.String);
        }

        return Folded.NotApplicable;
    }

    private Folded Divide(SyntaxToken op, ConstantValue left, ConstantValue right)
    {
        if (RealOf(left) is not { } a || RealOf(right) is not { } b)
        {
            return Folded.NotApplicable;
        }

        if (b == 0)
        {
            _diagnostics.Report(op, "E2052");
            return Folded.Failed;
        }

        return Real(a / b, op);
    }

    private Folded IntegerDivide(SyntaxToken op, ConstantValue left, ConstantValue right)
    {
        if (left.Value is not BigInteger l || right.Value is not BigInteger r || !IsInteger(left) || !IsInteger(right))
        {
            return Folded.NotApplicable;
        }

        if (r.IsZero)
        {
            _diagnostics.Report(op, "E2052");
            return Folded.Failed;
        }

        // LANG-225: div truncates toward zero, mod has the sign of the dividend (BigInteger semantics)
        return Integer(op.Kind == SyntaxKind.DivKeyword ? BigInteger.Divide(l, r) : BigInteger.Remainder(l, r), op);
    }

    private Folded Logical(SyntaxToken op, ConstantValue left, ConstantValue right)
    {
        if (left.Value is bool a && right.Value is bool b)
        {
            return new ConstantValue(op.Kind switch { SyntaxKind.AndKeyword => a && b, SyntaxKind.OrKeyword => a || b, _ => a ^ b }, _types.Boolean);
        }

        if (left.Value is BigInteger l && right.Value is BigInteger r && IsInteger(left) && IsInteger(right))
        {
            return Integer(op.Kind switch { SyntaxKind.AndKeyword => l & r, SyntaxKind.OrKeyword => l | r, _ => l ^ r }, op);
        }

        return Folded.NotApplicable;
    }

    /// <summary>LANG-224: in the left operand's computation type, count modulo its width, <c>shr</c> zero-filling.</summary>
    private Folded Shift(SyntaxToken op, ConstantValue left, ConstantValue right)
    {
        if (left.Type is not IntegerTypeSymbol type || right.Value is not BigInteger r || !IsInteger(right))
        {
            return Folded.NotApplicable;
        }

        var (bits, signed) = ComputationType(type);
        var count = (int)(((r % bits) + bits) % bits);
        var pattern = Wrap((BigInteger)left.Value!, bits, isSigned: false);
        var shifted = op.Kind == SyntaxKind.ShlKeyword ? pattern << count : pattern >> count;
        return Integer(Wrap(shifted, bits, signed), op);
    }

    private Folded Relational(SyntaxToken op, ConstantValue left, ConstantValue right)
    {
        int comparison;
        if (left.Value is BigInteger l && right.Value is BigInteger r && IsInteger(left) == IsInteger(right)
            && (IsInteger(left) || left.Type == right.Type))
        {
            comparison = l.CompareTo(r);             // integers (LANG-302: mathematical values) or chars
        }
        else if (RealOf(left) is { } a && RealOf(right) is { } b)
        {
            comparison = a.CompareTo(b);
        }
        else if (left.Value is bool x && right.Value is bool y)
        {
            comparison = x.CompareTo(y);
        }
        else if (StringOf(left) is { } s && StringOf(right) is { } t)
        {
            comparison = string.CompareOrdinal(s, t);
        }
        else
        {
            return Folded.NotApplicable;
        }

        var result = op.Kind switch
        {
            SyntaxKind.EqualsToken => comparison == 0,
            SyntaxKind.LessThanGreaterThanToken => comparison != 0,
            SyntaxKind.LessThanToken => comparison < 0,
            SyntaxKind.GreaterThanToken => comparison > 0,
            SyntaxKind.LessThanEqualsToken => comparison <= 0,
            _ => comparison >= 0,
        };
        return new ConstantValue(result, _types.Boolean);
    }

    private Folded Inapplicable(SyntaxToken op, ConstantValue left, ConstantValue right)
    {
        _diagnostics.Report(op, "E2011", op.Text, $"'{left.Type.Name}' and '{right.Type.Name}'");
        return Folded.Failed;
    }

    // ------------------------------------------------------------------ values

    /// <summary>LANG-341: the first of Integer, Int64, UInt64 holding the value; E2051 outside both 64-bit ranges.</summary>
    private ConstantValue? Integer(BigInteger value, SyntaxToken op)
    {
        if (IntegerTypeFor(value) is { } type)
        {
            return new ConstantValue(value, type);
        }

        _diagnostics.Report(op, "E2051");
        return null;
    }

    private ConstantValue? Real(double value, SyntaxToken op)
    {
        if (double.IsFinite(value))
        {
            return new ConstantValue(value, _types.Double);
        }

        _diagnostics.Report(op, "E2051");
        return null;
    }

    private TypeSymbol? IntegerTypeFor(BigInteger value)
    {
        foreach (var type in (ReadOnlySpan<TypeSymbol>)[_types.Integer, _types.Int64, _types.UInt64])
        {
            var integer = (IntegerTypeSymbol)type;
            if (value >= integer.Min && value <= integer.Max)
            {
                return type;
            }
        }

        return null;
    }

    /// <summary>LANG-222 computation type: (bits, signed) of Integer, Cardinal, Int64 or UInt64.</summary>
    internal static (int Bits, bool Signed) ComputationType(IntegerTypeSymbol type) =>
        type.Bits < 32 || (type.Bits == 32 && type.IsSigned) ? (32, true) : (type.Bits, type.IsSigned);

    /// <summary>Two's complement truncation to <paramref name="bits"/>.</summary>
    internal static BigInteger Wrap(BigInteger value, int bits, bool isSigned)
    {
        var modulus = BigInteger.One << bits;
        var unsigned = ((value % modulus) + modulus) % modulus;
        return isSigned && unsigned >= modulus >> 1 ? unsigned - modulus : unsigned;
    }

    private static bool IsInteger(ConstantValue value) => value.Type is IntegerTypeSymbol;

    private static BigInteger? OrdinalOf(ConstantValue value) => value.Value switch
    {
        BigInteger i => i,
        bool b => b ? BigInteger.One : BigInteger.Zero,
        _ => null,
    };

    private static double? RealOf(ConstantValue value) => value.Value switch
    {
        double d => d,
        BigInteger i when IsInteger(value) => (double)i,
        _ => null,
    };

    /// <summary>Strings and characters (a character constant is usable as a string, LANG-343).</summary>
    private static string? StringOf(ConstantValue value) => value.Value switch
    {
        string s => s,
        BigInteger i when value.Type is CharTypeSymbol => ((char)(int)i).ToString(),
        _ => null,
    };
}
