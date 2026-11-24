using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FreexComp.Rtl.Tests.Harness;

namespace FreexComp.UiFramework.Tests;

/// <summary>
/// Group B (testing.md §1, layout.md UIF-331): <c>pascal/LayoutHost.pas</c> is compiled once and run over every case of
/// the lead-owned corpus <c>tests/layout-corpus/</c> (<c>LayoutHost &lt;form&gt; [&lt;steps&gt;]</c>); its stdout is compared
/// with the case's <c>.expected.json</c> structurally (UIF-324: same keys in the same order, same array lengths,
/// <c>class</c>/<c>name</c> ASCII case-insensitive, numbers and Booleans exact). One row per case, display name
/// <c>layout/&lt;Enn-slug&gt;</c> (UIF-1200).
/// </summary>
[Trait("Kind", "Native")]
[Trait("Category", "Toolchain")]
public sealed class LayoutCasesTheory
{
    public static IEnumerable<TheoryDataRow<string>> Cases() =>
        UiCorpus.DiscoverLayout().Select(c =>
        {
            var row = new TheoryDataRow<string>(c.Id) { TestDisplayName = c.Id };
            row.Traits["Req"] = ["UIF-331"];
            return row;
        });

    [Theory]
    [Trait("Phase", "4")]
    [MemberData(nameof(Cases))]
    public async Task LayoutCases_Runtime(string id)
    {
        var @case = UiCorpus.DiscoverLayout().Single(c => c.Id == id);
        Assert.True(@case.ExpectedPath is not null, $"{id}: {@case.Slug}.expected.json does not exist");

        var failure = await LayoutHost.RunAsync(@case, TestContext.Current.CancellationToken);

        Assert.True(failure is null, failure);
    }

    public static IEnumerable<TheoryDataRow<string>> IdempotentCases() =>
        UiCorpus.DiscoverLayout().Select(c =>
        {
            var row = new TheoryDataRow<string>(c.Id) { TestDisplayName = "Layout_Idempotent " + c.Id };
            row.Traits["Req"] = ["UIF-226"];
            return row;
        });

    /// <summary>
    /// UIF-226 over every corpus case (U-128): <c>LayoutHost --check-idempotent</c> runs the case's steps and, after every
    /// dump, a second layout pass over the whole tree; a second dump that differs from the first fails the host.
    /// </summary>
    [Theory]
    [Trait("Phase", "4")]
    [MemberData(nameof(IdempotentCases))]
    public async Task Layout_Idempotent(string id)
    {
        var @case = UiCorpus.DiscoverLayout().Single(c => c.Id == id);

        var failure = await LayoutHost.RunIdempotentAsync(@case, TestContext.Current.CancellationToken);

        Assert.True(failure is null, failure);
    }

    [Fact]
    public void Layout_Idempotent_CoversWholeCorpus()
    {
        var ids = UiCorpus.DiscoverLayout().Select(c => c.Id[(c.Id.IndexOf('/') + 1)..][..3]).Distinct().ToList();

        Assert.Equal(Enumerable.Range(1, 40).Select(n => $"E{n:00}"), ids.Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Req", "UIF-324")]
    public void LayoutCases_ComparisonIsStructural()
    {
        const string expected = """{ "a": [ { "class": "TPanel", "name": "P", "left": 1, "visible": true } ] }""";

        Assert.Null(LayoutHost.Compare(expected, """{"a":[{"class":"tpanel","name":"p","left":1,"visible":true}]}"""));
        Assert.Contains("$.a[0].left", LayoutHost.Compare(expected, """{"a":[{"class":"TPanel","name":"P","left":2,"visible":true}]}"""));
        Assert.Contains("$.a[0].visible", LayoutHost.Compare(expected, """{"a":[{"class":"TPanel","name":"P","left":1,"visible":false}]}"""));
        Assert.Contains("key", LayoutHost.Compare(expected, """{"a":[{"name":"P","class":"TPanel","left":1,"visible":true}]}"""));
        Assert.Contains("length", LayoutHost.Compare(expected, """{"a":[]}"""));
        Assert.Contains("$.a[0].name", LayoutHost.Compare(expected, """{"a":[{"class":"TPanel","name":"Q","left":1,"visible":true}]}"""));
    }
}

/// <summary>Compiles <c>LayoutHost.pas</c> once per test run and runs it per corpus case (UIF-331).</summary>
public static class LayoutHost
{
    public const string ProgramName = "LayoutHost";

    private static readonly TimeSpan CompileLimit = Gui.BuildSafetyNet;
    internal static readonly TimeSpan RunLimit = Gui.SafetyNet;                      // UIF-1208, group A/B; a safety net, not a budget (U-139)
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static Task<(string? Exe, string? Failure)>? compiled;

    public static string SourcePath => Path.Combine(UiCorpus.PascalDirectory, ProgramName + ".pas");

    /// <summary>Null when the host's output for <paramref name="case"/> equals its expectation; otherwise what differed.</summary>
    public static async Task<string?> RunAsync(LayoutCase @case, CancellationToken ct)
    {
        var (exe, failure) = await CompileOnceAsync(ct).ConfigureAwait(false);
        if (exe is null)
        {
            return $"{@case.Id}: {failure}";
        }

        List<string> args = [@case.FormPath];
        if (@case.StepsPath is not null)
        {
            args.Add(@case.StepsPath);
        }

        var run = await RunProcessAsync(exe, GoldenRunner.CommandLine(args, null), Path.GetDirectoryName(exe)!, RunLimit, ct).ConfigureAwait(false);
        if (run.Stopped is not null)
        {
            return $"{@case.Id}: {run.Stopped}";
        }

        var stdout = run.StdOut.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (run.ExitCode != 0)
        {
            return $"{@case.Id}: LayoutHost exit code {run.ExitCode}, expected 0\n--- stdout\n{stdout}\n--- stderr\n{run.StdErr}";
        }

        var expected = File.ReadAllText(@case.ExpectedPath!, Encoding.UTF8);
        return Compare(expected, stdout) is { } difference
            ? $"{@case.Id}: {difference}\n--- expected\n{expected.Replace("\r\n", "\n", StringComparison.Ordinal)}\n--- actual\n{stdout}"
            : null;
    }

    /// <summary>UIF-226: null when a second layout pass after every dump of <paramref name="case"/> changed nothing.</summary>
    public static async Task<string?> RunIdempotentAsync(LayoutCase @case, CancellationToken ct)
    {
        var (exe, failure) = await CompileOnceAsync(ct).ConfigureAwait(false);
        if (exe is null)
        {
            return $"{@case.Id}: {failure}";
        }

        List<string> args = ["--check-idempotent", @case.FormPath];
        if (@case.StepsPath is not null)
        {
            args.Add(@case.StepsPath);
        }

        var run = await RunProcessAsync(exe, GoldenRunner.CommandLine(args, null), Path.GetDirectoryName(exe)!, RunLimit, ct).ConfigureAwait(false);
        if (run.Stopped is not null)
        {
            return $"{@case.Id}: {run.Stopped}";
        }

        return run.ExitCode == 0
            ? null
            : $"{@case.Id}: LayoutHost --check-idempotent exit code {run.ExitCode}, expected 0\n--- stderr\n{run.StdErr.Replace("\r\n", "\n", StringComparison.Ordinal)}";
    }

    /// <summary>UIF-324: null when equal; otherwise the JSON path of the first difference.</summary>
    public static string? Compare(string expected, string actual)
    {
        JsonDocument e, a;
        try
        {
            e = JsonDocument.Parse(expected);
        }
        catch (JsonException x)
        {
            return "expectation is not JSON: " + x.Message;
        }

        try
        {
            a = JsonDocument.Parse(actual);
        }
        catch (JsonException x)
        {
            e.Dispose();
            return "output is not JSON: " + x.Message;
        }

        using (e)
        using (a)
        {
            return Compare(e.RootElement, a.RootElement, "$", null);
        }
    }

    private static string? Compare(JsonElement e, JsonElement a, string path, string? key)
    {
        if (e.ValueKind != a.ValueKind)
        {
            return $"{path}: expected {Show(e)}, actual {Show(a)}";
        }

        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                var ep = e.EnumerateObject().ToList();
                var ap = a.EnumerateObject().ToList();
                for (var i = 0; i < Math.Max(ep.Count, ap.Count); i++)
                {
                    if (i >= ep.Count || i >= ap.Count || ep[i].Name != ap[i].Name)
                    {
                        return $"{path}: key {i} expected '{(i < ep.Count ? ep[i].Name : "(none)")}', actual '{(i < ap.Count ? ap[i].Name : "(none)")}'";
                    }

                    if (Compare(ep[i].Value, ap[i].Value, path + "." + ep[i].Name, ep[i].Name) is { } d)
                    {
                        return d;
                    }
                }

                return null;
            case JsonValueKind.Array:
                var ea = e.EnumerateArray().ToList();
                var aa = a.EnumerateArray().ToList();
                if (ea.Count != aa.Count)
                {
                    return $"{path}: array length expected {ea.Count}, actual {aa.Count}";
                }

                for (var i = 0; i < ea.Count; i++)
                {
                    if (Compare(ea[i], aa[i], $"{path}[{i}]", null) is { } d)
                    {
                        return d;
                    }
                }

                return null;
            case JsonValueKind.String:
                var comparison = key is "class" or "name" ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                return string.Equals(e.GetString(), a.GetString(), comparison) ? null : $"{path}: expected {Show(e)}, actual {Show(a)}";
            case JsonValueKind.Number:
                return e.GetRawText() == a.GetRawText() ? null : $"{path}: expected {Show(e)}, actual {Show(a)}";
            default:
                return null;                                                      // true/false/null: equal kinds are equal values
        }
    }

    private static string Show(JsonElement x) => x.ValueKind is JsonValueKind.Object or JsonValueKind.Array ? x.ValueKind.ToString() : x.GetRawText();

    private static async Task<(string? Exe, string? Failure)> CompileOnceAsync(CancellationToken ct)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            compiled ??= CompileAsync(SourcePath, ct);
            var result = await compiled.ConfigureAwait(false);
            if (result.Exe is null)
            {
                compiled = null;                                                  // a cancelled or failed build is retried by the next row
            }

            return result;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// As the golden runner compiles group A programs (UIF-1201): opc, the RTL, <c>-U rtl/tests</c>, <c>-U ui-framework/src</c>;
    /// the executable goes to a fresh work directory.
    /// </summary>
    internal static async Task<(string? Exe, string? Failure)> CompileAsync(string sourcePath, CancellationToken ct)
    {
        var setup = UiCorpus.Setup;
        var missing = new[] { "clang.exe", "lld-link.exe", "llvm-dlltool.exe" }.Select(t => Path.Combine(setup.ToolchainDirectory, t))
            .Append(setup.OpcPath).Where(p => !File.Exists(p)).ToList();
        if (missing.Count > 0)
        {
            return (null, $"toolchain missing: {string.Join(", ", missing)} (run scripts/bootstrap.ps1).");     // fails, never skips (UIF-1203)
        }

        if (!File.Exists(sourcePath))
        {
            return (null, $"{sourcePath} does not exist");
        }

        var work = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var exe = Path.Combine(work, Path.GetFileNameWithoutExtension(sourcePath) + ".exe");
        List<string> args =
        [
            sourcePath, "-U", setup.UnitDirectory, .. setup.ExtraUnitDirectories.SelectMany(d => new[] { "-U", d }),
            "-o", exe, "--obj-dir", Path.Combine(work, "obj"), "--rtl", setup.RtlDirectory, "--toolchain", setup.ToolchainDirectory,
        ];
        var opc = await RunProcessAsync(setup.OpcPath, GoldenRunner.CommandLine(args, null), Path.GetDirectoryName(sourcePath)!, CompileLimit, ct).ConfigureAwait(false);
        if (opc.Stopped is not null)
        {
            return (null, "opc " + opc.Stopped);
        }

        return opc.ExitCode == 0 && File.Exists(exe)
            ? (exe, null)
            : (null, $"opc exit code {opc.ExitCode}, expected 0\n{opc.StdOut}{opc.StdErr}\n(work directory: {work})");
    }

    internal readonly record struct ProcessResult(int ExitCode, string StdOut, string StdErr, string? Stopped);

    /// <summary>One process with empty stdin, killed (with its tree) when <paramref name="limit"/> or the test's token expires.</summary>
    internal static async Task<ProcessResult> RunProcessAsync(string file, string arguments, string workingDirectory, TimeSpan limit, CancellationToken ct)
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
        psi.Environment["FXC_UI_NONINTERACTIVE"] = "1";

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
            return new(-1, "", "", ct.IsCancellationRequested ? "test cancelled" : $"{Path.GetFileName(file)} did not exit by itself (safety net {limit.TotalSeconds:F0} s); its process tree was killed");
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
