namespace FreexComp.Build.Tests;

/// <summary>
/// C-269, I-217: a build through <see cref="BuildEngine.RunAsync"/> never blocks a thread-pool thread, so it completes inside
/// the IDE process even when no pool worker is free. The pool is pinned (min = max) and every worker is held by a blocker
/// before the build starts; the tools are fakes that finish at once. No timing is asserted: the deadline is only a safety net
/// against a hang (TESTING.md).
/// </summary>
[Trait("Phase", "3")]
[Trait("Kind", "Unit")]
[Collection(nameof(BuildEngineTests))]
public sealed class BuildEngineTests
{
    private static readonly TimeSpan SafetyNet = TimeSpan.FromMinutes(2);

    // the test is synchronous by design: an await could not resume while every pool worker is blocked
#pragma warning disable xUnit1031
    [Fact]
    public void EmitDoesNotBlockPoolThreads()
    {
        var ct = TestContext.Current.CancellationToken;
        var workspace = new BuildWorkspace(
            "program Hello; uses Greeting; begin Writeln(Text) end.");
        workspace.Fs.Add(@"C:\p\Greeting.pas", "unit Greeting; interface function Text: string; implementation function Text: string; begin Result := 'hi'; end; end.");
        var log = new EventLog();
        ThreadPool.GetMinThreads(out var minWorkers, out var minIo);
        ThreadPool.GetMaxThreads(out var maxWorkers, out var maxIo);
        var workers = Math.Max(Environment.ProcessorCount, minWorkers);
        using var release = new ManualResetEventSlim(false);
        using var allStarted = new CountdownEvent(workers - (Thread.CurrentThread.IsThreadPoolThread ? 1 : 0));
        try
        {
            // pin the pool: no worker is injected beyond `workers`, then hold every one of them
            Assert.True(ThreadPool.SetMaxThreads(workers, maxIo));
            Assert.True(ThreadPool.SetMinThreads(workers, minIo));
            var started = 0;
            for (var i = 0; i < workers; i++)
            {
                ThreadPool.UnsafeQueueUserWorkItem(_ =>
                {
                    if (Interlocked.Increment(ref started) <= allStarted.InitialCount)
                    {
                        allStarted.Signal();
                    }

                    release.WaitHandle.WaitOne();
                }, null);
            }

            Assert.True(allStarted.Wait(SafetyNet, ct), "the blockers did not occupy every pool worker");

            // the whole build, started from a thread that is not a pool worker
            Task<BuildResult>? build = null;
            var thread = new Thread(() => build = new BuildEngine(workspace.Fs, workspace.Tools).RunAsync(workspace.Request(), log, ct))
            {
                IsBackground = true,
                Name = "I-217 RunAsync",
            };
            thread.Start();
            Assert.True(thread.Join(SafetyNet), "RunAsync did not return");
            Assert.True(build!.Wait(SafetyNet, ct), "RunAsync did not complete with every pool worker blocked");
            var result = build.Result;
            Assert.True(result.Success, string.Join("\n", log.Lines));
            Assert.Equal(BuildWorkspace.Exe, Assert.Single(result.Projects).OutputPath);
        }
        finally
        {
            release.Set();
            ThreadPool.SetMaxThreads(maxWorkers, maxIo);
            ThreadPool.SetMinThreads(minWorkers, minIo);
        }

        Assert.True(workspace.Fs.FileExists(BuildWorkspace.Exe));
    }
#pragma warning restore xUnit1031
}

[CollectionDefinition(nameof(BuildEngineTests), DisableParallelization = true)]
public sealed class BuildEngineTestsCollection;
