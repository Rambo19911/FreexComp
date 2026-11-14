using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests;

/// <summary>Unhandled exceptions on the UI thread (IDE-ARCH-091) with the repeat rule of IDE-ARCH-045.</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class ErrorHandlingTests
{
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-091")]
    [Trait("Req", "IDE-ARCH-045")]
    [Trait("Req", "UI-MSG-005")]
    public async Task UnhandledUiExceptionShowsInternalErrorOnce()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.MessageAnswers["Msg.App.InternalError"] = "Continue");
        h.ExpectLoggedError();
        h.ExpectUnhandledException();

        static void Throw(string message) => Dispatcher.UIThread.Post(() => throw new InvalidOperationException(message));

        // Two identical errors within 10 s: both logged, the dialog once; the IDE keeps running.
        Throw("boom");
        Throw("boom");
        await h.WaitForAsync(() => h.Log.Lines.Count(l => l.StartsWith("ERROR ", StringComparison.Ordinal)) == 2, "two errors logged");
        var request = Assert.Single(h.Messages.Requests);
        Assert.Equal("Msg.App.InternalError", request.MessageId);
        Assert.Contains("boom", request.Details, StringComparison.Ordinal); // detail in the Details area, never in the text
        Assert.Empty(request.Args);
        Assert.Contains(h.Log.Lines, l => l.StartsWith("ERROR ", StringComparison.Ordinal) && l.Contains("boom", StringComparison.Ordinal));
        Assert.True(h.Window.IsVisible);

        // A different error, and the same one after 10 s, show the dialog again.
        Throw("other");
        await h.WaitForAsync(() => h.Messages.Requests.Count == 2, "dialog for a different error");
        h.Clock.Advance(TimeSpan.FromSeconds(11));
        Throw("other");
        await h.WaitForAsync(() => h.Messages.Requests.Count == 3, "dialog after 10 s");
        Assert.True(h.Window.IsVisible);
        Assert.True(h.Services.Commands.CanExecute("file.exit")); // still working
    }
}
