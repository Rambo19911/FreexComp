using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Languages;

/// <summary>Every extension without a language (IDE-P1-055): no colours, no comments, no brackets.</summary>
public sealed class PlainTextLanguage : ILanguageDefinition
{
    public string Id => "plaintext";
    public string DisplayName => Strings.Language_PlainText;
    public IReadOnlyList<string> Extensions { get; } = [];
    public string? LineComment => null;
    public (string Start, string End)? BlockComment => null;
    public IReadOnlyList<(char Open, char Close)> Brackets { get; } = [];
    public ITokenClassifier CreateClassifier() => new NoColors();

    private sealed class NoColors : ITokenClassifier
    {
        public IReadOnlyList<TokenClassSpan> Classify(ITextBuffer buffer, LineRange lines) => [];

        public event EventHandler? ClassificationChanged { add { } remove { } }
    }
}
