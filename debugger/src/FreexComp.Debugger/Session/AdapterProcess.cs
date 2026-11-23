using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace FreexComp.Debugger.Session;

/// <summary>
/// The debug adapter child process (architecture §3): redirected stdio (UTF-8 without byte-order mark), stderr
/// captured line by line, and a kill-on-close job object so that neither the adapter nor anything it started
/// (the debuggee) can outlive the session.
/// </summary>
internal sealed class AdapterProcess : IAsyncDisposable
{
    private const int StderrTailLines = 20;
    private static readonly TimeSpan ExitWaitAfterKill = TimeSpan.FromSeconds(10);

    private readonly Process _process;
    private readonly JobHandle? _job;
    private readonly Lock _gate = new();
    private readonly Queue<string> _stderrTail = new();
    private int? _exitCode;
    private bool _disposed;

    private AdapterProcess(Process process, Action<string>? onStderrLine)
    {
        _process = process;
        ProcessId = process.Id;
        StandardInput = process.StandardInput.BaseStream;
        StandardOutput = process.StandardOutput.BaseStream;
        _job = JobHandle.TryCreateFor(process);
        StderrCompleted = Task.Run(() => ReadStderrAsync(process.StandardError, onStderrLine));
    }

    /// <summary>
    /// Starts the adapter. Throws <see cref="DebuggerException"/>: <c>AdapterNotFound</c> when the file does not
    /// exist (no search is performed, DBG-004), <c>AdapterStartFailed</c> when the process cannot be created.
    /// </summary>
    /// <param name="adapterPath">Absolute path of the adapter executable.</param>
    /// <param name="arguments">Command line arguments; empty for the real adapter (tests pass a scenario file).</param>
    /// <param name="onStderrLine">Called for every stderr line, on the stderr reader task.</param>
    public static AdapterProcess Start(string adapterPath, IReadOnlyList<string> arguments, Action<string>? onStderrLine)
    {
        if (!File.Exists(adapterPath))
            throw new DebuggerException(DebugErrorCode.AdapterNotFound, Messages.AdapterNotFound + adapterPath);

        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var info = new ProcessStartInfo(adapterPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = utf8,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(adapterPath)),
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        // Only meaningful for the CodeView/PDB debug container; harmless otherwise (DBG-005).
        info.Environment["LLDB_USE_NATIVE_PDB_READER"] = "1";
        // The adapter needs a Python runtime library; only the bundled one may satisfy it, never one that happens to
        // be installed on the machine: its directory goes first on the adapter's own search path.
        if (BundledPythonDirectory(adapterPath) is { } python)
            info.Environment["PATH"] = python + Path.PathSeparator + (info.Environment.TryGetValue("PATH", out var path) ? path : "");

        Process process;
        try
        {
            process = Process.Start(info) ?? throw new InvalidOperationException("no process was started");
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            throw new DebuggerException(DebugErrorCode.AdapterStartFailed, Messages.AdapterStartFailed + e.Message, inner: e);
        }

        return new AdapterProcess(process, onStderrLine);
    }

    /// <summary>
    /// The directory of the Python runtime bundled with the toolchain the adapter belongs to: <c>python-embed</c>
    /// (development tree) or <c>python</c> (installed product) next to the toolchain folder, i.e. two levels above the
    /// adapter's <c>bin</c> directory. Null when there is none (the adapter then runs with the unchanged environment).
    /// </summary>
    internal static string? BundledPythonDirectory(string adapterPath)
    {
        var toolchain = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(adapterPath)));
        if (Path.GetDirectoryName(toolchain) is not { } parent)
            return null;
        foreach (var name in (string[])["python-embed", "python"])
        {
            var candidate = Path.Combine(parent, name);
            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }

    public int ProcessId { get; }

    /// <summary>Bytes to the adapter.</summary>
    public Stream StandardInput { get; }

    /// <summary>Bytes from the adapter.</summary>
    public Stream StandardOutput { get; }

    /// <summary>True when the adapter was put into a kill-on-close job object (DBG-008).</summary>
    public bool IsInJob => _job is not null;

    /// <summary>Completes when the adapter's stderr reached its end.</summary>
    public Task StderrCompleted { get; }

    public bool HasExited
    {
        get
        {
            lock (_gate)
                return _exitCode is not null || (!_disposed && _process.HasExited);
        }
    }

    /// <summary>Exit code once the adapter has exited, else null.</summary>
    public int? ExitCode
    {
        get
        {
            lock (_gate)
            {
                if (_exitCode is null && !_disposed && _process.HasExited)
                    _exitCode = _process.ExitCode;
                return _exitCode;
            }
        }
    }

    /// <summary>The last 20 stderr lines (appended to the termination message when the adapter crashes, DBG-006).</summary>
    public IReadOnlyList<string> LastStderrLines
    {
        get
        {
            lock (_gate)
                return [.. _stderrTail];
        }
    }

    public Task WaitForExitAsync(CancellationToken ct) =>
        HasExited ? Task.CompletedTask : _process.WaitForExitAsync(ct);

    /// <summary>Closes the adapter's stdin (it then sees end of input).</summary>
    public void CloseInput()
    {
        try
        {
            StandardInput.Close();
        }
        catch (IOException)
        {
            // The pipe is already broken: the adapter is gone.
        }
    }

    /// <summary>Kills the adapter and everything it started. No-op when it has already exited.</summary>
    public void Kill()
    {
        if (HasExited)
            return;
        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception or AggregateException)
        {
            // Exited meanwhile, or a descendant could not be terminated: the job object covers the rest.
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
        }

        CloseInput();
        _job?.Dispose();   // kill-on-close: ends the adapter and every descendant
        Kill();            // without a job (nested job restrictions) the tree is killed by walking it
        try
        {
            await _process.WaitForExitAsync().WaitAsync(ExitWaitAfterKill).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Nothing more can be done; the handle is released below.
        }

        lock (_gate)
        {
            if (_exitCode is null && _process.HasExited)
                _exitCode = _process.ExitCode;
            _disposed = true;
        }

        _process.Dispose();
    }

    private async Task ReadStderrAsync(StreamReader reader, Action<string>? onLine)
    {
        try
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                lock (_gate)
                {
                    if (_stderrTail.Count == StderrTailLines)
                        _stderrTail.Dequeue();
                    _stderrTail.Enqueue(line);
                }

                onLine?.Invoke(line);
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            // The pipe broke or the process object was disposed: end of stderr.
        }
    }

    /// <summary>A Win32 job object with <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c>.</summary>
    private sealed class JobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private const int ExtendedLimitInformationClass = 9;
        private const uint JobObjectLimitKillOnJobClose = 0x2000;

        public JobHandle() : base(ownsHandle: true)
        {
        }

        /// <summary>Null when the job cannot be created or the process cannot be assigned (the session continues, DBG-008).</summary>
        public static JobHandle? TryCreateFor(Process process)
        {
            var job = CreateJobObjectW(IntPtr.Zero, null);
            if (job.IsInvalid)
            {
                job.Dispose();
                return null;
            }

            var limits = new JobObjectExtendedLimitInformation { BasicLimitInformation = { LimitFlags = JobObjectLimitKillOnJobClose } };
            try
            {
                if (SetInformationJobObject(job, ExtendedLimitInformationClass, ref limits, (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>())
                    && AssignProcessToJobObject(job, process.Handle))
                    return job;
            }
            catch (Exception e) when (e is InvalidOperationException or Win32Exception)
            {
                // The process exited before its handle could be used.
            }

            // Not assigned: closing the empty job kills nothing.
            job.Dispose();
            return null;
        }

        protected override bool ReleaseHandle() => CloseHandle(handle);

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

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern JobHandle CreateJobObjectW(IntPtr jobAttributes, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(
            JobHandle job, int infoClass, ref JobObjectExtendedLimitInformation info, uint length);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(JobHandle job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
