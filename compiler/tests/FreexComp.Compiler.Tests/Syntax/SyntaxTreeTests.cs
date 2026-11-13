using System.Collections.Immutable;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Tests.Syntax;

/// <summary>
/// Syntax tree infrastructure (api.md §4, CMP-220, 221; task C-011): parent links, spans, navigation, round trip,
/// <c>FindToken</c>/<c>FindNode</c>, trivia queries and visitors. Trees are assembled by hand from lexer tokens
/// (the parser arrives with C-012) under the test-only root <see cref="TestRootSyntax"/>.
/// </summary>
[Trait("Phase", "2")]
[Trait("Kind", "Unit")]
public sealed class SyntaxTreeTests
{
    private const string Path = @"C:\p\test.pas";

    //                          0         1         2         3
    //                          0123456789012345678901234567890123 4 5
    private const string Text = "  Foo.Bar.Baz { c } := X // end\r\n";

    /// <summary>
    /// Root[ QualifiedName[ QualifiedName[ Foo . Bar ] . Baz ]  :=  IdentifierName[X]  EOF ].
    /// </summary>
    private static (SyntaxTree Tree, TestRootSyntax Root, QualifiedNameSyntax Outer, QualifiedNameSyntax Inner, IdentifierNameSyntax X)
        Build(string text = Text, ImmutableArray<Diagnostic> diagnostics = default)
    {
        var source = SourceText.From(Path, text);
        var t = Lexer.Tokenize(source);
        Assert.Equal(8, t.Length);
        var inner = new QualifiedNameSyntax(new IdentifierNameSyntax(t[0]), t[1], new IdentifierNameSyntax(t[2]));
        var outer = new QualifiedNameSyntax(inner, t[3], new IdentifierNameSyntax(t[4]));
        var x = new IdentifierNameSyntax(t[6]);
        var root = new TestRootSyntax([outer, t[5], x, t[7]]);
        var tree = new SyntaxTree(source, root, diagnostics.IsDefault ? [] : diagnostics, []);
        return (tree, root, outer, inner, x);
    }

    private static SyntaxTree FlatTree(string text)
    {
        var source = SourceText.From(Path, text);
        var root = new TestRootSyntax([.. Lexer.Tokenize(source).Select(t => (SyntaxNodeOrToken)t)]);
        return new SyntaxTree(source, root, [], []);
    }

    // ------------------------------------------------------------------ round trip, text

    [Fact]
    [Trait("Req", "CMP-220")]
    public void RoundTrip_RootToFullString_EqualsSourceText()
    {
        var (tree, root, outer, _, x) = Build();

        Assert.Equal(Text, root.ToFullString());
        Assert.Equal(tree.Text.Text, tree.Root.ToFullString());
        Assert.Equal("  Foo.Bar.Baz { c } ", outer.ToFullString());
        Assert.Equal("X // end\r\n", x.ToFullString());
    }

    [Fact]
    public void ToString_IsTheTextOfSpan_WithoutOuterTrivia()
    {
        var (_, root, outer, inner, x) = Build();

        Assert.Equal("Foo.Bar.Baz", outer.ToString());
        Assert.Equal("Foo.Bar", inner.ToString());
        Assert.Equal("X", x.ToString());
        Assert.Equal("Foo.Bar.Baz { c } := X // end\r\n", root.ToString());   // the root's span ends at EndOfFileToken
        Assert.Equal(TextSpan.FromBounds(2, Text.Length), root.Span);
    }

    [Fact]
    [Trait("Req", "CMP-220")]
    public void RoundTrip_FlatTreeOfEveryToken_ReproducesTextWithDirectivesAndDisabledText()
    {
        const string text = "{$DEFINE A}\r\n(* x *) a {$IFDEF B} hidden {$ENDIF} 'q''s' #13 $FF ; // t\r\n";
        Assert.Equal(text, FlatTree(text).Root.ToFullString());
    }

    // ------------------------------------------------------------------ spans

    [Fact]
    public void Spans_OfNodes_CoverTheirTokens()
    {
        var (_, root, outer, inner, x) = Build();

        Assert.Equal(TextSpan.FromBounds(2, 13), outer.Span);
        Assert.Equal(TextSpan.FromBounds(0, 20), outer.FullSpan);
        Assert.Equal(TextSpan.FromBounds(2, 9), inner.Span);
        Assert.Equal(TextSpan.FromBounds(0, 9), inner.FullSpan);
        Assert.Equal(TextSpan.FromBounds(23, 24), x.Span);
        Assert.Equal(TextSpan.FromBounds(23, 33), x.FullSpan);
        Assert.Equal(TextSpan.FromBounds(0, Text.Length), root.FullSpan);
    }

    // ------------------------------------------------------------------ parents and tree

    [Fact]
    [Trait("Req", "CMP-221")]
    public void Parents_AreLinked_ForNodesAndTokens()
    {
        var (tree, root, outer, inner, x) = Build();

        Assert.Null(root.Parent);
        Assert.Same(root, outer.Parent);
        Assert.Same(outer, inner.Parent);
        Assert.Same(root, x.Parent);
        Assert.Same(x, x.Identifier.Parent);
        Assert.Same(outer, outer.DotToken.Parent);
        Assert.Same(inner, ((IdentifierNameSyntax)inner.Left).Identifier.Parent!.Parent);
        Assert.All(root.DescendantNodes().Prepend(root), n => Assert.Same(tree, n.SyntaxTree));
        Assert.Same(root, tree.Root);
        Assert.All(root.DescendantTokens(), t => Assert.NotNull(t.Parent));
    }

    [Fact]
    public void TypedProperties_ReturnChildrenInSourceOrder()
    {
        var (_, _, outer, inner, _) = Build();

        Assert.Same(inner, outer.Left);
        Assert.Equal("Baz", outer.Right.Identifier.Text);
        Assert.Equal("Foo", ((IdentifierNameSyntax)inner.Left).Identifier.Text);
        Assert.Equal(SyntaxKind.QualifiedName, outer.Kind);
        Assert.Equal(SyntaxKind.IdentifierName, outer.Right.Kind);
    }

    // ------------------------------------------------------------------ navigation

    [Fact]
    public void ChildNodesAndTokens_AreInSourceOrder()
    {
        var (_, root, outer, _, x) = Build();

        var children = root.ChildNodesAndTokens().ToList();
        Assert.Equal(4, children.Count);
        Assert.True(children[0].IsNode);
        Assert.Same(outer, children[0].AsNode());
        Assert.False(children[1].IsNode);
        Assert.Equal(SyntaxKind.ColonEqualsToken, children[1].AsToken().Kind);
        Assert.Same(x, children[2].AsNode());
        Assert.Equal(SyntaxKind.EndOfFileToken, children[3].AsToken().Kind);
        Assert.Equal([outer, x], root.ChildNodes());
    }

    [Fact]
    public void DescendantNodes_ArePreOrder_AndRespectDescendInto()
    {
        var (_, root, outer, inner, x) = Build();

        Assert.Equal(
            ["Foo.Bar.Baz", "Foo.Bar", "Foo", "Bar", "Baz", "X"],
            root.DescendantNodes().Select(n => n.ToString()));
        Assert.Equal(
            [outer, x],
            root.DescendantNodes(n => n is not QualifiedNameSyntax));
        Assert.Equal(["Foo", "Bar"], inner.DescendantNodes().Select(n => n.ToString()));
    }

    [Fact]
    public void DescendantTokens_AreAllTokensInOrder()
    {
        var (_, root, outer, _, _) = Build();

        Assert.Equal(["Foo", ".", "Bar", ".", "Baz", ":=", "X", ""], root.DescendantTokens().Select(t => t.Text));
        Assert.Equal(Lexer.Tokenize(SourceText.From(Path, Text)).Select(t => (t.Kind, t.Span)),
            root.DescendantTokens().Select(t => (t.Kind, t.Span)));
        Assert.Equal("Foo", outer.FirstToken().Text);
        Assert.Equal("Baz", outer.LastToken().Text);
        Assert.Equal(SyntaxKind.EndOfFileToken, root.LastToken().Kind);
    }

    [Fact]
    public void Ancestors_FromInnermostToRoot()
    {
        var (_, root, outer, inner, _) = Build();
        var foo = (IdentifierNameSyntax)inner.Left;

        Assert.Equal([foo, inner, outer, root], foo.AncestorsAndSelf());
        Assert.Same(inner, foo.FirstAncestorOrSelf<QualifiedNameSyntax>());
        Assert.Same(foo, foo.FirstAncestorOrSelf<NameSyntax>());
        Assert.Same(root, foo.FirstAncestorOrSelf<CompilationUnitSyntax>());
        Assert.Null(foo.FirstAncestorOrSelf<StatementSyntax>());
    }

    [Fact]
    public void GetNextAndPreviousToken_CrossNodeBoundaries()
    {
        var (_, root, _, _, x) = Build();
        var tokens = root.DescendantTokens().ToList();

        for (var i = 0; i < tokens.Count; i++)
        {
            var next = tokens[i].GetNextToken();
            var previous = tokens[i].GetPreviousToken();
            Assert.Equal(i + 1 < tokens.Count ? tokens[i + 1] : default, next);
            Assert.Equal(i > 0 ? tokens[i - 1] : default, previous);
        }

        Assert.Equal(SyntaxKind.ColonEqualsToken, x.Identifier.GetPreviousToken().Kind);
        Assert.Equal(SyntaxKind.None, root.LastToken().GetNextToken().Kind);
    }

    [Fact]
    public void ParentlessToken_HasNoNeighbours()
    {
        var token = Lexer.Tokenize(SourceText.From(Path, "a b"))[0];

        Assert.Null(token.Parent);
        Assert.Equal(default, token.GetNextToken());
        Assert.Equal(default, token.GetPreviousToken());
    }

    // ------------------------------------------------------------------ FindToken / FindNode

    [Theory]
    [InlineData(0, "Foo")]      // leading trivia of Foo
    [InlineData(2, "Foo")]
    [InlineData(4, "Foo")]
    [InlineData(5, ".")]
    [InlineData(10, "Baz")]
    [InlineData(15, "Baz")]     // inside "{ c }", trailing trivia of Baz
    [InlineData(20, ":=")]
    [InlineData(23, "X")]
    [InlineData(28, "X")]       // inside the line comment
    [InlineData(33, "")]        // end of text: EndOfFileToken
    public void FindToken_ReturnsTheTokenWhoseFullSpanContainsThePosition(int position, string expected)
    {
        var (tree, _, _, _, _) = Build();

        var token = tree.FindToken(position);

        Assert.Equal(expected, token.Text);
        Assert.NotNull(token.Parent);
    }

    [Fact]
    public void FindToken_InLeadingTriviaOfEndOfFile_ReturnsEndOfFile()
    {
        var tree = FlatTree("a\r\n\r\n  ");

        Assert.Equal(SyntaxKind.EndOfFileToken, tree.FindToken(5).Kind);
        Assert.Equal(SyntaxKind.EndOfFileToken, tree.FindToken(7).Kind);
        Assert.Equal(SyntaxKind.IdentifierToken, tree.FindToken(0).Kind);
    }

    [Theory]
    [InlineData(6, 3, "Bar", SyntaxKind.IdentifierName)]
    [InlineData(2, 7, "Foo.Bar", SyntaxKind.QualifiedName)]
    [InlineData(4, 4, "Foo.Bar", SyntaxKind.QualifiedName)]          // "o.Ba"
    [InlineData(2, 11, "Foo.Bar.Baz", SyntaxKind.QualifiedName)]
    [InlineData(10, 1, "Baz", SyntaxKind.IdentifierName)]
    [InlineData(15, 1, "Baz", SyntaxKind.IdentifierName)]            // in "{ c }": trailing trivia belongs to Baz
    [InlineData(10, 11, "Foo.Bar.Baz { c } := X // end\r\n", SyntaxKind.None)]  // spans Baz … := → root
    [InlineData(23, 1, "X", SyntaxKind.IdentifierName)]
    public void FindNode_ReturnsTheInnermostNodeContainingTheSpan(int start, int length, string text, SyntaxKind kind)
    {
        var (tree, _, _, _, _) = Build();

        var node = tree.FindNode(new TextSpan(start, length));

        Assert.Equal(text, node.ToString());
        Assert.Equal(kind, node.Kind);
    }

    // ------------------------------------------------------------------ trivia queries

    [Fact]
    public void DescendantTrivia_Directives_DisabledRegions()
    {
        const string text = "{$DEFINE A} a {$IFDEF B} hidden {$ENDIF} { c } b";
        var tree = FlatTree(text);

        Assert.Equal(
            [SyntaxKind.DirectiveTrivia, SyntaxKind.WhitespaceTrivia, SyntaxKind.WhitespaceTrivia, SyntaxKind.DirectiveTrivia,
             SyntaxKind.DisabledTextTrivia, SyntaxKind.DirectiveTrivia, SyntaxKind.WhitespaceTrivia,
             SyntaxKind.BlockCommentTrivia, SyntaxKind.WhitespaceTrivia],
            tree.DescendantTrivia().Select(t => t.Kind));
        Assert.Equal(["DEFINE", "IFDEF", "ENDIF"], tree.Directives.Select(d => d.Name));
        Assert.Equal([TextSpan.FromBounds(24, 32)], tree.DisabledRegions);
        Assert.Equal(" hidden ", text[24..32]);
    }

    // ------------------------------------------------------------------ missing tokens, diagnostics

    [Fact]
    public void MissingToken_IsZeroWidth_AndANodeOfMissingTokensIsMissing()
    {
        var source = SourceText.From(Path, "a");
        var t = Lexer.Tokenize(source);
        var missing = SyntaxToken.Missing(SyntaxKind.IdentifierToken, 1);
        var name = new IdentifierNameSyntax(missing);
        var present = new IdentifierNameSyntax(t[0]);
        var root = new TestRootSyntax([present, name, t[1]]);
        _ = new SyntaxTree(source, root, [], []);

        Assert.True(name.Identifier.IsMissing);
        Assert.Equal("", name.Identifier.Text);
        Assert.Equal(new TextSpan(1, 0), name.Span);
        Assert.True(name.IsMissing);
        Assert.False(present.IsMissing);
        Assert.False(root.IsMissing);
        Assert.Equal("a", root.ToFullString());
    }

    [Fact]
    public void ContainsDiagnostics_IsTrueForNodesWhoseFullSpanContainsADiagnostic()
    {
        var location = new Location(Path, new TextSpan(6, 3), default);
        var (_, root, outer, inner, x) = Build(diagnostics: [new Diagnostic("E1100", DiagnosticSeverity.Error, "m", location, [])]);

        Assert.True(root.ContainsDiagnostics);
        Assert.True(outer.ContainsDiagnostics);
        Assert.True(inner.ContainsDiagnostics);
        Assert.False(((IdentifierNameSyntax)inner.Left).ContainsDiagnostics);
        Assert.False(x.ContainsDiagnostics);
    }

    // ------------------------------------------------------------------ separated lists

    [Fact]
    public void SeparatedList_KeepsNodesAndSeparatorsInOrder_WithParents()
    {
        var source = SourceText.From(Path, "A, B, C");
        var t = Lexer.Tokenize(source);
        var list = new SeparatedSyntaxList<IdentifierNameSyntax>(
            [new IdentifierNameSyntax(t[0]), t[1], new IdentifierNameSyntax(t[2]), t[3], new IdentifierNameSyntax(t[4])]);
        var root = new TestRootSyntax(list, t[5]);
        _ = new SyntaxTree(source, root, [], []);

        Assert.Equal(3, root.Names.Count);
        Assert.Equal(["A", "B", "C"], root.Names.Select(n => n.ToString()));
        Assert.Equal([",", ","], root.Names.Separators.Select(s => s.Text));
        Assert.All(root.Names.Separators, s => Assert.Same(root, s.Parent));
        Assert.All(root.Names, n => Assert.Same(root, n.Parent));
        Assert.Equal("A, B, C", root.ToFullString());
        Assert.Equal(6, root.ChildNodesAndTokens().Count());
        Assert.Equal(",", root.Names[0].Identifier.GetNextToken().Text);
    }

    // ------------------------------------------------------------------ visitors

    private sealed class NameVisitor : SyntaxVisitor<string>
    {
        public override string DefaultVisit(SyntaxNode node) => "?";

        public override string VisitIdentifierName(IdentifierNameSyntax node) => node.Identifier.Text;

        public override string VisitQualifiedName(QualifiedNameSyntax node) => Visit(node.Left) + "/" + Visit(node.Right);
    }

    private sealed class CountingWalker : SyntaxWalker
    {
        public List<string> Identifiers { get; } = [];

        public List<SyntaxKind> Tokens { get; } = [];

        public List<SyntaxKind> Trivia { get; } = [];

        public override object? VisitIdentifierName(IdentifierNameSyntax node)
        {
            Identifiers.Add(node.Identifier.Text);
            return base.VisitIdentifierName(node);
        }

        public override void VisitToken(SyntaxToken token)
        {
            Tokens.Add(token.Kind);
            base.VisitToken(token);
        }

        public override void VisitTrivia(SyntaxTrivia trivia) => Trivia.Add(trivia.Kind);
    }

    [Fact]
    public void Visitor_DispatchesToTheTypedVisitMethod()
    {
        var (_, root, outer, _, x) = Build();
        var visitor = new NameVisitor();

        Assert.Equal("Foo/Bar/Baz", outer.Accept(visitor));
        Assert.Equal("X", visitor.Visit(x));
        Assert.Equal("?", visitor.Visit(root));
        Assert.Null(visitor.Visit(null));
    }

    [Fact]
    public void Walker_VisitsEveryNodeTokenAndTriviaDepthFirst()
    {
        var (_, root, _, _, _) = Build();
        var walker = new CountingWalker();

        walker.Visit(root);

        Assert.Equal(["Foo", "Bar", "Baz", "X"], walker.Identifiers);
        Assert.Equal(root.DescendantTokens().Select(t => t.Kind), walker.Tokens);
        Assert.Equal(
            [SyntaxKind.WhitespaceTrivia, SyntaxKind.WhitespaceTrivia, SyntaxKind.BlockCommentTrivia, SyntaxKind.WhitespaceTrivia,
             SyntaxKind.WhitespaceTrivia, SyntaxKind.WhitespaceTrivia, SyntaxKind.LineCommentTrivia, SyntaxKind.EndOfLineTrivia],
            walker.Trivia);
    }
}

/// <summary>Test-only compilation unit holding arbitrary children (the real ones arrive with the parser).</summary>
internal sealed class TestRootSyntax : CompilationUnitSyntax
{
    public TestRootSyntax(IEnumerable<SyntaxNodeOrToken> children)
        : base(SyntaxKind.None)
    {
        foreach (var child in children)
        {
            Add(child);
        }
    }

    public TestRootSyntax(SeparatedSyntaxList<IdentifierNameSyntax> names, SyntaxToken endOfFile)
        : base(SyntaxKind.None)
    {
        Names = Add(names);
        Add(endOfFile);
    }

    public SeparatedSyntaxList<IdentifierNameSyntax> Names { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.DefaultVisit(this);
}
