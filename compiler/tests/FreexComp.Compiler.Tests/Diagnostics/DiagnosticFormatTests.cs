using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Tests.Diagnostics;

/// <summary>Text and JSON forms of docs/interfaces/compiler-cli.md §2; templates per CMP-502.</summary>
[Trait("Phase", "1")]
public sealed class DiagnosticFormatTests
{
    private static readonly SourceText Source = SourceText.From(@"C:\p\Unit1.pas", "program P;\r\nbegin\r\n  Foo;\r\nend.\r\n");

    [Fact]
    [Trait("Req", "CMP-502")]
    public void Format_FillsPlaceholdersLeftToRight()
    {
        Assert.Equal(
            "Incompatible types: 'Integer' and 'string'",
            DiagnosticCatalog.Get("E2010").Format("Integer", "string"));
        Assert.Equal("Too many errors; stopping after 100", DiagnosticCatalog.Get("E0010").Format(100));
        Assert.Equal("Build cancelled", DiagnosticCatalog.Get("E9010").Format());
    }

    [Fact]
    [Trait("Req", "CMP-502")]
    public void Format_UsesInvariantCulture()
    {
        Assert.Equal("Constant value 1.5 is out of range for type 'Byte'", DiagnosticCatalog.Get("E2012").Format(1.5, "Byte"));
    }

    [Fact]
    [Trait("Req", "CMP-502")]
    public void Format_WrongArgumentCount_Throws()
    {
        Assert.Throws<ArgumentException>(() => DiagnosticCatalog.Get("E2010").Format("Integer"));
        Assert.Throws<ArgumentException>(() => DiagnosticCatalog.Get("E2003").Format("a", "b"));
    }

    [Fact]
    public void Create_TakesSeverityAndMessageFromCatalogue()
    {
        var d = Diagnostic.Create("E2003", Loc(21, 3), "Foo");

        Assert.Equal("E2003", d.Code);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Equal("Undeclared identifier 'Foo'", d.Message);
        Assert.Equal(["Foo"], d.Arguments);
        Assert.Empty(d.Related);
    }

    [Fact]
    [Trait("Req", "LANG-101")]
    public void ToText_PrintsOneBasedLineAndColumn()
    {
        var d = Diagnostic.Create("E2003", Loc(21, 3), "Foo");

        Assert.Equal(@"C:\p\Unit1.pas(3,3): error E2003: Undeclared identifier 'Foo'", d.ToText());
    }

    [Theory]
    [InlineData("W1041", "warning")]
    [InlineData("H2001", "hint")]
    public void ToText_SeverityWords(string code, string word)
    {
        var d = Diagnostic.Create(code, Loc(0, 7), "x");

        Assert.StartsWith(@"C:\p\Unit1.pas(1,1): " + word + " " + code + ": ", d.ToText(), StringComparison.Ordinal);
    }

    [Fact]
    public void ToText_WithoutPosition_PrintsZeroZero()
    {
        var d = Diagnostic.Create("E9003", Location.ForFile(@"C:\p\a.pproj"), @"C:\p\a.pproj");

        Assert.False(d.Location.HasPosition);
        Assert.Equal(@"C:\p\a.pproj(0,0): error E9003: Project file not found: 'C:\p\a.pproj'", d.ToText());
        Assert.Equal(
            """{"file":"C:\\p\\a.pproj","line":0,"col":0,"endLine":0,"endCol":0,"severity":"error","code":"E9003","message":"Project file not found: 'C:\\p\\a.pproj'","related":[]}""",
            d.ToJson());
    }

    [Fact]
    public void ToJson_MatchesInterfaceShape()
    {
        var d = Diagnostic.Create("E2003", Loc(21, 3), "Foo");

        Assert.Equal(
            """{"file":"C:\\p\\Unit1.pas","line":3,"col":3,"endLine":3,"endCol":6,"severity":"error","code":"E2003","message":"Undeclared identifier 'Foo'","related":[]}""",
            d.ToJson());
    }

    [Fact]
    [Trait("Req", "CMP-508")]
    public void ToJson_IncludesRelatedLocations()
    {
        var d = Diagnostic.Create("E2004", Loc(21, 3), "Foo") with
        {
            Related = [new RelatedLocation(Loc(0, 7), "Previous declaration of 'Foo'")],
        };

        Assert.Equal(
            """{"file":"C:\\p\\Unit1.pas","line":3,"col":3,"endLine":3,"endCol":6,"severity":"error","code":"E2004","message":"Identifier redeclared: 'Foo'","related":[{"file":"C:\\p\\Unit1.pas","line":1,"col":1,"message":"Previous declaration of 'Foo'"}]}""",
            d.ToJson());
    }

    [Fact]
    public void ToJson_EscapesOnlyWhatJsonRequires()
    {
        var d = Diagnostic.Create("E1210", Loc(0, 1), "quote \" tab \t é 😀 <b>");

        Assert.Contains("""
            "message":"quote \" tab \t é 😀 <b>"
            """, d.ToJson(), StringComparison.Ordinal);
        Assert.DoesNotContain("\n", d.ToJson(), StringComparison.Ordinal);
    }

    private static Location Loc(int start, int length)
    {
        var span = new TextSpan(start, length);
        return new Location(
            Source.Path,
            span,
            new LinePositionSpan(Source.Lines.GetLinePosition(span.Start), Source.Lines.GetLinePosition(span.End)));
    }
}
