using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Tests.Editing;

[Trait("Phase", "1")]
public sealed class FoldingTests
{
    private static string Format(IEnumerable<FoldRegion> regions) => string.Join("\n", regions.Select(r => r.ToString()));

    /// <summary>testing.md §4: each <c>.folds</c> line is <c>&lt;startLine&gt;-&lt;endLine&gt; &lt;kind&gt;</c>, in region order.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-050")]
    [Trait("Req", "IDE-ED-051")]
    [Trait("Req", "IDE-ED-052")]
    [Trait("Req", "IDE-ED-053")]
    [Trait("Req", "IDE-P1-060")]
    public void GoldenFilesMatch()
    {
        var registry = new LanguageRegistry();
        var directory = Path.Combine(AppContext.BaseDirectory, "TestData", "folding");
        var sources = Directory.GetFiles(directory).Where(f => !f.EndsWith(".folds", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToList();
        string[] pascalCases = ["blocks", "classes", "comments", "conditionals", "regions", "routines", "sections", "unbalanced"];
        Assert.Equal(pascalCases, sources.Where(f => f.EndsWith(".pas", StringComparison.Ordinal)).Select(Path.GetFileNameWithoutExtension));
        Assert.Contains(sources, f => Path.GetFileName(f) == "sample.form");
        Assert.Contains(sources, f => Path.GetFileName(f) == "sample.json");

        var failures = new List<string>();
        foreach (var source in sources)
        {
            var provider = registry.ForPath(source).CreateFoldingProvider();
            Assert.NotNull(provider);
            var buffer = new StringTextBuffer(File.ReadAllText(source));
            var regions = provider.GetRegions(buffer);
            foreach (var r in regions)
            {
                // IDE-ED-050: from the end of the start line's text to the end of the end line.
                var (startOffset, startLength, _) = buffer.GetLine(r.StartLine);
                var (endOffset, endLength, _) = buffer.GetLine(r.EndLine);
                Assert.True(r.StartLine < r.EndLine);
                Assert.Equal(startOffset + buffer.GetText(startOffset, startLength).TrimEnd().Length, r.StartOffset);
                Assert.Equal(endOffset + endLength, r.EndOffset);
            }

            var expected = File.ReadAllText(source + ".folds").ReplaceLineEndings("\n").TrimEnd('\n');
            var actual = Format(regions);
            if (expected != actual)
                failures.Add($"--- {Path.GetFileName(source)}: actual ---\n{actual}");
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));

        // (g) the box of a {$REGION} shows its name; others show "…".
        var named = new PascalFoldingProvider().GetRegions(new StringTextBuffer(File.ReadAllText(Path.Combine(directory, "regions.pas"))));
        Assert.Equal(["…", "Public types", "…", "Inner", "…"], named.Select(r => r.Title));

        // Plain text has no regions; byte soup never throws.
        Assert.Null(new PlainTextLanguage().CreateFoldingProvider());
        const string alphabet = "beginendtrycaserepeatuntilclassrecordobjectinterface=;(){}$REGIONIFDEFENDIF/' \n";
        var random = new Random(4711);
        foreach (var language in new ILanguageDefinition[] { new PascalLanguage(), new FormLanguage(), new JsonLanguage() })
        {
            for (int seed = 0; seed < 200; seed++)
            {
                var soup = new string(Enumerable.Range(0, random.Next(0, 120)).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
                language.CreateFoldingProvider()!.GetRegions(new StringTextBuffer(soup));
            }
        }
    }

    private static TextDocumentViewModel Pascal(string text) =>
        new(new StringTextBuffer(text), @"C:\p\Unit1.pas", null, new PascalLanguage(), FileEncoding.Utf8, FreexComp.Ide.Core.Services.LineEndingKind.Lf);

    private const string Source = """
        unit Unit1;
        interface
        implementation
        procedure A;
        begin
          Writeln;
        end;
        procedure B;
        begin
          Writeln;
        end;
        end.
        """;

    private static FoldRegion Routine(TextDocumentViewModel document, string heading) =>
        document.Folding.Regions.Single(r => r.Kind == FoldKind.Routine && r.StartLineText == heading);

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-056")]
    [Trait("Req", "IDE-ED-057")]
    [Trait("Req", "IDE-ED-055")]
    [Trait("Req", "IDE-P1-061")]
    public void StatePreservedAcrossEdits()
    {
        var document = Pascal(Source);
        var buffer = document.Buffer;
        var folding = document.Folding;
        int changed = 0;
        folding.Changed += (_, _) => changed++;
        folding.SetFolded(Routine(document, "procedure B;"), true);
        Assert.True(changed > 0);
        Assert.Equal([8], document.ViewState.FoldedLines);

        // Lines inserted above and an edit in another region: B stays folded at its shifted line.
        buffer.ApplyEdits([new TextEdit(buffer.GetLine(4).Offset, 0, "// note\n")]);
        buffer.ApplyEdits([new TextEdit(buffer.GetLine(7).Offset, 0, "  ")]);
        Assert.True(Routine(document, "procedure B;").IsFolded);
        Assert.Equal(9, Routine(document, "procedure B;").StartLine);
        Assert.Equal([9], document.ViewState.FoldedLines);
        Assert.False(Routine(document, "procedure A;").IsFolded);

        // Undoing restores the text; the fold stays.
        buffer.Undo();
        buffer.Undo();
        Assert.Equal(Source, buffer.Text);
        Assert.Equal([8], document.ViewState.FoldedLines);

        // A changed start line is another region: unfolded.
        buffer.ApplyEdits([new TextEdit(buffer.GetLine(8).Offset + "procedure ".Length, 1, "C")]);
        Assert.DoesNotContain(folding.Regions, r => r.IsFolded);
        Assert.Empty(document.ViewState.FoldedLines);
        buffer.Undo();

        // Typing right after the "…" box unfolds it (IDE-ED-055).
        var b = Routine(document, "procedure B;");
        folding.SetFolded(b, true);
        buffer.ApplyEdits([new TextEdit(b.EndOffset, 0, " ")]);
        Assert.False(Routine(document, "procedure B;").IsFolded);

        // A caret placed inside a folded region unfolds it; the restored session list folds again (IDE-ED-057).
        document.ViewState.FoldedLines = [4, 8, 99];
        Assert.True(Routine(document, "procedure A;").IsFolded);
        Assert.True(Routine(document, "procedure B;").IsFolded);
        Assert.Equal([4, 8], document.ViewState.FoldedLines);
        document.ViewState.Caret = new TextPosition(10, 3);
        Assert.False(Routine(document, "procedure B;").IsFolded);
        Assert.True(Routine(document, "procedure A;").IsFolded);
        Assert.Equal([4], document.ViewState.FoldedLines);

        // Revert keeps the folds whose start line is unchanged.
        document.Reload(Source.Replace("Writeln;", "Halt;", StringComparison.Ordinal));
        Assert.True(Routine(document, "procedure A;").IsFolded);
        Assert.Equal([4], document.ViewState.FoldedLines);
    }

    /// <summary>Pascal regions; for a buffer other than <see cref="Document"/> (a background snapshot) it first waits for <see cref="Gate"/>.</summary>
    private sealed class GatedProvider : IFoldingProvider
    {
        private readonly PascalFoldingProvider _inner = new();

        public ITextBuffer? Document { get; set; }

        public ManualResetEventSlim Gate { get; } = new(false);

        /// <summary>True on a thread while the test calls into the model synchronously (a thread-pool thread that runs later sees false).</summary>
        public ThreadLocal<bool> InsideCaller { get; } = new();

        public System.Collections.Concurrent.ConcurrentQueue<(bool OnDocument, bool Synchronous, string Text)> Calls { get; } = new();

        public IReadOnlyList<FoldRegion> GetRegions(ITextBuffer buffer)
        {
            var onDocument = ReferenceEquals(buffer, Document);
            Calls.Enqueue((onDocument, InsideCaller.Value, buffer.Text));
            if (!onDocument)
                Gate.Wait(TimeSpan.FromSeconds(20));
            return _inner.GetRegions(buffer);
        }
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-081")]
    [Trait("Req", "IDE-ARCH-083")]
    [Trait("Req", "IDE-P1-061")]
    [Trait("Req", "IDE-ARCH-033")]
    public async Task LargeBufferRecomputesInBackground()
    {
        // A small buffer: computed at once, on the calling thread, from the document buffer itself.
        var small = new StringTextBuffer("procedure A;\r\nbegin\r\nend;\r\n");
        var smallProvider = new GatedProvider { Document = small };
        smallProvider.InsideCaller.Value = true;
        var smallModel = new FoldingModel(small, new EditorViewState(), smallProvider);
        smallProvider.InsideCaller.Value = false;
        Assert.Equal("1-3 routine", Format(smallModel.Regions));
        Assert.True(smallProvider.Calls.TryDequeue(out var call) && call.OnDocument && call.Synchronous);

        // Larger than 200 KB: a snapshot of the text is folded on the thread pool; nothing changes until it arrives.
        var filler = string.Concat(Enumerable.Repeat("// " + new string('x', 97) + "\r\n", WholeTextClassifier.BackgroundThreshold / 100 + 10));
        var buffer = new StringTextBuffer("procedure A;\r\nbegin\r\nend;\r\n" + filler);
        Assert.True(buffer.Length > WholeTextClassifier.BackgroundThreshold);
        var provider = new GatedProvider { Document = buffer };
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.InsideCaller.Value = true;
        var model = new FoldingModel(buffer, new EditorViewState(), provider);
        model.Changed += (_, _) =>
        {
            if (Format(model.Regions).StartsWith("1-3 routine\n4-6 routine", StringComparison.Ordinal))
                changed.TrySetResult();
        };
        Assert.Empty(model.Regions);

        // Two edits while the first run waits: the outdated results are dropped, the latest text is folded.
        buffer.ApplyEdits([new TextEdit(buffer.Length, 0, "procedure Z;\r\nbegin\r\nend;\r\n")]);
        buffer.ApplyEdits([new TextEdit(0, 0, "procedure B;\r\nbegin\r\nend;\r\n")]);
        Assert.Empty(model.Regions);
        provider.InsideCaller.Value = false;
        provider.Gate.Set();
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        // B, A, the filler comments and Z of the latest text.
        Assert.Equal(Format(new PascalFoldingProvider().GetRegions(new StringTextBuffer(buffer.Text))), Format(model.Regions));
        Assert.EndsWith("routine", Format(model.Regions), StringComparison.Ordinal);
        var calls = provider.Calls.ToList();
        Assert.All(calls, c => Assert.False(c.OnDocument));                                  // never the live buffer
        Assert.All(calls, c => Assert.False(c.Synchronous));                                // never inside the edit
        Assert.Equal(buffer.Text, calls[^1].Text);
        Assert.True(calls.Count >= 2, "the outdated snapshot was folded again from the latest text");
    }}
