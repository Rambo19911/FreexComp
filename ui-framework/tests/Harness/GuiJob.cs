using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FreexComp.UiFramework.Tests;

/// <summary>
/// U-162: the job object of the test host. Every program that <see cref="StartGui"/> or
/// <see cref="LayoutHost.RunProcessAsync"/> starts is put into it right after its start (its children are born in it);
/// its only handle is held by the test host until the host ends, and the job kills every process in it when that handle
/// closes, so no test program outlives its test host (also when the host is stopped or killed).
/// </summary>
public static partial class Gui
{
    private const uint JobObjectLimitKillOnJobClose = 0x2000;
    private const int JobObjectExtendedLimitInformationClass = 9;

    private static readonly Lazy<IntPtr> HostJob = new(CreateHostJob);

    /// <summary>
    /// Puts <paramref name="process"/> (just started, so its handle is still its own) into the job of the test host. A
    /// program that already ended (a short layout or dump run) cannot be assigned any more and needs no job.
    /// </summary>
    internal static void AssignToHostJob(Process process)
    {
        if (!AssignProcessToJobObject(HostJob.Value, process.Handle))
        {
            var error = Marshal.GetLastPInvokeError();
            if (process.HasExited)
            {
                return;                                             // ERROR_ACCESS_DENIED for an ended process
            }

            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // exited meanwhile
            }

            throw new InvalidOperationException($"the test program {process.Id} was not put into the job of the test host (error {error})");
        }
    }

    private static IntPtr CreateHostJob()
    {
        var job = CreateJobObjectW(IntPtr.Zero, null);
        if (job == IntPtr.Zero)
        {
            throw new InvalidOperationException($"CreateJobObject failed (error {Marshal.GetLastPInvokeError()})");
        }

        var info = new JobObjectExtendedLimitInformation { LimitFlags = JobObjectLimitKillOnJobClose };
        if (!SetInformationJobObject(job, JobObjectExtendedLimitInformationClass, ref info, (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>()))
        {
            throw new InvalidOperationException($"SetInformationJobObject failed (error {Marshal.GetLastPInvokeError()})");
        }

        return job;                                                 // never closed: it closes when the test host ends
    }

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObjectW(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref JobObjectExtendedLimitInformation info, uint length);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    /// <summary>JOBOBJECT_EXTENDED_LIMIT_INFORMATION (the basic limits, the I/O counters, four sizes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
