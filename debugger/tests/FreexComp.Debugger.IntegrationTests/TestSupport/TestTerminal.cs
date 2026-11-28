using System.Collections;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using FreexComp.Debugger.Tests.TestSupport;
using Microsoft.Win32.SafeHandles;

namespace FreexComp.Debugger.IntegrationTests.TestSupport;

/// <summary>
/// The test terminal of the integration tests (terminal mode, debuggee-console spec §8): starts
/// <c>FakeAdapter.exe --console-run -- &lt;command line&gt;</c> in a new, hidden console (no window appears), with the
/// requested directory and environment, inside a job object with kill-on-close. Records every call. Disposal ends
/// everything it started, whatever the test did.
/// </summary>
internal sealed class TestTerminal : IDebuggeeTerminal, IAsyncDisposable
{
    private readonly Lock _gate = new();
    private readonly List<DebuggeeTerminalRequest> _requests = [];
    private readonly List<Run> _runs = [];

    /// <summary>The helper executable (the FakeAdapter, copied next to this test assembly).</summary>
    public static string HelperPath => Path.Combine(AppContext.BaseDirectory, "FakeAdapter.exe");

    /// <summary>Every request the session made, in order.</summary>
    public IReadOnlyList<DebuggeeTerminalRequest> Requests
    {
        get
        {
            lock (_gate)
                return [.. _requests];
        }
    }

    /// <summary>The one run the session started; fails when there is none or more than one.</summary>
    public Run TheRun
    {
        get
        {
            lock (_gate)
                return Assert.Single(_runs);
        }
    }

    public Task<IDebuggeeTerminalRun> StartAsync(DebuggeeTerminalRequest request, CancellationToken ct = default)
    {
        lock (_gate)
            _requests.Add(request);
        ct.ThrowIfCancellationRequested();
        var run = Run.Start(request);
        lock (_gate)
            _runs.Add(run);
        return Task.FromResult<IDebuggeeTerminalRun>(run);
    }

    public async ValueTask DisposeAsync()
    {
        Run[] runs;
        lock (_gate)
            runs = [.. _runs];
        foreach (var run in runs)
            await run.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// The system's argument rules (the debug console's join, IDE-P3-099): one space between arguments; an argument
    /// with a space, tab or quote, or an empty one, in quotes, a quote inside with a backslash, a run of backslashes
    /// before a quote doubled.
    /// </summary>
    public static string Join(IEnumerable<string> arguments) => string.Join(' ', arguments.Select(Quote));

    private static string Quote(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '"']) < 0)
            return argument;
        var text = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            text.Append('\\', c == '"' ? 2 * backslashes + 1 : backslashes).Append(c);
            backslashes = 0;
        }

        return text.Append('\\', 2 * backslashes).Append('"').ToString();
    }

    /// <summary>One console: the helper process that owns it, in a job of its own.</summary>
    internal sealed class Run : IDebuggeeTerminalRun, IAsyncDisposable
    {
        private readonly Lock _gate = new();
        private readonly SafeJobHandle _job;
        private readonly SafeProcessHandle _process;
        private readonly TaskCompletionSource<int?> _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<bool> _closeCalls = [];
        private readonly RegisteredWaitHandle _exitWait;
        private readonly EventWaitHandle _exitHandle;
        private bool _gone;   // the console was ended from outside (CloseAsync(false), KillConsoleOwner): Ended is null

        private Run(SafeJobHandle job, SafeProcessHandle process, int processId, DateTime started)
        {
            _job = job;
            _process = process;
            ProcessId = processId;
            Started = started;
            _exitHandle = new EventWaitHandle(false, EventResetMode.ManualReset) { SafeWaitHandle = new SafeWaitHandle(process.DangerousGetHandle(), ownsHandle: false) };
            _exitWait = ThreadPool.RegisterWaitForSingleObject(_exitHandle, (_, _) => OnExited(), null, Timeout.Infinite, executeOnlyOnce: true);
        }

        public int ProcessId { get; }

        /// <summary>When the console's owner was started (identifies it together with <see cref="ProcessId"/>).</summary>
        public DateTime Started { get; }

        public Task<int?> Ended => _ended.Task;

        /// <summary>The <c>keepWindow</c> of every <see cref="CloseAsync"/> call, in order.</summary>
        public IReadOnlyList<bool> CloseCalls
        {
            get
            {
                lock (_gate)
                    return [.. _closeCalls];
            }
        }

        public async Task CloseAsync(bool keepWindow, CancellationToken ct = default)
        {
            lock (_gate)
                _closeCalls.Add(keepWindow);
            if (keepWindow)
                return;
            End();
            await _ended.Task.WaitAsync(ct).ConfigureAwait(false);
        }

        /// <summary>Ends the console's owner and everything in its job, as a user who closes the window would.</summary>
        public void KillConsoleOwner() => End();

        /// <summary>The processes the console's owner started so far (direct and indirect), by process id.</summary>
        public IReadOnlyList<int> Descendants()
        {
            var parents = new ProcessRegistry().Parents();
            var known = new Dictionary<int, DateTime> { [ProcessId] = Started };
            for (var found = true; found;)
            {
                found = false;
                foreach (var (child, parent) in parents)
                {
                    if (known.ContainsKey(child) || !known.TryGetValue(parent, out var parentStarted) || StartTimeOf(child) is not { } childStarted
                        || childStarted < parentStarted)
                        continue;
                    known.Add(child, childStarted);
                    found = true;
                }
            }

            return [.. known.Keys.Where(id => id != ProcessId)];
        }

        private void End()
        {
            lock (_gate)
                _gone = true;
            if (!TerminateJobObject(_job, 1) && !_ended.Task.IsCompleted)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "TerminateJobObject failed");
        }

        private void OnExited()
        {
            int? code = GetExitCodeProcess(_process, out var exitCode) ? unchecked((int)exitCode) : null;
            lock (_gate)
            {
                if (_gone)
                    code = null;
            }

            _ended.TrySetResult(code);
        }

        public async ValueTask DisposeAsync()
        {
            End();
            await _ended.Task.WaitAsync(IntegrationFixture.Safety).ConfigureAwait(false);
            _exitWait.Unregister(null);
            _exitHandle.Dispose();
            _job.Dispose();
            _process.Dispose();
        }

        public static Run Start(DebuggeeTerminalRequest request)
        {
            var commandLine = new StringBuilder(Join([HelperPath, "--console-run", "--"]) + " " + Join(request.Command));
            var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>(), Flags = StartfUseShowWindow, ShowWindow = SwHide };
            var environment = EnvironmentBlock(request.Environment);
            if (!CreateProcessW(null, commandLine, IntPtr.Zero, IntPtr.Zero, false, CreateNewConsole | CreateUnicodeEnvironment | CreateSuspended,
                    environment, request.WorkingDirectory, ref startup, out var info))
                throw new Win32Exception(Marshal.GetLastPInvokeError());

            var process = new SafeProcessHandle(info.Process, ownsHandle: true);
            using var thread = new SafeProcessHandle(info.Thread, ownsHandle: true);
            var job = CreateJobObjectW(IntPtr.Zero, null);
            var limits = new JobObjectExtendedLimitInformation { BasicLimitInformation = { LimitFlags = JobObjectLimitKillOnJobClose } };
            if (job.IsInvalid
                || !SetInformationJobObject(job, ExtendedLimitInformationClass, ref limits, (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>())
                || !AssignProcessToJobObject(job, process))
            {
                var error = Marshal.GetLastPInvokeError();
                TerminateProcess(process, 1);
                process.Dispose();
                job.Dispose();
                throw new Win32Exception(error, "The console's job object could not be set up");
            }

            using var started = System.Diagnostics.Process.GetProcessById(info.ProcessId);
            var run = new Run(job, process, info.ProcessId, started.StartTime);
            ProcessLeakFixture.Registry.Track(info.ProcessId, "test terminal");
            if (ResumeThread(thread) == uint.MaxValue)
            {
                var error = Marshal.GetLastPInvokeError();
                run.End();
                throw new Win32Exception(error, "ResumeThread failed");
            }

            return run;
        }

        private static DateTime? StartTimeOf(int processId)
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(processId);
                return process.StartTime;
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception)
            {
                return null;
            }
        }

        /// <summary>This process's environment with the request's changes (a null value removes the variable).</summary>
        private static string EnvironmentBlock(IReadOnlyDictionary<string, string?>? changes)
        {
            var variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
                variables[(string)entry.Key] = (string?)entry.Value ?? "";
            foreach (var (name, value) in changes ?? new Dictionary<string, string?>())
            {
                if (value is null)
                    variables.Remove(name);
                else
                    variables[name] = value;
            }

            var block = new StringBuilder();
            foreach (var (name, value) in variables)
                block.Append(name).Append('=').Append(value).Append('\0');
            return block.Append('\0').ToString();
        }
    }

    // ---- system calls ----

    private const uint CreateSuspended = 0x00000004;
    private const uint CreateNewConsole = 0x00000010;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const int StartfUseShowWindow = 0x00000001;
    private const short SwHide = 0;
    private const int ExtendedLimitInformationClass = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x2000;

    private sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeJobHandle() : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
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
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public int ProcessId;
        public int ThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
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
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(string? applicationName, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint creationFlags, string environment, string currentDirectory,
        ref StartupInfo startupInfo, out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern uint ResumeThread(SafeProcessHandle thread);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern SafeJobHandle CreateJobObjectW(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeJobHandle job, int infoClass, ref JobObjectExtendedLimitInformation info, uint length);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeJobHandle job, SafeProcessHandle process);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(SafeJobHandle job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
