using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Xunit.Sdk;
using Xunit.v3;

namespace FreexComp.Debugger.Tests.TestSupport;

/// <summary>
/// The processes a test run started: process id and start time (a process id alone may be reused), and the test
/// that started it. <see cref="KillLeaked"/> finds those still alive, kills them (whole tree) and reports them.
/// </summary>
internal sealed class ProcessRegistry
{
    private readonly ConcurrentDictionary<int, (DateTime Started, string What, string Owner)> _processes = new();

    public int Count => _processes.Count;

    /// <summary>How a leaked process is killed (whole tree); a test replaces it to fail the way the system call does.</summary>
    public Action<Process> KillTree { get; init; } = process => process.Kill(entireProcessTree: true);

    public bool IsTracked(int processId) => _processes.ContainsKey(processId);

    /// <summary>Records a running process; one that has already ended needs no record.</summary>
    /// <param name="processId">The process.</param>
    /// <param name="what">What it is, for the report.</param>
    /// <param name="owner">The test that started it (<see cref="ProcessLeakFixture.CurrentTest"/>); null: the test running now.</param>
    public void Track(int processId, string what, string? owner = null)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            _processes[processId] = (process.StartTime, what + " (" + process.ProcessName + ")", owner ?? ProcessLeakFixture.CurrentTest);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Not running any more (or not ours to look at): nothing to leak.
        }
    }

    /// <summary>Records a process and everything it has started so far (the adapter, its debug server, the debuggee).</summary>
    public void TrackTree(int processId, string what, string? owner = null)
    {
        owner ??= ProcessLeakFixture.CurrentTest;
        Track(processId, what, owner);
        var parents = ParentsByProcessId();
        var known = new HashSet<int> { processId };
        // Children have higher start times, not higher ids: repeat until no new descendant is found.
        for (var found = true; found;)
        {
            found = false;
            foreach (var (child, parent) in parents)
            {
                if (known.Contains(parent) && known.Add(child))
                {
                    Track(child, "started by " + what, owner);
                    found = true;
                }
            }
        }
    }

    /// <summary>
    /// Kills every recorded process that is still alive and returns one description per such process. A process that
    /// is on its way out (its job object was just closed) gets <paramref name="grace"/> to finish first: the wait is
    /// on the process's exit, the time only a safety net. With <paramref name="owner"/> only the processes of that test
    /// are looked at.
    /// </summary>
    public IReadOnlyList<string> KillLeaked(TimeSpan grace = default, string? owner = null)
    {
        // First find everything that is alive, then kill: killing one process tree must not hide its members from the report.
        var leaked = new List<string>();
        var alive = new List<Process>();
        foreach (var (processId, (started, what, startedBy)) in _processes.ToArray().OrderBy(entry => entry.Key))
        {
            if (owner is not null && startedBy != owner)
                continue;
            _processes.TryRemove(processId, out _);
            Process? process = null;
            try
            {
                process = Process.GetProcessById(processId);
                if (!process.HasExited && process.StartTime == started && !(grace > TimeSpan.Zero && process.WaitForExit(grace)))
                {
                    leaked.Add($"{what}, process id {processId}");
                    alive.Add(process);
                    process = null;
                }
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Gone in the meantime.
            }
            finally
            {
                process?.Dispose();
            }
        }

        foreach (var process in alive)
        {
            using (process)
            {
                try
                {
                    KillTree(process);
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException)
                {
                    // Gone in the meantime (killed with its parent), or a member of its tree is already ending by
                    // itself and cannot be terminated a second time. Either way the wait below sees it out.
                }

                try
                {
                    process.WaitForExit(TimeSpan.FromSeconds(30));
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Gone in the meantime.
                }
            }
        }

        return leaked;
    }

    private static Dictionary<int, int> ParentsByProcessId()
    {
        var parents = new Dictionary<int, int>();
        var snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);
        if (snapshot == InvalidHandle)
            return parents;
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            for (var more = Process32FirstW(snapshot, ref entry); more; more = Process32NextW(snapshot, ref entry))
                parents[(int)entry.ProcessId] = (int)entry.ParentProcessId;
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return parents;
    }

    private const uint SnapshotProcesses = 0x00000002;
    private static readonly nint InvalidHandle = -1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public nint DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int BasePriority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32FirstW(nint snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32NextW(nint snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}

/// <summary>
/// Process-leak check of a whole test assembly (debugger testing DBG-334): every helper that starts a process records
/// it in <see cref="Registry"/>. After each test, <see cref="ProcessLeakCheckAttribute"/> fails the test when one of
/// its processes is still alive; when the last test has finished, no recorded process at all may be alive. Leftovers
/// are killed and fail the run.
/// </summary>
public sealed class ProcessLeakFixture : IDisposable
{
    /// <summary>
    /// How long a process that is being ended (its job object was just closed) may take to disappear. The wait is on
    /// the process's exit; the time is a safety net for a loaded machine.
    /// </summary>
    internal static readonly TimeSpan ExitGrace = TimeSpan.FromSeconds(30);

    internal static ProcessRegistry Registry { get; } = new();

    /// <summary>Identifies the running test ("" outside a test).</summary>
    internal static string CurrentTest => TestContext.Current.Test?.UniqueID ?? "";

    public void Dispose()
    {
        var leaked = Registry.KillLeaked();
        if (leaked.Count > 0)
            throw new XunitException("Processes left running by the tests (killed now):\n  " + string.Join("\n  ", leaked));
    }
}

/// <summary>
/// Applied to the test assembly: after every test, the processes recorded for that test must be gone. One that is
/// still alive is killed and fails the test that started it.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ProcessLeakCheckAttribute : BeforeAfterTestAttribute
{
    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        var leaked = ProcessLeakFixture.Registry.KillLeaked(ProcessLeakFixture.ExitGrace, test.UniqueID);
        if (leaked.Count > 0)
            throw new XunitException("The test left processes running (killed now): " + string.Join("; ", leaked));
    }
}
