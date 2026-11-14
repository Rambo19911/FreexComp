using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Text;
using FreexComp.LanguageService;

namespace FreexComp.Ide.Core.Languages;

/// <summary>
/// Object Pascal <see cref="CodeTokens"/> from the compiler's lexer (editor-features.md §7): <c>( ) [ ]</c>, the block
/// keywords paired as in IDE-ED-052 (<see cref="PascalIndentRules.IsBlockOpener"/>), and identifiers. Strings and
/// comments are literal tokens or trivia, so nothing inside them counts.
/// </summary>
internal static class PascalCodeTokens
{
    public static CodeTokens Scan(string text)
    {
        var lexed = Lexer.Tokenize(SourceText.From(EditorOperations.LexerPath, text));
        // IDE-ED-074: an identifier used as a contextual keyword here (read, write, private, …) is a keyword.
        var contextual = Classifier.GetLexicalClassifications(lexed, new TextSpan(0, text.Length))
            .Where(s => s.Kind == ClassificationKind.ContextualKeyword).Select(s => s.Span.Start).ToHashSet();
        var tokens = new List<CodeToken>();
        var keys = new List<char>();
        var blocks = new List<(int Token, SyntaxKind Kind)>(); // open keyword blocks: index in tokens, opener kind
        var keywordPartners = new Dictionary<int, int>();
        for (int i = 0; i < lexed.Length; i++)
        {
            var token = lexed[i];
            var span = token.Span;
            var top = blocks.Count > 0 ? blocks[^1].Kind : SyntaxKind.None;
            switch (token.Kind)
            {
                case SyntaxKind.OpenParenToken or SyntaxKind.OpenBracketToken:
                    Add(new CodeToken(span.Start, span.Length, CodeTokenKind.Open, i), token.Kind == SyntaxKind.OpenParenToken ? '(' : '[');
                    break;
                case SyntaxKind.CloseParenToken or SyntaxKind.CloseBracketToken:
                    Add(new CodeToken(span.Start, span.Length, CodeTokenKind.Close, i), token.Kind == SyntaxKind.CloseParenToken ? '(' : '[');
                    break;
                case SyntaxKind.EndKeyword or SyntaxKind.UntilKeyword:
                    Add(new CodeToken(span.Start, span.Length, CodeTokenKind.Close, i, IsKeyword: true), ' ');
                    if (PascalIndentRules.ClosesBlock(token.Kind, top))
                    {
                        keywordPartners[blocks[^1].Token] = tokens.Count - 1;
                        keywordPartners[tokens.Count - 1] = blocks[^1].Token;
                        blocks.RemoveAt(blocks.Count - 1);
                    }

                    break;
                case var _ when PascalIndentRules.IsBlockOpener(lexed, i, top):
                    Add(new CodeToken(span.Start, span.Length, CodeTokenKind.Open, i, IsKeyword: true), ' ');
                    blocks.Add((tokens.Count - 1, token.Kind));
                    break;
                case SyntaxKind.IdentifierToken when !contextual.Contains(span.Start):
                    Add(new CodeToken(span.Start, span.Length, CodeTokenKind.Identifier, i), ' ');
                    break;
            }
        }

        var partners = CodeTokens.PairBrackets(tokens, k => keys[k]);
        foreach (var (token, partner) in keywordPartners)
            partners[token] = partner;
        return new CodeTokens(text, tokens, partners, ignoreCase: true);

        void Add(CodeToken token, char key)
        {
            tokens.Add(token);
            keys.Add(key);
        }
    }
}
