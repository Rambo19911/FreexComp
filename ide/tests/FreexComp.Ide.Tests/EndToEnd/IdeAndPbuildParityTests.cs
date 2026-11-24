using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Build;
using FreexComp.Compiler;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.EndToEnd;

/// <summary>
/// F09-001 "one build engine for IDE and command line" (compiler-cli.md §2, §4): the same project, at the same path, is
/// built by the IDE (the Build command through the real <c>BuildService</c>, build engine, compiler and toolchain) and by
/// the <c>pbuild</c> program started as a process; the executable, the diagnostics and the event stream are the same.
/// The outputs of the first build are removed before the second, so that both are full builds of identical inputs.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "E2E")]
[Trait("Category", "Toolchain")]
public sealed class IdeAndPbuildParityTests
{
    /// <summary>Compiles and links; the function never assigns its result: one warning.</summary>
    private const string WarningProgram = "program Parity;\n\nfunction Answer: Integer;\nbegin\nend;\n\nbegin\n  WriteLn('Parity ', Answer >= 0);\nend.\n";

    /// <summary>The same warning, then an undeclared identifier: one warning and one error, no executable.</summary>
    private const string ErrorProgram = "program Parity;\n\nfunction Answer: Integer;\nbegin\nend;\n\nbegin\n  Foo := Answer;\nend.\n";

    /// <summary>
    /// Acceptance test 1, success: byte-identical <c>.exe</c>; the diagnostic lines and tool lines <c>pbuild</c> prints (text
    /// form) are the ones the IDE received, in the same order, and the Messages pane lists the same diagnostics.
    /// </summary>
    [AvaloniaFact(Timeout = 120000)]
    public async Task WarningProject_ByteIdenticalExe_IdenticalDiagnostics()
    {
        var run = await BuildWithBothAsync(WarningProgram, json: false);

        Assert.True(run.IdeResult.Success, "IDE build failed: " + string.Join(" | ", run.PbuildOut));
        Assert.Equal(0, run.PbuildExitCode);
        Assert.Equal(TextLines(run.IdeEvents), run.PbuildOut[..^1]);
        Assert.Matches(@"^Build succeeded \(0 errors, 1 warnings, 0 hints, \d+\.\d\d s\)$", run.PbuildOut[^1]);
        var warning = Assert.Single(run.IdeDiagnostics);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal(run.IdeDiagnostics.Select(MessageRow), run.MessageRows);

        Assert.NotNull(run.IdeExe);
        Assert.NotNull(run.PbuildExe);
        Assert.True(run.IdeExe.Length > 1024, "the IDE's executable is too small to be a program");
        Assert.True(run.IdeExe.AsSpan().SequenceEqual(run.PbuildExe),
            $"the executables differ (IDE {run.IdeExe.Length} bytes, pbuild {run.PbuildExe.Length} bytes, first difference at {FirstDifference(run.IdeExe, run.PbuildExe)})");
    }

    /// <summary>Acceptance test 1, failure: the same diagnostics (file, line, column, severity, code, message; set and order), no executable from either.</summary>
    [AvaloniaFact(Timeout = 120000)]
    public async Task ErrorProject_IdenticalDiagnostics_NoExe()
    {
        var run = await BuildWithBothAsync(ErrorProgram, json: false);

        Assert.False(run.IdeResult.Success);
        Assert.Equal(1, run.PbuildExitCode);
        Assert.Equal(TextLines(run.IdeEvents), run.PbuildOut[..^1]);
        Assert.Matches(@"^Build failed \(1 errors, 1 warnings, 0 hints, \d+\.\d\d s\)$", run.PbuildOut[^1]);
        Assert.Equal([DiagnosticSeverity.Warning, DiagnosticSeverity.Error], run.IdeDiagnostics.Select(d => d.Severity).Order());
        Assert.Equal(run.IdeDiagnostics.Select(MessageRow), run.MessageRows);
        Assert.Null(run.IdeExe);
        Assert.Null(run.PbuildExe);
    }

    /// <summary>
    /// Acceptance test 2: every line of <c>pbuild --diagnostics json</c> equals the serialised event the IDE received at
    /// that position, and the summary line equals the IDE's build result. Only the elapsed times (<c>ms</c>) are left out:
    /// they are measurements of two different runs.
    /// </summary>
    [AvaloniaTheory(Timeout = 120000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PbuildJson_EqualsTheBuildEventStreamTheIdeReceived(bool withError)
    {
        var run = await BuildWithBothAsync(withError ? ErrorProgram : WarningProgram, json: true);

        var expected = run.IdeEvents.Select(Serialize).Append(Summary(run.IdeResult)).ToArray();
        var actual = run.PbuildOut.Select(WithoutElapsed).ToArray();
        Assert.Equal(expected, actual);
        Assert.Equal(withError ? 1 : 0, run.PbuildExitCode);
        Assert.Equal(withError ? 2 : 1, run.IdeDiagnostics.Count); // the stream is not trivially empty
        Assert.IsType<BuildEvent.Started>(run.IdeEvents[0]);
        Assert.IsType<BuildEvent.Finished>(run.IdeEvents[^1]);
        // The Messages pane got exactly the diagnostics of the JSON stream.
        var jsonRows = run.PbuildOut.Select(l => JsonNode.Parse(l)!.AsObject()).Where(o => o.ContainsKey("code"))
            .Select(o => new Row((string)o["severity"]!, (string)o["code"]!, (string)o["message"]!, (string)o["file"]!, (int)o["line"]!, (int)o["col"]!));
        Assert.Equal(jsonRows, run.MessageRows);
    }

    private sealed record Row(string Severity, string Code, string Message, string? File, int Line, int Column);

    private sealed record ParityRun(IReadOnlyList<BuildEvent> IdeEvents, BuildResult IdeResult, IReadOnlyList<Row> MessageRows, byte[]? IdeExe,
        int PbuildExitCode, string[] PbuildOut, byte[]? PbuildExe)
    {
        public IReadOnlyList<Diagnostic> IdeDiagnostics => [.. IdeEvents.OfType<BuildEvent.Diagnostic>().Select(d => d.Value)];
    }

    /// <summary>Records what the IDE's <c>BuildService</c> gets from the engine: every event, in order, and the result.</summary>
    private sealed class RecordingEngine(IBuildEngine inner) : IBuildEngine
    {
        private readonly List<BuildEvent> _events = [];

        public BuildResult? Result { get; private set; }

        public IReadOnlyList<BuildEvent> Events
        {
            get
            {
                lock (_events)
                    return [.. _events];
            }
        }

        public async Task<BuildResult> RunAsync(BuildRequest request, IProgress<BuildEvent> progress, CancellationToken ct)
        {
            var result = await inner.RunAsync(request, new Tee(_events, progress), ct);
            Result = result;
            return result;
        }

        private sealed class Tee(List<BuildEvent> events, IProgress<BuildEvent> next) : IProgress<BuildEvent>
        {
            public void Report(BuildEvent value)
            {
                lock (events)
                    events.Add(value);
                next.Report(value);
            }
        }
    }

    private static async Task<ParityRun> BuildWithBothAsync(string program, bool json)
    {
        RecordingEngine? recorder = null;
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.RealBuild = true;
            o.WrapBuildEngine = engine => recorder = new RecordingEngine(engine);
            o.InitialFiles[@"Parity\Parity.pproj"] = """
                { "formatVersion": 1, "name": "Parity", "appType": "console", "mainSource": "Parity.pas",
                  "configurations": { "Debug": { "optimization": 0, "debugInfo": true, "defines": [], "outputDir": "bin/Debug" },
                                      "Release": { "optimization": 2, "debugInfo": false, "defines": [], "outputDir": "bin/Release" } },
                  "files": [ { "path": "Parity.pas" } ] }
                """;
            o.InitialFiles[@"Parity\Parity.pas"] = program;
        });
        var toolchain = h.Services.Toolchain.Resolve();
        if (toolchain.LlvmBinDir is null || toolchain.RtlDir is null)
            Assert.Fail("LLVM toolchain not found (run scripts/bootstrap.ps1)"); // testing.md: fails, never skips
        var pbuild = Path.Combine(AppContext.BaseDirectory, "pbuild.exe");
        Assert.True(File.Exists(pbuild), "pbuild.exe is not next to the tests: " + pbuild);

        var projectDir = Path.Combine(h.TempDir, "Parity");
        var projectPath = Path.Combine(projectDir, "Parity.pproj");
        var exePath = Path.Combine(projectDir, @"bin\Debug\Parity.exe");
        var sources = Directory.GetFiles(projectDir, "*", SearchOption.AllDirectories);
        Assert.True(await h.Services.Projects.OpenAsync(projectPath), "Parity.pproj did not open");

        // The IDE: Build (Ctrl+Shift+B), Building then Idle.
        var states = new List<IdeState>();
        h.Services.State.Changed += (_, _) => states.Add(h.Services.State.Current.State);
        h.Press("Ctrl+Shift+B");
        await h.WaitForAsync(() => states.Contains(IdeState.Building) && h.Services.State.Current.State == IdeState.Idle, "IDE build finished");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(recorder);
        var ideResult = recorder.Result;
        Assert.True(ideResult is not null, "the IDE's build did not reach the engine. Log: " + string.Join(" | ", h.Log.Lines));
        var ideEvents = recorder.Events;
        var rows = h.Services.Messages.Items.Where(i => !i.IsSummary).Select(MessageRow).ToList();
        var ideExe = File.Exists(exePath) ? File.ReadAllBytes(exePath) : null;

        // Everything the first build wrote goes away: pbuild must do a full build of the same inputs, not find it up to date.
        foreach (var file in Directory.GetFiles(projectDir, "*", SearchOption.AllDirectories).Except(sources, StringComparer.OrdinalIgnoreCase))
            File.Delete(file);
        Assert.Equal(sources.Order(), Directory.GetFiles(projectDir, "*", SearchOption.AllDirectories).Order());

        // pbuild: the same project file, the toolchain and RTL the IDE located, the IDE's active configuration.
        var start = new ProcessStartInfo(pbuild)
        {
            WorkingDirectory = projectDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in (string[])["Parity.pproj", "--config", h.Services.Projects.Project!.ActiveConfiguration, "--diagnostics", json ? "json" : "text",
                     "--toolchain", toolchain.LlvmBinDir, "--rtl", toolchain.RtlDir])
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            var exit = process.WaitForExitAsync(TestContext.Current.CancellationToken);
            await h.WaitForAsync(() => exit.IsCompleted && stdout.IsCompleted && stderr.IsCompleted, "pbuild finished");
            await exit;
            Assert.Equal("", await stderr);
            var lines = (await stdout).ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
            var pbuildExe = File.Exists(exePath) ? File.ReadAllBytes(exePath) : null;
            return new ParityRun(ideEvents, ideResult, rows, ideExe, process.ExitCode, lines, pbuildExe);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    /// <summary>The text form of the events (compiler-cli.md §2, §4): diagnostics as lines, output lines as they are; nothing else is printed.</summary>
    private static string[] TextLines(IEnumerable<BuildEvent> events) =>
    [
        .. events.Select(e => e switch
        {
            BuildEvent.Diagnostic d => d.Value.ToText(),
            BuildEvent.CommandLine c => "> " + c.Text,
            BuildEvent.OutputLine o => o.Text,
            _ => null,
        }).OfType<string>(),
    ];

    /// <summary>One event as its <c>--diagnostics json</c> line (compiler-cli.md §2, §4), elapsed time left out.</summary>
    private static string Serialize(BuildEvent e) => WithoutElapsed((e switch
    {
        BuildEvent.Started s => new JsonObject
        {
            ["event"] = "started", ["project"] = s.ProjectPath, ["configuration"] = s.Configuration, ["platform"] = s.Platform,
            ["target"] = s.Target.ToString().ToLowerInvariant(),
        },
        BuildEvent.Diagnostic d => JsonNode.Parse(d.Value.ToJson())!,
        BuildEvent.CommandLine c => new JsonObject { ["event"] = "commandLine", ["text"] = c.Text },
        BuildEvent.OutputLine o => new JsonObject
        {
            ["event"] = "output",
            ["source"] = o.Source switch
            {
                OutputSource.Tool => "tool",
                OutputSource.ProgramStdOut => "stdout",
                OutputSource.ProgramStdErr => "stderr",
                _ => "info",
            },
            ["text"] = o.Text,
        },
        BuildEvent.Finished f => new JsonObject
        {
            ["event"] = "finished", ["project"] = f.ProjectPath, ["success"] = f.Success, ["upToDate"] = f.UpToDate,
            ["errors"] = f.Errors, ["warnings"] = f.Warnings, ["hints"] = f.Hints, ["ms"] = 0,
        },
        _ => throw new InvalidOperationException("unknown event " + e),
    }).ToJsonString());

    private static string Summary(BuildResult result) => WithoutElapsed(new JsonObject
    {
        ["summary"] = new JsonObject
        {
            ["errors"] = result.Errors, ["warnings"] = result.Warnings, ["hints"] = result.Hints, ["output"] = result.OutputPath, ["ms"] = 0,
        },
    }.ToJsonString());

    /// <summary>The line re-serialised (same members, same order) with every elapsed time (<c>ms</c>) set to 0.</summary>
    private static string WithoutElapsed(string line)
    {
        var node = JsonNode.Parse(line)!.AsObject();
        foreach (var o in (JsonObject?[])[node, node["summary"] as JsonObject])
        {
            if (o is not null && o.ContainsKey("ms"))
                o["ms"] = 0;
        }

        return node.ToJsonString();
    }

    /// <summary>A diagnostic as the Messages pane lists it (IDE-P3-060: 1-based position).</summary>
    private static Row MessageRow(Diagnostic d)
    {
        var start = d.Location.LineSpan.Start;
        var severity = d.Severity switch
        {
            DiagnosticSeverity.Error => "error",
            DiagnosticSeverity.Warning => "warning",
            _ => "hint",
        };
        return new Row(severity, d.Code, d.Message, d.Location.Path, start.Line + 1, start.Column + 1);
    }

    private static Row MessageRow(MessageItem item) =>
        new(item.Severity.ToString().ToLowerInvariant(), item.Code, item.Text, item.Location?.FilePath, item.Location?.Line ?? 0, item.Location?.Column ?? 0);

    private static string FirstDifference(byte[] a, byte[] b)
    {
        var i = a.AsSpan().CommonPrefixLength(b);
        return string.Create(CultureInfo.InvariantCulture, $"0x{i:X}");
    }
}
