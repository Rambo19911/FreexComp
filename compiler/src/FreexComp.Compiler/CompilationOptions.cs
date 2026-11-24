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

    /// <summary>Default RTTI scope, <c>broad</c> | <c>published</c> (LANG-648; <c>--rtti</c>, <c>.pproj</c> <c>rtti</c>).</summary>
    public string Rtti { get; init; } = "broad";

    /// <summary>
    /// Application manifest (CMP-628): <c>default</c> | <c>none</c> | absolute path of a manifest file; "" → <c>default</c>
    /// for GUI apps, <c>none</c> for console apps.
    /// </summary>
    public string Manifest { get; init; } = "";

    /// <summary>The compilation target (CMP-1003): one compilation has one target.</summary>
    public Targets.TargetInfo Target { get; init; } = Targets.TargetRegistry.Default;

    /// <summary>Unit scope names tried as prefixes for unresolved unit names, in order (LANG-535; <c>--unit-scope</c>).</summary>
    public ImmutableArray<string> UnitScopeNames { get; init; } = [];
}

/// <summary>CMP-241: the ABI version of compiled code (rtl-abi.md).</summary>
public static class AbiInfo
{
    public const int Version = 1;
}

/// <summary>CMP-241: the compiler version printed by <c>opc --version</c>.</summary>
public static class CompilerInfo
{
    public const string Version = "0.1.0";

    /// <summary>The LLVM version the compiler is pinned to (scripts/toolchain.json, CMP-601).</summary>
    public const string LlvmPin = Toolchain.ToolchainVersionCheck.PinnedVersion;
}
