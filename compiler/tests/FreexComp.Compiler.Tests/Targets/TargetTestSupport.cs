using System.Collections.Immutable;
using FreexComp.Compiler.Targets;

namespace FreexComp.Compiler.Tests.Targets;

/// <summary>Ad-hoc descriptors (CMP-1004: <c>TargetInfo.Win64 with { … }</c>) and in-memory compilations to stage <c>ll</c> for the target tests.</summary>
internal static class TargetTestSupport
{
    public const string Rtl = @"C:\rtl";
    public const string Obj = @"C:\p\obj";

    /// <summary>The fake 32-bit descriptor of CMP-1032.</summary>
    public static TargetInfo Fake32 { get; } = TargetInfo.Win64 with
    {
        Id = "fake32",
        Triple = "i686-pc-windows-msvc",
        PointerSize = 4,
        PointerAlign = 4,
        PredefinedSymbols = ["MSWINDOWS", "WINDOWS", "WIN32", "CPUX86", "CPU32BITS"],
    };

    /// <summary>The smallest <c>System</c> the compiler accepts: nothing in it depends on a target.</summary>
    public const string StubSystem = """
        unit System;
        interface
        type
          TObject = class
          end;
          TVarRec = record
            case VType: Byte of
              0: (VInteger: Integer);
              1: (VPointer: Pointer);
              2: (VInt64: Int64);
          end;
        var Input, Output: Text;
        implementation
        end.
        """;

    /// <summary>Compiles <paramref name="main"/> (<c>C:\p\&lt;name&gt;.pas</c>) to stage <c>ll</c> for <paramref name="target"/>; unit name → <c>.ll</c> text.</summary>
    public static Dictionary<string, string> EmitLl(TargetInfo target, string name, string main, string system = StubSystem,
        Func<CompilationOptions, CompilationOptions>? configure = null, params (string Path, string Text)[] files)
    {
        var fs = new InMemoryFileSystem().Add(Path.Combine(Rtl, "System.pas"), system).Add(@"C:\p\" + name + ".pas", main);
        foreach (var (path, text) in files)
        {
            fs.Add(path, text);
        }

        var options = new CompilationOptions { MainFile = @"C:\p\" + name + ".pas", RtlDirectory = Rtl, ObjectDirectory = Obj, Target = target };
        options = configure?.Invoke(options) ?? options;
        var result = Compilation.Create(options, [], fs).Emit(new EmitOptions { Stage = EmitStage.Ll }, TestContext.Current.CancellationToken);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        return fs.EnumerateFiles(Obj, "*.ll").ToDictionary(p => Path.GetFileNameWithoutExtension(p), fs.ReadAllText, StringComparer.OrdinalIgnoreCase);
    }

    public static ImmutableArray<string> Lines(string text) => [.. text.Split('\n')];
}
