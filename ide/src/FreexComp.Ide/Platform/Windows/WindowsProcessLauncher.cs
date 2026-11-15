using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Platform;
using Microsoft.Win32.SafeHandles;

namespace FreexComp.Ide.Platform.Windows;

/// <summary>
/// IDE-P3-083: every program runs in its own Job Object with <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c>. The process is
/// created suspended, assigned to the job, then resumed, so each process it starts belongs to the job too and dies with
/// it — on <see cref="IRunningProcess.KillTreeAsync"/>, when the job handle is closed, and when the IDE itself ends.
/// Captured pipes are read on their own background tasks in chunks of up to 4 096 chars (IDE-P3-073).
/// </summary>
public sealed class WindowsProcessLauncher(IClock clock) : IProcessLauncher
{
    /// <summary>Pipe ends are inheritable only between their creation and CreateProcess; one start at a time.</summary>
    private static readonly Lock CreateGate = new();

    public IRunningProcess Start(ProcessStartRequest request, Action<ProcessOutputLine> onOutput)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(onOutput);
        var capture = request.Capture && !request.ConsoleHost;
        var job = Native.CreateJobObjectW(IntPtr.Zero, null);
        if (job.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            SetKillOnClose(job);
            SafeFileHandle? stdoutRead = null, stderrRead = null;
            Native.PROCESS_INFORMATION info;
            lock (CreateGate)
            {
                SafeFileHandle? stdinRead = null, stdinWrite = null, stdoutWrite = null, stderrWrite = null;
                try
                {
                    var startup = new Native.STARTUPINFOW { cb = Marshal.SizeOf<Native.STARTUPINFOW>() };
                    uint flags = Native.CREATE_SUSPENDED | Native.CREATE_UNICODE_ENVIRONMENT;
                    if (capture)
                    {
                        // IDE-P3-082: stdin redirected and closed at once, stdout/stderr captured, no console window.
                        (stdinRead, stdinWrite) = CreatePipe(parentEnd: false);
                        (stdoutRead, stdoutWrite) = CreatePipe(parentEnd: true);
                        (stderrRead, stderrWrite) = CreatePipe(parentEnd: true);
                        startup.dwFlags = Native.STARTF_USESTDHANDLES;
                        startup.hStdInput = stdinRead.DangerousGetHandle();
                        startup.hStdOutput = stdoutWrite.DangerousGetHandle();
                        startup.hStdError = stderrWrite.DangerousGetHandle();
                        flags |= Native.CREATE_NO_WINDOW;
                    }
                    else
                    {
                        flags |= Native.CREATE_NEW_CONSOLE; // its own console window (external console, console host)
                    }

                    var commandLine = new StringBuilder("\"" + request.FileName + "\"" + (request.Arguments.Length > 0 ? " " + request.Arguments : ""));
                    if (!Native.CreateProcessW(request.FileName, commandLine, IntPtr.Zero, IntPtr.Zero, capture, flags, IntPtr.Zero,
                            request.WorkingDirectory, ref startup, out info))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                catch
                {
                    stdoutRead?.Dispose();
                    stderrRead?.Dispose();
                    throw;
                }
                finally
                {
                    // The child holds its own copies; closing the stdin write end gives it end-of-file.
                    stdinRead?.Dispose();
                    stdinWrite?.Dispose();
                    stdoutWrite?.Dispose();
                    stderrWrite?.Dispose();
                }
            }

            var processHandle = new SafeProcessHandle(info.hProcess, ownsHandle: true);
            using var threadHandle = new SafeProcessHandle(info.hThread, ownsHandle: true);
            if (!Native.AssignProcessToJobObject(job, processHandle))
            {
                var error = Marshal.GetLastWin32Error();
                Native.TerminateProcess(processHandle, 1);
                processHandle.Dispose();
                stdoutRead?.Dispose();
                stderrRead?.Dispose();
                throw new Win32Exception(error);
            }

            if (Native.ResumeThread(threadHandle) == uint.MaxValue)
            {
                var error = Marshal.GetLastWin32Error();
                Native.TerminateJobObject(job, 1);
                processHandle.Dispose();
                stdoutRead?.Dispose();
                stderrRead?.Dispose();
                throw new Win32Exception(error);
            }

            return new RunningProcess(clock, job, processHandle, info.dwProcessId, stdoutRead, stderrRead, onOutput);
        }
        catch
        {
            job.Dispose();
            throw;
        }
    }

    private static void SetKillOnClose(SafeJobHandle job)
    {
        var limits = new Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        limits.BasicLimitInformation.LimitFlags = Native.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        if (!Native.SetInformationJobObject(job, Native.JobObjectExtendedLimitInformation, ref limits, Marshal.SizeOf<Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    /// <summary>An anonymous pipe whose child end is inheritable; <paramref name="parentEnd"/> true = the IDE reads.</summary>
    private static (SafeFileHandle Read, SafeFileHandle Write) CreatePipe(bool parentEnd)
    {
        var security = new Native.SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<Native.SECURITY_ATTRIBUTES>(), bInheritHandle = 1 };
        if (!Native.CreatePipe(out var read, out var write, ref security, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var parent = parentEnd ? read : write;
        if (!Native.SetHandleInformation(parent, Native.HANDLE_FLAG_INHERIT, 0))
        {
            var error = Marshal.GetLastWin32Error();
            read.Dispose();
            write.Dispose();
            throw new Win32Exception(error);
        }

        return (read, write);
    }

    private sealed class RunningProcess : IRunningProcess
    {
        private static readonly TimeSpan DrainLimit = TimeSpan.FromSeconds(2); // IDE-P3-084
        private static readonly TimeSpan KillLimit = TimeSpan.FromSeconds(3); // IDE-P3-090

        private readonly SafeJobHandle _job;
        private readonly SafeProcessHandle _process;
        private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _rootExited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task[] _readers;
        private readonly ManualResetEvent _wait;
        private readonly RegisteredWaitHandle _registration;

        public RunningProcess(IClock clock, SafeJobHandle job, SafeProcessHandle process, int processId, SafeFileHandle? stdout,
            SafeFileHandle? stderr, Action<ProcessOutputLine> onOutput)
        {
            _job = job;
            _process = process;
            ProcessId = processId;
            _readers =
            [
                .. new[] { (stdout, false), (stderr, true) }
                    .Where(p => p.Item1 is not null)
                    .Select(p => ReadAsync(clock, p.Item1!, p.Item2, onOutput)),
            ];
            _wait = new ManualResetEvent(false) { SafeWaitHandle = new SafeWaitHandle(process.DangerousGetHandle(), ownsHandle: false) };
            _registration = ThreadPool.RegisterWaitForSingleObject(_wait, (_, _) => _ = OnRootExitedAsync(), null, Timeout.Infinite, executeOnlyOnce: true);
        }

        public int ProcessId { get; }

        public Task<int> Exited => _exited.Task;

        public async Task<bool> KillTreeAsync()
        {
            try
            {
                if (!_rootExited.Task.IsCompleted && !Native.TerminateJobObject(_job, 1))
                    return false;
            }
            catch (ObjectDisposedException)
            {
                // The job was closed: the root exited (or the process was disposed) and the tree is gone.
            }

            try
            {
                await _rootExited.Task.WaitAsync(KillLimit).ConfigureAwait(false);
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
        }

        public void Dispose()
        {
            _registration.Unregister(null);
            _job.Dispose(); // KILL_ON_JOB_CLOSE: anything still running ends now
            _wait.Dispose();
            _process.Dispose();
        }

        private static Task ReadAsync(IClock clock, SafeFileHandle handle, bool isError, Action<ProcessOutputLine> onOutput) =>
            Task.Factory.StartNew(() =>
            {
                var splitter = new OutputLineSplitter(clock, line => onOutput(new ProcessOutputLine(line, isError)));
                using var stream = new FileStream(handle, FileAccess.Read, 4096, isAsync: false);
                using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false);
                var buffer = new char[4096];
                try
                {
                    int count;
                    while ((count = reader.Read(buffer, 0, buffer.Length)) > 0)
                        splitter.Write(new string(buffer, 0, count));
                }
                catch (IOException)
                {
                    // The pipe broke: what arrived so far is kept.
                }
                catch (ObjectDisposedException)
                {
                }

                splitter.Complete();
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        private async Task OnRootExitedAsync()
        {
            int code;
            try
            {
                code = Native.GetExitCodeProcess(_process, out var exitCode) ? unchecked((int)exitCode) : -1;
            }
            catch (ObjectDisposedException)
            {
                code = -1; // disposed while the wait fired
            }

            _rootExited.TrySetResult();
            try
            {
                await Task.WhenAll(_readers).WaitAsync(DrainLimit).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // A descendant still holds a pipe; closing the job below ends it.
            }

            _job.Dispose(); // IDE-P3-084: descendants still alive end with the job
            _exited.TrySetResult(code);
        }
    }

    internal sealed class SafeJobHandle() : SafeHandleZeroOrMinusOneIsInvalid(ownsHandle: true)
    {
        protected override bool ReleaseHandle() => Native.CloseHandle(handle);
    }

    private static class Native
    {
        public const uint CREATE_SUSPENDED = 0x00000004;
        public const uint CREATE_NEW_CONSOLE = 0x00000010;
        public const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
        public const uint CREATE_NO_WINDOW = 0x08000000;
        public const int STARTF_USESTDHANDLES = 0x00000100;
        public const uint HANDLE_FLAG_INHERIT = 0x00000001;
        public const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
        public const int JobObjectExtendedLimitInformation = 9;

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

        [StructLayout(LayoutKind.Sequential)]
        public struct SECURITY_ATTRIBUTES
        {
            public int nLength;
            public IntPtr lpSecurityDescriptor;
            public int bInheritHandle;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JOBOBJECT_BASIC_LIMIT_INFORMATION
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
        public struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern SafeJobHandle CreateJobObjectW(IntPtr jobAttributes, string? name);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetInformationJobObject(SafeJobHandle job, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, int length);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AssignProcessToJobObject(SafeJobHandle job, SafeProcessHandle process);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TerminateJobObject(SafeJobHandle job, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreateProcessW(string? applicationName, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint creationFlags, IntPtr environment, string? currentDirectory,
            ref STARTUPINFOW startupInfo, out PROCESS_INFORMATION processInformation);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        public static extern uint ResumeThread(SafeProcessHandle thread);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreatePipe(out SafeFileHandle readPipe, out SafeFileHandle writePipe, ref SECURITY_ATTRIBUTES attributes, int size);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetHandleInformation(SafeHandle handle, uint mask, uint flags);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);
    }
}
