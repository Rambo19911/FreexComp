using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Tests.Syntax;

/// <summary>
/// Include files in the main tree (api.md addendum, lead decision option (a); LANG-875, 876, CMP-220): included tokens
/// are ordinary children at the directive's place with their own file positions; text and span queries of the tree
/// ignore them.
/// </summary>
public sealed partial class ParserTests
{
    private const string IncludePath = @"C:\p\part.inc";

    [Fact]
    [Trait("Req", "LANG-875")]
    public void Include_VarDeclarationFromAnInclude_IsARealChildOfTheBlock()
    {
        var fs = new InMemoryFileSystem().Add(IncludePath, "var\r\n  X: Integer;\r\n");
        var text = "program P;\r\n{$I part.inc}\r\nvar Y: Integer;\r\nbegin end.\r\n";

        var tree = Parse(text, fs);

        Assert.Empty(tree.Diagnostics);
        Assert.Equal(text, tree.Root.ToFullString());
        var block = ((ProgramSyntax)tree.Root).Block;
        Assert.Equal(2, block.Declarations.Count);
        var included = Assert.IsType<VarSectionSyntax>(block.Declarations[0]);
        Assert.Equal("X", included.Declarations[0].Identifiers[0].Identifier.Text);
        Assert.Same(block, included.Parent);

        // the included token keeps its own file-relative span …
        var x = included.Declarations[0].Identifiers[0].Identifier;
        Assert.Equal(new TextSpan(7, 1), x.Span);
        Assert.Equal("X", "var\r\n  X: Integer;\r\n".Substring(x.Span.Start, x.Span.Length));

        // … but the tree's text and span queries see only the main file
        Assert.Equal(new TextSpan(12, 0), included.Span);       // zero width after the heading's ';' and its line break
        Assert.Equal("", included.ToFullString());
        Assert.Equal("{$I part.inc}\r\nvar Y: Integer;\r\n", block.Declarations[1].ToFullString());   // the directive is leading trivia of 'var'
        Assert.Equal("{$I part.inc}\r\nvar Y: Integer;\r\nbegin end", block.ToFullString());
        Assert.Equal("var", tree.FindToken(text.IndexOf("var", StringComparison.Ordinal)).Text);
        Assert.Equal("var", tree.FindToken(14).Text);           // inside the directive: leading trivia of the main 'var'
        Assert.Equal(text.IndexOf("var", StringComparison.Ordinal), tree.FindToken(14).Span.Start);
        Assert.DoesNotContain(tree.DescendantTrivia(), t => t.Text.Contains("Integer;\r\n", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Req", "LANG-875")]
    public void Include_StatementsFromAnInclude_AreRealChildrenOfTheCompoundStatement()
    {
        var fs = new InMemoryFileSystem().Add(IncludePath, "begin end;\r\nbegin begin end end;\r\n");
        var text = "begin\r\n  {$I part.inc}\r\n  begin end\r\nend.\r\n";

        var tree = Parse(text, fs);

        Assert.Empty(tree.Diagnostics);
        Assert.Equal(text, tree.Root.ToFullString());
        Assert.Equal(
            "(Program (Block (CompoundStatement begin (CompoundStatement begin end) ; (CompoundStatement begin (CompoundStatement begin end) end) ; "
            + "(CompoundStatement begin end) end)) . <eof>)",
            SyntaxDump.Of(tree.Root));
        var body = ((ProgramSyntax)tree.Root).Block.Body;
        Assert.Equal(3, body.Statements.Count);
        Assert.Equal(TextSpan.FromBounds(0, text.IndexOf("end.", StringComparison.Ordinal) + 3), body.Span);
        Assert.Equal("begin end", body.Statements[2].ToString());
    }

    [Fact]
    [Trait("Req", "LANG-876")]
    public void Include_SyntaxErrorsInsideAnInclude_ReportTheIncludeFilesPosition()
    {
        var fs = new InMemoryFileSystem().Add(IncludePath, "var\r\n  X: ;\r\n");
        var text = "program P;\r\n{$I part.inc}\r\nbegin end.\r\n";

        var tree = Parse(text, fs);

        Assert.Equal(text, tree.Root.ToFullString());
        var diagnostic = Assert.Single(tree.Diagnostics);
        Assert.Equal("E1106", diagnostic.Code);
        Assert.Equal(IncludePath, diagnostic.Location.Path);
        Assert.Equal(new LinePosition(1, 5), diagnostic.Location.LineSpan.Start);
    }

    [Fact]
    public void Include_SkippedTokensOfAnInclude_NeverBecomeTriviaOfMainFileTokens()
    {
        var fs = new InMemoryFileSystem().Add(IncludePath, "garbage 1 2\r\n");
        var text = "program P;\r\n{$I part.inc}\r\nbegin end.\r\n";

        var tree = Parse(text, fs);

        Assert.Equal(text, tree.Root.ToFullString());
        var diagnostic = Assert.Single(tree.Diagnostics);
        Assert.Equal(("E1100", IncludePath), (diagnostic.Code, diagnostic.Location.Path));
    }
}
