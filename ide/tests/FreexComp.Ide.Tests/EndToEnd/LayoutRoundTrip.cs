using System.Diagnostics;
using System.Text;
using FreexComp.Build;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.Layout;
using FreexComp.Designer.Rendering;
using FreexComp.Designer.Text;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Designer.CodeSync;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests.EndToEnd;

/// <summary>
/// The procedure of layout-engine.md DSG-LAY-061: build a gui project with the real build engine (Debug), run the exe with
/// <c>--dump-layout</c>, and lay out every form of the runtime dump with the designer's engine (run-time mode, sidecar
/// <c>.steps</c>) over the real framework metadata; the two documents are compared per DSG-LAY-054.
/// </summary>
internal static class LayoutRoundTrip
{
    /// <summary>Step (2): the exe must exit within this time with code 0.</summary>
    public static readonly TimeSpan DumpTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// The catalog the IDE loads in this checkout: its metadata service over the real files, with the automatic folder
    /// search from the test assembly's folder — the framework root, then the data-access root (DSG-TP-001, DSG-TP-003).
    /// </summary>
    private static readonly Lazy<ComponentCatalog> IdeCatalog = new(() =>
    {
        var log = new RecordingLog();
        FreexComp.Ide.Core.Designer.ComponentMetadataService? metadata = null;
        TestUiThread.RunAsync(async () => // its own UI thread: the load's results are applied there, whatever thread asks first
        {
            metadata = Compose(log).Metadata;
            Assert.NotNull(metadata);
            await metadata.LoadAsync();
        }).GetAwaiter().GetResult();
        Assert.NotNull(metadata);
        Assert.True(metadata.State == FreexComp.Ide.Core.Designer.MetadataLoadState.Loaded,
            $"component metadata not loaded ({metadata.State}): {metadata.MissingIndexPath ?? metadata.FailureReason}");
        Assert.DoesNotContain(log.Lines, line => line.StartsWith("WARN", StringComparison.Ordinal) || line.StartsWith("ERROR", StringComparison.Ordinal));
        return metadata.Catalog;
    });

    public static string RepoRoot => SpecTables.RepoRootPath();

    public static string FrameworkDir => Path.Combine(RepoRoot, "ui-framework");

    public static string FrameworkDesignDir => Path.Combine(FrameworkDir, "design");

    public static ComponentCatalog Catalog => IdeCatalog.Value;

    /// <summary>
    /// Step (1): builds <paramref name="projectPath"/> (Debug) with the real engine; the LLVM folder and the RTL come from
    /// the IDE's automatic search from the test assembly's folder (IDE-SET-050), the framework and data-access sources are extra search paths as the IDE passes them (IDE-P3-010). Fails (never
    /// skips, testing.md rule 5) when the toolchain is missing or the build reports errors. Returns the exe.
    /// </summary>
    public static async Task<string> BuildAsync(string projectPath)
    {
        var toolchain = Toolchain();
        var engine = new BuildEngine(FreexComp.Compiler.PhysicalFileSystem.Instance);
        var request = new BuildRequest
        {
            ProjectPath = projectPath,
            Configuration = "Debug",
            ToolchainDirectory = toolchain.LlvmBinDir,
            RtlDirectory = toolchain.RtlDir,
            ExtraSearchPaths = [.. new[] { FrameworkDir, toolchain.DataDir }.OfType<string>().Select(dir => Path.Combine(dir, "src"))],
        };
        var result = await CurrentTest.Deadline.WaitAsync(
            engine.RunAsync(request, new SyncProgress(_ => { }), CurrentTest.Deadline.Token),
            "build of " + Path.GetFileName(projectPath));
        var diagnostics = result.Projects.SelectMany(p => p.Diagnostics)
            .Where(d => d.Severity == FreexComp.Compiler.DiagnosticSeverity.Error).Select(d => d.ToString()).ToList();
        Assert.True(result.Success && result.OutputPath is not null && File.Exists(result.OutputPath),
            $"build of {projectPath} failed (run scripts/bootstrap.ps1 when the toolchain is missing):\n" + string.Join("\n", diagnostics));
        return result.OutputPath!;
    }

    /// <summary>The IDE's automatic toolchain search from the test assembly's folder inside the checkout (IDE-SET-050).</summary>
    public static ToolchainPaths Toolchain()
    {
        var paths = Compose(new RecordingLog()).Toolchain.Resolve();
        if (paths.LlvmBinDir is null || paths.RtlDir is null)
            Assert.Fail("LLVM toolchain not found (run scripts/bootstrap.ps1)"); // testing.md rule 5: fails, never skips
        return paths;
    }

    /// <summary>The service graph over the real file system, searching from the test assembly's folder inside the checkout.</summary>
    private static FreexComp.Ide.Core.Composition.IdeServices Compose(RecordingLog log) => TestComposition.Build(o =>
    {
        o.FileSystem = new FreexComp.Ide.Core.Platform.PhysicalFileSystem();
        o.ToolchainEnvironment = new ToolchainEnvironment(null, AppContext.BaseDirectory, []);
        o.Log = log;
    });

    /// <summary>
    /// Step (2): runs <paramref name="exe"/> with <c>--dump-layout &lt;dir&gt;/runtime.json</c>; asserts exit code 0 within
    /// <see cref="DumpTimeout"/>. The process (and its tree) is killed in every case before returning.
    /// </summary>
    public static async Task<LayoutDumpDocument> RunDumpAsync(string exe, string workDir)
    {
        var dump = Path.Combine(workDir, "runtime.json");
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };
        psi.ArgumentList.Add("--dump-layout");
        psi.ArgumentList.Add(dump);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("could not start " + exe);
        try
        {
            var output = ChildProcessOutput.Read(process); // I-262: dedicated threads, not the pool
            var (stdout, stderr) = (output.StandardOutput, output.StandardError);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(CurrentTest.Deadline.Token);
            timeout.CancelAfter(DumpTimeout < CurrentTest.Deadline.Remaining ? DumpTimeout : CurrentTest.Deadline.Remaining);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail($"{Path.GetFileName(exe)} --dump-layout did not exit within {DumpTimeout.TotalSeconds:0} s");
            }

            Assert.True(process.ExitCode == 0, $"{Path.GetFileName(exe)} --dump-layout: exit code {process.ExitCode}\n--- stderr\n{await stderr}\n--- stdout\n{await stdout}");
            Assert.True(File.Exists(dump), "no dump written: " + dump);
            return LayoutDump.Parse(File.ReadAllText(dump, Encoding.UTF8));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
        }
    }

    /// <summary>
    /// Step (3): for every form of <paramref name="runtime"/>, in its order, the project's form file whose root object has
    /// that name, parsed and laid out in run-time mode with its <c>.steps</c> sidecar (else a single <c>dump</c>). Frame
    /// instances resolve with the frames of the folder's units, as the IDE's project frame catalog reads them (DSG-FD-103).
    /// </summary>
    public static LayoutDumpDocument DesignerDump(string projectDir, LayoutDumpDocument runtime)
    {
        var catalog = Catalog;
        var engine = new LayoutEngine(catalog, RendererRegistry.CreateDefault());
        var files = Directory.EnumerateFiles(projectDir, "*.form", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToList();
        var frames = new FreexComp.Ide.Core.Designer.ProjectFrameCatalog(new FreexComp.Ide.Core.Platform.PhysicalFileSystem(),
            () => [.. files.Where(f => File.Exists(Path.ChangeExtension(f, ".pas")))
                .Select(f => new FreexComp.Ide.Core.Designer.FrameSource(Path.ChangeExtension(f, ".pas"), f))],
            () => catalog);
        frames.Refresh();
        var forms = new Dictionary<string, (string Path, FreexComp.Designer.Model.FormDocument Document)>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var parsed = Parse(file, catalog, frames);
            Assert.True(parsed.Document is { Diagnostics.Count: 0 }, file + ":\n" + string.Join("\n", parsed.Diagnostics));
            forms[parsed.Document!.Root.Name] = (file, parsed.Document);
        }

        var result = new List<LayoutDumpForm>();
        foreach (var form in runtime.Forms)
        {
            Assert.True(forms.TryGetValue(form.Name, out var entry), $"no form file in {projectDir} has the root object '{form.Name}'");
            var stepsPath = entry.Path + ".steps";
            var steps = File.Exists(stepsPath) ? File.ReadAllText(stepsPath, Encoding.UTF8) : "dump\n";
            var dumps = engine.ComputeWithSteps(entry.Document, steps, LayoutMode.RunTime);
            result.Add(dumps[^1].ToDump());
        }

        return new LayoutDumpDocument(LayoutDump.FormatVersion, LayoutDump.Dpi, result);
    }

    /// <summary>
    /// Parses a form file as the designer does (FormDesignerViewModel): the root binds to the ancestor its unit declares
    /// (<c>class(TForm)</c>) when the catalog does not know the form class itself.
    /// </summary>
    private static FormParseResult Parse(string file, ComponentCatalog catalog, FreexComp.Designer.Model.IFrameCatalog frames)
    {
        var text = File.ReadAllText(file, Encoding.UTF8);
        var first = FormParser.Parse(text, file, catalog, null, frames);
        var unit = Path.ChangeExtension(file, ".pas");
        if (first.Document is not { } document || catalog.Find(document.Root.TypeName) is not null || !File.Exists(unit))
            return first;
        var ancestor = FormClassLocator.Locate(File.ReadAllText(unit, Encoding.UTF8), document.Root.TypeName, unit).FormClass?.AncestorName;
        return ancestor is null ? first : FormParser.Parse(text, file, catalog, ancestor, frames);
    }

    /// <summary>Step (4): equality per DSG-LAY-054; the message lists every difference and both documents.</summary>
    public static void AssertEqual(LayoutDumpDocument designer, LayoutDumpDocument runtime, string what)
    {
        var differences = LayoutDump.Compare(designer, runtime);
        Assert.True(differences.Count == 0, $"{what}: designer and runtime layouts differ:\n{string.Join("\n", differences)}\n"
            + $"--- designer\n{LayoutDump.Serialize(designer)}--- runtime\n{LayoutDump.Serialize(runtime)}");
    }

    /// <summary>Copies a folder (no <c>bin</c>/<c>obj</c>) into <paramref name="target"/>.</summary>
    public static void CopyFolder(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (relative.Split(Path.DirectorySeparatorChar)[0] is "bin" or "obj")
                continue;
            var to = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(file, to);
        }
    }

    /// <summary>Progress delivered on the engine's thread (no synchronization context capture).</summary>
    private sealed class SyncProgress(Action<BuildEvent> report) : IProgress<BuildEvent>
    {
        public void Report(BuildEvent value) => report(value);
    }
}
