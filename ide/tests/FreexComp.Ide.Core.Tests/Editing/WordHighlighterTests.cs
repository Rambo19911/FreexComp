using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Editing;

/// <summary>The word-occurrence highlighter's timing (IDE-ED-074) and large buffers (IDE-ARCH-081).</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Unit")]
public sealed class WordHighlighterTests
{
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-074")]
    [Trait("Req", "IDE-ARCH-081")]
    public async Task LargeBufferIsHighlightedWhenItsTokensArrive()
    {
        var text = string.Concat(Enumerable.Repeat("Foo := Bar;\r\n", 20_000)); // 260 000 chars
        Assert.True(text.Length > WholeTextClassifier.BackgroundThreshold);
        var buffer = new StringTextBuffer(text);
        var state = new EditorViewState();
        var clock = new ManualClock();
        var tokens = new CodeTokenIndex(buffer, new PascalLanguage());
        var highlighter = new WordHighlighter(buffer, state, tokens, () => clock, () => true);
        var arrived = new TaskCompletionSource();
        highlighter.Changed += (_, _) =>
        {
            if (highlighter.Highlights.Count > 0)
                arrived.TrySetResult();
        };

        state.Caret = new TextPosition(1, 2);
        clock.Advance(WordHighlighter.Delay);
        Assert.Empty(highlighter.Highlights); // the tokens are scanned in the background, not on this thread
        await CurrentTest.Deadline.WaitAsync(arrived.Task, "the highlights from the background token scan");
        Assert.Equal(19_999, highlighter.Highlights.Count);
        Assert.Equal(13, highlighter.Highlights[0].Offset);

        // Moving the caret clears them at once.
        state.Caret = new TextPosition(1, 9);
        Assert.Empty(highlighter.Highlights);
    }
}
