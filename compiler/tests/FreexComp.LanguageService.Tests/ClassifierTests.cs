using System.Collections.Immutable;
using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.LanguageService.Tests;

/// <summary>language-service.md §2, CMP-710…714 (lexical classifier, Phase 1).</summary>
[Trait("Phase", "1")]
public sealed class ClassifierTests
{
    private const string Path = @"C:\p\test.pas";

    private static ImmutableArray<ClassifiedSpan> Classify(string text, string[]? defines = null, TextSpan? range = null)
    {
        var tokens = Lexer.Tokenize(SourceText.From(Path, text), new ParseOptions([.. defines ?? []]));
        return Classifier.GetLexicalClassifications(tokens, range ?? new TextSpan(0, text.Length));
    }

    /// <summary>(text of the span, kind) for every span.</summary>
    private static (string, ClassificationKind)[] Pieces(string text, string[]? defines = null) =>
        [.. Classify(text, defines).Select(s => (text.Substring(s.Span.Start, s.Span.Length), s.Kind))];

    private static ClassificationKind KindOf(string text, string piece) =>
        Classify(text).Single(s => text.Substring(s.Span.Start, s.Span.Length) == piece).Kind;

    // ------------------------------------------------------------------ token kinds (CMP-710)

    [Fact]
    [Trait("Req", "CMP-710")]
    public void Lexical_EveryTokenClass()
    {
        const string text = "begin x := 12 + $FF * 1.5e3; s := 'abc'#13'd'; c := #9; p^.q[1] := @r; end. // c\n{ b } /// d\n{$R+}";

        Assert.Equal(
            [
                ("begin", ClassificationKind.Keyword), ("x", ClassificationKind.Identifier), (":=", ClassificationKind.Operator),
                ("12", ClassificationKind.NumberLiteral), ("+", ClassificationKind.Operator), ("$FF", ClassificationKind.NumberLiteral),
                ("*", ClassificationKind.Operator), ("1.5e3", ClassificationKind.NumberLiteral), (";", ClassificationKind.Punctuation),
                ("s", ClassificationKind.Identifier), (":=", ClassificationKind.Operator), ("'abc'", ClassificationKind.StringLiteral),
                ("#13", ClassificationKind.CharLiteral), ("'d'", ClassificationKind.StringLiteral), (";", ClassificationKind.Punctuation),
                ("c", ClassificationKind.Identifier), (":=", ClassificationKind.Operator), ("#9", ClassificationKind.CharLiteral),
                (";", ClassificationKind.Punctuation), ("p", ClassificationKind.Identifier), ("^", ClassificationKind.Operator),
                (".", ClassificationKind.Punctuation), ("q", ClassificationKind.Identifier), ("[", ClassificationKind.Punctuation),
                ("1", ClassificationKind.NumberLiteral), ("]", ClassificationKind.Punctuation), (":=", ClassificationKind.Operator),
                ("@", ClassificationKind.Operator), ("r", ClassificationKind.Identifier), (";", ClassificationKind.Punctuation),
                ("end", ClassificationKind.Keyword), (".", ClassificationKind.Punctuation), ("// c", ClassificationKind.Comment),
                ("{ b }", ClassificationKind.Comment), ("/// d", ClassificationKind.DocComment), ("{$R+}", ClassificationKind.Directive),
            ],
            Pieces(text));
    }

    [Theory]
    [Trait("Req", "CMP-710")]
    [InlineData("%1010", ClassificationKind.NumberLiteral)]
    [InlineData("&17", ClassificationKind.NumberLiteral)]
    [InlineData("1..5", ClassificationKind.NumberLiteral)]
    [InlineData("(* c *)", ClassificationKind.Comment)]
    [InlineData("{** doc }", ClassificationKind.DocComment)]
    [InlineData("(*$I-*)", ClassificationKind.Directive)]
    [InlineData("{$I defs.inc}", ClassificationKind.Directive)]
    [InlineData("&type", ClassificationKind.Identifier)]
    [InlineData("String", ClassificationKind.Keyword)]
    [InlineData("Self", ClassificationKind.Identifier)]
    public void Lexical_FirstSpanKind(string text, ClassificationKind kind)
    {
        Assert.Equal(kind, Classify(text)[0].Kind);
    }

    [Theory]
    [Trait("Req", "CMP-710")]
    [InlineData("+ - * / = <> < > <= >= := @ ^ ..", ClassificationKind.Operator)]
    [InlineData(". , ; : ( ) [ ] (. .)", ClassificationKind.Punctuation)]
    public void Lexical_OperatorsAndPunctuation(string text, ClassificationKind kind)
    {
        Assert.All(Classify(text), s => Assert.Equal(kind, s.Kind));
        Assert.Equal(text.Split(' ').Length, Classify(text).Length);
    }

    [Theory]
    [Trait("Req", "CMP-710")]
    [InlineData("'a'", ClassificationKind.CharLiteral)]          // one-char literal
    [InlineData("''''", ClassificationKind.CharLiteral)]         // one apostrophe
    [InlineData("'ab'", ClassificationKind.StringLiteral)]
    [InlineData("''", ClassificationKind.StringLiteral)]          // empty string literal
    [InlineData("#65", ClassificationKind.CharLiteral)]
    [InlineData("#$41", ClassificationKind.CharLiteral)]
    [InlineData("#13#10", ClassificationKind.CharLiteral)]       // adjacent char parts are one span
    public void Lexical_StringLiteralWholeTokenKind(string text, ClassificationKind kind)
    {
        Assert.Equal([(text, kind)], Pieces(text));
    }

    [Fact]
    [Trait("Req", "CMP-710")]
    public void Lexical_StringLiteralParts_AreSplit()
    {
        Assert.Equal(
            [("'It''s'", ClassificationKind.StringLiteral), ("#33#10", ClassificationKind.CharLiteral), ("'x'", ClassificationKind.StringLiteral)],
            Pieces("'It''s'#33#10'x'"));
        Assert.Equal([("'a'", ClassificationKind.StringLiteral), ("#13", ClassificationKind.CharLiteral)], Pieces("'a'#13"));
    }

    // ------------------------------------------------------------------ Invalid

    [Fact]
    [Trait("Req", "CMP-710")]
    public void Lexical_UnterminatedString_IsInvalid()
    {
        Assert.Equal([("x", ClassificationKind.Identifier), ("'abc", ClassificationKind.Invalid), ("y", ClassificationKind.Identifier)], Pieces("x 'abc\ny"));
        Assert.Equal([("'ok'", ClassificationKind.StringLiteral), ("#10", ClassificationKind.CharLiteral), ("'bad", ClassificationKind.Invalid)], Pieces("'ok'#10'bad"));
    }

    [Fact]
    [Trait("Req", "CMP-710")]
    public void Lexical_IllegalCharacter_IsInvalid()
    {
        Assert.Equal([("a", ClassificationKind.Identifier), ("!", ClassificationKind.Invalid), ("b", ClassificationKind.Identifier)], Pieces("a ! b"));
        Assert.Equal(ClassificationKind.Invalid, KindOf("a 😀€ b", "€"));
    }

    [Fact]
    public void Lexical_CharCodeOutOfRange_IsInvalid()
    {
        Assert.Equal([("'a'", ClassificationKind.StringLiteral), ("#70000", ClassificationKind.Invalid)], Pieces("'a'#70000"));
    }

    [Fact]
    public void Lexical_LoneSymbolTokens_AreOperators()
    {
        Assert.Equal([("#", ClassificationKind.Operator), ("$", ClassificationKind.Operator), ("&", ClassificationKind.Operator)], Pieces("# $ &"));
    }

    // ------------------------------------------------------------------ ContextualKeyword (CMP-713)

    [Theory]
    [Trait("Req", "CMP-713")]
    [InlineData("procedure Foo; virtual; abstract;", "virtual", ClassificationKind.ContextualKeyword)]
    [InlineData("procedure Foo; virtual; abstract;", "abstract", ClassificationKind.ContextualKeyword)]
    [InlineData("procedure Foo(A: Integer); overload;", "overload", ClassificationKind.ContextualKeyword)]
    [InlineData("function F: Integer; stdcall; external 'k.dll' name 'F';", "external", ClassificationKind.ContextualKeyword)]
    [InlineData("function F: Integer; stdcall; external 'k.dll' name 'F';", "name", ClassificationKind.Identifier)]
    [InlineData("property Count: Integer read FCount write SetCount default 0;", "read", ClassificationKind.ContextualKeyword)]
    [InlineData("property Count: Integer read FCount write SetCount default 0;", "write", ClassificationKind.ContextualKeyword)]
    [InlineData("property Count: Integer read FCount write SetCount default 0;", "default", ClassificationKind.ContextualKeyword)]
    [InlineData("property Items[I: Integer; J: Integer]: T read GetItem; default;", "read", ClassificationKind.ContextualKeyword)]
    [InlineData("property Items[I: Integer; J: Integer]: T read GetItem; default;", "default", ClassificationKind.ContextualKeyword)]
    [InlineData("property P: Integer index 3 read GetP stored False nodefault;", "index", ClassificationKind.ContextualKeyword)]
    [InlineData("property P: Integer index 3 read GetP stored False nodefault;", "stored", ClassificationKind.ContextualKeyword)]
    [InlineData("property P: Integer index 3 read GetP stored False nodefault;", "nodefault", ClassificationKind.ContextualKeyword)]
    [InlineData("property I: IFoo read FI implements IFoo;", "implements", ClassificationKind.ContextualKeyword)]
    [InlineData("x := read(f);", "read", ClassificationKind.Identifier)]
    [InlineData("property A: Integer; x := read;", "read", ClassificationKind.Identifier)]       // property ended at ';'
    [InlineData("var name: string;", "name", ClassificationKind.Identifier)]
    [InlineData("var message: string;", "message", ClassificationKind.Identifier)]
    [InlineData("a := b; index := 1;", "index", ClassificationKind.ContextualKeyword)]          // CMP-713 (a) approximation
    [InlineData("if f(x) virtual", "virtual", ClassificationKind.ContextualKeyword)]
    [InlineData("procedure P; VIRTUAL;", "VIRTUAL", ClassificationKind.ContextualKeyword)]
    public void Lexical_ContextualKeywordRule(string text, string word, ClassificationKind kind)
    {
        Assert.Equal(kind, KindOf(text, word));
    }

    // ------------------------------------------------------------------ AsmText (CMP-714)

    [Fact]
    [Trait("Req", "CMP-714")]
    public void Lexical_AsmBlock_IsOneAsmTextSpanPerLine()
    {
        const string text = "asm\n  mov eax, [ebx+4] // load\n  ret\nend;";

        Assert.Equal(
            [
                ("asm", ClassificationKind.Keyword), ("mov eax, [ebx+4]", ClassificationKind.AsmText), ("// load", ClassificationKind.Comment),
                ("ret", ClassificationKind.AsmText), ("end", ClassificationKind.Keyword), (";", ClassificationKind.Punctuation),
            ],
            Pieces(text));
    }

    [Fact]
    [Trait("Req", "CMP-714")]
    public void Lexical_AsmBlock_CommentSplitsALine()
    {
        Assert.Equal(
            [
                ("ASM", ClassificationKind.Keyword), ("nop", ClassificationKind.AsmText), ("{ x }", ClassificationKind.Comment),
                ("nop", ClassificationKind.AsmText), ("END", ClassificationKind.Keyword),
            ],
            Pieces("ASM nop { x } nop END"));
    }

    [Fact]
    [Trait("Req", "CMP-714")]
    public void Lexical_AsmWithoutEnd_RunsToTheEndOfFile()
    {
        Assert.Equal([("asm", ClassificationKind.Keyword), ("mov eax, 1", ClassificationKind.AsmText)], Pieces("asm mov eax, 1"));
    }

    [Fact]
    public void Lexical_AsmAsAnIdentifierInExpressions_IsStillPaired()
    {
        // Lexical approximation: any 'asm' identifier token starts an asm block.
        Assert.Equal(ClassificationKind.Keyword, KindOf("x asm y end", "asm"));
    }

    // ------------------------------------------------------------------ directives and disabled text

    [Fact]
    [Trait("Req", "CMP-710")]
    public void Lexical_DisabledText_IsOneSpanAndDirectivesStayDirectives()
    {
        const string text = "{$IFDEF X}\n  a := 'unterminated;\n{$ELSE}\n  b;\n{$ENDIF}";

        Assert.Equal(
            [
                ("{$IFDEF X}", ClassificationKind.Directive), ("\n  a := 'unterminated;\n", ClassificationKind.DisabledText),
                ("{$ELSE}", ClassificationKind.Directive), ("b", ClassificationKind.Identifier), (";", ClassificationKind.Punctuation),
                ("{$ENDIF}", ClassificationKind.Directive),
            ],
            Pieces(text));
        Assert.Equal(
            [("{$IFDEF X}", ClassificationKind.Directive), ("a", ClassificationKind.Identifier), ("{$ENDIF}", ClassificationKind.Directive)],
            Pieces("{$IFDEF X} a {$ENDIF}", ["X"]));
    }

    // ------------------------------------------------------------------ ranges and coverage (CMP-711)

    [Fact]
    [Trait("Req", "CMP-711")]
    public void Lexical_Range_ReturnsOnlyIntersectingSpansClippedToTheRange()
    {
        const string text = "alpha { comment } beta";

        var spans = Classify(text, range: new TextSpan(3, 6));

        Assert.Equal([new ClassifiedSpan(new TextSpan(3, 2), ClassificationKind.Identifier), new ClassifiedSpan(new TextSpan(6, 3), ClassificationKind.Comment)], spans);
        Assert.Empty(Classify(text, range: new TextSpan(5, 1)));
        Assert.Empty(Classify(text, range: new TextSpan(text.Length, 0)));
    }

    [Fact]
    [Trait("Req", "CMP-711")]
    public void Lexical_SpansCoverEveryNonWhitespaceCharacterExactlyOnce()
    {
        var random = new Random(711);
        string[] pieces =
        [
            "begin", "end", "x", "virtual", ";", ")", "property", "read", "asm", "mov", "12", "$FF", "1.5", "'s'", "'open", "#13", "#70000",
            "{ c }", "{ open", "(* c *)", "// l", "/// d", "{$R+}", "{$IFDEF A}", "{$ELSE}", "{$ENDIF}", " ", "\n", "\r\n", "\t", "!", "€",
            "+", ":=", "..", "[", "]", "(", "#", "$", "&",
        ];
        for (var i = 0; i < 300; i++)
        {
            var text = string.Concat(Enumerable.Range(0, random.Next(0, 30)).Select(_ => pieces[random.Next(pieces.Length)]));
            var tokens = Lexer.Tokenize(SourceText.From(Path, text));
            var spans = Classifier.GetLexicalClassifications(tokens, new TextSpan(0, text.Length));

            var covered = new int[text.Length];
            var previousEnd = 0;
            foreach (var span in spans)
            {
                Assert.True(span.Span.Length > 0);
                Assert.True(span.Span.Start >= previousEnd, $"overlap or unsorted in {text}");
                previousEnd = span.Span.End;
                for (var p = span.Span.Start; p < span.Span.End; p++)
                {
                    covered[p]++;
                }
            }

            var whitespace = new bool[text.Length];
            foreach (var trivia in tokens.SelectMany(t => t.LeadingTrivia.Concat(t.TrailingTrivia)))
            {
                if (trivia.Kind is SyntaxKind.WhitespaceTrivia or SyntaxKind.EndOfLineTrivia)
                {
                    for (var p = trivia.Span.Start; p < trivia.Span.End; p++)
                    {
                        whitespace[p] = true;
                    }
                }
            }

            for (var p = 0; p < text.Length; p++)
            {
                // Whitespace may be uncovered (or covered once, inside an AsmText line span); everything else once.
                Assert.True(
                    whitespace[p] ? covered[p] <= 1 : covered[p] == 1,
                    $"char {p} of '{text}' covered {covered[p]} times");
            }
        }
    }

    [Fact]
    public void MalformedInputNeverThrows()
    {
        var random = new Random(131);
        for (var i = 0; i < 300; i++)
        {
            var chars = new char[random.Next(0, 150)];
            for (var j = 0; j < chars.Length; j++)
            {
                chars[j] = random.Next(3) == 0 ? "{}()*/'#$%&._1eE\r\n asmend;"[random.Next(26)] : (char)random.Next(0, 0x10000);
            }

            var text = new string(chars);
            var tokens = Lexer.Tokenize(SourceText.From(Path, text));
            var start = random.Next(0, text.Length + 1);
            var range = new TextSpan(start, random.Next(0, text.Length - start + 1));

            var spans = Classifier.GetLexicalClassifications(tokens, range);

            Assert.All(spans, s => Assert.True(s.Span.Start >= range.Start && s.Span.End <= range.End));
        }

        Assert.Empty(Classifier.GetLexicalClassifications([], new TextSpan(0, 10)));
        Assert.Empty(Classifier.GetLexicalClassifications(default, new TextSpan(0, 10)));
    }

    // ------------------------------------------------------------------ colour tokens (CMP-710 ↔ design-tokens.md §3.1)

    [Theory]
    [InlineData(ClassificationKind.Keyword, "syntax.keyword")]
    [InlineData(ClassificationKind.ContextualKeyword, "syntax.keyword")]
    [InlineData(ClassificationKind.Identifier, "syntax.text")]
    [InlineData(ClassificationKind.CharLiteral, "syntax.char")]
    [InlineData(ClassificationKind.DisabledText, "syntax.inactive")]
    [InlineData(ClassificationKind.Invalid, "syntax.invalid")]
    [InlineData(ClassificationKind.DeprecatedSymbol, "")]
    public void GetColorToken_ExamplesFromTheTable(ClassificationKind kind, string token)
    {
        Assert.Equal(token, Classifier.GetColorToken(kind));
    }

    [Fact]
    [Trait("Category", "Spec")]
    [Trait("Req", "CMP-710")]
    public void ColorTokensMatchDesignTokens()
    {
        var docs = RepoPaths.RequireDocs();
        var cmp710 = TableRows(System.IO.Path.Combine(docs, "spec", "compiler", "language-service.md"), "| Kind | Source |")
            .ToDictionary(r => r[0].Trim('`'), r => r[3] == "—" ? "" : r[3].Trim('`'));
        var designTokens = TableRows(System.IO.Path.Combine(docs, "spec", "ui", "design-tokens.md"), "| Token | Kinds (CMP-710)")
            .Select(r => r[0].Trim('`'))
            .ToHashSet();

        Assert.Equal(cmp710.Keys.Order(StringComparer.Ordinal), Enum.GetNames<ClassificationKind>().Order(StringComparer.Ordinal));
        foreach (var (kind, token) in cmp710)
        {
            Assert.Equal(token, Classifier.GetColorToken(Enum.Parse<ClassificationKind>(kind)));
        }

        Assert.Equal(designTokens.Order(StringComparer.Ordinal), cmp710.Values.Where(t => t.Length > 0).Distinct().Order(StringComparer.Ordinal));
    }

    /// <summary>Cells of the Markdown table whose header line starts with <paramref name="header"/>.</summary>
    private static IEnumerable<string[]> TableRows(string file, string header)
    {
        var lines = File.ReadAllLines(file);
        var i = Array.FindIndex(lines, l => l.StartsWith(header, StringComparison.Ordinal));
        Assert.True(i >= 0, header + " not found in " + file);
        for (i += 2; i < lines.Length && lines[i].StartsWith('|'); i++)
        {
            yield return [.. lines[i].Split('|')[1..^1].Select(c => c.Trim())];
        }
    }
}
