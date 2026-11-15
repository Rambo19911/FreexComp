using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.LanguageService.Tests;

/// <summary>language-service.md §10 CMP-765: smart indent on Enter and bracket matching (C-140).</summary>
[Trait("Phase", "3")]
public sealed class IndentationTests
{
    private const string Path = @"C:\p\test.pas";

    /// <summary>The desired indentation of the line that holds the marker <c>$$</c> (removed before parsing).</summary>
    private static int Indent(string markup)
    {
        markup = markup.ReplaceLineEndings("\n");
        var caret = markup.IndexOf("$$", StringComparison.Ordinal);
        var text = markup.Remove(caret, 2);
        var tree = SyntaxTree.Parse(SourceText.From(Path, text));
        var line = text[..caret].Count(c => c == '\n');
        return IndentationService.GetDesiredIndentation(tree, line, 2);
    }

    [Fact]
    [Trait("Req", "CMP-765")]
    public void AfterBegin()
    {
        Assert.Equal(2, Indent("program P;\nbegin\n$$\nend."));
        Assert.Equal(4, Indent("program P;\nbegin\n  begin\n$$\n  end;\nend."));
    }

    [Fact]
    [Trait("Req", "CMP-765")]
    public void AfterAStatementInABlock_KeepsTheBlockIndentation()
    {
        Assert.Equal(2, Indent("program P;\nbegin\n  Writeln;\n$$\nend."));
        Assert.Equal(2, Indent("program P;\nbegin\n  begin\n  end;\n$$\nend."));
    }

    [Fact]
    [Trait("Req", "CMP-765")]
    public void AfterThen()
    {
        Assert.Equal(4, Indent("program P;\nbegin\n  if True then\n$$\nend."));
        Assert.Equal(4, Indent("program P;\nbegin\n  while True do\n$$\nend."));
    }

    [Fact]
    [Trait("Req", "CMP-765")]
    public void InClassBody()
    {
        Assert.Equal(4, Indent("unit U;\ninterface\ntype\n  TFoo = class\n$$\n  end;\nimplementation\nend."));
        Assert.Equal(4, Indent("unit U;\ninterface\ntype\n  TFoo = class\n  private\n    X: Integer;\n$$\n  end;\nimplementation\nend."));
    }

    [Fact]
    [Trait("Req", "CMP-765")]
    public void InCase()
    {
        Assert.Equal(4, Indent("program P;\nvar I: Integer;\nbegin\n  case I of\n$$\n  end;\nend."));
    }

    [Fact]
    [Trait("Req", "CMP-765")]
    public void InDeclarationSection()
    {
        Assert.Equal(2, Indent("program P;\nvar\n$$\nbegin end."));
        Assert.Equal(2, Indent("program P;\nvar\n  I: Integer;\n$$\nbegin end."));
    }

    [Fact]
    [Trait("Req", "CMP-765")]
    public void InTryAndRepeat()
    {
        Assert.Equal(4, Indent("program P;\nbegin\n  try\n$$\n  finally\n  end;\nend."));
        Assert.Equal(4, Indent("program P;\nbegin\n  repeat\n$$\n  until True;\nend."));
    }

    [Fact]
    public void OutsideAnyConstruct_IsZero()
    {
        Assert.Equal(0, Indent("$$program P;\nbegin end."));
        Assert.Equal(0, Indent("program P;\n$$\nbegin end."));
    }

    [Theory]
    [InlineData("program P; begin F(1, (2)) end.", "(1", ") e")]
    [InlineData("program P; begin F(1, (2)) end.", "(2", ")) ")]
    [InlineData("program P; var A: array[0..1] of Integer; begin end.", "[0", "] of")]
    [InlineData("program P; begin begin end end.", "begin end end", "end end.")]
    [InlineData("program P; begin begin end end.", "begin begin", "end.")]
    public void MatchingBracket(string text, string open, string close)
    {
        var tree = SyntaxTree.Parse(SourceText.From(Path, text));
        var openPos = text.IndexOf(open, StringComparison.Ordinal);
        var closePos = text.IndexOf(close, StringComparison.Ordinal);

        Assert.Equal(closePos, IndentationService.GetMatchingBracket(tree, openPos)?.Start);
        Assert.Equal(openPos, IndentationService.GetMatchingBracket(tree, closePos)?.Start);
    }

    [Fact]
    public void MatchingBracket_NoBracket_IsNull()
    {
        var tree = SyntaxTree.Parse(SourceText.From(Path, "program P; begin end."));

        Assert.Null(IndentationService.GetMatchingBracket(tree, 1));
        Assert.Equal(new TextSpan(17, 3), IndentationService.GetMatchingBracket(tree, 11));
    }
}
