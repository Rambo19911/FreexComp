using System.Diagnostics;
using System.Text;

namespace FreexComp.Data.Tests;

/// <summary>
/// Compiles and runs one golden program (DAT-400, DAT-401): opc with <c>-U data/src -U data/tests/programs/lib</c>
/// (+ the UI framework sources), the SQLite library copied next to the exe, the exe started in a fresh, empty
/// working directory with a copy of <c>data/tests/fixtures</c> beside it (the program reads <c>../fixtures/NAME</c>);
/// exit code 0 and stdout equal to <c>NAME.out</c> after CRLF→LF. Returns null or what failed.
/// The time limits are safety nets against a hung process, not performance checks.
/// </summary>
public static class GoldenProgramRunner
{
    public static readonly TimeSpan CompileLimit = TimeSpan.FromSeconds(600);

    public static readonly TimeSpan RunLimit = TimeSpan.FromSeconds(120);

    private static readonly string[] RequiredTools = ["clang.exe", "lld-link.exe", "llvm-dlltool.exe"];

    /// <summary>DAT-401, DAT-409: what is missing of toolchain, opc and the SQLite library — a failure, never a skip.</summary>
    public static string? MissingPrerequisites(string toolchainDirectory, string opcPath, string sqliteLibrary)
    {
        var missing = RequiredTools.Select(t => Path.Combine(toolchainDirectory, t)).Append(opcPath).Append(sqliteLibrary)
            .Where(p => !File.Exists(p)).ToList();
        return missing.Count == 0 ? null : $"missing: {string.Join(", ", missing)} (run scripts/bootstrap.ps1)";
    }

    public static async Task<string?> RunAsync(DataProgram program, CancellationToken ct)
    {
        if (MissingPrerequisites(DataCorpus.ToolchainDirectory, DataCorpus.OpcPath, DataCorpus.SqliteLibrary) is { } missing)
        {
            return $"{program.Name}: {missing}";
        }

        var work = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", "data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var failure = await RunInAsync(program, work, ct).ConfigureAwait(false);
        if (failure is null)
        {
            TryDelete(work);                                                        // kept on failure for inspection
            return null;
        }

        return $"{program.Name}: {failure}\n(work directory: {work})";
    }

    /// <summary>Null when <paramref name="actual"/> equals <paramref name="expected"/> byte-for-byte after CRLF→LF; else both texts with visible line ends.</summary>
    public static string? Compare(string expected, string actual)
    {
        expected = Normalize(expected);
        actual = Normalize(actual);
        return string.Equals(expected, actual, StringComparison.Ordinal)
            ? null
            : $"program stdout differs\n--- expected\n{Visible(expected)}\n--- actual\n{Visible(actual)}";
    }

    private static async Task<string?> RunInAsync(DataProgram program, string work, CancellationToken ct)
    {
        if (!File.Exists(program.ExpectedPath))
        {
            return $"{Path.GetFileName(program.ExpectedPath)} does not exist";
        }

        var header = DataCorpus.ReadHeader(await File.ReadAllTextAsync(program.SourcePath, ct).ConfigureAwait(false));
        var bin = Directory.CreateDirectory(Path.Combine(work, "bin")).FullName;
        var exe = Path.Combine(bin, program.Name + ".exe");
        List<string> args =
        [
            program.SourcePath, "-U", DataCorpus.SourceDirectory, "-U", DataCorpus.LibDirectory, "-U", DataCorpus.UiSourceDirectory,
            "-o", exe, "--obj-dir", Path.Combine(work, "obj"),
            "--rtl", DataCorpus.RtlDirectory, "--toolchain", DataCorpus.ToolchainDirectory, .. header.OpcOptions,
        ];

        var opc = await RunProcessAsync(DataCorpus.OpcPath, args, Path.GetDirectoryName(program.SourcePath)!, CompileLimit, ct).ConfigureAwait(false);
        if (opc.Stopped is { } opcStopped)
        {
            return "opc " + opcStopped;
        }

        if (opc.ExitCode != 0)
        {
            return $"opc exit code {opc.ExitCode}, expected 0\n{Normalize(opc.StdOut)}{opc.StdErr}";
        }

        File.Copy(DataCorpus.SqliteLibrary, Path.Combine(bin, Path.GetFileName(DataCorpus.SqliteLibrary)), overwrite: true);
        var runDirectory = Directory.CreateDirectory(Path.Combine(work, "run")).FullName;      // fresh and empty (DAT-401, DAT-403)
        CopyFixtures(Path.Combine(work, "fixtures"));                                          // a copy: programs cannot change the originals
        var run = await RunProcessAsync(exe, [], runDirectory, RunLimit, ct).ConfigureAwait(false);
        if (run.Stopped is { } runStopped)
        {
            return "program " + runStopped;
        }

        if (run.ExitCode != 0)
        {
            return $"program exit code {run.ExitCode}, expected 0\n--- stdout\n{Normalize(run.StdOut)}\n--- stderr\n{run.StdErr}";
        }

        var expected = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(program.ExpectedPath, ct).ConfigureAwait(false));
        return Compare(expected, run.StdOut);
    }

    /// <summary>Copies the files of <c>data/tests/fixtures</c> (when it exists) into <paramref name="target"/>, beside the run directory.</summary>
    private static void CopyFixtures(string target)
    {
        if (!Directory.Exists(DataCorpus.FixturesDirectory))
        {
            return;
        }

        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(DataCorpus.FixturesDirectory))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        }
    }

    internal static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // the just-exited program's image can stay locked for a moment; left behind under %TEMP%\FreexComp.Tests
        }
    }

    internal static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    internal static string Visible(string text) => text.Replace("\t", "\\t", StringComparison.Ordinal).Replace(" \n", "·\n", StringComparison.Ordinal).Replace("\n", "\\n\n", StringComparison.Ordinal);

    internal readonly record struct ProcessResult(int ExitCode, string StdOut, string StdErr, string? Stopped);

    /// <summary>Runs a process to its end, or stops its whole tree at <paramref name="limit"/> or on the test's cancellation.</summary>
    internal static async Task<ProcessResult> RunProcessAsync(string file, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan limit, CancellationToken ct,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var psi = new ProcessStartInfo(file)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
        {
            psi.Environment[name] = value;
        }

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stop.CancelAfter(limit);
        using var process = Process.Start(psi)!;
        try
        {
            process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEndAsync(stop.Token);
            var stderr = process.StandardError.ReadToEndAsync(stop.Token);
            await process.WaitForExitAsync(stop.Token).ConfigureAwait(false);
            return new(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false), null);
        }
        catch (OperationCanceledException)
        {
            var reason = ct.IsCancellationRequested ? "test cancelled" : $"time limit {limit.TotalSeconds:F0} s reached";
            return new(-1, "", "", $"{reason}: {Path.GetFileName(file)} stopped");
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
