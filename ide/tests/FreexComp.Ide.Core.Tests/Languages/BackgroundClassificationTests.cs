using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Tests.Languages;

/// <summary>IDE-P1-053 / IDE-P1-056 / IDE-ARCH-081: buffers over 200 KB are lexed on the thread pool.</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Unit")]
public sealed class BackgroundClassificationTests
{
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-053")]
    [Trait("Req", "IDE-P1-056")]
    public async Task LargeBufferIsClassifiedInTheBackground()
    {
        var text = string.Concat(Enumerable.Repeat("begin end;\r\n", 20_000)); // 240 000 chars
        Assert.True(text.Length > WholeTextClassifier.BackgroundThreshold);
        var buffer = new StringTextBuffer(text);
        var classifier = new PascalLanguage().CreateClassifier();
        var updates = 0;
        var updated = new TaskCompletionSource();
        classifier.ClassificationChanged += (_, _) =>
        {
            updates++;
            updated.TrySetResult();
        };

        // Not lexed on the calling thread: no colours yet, they arrive with ClassificationChanged.
        Assert.Empty(classifier.Classify(buffer, new LineRange(1, 1)));
        var deadline = CurrentTest.Deadline;
        await deadline.WaitAsync(updated.Task, "the first background classification");
        Assert.Contains(new TokenClassSpan(0, 5, "syntax.keyword"), classifier.Classify(buffer, new LineRange(1, 1)));

        // An edit: until the new result arrives, the old spans follow the text; the edited token is plain text.
        updated = new TaskCompletionSource();
        buffer.ApplyEdits([new TextEdit(0, 0, "x")]);
        var interim = classifier.Classify(buffer, new LineRange(1, 2));
        Assert.DoesNotContain(interim, s => s.Offset == 0);
        Assert.Contains(new TokenClassSpan(13, 5, "syntax.keyword"), interim); // line 2 "begin", shifted by one
        await deadline.WaitAsync(updated.Task, "the classification of the edited version");
        var fresh = classifier.Classify(buffer, new LineRange(1, 1));
        Assert.DoesNotContain(fresh, s => s.TokenClass == "syntax.keyword" && s.Offset <= 1); // "xbegin" is an identifier
        Assert.Contains(fresh, s => s.TokenClass == "syntax.keyword" && s.Offset == 7);        // "end"
        Assert.Equal(2, updates);
    }

    [Fact]
    [Trait("Req", "IDE-P1-056")]
    public void SmallBufferIsClassifiedSynchronously()
    {
        var classifier = new PascalLanguage().CreateClassifier();
        var raised = false;
        classifier.ClassificationChanged += (_, _) => raised = true;
        Assert.Contains(new TokenClassSpan(0, 5, "syntax.keyword"), classifier.Classify(new StringTextBuffer("begin end."), new LineRange(1, 1)));
        Assert.False(raised);
    }
}
