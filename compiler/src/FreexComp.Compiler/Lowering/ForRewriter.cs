using FreexComp.Compiler.Binding;

namespace FreexComp.Compiler.Lowering;

/// <summary>
/// CMP-321 / LANG-361: <c>for v := a to b do S</c> →
/// <code>
/// tmpA := a; tmpB := b;
/// if tmpA &lt;= tmpB then begin            // downto: &gt;=
///   v := tmpA;
///   loop S; step: if v = tmpB then break; v := v + 1 end   // downto: v - 1
/// end
/// </code>
/// The equality test before the increment means the loop never overflows at <c>High(T)</c>; <c>Continue</c> runs the step.
/// </summary>
internal static class ForRewriter
{
    public static LStatement Rewrite(LoweringContext context, BoundForStatement loop)
    {
        var type = loop.Variable.Type;
        var start = context.Lower(loop.Start);
        var finish = context.Lower(loop.Finish);
        var first = context.NewTemp(type);
        var last = context.NewTemp(type);
        var label = context.NewLoop("for");
        var body = context.LowerLoopBody(label, loop.Body);

        var step = new LBlock([
            new LIf(context.Compare(LCompareOperator.Equal, context.Lower(loop.Variable), new LLocalRef(last)), new LBreak(label), @else: null),
            new LAssign(context.Lower(loop.Variable),
                context.Step(context.Lower(loop.Variable), type, loop.IsDownTo ? LBinaryOperator.Subtract : LBinaryOperator.Add)),
        ]);
        var test = context.Compare(loop.IsDownTo ? LCompareOperator.GreaterOrEqual : LCompareOperator.LessOrEqual, new LLocalRef(first), new LLocalRef(last));
        return new LBlock([
            new LAssign(new LLocalRef(first), start),
            new LAssign(new LLocalRef(last), finish),
            new LIf(test, new LBlock([new LAssign(context.Lower(loop.Variable), new LLocalRef(first)), new LLoop(label, body, step)]), @else: null),
        ]);
    }
}
