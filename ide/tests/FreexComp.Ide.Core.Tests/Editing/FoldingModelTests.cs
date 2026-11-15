using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Tests.Editing;

/// <summary>The background run of <see cref="FoldingModel"/> (IDE-ARCH-081/083, I-204).</summary>
[Trait("Phase", "3")]
[Trait("Kind", "Unit")]
public sealed class FoldingModelTests
{
    /// <summary>
    /// I-204: a run that has already ended when its continuation is attached (a fast run, the continuation applied at once)
    /// must not let a second run start beside the next one, and <c>IsComputing</c> stays true while a run is in flight.
    /// The gate: the background runner is the test's, and the synchronization context runs posted work inline, so the
    /// ended run's continuation runs inside <c>StartBackground</c> every time.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-083")]
    public void FastRunDoesNotOverlap()
    {
        var buffer = new StringTextBuffer("x");
        var model = new FoldingModel(buffer, new EditorViewState(), new NoRegions());
        var pending = new List<TaskCompletionSource<List<FoldRegion>>>();
        var started = 0;
        var large = new string('a', WholeTextClassifier.BackgroundThreshold + 1);
        model.RunInBackground = work =>
        {
            started++;
            if (started == 1)
            {
                // Run 1 is fast: it has ended before its continuation is attached, and the text changed meanwhile.
                var result = work();
                buffer.ApplyEdits([new TextEdit(0, 0, "y")]);
                return Task.FromResult(result);
            }

            var gate = new TaskCompletionSource<List<FoldRegion>>();
            pending.Add(gate);
            return gate.Task;
        };

        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new InlineContext());
        try
        {
            buffer.ApplyEdits([new TextEdit(0, buffer.Length, large)]); // large: folded in the background

            // Run 1 ended stale: exactly one further run, still in flight, and the model knows it.
            Assert.Equal(2, started);
            Assert.Single(pending);
            Assert.True(model.IsComputing);

            // It ends for the current version: applied, nothing else starts.
            pending[0].SetResult([]);
            Assert.Equal(2, started);
            Assert.False(model.IsComputing);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private sealed class NoRegions : IFoldingProvider
    {
        public IReadOnlyList<FoldRegion> GetRegions(ITextBuffer buffer) => [];
    }

    /// <summary>Runs posted work at once on the posting thread.</summary>
    private sealed class InlineContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => d(state);

        public override SynchronizationContext CreateCopy() => this;
    }
}
