using FreexComp.Debugger.Expressions;

namespace FreexComp.Debugger.Tests;

/// <summary>
/// Subset validation and lowering of the compiler's syntax nodes to the evaluator's tree (Pascal values §9, §9.1):
/// tree dumps, syntax error messages, rejected constructs, classification of <c>Name(…)</c>.
/// </summary>
public sealed class ExpressionParserTests
{
    private static readonly string[] TypeNames = ["Integer", "TColor", "TDog", "PInteger", "string"];

    private static bool IsTypeName(string name) => TypeNames.Contains(name, StringComparer.OrdinalIgnoreCase);

    private static string Dump(string text) => SubsetValidator.Parse(text, IsTypeName).Tree.Dump();

    private static DebuggerException Error(string text) =>
        Assert.Throws<DebuggerException>(() => SubsetValidator.Parse(text, IsTypeName));

    [Theory]
    // literals
    [InlineData("42", "42")]
    [InlineData("$FF", "255")]
    [InlineData("18446744073709551615", "18446744073709551615")]
    [InlineData("1.5", "1.5")]
    [InlineData("1e3", "1000")]
    [InlineData("'abc'", "'abc'")]
    [InlineData("'it''s'", "'it''s'")]
    [InlineData("''", "''")]
    [InlineData("'A'", "'A'")]                      // one character: a Char
    [InlineData("#13", "#13")]
    [InlineData("'a'#13#10'b'", "'a'#13#10'b'")]
    [InlineData("nil", "nil")]
    [InlineData("True", "True")]
    // precedence and associativity are the language's
    [InlineData("Total * 2 + 1", "(+ (* Total 2) 1)")]
    [InlineData("1 + 2 * 3", "(+ 1 (* 2 3))")]
    [InlineData("(1 + 2) * 3", "(* (+ 1 2) 3)")]
    [InlineData("10 - 4 - 3", "(- (- 10 4) 3)")]
    [InlineData("A div B mod C", "(mod (div A B) C)")]
    [InlineData("A shl 2 shr 1", "(shr (shl A 2) 1)")]
    [InlineData("A and B or C xor D", "(xor (or (and A B) C) D)")]
    [InlineData("not A and B", "(and (not A) B)")]
    [InlineData("-A + B", "(+ (- A) B)")]
    [InlineData("+A", "(+ A)")]
    [InlineData("A * -B", "(* A (- B))")]
    [InlineData("A / B", "(/ A B)")]
    [InlineData("A = B", "(= A B)")]
    [InlineData("A <> B", "(<> A B)")]
    [InlineData("A <= B + 1", "(<= A (+ B 1))")]
    [InlineData("A >= B", "(>= A B)")]
    [InlineData("Ok and (Ch = 'A')", "(and Ok (= Ch 'A'))")]
    [InlineData("clRed in Colors", "(in clRed Colors)")]
    // designators
    [InlineData("P.X", "(. P X)")]
    [InlineData("Pet.Owner.Name", "(. (. Pet Owner) Name)")]
    [InlineData("Nums[1]", "([] Nums 1)")]
    [InlineData("M[I, J + 1]", "([] ([] M I) (+ J 1))")]
    [InlineData("PTotal^", "(^ PTotal)")]
    [InlineData("List^.Next^.Value", "(. (^ (. (^ List) Next)) Value)")]
    [InlineData("@Total", "(@ Total)")]
    [InlineData("PTotal = @Total", "(= PTotal (@ Total))")]
    [InlineData("@P.X", "(@ (. P X))")]
    // set constructors
    [InlineData("[]", "(set)")]
    [InlineData("[clRed, clBlue]", "(set clRed clBlue)")]
    [InlineData("[1..3, 7]", "(set (.. 1 3) 7)")]
    [InlineData("Colors + [clGreen]", "(+ Colors (set clGreen))")]
    // builtins (any letter case) and typecasts
    [InlineData("Length(S)", "(Length S)")]
    [InlineData("high(Nums)", "(High Nums)")]
    [InlineData("ORD(Color) + 1", "(+ (Ord Color) 1)")]
    [InlineData("SizeOf(Integer)", "(SizeOf Integer)")]
    [InlineData("SizeOf(string)", "(SizeOf string)")]
    [InlineData("Integer(Color)", "(cast Integer Color)")]
    [InlineData("tcolor(1)", "(cast tcolor 1)")]
    [InlineData("TDog(Pet).Name", "(. (cast TDog Pet) Name)")]
    [InlineData("PInteger(P)^", "(^ (cast PInteger P))")]
    public void Tree(string text, string expected)
    {
        Assert.Equal(expected, Dump(text));
    }

    [Fact]
    public void Tree_CoversAtLeastFortyInputs()
    {
        var cases = typeof(ExpressionParserTests).GetMethod(nameof(Tree))!
            .GetCustomAttributes(typeof(InlineDataAttribute), inherit: false).Length;

        Assert.True(cases >= 40, $"only {cases} tree cases");
    }

    [Theory]
    [InlineData("Total * 2 + 1", "Total * 2 + 1")]
    [InlineData("  nums [ 1 ] ", "nums[1]")]
    [InlineData("(A+B)*C", "(A + B) * C")]
    [InlineData("A+(B*C)", "A + B * C")]
    [InlineData("A-(B-C)", "A - (B - C)")]
    [InlineData("not(A=B)", "not (A = B)")]
    [InlineData("-(A+B)", "-(A + B)")]
    [InlineData("M[I,J]", "M[I][J]")]
    [InlineData("TDog( Pet ).Name", "TDog(Pet).Name")]
    [InlineData("( PTotal ) ^", "PTotal^")]
    [InlineData("@ P . X", "@P.X")]
    [InlineData("[1 .. 3,7]", "[1..3, 7]")]
    [InlineData("LENGTH(S)+$10", "Length(S) + 16")]
    [InlineData("'it''s' + #13", "'it''s' + #13")]
    public void NormalisedText(string text, string expected)
    {
        Assert.Equal(expected, SubsetValidator.Parse(text, IsTypeName).Tree.ToPascal());
    }

    [Theory]
    [InlineData("", "Expression expected")]
    [InlineData("Total +", "Expression expected at column 8")]
    [InlineData("Total 1", "Unexpected '1' at column 7")]
    [InlineData("(Total", "')' expected at column 7")]
    public void SyntaxErrors(string text, string message)
    {
        var exception = Error(text);

        Assert.Equal(DebugErrorCode.ExpressionSyntax, exception.Code);
        Assert.Equal(message, exception.Message);
    }

    [Theory]
    [InlineData("Pet as TDog", "'as' is not supported in debugger expressions at column 5")]
    [InlineData("Pet is TDog", "'is' is not supported in debugger expressions at column 5")]
    [InlineData("1 + inherited Foo", "'inherited' is not supported in debugger expressions at column 5")]
    [InlineData("string(Ch)", "'string cast' is not supported in debugger expressions at column 1")]
    [InlineData("TList<Integer>.Create", "'generic name' is not supported in debugger expressions at column 1")]
    [InlineData("Length(S:4)", "'argument width' is not supported in debugger expressions at column 9")]
    [InlineData("procedure begin end", "'anonymous method' is not supported in debugger expressions at column 1")]
    [InlineData("1 + function: Integer begin Result := 1 end", "'anonymous method' is not supported in debugger expressions at column 5")]
    public void RejectedConstructs(string text, string message)
    {
        var exception = Error(text);

        Assert.Equal(DebugErrorCode.ExpressionSyntax, exception.Code);
        Assert.Equal(message, exception.Message);
    }

    [Theory]
    [InlineData("Foo(1)")]
    [InlineData("Pet.Describe()")]
    [InlineData("Factorial(N - 1) + 1")]
    [InlineData("Nums[0](1)")]
    public void OtherCalls_AreNotSupported(string text)
    {
        var exception = Error(text);

        Assert.Equal(DebugErrorCode.ExpressionEvaluation, exception.Code);
        Assert.Equal("Function calls are not supported in expressions", exception.Message);
    }

    [Fact]
    public void FormatSuffix_PassedThrough()
    {
        var parsed = SubsetValidator.Parse("Total * 2,x", IsTypeName, allowFormatSuffix: true);

        Assert.Equal("(* Total 2)", parsed.Tree.Dump());
        Assert.Equal(FormatSpec.Hex, parsed.Format);
        Assert.Equal("Total * 2", parsed.Text);
    }
}
