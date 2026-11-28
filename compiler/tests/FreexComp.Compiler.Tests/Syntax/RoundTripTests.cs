using System.Text;
using System.Text.RegularExpressions;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Tests.Syntax;

/// <summary>
/// api.md CMP-220 and grammar.md LANG-180: for every input — repository sources, spec examples, random token
/// sequences, random characters, mutated valid programs — the parser never throws, the tree ends at the end-of-file
/// token and <c>Root.ToFullString() == Text.Text</c> (C-020). All random inputs use fixed seeds.
/// </summary>
[Trait("Phase", "2")]
[Trait("Kind", "Unit")]
[Trait("Req", "CMP-220")]
[Trait("Req", "LANG-180")]
public sealed class RoundTripTests
{
    private const string Path = @"C:\p\fuzz.pas";

    private static readonly string[] Vocabulary =
    [
        .. SyntaxFacts.ReservedWords, .. SyntaxFacts.ContextualKeywords,
        "+", "-", "*", "/", "=", "<>", "<", ">", "<=", ">=", ":=", ".", "..", ",", ";", ":", "(", ")", "[", "]", "^", "@",
        "#", "$", "&", "(.", ".)", "A", "B", "TFoo", "&begin", "1", "2.5", "1e3", "$FF", "%101", "&17", "'s'", "''", "#13",
        "'open", "{c}", "(* c *)", "// c\n", "{$IFDEF X}", "{$ELSE}", "{$ENDIF}", "{$R+}", "{ open", "\r\n", "\n", " ", "\t", "?",
    ];

    /// <summary>Error-free sources that cover the whole grammar; the mutation corpus.</summary>
    private static readonly string[] ValidSources =
    [
        """
        program Demo;
        uses Sys.Utils, Other in 'other.pas';
        label 10;
        const N = 10; Names: array[0..1] of string = ('a', 'b'); Origin: TPoint = (X: 0; Y: -1);
        type
          TPoint = record X, Y: Integer; end;
          TDigits = set of '0'..'9';
          TProc = reference to procedure(const S: string);
          TShape = class abstract(TInterfacedObject, IShape)
          strict private
            FName: string;
          public
            constructor Create(const AName: string); virtual;
            function Area: Double; virtual; abstract;
            property Name: string read FName write FName;
            property Items[I: Integer]: TShape read GetItem; default;
          end;
          [Tag] TList<T: class> = class(TEnumerable<T>)
            function Map<R>(const F: TFunc<T, R>): TList<R>;
          end;
        var I: Integer; P: ^TPoint;
        function Max(A, B: Integer): Integer; inline;
        begin
          if A > B then Result := A else Result := B;
        end;
        begin
          I := Max(1, 2) * -3 + Ord('A') div 2;
          case I of 1, 3..5: WriteLn('odd'); else Exit; end;
          for var K := 0 to 9 do while not (I = 0) do Dec(I);
          repeat Inc(I) until (I >= 10) or (I < 0);
          try raise EError.Create('x') except on E: EError do WriteLn(E.Message) end;
          Run(procedure begin I := TList<Integer>.Count end);
        10:
        end.
        """,
        """
        unit Sys.Utils deprecated;
        interface
        uses A;
        type
          IShape = interface ['{8E2B1C3A-0000-4000-8000-000000000001}'] function Area: Double; end;
          TVec = record private FX: Single; public class operator Add(const A, B: TVec): TVec; end;
        procedure P(var X; out Y: Integer); stdcall;
        implementation
        procedure P(var X; out Y: Integer);
        begin
          Y := Integer(X) + &begin;
          with Q do try Y := 1 finally Y := 2 end;
        end;
        initialization
          P(Q, R);
        finalization
        end.
        """,
        """
        program Literals;
        const K = Integer.Ten * 2; Big = 3000000000;
        begin
          WriteLn(42.Squared, (1 + 2).Squared, -5.Squared, $10.Squared, 1.5.Half:0:2, 1.5e1.Half);
          WriteLn('abc'.Len, 'abc'.Twice.Len, ''.Len, #55.IsDigit, 'x'.Len, True.ToText, 42.ToText.Chars[0]);
          WriteLn(string.Repeated('ab', 2), string.Join(',', ['a', 'b']), Integer.Parse('12') + 1, 1.e5);
          case K of 1..5: WriteLn(K); end;
        end.
        """,
    ];

    [Fact]
    public void RandomTokenSequences_NeverThrowAndRoundTrip()
    {
        var random = new Random(20201);
        for (var i = 0; i < 1500; i++)
        {
            var builder = new StringBuilder();
            for (var n = random.Next(0, 40); n > 0; n--)
            {
                builder.Append(Vocabulary[random.Next(Vocabulary.Length)]).Append(random.Next(3) == 0 ? "" : " ");
            }

            AssertRoundTrip(builder.ToString());
        }
    }

    [Fact]
    public void RandomCharacters_NeverThrowAndRoundTrip()
    {
        const string alphabet = "abzAZ_09 \t\r\n;:.,=<>+-*/()[]{}'#$%&@^\"?\\~`|!\u00e9\u0000\u2026";
        var random = new Random(20202);
        for (var i = 0; i < 500; i++)
        {
            var chars = new char[random.Next(0, 60)];
            for (var c = 0; c < chars.Length; c++)
            {
                chars[c] = alphabet[random.Next(alphabet.Length)];
            }

            AssertRoundTrip(new string(chars));
        }
    }

    [Fact]
    [Trait("Req", "LANG-997")]
    public void ValidSources_ParseWithoutDiagnostics()
    {
        foreach (var text in ValidSources)
        {
            var tree = AssertRoundTrip(text);
            Assert.Empty(tree.Diagnostics);
        }
    }

    [Fact]
    public void MutatedValidSources_NeverThrowAndRoundTrip()
    {
        var random = new Random(20203);
        foreach (var source in ValidSources)
        {
            var tokens = Lexer.Tokenize(SourceText.From(Path, source)).Select(t => t.ToFullString()).ToList();
            for (var i = 0; i < 100; i++)
            {
                var mutated = new List<string>(tokens);
                for (var m = random.Next(1, 4); m > 0; m--)
                {
                    var at = random.Next(mutated.Count);
                    switch (random.Next(5))
                    {
                        case 0:
                            mutated.RemoveAt(at);
                            break;
                        case 1:
                            mutated.Insert(at, mutated[at]);
                            break;
                        case 2:
                            mutated.Insert(at, " " + Vocabulary[random.Next(Vocabulary.Length)] + " ");
                            break;
                        case 3 when at + 1 < mutated.Count:
                            (mutated[at], mutated[at + 1]) = (mutated[at + 1], mutated[at]);
                            break;
                        default:
                            mutated.RemoveRange(at, mutated.Count - at);   // truncation
                            break;
                    }

                    if (mutated.Count == 0)
                    {
                        break;
                    }
                }

                AssertRoundTrip(string.Concat(mutated));
            }
        }
    }

    [Fact]
    public void RepositorySources_RoundTrip()
    {
        var files = new[] { "tests", System.IO.Path.Combine("rtl", "src") }
            .Select(folder => System.IO.Path.Combine(RepoPaths.Root, folder))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories))
            .Where(file => file.EndsWith(".pas", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".inc", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var tree = SyntaxTree.Parse(SourceText.From(file, text));
            Assert.Equal(text, tree.Root.ToFullString());
        }
    }

    [Fact]
    [Trait("Category", "Spec")]
    public void SpecExamples_RoundTrip()
    {
        var docs = RepoPaths.RequireDocs();
        var blocks = Directory.EnumerateFiles(System.IO.Path.Combine(docs, "spec"), "*.md", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), @"```pascal\r?\n(.*?)```", RegexOptions.Singleline)
                .Select(m => m.Groups[1].Value))
            .ToList();

        Assert.NotEmpty(blocks);
        foreach (var block in blocks)
        {
            AssertRoundTrip(block);
        }
    }

    private static SyntaxTree AssertRoundTrip(string text)
    {
        var tree = SyntaxTree.Parse(SourceText.From(Path, text));
        Assert.Equal(text, tree.Root.ToFullString());
        Assert.Equal(SyntaxKind.EndOfFileToken, tree.Root.LastToken().Kind);
        Assert.True(tree.Diagnostics.Count(d => d.Code.StartsWith("E11", StringComparison.Ordinal)) <= 101, text);   // LANG-184
        return tree;
    }
}
