using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace FreexComp.Tests.Shared;

/// <summary>One line of a scenario's <c>steps.txt</c>.</summary>
/// <param name="Verb"><c>dll</c>, <c>exe</c>, <c>run</c> or <c>exists</c>.</param>
/// <param name="Target">The source file (<c>dll</c>, <c>exe</c>), the program (<c>run</c>) or the file to look for (<c>exists</c>).</param>
/// <param name="Arguments">Extra <c>opc</c> options (<c>dll</c>, <c>exe</c>) or the program's arguments (<c>run</c>).</param>
/// <param name="Line">1-based line of <c>steps.txt</c>.</param>
internal sealed record DllScenarioStep(string Verb, string Target, ImmutableArray<string> Arguments, int Line);

/// <summary>What the runner needs from its test project.</summary>
/// <param name="OpcPath">The <c>opc</c> executable.</param>
/// <param name="OpcOptions">
/// Options every build step carries after the generated ones and before the step's own: RTL root, toolchain, and whatever the
/// project adds (<c>--target</c>, <c>-U</c> for shared test units).
/// </param>
internal sealed record DllScenarioSetup(string OpcPath, ImmutableArray<string> OpcOptions);

/// <summary>The result of one started process; <c>Stopped</c> is the limit's report when a limit or the cancellation ended it.</summary>
internal sealed record DllScenarioProcessResult(int? ExitCode, string StdOut, string? Stopped);

/// <summary>Starts <paramref name="file"/> with <paramref name="arguments"/> in <paramref name="workingDirectory"/> (replaceable in the harness's own tests).</summary>
internal delegate Task<DllScenarioProcessResult> DllScenarioProcess(string file, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken ct);

/// <summary>
/// The scenario golden harness of dll-output.md §10.3 (task C-290), shared with the RTL test project: a scenario is a folder
/// with sources, <c>steps.txt</c> and one <c>&lt;host&gt;.out</c> per <c>run</c> step. Steps, one per line (blank lines and
/// lines starting with <c>#</c> are ignored):
/// <list type="bullet">
/// <item><c>dll &lt;file.pas&gt; [opc options]</c> / <c>exe &lt;file.pas&gt; [opc options]</c> — <c>opc</c> in the scenario folder with
/// <c>-o &lt;work&gt;\&lt;base&gt;.dll|.exe --obj-dir &lt;work&gt;\obj\&lt;base&gt; -L &lt;work&gt;</c>, the setup's options, <c>-O0|-O2</c>, then
/// the step's options; exit code 0 expected;</item>
/// <item><c>run &lt;name&gt;.exe [arguments]</c> — the program in <c>&lt;work&gt;</c>; stdout (CRLF→LF) must equal
/// <c>&lt;name&gt;.out</c> and the exit code 0, or the number in an optional <c>&lt;name&gt;.exit</c>;</item>
/// <item><c>exists &lt;file&gt;</c> — the file must exist in <c>&lt;work&gt;</c> at that point (after the preceding run has exited).</item>
/// </list>
/// Every process runs under the limits of <see cref="NativeRun"/>. The work directory is fresh per run, deleted after a pass and
/// kept (and named in the failure) otherwise.
/// </summary>
internal static class DllScenario
{
    public const string StepsFile = "steps.txt";

    private static readonly string[] Verbs = ["dll", "exe", "run", "exists"];

    /// <summary>The scenario folders (those holding a <c>steps.txt</c>) directly under <paramref name="root"/>, by name.</summary>
    public static ImmutableArray<string> Discover(string root) =>
        Directory.Exists(root)
            ? [.. Directory.EnumerateDirectories(root).Where(d => File.Exists(Path.Combine(d, StepsFile))).Order(StringComparer.Ordinal)]
            : [];

    /// <summary>
    /// Parses <c>steps.txt</c>. <see cref="FormatException"/> (with the line number) for an unknown verb, a step without target, a
    /// <c>dll</c>/<c>exe</c> target that is no <c>.pas</c> file, a <c>run</c> target that is no <c>.exe</c>, an <c>exists</c>
    /// step with more than a file name or before the first <c>run</c>, and a file without any <c>run</c> step.
    /// </summary>
    public static ImmutableArray<DllScenarioStep> Parse(string text)
    {
        var steps = ImmutableArray.CreateBuilder<DllScenarioStep>();
        var lines = text.TrimStart('﻿').ReplaceLineEndings("\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            FormatException Bad(string what) => new(string.Create(CultureInfo.InvariantCulture, $"{StepsFile}({i + 1}): {what}: '{line}'"));
            if (!Verbs.Contains(words[0], StringComparer.Ordinal))
            {
                throw Bad("unknown verb (expected dll, exe, run or exists)");
            }

            if (words.Length < 2)
            {
                throw Bad("missing file name");
            }

            var step = new DllScenarioStep(words[0], words[1], [.. words.Skip(2)], i + 1);
            switch (step.Verb)
            {
                case "dll" or "exe" when !step.Target.EndsWith(".pas", StringComparison.OrdinalIgnoreCase):
                    throw Bad("a build step names a .pas file");
                case "run" when !step.Target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase):
                    throw Bad("a run step names an .exe file");
                case "exists" when !step.Arguments.IsEmpty:
                    throw Bad("an exists step names one file");
                case "exists" when !steps.Any(s => s.Verb == "run"):
                    throw Bad("an exists step follows a run step");
            }

            steps.Add(step);
        }

        return steps.Any(s => s.Verb == "run")
            ? steps.ToImmutable()
            : throw new FormatException($"{StepsFile}: no run step");
    }

    /// <summary>The <c>opc</c> command line of a build step (see the class remarks).</summary>
    public static ImmutableArray<string> OpcArguments(DllScenarioStep step, string scenarioDirectory, string work, bool o2, DllScenarioSetup setup)
    {
        var baseName = Path.GetFileNameWithoutExtension(step.Target);
        return
        [
            Path.Combine(scenarioDirectory, step.Target),
            "-o", Path.Combine(work, baseName + (step.Verb == "dll" ? ".dll" : ".exe")),
            "--obj-dir", Path.Combine(work, "obj", baseName),
            "-L", work,
            .. setup.OpcOptions,
            o2 ? "-O2" : "-O0",
            .. step.Arguments,
        ];
    }

    /// <summary>
    /// Runs the scenario in <paramref name="scenarioDirectory"/> at <c>-O0</c> or <c>-O2</c>; null when every step passed, else
    /// what failed (with the kept work directory). <paramref name="process"/> replaces the process start in the harness's tests.
    /// </summary>
    public static async Task<string?> RunAsync(string scenarioDirectory, bool o2, DllScenarioSetup setup, CancellationToken ct, DllScenarioProcess? process = null)
    {
        var name = Path.GetFileName(scenarioDirectory.TrimEnd(Path.DirectorySeparatorChar)) + (o2 ? " -O2" : "");
        ImmutableArray<DllScenarioStep> steps;
        try
        {
            steps = Parse(File.ReadAllText(Path.Combine(scenarioDirectory, StepsFile)));
        }
        catch (Exception e) when (e is FormatException or IOException)
        {
            return $"{name}: {e.Message}";
        }

        var work = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var failure = await RunStepsAsync(steps, scenarioDirectory, work, o2, setup, process ?? StartAsync, ct).ConfigureAwait(false);
        if (failure is not null)
        {
            return $"{name}: {failure}\n(work directory: {work})";                // kept for inspection
        }

        try
        {
            Directory.Delete(work, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // a just-exited image can stay locked for a moment: left behind under %TEMP%\FreexComp.Tests
        }

        return null;
    }

    private static async Task<string?> RunStepsAsync(ImmutableArray<DllScenarioStep> steps, string scenarioDirectory, string work, bool o2,
        DllScenarioSetup setup, DllScenarioProcess process, CancellationToken ct)
    {
        foreach (var step in steps)
        {
            string At(string what) => string.Create(CultureInfo.InvariantCulture, $"{StepsFile}({step.Line}) {step.Verb} {step.Target}: {what}");
            switch (step.Verb)
            {
                case "dll" or "exe":
                    var built = await process(setup.OpcPath, OpcArguments(step, scenarioDirectory, work, o2, setup), scenarioDirectory, ct).ConfigureAwait(false);
                    if (built.Stopped is { } opcStopped)
                    {
                        return At("opc stopped: " + opcStopped);
                    }

                    if (built.ExitCode != 0)
                    {
                        return At($"opc exit code {built.ExitCode}, expected 0\n{Normalize(built.StdOut)}");
                    }

                    break;

                case "run":
                    var expectedPath = Path.Combine(scenarioDirectory, Path.ChangeExtension(step.Target, ".out"));
                    if (!File.Exists(expectedPath))
                    {
                        return At(Path.GetFileName(expectedPath) + " is missing");
                    }

                    var exitPath = Path.ChangeExtension(expectedPath, ".exit");
                    var expectedExit = File.Exists(exitPath) ? int.Parse(File.ReadAllText(exitPath).Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) : 0;
                    var run = await process(Path.Combine(work, step.Target), step.Arguments, work, ct).ConfigureAwait(false);
                    if (run.Stopped is { } stopped)
                    {
                        return At("program stopped: " + stopped);
                    }

                    var stdout = Normalize(run.StdOut);
                    if (run.ExitCode != expectedExit)
                    {
                        return At($"exit code {run.ExitCode}, expected {expectedExit}\nstdout:\n{stdout}");
                    }

                    var expected = Normalize(Encoding.UTF8.GetString(File.ReadAllBytes(expectedPath)).TrimStart('﻿'));
                    if (!string.Equals(expected, stdout, StringComparison.Ordinal))
                    {
                        return At($"stdout differs\n--- expected\n{expected}\n--- actual\n{stdout}");
                    }

                    break;

                default:
                    if (!File.Exists(Path.Combine(work, step.Target)))
                    {
                        return At("the file does not exist in the work directory");
                    }

                    break;
            }
        }

        return null;
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>The real process start: stdin closed, stdout captured as UTF-8, stderr drained, limits of <see cref="NativeRun"/>, tree killed at the end.</summary>
    private static async Task<DllScenarioProcessResult> StartAsync(string file, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken ct)
    {
        var start = new ProcessStartInfo(file)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var limit = new RunLimit(Path.GetFileName(file), NativeRun.CpuLimit, NativeRun.WallLimit, ct);
        using var process = Process.Start(start)!;
        limit.Attach(process.Id);
        try
        {
            process.StandardInput.Close();
            var copyOut = PipePump.Bytes(process.StandardOutput.BaseStream, "stdout");       // C-319: each pipe on its own thread
            var drainErr = PipePump.Drain(process.StandardError.BaseStream, "stderr");
            await Task.WhenAll(copyOut, drainErr).WaitAsync(limit.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            return new DllScenarioProcessResult(process.ExitCode, Encoding.UTF8.GetString(await copyOut.ConfigureAwait(false)), null);
        }
        catch (OperationCanceledException) when (limit.Report is { } report)
        {
            return new DllScenarioProcessResult(null, "", report);
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // exited between the check and the kill
            }
        }
    }
}
