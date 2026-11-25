using System.Text.Json;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Tests.Projects;
using FreexComp.Ide.TestSupport;
using F = FreexComp.Ide.Core.Tests.Projects.ProjectGroupFixture;

namespace FreexComp.Ide.Core.Tests.Dialogs;

/// <summary>
/// The dialog <c>ProjectDependencies</c> and <c>project.dependencies</c> (project-manager.md IDE-PM-175…180) over the
/// group of <see cref="ProjectGroupFixture"/> (App, Lib, Tools below <c>C:\g</c>).
/// </summary>
[Trait("Phase", "8")]
public sealed class ProjectDependenciesDialogTests
{
    private static readonly IdeStateInfo Building = new(IdeState.Building, BuildKind.Build, false, null, null);

    /// <summary>Writes <c>&lt;name&gt;.pproj</c> of the fixture again with <c>dependencies</c> and an unknown key.</summary>
    private static void WithDependencies(F f, string name, params string[] dependencies) => f.File(F.Proj(name), $$"""
        { "formatVersion": 1, "name": "{{name}}", "mainSource": "{{name}}.pas",
          "files": [ { "path": "{{name}}.pas" }, { "path": "{{name}}Unit.pas" } ],
          "dependencies": [{{string.Join(", ", dependencies.Select(d => $"\"{d}\""))}}],
          "x-keep": { "note": "kept" } }
        """);

    private static string[] Dependencies(string projectText)
    {
        using var json = JsonDocument.Parse(projectText);
        return json.RootElement.TryGetProperty("dependencies", out var list) ? [.. list.EnumerateArray().Select(e => e.GetString()!)] : [];
    }

    private static (string Name, string Path, bool Checked, bool Outside)[] Rows(ProjectDependenciesViewModel dialog) =>
        [.. dialog.Rows.Select(r => (r.Name, r.DisplayPath, r.IsChecked, r.IsOutside))];

    /// <summary>Runs <c>project.dependencies</c> (target by <paramref name="argument"/> or by the focus) and drives the dialog.</summary>
    private static async Task<ProjectDependenciesViewModel?> ShowAsync(F f, Func<ProjectDependenciesViewModel, Task> drive, string? argument = null)
    {
        ProjectDependenciesViewModel? shown = null;
        f.Dialogs.Script(ProjectDependenciesViewModel.DialogName, async dialog =>
        {
            shown = (ProjectDependenciesViewModel)dialog;
            await drive(shown);
            if (!shown.IsClosed)
                await shown.PressCancelAsync();
        });
        await f.Services.Commands.ExecuteAsync("project.dependencies", argument);
        return shown;
    }

    private static int Writes(F f, string file) => f.Recording.Calls.Count(call => call.Contains(file, StringComparison.Ordinal)
        && (call.StartsWith("Write", StringComparison.Ordinal) || call.StartsWith("Replace", StringComparison.Ordinal)
            || call.StartsWith("Move", StringComparison.Ordinal) || call.StartsWith("Overwrite", StringComparison.Ordinal)));

    /// <summary>
    /// IDE-PM-175, IDE-PM-176: the target is the selected root of the focused Project Manager, else the active project; the
    /// rows are the other available members in group order (checked from <c>dependencies</c>, paths compared as full
    /// paths without regard to case), then the entries outside the group — checked, with the suffix. Without another
    /// project: the placeholder. Enabled for an available member while Idle.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-175")]
    [Trait("Req", "IDE-PM-176")]
    public Task ListsOtherMembersThenOutsideEntries_InitialChecks() => TestUiThread.RunAsync(async () =>
    {
        var f = new F();
        f.AddProject("Shared", directory: @"D:\common");
        WithDependencies(f, "App", "D:/common/Shared.pproj", "../TOOLS/tools.pproj");
        await f.OpenGroupAsync("App/App.pproj", "App/App.pproj", "Lib/Lib.pproj", "Gone/Gone.pproj", "Tools/Tools.pproj");
        var commands = f.Services.Commands;
        f.Services.FocusTracker.Update(F.EditorFocus);

        // The editor has the focus: the active project.
        Assert.True(commands.CanExecute("project.dependencies"));
        var dialog = await ShowAsync(f, d =>
        {
            Assert.Equal(("Dependencies of App", "_Projects that are built before App:"), (d.Title, d.ProjectsLabel));
            Assert.Equal((520.0, 360.0, true), (d.Width, d.Height, d.CanResize));
            Assert.Equal(
            [
                ("Lib", "../Lib/Lib.pproj", false, false),
                ("Tools", "../Tools/Tools.pproj", true, false),
                ("Shared", "D:/common/Shared.pproj", true, true),
            ], Rows(d));
            Assert.Equal([null, null, "(not in the group)"], d.Rows.Select(r => r.Suffix));
            Assert.Equal(@"D:\common\Shared.pproj", d.Rows[2].FullPath);
            Assert.Null(d.Placeholder);
            Assert.Null(d.Error);
            Assert.True(d.Ok.IsEnabled);
            return Task.CompletedTask;
        });
        Assert.NotNull(dialog);
        Assert.False(dialog.Result);

        // The Project Manager has the focus: the selected root — a listed member.
        f.Services.FocusTracker.Update(F.ProjectManagerFocus);
        f.Pm.Select(f.Pm.Find("$project:Lib")!);
        Assert.True(commands.CanExecute("project.dependencies"));
        dialog = await ShowAsync(f, d =>
        {
            Assert.Equal("Dependencies of Lib", d.Title);
            Assert.Equal([("App", "../App/App.pproj", false, false), ("Tools", "../Tools/Tools.pproj", false, false)], Rows(d));
            return Task.CompletedTask;
        });
        Assert.NotNull(dialog);

        // The context menus list the command (UI-TW-129); an unavailable root has none and the command is disabled for it.
        Assert.Contains(f.Pm.ContextMenu(), i => i.CommandId == "project.dependencies");
        f.Pm.Select(f.Pm.Find("$project")!);
        Assert.Contains(f.Pm.ContextMenu(), i => i.CommandId == "project.dependencies");
        f.Pm.Select(f.Pm.Find("$project:Gone")!);
        Assert.DoesNotContain(f.Pm.ContextMenu(), i => i.CommandId == "project.dependencies");
        Assert.False(commands.CanExecute("project.dependencies"));
        Assert.Null(await ShowAsync(f, _ => Task.CompletedTask));

        // Idle only.
        f.Pm.Select(f.Pm.Find("$project")!);
        Assert.True(commands.CanExecute("project.dependencies"));
        Assert.True(f.Services.State.TryTransition(Building));
        Assert.False(commands.CanExecute("project.dependencies"));
        Assert.True(f.Services.State.TryTransition(IdeStateInfo.Idle));

        // A project opened alone: no row, the placeholder; nothing open: disabled.
        var alone = new F();
        Assert.True(await alone.Projects.OpenAsync(F.Proj("Tools")));
        dialog = await ShowAsync(alone, d =>
        {
            Assert.Empty(d.Rows);
            Assert.Equal("There are no other projects in the group. Add a project to the group to make it a dependency.", d.Placeholder);
            Assert.True(d.Ok.IsEnabled);
            return Task.CompletedTask;
        });
        Assert.NotNull(dialog);
        Assert.False(new F().Services.Commands.CanExecute("project.dependencies"));
    });

    /// <summary>
    /// Card F09-010 scope 2, IDE-PM-177: App depends on Lib and Lib on Tools; ticking App in the dialog of Tools shows the
    /// cycle with the project names (first = last) and disables OK — a cycle cannot be saved. Unticking clears it.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-177")]
    public Task Cycle_ShowsThePathAndDisablesOk() => TestUiThread.RunAsync(async () =>
    {
        var f = new F();
        WithDependencies(f, "App", "../Lib/Lib.pproj");
        WithDependencies(f, "Lib", "../Tools/Tools.pproj");
        await f.OpenGroupAsync();
        var before = f.Text(F.Proj("Tools"));

        var dialog = await ShowAsync(f, async d =>
        {
            Assert.Equal([("App", "../App/App.pproj", false, false), ("Lib", "../Lib/Lib.pproj", false, false)], Rows(d));
            Assert.Null(d.Error);
            var changes = new List<string?>();
            d.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

            d.Rows[0].IsChecked = true; // Tools → App → Lib → Tools
            Assert.Equal("This selection creates a circular dependency:\nTools \u2192 App \u2192 Lib \u2192 Tools", d.Error);
            Assert.Contains(nameof(ProjectDependenciesViewModel.Error), changes);
            Assert.False(d.Ok.IsEnabled);
            await d.PressDefaultAsync(); // Enter does nothing
            Assert.False(d.IsClosed);
            Assert.Equal(before, f.Text(F.Proj("Tools")));

            // The shorter cycle through Lib alone.
            d.Rows[0].IsChecked = false;
            Assert.Null(d.Error);
            Assert.True(d.Ok.IsEnabled);
            d.Rows[1].IsChecked = true;
            Assert.Equal("This selection creates a circular dependency:\nTools \u2192 Lib \u2192 Tools", d.Error);
            Assert.False(d.Ok.IsEnabled);
            d.Rows[1].IsChecked = false;
            Assert.Null(d.Error);
            Assert.True(d.Ok.IsEnabled);
        }, F.Proj("Tools"));

        Assert.NotNull(dialog);
        Assert.Equal(before, f.Text(F.Proj("Tools")));
    });

    /// <summary>
    /// IDE-PM-178, IDE-PM-180: OK writes <c>dependencies</c> — an entry that was there keeps its text, a newly ticked
    /// project is relative to the project's directory with <c>/</c> (another drive: absolute), group order first, then
    /// the outside entries. The loaded project goes through the project service (saved at once), a listed member through
    /// the serializer; every other key of either file is kept.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-178")]
    [Trait("Req", "IDE-PM-180")]
    public Task Ok_WritesRelativePaths_ForTheActiveProjectAndForAListedMember() => TestUiThread.RunAsync(async () =>
    {
        var f = new F();
        f.AddProject("Ext", directory: @"D:\ext");
        f.AddProject("Shared", directory: @"D:\common");
        WithDependencies(f, "App", "D:/common/Shared.pproj", "../TOOLS/tools.pproj");
        WithDependencies(f, "Lib");
        await f.OpenGroupAsync("App/App.pproj", "App/App.pproj", "Lib/Lib.pproj", "Tools/Tools.pproj", "D:/ext/Ext.pproj");
        f.Services.FocusTracker.Update(F.EditorFocus);

        // The active project.
        var dialog = await ShowAsync(f, async d =>
        {
            Assert.Equal(["Lib", "Tools", "Ext", "Shared"], d.Rows.Select(r => r.Name));
            d.Rows[0].IsChecked = true;
            d.Rows[2].IsChecked = true;
            Assert.Equal(["../Lib/Lib.pproj", "../TOOLS/tools.pproj", "D:/ext/Ext.pproj", "D:/common/Shared.pproj"], d.Entries);
            await d.PressDefaultAsync();
            Assert.True(d.IsClosed);
        });
        Assert.True(dialog!.Result);
        var app = f.Text(F.Proj("App"));
        Assert.Equal(["../Lib/Lib.pproj", "../TOOLS/tools.pproj", "D:/ext/Ext.pproj", "D:/common/Shared.pproj"], Dependencies(app));
        Assert.Contains("\"x-keep\"", app, StringComparison.Ordinal);
        Assert.Contains("\"note\": \"kept\"", app, StringComparison.Ordinal);
        Assert.Equal(Dependencies(app), f.Projects.Definition!.Dependencies);
        Assert.Equal(["AppUnit.pas"], f.Projects.Definition.Files.Select(file => file.Path).Where(p => p.EndsWith("Unit.pas", StringComparison.Ordinal)));
        Assert.DoesNotContain(f.Services.Documents.Documents, d => d.IsModified); // saved at once, as Project Options
        Assert.Empty(f.Dialogs.Requests);
        Assert.Equal(F.Proj("App"), f.Projects.Project!.FilePath);

        // A listed member: the file is written, the loaded project and the group file are not touched.
        var group = f.Text(F.GroupPath);
        var libBefore = f.Text(F.Proj("Lib"));
        dialog = await ShowAsync(f, async d =>
        {
            Assert.Equal("Dependencies of Lib", d.Title);
            Assert.Equal(["App", "Tools", "Ext"], d.Rows.Select(r => r.Name));
            d.Rows[1].IsChecked = true;
            await d.PressDefaultAsync();
            Assert.True(d.IsClosed);
        }, F.Proj("Lib"));
        Assert.True(dialog!.Result);
        var lib = f.Text(F.Proj("Lib"));
        Assert.Equal(["../Tools/Tools.pproj"], Dependencies(lib));
        Assert.Contains("\"note\": \"kept\"", lib, StringComparison.Ordinal);
        using (var was = JsonDocument.Parse(libBefore))
        using (var now = JsonDocument.Parse(lib))
        {
            // Every key the file had keeps its value (the writer is the serializer's canonical one, as for the loaded project).
            Assert.All(was.RootElement.EnumerateObject().Where(p => p.Name != "dependencies"),
                p => Assert.Equal(JsonSerializer.Serialize(p.Value), JsonSerializer.Serialize(now.RootElement.GetProperty(p.Name))));
        }

        Assert.Equal(app, f.Text(F.Proj("App")));
        Assert.Equal(group, f.Text(F.GroupPath));
        Assert.Equal(F.Proj("App"), f.Projects.Project!.FilePath);
        Assert.Empty(f.Dialogs.Requests);

        // The next dialog of that member shows what was written.
        await ShowAsync(f, d =>
        {
            Assert.Equal([false, true, false], d.Rows.Select(r => r.IsChecked));
            return Task.CompletedTask;
        }, F.Proj("Lib"));
    });

    /// <summary>
    /// IDE-PM-178, IDE-PM-179: OK without a change writes nothing; an outside entry that is unticked is removed and cannot
    /// be added again from the dialog.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-178")]
    [Trait("Req", "IDE-PM-179")]
    public Task UntickedOutsideEntryIsRemoved_NoChangeWritesNothing() => TestUiThread.RunAsync(async () =>
    {
        var f = new F();
        WithDependencies(f, "App", "../Lib/Lib.pproj", "D:/common/Shared.pproj");
        await f.OpenGroupAsync();
        f.Services.FocusTracker.Update(F.EditorFocus);
        var before = f.Text(F.Proj("App"));
        var definition = f.Projects.Definition;
        var writes = Writes(f, "App.pproj");

        // OK, nothing changed (also after a change that was taken back).
        var dialog = await ShowAsync(f, async d =>
        {
            d.Rows[1].IsChecked = true;
            d.Rows[1].IsChecked = false;
            await d.PressDefaultAsync();
            Assert.True(d.IsClosed);
        });
        Assert.True(dialog!.Result);
        Assert.Equal(before, f.Text(F.Proj("App")));
        Assert.Equal(writes, Writes(f, "App.pproj"));
        Assert.Same(definition, f.Projects.Definition);

        // The outside entry (the file does not exist: named after its file) is unticked: OK removes it.
        dialog = await ShowAsync(f, async d =>
        {
            Assert.Equal([("Lib", "../Lib/Lib.pproj", true, false), ("Tools", "../Tools/Tools.pproj", false, false),
                ("Shared", "D:/common/Shared.pproj", true, true)], Rows(d));
            d.Rows[2].IsChecked = false;
            Assert.Null(d.Error);
            await d.PressDefaultAsync();
        });
        Assert.True(dialog!.Result);
        Assert.Equal(["../Lib/Lib.pproj"], Dependencies(f.Text(F.Proj("App"))));

        // It is not offered again: only members of the group can be ticked.
        await ShowAsync(f, d =>
        {
            Assert.Equal([("Lib", "../Lib/Lib.pproj", true, false), ("Tools", "../Tools/Tools.pproj", false, false)], Rows(d));
            return Task.CompletedTask;
        });
    });

    /// <summary>
    /// IDE-PM-178: a failed write shows <c>Msg.Dependencies.SaveFailed</c> (project name, system text) and keeps the dialog
    /// open — for the loaded project and for a listed member; nothing changes in memory.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-178")]
    public Task WriteFails_MessageAndDialogStaysOpen() => TestUiThread.RunAsync(async () =>
    {
        var f = new F();
        await f.OpenGroupAsync();
        f.Services.FocusTracker.Update(F.EditorFocus);
        f.Dialogs.Answer("Msg.Dependencies.SaveFailed", "OK");
        string? failing = null;
        f.Recording.FailWith = call => failing is not null && call.Contains(failing, StringComparison.Ordinal)
            && (call.StartsWith("Write", StringComparison.Ordinal) || call.StartsWith("Replace", StringComparison.Ordinal)
                || call.StartsWith("Move", StringComparison.Ordinal) || call.StartsWith("Overwrite", StringComparison.Ordinal))
            ? new IOException("The disk is full.")
            : null;

        foreach (var (name, argument) in new[] { ("App", (string?)null), ("Lib", F.Proj("Lib")) })
        {
            var before = f.Text(F.Proj(name));
            var definition = f.Projects.Definition;
            f.Dialogs.Requests.Clear();
            failing = name + ".pproj";
            var dialog = await ShowAsync(f, async d =>
            {
                Assert.Equal("Dependencies of " + name, d.Title);
                d.Rows[^1].IsChecked = true; // Tools
                await d.PressDefaultAsync();

                var request = Assert.Single(f.Dialogs.Requests);
                Assert.Equal(("Msg.Dependencies.SaveFailed", name, "The disk is full."), (request.MessageId, request.Args[0], request.Args[1]));
                Assert.False(d.IsClosed);
                Assert.True(d.Rows[^1].IsChecked); // the selection is kept
                Assert.Equal(before, f.Text(F.Proj(name)));
                Assert.Same(definition, f.Projects.Definition);

                // The write works again: OK saves and closes.
                failing = null;
                await d.PressDefaultAsync();
                Assert.True(d.IsClosed);
            }, argument);
            Assert.True(dialog!.Result);
            Assert.Single(f.Dialogs.Requests);
            Assert.Equal(["../Tools/Tools.pproj"], Dependencies(f.Text(F.Proj(name))));
            Assert.DoesNotContain(f.Files.AllFiles, p => p.Contains(".~tmp", StringComparison.Ordinal));
        }
    });
}
