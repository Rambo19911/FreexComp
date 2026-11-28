using System.Globalization;
using System.Text.Json;
using FreexComp.Build;
using FreexComp.Compiler;
using FreexComp.Debugger;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Menus;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Debugging;

/// <summary>
/// Running a listed member of a project group under the debugger from its root row (group-member-run.md IDE-PM-199,
/// and IDE-PM-191, 192, 197 for the debugger): the row's project file is the argument of <c>run.start</c>; the member
/// is built with its own configuration and a debug session is created for its output with its own run parameters and
/// source roots; the active project does not change. Real sessions over the scripted adapter; the projects are on the
/// real disk under the bed's temp folder: Hello (active), Tools (console; run parameters, a search path), Plug (a
/// library with a host application) and <c>All.pgroup</c>.
/// </summary>
[Trait("Phase", "8")]
[Trait("Phase", "6")] // with Category Integration: the tests that start the scripted debug adapter (IDE-TEST-013)
[Trait("Category", "Integration")]
public sealed class GroupDebugRunTests
{
    private const string Command = "run.start";
    private const string ToolsArguments = "--selftest \"a b\"";

    private static readonly FocusState ProjectManagerFocus = new(CommandScope.ProjectManager, ProjectManagerViewModel.ToolName, null, false);

    private static string F(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    /// <summary>The bed with the product's phase: the commands of a project group are there.</summary>
    private static DebugTestBed NewBed() => new(o => o.ImplementedPhase = null);

    private static string Root(DebugTestBed bed) => Path.GetDirectoryName(bed.Dir)!;

    private static string GroupPath(DebugTestBed bed) => Path.Combine(Root(bed), "All.pgroup");

    private static string Dir(DebugTestBed bed, string name) => Path.Combine(Root(bed), name);

    private static string Proj(DebugTestBed bed, string name) => Path.Combine(Root(bed), name, name + ".pproj");

    private static string ToolsUnit(DebugTestBed bed) => Path.Combine(Root(bed), @"Tools\ToolsUnit.pas");

    private static string ToolsExe(DebugTestBed bed, string configuration = "Debug") => Path.Combine(Root(bed), $@"Tools\bin\{configuration}\Tools.exe");

    private static string ToolsData(DebugTestBed bed) => Path.Combine(Root(bed), @"Tools\data");

    /// <summary>
    /// The group opened with Hello as the active project; the fake engine's default output is the program of Tools in
    /// <paramref name="toolsConfiguration"/>, the member's active configuration (<c>Release</c> has no debug information).
    /// </summary>
    private static async Task OpenGroupAsync(DebugTestBed bed, string toolsConfiguration = "Debug")
    {
        await bed.OpenHelloAsync();
        bed.File(Proj(bed, "Tools"), $$"""
            { "formatVersion": 1, "name": "Tools", "appType": "console", "mainSource": "Tools.pas",
              "activeConfiguration": "{{toolsConfiguration}}", "searchPaths": ["lib"],
              "configurations": { "Debug": { "optimization": 0, "debugInfo": true, "defines": [], "outputDir": "bin/Debug" },
                                  "Release": { "optimization": 2, "debugInfo": false, "defines": [], "outputDir": "bin/Release" } },
              "run": { "arguments": "--selftest \"a b\"", "workingDir": "data" },
              "files": [ { "path": "Tools.pas" }, { "path": "ToolsUnit.pas" } ] }
            """);
        bed.File(Path.Combine(Dir(bed, "Tools"), "Tools.pas"), "program Tools;\nuses ToolsUnit;\nbegin\n  Run;\nend.\n");
        bed.File(ToolsUnit(bed), "unit ToolsUnit;\ninterface\nprocedure Run;\nimplementation\nprocedure Run;\nbegin\n  WriteLn('a');\n  WriteLn('b');\nend;\nend.\n");
        bed.File(Proj(bed, "Plug"), """
            { "formatVersion": 1, "name": "Plug", "appType": "library", "mainSource": "Plug.pas",
              "run": { "arguments": "-n 3", "workingDir": "", "hostApplication": "host/Loader.exe" },
              "files": [ { "path": "Plug.pas" } ] }
            """);
        bed.File(Path.Combine(Dir(bed, "Plug"), "Plug.pas"), "library Plug;\nbegin\nend.\n");
        bed.File(GroupPath(bed), """
            { "formatVersion": 1, "projects": ["Hello/Hello.pproj", "Tools/Tools.pproj", "Plug/Plug.pproj"], "activeProject": "Hello/Hello.pproj" }
            """);
        Directory.CreateDirectory(ToolsData(bed));
        bed.Engine.DefaultOutputPath = ToolsExe(bed, toolsConfiguration);
        Assert.True(await bed.Services.Projects.OpenAsync(GroupPath(bed)));
        await bed.Services.ProjectManager.MissingFileChecks;
        bed.Services.FocusTracker.Update(ProjectManagerFocus);
        Assert.Equal(bed.ProjectPath, bed.Services.Projects.Project!.FilePath);
        Assert.Equal([true, true, true], bed.Services.Groups.Members.Select(m => m.IsAvailable));
    }

    /// <summary>The context menu of the root row of <paramref name="member"/>.</summary>
    private static IReadOnlyList<MenuItemModel> RootMenu(DebugTestBed bed, string member)
    {
        var pm = bed.Services.ProjectManager;
        pm.Select(pm.Find(member == "Hello" ? "$project" : "$project:" + member)!);
        return pm.ContextMenu();
    }

    /// <summary>The item of <paramref name="command"/> in the menu of the listed root of <paramref name="member"/>.</summary>
    private static MenuItemModel Item(DebugTestBed bed, string member, string command = Command) =>
        Assert.Single(RootMenu(bed, member), i => i.CommandId == command);

    private static string Verified(int id, int line) =>
        $$"""{ "on": "setBreakpoints", "respond": { "breakpoints": [ { "id": {{id}}, "verified": true, "line": {{line}} } ] } }""";

    /// <summary>What a <c>setBreakpoints</c> request asked for: the file and the lines.</summary>
    private static (string Path, string Lines) Sent(string argumentsJson)
    {
        var root = JsonDocument.Parse(argumentsJson).RootElement;
        return (root.GetProperty("source").GetProperty("path").GetString()!,
            string.Join(",", root.GetProperty("breakpoints").EnumerateArray().Select(b => b.GetProperty("line").GetInt32())));
    }

    private static (string Program, string[] Arguments, string WorkingDir) Launch(string argumentsJson)
    {
        var root = JsonDocument.Parse(argumentsJson).RootElement;
        return (root.GetProperty("program").GetString()!, [.. root.GetProperty("args").EnumerateArray().Select(a => a.GetString()!)],
            root.GetProperty("cwd").GetString()!);
    }

    /// <summary>Runs <paramref name="command"/> with <paramref name="argument"/> and waits for the stop it leads to.</summary>
    private static async Task<StopInfo> StopAfterAsync(DebugTestBed bed, string command, string? argument)
    {
        var stopped = new TaskCompletionSource<StopInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnStopped(object? sender, StopInfo stop) => stopped.TrySetResult(stop);
        bed.Debug.Stopped += OnStopped;
        try
        {
            await bed.Services.Commands.ExecuteAsync(command, argument);
            await DebugTestBed.WaitAsync(stopped.Task, "the stop after " + command);
            return await stopped.Task;
        }
        finally
        {
            bed.Debug.Stopped -= OnStopped;
        }
    }

    /// <summary>
    /// Card F09-056, acceptance 2. IDE-PM-190: the listed root has Run before Run Without Debugging, both with the row's
    /// project file as their target. IDE-PM-199: one build request for that file with the member's configuration, then one
    /// debug session — the program is the member's output, the arguments are split from its run parameters, the working
    /// directory is its own, the user-code roots are its directory and its search paths; a breakpoint in a unit of the
    /// member, opened as an ordinary document outside the loaded project, is in the launch set and stops the program
    /// there. IDE-PM-196: the group file, the loaded project and the active member are what they were.
    /// </summary>
    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "IDE-PM-190")]
    [Trait("Req", "IDE-PM-196")]
    [Trait("Req", "IDE-PM-199")]
    public Task RunOnListedMember_CreatesDebugSessionForItsOutput() => TestUiThread.RunAsync(async () =>
    {
        using var bed = NewBed();
        await OpenGroupAsync(bed);
        var commands = bed.Services.Commands;
        var tools = Proj(bed, "Tools");
        var unit = ToolsUnit(bed);
        var groupText = File.ReadAllText(GroupPath(bed));
        var toolsText = File.ReadAllText(tools);

        // The menu: …, Clean, —, Run, Run Without Debugging, —, …; both run items carry the row's project file.
        var menu = RootMenu(bed, "Tools");
        var ids = menu.Select(i => i.CommandId ?? "-").ToList();
        var at = ids.IndexOf(Command);
        Assert.True(at >= 4, "the listed root has no Run item");
        Assert.Equal(["project.build", "project.rebuild", "project.clean", "-", Command, "run.startWithoutDebugging", "-"], ids.Skip(at - 4).Take(7));
        var item = menu[at];
        Assert.Equal((tools, true, true), (item.Argument, item.ArgumentIsTarget, item.IsEnabled));

        // A unit of the member is an ordinary document outside the loaded project (IDE-PM-163); it holds a breakpoint.
        var document = await bed.OpenDocumentAsync(unit);
        Assert.DoesNotContain(bed.Services.Projects.Project!.Files, f => string.Equals(Path.GetFileName(f.Path), "ToolsUnit.pas", StringComparison.OrdinalIgnoreCase));
        bed.Breakpoints.Toggle(unit, 7);
        bed.Sessions.UseScenario($$"""
            { "steps": [
              {{Verified(1, 7)}},
              { "on": "configurationDone", "respond": {}, "thenEvents": [ {{DebugScenarios.StoppedEvent(more: """, "hitBreakpointIds": [1]""")}} ] },
              {{DebugScenarios.AtStop([("Run", unit, 7)])}} ] }
            """);
        var states = new List<IdeState>();
        bed.Services.State.Changed += (_, _) => states.Add(bed.State);

        var stop = await StopAfterAsync(bed, Command, item.Argument);

        // One build request for the member, with its own configuration and platform.
        var request = Assert.Single(bed.Engine.Requests);
        Assert.Equal((tools, null, "Debug", "win64", BuildTarget.Build),
            (request.ProjectPath, request.ProjectInGroup, request.Configuration, request.Platform, request.Target));
        Assert.Empty(bed.Processes.Started); // not through the process launcher

        // One session for the member's output: adapter, user-code roots, program, arguments, working directory.
        var session = Assert.Single(bed.Sessions.Sessions);
        Assert.Same(session, bed.Debug.Session);
        var options = Assert.Single(bed.Sessions.Requested);
        Assert.Equal(Path.Combine(bed.LlvmDir, "lldb-dap.exe"), options.AdapterPath);
        Assert.Equal([Dir(bed, "Tools"), Path.Combine(Dir(bed, "Tools"), "lib")], options.UserSourceRoots);
        Assert.Equal(ToolsExe(bed), bed.Engine.Current!.Result.OutputPath);
        var launch = Launch(Assert.Single(bed.Sessions.SentArguments("launch")));
        Assert.Equal((ToolsExe(bed), ToolsData(bed)), (launch.Program, launch.WorkingDir));
        Assert.Equal(["--selftest", "a b"], launch.Arguments);
        Assert.Equal([(unit, "7")], bed.Sessions.SentArguments("setBreakpoints").Select(Sent)); // the launch set

        // Building, then the session without Idle in between; the stop is in the member's unit, shown in its document.
        Assert.Equal([IdeState.Building, IdeState.DebugRunning, IdeState.Paused], states);
        Assert.Equal((StopReason.Breakpoint, unit, 7), (stop.Reason, stop.Location!.Path, stop.Location.Line));
        Assert.Equal((IdeState.Paused, ToolsExe(bed), unit, 7),
            (bed.State, bed.Services.State.Current.ProgramPath, bed.Services.State.Current.PausedFile, bed.Services.State.Current.PausedLine));
        Assert.Same(document, bed.Services.Documents.ActiveDocument);
        Assert.Contains(bed.Output(OutputChannels.Program),
            l => l.Kind == OutputKind.Ide && l.Text.StartsWith("Program started: " + ToolsExe(bed) + " " + ToolsArguments + " (process ", StringComparison.Ordinal));

        // What the member's session did not change.
        Assert.Equal(groupText, File.ReadAllText(GroupPath(bed)));
        Assert.Equal(toolsText, File.ReadAllText(tools));
        Assert.Equal(bed.ProjectPath, bed.Services.Projects.Project!.FilePath);
        Assert.Equal("Debug", bed.Services.Projects.Project.ActiveConfiguration);
        Assert.Equal(bed.ProjectPath, bed.Services.Groups.ActiveMember!.FilePath);
        Assert.Empty(bed.Dialogs.Requests);

        await commands.ExecuteAsync("run.stop");
        Assert.Equal(IdeState.Idle, bed.State);
        Assert.Null(bed.Debug.Session);
        await bed.WaitForScenarioCompleteAsync();
        Assert.True(Item(bed, "Tools").IsEnabled);
        await bed.Debug.ShutdownAsync();
    });

    /// <summary>
    /// IDE-PM-199 (2): a member whose active configuration builds without debug information asks
    /// <c>Msg.Debug.MemberNoDebugInfo</c> with its name and that configuration — two buttons, none that opens the Project
    /// Options of the active project. Cancel builds nothing; Run Anyway builds the member and starts the session.
    /// </summary>
    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "IDE-PM-199")]
    public Task ListedMemberWithoutDebugInfo_AsksTheMemberQuestion() => TestUiThread.RunAsync(async () =>
    {
        using var bed = NewBed();
        await OpenGroupAsync(bed, toolsConfiguration: "Release");
        var commands = bed.Services.Commands;
        var tools = Proj(bed, "Tools");
        var opened = 0;
        bed.Debug.OpenProjectOptions = () =>
        {
            opened++;
            return Task.CompletedTask;
        };
        bed.Sessions.UseScenario("{}");
        bed.Dialogs.Answer("Msg.Debug.NoDebugInfo", "ProjectOptions"); // must not be the message shown
        bed.Dialogs.Enqueue("Msg.Debug.MemberNoDebugInfo", "Cancel", "RunAnyway");

        // Cancel: nothing is built, no session.
        await commands.ExecuteAsync(Command, tools);
        var message = Assert.Single(bed.Dialogs.Requests);
        Assert.Equal("Msg.Debug.MemberNoDebugInfo", message.MessageId);
        Assert.Equal(["Tools", "Release"], message.Args);
        Assert.Equal(["RunAnyway", "Cancel"], MessageDefinitions.Get(message.MessageId).Buttons);
        Assert.Equal(MessageKind.Question, MessageDefinitions.Get(message.MessageId).Kind);
        Assert.Equal("The active configuration of Tools (Release) builds without debug information, so breakpoints will not stop. Run anyway?",
            F(Strings.Msg_Debug_MemberNoDebugInfo, [.. message.Args]));
        Assert.Empty(bed.Engine.Requests);
        Assert.Empty(bed.Sessions.Sessions);
        Assert.Equal(IdeState.Idle, bed.State);

        // Run Anyway: the member is built in that configuration and its program runs under the debugger.
        await commands.ExecuteAsync(Command, tools);
        Assert.Equal(["Msg.Debug.MemberNoDebugInfo", "Msg.Debug.MemberNoDebugInfo"], bed.Dialogs.Requests.Select(r => r.MessageId));
        var request = Assert.Single(bed.Engine.Requests);
        Assert.Equal((tools, "Release"), (request.ProjectPath, request.Configuration));
        Assert.Same(Assert.Single(bed.Sessions.Sessions), bed.Debug.Session);
        Assert.Equal(ToolsExe(bed, "Release"), Launch(Assert.Single(bed.Sessions.SentArguments("launch"))).Program);
        Assert.Equal(IdeState.DebugRunning, bed.State);

        // The active project has debug information: its own start asks nothing (the member's configuration is not its own).
        await bed.Debug.StopAsync();
        Assert.True(await bed.Debug.StartAsync());
        Assert.Equal(2, bed.Dialogs.Requests.Count);
        Assert.Equal(bed.ProjectPath, bed.Engine.Requests[^1].ProjectPath);

        Assert.Equal(0, opened);
        Assert.Empty(bed.Dialogs.Dialogs);
        await bed.Debug.ShutdownAsync();
    });

    /// <summary>
    /// IDE-PM-199 (5): when the debugger cannot be started for the member — no adapter, or the adapter ends right after
    /// the launch — <c>Msg.Debug.StartFailed</c> offers the run without the debugger, and that run is the same member
    /// (its program, arguments and working directory), not the active project.
    /// </summary>
    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "IDE-PM-199")]
    public Task StartFailed_RunWithoutDebuggingStartsTheSameMember() => TestUiThread.RunAsync(async () =>
    {
        using var bed = NewBed();
        await OpenGroupAsync(bed);
        var commands = bed.Services.Commands;
        var tools = Proj(bed, "Tools");
        bed.Services.Settings.Set(Core.Settings.SettingsCatalog.RunConsoleMode, "output"); // the harness preset (testing.md §2.1)
        var expected = new ProcessStartRequest(ToolsExe(bed), ToolsArguments, ToolsData(bed));

        async Task ExitAsync()
        {
            var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnEnded(object? sender, EventArgs e) => ended.TrySetResult();
            bed.Services.Run.ProgramEnded += OnEnded;
            bed.Processes.Last.Exit(0);
            await DebugTestBed.WaitAsync(ended.Task, "the program's end");
            bed.Services.Run.ProgramEnded -= OnEnded;
            Assert.Equal(IdeState.Idle, bed.State);
        }

        // The toolchain folder of the test has no debug adapter. Cancel: nothing runs.
        bed.Sessions.UseNoAdapter();
        bed.Dialogs.Enqueue("Msg.Debug.StartFailed", "Cancel", "RunWithoutDebugging");
        await commands.ExecuteAsync(Command, tools);
        var failed = Assert.Single(bed.Dialogs.Requests);
        Assert.Equal(("Msg.Debug.StartFailed", "Debug adapter not found: " + Path.Combine(bed.LlvmDir, "lldb-dap.exe")), (failed.MessageId, Assert.Single(failed.Args)));
        Assert.Empty(bed.Processes.Started);
        Assert.Equal(IdeState.Idle, bed.State);
        Assert.Equal([tools], bed.Engine.Requests.Select(r => r.ProjectPath));

        // Run Without Debugging: the member's program through the process launcher.
        await commands.ExecuteAsync(Command, tools);
        Assert.Equal(expected, Assert.Single(bed.Processes.Started).Request);
        Assert.Equal(new IdeStateInfo(IdeState.Running, null, false, ToolsExe(bed), bed.Processes.Last.ProcessId), bed.Services.State.Current);
        Assert.Null(bed.Debug.Session);
        Assert.Equal([tools, tools, tools], bed.Engine.Requests.Select(r => r.ProjectPath)); // the debug start, then the run's own build
        Assert.Equal(bed.ProjectPath, bed.Services.Projects.Project!.FilePath);
        await ExitAsync();

        // The adapter ends right after the launch: the same question, the same member.
        bed.Sessions.UseScenario("""{ "steps": [ { "on": "configurationDone", "respond": {}, "thenExit": 7 } ] }""");
        bed.Dialogs.Enqueue("Msg.Debug.StartFailed", "RunWithoutDebugging");
        await commands.ExecuteAsync(Command, tools);
        await bed.WaitForStateAsync(IdeState.Running);
        Assert.Equal(3, bed.Dialogs.Requests.Count);
        Assert.Equal([expected, expected], bed.Processes.Started.Select(p => p.Request));
        Assert.DoesNotContain(bed.ProjectPath, bed.Engine.Requests.Select(r => r.ProjectPath));
        Assert.Equal(bed.ProjectPath, bed.Services.Projects.Project!.FilePath);
        await ExitAsync();
        await bed.Debug.ShutdownAsync();
    });

    /// <summary>
    /// Card F09-056, acceptance 3, under the debugger. IDE-PM-199 (3) with IDE-PM-195: a failed build of the member
    /// starts no session and brings Messages to the front; nothing is asked. With the program of an earlier build of the
    /// member's active configuration on disk <c>Msg.Run.BuildFailedRunPrevious</c> is asked — Cancel starts nothing, Run
    /// Last Build debugs the member's file.
    /// </summary>
    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "IDE-PM-195")]
    [Trait("Req", "IDE-PM-199")]
    public Task FailedBuildOfTheMember_StartsNoSession() => TestUiThread.RunAsync(async () =>
    {
        using var bed = NewBed();
        await OpenGroupAsync(bed);
        var commands = bed.Services.Commands;
        var tools = Proj(bed, "Tools");
        var error = FakeBuildEngine.Diag("E2003", DiagnosticSeverity.Error, "Undeclared identifier \"Foo\"", ToolsUnit(bed), 7, 3, 3);
        var shown = 0;
        var show = bed.Services.Build.ShowMessages;
        bed.Services.Build.ShowMessages = () =>
        {
            shown++;
            show?.Invoke();
        };
        bed.Sessions.UseScenario("{}");

        bed.Engine.Enqueue(FakeBuildEngine.Failure(tools, error));
        await commands.ExecuteAsync(Command, tools);

        Assert.Equal(tools, Assert.Single(bed.Engine.Requests).ProjectPath);
        Assert.Empty(bed.Sessions.Sessions);
        Assert.Empty(bed.Processes.Started);
        Assert.Equal(1, shown);
        Assert.Equal("E2003", bed.Services.Messages.Items[0].Code);
        Assert.Equal(IdeState.Idle, bed.State);
        Assert.Empty(bed.Dialogs.Requests);

        // The program of an earlier build is there: the question, with Cancel as its default; Cancel starts nothing.
        bed.File(ToolsExe(bed), "");
        bed.Dialogs.Enqueue("Msg.Run.BuildFailedRunPrevious", "Cancel", "RunLastBuild");
        bed.Engine.Enqueue(FakeBuildEngine.Failure(tools, error));
        await commands.ExecuteAsync(Command, tools);
        Assert.Equal("Msg.Run.BuildFailedRunPrevious", Assert.Single(bed.Dialogs.Requests).MessageId);
        Assert.Equal("Cancel", MessageDefinitions.Get("Msg.Run.BuildFailedRunPrevious").Buttons[0]);
        Assert.Empty(bed.Sessions.Sessions);
        Assert.Equal(IdeState.Idle, bed.State);

        // Run Last Build: a session for that file of the member.
        bed.Engine.Enqueue(FakeBuildEngine.Failure(tools, error));
        await commands.ExecuteAsync(Command, tools);
        Assert.Same(Assert.Single(bed.Sessions.Sessions), bed.Debug.Session);
        var launch = Launch(Assert.Single(bed.Sessions.SentArguments("launch")));
        Assert.Equal((ToolsExe(bed), ToolsData(bed)), (launch.Program, launch.WorkingDir));
        Assert.Equal((IdeState.DebugRunning, ToolsExe(bed)), (bed.State, bed.Services.State.Current.ProgramPath));
        Assert.Equal([tools, tools, tools], bed.Engine.Requests.Select(r => r.ProjectPath));
        Assert.Equal(3, shown);
        Assert.Equal(bed.ProjectPath, bed.Services.Projects.Project!.FilePath);
        await bed.Debug.ShutdownAsync();
    });

    /// <summary>
    /// IDE-PM-197 for a debug session, IDE-PM-199: <c>run.restart</c> ends the session of the member and launches the
    /// member again under the debugger, not the active project. A member that is no longer available: the session is
    /// ended, nothing starts, the status says so.
    /// </summary>
    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "IDE-PM-197")]
    [Trait("Req", "IDE-PM-199")]
    public Task RestartRepeatsTheListedMemberUnderTheDebugger() => TestUiThread.RunAsync(async () =>
    {
        using var bed = NewBed();
        await OpenGroupAsync(bed);
        var commands = bed.Services.Commands;
        var tools = Proj(bed, "Tools");
        bed.Sessions.UseScenario("{}");
        await commands.ExecuteAsync(Command, tools);
        var first = Assert.Single(bed.Sessions.Sessions);
        Assert.Equal(IdeState.DebugRunning, bed.State);
        Assert.True(commands.CanExecute("run.restart"));

        await commands.ExecuteAsync("run.restart");

        Assert.Equal(DebugState.Terminated, first.State);
        Assert.Equal(2, bed.Sessions.Sessions.Count);
        Assert.Same(bed.Sessions.Last, bed.Debug.Session);
        Assert.Equal([tools, tools], bed.Engine.Requests.Select(r => r.ProjectPath));
        var launches = bed.Sessions.SentArguments("launch").Select(Launch).ToList();
        Assert.Equal([ToolsExe(bed), ToolsExe(bed)], launches.Select(l => l.Program));
        Assert.Equal((ToolsData(bed), "--selftest|a b"), (launches[1].WorkingDir, string.Join("|", launches[1].Arguments)));
        Assert.Equal(bed.Sessions.Requested[0].UserSourceRoots, bed.Sessions.Requested[1].UserSourceRoots);
        Assert.Equal((IdeState.DebugRunning, ToolsExe(bed)), (bed.State, bed.Services.State.Current.ProgramPath));
        Assert.Empty(bed.Processes.Started);
        Assert.Equal(bed.ProjectPath, bed.Services.Projects.Project!.FilePath);
        Assert.Empty(bed.Dialogs.Requests);

        // The member's file is gone before the restart: the session ends, nothing starts, Msg.Run.MemberUnavailable.
        File.Delete(tools);
        bed.Services.Groups.Refresh();
        Assert.False(bed.Services.Groups.Members.Single(m => m.FilePath == tools).IsAvailable);
        var second = bed.Sessions.Last;
        await commands.ExecuteAsync("run.restart");
        Assert.Equal(DebugState.Terminated, second.State);
        Assert.Equal(2, bed.Sessions.Sessions.Count);
        Assert.Equal(2, bed.Engine.Requests.Count);
        Assert.Equal(F(Strings.Msg_Run_MemberUnavailable, "Tools"), bed.Services.Status.Message);
        Assert.Equal(IdeState.Idle, bed.State);
        Assert.Null(bed.Debug.Session);

        // After a session of the active project, Restart is the active project again.
        Assert.True(await bed.Debug.StartAsync());
        await commands.ExecuteAsync("run.restart");
        Assert.Equal([tools, tools, bed.ProjectPath, bed.ProjectPath], bed.Engine.Requests.Select(r => r.ProjectPath));
        Assert.Equal(4, bed.Sessions.Sessions.Count);
        await bed.Debug.ShutdownAsync();
        Assert.Empty(bed.Dialogs.Requests);
    });

    /// <summary>
    /// IDE-PM-192 for <c>run.start</c>: the item of a listed root is enabled only while the IDE is Idle, for an available
    /// member that is runnable — a library in addition needs its host application file (IDE-TGT-014). While a session
    /// exists it is disabled and executing the command with the path starts nothing; the command without argument keeps
    /// its own states (Continue while paused). The active project's own path as the target is the command without one.
    /// </summary>
    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "IDE-PM-192")]
    public Task ListedMemberRunItem_EnabledOnlyWhenIdleAvailableAndDebuggable() => TestUiThread.RunAsync(async () =>
    {
        using var bed = NewBed();
        await OpenGroupAsync(bed);
        var commands = bed.Services.Commands;
        var tools = Proj(bed, "Tools");
        var plug = Proj(bed, "Plug");

        // A library member whose host application is no file: it can be run (the start reports the missing host), not debugged.
        Assert.Equal((false, true), (Item(bed, "Plug").IsEnabled, Item(bed, "Plug", "run.startWithoutDebugging").IsEnabled));
        Assert.False(commands.CanExecute(Command, plug));
        await commands.ExecuteAsync(Command, plug);
        Assert.Empty(bed.Engine.Requests);
        Assert.Equal(IdeState.Idle, bed.State);
        var host = bed.File(Path.Combine(Dir(bed, "Plug"), @"host\Loader.exe"), "");
        Assert.True(Item(bed, "Plug").IsEnabled);

        // A path that is no member of the group: nothing.
        var stranger = Path.Combine(Root(bed), @"Other\Other.pproj");
        Assert.False(commands.CanExecute(Command, stranger));
        await commands.ExecuteAsync(Command, stranger);
        Assert.Empty(bed.Engine.Requests);

        // A library member is debugged through its host: the host is the program, the built library a module of the session.
        var dll = Path.Combine(Dir(bed, "Plug"), "Plug.dll");
        bed.Engine.DefaultOutputPath = dll;
        bed.Sessions.UseScenario("{}");
        await commands.ExecuteAsync(Command, plug);
        Assert.Equal(plug, Assert.Single(bed.Engine.Requests).ProjectPath);
        var launch = Launch(Assert.Single(bed.Sessions.SentArguments("launch")));
        Assert.Equal((host, Path.GetDirectoryName(host)), (launch.Program, launch.WorkingDir));
        Assert.Equal(["-n", "3"], launch.Arguments);
        Assert.Equal([Dir(bed, "Plug")], bed.Sessions.Requested[^1].UserSourceRoots);
        Assert.Equal((IdeState.DebugRunning, host), (bed.State, bed.Services.State.Current.ProgramPath));
        await bed.WaitForLogAsync(() => bed.Debug.Log.Any(row => row.Message.Contains(Path.ChangeExtension(dll, ".pdi"), StringComparison.OrdinalIgnoreCase)));

        // While that session runs: the items of every listed root are disabled, and the command with a path starts nothing.
        Assert.Equal((false, false), (Item(bed, "Tools").IsEnabled, Item(bed, "Plug").IsEnabled));
        Assert.False(commands.CanExecute(Command, tools));
        await commands.ExecuteAsync(Command, tools);
        Assert.Single(bed.Engine.Requests);
        Assert.Single(bed.Sessions.Sessions);
        await bed.Debug.StopAsync();
        Assert.True(Item(bed, "Tools").IsEnabled);

        // Paused in a session of the active project: the listed item is disabled; the command without argument continues.
        bed.Engine.DefaultOutputPath = bed.Exe;
        bed.Sessions.UseScenario($$"""
            { "steps": [ {{DebugTestBed.StopAtStart(bed.UnitPath, 7)}}, {{DebugScenarios.Continue()}} ] }
            """);
        await StopAfterAsync(bed, Command, null);
        Assert.Equal(IdeState.Paused, bed.State);
        Assert.Equal((false, false), (Item(bed, "Tools").IsEnabled, commands.CanExecute(Command, tools)));
        Assert.True(commands.CanExecute(Command));
        await commands.ExecuteAsync(Command, tools); // nothing: not a Continue either
        Assert.Equal(IdeState.Paused, bed.State);
        Assert.Empty(bed.Sessions.SentRequests("continue"));
        await commands.ExecuteAsync(Command, bed.ProjectPath); // the active project's own path is the command without argument
        await bed.WaitForStateAsync(IdeState.DebugRunning);
        Assert.Single(bed.Sessions.SentRequests("continue"));
        Assert.Equal(2, bed.Sessions.Sessions.Count);
        await bed.Debug.ShutdownAsync();
        Assert.Empty(bed.Dialogs.Requests);
    });
}
