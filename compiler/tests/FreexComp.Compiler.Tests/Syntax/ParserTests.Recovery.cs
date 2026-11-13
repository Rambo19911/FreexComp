using FreexComp.Compiler.Syntax;

namespace FreexComp.Compiler.Tests.Syntax;

/// <summary>
/// grammar.md §8 (LANG-180…184) and the specific E11xx codes of diagnostics.md §4: missing tokens, skipped tokens,
/// synchronisation points, one error per cause (C-020).
/// </summary>
public sealed partial class ParserTests
{
    // ------------------------------------------------------------------ missing tokens (LANG-181)

    [Theory]
    [Trait("Req", "LANG-181")]
    [InlineData("begin\r\n  x := 1\r\n  y := 2;\r\nend.", "E1101(3,3): ';' expected but 'y' found")]
    [InlineData("var A: Integer B: Integer; begin end.", "E1101(1,16): ';' expected but 'B' found")]
    [InlineData("begin if x y := 1 end.", "E1101(1,12): 'then' expected but 'y' found")]
    [InlineData("begin while x y := 1 end.", "E1101(1,15): 'do' expected but 'y' found")]
    [InlineData("begin for i := 1 to 10 x := 1 end.", "E1101(1,24): 'do' expected but 'x' found")]
    [InlineData("begin case x 1: ; end end.", "E1101(1,14): 'of' expected but '1' found")]
    [InlineData("begin F(1, 2; x := 3 end.", "E1101(1,13): ')' expected but ';' found")]
    [InlineData("begin x := a[1; y := 2 end.", "E1101(1,15): ']' expected but ';' found")]
    [InlineData("type T = (a, b; U = Integer; begin end.", "E1101(1,15): ')' expected but ';' found")]
    [InlineData("type T = class(TObject procedure X; end; begin end.", "E1101(1,24): ')' expected but 'procedure' found")]
    [InlineData("unit U; interface procedure P; implementation procedure P begin end; end.", "E1101(1,59): ';' expected but 'begin' found")]
    [InlineData("begin repeat x := 1 end.", "E1101(1,21): 'until' expected but 'end' found")]
    public void Recovery_MissingTokenIsInsertedWithOneE1101(string text, string expected)
    {
        Assert.Equal([expected], Diagnostics(text));
    }

    [Fact]
    [Trait("Req", "LANG-181")]
    public void Recovery_MissingSemicolonBetweenCaseArms_IsInserted()
    {
        var text = "begin case x of 1: y := 1 2: y := 2 end end.";

        Assert.Equal(["E1101(1,27): ';' expected but '2' found"], Diagnostics(text));
        var caseStatement = (CaseStatementSyntax)((ProgramSyntax)Parse(text).Root).Block.Body.Statements[0];
        Assert.Equal(2, caseStatement.Arms.Count);
        Assert.True(caseStatement.Arms.Separators.Single().IsMissing);
    }

    // ------------------------------------------------------------------ synchronisation points (LANG-183)

    [Fact]
    [Trait("Req", "LANG-183")]
    public void Recovery_MissingEndBeforeANestedRoutine_StopsTheStatementList()
    {
        var text = "procedure P; begin x := 1; procedure Q; begin end; begin end.";

        Assert.Equal(["E1101(1,28): 'end' expected but 'procedure' found"], Diagnostics(text));
        var declarations = ((ProgramSyntax)Parse(text).Root).Block.Declarations;
        Assert.Equal(["P", "Q"], declarations.Cast<RoutineDeclarationSyntax>().Select(r => r.Heading.Name.ToString()));
        Assert.True(((RoutineDeclarationSyntax)declarations[0]).Block!.Body.EndKeyword.IsMissing);
    }

    [Theory]
    [Trait("Req", "LANG-183")]
    [InlineData("unit U; interface implementation procedure P; begin x := 1; initialization end.",
        "E1101(1,61): 'end' expected but 'initialization' found")]
    [InlineData("unit U; interface implementation procedure P; begin x := 1; type T = Integer; end.",
        "E1101(1,61): 'end' expected but 'type' found")]
    [InlineData("type T = record X: Integer; begin end.", "E1101(1,29): 'end' expected but 'begin' found")]
    [InlineData("type T = class X: Integer; type U = Integer; begin end.", "E1101(1,46): 'end' expected but 'begin' found")]
    [InlineData("type T = class procedure X; begin end.", "E1101(1,29): 'end' expected but 'begin' found")]
    public void Recovery_UnterminatedBlocksAndTypes_ResynchroniseAtDeclarationKeywords(string text, string expected)
    {
        Assert.Equal([expected], Diagnostics(text));
    }

    [Fact]
    [Trait("Req", "LANG-182")]
    public void Recovery_GarbageInAStatementList_IsSkippedUpToTheNextStatement()
    {
        var text = "begin x := 1; ) ] ; y := 2 end.";

        Assert.Equal(["E1100(1,15): Syntax error: unexpected ')'"], Diagnostics(text));
        Assert.Equal(
            "(AssignmentStatement (IdentifierName x) := (LiteralExpression 1)) ; (EmptyStatement) ; "
            + "(AssignmentStatement (IdentifierName y) := (LiteralExpression 2))",
            string.Join(" ", ((ProgramSyntax)Parse(text).Root).Block.Body.Statements.GetWithSeparators()
                .Select(item => item.AsNode() is { } node ? SyntaxDump.Of(node) : item.AsToken().Text)));
    }

    [Fact]
    [Trait("Req", "LANG-182")]
    public void Recovery_GarbageBeforeAnAttributedRoutine_StopsAtTheAttribute()
    {
        var text = "program Q; 123 [Test] procedure P; begin end; begin end.";

        Assert.Equal(["E1100(1,12): Syntax error: unexpected '123'"], Diagnostics(text));
        Assert.Single(((RoutineDeclarationSyntax)((ProgramSyntax)Parse(text).Root).Block.Declarations[0]).Attributes);
    }

    [Fact]
    [Trait("Req", "LANG-180")]
    public void Recovery_SeveralIndependentErrors_AreEachReportedOnce()
    {
        Assert.Equal(
            [
                "E1106(1,8): Type expected but ';' found",
                "E1103(1,33): Expression expected but ';' found",
                "E1101(1,42): ')' expected but ';' found",
            ],
            Diagnostics("var A: ; B: Integer; begin x := ; y := (1; end."));
    }

    // ------------------------------------------------------------------ specific codes

    [Theory]
    [InlineData("var begin: Integer; begin end.", "E1109(1,5): 'begin' is a reserved word and cannot be used as an identifier")]
    [InlineData("procedure end; begin end; begin end.", "E1109(1,11): 'end' is a reserved word and cannot be used as an identifier")]
    [InlineData("const type = 1; begin end.", "E1109(1,7): 'type' is a reserved word and cannot be used as an identifier")]
    [InlineData("procedure P(var of: Integer); begin end; begin end.", "E1109(1,17): 'of' is a reserved word and cannot be used as an identifier")]
    [InlineData("type T = record A, do: Byte; end; begin end.", "E1109(1,20): 'do' is a reserved word and cannot be used as an identifier")]
    public void Recovery_ReservedWordAsDeclaredName_IsE1109AndTheDeclarationParses(string text, string expected)
    {
        Assert.Equal([expected], Diagnostics(text));
    }

    [Fact]
    public void Recovery_ReservedWordAsDeclaredName_KeepsTheKeywordTokenAsTheName()
    {
        Assert.Equal(
            "(Program (Block (VarSection var (VarDeclaration (IdentifierName begin) : (NamedType (IdentifierName Integer)) ;)) "
            + "(CompoundStatement begin end)) . <eof>)",
            Dump("var begin: Integer; begin end.", expectNoDiagnostics: false));
    }

    [Fact]
    public void Recovery_ReservedWordWhereNoNameIsDeclared_StaysE1102()
    {
        Assert.Equal(["E1102(1,10): Identifier expected but 'end' found"], Diagnostics("begin x. end."));
    }

    [Theory]
    [InlineData("type T = class procedure X; forward; end; begin end.", "E1114(1,29): Directive 'forward' is not allowed here")]
    [InlineData("unit U; interface procedure P; forward; implementation end.", "E1114(1,32): Directive 'forward' is not allowed here")]
    [InlineData("procedure P; overload; overload; begin end; begin end.", "E1115(1,24): Duplicate directive 'overload'")]
    [InlineData("type T = class procedure X; virtual; abstract; Virtual; end; begin end.", "E1115(1,48): Duplicate directive 'Virtual'")]
    public void Recovery_DirectiveErrors(string text, string expected)
    {
        Assert.Equal([expected], Diagnostics(text));
    }

    [Fact]
    public void Recovery_ForwardOutsideInterfaceAndMethodImplementationDirectives_AreAccepted()
    {
        Assert.Empty(Diagnostics("procedure P; forward; procedure TFoo.Destroy; override; begin end; procedure P; begin end; begin end."));
    }

    // ------------------------------------------------------------------ the tree is always complete (LANG-180)

    [Theory]
    [Trait("Req", "LANG-180")]
    [InlineData("begin x := 1; type")]
    [InlineData("unit U; interface implementation procedure P; begin")]
    [InlineData("type T = class(")]
    [InlineData("type T<A: = ")]
    [InlineData("begin x := procedure(")]
    [InlineData("[A(")]
    public void Recovery_TruncatedInputs_GiveACompleteTreeEndingAtEndOfFile(string text) => AssertCompleteTree(text);

    [Theory]
    [Trait("Req", "LANG-180")]
    [InlineData("begin x := ", "(", 20000, " end.")]
    [InlineData("begin x := ", "-", 20000, "1 end.")]
    [InlineData("", "begin ", 5000, "end.")]
    [InlineData("begin ", "if x then ", 5000, "; end.")]
    [InlineData("type T = ", "record A: ", 3000, "Byte; end.")]
    [InlineData("const C = ", "(", 20000, "1);")]
    [InlineData("var L: ", "TList<", 5000, "Integer; begin end.")]
    [InlineData("", "procedure P; ", 5000, "begin end.")]
    public void Recovery_DeepNesting_ReportsE1120(string prefix, string repeated, int count, string suffix)
    {
        var text = prefix + string.Concat(Enumerable.Repeat(repeated, count)) + suffix;

        var tree = AssertCompleteTree(text);

        // The rest of the file is given up with exactly one E1120 (C-170); no E1100 for the abandoned tokens.
        var nesting = Assert.Single(tree.Diagnostics, d => d.Code == "E1120");
        Assert.Equal("Nesting too deep", nesting.Message);
        Assert.DoesNotContain(tree.Diagnostics, d => d.Code == "E1100");
    }

    private static SyntaxTree AssertCompleteTree(string text)
    {
        var tree = Parse(text);

        Assert.Equal(text, tree.Root.ToFullString());
        Assert.Equal(SyntaxKind.EndOfFileToken, tree.Root.LastToken().Kind);
        Assert.NotEmpty(tree.Diagnostics);
        return tree;
    }
}
