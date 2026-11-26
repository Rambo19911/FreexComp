using System.Collections.Immutable;

namespace FreexComp.Compiler.Toolchain;

/// <summary>One complete top-level statement of a resource tool input: offsets into the text, 1-based line and column of its first token.</summary>
internal readonly record struct ResourceStatement(int Start, int End, int Line, int Column);

/// <summary>
/// C-348: splits the preprocessed text of a resource script into its top-level statements, so that the compiler can run the
/// resource tool on prefixes of whole statements and find the statement of an error the tool reports without a line. Only the
/// outline of a statement is read: a <c>#</c> line, <c>LANGUAGE</c> / <c>VERSION</c> / <c>CHARACTERISTICS</c> with their
/// arguments, or a resource (name, type, then a file name or a <c>BEGIN</c> … <c>END</c> block).
/// </summary>
internal static class ResourceStatements
{
    /// <summary>Resource types whose statement always ends with a block (their headers carry numbers, strings and options).</summary>
    private static readonly HashSet<string> BlockTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "STRINGTABLE", "DIALOG", "DIALOGEX", "MENU", "MENUEX", "ACCELERATORS", "VERSIONINFO", "TOOLBAR", "DLGINIT",
    };

    /// <summary>Words of a resource header that are no file name.</summary>
    private static readonly HashSet<string> HeaderWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "PRELOAD", "LOADONCALL", "FIXED", "MOVEABLE", "DISCARDABLE", "PURE", "IMPURE", "SHARED", "NONSHARED",
    };

    private enum Kind { Word, Number, String, Punctuation, Directive }

    private readonly record struct Token(Kind Kind, int Start, int End, int Line, int Column);

    /// <summary>The complete statements in order; text after the last of them that forms no complete statement is left out.</summary>
    public static ImmutableArray<ResourceStatement> Split(string text)
    {
        var tokens = Tokens(text);
        var statements = ImmutableArray.CreateBuilder<ResourceStatement>();
        var i = 0;
        while (i < tokens.Count)
        {
            var end = StatementEnd(text, tokens, i);
            if (end < 0)
            {
                break;                                                      // runs to the end of the text
            }

            statements.Add(new ResourceStatement(tokens[i].Start, tokens[end].End, tokens[i].Line, tokens[i].Column));
            i = end + 1;
        }

        return statements.ToImmutable();
    }

    /// <returns>The index of the last token of the statement that starts at <paramref name="first"/>, or -1 if it is not complete.</returns>
    private static int StatementEnd(string text, List<Token> tokens, int first)
    {
        var start = tokens[first];
        if (start.Kind == Kind.Directive)
        {
            return first;
        }

        var word = Text(text, start);
        if (IsOption(word))
        {
            var end = SkipArguments(text, tokens, first + 1, word.Equals("LANGUAGE", StringComparison.OrdinalIgnoreCase) ? 2 : 1);
            return end < 0 ? -1 : end - 1;
        }

        string type;
        int j;
        if (word.Equals("STRINGTABLE", StringComparison.OrdinalIgnoreCase))
        {
            (type, j) = (word, first + 1);
        }
        else if (first + 1 < tokens.Count)
        {
            (type, j) = (Text(text, tokens[first + 1]), first + 2);
        }
        else
        {
            return -1;
        }

        var block = BlockTypes.Contains(type);
        while (j < tokens.Count)
        {
            var token = tokens[j];
            var current = Text(text, token);
            if (IsBegin(current))
            {
                var depth = 0;
                for (; j < tokens.Count; j++)
                {
                    var inner = Text(text, tokens[j]);                 // a string keeps its quotes: never BEGIN or END
                    if (IsBegin(inner))
                    {
                        depth++;
                    }
                    else if (IsEnd(inner) && --depth == 0)
                    {
                        return j;
                    }
                }

                return -1;
            }

            if (token.Kind == Kind.Word && IsOption(current))
            {
                j = SkipArguments(text, tokens, j + 1, current.Equals("LANGUAGE", StringComparison.OrdinalIgnoreCase) ? 2 : 1);
                if (j < 0)
                {
                    return -1;
                }

                continue;
            }

            if (!block && (token.Kind == Kind.String || (token.Kind == Kind.Word && !HeaderWords.Contains(current))))
            {
                return j;                                                   // the file name ends the statement
            }

            j++;
        }

        return -1;
    }

    private static bool IsOption(string word) =>
        word.Equals("LANGUAGE", StringComparison.OrdinalIgnoreCase) || word.Equals("VERSION", StringComparison.OrdinalIgnoreCase)
        || word.Equals("CHARACTERISTICS", StringComparison.OrdinalIgnoreCase);

    private static bool IsBegin(string word) => word == "{" || word.Equals("BEGIN", StringComparison.OrdinalIgnoreCase);

    private static bool IsEnd(string word) => word == "}" || word.Equals("END", StringComparison.OrdinalIgnoreCase);

    /// <summary>Skips <paramref name="count"/> comma-separated expressions; returns the index after them, or -1 when the text ends first.</summary>
    private static int SkipArguments(string text, List<Token> tokens, int j, int count)
    {
        for (var k = 0; k < count; k++)
        {
            if (k > 0)
            {
                if (j >= tokens.Count || Text(text, tokens[j]) != ",")
                {
                    break;                                                  // fewer arguments: the tool reports it
                }

                j++;
            }

            if (j >= tokens.Count)
            {
                return -1;
            }

            j = SkipExpression(text, tokens, j);
        }

        return j > tokens.Count ? -1 : j;
    }

    private static int SkipExpression(string text, List<Token> tokens, int j)
    {
        while (j < tokens.Count)
        {
            while (j < tokens.Count && Text(text, tokens[j]) is "-" or "~" or "!" || j < tokens.Count && Text(text, tokens[j]).Equals("NOT", StringComparison.OrdinalIgnoreCase))
            {
                j++;
            }

            if (j < tokens.Count && Text(text, tokens[j]) == "(")
            {
                var depth = 0;
                for (; j < tokens.Count; j++)
                {
                    var t = Text(text, tokens[j]);
                    depth += t == "(" ? 1 : t == ")" ? -1 : 0;
                    if (depth == 0)
                    {
                        break;
                    }
                }
            }

            j++;
            if (j < tokens.Count && Text(text, tokens[j]) is "+" or "-" or "*" or "/" or "|" or "&" or "^" or "%")
            {
                j++;
                continue;
            }

            break;
        }

        return j;
    }

    private static string Text(string text, Token token) => text[token.Start..token.End];

    private static List<Token> Tokens(string text)
    {
        var tokens = new List<Token>();
        var line = 1;
        var lineStart = 0;
        var atLineStart = true;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '\n')
            {
                i++;
                line++;
                lineStart = i;
                atLineStart = true;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            var (start, tokenLine, tokenColumn) = (i, line, i - lineStart + 1);
            Kind kind;
            if (c == '#' && atLineStart)
            {
                kind = Kind.Directive;
                while (i < text.Length && text[i] != '\n')
                {
                    i++;
                }
            }
            else if (c == '"' || (c is 'L' or 'l' && i + 1 < text.Length && text[i + 1] == '"'))
            {
                kind = Kind.String;
                i = c == '"' ? i + 1 : i + 2;
                while (i < text.Length)
                {
                    if (text[i] == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            i += 2;                                         // a doubled quote is part of the string
                            continue;
                        }

                        i++;
                        break;
                    }

                    if (text[i] == '\n')
                    {
                        line++;
                        lineStart = i + 1;
                    }

                    i++;
                }
            }
            else if (char.IsLetterOrDigit(c) || c is '_' or '.' or '\\' or ':' or '$' or '@')
            {
                kind = char.IsAsciiDigit(c) ? Kind.Number : Kind.Word;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '.' or '\\' or ':' or '$' or '@'))
                {
                    i++;
                }
            }
            else
            {
                kind = Kind.Punctuation;
                i++;
            }

            tokens.Add(new Token(kind, start, i, tokenLine, tokenColumn));
            atLineStart = false;
        }

        return tokens;
    }
}
