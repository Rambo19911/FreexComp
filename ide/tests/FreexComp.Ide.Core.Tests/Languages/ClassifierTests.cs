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
}
