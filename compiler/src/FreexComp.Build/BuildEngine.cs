using System.Collections.Immutable;
using System.Diagnostics;
using FreexComp.Compiler;
using FreexComp.Compiler.Targets;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Toolchain;

namespace FreexComp.Build;

/// <summary>
/// The build engine of compiler-cli.md §4 and build-engine.md (CMP-800…835): loads the project, maps it to
/// <see cref="CompilationOptions"/> and runs <see cref="Compilation.Emit"/> (which owns the incremental logic, CMP-820), translating
/// its callbacks into <see cref="BuildEvent"/>s. User errors are diagnostics, never exceptions (CMP-804); cancellation returns a
/// result (CMP-803).
/// </summary>
public sealed class BuildEngine : IBuildEngine
{
    private readonly IFileSystem _fs;
    private readonly IProcessRunner? _processRunner;

    /// <param name="processRunner">Injectable for tests; null → real processes.</param>
    public BuildEngine(IFileSystem fs, IProcessRunner? processRunner = null)
    {
        ArgumentNullException.ThrowIfNull(fs);
        _fs = fs;
        _processRunner = processRunner;
    }

    /// <summary>The clock of the tool timeouts (CMP-1137); a test seam, null = the system clock.</summary>
    internal TimeProvider? ToolTimeProvider { get; init; }

    /// <summary>
    /// A test seam (CMP-1004: a descriptor outside the registry cannot be named in a project): maps the registered target of the
    /// effective platform to the descriptor the build uses; null = the registered one.
    /// </summary>
    internal Func<TargetInfo, TargetInfo>? TargetOverride { get; init; }

    /// <summary>
    /// Never blocks a thread-pool thread (C-269, I-217): the compile runs on the calling thread up to its first tool process and
    /// tools are awaited. A caller on a UI thread starts it with <c>Task.Run</c>.
    /// </summary>
    public async Task<BuildResult> RunAsync(BuildRequest request, IProgress<BuildEvent> progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(progress);
        var clock = Stopwatch.StartNew();
        var path = PathUtil.Normalize(request.ProjectPath);
        ImmutableArray<ProjectOutcome> outcomes = path.EndsWith(".pgroup", StringComparison.OrdinalIgnoreCase)
            ? await BuildGroupAsync(path, request, progress, ct).ConfigureAwait(false)
            : await BuildGraphAsync(path, [path], [], path, request, progress, ct).ConfigureAwait(false);   // C-238: dependencies first
        return Aggregate(outcomes, clock.Elapsed);
    }

    /// <summary>
    /// CMP-821: the projects of the group (or <see cref="BuildRequest.ProjectInGroup"/>; for <c>Run</c> the run project) and every
    /// project they depend on, in topological order of <c>dependencies</c>; ties by the group's order, then discovery order, the
    /// requested / active project last (CMP-807). A cycle is E9006 and nothing is built; a failed project skips its dependants.
    /// Group-level errors (group file, cycle) are reported in a Started…Finished bracket of the group file.
    /// </summary>
    private async Task<ImmutableArray<ProjectOutcome>> BuildGroupAsync(string path, BuildRequest request, IProgress<BuildEvent> progress, CancellationToken ct)
    {
        var groupLoad = ProjectSerializer.LoadGroup(path, _fs);
        if (groupLoad.Value is not { } group || HasErrors(groupLoad.Diagnostics))
        {
            return [await BuildProjectAsync(path, new LoadResult<Project>(null, groupLoad.Diagnostics), request.Target, request, progress, ct).ConfigureAwait(false)];
        }

        var directory = Path.GetDirectoryName(path)!;
        var listed = group.Projects.Select(p => Resolve(directory, p)).ToList();
        var requested = request.ProjectInGroup is { } inGroup ? Resolve(directory, inGroup)
            : request.Target == BuildTarget.Run ? Resolve(directory, group.ActiveProject)
            : null;
        var last = requested ?? Resolve(directory, group.ActiveProject);
        return await BuildGraphAsync(path, requested is null ? listed : [requested], listed, last, request, progress, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// <paramref name="roots"/> and every project they depend on, in topological order; ties by <paramref name="listed"/>, then
    /// discovery order, <paramref name="last"/> last. For <c>Run</c> only <paramref name="last"/> runs, the others are built. A cycle is
    /// E9006 reported in a bracket of <paramref name="path"/> (the group file, or the single requested project).
    /// </summary>
    private async Task<ImmutableArray<ProjectOutcome>> BuildGraphAsync(
        string path, List<string> roots, List<string> listed, string last, BuildRequest request, IProgress<BuildEvent> progress, CancellationToken ct)
    {
        var loads = new Dictionary<string, LoadResult<Project>>(PathUtil.Comparer);
        var dependencies = new Dictionary<string, List<string>>(PathUtil.Comparer);
        var discovered = new List<string>();
        void Visit(string project)
        {
            if (loads.ContainsKey(project))
            {
                return;
            }

            var load = ProjectSerializer.LoadProject(project, _fs);
            loads[project] = load;
            discovered.Add(project);
            var projectDirectory = Path.GetDirectoryName(project)!;
            dependencies[project] = load.Value is { } p && !HasErrors(load.Diagnostics)
                ? [.. p.Dependencies.Select(d => Resolve(projectDirectory, d))]
                : [];
            foreach (var dependency in dependencies[project])
            {
                Visit(dependency);
            }
        }

        foreach (var root in roots)
        {
            Visit(root);
        }

        int Position(string project) => listed.FindIndex(p => PathUtil.AreEqual(p, project)) is var i and >= 0 ? i : listed.Count + discovered.IndexOf(project);
        int Rank(string project) => PathUtil.AreEqual(project, last) ? int.MaxValue : Position(project);

        var remaining = new HashSet<string>(discovered, PathUtil.Comparer);
        var order = new List<string>();
        while (remaining.Count > 0
               && remaining.Where(p => !dependencies[p].Any(remaining.Contains)).OrderBy(Rank).FirstOrDefault() is { } ready)
        {
            remaining.Remove(ready);
            order.Add(ready);
        }

        if (remaining.Count > 0)
        {
            var cycle = FindCycle(remaining.OrderBy(Position).First(), dependencies, remaining);
            var e9006 = Diagnostic.Create("E9006", Location.ForFile(path), string.Join(" -> ", cycle));
            return [await BuildProjectAsync(path, new LoadResult<Project>(null, [e9006]), request.Target, request, progress, ct).ConfigureAwait(false)];
        }

        var outcomes = ImmutableArray.CreateBuilder<ProjectOutcome>();
        var failed = new Dictionary<string, string>(PathUtil.Comparer);                        // project → its name
        var libraries = new Dictionary<string, string>(PathUtil.Comparer);                     // library project → the DLL it built (CMP-1084)
        foreach (var project in order)
        {
            var target = request.Target == BuildTarget.Run && !PathUtil.AreEqual(project, last) ? BuildTarget.Build : request.Target;
            var failedDependency = dependencies[project].FirstOrDefault(failed.ContainsKey);
            ImmutableArray<string> libraryDependencies = [.. dependencies[project].Where(libraries.ContainsKey).Select(d => libraries[d]).Distinct(PathUtil.Comparer)];
            var outcome = await BuildProjectAsync(
                project, loads[project], target, request, progress, ct, failedDependency is null ? null : failed[failedDependency], libraryDependencies)
                .ConfigureAwait(false);
            outcomes.Add(outcome);
            if (!outcome.Result.Success)
            {
                failed[project] = loads[project].Value?.Name ?? Path.GetFileNameWithoutExtension(project);
            }
            else if (loads[project].Value is { AppType: "library" } && outcome.Result.OutputPath is { } dll)
            {
                libraries[project] = dll;
            }

            if (outcome.Canceled)
            {
                break;
            }
        }

        return outcomes.ToImmutable();
    }

    /// <summary>A dependency cycle through the part of <paramref name="within"/> reachable from <paramref name="start"/>: <c>A, B, …, A</c>.</summary>
    private static List<string> FindCycle(string start, Dictionary<string, List<string>> dependencies, HashSet<string> within)
    {
        var stack = new List<string>();
        var done = new HashSet<string>(PathUtil.Comparer);
        List<string>? Walk(string node)
        {
            var onStack = stack.FindIndex(p => PathUtil.AreEqual(p, node));
            if (onStack >= 0)
            {
                return [.. stack[onStack..], node];
            }

            if (!done.Add(node))
            {
                return null;
            }

            stack.Add(node);
            foreach (var next in dependencies[node].Where(within.Contains))
            {
                if (Walk(next) is { } cycle)
                {
                    return cycle;
                }
            }

            stack.RemoveAt(stack.Count - 1);
            return null;
        }

        return Walk(start) ?? [start];
    }

    /// <summary>A project path (relative to <paramref name="directory"/> or absolute, '/' or '\') → normalised absolute path.</summary>
    private static string Resolve(string directory, string path) =>
        PathUtil.Normalize(Path.IsPathFullyQualified(path) ? path : Path.Combine(directory, path.Replace('/', '\\')));

    private static BuildResult Aggregate(ImmutableArray<ProjectOutcome> outcomes, TimeSpan elapsed)
    {
        var all = outcomes.SelectMany(o => o.Result.Diagnostics).ToList();
        var canceled = outcomes.Any(o => o.Canceled);
        return new BuildResult(
            outcomes.All(o => o.Result.Success) && !canceled,
            canceled,
            all.Count(d => d.Severity == DiagnosticSeverity.Error),
            all.Count(d => d.Severity == DiagnosticSeverity.Warning),
            all.Count(d => d.Severity == DiagnosticSeverity.Hint),
            [.. outcomes.Select(o => o.Result)],
            outcomes.Select(o => o.RunExitCode).LastOrDefault(c => c is not null),
            elapsed);
    }

    /// <summary>
    /// One project: exactly one <c>Started</c>, its events, exactly one <c>Finished</c> (CMP-800). <paramref name="load"/> without a
    /// value reports its diagnostics only (also used for group-level errors); <paramref name="skippedBecause"/> = the name of a failed
    /// dependency (CMP-821: no diagnostics of its own, 0 errors, one Info line). <paramref name="libraryDependencies"/> = the DLLs
    /// built by the direct dependencies that are library projects (CMP-1084).
    /// </summary>
    private async Task<ProjectOutcome> BuildProjectAsync(
        string path, LoadResult<Project> load, BuildTarget target, BuildRequest request, IProgress<BuildEvent> progress, CancellationToken ct,
        string? skippedBecause = null, ImmutableArray<string> libraryDependencies = default)
    {
        var clock = Stopwatch.StartNew();
        var sink = new EventSink(progress, path);
        var location = Location.ForFile(path);
        var project = load.Value;
        var requested = request.Configuration ?? project?.ActiveConfiguration ?? "Debug";
        var configuration = project?.ConfigurationNames.FirstOrDefault(n => string.Equals(n, requested, StringComparison.OrdinalIgnoreCase));
        var platform = request.Platform ?? (project is { Platforms.IsEmpty: false } ? project.Platforms[0] : TargetRegistry.Default.Id);
        sink.Report(new BuildEvent.Started(DateTime.UtcNow, path, configuration ?? requested, platform, target));

        var diagnostics = skippedBecause is null ? new List<Diagnostic>(load.Diagnostics) : [];
        var upToDate = false;
        string? output = null;
        var canceled = false;
        int? runExitCode = null;
        if (skippedBecause is not null)
        {
            sink.Info($"{project?.Name ?? Path.GetFileNameWithoutExtension(path)}: skipped because {skippedBecause} failed");
        }
        else if (project is not null && !HasErrors(diagnostics))
        {
            if (configuration is null)
            {
                diagnostics.Add(Diagnostic.Create("E9005", location, requested));
            }
            else if (!project.Platforms.Contains(platform, StringComparer.Ordinal))
            {
                // CMP-1019 rule 2: the effective platform must be one the project lists
                diagnostics.Add(Diagnostic.Create("E9012", location, platform, project.Name, string.Join(", ", project.Platforms)));
            }
            else if (!TargetRegistry.TryGet(platform, out var registered))
            {
                diagnostics.Add(Diagnostic.Create("E9002", location, $"unknown platform '{platform}'"));        // CMP-1019 rule 1 (a model not made by the loader)
            }
            else
            {
                var run = new ProjectRun(this, project, configuration, TargetOverride?.Invoke(registered) ?? registered, request, sink, diagnostics, location)
                {
                    LibraryDependencies = libraryDependencies.IsDefault ? [] : libraryDependencies,
                };
                try
                {
                    (upToDate, output) = await run.ExecuteAsync(target, ct).ConfigureAwait(false);
                    runExitCode = run.RunExitCode;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    run.RemovePartialOutputs();
                    diagnostics.Add(Diagnostic.Create("E9010", location));
                    canceled = true;
                    upToDate = false;
                    output = null;
                }
            }
        }

        sink.ReportDiagnostics(diagnostics);
        var success = !HasErrors(diagnostics) && !canceled && skippedBecause is null;
        upToDate &= success;
        sink.Report(new BuildEvent.Finished(
            DateTime.UtcNow, path, success, upToDate,
            Count(diagnostics, DiagnosticSeverity.Error), Count(diagnostics, DiagnosticSeverity.Warning), Count(diagnostics, DiagnosticSeverity.Hint),
            clock.Elapsed));
        return new ProjectOutcome(
            new ProjectBuildResult(path, configuration ?? requested, success, upToDate, success ? output : null, [.. diagnostics]),
            canceled,
            runExitCode);
    }

    private static bool HasErrors(IEnumerable<Diagnostic> diagnostics) => diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    private static int Count(IEnumerable<Diagnostic> diagnostics, DiagnosticSeverity severity) => diagnostics.Count(d => d.Severity == severity);

    private sealed record ProjectOutcome(ProjectBuildResult Result, bool Canceled, int? RunExitCode);

    /// <summary>Raises the events of one project in order; callbacks of parallel tool runs are serialised.</summary>
    private sealed class EventSink(IProgress<BuildEvent> progress, string projectPath)
    {
        private readonly Lock _gate = new();
        private int _reportedDiagnostics;

        public string ProjectPath => projectPath;

        /// <summary>CMP-801: the diagnostics not yet reported, in their (CMP-504) order.</summary>
        public void ReportDiagnostics(List<Diagnostic> diagnostics)
        {
            for (; _reportedDiagnostics < diagnostics.Count; _reportedDiagnostics++)
            {
                Report(new BuildEvent.Diagnostic(DateTime.UtcNow, projectPath, diagnostics[_reportedDiagnostics]));
            }
        }

        public void Report(BuildEvent e)
        {
            lock (_gate)
            {
                progress.Report(e);
            }
        }

        public void Info(string text) => Report(new BuildEvent.OutputLine(DateTime.UtcNow, projectPath, OutputSource.Info, text));

        public void Tool(string text) => Report(new BuildEvent.OutputLine(DateTime.UtcNow, projectPath, OutputSource.Tool, text));

        public void Command(string text) => Report(new BuildEvent.CommandLine(DateTime.UtcNow, projectPath, text));

        /// <summary>CMP-1139: the command line of a back-end job, with its unit or DLL.</summary>
        public void JobCommand(string text, string? unit, string? dll) =>
            Report(new BuildEvent.CommandLine(DateTime.UtcNow, projectPath, text) { Unit = unit, Dll = dll });
    }

    /// <summary>The targets of one loaded project and configuration (CMP-830…835).</summary>
    private sealed class ProjectRun(
        BuildEngine engine,
        Project project,
        string configuration,
        TargetInfo target,
        BuildRequest request,
        EventSink sink,
        List<Diagnostic> diagnostics,
        Location location)
    {
        /// <summary>The name of the file in the object directory that lists the deployed files, one per line (CMP-1084).</summary>
        private const string DeployedFile = "deployed.txt";

        private readonly IFileSystem _fs = engine._fs;
        private string? _exe;
        private DateTime? _exeTimeBefore;
        private string? _objectDirectory;

        /// <summary>Target = Run and the program ran.</summary>
        public int? RunExitCode { get; private set; }

        /// <summary>CMP-1084: the DLLs of the direct dependencies that are library projects, in dependency order.</summary>
        public ImmutableArray<string> LibraryDependencies { get; init; } = [];

        private bool IsLibrary => project.AppType == "library";

        /// <returns>Whether nothing had to be done, and the output exe.</returns>
        public async Task<(bool UpToDate, string? Output)> ExecuteAsync(BuildTarget target, CancellationToken ct)
        {
            switch (target)
            {
                case BuildTarget.Clean:
                    Clean();
                    return (false, null);
                case BuildTarget.Rebuild:
                    if (!Clean())
                    {
                        return (false, null);
                    }

                    return await BuildAsync(force: true, ct).ConfigureAwait(false);
                case BuildTarget.Run:
                    var built = await BuildAsync(force: false, ct).ConfigureAwait(false);
                    if (built.Output is not null && IsLibrary)
                    {
                        // CMP-1083: a library runs inside its host application
                        if (ProjectOptions.GetHostApplicationPath(project) is { } host)
                        {
                            await RunProgramAsync(host, ct).ConfigureAwait(false);
                        }
                        else
                        {
                            diagnostics.Add(Diagnostic.Create("E9014", location, project.Name));
                        }
                    }
                    else if (built.Output is not null)
                    {
                        await RunProgramAsync(built.Output, ct).ConfigureAwait(false);
                    }

                    return built;
                default:
                    return await BuildAsync(force: false, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// CMP-832: <c>obj/&lt;config&gt;/</c> (the baseline target) or <c>obj/&lt;target id&gt;/&lt;config&gt;/</c> (CMP-1020) recursively and the
        /// output exe (+ <c>.pdb</c>, <c>.pdi</c>); nothing else. CMP-1081: for a library the DLL with its import library; CMP-1084:
        /// and exactly the files recorded in <c>&lt;objdir&gt;/deployed.txt</c>.
        /// </summary>
        private bool Clean()
        {
            var options = ProjectOptions.ToCompilationOptions(project, configuration, target);
            var objectDirectory = options.ObjectDirectory;
            var inside = objectDirectory.StartsWith(project.Directory.TrimEnd('\\') + @"\", StringComparison.OrdinalIgnoreCase)
                && (objectDirectory.EndsWith(@"\obj\" + configuration, StringComparison.OrdinalIgnoreCase)
                    || objectDirectory.EndsWith(@"\obj\" + target.Id + @"\" + configuration, StringComparison.OrdinalIgnoreCase));
            if (!inside)
            {
                diagnostics.Add(Diagnostic.Create("E9002", location,
                    $"object directory '{objectDirectory}' is not '<project dir>/obj/<config>' or '<project dir>/obj/<target>/<config>'"));
                return false;
            }

            var exe = ProjectOptions.GetOutputPath(project, configuration);
            var deployed = ReadDeployed(objectDirectory).Select(name => Path.Combine(Path.GetDirectoryName(exe)!, name)).ToList();
            try
            {
                _fs.DeleteDirectory(objectDirectory);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(Diagnostic.Create("E0004", location, objectDirectory, e.Message));
                return false;
            }

            List<string> outputs = [exe, Path.ChangeExtension(exe, ".pdb"), Path.ChangeExtension(exe, ".pdi")];
            if (IsLibrary)
            {
                outputs.Add(Path.ChangeExtension(exe, ".lib"));
            }

            foreach (var file in outputs.Concat(deployed))
            {
                try
                {
                    _fs.Delete(file);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    diagnostics.Add(Diagnostic.Create("E4008", location, file));             // CMP-806: held by a running program
                    return false;
                }
            }

            return true;
        }

        /// <summary>CMP-830, 834, 835.</summary>
        private async Task<(bool UpToDate, string? Output)> BuildAsync(bool force, CancellationToken ct)
        {
            if (!project.Events.PreBuild.IsEmpty || !project.Events.PostBuild.IsEmpty)
            {
                diagnostics.Add(Diagnostic.Create("W9001", location));
            }

            var projectOptions = ProjectOptions.ToCompilationOptions(project, configuration, target);    // CMP-1019 rule 3
            var options = projectOptions with                                               // CMP-805
            {
                ToolchainDirectory = request.ToolchainDirectory ?? "",
                RtlDirectory = request.RtlDirectory ?? "",
                MaxParallelism = request.MaxParallelism != 0 ? request.MaxParallelism : project.Jobs,   // CMP-1130; 0 = one per processor
                SearchPaths = [.. projectOptions.SearchPaths,
                    .. request.ExtraSearchPaths.Select(PathUtil.Normalize).Where(p => !projectOptions.SearchPaths.Contains(p, PathUtil.Comparer))],
                // CMP-1084 (a): the directory of each direct library dependency, after the project's own library paths
                LibraryPaths = [.. projectOptions.LibraryPaths,
                    .. LibraryDependencies.Select(d => Path.GetDirectoryName(d)!).Distinct(PathUtil.Comparer)
                        .Where(p => !projectOptions.LibraryPaths.Contains(p, PathUtil.Comparer))],
            };
            foreach (var file in project.Files)
            {
                var full = PathUtil.Normalize(Path.Combine(project.Directory, file.Path.Replace('/', '\\')));
                if (!PathUtil.AreEqual(full, options.MainFile) && !Exists(full))
                {
                    diagnostics.Add(Diagnostic.Create("W9002", location, full));
                }
            }

            if (!Exists(options.MainFile))
            {
                diagnostics.Add(Diagnostic.Create("E9004", location, options.MainFile));
                return (false, null);
            }

            var output = ProjectOptions.GetOutputPath(project, configuration);
            _exe = output;
            _exeTimeBefore = _fs.FileExists(output) ? _fs.GetLastWriteTimeUtc(output) : null;
            _objectDirectory = options.ObjectDirectory;
            var didWork = false;
            var emit = new EmitOptions
            {
                Stage = EmitStage.Exe,
                OutputPath = output,
                Verbose = request.Verbose,
                ForceRebuild = force,
                CommandLineSink = request.Verbose ? sink.Command : null,
                JobCommandLineSink = request.Verbose ? sink.JobCommand : null,
                ToolTimeProvider = engine.ToolTimeProvider,
                ToolOutputSink = sink.Tool,
                ProcessRunner = engine._processRunner,
                InfoSink = line =>
                {
                    didWork = true;
                    sink.Info(line);
                },
            };
            var compilation = Compilation.Create(options, request.OpenFiles, _fs);
            // C-269, I-217: awaited on the engine's own thread, no hop to a pool worker (with every worker busy the hop would never
            // run); a caller on a UI thread starts RunAsync with Task.Run (the IDE's build service does)
            var result = await compilation.EmitAsync(emit, ct).ConfigureAwait(false);
            diagnostics.AddRange(result.Diagnostics);
            if (!result.Success)
            {
                return (false, null);
            }

            if (!Deploy(output, options.ObjectDirectory, ref didWork))
            {
                return (false, null);
            }

            if (!didWork)
            {
                sink.Info(project.Name + ": up to date");
            }

            return (!didWork, result.OutputPath);
        }

        /// <summary>
        /// CMP-1084 (b): after a successful build each direct library dependency's DLL (and its <c>.pdb</c> / <c>.pdi</c> when present)
        /// is copied next to <paramref name="output"/> when it lives in another directory and the bytes differ; the copied file names
        /// are recorded in <c>&lt;objdir&gt;/deployed.txt</c>. A target that is in use → E4008 and false.
        /// </summary>
        private bool Deploy(string output, string objectDirectory, ref bool didWork)
        {
            var outputDirectory = Path.GetDirectoryName(output)!;
            var names = new List<string>();
            foreach (var dll in LibraryDependencies.Where(d => !PathUtil.AreEqual(Path.GetDirectoryName(d)!, outputDirectory)))
            {
                foreach (var source in new[] { dll, Path.ChangeExtension(dll, ".pdb"), Path.ChangeExtension(dll, ".pdi") }.Where(_fs.FileExists))
                {
                    var name = Path.GetFileName(source);
                    var destination = Path.Combine(outputDirectory, name);
                    if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        names.Add(name);
                    }

                    try
                    {
                        var bytes = _fs.ReadAllBytes(source);
                        if (_fs.FileExists(destination) && _fs.ReadAllBytes(destination).AsSpan().SequenceEqual(bytes))
                        {
                            continue;
                        }

                        sink.Info("Deploying " + name);
                        didWork = true;
                        _fs.WriteAllBytes(destination, bytes);
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        diagnostics.Add(Diagnostic.Create("E4008", location, destination));
                        return false;
                    }
                }
            }

            var text = string.Concat(names.Select(n => n + "\n"));
            var record = Path.Combine(objectDirectory, DeployedFile);
            if (names.Count > 0 && !(_fs.FileExists(record) && _fs.ReadAllText(record) == text))
            {
                _fs.WriteAllBytes(record, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text));
            }

            return true;
        }

        /// <summary>The file names recorded by <see cref="Deploy"/>; names that are not plain file names are ignored (Clean deletes nothing else).</summary>
        private List<string> ReadDeployed(string objectDirectory)
        {
            var record = Path.Combine(objectDirectory, DeployedFile);
            try
            {
                return !_fs.FileExists(record) ? []
                    : [.. _fs.ReadAllText(record).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(n => n == Path.GetFileName(n) && n is not ("." or ".."))];
            }
            catch (IOException)
            {
                return [];
            }
        }

        /// <summary>
        /// CMP-833: the exe with <c>RunArguments ?? run.arguments</c> (Windows command-line rules) in <c>run.workingDir</c> (relative to
        /// the project; empty = the exe's directory). Redirected: stdout/stderr lines as OutputLine events, stdin closed (through the
        /// process runner); otherwise the child inherits the console.
        /// </summary>
        private async Task RunProgramAsync(string exe, CancellationToken ct)
        {
            if (!_fs.FileExists(exe))
            {
                diagnostics.Add(Diagnostic.Create("E9009", location, exe));
                return;
            }

            sink.ReportDiagnostics(diagnostics);                                            // the build's diagnostics come first
            var arguments = CommandLineArguments.Split(request.RunArguments ?? project.Run.Arguments);
            var workingDirectory = project.Run.WorkingDir.Length == 0
                ? Path.GetDirectoryName(exe)!
                : PathUtil.Normalize(Path.Combine(project.Directory, project.Run.WorkingDir.Replace('/', '\\')));
            _exe = null;                                                                    // a complete exe is not a partial output
            int exitCode;
            try
            {
                exitCode = request.RedirectRunOutput
                    ? await (engine._processRunner ?? ProcessRunner.Instance).RunAsync(
                        exe,
                        arguments,
                        workingDirectory,
                        line => sink.Report(new BuildEvent.OutputLine(DateTime.UtcNow, sink.ProjectPath, OutputSource.ProgramStdOut, line)),
                        line => sink.Report(new BuildEvent.OutputLine(DateTime.UtcNow, sink.ProjectPath, OutputSource.ProgramStdErr, line)),
                        ct).ConfigureAwait(false)
                    : await RunInheritingTheConsoleAsync(exe, arguments, workingDirectory, ct).ConfigureAwait(false);
            }
            catch (System.ComponentModel.Win32Exception e)
            {
                // the program cannot be started: reported like a tool that cannot start (E4004, exit code -1)
                sink.Info(e.Message);
                diagnostics.Add(Diagnostic.Create("E4004", location, Path.GetFileName(exe), -1));
                return;
            }

            RunExitCode = exitCode;
            sink.Info("Program exited with code " + exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>No redirection: the child shares the caller's console (<c>pbuild --target run</c>); cancellation kills its tree.</summary>
        private static async Task<int> RunInheritingTheConsoleAsync(string exe, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken ct)
        {
            var start = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = workingDirectory };
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start)!;
            try
            {
                await process.WaitForExitAsync(ct).ConfigureAwait(false);
                return process.ExitCode;
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    try
                    {
                        await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);     // C-269: no thread waits
                    }
                    catch (OperationCanceledException)
                    {
                        // the tree did not exit within the limit
                    }
                }
            }
        }

        /// <summary>CMP-803: an exe written (or half-written) by this build and temporary files of the object directory are removed.</summary>
        public void RemovePartialOutputs()
        {
            try
            {
                if (_exe is not null && _fs.FileExists(_exe) && (_exeTimeBefore is null || _fs.GetLastWriteTimeUtc(_exe) != _exeTimeBefore))
                {
                    _fs.Delete(_exe);
                }

                if (_objectDirectory is not null)
                {
                    foreach (var temp in _fs.EnumerateFiles(_objectDirectory, "*.tmp").ToList())
                    {
                        _fs.Delete(temp);
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // best effort: the next build replaces what is left
            }
        }

        /// <summary>On disk or as an open editor buffer (CMP-805).</summary>
        private bool Exists(string path) =>
            request.OpenFiles.Any(f => PathUtil.AreEqual(f.Path, path)) || _fs.FileExists(path);
    }
}
