using FreexComp.Ide.Platform;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests.Platform;

/// <summary><c>WindowsShellService.OpenConsole</c> (phase8-project-files.md §6) against the real OS.</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Native")]
public sealed class WindowsShellServiceTests
{
    /// <summary>
    /// IDE-PM-141: the interpreter named by <c>ComSpec</c> starts with the given working directory. The test hook passes
    /// an argument list that makes it write its current directory into a file there and exit at once, without a window;
    /// the product passes no arguments.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-141")]
    public async Task OpenConsole_StartsComSpecInDirectory()
    {
        using var temp = new TempDirectory();
        var directory = Path.Combine(temp.Path, "work dir"); // a space in the path
        Directory.CreateDirectory(directory);

        using var process = WindowsShellService.StartConsole(directory, ["/d", "/c", "cd>cwd.txt"], hidden: true, out var reason);

        Assert.True(process is not null, reason);
        Assert.Equal("", reason);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, process.ExitCode);
        var interpreter = Environment.GetEnvironmentVariable("ComSpec")
            ?? Path.Combine(Environment.GetEnvironmentVariable("SystemRoot")!, "System32", "cmd.exe");
        Assert.Equal(interpreter, process.StartInfo.FileName, ignoreCase: true);
        Assert.Equal(directory, File.ReadAllText(Path.Combine(directory, "cwd.txt")).Trim(), ignoreCase: true);

        // A directory that does not exist: false with the OS reason, nothing started.
        IShellServiceProbe(new WindowsShellService(), Path.Combine(temp.Path, "missing"));
    }

    private static void IShellServiceProbe(Core.Platform.IShellService shell, string missingDirectory)
    {
        Assert.False(shell.OpenConsole(missingDirectory, out var reason));
        Assert.NotEqual("", reason);
        Assert.False(shell.OpenConsole(missingDirectory));
    }
}
