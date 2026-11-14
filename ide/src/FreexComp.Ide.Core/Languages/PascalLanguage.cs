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
    public Editing.IFoldingProvider CreateFoldingProvider() => new PascalFoldingProvider();

    public IIndentRules IndentRules { get; } = new PascalIndentRules();
}

/// <summary>
/// IDE-P1-056: whole-text <see cref="Lexer.Tokenize"/> + <see cref="Classifier.GetLexicalClassifications"/>, cached per
/// buffer version (<see cref="WholeTextClassifier"/>). Spans whose colour token is "" are dropped. Defines are empty
/// until the project service passes the active configuration's (later task).
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
        var tokens = Lexer.Tokenize(SourceText.From(PlaceholderPath, text));
        var spans = new List<TokenClassSpan>();
        foreach (var span in Classifier.GetLexicalClassifications(tokens, new TextSpan(0, text.Length)))
        {
            var token = Classifier.GetColorToken(span.Kind);
            if (token.Length > 0)
                spans.Add(new TokenClassSpan(span.Span.Start, span.Span.Length, token));
        }
        return spans;
    }
}
