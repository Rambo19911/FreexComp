using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FreexComp.Rtl.Tests.Harness;

/// <summary>One RTL test program <c>rtl/tests/&lt;group&gt;/&lt;TestName&gt;.pas</c>; <see cref="Id"/> is <c>&lt;group&gt;/&lt;TestName&gt;</c> (RTL-900).</summary>
public sealed record RtlCase(string Id, string SourcePath)
{
    public string Name => Path.GetFileNameWithoutExtension(SourcePath);

    /// <summary>The sibling file <c>&lt;TestName&gt;&lt;suffix&gt;</c> (suffix e.g. <c>.out</c>, <c>.2.exit</c>, <c>.err.txt</c>), or null when absent.</summary>
    public string? File(string suffix) => Path.Combine(Path.GetDirectoryName(SourcePath)!, Name + suffix) is var path && System.IO.File.Exists(path) ? path : null;
}

/// <summary>
/// Markers of the leading <c>//</c> comment block: <c>// phase: N</c> (default 2, RTL-908), <c>// covers: …</c> (RTL-905),
/// <c>// opc: …</c> (extra opc options, RTL-901), <c>// opc-run: tempdir | variants v… | rawargs text</c> (RTL-903, testing.md §3.1).
/// </summary>
public sealed record RtlHeader(int Phase, ImmutableArray<string> Covers, ImmutableArray<string> OpcOptions, bool TempDir, ImmutableArray<string> Variants, string? RawArgs);

/// <summary>Where the runner finds opc, the RTL, the pinned toolchain and the shared test units (<c>-U rtl/tests</c>).</summary>
public sealed record RtlSetup(string OpcPath, string RtlDirectory, string ToolchainDirectory, string UnitDirectory)
{
    public static string RepositoryRoot { get; } = FindRoot();

    public static string TestsDirectory => Path.Combine(RepositoryRoot, "rtl", "tests");

    /// <summary>The programs are compiled against this area's RTL, <c>rtl/src</c> (RTL-008).</summary>
    public static RtlSetup Default { get; } = new(
        Path.Combine(AppContext.BaseDirectory, "opc.exe"),
        Path.Combine(RepositoryRoot, "rtl", "src"),
        Path.Combine(RepositoryRoot, "third_party", "llvm", "bin"),
        TestsDirectory);

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

/// <summary>Discovery (RTL-900), header markers and theory rows (RTL-908).</summary>
public static class RtlCorpus
{
    private static readonly string[] NotGroups = ["bin", "obj", "Harness", "TestResults", "perf"];   // perf: PerfGuardTests (RTL-920)

    /// <summary>Every <c>.pas</c> in a group folder directly under <paramref name="root"/>, ordered by id. Files in <paramref name="root"/> itself are shared units.</summary>
    public static ImmutableArray<RtlCase> Discover(string root)
    {
        var cases = new List<RtlCase>();
        foreach (var group in Directory.EnumerateDirectories(root))
        {
            var groupName = Path.GetFileName(group);
            if (NotGroups.Contains(groupName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var source in Directory.EnumerateFiles(group, "*.pas"))
            {
                cases.Add(new RtlCase(groupName + "/" + Path.GetFileNameWithoutExtension(source), source));
            }
        }

        return [.. cases.OrderBy(c => c.Id, StringComparer.Ordinal)];
    }

    public static RtlHeader ReadHeader(string text)
    {
        var phase = 2;
        var covers = ImmutableArray.CreateBuilder<string>();
        var options = ImmutableArray.CreateBuilder<string>();
        var variants = ImmutableArray.CreateBuilder<string>();
        var tempDir = false;
        string? rawArgs = null;
        foreach (var raw in text.TrimStart('\uFEFF').ReplaceLineEndings("\n").Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("//", StringComparison.Ordinal))
            {
                break;
            }

            var comment = line[2..].Trim();
            if (Value(comment, "phase:") is { } p)
            {
                phase = int.Parse(p, NumberStyles.None, CultureInfo.InvariantCulture);
            }
            else if (Value(comment, "covers:") is { } c)
            {
                covers.AddRange(c.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries));
            }
            else if (Value(comment, "opc:") is { } o)
            {
                options.AddRange(Words(o));
            }
            else if (Value(comment, "opc-run:") is { } r)
            {
                var keyword = Words(r).FirstOrDefault();
                var rest = keyword is null ? "" : r[keyword.Length..].Trim();
                switch (keyword)
                {
                    case "tempdir":
                        tempDir = true;
                        break;
                    case "variants":
                        variants.AddRange(Words(rest));
                        break;
                    case "rawargs":
                        rawArgs = rest;
                        break;
                    default:
                        throw new FormatException($"unknown '// opc-run:' marker '{r}'");
                }
            }
        }

        return new RtlHeader(phase, covers.ToImmutable(), options.ToImmutable(), tempDir, variants.ToImmutable(), rawArgs);
    }

    public static RtlHeader ReadHeader(RtlCase @case) => ReadHeader(System.IO.File.ReadAllText(@case.SourcePath));

    /// <summary>The rows of the phase-<paramref name="phase"/> theory; each row carries the <c>Req</c> traits of its <c>// covers:</c> header.</summary>
    public static IEnumerable<TheoryDataRow<string>> Rows(IEnumerable<RtlCase> cases, int phase)
    {
        foreach (var @case in cases)
        {
            var header = ReadHeader(@case);
            if (header.Phase != phase)
            {
                continue;
            }

            var row = new TheoryDataRow<string>(@case.Id) { TestDisplayName = @case.Id };
            if (header.Covers.Length > 0)
            {
                row.Traits["Req"] = [.. header.Covers];
            }

            yield return row;
        }
    }

    private static string? Value(string comment, string key) => comment.StartsWith(key, StringComparison.Ordinal) ? comment[key.Length..].Trim() : null;

    private static string[] Words(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>Compiles and runs one program per the golden contract (RTL-901, 902, 903); returns null or what failed.</summary>
public static class GoldenRunner
{
    /// <summary>compiler-cli.md §5: per process (opc and each program run).</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private static readonly string[] RequiredTools = ["clang.exe", "lld-link.exe", "llvm-dlltool.exe"];

    /// <summary>Compiles and runs <paramref name="case"/>; null on success. <paramref name="runTime"/> receives the wall time of each program run (not of opc).</summary>
    public static async Task<string?> RunAsync(RtlCase @case, RtlSetup setup, CancellationToken ct, Action<TimeSpan>? runTime = null)
    {
        // RTL-909: a missing toolchain fails the test, it never skips it
        var missing = RequiredTools.Select(t => Path.Combine(setup.ToolchainDirectory, t)).Append(setup.OpcPath)
            .Where(p => !System.IO.File.Exists(p)).ToList();
        if (missing.Count > 0)
        {
            return $"{@case.Id}: toolchain missing: {string.Join(", ", missing)} (run scripts/bootstrap.ps1).";
        }

        var header = RtlCorpus.ReadHeader(@case);
        var work = NewTempDirectory();
        var failure = await RunInAsync(@case, header, setup, work, runTime, ct).ConfigureAwait(false);
        if (failure is null)
        {
            TryDelete(work);                                                        // kept on failure for inspection (TESTING.md rule 3)
        }

        return failure is null ? null : $"{@case.Id}: {failure}\n(work directory: {work})";
    }

    /// <summary>RTL-902: null when every non-empty line of <paramref name="patterns"/> (a regex) matches some line of <paramref name="stderr"/>.</summary>
    public static string? CheckStderr(string patterns, string stderr)
    {
        var lines = Normalize(stderr).Split('\n');
        var unmatched = Normalize(patterns).Split('\n').Where(p => p.Length > 0)
            .Where(p => !lines.Any(l => Regex.IsMatch(l, p, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5)))).ToList();
        return unmatched.Count == 0 ? null : $"stderr matches no line for pattern(s) {string.Join(", ", unmatched)}\n--- stderr\n{stderr}";
    }

    /// <summary>A Windows command line: each argument quoted where needed (CommandLineToArgvW rules), then <paramref name="raw"/> verbatim.</summary>
    public static string CommandLine(IReadOnlyList<string> args, string? raw)
    {
        var parts = args.Select(Quote).ToList();
        if (!string.IsNullOrEmpty(raw))
        {
            parts.Add(raw);
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Best-effort clean-up: the just-exited program's image can stay locked for a moment, which must not turn a
    /// passing test into a failure.
    /// </summary>
    public static void TryDelete(string directory)
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

    private static async Task<string?> RunInAsync(RtlCase @case, RtlHeader header, RtlSetup setup, string work, Action<TimeSpan>? runTime, CancellationToken ct)
    {
        var exe = Path.Combine(work, @case.Name + ".exe");
        List<string> args =
        [
            @case.SourcePath, "-U", setup.UnitDirectory, "-o", exe, "--obj-dir", Path.Combine(work, "obj"),
            "--rtl", setup.RtlDirectory, "--toolchain", setup.ToolchainDirectory, .. header.OpcOptions,
        ];

        // the source folder is the current directory, so relative paths in "// opc:" options resolve against it
        var opc = await RunProcessAsync(setup.OpcPath, CommandLine(args, null), Path.GetDirectoryName(@case.SourcePath)!, [], ct).ConfigureAwait(false);
        if (opc is null)
        {
            return $"opc timed out after {Timeout.TotalSeconds} s";
        }

        if (opc.Value.ExitCode != 0)
        {
            return $"opc exit code {opc.Value.ExitCode}, expected 0\n{Normalize(opc.Value.StdOut)}{opc.Value.StdErr}";
        }

        if (header.Variants.IsEmpty)
        {
            return await RunOnceAsync(@case, header, exe, work, null, runTime, ct).ConfigureAwait(false);
        }

        foreach (var variant in header.Variants)
        {
            if (await RunOnceAsync(@case, header, exe, work, variant, runTime, ct).ConfigureAwait(false) is { } failure)
            {
                return $"variant {variant}: {failure}";
            }
        }

        return null;
    }

    /// <summary>One run: argument list = [temp dir] [variant] then raw arguments; expectation files <c>.&lt;variant&gt;.*</c> first, else unsuffixed.</summary>
    private static async Task<string?> RunOnceAsync(RtlCase @case, RtlHeader header, string exe, string work, string? variant, Action<TimeSpan>? runTime, CancellationToken ct)
    {
        string? Expect(string suffix) => (variant is null ? null : @case.File("." + variant + suffix)) ?? @case.File(suffix);

        var outPath = variant is null ? @case.File(".out") : @case.File("." + variant + ".out");
        if (outPath is null)
        {
            return $"{@case.Name}{(variant is null ? "" : "." + variant)}.out does not exist";
        }

        var programArgs = new List<string>();
        var tempDir = header.TempDir ? NewTempDirectory() : null;
        if (tempDir is not null)
        {
            programArgs.Add(tempDir);
        }

        if (variant is not null)
        {
            programArgs.Add(variant);
        }

        var input = Expect(".in") is { } inPath ? System.IO.File.ReadAllBytes(inPath) : [];
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var run = await RunProcessAsync(exe, CommandLine(programArgs, header.RawArgs), work, input, ct).ConfigureAwait(false);
        runTime?.Invoke(clock.Elapsed);
        var failure = Check(run);
        if (tempDir is not null && failure is null)
        {
            TryDelete(tempDir);
        }

        return failure;

        string? Check((int ExitCode, string StdOut, string StdErr)? result)
        {
            if (result is not { } r)
            {
                return $"program timed out after {Timeout.TotalSeconds} s";
            }

            var expectedExit = Expect(".exit") is { } exitPath
                ? int.Parse(System.IO.File.ReadAllText(exitPath).Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)
                : 0;
            var stdout = Normalize(r.StdOut);
            if (r.ExitCode != expectedExit)
            {
                return $"program exit code {r.ExitCode}, expected {expectedExit}\n--- stdout\n{stdout}\n--- stderr\n{r.StdErr}";
            }

            var expected = Normalize(Encoding.UTF8.GetString(System.IO.File.ReadAllBytes(outPath)));
            if (!string.Equals(expected, stdout, StringComparison.Ordinal))
            {
                return $"program stdout differs\n--- expected\n{Visible(expected)}\n--- actual\n{Visible(stdout)}";
            }

            return Expect(".err.txt") is { } errPath ? CheckStderr(System.IO.File.ReadAllText(errPath, Encoding.UTF8), r.StdErr) : null;
        }
    }

    private static string NewTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>Makes line ends and trailing whitespace visible in failure messages.</summary>
    private static string Visible(string text) => text.Replace("\t", "\\t", StringComparison.Ordinal).Replace(" \n", "·\n", StringComparison.Ordinal).Replace("\n", "\\n\n", StringComparison.Ordinal);

    private static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '"']) < 0)
        {
            return arg;
        }

        var sb = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var ch in arg)
        {
            if (ch == '\\')
            {
                backslashes++;
                continue;
            }

            sb.Append('\\', ch == '"' ? backslashes * 2 + 1 : backslashes).Append(ch);
            backslashes = 0;
        }

        return sb.Append('\\', backslashes * 2).Append('"').ToString();
    }

    /// <summary>Runs a process with <paramref name="stdin"/> as its whole input; null after the timeout. The process tree is killed in <c>finally</c>.</summary>
    private static async Task<(int ExitCode, string StdOut, string StdErr)?> RunProcessAsync(string file, string arguments, string workingDirectory, byte[] stdin, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(file, arguments)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        using var process = Process.Start(psi)!;
        try
        {
            var stdout = new MemoryStream();
            var stderr = new MemoryStream();
            var copyOut = process.StandardOutput.BaseStream.CopyToAsync(stdout, timeout.Token);
            var copyErr = process.StandardError.BaseStream.CopyToAsync(stderr, timeout.Token);
            try
            {
                await process.StandardInput.BaseStream.WriteAsync(stdin, timeout.Token).ConfigureAwait(false);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // the program exited without reading its input
            }

            await Task.WhenAll(copyOut, copyErr).ConfigureAwait(false);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return (process.ExitCode, Encoding.UTF8.GetString(stdout.ToArray()), Encoding.UTF8.GetString(stderr.ToArray()));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
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
