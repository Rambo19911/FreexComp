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
/// <item><c>--exit-with &lt;code&gt;</c>: prints <see cref="ChildGreeting"/> to its console and exits with that code.</item>
/// <item><c>--console-key &lt;pid&gt; &lt;file&gt;</c>: attaches to the console of process <c>pid</c>, writes its title and the
/// text of its screen buffer to <c>file</c> (UTF-8), then injects one key press (space) with <c>WriteConsoleInputW</c>
/// (I-138, <c>ConsoleHostTests</c>).</item>
/// <item><c>--peek-handle &lt;value&gt;</c>: prints <see cref="Inherited"/> when handle <c>value</c> is a pipe holding
/// <see cref="PipeMarker"/>, otherwise <see cref="NotInherited"/> (I-188).</item>
/// </list>
/// </summary>
internal static class ProcessHelper
{
    public const string SpawnChild = "--spawn-child";
    public const string Wait = "--wait";
    public const string WriteBoth = "--write-both";
    public const string ExitWith = "--exit-with";
    public const string ConsoleKey = "--console-key";
    public const string PeekHandle = "--peek-handle";
    public const string ChildGreeting = "child says hello";
    public const string PipeMarker = "freexcomp-leak-marker";
    public const string Inherited = "inherited";
    public const string NotInherited = "not inherited";

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
        var index = Array.FindIndex(args, a => a is SpawnChild or Wait or WriteBoth or ExitWith or ConsoleKey or PeekHandle);
        if (index < 0)
            return;
        Environment.Exit(RunMode(args[index], args[(index + 1)..]));
    }

    private static int RunMode(string mode, string[] rest)
    {
        switch (mode)
        {
            case ExitWith:
                Console.Out.WriteLine(ChildGreeting);
                Console.Out.Flush();
                return int.Parse(rest[0], System.Globalization.CultureInfo.InvariantCulture);
            case ConsoleKey:
                return ConsoleKeyHelper.Run(int.Parse(rest[0], System.Globalization.CultureInfo.InvariantCulture), rest[1]);
            case PeekHandle:
                Console.Out.WriteLine(PeekMarker(long.Parse(rest[0], System.Globalization.CultureInfo.InvariantCulture)) ? Inherited : NotInherited);
                Console.Out.Flush();
                return 0;
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

    /// <summary>True when <paramref name="value"/> is, in this process, the read end of a pipe holding <see cref="PipeMarker"/>.</summary>
    private static bool PeekMarker(long value)
    {
        var buffer = new byte[PipeMarker.Length];
        return PeekNamedPipe((IntPtr)value, buffer, buffer.Length, out var read, IntPtr.Zero, IntPtr.Zero)
            && read == buffer.Length && Encoding.ASCII.GetString(buffer) == PipeMarker;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool PeekNamedPipe(IntPtr pipe, byte[] buffer, int size, out int read, IntPtr available, IntPtr left);

    private static void WaitForever()
    {
        using var never = new ManualResetEventSlim(false);
        never.Wait();
    }
}
