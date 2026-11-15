using System.Collections.Immutable;
using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Ide.Core.Resources;
using FreexComp.LanguageService;

namespace FreexComp.Ide.Core.Languages;

/// <summary>
/// Object Pascal (IDE-P1-050): tokens only from the compiler's lexer, classes only from its lexical classifier,
/// colour tokens only from <see cref="Classifier.GetColorToken"/> (IDE-P1-056). No keyword table here.
/// </summary>
public sealed class PascalLanguage : ILanguageDefinition
{
    public string Id => "pascal";
    public string DisplayName => Strings.Language_Pascal;
    public IReadOnlyList<string> Extensions { get; } = [".pas", ".pp", ".inc"];
    public string? LineComment => "//";
    public (string Start, string End)? BlockComment => ("{", "}");
    public IReadOnlyList<(char Open, char Close)> Brackets { get; } = [('(', ')'), ('[', ']')];
    public ITokenClassifier CreateClassifier() => new PascalClassifier();
    public Editing.IFoldingProvider CreateFoldingProvider() => new PascalTreeFoldingProvider(); // P3: from the syntax tree (I-113)

    public IIndentRules IndentRules { get; } = new PascalIndentRules();

    public Editing.CodeTokens ScanCodeTokens(string text) => PascalCodeTokens.Scan(text);
}

/// <summary>
/// IDE-P1-056, phase1-shell.md §5 (from P3, I-199): whole-text <see cref="SyntaxTree.Parse"/> (no file system, so includes
/// are not read) + <see cref="Classifier.GetSyntacticClassifications"/> for buffers up to
/// <see cref="WholeTextClassifier.BackgroundThreshold"/> (larger ones: the lexical classifier), cached per buffer version
/// (<see cref="WholeTextClassifier"/>): the immediate layer drawn while typing. Parser-skipped tokens keep their lexical
/// class (C-248), so half-typed code does not turn red. Spans whose colour token is "" are dropped. Defines are empty here;
/// the active configuration's defines arrive for project files through the document's <see cref="Editing.SemanticLayer"/>.
/// </summary>
public sealed class PascalClassifier : WholeTextClassifier
{
    /// <summary>
    /// <see cref="SourceText"/> needs an absolute path; the classifier does not know the document's path
    /// (<see cref="ILanguageDefinition.CreateClassifier"/> takes none) and the lexer uses it only for diagnostics
    /// and include lookup, neither of which affects colours.
    /// </summary>
    private const string PlaceholderPath = @"C:\Untitled.pas";

    protected override List<TokenClassSpan> Lex(string text)
    {
        var source = SourceText.From(PlaceholderPath, text);
        var all = new TextSpan(0, text.Length);
        ImmutableArray<ClassifiedSpan> classified;
        if (text.Length > BackgroundThreshold)
        {
            // A large buffer (classified in the background) stays lexical: a whole-text parse costs about three lexes and
            // would break the 1 MB budget of IDE-P1-056; its syntactic classes come from the semantic layer.
            classified = Classifier.GetLexicalClassifications(Lexer.Tokenize(source), all);
        }
        else
        {
            var tree = SyntaxTree.Parse(source);
            classified = Classifier.GetSyntacticClassifications(tree, all);
            var skipped = SkippedRegions(tree);
            if (skipped.Count > 0)
                classified = WithLexicalInside(classified, Classifier.GetLexicalClassifications(Lexer.Tokenize(source), all), skipped);
        }

        var spans = new List<TokenClassSpan>();
        foreach (var span in classified)
        {
            var token = Classifier.GetColorToken(span.Kind);
            if (token.Length > 0)
                spans.Add(new TokenClassSpan(span.Span.Start, span.Span.Length, token));
        }

        if (spans.Any(s => s.TokenClass == ColorTokens.Invalid))
            AttachDiagnostics(spans, Lexer.GetDiagnostics(source)); // lexed a second time only when there is an error
        return spans;
    }

    /// <summary>
    /// The runs of <c>SkippedTokensTrivia</c> (tokens the parser skipped while recovering, and illegal characters) between
    /// two of the tree's own non-empty tokens, as [start, end) offsets in text order.
    /// </summary>
    private static List<(int Start, int End)> SkippedRegions(SyntaxTree tree)
    {
        var regions = new List<(int Start, int End)>();
        int start = -1, end = -1;
        void Trivia(ImmutableArray<SyntaxTrivia> trivia)
        {
            foreach (var piece in trivia)
            {
                if (piece.Kind != SyntaxKind.SkippedTokensTrivia)
                    continue;
                if (start < 0)
                    start = piece.Span.Start;
                end = piece.Span.End;
            }
        }

        foreach (var token in tree.Root.DescendantTokens()) // parsed without a file system: every token is the buffer's own
        {
            Trivia(token.LeadingTrivia);
            if (token.Span.Length > 0 && start >= 0)
            {
                regions.Add((start, end));
                start = -1;
            }

            Trivia(token.TrailingTrivia);
        }

        if (start >= 0)
            regions.Add((start, end));
        return regions;
    }

    /// <summary>
    /// CMP-710/714 (C-248): a skipped token keeps its lexical class, and that includes the lexical classifier's asm state
    /// (the syntactic classifier re-lexes each skipped piece on its own, so a skipped <c>asm</c> block would lose it).
    /// Inside <paramref name="regions"/> the lexical spans (clipped) replace the syntactic ones.
    /// </summary>
    private static ImmutableArray<ClassifiedSpan> WithLexicalInside(
        ImmutableArray<ClassifiedSpan> syntactic, ImmutableArray<ClassifiedSpan> lexical,
        List<(int Start, int End)> regions)
    {
        // Both span lists and the regions are sorted: one forward pass each (a file full of errors stays linear).
        var result = ImmutableArray.CreateBuilder<ClassifiedSpan>();
        int r = 0;
        foreach (var span in syntactic)
        {
            while (r < regions.Count && regions[r].End <= span.Span.Start)
                r++;
            if (r == regions.Count || span.Span.End <= regions[r].Start)
                result.Add(span);
        }

        r = 0;
        foreach (var span in lexical)
        {
            while (r < regions.Count && regions[r].End <= span.Span.Start)
                r++;
            for (int i = r; i < regions.Count && regions[i].Start < span.Span.End; i++)
            {
                int s = Math.Max(regions[i].Start, span.Span.Start), e = Math.Min(regions[i].End, span.Span.End);
                if (s < e)
                    result.Add(new ClassifiedSpan(TextSpan.FromBounds(s, e), span.Kind));
            }
        }

        result.Sort((a, b) => a.Span.Start.CompareTo(b.Span.Start));
        return result.ToImmutable();
    }

    /// <summary>
    /// I-174: every invalid span gets the code and message of the first lexer error inside it (a zero-length
    /// diagnostic counts at the span's end too), so the error marker's tooltip can show it (UI-ED-131).
    /// </summary>
    private static void AttachDiagnostics(List<TokenClassSpan> spans, IReadOnlyList<Diagnostic> diagnostics)
    {
        for (int i = 0; i < spans.Count; i++)
        {
            var span = spans[i];
            if (span.TokenClass != ColorTokens.Invalid)
                continue;
            int end = span.Offset + span.Length;
            foreach (var diagnostic in diagnostics)
            {
                var at = diagnostic.Location.Span;
                bool inside = at.Length == 0 ? at.Start >= span.Offset && at.Start <= end : at.Start < end && at.Start + at.Length > span.Offset;
                if (diagnostic.Severity == DiagnosticSeverity.Error && inside)
                {
                    spans[i] = span with { Code = diagnostic.Code, Message = diagnostic.Message };
                    break;
                }
            }
        }
    }
}
