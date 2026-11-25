using System.Collections.Immutable;

namespace FreexComp.Compiler;

/// <summary><c>Library</c>: a dynamic-link library; goes with a library main file (CMP-1060).</summary>
public enum AppType { Console, Gui, Library }

public enum OptimizationLevel { O0, O1, O2 }

public enum EmitStage { Check, Ll, Obj, Exe }

public enum DebugFormat { CodeView, Dwarf }

/// <summary>CMP-1104: the <c>requestedExecutionLevel</c> of the generated application manifest.</summary>
public enum ManifestExecutionLevel { AsInvoker, HighestAvailable, RequireAdministrator }

/// <summary>CMP-1105: <c>Default</c> = per-monitor-v2 for GUI programs, not declared for console programs.</summary>
public enum ManifestDpiAwareness { Default, PerMonitorV2, System, Unaware }

/// <summary>api.md §1.</summary>
public sealed record CompilationOptions
{
    /// <summary>Absolute path of the program (or a unit, for --check).</summary>
    public required string MainFile { get; init; }

    /// <summary>-U, in order.</summary>
    public ImmutableArray<string> SearchPaths { get; init; } = [];

    /// <summary>
    /// -L, in order: directories searched for ready-made import libraries <c>&lt;dll-base&gt;.lib</c> (CMP-1070, 1071 step 3).
    /// </summary>
    public ImmutableArray<string> LibraryPaths { get; init; } = [];

    /// <summary>-D.</summary>
    public ImmutableArray<string> Defines { get; init; } = [];

    /// <summary>
    /// Absolute <c>.rc</c> / <c>.res</c> paths, compiled and linked in this order (CMP-1314): a resource script is compiled with
    /// <see cref="Defines"/> and <see cref="SearchPaths"/>, a compiled resource file is passed to the link as it is.
    /// </summary>
    public ImmutableArray<string> ResourceFiles { get; init; } = [];

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
    /// Application manifest (CMP-628, CMP-1100): <c>default</c> | <c>none</c> | absolute path of a manifest file; "" →
    /// <c>default</c> for console and GUI programs (a library has none).
    /// </summary>
    public string Manifest { get; init; } = "";

    /// <summary>CMP-1104: the requested execution level of the generated manifest; affects only the manifest.</summary>
    public ManifestExecutionLevel ManifestExecutionLevel { get; init; }

    /// <summary>CMP-1105: the DPI awareness of the generated manifest; affects only the manifest.</summary>
    public ManifestDpiAwareness ManifestDpiAwareness { get; init; }

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
