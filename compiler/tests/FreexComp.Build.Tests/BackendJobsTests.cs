using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using FreexComp.Compiler;
using FreexComp.PBuild;

namespace FreexComp.Build.Tests;

/// <summary>
/// F01-010, C-273…C-275: the back-end job queue (parallel-backend.md CMP-1134…1144) through the build engine with a fake
/// process runner (<see cref="JobRunner"/>; no toolchain). Nothing here measures wall-clock time: concurrency is proved with
/// gates and counters, completion order is scripted, timeouts run on a manual clock. The only real-time value is
/// <see cref="SafetyNet"/>, which ends a test that would otherwise hang.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Unit")]
public sealed partial class BackendJobsTests
{
    private static readonly TimeSpan SafetyNet = TimeSpan.FromMinutes(2);

    private static async Task<(BuildResult Result, EventLog Log)> RunAsync(
        BuildWorkspace w, JobRunner runner, int jobs, bool verbose = false, CancellationToken? ct = null, TimeProvider? clock = null)
    {
        var log = new EventLog();
        var engine = new BuildEngine(w.Fs, runner) { ToolTimeProvider = clock };
        var result = await engine
            .RunAsync(w.Request() with { MaxParallelism = jobs, Verbose = verbose }, log, ct ?? TestContext.Current.CancellationToken)
            .WaitAsync(SafetyNet, TestContext.Current.CancellationToken);
        return (result, log);
    }

    /// <summary>Every event without its time stamps, with the full command line and its unit / DLL.</summary>
    private static ImmutableArray<string> Describe(EventLog log) =>
    [
        .. log.Events.Select(e => e switch
        {
            BuildEvent.Started s => $"started {s.Configuration} {s.Platform} {s.Target}",
            BuildEvent.Diagnostic d => "diagnostic " + d.Value.ToText() + string.Concat(d.Value.Related.Select(r => "\n  " + r.Message)),
            BuildEvent.CommandLine c => $"command unit={c.Unit ?? "-"} dll={c.Dll ?? "-"} {c.Text}",
            BuildEvent.OutputLine o => $"{o.Source} {o.Text}",
            BuildEvent.Finished f => $"finished success={f.Success} upToDate={f.UpToDate} {f.Errors}/{f.Warnings}/{f.Hints}",
            _ => e.ToString(),
        }),
    ];

    // ------------------------------------------------------------------ scheduling (CMP-1134…1137)

    /// <summary>
    /// CMP-1136: 16 stale units; the gate lets compile processes through only when <c>J</c> of them wait in it, so the peak is
    /// exactly <c>J</c> (a lower limit would never open the gate, a higher one would raise the peak), and never more than
    /// <c>J</c> processes of any kind exist. With <c>J = 1</c> the processes start one after another in job-key order.
    /// </summary>
    [Theory]
    [Trait("Req", "CMP-1136")]
    [InlineData(8)]
    [InlineData(4)]
    [InlineData(1)]
    public async Task PeakConcurrencyEqualsJobLimit(int jobs)
    {
        var w = JobFixture.Workspace(units: 14);
        var runner = new JobRunner(w.Tools);
        var gate = new BatchGate(runner, size: jobs, total: 16, tool: "clang.exe");

        var (result, log) = await RunAsync(w, runner, jobs);

        Assert.True(result.Success, string.Join("\n", log.Lines));
        Assert.Equal(jobs, gate.PeakWaiting);
        Assert.Equal(jobs, runner.Peak("clang.exe"));
        Assert.Equal(jobs, runner.PeakAll);
        Assert.Equal(16 / jobs, gate.Openings);
        Assert.Equal([.. JobFixture.CompileJobs(14), .. JobFixture.DefaultDlls], runner.Started);       // CMP-1135, CMP-1136: key order
        if (jobs == 1)
        {
            Assert.Equal(runner.Started, runner.Ended);                                               // strictly one after another
        }
    }

    /// <summary>
    /// CMP-1134, CMP-1135: compile and import-library jobs are one queue under one limit — compile jobs first (unit
    /// initialisation order, the program last), then the import libraries by DLL name; the gate (all 12 jobs, batches of
    /// <c>J</c>) shows that never more than <c>J</c> processes exist, whatever their kind.
    /// </summary>
    [Theory]
    [Trait("Req", "CMP-1134")]
    [Trait("Req", "CMP-1135")]
    [InlineData(4)]
    [InlineData(3)]
    [InlineData(1)]
    public async Task ImportLibraryJobsShareTheLimit(int jobs)
    {
        var w = JobFixture.Workspace(units: 6, dlls: true);                         // 8 compile jobs + 4 import libraries
        var runner = new JobRunner(w.Tools);
        var gate = new BatchGate(runner, size: jobs, total: 12);

        var (result, log) = await RunAsync(w, runner, jobs);

        Assert.True(result.Success, string.Join("\n", log.Lines));
        Assert.Equal([.. JobFixture.CompileJobs(6), .. JobFixture.DllsWithExternals], runner.Started);
        Assert.Equal(jobs, gate.PeakWaiting);
        Assert.Equal(jobs, runner.PeakAll);
        Assert.Equal(Math.Min(jobs, 4), runner.Peak("llvm-dlltool.exe"));
        Assert.Equal("lld-link.exe", w.Tools.Calls[^1]);                            // the link is not a job: alone, after all of them
        Assert.All(JobFixture.DllsWithExternals, dll => Assert.True(w.Fs.FileExists(JobFixture.Lib(dll) + ".sha256")));
    }

    /// <summary>
    /// CMP-1137: the 120 s timeout of a job counts from the start of its process. On a manual clock with <c>J = 1</c>: the first
    /// job runs for 100 s; the second, queued behind it all that time, then runs for another 100 s (200 s after it was queued)
    /// and must not time out; the third is held for 121 s and does (E4003), which shows the clock drives the timeouts.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1137")]
    public async Task TimeoutCountsFromProcessStart()
    {
        var ct = TestContext.Current.CancellationToken;
        var w = JobFixture.Workspace(units: 1);                                     // System, U01, Hello
        var runner = new JobRunner(w.Tools);
        var clock = new ManualTimeProvider();
        var started = JobFixture.CompileJobs(1).ToDictionary(n => n, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        var release = JobFixture.CompileJobs(1).ToDictionary(n => n, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        runner.Hold = async (job, token) =>
        {
            if (job.Tool == "clang.exe")
            {
                started[job.Name].TrySetResult();
                await release[job.Name].Task.WaitAsync(token);
            }
        };

        var build = RunAsync(w, runner, jobs: 1, clock: clock);
        await started["System"].Task.WaitAsync(SafetyNet, ct);
        clock.Advance(TimeSpan.FromSeconds(100));
        Assert.False(started["U01"].Task.IsCompleted);                              // queued, no process yet
        release["System"].TrySetResult();
        await started["U01"].Task.WaitAsync(SafetyNet, ct);
        clock.Advance(TimeSpan.FromSeconds(100));
        release["U01"].TrySetResult();
        await started["Hello"].Task.WaitAsync(SafetyNet, ct);
        clock.Advance(TimeSpan.FromSeconds(121));
        var (result, log) = await build;

        Assert.False(result.Success);
        var timeout = Assert.Single(log.Events.OfType<BuildEvent.Diagnostic>()).Value;
        Assert.Equal("E4003", timeout.Code);
        Assert.Contains("120", timeout.Message, StringComparison.Ordinal);
        Assert.True(w.Fs.FileExists(JobFixture.Obj("System")) && w.Fs.FileExists(JobFixture.Pcu("System")));
        Assert.True(w.Fs.FileExists(JobFixture.Obj("U01")) && w.Fs.FileExists(JobFixture.Pcu("U01")), string.Join("\n", log.Lines));
        Assert.False(w.Fs.FileExists(JobFixture.Obj("Hello")));
        Assert.DoesNotContain("lld-link.exe", w.Tools.Calls);
    }

    // ------------------------------------------------------------------ reporting (CMP-1138, CMP-1139)

    private static readonly ImmutableArray<string> EightJobs = [.. JobFixture.CompileJobs(2), .. JobFixture.DllsWithExternals];

    /// <summary>Completion orders of the eight jobs that all run at the same time with <c>J = 8</c>; null = no script (<c>J = 1</c>).</summary>
    private static readonly ImmutableArray<(string Name, int Jobs, ImmutableArray<string>? Completion)> Schedules =
    [
        ("J=1", 1, null),
        ("J=8 key order", 8, EightJobs),
        ("J=8 reversed", 8, [.. EightJobs.Reverse()]),
        ("J=8 shuffled", 8, [EightJobs[5], EightJobs[2], EightJobs[7], EightJobs[0], EightJobs[3], EightJobs[6], EightJobs[1], EightJobs[4]]),
    ];

    /// <summary>Four compile jobs and four import libraries; every job prints two lines; <paramref name="failing"/> jobs exit with 3.</summary>
    private static (BuildWorkspace Workspace, JobRunner Runner) ReportingFixture(string[] failing, ImmutableArray<string>? completion)
    {
        var w = JobFixture.Workspace(units: 2, dlls: true);
        var runner = new JobRunner(w.Tools)
        {
            Script = job => ([job.Name + ": first line", "err:" + job.Name + ": second line"], failing.Contains(job.Name) ? 3 : 0),
        };
        if (completion is { } order)
        {
            _ = new ScriptedCompletion(runner, order);
        }

        return (w, runner);
    }

    /// <summary>
    /// CMP-1138, CMP-1139: the event sequence, and the stdout of <c>pbuild</c> in text and JSON form, are the same for
    /// <c>J = 1</c> and for <c>J = 8</c> with the processes ending in key order, in reverse and shuffled — with and without
    /// <c>--verbose</c>, with every job succeeding and with a compile job and an import-library job failing.
    /// </summary>
    [Theory]
    [Trait("Req", "CMP-1138")]
    [Trait("Req", "CMP-1139")]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ReportingIdenticalForAnyJobLimitAndCompletionOrder(bool verbose, bool failures)
    {
        string[] failing = failures ? ["U02", "kernel32.dll"] : [];
        var sequences = new List<(string Schedule, ImmutableArray<string> Events, string Text, string Json)>();
        foreach (var (name, jobs, completion) in Schedules)
        {
            var (w, runner) = ReportingFixture(failing, completion);
            var (_, log) = await RunAsync(w, runner, jobs, verbose);
            if (completion is { } order)
            {
                Assert.Equal(order, runner.Ended);                                 // the script was followed
                Assert.Equal(8, runner.PeakAll);
            }

            sequences.Add((name, Describe(log), Pbuild(failing, jobs, completion, verbose, json: false), Pbuild(failing, jobs, completion, verbose, json: true)));
        }

        var serial = sequences[0];
        Assert.All(sequences, s => Assert.True(serial.Events.SequenceEqual(s.Events), $"{s.Schedule}:\n{string.Join("\n", s.Events)}\n--- J=1:\n{string.Join("\n", serial.Events)}"));
        Assert.All(sequences, s => Assert.Equal(serial.Text, s.Text));
        Assert.All(sequences, s => Assert.Equal(serial.Json, s.Json));

        // the released buffers: contiguous, in job-key order, each with Info line, command line (verbose), tool output, failure
        var events = serial.Events;
        var expected = new List<string> { "started Debug win64 Build" };
        foreach (var job in EightJobs)
        {
            var dll = job.EndsWith(".dll", StringComparison.Ordinal);
            if (!dll)
            {
                expected.Add("Info Compiling " + job);
            }

            if (verbose)
            {
                var command = events.Single(e => e.StartsWith(dll ? $"command unit=- dll={job} " : $"command unit={job} dll=- ", StringComparison.Ordinal));   // exactly once
                Assert.Contains(dll ? "llvm-dlltool.exe" : job + ".ll", command, StringComparison.Ordinal);
                expected.Add(command);
            }

            expected.Add($"Tool {job}: first line");
            expected.Add($"Tool err:{job}: second line");
        }

        Assert.Equal(expected, events[..expected.Count]);
        var rest = events[expected.Count..];
        if (failures)
        {
            // CMP-1140: both failures, in key order, and no link
            Assert.Equal(3, rest.Length);
            Assert.StartsWith(@"diagnostic C:\p\Hello.pas(0,0): error E4004: clang.exe failed with exit code 3" + "\n  U02: first line", rest[0], StringComparison.Ordinal);
            Assert.StartsWith(@"diagnostic C:\p\Hello.pas(0,0): error E4004: llvm-dlltool.exe failed with exit code 3" + "\n  kernel32.dll: first line", rest[1], StringComparison.Ordinal);
            Assert.Equal("finished success=False upToDate=False 2/0/0", rest[2]);
        }
        else
        {
            Assert.Equal("Info Linking Hello.exe", rest[0]);
            Assert.Equal("Tool fake lld-link done", rest[^2]);
            Assert.Equal("finished success=True upToDate=False 0/0/0", rest[^1]);
            Assert.Equal(verbose ? 4 : 3, rest.Length);                              // the link's command line is not a job's
            Assert.All(rest.Where(e => e.StartsWith("command ", StringComparison.Ordinal)), e => Assert.StartsWith("command unit=- dll=- ", e, StringComparison.Ordinal));
        }

        // CMP-1139: JSON command-line events of jobs carry "unit" or "dll"; text mode is the bare command after "> "
        var jsonCommands = serial.Json.Split('\n').Where(l => l.StartsWith("{\"event\":\"commandLine\"", StringComparison.Ordinal)).ToList();
        var textCommands = serial.Text.Split('\n').Where(l => l.StartsWith("> ", StringComparison.Ordinal)).ToList();
        if (verbose)
        {
            Assert.Equal(failures ? 8 : 9, jsonCommands.Count);
            Assert.Equal(jsonCommands.Count, textCommands.Count);
            Assert.All(EightJobs.Where(j => !j.EndsWith(".dll", StringComparison.Ordinal)), unit => Assert.Single(jsonCommands, l => l.EndsWith($",\"unit\":\"{unit}\"}}", StringComparison.Ordinal)));
            Assert.All(JobFixture.DllsWithExternals, dll => Assert.Single(jsonCommands, l => l.EndsWith($",\"dll\":\"{dll}\"}}", StringComparison.Ordinal)));
            if (!failures)
            {
                Assert.DoesNotContain("\"unit\"", jsonCommands[^1], StringComparison.Ordinal);       // the link
                Assert.DoesNotContain("\"dll\"", jsonCommands[^1], StringComparison.Ordinal);
            }
        }
        else
        {
            Assert.Empty(jsonCommands);
            Assert.Empty(textCommands);
        }
    }

    /// <summary>The stdout of one <c>pbuild</c> run of the reporting fixture, without the elapsed times.</summary>
    private static string Pbuild(string[] failing, int jobs, ImmutableArray<string>? completion, bool verbose, bool json)
    {
        var (w, runner) = ReportingFixture(failing, completion);
        var stdout = new StringWriter { NewLine = "\n" };
        var env = new PbuildEnvironment(@"C:\p", stdout, new StringWriter(), w.Fs)
        {
            ProcessRunner = runner,
            ToolchainDirectory = BuildWorkspace.Llvm,
            RtlDirectory = BuildWorkspace.Rtl,
        };
        List<string> args = ["Hello.pproj", "--jobs", jobs.ToString(System.Globalization.CultureInfo.InvariantCulture)];
        if (verbose)
        {
            args.Add("--verbose");
        }

        if (json)
        {
            args.AddRange(["--diagnostics", "json"]);
        }

        var code = PbuildCommand.Run(args, env, TestContext.Current.CancellationToken);

        Assert.Equal(failing.Length == 0 ? 0 : 1, code);
        return ElapsedTime().Replace(stdout.ToString(), "<t>");
    }

    [GeneratedRegex("""(?<="ms":)\d+|\d+\.\d\d(?= s\))""")]
    private static partial Regex ElapsedTime();

    // ------------------------------------------------------------------ failure and cancellation (CMP-1140…1142)

    /// <summary>
    /// CMP-1140: two of six compile jobs fail. Every job still runs (the import libraries too); the four good objects and their
    /// <c>.pcu</c> exist, the failed ones have no <c>.obj</c> and no <c>.pcu</c> but keep their <c>.ll</c> (CMP-603); the two
    /// failures are reported in job-key order although the later one ends first; nothing is linked.
    /// </summary>
    [Theory]
    [Trait("Req", "CMP-1140")]
    [InlineData(1)]
    [InlineData(6)]
    public async Task FailedJobDoesNotStopOthers(int jobs)
    {
        var w = JobFixture.Workspace(units: 4);                                     // System, U01…U04, Hello
        var runner = new JobRunner(w.Tools)
        {
            Script = job => job.Name is "U02" or "U04" ? ([job.Name + ": boom"], 1) : null,
        };
        if (jobs == 6)
        {
            // all six compile processes run at once; they end last-to-first, so U04 fails before U02
            _ = new ScriptedCompletion(runner, [.. JobFixture.CompileJobs(4).Reverse()]);
            var scripted = runner.Hold!;
            runner.Hold = (job, ct) => job.Tool == "clang.exe" ? scripted(job, ct) : Task.CompletedTask;
        }

        var (result, log) = await RunAsync(w, runner, jobs);

        Assert.False(result.Success);
        Assert.False(result.Canceled);
        Assert.Equal([.. JobFixture.CompileJobs(4), .. JobFixture.DefaultDlls], runner.Started);
        foreach (var unit in JobFixture.CompileJobs(4))
        {
            var failed = unit is "U02" or "U04";
            Assert.Equal(!failed, w.Fs.FileExists(JobFixture.Obj(unit)));
            Assert.Equal(!failed, w.Fs.FileExists(JobFixture.Pcu(unit)));
            Assert.True(w.Fs.FileExists(Path.ChangeExtension(JobFixture.Obj(unit), ".ll")));
        }

        var diagnostics = log.Events.OfType<BuildEvent.Diagnostic>().Select(d => d.Value).ToList();
        Assert.Equal(["E4004", "E4004"], diagnostics.Select(d => d.Code));
        Assert.Equal(["U02: boom", "U04: boom"], diagnostics.Select(d => Assert.Single(d.Related).Message));
        Assert.Equal(2, result.Errors);
        Assert.DoesNotContain("lld-link.exe", w.Tools.Calls);
        Assert.False(w.Fs.FileExists(BuildWorkspace.Exe));
        Assert.All(JobFixture.DefaultDlls, dll => Assert.True(w.Fs.FileExists(JobFixture.Lib(dll))));

        // the next build compiles exactly the two failed units again
        var again = new JobRunner(w.Tools);
        var (second, secondLog) = await RunAsync(w, again, jobs);
        Assert.True(second.Success, string.Join("\n", secondLog.Lines));
        Assert.Equal(["U02", "U04"], again.Started);
    }

    /// <summary>
    /// CMP-1141: when the compile process of a changed unit starts, the unit's old <c>.pcu</c> is already gone (the old
    /// <c>.obj</c> is still there: it is what the process is about to replace); the same for an import library and its
    /// recorded hash. Both are written again after the job succeeded.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1141")]
    public async Task StalePcuDeletedBeforeObjectIsRewritten()
    {
        var w = JobFixture.Workspace(units: 2, dlls: true);
        var (first, firstLog) = await RunAsync(w, new JobRunner(w.Tools), jobs: 2);
        Assert.True(first.Success, string.Join("\n", firstLog.Lines));
        var userLib = JobFixture.Lib("user32.dll");
        Assert.True(w.Fs.FileExists(JobFixture.Pcu("U01")) && w.Fs.FileExists(userLib + ".sha256"));

        w.Fs.Add(@"C:\p\U01.pas", JobFixture.UnitSource(1, dlls: true, value: 1));                    // implementation change
        w.Fs.Add(@"C:\rtl\winapi\def\user32.def", w.Fs.ReadAllText(@"C:\rtl\winapi\def\user32.def") + "\n; changed\n");
        var runner = new JobRunner(w.Tools);
        var atStart = new List<string>();
        runner.Hold = (job, _) =>
        {
            lock (atStart)
            {
                atStart.Add(job.Tool == "clang.exe"
                    ? $"{job.Name}: pcu={w.Fs.FileExists(JobFixture.Pcu(job.Name))} obj={w.Fs.FileExists(job.OutputPath)}"
                    : $"{job.Name}: hash={w.Fs.FileExists(job.OutputPath + ".sha256")} lib={w.Fs.FileExists(job.OutputPath)}");
            }

            return Task.CompletedTask;
        };

        var (second, secondLog) = await RunAsync(w, runner, jobs: 2);

        Assert.True(second.Success, string.Join("\n", secondLog.Lines));
        Assert.Equal(["U01: pcu=False obj=True", "user32.dll: hash=False lib=True"], atStart);
        Assert.True(w.Fs.FileExists(JobFixture.Pcu("U01")) && w.Fs.FileExists(userLib + ".sha256"));
    }

    /// <summary>
    /// CMP-1142. Compile jobs, <c>J = 2</c>: <c>System</c> finishes; <c>U01</c> writes half an object and <c>U02</c> an empty one,
    /// then both block; the build is cancelled. No further job starts, the two unfinished objects are gone (no zero-length file
    /// either), the finished job keeps its <c>.obj</c> and gets its <c>.pcu</c>, the result is <c>Canceled</c> with one E9010.
    /// Import-library job, <c>J = 1</c>: every object is done, <c>gdi32</c> finishes, <c>kernel32</c> writes half a library and
    /// blocks; after the cancellation that library is gone and has no hash, everything finished is kept.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1142")]
    public async Task Cancel_LeavesNoPartialOutputs()
    {
        var ct = TestContext.Current.CancellationToken;

        // --- compile jobs
        var w = JobFixture.Workspace(units: 4, dlls: true);
        var runner = new JobRunner(w.Tools);
        using var cts = new CancellationTokenSource();
        var blocked = 0;
        var bothBlocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runner.Hold = async (job, token) =>
        {
            if (job.Name is "U01" or "U02")
            {
                w.Fs.WriteAllBytes(job.OutputPath, job.Name == "U01" ? Encoding.UTF8.GetBytes("half an obj") : []);
                if (Interlocked.Increment(ref blocked) == 2)
                {
                    bothBlocked.TrySetResult();
                }

                await Task.Delay(Timeout.Infinite, token);
            }
        };

        var build = RunAsync(w, runner, jobs: 2, verbose: true, ct: cts.Token);
        await bothBlocked.Task.WaitAsync(SafetyNet, ct);
        Assert.True(w.Fs.FileExists(JobFixture.Obj("U01")) && w.Fs.FileExists(JobFixture.Obj("U02")));
        cts.Cancel();
        var (result, log) = await build;

        Assert.False(result.Success);
        Assert.True(result.Canceled);
        Assert.Equal("E9010", Assert.Single(log.Events.OfType<BuildEvent.Diagnostic>()).Value.Code);
        Assert.Equal(1, result.Errors);
        Assert.Equal(["System", "U01", "U02"], runner.Started);                    // nothing started after the cancellation
        Assert.True(w.Fs.FileExists(JobFixture.Obj("System")) && w.Fs.FileExists(JobFixture.Pcu("System")));
        foreach (var unit in JobFixture.CompileJobs(4).Skip(1))
        {
            Assert.False(w.Fs.FileExists(JobFixture.Obj(unit)), unit);
            Assert.False(w.Fs.FileExists(JobFixture.Pcu(unit)), unit);
        }

        AssertNoPartialFiles(w, objects: ["System"], libraries: []);
        // the unbroken prefix of finished buffers was released: System's, and nothing of the killed jobs
        Assert.Equal(["Info Compiling System"], Describe(log).Where(e => e.StartsWith("Info ", StringComparison.Ordinal)));
        Assert.Single(log.Events.OfType<BuildEvent.CommandLine>(), c => c.Unit == "System");
        Assert.DoesNotContain(log.Events.OfType<BuildEvent.CommandLine>(), c => c.Unit is "U01" or "U02");

        // the next build recovers: it compiles everything but System
        var recover = new JobRunner(w.Tools);
        var (recovered, recoveredLog) = await RunAsync(w, recover, jobs: 2);
        Assert.True(recovered.Success, string.Join("\n", recoveredLog.Lines));
        Assert.Equal([.. JobFixture.CompileJobs(4).Skip(1), .. JobFixture.DllsWithExternals], recover.Started);

        // --- an import-library job
        var w2 = JobFixture.Workspace(units: 2, dlls: true);
        var runner2 = new JobRunner(w2.Tools);
        using var cts2 = new CancellationTokenSource();
        var libraryBlocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runner2.Hold = async (job, token) =>
        {
            if (job.Name == "kernel32.dll")
            {
                w2.Fs.WriteAllBytes(job.OutputPath, Encoding.UTF8.GetBytes("half a lib"));
                libraryBlocked.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
            }
        };

        var build2 = RunAsync(w2, runner2, jobs: 1, ct: cts2.Token);
        await libraryBlocked.Task.WaitAsync(SafetyNet, ct);
        cts2.Cancel();
        var (result2, log2) = await build2;

        Assert.True(result2.Canceled);
        Assert.Equal("E9010", Assert.Single(log2.Events.OfType<BuildEvent.Diagnostic>()).Value.Code);
        Assert.Equal([.. JobFixture.CompileJobs(2), "gdi32.dll", "kernel32.dll"], runner2.Started);
        Assert.All(JobFixture.CompileJobs(2), unit => Assert.True(w2.Fs.FileExists(JobFixture.Obj(unit)) && w2.Fs.FileExists(JobFixture.Pcu(unit)), unit));
        Assert.True(w2.Fs.FileExists(JobFixture.Lib("gdi32.dll")) && w2.Fs.FileExists(JobFixture.Lib("gdi32.dll") + ".sha256"));
        Assert.False(w2.Fs.FileExists(JobFixture.Lib("kernel32.dll")));
        AssertNoPartialFiles(w2, objects: [.. JobFixture.CompileJobs(2)], libraries: ["gdi32.dll"]);
        Assert.DoesNotContain("lld-link.exe", w2.Tools.Calls);
        Assert.False(w2.Fs.FileExists(BuildWorkspace.Exe));
    }

    /// <summary>The object directory holds exactly the outputs of the jobs that exited with code 0, no zero-length file, no hash without its library.</summary>
    private static void AssertNoPartialFiles(BuildWorkspace w, string[] objects, string[] libraries)
    {
        var files = w.Fs.EnumerateFiles(JobFixture.ObjectDirectory, "*").Concat(w.Fs.EnumerateFiles(Path.Combine(JobFixture.ObjectDirectory, "imports"), "*")).ToList();
        Assert.All(files, f => Assert.True(w.Fs.GetLength(f) > 0, "zero-length " + f));
        Assert.Equal(objects.Select(JobFixture.Obj).Order(StringComparer.OrdinalIgnoreCase), files.Where(f => f.EndsWith(".obj", StringComparison.Ordinal)).Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(objects.Select(JobFixture.Pcu).Order(StringComparer.OrdinalIgnoreCase), files.Where(f => f.EndsWith(".pcu", StringComparison.Ordinal)).Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(libraries.Select(JobFixture.Lib).Order(StringComparer.OrdinalIgnoreCase), files.Where(f => f.EndsWith(".lib", StringComparison.Ordinal)).Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(libraries.Select(l => JobFixture.Lib(l) + ".sha256").Order(StringComparer.OrdinalIgnoreCase), files.Where(f => f.EndsWith(".sha256", StringComparison.Ordinal)).Order(StringComparer.OrdinalIgnoreCase));
        Assert.DoesNotContain(files, f => f.EndsWith(".tmp", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ the card's acceptance (C-275)

    /// <summary>
    /// The card's acceptance "16 sleeping jobs finish within budget", on simulated time instead of the wall clock (which a
    /// loaded machine stretches at will): every compile process sleeps 200 ms, and simulated time advances by one sleep exactly
    /// when every process that can run is asleep — the gate opens when <c>J</c> processes wait in it, one opening is one 200 ms
    /// period. 16 stale units at <c>J = 8</c> therefore take 2 periods = 400 ms, inside the 1 s budget; the serial build takes
    /// 16 periods = 3.2 s. <see cref="PeakConcurrencyEqualsJobLimit"/> is the same proof in terms of concurrency.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1136")]
    public async Task SixteenSleepingJobsFinishWithinBudget()
    {
        var sleep = TimeSpan.FromMilliseconds(200);
        var budget = TimeSpan.FromSeconds(1);

        async Task<TimeSpan> SimulatedTimeAsync(int jobs)
        {
            var w = JobFixture.Workspace(units: 14);                                // 16 compile jobs
            var runner = new JobRunner(w.Tools);
            var gate = new BatchGate(runner, size: jobs, total: 16, tool: "clang.exe");
            var (result, log) = await RunAsync(w, runner, jobs);
            Assert.True(result.Success, string.Join("\n", log.Lines));
            Assert.Equal(16, runner.Started.Count(n => !n.EndsWith(".dll", StringComparison.Ordinal)));
            return gate.Openings * sleep;
        }

        var parallel = await SimulatedTimeAsync(8);
        var serial = await SimulatedTimeAsync(1);

        Assert.Equal(TimeSpan.FromMilliseconds(400), parallel);
        Assert.True(parallel < budget, $"{parallel.TotalMilliseconds} ms of simulated time at J = 8; budget {budget.TotalMilliseconds} ms");
        Assert.Equal(TimeSpan.FromMilliseconds(3200), serial);
    }
}
