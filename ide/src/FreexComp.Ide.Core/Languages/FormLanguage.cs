using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.Languages;

/// <summary>
/// <c>.form</c> files opened as text (IDE-P1-055): a small hand-written lexer following form-format.md §1;
/// colours per design-tokens.md §3.1 (last paragraph). Forms have no comments (L7).
/// </summary>
public sealed class FormLanguage : ILanguageDefinition
{
    public string Id => "form";
    public string DisplayName => Strings.Language_Form;
    public IReadOnlyList<string> Extensions { get; } = [".form"];
    public string? LineComment => null;
    public (string Start, string End)? BlockComment => null;
    public IReadOnlyList<(char Open, char Close)> Brackets { get; } = [('(', ')'), ('[', ']'), ('<', '>'), ('{', '}')];
    public ITokenClassifier CreateClassifier() => new FormClassifier();

    private sealed class FormClassifier : WholeTextClassifier
    {
        private static readonly string[] Keywords = ["object", "inherited", "inline", "end", "item"];

        private enum Header { None, ExpectName, ExpectColon, ExpectType }

        protected override List<TokenClassSpan> Lex(string text)
        {
            var spans = new List<TokenClassSpan>();
            void Add(int start, int end, string token) => spans.Add(new TokenClassSpan(start, end - start, token));

            bool lineStart = true, value = false, binary = false;
            var header = Header.None;
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c is '\r' or '\n')
                {
                    lineStart = true;
                    value = false;
                    header = Header.None;
                    i++;
                    continue;
                }
                if (c is ' ' or '\t' or '﻿')
                {
                    i++;
                    continue;
                }

                int start = i;
                if (binary)
                {
                    if (c == '}')
                    {
                        binary = false;
                        Add(start, ++i, ColorTokens.Symbol);
                    }
                    else if (Uri.IsHexDigit(c))
                    {
                        while (i < text.Length && Uri.IsHexDigit(text[i])) i++;
                        Add(start, i, ColorTokens.Number);
                    }
                    else
                    {
                        Add(start, ++i, ColorTokens.Invalid);
                    }
                }
                else if (c is '\'' or '#')
                {
                    bool ok = ScanString(text, ref i);
                    Add(start, i, ok ? ColorTokens.String : ColorTokens.Invalid);
                }
                else if (char.IsAsciiDigit(c) || c == '$' || (c == '-' && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1])))
                {
                    ScanNumber(text, ref i);
                    Add(start, i, ColorTokens.Number);
                }
                else if (char.IsAsciiLetter(c) || c == '_')
                {
                    while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] == '_')) i++;
                    string word = text[start..i];
                    if (header == Header.ExpectName)
                    {
                        header = Header.ExpectColon;
                    }
                    else if (header == Header.ExpectType)
                    {
                        header = Header.None;
                        Add(start, i, ColorTokens.Type);
                    }
                    else if (lineStart && IsKeyword(word) && !NextIsPropertySyntax(text, i))
                    {
                        Add(start, i, ColorTokens.Keyword);
                        if (!word.Equals("end", StringComparison.OrdinalIgnoreCase) && !word.Equals("item", StringComparison.OrdinalIgnoreCase))
                            header = Header.ExpectName;
                    }
                    else if (value)
                    {
                        Add(start, i, ColorTokens.Constant);
                    }
                }
                else if (c is ':' or '=' or '+' or '-' or '.' or ',' or '[' or ']' or '(' or ')' or '<' or '>' or '{' or '}')
                {
                    if (c == ':' && header == Header.ExpectColon) header = Header.ExpectType;
                    if (c == '{' && value) binary = true;
                    if (c == '=') value = true;
                    Add(start, ++i, ColorTokens.Symbol);
                }
                else
                {
                    Add(start, ++i, ColorTokens.Invalid);
                }
                lineStart = false;
            }
            return spans;
        }

        private static bool IsKeyword(string word) => Keywords.Any(k => k.Equals(word, StringComparison.OrdinalIgnoreCase));

        // "Item = 1" or "End.X = 1" is a property line, not a keyword.
        private static bool NextIsPropertySyntax(string text, int i)
        {
            while (i < text.Length && text[i] is ' ' or '\t') i++;
            return i < text.Length && text[i] is '=' or '.';
        }

        // Adjacent 'quoted' and #n parts form one string; false (and i at the line end) when a quote is unterminated.
        private static bool ScanString(string text, ref int i)
        {
            while (i < text.Length)
            {
                if (text[i] == '\'')
                {
                    i++;
                    while (true)
                    {
                        if (i >= text.Length || text[i] is '\r' or '\n') return false;
                        if (text[i] == '\'')
                        {
                            if (i + 1 < text.Length && text[i + 1] == '\'')
                            {
                                i += 2;
                                continue;
                            }
                            i++;
                            break;
                        }
                        i++;
                    }
                }
                else if (text[i] == '#')
                {
                    i++;
                    bool hex = i < text.Length && text[i] == '$';
                    if (hex) i++;
                    int digits = i;
                    while (i < text.Length && (hex ? Uri.IsHexDigit(text[i]) : char.IsAsciiDigit(text[i]))) i++;
                    if (i == digits) return false;
                }
                else
                {
                    break;
                }
            }
            return true;
        }

        private static void ScanNumber(string text, ref int i)
        {
            if (text[i] == '$')
            {
                i++;
                while (i < text.Length && Uri.IsHexDigit(text[i])) i++;
                return;
            }
            if (text[i] == '-') i++;
            while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
            if (i + 1 < text.Length && text[i] == '.' && char.IsAsciiDigit(text[i + 1]))
            {
                i++;
                while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
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
}
