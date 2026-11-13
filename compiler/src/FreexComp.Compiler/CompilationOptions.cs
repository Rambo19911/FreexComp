using System.Collections.Immutable;

namespace FreexComp.Compiler;

public enum AppType { Console, Gui }

public enum OptimizationLevel { O0, O1, O2 }

public enum EmitStage { Check, Ll, Obj, Exe }

public enum DebugFormat { CodeView, Dwarf }

/// <summary>api.md §1.</summary>
public sealed record CompilationOptions
{
    /// <summary>Absolute path of the program (or a unit, for --check).</summary>
    public required string MainFile { get; init; }

    /// <summary>-U, in order.</summary>
    public ImmutableArray<string> SearchPaths { get; init; } = [];

    /// <summary>-D.</summary>
    public ImmutableArray<string> Defines { get; init; } = [];

    /// <summary>"" → &lt;dir of MainFile&gt;/obj/&lt;Debug|Release&gt;.</summary>
    public string ObjectDirectory { get; init; } = "";

    /// <summary>"" → default lookup (compiler-cli.md).</summary>
    public string RtlDirectory { get; init; } = "";

    /// <summary>"" → default lookup (CMP-600).</summary>
    public string ToolchainDirectory { get; init; } = "";

    public OptimizationLevel Optimization { get; init; } = OptimizationLevel.O0;

    public bool DebugInfo { get; init; }

    public DebugFormat DebugFormat { get; init; } = DebugFormat.CodeView;

    public AppType AppType { get; init; } = AppType.Console;

    /// <summary>Project-level defaults of {$R}, {$Q}, {$C}.</summary>
    public bool RangeChecks { get; init; }

    public bool OverflowChecks { get; init; }

    public bool Assertions { get; init; } = true;

    /// <summary>0 → Environment.ProcessorCount.</summary>
    public int MaxParallelism { get; init; }

    /// <summary>CMP-154.</summary>
    public bool UsePcuCache { get; init; } = true;
}
