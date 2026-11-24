using System.Text.RegularExpressions;

namespace FreexComp.Debugger.Tests;

/// <summary>
/// The redirected pipe of a child process is a synchronous handle: an asynchronous read of it parks a thread-pool
/// thread for as long as the child is silent. No debugger code, product or test, may read such a pipe that way; it
/// reads on a thread of its own (<c>DedicatedThread</c>, <c>StreamPump</c>).
/// </summary>
public sealed partial class ChildPipeReadScanTests
{
    // An asynchronous read straight off the redirected stream (or its base stream), and the event-based line readers.
    [GeneratedRegex(@"\bStandard(Output|Error)\s*\.\s*(BaseStream\s*\.\s*)?(Read\w*Async|CopyToAsync)\b|\bBegin(Output|Error)ReadLine\b")]
    private static partial Regex AsyncPipeRead();

    [Fact]
    public void NoCode_ReadsAChildPipeAsynchronously()
    {
        var root = Path.Combine(RepositoryRoot(), "debugger");
        var separator = Path.DirectorySeparatorChar;
        var files = (string[])[.. new[] { "src", "tests" }
            .SelectMany(part => Directory.EnumerateFiles(Path.Combine(root, part), "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{separator}bin{separator}", StringComparison.OrdinalIgnoreCase)
                && !file.Contains($"{separator}obj{separator}", StringComparison.OrdinalIgnoreCase))];
        Assert.True(files.Length > 50, $"Only {files.Length} source files found under {root}.");

        var hits = new List<string>();
        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (AsyncPipeRead().IsMatch(lines[i]))
                    hits.Add($"{Path.GetRelativePath(root, file)}({i + 1}): {lines[i].Trim()}");
            }
        }

        Assert.True(hits.Count == 0, "Asynchronous reads of a child process pipe:\n" + string.Join("\n", hits));
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreexComp.sln")))
                return directory.FullName;
        }

        throw new InvalidOperationException("FreexComp.sln not found above " + AppContext.BaseDirectory);
    }
}
