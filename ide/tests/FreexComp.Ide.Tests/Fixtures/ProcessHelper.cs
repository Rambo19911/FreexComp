using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace FreexComp.Ide.Tests.Fixtures;

/// <summary>
/// IDE-P3-094: the test assembly doubles as the helper program of the process tests. Started with one of the modes below
/// as its first argument, it does that and never reaches the test runner (the module initializer runs before Main).
/// <list type="bullet">
/// <item><c>--spawn-child</c>: starts a copy of itself in <c>--wait</c> mode, prints <c>child &lt;pid&gt;</c> and waits.</item>
/// <item><c>--wait</c>: waits until it is killed.</item>
/// <item><c>--write-both</c>: writes to stdout and stderr (UTF-8, ANSI colour codes, CR / LF / CRLF breaks, a final
/// partial line), reads stdin to its end, exits with code 3.</item>
/// </list>
/// </summary>
internal static class ProcessHelper
{
    public const string SpawnChild = "--spawn-child";
    public const string Wait = "--wait";
    public const string WriteBoth = "--write-both";

    /// <summary>How to start the helper: the dotnet host and the test assembly (falls back to the apphost exe).</summary>
    public static (string FileName, string Arguments) Command(string mode)
    {
        var assembly = typeof(ProcessHelper).Assembly.Location;
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        return host is { Length: > 0 } && File.Exists(host)
            ? (host, $"\"{assembly}\" {mode}")
            : (Path.ChangeExtension(assembly, ".exe"), mode);
    }

    [ModuleInitializer]
    internal static void Run()
    {
        var args = Environment.GetCommandLineArgs();
        var mode = args.FirstOrDefault(a => a is SpawnChild or Wait or WriteBoth);
        if (mode is null)
            return;
        Environment.Exit(RunMode(mode));
    }

    private static int RunMode(string mode)
    {
        switch (mode)
        {
            case SpawnChild:
                var (file, arguments) = Command(Wait);
                using (var child = Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = false })!)
                {
                    Console.Out.WriteLine("child " + child.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    Console.Out.Flush();
                }

                WaitForever();
                return 0;
            case Wait:
                WaitForever();
                return 0;
            default:
                var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
                var stderr = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true };
                stdout.Write("out 1\r\n\u001b[32mgrün\u001b[0m\n");
                stderr.Write("err 1\n");
                stdout.Write("cr\rName: ");
                var input = Console.In.ReadToEnd(); // closed stdin: end-of-file at once (IDE-P3-082)
                stderr.Write("stdin " + input.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n");
                return 3;
        }
    }

    private static void WaitForever()
    {
        using var never = new ManualResetEventSlim(false);
        never.Wait();
    }
}
