using FreexComp.Designer.Inspector;

namespace FreexComp.Designer.Tests.Inspector;

/// <summary>The integer-expression grammar's limits (object-inspector.md DSG-OI-040, I-220).</summary>
[Trait("Phase", "5")]
public sealed class IntegerExpressionTests
{
    private static string Nested(int depth) => new string('(', depth) + "1" + new string(')', depth);

    [Fact]
    [Trait("Req", "DSG-OI-040")]
    public void OneSignPerFactor()
    {
        // factor = ["-" | "+"] (...): one sign; a second needs parentheses.
        Assert.True(IntegerExpression.TryEvaluate("-5", out var v) && v == -5);
        Assert.True(IntegerExpression.TryEvaluate("-(-5)", out v) && v == 5);
        Assert.True(IntegerExpression.TryEvaluate("2*-3", out v) && v == -6);
        Assert.True(IntegerExpression.TryEvaluate("1 - -1", out v) && v == 2); // the second "-" is the factor's sign
        foreach (var bad in new[] { "--5", "+-5", "-+5", "2*--3", "1 - --1" })
        {
            Assert.False(IntegerExpression.TryEvaluate(bad, out _), bad);
        }
    }

    [Fact]
    [Trait("Req", "DSG-OI-040")]
    public void DeepInputIsRejected()
    {
        // Parentheses nest up to MaxDepth; one more is rejected as invalid, not evaluated.
        Assert.True(IntegerExpression.TryEvaluate(Nested(IntegerExpression.MaxDepth), out var v) && v == 1);
        Assert.False(IntegerExpression.TryEvaluate(Nested(IntegerExpression.MaxDepth + 1), out _));

        // Input far deeper than the stack allows ends in a refusal, not a stack overflow; a long flat chain still evaluates.
        Assert.False(IntegerExpression.TryEvaluate(Nested(1_000_000), out _));
        Assert.False(IntegerExpression.TryEvaluate(new string('(', 1_000_000), out _));
        Assert.True(IntegerExpression.TryEvaluate(string.Join("+", Enumerable.Repeat("1", 100_000)), out v) && v == 100_000);
    }
}
