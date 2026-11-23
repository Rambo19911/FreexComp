using FreexComp.Compiler.Tests.Toolchain;

namespace FreexComp.Compiler.Tests;

/// <summary>
/// C-269: <see cref="Compilation.EmitAsync"/> never blocks a thread-pool thread, so a build inside the IDE process completes even
/// when no pool worker is free. The pool is pinned (min = max) and every worker is held by a blocker before the emit starts; the
/// synchronous <see cref="Compilation.Emit"/> (sync over async: it needs a free worker) cannot make progress until they are
/// released. No timing is asserted: the deadline is only a safety net against a hang (TESTING.md).
/// </summary>
[Trait("Phase", "3")]
[Trait("Kind", "Unit")]
[Collection(nameof(CompilationTests))]
public sealed class CompilationTests
{
    private static readonly TimeSpan SafetyNet = TimeSpan.FromMinutes(2);

    private const string Toolchain = @"C:\c269\llvm\bin";

    private static Compilation Project()
    {
        var fs = new InMemoryFileSystem()
            .AddSystem()
            .Add(@"C:\p\P.pas", "program P; uses A, B; begin Writeln(FA + FB); end.")
            .Add(@"C:\p\A.pas", "unit A; interface function FA: Integer; implementation function FA: Integer; begin Result := 1; end; end.")
            .Add(@"C:\p\B.pas", "unit B; interface function FB: Integer; implementation function FB: Integer; begin Result := 2; end; end.");
        foreach (var tool in new[] { "clang.exe", "lld-link.exe", "llvm-dlltool.exe" })
        {
            fs.Add(Path.Combine(Toolchain, tool), "");
        }

        foreach (var def in Directory.GetFiles(TestRtl.DefinitionDirectory, "*.def"))
        {
            fs.Add(Path.Combine(@"C:\winapi\def", Path.GetFileName(def)), File.ReadAllText(def));
        }

        var options = new CompilationOptions
        {
            MainFile = @"C:\p\P.pas",
            RtlDirectory = @"C:\rtl",
            ObjectDirectory = @"C:\p\obj",
            ToolchainDirectory = Toolchain,
            MaxParallelism = 4,
        };
        return Compilation.Create(options, [], fs);
    }

    /// <summary>Tool processes that finish at once (clang reports the pinned version); no process is started.</summary>
    private static EmitOptions Exe() => new()
    {
        Stage = EmitStage.Exe,
        OutputPath = @"C:\p\P.exe",
        ProcessRunner = new FakeProcessRunner((call, _) => Task.FromResult(call.Arguments is ["--version"]
            ? (0, new[] { "clang version " + FreexComp.Compiler.Toolchain.ToolchainVersionCheck.PinnedVersion }, Array.Empty<string>())
            : (0, Array.Empty<string>(), Array.Empty<string>()))),
    };

    // the test is synchronous by design: an await could not resume while every pool worker is blocked
#pragma warning disable xUnit1031
    [Fact]
    public void EmitAsyncCompletesWithAllPoolWorkersBlocked()
    {
        var ct = TestContext.Current.CancellationToken;
        var newPath = Project();
        var oldPath = Project();
        ThreadPool.GetMinThreads(out var minWorkers, out var minIo);
        ThreadPool.GetMaxThreads(out var maxWorkers, out var maxIo);
        var workers = Math.Max(Environment.ProcessorCount, minWorkers);
        using var release = new ManualResetEventSlim(false);
        using var allStarted = new CountdownEvent(workers - (Thread.CurrentThread.IsThreadPoolThread ? 1 : 0));
        Thread? oldThread = null;
        EmitResult? oldResult = null;
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

            // the old path: synchronous Emit, needs a pool worker
            oldThread = new Thread(() => oldResult = oldPath.Emit(Exe(), ct)) { IsBackground = true, Name = "C-269 Emit" };
            oldThread.Start();

            // the new path, started from a thread that is not a pool worker
            Task<EmitResult>? emit = null;
            var newThread = new Thread(() => emit = newPath.EmitAsync(Exe(), ct)) { IsBackground = true, Name = "C-269 EmitAsync" };
            newThread.Start();
            Assert.True(newThread.Join(SafetyNet), "EmitAsync did not return");
            Assert.True(emit!.Wait(SafetyNet, ct), "EmitAsync did not complete with every pool worker blocked");
            var result = emit.Result;
            Assert.True(result.Success, string.Join("\n", result.Diagnostics));
            Assert.Equal(@"C:\p\P.exe", result.OutputPath);

            // still blocked: the old path cannot have started its work
            Assert.True(oldThread.IsAlive, "Emit completed without a free pool worker");
            Assert.Null(oldResult);
        }
        finally
        {
            release.Set();
            ThreadPool.SetMaxThreads(maxWorkers, maxIo);
            ThreadPool.SetMinThreads(minWorkers, minIo);
        }

        // released: the old path completes as before
        Assert.True(oldThread.Join(SafetyNet), "Emit did not complete after the pool was released");
        Assert.True(oldResult!.Success, string.Join("\n", oldResult.Diagnostics));
    }
}

#pragma warning restore xUnit1031

[CollectionDefinition(nameof(CompilationTests), DisableParallelization = true)]
public sealed class CompilationTestsCollection;
