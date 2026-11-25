using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace FreexComp.Ide.TestHelper;

/// <summary>
/// IDE-P3-094, I-194: the helper program of the process tests, a plain console exe started directly (its own apphost).
/// The first argument is the mode:
/// <list type="bullet">
/// <item><c>--spawn-child</c>: starts a copy of itself in <c>--wait</c> mode, prints <c>child &lt;pid&gt;</c> and waits.</item>
/// <item><c>--wait</c>: waits until it is killed.</item>
/// <item><c>--write-both</c>: writes to stdout and stderr (UTF-8, ANSI colour codes, CR / LF / CRLF breaks, a final
/// partial line), reads stdin to its end, exits with code 3.</item>
/// <item><c>--exit-with &lt;code&gt;</c>: prints <see cref="ChildGreeting"/> to its console and exits with that code.</item>
/// <item><c>--console-key &lt;pid&gt; &lt;file&gt;</c>: see <see cref="ConsoleKeyHelper"/> (I-138).</item>
/// <item><c>--peek-handle &lt;value&gt;</c>: prints <see cref="Inherited"/> when handle <c>value</c> is a pipe holding
/// <see cref="PipeMarker"/>, otherwise <see cref="NotInherited"/> (I-188).</item>
/// <item><c>--flood &lt;chars&gt;</c>: writes that many <c>o</c> to stdout and, at the same time, that many <c>e</c> to
/// stderr, then exits with code 0 (I-262).</item>
/// </list>
/// Unknown or missing mode: exit code <see cref="UsageError"/>.
/// </summary>
public static class HelperProgram
{
    public const string SpawnChild = "--spawn-child";
    public const string Wait = "--wait";
    public const string WriteBoth = "--write-both";
    public const string ExitWith = "--exit-with";
    public const string ConsoleKey = "--console-key";
    public const string PeekHandle = "--peek-handle";
    public const string Flood = "--flood";
    public const string ChildGreeting = "child says hello";
    public const string PipeMarker = "freexcomp-leak-marker";
    public const string Inherited = "inherited";
    public const string NotInherited = "not inherited";
    public const int UsageError = 64;

    /// <summary>The helper executable (next to this assembly).</summary>
    public static string ExecutablePath => Path.ChangeExtension(typeof(HelperProgram).Assembly.Location, ".exe");

    /// <summary>Exit code when a mode failed; the exception text went to stderr.</summary>
    public const int Failure = 70;

    public static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length == 0)
            return UsageError;
        try
        {
            return Run(args);
        }
        catch (Exception e)
        {
            // Any failure: its text on stderr, so the test can show why (a crash would only give 0xE0434352).
            Console.Error.WriteLine(e.ToString());
            return Failure;
        }
    }

    private static int Run(string[] args)
    {
        var rest = args[1..];
        switch (args[0])
        {
            case ExitWith:
                Console.Out.WriteLine(ChildGreeting);
                Console.Out.Flush();
                return int.Parse(rest[0], CultureInfo.InvariantCulture);
            case ConsoleKey:
                return ConsoleKeyHelper.Run(int.Parse(rest[0], CultureInfo.InvariantCulture), rest[1]);
            case PeekHandle:
                Console.Out.WriteLine(PeekMarker(long.Parse(rest[0], CultureInfo.InvariantCulture)) ? Inherited : NotInherited);
                Console.Out.Flush();
                return 0;
            case SpawnChild:
                var self = Environment.ProcessPath ?? ExecutablePath;
                using (var child = Process.Start(new ProcessStartInfo(self, Wait) { UseShellExecute = false })!)
                {
                    Console.Out.WriteLine("child " + child.Id.ToString(CultureInfo.InvariantCulture));
                    Console.Out.Flush();
                }

                WaitForever();
                return 0;
            case Wait:
                WaitForever();
                return 0;
            case Flood:
                var chars = int.Parse(rest[0], CultureInfo.InvariantCulture);
                var errors = new Thread(() => WriteRepeated(Console.OpenStandardError(), (byte)'e', chars));
                errors.Start();
                WriteRepeated(Console.OpenStandardOutput(), (byte)'o', chars);
                errors.Join();
                return 0;
            case WriteBoth:
                var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
                var stderr = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true };
                stdout.Write("out 1\r\n\u001b[32mgrün\u001b[0m\n");
                stderr.Write("err 1\n");
                stdout.Write("cr\rName: ");
                var input = Console.In.ReadToEnd(); // closed stdin: end-of-file at once (IDE-P3-082)
                stderr.Write("stdin " + input.Length.ToString(CultureInfo.InvariantCulture) + "\n");
                return 3;
            default:
                return UsageError;
        }
    }

    private static void WriteRepeated(Stream stream, byte value, int count)
    {
        var chunk = new byte[8192];
        Array.Fill(chunk, value);
        for (var left = count; left > 0; left -= chunk.Length)
            stream.Write(chunk, 0, Math.Min(chunk.Length, left));
        stream.Flush();
    }

    /// <summary>True when <paramref name="value"/> is, in this process, the read end of a pipe holding <see cref="PipeMarker"/>.</summary>
    private static bool PeekMarker(long value)
    {
        var buffer = new byte[PipeMarker.Length];
        return PeekNamedPipe((IntPtr)value, buffer, buffer.Length, out var read, IntPtr.Zero, IntPtr.Zero)
            && read == buffer.Length && Encoding.ASCII.GetString(buffer) == PipeMarker;
    }

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekNamedPipe(IntPtr pipe, byte[] buffer, int size, out int read, IntPtr available, IntPtr left);

    private static void WaitForever()
    {
        using var never = new ManualResetEventSlim(false);
        never.Wait();
    }
}
