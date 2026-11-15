using System.Collections.Immutable;
using FreexComp.Build;
using FreexComp.Compiler;
using FreexComp.Compiler.Text;

namespace FreexComp.Ide.TestSupport;

/// <summary>
/// IDE-P3-031: a scripted <see cref="IBuildEngine"/>. Each <see cref="RunAsync"/> takes the next <see cref="FakeBuild"/>
/// of the queue (none queued → a successful build of the requested project), reports its events synchronously on the
/// calling thread, then returns its result — at once, or when the test calls <see cref="FakeBuild.Release"/>. Every
/// request is recorded in <see cref="Requests"/>.
/// </summary>
public sealed class FakeBuildEngine : IBuildEngine
{
    private readonly Queue<FakeBuild> _queue = new();
    private readonly List<BuildRequest> _requests = [];
    private readonly Lock _gate = new();

    public IReadOnlyList<BuildRequest> Requests
    {
        get
        {
            lock (_gate)
                return [.. _requests];
        }
    }

    /// <summary>The build currently inside <see cref="RunAsync"/> (held or not); null before the first run.</summary>
    public FakeBuild? Current { get; private set; }

    /// <summary>Output path of the default successful build; null = <c>&lt;project dir&gt;\&lt;name&gt;.exe</c>.</summary>
    public string? DefaultOutputPath { get; set; }

    /// <summary>Queues the next run.</summary>
    public FakeBuild Enqueue(FakeBuild build)
    {
        ArgumentNullException.ThrowIfNull(build);
        lock (_gate)
            _queue.Enqueue(build);
        return build;
    }

    public async Task<BuildResult> RunAsync(BuildRequest request, IProgress<BuildEvent> progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(progress);
        FakeBuild build;
        lock (_gate)
        {
            _requests.Add(request);
            build = _queue.Count > 0 ? _queue.Dequeue() : Success(request.ProjectPath, DefaultOutputPath ?? DefaultExe(request.ProjectPath));
        }

        Current = build;
        build.Request = request;
        build.Token = ct;
        foreach (var e in build.Events(request))
            progress.Report(e);
        if (build.Throw is { } exception)
            throw exception;
        if (build.IsHeld)
        {
            build.SignalReached();
            if (build.HonorsCancel)
            {
                using (ct.Register(() => build.Release()))
                    await build.Released.ConfigureAwait(false);
            }
            else
            {
                await build.Released.ConfigureAwait(false);
            }

            if (ct.IsCancellationRequested && build.HonorsCancel)
            {
                progress.Report(new BuildEvent.Diagnostic(DateTime.UtcNow, request.ProjectPath,
                    new Diagnostic("E9010", DiagnosticSeverity.Error, "Build canceled", Location.ForFile(request.ProjectPath), [])));
                progress.Report(new BuildEvent.Finished(DateTime.UtcNow, request.ProjectPath, false, false, 1, 0, 0, TimeSpan.Zero));
                return Result(request.ProjectPath, success: false, canceled: true, errors: 1);
            }

            if (build.Bracket)
                progress.Report(build.FinishedEvent(request));
        }

        return build.Result;
    }

    /// <summary><c>&lt;project dir&gt;\&lt;project name&gt;.exe</c>.</summary>
    public static string DefaultExe(string projectPath) => Path.ChangeExtension(projectPath, ".exe");

    /// <summary>A successful build with the given extra events between Started and Finished.</summary>
    public static FakeBuild Success(string projectPath, string? outputPath, params BuildEvent[] events) =>
        new(events, Result(projectPath, success: true, outputPath: outputPath, warnings: events.Count(IsWarning)));

    /// <summary>A failed build reporting <paramref name="diagnostics"/>; the error count is that of the error diagnostics.</summary>
    public static FakeBuild Failure(string projectPath, params Diagnostic[] diagnostics) =>
        new([.. diagnostics.Select(d => (BuildEvent)new BuildEvent.Diagnostic(DateTime.UtcNow, projectPath, d))],
            Result(projectPath, success: false, errors: diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error),
                warnings: diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning)));

    public static BuildResult Result(string projectPath, bool success, string? outputPath = null, bool canceled = false, int errors = 0,
        int warnings = 0, int hints = 0, bool upToDate = false, TimeSpan? elapsed = null) =>
        new(success, canceled, errors, warnings, hints,
            [new ProjectBuildResult(projectPath, "Debug", success, upToDate, success ? outputPath : null, [])], null,
            elapsed ?? TimeSpan.FromMilliseconds(1234));

    /// <summary>A diagnostic at a 1-based position (0 = no position).</summary>
    public static Diagnostic Diag(string code, DiagnosticSeverity severity, string message, string path, int line = 0, int column = 0,
        int length = 0)
    {
        var start = line == 0 ? new LinePosition(-1, -1) : new LinePosition(line - 1, column - 1);
        var end = line == 0 ? start : new LinePosition(line - 1, column - 1 + length);
        var location = line == 0 ? Location.ForFile(path) : new Location(path, default, new LinePositionSpan(start, end));
        return new Diagnostic(code, severity, message, location, []);
    }

    private static bool IsWarning(BuildEvent e) => e is BuildEvent.Diagnostic { Value.Severity: DiagnosticSeverity.Warning };
}

/// <summary>One scripted run of <see cref="FakeBuildEngine"/>.</summary>
public sealed class FakeBuild(IReadOnlyList<BuildEvent> middle, BuildResult result)
{
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public BuildResult Result { get; set; } = result;

    /// <summary>Thrown by <c>RunAsync</c> after the events (IDE-P3-037).</summary>
    public Exception? Throw { get; set; }

    /// <summary>True: <c>RunAsync</c> returns only after <see cref="Release"/> (or, with <see cref="HonorsCancel"/>, cancellation).</summary>
    public bool IsHeld { get; set; }

    /// <summary>False: a held build ignores cancellation (a hung engine, IDE-P3-051).</summary>
    public bool HonorsCancel { get; set; } = true;

    /// <summary>False: no Started/Finished bracket around the events.</summary>
    public bool Bracket { get; set; } = true;

    public BuildRequest? Request { get; internal set; }

    public CancellationToken Token { get; internal set; }

    private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Task Released => _released.Task;

    /// <summary>Completes when a held build has reported its events and waits for <see cref="Release"/>.</summary>
    public Task Reached => _reached.Task;

    internal void SignalReached() => _reached.TrySetResult();

    public FakeBuild Held(bool honorsCancel = true)
    {
        IsHeld = true;
        HonorsCancel = honorsCancel;
        return this;
    }

    public void Release() => _released.TrySetResult();

    internal IEnumerable<BuildEvent> Events(BuildRequest request)
    {
        var now = DateTime.UtcNow;
        if (Bracket)
            yield return new BuildEvent.Started(now, request.ProjectPath, request.Configuration ?? "Debug", request.Platform ?? "win64", request.Target);
        foreach (var e in middle)
            yield return e;
        if (Bracket && !IsHeld && Throw is null)
            yield return FinishedEvent(request);
    }

    internal BuildEvent.Finished FinishedEvent(BuildRequest request) =>
        new(DateTime.UtcNow, request.ProjectPath, Result.Success, Result.UpToDate, Result.Errors, Result.Warnings, Result.Hints, Result.Elapsed);
}
