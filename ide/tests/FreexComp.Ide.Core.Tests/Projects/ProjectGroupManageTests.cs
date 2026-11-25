using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.TestSupport;
using F = FreexComp.Ide.Core.Tests.Projects.ProjectGroupFixture;

namespace FreexComp.Ide.Core.Tests.Projects;

/// <summary>Managing a project group (project-groups.md §4, IDE-PM-156; task I-275).</summary>
[Trait("Phase", "8")]
public sealed class ProjectGroupManageTests
{
    private static readonly IdeStateInfo Building = new(IdeState.Building, BuildKind.Build, false, null, null);

    /// <summary>The <c>projects</c> array of a group file as written (canonical text: one entry per line).</summary>
    private static string[] ProjectsIn(string groupText)
    {
        using var json = System.Text.Json.JsonDocument.Parse(groupText);
        return [.. json.RootElement.GetProperty("projects").EnumerateArray().Select(e => e.GetString()!)];
    }

    private static string ActiveIn(string groupText)
    {
        using var json = System.Text.Json.JsonDocument.Parse(groupText);
        return json.RootElement.GetProperty("activeProject").GetString()!;
    }

    /// <summary>Card F09-009 acceptance 1: add a second project → the <c>.pgroup</c> contains both relative paths.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-164")]
    [Trait("Req", "IDE-PM-155")]
    public Task AddExistingProject_ToSavedGroup_FileHasBothRelativePaths() => TestUiThread.RunAsync(async () =>
    {
        var f = new F();
        await f.OpenGroupAsync("App/App.pproj", "App/App.pproj");
        var commands = f.Services.Commands;
        var deep = f.AddProject("Deep", directory: F.Root + @"\more\Deep");

        Assert.True(commands.CanExecute("project.addExistingProject"));
        f.Pickers.NextOpen(F.Proj("Lib"));
        await commands.ExecuteAsync("project.addExistingProject");

        // The picker: *.pproj, several files allowed, starting in the active project's directory.
        var picker = Assert.Single(f.Pickers.Requests);
        Assert.Equal(("addExistingProject", "Add Existing Project", true), (picker.PromptId, picker.Title, picker.AllowMultiple));
        Assert.Equal(["FreexComp projects (*.pproj)", "All files (*.*)"], picker.Filters.Select(filter => filter.Label));
        Assert.Equal(F.Dir("App"), picker.InitialFolder);

        // Both paths relative to the group file, with '/'; written at once; the active project does not change.
        var text = f.Text(F.GroupPath);
        Assert.Contains("\"App/App.pproj\"", text, StringComparison.Ordinal);
        Assert.Contains("\"Lib/Lib.pproj\"", text, StringComparison.Ordinal);
        Assert.Equal(["App/App.pproj", "Lib/Lib.pproj"], ProjectsIn(text));
        Assert.Equal("App/App.pproj", ActiveIn(text));
        Assert.DoesNotContain("\\", text, StringComparison.Ordinal);
        Assert.Contains("\"x-note\": \"kept\"", text, StringComparison.Ordinal);
        Assert.Equal(["App", "Lib"], f.MemberNames());
        Assert.Equal("App", f.Groups.ActiveMember?.Name);
        Assert.Equal(F.Proj("App"), f.Projects.Project?.FilePath);
        Assert.Equal(["$project", "$project:Lib"], f.RootKeys());
        Assert.False(f.Groups.IsUnsaved);

        // Several files are appended in the picker's order.
        f.Pickers.NextOpen(deep, F.Proj("Tools"));
        await commands.ExecuteAsync("project.addExistingProject");
        Assert.Equal(["App/App.pproj", "Lib/Lib.pproj", "more/Deep/Deep.pproj", "Tools/Tools.pproj"], ProjectsIn(f.Text(F.GroupPath)));
        Assert.Equal(["App", "Lib", "Deep", "Tools"], f.MemberNames());

        // A canceled picker changes nothing; the command needs something open and the Idle state.
        f.Pickers.NextCancel();
        await commands.ExecuteAsync("project.addExistingProject");
        Assert.Equal(4, f.Groups.Members.Count);
        Assert.True(f.Services.State.TryTransition(Building));
        Assert.False(commands.CanExecute("project.addExistingProject"));
        Assert.False(commands.CanExecute("project.addNewProject"));
        Assert.True(f.Services.State.TryTransition(IdeStateInfo.Idle));
        await commands.ExecuteAsync("file.closeProject");
        Assert.False(commands.CanExecute("project.addExistingProject"));
        Assert.False(commands.CanExecute("project.addNewProject"));
        Assert.False(commands.CanExecute("file.saveGroupAs"));
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-164")]
    public Task AddExistingProject_AlreadyMember_Skipped() => TestUiThread.RunAsync(async () =>
    {
        var f = new F();
        await f.OpenGroupAsync("App/App.pproj", "App/App.pproj", "Lib/Lib.pproj");
        var before = f.Text(F.GroupPath);
        var changes = 0;
        f.Groups.Changed += (_, _) => changes++;

        // Only members (also written with other letter case): nothing is added, nothing is written; the status says why.
        f.Pickers.NextOpen(F.Proj("Lib").ToUpperInvariant(), F.Proj("App"));
        await f.Services.Commands.ExecuteAsync("project.addExistingProject");
        Assert.Equal(["App", "Lib"], f.MemberNames());
        Assert.Equal(before, f.Text(F.GroupPath));
        Assert.Equal(0, changes);
        Assert.Equal("App is already in the group.", f.Services.Status.Message);

        // A member among new files is skipped; the others are appended.
        Assert.Equal(1, await f.Groups.AddExistingProjectsAsync([F.Proj("Lib"), F.Proj("Tools"), F.Proj("Tools")]));
        Assert.Equal(["App", "Lib", "Tools"], f.MemberNames());
        Assert.Equal(["App/App.pproj", "Lib/Lib.pproj", "Tools/Tools.pproj"], ProjectsIn(f.Text(F.GroupPath)));
        Assert.Equal("Tools is already in the group.", f.Services.Status.Message);
        Assert.Empty(f.Dialogs.Requests);
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-169")]
    [Trait("Req", "IDE-PM-156")]
    public Task AddToImplicitGroup_MakesAnUnsavedGroup_AsksOnClose() => TestUiThread.RunAsync(async () =>
    {
        var f = new F();
        var commands = f.Services.Commands;
        bool NoGroupFile() => !f.Files.AllFiles.Any(p => p.EndsWith(".pgroup", StringComparison.OrdinalIgnoreCase));
        async Task OpenAppAndAddLibAsync()
        {
            Assert.True(await f.Projects.OpenAsync(F.Proj("App")));
            Assert.False(f.Groups.IsUnsaved);
            Assert.Equal(1, await f.Groups.AddExistingProjectsAsync([F.Proj("Lib")]));
        }

        // The second member makes the implicit group an unsaved group; nothing is written.
        await OpenAppAndAddLibAsync();
        Assert.True(f.Groups.IsUnsaved);
        Assert.Null(f.Groups.FilePath);
        Assert.Equal(["App", "Lib"], f.MemberNames());
        Assert.Equal(["$project", "$project:Lib"], f.RootKeys());
        Assert.True(NoGroupFile());
        var unit = await f.OpenDocumentAsync(F.Unit("App"));
        F.Type(unit, "{ edited }");

        // Cancel: nothing is closed, and the question came before the unsaved-documents prompt.
        f.Dialogs.Enqueue("Msg.Group.SaveQuestion", "Cancel");
        await commands.ExecuteAsync("file.closeProject");
        Assert.Equal(["Msg.Group.SaveQuestion"], f.Dialogs.Requests.Select(r => r.MessageId));
        Assert.Equal(F.Proj("App"), f.Projects.Project?.FilePath);
        Assert.True(f.Groups.IsUnsaved);

        // The same question on exit (before the documents) and when another project is opened.
        f.Dialogs.Enqueue("Msg.Group.SaveQuestion", "Cancel", "Cancel");
        Assert.False(await f.Services.Application.ExitAsync());
        Assert.False(await f.Projects.OpenAsync(F.Proj("Tools")));
        Assert.Equal(["Msg.Group.SaveQuestion", "Msg.Group.SaveQuestion", "Msg.Group.SaveQuestion"], f.Dialogs.Requests.Select(r => r.MessageId));
        Assert.Equal(F.Proj("App"), f.Projects.Project?.FilePath);

        // Save…: file.saveGroupAs; canceling its picker cancels the close.
        f.Dialogs.Enqueue("Msg.Group.SaveQuestion", "Save");
        f.Pickers.NextCancel();
        await commands.ExecuteAsync("file.closeProject");
        Assert.Equal("saveGroupAs", f.Pickers.Requests[^1].PromptId);
        Assert.True(f.Groups.IsUnsaved);
        Assert.True(NoGroupFile());
        Assert.Equal(4, f.Dialogs.Requests.Count);

        // Save… with a file: the group is written, then the close goes on with the documents of the active project.
        var saved = F.Root + @"\Saved.pgroup";
        f.Dialogs.Enqueue("Msg.Group.SaveQuestion", "Save");
        f.Dialogs.Enqueue("Msg.File.SaveChangesOne", "DontSave");
        f.Pickers.NextSave(saved);
        await commands.ExecuteAsync("file.closeProject");
        Assert.Equal(["App/App.pproj", "Lib/Lib.pproj"], ProjectsIn(f.Text(saved)));
        Assert.Equal("App/App.pproj", ActiveIn(f.Text(saved)));
        Assert.Equal(["Msg.Group.SaveQuestion", "Msg.File.SaveChangesOne"], f.Dialogs.Requests.Skip(4).Select(r => r.MessageId));
        Assert.Null(f.Projects.Project);
        Assert.False(f.Groups.IsOpen);
        f.Files.DeleteFile(saved);

        // Don't Save: the close goes on and nothing is written.
        await OpenAppAndAddLibAsync();
        f.Dialogs.Enqueue("Msg.Group.SaveQuestion", "DontSave");
        Assert.True(await f.Projects.OpenAsync(F.Proj("Tools")));
        Assert.Equal(["Tools"], f.MemberNames());
        Assert.False(f.Groups.IsUnsaved);
        Assert.True(NoGroupFile());

        // A saved group and an implicit group of one are never asked about.
        await commands.ExecuteAsync("file.closeProject");
        await f.OpenGroupAsync();
        await commands.ExecuteAsync("file.closeProject");
        Assert.Equal(7, f.Dialogs.Requests.Count);
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-165")]
    public Task AddNewProject_AppendsAndActivates_CancelKeepsItListed() => TestUiThread.RunAsync(async () =>
    {
        var f = new F();
        await f.OpenGroupAsync();
        var commands = f.Services.Commands;
        var clock = (ManualClock)f.Services.Clock;
        string? name = null;
        f.Dialogs.Script(NewProjectViewModel.DialogName, async dialog =>
        {
            var newProject = (NewProjectViewModel)dialog;
            if (name is null)
            {
                await newProject.PressCancelAsync();
                return;
            }

            // The dialog validates after its delay, off the UI thread: Create is pressed once that check has ended.
            var checkedValues = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            newProject.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(NewProjectViewModel.IsChecking) && !newProject.IsChecking)
                    checkedValues.TrySetResult();
            };
            newProject.ProjectName = name;
            newProject.Location = F.Root;
            clock.Advance(NewProjectViewModel.CheckDelay);
            await checkedValues.Task;
            await newProject.PressDefaultAsync();
        });

        // The created project is appended and activated (the switch of IDE-PM-161).
        Assert.True(commands.CanExecute("project.addNewProject"));
        name = "Fresh";
        await commands.ExecuteAsync("project.addNewProject");
        var fresh = F.Proj("Fresh");
        Assert.True(f.Files.FileExists(fresh));
        Assert.Equal(["App", "Lib", "Tools", "Fresh"], f.MemberNames());
        Assert.Equal("Fresh", f.Groups.ActiveMember?.Name);
        Assert.Equal(fresh, f.Projects.Project?.FilePath);
        Assert.Equal(["App/App.pproj", "Lib/Lib.pproj", "Tools/Tools.pproj", "Fresh/Fresh.pproj"], ProjectsIn(f.Text(F.GroupPath)));
        Assert.Equal("Fresh/Fresh.pproj", ActiveIn(f.Text(F.GroupPath)));
        Assert.Equal(F.Dir("Fresh") + @"\Fresh.pas", f.Services.Documents.ActiveDocument?.FilePath);

        // A canceled activation (the unsaved prompt of the outgoing project) leaves the new project added and listed.
        F.Type(await f.OpenDocumentAsync(F.Dir("Fresh") + @"\Fresh.pas"), "{ edited }");
        f.Dialogs.Enqueue("Msg.File.SaveChangesOne", "Cancel");
        name = "Second";
        await commands.ExecuteAsync("project.addNewProject");
        Assert.True(f.Files.FileExists(F.Proj("Second")));
        Assert.Equal(["App", "Lib", "Tools", "Fresh", "Second"], f.MemberNames());
        Assert.Equal("Fresh", f.Groups.ActiveMember?.Name);
        Assert.Equal(fresh, f.Projects.Project?.FilePath);
        Assert.EndsWith("Second/Second.pproj", ProjectsIn(f.Text(F.GroupPath))[^1], StringComparison.Ordinal);
        Assert.Equal("Fresh/Fresh.pproj", ActiveIn(f.Text(F.GroupPath)));
        Assert.True(f.Groups.Members[^1].IsAvailable);

        // A canceled dialog adds nothing.
        name = null;
        await commands.ExecuteAsync("project.addNewProject");
        Assert.Equal(5, f.Groups.Members.Count);
        Assert.Equal(3, f.Dialogs.Dialogs.Count);
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-166")]
    public Task RemoveFromGroup_ListedAndActive_NoFileDeleted_NeighbourActivated() => TestUiThread.RunAsync(async () =>
    {
        var f = new F();
        await f.OpenGroupAsync();
        var commands = f.Services.Commands;
        var files = f.Files.AllFiles.Where(p => p.EndsWith(".pproj", StringComparison.Ordinal) || p.EndsWith(".pas", StringComparison.Ordinal))
            .ToDictionary(p => p, f.Text);

        // A listed member: gone from the list and the file; no confirmation; the active project stays.
        Assert.True(commands.CanExecute("project.removeFromGroup", F.Proj("Lib")));
        await commands.ExecuteAsync("project.removeFromGroup", F.Proj("Lib"));
        Assert.Equal(["App", "Tools"], f.MemberNames());
        Assert.Equal(["App/App.pproj", "Tools/Tools.pproj"], ProjectsIn(f.Text(F.GroupPath)));
        Assert.Equal("App", f.Groups.ActiveMember?.Name);

        // The active project: closed first — Cancel at its unsaved prompt removes nothing.
        var unit = await f.OpenDocumentAsync(F.Unit("App"));
        F.Type(unit, "{ edited }");
        f.Services.FocusTracker.Update(F.ProjectManagerFocus);
        f.Pm.Select(f.Pm.Find("$project")!);
        Assert.True(commands.CanExecute("project.removeFromGroup")); // the target: the selected root row
        f.Dialogs.Enqueue("Msg.File.SaveChangesOne", "Cancel");
        await commands.ExecuteAsync("project.removeFromGroup");
        Assert.Equal(["App", "Tools"], f.MemberNames());
        Assert.Equal(F.Proj("App"), f.Projects.Project?.FilePath);

        // Then the following available member is activated.
        f.Dialogs.Enqueue("Msg.File.SaveChangesOne", "DontSave");
        await commands.ExecuteAsync("project.removeFromGroup");
        Assert.Equal(["Tools"], f.MemberNames());
        Assert.Equal("Tools", f.Groups.ActiveMember?.Name);
        Assert.Equal(F.Proj("Tools"), f.Projects.Project?.FilePath);
        Assert.Equal(["Tools/Tools.pproj"], ProjectsIn(f.Text(F.GroupPath)));
        Assert.Equal("Tools/Tools.pproj", ActiveIn(f.Text(F.GroupPath)));
        Assert.DoesNotContain(unit, f.Services.Documents.Documents);

        // Without a following one: the preceding one; an unavailable member is passed over.
        Assert.Equal(3, await f.Groups.AddExistingProjectsAsync([F.Proj("Gone"), F.Proj("App"), F.Proj("Lib")]));
        Assert.True(await f.Groups.SetActiveAsync(F.Proj("Lib"))); // Tools, Gone, App, Lib*
        Assert.True(await f.Groups.RemoveAsync(F.Proj("Lib")));
        Assert.Equal("App", f.Groups.ActiveMember?.Name);
        Assert.True(await f.Groups.SetActiveAsync(F.Proj("Tools"))); // Tools*, Gone, App
        Assert.True(await f.Groups.RemoveAsync(F.Proj("Tools")));
        Assert.Equal("App", f.Groups.ActiveMember?.Name); // not Gone
        Assert.Equal(["Gone", "App"], f.MemberNames());

        // The last members of a saved group: an empty group stays open.
        Assert.True(await f.Groups.RemoveAsync(F.Proj("Gone")));
        Assert.True(await f.Groups.RemoveAsync(F.Proj("App")));
        Assert.Empty(f.Groups.Members);
        Assert.Null(f.Projects.Project);
        Assert.Equal(F.GroupPath, f.Groups.FilePath);
        Assert.True(f.Groups.IsOpen);
        Assert.True(f.Pm.IsEmptyGroup);
        Assert.Empty(ProjectsIn(f.Text(F.GroupPath)));
        Assert.Equal(["project.addExistingProject", "project.addNewProject"], f.Pm.EmptyLinks.Select(c => c.Id)); // Msg.Group.Empty
        Assert.False(commands.CanExecute("project.removeFromGroup", F.Proj("App"))); // not a member any more

        // The last member of an implicit group: as file.closeProject.
        Assert.True(await f.Projects.OpenAsync(F.Proj("App")));
        Assert.True(await f.Groups.RemoveAsync(F.Proj("App")));
        Assert.Null(f.Projects.Project);
        Assert.False(f.Groups.IsOpen);

        // No file was touched (dependencies of other projects that name a removed project stay, too).
        Assert.All(files, entry => Assert.Equal(entry.Value, f.Text(entry.Key)));
        Assert.Equal(2, f.Dialogs.Requests.Count);
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-167")]
    public Task SaveGroupAs_RelativePaths_OtherDriveAbsolute_UnknownKeysKept() => TestUiThread.RunAsync(async () =>
    {
        var f = new F();
        await f.OpenGroupAsync(active: "Lib/Lib.pproj");
        var commands = f.Services.Commands;
        var external = f.AddProject("Ext", directory: @"D:\ext");
        Assert.Equal(1, await f.Groups.AddExistingProjectsAsync([external]));
        Assert.EndsWith("D:/ext/Ext.pproj", ProjectsIn(f.Text(F.GroupPath))[^1], StringComparison.Ordinal);
        var old = f.Text(F.GroupPath);
        var target = F.Root + @"\sub\New.pgroup";
        f.Files.CreateDirectory(F.Root + @"\sub");

        Assert.True(commands.CanExecute("file.saveGroupAs"));
        f.Pickers.NextSave(target);
        await commands.ExecuteAsync("file.saveGroupAs");

        // The picker: *.pgroup, next to the first member, "<name of the first member>Group.pgroup".
        var picker = Assert.Single(f.Pickers.Requests);
        Assert.Equal(("saveGroupAs", "Save Project Group As", "AppGroup.pgroup", "pgroup"),
            (picker.PromptId, picker.Title, picker.SuggestedName, picker.DefaultExtension));
        Assert.Equal(["FreexComp project groups (*.pgroup)"], picker.Filters.Select(filter => filter.Label));
        Assert.Equal(F.Dir("App"), picker.InitialFolder);

        // Paths relative to the new file with '/', the other drive absolute, the active project, the unknown key.
        var text = f.Text(target);
        Assert.Equal(["../App/App.pproj", "../Lib/Lib.pproj", "../Tools/Tools.pproj", "D:/ext/Ext.pproj"], ProjectsIn(text));
        Assert.Equal("../Lib/Lib.pproj", ActiveIn(text));
        Assert.Contains("\"formatVersion\": 1", text, StringComparison.Ordinal);
        Assert.Contains("\"x-note\": \"kept\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\\", text, StringComparison.Ordinal);

        // The group is a saved group at the new path; the old file is not changed, later changes go to the new one.
        Assert.Equal(target, f.Groups.FilePath);
        Assert.Equal(old, f.Text(F.GroupPath));
        Assert.Equal(["App", "Lib", "Tools", "Ext"], f.MemberNames());
        Assert.Equal("Lib", f.Groups.ActiveMember?.Name);
        Assert.Equal(target, f.Services.RecentItems.Projects[0].Path);
        Assert.Equal(target, f.Services.RecentItems.LastProject?.Path);
        Assert.True(await f.Groups.SetActiveAsync(external));
        Assert.Equal("D:/ext/Ext.pproj", ActiveIn(f.Text(target)));
        Assert.Equal(old, f.Text(F.GroupPath));

        // The written file opens again with the same members.
        await commands.ExecuteAsync("file.closeProject");
        Assert.True(await f.Projects.OpenAsync(target));
        Assert.Equal([F.Proj("App"), F.Proj("Lib"), F.Proj("Tools"), external], f.Groups.Members.Select(m => m.FilePath));
        Assert.Equal(external, f.Projects.Project?.FilePath);
        await commands.ExecuteAsync("file.closeProject");

        // An implicit group can be saved too: it becomes a saved group; a canceled picker changes nothing.
        Assert.True(await f.Projects.OpenAsync(F.Proj("Tools")));
        Assert.True(commands.CanExecute("file.saveGroupAs"));
        f.Pickers.NextCancel();
        await commands.ExecuteAsync("file.saveGroupAs");
        Assert.Equal("ToolsGroup.pgroup", f.Pickers.Requests[^1].SuggestedName);
        Assert.Null(f.Groups.FilePath);
        var own = F.Dir("Tools") + @"\ToolsGroup.pgroup";
        Assert.True(await f.Groups.SaveAsAsync(own));
        Assert.Equal(own, f.Groups.FilePath);
        Assert.Equal(["Tools.pproj"], ProjectsIn(f.Text(own)));
        Assert.Equal("Tools.pproj", ActiveIn(f.Text(own)));

        // A file that cannot be written: Msg.Group.SaveFailed, the group stays where it was.
        f.Recording.FailWith = call => call.Contains("Denied.pgroup", StringComparison.Ordinal) && !call.StartsWith("FileExists", StringComparison.Ordinal)
            ? new IOException("Access is denied.")
            : null;
        f.Dialogs.Answer("Msg.Group.SaveFailed", "OK");
        Assert.False(await f.Groups.SaveAsAsync(F.Root + @"\Denied.pgroup"));
        var failure = Assert.Single(f.Dialogs.Requests);
        Assert.Equal(("Msg.Group.SaveFailed", "Denied.pgroup", "Access is denied."), (failure.MessageId, failure.Args[0], failure.Args[1]));
        Assert.Equal(own, f.Groups.FilePath);
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-168")]
    public Task MoveProjectUpDown_ChangesOrderInFile_DisabledAtEnds() => TestUiThread.RunAsync(async () =>
    {
        var f = new F();
        await f.OpenGroupAsync();
        var commands = f.Services.Commands;
        f.Services.FocusTracker.Update(F.ProjectManagerFocus);

        // The first root (the active project here): up is disabled, down moves it.
        f.Pm.Select(f.Pm.Find("$project")!);
        Assert.False(commands.CanExecute("project.moveProjectUp"));
        Assert.True(commands.CanExecute("project.moveProjectDown"));
        await commands.ExecuteAsync("project.moveProjectDown");
        Assert.Equal(["Lib", "App", "Tools"], f.MemberNames());
        Assert.Equal(["Lib/Lib.pproj", "App/App.pproj", "Tools/Tools.pproj"], ProjectsIn(f.Text(F.GroupPath)));
        Assert.Equal("App/App.pproj", ActiveIn(f.Text(F.GroupPath))); // the active project does not change
        Assert.Equal("App", f.Groups.ActiveMember?.Name);
        Assert.Equal(["$project:Lib", "$project", "$project:Tools"], f.RootKeys());
        Assert.Equal(["$project"], f.Pm.Selection.Select(n => n.Key)); // the moved row stays selected

        // In the middle both are enabled; at the end down is disabled.
        Assert.True(commands.CanExecute("project.moveProjectUp"));
        Assert.True(commands.CanExecute("project.moveProjectDown"));
        await commands.ExecuteAsync("project.moveProjectDown");
        Assert.Equal(["Lib", "Tools", "App"], f.MemberNames());
        Assert.False(commands.CanExecute("project.moveProjectDown"));
        Assert.True(commands.CanExecute("project.moveProjectUp"));

        // A listed root, also by its path; an unknown path and a file row are no target.
        f.Pm.Select(f.Pm.Find("$project:Tools")!);
        await commands.ExecuteAsync("project.moveProjectUp");
        Assert.Equal(["Tools/Tools.pproj", "Lib/Lib.pproj", "App/App.pproj"], ProjectsIn(f.Text(F.GroupPath)));
        Assert.False(commands.CanExecute("project.moveProjectUp"));
        Assert.True(commands.CanExecute("project.moveProjectUp", F.Proj("App")));
        Assert.False(commands.CanExecute("project.moveProjectUp", @"C:\elsewhere\Other.pproj"));
        f.Pm.Select(f.Pm.Find("AppUnit.pas")!);
        Assert.False(commands.CanExecute("project.moveProjectUp"));
        Assert.False(commands.CanExecute("project.moveProjectDown"));
        Assert.False(commands.CanExecute("project.removeFromGroup"));

        // By position (dragging a root row): to the front; its own position is no move.
        Assert.True(await f.Groups.MoveToAsync(F.Proj("App"), 0));
        Assert.Equal(["App", "Tools", "Lib"], f.MemberNames());
        Assert.False(await f.Groups.MoveToAsync(F.Proj("App"), 0));
        Assert.False(await f.Groups.MoveToAsync(F.Proj("App"), 3));

        // Idle only; nothing to move in a group of one.
        f.Pm.Select(f.Pm.Find("$project")!);
        Assert.True(f.Services.State.TryTransition(Building));
        Assert.False(commands.CanExecute("project.moveProjectDown"));
        Assert.False(commands.CanExecute("project.removeFromGroup"));
        Assert.True(f.Services.State.TryTransition(IdeStateInfo.Idle));
        Assert.True(await f.Projects.OpenAsync(F.Proj("Lib")));
        f.Pm.Select(f.Pm.Find("$project")!);
        Assert.False(commands.CanExecute("project.moveProjectUp"));
        Assert.False(commands.CanExecute("project.moveProjectDown"));
        Assert.Empty(f.Dialogs.Requests);
    });
}
