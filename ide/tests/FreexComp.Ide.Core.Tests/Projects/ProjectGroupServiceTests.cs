using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;
using F = FreexComp.Ide.Core.Tests.Projects.ProjectGroupFixture;

namespace FreexComp.Ide.Core.Tests.Projects;

/// <summary><c>IProjectGroupService</c>: the model and activating a project (project-groups.md §2, §3; task I-274).</summary>
[Trait("Phase", "8")]
public sealed class ProjectGroupServiceTests
{
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-153")]
    public Task OpenProject_IsAnImplicitGroupOfOne_NothingWritten() => TestUiThread.RunAsync(async () =>
    {
        var f = new F();
        IEnumerable<string> ProjectFiles() => f.Files.AllFiles.Where(p => p.StartsWith(F.Root, StringComparison.Ordinal)
            && !p.Contains(@"\.freexcomp\", StringComparison.Ordinal)); // the per-user state folder is no project content
        var before = ProjectFiles().ToDictionary(p => p, f.Text);
        var changes = 0;
        f.Groups.Changed += (_, _) => changes++;

        Assert.True(await f.Projects.OpenAsync(F.Proj("App")));

        var member = Assert.Single(f.Groups.Members);
        Assert.Equal(F.Proj("App"), member.FilePath);
        Assert.Same(member, f.Groups.ActiveMember);
        Assert.Same(f.Projects.Definition, member.Definition);
        Assert.Null(f.Groups.FilePath); // an implicit group
        Assert.True(f.Groups.IsOpen);
        Assert.Equal(1, changes);

        // Nothing is written and nothing looks different from a single project.
        Assert.Equal(before, ProjectFiles().ToDictionary(p => p, f.Text));
        Assert.DoesNotContain(f.Files.AllFiles, p => p.EndsWith(".pgroup", StringComparison.OrdinalIgnoreCase));
        var root = Assert.Single(f.Pm.Roots);
        Assert.Equal((ProjectManagerViewModel.RootKey, "App", false), (root.Key, root.AutomationName, root.IsListed));
        Assert.Equal(F.Proj("App"), Assert.Single(f.Services.RecentItems.Projects).Path);
        Assert.False(f.Services.Commands.CanExecute("project.setActive", F.Proj("App"))); // it is the active one

        // Another project opened alone replaces it: again an implicit group of one.
        Assert.True(await f.Projects.OpenAsync(F.Proj("Lib")));
        Assert.Equal(["Lib"], f.MemberNames());
        Assert.Null(f.Groups.FilePath);
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-151")]
    [Trait("Req", "IDE-PM-152")]
    [Trait("Req", "IDE-PM-154")]
    public Task OpenGroup_ListsMembersAndLoadsTheActiveProject() => TestUiThread.RunAsync(async () =>
    {
        var f = new F();
        f.File(F.Dir("Lib") + @"\.freexcomp\session.json",
            """{ "formatVersion": 1, "project": "Lib.pproj", "documents": [ { "path": "Lib.pas" }, { "path": "LibUnit.pas" } ], "activeDocument": "LibUnit.pas" }""");
        var loose = f.File(@"C:\loose\notes.txt", "notes\n");
        Assert.True(await f.Projects.OpenAsync(F.Proj("Tools"))); // something is open: the group replaces it
        await f.OpenDocumentAsync(loose);

        await f.OpenGroupAsync(active: "Lib/Lib.pproj");

        // Three members in the order of the file; exactly one — the active project — is loaded.
        Assert.Equal(F.GroupPath, f.Groups.FilePath);
        Assert.Equal([F.Proj("App"), F.Proj("Lib"), F.Proj("Tools")], f.Groups.Members.Select(m => m.FilePath));
        Assert.Equal(["App", "Lib", "Tools"], f.MemberNames());
        Assert.Equal("Lib", f.Groups.ActiveMember?.Name);
        Assert.Equal(F.Proj("Lib"), f.Projects.Project?.FilePath);
        Assert.Same(f.Projects.Definition, f.Groups.ActiveMember!.Definition);

        // A listed member is read through the serializer: name, active configuration, platform, file list.
        var app = f.Groups.Members[0];
        Assert.True(app.IsAvailable);
        Assert.Null(app.Reason);
        Assert.Equal(("App", "Debug", "win64"), (app.Name, app.Configuration, app.Platform));
        Assert.Equal(["App.pas", "AppUnit.pas"], app.Files.Select(file => file.Path));

        // The session of the active project is restored; no document of a listed member is opened; the document that
        // belongs to no project stays.
        Assert.Equal([loose, F.Dir("Lib") + @"\Lib.pas", F.Unit("Lib")], f.OpenPaths());
        Assert.Equal(F.Unit("Lib"), f.Services.Documents.ActiveDocument?.FilePath);
        Assert.False(f.Projects.IsMember(F.Unit("App")));

        // IDE-PM-157: the recent entry is the group file, not the member project.
        Assert.Equal(F.GroupPath, f.Services.RecentItems.Projects[0].Path);
        Assert.DoesNotContain(f.Services.RecentItems.Projects, p => p.Path == F.Proj("Lib"));
        Assert.Equal(F.GroupPath, f.Services.RecentItems.LastProject?.Path);

        // Opening the same group again changes nothing.
        var changes = 0;
        f.Groups.Changed += (_, _) => changes++;
        Assert.True(await f.Projects.OpenAsync(F.GroupPath));
        Assert.Equal(0, changes);

        // IDE-PM-152: a listed member changed on disk is read again (window activation, group operations, builds).
        f.File(F.Proj("Tools"), f.Text(F.Proj("Tools")).Replace("\"name\": \"Tools\"", "\"name\": \"Tools2\"", StringComparison.Ordinal));
        f.Groups.Refresh();
        Assert.Equal(1, changes);
        Assert.Equal(["App", "Lib", "Tools2"], f.MemberNames());
        f.Groups.Refresh();
        Assert.Equal(1, changes); // nothing differs
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-154")]
    public Task OpenGroup_ActiveProjectUnavailable_FirstAvailableIsUsed_FileNotRewritten() => TestUiThread.RunAsync(async () =>
    {
        var f = new F();
        f.WriteGroup("Gone/Gone.pproj", "Gone/Gone.pproj", "Lib/Lib.pproj", "Tools/Tools.pproj");
        var text = f.Text(F.GroupPath);

        Assert.True(await f.Projects.OpenAsync(F.GroupPath));

        Assert.Equal(F.Proj("Lib"), f.Projects.Project?.FilePath); // the first available member
        Assert.Equal("Lib", f.Groups.ActiveMember?.Name);
        Assert.Equal(text, f.Text(F.GroupPath)); // in memory only
        Assert.DoesNotContain(f.Recording.Calls, c => c.Contains("All.pgroup", StringComparison.Ordinal) && !c.StartsWith("FileExists", StringComparison.Ordinal)
            && !c.StartsWith("ReadAllBytes", StringComparison.Ordinal) && !c.StartsWith("GetLastWriteTimeUtc", StringComparison.Ordinal));

        // A group without activeProject, and one whose activeProject is not a member: the first available member too.
        f.File(F.Root + @"\Two.pgroup", """{ "formatVersion": 1, "projects": ["Tools/Tools.pproj", "App/App.pproj"] }""");
        Assert.True(await f.Projects.OpenAsync(F.Root + @"\Two.pgroup"));
        Assert.Equal("Tools", f.Groups.ActiveMember?.Name);

        // No available member at all: the group is open without an active project.
        f.File(F.Root + @"\None.pgroup", """{ "formatVersion": 1, "projects": ["Gone/Gone.pproj"], "activeProject": "Gone/Gone.pproj" }""");
        Assert.True(await f.Projects.OpenAsync(F.Root + @"\None.pgroup"));
        Assert.Null(f.Projects.Project);
        Assert.Null(f.Groups.ActiveMember);
        Assert.Equal(F.Root + @"\None.pgroup", f.Groups.FilePath);
        Assert.Equal(["Gone"], f.MemberNames());
        Assert.False(f.Services.Commands.CanExecute("project.build")); // as without a project
        Assert.False(f.Pm.IsEmptyGroup); // it has a row

        // A group that lists nothing shows Msg.Group.Empty in the Project Manager.
        f.File(F.Root + @"\Empty.pgroup", """{ "formatVersion": 1, "projects": [] }""");
        Assert.True(await f.Projects.OpenAsync(F.Root + @"\Empty.pgroup"));
        Assert.True(f.Pm.IsEmptyGroup);
        Assert.False(f.Pm.HasProject);
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-154")]
    public Task OpenGroup_InvalidOrNewerFile_OpenFailedWithParserText() => TestUiThread.RunAsync(async () =>
    {
        var f = new F();
        Assert.True(await f.Projects.OpenAsync(F.Proj("App")));
        var invalid = f.File(F.Root + @"\Invalid.pgroup", "{}");
        var newer = f.File(F.Root + @"\Newer.pgroup", """{ "formatVersion": 99, "projects": [] }""");
        var broken = f.File(F.Root + @"\Broken.pgroup", "{ \"formatVersion\": 1,\n  \"projects\": [ }");
        var missing = F.Root + @"\Missing.pgroup";
        f.Dialogs.Answer("Msg.Project.OpenFailed", "OK");

        foreach (var path in new[] { invalid, newer, broken, missing })
            Assert.False(await f.Projects.OpenAsync(path));

        // Every failure is Msg.Project.OpenFailed with the path and the parser's text (E9002, E9001, E9002, E9003).
        Assert.Equal(["Msg.Project.OpenFailed", "Msg.Project.OpenFailed", "Msg.Project.OpenFailed", "Msg.Project.OpenFailed"],
            f.Dialogs.Requests.Select(r => r.MessageId));
        Assert.Equal([invalid, newer, broken, missing], f.Dialogs.Requests.Select(r => r.Args[0]));
        Assert.StartsWith("Invalid.pgroup(1,", f.Dialogs.Requests[0].Args[1], StringComparison.Ordinal);
        Assert.Contains("missing required key", f.Dialogs.Requests[0].Args[1], StringComparison.Ordinal);
        Assert.Contains("99", f.Dialogs.Requests[1].Args[1], StringComparison.Ordinal);
        Assert.Contains("newer", f.Dialogs.Requests[1].Args[1], StringComparison.Ordinal);
        Assert.StartsWith("Broken.pgroup(2,", f.Dialogs.Requests[2].Args[1], StringComparison.Ordinal);
        Assert.Contains("not found", f.Dialogs.Requests[3].Args[1], StringComparison.OrdinalIgnoreCase);

        // Nothing changed: the project that was open still is.
        Assert.Equal(F.Proj("App"), f.Projects.Project?.FilePath);
        Assert.Equal(["App"], f.MemberNames());
        Assert.Null(f.Groups.FilePath);

        // Open as Text opens the group file in the editor.
        f.Dialogs.Answer("Msg.Project.OpenFailed", "OpenAsText");
        Assert.False(await f.Projects.OpenAsync(invalid));
        Assert.Equal(invalid, f.Services.Documents.ActiveDocument?.FilePath);
        Assert.Equal(F.Proj("App"), f.Projects.Project?.FilePath);
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-152")]
    public Task UnavailableMember_KeptWithReason_NotActivatable() => TestUiThread.RunAsync(async () =>
    {
        var f = new F();
        f.File(F.Proj("Broken"), "{ \"formatVersion\": 1, \"name\": ");
        f.File(F.Proj("NoName"), """{ "formatVersion": 1, "name": "9 lives", "mainSource": "A.pas" }""");
        await f.OpenGroupAsync("App/App.pproj", "App/App.pproj", "Gone/Gone.pproj", "Broken/Broken.pproj", "NoName/NoName.pproj", "Lib/Lib.pproj");

        // Missing, unreadable and refused files stay in the list, each with the reason.
        Assert.Equal(["App", "Gone", "Broken", "NoName", "Lib"], f.MemberNames()); // the file name without extension
        Assert.Equal([true, false, false, false, true], f.Groups.Members.Select(m => m.IsAvailable));
        Assert.Contains("not found", f.Groups.Members[1].Reason, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(f.Groups.Members[2].Reason));
        Assert.False(string.IsNullOrWhiteSpace(f.Groups.Members[3].Reason));
        Assert.All(f.Groups.Members.Where(m => !m.IsAvailable), m =>
        {
            Assert.Null(m.Definition);
            Assert.Null(m.Configuration);
            Assert.Empty(m.Files);
        });

        // Not activatable: by the service, by the command, from the row.
        foreach (var name in new[] { "Gone", "Broken", "NoName" })
        {
            Assert.False(f.Groups.CanSetActive(F.Proj(name)));
            Assert.False(f.Services.Commands.CanExecute("project.setActive", F.Proj(name)));
            Assert.False(await f.Groups.SetActiveAsync(F.Proj(name)));
        }

        Assert.False(f.Groups.CanSetActive(@"C:\elsewhere\Other.pproj")); // not a member
        Assert.False(f.Groups.CanSetActive(null));
        f.Pm.Select(f.Pm.Find("$project:Gone")!);
        await f.Pm.OpenSelectionAsync();
        Assert.Equal("App", f.Groups.ActiveMember?.Name);
        Assert.Empty(f.Dialogs.Requests);

        // The file appears: the member is available after the next re-read.
        f.AddProject("Gone");
        f.Groups.Refresh();
        Assert.True(f.Groups.Members[1].IsAvailable);
        Assert.True(f.Groups.CanSetActive(F.Proj("Gone")));
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-160")]
    [Trait("Req", "IDE-PM-161")]
    [Trait("Req", "IDE-PM-162")]
    [Trait("Req", "IDE-PM-155")]
    public Task SetActive_SwitchesTheLoadedProject_WritesGroupFile_RestoresItsSession() => TestUiThread.RunAsync(async () =>
    {
        var f = new F();
        await f.OpenGroupAsync();
        var loose = f.File(@"C:\loose\notes.txt", "notes\n");
        await f.OpenDocumentAsync(F.Unit("App"));
        await f.OpenDocumentAsync(loose);
        var listedFile = await f.OpenDocumentAsync(F.Unit("Tools")); // a file of a listed member: belongs to neither project
        var changes = 0;
        f.Groups.Changed += (_, _) => changes++;
        var title = new MainWindowViewModel(f.Services);
        Assert.StartsWith("App – ", title.Title, StringComparison.Ordinal);

        Assert.True(f.Services.Commands.CanExecute("project.setActive", F.Proj("Lib")));
        await f.Services.Commands.ExecuteAsync("project.setActive", F.Proj("Lib"));

        // The loaded project is Lib; App's documents are closed, the others stay.
        Assert.Equal(F.Proj("Lib"), f.Projects.Project?.FilePath);
        Assert.Equal("Lib", f.Groups.ActiveMember?.Name);
        Assert.Equal(["App", "Lib", "Tools"], f.MemberNames()); // the group stays open, the order too
        Assert.Equal([loose, F.Unit("Tools"), F.Dir("Lib") + @"\Lib.pas"], f.OpenPaths());
        Assert.Same(listedFile, f.Services.Documents.Documents[1]);
        Assert.True(changes > 0);
        Assert.StartsWith("Lib – ", title.Title, StringComparison.Ordinal); // <active project> – FreexComp

        // IDE-PM-155: the active member is written at once; unknown keys and the list survive.
        var text = f.Text(F.GroupPath);
        Assert.Contains("\"activeProject\": \"Lib/Lib.pproj\"", text, StringComparison.Ordinal);
        Assert.Contains("\"x-note\": \"kept\"", text, StringComparison.Ordinal);
        Assert.Contains("\"App/App.pproj\"", text, StringComparison.Ordinal);
        Assert.EndsWith("\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", text, StringComparison.Ordinal);

        // The outgoing project's session was written; activating it again restores its documents.
        Assert.True(f.Files.FileExists(F.Dir("App") + @"\.freexcomp\session.json"));
        Assert.True(await f.Groups.SetActiveAsync(F.Proj("App")));
        Assert.Equal(F.Proj("App"), f.Projects.Project?.FilePath);
        Assert.Contains(F.Unit("App"), f.OpenPaths());
        Assert.DoesNotContain(F.Dir("Lib") + @"\Lib.pas", f.OpenPaths());
        Assert.Contains("\"activeProject\": \"App/App.pproj\"", f.Text(F.GroupPath), StringComparison.Ordinal);

        // IDE-PM-157: the recent entry stays the group file.
        Assert.Equal([F.GroupPath], f.Services.RecentItems.Projects.Select(p => p.Path));
        Assert.Empty(f.Dialogs.Requests);
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-161")]
    public Task SetActive_CanceledAtTheUnsavedPrompt_ChangesNothing() => TestUiThread.RunAsync(async () =>
    {
        var f = new F();
        await f.OpenGroupAsync();
        var unit = await f.OpenDocumentAsync(F.Unit("App"));
        F.Type(unit, "{ edited }");
        var group = f.Text(F.GroupPath);
        f.Dialogs.Enqueue("Msg.File.SaveChangesOne", "Cancel");

        Assert.False(await f.Groups.SetActiveAsync(F.Proj("Lib")));

        Assert.Equal("Msg.File.SaveChangesOne", Assert.Single(f.Dialogs.Requests).MessageId);
        Assert.Equal(F.Proj("App"), f.Projects.Project?.FilePath);
        Assert.Equal("App", f.Groups.ActiveMember?.Name);
        Assert.True(unit.IsModified);
        Assert.Contains(unit, f.Services.Documents.Documents);
        Assert.Equal(group, f.Text(F.GroupPath));
        Assert.Equal(F.GroupPath, f.Groups.FilePath);

        // Don't Save lets the switch go on: one prompt over the outgoing project's documents.
        f.Dialogs.Enqueue("Msg.File.SaveChangesOne", "DontSave");
        Assert.True(await f.Groups.SetActiveAsync(F.Proj("Lib")));
        Assert.Equal(2, f.Dialogs.Requests.Count);
        Assert.Equal("Lib", f.Groups.ActiveMember?.Name);
        Assert.DoesNotContain(unit, f.Services.Documents.Documents);
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-160")]
    public Task SetActive_DisabledWhileBuildingOrDebugging() => TestUiThread.RunAsync(async () =>
    {
        var f = new F();
        await f.OpenGroupAsync();
        var commands = f.Services.Commands;
        var state = f.Services.State;
        var lib = F.Proj("Lib");
        IdeStateInfo In(IdeState s) => new(s, s == IdeState.Building ? BuildKind.Build : null, false, null, null);

        Assert.True(commands.CanExecute("project.setActive", lib));

        // The target without an argument: the selected root row of the focused Project Manager.
        Assert.False(commands.CanExecute("project.setActive"));
        f.Services.FocusTracker.Update(F.ProjectManagerFocus);
        f.Pm.Select(f.Pm.Find("$project:Lib")!);
        Assert.True(commands.CanExecute("project.setActive"));
        f.Pm.Select(f.Pm.Find("$project")!);
        Assert.False(commands.CanExecute("project.setActive")); // the active root
        f.Pm.Select(f.Pm.Find("$project:Lib")!);

        Assert.True(state.TryTransition(In(IdeState.Building)));
        Assert.False(commands.CanExecute("project.setActive", lib));
        Assert.False(commands.CanExecute("project.setActive"));
        await commands.ExecuteAsync("project.setActive", lib); // a no-op
        Assert.Equal("App", f.Groups.ActiveMember?.Name);

        Assert.True(state.TryTransition(In(IdeState.DebugRunning)));
        Assert.False(commands.CanExecute("project.setActive", lib));
        Assert.True(state.TryTransition(In(IdeState.Paused)));
        Assert.False(commands.CanExecute("project.setActive", lib));
        Assert.True(state.TryTransition(In(IdeState.Idle)));
        Assert.True(state.TryTransition(In(IdeState.Building)));
        Assert.True(state.TryTransition(In(IdeState.Running)));
        Assert.False(commands.CanExecute("project.setActive", lib));
        Assert.True(state.TryTransition(In(IdeState.Idle)));

        Assert.True(commands.CanExecute("project.setActive"));
        await commands.ExecuteAsync("project.setActive");
        Assert.Equal("Lib", f.Groups.ActiveMember?.Name);
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-155")]
    public Task GroupWriteFails_ChangeIsUndone_MessageShown() => TestUiThread.RunAsync(async () =>
    {
        var f = new F();
        await f.OpenGroupAsync();
        await f.OpenDocumentAsync(F.Unit("App"));
        var group = f.Text(F.GroupPath);
        f.Recording.FailWith = call => call.Contains("All.pgroup", StringComparison.Ordinal)
            && (call.StartsWith("Write", StringComparison.Ordinal) || call.StartsWith("Replace", StringComparison.Ordinal)
                || call.StartsWith("Move", StringComparison.Ordinal) || call.StartsWith("Overwrite", StringComparison.Ordinal))
            ? new IOException("The disk is full.")
            : null;
        f.Dialogs.Answer("Msg.Group.SaveFailed", "OK");

        Assert.False(await f.Groups.SetActiveAsync(F.Proj("Lib")));

        // Msg.Group.SaveFailed with the file name and the system's text; the change is undone in memory.
        var request = Assert.Single(f.Dialogs.Requests);
        Assert.Equal(("Msg.Group.SaveFailed", "All.pgroup", "The disk is full."), (request.MessageId, request.Args[0], request.Args[1]));
        Assert.Equal("App", f.Groups.ActiveMember?.Name);
        Assert.Equal(F.Proj("App"), f.Projects.Project?.FilePath);
        Assert.Contains(F.Unit("App"), f.OpenPaths()); // its session came back with it
        Assert.Equal(group, f.Text(F.GroupPath));
        Assert.DoesNotContain(f.Files.AllFiles, p => p.Contains(".~tmp", StringComparison.Ordinal));
        Assert.Equal([ProjectManagerViewModel.RootKey, "$project:Lib", "$project:Tools"], f.RootKeys());

        // Once the file can be written the same change goes through.
        f.Recording.FailWith = null;
        Assert.True(await f.Groups.SetActiveAsync(F.Proj("Lib")));
        Assert.Contains("\"activeProject\": \"Lib/Lib.pproj\"", f.Text(F.GroupPath), StringComparison.Ordinal);
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-158")]
    public Task CloseProject_ClosesTheGroup() => TestUiThread.RunAsync(async () =>
    {
        var f = new F();
        await f.OpenGroupAsync();
        var loose = await f.OpenDocumentAsync(f.File(@"C:\loose\notes.txt", "notes\n"));
        var unit = await f.OpenDocumentAsync(F.Unit("App"));
        F.Type(unit, "{ edited }");
        var group = f.Text(F.GroupPath);
        var commands = f.Services.Commands;

        // The unsaved prompt of the active project (IDE-PM-014) can cancel the whole close.
        f.Dialogs.Enqueue("Msg.File.SaveChangesOne", "Cancel");
        await commands.ExecuteAsync("file.closeProject");
        Assert.Equal(3, f.Groups.Members.Count);
        Assert.Equal(F.GroupPath, f.Groups.FilePath);

        f.Dialogs.Enqueue("Msg.File.SaveChangesOne", "DontSave");
        await commands.ExecuteAsync("file.closeProject");

        // The active project is closed, then the list: nothing is open, nothing was written.
        Assert.Null(f.Projects.Project);
        Assert.Empty(f.Groups.Members);
        Assert.Null(f.Groups.ActiveMember);
        Assert.Null(f.Groups.FilePath);
        Assert.False(f.Groups.IsOpen);
        Assert.Empty(f.Pm.Roots);
        Assert.False(f.Pm.IsEmptyGroup);
        Assert.Equal([loose], f.Services.Documents.Documents);
        Assert.Equal(group, f.Text(F.GroupPath));
        Assert.False(commands.CanExecute("file.closeProject"));
        Assert.Null(f.Services.RecentItems.LastProject); // closed by the user: not reopened at the next start-up
        Assert.Equal(F.GroupPath, f.Services.RecentItems.Projects[0].Path);

        // A group without an available member has no project to close, yet it is closed the same way.
        f.File(F.Root + @"\None.pgroup", """{ "formatVersion": 1, "projects": ["Gone/Gone.pproj"] }""");
        Assert.True(await f.Projects.OpenAsync(F.Root + @"\None.pgroup"));
        Assert.Null(f.Projects.Project);
        Assert.True(commands.CanExecute("file.closeProject"));
        await commands.ExecuteAsync("file.closeProject");
        Assert.False(f.Groups.IsOpen);
        Assert.Empty(f.Pm.Roots);
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-163")]
    public Task ListedMemberFile_OpensOutsideTheProject_FileCommandsDisabled() => TestUiThread.RunAsync(async () =>
    {
        var f = new F();
        await f.OpenGroupAsync();
        var commands = f.Services.Commands;
        f.Services.FocusTracker.Update(F.ProjectManagerFocus);
        string[] fileCommands =
        [
            "project.addNewUnit", "project.addNewForm", "project.addNewFile", "project.addExistingFile", "project.newFolder",
            "project.removeFile", "project.renameFile", "project.deleteFile", "project.moveFile",
        ];

        // The control: on a file row of the active project the commands are available.
        f.Pm.Select(f.Pm.Find("AppUnit.pas")!);
        Assert.All(fileCommands.Except(["project.newFolder"]), id => Assert.True(commands.CanExecute(id), id));
        f.Pm.Select(f.Pm.Find("$project")!);
        Assert.True(commands.CanExecute("project.newFolder"));

        // The row of a listed member's file opens the file as a document outside the open project.
        var row = f.Pm.Find("Lib:LibUnit.pas")!;
        Assert.True(row.IsListed);
        Assert.Equal(F.Unit("Lib"), row.FullPath);
        f.Pm.Select(row);
        await f.Pm.OpenSelectionAsync();
        var document = Assert.IsAssignableFrom<TextDocumentViewModel>(f.Services.Documents.ActiveDocument);
        Assert.Equal(F.Unit("Lib"), document.FilePath);
        Assert.False(f.Projects.IsMember(document.FilePath));
        Assert.Equal(F.Proj("App"), f.Projects.Project?.FilePath); // nothing was activated
        Assert.False(row.IsModified);
        F.Type(document, "{ edited }");
        Assert.False(row.IsModified); // rows of listed members carry no document marker
        Assert.False(row.IsActiveDocument);

        // A unit with a form opens as code, whatever designer.defaultView says.
        Assert.Equal("design", f.Services.Settings.Get(FreexComp.Ide.Core.Settings.SettingsCatalog.DesignerDefaultView));
        f.Pm.Select(f.Pm.Find("Lib:LibForm.pas")!);
        await f.Pm.OpenSelectionAsync();
        var formUnit = f.Services.Documents.ActiveDocument;
        Assert.Equal(F.Dir("Lib") + @"\LibForm.pas", formUnit?.FilePath);
        Assert.True(formUnit is not FormDocumentViewModel form || form.ActiveView == DocumentView.Code);

        // File-management commands are disabled on rows of listed members: file, form child, folder-less root.
        foreach (var key in new[] { "Lib:LibUnit.pas", "Lib:LibForm.pas", "Lib:LibForm.form", "$project:Lib" })
        {
            f.Pm.Select(f.Pm.Find(key)!);
            Assert.All(fileCommands, id => Assert.False(commands.CanExecute(id), key + " " + id));
            Assert.Empty(f.Pm.SelectedFilePaths);
            Assert.Null(f.Pm.SelectedFolderPath);
        }

        Assert.False(f.Pm.CanStartDrag(f.Pm.Find("Lib:LibUnit.pas")!));
        Assert.Null(f.Pm.DropTargetFor(f.Pm.Find("$project:Lib")));
        Assert.DoesNotContain(f.Pm.ContextMenu(), i => i.CommandId is not null && fileCommands.Contains(i.CommandId));

        // Outside the Project Manager the commands follow the open project again.
        f.Services.FocusTracker.Update(F.EditorFocus);
        Assert.True(commands.CanExecute("project.addNewUnit"));
    });
}
