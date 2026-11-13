using FreexComp.Compiler.Binding;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>
/// S-expression form of a bound tree for assertions: a node with a constant value prints as <c>value:Type</c>,
/// a variable as <c>name:Type</c>, an implicit conversion as <c>(conv Type operand)</c>, a typecast as
/// <c>(cast Type operand)</c>, an operator as <c>(Kind:Type operands…)</c>.
/// </summary>
internal static class BoundDump
{
    public static string Of(BoundNode node) => node switch
    {
        BoundExpression { ConstantValue: { } value } e => $"{value.ToDisplayString()}:{e.Type.Name}",
        BoundVariableExpression v => $"{v.Variable.Name}:{v.Type.Name}",
        BoundConversionExpression { IsExplicit: false } c => $"(conv {c.Type.Name} {Of(c.Operand)})",
        BoundConversionExpression c => $"(cast {c.Type.Name} {Of(c.Operand)})",
        BoundUnaryExpression u => $"({u.OperatorKind}:{u.Type.Name} {Of(u.Operand)})",
        BoundBinaryExpression b => $"({b.OperatorKind}:{b.Type.Name} {Of(b.Left)} {Of(b.Right)})",
        BoundErrorExpression => "<error>",
        _ => $"<{node.Kind}>",
    };
}
