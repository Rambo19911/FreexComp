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
/// CMP-1410 (C-387): for a captured <c>for var</c> variable the hidden <c>$for&lt;m&gt;</c> takes the role of <c>v</c>, and every
/// iteration first makes a new block frame of the loop and stores <c>$for&lt;m&gt;</c> into its field <c>v</c>.
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
        var iteration = context.TakeIterationFrame(loop.Variable);
        var control = iteration is null ? loop.Variable : context.ForDriver(loop.Variable);
        var body = context.LowerLoopBody(label, loop.Body);
        if (iteration is { } creation)
        {
            body = new LBlock([.. creation, new LAssign(context.Lower(loop.Variable), context.Lower(control)), body]);
        }

        var step = new LBlock([
            new LIf(context.Compare(LCompareOperator.Equal, context.Lower(control), new LLocalRef(last)), new LBreak(label), @else: null),
            new LAssign(context.Lower(control),
                context.Step(context.Lower(control), type, loop.IsDownTo ? LBinaryOperator.Subtract : LBinaryOperator.Add)),
        ]);
        var test = context.Compare(loop.IsDownTo ? LCompareOperator.GreaterOrEqual : LCompareOperator.LessOrEqual, new LLocalRef(first), new LLocalRef(last));
        return new LBlock([
            new LAssign(new LLocalRef(first), start),
            new LAssign(new LLocalRef(last), finish),
            new LIf(test, new LBlock([new LAssign(context.Lower(control), new LLocalRef(first)), new LLoop(label, body, step)]), @else: null),
        ]);
    }
}
