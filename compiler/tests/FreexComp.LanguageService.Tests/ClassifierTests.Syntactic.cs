using System.Collections.Immutable;
using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.LanguageService.Tests;

/// <summary>language-service.md §2, CMP-711…713: the tree-based (syntactic) classifier (C-151).</summary>
public sealed partial class ClassifierTests
{
    private static ImmutableArray<ClassifiedSpan> ClassifySyntactic(string text, TextSpan? range = null)
    {
        var tree = SyntaxTree.Parse(SourceText.From(Path, text));
        return Classifier.GetSyntacticClassifications(tree, range ?? new TextSpan(0, text.Length));
    }

    private static ClassificationKind SyntacticKindOf(string text, string piece, int occurrence = 0)
    {
        var start = -1;
        for (var i = 0; i <= occurrence; i++)
        {
            start = text.IndexOf(piece, start + 1, StringComparison.Ordinal);
        }

        return ClassifySyntactic(text).Single(s => s.Span == new TextSpan(start, piece.Length)).Kind;
    }

    [Fact]
    [Trait("Phase", "3")]
    [Trait("Req", "CMP-712")]
    public void Syntactic_SameKindsAsLexicalOnValidCode()
    {
        const string text = "program P;\nconst S = 'abc'#13'd'; C = #9; N = $FF;\nvar x: Integer; // c\n{ b } /// d\n{$R+}\nbegin x := 12 + N * 2; end.";

        Assert.Equal(Classify(text), ClassifySyntactic(text));
    }

    [Theory]
    [Trait("Phase", "3")]
    [Trait("Req", "CMP-713")]
    [InlineData("unit U; interface type T = class procedure Foo; virtual; abstract; end; implementation end.", "virtual")]
    [InlineData("unit U; interface type T = class procedure Foo; virtual; abstract; end; implementation end.", "abstract")]
    [InlineData("unit U; interface type T = class private FP: Integer; public property P: Integer read FP write FP default 0; end; implementation end.", "private")]
    [InlineData("unit U; interface type T = class private FP: Integer; public property P: Integer read FP write FP default 0; end; implementation end.", "read")]
    [InlineData("unit U; interface type T = class private FP: Integer; public property P: Integer read FP write FP default 0; end; implementation end.", "default")]
    [InlineData("unit U; interface procedure F(A: Integer); overload; implementation procedure F(A: Integer); begin end; end.", "overload")]
    public void Syntactic_DirectivesInDirectivePosition_AreContextualKeywords(string text, string word)
    {
        Assert.Equal(ClassificationKind.ContextualKeyword, SyntacticKindOf(text, word));
    }

    /// <summary>CMP-713 (a) is an approximation: an identifier after ';' that the parser does not use as a directive.</summary>
    [Fact]
    [Trait("Phase", "3")]
    [Trait("Req", "CMP-713")]
    public void Syntactic_IdentifierAfterSemicolon_IsIdentifier()
    {
        const string text = "program P; var index: Integer; begin index := 1; index := 2 end.";

        Assert.Equal(ClassificationKind.ContextualKeyword, KindOf2(text, "index", 2));             // the lexical approximation
        Assert.Equal(ClassificationKind.Identifier, SyntacticKindOf(text, "index", 2));
        Assert.Equal(ClassificationKind.Identifier, SyntacticKindOf(text, "index", 0));
    }

    /// <summary>The lexical rule misses directives that do not follow ';' or ')' (here the parameter modifier <c>out</c>).</summary>
    [Fact]
    [Trait("Phase", "3")]
    [Trait("Req", "CMP-713")]
    public void Syntactic_OutParameterModifier_IsContextualKeyword()
    {
        const string text = "program P; procedure Q(out X: Integer); begin X := 1 end; begin end.";

        Assert.Equal(ClassificationKind.Identifier, KindOf2(text, "out", 0));
        Assert.Equal(ClassificationKind.ContextualKeyword, SyntacticKindOf(text, "out"));
    }

    [Fact]
    [Trait("Phase", "3")]
    [Trait("Req", "CMP-711")]
    public void Syntactic_Range_ReturnsOnlyIntersectingSpansClippedToTheRange()
    {
        const string text = "program alpha; { comment } begin end.";

        Assert.Equal(
            [new ClassifiedSpan(new TextSpan(10, 3), ClassificationKind.Identifier), new ClassifiedSpan(new TextSpan(13, 1), ClassificationKind.Punctuation),
             new ClassifiedSpan(new TextSpan(15, 2), ClassificationKind.Comment)],
            ClassifySyntactic(text, new TextSpan(10, 7)));
        Assert.Empty(ClassifySyntactic(text, new TextSpan(text.Length, 0)));
    }

    [Fact]
    [Trait("Phase", "3")]
    public void Syntactic_FileWithSyntaxErrors_DoesNotThrowAndKeepsSkippedText()
    {
        const string text = "program P; begin x := ; ! 'open\nend";

        var spans = ClassifySyntactic(text);

        Assert.Contains(spans, s => s.Kind == ClassificationKind.Invalid && text.Substring(s.Span.Start, s.Span.Length) == "!");
        Assert.Contains(spans, s => s.Kind == ClassificationKind.Invalid && text.Substring(s.Span.Start, s.Span.Length) == "'open");
        Assert.All(spans, s => Assert.True(s.Span.Length > 0));
    }

    [Fact]
    [Trait("Phase", "3")]
    public void Syntactic_DisabledText()
    {
        const string text = "program P;\n{$IFDEF X}\n  a := 'unterminated;\n{$ENDIF}\nbegin end.";

        Assert.Contains(ClassifySyntactic(text), s => s.Kind == ClassificationKind.DisabledText);
        Assert.Equal(Classify(text), ClassifySyntactic(text));
    }

    /// <summary>
    /// CMP-712: on every golden source without syntax errors the two classifiers return the same spans and the same kinds,
    /// except where CMP-713 is an approximation (ContextualKeyword ↔ Identifier).
    /// </summary>
    [Fact]
    [Trait("Phase", "3")]
    [Trait("Req", "CMP-712")]
    public void LexicalAndSyntacticAgreeOnGoldens()
    {
        var files = Directory.GetFiles(System.IO.Path.Combine(RepoPaths.Root, "tests", "compiler"), "*.pas", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        var compared = 0;
        foreach (var file in files.Order(StringComparer.Ordinal))
        {
            var text = SourceText.From(file, File.ReadAllText(file));
            var tree = SyntaxTree.Parse(text);
            if (tree.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                continue;
            }

            compared++;
            var range = new TextSpan(0, text.Text.Length);
            var lexical = Classifier.GetLexicalClassifications(Lexer.Tokenize(text), range);
            var syntactic = Classifier.GetSyntacticClassifications(tree, range);

            Assert.True(lexical.Length == syntactic.Length, $"{file}: {lexical.Length} lexical vs {syntactic.Length} syntactic spans");
            for (var i = 0; i < lexical.Length; i++)
            {
                Assert.True(lexical[i].Span == syntactic[i].Span, $"{file}: span {i} {lexical[i]} vs {syntactic[i]}");
                var approximation = lexical[i].Kind is ClassificationKind.ContextualKeyword or ClassificationKind.Identifier
                                    && syntactic[i].Kind is ClassificationKind.ContextualKeyword or ClassificationKind.Identifier;
                Assert.True(lexical[i].Kind == syntactic[i].Kind || approximation,
                    $"{file}: '{text.Text.Substring(lexical[i].Span.Start, lexical[i].Span.Length)}' at {lexical[i].Span.Start}: {lexical[i].Kind} vs {syntactic[i].Kind}");
            }
        }

        Assert.True(compared > 100, $"only {compared} golden files compared");
    }

    private static ClassificationKind KindOf2(string text, string piece, int occurrence)
    {
        var start = -1;
        for (var i = 0; i <= occurrence; i++)
        {
            start = text.IndexOf(piece, start + 1, StringComparison.Ordinal);
        }

        return Classify(text).Single(s => s.Span == new TextSpan(start, piece.Length)).Kind;
    }
}
