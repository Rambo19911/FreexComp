using FreexComp.Compiler.Syntax;

namespace FreexComp.Compiler.Tests.Syntax;

/// <summary>
/// api.md §4 / §4.1: <c>SyntaxFactory.ParseNode&lt;T&gt;</c> parses a fragment (expression, statement, type, member or
/// routine declaration). The stand-alone expression form is the debugger's parser (pascal-values.md §9, G-042):
/// the node's <c>SyntaxTree.Diagnostics</c> hold the syntax errors with spans relative to the fragment text, and
/// surplus input after the expression is one E1100 on the first surplus token (C-141).
/// </summary>
[Trait("Phase", "3")]
[Trait("Kind", "Unit")]
public sealed class ParseNodeTests
{
    [Theory]
    [InlineData("1", typeof(LiteralExpressionSyntax))]
    [InlineData("'abc'#13", typeof(LiteralExpressionSyntax))]
    [InlineData("A + B * 2", typeof(BinaryExpressionSyntax))]
    [InlineData("not Done", typeof(UnaryExpressionSyntax))]
    [InlineData("P^.Next", typeof(MemberAccessExpressionSyntax))]
    [InlineData("List[I + 1]", typeof(ElementAccessExpressionSyntax))]
    [InlineData("Length(S)", typeof(InvocationExpressionSyntax))]
    [InlineData("(X)", typeof(ParenthesizedExpressionSyntax))]
    [InlineData("[1, 3..5]", typeof(SetConstructorSyntax))]
    [InlineData("Self", typeof(IdentifierNameSyntax))]
    [InlineData("  X in [1]  ", typeof(BinaryExpressionSyntax))]
    public void StandAloneExpression(string text, Type expected)
    {
        var node = SyntaxFactory.ParseNode<ExpressionSyntax>(text);

        Assert.IsType(expected, node);
        Assert.Empty(node.SyntaxTree.Diagnostics);
        Assert.Equal(text, node.SyntaxTree.Root.ToFullString());
        Assert.Equal(text.Trim(), node.ToString());
        Assert.Same(node.SyntaxTree.Root, node.Parent);
    }

    /// <summary>C-245: an empty fragment names what the fragment lacks (the category's existing code), not E1108 'end.' expected.</summary>
    [Fact]
    public void EmptyText_HasFragmentMessage()
    {
        var expression = SyntaxFactory.ParseNode<ExpressionSyntax>("  ").SyntaxTree.Diagnostics;
        var statement = SyntaxFactory.ParseNode<StatementSyntax>("").SyntaxTree.Diagnostics;
        var type = SyntaxFactory.ParseNode<TypeSyntax>("").SyntaxTree.Diagnostics;
        var member = SyntaxFactory.ParseNode<RoutineDeclarationSyntax>("").SyntaxTree.Diagnostics;

        Assert.Equal("E1103", Assert.Single(expression).Code);
        Assert.Equal("Expression expected but 'end of file' found", expression[0].Message);
        Assert.Equal("E1104", Assert.Single(statement).Code);
        Assert.Equal("E1106", Assert.Single(type).Code);
        Assert.Equal("E1107", Assert.Single(member).Code);
        Assert.Equal(2, expression[0].Location.Span.Start);
    }

    [Fact]
    public void StandAloneExpression_PrecedenceIsTheLanguages()
    {
        var node = Assert.IsType<BinaryExpressionSyntax>(SyntaxFactory.ParseNode<ExpressionSyntax>("1 + 2 * 3 = 7"));

        Assert.Equal(SyntaxKind.EqualsToken, node.OperatorToken.Kind);
        var left = Assert.IsType<BinaryExpressionSyntax>(node.Left);
        Assert.Equal(SyntaxKind.PlusToken, left.OperatorToken.Kind);
        Assert.IsType<BinaryExpressionSyntax>(left.Right);
    }

    [Fact]
    public void StandAloneExpression_TrailingInput_IsOneE1100OnTheFirstSurplusToken()
    {
        const string text = "A + B C D";
        var node = SyntaxFactory.ParseNode<ExpressionSyntax>(text);

        Assert.Equal("A + B", node.ToString());
        var diagnostic = Assert.Single(node.SyntaxTree.Diagnostics);
        Assert.Equal("E1100", diagnostic.Code);
        Assert.Equal(6, diagnostic.Location.Span.Start);
        Assert.Equal(1, diagnostic.Location.Span.Length);
        Assert.Equal(6, diagnostic.Location.LineSpan.Start.Column);
        Assert.Equal("Syntax error: unexpected 'C'", diagnostic.Message);
        Assert.Equal(text, node.SyntaxTree.Root.ToFullString());
    }

    [Fact]
    public void StandAloneExpression_TrailingSemicolon_IsSurplus()
    {
        var node = SyntaxFactory.ParseNode<ExpressionSyntax>("X;");

        var diagnostic = Assert.Single(node.SyntaxTree.Diagnostics);
        Assert.Equal("E1100", diagnostic.Code);
        Assert.Equal(1, diagnostic.Location.Span.Start);
    }

    [Fact]
    public void StandAloneExpression_MissingOperand_HasADiagnosticWithItsSpan()
    {
        var node = SyntaxFactory.ParseNode<ExpressionSyntax>("A + )");

        Assert.NotEmpty(node.SyntaxTree.Diagnostics);
        var first = node.SyntaxTree.Diagnostics[0];
        Assert.StartsWith("E1", first.Code, StringComparison.Ordinal);
        Assert.Equal(4, first.Location.Span.Start);
        Assert.Equal("A + )", node.SyntaxTree.Root.ToFullString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void StandAloneExpression_EmptyText_IsAnErrorNotAnException(string text)
    {
        var node = SyntaxFactory.ParseNode<ExpressionSyntax>(text);

        Assert.True(node.IsMissing);
        Assert.NotEmpty(node.SyntaxTree.Diagnostics);
        Assert.Equal(text, node.SyntaxTree.Root.ToFullString());
    }

    [Fact]
    public void StandAloneExpression_LexicalErrors_AreInTheDiagnostics()
    {
        var node = SyntaxFactory.ParseNode<ExpressionSyntax>("'open");

        Assert.Contains(node.SyntaxTree.Diagnostics, d => d.Code == "E1020" && d.Location.Span.Start == 0);
    }

    [Fact]
    public void StandAloneExpression_ConcreteTypeArgument_IsAccepted()
    {
        var node = SyntaxFactory.ParseNode<BinaryExpressionSyntax>("A - B");

        Assert.Equal(SyntaxKind.MinusToken, node.OperatorToken.Kind);
    }

    [Fact]
    public void WrongNodeType_Throws()
    {
        Assert.Throws<ArgumentException>(() => SyntaxFactory.ParseNode<BinaryExpressionSyntax>("A"));
    }

    [Theory]
    [InlineData("X := 1", typeof(AssignmentStatementSyntax))]
    [InlineData("begin A; B end", typeof(CompoundStatementSyntax))]
    [InlineData("if A then B else C", typeof(IfStatementSyntax))]
    [InlineData("WriteLn('x')", typeof(ExpressionStatementSyntax))]
    public void Statement(string text, Type expected)
    {
        var node = SyntaxFactory.ParseNode<StatementSyntax>(text);

        Assert.IsType(expected, node);
        Assert.Empty(node.SyntaxTree.Diagnostics);
    }

    [Theory]
    [InlineData("Integer", typeof(NamedTypeSyntax))]
    [InlineData("array of string", typeof(ArrayTypeSyntax))]
    [InlineData("set of Byte", typeof(SetTypeSyntax))]
    [InlineData("class(TObject) end", typeof(ClassTypeSyntax))]
    [InlineData("procedure(Sender: TObject) of object", typeof(ProceduralTypeSyntax))]
    public void Type(string text, Type expected)
    {
        var node = SyntaxFactory.ParseNode<TypeSyntax>(text);

        Assert.IsType(expected, node);
        Assert.Empty(node.SyntaxTree.Diagnostics);
    }

    [Theory]
    [InlineData("procedure Button1Click(Sender: TObject);", typeof(MethodDeclarationSyntax))]
    [InlineData("FCount: Integer;", typeof(FieldDeclarationSyntax))]
    [InlineData("property Count: Integer read FCount;", typeof(PropertyDeclarationSyntax))]
    [InlineData("function GetName: string; virtual; abstract;", typeof(MethodDeclarationSyntax))]
    public void Member(string text, Type expected)
    {
        var node = SyntaxFactory.ParseNode<MemberDeclarationSyntax>(text);

        Assert.IsType(expected, node);
        Assert.Empty(node.SyntaxTree.Diagnostics);
        Assert.Equal(text, node.ToString());
    }

    [Fact]
    public void RoutineImplementation()
    {
        const string text = "procedure TForm1.Button1Click(Sender: TObject);\r\nbegin\r\n  Close;\r\nend;\r\n";
        var node = SyntaxFactory.ParseNode<RoutineDeclarationSyntax>(text);

        Assert.Empty(node.SyntaxTree.Diagnostics);
        Assert.IsType<QualifiedNameSyntax>(node.Heading.Name);
        Assert.NotNull(node.Block);
        Assert.Equal(text, node.SyntaxTree.Root.ToFullString());
    }

    [Fact]
    public void Declarations()
    {
        Assert.Empty(SyntaxFactory.ParseNode<TypeDeclarationSyntax>("TFoo = class end;").SyntaxTree.Diagnostics);
        Assert.Empty(SyntaxFactory.ParseNode<VarDeclarationSyntax>("A, B: Integer;").SyntaxTree.Diagnostics);
        Assert.Empty(SyntaxFactory.ParseNode<ConstDeclarationSyntax>("N = 10;").SyntaxTree.Diagnostics);
        Assert.Empty(SyntaxFactory.ParseNode<UsesClauseSyntax>("uses A, B.C;").SyntaxTree.Diagnostics);
    }

    [Fact]
    public void UnsupportedNodeType_Throws()
    {
        Assert.Throws<ArgumentException>(() => SyntaxFactory.ParseNode<UnitSyntax>("unit A; interface implementation end."));
    }
}
