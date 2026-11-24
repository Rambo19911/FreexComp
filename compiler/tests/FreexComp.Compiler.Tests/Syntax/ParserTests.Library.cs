using FreexComp.Compiler.Syntax;

namespace FreexComp.Compiler.Tests.Syntax;

/// <summary>dll-output.md §2: library files and the <c>exports</c> clause (LANG-540…546, CMP-1056; C-285).</summary>
public sealed partial class ParserTests
{
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-540")]
    [Trait("Req", "LANG-541")]
    [Trait("Req", "CMP-1056")]
    public void Library_HeadingUsesDeclarationsBlock()
    {
        var text = "library My.Lib;\r\nuses A, B in 'b.pas';\r\nvar X: Integer;\r\nprocedure P; begin end;\r\nexports P;\r\nbegin\r\n  X := 1;\r\nend.\r\n";

        Assert.Equal(
            "(Library library (QualifiedName (IdentifierName My) . (IdentifierName Lib)) ; "
            + "(UsesClause uses (UsedUnit (IdentifierName A)) , (UsedUnit (IdentifierName B) in 'b.pas') ;) "
            + "(Block (VarSection var (VarDeclaration (IdentifierName X) : (NamedType (IdentifierName Integer)) ;)) "
            + "(RoutineDeclaration (RoutineHeading procedure (IdentifierName P)) ; (Block (CompoundStatement begin end)) ;) "
            + "(ExportsClause exports (ExportItem (IdentifierName P)) ;) "
            + "(CompoundStatement begin (AssignmentStatement (IdentifierName X) := (LiteralExpression 1)) ; (EmptyStatement) end)) . <eof>)",
            Dump(text));

        var library = Assert.IsType<LibrarySyntax>(Parse(text).Root);
        Assert.Equal(SyntaxKind.Library, library.Kind);
        Assert.Equal(SyntaxKind.LibraryKeyword, library.LibraryKeyword.Kind);
        Assert.Equal("My.Lib", library.Name.ToString());
        Assert.Equal(2, library.UsesClause!.Units.Count);
        Assert.Equal(3, library.Block.Declarations.Count);
        Assert.IsType<ExportsClauseSyntax>(library.Block.Declarations[2]);
        Assert.False(library.Block.Body.BeginKeyword.IsMissing);
        Assert.Equal(SyntaxKind.EndOfFileToken, library.EndOfFileToken.Kind);
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-541")]
    public void Library_EndWithoutBlock()
    {
        var text = "library L;\r\nend.\r\n";

        Assert.Equal("(Library library (IdentifierName L) ; (Block (CompoundStatement <missing BeginKeyword> end)) . <eof>)", Dump(text));
        var library = Assert.IsType<LibrarySyntax>(Parse(text).Root);
        Assert.True(library.Block.Body.BeginKeyword.IsMissing);
        Assert.Empty(library.Block.Body.Statements);
        Assert.Null(library.UsesClause);

        // the heading is mandatory and has no parameter list; a library without 'begin' or 'end' is a syntax error
        Assert.Equal(["E1102(1,8): Identifier expected but ';' found"], Diagnostics("library;\r\nend."));
        Assert.Equal(["E1108(2,1): 'end.' expected but end of file found"], Diagnostics("library L;\r\n"));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-544")]
    [Trait("Req", "LANG-546")]
    [Trait("Req", "CMP-1056")]
    public void Exports_ItemsIndexNameEitherOrder()
    {
        var text = "library L;\r\nexports\r\n  A,\r\n  B index 7,\r\n  U.C name 'c_name',\r\n  D index N + 1 name 'd',\r\n  E name Prefix + 'e' index 3;\r\nexports Name, Index name 'i';\r\nend.\r\n";

        Assert.Equal(
            "(Library library (IdentifierName L) ; (Block "
            + "(ExportsClause exports (ExportItem (IdentifierName A)) , "
            + "(ExportItem (IdentifierName B) (ExportSpecifier index (LiteralExpression 7))) , "
            + "(ExportItem (QualifiedName (IdentifierName U) . (IdentifierName C)) (ExportSpecifier name (LiteralExpression 'c_name'))) , "
            + "(ExportItem (IdentifierName D) (ExportSpecifier index (BinaryExpression (IdentifierName N) + (LiteralExpression 1))) (ExportSpecifier name (LiteralExpression 'd'))) , "
            + "(ExportItem (IdentifierName E) (ExportSpecifier name (BinaryExpression (IdentifierName Prefix) + (LiteralExpression 'e'))) (ExportSpecifier index (LiteralExpression 3))) ;) "
            + "(ExportsClause exports (ExportItem (IdentifierName Name)) , (ExportItem (IdentifierName Index) (ExportSpecifier name (LiteralExpression 'i'))) ;) "
            + "(CompoundStatement <missing BeginKeyword> end)) . <eof>)",
            Dump(text));

        var library = (LibrarySyntax)Parse(text).Root;
        var items = ((ExportsClauseSyntax)library.Block.Declarations[0]).Items;
        Assert.Equal(5, items.Count);
        Assert.Null(items[0].IndexSpecifier);
        Assert.Null(items[0].NameSpecifier);
        Assert.Equal("7", items[1].IndexSpecifier!.Value.ToString());
        Assert.Equal(SyntaxKind.IndexKeyword, items[1].IndexSpecifier!.Keyword.ContextualKind);
        Assert.Equal("U.C", items[2].Name.ToString());
        Assert.Equal("'c_name'", items[2].NameSpecifier!.Value.ToString());
        Assert.Equal(SyntaxKind.NameKeyword, items[2].NameSpecifier!.Keyword.ContextualKind);
        // either order: the properties are by kind, not by position
        Assert.Equal("N + 1", items[3].IndexSpecifier!.Value.ToString());
        Assert.Equal("'d'", items[3].NameSpecifier!.Value.ToString());
        Assert.Equal("3", items[4].IndexSpecifier!.Value.ToString());
        Assert.Equal("Prefix + 'e'", items[4].NameSpecifier!.Value.ToString());
    }

    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-546")]
    [InlineData("library L;\r\nexports A index 1 index 2;\r\nend.", "E1100(2,19): Syntax error: unexpected 'index'", "1")]
    [InlineData("library L;\r\nexports A name 'a' name 'b', B;\r\nend.", "E1100(2,20): Syntax error: unexpected 'name'", null)]
    [InlineData("library L;\r\nexports A index 1 name 'a' index 3;\r\nend.", "E1100(2,28): Syntax error: unexpected 'index'", "1")]
    public void Exports_RepeatedSpecifierIsSyntaxError(string text, string expected, string? index)
    {
        Assert.Equal([expected], Diagnostics(text));

        // the first occurrence stays in the tree; the repeated one is skipped and the clause goes on
        var clause = (ExportsClauseSyntax)((LibrarySyntax)Parse(text).Root).Block.Declarations[0];
        Assert.Equal(index, clause.Items[0].IndexSpecifier?.Value.ToString());
        Assert.False(clause.SemicolonToken.IsMissing);
    }

    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-544")]
    [InlineData("program P;\r\nprocedure A; begin end;\r\nexports A;\r\nbegin\r\nend.", 3, 1)]
    [InlineData("unit U;\r\ninterface\r\nprocedure A;\r\nexports A;\r\nimplementation\r\nprocedure A; begin end;\r\nend.", 4, 1)]
    [InlineData("unit U;\r\ninterface\r\nimplementation\r\nprocedure A; begin end;\r\nexports A name 'x' index 2;\r\nend.", 5, 1)]
    [InlineData("library L;\r\nprocedure A;\r\n  procedure B; begin end;\r\n  exports B;\r\nbegin end;\r\nend.", 4, 3)]
    public void Exports_OutsideTheDeclarationPartOfALibraryFile_IsE2211_AndStillParsed(string text, int line, int column)
    {
        Assert.Equal([$"E2211({line},{column}): 'exports' is only allowed in a library file"], Diagnostics(text));
        Assert.Single(Parse(text).Root.DescendantNodes().OfType<ExportsClauseSyntax>());
    }
}
