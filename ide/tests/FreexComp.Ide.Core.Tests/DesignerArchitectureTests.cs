using System.Reflection;
using System.Text.RegularExpressions;
using FreexComp.Designer.Surface;
using FreexComp.Ide.Core.Designer.CodeSync;
using FreexComp.Ide.Core.Tests.Designer;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests;

/// <summary>The designer's architecture and test rules (designer README.md §1, testing.md §1; D-087).</summary>
[Trait("Phase", "5")]
public sealed partial class DesignerArchitectureTests
{
    [Fact]
    [Trait("Req", "DSG-ARCH-002")]
    public void CodeSyncLivesInIdeCoreOverDesignerAndCompiler()
    {
        Assert.Equal("FreexComp.Ide.Core.Designer.CodeSync", typeof(CodeSyncService).Namespace);
        Assert.Equal("FreexComp.Ide.Core", typeof(CodeSyncService).Assembly.GetName().Name);
        Assert.True(typeof(FreexComp.Designer.CodeSync.ICodeSync).IsAssignableFrom(typeof(CodeSyncService)));

        // Core depends on the designer and on the compiler (syntax trees); the designer knows neither Core nor the buffer.
        var core = typeof(CodeSyncService).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.Contains("FreexComp.Designer", core);
        Assert.Contains("FreexComp.Compiler", core);
        Assert.DoesNotContain("FreexComp.Ide.Core", typeof(DesignSurfaceController).Assembly.GetReferencedAssemblies().Select(a => a.Name));
        var used = IlReferences.Read(typeof(CodeSyncService).Assembly.Location)
            .Where(r => r.UserNamespace == "FreexComp.Ide.Core.Designer.CodeSync")
            .Select(r => r.TargetType).ToHashSet(StringComparer.Ordinal);
        Assert.Contains(used, t => t.StartsWith("FreexComp.Compiler.", StringComparison.Ordinal));
        Assert.Contains(used, t => t.StartsWith("FreexComp.Ide.Core.Text.", StringComparison.Ordinal)); // the document text buffer
    }

    [Fact]
    [Trait("Req", "DSG-ARCH-003")]
    public void InteractionLogicIsDrivenByAbstractInputRecords()
    {
        var designer = typeof(DesignSurfaceController).Assembly;
        Assert.DoesNotContain(designer.GetReferencedAssemblies(), a => a.Name!.StartsWith("Avalonia", StringComparison.Ordinal));

        // The controller's whole input surface takes the abstract records (and plain values), no UI event types.
        var pointer = typeof(DesignSurfaceController).GetMethod(nameof(DesignSurfaceController.OnPointer))!;
        Assert.Equal([typeof(PointerInput)], pointer.GetParameters().Select(p => p.ParameterType));
        var key = typeof(DesignSurfaceController).GetMethod(nameof(DesignSurfaceController.OnKey))!;
        Assert.Equal([typeof(KeyInput)], key.GetParameters().Select(p => p.ParameterType));
        Assert.True(typeof(PointerInput).IsValueType && typeof(KeyInput).IsValueType);
        var publicParameters = typeof(DesignSurfaceController).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(m => m.GetParameters()).Select(p => p.ParameterType.Assembly.GetName().Name!);
        Assert.DoesNotContain(publicParameters, n => n.StartsWith("Avalonia", StringComparison.Ordinal));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-ARCH-004")]
    public Task FormFileIsTheOnlyStoreOfFormState() => TestUiThread.RunAsync(FormFileIsTheOnlyStoreOfFormStateOnUiThread);

    private static async Task FormFileIsTheOnlyStoreOfFormStateOnUiThread()
    {
        var f = new DesignerFixture();
        var document = await f.OpenAsync();
        var designer = document.Designer;

        // Select, move a control, collapse nothing: then save. Only the unit and the form are written in the folder.
        DesignerFixture.Select(designer, "OkButton");
        designer.Surface!.OnKey(new KeyInput(DesignKey.Right, InputModifiers.Ctrl));
        Assert.True(await f.Services.Documents.SaveAsync(document));
        Assert.Contains("    Left = 233\n", f.Disk(DesignerFixture.FormPath), StringComparison.Ordinal);
        var files = f.Memory.EnumerateFiles(DesignerFixture.Dir, "*").Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(["MainForm.form", "MainForm.pas"], files);

        // The selection is not stored in the form: the file is the original text with only the moved Left changed.
        Assert.Equal(MovedOkButtonForm, f.Disk(DesignerFixture.FormPath));
    }

    /// <summary><see cref="DesignerFixture.Form"/> with OkButton moved one pixel right.</summary>
    private static string MovedOkButtonForm => DesignerFixture.Form.Replace(
        "  object OkButton: TButton\n    Left = 232\n", "  object OkButton: TButton\n    Left = 233\n", StringComparison.Ordinal);

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-ARCH-004")]
    public Task SelectionIsSessionOnly() => TestUiThread.RunAsync(SelectionIsSessionOnlyOnUiThread);

    private static async Task SelectionIsSessionOnlyOnUiThread()
    {
        var f = new DesignerFixture();
        var document = await f.OpenAsync();
        DesignerFixture.Select(document.Designer, "OkButton");
        document.Designer.Surface!.OnKey(new KeyInput(DesignKey.Right, InputModifiers.Ctrl));
        Assert.True(await f.Services.Documents.SaveAsync(document));
        f.Services.Session.Save();

        // Neither the form nor the unit holds the selection; every other file written lies under the user directories.
        Assert.Equal(MovedOkButtonForm, f.Disk(DesignerFixture.FormPath));
        Assert.Equal(DesignerFixture.Unit, f.Disk(DesignerFixture.UnitPath));
        var paths = f.Services.UserPaths;
        var userDirs = new[] { paths.LocalDirectory, paths.RoamingDirectory }.Select(d => d.TrimEnd('\\') + "\\").ToList();
        bool InUserDir(string p) => userDirs.Exists(d => p.StartsWith(d, StringComparison.OrdinalIgnoreCase));
        var stray = f.Memory.AllFiles.Where(p => !string.Equals(p, DesignerFixture.UnitPath, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(p, DesignerFixture.FormPath, StringComparison.OrdinalIgnoreCase) && !InUserDir(p)).ToList();
        Assert.True(stray.Count == 0, string.Join(Environment.NewLine, stray));
        Assert.Contains(f.Memory.AllFiles, InUserDir); // the session was written, there

        // Without any session state the form still opens, with the form itself selected and the saved position.
        Assert.True(await f.Services.Documents.CloseAsync(document));
        foreach (var file in f.Memory.AllFiles.Where(InUserDir))
        {
            f.Memory.DeleteFile(file);
        }

        var reopened = await f.Services.Documents.OpenAsync(DesignerFixture.UnitPath);
        Assert.True(reopened.Success, reopened.MessageId);
        var form = Assert.IsType<FormDocumentViewModel>(reopened.Value);
        Assert.True(form.ShowView(DocumentView.Design));
        Assert.True(form.Designer.Surface!.Selection.IsFormOnly);
        Assert.Equal("233", DesignerFixture.Stored(form.Designer, "OkButton", "Left"));
    }

    [GeneratedRegex(@"Trait\(\s*""Req""\s*,\s*""(?:DSG-|UI-DSG-)")]
    private static partial Regex DesignerReq();

    [GeneratedRegex(@"Trait\(\s*""Phase""\s*,\s*""([0-9]+)""\s*\)")]
    private static partial Regex PhaseTrait();

    [Fact]
    [Trait("Req", "DSG-ARCH-006")]
    public void DesignerRequirementTestsArePhase5OrLater()
    {
        // Every designer rule is Phase 5 (unless tagged P8+): a test file referencing one tags it Phase 5 or later.
        var problems = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(SpecTables.RepoRootPath(), "ide", "tests"), "*.cs", SearchOption.AllDirectories))
        {
            var parts = file.Split(Path.DirectorySeparatorChar);
            if (parts.Contains("obj") || parts.Contains("bin"))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            if (!DesignerReq().IsMatch(text))
            {
                continue;
            }

            var phases = PhaseTrait().Matches(text).Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();
            if (!phases.Any(p => p >= 5))
            {
                problems.Add($"{Path.GetFileName(file)}: designer requirement without a Phase 5+ trait");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    [Trait("Req", "DSG-TEST-004")]
    public void DesignerCodeUsesNoWallClock()
    {
        // The designer takes an injected TimeProvider (nudge merging, DSG-SUR-107); no direct clock reads or timed waits.
        var clock = IlReferences.Read(typeof(DesignSurfaceController).Assembly.Location)
            .Where(r => (r.TargetType, r.Member) is ("System.DateTime", "get_Now") or ("System.DateTime", "get_UtcNow")
                or ("System.DateTimeOffset", "get_Now") or ("System.DateTimeOffset", "get_UtcNow") or ("System.Environment", "get_TickCount")
                or ("System.Environment", "get_TickCount64") or ("System.Threading.Tasks.Task", "Delay") or ("System.Threading.Thread", "Sleep")
                || r.TargetType == "System.Diagnostics.Stopwatch")
            .Select(r => $"{r.UserType} uses {r.TargetType}.{r.Member}").Distinct().ToList();
        Assert.True(clock.Count == 0, string.Join(Environment.NewLine, clock));
        Assert.Contains(typeof(DesignSurfaceController).GetConstructors().SelectMany(c => c.GetParameters()), p => p.ParameterType == typeof(TimeProvider));

        // The Object Inspector's filter delay (DSG-OI-021) runs on the IDE clock, which tests replace by a manual one.
        Assert.IsType<ManualClock>(new TestCompositionOptions().Clock);
    }

    [Fact]
    [Trait("Req", "DSG-TEST-010")]
    public void TestDataKeepsItsBytes()
    {
        var attributes = File.ReadAllLines(Path.Combine(SpecTables.RepoRootPath(), ".gitattributes")).Select(l => l.Trim());
        Assert.Contains("ide/tests/TestData/** -text", attributes);

        // A golden unit with CR LF line ends is compared byte-exact: the CR are really in the file.
        var golden = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "units", "MainForm.pas"));
        Assert.Contains((byte)'\r', golden);
    }
}
