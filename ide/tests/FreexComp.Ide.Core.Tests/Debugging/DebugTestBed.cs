using System.Text;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Debugging;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Debugging;

/// <summary>
/// A Phase 6 test composition for the debug integration (testing.md §5.4): the <c>Hello</c> project on the real disk
/// under a temp folder (the session checks that the program exists), the fake build engine, a valid (empty) toolchain,
/// and debug sessions over the scripted adapter. Must be used on a test UI thread: session events are applied there.
/// </summary>
internal sealed class DebugTestBed : IDisposable
{
    private readonly TempDirectory _temp = new();

    public DebugTestBed()
    {
        Dir = _temp.Combine("Hello");
        Services = TestComposition.Build(o =>
        {
            o.FileSystem = new PhysicalFileSystem();
            o.Clock = Clock;
            o.Log = Log;
            o.BuildEngine = Engine;
            o.Processes = Processes;
            o.DebugSessions = Sessions;
            o.ImplementedPhase = 6;
            o.UserDir = _temp.Combine("user");
        });
        foreach (var tool in (string[])[@"tc\llvm\clang.exe", @"tc\llvm\lld-link.exe", @"tc\llvm\llvm-dlltool.exe", @"tc\rtl\System.pas"])
            File(_temp.Combine(tool), "");
        Services.Settings.Set(SettingsCatalog.ToolchainLlvmBinDir, LlvmDir);
        Services.Settings.Set(SettingsCatalog.ToolchainRtlDir, _temp.Combine(@"tc\rtl"));
        Engine.DefaultOutputPath = Exe;
        Directory.CreateDirectory(Path.GetDirectoryName(Exe)!);
    }

    public string Dir { get; }

    public string LlvmDir => _temp.Combine(@"tc\llvm");

    public string ProjectPath => Path.Combine(Dir, "Hello.pproj");

    public string MainPath => Path.Combine(Dir, "Hello.pas");

    public string UnitPath => Path.Combine(Dir, "Unit1.pas");

    public string Exe => Path.Combine(Dir, @"bin\Debug\Hello.exe");

    public string SessionPath => Path.Combine(Dir, @".freexcomp\session.json");

    public ManualClock Clock { get; } = new();

    public RecordingLog Log { get; } = new();

    public FakeBuildEngine Engine { get; } = new();

    public FakeProcessLauncher Processes { get; } = new();

    public FakeAdapterSessions Sessions { get; } = new();

    public IdeServices Services { get; }

    public DebugService Debug => Services.Debug!;

    public IBreakpointService Breakpoints => Services.Breakpoints!;

    public FakeDialogService Dialogs => (FakeDialogService)Services.Dialogs;

    public IdeState State => Services.State.Current.State;

    /// <summary>A scenario step: the thread list of every stop.</summary>
    public const string Threads = DebugScenarios.Threads;

    public static string Exit(int exitCode = 0) => DebugScenarios.Exit(exitCode);

    public static string StopAtStart(string path, int line, string stoppedMore = "") => DebugScenarios.StopAtStart(path, line, stoppedMore);

    public static string Json(string text) => DebugScenarios.Json(text);

    public string File(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    /// <summary>Writes and opens Hello.pproj: configurations <c>Debug</c> (debug information) and <c>Release</c> (none).</summary>
    public async Task OpenHelloAsync(string extra = "")
    {
        File(ProjectPath, $$"""
            { "formatVersion": 1, "name": "Hello", "appType": "console", "mainSource": "Hello.pas",
              "configurations": { "Debug": { "optimization": 0, "debugInfo": true, "defines": [], "outputDir": "bin/Debug" },
                                  "Release": { "optimization": 2, "debugInfo": false, "defines": [], "outputDir": "bin/Debug" } },
              "files": [ { "path": "Hello.pas" }, { "path": "Unit1.pas" } ]{{extra}} }
            """);
        File(MainPath, "program Hello;\nuses Unit1;\nbegin\n  Run;\nend.\n");
        File(UnitPath, "unit Unit1;\ninterface\nprocedure Run;\nimplementation\nprocedure Run;\nbegin\n  WriteLn('a');\n  WriteLn('b');\n  WriteLn('c');\nend;\nend.\n");
        Assert.True(await Services.Projects.OpenAsync(ProjectPath));
    }

    public async Task<TextDocumentViewModel> OpenDocumentAsync(string path)
    {
        var result = await Services.Documents.OpenAsync(path);
        Assert.True(result.Success, result.MessageId);
        return Assert.IsType<TextDocumentViewModel>(result.Value);
    }

    /// <summary>Completes on the UI thread once the IDE state is <paramref name="state"/> (it may be already).</summary>
    public Task WaitForStateAsync(IdeState state)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Check(object? sender, EventArgs e)
        {
            if (Services.State.Current.State != state)
                return;
            Services.State.Changed -= Check;
            reached.TrySetResult();
        }

        Services.State.Changed += Check;
        Check(null, EventArgs.Empty);
        return CurrentTest.Deadline.WaitAsync(reached.Task, $"IDE state {state}");
    }

    /// <summary>
    /// Completes on the UI thread once <paramref name="condition"/> holds for the breakpoints (it may already): the
    /// session reports their launch-time status with events of its own thread, which arrive independently of the launch.
    /// </summary>
    public Task WaitForBreakpointsAsync(Func<bool> condition, string step)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Check(object? sender, EventArgs e)
        {
            if (!condition())
                return;
            Breakpoints.Changed -= Check;
            reached.TrySetResult();
        }

        Breakpoints.Changed += Check;
        Check(null, EventArgs.Empty);
        return CurrentTest.Deadline.WaitAsync(reached.Task, step);
    }

    /// <summary>Completes on the UI thread once <paramref name="condition"/> holds for the Event Log's rows (it may already).</summary>
    public Task WaitForLogAsync(Func<bool> condition)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Check(object? sender, Core.Debugging.DebugLogEntry? e)
        {
            if (!condition())
                return;
            Debug.LogChanged -= Check;
            reached.TrySetResult();
        }

        Debug.LogChanged += Check;
        Check(null, null);
        return CurrentTest.Deadline.WaitAsync(reached.Task, "the Event Log's rows");
    }

    /// <summary>Waits for a task of the product, bounded by the test's own timeout.</summary>
    public static Task WaitAsync(Task task, string step) => CurrentTest.Deadline.WaitAsync(task, step);

    /// <summary>The lines of an Output channel, after letting batched output reach it (50 ms, IDE-P3-070).</summary>
    public IReadOnlyList<OutputLine> Output(string channel)
    {
        Clock.Advance(TimeSpan.FromMilliseconds(50));
        return Services.Output.GetLines(channel);
    }

    public void Dispose()
    {
        Sessions.Dispose();
        _temp.Dispose();
    }
}
