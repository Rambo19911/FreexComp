namespace FreexComp.UiFramework.Tests;

/// <summary>
/// The entry point of the test assembly: the one the test framework generates (kept as it is), plus U-162's holder
/// mode <c>--hold-gui &lt;exe&gt;</c>, in which the assembly is a test host of its own that starts one test program
/// with <see cref="Gui.StartGui"/> and holds it until it is killed (<see cref="GuiHarnessTests.TestProgramDiesWithTestHost"/>).
/// </summary>
internal static class EntryPoint
{
    public const string HoldGui = "--hold-gui";

    public static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == HoldGui)
        {
            return Hold(args[1]);
        }

        if (args.Any(arg => arg == "--server" || arg == "--internal-msbuild-node"))
        {
            return Xunit.MicrosoftTestingPlatform.TestPlatformTestFramework.RunAsync(args, SelfRegisteredExtensions.AddSelfRegisteredExtensions).GetAwaiter().GetResult();
        }

        return Xunit.Runner.InProc.SystemConsole.ConsoleRunner.Run(args).GetAwaiter().GetResult();
    }

    /// <summary>Starts the GUI probe, writes <c>holding &lt;pid&gt;</c> once its window is up and waits on stdin (it is killed there).</summary>
    private static int Hold(string exe)
    {
        using var app = Gui.StartGui(exe, [], new Dictionary<string, string>());
        if (Gui.WaitForMainWindow(app, "FxcGuiProbe", "Probe Window") == IntPtr.Zero)
        {
            Console.Error.WriteLine("the probe window was not found\n" + app.StdOut + app.StdErr);
            return 1;
        }

        Console.WriteLine($"holding {app.Process.Id}");
        Console.Out.Flush();
        Console.In.ReadLine();                                      // a closed stdin ends the holder by itself
        return 0;
    }
}
