using System.Collections.Immutable;
using FreexComp.Compiler;

namespace FreexComp.Build;

/// <summary>The build engine contract (docs/interfaces/compiler-cli.md §4; behaviour: build-engine.md CMP-800…842).</summary>
public interface IBuildEngine
{
    Task<BuildResult> RunAsync(BuildRequest request, IProgress<BuildEvent> progress, CancellationToken ct);
}

public enum BuildTarget { Build, Rebuild, Clean, Run }

/// <summary>What a build does with the command lists of a project (project-trust.md CMP-1241…1243).</summary>
public enum EventsPolicy { RequireTrust, Run, Skip }

public sealed record BuildRequest
{
    /// <summary>.pproj or .pgroup, absolute.</summary>
    public required string ProjectPath { get; init; }

    /// <summary>null → the project's activeConfiguration.</summary>
    public string? Configuration { get; init; }

    /// <summary>The target id; null → the project's active platform (<see cref="ProjectOptions.GetEffectivePlatform"/>).</summary>
    public string? Platform { get; init; }

    public BuildTarget Target { get; init; } = BuildTarget.Build;

    /// <summary>true → CommandLine events.</summary>
    public bool Verbose { get; init; }

    /// <summary>.pgroup: build only this project (+ dependencies); null → all.</summary>
    public string? ProjectInGroup { get; init; }

    /// <summary>Group / dependency builds: start no further project after the first failed one (CMP-1340); false → only its dependants are skipped.</summary>
    public bool StopOnFirstFailure { get; init; }

    /// <summary>Unsaved editor buffers overriding files on disk.</summary>
    public ImmutableArray<SourceText> OpenFiles { get; init; } = [];

    /// <summary>LLVM bin dir; null → default lookup (--toolchain).</summary>
    public string? ToolchainDirectory { get; init; }

    /// <summary>RTL root; null → default lookup (--rtl).</summary>
    public string? RtlDirectory { get; init; }

    /// <summary>Absolute unit dirs searched after the project's searchPaths.</summary>
    public ImmutableArray<string> ExtraSearchPaths { get; init; } = [];

    /// <summary>Target = Run: overrides the project's run.arguments.</summary>
    public string? RunArguments { get; init; }

    /// <summary>Target = Run: capture stdout/stderr as OutputLine events.</summary>
    public bool RedirectRunOutput { get; init; } = true;

    /// <summary>Project commands: the trust gate (C-324). RequireTrust → E9021 for a project that has commands and is not trusted.</summary>
    public EventsPolicy Events { get; init; } = EventsPolicy.RequireTrust;

    /// <summary>The trust list read for <see cref="EventsPolicy.RequireTrust"/>; null → nothing is trusted.</summary>
    public ITrustStore? TrustStore { get; init; }

    /// <summary>0 → the project's "jobs", else one job per processor.</summary>
    public int MaxParallelism { get; init; }
}

/// <param name="Success">Every project succeeded and the build was not cancelled.</param>
/// <param name="Canceled">ct was cancelled (one E9010 diagnostic is also reported).</param>
/// <param name="Projects">In build order.</param>
/// <param name="RunExitCode">Target = Run and the program ran.</param>
public sealed record BuildResult(
    bool Success,
    bool Canceled,
    int Errors,
    int Warnings,
    int Hints,
    ImmutableArray<ProjectBuildResult> Projects,
    int? RunExitCode,
    TimeSpan Elapsed)
{
    /// <summary>CMP-807: <see cref="Success"/> and every project reported UpToDate.</summary>
    public bool UpToDate => Success && Projects.All(p => p.UpToDate);

    /// <summary>CMP-807: OutputPath of the last entry of <see cref="Projects"/> (the requested project).</summary>
    public string? OutputPath => Projects.IsDefaultOrEmpty ? null : Projects[^1].OutputPath;
}

public sealed record ProjectBuildResult(
    string ProjectPath,
    string Configuration,
    bool Success,
    bool UpToDate,
    string? OutputPath,
    ImmutableArray<Diagnostic> Diagnostics);

public abstract record BuildEvent(DateTime TimestampUtc)
{
    public sealed record Started(DateTime T, string ProjectPath, string Configuration, string Platform, BuildTarget Target) : BuildEvent(T);

    public sealed record Diagnostic(DateTime T, string ProjectPath, FreexComp.Compiler.Diagnostic Value) : BuildEvent(T);

    public sealed record CommandLine(DateTime T, string ProjectPath, string Text) : BuildEvent(T)
    {
        /// <summary>The unit a compile job's command line belongs to (CMP-1139); null for every other command.</summary>
        public string? Unit { get; init; }

        /// <summary>The DLL an import-library job's command line belongs to (CMP-1139); null for every other command.</summary>
        public string? Dll { get; init; }
    }

    public sealed record OutputLine(DateTime T, string ProjectPath, OutputSource Source, string Text) : BuildEvent(T);

    public sealed record Finished(DateTime T, string ProjectPath, bool Success, bool UpToDate, int Errors, int Warnings, int Hints, TimeSpan Elapsed) : BuildEvent(T);
}

public enum OutputSource { Tool, ProgramStdOut, ProgramStdErr, Info }
