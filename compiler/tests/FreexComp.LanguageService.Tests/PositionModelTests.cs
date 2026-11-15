// Model lookups of an in-memory snapshot are synchronous; the optional token is left out on purpose.
#pragma warning disable xUnit1051

using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.LanguageService.Tests;

/// <summary>
/// C-244 (CMP-702): completion binds only the body around the caret (<c>Compilation.GetSemanticModel(path, position)</c>), and later
/// snapshots build on that partial run. Differential tests: the completion lists equal those of the full model at many positions of
/// golden programs, and the diagnostics after a series of edits answered from position models equal a fresh full compilation's.
/// </summary>
[Trait("Phase", "3")]
[Trait("Kind", "Unit")]
[Trait("Req", "CMP-702")]
public sealed class PositionModelTests
{
    private static readonly CompletionTrigger Invoked = new(CompletionTriggerKind.Invoked, '\0');

    public static TheoryData<string> GoldenPrograms()
    {
        var data = new TheoryData<string>();
        var root = Path.Combine(RepoPaths.Root, "tests", "compiler");
        foreach (var file in Directory.GetFiles(root, "*.pas", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var text = File.ReadAllText(file);
            if (!text.Contains("// opc:", StringComparison.Ordinal) && !System.Text.RegularExpressions.Regex.IsMatch(text, @"(?im)^\s*uses\b|\{\$I")
                && System.Text.RegularExpressions.Regex.IsMatch(text, @"^program\s", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                && (text.Contains("procedure", StringComparison.OrdinalIgnoreCase) || text.Contains("function", StringComparison.OrdinalIgnoreCase)))
            {
                data.Add(Path.GetRelativePath(root, file));
            }

            if (data.Count == 15)
            {
                break;
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(GoldenPrograms))]
    public void CompletionLists_EqualThoseOfTheFullModel(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(RepoPaths.Root, "tests", "compiler", relativePath)).ReplaceLineEndings("\n");
        var tree = SyntaxTree.Parse(SourceText.From(TestFiles.PathOf("P.pas"), text));
        var positions = tree.Root.DescendantTokens().Where(t => t.Kind == SyntaxKind.IdentifierToken && !t.IsMissing)
            .Select((t, i) => (t, i)).Where(p => p.i % 5 == 0).Select(p => p.t.Span.End).Take(30).ToList();
        Assert.NotEmpty(positions);

        var path = TestFiles.PathOf("P.pas");
        var full = Open(text);
        var partial = Open(text);
        _ = partial.GetDocument(path).SemanticModel;                               // the first snapshot is analysed in full
        for (var i = 0; i < positions.Count; i++)
        {
            // each edit (trailing spaces) gives a snapshot whose completion uses a position model built on the previous run
            var position = positions[i];
            var edited = text + new string(' ', i + 1);
            full.UpdateDocument(path, edited);
            _ = full.GetDocument(path).SemanticModel;                              // the full model exists: completion uses it
            partial.UpdateDocument(path, edited);
            var document = partial.GetDocument(path);
            var positionModel = document.Compilation.GetSemanticModel(document.Path, position);
            var expected = Items(full.GetDocument(path), position);
            var actual = Items(document, position);

            Assert.NotSame(document.Compilation.GetSemanticModel(document.Path), positionModel);
            Assert.True(expected.SequenceEqual(actual), $"{relativePath} at {position}: {Diff(expected, actual)}");
        }
    }

    [Fact]
    public void DiagnosticsAfterEditsAnsweredByPositionModels_EqualAFreshCompilation()
    {
        const string unit = "unit U1;\ninterface\nfunction Twice(A: Integer): Integer;\nfunction Thrice(A: Integer): Integer;\nimplementation\n"
                            + "function Twice(A: Integer): Integer;\nvar\n  T: Integer;\nbegin\n  T := A;\n  Result := T * 2;\nend;\n\n"
                            + "function Thrice(A: Integer): Integer;\nbegin\n  Result := Twice(A) + A;\nend;\nend.";
        const string program = "program P;\nuses U1;\nvar X: Integer;\nbegin\n  X := Thrice(2);\n  Writeln(Twice(X));\nend.";
        var workspace = new Workspace(TestFiles.Files(("P.pas", program), ("U1.pas", unit)));
        workspace.SetProject(TestFiles.Options());
        var path = TestFiles.PathOf("U1.pas");
        var random = new Random(244);
        string[] inserts = ["A", " ", "+ 1", ";", "X", "T", ".", "Result", "(", "end;", "Hidden", "\n"];
        for (var step = 0; step < 40; step++)
        {
            var current = workspace.GetDocument(path).Text.Text;
            var body = current.IndexOf("begin", current.IndexOf("implementation", StringComparison.Ordinal), StringComparison.Ordinal);
            var position = random.Next(body, current.LastIndexOf("end.", StringComparison.Ordinal));
            if (random.Next(3) == 0 && position < current.Length - 1)
            {
                workspace.UpdateDocument(path, [new TextChange(new TextSpan(position, 1), "")]);
            }
            else
            {
                workspace.UpdateDocument(path, [new TextChange(new TextSpan(position, 0), inserts[random.Next(inserts.Length)])]);
            }

            _ = CompletionService.GetCompletions(workspace.GetDocument(path), Math.Min(position, workspace.GetDocument(path).Text.Text.Length), Invoked, default);
            if (step % 4 == 3)
            {
                var snapshot = workspace.CurrentCompilation;
                var fresh = Compilation.Create(TestFiles.Options(), [SourceText.From(path, snapshot.GetSyntaxTree(path).Text.Text)],
                    TestFiles.Files(("P.pas", program), ("U1.pas", unit)));
                Assert.Equal(Render(fresh.GetDiagnostics()), Render(snapshot.GetDiagnostics()));
            }
        }
    }

    private static Workspace Open(string text)
    {
        var workspace = new Workspace(TestFiles.Files(("P.pas", text)));
        workspace.SetProject(TestFiles.Options());
        return workspace;
    }

    private static List<string> Items(Document document, int position) =>
        [.. CompletionService.GetCompletions(document, position, Invoked, default).Items
            .Select(i => $"{i.Label}|{i.Kind}|{i.InsertText}|{i.SortText}|{i.Detail}|{i.IsDeprecated}")];

    private static string Diff(List<string> expected, List<string> actual) =>
        "missing [" + string.Join(", ", expected.Except(actual).Take(5)) + "] extra [" + string.Join(", ", actual.Except(expected).Take(5)) + "]";

    private static List<string> Render(IEnumerable<Diagnostic> diagnostics) => [.. diagnostics.Select(d => d.ToString())];
}
