using System.Runtime.CompilerServices;

namespace FreexComp.Ide.Tests.Infrastructure;

/// <summary>
/// I-263: the thread pool of the test process. The headless session runs its loop on a pool thread for the whole run,
/// and the test framework blocks one more pool thread per running UI test while the test's body runs on the session;
/// tests run in as many collections at once as there are processors. With the runtime's default minimum (one worker
/// per processor) the first tests of a run block every worker, and the pool then adds workers at its starvation rate —
/// on a machine whose processors are all busy, one after about a second per existing worker. Everything that needs a
/// worker waits meanwhile: the continuation of an <c>await</c>, the exit notification of a child process, the test
/// framework's own timeout. Below <see cref="WorkerMinimum"/> the pool creates a worker as soon as one is needed.
/// </summary>
internal static class TestThreadPool
{
    /// <summary>The workers the pool creates on demand, without its starvation delay.</summary>
    public static int WorkerMinimum => 4 * Environment.ProcessorCount;

    /// <summary>Runs before any code of the test assembly, so before the first test and the headless session.</summary>
    [ModuleInitializer]
    internal static void RaiseWorkerMinimum()
    {
        ThreadPool.GetMinThreads(out var workers, out var completionPorts);
        if (workers < WorkerMinimum)
            ThreadPool.SetMinThreads(WorkerMinimum, completionPorts);
    }
}
