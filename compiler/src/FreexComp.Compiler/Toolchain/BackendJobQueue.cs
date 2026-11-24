using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Toolchain;

/// <summary>
/// One back-end job (CMP-1134): one external process that turns one input into one output. Exactly one of
/// <paramref name="Unit"/> (compile job, <c>.ll</c> → <c>.obj</c>) and <paramref name="Dll"/> (import-library job, <c>.def</c> →
/// <c>.lib</c>) is set.
/// </summary>
internal sealed record BackendJob(string? Unit, string? Dll, string ToolPath, IReadOnlyList<string> Arguments, string WorkingDirectory, string OutputPath);

internal enum BackendJobState
{
    /// <summary>Never started (cancellation came first).</summary>
    NotStarted,

    /// <summary>Started, and its process was killed by the cancellation.</summary>
    Unfinished,

    /// <summary>The process exited with code 0: the output is complete.</summary>
    Succeeded,

    /// <summary>The process failed or timed out (E4004 / E4003); the output was deleted.</summary>
    Failed,
}

/// <summary>Where the released buffers of the jobs go (CMP-1138).</summary>
internal sealed record BackendJobSinks
{
    /// <summary><c>Compiling &lt;Unit&gt;</c> of a compile job (CMP-802).</summary>
    public Action<string>? Info { get; init; }

    /// <summary>The command line of a job with its unit or DLL (only set with <c>--verbose</c>).</summary>
    public Action<string, BackendJob>? CommandLine { get; init; }

    /// <summary>Raw tool stdout/stderr lines.</summary>
    public Action<string>? ToolOutput { get; init; }

    /// <summary>The failure diagnostic of a job (E4003 / E4004).</summary>
    public required Action<Diagnostic> Diagnostic { get; init; }
}

/// <summary>
/// The back-end job queue (parallel-backend.md CMP-1134…1142). Jobs are given in job-key order (CMP-1135) and started in that
/// order whenever fewer than the limit are running (CMP-1136), each through <see cref="ToolRunner"/> with its own timeout
/// counted from the start of its process (CMP-1137). Everything a job reports is buffered and released in key order, when the
/// job and every job before it have finished (CMP-1138), so the sinks see the same sequence for every limit and every
/// completion order. A failing job does not stop the others; its output file is deleted (CMP-1140). On cancellation no further
/// job starts, the running processes are killed and awaited, and the output of every started job that did not finish is deleted
/// (CMP-1142); the caller learns each job's state and decides what to keep.
/// </summary>
internal sealed class BackendJobQueue(IProcessRunner processRunner, IFileSystem fs, ToolRunOptions toolOptions, BackendJobSinks sinks, int limit)
{
    /// <returns>The state of each job, in the order of <paramref name="jobs"/>. Never throws for a cancellation.</returns>
    public async Task<BackendJobState[]> RunAsync(IReadOnlyList<BackendJob> jobs, CancellationToken ct)
    {
        var states = new BackendJobState[jobs.Count];
        var buffers = new List<Action>?[jobs.Count];
        var released = 0;
        var release = new Lock();
        using var slots = new SemaphoreSlim(Math.Max(1, limit));
        var running = new List<Task>(jobs.Count);

        // the dispatcher: one job after another in key order, each as soon as a slot is free; a job runs synchronously up to the
        // start of its process, so processes also start in key order
        for (var i = 0; i < jobs.Count && !ct.IsCancellationRequested; i++)
        {
            try
            {
                await slots.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (ct.IsCancellationRequested)
            {
                slots.Release();
                break;
            }

            running.Add(RunJobAsync(i));
        }

        await Task.WhenAll(running).ConfigureAwait(false);
        if (ct.IsCancellationRequested)
        {
            for (var i = 0; i < jobs.Count; i++)
            {
                if (states[i] == BackendJobState.Unfinished)
                {
                    DeleteOutput(jobs[i]);
                }
            }
        }

        return states;

        async Task RunJobAsync(int index)
        {
            var job = jobs[index];
            states[index] = BackendJobState.Unfinished;
            var buffer = new List<Action>();
            void Buffer(Action report)
            {
                lock (buffer)
                {
                    buffer.Add(report);
                }
            }

            if (job.Unit is { } unit && sinks.Info is { } info)
            {
                Buffer(() => info("Compiling " + unit));
            }

            var runner = new ToolRunner(processRunner, toolOptions with
            {
                CommandLineSink = sinks.CommandLine is { } command ? line => Buffer(() => command(line, job)) : null,
                ToolOutputSink = sinks.ToolOutput is { } output ? line => Buffer(() => output(line)) : null,
            });
            try
            {
                var result = await runner.RunAsync(job.ToolPath, job.Arguments, job.WorkingDirectory, ct).ConfigureAwait(false);
                if (result.Failure is { } failure)
                {
                    DeleteOutput(job);                                   // CMP-1140: no output of a failed job stays
                    Buffer(() => sinks.Diagnostic(failure));
                    states[index] = BackendJobState.Failed;
                }
                else
                {
                    states[index] = BackendJobState.Succeeded;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;                                                  // unfinished: its buffer is discarded (CMP-1142)
            }
            finally
            {
                slots.Release();
            }

            // CMP-1138: hand over this buffer and every later finished one, as far as the unbroken prefix reaches
            lock (release)
            {
                buffers[index] = buffer;
                for (; released < buffers.Length && buffers[released] is { } next; released++)
                {
                    foreach (var report in next)
                    {
                        report();
                    }
                }
            }
        }
    }

    /// <summary>Best effort: a file that cannot be deleted now is replaced by the next build (its <c>.pcu</c> / hash is gone).</summary>
    private void DeleteOutput(BackendJob job)
    {
        try
        {
            fs.Delete(job.OutputPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // still held by something else: nothing more can be done for it here
        }
    }
}
