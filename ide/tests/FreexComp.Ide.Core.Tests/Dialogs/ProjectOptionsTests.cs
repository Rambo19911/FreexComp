using System.Text.Json;
using FreexComp.Build;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Tests.Build;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Dialogs;

/// <summary>
/// The Search Paths page of Project Options per configuration (phase8-configurations.md § "Decision 2026-11-24", I-252):
/// own entries are edited, project-level and ancestor entries are listed read-only with their origin, and
/// <c>Do not inherit</c> writes the reset marker (configurations.md CMP-1152, CMP-1153, CMP-1160).
/// </summary>
[Trait("Phase", "8")]
public sealed class ProjectOptionsTests
{
    private static async Task<(BuildTestBed Bed, ProjectOptionsViewModel Options)> OpenAsync()
    {
        var bed = new BuildTestBed();
        await bed.OpenHelloAsync(extra: ConfigurationManagerTests.Chain);
        return (bed, NewConfigurationTests.Options(bed));
    }

    private static string[] Own(ProjectOptionsViewModel options) => [.. options.SearchPaths.Select(r => r.Path)];

    private static (string Path, string? Origin)[] Inherited(ProjectOptionsViewModel options) =>
        [.. options.InheritedSearchPaths.Select(r => (r.Path, r.Origin))];

    private static string[] Effective(BuildTestBed bed, string configuration) =>
        [.. ConfigurationResolver.Resolve(bed.Services.Projects.Definition!, configuration).SearchPaths];

    private static string Saved(BuildTestBed bed, string configuration) =>
        JsonSerializer.Serialize(NewConfigurationTests.SavedConfigurations(bed).GetProperty(configuration));

    private static string[] SavedProjectLevel(BuildTestBed bed) =>
        [.. JsonDocument.Parse(bed.Text(BuildTestBed.ProjectPath)).RootElement.GetProperty("searchPaths").EnumerateArray().Select(e => e.GetString()!)];

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-CFG-001")]
    public Task SearchPaths_PerConfiguration_RoundTrip() => TestUiThread.RunAsync(async () =>
    {
        var (bed, options) = await OpenAsync();
        var original = bed.Text(BuildTestBed.ProjectPath);
        Assert.Equal("Do not _inherit", Strings.ProjectOptions_SearchPathsDoNotInherit);
        Assert.Equal("Project", Strings.ProjectOptions_SearchPathOriginProject);

        // The page opens on "All configurations": the project-level list, as before.
        Assert.True(options.Pages.Single(p => p.Id == "SearchPaths").PerConfiguration);
        Assert.True(options.SearchPathsAllConfigurations);
        Assert.Equal(["lib"], Own(options));
        Assert.Empty(options.InheritedSearchPaths);

        // A configuration: its own entries are the editable list; choosing it changes nothing.
        options.SearchPathsAllConfigurations = false;
        options.SelectedConfiguration = "Release-Demo";
        Assert.Empty(options.SearchPaths);
        Assert.Equal([("lib", "Project"), ("rel", "Release")], Inherited(options));
        Assert.False(options.Apply.IsEnabled);
        await options.Ok.PressAsync();
        Assert.Equal(original, bed.Text(BuildTestBed.ProjectPath)); // byte-identical without a change

        options = NewConfigurationTests.Options(bed);
        options.SearchPathsAllConfigurations = false;
        options.SelectedConfiguration = "Release-Demo";
        options.AddSearchPath("demo");
        options.AddSearchPath("extra");
        options.AddSearchPath("DEMO"); // a duplicate of an own entry is dropped
        options.MoveSearchPath(1, -1);
        Assert.Equal(["extra", "demo"], Own(options));
        Assert.True(options.Apply.IsEnabled);

        // The other configurations and the project level keep their lists.
        options.SelectedConfiguration = "Release";
        Assert.Equal(["rel"], Own(options));
        Assert.Equal([("lib", "Project")], Inherited(options));
        options.SearchPathsAllConfigurations = true;
        Assert.Equal(["lib"], Own(options));
        Assert.Empty(options.InheritedSearchPaths);

        await options.Apply.PressAsync();
        Assert.Equal("""{"inherits":"Release","defines":["DEMO"],"searchPaths":["extra","demo"]}""", Saved(bed, "Release-Demo"));
        Assert.Equal(["lib"], SavedProjectLevel(bed));
        Assert.Contains("\"searchPaths\":[\"rel\"]", Saved(bed, "Release"), StringComparison.Ordinal);
        Assert.Equal(["lib", "rel", "extra", "demo"], Effective(bed, "Release-Demo"));

        // Read again: the saved entries are the own rows; a descendant lists them as inherited, in resolution order.
        options = NewConfigurationTests.Options(bed);
        options.SearchPathsAllConfigurations = false;
        options.SelectedConfiguration = "Release-Demo";
        Assert.Equal(["extra", "demo"], Own(options));
        options.SelectedConfiguration = "Profiling";
        Assert.Empty(options.SearchPaths);
        Assert.Equal([("lib", "Project"), ("rel", "Release"), ("extra", "Release-Demo"), ("demo", "Release-Demo")], Inherited(options));

        // Edit and remove act on the own rows; "All configurations" still edits the project-level list.
        options.SelectedConfiguration = "Release-Demo";
        options.EditSearchPath(0, "extra2");
        options.RemoveSearchPath(1);
        options.SearchPathsAllConfigurations = true;
        options.AddSearchPath("common");
        await options.Ok.PressAsync();
        Assert.Equal(["lib", "common"], SavedProjectLevel(bed));
        Assert.Equal(["lib", "common", "rel", "extra2"], Effective(bed, "Release-Demo"));
        Assert.Equal(["lib", "common"], Effective(bed, "Debug"));
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-CFG-001")]
    public Task InheritedEntriesAreReadOnly() => TestUiThread.RunAsync(async () =>
    {
        var (bed, options) = await OpenAsync();
        var original = bed.Text(BuildTestBed.ProjectPath);
        options.SearchPathsAllConfigurations = false;
        options.SelectedConfiguration = "Profiling";

        // Inherited rows carry their origin and are no part of the editable list.
        Assert.All(options.InheritedSearchPaths, r => Assert.True(r.IsInherited));
        Assert.Equal([("lib", "Project"), ("rel", "Release")], Inherited(options));
        Assert.Empty(options.SearchPaths);
        options.EditSearchPath(0, "changed");
        options.RemoveSearchPath(0);
        options.MoveSearchPath(0, 1);
        Assert.Equal([("lib", "Project"), ("rel", "Release")], Inherited(options));
        Assert.False(options.Apply.IsEnabled);

        // An own entry may repeat an inherited one (CMP-1160: no de-duplication); it is an editable row of its own.
        options.AddSearchPath("rel");
        var own = Assert.Single(options.SearchPaths);
        Assert.False(own.IsInherited);
        Assert.Null(own.Origin);
        Assert.Equal(2, options.InheritedSearchPaths.Count);
        options.RemoveSearchPath(0);

        // The inherited rows are those of the working copy: an edit of an ancestor or of the project shows at once.
        var notified = new List<string?>();
        options.PropertyChanged += (_, e) => notified.Add(e.PropertyName);
        options.SelectedConfiguration = "Release";
        options.AddSearchPath("r2");
        Assert.Contains(nameof(ProjectOptionsViewModel.InheritedSearchPaths), notified);
        options.SearchPathsAllConfigurations = true;
        options.AddSearchPath("base2");
        options.SearchPathsAllConfigurations = false;
        options.SelectedConfiguration = "Profiling";
        Assert.Equal([("lib", "Project"), ("base2", "Project"), ("rel", "Release"), ("r2", "Release")], Inherited(options));

        // An ancestor that does not inherit cuts the rows before it.
        options.SelectedConfiguration = "Release";
        options.SearchPathsDoNotInherit = true;
        options.SelectedConfiguration = "Profiling";
        Assert.Equal([("rel", "Release"), ("r2", "Release")], Inherited(options));

        // An invalid own entry marks the page; inherited rows are never the reason.
        options.AddSearchPath("a|b");
        Assert.Equal(Strings.Msg_Project_LocationInvalid, options.SearchPathsError);
        Assert.True(options.Pages.Single(p => p.Id == "SearchPaths").HasError);
        options.SelectedConfiguration = "Debug";
        Assert.Null(options.SearchPathsError);
        Assert.True(options.Pages.Single(p => p.Id == "SearchPaths").HasError); // another configuration still holds it
        Assert.False(options.Ok.IsEnabled);
        Assert.Equal(original, bed.Text(BuildTestBed.ProjectPath));
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-CFG-001")]
    public Task DoNotInherit_WritesResetMarker() => TestUiThread.RunAsync(async () =>
    {
        var (bed, options) = await OpenAsync();

        // The check box belongs to a configuration.
        Assert.False(options.CanSearchPathsDoNotInherit);
        options.SearchPathsDoNotInherit = true;
        Assert.False(options.SearchPathsDoNotInherit);
        Assert.False(options.Apply.IsEnabled);

        options.SearchPathsAllConfigurations = false;
        options.SelectedConfiguration = "Release-Demo";
        Assert.True(options.CanSearchPathsDoNotInherit);
        Assert.False(options.SearchPathsDoNotInherit);
        var notified = new List<string?>();
        options.PropertyChanged += (_, e) => notified.Add(e.PropertyName);
        options.SearchPathsDoNotInherit = true;
        Assert.Contains(nameof(ProjectOptionsViewModel.SearchPathsDoNotInherit), notified);
        Assert.Contains(nameof(ProjectOptionsViewModel.InheritedSearchPaths), notified);
        Assert.Empty(options.InheritedSearchPaths); // hidden
        Assert.Empty(options.SearchPaths); // the marker is no row
        await options.Apply.PressAsync();
        Assert.Equal("""{"inherits":"Release","defines":["DEMO"],"searchPaths":["!"]}""", Saved(bed, "Release-Demo"));
        Assert.Empty(Effective(bed, "Release-Demo"));

        options.AddSearchPath("only");
        await options.Apply.PressAsync();
        Assert.Equal("""{"inherits":"Release","defines":["DEMO"],"searchPaths":["!","only"]}""", Saved(bed, "Release-Demo"));
        Assert.Equal(["only"], Effective(bed, "Release-Demo"));
        Assert.Equal(["only"], Effective(bed, "Profiling"));
        Assert.Equal(["lib", "rel"], Effective(bed, "Release"));

        // Read again: checked, and the marker is not an entry. A descendant starts after the reset.
        options = NewConfigurationTests.Options(bed);
        options.SearchPathsAllConfigurations = false;
        options.SelectedConfiguration = "Release-Demo";
        Assert.True(options.SearchPathsDoNotInherit);
        Assert.Equal(["only"], options.SearchPaths.Select(r => r.Path));
        Assert.Empty(options.InheritedSearchPaths);
        options.SelectedConfiguration = "Profiling";
        Assert.False(options.SearchPathsDoNotInherit);
        Assert.Equal([("only", "Release-Demo")], Inherited(options));

        // A typed "!" is not a path: only the check box writes the marker.
        options.AddSearchPath("!");
        Assert.Equal(Strings.Msg_Project_LocationInvalid, options.SearchPathsError);
        options.RemoveSearchPath(0);
        Assert.Null(options.SearchPathsError);

        // Clearing the check box removes the marker and shows the inherited rows again.
        options.SelectedConfiguration = "Release-Demo";
        options.SearchPathsDoNotInherit = false;
        Assert.Equal([("lib", "Project"), ("rel", "Release")], Inherited(options));
        await options.Ok.PressAsync();
        Assert.Equal("""{"inherits":"Release","defines":["DEMO"],"searchPaths":["only"]}""", Saved(bed, "Release-Demo"));
        Assert.Equal(["lib", "rel", "only"], Effective(bed, "Release-Demo"));

        // A root configuration may discard the project-level entries too (CMP-1153).
        options = NewConfigurationTests.Options(bed);
        options.SearchPathsAllConfigurations = false;
        options.SelectedConfiguration = "Debug";
        Assert.Equal([("lib", "Project")], Inherited(options));
        options.SearchPathsDoNotInherit = true;
        await options.Ok.PressAsync();
        Assert.Empty(Effective(bed, "Debug"));
        Assert.Contains("\"searchPaths\":[\"!\"]", Saved(bed, "Debug"), StringComparison.Ordinal);
    });
}
