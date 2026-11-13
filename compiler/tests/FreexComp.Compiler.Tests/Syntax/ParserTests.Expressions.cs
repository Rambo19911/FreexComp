using FreexComp.Compiler.Syntax;

namespace FreexComp.Compiler.Tests.Syntax;

/// <summary>grammar.md §7: precedence, designators, calls, typecasts, set constructors, <c>@</c>, <c>inherited</c> (C-016).</summary>
public sealed partial class ParserTests
{
    /// <summary>The dump of <paramref name="expression"/> as the value of <c>const X = …;</c>, which must parse cleanly.</summary>
    private static string DumpExpression(string expression)
    {
        var text = "const X = " + expression + "; begin end.";
        var tree = Parse(text);
        Assert.Equal(text, tree.Root.ToFullString());
        Assert.Empty(tree.Diagnostics);
        var section = Assert.IsType<ConstSectionSyntax>(((ProgramSyntax)tree.Root).Block.Declarations[0]);
        return SyntaxDump.Of(Assert.Single(section.Declarations).Value);
    }

    /// <summary>The diagnostics of <paramref name="expression"/> as the value of <c>const X = …;</c>.</summary>
    private static string[] ExpressionDiagnostics(string expression) => Diagnostics("const X = " + expression + "; begin end.");

    private static string Id(string name) => $"(IdentifierName {name})";

    private static string Bin(string left, string op, string right) => $"(BinaryExpression {left} {op} {right})";

    // ------------------------------------------------------------------ precedence (LANG-170, 171, LANG-300)

    [Theory]
    [Trait("Req", "LANG-170")]
    [InlineData("a + b * c = d - e", "(BinaryExpression (BinaryExpression (IdentifierName a) + (BinaryExpression (IdentifierName b) * (IdentifierName c))) = (BinaryExpression (IdentifierName d) - (IdentifierName e)))")]
    [InlineData("a or b and c", "(BinaryExpression (IdentifierName a) or (BinaryExpression (IdentifierName b) and (IdentifierName c)))")]
    [InlineData("a - b - c", "(BinaryExpression (BinaryExpression (IdentifierName a) - (IdentifierName b)) - (IdentifierName c))")]
    [InlineData("a div b mod c shl d", "(BinaryExpression (BinaryExpression (BinaryExpression (IdentifierName a) div (IdentifierName b)) mod (IdentifierName c)) shl (IdentifierName d))")]
    [InlineData("x as T is U", "(BinaryExpression (BinaryExpression (IdentifierName x) as (IdentifierName T)) is (IdentifierName U))")]
    [InlineData("a xor b in s", "(BinaryExpression (BinaryExpression (IdentifierName a) xor (IdentifierName b)) in (IdentifierName s))")]
    [InlineData("a / b <= c shr 2", "(BinaryExpression (BinaryExpression (IdentifierName a) / (IdentifierName b)) <= (BinaryExpression (IdentifierName c) shr (LiteralExpression 2)))")]
    public void Expressions_PrecedenceLevels(string expression, string expected)
    {
        Assert.Equal(expected, DumpExpression(expression));
    }

    [Fact]
    [Trait("Req", "LANG-171")]
    public void Expressions_UnaryOperators()
    {
        Assert.Equal($"(UnaryExpression - {Bin(Id("a"), "*", Id("b"))})", DumpExpression("-a * b"));
        Assert.Equal(Bin(Id("a"), "*", $"(UnaryExpression - {Id("b")})"), DumpExpression("a * -b"));
        Assert.Equal(Bin(Id("a"), "-", $"(UnaryExpression - {Id("b")})"), DumpExpression("a - -b"));
        Assert.Equal($"(UnaryExpression not (UnaryExpression not {Id("a")}))", DumpExpression("not not a"));
        Assert.Equal(Bin($"(UnaryExpression not {Id("a")})", "=", Id("b")), DumpExpression("not a = b"));
    }

    [Fact]
    public void Expressions_AddressOf_AppliesToTheWholeDesignator()
    {
        Assert.Equal(
            $"(AddressOfExpression @ (DereferenceExpression (MemberAccessExpression {Id("a")} . {Id("b")}) ^))",
            DumpExpression("@a.b^"));
        Assert.Equal($"(AddressOfExpression @ (AddressOfExpression @ {Id("p")}))", DumpExpression("@@p"));
        Assert.Equal(Bin($"(AddressOfExpression @ {Id("a")})", "=", "(LiteralExpression nil)"), DumpExpression("@a = nil"));
    }

    // ------------------------------------------------------------------ designators (LANG-313, 314)

    [Fact]
    public void Expressions_DesignatorSuffixesChainLeftToRight()
    {
        Assert.Equal(
            "(InvocationExpression (MemberAccessExpression (DereferenceExpression (ElementAccessExpression "
            + $"(MemberAccessExpression {Id("a")} . {Id("b")}) [ {Id("i")} , {Id("j")} ]) ^) . {Id("c")}) "
            + $"(ArgumentList ( (Argument {Id("x")}) , (Argument (LiteralExpression 1)) )))",
            DumpExpression("a.b[i, j]^.c(x, 1)"));
    }

    [Fact]
    public void Expressions_CallsWithAndWithoutArguments()
    {
        Assert.Equal($"(InvocationExpression {Id("f")} (ArgumentList ( )))", DumpExpression("f()"));
        Assert.Equal(
            $"(InvocationExpression (InvocationExpression {Id("f")} (ArgumentList ( (Argument {Id("x")}) ))) (ArgumentList ( )))",
            DumpExpression("f(x)()"));

        var invocation = Assert.IsType<InvocationExpressionSyntax>(
            ((ConstSectionSyntax)((ProgramSyntax)Parse("const X = f(a, b); begin end.").Root).Block.Declarations[0]).Declarations[0].Value);
        Assert.Equal(2, invocation.ArgumentList.Arguments.Count);
        Assert.Equal("b", invocation.ArgumentList.Arguments[1].Expression.ToString());
        Assert.Null(invocation.ArgumentList.Arguments[1].Width);
    }

    [Fact]
    public void Expressions_ArgumentsWithWidthAndDecimals()
    {
        Assert.Equal(
            $"(InvocationExpression {Id("Write")} (ArgumentList ( (Argument {Id("x")} : (LiteralExpression 10) : (LiteralExpression 2)) , "
            + $"(Argument {Id("s")} : {Bin(Id("w"), "+", "(LiteralExpression 1)")}) )))",
            DumpExpression("Write(x:10:2, s:w + 1)"));
    }

    [Fact]
    [Trait("Req", "LANG-314")]
    public void Expressions_TypecastsAreCallsAndTakeSuffixes()
    {
        Assert.Equal(
            $"(MemberAccessExpression (InvocationExpression {Id("TFoo")} (ArgumentList ( (Argument {Id("obj")}) ))) . {Id("Bar")})",
            DumpExpression("TFoo(obj).Bar"));
        Assert.Equal(
            $"(ElementAccessExpression (StringCastExpression string ( {Id("p")} )) [ (LiteralExpression 1) ])",
            DumpExpression("string(p)[1]"));
    }

    [Fact]
    public void Expressions_BareStringKeyword_IsAPredefinedTypeExpression()
    {
        Assert.Equal(
            $"(InvocationExpression {Id("SizeOf")} (ArgumentList ( (Argument (PredefinedTypeExpression string)) )))",
            DumpExpression("SizeOf(string)"));
        Assert.Equal($"(StringCastExpression string ( {Id("p")} ))", DumpExpression("string(p)"));

        var text = "const X = string; begin end.";
        var value = ((ConstSectionSyntax)((ProgramSyntax)Parse(text).Root).Block.Declarations[0]).Declarations[0].Value;
        Assert.Equal(SyntaxKind.StringKeyword, Assert.IsType<PredefinedTypeExpressionSyntax>(value).Keyword.Kind);
    }

    [Fact]
    public void Expressions_ParenthesisedExpressionTakesSuffixes()
    {
        Assert.Equal(
            $"(MemberAccessExpression (DereferenceExpression (ParenthesizedExpression ( {Id("p")} )) ^) . {Id("x")})",
            DumpExpression("(p)^.x"));
    }

    [Fact]
    public void Expressions_TypedConstantParenthesisTakesSuffixes()
    {
        Assert.Equal(
            "(ConstSection const (ConstDeclaration Q : (NamedType (IdentifierName Integer)) = (BinaryExpression "
            + $"(ElementAccessExpression (ParenthesizedExpression ( {Id("A")} )) [ (LiteralExpression 0) ]) + (LiteralExpression 1)) ;))",
            DumpDeclarations("const Q: Integer = (A)[0] + 1;"));
    }

    // ------------------------------------------------------------------ set constructors, nil, inherited

    [Fact]
    public void Expressions_SetConstructors()
    {
        Assert.Equal("(SetConstructor [ ])", DumpExpression("[]"));
        Assert.Equal(
            "(SetConstructor [ (SetElement (LiteralExpression 1)) , (SetElement (LiteralExpression 3) .. (LiteralExpression 5)) , "
            + $"(SetElement {Bin(Id("a"), "+", "(LiteralExpression 1)")}) ])",
            DumpExpression("[1, 3..5, a + 1]"));
        Assert.Equal(
            Bin(Id("c"), "in", $"(SetConstructor [ (SetElement (LiteralExpression 'a') .. (LiteralExpression 'z')) ])"),
            DumpExpression("c in ['a'..'z']"));
    }

    [Fact]
    public void Expressions_Nil()
    {
        Assert.Equal(Bin(Id("p"), "<>", "(LiteralExpression nil)"), DumpExpression("p <> nil"));
    }

    [Fact]
    public void Expressions_Inherited()
    {
        Assert.Equal("(InheritedExpression inherited)", DumpExpression("inherited"));
        Assert.Equal($"(InheritedExpression inherited {Id("Foo")})", DumpExpression("inherited Foo"));
        Assert.Equal(
            $"(MemberAccessExpression (InvocationExpression (InheritedExpression inherited {Id("Create")}) "
            + $"(ArgumentList ( (Argument (LiteralExpression 1)) ))) . {Id("Bar")})",
            DumpExpression("inherited Create(1).Bar"));
    }

    // ------------------------------------------------------------------ errors

    [Fact]
    public void Expressions_MissingOperand_IsE1103()
    {
        Assert.Equal(["E1103(1,14): Expression expected but ';' found"], ExpressionDiagnostics("a +"));
        Assert.Equal(["E1103(1,15): Expression expected but ')' found"], ExpressionDiagnostics("f(1,)"));
        Assert.Equal(["E1103(1,11): Expression expected but ')' found"], ExpressionDiagnostics(")"));
    }

    [Fact]
    public void Expressions_MissingClosingBracketOrName()
    {
        Assert.Equal(["E1101(1,14): ']' expected but ';' found"], ExpressionDiagnostics("a[1"));
        Assert.Equal(["E1101(1,14): ')' expected but ';' found"], ExpressionDiagnostics("f(1"));
        Assert.Equal(["E1102(1,13): Identifier expected but ';' found"], ExpressionDiagnostics("a."));
        Assert.Equal(["E1101(1,16): ']' expected but ';' found"], ExpressionDiagnostics("[1, 2"));
    }

    [Theory]
    [Trait("Req", "CMP-220")]
    [InlineData("const X = a.b[1, 2]^.c(x:1:2, [1..2], @y, inherited Z); begin end.")]
    [InlineData("const X = f(((; begin end.")]
    [InlineData("const X = [[[1..; begin end.")]
    [InlineData("const X = a.^[)(; begin end.")]
    public void Expressions_RoundTrip(string text)
    {
        Assert.Equal(text, Parse(text).Root.ToFullString());
    }
}
