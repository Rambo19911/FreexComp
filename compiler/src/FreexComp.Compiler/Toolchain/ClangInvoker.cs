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
        ImmutableArray<string> args =
            ["-c", llPath, "-o", objPath, level, "--target=x86_64-pc-windows-msvc", "-fno-stack-protector", "-Wno-override-module"];
        return debugInfo ? args.Add("-g") : args;
    }

    public Task<ToolResult> CompileAsync(string llPath, string objPath, OptimizationLevel optimization, bool debugInfo, CancellationToken ct) =>
        runner.RunAsync(clangPath, BuildArguments(llPath, objPath, optimization, debugInfo), Path.GetDirectoryName(objPath)!, ct);
}
