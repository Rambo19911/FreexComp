// Helper modes for consoles (debugger testing, DBG-559 b). None of them speaks the protocol; each runs in a process of
// its own, so the test process never changes its own console. Exit code 0, or 1 with the failed call and its system
// error on stderr.
//   --console-run -- <command line>   (started in a new console) runs the command line, exactly as given, as a child
//                                      in this console with this process's environment and directory; Ctrl+C and
//                                      Ctrl+Break reach the child but do not end this process; exits with the child's
//                                      exit code.
//   --console-type <pid> <file>        attaches to the console of <pid> and writes the UTF-8 text of <file> into its
//                                      input buffer: one key-down and one key-up record per UTF-16 unit, a line feed as
//                                      the Enter key (a carriage return is dropped).
//   --console-screen <pid> <file>      writes the console's screen rows, from the first to the cursor's, right-trimmed,
//                                      to <file> as UTF-8 lines.
//   --console-ctrl-c <pid>             attaches, swallows the event itself and sends Ctrl+C to every process of the console.

using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace FakeAdapter;

internal static class ConsoleModes
{
    private const string RunMarker = " --console-run -- ";

    /// <summary>Runs the mode <paramref name="args"/> names; null when they name none of the console modes.</summary>
    public static int? TryRun(string[] args)
    {
        try
        {
            return args switch
            {
                ["--console-run", "--", ..] => Run(),
                ["--console-type", var pid, var file] => Type(Pid(pid), file),
                ["--console-screen", var pid, var file] => Screen(Pid(pid), file),
                ["--console-ctrl-c", var pid] => CtrlC(Pid(pid)),
                _ => null,
            };
        }
        catch (Win32Exception e)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
    }

    private static int Pid(string text) => int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);

    // ---- --console-run ----

    private static readonly Native.HandlerRoutine SwallowCtrl = type => type is Native.CtrlCEvent or Native.CtrlBreakEvent;

    private static int Run()
    {
        // The raw command line: the part after the marker is the child's, character for character.
        var raw = Marshal.PtrToStringUni(Native.GetCommandLineW())!;
        var at = raw.IndexOf(RunMarker, StringComparison.Ordinal);
        if (at < 0)
            throw new Win32Exception(87, "GetCommandLineW failed: no \"" + RunMarker.Trim() + "\" in the command line");
        var commandLine = raw[(at + RunMarker.Length)..];

        // Ctrl+C on (a process that started this one may have turned it off, and children inherit the setting);
        // this process itself survives both keys.
        if (!Native.SetConsoleCtrlHandler(IntPtr.Zero, false))
            throw Failed("SetConsoleCtrlHandler");
        if (!Native.SetConsoleCtrlHandler(SwallowCtrl, true))
            throw Failed("SetConsoleCtrlHandler");

        var startup = new Native.StartupInfo { Size = Marshal.SizeOf<Native.StartupInfo>() };
        var line = new StringBuilder(commandLine);
        if (!Native.CreateProcessW(null, line, IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, null, ref startup, out var info))
            throw Failed("CreateProcessW");
        Native.CloseHandle(info.Thread);
        try
        {
            if (Native.WaitForSingleObject(info.Process, Native.Infinite) != 0)
                throw Failed("WaitForSingleObject");
            if (!Native.GetExitCodeProcess(info.Process, out var code))
                throw Failed("GetExitCodeProcess");
            return unchecked((int)code);
        }
        finally
        {
            Native.CloseHandle(info.Process);
        }
    }

    // ---- --console-type ----

    private static int Type(int processId, string file)
    {
        var text = File.ReadAllText(file, new UTF8Encoding(false));
        var records = new List<Native.InputRecord>();
        foreach (var unit in text)
        {
            if (unit == '\r')
                continue;
            var (key, character) = unit == '\n' ? (Native.VkReturn, (ushort)'\r') : ((ushort)0, (ushort)unit);
            records.Add(new Native.InputRecord { EventType = Native.KeyEvent, KeyDown = 1, RepeatCount = 1, VirtualKeyCode = key, UnicodeChar = character });
            records.Add(new Native.InputRecord { EventType = Native.KeyEvent, KeyDown = 0, RepeatCount = 1, VirtualKeyCode = key, UnicodeChar = character });
        }

        Attach(processId);
        using var input = Open("CONIN$");
        var all = records.ToArray();
        for (var done = 0; done < all.Length;)
        {
            if (!Native.WriteConsoleInputW(input, all[done..], all.Length - done, out var written) || written <= 0)
                throw Failed("WriteConsoleInputW");
            done += written;
        }

        Native.FreeConsole();
        return 0;
    }

    // ---- --console-screen ----

    private static int Screen(int processId, string file)
    {
        Attach(processId);
        using var output = Open("CONOUT$");
        if (!Native.GetConsoleScreenBufferInfo(output, out var info))
            throw Failed("GetConsoleScreenBufferInfo");
        var text = new StringBuilder();
        var row = new char[info.Width];
        for (var y = 0; y <= info.CursorY; y++)
        {
            if (!Native.ReadConsoleOutputCharacterW(output, row, info.Width, y << 16, out var read))
                throw Failed("ReadConsoleOutputCharacterW");
            text.Append(new string(row, 0, read).TrimEnd()).Append('\n');
        }

        Native.FreeConsole();
        File.WriteAllText(file, text.ToString(), new UTF8Encoding(false));
        return 0;
    }

    // ---- --console-ctrl-c ----

    private static int CtrlC(int processId)
    {
        Attach(processId);
        // This process is in the console now and gets the event too.
        if (!Native.SetConsoleCtrlHandler(SwallowCtrl, true))
            throw Failed("SetConsoleCtrlHandler");
        if (!Native.GenerateConsoleCtrlEvent(Native.CtrlCEvent, 0))
            throw Failed("GenerateConsoleCtrlEvent");
        return 0;
    }

    // ---- helpers ----

    private static void Attach(int processId)
    {
        Native.FreeConsole();
        if (!Native.AttachConsole(processId))
            throw Failed("AttachConsole");
    }

    private static SafeFileHandle Open(string name)
    {
        var handle = Native.CreateFileW(name, Native.GenericRead | Native.GenericWrite, Native.FileShareRead | Native.FileShareWrite,
            IntPtr.Zero, Native.OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
            throw Failed("CreateFileW " + name);
        return handle;
    }

    private static Win32Exception Failed(string call)
    {
        var error = Marshal.GetLastPInvokeError();
        return new Win32Exception(error, call + " failed: " + new Win32Exception(error).Message + " (error " + error.ToString(CultureInfo.InvariantCulture) + ")");
    }

    private static class Native
    {
        public const uint CtrlCEvent = 0;
        public const uint CtrlBreakEvent = 1;
        public const ushort KeyEvent = 1;
        public const ushort VkReturn = 0x0D;
        public const uint Infinite = 0xFFFFFFFF;
        public const uint GenericRead = 0x80000000;
        public const uint GenericWrite = 0x40000000;
        public const uint FileShareRead = 1;
        public const uint FileShareWrite = 2;
        public const uint OpenExisting = 3;

        public delegate bool HandlerRoutine(uint type);

        // KEY_EVENT_RECORD inside INPUT_RECORD; the character is a 16-bit unit, never marshalled through a code page.
        [StructLayout(LayoutKind.Explicit, Size = 20)]
        public struct InputRecord
        {
            [FieldOffset(0)] public ushort EventType;
            [FieldOffset(4)] public int KeyDown;
            [FieldOffset(8)] public ushort RepeatCount;
            [FieldOffset(10)] public ushort VirtualKeyCode;
            [FieldOffset(12)] public ushort VirtualScanCode;
            [FieldOffset(14)] public ushort UnicodeChar;
            [FieldOffset(16)] public uint ControlKeyState;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ScreenBufferInfo
        {
            public short Width;
            public short Height;
            public short CursorX;
            public short CursorY;
            public ushort Attributes;
            public short WindowLeft;
            public short WindowTop;
            public short WindowRight;
            public short WindowBottom;
            public short MaxWidth;
            public short MaxHeight;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct StartupInfo
        {
            public int Size;
            public IntPtr Reserved;
            public IntPtr Desktop;
            public IntPtr Title;
            public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
            public short ShowWindow;
            public short Reserved2Size;
            public IntPtr Reserved2;
            public IntPtr StdInput, StdOutput, StdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ProcessInformation
        {
            public IntPtr Process;
            public IntPtr Thread;
            public int ProcessId;
            public int ThreadId;
        }

        [DllImport("kernel32.dll", ExactSpelling = true)]
        public static extern IntPtr GetCommandLineW();

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetConsoleCtrlHandler(IntPtr handler, [MarshalAs(UnmanagedType.Bool)] bool add);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetConsoleCtrlHandler(HandlerRoutine handler, [MarshalAs(UnmanagedType.Bool)] bool add);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GenerateConsoleCtrlEvent(uint ctrlEvent, uint processGroupId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreateProcessW(string? applicationName, StringBuilder commandLine, IntPtr processAttributes,
            IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint creationFlags, IntPtr environment,
            string? currentDirectory, ref StartupInfo startupInfo, out ProcessInformation processInformation);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        public static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool FreeConsole();

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AttachConsole(int processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetConsoleScreenBufferInfo(SafeFileHandle output, out ScreenBufferInfo info);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReadConsoleOutputCharacterW(SafeFileHandle output, [Out] char[] text, int length, int coord, out int read);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WriteConsoleInputW(SafeFileHandle input, Native.InputRecord[] records, int length, out int written);
    }
}
