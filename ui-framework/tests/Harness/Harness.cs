using System.Runtime.InteropServices;

namespace FreexComp.UiFramework.Tests;

/// <summary>Environment checks of this area's tests (testing.md UIF-1203).</summary>
public static class Harness
{
    public const string NoDesktopReason = "no interactive window station";

    private const int UoiFlags = 1;
    private const uint WsfVisible = 1;

    /// <summary>True when the process's window station is the interactive one (visible), so windows can be created and receive input.</summary>
    public static bool HasInteractiveWindowStation()
    {
        var station = GetProcessWindowStation();
        if (station == IntPtr.Zero)
        {
            return false;
        }

        var flags = new UserObjectFlags();
        return GetUserObjectInformationW(station, UoiFlags, ref flags, (uint)Marshal.SizeOf<UserObjectFlags>(), out _)
            && (flags.Flags & WsfVisible) != 0;
    }

    /// <summary>The only permitted skip (UIF-1203): a <c>Category=Foreground</c> test without an interactive window station.</summary>
    public static void RequireDesktop()
    {
        if (!HasInteractiveWindowStation())
        {
            Assert.Skip(NoDesktopReason);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UserObjectFlags
    {
        public int Inherit;
        public int Reserved;
        public uint Flags;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetProcessWindowStation();

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserObjectInformationW(IntPtr handle, int index, ref UserObjectFlags info, uint length, out uint needed);
}
