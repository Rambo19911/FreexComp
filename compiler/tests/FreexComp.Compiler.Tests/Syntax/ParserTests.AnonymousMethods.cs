using FreexComp.Compiler.Syntax;

namespace FreexComp.Compiler.Tests.Syntax;

/// <summary>grammar.md §7 anonymousMethod and generics-anonymous.md §2 (LANG-720; C-019).</summary>
public sealed partial class ParserTests
{
    [Fact]
    [Trait("Req", "LANG-720")]
    public void AnonymousMethods_ProcedureAndFunctionExpressions()
    {
        Assert.Equal(
            "(AssignmentStatement (IdentifierName F) := (AnonymousMethodExpression procedure (Block (CompoundStatement begin end)))) ; "
            + "(AssignmentStatement (IdentifierName Result) := (AnonymousMethodExpression function (ParameterList ( "
            + "(Parameter (IdentifierName x) : (NamedType (IdentifierName Integer))) )) : (NamedType (IdentifierName Integer)) "
            + "(Block (CompoundStatement begin (AssignmentStatement (IdentifierName Result) := "
            + "(BinaryExpression (IdentifierName x) + (IdentifierName n))) end))))",
            DumpBody("F := procedure begin end; Result := function(x: Integer): Integer begin Result := x + n end"));
    }

    [Fact]
    public void AnonymousMethods_AsArgumentsWithLocalDeclarations()
    {
        Assert.Equal(
            "(ExpressionStatement (InvocationExpression (IdentifierName Run) (ArgumentList ( (Argument (AnonymousMethodExpression procedure "
            + "(ParameterList ( (Parameter const (IdentifierName S) : (StringType string)) )) "
            + "(Block (VarSection var (VarDeclaration (IdentifierName I) : (NamedType (IdentifierName Integer)) ;)) "
            + "(CompoundStatement begin (AssignmentStatement (IdentifierName I) := (LiteralExpression 1)) end)))) , "
            + "(Argument (LiteralExpression 2)) ))))",
            DumpBody("Run(procedure(const S: string) var I: Integer; begin I := 1 end, 2)"));

        var tree = Parse("begin F := function: Boolean begin end end.");
        Assert.Empty(tree.Diagnostics);
        var assignment = (AssignmentStatementSyntax)((ProgramSyntax)tree.Root).Block.Body.Statements[0];
        var method = Assert.IsType<AnonymousMethodExpressionSyntax>(assignment.Value);
        Assert.Equal(SyntaxKind.FunctionKeyword, method.Keyword.Kind);
        Assert.Null(method.ParameterList);
        Assert.Equal("Boolean", method.ResultType!.ToString());
        Assert.Equal("begin end", method.Block.Body.ToString());
    }

    [Fact]
    public void AnonymousMethods_NestedAndInsideIfStatements()
    {
        Assert.Equal(
            "(IfStatement if (IdentifierName B) then (AssignmentStatement (IdentifierName F) := (AnonymousMethodExpression procedure "
            + "(Block (CompoundStatement begin (AssignmentStatement (IdentifierName G) := (AnonymousMethodExpression procedure "
            + "(Block (CompoundStatement begin end)))) end)))) else (ExpressionStatement (IdentifierName Exit)))",
            DumpBody("if B then F := procedure begin G := procedure begin end end else Exit"));
    }

    [Fact]
    [Trait("Req", "LANG-721")]
    public void AnonymousMethods_SpecExampleParsesWithoutDiagnostics()
    {
        const string text = """
            program Anon;
            type TIntFunc = reference to function(x: Integer): Integer;
            function MakeAdder(n: Integer): TIntFunc;
            begin
              Result := function(x: Integer): Integer begin Result := x + n; n := n + 1 end;
            end;
            var f: TIntFunc;
            begin
              f := MakeAdder(10);
              Writeln(f(1), ' ', f(1));
            end.
            """;

        var tree = Parse(text);

        Assert.Equal(text, tree.Root.ToFullString());
        Assert.Empty(tree.Diagnostics);
    }

    [Fact]
    public void AnonymousMethods_FunctionWithoutResultType_IsE1101()
    {
        Assert.Equal(["E1101(1,21): ':' expected but 'begin' found"], Diagnostics("begin F := function begin end end."));
    }
}
