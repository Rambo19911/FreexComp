using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace FreexComp.Ide.Tests.Fixtures;

/// <summary>
/// The <c>--console-key</c> mode of <see cref="ProcessHelper"/> (I-138): runs in its own helper process so the test
/// process never changes its console. Attaches to another process's console, records its title and screen text, then
/// injects one key press.
/// </summary>
internal static class ConsoleKeyHelper
{
    public const string TitlePrefix = "title: ";

    public static int Run(int processId, string outputFile)
    {
        Native.FreeConsole();
        if (!Native.AttachConsole(processId))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        using var input = Open("CONIN$");
        using var output = Open("CONOUT$");

        var title = new StringBuilder(1024);
        Native.GetConsoleTitleW(title, title.Capacity);
        if (!Native.GetConsoleScreenBufferInfo(output, out var info))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var text = new StringBuilder();
        text.Append(TitlePrefix).Append(title).Append('\n');
        var line = new StringBuilder(info.Width);
        for (short row = 0; row <= info.CursorY; row++)
        {
            line.Clear();
            line.Capacity = Math.Max(line.Capacity, info.Width);
            if (!Native.ReadConsoleOutputCharacterW(output, line, info.Width, row << 16, out var read))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            text.Append(line.ToString(0, Math.Min(read, line.Length)).TrimEnd()).Append('\n');
        }

        File.WriteAllText(outputFile, text.ToString(), new UTF8Encoding(false));

        Native.INPUT_RECORD[] keys =
        [
            new() { EventType = Native.KEY_EVENT, KeyDown = 1, RepeatCount = 1, VirtualKeyCode = 0x20, Char = ' ' },
            new() { EventType = Native.KEY_EVENT, KeyDown = 0, RepeatCount = 1, VirtualKeyCode = 0x20, Char = ' ' },
        ];
        if (!Native.WriteConsoleInputW(input, keys, keys.Length, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        Native.FreeConsole();
        return 0;
    }

    private static SafeFileHandle Open(string name)
    {
        var handle = Native.CreateFileW(name, Native.GENERIC_READ | Native.GENERIC_WRITE, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero,
            Native.OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return handle;
    }

    private static class Native
    {
        public const ushort KEY_EVENT = 1;
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 1;
        public const uint FILE_SHARE_WRITE = 2;
        public const uint OPEN_EXISTING = 3;

        [StructLayout(LayoutKind.Explicit, Size = 20)]
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

        [StructLayout(LayoutKind.Sequential)]
        public struct CONSOLE_SCREEN_BUFFER_INFO
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

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool FreeConsole();

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AttachConsole(int processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern int GetConsoleTitleW(StringBuilder title, int size);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetConsoleScreenBufferInfo(SafeFileHandle output, out CONSOLE_SCREEN_BUFFER_INFO info);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReadConsoleOutputCharacterW(SafeFileHandle output, StringBuilder text, int length, int coord, out int read);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WriteConsoleInputW(SafeFileHandle input, Native.INPUT_RECORD[] records, int length, out int written);
    }
}
