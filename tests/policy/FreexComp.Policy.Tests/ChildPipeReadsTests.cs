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
    };

    /// <summary>The area guard tests: they hold the forbidden calls as samples.</summary>
    private static readonly HashSet<string> Guards = new(StringComparer.OrdinalIgnoreCase)
    {
        "tests/policy/FreexComp.Policy.Tests/ChildPipeReadsTests.cs",
        "ide/tests/FreexComp.Ide.Tests/TestSupportTests.cs",
        "compiler/tests/FreexComp.Compiler.Tests/Toolchain/ProcessRunnerTests.cs",
        "debugger/tests/FreexComp.Debugger.Tests/ChildPipeReadScanTests.cs",
        "data/tests/FreexComp.Data.Tests/ChildPipeTests.cs",
        "rtl/tests/Harness/ChildPipeTests.cs",
        "ui-framework/tests/Harness/PipeCaptureTests.cs",
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

        var unexpected = found.Where(p => !Open.ContainsKey(p) && !Guards.Contains(p)).ToList();
        Assert.True(unexpected.Count == 0, "Asynchronous child-pipe reads (use a dedicated thread): " + string.Join(", ", unexpected));

        var fixedSites = Open.Keys.Where(p => !found.Contains(p, StringComparer.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToList();
        Assert.True(fixedSites.Count == 0, "Remove from the open list (fixed): " + string.Join(", ", fixedSites));
    }
}
