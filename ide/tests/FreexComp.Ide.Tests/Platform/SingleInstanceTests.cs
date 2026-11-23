using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Platform.Windows;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Platform;

/// <summary>I-213: a second IDE with a file argument hands the file to the running IDE (local named pipe + mutex).</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
[Trait("Category", "Integration")]
public sealed class SingleInstanceTests
{
    /// <summary>
    /// The harness IDE is the running instance of its user directory; the real IDE executable started with that
    /// <c>--user-dir</c> and a file hands the file over and exits with code 0 before any window exists; the file opens in
    /// the running IDE. Another user directory has no running instance (no hand-over).
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-010")]
    public async Task SecondInstanceHandsOverFile()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.InitialFiles["notes.txt"] = "handed over");
        var file = Path.Combine(h.TempDir, "notes.txt");
        var key = InstanceHandOver.Key(h.UserDir);
        Assert.NotEqual(key, InstanceHandOver.Key(Path.Combine(h.UserDir, "other")));
        Assert.False(InstanceHandOver.TrySend(InstanceHandOver.Key(Path.Combine(h.UserDir, "other")), [file], TimeSpan.FromSeconds(5)));

        using var claim = InstanceHandOver.TryClaim(key);
        Assert.NotNull(claim);
        Assert.Null(InstanceHandOver.TryClaim(key)); // one running instance per user directory
        claim.Serve(paths => Dispatcher.UIThread.Post(() => _ = CompositionRoot.ReceiveHandOverAsync(h.Services, h.Window, paths)));

        var exe = Path.Combine(AppContext.BaseDirectory, "FreexComp.Ide.exe");
        Assert.True(File.Exists(exe), exe);
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = h.TempDir };
        foreach (var argument in (string[])["--user-dir", h.UserDir, "notes.txt"]) // relative to the second instance's directory
            start.ArgumentList.Add(argument);
        using var second = Process.Start(start)!;
        try
        {
            await h.WaitForAsync(() => second.HasExited, "the second instance exits after the hand-over");
            Assert.Equal(0, second.ExitCode);
            await h.WaitForAsync(() => h.Services.Documents.Documents.Any(d => string.Equals(d.FilePath, file, StringComparison.OrdinalIgnoreCase)),
                "the handed-over file opens in the running IDE");
            Assert.Equal(file, h.Services.Documents.ActiveDocument?.FilePath, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            if (!second.HasExited)
                second.Kill(entireProcessTree: true);
        }
    }
}
