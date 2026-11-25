using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FreexComp.Tests.Shared;

namespace FreexComp.Compiler.GoldenTests;

/// <summary>One golden test (CMP-900): <c>&lt;milestone&gt;/&lt;Name&gt;.pas</c> or <c>&lt;milestone&gt;/&lt;Name&gt;/&lt;Name&gt;.pas</c>.</summary>
/// <param name="Id">Display name <c>&lt;milestone&gt;/&lt;Name&gt;</c> (CMP-901).</param>
public sealed record GoldenCase(string Id, string SourcePath)
{
    public string Name => Path.GetFileNameWithoutExtension(SourcePath);

    /// <summary>
    /// dll-output.md §10.2: the main file is a library file (its first token after comments is <c>library</c>, LANG-540). Such a
    /// case builds a DLL and has no run step.
    /// </summary>
    public bool IsLibrary => GoldenCorpus.IsLibrarySource(System.IO.File.ReadAllText(SourcePath));

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

    /// <summary>True when the first token of <paramref name="text"/> — after white space and <c>//</c>, <c>{ }</c>, <c>(* *)</c> comments — is <c>library</c>.</summary>
    public static bool IsLibrarySource(string text)
    {
        var i = text.StartsWith('\uFEFF') ? 1 : 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                i++;
            }
            else if (string.CompareOrdinal(text, i, "//", 0, 2) == 0)
            {
                i = text.IndexOf('\n', i) is var end && end < 0 ? text.Length : end + 1;
            }
            else if (text[i] == '{')
            {
                i = text.IndexOf('}', i) is var end && end < 0 ? text.Length : end + 1;
            }
            else if (string.CompareOrdinal(text, i, "(*", 0, 2) == 0)
            {
                i = text.IndexOf("*)", i + 2, StringComparison.Ordinal) is var end && end < 0 ? text.Length : end + 2;
            }
            else
            {
                break;
            }
        }

        const string keyword = "library";
        return string.Compare(text, i, keyword, 0, keyword.Length, StringComparison.OrdinalIgnoreCase) == 0
               && (i + keyword.Length == text.Length || !(char.IsAsciiLetterOrDigit(text[i + keyword.Length]) || text[i + keyword.Length] == '_'));
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

    /// <summary>
    /// CMP-903 (lead decision 2026-11-14): <c>OPC_TEST_RTL</c>, else the mini RTL while
    /// <c>compiler/tests/minirtl/System.pas</c> exists (until the CMP-631 hand-over, C-149 deletes it), else <c>rtl/src</c>.
    /// </summary>
    public static string SelectRtl(string root, string? environment)
    {
        if (!string.IsNullOrEmpty(environment))
        {
            return environment;
        }

        var mini = Path.Combine(root, "compiler", "tests", "minirtl");
        return System.IO.File.Exists(Path.Combine(mini, "System.pas")) ? mini : Path.Combine(root, "rtl", "src");
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
    private static readonly string[] RequiredTools = ["clang.exe", "lld-link.exe", "llvm-dlltool.exe"];

    /// <summary>Null when the case has a valid set of expectation files.</summary>
    public static string? CheckExpectations(GoldenCase @case)
    {
        var (output, error, diag) = (@case.File(".out"), @case.File(".err"), @case.File(".diag"));
        var name = @case.Name;
        if (@case.IsLibrary)
        {
            // dll-output.md §10.2: a library is not run - diagnostics only
            return (output, error, diag) switch
            {
                (not null, _, _) => $"{@case.Id}: {name}.out exists, but a library file is not run",
                (null, null, null) => $"{@case.Id}: neither {name}.err nor {name}.diag exists",
                (null, not null, not null) => $"{@case.Id}: both {name}.err and {name}.diag exist",
                _ => null,
            };
        }

        return (output, error) switch
        {
            (null, null) => $"{@case.Id}: neither {name}.out nor {name}.err exists",
            (not null, not null) => $"{@case.Id}: both {name}.out and {name}.err exist",
            (null, _) when diag is not null => $"{@case.Id}: {name}.diag needs {name}.out (CMP-902)",
            _ => null,
        };
    }

    /// <summary>
    /// compiler-cli.md §5's per-process limit, by the C-249 rule (C-258): opc and the program are each stopped at 20 s of
    /// their own CPU time (<see cref="NativeRun.CpuLimit"/>), at the wall-clock cap <see cref="NativeRun.WallLimit"/>, or
    /// when the test is cancelled; the failure then reports elapsed wall time, CPU time and process state. The program's
    /// limits can be lowered for the harness's own tests.
    /// </summary>
    public static async Task<string?> RunAsync(GoldenCase @case, bool o2, GoldenSetup setup, CancellationToken ct,
        TimeSpan? programCpuLimit = null, TimeSpan? programWallLimit = null)
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
        var programLimits = (programCpuLimit ?? NativeRun.CpuLimit, programWallLimit ?? NativeRun.WallLimit);
        var failure = await RunInAsync(@case, o2, setup, temp, programLimits, ct).ConfigureAwait(false);
        if (failure is null)
        {
            TryDelete(temp);                                                        // kept on failure for inspection (TESTING.md rule 3)
        }

        return failure is null ? null : $"{@case.Id}{(o2 ? " -O2" : "")}: {failure}\n(work directory: {temp})";
    }

    /// <summary>
    /// Best-effort clean-up of a passed case's folder: the just-exited program's image can stay locked for a moment
    /// (the OS or a scanner still holds it), which must not turn a passing case into a failure.
    /// </summary>
    internal static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // left behind under %TEMP%\FreexComp.Tests
        }
    }

    private static async Task<string?> RunInAsync(GoldenCase @case, bool o2, GoldenSetup setup, string temp, (TimeSpan Cpu, TimeSpan Wall) programLimits, CancellationToken ct)
    {
        var isLibrary = @case.IsLibrary;
        var exe = Path.Combine(temp, @case.Name + (isLibrary ? ".dll" : ".exe"));
        List<string> args =
        [
            @case.SourcePath, "-o", exe, "--obj-dir", Path.Combine(temp, "obj"), "--rtl", setup.RtlDirectory,
            "--toolchain", setup.ToolchainDirectory, o2 ? "-O2" : "-O0",
            .. GoldenCorpus.ReadHeader(@case).Options,
        ];

        // the source folder is the current directory, so relative paths in "// opc:" options resolve against it
        var opc = await RunProcessAsync(setup.OpcPath, args, Path.GetDirectoryName(@case.SourcePath)!, [], (NativeRun.CpuLimit, NativeRun.WallLimit), ct).ConfigureAwait(false);
        if (opc.Stopped is { } opcStopped)
        {
            return "opc stopped: " + opcStopped;
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

        if (isLibrary)
        {
            return null;                                                            // dll-output.md §10.2: no run step
        }

        var input = @case.File(".in") is { } inPath ? System.IO.File.ReadAllBytes(inPath) : [];
        var run = await RunProcessAsync(exe, [], temp, input, programLimits, ct).ConfigureAwait(false);
        if (run.Stopped is { } programStopped)
        {
            return "program stopped: " + programStopped;
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

    /// <summary>
    /// Runs a process with <paramref name="stdin"/> as its whole input under the C-249 limits (<see cref="RunLimit"/>);
    /// Stopped is the limit's report when a limit or the test's cancellation stopped it. The process tree is killed in
    /// <c>finally</c>, after the report has captured its state.
    /// </summary>
    private static async Task<(int? ExitCode, string StdOut, string? Stopped)> RunProcessAsync(string file, IReadOnlyList<string> args, string workingDirectory, byte[] stdin,
        (TimeSpan Cpu, TimeSpan Wall) limits, CancellationToken ct)
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

        using var limit = new RunLimit(Path.GetFileName(file), limits.Cpu, limits.Wall, ct);
        using var process = Process.Start(psi)!;
        limit.Attach(process.Id);
        try
        {
            var copyOut = PipePump.Bytes(process.StandardOutput.BaseStream, "stdout");       // C-319: each pipe on its own thread
            var drainErr = PipePump.Drain(process.StandardError.BaseStream, "stderr");       // stderr is ignored (CMP-901)
            try
            {
                await process.StandardInput.BaseStream.WriteAsync(stdin, limit.Token).ConfigureAwait(false);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // the program exited without reading its input
            }

            await Task.WhenAll(copyOut, drainErr).WaitAsync(limit.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            return (process.ExitCode, Encoding.UTF8.GetString(await copyOut.ConfigureAwait(false)), null);
        }
        catch (OperationCanceledException) when (limit.Report is { } report)
        {
            return (null, "", report);
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
