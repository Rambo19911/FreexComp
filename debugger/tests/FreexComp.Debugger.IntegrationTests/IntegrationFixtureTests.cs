using System.Diagnostics;
using System.Text;
using FreexComp.Debugger.IntegrationTests.TestSupport;
using FreexComp.Debugger.Tests.TestSupport;
using FreexComp.Debugger.Values.TypeInfo;

namespace FreexComp.Debugger.IntegrationTests;

/// <summary>
/// The integration fixture itself (scripted test DBG-300 … DBG-302): toolchain, compiler, the demo program built with
/// debug information.
/// </summary>
[Collection(RealAdapterCollection.Name)]
[Trait("Kind", "Debug")]
[Trait("Category", "Integration")]
[Trait("Category", "Toolchain")]
public sealed class IntegrationFixtureTests(IntegrationFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Req", "DBG-301")]
    [Trait("Req", "DBG-302")]
    public async Task Builds_DbgDemo_WithSidecar()
    {
        fixture.RequireToolchain();

        var program = fixture.Build("DbgDemo");

        // Built in a fresh directory of its own, from a copy of the source; debug information for the adapter (the
        // debug container is CodeView: a .pdb) and the Pascal type information next to the executable.
        Assert.StartsWith(
            Path.Combine(Path.GetTempPath(), "FreexComp.Tests") + Path.DirectorySeparatorChar, program.Directory, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(program.Directory, Path.GetDirectoryName(program.SourcePath));
        Assert.True(File.Exists(program.ExePath), program.ExePath);
        Assert.True(File.Exists(program.TypeInfoPath), program.TypeInfoPath);
        Assert.True(File.Exists(Path.ChangeExtension(program.ExePath, ".pdb")), "DbgDemo.pdb");
        Assert.Same(program, fixture.Build("DbgDemo"));   // built once per run

        // The type information is the one the session will load: it knows the routine of line 69 and the raise hook.
        var typeInfo = PdiLoader.Load(program.TypeInfoPath);
        Assert.NotNull(typeInfo);
        Assert.Equal("DbgDemo", typeInfo.Program);
        Assert.Equal("Run", typeInfo.FindRoutine(program.SourcePath, 69)?.DisplayName);
        Assert.Equal("Factorial", typeInfo.FindRoutine(program.SourcePath, 40)?.DisplayName);
        Assert.NotEqual("", typeInfo.Abi.RaiseHookSymbol);

        // Run without a debugger the program prints three lines and exits with code 0.
        using var process = Process.Start(new ProcessStartInfo(program.ExePath)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8,
        })!;
        ProcessLeakFixture.Registry.Track(process.Id, "DbgDemo run without a debugger");
        try
        {
            var output = await process.StandardOutput.ReadToEndAsync(Ct).WaitAsync(IntegrationFixture.Safety, Ct);
            await process.WaitForExitAsync(Ct).WaitAsync(IntegrationFixture.Safety, Ct);
            Assert.Equal("Dog Rex\n24\ndone\n", output.Replace("\r\n", "\n", StringComparison.Ordinal));
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    [Trait("Req", "DBG-300")]
    public void DbgDemo_Source_LineNumbersAreTheContract()
    {
        // The line numbers of the demo program are part of the scripted test: UTF-8 without BOM, LF, 78 lines, no
        // line end after the last one.
        var bytes = File.ReadAllBytes(fixture.ProgramSource("DbgDemo"));
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble), "byte order mark");
        Assert.DoesNotContain((byte)'\r', bytes);
        var lines = Encoding.UTF8.GetString(bytes).Split('\n');
        Assert.Equal(78, lines.Length);
        Assert.Equal("program DbgDemo;", lines[0]);
        Assert.Equal("    Result := N * Factorial(N - 1);", lines[40 - 1]);
        Assert.Equal("  S := 'Héllo';", lines[61 - 1]);
        Assert.Equal("    Nums[I] := (I + 1) * 10;", lines[66 - 1]);
        Assert.Equal("  Total := Factorial(4);", lines[69 - 1]);
        Assert.Equal("  WriteLn(Total);", lines[71 - 1]);
        Assert.Equal("  Run;", lines[77 - 1]);
        Assert.Equal("end.", lines[78 - 1]);
    }
}
