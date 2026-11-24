using System.Text.Json;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests.EndToEnd;

/// <summary>
/// Designer ↔ runtime layout equality for the data-access CRUD sample (<c>data/samples/crud</c>, ADR 004): the procedure of
/// layout-engine.md DSG-LAY-061 with the component metadata and the unit search paths the IDE uses in Phase 7 (framework,
/// then data access). Shared by <see cref="DumpLayoutEqualityTests"/> and the cross-area test of the lead-owned E2E project.
/// </summary>
public static class DataLayoutScenario
{
    /// <summary>
    /// Copies the sample to a temporary folder, builds it (Debug), puts the database library beside the program (it opens
    /// its database at start-up), runs it with <c>--dump-layout</c> and compares every form of the runtime dump with the
    /// designer engine's run-time layout of the form file of that name. Nothing is written into the repository.
    /// </summary>
    public static async Task CrudFormEqualsRuntimeAsync()
    {
        using var temp = new TempDirectory();
        var source = Path.Combine(LayoutRoundTrip.RepoRoot, "data", "samples", "crud");
        Assert.True(File.Exists(Path.Combine(source, "Crud.pproj")), "sample project missing: " + Path.Combine(source, "Crud.pproj"));
        var projectDir = Path.Combine(temp.Path, "crud");
        LayoutRoundTrip.CopyFolder(source, projectDir);

        var exe = await LayoutRoundTrip.BuildAsync(Path.Combine(projectDir, "Crud.pproj"));
        var library = DatabaseLibrary();
        File.Copy(library, Path.Combine(Path.GetDirectoryName(exe)!, Path.GetFileName(library)), overwrite: true);
        var runtime = await LayoutRoundTrip.RunDumpAsync(exe, temp.Path);
        Assert.NotEmpty(runtime.Forms);
        var designer = LayoutRoundTrip.DesignerDump(projectDir, runtime);
        LayoutRoundTrip.AssertEqual(designer, runtime, "crud");

        // The dump is the whole form: its 13 controls (the three non-visual data components have no layout), data-aware ones included.
        var classes = Flatten(runtime.Forms[0].Controls).Select(c => c.Class).ToList();
        Assert.Equal(13, classes.Count);
        Assert.Contains("TDBGrid", classes);
        Assert.Contains("TDBNavigator", classes);
    }

    private static IEnumerable<FreexComp.Designer.Layout.LayoutDumpControl> Flatten(IEnumerable<FreexComp.Designer.Layout.LayoutDumpControl> controls) =>
        controls.SelectMany(c => Flatten(c.Controls).Prepend(c));

    /// <summary>The database library of the pinned toolchain (<c>scripts/toolchain.json</c> <c>sqlite.installDir</c>); a missing one fails.</summary>
    private static string DatabaseLibrary()
    {
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(LayoutRoundTrip.RepoRoot, "scripts", "toolchain.json")));
        var installDir = config.RootElement.GetProperty("sqlite").GetProperty("installDir").GetString()!;
        var library = Path.Combine(LayoutRoundTrip.RepoRoot, installDir.Replace('/', Path.DirectorySeparatorChar), "sqlite3.dll");
        Assert.True(File.Exists(library), "database library not found (run scripts/bootstrap.ps1): " + library);
        return library;
    }
}
