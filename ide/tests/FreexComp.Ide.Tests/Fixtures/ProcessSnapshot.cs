using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FreexComp.Ide.Tests.Fixtures;

/// <summary>The processes whose parent is a given process (Toolhelp snapshot), for failure messages of the process tests.</summary>
internal static class ProcessSnapshot
{
    public static IReadOnlyList<(int Id, string Name)> Children(int parentId)
    {
        var result = new List<(int, string)>();
        using var snapshot = Native.CreateToolhelp32Snapshot(Native.TH32CS_SNAPPROCESS, 0);
        if (snapshot.IsInvalid)
            return result;
        var entry = new Native.PROCESSENTRY32W { dwSize = Marshal.SizeOf<Native.PROCESSENTRY32W>() };
        for (var ok = Native.Process32FirstW(snapshot, ref entry); ok; ok = Native.Process32NextW(snapshot, ref entry))
        {
            if (entry.th32ParentProcessID == parentId)
                result.Add((entry.th32ProcessID, entry.szExeFile));
        }

        return result;
    }

    private static class Native
    {
        public const uint TH32CS_SNAPPROCESS = 0x00000002;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PROCESSENTRY32W
        {
            public int dwSize;
            public int cntUsage;
            public int th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public int th32ModuleID;
            public int cntThreads;
            public int th32ParentProcessID;
            public int pcPriClassBase;
            public int dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        public static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, int processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool Process32FirstW(SafeFileHandle snapshot, ref PROCESSENTRY32W entry);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool Process32NextW(SafeFileHandle snapshot, ref PROCESSENTRY32W entry);
    }
}
