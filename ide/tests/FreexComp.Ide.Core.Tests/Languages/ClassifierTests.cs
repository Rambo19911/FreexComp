using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Tests.Languages;

[Trait("Phase", "1")]
public sealed class ClassifierTests
{
    private static List<(string Text, string Token)> Classify(ILanguageDefinition language, string text, LineRange? lines = null)
    {
        var buffer = new StringTextBuffer(text);
        var spans = language.CreateClassifier().Classify(buffer, lines ?? new LineRange(1, buffer.LineCount));
        for (int i = 1; i < spans.Count; i++)
            Assert.True(spans[i - 1].Offset + spans[i - 1].Length <= spans[i].Offset, "spans are sorted and do not overlap");
        return spans.Select(s => (buffer.GetText(s.Offset, s.Length), s.TokenClass)).ToList();
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-055")]
    public void FormAndJsonLanguages()
    {
        const string kw = "syntax.keyword", type = "syntax.type", str = "syntax.string", num = "syntax.number",
            con = "syntax.constant", sym = "syntax.symbol", bad = "syntax.invalid";

        // .form (form-format.md §1, design-tokens.md §3.1): property and component names stay unclassified.
        const string form = """
            object MainForm: TMainForm
              Caption = 'it''s'#13#10'x' +
                'y'
              Left = -8
              Width = $FF
              Scale = 1.5
              Anchors = [akLeft, akTop]
              Lines = (
                'a'
              )
              Data = {
                0A1B
              }
              Items = <
                item
                  Kind = ikOne
                end>
              OnClick = Button1Click
              Font.Style = []
              Item = 1
              object Btn: TButton
              end
              Hint = 'open
            END
            @
            """;
        (string, string)[] expectedForm =
        [
            ("object", kw), (":", sym), ("TMainForm", type),
            ("=", sym), ("'it''s'#13#10'x'", str), ("+", sym), ("'y'", str),
            ("=", sym), ("-8", num),
            ("=", sym), ("$FF", num),
            ("=", sym), ("1.5", num),
            ("=", sym), ("[", sym), ("akLeft", con), (",", sym), ("akTop", con), ("]", sym),
            ("=", sym), ("(", sym), ("'a'", str), (")", sym),
            ("=", sym), ("{", sym), ("0A1B", num), ("}", sym),
            ("=", sym), ("<", sym), ("item", kw), ("=", sym), ("ikOne", con), ("end", kw), (">", sym),
            ("=", sym), ("Button1Click", con),
            (".", sym), ("=", sym), ("[", sym), ("]", sym),
            ("=", sym), ("1", num),                                  // "Item" here is a property name
            ("object", kw), (":", sym), ("TButton", type), ("end", kw),
            ("=", sym), ("'open", bad),                              // unterminated string: to the end of the line
            ("END", kw),                                             // keywords are case-insensitive
            ("@", bad),
        ];
        Assert.Equal(expectedForm, Classify(new FormLanguage(), form));

        // JSON (RFC 8259): keys are syntax.type.
        const string json = """
            {
              "name": "App",
              "version": 2,
              "ratio": -1.5e3,
              "flags": [true, false, null],
              "esc": "a\"b",
              "bad": tru,
              "open": "x
            }
            """;
        (string, string)[] expectedJson =
        [
            ("{", sym),
            ("\"name\"", type), (":", sym), ("\"App\"", str), (",", sym),
            ("\"version\"", type), (":", sym), ("2", num), (",", sym),
            ("\"ratio\"", type), (":", sym), ("-1.5e3", num), (",", sym),
            ("\"flags\"", type), (":", sym), ("[", sym), ("true", kw), (",", sym), ("false", kw), (",", sym), ("null", kw), ("]", sym), (",", sym),
            ("\"esc\"", type), (":", sym), ("\"a\\\"b\"", str), (",", sym),
            ("\"bad\"", type), (":", sym), ("tru", bad), (",", sym),
            ("\"open\"", type), (":", sym), ("\"x", bad),
            ("}", sym),
        ];
        Assert.Equal(expectedJson, Classify(new JsonLanguage(), json));

        // A line range returns only the spans on those lines.
        Assert.Equal([("\"version\"", type), (":", sym), ("2", num), (",", sym)], Classify(new JsonLanguage(), json, new LineRange(3, 3)));

        // Plain text has no colours.
        Assert.Empty(Classify(new PlainTextLanguage(), form));

        // Never throws on byte soup (IDE-P1-052 applied to these lexers).
        var random = new Random(1234);
        const string alphabet = "objectendiTm:=+-.$#'\"{}[]()<>,0123456789eE\\ \t\r\n@é\ud800";
        foreach (var language in new ILanguageDefinition[] { new FormLanguage(), new JsonLanguage() })
        {
            for (int seed = 0; seed < 300; seed++)
            {
                var soup = new string(Enumerable.Range(0, random.Next(0, 80)).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
                Classify(language, soup);
            }
        }
    }

    /// <summary>
    /// testing.md §4.2: each <c>.classes</c> line is <c>&lt;line&gt;:&lt;startCol&gt;-&lt;endCol&gt; &lt;tokenClass&gt;</c>,
    /// 1-based, end column inclusive; a span crossing line breaks is written once per line (delimiters excluded).
    /// </summary>
    private static string FormatGolden(ILanguageDefinition language, string text)
    {
        var buffer = new StringTextBuffer(text);
        var lines = new List<string>();
        foreach (var span in language.CreateClassifier().Classify(buffer, new LineRange(1, buffer.LineCount)))
        {
            int offset = span.Offset, end = span.Offset + span.Length;
            while (offset < end)
            {
                var position = buffer.GetPosition(offset);
                var (lineOffset, lineLength, _) = buffer.GetLine(position.Line);
                int pieceEnd = Math.Min(end, lineOffset + lineLength);
                if (pieceEnd > offset)
                    lines.Add($"{position.Line}:{position.Column}-{position.Column + pieceEnd - offset - 1} {span.TokenClass}");
                offset = position.Line < buffer.LineCount ? buffer.GetLine(position.Line + 1).Offset : end;
            }
        }
        return string.Join("\n", lines);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-051")]
    [Trait("Req", "IDE-P1-056")]
    public void GoldenFilesMatch()
    {
        var registry = new LanguageRegistry();
        var directory = Path.Combine(AppContext.BaseDirectory, "TestData", "files", "highlight");
        var sources = Directory.GetFiles(directory).Where(f => !f.EndsWith(".classes", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToList();

        string[] pascalCases = ["asm", "chars", "comments", "contextual", "directives", "keywords", "multiline-string", "numbers", "strings", "unterminated"];
        Assert.Equal(pascalCases, sources.Where(f => f.EndsWith(".pas", StringComparison.Ordinal)).Select(Path.GetFileNameWithoutExtension));
        Assert.Contains(sources, f => Path.GetFileName(f) == "sample.form");
        Assert.Contains(sources, f => Path.GetFileName(f) == "sample.pproj");

        var failures = new List<string>();
        foreach (var source in sources)
        {
            var language = registry.ForPath(source);
            Assert.NotEqual("plaintext", language.Id);
            var expected = File.ReadAllText(source + ".classes").ReplaceLineEndings("\n").TrimEnd('\n');
            var actual = FormatGolden(language, File.ReadAllText(source));
            if (expected != actual)
                failures.Add($"--- {Path.GetFileName(source)}: actual ---\n{actual}");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-050")]
    public void KeywordSetComesFromCompiler()
    {
        var registry = new LanguageRegistry();
        var pascal = registry.GetById("pascal")!;
        Assert.IsType<PascalLanguage>(pascal);
        foreach (var extension in new[] { ".pas", ".pp", ".inc" })
            Assert.Same(pascal, registry.ForPath("Unit1" + extension));
        Assert.Equal("Object Pascal", pascal.DisplayName);
        Assert.Equal("//", pascal.LineComment);
        Assert.Equal(("{", "}"), pascal.BlockComment);

        // Every reserved word of the compiler is a keyword, in any letter case.
        Assert.NotEmpty(FreexComp.Compiler.Syntax.SyntaxFacts.ReservedWords);
        foreach (var word in FreexComp.Compiler.Syntax.SyntaxFacts.ReservedWords)
        {
            Assert.Equal([(word, "syntax.keyword")], Classify(pascal, word));
            Assert.Equal([(word.ToUpperInvariant(), "syntax.keyword")], Classify(pascal, word.ToUpperInvariant()));
        }

        // A contextual keyword is one only in directive position (CMP-713), an identifier elsewhere.
        Assert.Equal(("virtual", "syntax.keyword"), Classify(pascal, "procedure P; virtual;")[3]);
        Assert.Equal(("virtual", "syntax.text"), Classify(pascal, "virtual := 1;")[0]);

        // The IDE holds no Pascal keyword table: no field of PascalLanguage or its nested types holds a reserved word.
        var reserved = FreexComp.Compiler.Syntax.SyntaxFacts.ReservedWords.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var types = new[] { typeof(PascalLanguage) }.Concat(typeof(PascalLanguage).GetNestedTypes(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic));
        const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly;
        foreach (var type in types)
        {
            foreach (var field in type.GetFields(all))
            {
                Assert.False(field.FieldType == typeof(string) && field.IsLiteral && reserved.Contains((string)field.GetRawConstantValue()!),
                    $"{type.Name}.{field.Name} is a keyword constant");
                if (field.IsStatic && !field.IsLiteral && typeof(System.Collections.Generic.IEnumerable<string>).IsAssignableFrom(field.FieldType))
                {
                    var values = (IEnumerable<string>?)field.GetValue(null) ?? [];
                    Assert.DoesNotContain(values, reserved.Contains);
                }
            }
        }
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-052")]
    public void MalformedInputNeverThrows()
    {
        var pascal = new PascalLanguage();
        const string alphabet = "beginendasmprocedurevirtual'#$&%{}(*)/\\0123456789_.,;:=<>^@[]+-\" \t\r\n\0Ć©𐀀￿";
        for (int seed = 0; seed < 1000; seed++)
        {
            var random = new Random(seed);
            var soup = new string(Enumerable.Range(0, random.Next(0, 120)).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
            var buffer = new StringTextBuffer(soup);
            var classifier = pascal.CreateClassifier();
            int first = random.Next(1, buffer.LineCount + 1);
            var spans = classifier.Classify(buffer, new LineRange(first, random.Next(first, buffer.LineCount + 2)));
            Assert.All(spans, s => Assert.True(s.Offset >= 0 && s.Length > 0 && s.Offset + s.Length <= soup.Length && s.TokenClass.Length > 0));
            for (int i = 1; i < spans.Count; i++)
                Assert.True(spans[i - 1].Offset + spans[i - 1].Length <= spans[i].Offset, $"seed {seed}: spans overlap");
        }

        // Unterminated string: to the end of its line; unterminated comment: to the end of the file (IDE-P1-052).
        Assert.Equal([("x", "syntax.text"), (":=", "syntax.symbol"), ("'abc", "syntax.invalid"), ("y", "syntax.text")],
            Classify(pascal, "x := 'abc\ny"));
        Assert.Equal([("x", "syntax.text"), ("{ open\nstill", "syntax.comment")], Classify(pascal, "x { open\nstill"));
    }
}
