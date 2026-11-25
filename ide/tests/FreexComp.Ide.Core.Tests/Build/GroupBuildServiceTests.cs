using FreexComp.Build;
using FreexComp.Compiler;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Build;

/// <summary>
/// Building a project group (project-manager.md IDE-PM-170…174): Build All / Rebuild All / Clean All as one request for
/// the group file, build of a member from its root row, the setting <c>build.continueAfterFailure</c> and the summary
/// that names the skipped projects. Three projects below <c>C:\g</c> (App, Lib, Tools) and <c>C:\g\All.pgroup</c>.
/// </summary>
[Trait("Phase", "8")]
public sealed class GroupBuildServiceTests
{
    private const string Root = @"C:\g";
    private const string GroupPath = Root + @"\All.pgroup";
    private static readonly FocusState ProjectManagerFocus = new(CommandScope.ProjectManager, ProjectManagerViewModel.ToolName, null, false);
    private static readonly IdeStateInfo Building = new(IdeState.Building, BuildKind.Build, false, null, null);

    private static string Proj(string name) => $@"{Root}\{name}\{name}.pproj";

    private static string Unit(string name) => $@"{Root}\{name}\{name}Unit.pas";

    private static BuildTestBed NewBed()
    {
        var bed = new BuildTestBed();
        foreach (var name in (string[])["App", "Lib", "Tools"])
        {
            bed.File(Proj(name), $$"""
                { "formatVersion": 1, "name": "{{name}}", "mainSource": "{{name}}.pas",
                  "files": [ { "path": "{{name}}.pas" }, { "path": "{{name}}Unit.pas" } ] }
                """);
            bed.File($@"{Root}\{name}\{name}.pas", $"program {name};\nbegin\nend.\n");
            bed.File(Unit(name), $"unit {name}Unit;\ninterface\nimplementation\nend.\n");
        }

        return bed;
    }

    private static async Task<BuildTestBed> OpenGroupAsync()
    {
        var bed = NewBed();
        bed.File(GroupPath, """
            { "formatVersion": 1, "projects": ["App/App.pproj", "Lib/Lib.pproj", "Tools/Tools.pproj"], "activeProject": "App/App.pproj" }
            """);
        Assert.True(await bed.Services.Projects.OpenAsync(GroupPath));
        await bed.Services.ProjectManager.MissingFileChecks;
        return bed;
    }

    /// <summary>The events of a request over several projects, as the engine reports them (CMP-1341): one bracket per project.</summary>
    private static FakeBuild Script(params (string Project, bool Success, BuildEvent[] Events)[] projects)
    {
        var now = DateTime.UtcNow;
        var events = new List<BuildEvent>();
        var results = new List<ProjectBuildResult>();
        foreach (var (project, success, middle) in projects)
        {
            var errors = middle.Count(e => e is BuildEvent.Diagnostic { Value.Severity: DiagnosticSeverity.Error });
            events.Add(new BuildEvent.Started(now, Proj(project), "Debug", "win64", BuildTarget.Build));
            events.AddRange(middle);
            events.Add(new BuildEvent.Finished(now, Proj(project), success, false, errors, 0, 0, TimeSpan.FromSeconds(1)));
            results.Add(new ProjectBuildResult(Proj(project), "Debug", success, false, success ? Path.ChangeExtension(Proj(project), ".exe") : null, []));
        }

        var total = events.Count(e => e is BuildEvent.Diagnostic { Value.Severity: DiagnosticSeverity.Error });
        return new FakeBuild(events, new BuildResult(projects.All(p => p.Success), false, total, 0, 0, [.. results], null, TimeSpan.FromSeconds(3)))
        {
            Bracket = false,
        };
    }

    private static BuildEvent Error(string project, string message) => new BuildEvent.Diagnostic(DateTime.UtcNow, Proj(project),
        FakeBuildEngine.Diag("E2003", DiagnosticSeverity.Error, message, Unit(project), 3, 1, 1));

    private static BuildEvent Skipped(string project, string failed) => new BuildEvent.OutputLine(DateTime.UtcNow, Proj(project), OutputSource.Info,
        $"{project}: skipped because the build stopped after {failed} failed");

    /// <summary>
    /// IDE-PM-171, IDE-PM-172: Build All is one request for the group file — no project in the group, no configuration, no
    /// platform, target Build — with <c>StopOnFirstFailure</c> unless <c>build.continueAfterFailure</c> is set. The
    /// command needs two or more members and the Idle state; the active project does not change.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-171")]
    [Trait("Req", "IDE-PM-172")]
    public Task BuildAll_RequestsTheGroupFile_WithStopOnFirstFailure() => TestUiThread.RunAsync(async () =>
    {
        var bed = await OpenGroupAsync();
        var commands = bed.Services.Commands;
        Assert.False(bed.Services.Settings.Get(SettingsCatalog.BuildContinueAfterFailure));

        Assert.True(commands.CanExecute("project.buildAll"));
        await commands.ExecuteAsync("project.buildAll");

        var request = Assert.Single(bed.Engine.Requests);
        Assert.Equal((GroupPath, null, null, null, BuildTarget.Build),
            (request.ProjectPath, request.ProjectInGroup, request.Configuration, request.Platform, request.Target));
        Assert.True(request.StopOnFirstFailure);
        Assert.Equal(Proj("App"), bed.Services.Projects.Project!.FilePath);
        Assert.Equal(IdeState.Idle, bed.Services.State.Current.State);
        Assert.Empty(bed.Processes.Started); // nothing is run

        // The setting on: the other projects are built after a failure.
        bed.Services.Settings.Set(SettingsCatalog.BuildContinueAfterFailure, true);
        await commands.ExecuteAsync("project.buildAll");
        Assert.False(bed.Engine.Requests[^1].StopOnFirstFailure);
        Assert.Equal(GroupPath, bed.Engine.Requests[^1].ProjectPath);

        // Idle only; a group of two or more members only.
        Assert.True(bed.Services.State.TryTransition(Building));
        Assert.All((string[])["project.buildAll", "project.rebuildAll", "project.cleanAll"], id => Assert.False(commands.CanExecute(id)));
        Assert.True(bed.Services.State.TryTransition(IdeStateInfo.Idle));
        Assert.All((string[])["project.buildAll", "project.rebuildAll", "project.cleanAll"], id => Assert.True(commands.CanExecute(id)));
        Assert.True(await bed.Services.Groups.RemoveAsync(Proj("Lib")));
        Assert.True(commands.CanExecute("project.buildAll")); // two members left
        Assert.True(await bed.Services.Groups.RemoveAsync(Proj("Tools")));
        Assert.All((string[])["project.buildAll", "project.rebuildAll", "project.cleanAll"], id => Assert.False(commands.CanExecute(id)));
        await commands.ExecuteAsync("project.buildAll");
        Assert.Equal(2, bed.Engine.Requests.Count);

        // A project opened alone: an implicit group of one.
        var alone = NewBed();
        Assert.True(await alone.Services.Projects.OpenAsync(Proj("App")));
        Assert.False(alone.Services.Commands.CanExecute("project.buildAll"));
        Assert.Null(await alone.Services.Build.BuildGroupAsync(BuildKind.Build));
        Assert.Empty(alone.Engine.Requests);
    });

    /// <summary>
    /// IDE-PM-171: Rebuild All and Clean All are the same request with their targets. Save-before-build covers every
    /// modified file document (also a file of a listed member, which is a document outside the loaded project) for Build
    /// All and Rebuild All; Clean All saves nothing.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-171")]
    public Task RebuildAllAndCleanAll_UseTheirTargets_CleanSavesNothing() => TestUiThread.RunAsync(async () =>
    {
        var bed = await OpenGroupAsync();
        var commands = bed.Services.Commands;
        var own = await bed.OpenDocumentAsync(Unit("App"));
        var listed = await bed.OpenDocumentAsync(Unit("Lib"));
        BuildTestBed.Type(own, "{a}");
        BuildTestBed.Type(listed, "{l}");

        await commands.ExecuteAsync("project.rebuildAll");
        var rebuild = Assert.Single(bed.Engine.Requests);
        Assert.Equal((GroupPath, null, null, null, BuildTarget.Rebuild),
            (rebuild.ProjectPath, rebuild.ProjectInGroup, rebuild.Configuration, rebuild.Platform, rebuild.Target));
        Assert.False(own.IsModified);
        Assert.False(listed.IsModified);
        Assert.StartsWith("{a}", bed.Text(Unit("App")), StringComparison.Ordinal);
        Assert.StartsWith("{l}", bed.Text(Unit("Lib")), StringComparison.Ordinal);

        BuildTestBed.Type(own, "{b}");
        BuildTestBed.Type(listed, "{m}");
        await commands.ExecuteAsync("project.cleanAll");
        var clean = bed.Engine.Requests[^1];
        Assert.Equal(2, bed.Engine.Requests.Count);
        Assert.Equal((GroupPath, null, null, null, BuildTarget.Clean),
            (clean.ProjectPath, clean.ProjectInGroup, clean.Configuration, clean.Platform, clean.Target));
        Assert.True(clean.StopOnFirstFailure);
        Assert.True(own.IsModified);
        Assert.True(listed.IsModified);
        Assert.StartsWith("{a}", bed.Text(Unit("App")), StringComparison.Ordinal);

        // Build All saves like Rebuild All.
        await commands.ExecuteAsync("project.buildAll");
        Assert.Equal(BuildTarget.Build, bed.Engine.Requests[^1].Target);
        Assert.False(own.IsModified);
        Assert.False(listed.IsModified);
        Assert.Equal(Proj("App"), bed.Services.Projects.Project!.FilePath);
    });

    /// <summary>
    /// IDE-PM-171: the engine builds a group from its file, so an unsaved group is saved first through
    /// <c>file.saveGroupAs</c>; canceling the picker means no build and the status <c>Msg.Group.SaveToBuildAll</c>.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-171")]
    public Task BuildAll_UnsavedGroup_SavesTheGroupFirst_CancelMeansNoBuild() => TestUiThread.RunAsync(async () =>
    {
        var bed = NewBed();
        var commands = bed.Services.Commands;
        var pickers = (FakeFileDialogService)bed.Services.FileDialogs;
        Assert.True(await bed.Services.Projects.OpenAsync(Proj("App")));
        Assert.Equal(1, await bed.Services.Groups.AddExistingProjectsAsync([Proj("Lib")]));
        Assert.True(bed.Services.Groups.IsUnsaved);
        Assert.True(commands.CanExecute("project.buildAll"));

        // Cancel in the picker: nothing is built, nothing is written.
        pickers.NextCancel();
        await commands.ExecuteAsync("project.buildAll");
        Assert.Equal("saveGroupAs", Assert.Single(pickers.Requests).PromptId);
        Assert.Empty(bed.Engine.Requests);
        Assert.Equal("Save the project group to build all projects.", bed.Services.Status.Message);
        Assert.True(bed.Services.Groups.IsUnsaved);
        Assert.Equal(IdeState.Idle, bed.Services.State.Current.State);

        // Saved: the request names the new group file.
        var target = Root + @"\Pair.pgroup";
        pickers.NextSave(target);
        await commands.ExecuteAsync("project.buildAll");
        Assert.Equal(2, pickers.Requests.Count);
        Assert.True(bed.Fs.FileExists(target));
        Assert.Equal(target, bed.Services.Groups.FilePath);
        Assert.False(bed.Services.Groups.IsUnsaved);
        var request = Assert.Single(bed.Engine.Requests);
        Assert.Equal((target, null, BuildTarget.Build), (request.ProjectPath, request.ProjectInGroup, request.Target));

        // A saved group asks nothing more.
        await commands.ExecuteAsync("project.cleanAll");
        Assert.Equal(2, pickers.Requests.Count);
        Assert.Equal((target, BuildTarget.Clean), (bed.Engine.Requests[^1].ProjectPath, bed.Engine.Requests[^1].Target));
    });

    /// <summary>
    /// Card F09-009 acceptance 3 at IDE level, IDE-PM-173: the second project fails, the third is skipped — Output has the
    /// engine's skipped line and closes with <c>Msg.Build.GroupSummary</c>, which is also the status text and the summary
    /// row; Messages lists the error. With every project built (setting on) the text ends after the counts; a request
    /// whose projects all succeed keeps the ordinary summary.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-173")]
    public Task BuildAll_FailedProject_SummaryNamesTheSkippedProjects() => TestUiThread.RunAsync(async () =>
    {
        var bed = await OpenGroupAsync();
        var build = bed.Services.Build;
        var messages = bed.Services.Messages;
        bed.Engine.Enqueue(Script(
            ("Lib", true, []),
            ("Tools", false, [Error("Tools", "Undeclared identifier 'x'")]),
            ("App", false, [Skipped("App", "Tools")])));

        var result = await build.BuildGroupAsync(BuildKind.Build);

        Assert.False(result!.Success);
        const string Summary = "1 succeeded, 1 failed, 1 skipped: App";
        var output = bed.OutputText(OutputChannels.Build);
        Assert.Contains("App: skipped because the build stopped after Tools failed", output);
        Assert.Equal(Summary, output[^1]);
        Assert.DoesNotContain("Build failed: 1 error", output);
        Assert.Equal(Summary, build.ResultText);
        Assert.Equal(["E2003", ""], messages.Items.Select(i => i.Code));
        Assert.Equal((Summary, true, MessageSeverity.Error), (messages.Items[^1].Text, messages.Items[^1].IsSummary, messages.Items[^1].Severity));
        Assert.Equal(IdeState.Idle, bed.Services.State.Current.State);

        // Two skipped projects: both names, in event order.
        bed.Engine.Enqueue(Script(
            ("Lib", false, [Error("Lib", "Undeclared identifier 'y'")]),
            ("Tools", false, [Skipped("Tools", "Lib")]),
            ("App", false, [Skipped("App", "Lib")])));
        await build.BuildGroupAsync(BuildKind.Rebuild);
        Assert.Equal("0 succeeded, 1 failed, 2 skipped: Tools, App", build.ResultText);

        // build.continueAfterFailure: the engine goes on, nothing is skipped — the text ends after the counts; the
        // diagnostics of every project are rows, in event order.
        bed.Services.Settings.Set(SettingsCatalog.BuildContinueAfterFailure, true);
        bed.Engine.Enqueue(Script(
            ("Lib", false, [Error("Lib", "Undeclared identifier 'y'")]),
            ("Tools", false, [Error("Tools", "Undeclared identifier 'x'")]),
            ("App", true, [])));
        await build.BuildGroupAsync(BuildKind.Build);
        Assert.False(bed.Engine.Requests[^1].StopOnFirstFailure);
        Assert.Equal("1 succeeded, 2 failed, 0 skipped", build.ResultText);
        Assert.Equal("1 succeeded, 2 failed, 0 skipped", bed.OutputText(OutputChannels.Build)[^1]);
        Assert.Equal(["Undeclared identifier 'y'", "Undeclared identifier 'x'"], messages.Items.Where(i => !i.IsSummary).Select(i => i.Text));

        // Every project built: the ordinary summary.
        bed.Engine.Enqueue(Script(("Lib", true, []), ("Tools", true, []), ("App", true, [])));
        Assert.True((await build.BuildGroupAsync(BuildKind.Build))!.Success);
        Assert.StartsWith("Build succeeded", build.ResultText, StringComparison.Ordinal);

        // One project of its own that fails: the ordinary failure text.
        bed.Engine.Enqueue(Script(("App", false, [Error("App", "Undeclared identifier 'z'")])));
        await build.BuildAsync(BuildKind.Build);
        Assert.Equal("Build failed: 1 error", build.ResultText);
    });

    /// <summary>
    /// IDE-PM-170: Build, Rebuild and Clean in the context menu of a listed root are one request for that member's
    /// <c>.pproj</c> with its own active configuration; its modified files are saved (not for Clean), the active project
    /// stays. From the menu, the tool bar and the shortcut the commands act on the active project, whatever is selected.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-170")]
    public Task RootRowBuild_BuildsThatMember_ActiveProjectUnchanged() => TestUiThread.RunAsync(async () =>
    {
        var bed = NewBed();
        bed.File(Proj("Lib"), """
            { "formatVersion": 1, "name": "Lib", "mainSource": "Lib.pas", "files": [ { "path": "Lib.pas" }, { "path": "LibUnit.pas" } ],
              "activeConfiguration": "Release", "configurations": { "Debug": {}, "Release": {} } }
            """);
        bed.File(GroupPath, """
            { "formatVersion": 1, "projects": ["App/App.pproj", "Lib/Lib.pproj", "Tools/Tools.pproj", "Gone/Gone.pproj"], "activeProject": "App/App.pproj" }
            """);
        Assert.True(await bed.Services.Projects.OpenAsync(GroupPath));
        var pm = bed.Services.ProjectManager;
        await pm.MissingFileChecks;
        var commands = bed.Services.Commands;
        bed.Services.FocusTracker.Update(ProjectManagerFocus);
        var own = await bed.OpenDocumentAsync(Unit("App"));
        var listed = await bed.OpenDocumentAsync(Unit("Lib"));
        BuildTestBed.Type(own, "{a}");
        BuildTestBed.Type(listed, "{l}");

        // The listed root's menu: the three items carry the member's path and are enabled.
        pm.Select(pm.Find("$project:Lib")!);
        var menu = pm.ContextMenu();
        var items = (string[])["project.build", "project.rebuild", "project.clean"];
        Assert.All(items, id =>
        {
            var item = Assert.Single(menu, i => i.CommandId == id);
            Assert.Equal((Proj("Lib"), true, true), (item.Argument, item.ArgumentIsTarget, item.IsEnabled));
        });

        var targets = new List<BuildTarget>();
        foreach (var id in items)
        {
            var item = menu.Single(i => i.CommandId == id);
            await commands.ExecuteAsync(id, item.Argument);
            var request = bed.Engine.Requests[^1];
            Assert.Equal((Proj("Lib"), null, "Release"), (request.ProjectPath, request.ProjectInGroup, request.Configuration));
            Assert.True(request.StopOnFirstFailure);
            targets.Add(request.Target);
        }

        Assert.Equal([BuildTarget.Build, BuildTarget.Rebuild, BuildTarget.Clean], targets);
        Assert.Equal(3, bed.Engine.Requests.Count);
        Assert.Equal(Proj("App"), bed.Services.Projects.Project!.FilePath);
        Assert.Equal(Proj("App"), bed.Services.Groups.ActiveMember!.FilePath);
        Assert.False(listed.IsModified); // the member's own file was saved before its build
        Assert.True(own.IsModified); // a file of another project was not

        // The command without the row's path (menu, tool bar, shortcut): the active project, also with the listed root selected.
        Assert.Equal(["$project:Lib"], pm.Selection.Select(n => n.Key));
        await commands.ExecuteAsync("project.build");
        Assert.Equal((Proj("App"), "Debug"), (bed.Engine.Requests[^1].ProjectPath, bed.Engine.Requests[^1].Configuration));
        Assert.False(own.IsModified);

        // The active root's items have no path: they are the commands as before.
        pm.Select(pm.Find("$project")!);
        Assert.All(items, id => Assert.Null(Assert.Single(pm.ContextMenu(), i => i.CommandId == id).Argument));

        // An unavailable member has no build items and cannot be built by its path; Idle only.
        pm.Select(pm.Find("$project:Gone")!);
        Assert.DoesNotContain(pm.ContextMenu(), i => i.CommandId is "project.build" or "project.rebuild" or "project.clean");
        Assert.False(commands.CanExecute("project.build", Root + @"\Gone\Gone.pproj"));
        Assert.Null(await bed.Services.Build.BuildMemberAsync(Root + @"\Gone\Gone.pproj", BuildKind.Build));
        Assert.Equal(4, bed.Engine.Requests.Count);
        Assert.True(commands.CanExecute("project.build", Proj("Lib")));
        Assert.True(bed.Services.State.TryTransition(Building));
        Assert.False(commands.CanExecute("project.build", Proj("Lib")));
        Assert.True(bed.Services.State.TryTransition(IdeStateInfo.Idle));
    });

    /// <summary>
    /// IDE-PM-174: trust applies to a group request and to the request of a listed member as to a single project with
    /// dependencies — a member with commands that is not trusted brings the question before anything is built; Cancel
    /// builds nothing, "Build without events" builds with the events skipped; Clean asks nothing.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-174")]
    public Task GroupAndMemberBuilds_AskAboutUntrustedCommandsLikeASingleProject() => TestUiThread.RunAsync(async () =>
    {
        var bed = NewBed();
        bed.File(Proj("Lib"), """
            { "formatVersion": 1, "name": "Lib", "mainSource": "Lib.pas", "files": [ { "path": "Lib.pas" }, { "path": "LibUnit.pas" } ],
              "events": { "preBuild": [ "echo generate" ] } }
            """);
        bed.File(GroupPath, """
            { "formatVersion": 1, "projects": ["App/App.pproj", "Lib/Lib.pproj", "Tools/Tools.pproj"], "activeProject": "App/App.pproj" }
            """);
        Assert.True(await bed.Services.Projects.OpenAsync(GroupPath));
        await bed.Services.ProjectManager.MissingFileChecks;
        var build = bed.Services.Build;
        var answer = "Cancel";
        var shown = new List<(string Title, string[] Projects)>();
        bed.Dialogs.Script(TrustProjectViewModel.DialogName, async dialog =>
        {
            var prompt = Assert.IsType<TrustProjectViewModel>(dialog);
            shown.Add((prompt.Title, [.. prompt.Rows.Where(r => r.Kind == TrustRowKind.Project).Select(r => r.Text)]));
            Assert.Equal(IdeState.Idle, bed.Services.State.Current.State);
            await prompt.Buttons.Single(b => b.Id == answer).PressAsync();
        });

        // Build All, canceled at the question: nothing reaches the engine.
        Assert.Null(await build.BuildGroupAsync(BuildKind.Build));
        Assert.Equal("Project Commands — All", shown[0].Title);
        Assert.Equal([Proj("Lib")], shown[0].Projects);
        Assert.Empty(bed.Engine.Requests);
        Assert.Equal("Build not started: the project's commands were not trusted", bed.Services.Status.Message);

        // Build All without the events; the member's own build asks the same way.
        answer = "BuildWithoutEvents";
        Assert.NotNull(await build.BuildGroupAsync(BuildKind.Rebuild));
        Assert.Equal((GroupPath, EventsPolicy.Skip), (bed.Engine.Requests[^1].ProjectPath, bed.Engine.Requests[^1].Events));
        Assert.NotNull(await build.BuildMemberAsync(Proj("Lib"), BuildKind.Build));
        Assert.Equal((Proj("Lib"), EventsPolicy.Skip), (bed.Engine.Requests[^1].ProjectPath, bed.Engine.Requests[^1].Events));
        Assert.Equal("Project Commands — Lib", shown[^1].Title);
        Assert.Equal(3, shown.Count);

        // A member without commands, and Clean: no question.
        Assert.NotNull(await build.BuildMemberAsync(Proj("Tools"), BuildKind.Build));
        Assert.Equal(EventsPolicy.RequireTrust, bed.Engine.Requests[^1].Events);
        Assert.NotNull(await build.BuildGroupAsync(BuildKind.Clean));
        Assert.Equal(3, shown.Count);
        Assert.Equal(4, bed.Engine.Requests.Count);
    });

    /// <summary>
    /// IDE-PM-172: every build request of the IDE carries <c>StopOnFirstFailure</c> = not
    /// <c>build.continueAfterFailure</c> — also the request of a single project (its dependencies are projects too); a
    /// change of the setting applies to the next build.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-172")]
    public Task EverySingleProjectRequestCarriesTheSetting() => TestUiThread.RunAsync(async () =>
    {
        var bed = new BuildTestBed();
        await bed.OpenHelloAsync();
        var commands = bed.Services.Commands;
        var ids = (string[])["project.build", "project.rebuild", "project.clean"];

        foreach (var id in ids)
            await commands.ExecuteAsync(id);
        Assert.Equal([true, true, true], bed.Engine.Requests.Select(r => r.StopOnFirstFailure));
        Assert.All(bed.Engine.Requests, r => Assert.Equal(BuildTestBed.ProjectPath, r.ProjectPath));

        bed.Services.Settings.Set(SettingsCatalog.BuildContinueAfterFailure, true);
        foreach (var id in ids)
            await commands.ExecuteAsync(id);
        Assert.Equal([false, false, false], bed.Engine.Requests.Skip(3).Select(r => r.StopOnFirstFailure));

        // The build before a run is the same request.
        bed.Services.Settings.Set(SettingsCatalog.BuildContinueAfterFailure, false);
        Assert.NotNull(await bed.Services.Build.BuildAsync(BuildKind.Build, continueToRun: true));
        Assert.True(bed.Engine.Requests[^1].StopOnFirstFailure);
        Assert.Equal(7, bed.Engine.Requests.Count);
    });
}
