using System.Collections.Immutable;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Tests.Syntax;

/// <summary>
/// docs/spec/language/grammar.md, one positive test per production (tree shape as an S-expression,
/// <see cref="SyntaxDump"/>) plus the error cases. Groups: CompilationUnit* (§1, C-012), Declarations* (§2, C-013), …
/// </summary>
[Trait("Phase", "2")]
[Trait("Kind", "Unit")]
public sealed partial class ParserTests
{
    private const string Path = @"C:\p\test.pas";

    private static SyntaxTree Parse(string text, IFileSystem? fs = null) =>
        SyntaxTree.Parse(SourceText.From(Path, text), null, fs);

    /// <summary>Parses, asserts the round trip (CMP-220) and returns the S-expression of the root.</summary>
    private static string Dump(string text, bool expectNoDiagnostics = true)
    {
        var tree = Parse(text);
        Assert.Equal(text, tree.Root.ToFullString());
        if (expectNoDiagnostics)
        {
            Assert.Empty(tree.Diagnostics);
        }

        return SyntaxDump.Of(tree.Root);
    }

    /// <summary>"CODE(line,col): message" for every diagnostic, 1-based.</summary>
    private static string[] Diagnostics(string text)
    {
        var tree = Parse(text);
        Assert.Equal(text, tree.Root.ToFullString());
        return [.. tree.Diagnostics.Select(d =>
            $"{d.Code}({d.Location.LineSpan.Start.Line + 1},{d.Location.LineSpan.Start.Column + 1}): {d.Message}")];
    }

    private const string EmptyBlock = "(Block (CompoundStatement begin end))";

    // ------------------------------------------------------------------ programs (grammar.md §1)

    [Fact]
    [Trait("Req", "LANG-150")]
    public void CompilationUnit_ProgramWithHeading()
    {
        var text = "program Hello;\r\nbegin\r\nend.\r\n";

        Assert.Equal($"(Program program (IdentifierName Hello) ; {EmptyBlock} . <eof>)", Dump(text));
        var program = Assert.IsType<ProgramSyntax>(Parse(text).Root);
        Assert.Equal("Hello", program.Name!.Identifier.Text);
        Assert.Equal(SyntaxKind.EndOfFileToken, program.EndOfFileToken.Kind);
        Assert.Equal(SyntaxKind.DotToken, program.DotToken.Kind);
        Assert.Empty(program.Block.Declarations);
    }

    [Fact]
    public void CompilationUnit_ProgramWithoutHeading()
    {
        Assert.Equal($"(Program {EmptyBlock} . <eof>)", Dump("begin end."));
        var program = (ProgramSyntax)Parse("begin end.").Root;
        Assert.Null(program.ProgramKeyword);
        Assert.Null(program.Name);
        Assert.Null(program.SemicolonToken);
        Assert.Null(program.UsesClause);
    }

    [Fact]
    public void CompilationUnit_ProgramParameterListIsKept()
    {
        Assert.Equal(
            "(Program program (IdentifierName P) (ProgramParameterList ( (IdentifierName Input) , (IdentifierName Output) )) ; "
            + EmptyBlock + " . <eof>)",
            Dump("program P(Input, Output); begin end."));
    }

    [Fact]
    public void CompilationUnit_ProgramUsesClause_WithDottedNamesAndInPaths()
    {
        Assert.Equal(
            "(Program (UsesClause uses (UsedUnit (IdentifierName A)) , "
            + @"(UsedUnit (QualifiedName (QualifiedName (IdentifierName B) . (IdentifierName C)) . (IdentifierName D)) in 'sub\d.pas') ;) "
            + EmptyBlock + " . <eof>)",
            Dump(@"uses A, B.C.D in 'sub\d.pas'; begin end."));

        var uses = ((ProgramSyntax)Parse("uses A, B in 'b.pas'; begin end.").Root).UsesClause!;
        Assert.Equal(2, uses.Units.Count);
        Assert.Null(uses.Units[0].InKeyword);
        Assert.Equal("'b.pas'", uses.Units[1].Path!.Value.Text);
        Assert.Equal("b.pas", uses.Units[1].Path!.Value.Value);
    }

    // ------------------------------------------------------------------ units

    [Fact]
    [Trait("Req", "LANG-152")]
    public void CompilationUnit_UnitSkeleton()
    {
        Assert.Equal(
            "(Unit unit (QualifiedName (IdentifierName Foo) . (IdentifierName Bar)) ; "
            + "(InterfaceSection interface (UsesClause uses (UsedUnit (IdentifierName A)) ;)) "
            + "(ImplementationSection implementation (UsesClause uses (UsedUnit (IdentifierName B)) ;)) end . <eof>)",
            Dump("unit Foo.Bar;\r\ninterface\r\nuses A;\r\nimplementation\r\nuses B;\r\nend.\r\n"));
    }

    [Fact]
    public void CompilationUnit_UnitInitializationAndFinalization()
    {
        Assert.Equal(
            "(Unit unit (IdentifierName U) ; (InterfaceSection interface) (ImplementationSection implementation) "
            + "(InitializationSection initialization (CompoundStatement begin end) ; (EmptyStatement)) "
            + "(FinalizationSection finalization (EmptyStatement) ; (EmptyStatement)) end . <eof>)",
            Dump("unit U; interface implementation initialization begin end; finalization ; end."));

        var unit = (UnitSyntax)Parse("unit U; interface implementation initialization finalization end.").Root;
        Assert.Equal(SyntaxKind.InitializationKeyword, unit.InitializationSection!.Keyword.Kind);
        Assert.NotNull(unit.FinalizationSection);
        Assert.Equal(SyntaxKind.EndKeyword, unit.EndKeyword.Kind);
    }

    [Fact]
    public void CompilationUnit_UnitBeginEndIsAnInitializationSection()
    {
        Assert.Equal(
            "(Unit unit (IdentifierName U) ; (InterfaceSection interface) (ImplementationSection implementation) "
            + "(InitializationSection begin) end . <eof>)",
            Dump("unit U; interface implementation begin end."));
        Assert.Equal(
            "(Unit unit (IdentifierName U) ; (InterfaceSection interface) (ImplementationSection implementation) end . <eof>)",
            Dump("unit U; interface implementation end."));
    }

    [Fact]
    public void CompilationUnit_UnitHintDirectives_AreContextualKeywords()
    {
        var text = "unit U deprecated 'old' platform experimental; interface implementation end.";

        Assert.Equal(
            "(Unit unit (IdentifierName U) (HintDirective deprecated 'old') (HintDirective platform) (HintDirective experimental) ; "
            + "(InterfaceSection interface) (ImplementationSection implementation) end . <eof>)",
            Dump(text));
        var hints = ((UnitSyntax)Parse(text).Root).HintDirectives;
        Assert.Equal(
            [SyntaxKind.DeprecatedKeyword, SyntaxKind.PlatformKeyword, SyntaxKind.ExperimentalKeyword],
            hints.Select(h => h.Keyword.ContextualKind));
        Assert.All(hints, h => Assert.Equal(SyntaxKind.IdentifierToken, h.Keyword.Kind));
        Assert.Equal("'old'", hints[0].Message!.Value.Text);
    }

    // ------------------------------------------------------------------ statement lists of the skeleton

    [Fact]
    public void CompilationUnit_EmptyStatementsAroundSeparators()
    {
        Assert.Equal(
            "(Program (Block (CompoundStatement begin (EmptyStatement) ; (EmptyStatement) end)) . <eof>)",
            Dump("begin ; end."));
        var body = ((ProgramSyntax)Parse("begin ; end.").Root).Block.Body;
        Assert.Equal(2, body.Statements.Count);
        Assert.Equal(new TextSpan(6, 0), body.Statements[0].Span);
        Assert.Equal(new TextSpan(8, 0), body.Statements[1].Span);   // empty: zero width after the previous token's trivia
    }

    [Fact]
    public void CompilationUnit_NestedCompoundStatements()
    {
        Assert.Equal(
            "(Program (Block (CompoundStatement begin (CompoundStatement begin end) ; (CompoundStatement begin end) end)) . <eof>)",
            Dump("begin begin end; begin end end."));
    }

    // ------------------------------------------------------------------ errors and recovery (LANG-150, 151, 181…184)

    [Fact]
    [Trait("Req", "LANG-150")]
    public void CompilationUnit_TextAfterFinalEnd_IsHintAndSkippedTrivia()
    {
        var text = "begin end. junk 1\r\n";

        Assert.Equal(["H1001(1,12): Text after final 'end.' is ignored"], Diagnostics(text));
        var eof = ((ProgramSyntax)Parse(text).Root).EndOfFileToken;
        Assert.Equal(
            ["junk", "1"],
            eof.LeadingTrivia.Where(t => t.Kind == SyntaxKind.SkippedTokensTrivia).Select(t => t.Text));
    }

    [Fact]
    [Trait("Req", "LANG-181")]
    public void CompilationUnit_MissingSemicolonAfterHeading_IsInserted()
    {
        var text = "program P begin end.";

        Assert.Equal(["E1101(1,11): ';' expected but 'begin' found"], Diagnostics(text));
        Assert.Equal($"(Program program (IdentifierName P) <missing SemicolonToken> {EmptyBlock} . <eof>)", Dump(text, false));
        var semicolon = ((ProgramSyntax)Parse(text).Root).SemicolonToken!.Value;
        Assert.True(semicolon.IsMissing);
        Assert.Equal(new TextSpan(10, 0), semicolon.Span);
    }

    [Fact]
    [Trait("Req", "LANG-181")]
    public void CompilationUnit_MissingSemicolonBetweenStatements()
    {
        Assert.Equal(["E1101(1,17): ';' expected but 'begin' found"], Diagnostics("begin begin end begin end end."));
    }

    [Fact]
    public void CompilationUnit_EndOfFileBeforeFinalEnd_IsE1108()
    {
        Assert.Equal(["E1108(1,10): 'end.' expected but end of file found"], Diagnostics("begin end"));
        Assert.Equal($"(Program {EmptyBlock} <missing DotToken> <eof>)", Dump("begin end", false));
        Assert.Equal(["E1108(1,1): 'end.' expected but end of file found"], Diagnostics(""));
        Assert.Equal(
            "(Program (Block (CompoundStatement <missing BeginKeyword> <missing EndKeyword>)) <missing DotToken> <eof>)",
            Dump("", false));
    }

    [Fact]
    public void CompilationUnit_UnitWithoutInterface_IsE1112()
    {
        var text = "unit U; implementation end.";

        Assert.Equal(["E1112(1,9): 'interface' expected but 'implementation' found"], Diagnostics(text));
        Assert.Equal(
            "(Unit unit (IdentifierName U) ; (InterfaceSection <missing InterfaceKeyword>) (ImplementationSection implementation) end . <eof>)",
            Dump(text, false));
    }

    [Fact]
    public void CompilationUnit_UnitWithoutImplementation_IsE1113()
    {
        Assert.Equal(["E1113(1,19): 'implementation' expected but 'end' found"], Diagnostics("unit U; interface end."));
    }

    [Fact]
    [Trait("Req", "LANG-182")]
    public void CompilationUnit_UnexpectedTokens_AreSkippedIntoTriviaWithOneE1100()
    {
        var text = "program P; 123 456 begin end.";

        Assert.Equal(["E1100(1,12): Syntax error: unexpected '123'"], Diagnostics(text));
        var begin = ((ProgramSyntax)Parse(text).Root).Block.Body.BeginKeyword;
        Assert.Equal(
            [(SyntaxKind.SkippedTokensTrivia, "123"), (SyntaxKind.WhitespaceTrivia, " "), (SyntaxKind.SkippedTokensTrivia, "456"), (SyntaxKind.WhitespaceTrivia, " ")],
            begin.LeadingTrivia.Select(t => (t.Kind, t.Text)));
    }

    [Fact]
    public void CompilationUnit_FirstTokenCannotStartAProgram_IsE1111()
    {
        Assert.Equal(["E1111(1,1): Unit or program heading expected"], Diagnostics("end."));
        Assert.Equal("(Program (Block (CompoundStatement <missing BeginKeyword> end)) . <eof>)", Dump("end.", false));
    }

    [Theory]
    [Trait("Req", "LANG-151")]
    [InlineData("package L; begin end.", "package")]
    public void CompilationUnit_Package_IsNotSupported(string text, string word)   // 'library': ParserTests.Library (LANG-540)
    {
        Assert.Equal([$"E2900(1,1): Feature not supported: {word}"], Diagnostics(text));
        Assert.Equal($"(Program {word} (IdentifierName L) ; {EmptyBlock} . <eof>)", Dump(text, false));
    }

    [Fact]
    [Trait("Req", "LANG-184")]
    public void CompilationUnit_AtMost100SyntaxErrors_ThenE1199()
    {
        var text = "begin " + string.Concat(Enumerable.Repeat("begin end ", 120)) + "end.";

        var diagnostics = Diagnostics(text);

        Assert.Equal(101, diagnostics.Length);
        Assert.All(diagnostics.Take(100), d => Assert.StartsWith("E1101", d));
        Assert.StartsWith("E1199", diagnostics[100]);
    }

    // ------------------------------------------------------------------ include files

    [Fact]
    [Trait("Req", "CMP-220")]
    public void CompilationUnit_IncludeFiles_AlsoGetSeparateFlatTrees()
    {
        var fs = new InMemoryFileSystem().Add(@"C:\p\part.inc", "{ inc }\r\nconst X = 1;\r\n");
        var text = "program P;\r\n{$I part.inc}\r\nbegin end.\r\n";

        var tree = Parse(text, fs);

        Assert.Equal(text, tree.Root.ToFullString());
        Assert.Empty(tree.Diagnostics);
        Assert.Equal(
            "(Program program (IdentifierName P) ; (Block (ConstSection const (ConstDeclaration X = (LiteralExpression 1) ;)) "
            + "(CompoundStatement begin end)) . <eof>)",
            SyntaxDump.Of(tree.Root));
        var included = Assert.Single(tree.IncludedTrees);
        Assert.Equal(@"C:\p\part.inc", included.Text.Path);
        Assert.IsType<IncludeFileSyntax>(included.Root);
        Assert.Equal("{ inc }\r\nconst X = 1;\r\n", included.Root.ToFullString());
        Assert.Equal("(IncludeFile const X = 1 ; <eof>)", SyntaxDump.Of(included.Root));
    }

    // ------------------------------------------------------------------ round trip and robustness (CMP-220, LANG-180)

    [Theory]
    [Trait("Req", "CMP-220")]
    [InlineData("")]
    [InlineData("program")]
    [InlineData("unit")]
    [InlineData("unit U; interface implementation initialization begin end; finalization end. trailing")]
    [InlineData("program P(; uses ,,; var x: Integer; begin x := 1; if x then begin end end.")]
    [InlineData("{$IFDEF X} program Q; {$ENDIF} // c\r\nbegin (* c *) end.{ after }")]
    [InlineData("uses A in; uses B; begin end")]
    public void CompilationUnit_RoundTrip(string text)
    {
        Assert.Equal(text, Parse(text).Root.ToFullString());
    }

    [Fact]
    [Trait("Req", "LANG-180")]
    public void CompilationUnit_RandomTokenSequences_NeverThrowAndRoundTrip()
    {
        string[] vocabulary =
        [
            "program", "unit", "interface", "implementation", "initialization", "finalization", "uses", "in", "begin",
            "end", ".", ";", ",", "(", ")", "A", "B", "'s'", "1", "deprecated", "library", "var", "{c}", "\r\n", " ",
        ];
        var random = new Random(12345);
        for (var i = 0; i < 500; i++)
        {
            var text = string.Concat(Enumerable.Range(0, random.Next(0, 30)).Select(_ => vocabulary[random.Next(vocabulary.Length)] + " "));
            var tree = Parse(text);
            Assert.Equal(text, tree.Root.ToFullString());
            Assert.Equal(SyntaxKind.EndOfFileToken, tree.Root.LastToken().Kind);
        }
    }
}
