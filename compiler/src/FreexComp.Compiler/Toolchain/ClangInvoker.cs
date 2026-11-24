using System.Collections.Immutable;

namespace FreexComp.Compiler.Toolchain;

/// <summary>CMP-605: one <c>clang -c</c> per unit, run in the object directory. A failing <c>.ll</c> is kept (CMP-603).</summary>
internal sealed class ClangInvoker(ToolRunner runner, string clangPath)
{
    public static ImmutableArray<string> BuildArguments(string llPath, string objPath, OptimizationLevel optimization, bool debugInfo)
    {
        var level = optimization switch
        {
            OptimizationLevel.O0 => "-O0",
            OptimizationLevel.O1 => "-O1",
            OptimizationLevel.O2 => "-O2",
            _ => throw new ArgumentOutOfRangeException(nameof(optimization)),
        };
        // -mno-incremental-linker-compatible: the COFF header of the object carries no time stamp (it would be the only byte
        // sequence of an .obj that differs between two builds of the same .ll; CMP-001, CMP-1144)
        ImmutableArray<string> args =
            ["-c", llPath, "-o", objPath, level, "--target=x86_64-pc-windows-msvc", "-fno-stack-protector", "-Wno-override-module",
                "-mno-incremental-linker-compatible"];
        return debugInfo ? args.Add("-g") : args;
    }

    /// <summary>The compile job of one unit (CMP-1134).</summary>
    public static BackendJob Job(string clangPath, string unit, string llPath, string objPath, OptimizationLevel optimization, bool debugInfo) =>
        new(unit, null, clangPath, BuildArguments(llPath, objPath, optimization, debugInfo), Path.GetDirectoryName(objPath)!, objPath);

    public Task<ToolResult> CompileAsync(string llPath, string objPath, OptimizationLevel optimization, bool debugInfo, CancellationToken ct) =>
        runner.RunAsync(clangPath, BuildArguments(llPath, objPath, optimization, debugInfo), Path.GetDirectoryName(objPath)!, ct);
}
