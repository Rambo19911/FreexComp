using System.ComponentModel;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Resources;
using Microsoft.Win32.SafeHandles;

namespace FreexComp.Ide.Platform.Windows;

/// <summary>
/// IDE-P3-087: the IDE executable in console-host mode,
/// <c>FreexComp.exe --console-host --pipe &lt;name&gt; --cwd &lt;dir&gt; -- &lt;exe&gt; &lt;arguments&gt;</c> (handled in
/// <c>Program.Main</c> before any UI or service exists). It owns a console window, runs the program in it, reports
/// <c>started &lt;pid&gt;</c> and <c>exited &lt;code&gt;</c> over the IDE's named pipe, then waits for one key press and exits with
/// the program's code. The program's command line is the raw tail after <c>--</c>, so its arguments stay verbatim.
/// </summary>
public static class ConsoleHost
{
    public const string Option = "--console-host";
    public const string PipeOption = "--pipe";
    public const string CwdOption = "--cwd";
    private const string Separator = "--";
    private const int UsageError = 2;

    /// <summary>
    /// The host's command line (built by <see cref="WindowsProcessLauncher"/>; parsed back by <see cref="Run"/>).
    /// <paramref name="prefix"/> = what goes before <see cref="Option"/> (the quoted IDE assembly when the IDE runs under the
    /// <c>dotnet</c> host, otherwise empty).
    /// </summary>
    public static string CommandLine(string hostPath, string prefix, string pipe, string workingDirectory, string fileName, string arguments) =>
        Quote(hostPath) + (prefix.Length > 0 ? " " + prefix : "") + $" {Option} {PipeOption} {pipe} {CwdOption} {Quote(workingDirectory)} {Separator} "
        + Quote(fileName) + (arguments.Length > 0 ? " " + arguments : "");

    /// <summary>A path quoted for the Windows command-line rules (a trailing backslash is doubled before the quote).</summary>
    public static string Quote(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var trailing = path.Length - path.TrimEnd('\\').Length;
        return "\"" + path + new string('\\', trailing) + "\"";
    }

    /// <summary>The raw text after the first <c> -- </c> outside quotes, or null.</summary>
    public static string? Tail(string commandLine)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        var quoted = false;
        for (var i = 0; i < commandLine.Length; i++)
        {
            if (commandLine[i] == '"')
                quoted = !quoted;
            else if (!quoted && string.CompareOrdinal(commandLine, i, " -- ", 0, 4) == 0)
                return commandLine[(i + 4)..];
        }

        return null;
    }

    /// <summary>Host mode; <paramref name="args"/> = the process arguments (first one <see cref="Option"/>).</summary>
    public static int Run(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        string? pipe = null, cwd = null;
        for (var i = 0; i + 1 < args.Length && args[i] != Separator; i++)
        {
            if (args[i] == PipeOption)
                pipe = args[++i];
            else if (args[i] == CwdOption)
                cwd = args[++i];
        }

        var tail = Tail(Marshal.PtrToStringUni(Native.GetCommandLineW()) ?? "");
        var separator = Array.IndexOf(args, Separator);
        if (pipe is null || cwd is null || tail is null || separator < 0 || separator + 1 >= args.Length)
            return UsageError;
        var exe = args[separator + 1];

        Native.AllocConsole(); // fails only when a console exists already: it is used then
        Native.SetConsoleTitleW(Path.GetFileName(exe));
        using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, c => c.Cancel = true); // Ctrl+C ends the program, not the host
        using var breakKey = PosixSignalRegistration.Create(PosixSignal.SIGQUIT, c => c.Cancel = true);

        int code;
        using (var client = new NamedPipeClientStream(".", pipe, PipeDirection.Out, PipeOptions.CurrentUserOnly))
        {
            try
            {
                client.Connect(TimeSpan.FromSeconds(10));
            }
            catch (TimeoutException)
            {
                return UsageError;
            }

            var report = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            if (!TryStart(exe, tail, cwd, out var process, out var processId, out var error))
            {
                // Nothing ran: the pipe closes without "exited" (IDE-P3-088 Terminated); the window shows why.
                Write(error + "\r\n");
                code = -1;
            }
            else
            {
                using (process)
                {
                    report.WriteLine("started " + processId.ToString(CultureInfo.InvariantCulture));
                    Native.WaitForSingleObject(process, Native.INFINITE);
                    code = Native.GetExitCodeProcess(process, out var exitCode) ? unchecked((int)exitCode) : -1;
                }

                // Input typed while the program ran is dropped; the prompt is on screen before the IDE hears "exited".
                Native.FlushConsoleInputBuffer(Native.GetStdHandle(Native.STD_INPUT_HANDLE));
                Write("\r\n" + string.Format(CultureInfo.CurrentCulture, Strings.Msg_Run_PressAnyKey, RunService.FormatExitCode(code)));
                try
                {
                    report.WriteLine("exited " + code.ToString(CultureInfo.InvariantCulture));
                }
                catch (IOException)
                {
                    // The IDE is gone; the window still waits for its key.
                }
            }
        }

        WaitForKey();
        return code;
    }

    private static bool TryStart(string exe, string commandLine, string cwd, out SafeProcessHandle process, out int processId, out string error)
    {
        var startup = new Native.STARTUPINFOW { cb = Marshal.SizeOf<Native.STARTUPINFOW>() };
        if (!Native.CreateProcessW(exe, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, false, Native.CREATE_UNICODE_ENVIRONMENT, IntPtr.Zero,
                cwd, ref startup, out var info))
        {
            process = new SafeProcessHandle();
            processId = 0;
            error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            return false;
        }

        Native.CloseHandle(info.hThread);
        process = new SafeProcessHandle(info.hProcess, ownsHandle: true);
        processId = info.dwProcessId;
        error = "";
        return true;
    }

    private static void Write(string text) => Native.WriteConsoleW(Native.GetStdHandle(Native.STD_OUTPUT_HANDLE), text, text.Length, out _, IntPtr.Zero);

    private static void WaitForKey()
    {
        var input = Native.GetStdHandle(Native.STD_INPUT_HANDLE);
        var records = new Native.INPUT_RECORD[1];
        while (Native.ReadConsoleInputW(input, records, 1, out var read))
        {
            if (read == 1 && records[0].EventType == Native.KEY_EVENT && records[0].KeyDown != 0)
                return;
        }
    }

    private static class Native
    {
        public const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
        public const uint INFINITE = 0xFFFFFFFF;
        public const int STD_INPUT_HANDLE = -10;
        public const int STD_OUTPUT_HANDLE = -11;
        public const ushort KEY_EVENT = 1;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct STARTUPINFOW
        {
            public int cb;
            public IntPtr lpReserved;
            public IntPtr lpDesktop;
            public IntPtr lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        [StructLayout(LayoutKind.Explicit, Size = 20)]
        public struct INPUT_RECORD
        {
            [FieldOffset(0)]
            public ushort EventType;
            [FieldOffset(4)]
            public int KeyDown;
        }

        [DllImport("kernel32.dll", ExactSpelling = true)]
        public static extern IntPtr GetCommandLineW();

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetConsoleTitleW(string title);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        public static extern IntPtr GetStdHandle(int which);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WriteConsoleW(IntPtr output, string text, int length, out int written, IntPtr reserved);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool FlushConsoleInputBuffer(IntPtr input);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReadConsoleInputW(IntPtr input, [Out] INPUT_RECORD[] records, int length, out int read);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreateProcessW(string? applicationName, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint creationFlags, IntPtr environment, string? currentDirectory,
            ref STARTUPINFOW startupInfo, out PROCESS_INFORMATION processInformation);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        public static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);
    }
}
