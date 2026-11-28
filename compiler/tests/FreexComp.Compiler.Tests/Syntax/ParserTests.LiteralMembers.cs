namespace FreexComp.Compiler.Tests.Syntax;

/// <summary>LANG-997 (C-444): member access on literals and on the keyword <c>string</c> (grammar.md §6 <c>factor</c>).</summary>
public sealed partial class ParserTests
{
    private static string Member(string receiver, string name) => $"(MemberAccessExpression {receiver} . {Id(name)})";

    public static TheoryData<string, string> LiteralMemberAccessCases => new()
    {
        // each literal kind with one suffix
        { "42.Squared", Member(Lit("42"), "Squared") },
        { "$FF.ToText", Member(Lit("$FF"), "ToText") },
        { "1.5.Frac", Member(Lit("1.5"), "Frac") },
        { "1.5e3.Half", Member(Lit("1.5e3"), "Half") },
        { "1.e5", Member(Lit("1"), "e5") },
        { "'abc'.Len", Member(Lit("'abc'"), "Len") },
        { "#65.IsDigit", Member(Lit("#65"), "IsDigit") },
        { "''.Len", Member(Lit("''"), "Len") },
        // several suffixes
        { "'abc'.Twice.Len", Member(Member(Lit("'abc'"), "Twice"), "Len") },
        { "42.ToText.Chars[0]", $"(ElementAccessExpression {Member(Member(Lit("42"), "ToText"), "Chars")} [ {Lit("0")} ])" },
        { "'abc'.Pos('b')", $"(InvocationExpression {Member(Lit("'abc'"), "Pos")} (ArgumentList ( (Argument {Lit("'b'")}) )))" },
        { "'abc'.P^", $"(DereferenceExpression {Member(Lit("'abc'"), "P")} ^)" },
        { "42.Make<Integer>", $"(MemberAccessExpression {Lit("42")} . (GenericName Make (TypeArgumentList < (NamedType {Id("Integer")}) >)))" },
        // string.Name (LANG-997 (b))
        { "string.Join(',', A)", $"(InvocationExpression {Member("(PredefinedTypeExpression string)", "Join")} (ArgumentList ( (Argument {Lit("','")}) , (Argument {Id("A")}) )))" },
        { "string.Empty.Len", Member(Member("(PredefinedTypeExpression string)", "Empty"), "Len") },
        // a sign or not applies to the whole factor (LANG-997 (c))
        { "-5.Squared", $"(UnaryExpression - {Member(Lit("5"), "Squared")})" },
        { "not 5.IsEven", $"(UnaryExpression not {Member(Lit("5"), "IsEven")})" },
        { "(-5).Squared", Member($"(ParenthesizedExpression ( (UnaryExpression - {Lit("5")}) ))", "Squared") },
        { "42.Squared * 2", Bin(Member(Lit("42"), "Squared"), "*", Lit("2")) },
        // unchanged forms
        { "[1..5]", $"(SetConstructor [ (SetElement {Lit("1")} .. {Lit("5")}) ])" },
        { "string(p)", $"(StringCastExpression string ( {Id("p")} ))" },
        { "nil", Lit("nil") },
    };

    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-997")]
    [MemberData(nameof(LiteralMemberAccessCases))]
    public void LiteralMemberAccess(string expression, string expected)
    {
        Assert.Equal(expected, DumpExpression(expression));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-997")]
    public void LiteralMemberAccess_SubrangesAndBareStringUnchanged()
    {
        Assert.Equal(
            "(Program (Block (TypeSection type (TypeDeclaration T = (SubrangeType (LiteralExpression 1) .. (LiteralExpression 5)) ;)) "
            + "(CompoundStatement begin end)) . <eof>)",
            Dump("type T = 1..5; begin end."));
        Assert.Equal($"(InvocationExpression {Id("SizeOf")} (ArgumentList ( (Argument (PredefinedTypeExpression string)) )))", DumpExpression("SizeOf(string)"));
    }

    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-997")]
    [InlineData("const X = 42[0]; begin end.", "E1101(1,13): ';' expected but '[' found | E1100(1,16): Syntax error: unexpected ';'")]
    [InlineData("const X = 42^; begin end.", "E1101(1,13): ';' expected but '^' found")]
    [InlineData("const X = 'abc'(1); begin end.", "E1101(1,16): ';' expected but '(' found")]
    [InlineData("const X = nil.X; begin end.", "E1101(1,14): ';' expected but '.' found")]
    [InlineData("begin 'abc'.Print; end.", "E1100(1,7): Syntax error: unexpected ''abc''")]
    public void LiteralMemberAccess_Rejected(string text, string expected)
    {
        Assert.Equal(expected, string.Join(" | ", Diagnostics(text)));
    }
}
