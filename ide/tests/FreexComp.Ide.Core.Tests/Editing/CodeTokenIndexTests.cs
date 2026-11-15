using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Tests.Editing;

/// <summary>The background scan of <see cref="CodeTokenIndex"/> (IDE-ARCH-081/083/084, I-182).</summary>
[Trait("Phase", "3")]
[Trait("Kind", "Unit")]
public sealed class CodeTokenIndexTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    /// <summary>
    /// I-182: when a scan ends for an outdated version, the <see cref="CodeTokenIndex.Changed"/> handler's request for the
    /// tokens starts a new scan at once (the ended scan no longer counts as running), and its result arrives without any
    /// further request.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-083")]
    [Trait("Req", "IDE-ARCH-084")]
    public async Task StaleScanRestarts()
    {
        var ct = TestContext.Current.CancellationToken;
        var text = string.Concat(Enumerable.Repeat("begin F(x); end;\r\n", 12_000)); // 216 000 chars
        Assert.True(text.Length > WholeTextClassifier.BackgroundThreshold);
        var buffer = new StringTextBuffer(text);
        using var language = new GatedLanguage();
        var index = new CodeTokenIndex(buffer, language);
        var fresh = new TaskCompletionSource<CodeTokens>(TaskCreationOptions.RunContinuationsAsynchronously);
        index.Changed += (_, _) =>
        {
            // The consumer's reaction (word highlight, bracket match): ask again.
            if (index.Current is { } tokens)
                fresh.TrySetResult(tokens);
        };

        // Scan 1 starts for version v1 and is held; an edit makes it stale.
        Assert.Null(index.Current);
        Assert.True(await language.Entered.WaitAsync(Limit, ct));
        buffer.ApplyEdits([new TextEdit(0, 0, "x")]);
        Assert.Null(index.Current); // scan 1 still runs: no second scan yet
        Assert.Equal(1, language.Scans);

        // Scan 1 ends stale: the handler's request starts scan 2 for the current version.
        language.Release.Release();
        Assert.True(await language.Entered.WaitAsync(Limit, ct));
        Assert.Equal(2, language.Scans);

        // Scan 2 ends: the tokens of the current version arrive through Changed, with no other request.
        language.Release.Release();
        var tokens = await fresh.Task.WaitAsync(Limit, ct);
        Assert.Equal(buffer.Version, tokens.Version);
        Assert.Null(tokens.MatchAt(0)); // "xbegin" is an identifier
        Assert.Same(tokens, index.Current);
        Assert.Equal(2, language.Scans);
    }

    /// <summary>Pascal tokens, but each scan waits for <see cref="Release"/> after signalling <see cref="Entered"/>.</summary>
    private sealed class GatedLanguage : ILanguageDefinition, IDisposable
    {
        private readonly PascalLanguage _pascal = new();
        private int _scans;

        public SemaphoreSlim Entered { get; } = new(0);

        public SemaphoreSlim Release { get; } = new(0);

        public int Scans => Volatile.Read(ref _scans);

        public string Id => _pascal.Id;

        public string DisplayName => _pascal.DisplayName;

        public IReadOnlyList<string> Extensions => _pascal.Extensions;

        public string? LineComment => _pascal.LineComment;

        public (string Start, string End)? BlockComment => _pascal.BlockComment;

        public IReadOnlyList<(char Open, char Close)> Brackets => _pascal.Brackets;

        public ITokenClassifier CreateClassifier() => _pascal.CreateClassifier();

        public IFoldingProvider? CreateFoldingProvider() => null;

        public CodeTokens ScanCodeTokens(string text)
        {
            Interlocked.Increment(ref _scans);
            Entered.Release();
            Release.Wait(Limit);
            return ((ILanguageDefinition)_pascal).ScanCodeTokens(text);
        }

        public void Dispose()
        {
            Entered.Dispose();
            Release.Dispose();
        }
    }
}
