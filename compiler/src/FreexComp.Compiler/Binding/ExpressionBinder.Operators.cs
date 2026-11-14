using System.Collections.Immutable;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

// Operator overloading on records (LANG-731…740, C-123): operators bind to static calls of the declared operator methods.
internal sealed partial class ExpressionBinder
{
    /// <summary>
    /// LANG-442 among operator <paramref name="candidates"/> for already bound <paramref name="arguments"/>: the call (arguments
    /// converted), an error after an ambiguity (E2122), or null when no candidate applies (nothing reported).
    /// </summary>
    private BoundExpression? OperatorCall(SyntaxNode syntax, ImmutableArray<RoutineSymbol> candidates, ImmutableArray<BoundExpression> arguments)
    {
        var resolution = new OverloadResolver(_conversions).Resolve(candidates, arguments, IsVariableArgument);
        if (resolution.Routine is not MethodSymbol method)
        {
            if (resolution.Tied.IsEmpty)
            {
                return null;
            }

            _diagnostics.Report(syntax, "E2122", [.. resolution.Tied.Where(c => !c.Locations.IsEmpty).Select(c => new RelatedLocation(c.Locations[0], "candidate"))],
                resolution.Tied[0].Name);
            return Error(syntax, [.. arguments]);
        }

        ImmutableArray<BoundExpression> converted = [.. arguments.Select((a, i) => ConvertForAssignment(a, method.Parameters[i].Type, a.Syntax))];
        return new BoundCallExpression(syntax, method, converted) { Receiver = BoundReceiver.Static };
    }

    /// <summary>A conversion operator applied to <paramref name="value"/> (its argument converted to the parameter type first).</summary>
    private BoundExpression ConversionCall(SyntaxNode at, BoundExpression value, MethodSymbol conversion)
    {
        var parameter = conversion.Parameters[0];
        var argument = ReferenceEquals(value.Type, parameter.Type) ? value : ConvertForAssignment(value, parameter.Type, at);
        return argument.HasErrors ? Error(at, argument) : new BoundCallExpression(value.Syntax, conversion, [argument]) { Receiver = BoundReceiver.Static };
    }

    /// <summary>
    /// LANG-732 for <c>a op b</c> with a record operand: the declared operators of the operands' records (<c>&lt;&gt;</c> falls back to
    /// <c>not Equal</c>); else the built-in operator after one <c>Implicit</c> conversion of a record operand. Null when nothing
    /// applies (the caller reports the built-in operator's error).
    /// </summary>
    private BoundExpression? UserBinary(BinaryExpressionSyntax binary, BoundExpression left, BoundExpression right)
    {
        var op = binary.OperatorToken.Kind;
        foreach (var name in Operators.BinaryNames(op))
        {
            var candidates = Operators.Find(name, left.Type, right.Type);
            if (!candidates.IsEmpty && OperatorCall(binary, candidates, [left, right]) is { } call)
            {
                return call;
            }
        }

        if (op == SyntaxKind.LessThanGreaterThanToken && Operators.Find("Equal", left.Type, right.Type) is { IsEmpty: false } equal
            && OperatorCall(binary, equal, [left, right]) is { } equals)
        {
            return equals.HasErrors ? equals : new BoundUnaryExpression(binary, BoundUnaryOperatorKind.Not, equals, equals.Type, constantValue: null);
        }

        // at most one Implicit conversion of one operand, then the built-in operator
        if (left.Type is RecordTypeSymbol && right.Type is not RecordTypeSymbol && Through(left, right.Type) is { } converted)
        {
            return BindBinaryOperation(binary, ConversionCall(binary.Left, left, converted), right);
        }

        if (right.Type is RecordTypeSymbol && left.Type is not RecordTypeSymbol && Through(right, left.Type) is { } convertedRight)
        {
            return BindBinaryOperation(binary, left, ConversionCall(binary.Right, right, convertedRight));
        }

        return null;
    }

    /// <summary>The <c>Implicit</c> operator to take a record operand to the other operand's kind: the same type, else the same family.</summary>
    private static MethodSymbol? Through(BoundExpression record, TypeSymbol other)
    {
        var conversions = Operators.ConversionsFrom(record.Type).ToList();
        return conversions.FirstOrDefault(m => ReferenceEquals(m.ReturnType, other))
            ?? conversions.FirstOrDefault(m => IsNumeric(m.ReturnType!) && IsNumeric(other))
            ?? conversions.FirstOrDefault(m => m.ReturnType is StringTypeSymbol && other is StringTypeSymbol or CharTypeSymbol)
            ?? conversions.FirstOrDefault(m => m.ReturnType is BooleanTypeSymbol && other is BooleanTypeSymbol);
    }

    /// <summary>LANG-731 <c>-a</c>, <c>+a</c>, <c>not a</c> on a record: <c>Negative</c>, <c>Positive</c>, <c>LogicalNot</c>; null when none applies.</summary>
    private BoundExpression? UserUnary(UnaryExpressionSyntax unary, BoundUnaryOperatorKind kind, BoundExpression operand)
    {
        var name = kind switch
        {
            BoundUnaryOperatorKind.Negate => "Negative",
            BoundUnaryOperatorKind.Identity => "Positive",
            _ => "LogicalNot",
        };
        var candidates = Operators.Find(name, operand.Type);
        return candidates.IsEmpty ? null : OperatorCall(unary, candidates, [operand]);
    }

    /// <summary>LANG-731 <c>Inc(a)</c> / <c>Dec(a)</c> on a record with that operator: the operator applied to <c>a</c>; null otherwise.</summary>
    public BoundExpression? UserIncDec(SyntaxNode at, BoundExpression target, string name)
    {
        var candidates = Operators.Find(name, target.Type);
        return candidates.IsEmpty ? null : OperatorCall(at, candidates, [target]);
    }
}