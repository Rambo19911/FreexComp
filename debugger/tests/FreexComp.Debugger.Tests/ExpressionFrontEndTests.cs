using FreexComp.Compiler.Syntax;
using FreexComp.Debugger.Expressions;

namespace FreexComp.Debugger.Tests;

/// <summary>
/// The expression front end over the compiler's parser (Pascal values §9, §9.1): literal forms, syntax errors with
/// their 1-based column, the format-suffix split.
/// </summary>
public sealed class ExpressionFrontEndTests
{
    private static SyntaxToken Literal(string text)
    {
        var parsed = ExpressionFrontEnd.Parse(text);
        return Assert.IsType<LiteralExpressionSyntax>(parsed.Root).Token;
    }

    private static string SyntaxError(string text, bool allowFormatSuffix = false)
    {
        var exception = Assert.Throws<DebuggerException>(() => ExpressionFrontEnd.Parse(text, allowFormatSuffix));
        Assert.Equal(DebugErrorCode.ExpressionSyntax, exception.Code);
        return exception.Message;
    }

    [Theory]
    [InlineData("0", 0L)]
    [InlineData("42", 42L)]
    [InlineData("9223372036854775807", long.MaxValue)]
    [InlineData("$FF", 255L)]
    [InlineData("$ff", 255L)]
    [InlineData("$7FFFFFFFFFFFFFFF", long.MaxValue)]
    public void IntegerLiterals(string text, long expected)
    {
        var token = Literal(text);

        Assert.Equal(SyntaxKind.IntegerLiteralToken, token.Kind);
        Assert.Equal(expected, token.Value);
    }

    [Theory]
    [InlineData("18446744073709551615")]
    [InlineData("$FFFFFFFFFFFFFFFF")]
    public void IntegerLiterals_AboveInt64_AreUnsigned(string text)
    {
        Assert.Equal(ulong.MaxValue, Literal(text).Value);
    }

    [Theory]
    [InlineData("1.5", 1.5)]
    [InlineData("0.25", 0.25)]
    [InlineData("1e3", 1000.0)]
    [InlineData("1E3", 1000.0)]
    [InlineData("1.5E-2", 0.015)]
    [InlineData("2e+2", 200.0)]
    public void FloatLiterals(string text, double expected)
    {
        var token = Literal(text);

        Assert.Equal(SyntaxKind.RealLiteralToken, token.Kind);
        Assert.Equal(expected, token.Value);
    }

    [Theory]
    [InlineData("'abc'", "abc")]
    [InlineData("''", "")]
    [InlineData("'it''s'", "it's")]
    [InlineData("''''", "'")]
    [InlineData("'Héllo'", "Héllo")]
    [InlineData("#65", "A")]
    [InlineData("#$41", "A")]
    [InlineData("#13#10", "\r\n")]
    [InlineData("'a'#13#10'b'", "a\r\nb")]
    [InlineData("#9'x'", "\tx")]
    public void StringAndCharLiterals(string text, string expected)
    {
        var token = Literal(text);

        Assert.Equal(SyntaxKind.StringLiteralToken, token.Kind);
        Assert.Equal(expected, token.Value);
    }

    [Fact]
    public void Nil_True_False()
    {
        Assert.Equal(SyntaxKind.NilKeyword, Literal("nil").Kind);
        Assert.Equal(SyntaxKind.NilKeyword, Literal("NIL").Kind);
        // True and False are identifiers of the language: resolved by the evaluator (§9.2 rule 4).
        Assert.Equal("True", Assert.IsType<IdentifierNameSyntax>(ExpressionFrontEnd.Parse("True").Root).Identifier.Text);
        Assert.Equal("false", Assert.IsType<IdentifierNameSyntax>(ExpressionFrontEnd.Parse("false").Root).Identifier.Text);
    }

    [Fact]
    public void Text_IsTrimmed_NodeKindsAreTheCompilers()
    {
        var parsed = ExpressionFrontEnd.Parse("  Total * 2 + 1 ");

        Assert.Equal("Total * 2 + 1", parsed.Text);
        Assert.Equal(FormatSpec.None, parsed.Format);
        // Precedence is the language's: (Total * 2) + 1.
        var sum = Assert.IsType<BinaryExpressionSyntax>(parsed.Root);
        Assert.Equal(SyntaxKind.PlusToken, sum.OperatorToken.Kind);
        Assert.Equal(SyntaxKind.AsteriskToken, Assert.IsType<BinaryExpressionSyntax>(sum.Left).OperatorToken.Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void Empty_ExpressionExpected(string text)
    {
        Assert.Equal("Expression expected", SyntaxError(text));
    }

    [Theory]
    // The worked examples of the specification (§9.7, features §2.2).
    [InlineData("Total +", "Expression expected at column 8")]
    [InlineData("I >", "Expression expected at column 4")]
    [InlineData("Total + ", "Expression expected at column 9")]
    [InlineData("* 2", "Expression expected at column 1")]
    [InlineData("1 + * 2", "Expression expected at column 5")]
    [InlineData("()", "Expression expected at column 2")]
    [InlineData("Total + )", "Expression expected at column 9")]
    // Trailing input after a complete expression.
    [InlineData("1 2", "Unexpected '2' at column 3")]
    [InlineData("Total Total", "Unexpected 'Total' at column 7")]
    [InlineData("Total)", "Unexpected ')' at column 6")]
    [InlineData("Total,x", "Unexpected ',' at column 6")]
    [InlineData("A := 1", "Unexpected ':=' at column 3")]
    [InlineData("1 ? 2", "Unexpected '?' at column 3")]
    // Missing closers.
    [InlineData("(1 + 2", "')' expected at column 7")]
    [InlineData("A[1", "']' expected at column 4")]
    [InlineData("Length(S", "')' expected at column 9")]
    [InlineData("[1, 2", "']' expected at column 6")]
    [InlineData("P.", "Identifier expected at column 3")]
    [InlineData("P.+1", "Identifier expected at column 3")]
    // Malformed literals.
    [InlineData("'abc", "Invalid string literal at column 1")]
    [InlineData("S + 'abc", "Invalid string literal at column 5")]
    [InlineData("12abc", "Invalid number '12abc' at column 1")]
    [InlineData("1 + 1e", "Invalid number '1e' at column 5")]
    // One relational operator per expression.
    [InlineData("a = b = c", "Unexpected '=' at column 7")]
    [InlineData("1 < 2 < 3", "Unexpected '<' at column 7")]
    // No comments.
    [InlineData("1 {c} + 2", "Unexpected '{c}' at column 3")]
    [InlineData("1 // x", "Unexpected '// x' at column 3")]
    public void SyntaxErrors_WithColumn(string text, string expected)
    {
        Assert.Equal(expected, SyntaxError(text));
    }

    [Fact]
    public void ParenthesisedRelation_IsOneOperatorEach()
    {
        Assert.IsType<BinaryExpressionSyntax>(ExpressionFrontEnd.Parse("(a = b) = c").Root);
    }

    [Theory]
    [InlineData("Total,x", "Total", "Hex", 0)]
    [InlineData("Total,X", "Total", "Hex", 0)]
    [InlineData("Total, h", "Total", "Hex", 0)]
    [InlineData(" Total ,d ", "Total", "Decimal", 0)]
    [InlineData("P,10", "P", "Count", 10)]
    [InlineData("A[1, 2],x", "A[1, 2]", "Hex", 0)]
    [InlineData("High(Nums),d", "High(Nums)", "Decimal", 0)]
    [InlineData("S + ',x',h", "S + ',x'", "Hex", 0)]
    public void FormatSuffix_SplitAtLastTopLevelComma(string text, string expression, string format, int count)
    {
        var parsed = ExpressionFrontEnd.Parse(text, allowFormatSuffix: true);

        Assert.Equal(expression, parsed.Text);
        Assert.Equal(format, parsed.Format.ToString());
        Assert.Equal(count, parsed.Count);
    }

    [Theory]
    [InlineData("A[1, x]")]          // inside brackets
    [InlineData("[1, 2]")]
    [InlineData("'a,x'")]            // inside quotes
    [InlineData("Total")]
    public void FormatSuffix_NotSplit(string text)
    {
        var parsed = ExpressionFrontEnd.Parse(text, allowFormatSuffix: true);

        Assert.Equal(text, parsed.Text);
        Assert.Equal(FormatSpec.None, parsed.Format);
    }

    [Fact]
    public void FormatSuffix_OnlyKnownSpecs_AndOnlyWhenAllowed()
    {
        // Not one of the specs: the comma stays part of the text and is a syntax error.
        Assert.Equal("Unexpected ',' at column 6", SyntaxError("Total,y", allowFormatSuffix: true));
        // Conditions take no suffix.
        Assert.Equal("Unexpected ',' at column 6", SyntaxError("Total,x"));
        // Only a suffix: nothing to evaluate.
        Assert.Equal("Expression expected", SyntaxError(",x", allowFormatSuffix: true));
    }

    [Fact]
    [Trait("Req", "DBG-508")]
    public void RawSuffix_SplitOff_CombinedSuffixRejected()
    {
        foreach (var (text, expression) in new[] { ("L,r", "L"), (" L , R ", "L"), ("A[1, 2],r", "A[1, 2]"), ("S + ',r',r", "S + ',r'") })
        {
            var parsed = ExpressionFrontEnd.Parse(text, allowFormatSuffix: true);
            Assert.Equal(expression, parsed.Text);
            Assert.Equal(FormatSpec.Raw, parsed.Format);
        }

        // r cannot be combined with another suffix.
        Assert.Equal("Unknown format specifier 'rx'", SyntaxError("L,rx", allowFormatSuffix: true));
        Assert.Equal("Unknown format specifier 'xr'", SyntaxError("L,xr", allowFormatSuffix: true));
        Assert.Equal("Unknown format specifier 'r10'", SyntaxError("L,r10", allowFormatSuffix: true));
        Assert.Equal("Unknown format specifier 'Rd'", SyntaxError("L, Rd", allowFormatSuffix: true));
        // Conditions and log points take no suffix (unchanged).
        Assert.Equal("Unexpected ',' at column 2", SyntaxError("L,r"));
    }

    [Fact]
    public void ErrorColumn_CountsFromTheStartOfTheTypedText()
    {
        Assert.Equal("Expression expected at column 11", SyntaxError("   Total +,x", allowFormatSuffix: true));
    }
}
