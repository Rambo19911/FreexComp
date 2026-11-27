using System.Collections.Immutable;
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
        var (directives, nextInGroup) = Directives(lexed);
        return new CodeTokens(text, tokens, partners, ignoreCase: true, directives: directives, nextInGroup: nextInGroup);

        void Add(CodeToken token, char key)
        {
            tokens.Add(token);
            keys.Add(key);
        }
    }

    /// <summary>
    /// IDE-ED-075: the directives of the folding provider's directive groups (<see cref="PascalDirectiveGroups"/>), sorted
    /// by offset, each linked to the next member of its group in document order (the last back to the first); a group
    /// that is not closed links none of its members.
    /// </summary>
    private static (List<CodeToken> Directives, List<int> NextInGroup) Directives(ImmutableArray<SyntaxToken> lexed)
    {
        var members = new List<(CodeToken Token, int Group)>();
        var groups = PascalDirectiveGroups.Pair(lexed);
        for (int g = 0; g < groups.Count; g++)
        {
            foreach (var member in groups[g].Members)
                members.Add((new CodeToken(member.Start, member.Length, CodeTokenKind.Directive, member.TokenIndex), groups[g].IsClosed ? g : -1));
        }

        members.Sort((a, b) => a.Token.Offset.CompareTo(b.Token.Offset));
        var next = Enumerable.Repeat(-1, members.Count).ToList();
        var first = new Dictionary<int, int>();
        var last = new Dictionary<int, int>();
        for (int i = 0; i < members.Count; i++)
        {
            var group = members[i].Group;
            if (group < 0)
                continue;
            if (last.TryGetValue(group, out var previous))
                next[previous] = i;
            else
                first[group] = i;
            last[group] = i;
        }

        foreach (var (group, i) in last)
            next[i] = first[group];
        return ([.. members.Select(m => m.Token)], next);
    }
}
