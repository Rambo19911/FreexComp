using System.Diagnostics;

namespace FreexComp.E2E.Tests;

/// <summary>
/// Starts the real IDE executable (not the headless test host) with a throw-away user folder, waits for its main
/// window, checks the title, closes it like a user would and expects a clean exit. Needs an interactive desktop
/// (TESTING.md: Category=Foreground may skip, with the reason, when there is none).
/// </summary>
[Trait("Phase", "1")]
[Trait("Kind", "E2E")]
[Trait("Category", "Foreground")]
public sealed class IdeStartupTests
{
    [Fact]
    public async Task RealIdeShowsMainWindowAndExitsCleanly()
    {
        if (!Environment.UserInteractive)
        {
            Assert.Skip("no interactive desktop (Category=Foreground)");
        }

        var exe = Directory
            .EnumerateFiles(Path.Combine(RepoPaths.Root, "ide", "src", "FreexComp.Ide", "bin"), "FreexComp.Ide.exe", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        Assert.True(exe is not null, "FreexComp.Ide.exe not built; run dotnet build FreexComp.sln first");

        var userDir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"), "user");
        Directory.CreateDirectory(userDir);
        var psi = new ProcessStartInfo(exe!) { UseShellExecute = false };
        psi.ArgumentList.Add("--user-dir");
        psi.ArgumentList.Add(userDir);

        using var p = Process.Start(psi)!;
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (p.MainWindowHandle == IntPtr.Zero && !p.HasExited && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100, ct); // polling a condition with a deadline, not a blind sleep
                p.Refresh();
            }

            Assert.False(p.HasExited, $"IDE exited during start-up with code {(p.HasExited ? p.ExitCode : 0)}");
            Assert.NotEqual(IntPtr.Zero, p.MainWindowHandle);
            Assert.Equal("FreexComp", p.MainWindowTitle);

            Assert.True(p.CloseMainWindow(), "close message not delivered");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            await p.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, p.ExitCode);
        }
        finally
        {
            if (!p.HasExited)
            {
                p.Kill(entireProcessTree: true);
            }

            try { Directory.Delete(Path.GetDirectoryName(userDir)!, recursive: true); } catch (IOException) { }
        }
    }
}
