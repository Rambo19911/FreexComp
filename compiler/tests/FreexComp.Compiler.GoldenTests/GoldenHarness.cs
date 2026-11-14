using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FreexComp.Compiler.GoldenTests;

/// <summary>One golden test (CMP-900): <c>&lt;milestone&gt;/&lt;Name&gt;.pas</c> or <c>&lt;milestone&gt;/&lt;Name&gt;/&lt;Name&gt;.pas</c>.</summary>
/// <param name="Id">Display name <c>&lt;milestone&gt;/&lt;Name&gt;</c> (CMP-901).</param>
public sealed record GoldenCase(string Id, string SourcePath)
{
    public string Name => Path.GetFileNameWithoutExtension(SourcePath);

    /// <summary>The sibling expectation file with <paramref name="extension"/> (e.g. ".out"), or null when absent.</summary>
    public string? File(string extension) => Path.ChangeExtension(SourcePath, extension) is var path && System.IO.File.Exists(path) ? path : null;
}

/// <summary>The extra <c>opc</c> options (<c>// opc:</c>) and the <c>-O2</c> exemption (<c>// opc-skip-O2:</c>) of a test.</summary>
public sealed record GoldenHeader(ImmutableArray<string> Options, string? SkipO2Reason);

/// <summary>Directory scan of the golden corpus (CMP-900, 901) and the theory rows with the <c>-O2</c> dimension (CMP-904).</summary>
public static class GoldenCorpus
{
    /// <summary>Every test under <paramref name="root"/>, ordered by id. Other <c>.pas</c> files of a multi-unit folder are units, not tests.</summary>
    public static ImmutableArray<GoldenCase> Discover(string root)
    {
        var cases = new List<GoldenCase>();
        foreach (var milestone in Directory.EnumerateDirectories(root))
        {
            var milestoneName = Path.GetFileName(milestone);
            foreach (var source in Directory.EnumerateFiles(milestone, "*.pas"))
            {
                cases.Add(new GoldenCase(milestoneName + "/" + Path.GetFileNameWithoutExtension(source), source));
            }

            foreach (var folder in Directory.EnumerateDirectories(milestone))
            {
                var main = Path.Combine(folder, Path.GetFileName(folder) + ".pas");
                if (System.IO.File.Exists(main))
                {
                    cases.Add(new GoldenCase(milestoneName + "/" + Path.GetFileName(folder), main));
                }
            }
        }

        return [.. cases.OrderBy(c => c.Id, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Reads the directive comments from the leading <c>//</c> lines of the file (the header block that starts with
    /// <c>// covers:</c>, CMP-905); comments after the first non-comment line are ignored.
    /// </summary>
    public static GoldenHeader ReadHeader(string text)
    {
        var options = ImmutableArray.CreateBuilder<string>();
        string? skip = null;
        foreach (var raw in text.TrimStart('\uFEFF').ReplaceLineEndings("\n").Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("//", StringComparison.Ordinal))
            {
                break;
            }

            var comment = line[2..].Trim();
            if (comment.StartsWith("opc:", StringComparison.Ordinal))
            {
                options.AddRange(comment["opc:".Length..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            }
            else if (comment.StartsWith("opc-skip-O2:", StringComparison.Ordinal))
            {
                skip = comment["opc-skip-O2:".Length..].Trim();
            }
        }

        return new GoldenHeader(options.ToImmutable(), skip);
    }

    public static GoldenHeader ReadHeader(GoldenCase @case) => ReadHeader(System.IO.File.ReadAllText(@case.SourcePath));

    /// <summary>One row per case at <c>-O0</c>, plus one at <c>-O2</c> (trait <c>Opt=O2</c>) unless the case opts out (CMP-904).</summary>
    public static IEnumerable<TheoryDataRow<string, string>> Rows(IEnumerable<GoldenCase> cases)
    {
        foreach (var @case in cases)
        {
            yield return new TheoryDataRow<string, string>(@case.Id, "O0") { TestDisplayName = @case.Id };
            if (ReadHeader(@case).SkipO2Reason is null)
            {
                var o2 = new TheoryDataRow<string, string>(@case.Id, "O2") { TestDisplayName = @case.Id + " -O2" };
                o2.Traits["Opt"] = ["O2"];
                yield return o2;
            }
        }
    }
}

/// <summary>Comparison rules of CMP-901 / compiler-cli.md §5.</summary>
public static partial class GoldenText
{
    /// <summary>CRLF → LF; everything else (trailing whitespace, final newline) is kept.</summary>
    public static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>The path in front of each diagnostic line (<c>path(line,col): severity CODE:</c>) reduced to its file name.</summary>
    public static string ReducePaths(string text) => DiagnosticPath().Replace(text, m => Path.GetFileName(m.Groups["path"].Value) + m.Groups["rest"].Value);

    [GeneratedRegex(@"^(?<path>[^\n]*?)(?<rest>\(\d+,\d+\): (?:error|warning|hint) [A-Z]\d{4}: )", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex DiagnosticPath();
}

/// <summary>Where the harness finds the corpus, <c>opc</c>, the RTL (CMP-903) and the pinned toolchain.</summary>
public sealed record GoldenSetup(string OpcPath, string RtlDirectory, string ToolchainDirectory)
{
    public static string RepositoryRoot { get; } = FindRoot();

    public static string CorpusDirectory => Path.Combine(RepositoryRoot, "tests", "compiler");

    public static GoldenSetup Default { get; } = new(
        Path.Combine(AppContext.BaseDirectory, "opc.exe"),
        SelectRtl(RepositoryRoot, Environment.GetEnvironmentVariable("OPC_TEST_RTL")),
        Path.Combine(RepositoryRoot, "third_party", "llvm", "bin"));

    /// <summary>CMP-903: <c>OPC_TEST_RTL</c>, else <c>rtl/src</c> when it holds <c>System.pas</c>, else <c>compiler/tests/minirtl</c>.</summary>
    public static string SelectRtl(string root, string? environment)
    {
        if (!string.IsNullOrEmpty(environment))
        {
            return environment;
        }

        var rtl = Path.Combine(root, "rtl", "src");
        return System.IO.File.Exists(Path.Combine(rtl, "System.pas")) ? rtl : Path.Combine(root, "compiler", "tests", "minirtl");
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "FreexComp.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("FreexComp.sln not found above " + AppContext.BaseDirectory);
    }
}

/// <summary>Runs one golden case (CMP-901, 902): <c>opc</c> as a process, then the program; returns null or what failed.</summary>
public static class GoldenRunner
{
    /// <summary>compiler-cli.md §5: per process (opc and the program).</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private static readonly string[] RequiredTools = ["clang.exe", "lld-link.exe", "llvm-dlltool.exe"];

    /// <summary>Null when the case has a valid set of expectation files.</summary>
    public static string? CheckExpectations(GoldenCase @case)
    {
        var (output, error, diag) = (@case.File(".out"), @case.File(".err"), @case.File(".diag"));
        var name = @case.Name;
        return (output, error) switch
        {
            (null, null) => $"{@case.Id}: neither {name}.out nor {name}.err exists",
            (not null, not null) => $"{@case.Id}: both {name}.out and {name}.err exist",
            (null, _) when diag is not null => $"{@case.Id}: {name}.diag needs {name}.out (CMP-902)",
            _ => null,
        };
    }

    public static async Task<string?> RunAsync(GoldenCase @case, bool o2, GoldenSetup setup, CancellationToken ct)
    {
        if (CheckExpectations(@case) is { } invalid)
        {
            return invalid;
        }

        // CMP-910: a missing toolchain fails the case, it never skips it
        var missing = RequiredTools.Select(t => Path.Combine(setup.ToolchainDirectory, t)).Append(setup.OpcPath)
            .Where(p => !System.IO.File.Exists(p)).ToList();
        if (missing.Count > 0)
        {
            return $"Toolchain missing: {string.Join(", ", missing)} (run scripts/bootstrap.ps1).";
        }

        var temp = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var failure = await RunInAsync(@case, o2, setup, temp, ct).ConfigureAwait(false);
        if (failure is null)
        {
            Directory.Delete(temp, recursive: true);                                // kept on failure for inspection (TESTING.md rule 3)
        }

        return failure is null ? null : $"{@case.Id}{(o2 ? " -O2" : "")}: {failure}\n(work directory: {temp})";
    }

    private static async Task<string?> RunInAsync(GoldenCase @case, bool o2, GoldenSetup setup, string temp, CancellationToken ct)
    {
        var exe = Path.Combine(temp, @case.Name + ".exe");
        List<string> args =
        [
            @case.SourcePath, "-o", exe, "--obj-dir", Path.Combine(temp, "obj"), "--rtl", setup.RtlDirectory,
            "--toolchain", setup.ToolchainDirectory, o2 ? "-O2" : "-O0",
            .. GoldenCorpus.ReadHeader(@case).Options,
        ];

        // the source folder is the current directory, so relative paths in "// opc:" options resolve against it
        var opc = await RunProcessAsync(setup.OpcPath, args, Path.GetDirectoryName(@case.SourcePath)!, [], ct).ConfigureAwait(false);
        if (opc.ExitCode is null)
        {
            return $"opc timed out after {Timeout.TotalSeconds} s";
        }

        var diagnostics = GoldenText.ReducePaths(GoldenText.Normalize(opc.StdOut));
        if (@case.File(".err") is { } err)
        {
            if (opc.ExitCode != 1)
            {
                return $"opc exit code {opc.ExitCode}, expected 1\n{diagnostics}";
            }

            return Compare("diagnostics (stdout of opc)", ReadText(err), diagnostics);
        }

        if (opc.ExitCode != 0)
        {
            return $"opc exit code {opc.ExitCode}, expected 0\n{diagnostics}";
        }

        if (@case.File(".diag") is { } diag && Compare("diagnostics (stdout of opc)", ReadText(diag), diagnostics) is { } diagFailure)
        {
            return diagFailure;
        }

        var input = @case.File(".in") is { } inPath ? System.IO.File.ReadAllBytes(inPath) : [];
        var run = await RunProcessAsync(exe, [], temp, input, ct).ConfigureAwait(false);
        if (run.ExitCode is null)
        {
            return $"program timed out after {Timeout.TotalSeconds} s";
        }

        var expectedExit = @case.File(".exit") is { } exitPath ? int.Parse(System.IO.File.ReadAllText(exitPath).Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) : 0;
        var stdout = GoldenText.Normalize(run.StdOut);
        if (run.ExitCode != expectedExit)
        {
            return $"program exit code {run.ExitCode}, expected {expectedExit}\nstdout:\n{stdout}";
        }

        return Compare("program stdout", ReadText(@case.File(".out")!), stdout);
    }

    private static string ReadText(string path) => GoldenText.Normalize(Encoding.UTF8.GetString(System.IO.File.ReadAllBytes(path)));

    private static string? Compare(string what, string expected, string actual) =>
        string.Equals(expected, actual, StringComparison.Ordinal) ? null : $"{what} differs\n--- expected\n{Visible(expected)}\n--- actual\n{Visible(actual)}";

    /// <summary>Makes line ends and trailing whitespace visible in failure messages.</summary>
    private static string Visible(string text) => text.Replace("\t", "\\t", StringComparison.Ordinal).Replace(" \n", "·\n", StringComparison.Ordinal).Replace("\n", "\\n\n", StringComparison.Ordinal);

    /// <summary>Runs a process with <paramref name="stdin"/> as its whole input; ExitCode is null after the timeout. The process tree is killed in <c>finally</c>.</summary>
    private static async Task<(int? ExitCode, string StdOut)> RunProcessAsync(string file, IReadOnlyList<string> args, string workingDirectory, byte[] stdin, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(file)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        using var process = Process.Start(psi)!;
        try
        {
            var stdout = new MemoryStream();
            var copyOut = process.StandardOutput.BaseStream.CopyToAsync(stdout, timeout.Token);
            var drainErr = process.StandardError.BaseStream.CopyToAsync(Stream.Null, timeout.Token);   // stderr is ignored (CMP-901)
            try
            {
                await process.StandardInput.BaseStream.WriteAsync(stdin, timeout.Token).ConfigureAwait(false);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // the program exited without reading its input
            }

            await Task.WhenAll(copyOut, drainErr).ConfigureAwait(false);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return (process.ExitCode, Encoding.UTF8.GetString(stdout.ToArray()));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (null, "");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }
}
