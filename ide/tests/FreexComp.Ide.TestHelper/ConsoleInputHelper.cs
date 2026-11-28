using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace FreexComp.Ide.TestHelper;

/// <summary>
/// The console modes of <see cref="HelperProgram"/> for F14-047 (I-393). Each runs in its own helper process and attaches
/// to another process's console, so the test process never changes its own console (as <see cref="ConsoleKeyHelper"/>).
/// <list type="bullet">
/// <item><c>--console-type &lt;pid&gt; &lt;file&gt;</c>: writes the UTF-8 text of the file into the console's input buffer with
/// <c>WriteConsoleInputW</c>, one key-down and one key-up record per UTF-16 unit; a line feed is the Enter key, a carriage
/// return is skipped.</item>
/// <item><c>--console-ctrl-c &lt;pid&gt;</c>: installs a handler that swallows the event for this process and calls
/// <c>GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0)</c> — every process of that console gets Ctrl+C.</item>
/// <item><c>--read-line [--signal &lt;event&gt;] &lt;arguments…&gt;</c>: prints <c>ready</c>, every further argument in brackets,
/// sets the named event when one is given (the test then knows the program runs), reads one line and prints
/// <c>got: &lt;line&gt;</c>; exit code 0. Console text goes through <c>WriteConsoleW</c> / <c>ReadConsoleW</c> (UTF-16, whatever
/// the console's code page).</item>
/// </list>
/// </summary>
public static class ConsoleInputHelper
{
    public const string SignalOption = "--signal";

    private const ushort VkReturn = 0x0D;

    /// <summary>Keeps the Ctrl+C handler alive while the helper runs.</summary>
    private static readonly Native.HandlerRoutine Swallow = _ => true;

    public static int Type(int processId, string inputFile)
    {
        var text = File.ReadAllText(inputFile, new UTF8Encoding(false));
        using var input = Attach(processId);
        var records = new List<Native.INPUT_RECORD>();
        foreach (var c in text)
        {
            if (c == '\r')
                continue;
            var key = c == '\n' ? VkReturn : (ushort)0;
            var ch = c == '\n' ? '\r' : c;
            records.Add(new Native.INPUT_RECORD { EventType = Native.KEY_EVENT, KeyDown = 1, RepeatCount = 1, VirtualKeyCode = key, Char = ch });
            records.Add(new Native.INPUT_RECORD { EventType = Native.KEY_EVENT, KeyDown = 0, RepeatCount = 1, VirtualKeyCode = key, Char = ch });
        }

        var array = records.ToArray();
        if (array.Length > 0 && !Native.WriteConsoleInputW(input, array, array.Length, out _))
            throw Failed("WriteConsoleInputW");
        Native.FreeConsole();
        return 0;
    }

    public static int CtrlC(int processId)
    {
        using (Attach(processId))
        {
            // This process ignores the event (flag and handler): only the other processes of the console get it.
            if (!Native.SetConsoleCtrlHandler(Swallow, true) || !Native.IgnoreConsoleCtrlC(IntPtr.Zero, true))
                throw Failed("SetConsoleCtrlHandler");
            if (!Native.GenerateConsoleCtrlEvent(Native.CTRL_C_EVENT, 0))
                throw Failed("GenerateConsoleCtrlEvent");
        }

        Native.FreeConsole();
        return 0;
    }

    public static int ReadLine(string[] arguments)
    {
        string? signal = null;
        if (arguments.Length >= 2 && arguments[0] == SignalOption)
        {
            signal = arguments[1];
            arguments = arguments[2..];
        }

        var output = new StringBuilder("ready\r\n");
        foreach (var argument in arguments)
            output.Append('[').Append(argument).Append("]\r\n");
        Write(output.ToString());
        if (signal is not null && OperatingSystem.IsWindows())
        {
            using var ready = EventWaitHandle.OpenExisting(signal);
            ready.Set();
        }

        Write("got: " + Read() + "\r\n");
        return 0;
    }

    private static void Write(string text)
    {
        if (!Native.WriteConsoleW(Native.GetStdHandle(Native.STD_OUTPUT_HANDLE), text, text.Length, out _, IntPtr.Zero))
        {
            Console.Out.Write(text);
            Console.Out.Flush();
        }
    }

    /// <summary>One line without its end, read from the console in UTF-16 units (cooked mode: the console's line editing).</summary>
    private static string Read()
    {
        var input = Native.GetStdHandle(Native.STD_INPUT_HANDLE);
        var line = new StringBuilder();
        var buffer = new char[256];
        while (true)
        {
            if (!Native.ReadConsoleW(input, buffer, buffer.Length, out var read, IntPtr.Zero))
                return line.Length > 0 ? line.ToString() : Console.In.ReadLine() ?? "";
            if (read == 0)
                return line.ToString();
            line.Append(buffer, 0, read);
            var end = line.ToString().IndexOf('\n', StringComparison.Ordinal);
            if (end >= 0)
                return line.ToString(0, end).TrimEnd('\r');
        }
    }

    private static SafeFileHandle Attach(int processId)
    {
        Native.FreeConsole();
        if (!Native.AttachConsole(processId))
            throw Failed("AttachConsole");
        var handle = Native.CreateFileW("CONIN$", Native.GENERIC_READ | Native.GENERIC_WRITE, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
            IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid)
            throw Failed("CreateFileW");
        return handle;
    }

    private static Win32Exception Failed(string call)
    {
        var error = Marshal.GetLastWin32Error();
        return new Win32Exception(error, call + " failed: " + new Win32Exception(error).Message);
    }

    private static class Native
    {
        public const ushort KEY_EVENT = 1;
        public const uint CTRL_C_EVENT = 0;
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 1;
        public const uint FILE_SHARE_WRITE = 2;
        public const uint OPEN_EXISTING = 3;
        public const int STD_INPUT_HANDLE = -10;
        public const int STD_OUTPUT_HANDLE = -11;

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        public delegate bool HandlerRoutine(uint controlType);

        [StructLayout(LayoutKind.Explicit, Size = 20, CharSet = CharSet.Unicode)] // Char as one UTF-16 unit, not an ANSI byte
        public struct INPUT_RECORD
        {
            [FieldOffset(0)]
            public ushort EventType;
            [FieldOffset(4)]
            public int KeyDown;
            [FieldOffset(8)]
            public ushort RepeatCount;
            [FieldOffset(10)]
            public ushort VirtualKeyCode;
            [FieldOffset(12)]
            public ushort VirtualScanCode;
            [FieldOffset(14)]
            public char Char;
            [FieldOffset(16)]
            public uint ControlKeyState;
        }

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool FreeConsole();

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AttachConsole(int processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WriteConsoleInputW(SafeFileHandle input, INPUT_RECORD[] records, int length, out int written);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetConsoleCtrlHandler(HandlerRoutine handler, [MarshalAs(UnmanagedType.Bool)] bool add);

        [DllImport("kernel32.dll", EntryPoint = "SetConsoleCtrlHandler", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IgnoreConsoleCtrlC(IntPtr handler, [MarshalAs(UnmanagedType.Bool)] bool ignore);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GenerateConsoleCtrlEvent(uint controlEvent, uint processGroupId);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        public static extern IntPtr GetStdHandle(int which);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WriteConsoleW(IntPtr output, string text, int length, out int written, IntPtr reserved);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReadConsoleW(IntPtr input, [Out] char[] buffer, int length, out int read, IntPtr control);
    }
}
