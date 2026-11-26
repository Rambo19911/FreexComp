using FreexComp.Ide.Core.Debugging;

namespace FreexComp.Ide.Core.Tests.Debugging;

/// <summary>DBG-258 (I-223): the expression a value tooltip evaluates, determined from the syntax tree.</summary>
[Trait("Phase", "8")]
[Trait("Category", "Unit")]
public sealed class HoverExpressionTests
{
    private const string Source = """
        program P;
        var
          A: TThing;
          I, Total: Integer;
        begin
          Total := A.B[I]^.C + Nums[I];
          WriteLn(Total);
          F(I).X := 1;
          Q := Nums[F(I)];
          A.Run();
        end.
        """;

    /// <summary>The hovered text (<paramref name="at"/>, the <paramref name="occurrence"/>-th) → the expression, or null.</summary>
    [Theory]
    [InlineData("A.B", 0, "A")]
    [InlineData("B[", 0, "A.B")]
    [InlineData("I]^", 0, "I")]
    [InlineData("]^", 0, "A.B[I]")]
    [InlineData("^.C", 0, "A.B[I]^")]
    [InlineData("C +", 0, "A.B[I]^.C")]
    [InlineData("Nums[I]", 0, "Nums")]
    [InlineData("];\n  WriteLn", 0, "Nums[I]")]
    [InlineData("Total", 1, "Total")] // a variable in an expression
    [InlineData("Total", 0, "Total")] // its declaration
    [InlineData("Total)", 0, "Total")] // an argument
    [InlineData("WriteLn", 0, null)] // a routine that is called
    [InlineData("X :=", 0, null)] // a designator with a call in it
    [InlineData("]", 2, null)] // an index with a call in it
    [InlineData("Run", 0, null)] // a method that is called
    [InlineData("begin", 0, null)] // a keyword
    [InlineData(":=", 0, null)] // an operator
    [InlineData("1;", 0, null)] // a literal
    [Trait("Req", "DBG-258")]
    public void FullDesignatorUpToTheHoveredToken(string at, int occurrence, string? expected)
    {
        var text = Source.ReplaceLineEndings("\n");
        var offset = -1;
        for (var i = 0; i <= occurrence; i++)
            offset = text.IndexOf(at, offset + 1, StringComparison.Ordinal);
        Assert.True(offset >= 0, at);

        var result = HoverExpression.At(@"C:\p\P.pas", text, offset);

        Assert.Equal(expected, result?.Expression);
        if (result is { } found)
        {
            Assert.True(found.Start <= offset && offset < found.Start + found.Length, "the range is the hovered token");
            Assert.Equal(at.Length > 1 && char.IsLetter(at[0]) ? at.TakeWhile(char.IsLetterOrDigit).Count() : 1, found.Length);
        }
    }

    [Fact]
    [Trait("Req", "DBG-258")]
    public void NothingOutsideTheText()
    {
        Assert.Null(HoverExpression.At(@"C:\p\P.pas", "begin end.", -1));
        Assert.Null(HoverExpression.At(@"C:\p\P.pas", "begin end.", 10));
        Assert.Null(HoverExpression.At(@"C:\p\P.pas", "", 0));
    }
}
