using FreexComp.Compiler.Lowering;

namespace FreexComp.Compiler.Tests.Lowering;

/// <summary>
/// S-expression form of a lowered tree for assertions. Expressions: constants <c>value:Type</c>; locals, parameters and
/// temporaries by name, globals <c>@Name</c>; <c>(conv Type x)</c>, <c>(Op:Type operands…)</c> (<c>Op!</c> = overflow-checked),
/// <c>(range lo..hi x)</c>, <c>(Less a b)</c>,
/// <c>(select c a b)</c>, <c>(addr x)</c>, <c>(call Name:Type args…)</c>, <c>(let $vN value body)</c>. Statements:
/// <c>(block …)</c>, <c>(:= t v)</c>, <c>(if c then [else])</c>, <c>(loop L body [step])</c>, <c>(break L)</c>,
/// <c>(continue L)</c>, <c>(switch sel (case [values] [ranges] body)… [(default body)])</c>, <c>(call Name args…)</c>,
/// <c>(return)</c>, <c>(unreachable)</c>. Strings: <c>(length s)</c>, <c>(char s i0)</c> (<c>char!</c> = range-checked).
/// </summary>
internal static class LoweredDump
{
    public static string Of(LNode node) => node switch
    {
        LConst c => $"{c.Value.ToDisplayString()}:{c.Type.Name}",
        LLocalRef l => l.Local.Name,
        LGlobalRef g => "@" + g.Variable.Name,
        LRuntimeVariableRef r => "@System." + r.Variable.Name,
        LStringLiteral s => $"'{s.Value}'",
        LParamRef p => p.Parameter.Name,
        LOuterRef o => "^" + o.Variable.Name,
        LAddrOf a => $"(addr {Of(a.Variable)})",
        LUnary u => $"({u.Operator}{(u.IsChecked ? "!" : "")}:{u.Type.Name} {Of(u.Operand)})",
        LBinary b => $"({b.Operator}{(b.IsChecked ? "!" : "")}:{b.Type.Name} {Of(b.Left)} {Of(b.Right)})",
        LRangeCheck r => $"(range {r.Low}..{r.High} {Of(r.Value)})",
        LCompare c => $"({c.Operator} {Of(c.Left)} {Of(c.Right)})",
        LConvert c => $"(conv {c.Type.Name} {Of(c.Operand)})",
        LSelect s => $"(select {Of(s.Condition)} {Of(s.WhenTrue)} {Of(s.WhenFalse)})",
        LCall c => List($"call {Callee(c.Callee)}:{c.Type.Name}", c.Arguments),
        LTemp t => $"(let {t.Temp} {Of(t.Value)} {Of(t.Body)})",
        LTempRef t => t.Temp.ToString(),
        LIndex i => $"([] {Of(i.Array)} {Of(i.Index)})",
        LField f => $"(. {Of(f.Record)} {f.Field.Name})",
        LSequence s => $"(seq {Of(s.First)} {Of(s.Value)})",
        LView v => $"(view {v.Type.Name} {Of(v.Aggregate)})",
        LTestBit t => $"(bit? {Of(t.Address)} {Of(t.Element)})",
        LZero z => $"(zero {Of(z.Address)})",
        LSetBit s => $"({(s.Include ? "setbit" : "clearbit")} {Of(s.Address)} {Of(s.Element)})",
        LBlock b => List("block", b.Statements),
        LAssign a => $"(:= {Of(a.Target)} {Of(a.Value)})",
        LCallStatement c => List($"call {Callee(c.Callee)}", c.Arguments),
        LIf i => i.Else is null ? $"(if {Of(i.Condition)} {Of(i.Then)})" : $"(if {Of(i.Condition)} {Of(i.Then)} {Of(i.Else)})",
        LLoop l => l.Step is null ? $"(loop {l.Label} {Of(l.Body)})" : $"(loop {l.Label} {Of(l.Body)} {Of(l.Step)})",
        LBreak b => $"(break {b.Label})",
        LContinue c => $"(continue {c.Label})",
        LSwitch s => Switch(s),
        LMemSet m => $"(memset {Of(m.Target)} {Of(m.Count)} {Of(m.Value)})",
        LMemMove m => $"(memmove {Of(m.Source)} {Of(m.Destination)} {Of(m.Count)})",
        LStringLength l => $"(length {Of(l.String)})",
        LStringIndex i => $"(char{(i.IsChecked ? "!" : "")} {Of(i.String)} {Of(i.Index)})",
        LReturn => "(return)",
        LUnreachable => "(unreachable)",
        _ => throw new ArgumentOutOfRangeException(nameof(node), node.GetType().Name),
    };

    private static string Callee(LCallee callee) => callee switch
    {
        LRoutineCallee r => r.Routine.Name,
        LHelperCallee h => h.Helper.Name,
        _ => throw new ArgumentOutOfRangeException(nameof(callee)),
    };

    private static string List(string head, IEnumerable<LNode> items) => "(" + string.Join(" ", items.Select(Of).Prepend(head)) + ")";

    private static string Switch(LSwitch s)
    {
        var parts = new List<string> { "switch", Of(s.Selector) };
        foreach (var section in s.Sections)
        {
            parts.Add($"(case [{string.Join(" ", section.Values)}] [{string.Join(" ", section.Ranges.Select(r => $"{r.Low}..{r.High}"))}] {Of(section.Body)})");
        }

        if (s.Default is { } d)
        {
            parts.Add($"(default {Of(d)})");
        }

        return "(" + string.Join(" ", parts) + ")";
    }
}
