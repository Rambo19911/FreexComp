using System.Text.RegularExpressions;

namespace FreexComp.Policy.Tests;

/// <summary>
/// L-022: the redirected pipes of a child process are synchronous handles; an asynchronous read parks a thread-pool
/// thread until the child writes, and parallel tests starve the pool (debugger finding G-083). No C# source of the
/// repository reads a child's standard output or error asynchronously; the area sites still open are listed with their
/// task and leave the list when that task lands.
/// </summary>
[Trait("Phase", "8")]
public sealed partial class ChildPipeReadsTests
{
    [GeneratedRegex(@"Standard(Output|Error)\s*\.\s*(ReadToEndAsync|ReadLineAsync|ReadAsync|BaseStream\s*\.\s*(ReadAsync|CopyToAsync))\b|\bBegin(Output|Error)ReadLine\s*\(")]
    private static partial Regex AsyncPipeRead();

    /// <summary>Open sites, by repository-relative path (forward slashes) → task that removes them.</summary>
    private static readonly Dictionary<string, string> Open = new(StringComparer.OrdinalIgnoreCase)
    {
        ["compiler/src/FreexComp.Compiler/Toolchain/ProcessRunner.cs"] = "C-319",
        ["compiler/tests/Shared/NativeRun.cs"] = "C-319",
        ["compiler/tests/Shared/DllScenario.cs"] = "C-319",
        ["compiler/tests/FreexComp.Compiler.GoldenTests/GoldenHarness.cs"] = "C-319",
        ["compiler/tests/FreexComp.Compiler.GoldenTests/OpcProcessTests.cs"] = "C-319",
        ["compiler/tests/FreexComp.Compiler.GoldenTests/StackProbeImportTests.cs"] = "C-319",
        ["compiler/tests/FreexComp.Compiler.Tests/Emit/DeterminismTests.cs"] = "C-319",
        ["rtl/tests/Harness/GoldenRunner.cs"] = "R-233",
        ["rtl/tests/Harness/ImportTableTests.cs"] = "R-233",
        ["ui-framework/tests/Harness/GuiHarness.cs"] = "U-142",
        ["ui-framework/tests/Harness/LayoutCasesTheory.cs"] = "U-142",
        ["data/tests/FreexComp.Data.Tests/GoldenProgramRunner.cs"] = "A-080",
        ["ide/tests/FreexComp.Ide.Tests/Infrastructure/SkiaProcess.cs"] = "I-262",
        ["ide/tests/FreexComp.Ide.Tests/EndToEnd/IdeAndPbuildParityTests.cs"] = "I-262",
        ["ide/tests/FreexComp.Ide.Tests/EndToEnd/LayoutRoundTrip.cs"] = "I-262",
        ["ide/tests/FreexComp.Ide.Tests/Platform/ConsoleHostTests.cs"] = "I-262",
        ["debugger/tests/FreexComp.Debugger.IntegrationTests/TestSupport/IntegrationFixture.cs"] = "G-086",
        ["debugger/tests/FreexComp.Debugger.IntegrationTests/IntegrationFixtureTests.cs"] = "G-086",
        ["debugger/tests/FreexComp.Debugger.Tests/ProcessLeakFixtureTests.cs"] = "G-086",
        ["debugger/tests/spike/runner/DapClient.cs"] = "G-086",
        ["debugger/tests/FreexComp.Debugger.Tests/StreamPumpTests.cs"] = "G-086", // reads the fake adapter's stream; G-086 decides
    };

    [Theory]
    [InlineData("var o = p.StandardOutput.ReadToEndAsync(ct);")]
    [InlineData("var e = process.StandardError.ReadLineAsync();")]
    [InlineData("await p.StandardOutput.BaseStream.CopyToAsync(sink);")]
    [InlineData("process.BeginErrorReadLine();")]
    [Trait("Req", "L-022")]
    public void PatternFindsAsyncPipeReads(string line) => Assert.Matches(AsyncPipeRead(), line);

    [Theory]
    [InlineData("var o = ChildPipe.ReadToEndOnThread(p.StandardOutput, \"stdout\");")]
    [InlineData("var text = process.StandardOutput.ReadToEnd();")]
    [InlineData("var line = await pipe.ReadLineAsync();")] // a pipe the test created itself, not a child's stdio
    [Trait("Req", "L-022")]
    public void PatternIgnoresBlockingAndOwnPipes(string line) => Assert.DoesNotMatch(AsyncPipeRead(), line);

    [Fact]
    [Trait("Req", "L-022")]
    public void ChildPipeReadsAreNotAsync()
    {
        var found = Directory.EnumerateFiles(RepoPaths.Root, "*.cs", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(RepoPaths.Root, p).Replace('\\', '/'))
            .Where(p => !p.Split('/').Any(part => part is "bin" or "obj" or "third_party" or ".git"))
            .Where(p => AsyncPipeRead().IsMatch(File.ReadAllText(RepoPaths.Combine(p))))
            .Order(StringComparer.Ordinal)
            .ToList();

        var unexpected = found.Where(p => !Open.ContainsKey(p) && p != "tests/policy/FreexComp.Policy.Tests/ChildPipeReadsTests.cs").ToList();
        Assert.True(unexpected.Count == 0, "Asynchronous child-pipe reads (use a dedicated thread): " + string.Join(", ", unexpected));

        var fixedSites = Open.Keys.Where(p => !found.Contains(p, StringComparer.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToList();
        Assert.True(fixedSites.Count == 0, "Remove from the open list (fixed): " + string.Join(", ", fixedSites));
    }
}
