using FreexComp.Compiler.Binding;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>
/// S-expression form of a bound tree for assertions: a node with a constant value prints as <c>value:Type</c>,
/// a variable as <c>name:Type</c>, an implicit conversion as <c>(conv Type operand)</c>, a typecast as
/// <c>(cast Type operand)</c>, an operator as <c>(Kind:Type operands…)</c>. Statements: <c>(block …)</c>,
/// <c>(:= target value)</c>, <c>(if c then [else])</c>, <c>(while c body)</c>, <c>(repeat block c)</c>,
/// <c>(for v to|downto start finish body)</c>, <c>(case selector (arm [labels] body)… [(else block)])</c>,
/// <c>(break)</c>, <c>(continue)</c>, <c>(exit [value])</c>, <c>(halt [code])</c>; intrinsics <c>(Name:Type args…)</c> /
/// <c>(Name args…)</c>; calls <c>(call F:Type args…)</c> in
/// expressions, <c>(call P args…)</c> as statements; parameters print like variables.
/// </summary>
internal static class BoundDump
{
    public static string Of(BoundNode node) => node switch
    {
        BoundExpression { ConstantValue: { } value } e => $"{value.ToDisplayString()}:{e.Type.Name}",
        BoundVariableExpression v => $"{v.Variable.Name}:{v.Type.Name}",
        BoundParameterExpression p => $"{p.Parameter.Name}:{p.Type.Name}",
        BoundCallExpression c => List($"call {c.Routine.Name}:{c.Type.Name}", c.Arguments),
        BoundIntrinsicCallExpression c => List($"{c.Intrinsic.Name}:{c.Type.Name}", c.Arguments),
        BoundConversionExpression { IsExplicit: false } c => $"(conv {c.Type.Name} {Of(c.Operand)})",
        BoundConversionExpression c => $"(cast {c.Type.Name} {Of(c.Operand)})",
        BoundUnaryExpression u => $"({u.OperatorKind}:{u.Type.Name} {Of(u.Operand)})",
        BoundBinaryExpression b => $"({b.OperatorKind}:{b.Type.Name} {Of(b.Left)} {Of(b.Right)})",
        BoundSetConstructor s => "(" + string.Join(" ", s.Elements.Select(e => e.High is null ? Of(e.Low) : $"(.. {Of(e.Low)} {Of(e.High)})").Prepend($"set {s.Type.Name}")) + ")",
        BoundIndexExpression x => $"([] {Of(x.Array)} {Of(x.Index)}):{x.Type.Name}",
        BoundErrorExpression => "<error>",
        BoundBlock block => List("block", block.Statements),
        BoundAssignmentStatement a => $"(:= {Of(a.Target)} {Of(a.Value)})",
        BoundExpressionStatement e => $"(expr {Of(e.Expression)})",
        BoundCallStatement c => List($"call {c.Routine.Name}", c.Arguments),
        BoundIntrinsicCallStatement c => List(c.Intrinsic.Name, c.Arguments),
        BoundWriteStatement w => Write(w),
        BoundIfStatement i => i.Else is null ? $"(if {Of(i.Condition)} {Of(i.Then)})" : $"(if {Of(i.Condition)} {Of(i.Then)} {Of(i.Else)})",
        BoundWhileStatement w => $"(while {Of(w.Condition)} {Of(w.Body)})",
        BoundRepeatStatement r => $"(repeat {Of(r.Body)} {Of(r.Condition)})",
        BoundForStatement f => $"(for {Of(f.Variable)} {(f.IsDownTo ? "downto" : "to")} {Of(f.Start)} {Of(f.Finish)} {Of(f.Body)})",
        BoundCaseStatement c => Case(c),
        BoundBreakStatement => "(break)",
        BoundContinueStatement => "(continue)",
        BoundExitStatement x => x.Value is null ? "(exit)" : $"(exit {Of(x.Value)})",
        BoundHaltStatement h => h.ExitCode is null ? "(halt)" : $"(halt {Of(h.ExitCode)})",
        _ => $"<{node.Kind}>",
    };

    private static string Write(BoundWriteStatement w)
    {
        var parts = new List<string> { w.Intrinsic.Name };
        if (w.File is { } file)
        {
            parts.Add($"(file {Of(file)})");
        }

        foreach (var a in w.Arguments)
        {
            parts.Add(a.Width is null ? Of(a.Value) : a.Decimals is null ? $"(: {Of(a.Value)} {Of(a.Width)})" : $"(: {Of(a.Value)} {Of(a.Width)} {Of(a.Decimals)})");
        }

        return "(" + string.Join(" ", parts) + ")";
    }

    private static string List(string head, IEnumerable<BoundNode> items) =>
        "(" + string.Join(" ", items.Select(Of).Prepend(head)) + ")";

    private static string Case(BoundCaseStatement c)
    {
        var parts = new List<string> { "case", Of(c.Selector) };
        foreach (var arm in c.Arms)
        {
            var labels = string.Join(", ", arm.Labels.Select(l => l.High is null ? Value(l.Low) : $"{Value(l.Low)}..{Value(l.High)}"));
            parts.Add($"(arm [{labels}] {Of(arm.Body)})");
        }

        if (c.Else is { } otherwise)
        {
            parts.Add($"(else {Of(otherwise)})");
        }

        return "(" + string.Join(" ", parts) + ")";
    }

    /// <summary>A case label value without its type.</summary>
    private static string Value(BoundExpression e) => e.ConstantValue?.ToDisplayString() ?? Of(e);
}
