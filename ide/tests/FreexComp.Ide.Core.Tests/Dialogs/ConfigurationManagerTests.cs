using System.Text.Json;
using FreexComp.Build;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Tests.Build;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Dialogs;

/// <summary>
/// The Configuration Manager (phase8-configurations.md §4, I-248): the list in inheritance order, New, Rename, Delete,
/// Set Active, the parent combo and the own-values line, all on a working copy that <c>OK</c> applies.
/// </summary>
[Trait("Phase", "8")]
public sealed class ConfigurationManagerTests
{
    /// <summary>A child stands before its parent in the file; <c>Profiling</c> is two levels deep and uses the reset marker.</summary>
    internal const string Chain = """
        , "searchPaths": ["lib"], "defines": ["APP"],
          "configurations": {
            "Debug": { "optimization": 0, "debugInfo": true, "defines": ["DEBUG"], "outputDir": "bin/Debug" },
            "Release-Demo": { "inherits": "Release", "defines": ["DEMO"] },
            "Release": { "optimization": 2, "debugInfo": false, "defines": ["RELEASE"], "searchPaths": ["rel"], "outputDir": "bin/Release", "x-note": 1 },
            "Profiling": { "inherits": "Release-Demo", "debugInfo": true, "defines": ["!", "PROFILE"] } },
          "activeConfiguration": "Release-Demo"
        """;

    private static async Task<(BuildTestBed Bed, ConfigurationManagerViewModel Manager)> OpenAsync(string extra = Chain)
    {
        var bed = new BuildTestBed();
        await bed.OpenHelloAsync(extra: extra);
        return (bed, Manager(bed));
    }

    private static ConfigurationManagerViewModel Manager(BuildTestBed bed) =>
        new(bed.Services.Projects, bed.Dialogs, bed.Services.Status, bed.Services.Layout);

    private static void ScriptRename(BuildTestBed bed, string newName) =>
        bed.Dialogs.Script(RenameConfigurationViewModel.DialogName, async dialog =>
        {
            var sub = (RenameConfigurationViewModel)dialog;
            sub.ConfigName = newName;
            await sub.Rename.PressAsync();
        });

    private static JsonElement Saved(BuildTestBed bed) => JsonDocument.Parse(bed.Text(BuildTestBed.ProjectPath)).RootElement;

    private static string Entry(BuildTestBed bed, string name) => JsonSerializer.Serialize(Saved(bed).GetProperty("configurations").GetProperty(name));

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-CFG-011")]
    [Trait("Req", "UI-DLG-271")]
    [Trait("Req", "UI-DLG-272")]
    public Task ListOrderIndentAndActiveMarker() => TestUiThread.RunAsync(async () =>
    {
        var (bed, manager) = await OpenAsync();
        Assert.Equal(("ConfigurationManager", "Configuration Manager — Hello", 560.0, 420.0, true),
            (manager.Name, manager.Title, manager.Width, manager.Height, manager.CanResize));

        // Roots in file order, each followed by its descendants; 16 px per level; the active one is marked.
        Assert.Equal(
            [("Debug", 0, 0.0, false, "—"), ("Release", 0, 0.0, false, "—"), ("Release-Demo", 1, 16.0, true, "Release"), ("Profiling", 2, 32.0, false, "Release-Demo")],
            manager.Rows.Select(r => (r.Name, r.Level, r.Indent, r.IsActive, r.ParentText)));
        Assert.Equal(["Debug", "Release", "Release-Demo (active)", "Profiling"], manager.Rows.Select(r => r.AutomationName));
        Assert.Equal("Release-Demo", manager.SelectedName); // initial selection = the active configuration

        // Children in file order under each parent; the indent stops growing after six levels (UI-DLG-272).
        var project = bed.Services.Projects.Definition!;
        project = ConfigurationEdits.AddDerived(project, "A-Child", "Release");
        var parent = "Profiling";
        foreach (var name in (string[])["L3", "L4", "L5", "L6", "L7", "L8"])
        {
            project = ConfigurationEdits.AddDerived(project, name, parent);
            parent = name;
        }

        Assert.True(await bed.Services.Projects.ApplyOptionsAsync(project));
        manager = Manager(bed);
        Assert.Equal(["Debug", "Release", "Release-Demo", "Profiling", "L3", "L4", "L5", "L6", "L7", "L8", "A-Child"], manager.Rows.Select(r => r.Name));
        Assert.Equal([0.0, 0, 16, 32, 48, 64, 80, 96, 96, 96, 16], manager.Rows.Select(r => r.Indent));
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-CFG-013")]
    public Task Rename_UpdatesChildrenActiveAndKeepsPosition() => TestUiThread.RunAsync(async () =>
    {
        var (bed, manager) = await OpenAsync();
        manager.SelectedName = "Release";
        ScriptRename(bed, "Final");
        await manager.RenameAsync();
        Assert.Equal("Final", manager.SelectedName);
        Assert.Equal(["Debug", "Final", "Release-Demo", "Profiling"], manager.Rows.Select(r => r.Name));
        Assert.Equal("Final", manager.Rows[2].ParentText);

        manager.SelectedName = "Release-Demo"; // the active one
        ScriptRename(bed, "Demo");
        await manager.RenameAsync();
        Assert.True(manager.Rows.Single(r => r.Name == "Demo").IsActive);
        Assert.Equal("Release-Demo", bed.Services.Projects.Project!.ActiveConfiguration); // the working copy only

        await manager.Ok.PressAsync();
        Assert.True(manager.IsClosed);
        var saved = Saved(bed);
        Assert.Equal(["Debug", "Demo", "Final", "Profiling"], saved.GetProperty("configurations").EnumerateObject().Select(p => p.Name)); // positions kept
        Assert.Equal("Demo", saved.GetProperty("activeConfiguration").GetString());
        Assert.Equal("""{"inherits":"Final","defines":["DEMO"]}""", Entry(bed, "Demo"));
        Assert.Equal("""{"inherits":"Demo","debugInfo":true,"defines":["!","PROFILE"]}""", Entry(bed, "Profiling"));
        // Unknown keys follow; a written output folder is left alone.
        Assert.Equal("""{"optimization":2,"debugInfo":false,"defines":["RELEASE"],"searchPaths":["rel"],"outputDir":"bin/Release","x-note":1}""", Entry(bed, "Final"));
        Assert.Equal("Demo", bed.Services.Projects.Project!.ActiveConfiguration);
        Assert.Equal("bin/Demo", ConfigurationResolver.Resolve(bed.Services.Projects.Definition!, "Demo").OutputDir); // the default folder follows the name
        Assert.Equal(string.Format(Strings.Msg_Project_ConfigRenamed, "Release-Demo", "Demo"), bed.Services.Status.Message);
        Assert.Equal("Renamed configuration Release-Demo to Demo; existing output folders were kept", bed.Services.Status.Message);
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-CFG-013")]
    [Trait("Req", "UI-DLG-275")]
    public async Task Rename_Validation()
    {
        var sub = new RenameConfigurationViewModel(["Debug", "Release", "Release-Demo"], "Release");
        Assert.Equal(("RenameConfiguration", Strings.RenameConfiguration_Title, 360.0, false), (sub.Name, sub.Title, sub.Width, sub.CanResize));
        Assert.Equal(("Release", null, false), (sub.ConfigName, sub.Error, sub.Rename.IsEnabled)); // unchanged: valid, nothing to do

        sub.ConfigName = "bad name";
        Assert.Equal((Strings.Msg_ProjectOptions_ConfigNameInvalid, false), (sub.Error, sub.Rename.IsEnabled));
        sub.ConfigName = "";
        Assert.Equal((Strings.Msg_ProjectOptions_ConfigNameInvalid, false), (sub.Error, sub.Rename.IsEnabled));
        sub.ConfigName = new string('x', 33);
        Assert.Equal((Strings.Msg_ProjectOptions_ConfigNameInvalid, false), (sub.Error, sub.Rename.IsEnabled));
        sub.ConfigName = "debug"; // another configuration, compared without case
        Assert.Equal((string.Format(Strings.Msg_ProjectOptions_ConfigNameExists, "debug"), false), (sub.Error, sub.Rename.IsEnabled));
        sub.ConfigName = "Release";
        Assert.Equal((null, false), (sub.Error, sub.Rename.IsEnabled));
        sub.ConfigName = "RELEASE"; // a change of letter case only is a rename
        Assert.Equal((null, true), (sub.Error, sub.Rename.IsEnabled));

        await sub.Rename.PressAsync();
        Assert.True(sub.IsClosed);
        Assert.Equal("RELEASE", sub.Result);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-CFG-014")]
    public Task Delete_DisabledForLastAndForInherited_ElseConfirms() => TestUiThread.RunAsync(async () =>
    {
        var (bed, manager) = await OpenAsync();

        manager.SelectedName = "Release";
        Assert.False(manager.CanDelete);
        Assert.Equal(string.Format(Strings.Msg_ProjectOptions_ConfigDeleteInherited, "Release", "Release-Demo"), manager.DeleteToolTip);
        await manager.DeleteAsync(); // disabled: asks nothing
        Assert.Empty(bed.Dialogs.Requests);

        manager.SelectedName = "Profiling";
        Assert.True(manager.CanDelete);
        Assert.Null(manager.DeleteToolTip);
        bed.Dialogs.Enqueue("Msg.ProjectOptions.ConfigDeleteConfirm", "Cancel", "Delete");
        await manager.DeleteAsync();
        Assert.Equal(4, manager.Rows.Count);
        await manager.DeleteAsync();
        Assert.Equal(["Debug", "Release", "Release-Demo"], manager.Rows.Select(r => r.Name));
        Assert.Equal(["Profiling", "Profiling"], bed.Dialogs.Requests.Select(r => r.Args[0]));
        Assert.Equal("Release-Demo", manager.SelectedName); // the neighbouring row

        // Deleting the active configuration makes the first remaining one (file order) active.
        bed.Dialogs.Answer("Msg.ProjectOptions.ConfigDeleteConfirm", "Delete");
        await manager.DeleteAsync();
        Assert.Equal([("Debug", true), ("Release", false)], manager.Rows.Select(r => (r.Name, r.IsActive)));
        manager.SelectedName = "Release";
        Assert.True(manager.CanDelete); // nobody inherits from it any more
        await manager.DeleteAsync();

        // The last one stays.
        Assert.Equal("Debug", manager.SelectedName);
        Assert.False(manager.CanDelete);
        Assert.Equal(Strings.Msg_ProjectOptions_ConfigDeleteLast, manager.DeleteToolTip);

        await manager.Ok.PressAsync();
        Assert.Equal(["Debug"], Saved(bed).GetProperty("configurations").EnumerateObject().Select(p => p.Name));
        Assert.Equal("Debug", bed.Services.Projects.Project!.ActiveConfiguration);
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-CFG-010")]
    [Trait("Req", "IDE-CFG-015")]
    public Task SetActive_AppliedOnOkLikeSetConfiguration() => TestUiThread.RunAsync(async () =>
    {
        var (bed, manager) = await OpenAsync();
        Assert.False(manager.CanSetActive); // the active row is selected
        manager.SelectedName = "Debug";
        Assert.True(manager.CanSetActive);
        manager.SetActive();
        Assert.False(manager.CanSetActive);
        Assert.Equal(["Debug (active)", "Release", "Release-Demo", "Profiling"], manager.Rows.Select(r => r.AutomationName));
        Assert.Equal("Release-Demo", bed.Services.Projects.Project!.ActiveConfiguration);

        var changes = 0;
        bed.Services.Projects.ProjectChanged += (_, _) => changes++;
        await manager.Ok.PressAsync();
        Assert.True(manager.Result);
        Assert.Equal(("Debug", 1), (bed.Services.Projects.Project!.ActiveConfiguration, changes));
        var byDialog = bed.Text(BuildTestBed.ProjectPath);
        Assert.Equal("Debug", Saved(bed).GetProperty("activeConfiguration").GetString());

        // Exactly the effects of project.setConfiguration: the same file from the same start.
        await bed.Services.Commands.ExecuteAsync("project.setConfiguration", "Release-Demo");
        await bed.Services.Commands.ExecuteAsync("project.setConfiguration", "Debug");
        Assert.Equal(byDialog, bed.Text(BuildTestBed.ProjectPath));
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-CFG-010")]
    [Trait("Req", "IDE-CFG-018")]
    public Task CancelChangesNothing_OkWithoutChangeDoesNotModify() => TestUiThread.RunAsync(async () =>
    {
        var (bed, manager) = await OpenAsync();
        var definition = bed.Services.Projects.Definition!;
        var text = bed.Text(BuildTestBed.ProjectPath);
        var changes = 0;
        bed.Services.Projects.ProjectChanged += (_, _) => changes++;

        manager.SelectedName = "Debug";
        manager.SetActive();
        ScriptRename(bed, "Dbg");
        await manager.RenameAsync();
        NewConfigurationTests.ScriptNew(bed, "Extra", "Dbg", inherit: null);
        await manager.NewAsync();
        await manager.PressCancelAsync();
        Assert.True(manager.IsClosed);
        Assert.False(manager.Result);
        Assert.Same(definition, bed.Services.Projects.Definition);
        Assert.Equal((text, 0), (bed.Text(BuildTestBed.ProjectPath), changes));

        // OK with no change applies nothing.
        manager = Manager(bed);
        manager.SelectedName = "Profiling"; // a selection is not a change
        await manager.Ok.PressAsync();
        Assert.True(manager.IsClosed);
        Assert.Same(definition, bed.Services.Projects.Definition);
        Assert.Equal((text, 0), (bed.Text(BuildTestBed.ProjectPath), changes));
        Assert.Null(bed.Services.Status.Message);
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-CFG-016")]
    public Task ParentCombo_ExcludesSelfAndDescendants() => TestUiThread.RunAsync(async () =>
    {
        var (_, manager) = await OpenAsync();
        Assert.Equal("(none)", ConfigurationManagerViewModel.NoParent);

        Assert.Equal(["(none)", "Debug", "Release"], manager.ParentItems); // Release-Demo: not itself, not Profiling
        Assert.Equal("Release", manager.SelectedParent);
        manager.SelectedName = "Release";
        Assert.Equal(["(none)", "Debug"], manager.ParentItems); // its two descendants are excluded
        Assert.Equal("(none)", manager.SelectedParent);
        manager.SelectedName = "Profiling";
        Assert.Equal(["(none)", "Debug", "Release-Demo", "Release"], manager.ParentItems); // file order
        Assert.Equal("Release-Demo", manager.SelectedParent);
        manager.SelectedName = "Debug";
        Assert.Equal(["(none)", "Release-Demo", "Release", "Profiling"], manager.ParentItems);

        // A value outside the list is ignored: no cycle can be made.
        manager.SelectedName = "Release";
        manager.SelectedParent = "Profiling";
        Assert.Equal("(none)", manager.SelectedParent);
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-CFG-016")]
    public Task Reparent_KeepsOwnValues() => TestUiThread.RunAsync(async () =>
    {
        var (bed, manager) = await OpenAsync();

        // A derived configuration under another parent: its own define stays, the rest now comes from Debug.
        manager.SelectedParent = "Debug";
        Assert.Equal("Release-Demo", manager.SelectedName); // stays selected
        Assert.Equal(["Debug", "Release-Demo", "Profiling", "Release"], manager.Rows.Select(r => r.Name)); // re-ordered
        var effective = ConfigurationResolver.Resolve(manager.WorkingCopy, "Release-Demo");
        Assert.Equal((0, true), (effective.Optimization, effective.DebugInfo));
        Assert.Equal(["APP", "DEBUG", "DEMO"], effective.Defines);

        // A root configuration under a parent: every value it had stays its own.
        manager.SelectedName = "Release";
        manager.SelectedParent = "Debug";
        Assert.Equal(["Debug", "Release-Demo", "Profiling", "Release"], manager.Rows.Select(r => r.Name));
        Assert.Equal([0, 1, 2, 1], manager.Rows.Select(r => r.Level));
        effective = ConfigurationResolver.Resolve(manager.WorkingCopy, "Release");
        Assert.Equal((2, false), (effective.Optimization, effective.DebugInfo));
        Assert.Equal(["APP", "DEBUG", "RELEASE"], effective.Defines);

        await manager.Ok.PressAsync();
        Assert.Equal("""{"inherits":"Debug","defines":["DEMO"]}""", Entry(bed, "Release-Demo"));
        Assert.Equal("""{"inherits":"Debug","optimization":2,"debugInfo":false,"defines":["RELEASE"],"searchPaths":["rel"],"outputDir":"bin/Release","x-note":1}""",
            Entry(bed, "Release"));
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-CFG-016")]
    public Task ParentNone_DetachKeepsEffectiveValues() => TestUiThread.RunAsync(async () =>
    {
        var (bed, manager) = await OpenAsync();
        foreach (var name in (string[])["Profiling", "Release-Demo"])
        {
            var before = ConfigurationResolver.Resolve(manager.WorkingCopy, name);
            manager.SelectedName = name;
            manager.SelectedParent = ConfigurationManagerViewModel.NoParent;
            Assert.Equal(name, manager.SelectedName);
            Assert.Equal((0, "—"), (manager.Rows.Single(r => r.Name == name).Level, manager.Rows.Single(r => r.Name == name).ParentText));
            var after = ConfigurationResolver.Resolve(manager.WorkingCopy, name);
            Assert.Equal([name], after.Chain);
            Assert.Equal((before.Optimization, before.DebugInfo, before.OutputDir, before.Rtti), (after.Optimization, after.DebugInfo, after.OutputDir, after.Rtti));
            Assert.Equal(before.Defines, after.Defines);
            Assert.Equal(before.SearchPaths, after.SearchPaths);
        }

        Assert.Equal(["Debug", "Release-Demo", "Release", "Profiling"], manager.Rows.Select(r => r.Name)); // all roots: file order
        await manager.Ok.PressAsync();
        Assert.Equal("""{"optimization":2,"debugInfo":true,"defines":["!","PROFILE"],"searchPaths":["rel"],"outputDir":"bin/Profiling"}""", Entry(bed, "Profiling"));
        Assert.Equal("""{"optimization":2,"debugInfo":false,"defines":["RELEASE","DEMO"],"searchPaths":["rel"],"outputDir":"bin/Release-Demo"}""", Entry(bed, "Release-Demo"));
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-CFG-017")]
    [Trait("Req", "UI-DLG-274")]
    public Task OwnValuesLine() => TestUiThread.RunAsync(async () =>
    {
        var (bed, manager) = await OpenAsync("""
            , "configurations": {
                "Debug": { "optimization": 0, "debugInfo": true, "defines": [], "outputDir": "bin/Debug" },
                "Plain": { "inherits": "Debug" },
                "Some": { "inherits": "Debug", "debugInfo": false, "defines": ["X"] },
                "Full": { "inherits": "Debug", "optimization": 1, "debugInfo": false, "defines": ["X"], "searchPaths": ["p"], "outputDir": "out/full", "rtti": "published" } }
            """);
        string Own(string name)
        {
            manager.SelectedName = name;
            return manager.OwnValues;
        }

        Assert.Equal("all (no parent)", Own("Debug"));
        Assert.Equal("none", Own("Plain"));
        Assert.Equal("debug information, defines", Own("Some"));
        Assert.Equal("optimization, debug information, defines, search paths, output folder, RTTI", Own("Full"));

        // The line follows the operations: a detached configuration sets everything itself.
        manager.SelectedParent = ConfigurationManagerViewModel.NoParent;
        Assert.Equal("all (no parent)", manager.OwnValues);
        NewConfigurationTests.ScriptNew(bed, "Fresh", "Some", inherit: true);
        await manager.NewAsync();
        Assert.Equal(("Fresh", "none", "Some"), (manager.SelectedName, manager.OwnValues, manager.SelectedParent));
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-CFG-012")]
    [Trait("Req", "IDE-CFG-018")]
    public Task OperationsCombine_SavedProjectLoadsWithoutDiagnostics() => TestUiThread.RunAsync(async () =>
    {
        var (bed, manager) = await OpenAsync();

        // New (derived from the selected row, which is preselected in the sub-dialog) → rename its parent → re-parent → delete → OK.
        manager.SelectedName = "Release";
        NewConfigurationViewModel? seen = null;
        bed.Dialogs.Script(NewConfigurationViewModel.DialogName, async dialog =>
        {
            seen = (NewConfigurationViewModel)dialog;
            Assert.Equal(("Release", true), (seen.CopyFrom, seen.Inherit));
            seen.ConfigName = "Staging";
            await seen.Create.PressAsync();
        });
        await manager.NewAsync();
        Assert.NotNull(seen);
        Assert.Equal("Staging", manager.SelectedName); // the new row is selected
        Assert.Equal(["Debug", "Release", "Release-Demo", "Profiling", "Staging"], manager.Rows.Select(r => r.Name));

        manager.SelectedName = "Release";
        ScriptRename(bed, "Final");
        await manager.RenameAsync();
        manager.SelectedName = "Staging";
        Assert.Equal("Final", manager.SelectedParent);
        manager.SelectedParent = "Debug";
        NewConfigurationTests.ScriptNew(bed, "Final2", "Final", inherit: false); // a copy with the unknown key
        await manager.NewAsync();
        manager.SelectedName = "Profiling";
        bed.Dialogs.Answer("Msg.ProjectOptions.ConfigDeleteConfirm", "Delete");
        await manager.DeleteAsync();
        await manager.Ok.PressAsync();
        Assert.True(manager.IsClosed);

        var loaded = ProjectSerializer.ParseProject(BuildTestBed.ProjectPath, bed.Text(BuildTestBed.ProjectPath));
        Assert.Empty(loaded.Diagnostics);
        Assert.Equal(["Debug", "Release-Demo", "Final", "Staging", "Final2"], loaded.Value!.ConfigurationNames);
        Assert.Equal("""{"inherits":"Debug"}""", Entry(bed, "Staging"));
        Assert.Equal("""{"inherits":"Final","defines":["DEMO"]}""", Entry(bed, "Release-Demo"));
        Assert.Equal("""{"optimization":2,"debugInfo":false,"defines":["RELEASE"],"searchPaths":["rel"],"outputDir":"bin/Release","x-note":1}""", Entry(bed, "Final2"));
        foreach (var name in loaded.Value.ConfigurationNames)
            ConfigurationResolver.Resolve(loaded.Value, name); // every chain resolves
        Assert.Equal(ProjectSerializer.Save(loaded.Value), bed.Text(BuildTestBed.ProjectPath)); // canonical
    });
}
