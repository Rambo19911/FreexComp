using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.Languages;

/// <summary>
/// <c>.pproj</c>, <c>.pgroup</c> and <c>.json</c> files (IDE-P1-055): a small RFC 8259 lexer; keys
/// <c>syntax.type</c>, other strings <c>syntax.string</c>, <c>true</c>/<c>false</c>/<c>null</c> <c>syntax.keyword</c>
/// (design-tokens.md §3.1, last paragraph). JSON has no comments.
/// </summary>
public sealed class JsonLanguage : ILanguageDefinition
{
    public string Id => "json";
    public string DisplayName => Strings.Language_Json;
    public IReadOnlyList<string> Extensions { get; } = [".pproj", ".pgroup", ".json"];
    public string? LineComment => null;
    public (string Start, string End)? BlockComment => null;
    public IReadOnlyList<(char Open, char Close)> Brackets { get; } = [('{', '}'), ('[', ']')];
    public ITokenClassifier CreateClassifier() => new JsonClassifier();
    public Editing.IFoldingProvider CreateFoldingProvider() => BlockFoldingProvider.ForJson(CreateClassifier);
    public Editing.CodeTokens ScanCodeTokens(string text) => SymbolSpanCodeTokens.Scan(new JsonClassifier(), text, Brackets);

    private sealed class JsonClassifier : WholeTextClassifier
    {
        protected override List<TokenClassSpan> Lex(string text)
        {
            var spans = new List<TokenClassSpan>();
            void Add(int start, int end, string token) => spans.Add(new TokenClassSpan(start, end - start, token));

            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                int start = i;
                if (c is ' ' or '\t' or '\r' or '\n' or '﻿')
                {
                    i++;
                }
                else if (c == '"')
                {
                    if (!ScanString(text, ref i))
                        Add(start, i, ColorTokens.Invalid);
                    else
                        Add(start, i, NextIsColon(text, i) ? ColorTokens.Type : ColorTokens.String);
                }
                else if (char.IsAsciiDigit(c) || (c == '-' && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1])))
                {
                    ScanNumber(text, ref i);
                    Add(start, i, ColorTokens.Number);
                }
                else if (char.IsAsciiLetter(c))
                {
                    while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] == '_')) i++;
                    Add(start, i, text[start..i] is "true" or "false" or "null" ? ColorTokens.Keyword : ColorTokens.Invalid);
                }
                else if (c is '{' or '}' or '[' or ']' or ':' or ',')
                {
                    Add(start, ++i, ColorTokens.Symbol);
                }
                else
                {
                    Add(start, ++i, ColorTokens.Invalid);
                }
            }
            return spans;
        }

        // False (and i at the line end) when the string is not closed on its line.
        private static bool ScanString(string text, ref int i)
        {
            i++;
            while (i < text.Length && text[i] is not ('\r' or '\n'))
            {
                if (text[i] == '"')
                {
                    i++;
                    return true;
                }
                i += text[i] == '\\' && i + 1 < text.Length && text[i + 1] is not ('\r' or '\n') ? 2 : 1;
            }
            return false;
        }

        private static bool NextIsColon(string text, int i)
        {
            while (i < text.Length && text[i] is ' ' or '\t' or '\r' or '\n') i++;
            return i < text.Length && text[i] == ':';
        }

        private static void ScanNumber(string text, ref int i)
        {
            if (text[i] == '-') i++;
            while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
            if (i + 1 < text.Length && text[i] == '.' && char.IsAsciiDigit(text[i + 1]))
            {
                i++;
                while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
            }
            if (i < text.Length && text[i] is 'e' or 'E')
            {
                int j = i + 1;
                if (j < text.Length && text[j] is '+' or '-') j++;
                if (j < text.Length && char.IsAsciiDigit(text[j]))
                {
                    i = j;
                    while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
                }
            }
        }
    }
}
