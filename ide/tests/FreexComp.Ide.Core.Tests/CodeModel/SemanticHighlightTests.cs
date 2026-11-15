using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Tests.Build;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Tests.CodeModel;

/// <summary>The P3 classification layer (I-112/I-113, phase1-shell.md §5): semantic kinds and project-defined inactive code.</summary>
[Trait("Phase", "3")]
public sealed class SemanticHighlightTests
{
    private const string Configurations =
        """, "configurations": { "Debug": { "optimization": 0, "debugInfo": true, "defines": ["FEATURE"], "outputDir": "bin/Debug" } }""";

    private const string Unit =
        "unit Unit1;\ninterface\ntype\n  TShape = class\n  end;\nconst\n  Limit = 3;\nprocedure Run;\nimplementation\n" +
        "var\n  Shape: TShape;\nprocedure Run;\nbegin\n{$IFDEF FEATURE}\n  Shape := nil;\n{$ELSE}\n  Shape := TShape.Create;\n{$ENDIF}\nend;\nend.\n";

    internal static async Task<BuildTestBed> StartAsync(string extra = Configurations)
    {
        var bed = new BuildTestBed();
        var src = Path.Combine(SpecTables.RepoRootPath(), "rtl", "src");
        foreach (var file in Directory.GetFiles(src, "*.inc").Append(Path.Combine(src, "System.pas")).Append(Path.Combine(src, "unit-aliases.json")).Where(File.Exists))
            bed.File(@"G:\rtl\" + Path.GetFileName(file), File.ReadAllText(file));
        await bed.OpenHelloAsync(extra: extra);
        Assert.True(await bed.Services.Documents.CloseAllAsync());
        return bed;
    }

    private static string? ClassAt(IReadOnlyList<TokenClassSpan> spans, int offset) =>
        spans.FirstOrDefault(s => s.Offset <= offset && offset < s.Offset + s.Length).TokenClass;

    private static IReadOnlyList<TokenClassSpan> Drawn(TextDocumentViewModel doc) =>
        doc.CreateClassifier().Classify(doc.Buffer, new LineRange(1, doc.Buffer.LineCount));

    [Fact(Timeout = 30000)]
    public async Task TypesAndInactiveCode()
    {
        var bed = await StartAsync();
        bed.File(BuildTestBed.UnitPath, Unit);
        var doc = await bed.OpenDocumentAsync(BuildTestBed.UnitPath);
        var delay = TimeSpan.FromMilliseconds(bed.Services.Settings.Get(SettingsCatalog.EditorLiveDiagnosticsDelayMs));
        int declared = Unit.IndexOf("TShape", StringComparison.Ordinal), used = Unit.IndexOf("TShape;", StringComparison.Ordinal);
        int constant = Unit.IndexOf("Limit", StringComparison.Ordinal), active = Unit.IndexOf("Shape := nil", StringComparison.Ordinal);
        int inactive = Unit.IndexOf("Shape := TShape.Create", StringComparison.Ordinal);

        // Before the layer arrives: lexical colours only (no defines, so the FEATURE branch is the inactive one).
        Assert.Equal(-1, doc.Semantic.Version);
        Assert.Equal("syntax.text", ClassAt(Drawn(doc), declared));
        Assert.Equal("syntax.inactive", ClassAt(Drawn(doc), active));

        bed.Clock.Advance(delay);
        await bed.Services.SemanticHighlights.PendingFor(doc);
        Assert.Equal(doc.Buffer.Version, doc.Semantic.Version);
        var spans = Drawn(doc);
        Assert.Equal("syntax.type", ClassAt(spans, declared));
        Assert.Equal("syntax.type", ClassAt(spans, used));
        Assert.Equal("syntax.constant", ClassAt(spans, constant));
        Assert.Equal("syntax.routine", ClassAt(spans, Unit.IndexOf("Run;", StringComparison.Ordinal)));
        Assert.Equal("syntax.keyword", ClassAt(spans, Unit.IndexOf("begin", StringComparison.Ordinal)));
        // The project's FEATURE define: its branch is active code, the {$ELSE} branch is inactive.
        Assert.NotEqual("syntax.inactive", ClassAt(spans, active));
        Assert.Equal("syntax.inactive", ClassAt(spans, inactive));
        Assert.Equal("syntax.inactive", ClassAt(spans, inactive + "Shape := TShape".Length));

        // An edit: the layer follows the text; the edited token falls back to the lexical colours until the next result.
        BuildTestBed.Type(doc, "\n");
        spans = Drawn(doc);
        Assert.Equal("syntax.type", ClassAt(spans, declared + 1));
        BuildTestBed.Type(doc, "X", declared + 1);
        Assert.Equal("syntax.text", ClassAt(Drawn(doc), declared + 1));
        Assert.Equal("syntax.type", ClassAt(Drawn(doc), used + 2));
        bed.Clock.Advance(delay);
        await bed.Services.SemanticHighlights.PendingFor(doc);
        Assert.Equal(doc.Buffer.Version, doc.Semantic.Version);
        Assert.Equal("syntax.type", ClassAt(Drawn(doc), declared + 1)); // XTShape is a type again
        Assert.Equal("syntax.text", ClassAt(Drawn(doc), used + 2)); // TShape no longer resolves
    }

    /// <summary>Tokens the parser skipped in half-typed code keep their lexical classes (only lexer errors are <c>syntax.invalid</c>).</summary>
    [Fact(Timeout = 30000)]
    public async Task SkippedTokensKeepLexicalColours()
    {
        var bed = await StartAsync();
        var text = Unit.Replace("{$IFDEF FEATURE}", "  := ) Limit 'xy';\n{$IFDEF FEATURE}", StringComparison.Ordinal);
        bed.File(BuildTestBed.UnitPath, text);
        var doc = await bed.OpenDocumentAsync(BuildTestBed.UnitPath);
        bed.Clock.Advance(TimeSpan.FromSeconds(5));
        await bed.Services.SemanticHighlights.PendingFor(doc);
        Assert.Equal(doc.Buffer.Version, doc.Semantic.Version);
        Assert.DoesNotContain(doc.Semantic.Spans, s => s.TokenClass == "syntax.invalid");
        Assert.Equal("syntax.string", ClassAt(Drawn(doc), text.IndexOf("'xy'", StringComparison.Ordinal)));
        Assert.Equal("syntax.symbol", ClassAt(Drawn(doc), text.IndexOf(":= )", StringComparison.Ordinal)));
    }

    [Fact(Timeout = 30000)]
    public async Task LooseFilesKeepLexicalColours()
    {
        var bed = await StartAsync();
        var loose = bed.File(@"C:\elsewhere\Loose.pas", Unit);
        var doc = await bed.OpenDocumentAsync(loose);
        bed.Clock.Advance(TimeSpan.FromSeconds(5));
        await bed.Services.SemanticHighlights.PendingFor(doc);
        Assert.Equal(-1, doc.Semantic.Version);
    }
}
