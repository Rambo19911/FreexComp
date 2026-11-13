using System.Collections.Immutable;
using System.Numerics;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

/// <summary>
/// Binds expressions of routine bodies and initialisers to typed <see cref="BoundExpression"/>s (integer, Boolean,
/// char and real operands: LANG-220…230, LANG-300…306). Operands are wrapped in implicit conversions to the type the
/// operation is done in; constant sub-expressions are folded by the <see cref="ConstantFolder"/> (LANG-340). An
/// operand of the error type gives the error type without further diagnostics (CMP-135).
/// </summary>
internal sealed class ExpressionBinder
{
    private readonly BuiltInTypes _types;
    private readonly Scope _scope;
    private readonly DiagnosticBag _diagnostics;
    private readonly ConstantFolder _folder;
    private readonly Conversions _conversions = new();
    private readonly RoutineSymbol? _routine;

    /// <param name="routine">The routine whose body is bound; null at unit level.</param>
    public ExpressionBinder(BuiltInTypes types, Scope scope, DiagnosticBag diagnostics, RoutineSymbol? routine = null)
    {
        _types = types;
        _scope = scope;
        _diagnostics = diagnostics;
        _routine = routine;
        _folder = new ConstantFolder(types, scope, diagnostics);
    }

    /// <summary>Control variables (variables or value parameters) of the enclosing <c>for</c> loops (LANG-361 rule 6).</summary>
    public HashSet<Symbol> ForVariables { get; } = new(ReferenceEqualityComparer.Instance);

    public BoundExpression BindExpression(ExpressionSyntax syntax)
    {
        if (syntax.IsMissing)
        {
            return Error(syntax);
        }

        switch (syntax)
        {
            case LiteralExpressionSyntax literal:
                return BindLiteral(literal);
            case IdentifierNameSyntax name:
                return BindName(name);
            case ParenthesizedExpressionSyntax parenthesized:
                return BindExpression(parenthesized.Expression);
            case UnaryExpressionSyntax unary:
                return BindUnary(unary);
            case BinaryExpressionSyntax binary:
                return BindBinary(binary);
            case InvocationExpressionSyntax invocation:
                return BindInvocation(invocation);
            case PredefinedTypeExpressionSyntax predefined:
                _diagnostics.Report(predefined, "E2009", predefined.Keyword.Text);
                return Error(syntax);
            default:
                return Unsupported(syntax, DeclarationBinder.FeatureName(syntax.Kind));
        }
    }

    /// <summary>
    /// A condition of <c>if</c>/<c>while</c>/<c>repeat</c> (LANG-354/360): Boolean family, converted to <c>Boolean</c>
    /// (LANG-215); E2061 otherwise.
    /// </summary>
    public BoundExpression BindCondition(ExpressionSyntax syntax)
    {
        var condition = BindExpression(syntax);
        if (condition.HasErrors)
        {
            return condition;
        }

        if (condition.Type is not BooleanTypeSymbol)
        {
            _diagnostics.Report(syntax, "E2061", condition.Type.Name);
            return Error(syntax, condition);
        }

        return Convert(condition, _types.Boolean);
    }

    /// <summary>
    /// <paramref name="value"/> converted for assignment to <paramref name="destination"/> (LANG-280/351); E2010 / E2012
    /// at <paramref name="at"/> and an error expression when it is not assignment-compatible.
    /// </summary>
    public BoundExpression ConvertForAssignment(BoundExpression value, TypeSymbol destination, SyntaxNode at)
    {
        if (value.HasErrors || destination.TypeKind == TypeKind.Error || ReferenceEquals(value.Type, destination))
        {
            return value;
        }

        if (value.ConstantValue is { } constant)
        {
            if (_conversions.ConvertConstant(constant, destination, at, _diagnostics) is not { } converted)
            {
                return Error(at, value);
            }

            // a character constant converts to AnsiChar although Char does not (LANG-262)
            var kind = _conversions.Classify(value.Type, destination).Kind;
            return new BoundConversionExpression(value.Syntax, value, destination,
                kind == ConversionKind.None ? ConversionKind.ImplicitNumeric : kind, isExplicit: false, converted);
        }

        var conversion = _conversions.Classify(value.Type, destination);
        if (!conversion.Exists)
        {
            _conversions.ReportIncompatible(value.Type, destination, at, _diagnostics);
            return Error(at, value);
        }

        return new BoundConversionExpression(value.Syntax, value, destination, conversion.Kind, isExplicit: false, constantValue: null);
    }

    // ------------------------------------------------------------------ factors

    private BoundExpression BindLiteral(LiteralExpressionSyntax literal)
    {
        if (literal.Token.Kind == SyntaxKind.NilKeyword)
        {
            return Unsupported(literal, "nil");                         // pointers arrive with M2.2
        }

        return _folder.FoldLiteral(literal) is { } value ? new BoundLiteral(literal, value) : Error(literal);
    }

    private BoundExpression BindName(IdentifierNameSyntax name)
    {
        switch (LookupName(name))
        {
            case null:
                return Error(name);
            case VariableSymbol variable:
                return new BoundVariableExpression(name, variable);
            case ParameterSymbol parameter:
                return new BoundParameterExpression(name, parameter);
            case RoutineSymbol:
                return CallExpression(name, name, argumentList: null);             // LANG-312: parameterless call
            case ConstantSymbol { IsTyped: false, Value: { } value } constant:
                return new BoundConstantExpression(name, constant, value);
            case ConstantSymbol:
                return Error(name);                                     // its declaration already reported the error
            case TypeSymbol type:
                _diagnostics.Report(name, "E2009", type.Name);
                return Error(name);
            case IntrinsicSymbol:
                _diagnostics.Report(name, "E2034");                     // the control intrinsics have no value (LANG-363/364)
                return Error(name);
            case var symbol:
                return Unsupported(name, symbol.Kind.ToString().ToLowerInvariant() + " reference");
        }
    }

    /// <summary>The symbol a name binds to (LANG-310); E2003 and null when undeclared, null for a parser-inserted name.</summary>
    private Symbol? LookupName(IdentifierNameSyntax name)
    {
        var token = name.Identifier;
        if (token.IsMissing)
        {
            return null;
        }

        var text = token.Value as string ?? token.Text;
        var symbol = _scope.Lookup(text);
        if (symbol is null && _routine is { ReturnType: null } && IdentifierComparer.Instance.Equals(text, "Result"))
        {
            _diagnostics.Report(name, "E2132");
        }
        else if (symbol is null)
        {
            _diagnostics.ReportUndeclared(name, text);
        }

        return symbol;
    }

    /// <summary>A typecast <c>T(x)</c> or a function call; intrinsics are bound from C-029 on.</summary>
    private BoundExpression BindInvocation(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not IdentifierNameSyntax name)
        {
            return Unsupported(invocation, "routine call");
        }

        var symbol = LookupName(name);
        if (symbol is null)
        {
            return Error(invocation);
        }

        var arguments = invocation.ArgumentList.Arguments;
        if (symbol is IntrinsicSymbol)
        {
            _diagnostics.Report(invocation, "E2034");
            return Error(invocation);
        }

        if (symbol is RoutineSymbol)
        {
            return CallExpression(invocation, name, invocation.ArgumentList);
        }

        if (symbol is not TypeSymbol target)
        {
            return Unsupported(invocation, "routine call");
        }

        if (arguments.Count != 1)
        {
            return Unsupported(invocation, "typecast");
        }

        if (arguments[0].WidthColonToken is { } colon)
        {
            _diagnostics.Report(colon, "E2915");
            return Error(invocation);
        }

        var operand = BindExpression(arguments[0].Expression);
        return operand.HasErrors || target.TypeKind == TypeKind.Error ? Error(invocation, operand) : BindTypecast(invocation, target, operand);
    }

    /// <summary>LANG-290: ordinal → ordinal and integer → real; real → anything is E2040; the rest arrives with C-057.</summary>
    private BoundExpression BindTypecast(InvocationExpressionSyntax node, TypeSymbol target, BoundExpression operand)
    {
        var source = operand.Type;
        if (source is RealTypeSymbol)
        {
            _diagnostics.Report(node, "E2040", source.Name, target.Name);
            return Error(node, operand);
        }

        ConstantValue? constant;
        if (source.IsOrdinal && target.IsOrdinal)
        {
            constant = operand.ConstantValue is { } value ? _folder.FoldTypecast(node, target, value) : null;
        }
        else if (source is IntegerTypeSymbol && target is RealTypeSymbol)
        {
            constant = null;                                            // not a constant expression (LANG-340, lead decision C-024)
        }
        else
        {
            return Unsupported(node, "typecast");
        }

        return new BoundConversionExpression(node, operand, target, ConversionKind.Explicit, isExplicit: true, constant);
    }

    // ------------------------------------------------------------------ calls (LANG-420, 421, 441)

    /// <summary>A call in an expression: functions only (E2034 for a procedure).</summary>
    private BoundExpression CallExpression(SyntaxNode call, IdentifierNameSyntax name, ArgumentListSyntax? argumentList)
    {
        var (routine, arguments) = BindCall(call, name, argumentList);
        if (routine is null)
        {
            return Error(call, [.. arguments]);
        }

        if (routine.ReturnType is null)
        {
            _diagnostics.Report(call, "E2034");
            return Error(call, [.. arguments]);
        }

        return new BoundCallExpression(call, routine, arguments);
    }

    /// <summary>
    /// Binds the arguments of a call of the routine(s) <paramref name="name"/> denotes and picks the routine: the only
    /// candidate, or the only applicable one of an overload set (LANG-441; ranking by conversion cost, LANG-442, arrives
    /// with M2.5). Routine is null after an error (reported once); the bound arguments are returned either way.
    /// </summary>
    public (RoutineSymbol? Routine, ImmutableArray<BoundExpression> Arguments) BindCall(SyntaxNode call, IdentifierNameSyntax name, ArgumentListSyntax? argumentList)
    {
        var token = name.Identifier;
        var candidates = _scope.LookupCandidates(token.Value as string ?? token.Text);
        var syntaxes = argumentList?.Arguments ?? default;
        var builder = ImmutableArray.CreateBuilder<BoundExpression>(syntaxes.Count);
        var hasErrors = false;
        foreach (var argument in syntaxes)
        {
            if (argument.WidthColonToken is { } colon)
            {
                _diagnostics.Report(colon, "E2915");
                hasErrors = true;
            }

            var bound = BindExpression(argument.Expression);
            hasErrors |= bound.HasErrors;
            builder.Add(bound);
        }

        var arguments = builder.ToImmutable();
        if (hasErrors || candidates.IsEmpty)
        {
            return (null, arguments);
        }

        var routine = candidates[0];
        if (candidates.Length > 1)
        {
            var applicable = candidates.Where(c => IsApplicable(c, arguments)).ToList();
            if (applicable.Count != 1)
            {
                if (applicable.Count == 0)
                {
                    _diagnostics.Report(call, "E2123", routine.Name);
                }
                else
                {
                    _diagnostics.Report(call, "E2900", "overload resolution between compatible candidates");   // LANG-442, M2.5
                }

                return (null, arguments);
            }

            routine = applicable[0];
        }

        return CheckArguments(call, routine, syntaxes, arguments);
    }

    /// <summary>LANG-420/421: count, then each argument against its parameter.</summary>
    private (RoutineSymbol? Routine, ImmutableArray<BoundExpression> Arguments) CheckArguments(SyntaxNode call, RoutineSymbol routine,
        SeparatedSyntaxList<ArgumentSyntax> syntaxes, ImmutableArray<BoundExpression> arguments)
    {
        var parameters = routine.Parameters;
        if (arguments.Length < parameters.Length)
        {
            _diagnostics.Report(call, "E2110", routine.Name);
            return (null, arguments);
        }

        if (arguments.Length > parameters.Length)
        {
            _diagnostics.Report(syntaxes[parameters.Length], "E2111", routine.Name);
            return (null, arguments);
        }

        var converted = ImmutableArray.CreateBuilder<BoundExpression>(arguments.Length);
        var ok = true;
        for (var i = 0; i < arguments.Length; i++)
        {
            var (parameter, argument, syntax) = (parameters[i], arguments[i], syntaxes[i].Expression);
            if (parameter.IsByReference)
            {
                ok &= CheckReferenceArgument(argument, parameter, syntax);
                converted.Add(argument);
            }
            else
            {
                var value = ConvertForAssignment(argument, parameter.Type, syntax);
                ok &= !value.HasErrors;
                converted.Add(value);
            }
        }

        return (ok ? routine : null, converted.ToImmutable());
    }

    /// <summary>LANG-421 / LANG-281: an assignable variable reference of the identical type.</summary>
    private bool CheckReferenceArgument(BoundExpression argument, ParameterSymbol parameter, ExpressionSyntax syntax)
    {
        if (parameter.Type.TypeKind == TypeKind.Error)
        {
            return true;
        }

        switch (argument)
        {
            case BoundVariableExpression { Variable: var v } when ForVariables.Contains(v):
            case BoundParameterExpression { Parameter: var p } when ForVariables.Contains(p):
                _diagnostics.Report(syntax, "E2053");                   // LANG-361 rule 6
                return false;
            case BoundVariableExpression:
            case BoundParameterExpression { Parameter.Mode: not ParameterMode.Const }:
                break;
            case BoundParameterExpression:
            case { ConstantValue: not null }:
                _diagnostics.Report(syntax, "E2112");
                return false;
            default:
                _diagnostics.Report(syntax, "E2113");
                return false;
        }

        if (!ReferenceEquals(argument.Type, parameter.Type))
        {
            _diagnostics.Report(syntax, "E2010", argument.Type.Name, parameter.Type.Name);
            return false;
        }

        return true;
    }

    /// <summary>Count fits and every argument is compatible with its parameter (LANG-280/281), without diagnostics.</summary>
    private bool IsApplicable(RoutineSymbol candidate, ImmutableArray<BoundExpression> arguments)
    {
        if (candidate.Parameters.Length != arguments.Length)
        {
            return false;
        }

        for (var i = 0; i < arguments.Length; i++)
        {
            var (parameter, argument) = (candidate.Parameters[i], arguments[i]);
            var compatible = parameter.IsByReference
                ? argument is BoundVariableExpression or BoundParameterExpression { Parameter.Mode: not ParameterMode.Const }
                  && ReferenceEquals(argument.Type, parameter.Type)
                : ReferenceEquals(argument.Type, parameter.Type)
                  || (argument.ConstantValue is { } constant
                      ? _conversions.ConvertConstant(constant, parameter.Type, argument.Syntax, new DiagnosticBag()) != null
                      : _conversions.Classify(argument.Type, parameter.Type).Exists);
            if (!compatible)
            {
                return false;
            }
        }

        return true;
    }

    // ------------------------------------------------------------------ unary operators (LANG-227)

    private BoundExpression BindUnary(UnaryExpressionSyntax unary)
    {
        var operand = BindExpression(unary.Operand);
        if (operand.HasErrors)
        {
            return Error(unary, operand);
        }

        var op = unary.OperatorToken;
        var kind = op.Kind switch
        {
            SyntaxKind.MinusToken => BoundUnaryOperatorKind.Negate,
            SyntaxKind.PlusToken => BoundUnaryOperatorKind.Identity,
            _ => BoundUnaryOperatorKind.Not,
        };

        if (operand.ConstantValue is { } value)
        {
            return _folder.FoldUnary(op, value) is { } folded
                ? new BoundUnaryExpression(unary, kind, operand, folded.Type, folded)
                : Error(unary, operand);
        }

        switch (kind, operand.Type)
        {
            case (BoundUnaryOperatorKind.Negate or BoundUnaryOperatorKind.Identity, IntegerTypeSymbol integer):
            {
                // -Cardinal is Int64; -UInt64 stays UInt64 with W1010
                var type = kind == BoundUnaryOperatorKind.Negate && integer.Bits == 32 && !integer.IsSigned ? _types.Int64 : ComputationType(integer);
                if (kind == BoundUnaryOperatorKind.Negate && ReferenceEquals(type, _types.UInt64))
                {
                    _diagnostics.Report(op, "W1010");
                }

                return new BoundUnaryExpression(unary, kind, Convert(operand, type), type, constantValue: null);
            }

            case (BoundUnaryOperatorKind.Negate or BoundUnaryOperatorKind.Identity, RealTypeSymbol):
            case (BoundUnaryOperatorKind.Not, BooleanTypeSymbol):
                return new BoundUnaryExpression(unary, kind, operand, operand.Type, constantValue: null);

            case (BoundUnaryOperatorKind.Not, IntegerTypeSymbol integer):
            {
                // complement in the operand's own type for 8/16-bit types, else in its computation type
                var type = integer.Bits < 32 ? integer : ComputationType(integer);
                return new BoundUnaryExpression(unary, kind, Convert(operand, type), type, constantValue: null);
            }

            default:
                _diagnostics.Report(op, "E2011", op.Text, $"'{operand.Type.Name}'");
                return Error(unary, operand);
        }
    }

    // ------------------------------------------------------------------ binary operators (LANG-221…226, 301…305)

    private BoundExpression BindBinary(BinaryExpressionSyntax binary)
    {
        var left = BindExpression(binary.Left);
        var right = BindExpression(binary.Right);
        if (left.HasErrors || right.HasErrors)
        {
            return Error(binary, left, right);
        }

        var op = binary.OperatorToken;
        if (left.ConstantValue is { } l && right.ConstantValue is { } r)
        {
            return _folder.FoldBinary(op, l, r) is { } folded
                ? new BoundBinaryExpression(binary, OperatorKind(op), left, right, folded.Type, folded.Type, folded)
                : Error(binary, left, right);
        }

        var (lt, rt) = (left.Type, right.Type);
        switch (op.Kind)
        {
            case SyntaxKind.PlusToken or SyntaxKind.MinusToken or SyntaxKind.AsteriskToken:
                if (op.Kind == SyntaxKind.PlusToken && (lt is StringTypeSymbol || rt is StringTypeSymbol || (lt is CharTypeSymbol && rt is CharTypeSymbol)))
                {
                    return Unsupported(op, binary, "string expression");      // concatenation, LANG-243 (M2.2)
                }

                if (lt is IntegerTypeSymbol && rt is IntegerTypeSymbol)
                {
                    return IntegerOperation(binary, left, right);
                }

                return IsNumeric(lt) && IsNumeric(rt) ? RealOperation(binary, left, right) : Inapplicable(binary, left, right);

            case SyntaxKind.SlashToken:
                return IsNumeric(lt) && IsNumeric(rt) ? RealOperation(binary, left, right) : Inapplicable(binary, left, right);

            case SyntaxKind.DivKeyword or SyntaxKind.ModKeyword:
                return lt is IntegerTypeSymbol && rt is IntegerTypeSymbol ? IntegerOperation(binary, left, right) : Inapplicable(binary, left, right);

            case SyntaxKind.AndKeyword or SyntaxKind.OrKeyword or SyntaxKind.XorKeyword:
                if (lt is BooleanTypeSymbol && rt is BooleanTypeSymbol)
                {
                    return BooleanOperation(binary, left, right);
                }

                return lt is IntegerTypeSymbol && rt is IntegerTypeSymbol ? IntegerOperation(binary, left, right) : Inapplicable(binary, left, right);

            case SyntaxKind.ShlKeyword or SyntaxKind.ShrKeyword:
                if (lt is IntegerTypeSymbol shifted && rt is IntegerTypeSymbol)
                {
                    var type = ComputationType(shifted);
                    return new BoundBinaryExpression(binary, OperatorKind(op), Convert(left, type), Convert(right, type), type, type, constantValue: null);
                }

                return Inapplicable(binary, left, right);

            case SyntaxKind.EqualsToken or SyntaxKind.LessThanGreaterThanToken or SyntaxKind.LessThanToken
                or SyntaxKind.GreaterThanToken or SyntaxKind.LessThanEqualsToken or SyntaxKind.GreaterThanEqualsToken:
                return Relational(binary, left, right);

            default:
                return Unsupported(op, binary, $"operator '{op.Text}'");      // in, is, as: sets and classes (M2.2+)
        }
    }

    /// <summary>LANG-221/222: both operands in the combined computation type; W1010 for signed mixed with UInt64.</summary>
    private BoundBinaryExpression IntegerOperation(BinaryExpressionSyntax binary, BoundExpression left, BoundExpression right)
    {
        var (lt, rt) = AdaptedComputationTypes(left, right);
        var type = Combine(lt, rt, out var mixesSigns);
        if (mixesSigns)
        {
            _diagnostics.Report(binary.OperatorToken, "W1010");
        }

        return new BoundBinaryExpression(binary, OperatorKind(binary.OperatorToken), Convert(left, type), Convert(right, type), type, type, constantValue: null);
    }

    /// <summary>LANG-226/230: Single when both are Single (or a Single and an integer constant exact in Single), else Double.</summary>
    private BoundBinaryExpression RealOperation(BinaryExpressionSyntax binary, BoundExpression left, BoundExpression right)
    {
        var type = RealOperandType(left, right);
        return new BoundBinaryExpression(binary, OperatorKind(binary.OperatorToken), Convert(left, type), Convert(right, type), type, type, constantValue: null);
    }

    /// <summary>LANG-305: <c>and</c>/<c>or</c> short-circuit under <c>{$B-}</c>; C-style Booleans are normalised (LANG-215).</summary>
    private BoundBinaryExpression BooleanOperation(BinaryExpressionSyntax binary, BoundExpression left, BoundExpression right)
    {
        var op = binary.OperatorToken;
        var kind = OperatorKind(op);
        if (!op.DirectiveState.BoolEval)
        {
            kind = kind switch
            {
                BoundBinaryOperatorKind.And => BoundBinaryOperatorKind.ConditionalAnd,
                BoundBinaryOperatorKind.Or => BoundBinaryOperatorKind.ConditionalOr,
                _ => kind,
            };
        }

        var boolean = _types.Boolean;
        return new BoundBinaryExpression(binary, kind, Convert(left, boolean), Convert(right, boolean), boolean, boolean, constantValue: null);
    }

    /// <summary>LANG-301…302: integers (mathematically), reals, Booleans, chars of one kind.</summary>
    private BoundExpression Relational(BinaryExpressionSyntax binary, BoundExpression left, BoundExpression right)
    {
        var kind = OperatorKind(binary.OperatorToken);
        var boolean = _types.Boolean;
        var (lt, rt) = (left.Type, right.Type);
        if (lt is StringTypeSymbol || rt is StringTypeSymbol)
        {
            return Unsupported(binary.OperatorToken, binary, "string expression");    // LANG-244 (M2.2)
        }

        if (lt is IntegerTypeSymbol && rt is IntegerTypeSymbol)
        {
            var (l, r) = AdaptedComputationTypes(left, right);
            var type = Combine(l, r, out var mixesSigns);
            if (mixesSigns)
            {
                // LANG-302: signed vs unsigned 64-bit compares mathematically, without a warning
                return new BoundBinaryExpression(binary, kind, Convert(left, Signed64(l)), Convert(right, Signed64(r)), type, boolean, constantValue: null);
            }

            return new BoundBinaryExpression(binary, kind, Convert(left, type), Convert(right, type), type, boolean, constantValue: null);
        }

        if (IsNumeric(lt) && IsNumeric(rt))
        {
            var type = RealOperandType(left, right);
            return new BoundBinaryExpression(binary, kind, Convert(left, type), Convert(right, type), type, boolean, constantValue: null);
        }

        if (lt is BooleanTypeSymbol && rt is BooleanTypeSymbol)
        {
            return new BoundBinaryExpression(binary, kind, Convert(left, boolean), Convert(right, boolean), boolean, boolean, constantValue: null);
        }

        if (lt is CharTypeSymbol && rt is CharTypeSymbol)
        {
            // one char kind (LANG-280 rule 4); a character constant converts when its code fits the other kind
            var type = ReferenceEquals(lt, rt) ? lt
                : FitsCharType(left, rt) ? rt
                : FitsCharType(right, lt) ? lt
                : null;
            if (type != null)
            {
                return new BoundBinaryExpression(binary, kind, Convert(left, type), Convert(right, type), type, boolean, constantValue: null);
            }
        }

        return Inapplicable(binary, left, right);
    }

    private TypeSymbol Signed64(TypeSymbol computationType) =>
        ((IntegerTypeSymbol)computationType).IsSigned ? _types.Int64 : _types.UInt64;

    private static bool FitsCharType(BoundExpression expression, TypeSymbol type) =>
        expression.ConstantValue?.Value is BigInteger code && code <= ((CharTypeSymbol)type).MaxValue;

    private BoundErrorExpression Inapplicable(BinaryExpressionSyntax binary, BoundExpression left, BoundExpression right)
    {
        var op = binary.OperatorToken;
        _diagnostics.Report(op, "E2011", op.Text, $"'{left.Type.Name}' and '{right.Type.Name}'");
        return Error(binary, left, right);
    }

    private static BoundBinaryOperatorKind OperatorKind(SyntaxToken op) => op.Kind switch
    {
        SyntaxKind.PlusToken => BoundBinaryOperatorKind.Add,
        SyntaxKind.MinusToken => BoundBinaryOperatorKind.Subtract,
        SyntaxKind.AsteriskToken => BoundBinaryOperatorKind.Multiply,
        SyntaxKind.SlashToken => BoundBinaryOperatorKind.Divide,
        SyntaxKind.DivKeyword => BoundBinaryOperatorKind.IntegerDivide,
        SyntaxKind.ModKeyword => BoundBinaryOperatorKind.Modulo,
        SyntaxKind.AndKeyword => BoundBinaryOperatorKind.And,
        SyntaxKind.OrKeyword => BoundBinaryOperatorKind.Or,
        SyntaxKind.XorKeyword => BoundBinaryOperatorKind.Xor,
        SyntaxKind.ShlKeyword => BoundBinaryOperatorKind.Shl,
        SyntaxKind.ShrKeyword => BoundBinaryOperatorKind.Shr,
        SyntaxKind.EqualsToken => BoundBinaryOperatorKind.Equal,
        SyntaxKind.LessThanGreaterThanToken => BoundBinaryOperatorKind.NotEqual,
        SyntaxKind.LessThanToken => BoundBinaryOperatorKind.Less,
        SyntaxKind.GreaterThanToken => BoundBinaryOperatorKind.Greater,
        SyntaxKind.LessThanEqualsToken => BoundBinaryOperatorKind.LessOrEqual,
        SyntaxKind.GreaterThanEqualsToken => BoundBinaryOperatorKind.GreaterOrEqual,
        _ => throw new InvalidOperationException($"Not a binary operator: {op.Kind}"),
    };

    // ------------------------------------------------------------------ typing rules

    /// <summary>LANG-222: Integer, Cardinal, Int64 or UInt64.</summary>
    private TypeSymbol ComputationType(IntegerTypeSymbol type) => ConstantFolder.ComputationType(type) switch
    {
        (32, true) => _types.Integer,
        (32, false) => _types.Cardinal,
        (_, true) => _types.Int64,
        _ => _types.UInt64,
    };

    /// <summary>LANG-222: an integer constant takes the other operand's computation type when its value fits there.</summary>
    private (TypeSymbol Left, TypeSymbol Right) AdaptedComputationTypes(BoundExpression left, BoundExpression right)
    {
        var lt = ComputationType((IntegerTypeSymbol)left.Type);
        var rt = ComputationType((IntegerTypeSymbol)right.Type);
        if (left.ConstantValue?.Value is BigInteger l && Fits(l, rt))
        {
            lt = rt;
        }
        else if (right.ConstantValue?.Value is BigInteger r && Fits(r, lt))
        {
            rt = lt;
        }

        return (lt, rt);
    }

    /// <summary>LANG-221 table; <paramref name="mixesSigns"/> = a signed operand meets UInt64 (W1010).</summary>
    private TypeSymbol Combine(TypeSymbol left, TypeSymbol right, out bool mixesSigns)
    {
        mixesSigns = false;
        if (ReferenceEquals(left, right))
        {
            return left;
        }

        if (ReferenceEquals(left, _types.UInt64) || ReferenceEquals(right, _types.UInt64))
        {
            var other = (IntegerTypeSymbol)(ReferenceEquals(left, _types.UInt64) ? right : left);
            mixesSigns = other.IsSigned;
            return _types.UInt64;
        }

        return _types.Int64;                                            // Integer x Cardinal, or one side Int64
    }

    private TypeSymbol RealOperandType(BoundExpression left, BoundExpression right) =>
        IsSingleOperand(left, right) && IsSingleOperand(right, left) ? _types.Single : _types.Double;

    /// <summary>Single, or an integer constant exactly representable in Single beside a Single operand (LANG-230).</summary>
    private static bool IsSingleOperand(BoundExpression operand, BoundExpression other) => operand.Type switch
    {
        RealTypeSymbol { RealKind: RealKind.Single } => true,
        IntegerTypeSymbol when other.Type is RealTypeSymbol { RealKind: RealKind.Single } && operand.ConstantValue?.Value is BigInteger value
            => new BigInteger((float)value) == value,
        _ => false,
    };

    private static bool IsNumeric(TypeSymbol type) => type is IntegerTypeSymbol or RealTypeSymbol { RealKind: not RealKind.Currency };

    private static bool Fits(BigInteger value, TypeSymbol type) =>
        type is IntegerTypeSymbol integer && value >= integer.Min && value <= integer.Max;

    // ------------------------------------------------------------------ conversions and errors

    /// <summary>An implicit conversion of <paramref name="expression"/> to <paramref name="type"/> (none when it already has it).</summary>
    private static BoundExpression Convert(BoundExpression expression, TypeSymbol type)
    {
        if (ReferenceEquals(expression.Type, type))
        {
            return expression;
        }

        var constant = expression.ConstantValue is { } value ? ConvertConstant(value, type) : null;
        return new BoundConversionExpression(expression.Syntax, expression, type, ConversionKind.ImplicitNumeric, isExplicit: false, constant);
    }

    private static ConstantValue ConvertConstant(ConstantValue value, TypeSymbol type) => type switch
    {
        RealTypeSymbol { RealKind: RealKind.Single } => new ConstantValue((double)(float)RealValue(value), type),
        RealTypeSymbol => new ConstantValue(RealValue(value), type),
        _ => new ConstantValue(value.Value, type),
    };

    private static double RealValue(ConstantValue value) => value.Value is BigInteger i ? (double)i : (double)value.Value!;

    private BoundErrorExpression Error(SyntaxNode syntax, params BoundExpression[] children) =>
        new(syntax, _types.Error, [.. children]);

    /// <summary>E2900 for a construct of a later milestone (nothing when the syntax is already erroneous).</summary>
    private BoundErrorExpression Unsupported(SyntaxNode syntax, string feature)
    {
        if (!syntax.ContainsDiagnostics)
        {
            _diagnostics.Report(syntax, "E2900", feature);
        }

        return Error(syntax);
    }

    private BoundErrorExpression Unsupported(SyntaxToken op, BinaryExpressionSyntax binary, string feature)
    {
        _diagnostics.Report(op, "E2900", feature);
        return Error(binary);
    }
}
