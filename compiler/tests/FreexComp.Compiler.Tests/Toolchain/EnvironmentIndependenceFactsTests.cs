using System.Diagnostics;
using System.Text;
using FreexComp.Compiler.Tests.Emit;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Toolchain;

namespace FreexComp.Compiler.Tests.Toolchain;

/// <summary>
/// C-336: a build does not depend on the environment variables of the machine. The pinned compiler driver reads extra options
/// from <c>CCC_OVERRIDE_OPTIONS</c>, the pinned linker reads extra options from <c>LINK</c> and <c>_LINK_</c> and library
/// directories from <c>LIB</c>; every tool process of a build is started without them. Each fact builds the same sources twice
/// with the real tools, once with the variable set for the started processes only (never in the test process), and compares
/// the outputs byte for byte.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Native")]
[Trait("Category", "Toolchain")]
public sealed class EnvironmentIndependenceFactsTests
{
    private const string UnitSource = """
        unit EnvUnit;

        interface

        function Twice(V: Integer): Integer;
        function Sum(N: Integer): Integer;

        implementation

        function Twice(V: Integer): Integer;
        begin
          Result := V * 2;
        end;

        function Sum(N: Integer): Integer;
        var
          I: Integer;
        begin
          Result := 0;
          for I := 1 to N do
            Result := Result + Twice(I);
        end;

        end.
        """;

    private const string ProgramSource = """
        program EnvDemo;

        uses
          EnvUnit;

        begin
          WriteLn(Sum(10));
        end.
        """;

    private static string NewDirectory() => PathUtil.Normalize(Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N")));

    private static void Write(string path, string text) => PhysicalFileSystem.Instance.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));

    /// <summary>One full build through the real compile and link path; file name relative to the directory → content.</summary>
    private static SortedDictionary<string, byte[]> Build(string directory, Action<ProcessStartInfo>? starting)
    {
        var obj = Path.Combine(directory, "obj");
        var exe = Path.Combine(directory, "out", "EnvDemo.exe");
        var options = new CompilationOptions
        {
            MainFile = Path.Combine(directory, "EnvDemo.pas"),
            RtlDirectory = TestRtl.Directory,
            ToolchainDirectory = Path.GetDirectoryName(Clang.Path)!,
            ObjectDirectory = obj,
        };
        var result = Compilation.Create(options, [], PhysicalFileSystem.Instance)
            .Emit(new EmitOptions { OutputPath = exe, ProcessRunner = new ProcessRunner { Starting = starting } }, TestContext.Current.CancellationToken);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));

        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(obj, "*", SearchOption.AllDirectories)
                     .Where(f => Path.GetExtension(f) is ".obj" or ".lib" or ".rsp")
                     .Append(exe))
        {
            files.Add(Path.GetRelativePath(directory, file), File.ReadAllBytes(file));
        }

        // cleaned: the next build in this directory finds nothing up to date
        TestDirectories.Delete(obj);
        TestDirectories.Delete(Path.Combine(directory, "out"));
        return files;
    }

    /// <summary>
    /// <c>CCC_OVERRIDE_OPTIONS</c> appends an option to every start of the compiler driver (here one that changes the sections
    /// of the object file); <c>LINK</c> and <c>_LINK_</c> give the linker extra options (here the image version of the header);
    /// <c>LIB</c> names directories the linker searches for libraries (here one with a file that is no library under the name
    /// of an import library of the link). None of them changes an object file, an import library or the executable.
    /// </summary>
    [Theory]
    [InlineData("CCC_OVERRIDE_OPTIONS", "+-ffunction-sections")]
    [InlineData("LINK", "/version:7.5")]
    [InlineData("_LINK_", "/version:7.5")]
    [InlineData("LIB", null)]
    [Trait("Req", "CMP-001")]
    public void Variable_ChangesNoOutputOfABuild(string variable, string? value)
    {
        Assert.True(File.Exists(Clang.Path), $"Pinned toolchain missing: {Clang.Path} (run scripts/bootstrap.ps1).");
        var dir = NewDirectory();
        try
        {
            Write(Path.Combine(dir, "EnvDemo.pas"), ProgramSource);
            Write(Path.Combine(dir, "EnvUnit.pas"), UnitSource);
            if (value is null)
            {
                value = Path.Combine(dir, "from variable");
                Write(Path.Combine(value, "kernel32.lib"), "not a library");
            }

            var plain = Build(dir, starting: null);
            Assert.Contains(Path.Combine("obj", "EnvUnit.obj"), plain.Keys);
            Assert.Contains(Path.Combine("obj", "imports", "kernel32.lib"), plain.Keys);
            Assert.Contains(Path.Combine("out", "EnvDemo.exe"), plain.Keys);

            var withVariable = Build(dir, start => start.Environment[variable] = value);

            Assert.Equal(plain.Keys, withVariable.Keys);
            var differing = plain.Keys.Where(f => !plain[f].AsSpan().SequenceEqual(withVariable[f])).ToList();
            Assert.True(differing.Count == 0, variable + " changes: " + string.Join(", ", differing));
        }
        finally
        {
            TestDirectories.Delete(dir);
        }
    }

    /// <summary>
    /// The preprocessing step of a resource script (CMP-1303) starts the compiler driver too: a macro defined through
    /// <c>CCC_OVERRIDE_OPTIONS</c> does not reach the script.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1303")]
    public async Task CompilerDriverOptionsVariable_ChangesNoResourceFile()
    {
        Assert.True(File.Exists(Clang.Path), $"Pinned toolchain missing: {Clang.Path} (run scripts/bootstrap.ps1).");
        var paths = new ToolchainPaths(Path.GetDirectoryName(Clang.Path)!);
        Assert.True(File.Exists(paths.ResourceTool), $"Pinned toolchain missing: {paths.ResourceTool} (run scripts/bootstrap.ps1).");
        var dir = NewDirectory();
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var script = Path.Combine(dir, "scripts", "facts.rc");
            Write(script, "1 RCDATA\nBEGIN\n#ifdef FROM_ENVIRONMENT\n  7\n#else\n  1\n#endif\nEND\n");

            async Task<byte[]> Compile(string folder, ProcessRunner processes)
            {
                var obj = Path.Combine(dir, folder);
                var request = new ResourceCompileRequest(script, Path.Combine(obj, "facts.res"), obj);
                var compiler = new ResourceCompiler(new ToolRunner(processes, new ToolRunOptions(Location.ForFile(script))), paths, PhysicalFileSystem.Instance);
                var result = await compiler.CompileAsync(request, ct);
                Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
                return File.ReadAllBytes(request.OutputPath);
            }

            var plain = await Compile("plain", new ProcessRunner());
            var withVariable = await Compile("with variable", new ProcessRunner { Starting = start => start.Environment["CCC_OVERRIDE_OPTIONS"] = "+-DFROM_ENVIRONMENT" });

            Assert.Equal(plain, withVariable);
        }
        finally
        {
            TestDirectories.Delete(dir);
        }
    }
}
