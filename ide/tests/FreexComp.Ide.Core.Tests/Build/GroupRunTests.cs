using System.Globalization;
using FreexComp.Build;
using FreexComp.Compiler;
using FreexComp.Compiler.Text;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Menus;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Build;

/// <summary>
/// Running a listed member of a project group without the debugger from its root row (group-member-run.md
/// IDE-PM-190…198): the row's project file is the argument of <c>run.startWithoutDebugging</c>; the member is built with
/// its own configuration and started with its own run parameters; the active project does not change. Projects below
/// <c>C:\g</c>: App (active), Tools (console, active configuration Release, run parameters), Pkg (app type
/// <c>package</c>, which the project format does not load: an unavailable member), Lib (a library without a host
/// application), Plug (a library with one), Gone (no file) and <c>C:\g\All.pgroup</c>.
/// </summary>
[Trait("Phase", "8")]
public sealed class GroupRunTests
{
    private const string Root = @"C:\g";
    private const string GroupPath = Root + @"\All.pgroup";
    private const string Command = "run.startWithoutDebugging";
    private const string ToolsArguments = "--selftest \"a b\"";
    private const string ToolsExe = Root + @"\Tools\bin\Release\Tools.exe";
    private const string ToolsData = Root + @"\Tools\data";
    private const string AppExe = Root + @"\App\App.exe";
    private const string Configurations = """
        "configurations": { "Debug": { "optimization": 0, "debugInfo": true, "defines": [], "outputDir": "bin/Debug" },
                            "Release": { "optimization": 2, "debugInfo": false, "defines": [], "outputDir": "bin/Release" } }
        """;

    private static readonly FocusState ProjectManagerFocus = new(CommandScope.ProjectManager, ProjectManagerViewModel.ToolName, null, false);
    private static readonly IdeStateInfo Building = new(IdeState.Building, BuildKind.Build, false, null, null);

    private static string Proj(string name) => $@"{Root}\{name}\{name}.pproj";

    private static string F(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    private static void Project(BuildTestBed bed, string name, string appType, string extra = "")
    {
        bed.File(Proj(name), $$"""
            { "formatVersion": 1, "name": "{{name}}", "appType": "{{appType}}", "mainSource": "{{name}}.pas",
              "files": [ { "path": "{{name}}.pas" } ]{{extra}} }
            """);
        bed.File($@"{Root}\{name}\{name}.pas", $"program {name};\nbegin\nend.\n");
    }

    /// <summary>The group opened with App as the active project; the fake engine's default output is the program of Tools.</summary>
    private static async Task<BuildTestBed> OpenGroupAsync(string toolsWorkingDir = "data", bool createData = true)
    {
        var bed = new BuildTestBed();
        Project(bed, "App", "console");
        Project(bed, "Tools", "console", $$"""
            , "activeConfiguration": "Release", {{Configurations}},
              "run": { "arguments": "--selftest \"a b\"", "workingDir": "{{toolsWorkingDir}}" }
            """);
        Project(bed, "Pkg", "package");
        Project(bed, "Lib", "library", """, "run": { "arguments": "-x", "workingDir": "" }""");
        Project(bed, "Plug", "library", $$"""
            , {{Configurations}}, "run": { "arguments": "-n 3", "workingDir": "data", "hostApplication": "host/Loader.exe" }
            """);
        bed.File(GroupPath, """
            { "formatVersion": 1, "projects": ["App/App.pproj", "Tools/Tools.pproj", "Pkg/Pkg.pproj", "Lib/Lib.pproj", "Plug/Plug.pproj", "Gone/Gone.pproj"],
              "activeProject": "App/App.pproj" }
            """);
        if (createData)
            bed.Fs.CreateDirectory(ToolsData);
        bed.Fs.CreateDirectory(Root + @"\Plug\data");
        bed.Engine.DefaultOutputPath = ToolsExe;
        Assert.True(await bed.Services.Projects.OpenAsync(GroupPath));
        await bed.Services.ProjectManager.MissingFileChecks;
        bed.Services.FocusTracker.Update(ProjectManagerFocus);
        return bed;
    }

    /// <summary>The context menu of the root row of <paramref name="member"/>.</summary>
    private static IReadOnlyList<MenuItemModel> RootMenu(BuildTestBed bed, string member)
    {
        var pm = bed.Services.ProjectManager;
        pm.Select(pm.Find(member == "App" ? "$project" : "$project:" + member)!);
        return pm.ContextMenu();
    }

    /// <summary>The Run Without Debugging item of the listed root of <paramref name="member"/>.</summary>
    private static MenuItemModel RunItem(BuildTestBed bed, string member) => Assert.Single(RootMenu(bed, member), i => i.CommandId == Command);

    /// <summary>Completes when the run service reports that the program ended (it posts from the exit continuation).</summary>
    private static Task Ended(BuildTestBed bed)
    {
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bed.Services.Run.ProgramEnded += (_, _) => ended.TrySetResult();
        return ended.Task;
    }

    private static async Task ExitAsync(BuildTestBed bed, int code = 0)
    {
        var ended = Ended(bed);
        bed.Processes.Last.Exit(code);
        await ended;
        Assert.Equal(IdeState.Idle, bed.Services.State.Current.State);
    }

    private static Diagnostic ToolsError => FakeBuildEngine.Diag("E2003", DiagnosticSeverity.Error, "Undeclared identifier \"Foo\"",
        Root + @"\Tools\Tools.pas", 2, 1, 3);

    /// <summary>
    /// Card F09-056, acceptance 1. IDE-PM-190: the listed root has Run Without Debugging in a group of its own after the
    /// build items, with the row's project file as its target. IDE-PM-193: one request for that file with the member's
    /// configuration, then its program with its arguments in its working directory; Building, then Running without Idle
    /// in between. IDE-PM-196: the group file, the loaded project and the active member are what they were.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-190")]
    [Trait("Req", "IDE-PM-193")]
    [Trait("Req", "IDE-PM-196")]
    public Task RunWithoutDebuggingOnListedMember_StartsItsExe_ActiveUnchanged() => TestUiThread.RunAsync(async () =>
    {
        var bed = await OpenGroupAsync();
        var commands = bed.Services.Commands;
        var groupText = bed.Text(GroupPath);
        var toolsText = bed.Text(Proj("Tools"));

        // The menu: …, Clean, —, Run, Run Without Debugging, —, …; the item carries the row's project file and is enabled.
        var menu = RootMenu(bed, "Tools");
        var ids = menu.Select(i => i.CommandId ?? "-").ToList();
        var at = ids.IndexOf(Command);
        Assert.True(at >= 5, "the listed root has no Run Without Debugging item");
        Assert.Equal(["project.build", "project.rebuild", "project.clean", "-", "run.start", Command, "-"], ids.Skip(at - 5).Take(7));
        Assert.Equal("project.setActive", ids[0]);
        var item = menu[at];
        Assert.Equal((Proj("Tools"), true, true), (item.Argument, item.ArgumentIsTarget, item.IsEnabled));

        var states = new List<IdeState>();
        bed.Services.State.Changed += (_, _) => states.Add(bed.Services.State.Current.State);
        await commands.ExecuteAsync(Command, item.Argument);

        var request = Assert.Single(bed.Engine.Requests);
        Assert.Equal((Proj("Tools"), null, "Release", "win64", BuildTarget.Build),
            (request.ProjectPath, request.ProjectInGroup, request.Configuration, request.Platform, request.Target));
        Assert.True(request.StopOnFirstFailure);
        var process = Assert.Single(bed.Processes.Started);
        Assert.Equal(ToolsExe, bed.Engine.Current!.Result.OutputPath);
        Assert.Equal(new ProcessStartRequest(ToolsExe, ToolsArguments, ToolsData), process.Request);
        Assert.Equal([IdeState.Building, IdeState.Running], states); // no Idle in between (IDE-P3-002)
        Assert.Equal(new IdeStateInfo(IdeState.Running, null, false, ToolsExe, process.ProcessId), bed.Services.State.Current);
        Assert.Contains(F(Strings.Msg_Run_Started, ToolsExe, ToolsArguments, process.ProcessId), bed.OutputText(OutputChannels.Program));

        // What the member run did not change.
        Assert.Equal(groupText, bed.Text(GroupPath));
        Assert.Equal(toolsText, bed.Text(Proj("Tools")));
        Assert.Equal(Proj("App"), bed.Services.Projects.Project!.FilePath);
        Assert.Equal("Debug", bed.Services.Projects.Project.ActiveConfiguration);
        Assert.Equal(Proj("App"), bed.Services.Groups.ActiveMember!.FilePath);
        Assert.Empty(bed.Dialogs.Requests);

        await ExitAsync(bed, 3);
        Assert.Equal(3, bed.Services.Run.LastExitCode);
        Assert.Equal([IdeState.Building, IdeState.Running, IdeState.Idle], states);
        Assert.Equal(Proj("App"), bed.Services.Projects.Project!.FilePath);
    });

    /// <summary>
    /// Card F09-056, acceptance 3. IDE-PM-195: a failed build of the member starts nothing and brings Messages to the
    /// front; nothing is asked. With the program of an earlier build of the member's active configuration on disk
    /// <c>Msg.Run.BuildFailedRunPrevious</c> is asked — Cancel starts nothing, Run Last Build starts the member's file.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-195")]
    public Task BuildFailurePreventsLaunchAndFocusesMessages() => TestUiThread.RunAsync(async () =>
    {
        var bed = await OpenGroupAsync();
        var commands = bed.Services.Commands;
        var shown = 0;
        var show = bed.Services.Build.ShowMessages;
        bed.Services.Build.ShowMessages = () =>
        {
            shown++;
            show?.Invoke();
        };

        bed.Engine.Enqueue(FakeBuildEngine.Failure(Proj("Tools"), ToolsError));
        await commands.ExecuteAsync(Command, Proj("Tools"));

        Assert.Equal(Proj("Tools"), Assert.Single(bed.Engine.Requests).ProjectPath);
        Assert.Empty(bed.Processes.Started);
        Assert.Equal(1, shown);
        Assert.Equal("E2003", bed.Services.Messages.Items[0].Code);
        Assert.Equal(IdeState.Idle, bed.Services.State.Current.State);
        Assert.Empty(bed.Dialogs.Requests);

        // An earlier program of the member (and programs of the active project, which are not the ones asked about).
        bed.File(ToolsExe, "MZ");
        bed.File(AppExe, "MZ");
        bed.File(Root + @"\App\bin\Release\App.exe", "MZ");
        bed.Dialogs.Enqueue("Msg.Run.BuildFailedRunPrevious", "Cancel", "RunLastBuild");
        bed.Engine.Enqueue(FakeBuildEngine.Failure(Proj("Tools"), ToolsError));
        await commands.ExecuteAsync(Command, Proj("Tools"));
        Assert.Single(bed.Dialogs.Requests, r => r.MessageId == "Msg.Run.BuildFailedRunPrevious");
        Assert.Empty(bed.Processes.Started);
        Assert.Equal(2, shown);
        Assert.Equal(IdeState.Idle, bed.Services.State.Current.State);

        bed.Engine.Enqueue(FakeBuildEngine.Failure(Proj("Tools"), ToolsError));
        await commands.ExecuteAsync(Command, Proj("Tools"));
        Assert.Equal(3, bed.Engine.Requests.Count);
        Assert.Equal(new ProcessStartRequest(ToolsExe, ToolsArguments, ToolsData), Assert.Single(bed.Processes.Started).Request);
        Assert.Equal(IdeState.Running, bed.Services.State.Current.State);
        Assert.Equal(Proj("App"), bed.Services.Projects.Project!.FilePath);
        await ExitAsync(bed);
    });

    /// <summary>
    /// IDE-PM-190, IDE-PM-192: an unavailable root has no run item (a file that is missing; a package project, which
    /// the project format does not load, CMP-1080); the item of a member that is not runnable (a library without a host
    /// application) is disabled; while Building and while Running the item of a runnable member is disabled and the
    /// command with its path starts nothing — the restart question belongs to the command without argument, which still
    /// starts the active project.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-190")]
    [Trait("Req", "IDE-PM-192")]
    public Task ListedMemberRun_EnabledOnlyWhenIdleAvailableAndRunnable() => TestUiThread.RunAsync(async () =>
    {
        var bed = await OpenGroupAsync();
        var commands = bed.Services.Commands;
        var run = bed.Services.Run;

        Assert.Equal(["Pkg", "Gone"], bed.Services.Groups.Members.Where(m => !m.IsAvailable).Select(m => m.Name));
        Assert.All((string[])["Pkg", "Gone"], name =>
        {
            Assert.DoesNotContain(RootMenu(bed, name), i => i.CommandId is "run.start" or Command);
            Assert.False(commands.CanExecute(Command, Proj(name)), name);
            Assert.False(run.CanStartMember(Proj(name)), name);
        });
        Assert.False(RunItem(bed, "Lib").IsEnabled);
        Assert.False(commands.CanExecute(Command, Proj("Lib")));
        Assert.False(run.CanStartMember(Proj("Lib")));
        Assert.All((string[])["Tools", "Plug"], name =>
        {
            Assert.True(RunItem(bed, name).IsEnabled, name);
            Assert.True(run.CanStartMember(Proj(name)), name);
        });
        Assert.False(run.CanStartMember(Root + @"\Other\Other.pproj")); // no member of the group
        Assert.False(run.CanStartMember(null));
        Assert.False(await run.StartMemberAsync(Proj("Gone")));
        Assert.Null(bed.Services.Status.Message); // not an available member: nothing happens
        Assert.False(await run.StartMemberAsync(Proj("Lib")));
        Assert.Equal(Strings.Msg_Run_NotRunnable, bed.Services.Status.Message); // IDE-PM-193 (1)
        Assert.Empty(bed.Engine.Requests);
        Assert.Empty(bed.Dialogs.Requests);

        // Building: disabled; executing with the path does nothing.
        Assert.True(bed.Services.State.TryTransition(Building));
        Assert.False(RunItem(bed, "Tools").IsEnabled);
        Assert.False(commands.CanExecute(Command, Proj("Tools")));
        await commands.ExecuteAsync(Command, Proj("Tools"));
        Assert.False(await run.StartMemberAsync(Proj("Tools")));
        Assert.True(bed.Services.State.TryTransition(IdeStateInfo.Idle));
        Assert.Empty(bed.Engine.Requests);
        Assert.True(RunItem(bed, "Tools").IsEnabled);

        // The command without argument starts the active project, also with a listed root selected.
        bed.Engine.Enqueue(FakeBuildEngine.Success(Proj("App"), AppExe));
        Assert.Equal(["$project:Tools"], bed.Services.ProjectManager.Selection.Select(n => n.Key));
        await commands.ExecuteAsync(Command);
        Assert.Equal(Proj("App"), Assert.Single(bed.Engine.Requests).ProjectPath);
        Assert.Equal(AppExe, Assert.Single(bed.Processes.Started).Request.FileName);
        Assert.Equal(IdeState.Running, bed.Services.State.Current.State);

        // Running: disabled; executing with the path starts nothing and asks nothing. The command without argument —
        // and with the active project's own path, which is the same — stays available (it asks about a restart).
        Assert.False(RunItem(bed, "Tools").IsEnabled);
        Assert.False(commands.CanExecute(Command, Proj("Tools")));
        Assert.False(run.CanStartMember(Proj("Tools")));
        await commands.ExecuteAsync(Command, Proj("Tools"));
        Assert.False(await run.StartMemberAsync(Proj("Tools")));
        Assert.Single(bed.Engine.Requests);
        Assert.Single(bed.Processes.Started);
        Assert.Empty(bed.Dialogs.Requests);
        Assert.Equal(0, bed.Processes.Last.KillCount);
        Assert.True(commands.CanExecute(Command));
        Assert.True(commands.CanExecute(Command, Proj("App")));
        await ExitAsync(bed);

        // A target that is the active project's own path behaves as no argument.
        bed.Engine.Enqueue(FakeBuildEngine.Success(Proj("App"), AppExe));
        await commands.ExecuteAsync(Command, Proj("App"));
        Assert.Equal(Proj("App"), bed.Engine.Requests[^1].ProjectPath);
        Assert.Equal(AppExe, bed.Processes.Last.Request.FileName);
        Assert.Equal(2, bed.Processes.Started.Count);
        await ExitAsync(bed);
    });

    /// <summary>
    /// IDE-PM-194: the member's project file is open as a modified text document with other run arguments — the build
    /// saves it, and the start uses what was saved (the definition is taken from the group after the build returned).
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-194")]
    public Task ListedMemberRun_UsesRunParametersSavedByTheBuild() => TestUiThread.RunAsync(async () =>
    {
        var bed = await OpenGroupAsync();
        var document = await bed.OpenDocumentAsync(Proj("Tools"));
        const string Old = "--selftest \\\"a b\\\"";
        var at = document.Buffer.Text.IndexOf(Old, StringComparison.Ordinal);
        Assert.True(at > 0);
        document.Buffer.ApplyEdits([new TextEdit(at, Old.Length, "--fast 2")]);
        Assert.True(document.IsModified);
        Assert.Contains("--selftest", bed.Text(Proj("Tools")), StringComparison.Ordinal);

        await bed.Services.Commands.ExecuteAsync(Command, Proj("Tools"));

        Assert.False(document.IsModified);
        Assert.Contains("\"--fast 2\"", bed.Text(Proj("Tools")), StringComparison.Ordinal);
        Assert.Equal(Proj("Tools"), Assert.Single(bed.Engine.Requests).ProjectPath);
        var process = Assert.Single(bed.Processes.Started);
        Assert.Equal(new ProcessStartRequest(ToolsExe, "--fast 2", ToolsData), process.Request);
        Assert.Contains(F(Strings.Msg_Run_Started, ToolsExe, "--fast 2", process.ProcessId), bed.OutputText(OutputChannels.Program));
        Assert.Equal(Proj("App"), bed.Services.Projects.Project!.FilePath);
        await ExitAsync(bed);
    });

    /// <summary>
    /// IDE-PM-193 (6): a library member is built and its host application — resolved against the member's directory — is
    /// started with the member's arguments in the member's working directory, like a program of its own; a host file
    /// that does not exist shows <c>Msg.Run.NoExecutable</c> with its path.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-193")]
    public Task ListedMemberRun_LibraryStartsItsHostApplication() => TestUiThread.RunAsync(async () =>
    {
        const string Host = Root + @"\Plug\host\Loader.exe";
        const string Dll = Root + @"\Plug\bin\Debug\Plug.dll";
        var bed = await OpenGroupAsync();
        bed.File(Host, "MZ");
        bed.Services.Settings.Set(FreexComp.Ide.Core.Settings.SettingsCatalog.RunConsoleMode, "external"); // applies to console projects only

        bed.Engine.Enqueue(FakeBuildEngine.Success(Proj("Plug"), Dll));
        await bed.Services.Commands.ExecuteAsync(Command, RunItem(bed, "Plug").Argument);

        var request = Assert.Single(bed.Engine.Requests);
        Assert.Equal((Proj("Plug"), "Debug", BuildTarget.Build), (request.ProjectPath, request.Configuration, request.Target));
        Assert.True(bed.Fs.FileExists(Dll));
        var process = Assert.Single(bed.Processes.Started);
        Assert.Equal(new ProcessStartRequest(Host, "-n 3", Root + @"\Plug\data") { Capture = false, ConsoleHost = false }, process.Request);
        Assert.Equal(new IdeStateInfo(IdeState.Running, null, false, Host, process.ProcessId), bed.Services.State.Current);
        Assert.Equal(Proj("App"), bed.Services.Projects.Project!.FilePath);
        await ExitAsync(bed);

        // The host file is gone: the library is built, nothing is started.
        bed.Fs.DeleteFile(Host);
        bed.Dialogs.Answer("Msg.Run.NoExecutable", "OK");
        Assert.True(bed.Services.Run.CanStartMember(Proj("Plug"))); // runnable: it names a host (the file is checked after the build)
        bed.Engine.Enqueue(FakeBuildEngine.Success(Proj("Plug"), Dll));
        await bed.Services.Commands.ExecuteAsync(Command, Proj("Plug"));
        Assert.Equal(2, bed.Engine.Requests.Count);
        Assert.Equal([Host], Assert.Single(bed.Dialogs.Requests, r => r.MessageId == "Msg.Run.NoExecutable").Args);
        Assert.Single(bed.Processes.Started);
        Assert.Equal(IdeState.Idle, bed.Services.State.Current.State);
    });

    /// <summary>
    /// IDE-PM-193 with IDE-P3-035: the member's program is held by a process started outside the IDE (E4008) —
    /// <c>Msg.Build.OutputLocked</c>; Stop and Build ends that process and repeats the member's build, and the run
    /// continues from the repeated build; Cancel starts nothing.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-193")]
    public Task ListedMemberRun_OutputLocked_StopAndBuildThenStarts() => TestUiThread.RunAsync(async () =>
    {
        var bed = await OpenGroupAsync();
        FakeBuild Locked() => FakeBuildEngine.Failure(Proj("Tools"), Diagnostic.Create("E4008", Location.ForFile(ToolsExe), ToolsExe));

        bed.Engine.Enqueue(Locked());
        bed.Dialogs.Enqueue("Msg.Build.OutputLocked", "Cancel");
        await bed.Services.Commands.ExecuteAsync(Command, Proj("Tools"));
        Assert.Empty(bed.Processes.Started);
        Assert.Empty(bed.Processes.Terminated);
        Assert.Equal(IdeState.Idle, bed.Services.State.Current.State);

        bed.Engine.Enqueue(Locked());
        bed.Dialogs.Enqueue("Msg.Build.OutputLocked", "StopAndBuild");
        await bed.Services.Commands.ExecuteAsync(Command, Proj("Tools"));
        Assert.Equal(["Tools.exe"], bed.Dialogs.Requests[^1].Args);
        Assert.Equal([ToolsExe], bed.Processes.Terminated);
        Assert.Equal([Proj("Tools"), Proj("Tools"), Proj("Tools")], bed.Engine.Requests.Select(r => r.ProjectPath));
        Assert.Equal(new ProcessStartRequest(ToolsExe, ToolsArguments, ToolsData), Assert.Single(bed.Processes.Started).Request);
        Assert.Equal(IdeState.Running, bed.Services.State.Current.State);
        Assert.DoesNotContain(bed.Dialogs.Requests, r => r.MessageId != "Msg.Build.OutputLocked");
        await ExitAsync(bed);
    });

    /// <summary>
    /// IDE-PM-198: the member's working directory does not exist — <c>Msg.Run.MemberWorkingDirMissing</c> with the
    /// project name and the folder and the one button OK, in place of the message whose button opens the Run Parameters
    /// of the active project; nothing is started.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-198")]
    public Task ListedMemberRun_MissingWorkingDir_ShowsMemberMessage() => TestUiThread.RunAsync(async () =>
    {
        var bed = await OpenGroupAsync(toolsWorkingDir: "nowhere", createData: false);
        var opened = 0;
        bed.Services.Run.OpenRunParameters = () =>
        {
            opened++;
            return Task.CompletedTask;
        };
        bed.Dialogs.Answer("Msg.Run.MemberWorkingDirMissing", "OK");
        bed.Dialogs.Answer("Msg.Run.WorkingDirMissing", "RunParameters"); // must not be the message shown

        await bed.Services.Commands.ExecuteAsync(Command, Proj("Tools"));

        Assert.Single(bed.Engine.Requests);
        var message = Assert.Single(bed.Dialogs.Requests);
        Assert.Equal("Msg.Run.MemberWorkingDirMissing", message.MessageId);
        Assert.Equal(["Tools", Root + @"\Tools\nowhere"], message.Args);
        Assert.Equal(["OK"], MessageDefinitions.Get(message.MessageId).Buttons);
        Assert.Equal(MessageKind.Error, MessageDefinitions.Get(message.MessageId).Kind);
        Assert.Equal("The working folder set in the run parameters of Tools does not exist.\nC:\\g\\Tools\\nowhere\n"
            + "Activate the project to change its run parameters.", F(Strings.Msg_Run_MemberWorkingDirMissing, [.. message.Args]));
        Assert.Empty(bed.Processes.Started);
        Assert.Equal(0, opened);
        Assert.Empty(bed.Dialogs.Dialogs);
        Assert.Equal(IdeState.Idle, bed.Services.State.Current.State);
    });

    /// <summary>
    /// IDE-PM-197: <c>run.restart</c> and the Restart answer of <c>Msg.Run.AlreadyRunning</c> repeat the start for the
    /// member the running program was started for, not for the active project. A member that is no longer available:
    /// the program is stopped, nothing starts, the status says so.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-197")]
    public Task RestartRepeatsTheListedMember() => TestUiThread.RunAsync(async () =>
    {
        var bed = await OpenGroupAsync();
        var commands = bed.Services.Commands;
        await commands.ExecuteAsync(Command, Proj("Tools"));
        var first = Assert.Single(bed.Processes.Started);

        await commands.ExecuteAsync("run.restart");

        Assert.Equal(1, first.KillCount);
        Assert.Equal([Proj("Tools"), Proj("Tools")], bed.Engine.Requests.Select(r => r.ProjectPath));
        Assert.Equal([ToolsExe, ToolsExe], bed.Processes.Started.Select(p => p.Request.FileName));
        var second = bed.Processes.Last;
        Assert.Equal(new ProcessStartRequest(ToolsExe, ToolsArguments, ToolsData), second.Request);
        Assert.Equal(new IdeStateInfo(IdeState.Running, null, false, ToolsExe, second.ProcessId), bed.Services.State.Current);

        // The command without argument while the member's program runs: the question names it; Restart is the member again.
        bed.Dialogs.Enqueue("Msg.Run.AlreadyRunning", "Cancel", "Restart");
        await commands.ExecuteAsync(Command);
        Assert.Equal(["Tools.exe"], bed.Dialogs.Requests[^1].Args);
        Assert.Equal(0, second.KillCount);
        Assert.Equal(2, bed.Processes.Started.Count);
        await commands.ExecuteAsync(Command);
        Assert.Equal(1, second.KillCount);
        Assert.Equal([Proj("Tools"), Proj("Tools"), Proj("Tools")], bed.Engine.Requests.Select(r => r.ProjectPath));
        Assert.Equal(3, bed.Processes.Started.Count);
        var third = bed.Processes.Last;
        Assert.Equal(ToolsExe, third.Request.FileName);
        Assert.Equal(Proj("App"), bed.Services.Projects.Project!.FilePath);

        // The member's file is gone before the restart: stopped, nothing started, Msg.Run.MemberUnavailable.
        bed.Fs.DeleteFile(Proj("Tools"));
        bed.Services.Groups.Refresh();
        Assert.False(bed.Services.Groups.Members.Single(m => m.FilePath == Proj("Tools")).IsAvailable);
        await commands.ExecuteAsync("run.restart");
        Assert.Equal(1, third.KillCount);
        Assert.Equal(3, bed.Engine.Requests.Count);
        Assert.Equal(3, bed.Processes.Started.Count);
        Assert.Equal(F(Strings.Msg_Run_MemberUnavailable, "Tools"), bed.Services.Status.Message);
        Assert.Equal("Tools is no longer available in the project group", bed.Services.Status.Message);
        Assert.Equal(IdeState.Idle, bed.Services.State.Current.State);
        Assert.DoesNotContain(bed.Dialogs.Requests, r => r.MessageId != "Msg.Run.AlreadyRunning");
    });
}
