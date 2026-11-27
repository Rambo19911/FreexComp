using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Designer;
using FreexComp.Ide.Platform.Windows;
using FreexComp.Ide.TestSupport;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

[Trait("Kind", "Ui")]
public sealed partial class HighDpiTests
{
    [AvaloniaTheory(Timeout = 30000)]
    [Trait("Phase", "1")]
    [Trait("Req", "UI-TOK-062")]
    [Trait("Req", "UI-TOK-061")]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public async Task MainWindowLaysOutAtScales(double scale)
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.Scale = scale);

        Assert.Equal(scale, h.Window.RenderScaling);
        Assert.True(h.Window.UseLayoutRounding);
        AssertNoClippedText(h.Window);
    }

    [AvaloniaTheory(Timeout = 30000)]
    [Trait("Phase", "1")]
    [Trait("Req", "UI-WIN-002")]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public async Task MinimumWindowKeepsRegionMinimums(double scale)
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.Scale = scale;
            o.WindowSize = new Size(800, 500);
        });

        Assert.Equal(800, h.Window.MinWidth); // main-window.md §7
        Assert.Equal(500, h.Window.MinHeight);
        var area = h.Find<Control>(AutomationIds.DocumentArea);
        Assert.True(area.Bounds.Width >= 320 && area.Bounds.Height >= 160, $"document area {area.Bounds.Size}");
        Assert.Equal(24, h.Find<Control>(AutomationIds.StatusBar).Bounds.Height);
        Assert.Equal(28, h.Find<Control>(AutomationIds.MenuBar).Bounds.Height);
        AssertNoClippedText(h.Window);
    }

    private static void AssertNoClippedText(Window window)
    {
        var texts = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).ToList();
        Assert.NotEmpty(texts);
        foreach (var t in texts.Where(t => t.TextTrimming == TextTrimming.None))
        {
            var width = t.DesiredSize.Width - t.Margin.Left - t.Margin.Right;
            Assert.True(width <= t.Bounds.Width + 0.01, $"'{t.Text}' clipped: desired {width}, bounds {t.Bounds.Width}");
        }
    }

    // ---- F11-007 (I-335): application manifest, process awareness, raster assets, scale change ----

    /// <summary>The IDE executable the UI tests run next to (the build output of the IDE project, copied by the reference).</summary>
    private static string IdeExecutable()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "FreexComp.Ide.exe");
        Assert.True(File.Exists(exe), exe);
        return exe;
    }

    /// <summary>
    /// The application manifest embedded in the IDE executable (resource type 24, id 1) declares per-monitor-v2 DPI
    /// awareness in both forms the operating system reads, Windows 10/11 as the supported system and the invoker's
    /// execution level — and nothing else (no side-by-side dependency, no other window setting).
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-TOK-066")]
    public void ManifestDeclaresPerMonitorV2()
    {
        XNamespace asm1 = "urn:schemas-microsoft-com:asm.v1";
        XNamespace asm3 = "urn:schemas-microsoft-com:asm.v3";
        XNamespace compatibility = "urn:schemas-microsoft-com:compatibility.v1";
        XNamespace settings2005 = "http://schemas.microsoft.com/SMI/2005/WindowsSettings";
        XNamespace settings2016 = "http://schemas.microsoft.com/SMI/2016/WindowsSettings";

        var manifest = ReadManifest(IdeExecutable());
        var text = manifest.ToString();

        Assert.Equal(asm1 + "assembly", manifest.Root!.Name);
        var settings = Assert.Single(manifest.Descendants(asm3 + "windowsSettings"));
        Assert.Equal([settings2005 + "dpiAware", settings2016 + "dpiAwareness"], settings.Elements().Select(e => e.Name));
        Assert.Equal("true/pm", settings.Element(settings2005 + "dpiAware")!.Value.Trim());
        Assert.Equal("PerMonitorV2, PerMonitor", settings.Element(settings2016 + "dpiAwareness")!.Value.Trim());
        Assert.Equal(["{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}"], manifest.Descendants(compatibility + "supportedOS").Select(e => (string?)e.Attribute("Id")));
        Assert.Equal(["asInvoker"], manifest.Descendants(asm3 + "requestedExecutionLevel").Select(e => (string?)e.Attribute("level")));
        Assert.True(!manifest.Descendants(asm1 + "dependency").Any(), text); // no common-controls dependency
        Assert.DoesNotContain("longPathAware", text, StringComparison.OrdinalIgnoreCase);
    }

    private const uint LoadLibraryAsDataFile = 0x00000002;
    private const int ManifestResourceType = 24; // RT_MANIFEST
    private const int ApplicationManifestId = 1;

    private static XDocument ReadManifest(string executable)
    {
        var module = LoadLibraryExW(executable, IntPtr.Zero, LoadLibraryAsDataFile);
        Assert.True(module != IntPtr.Zero, $"{executable}: not loadable as a data file (error {Marshal.GetLastPInvokeError()})");
        try
        {
            var resource = FindResourceW(module, ApplicationManifestId, ManifestResourceType);
            Assert.True(resource != IntPtr.Zero, executable + ": no application manifest resource (type 24, id 1)");
            var bytes = new byte[SizeofResource(module, resource)];
            Marshal.Copy(LockResource(LoadResource(module, resource)), bytes, 0, bytes.Length);
            using var stream = new MemoryStream(bytes);
            return XDocument.Load(stream);
        }
        finally
        {
            FreeLibrary(module);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr LoadLibraryExW(string fileName, IntPtr file, uint flags);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr FindResourceW(IntPtr module, IntPtr name, IntPtr type);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int SizeofResource(IntPtr module, IntPtr resource);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr LockResource(IntPtr data);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int FreeLibrary(IntPtr module);

    /// <summary>
    /// F11-007 gap (e): the built IDE started with <c>--smoke-dpi</c> prints the DPI awareness its process has before
    /// anything of the UI library ran — the one the application manifest gave it —, shows no window and exits with 0.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Category", "Integration")]
    [Trait("Req", "UI-TOK-067")]
    [Trait("Req", "UI-TOK-060")]
    public async Task ProcessIsPerMonitorV2()
    {
        using var temp = new TempDirectory();
        var start = new ProcessStartInfo(IdeExecutable())
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = temp.Path,
        };
        foreach (var argument in (string[])["--user-dir", temp.Combine("user"), "--smoke-dpi"])
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = ChildProcessOutput.Read(process); // I-262: dedicated threads, not the pool
        try
        {
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true); // the test's deadline: no IDE is left behind
            throw;
        }

        Assert.Equal(("per-monitor-v2" + Environment.NewLine, "", 0), (await output.StandardOutput, await output.StandardError, process.ExitCode));
        Assert.False(Directory.Exists(temp.Combine("user")), "the smoke run starts nothing of the IDE: no user directory");
    }

    /// <summary>
    /// The start-up log (IDE-ARCH-015 step 3) gets exactly one line <c>dpi awareness: &lt;value&gt;</c> with the awareness
    /// of the thread that starts the IDE — here the test host's, whatever it is.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Category", "Integration")]
    [Trait("Req", "UI-TOK-067")]
    public void StartupLogsDpiAwareness()
    {
        using var temp = new TempDirectory();
        var window = new Window();
        var platform = CompositionRoot.CreatePlatform(StartupOptions.Parse(["--user-dir", temp.Path]), () => window);
        (platform.Log as IDisposable)?.Dispose(); // every entry has reached the file

        var value = DpiAwareness.Current;
        Assert.Contains(value, (string[])["per-monitor-v2", "per-monitor", "system", "unaware", "unaware-gdi-scaled"]);
        var lines = Directory.EnumerateFiles(platform.UserPaths.LogDirectory, "*.log").SelectMany(File.ReadAllLines)
            .Where(l => l.Contains("dpi awareness", StringComparison.Ordinal)).ToList();
        Assert.EndsWith(" INFO startup: dpi awareness: " + value, Assert.Single(lines), StringComparison.Ordinal);
    }

    [GeneratedRegex(@"[^\s""'<>()=]*\.(?:png|jpe?g|bmp|gif|ico)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RasterReference();

    /// <summary>
    /// F11-007 acceptance 2: the views of the IDE and of the designer name no raster image — every icon is vector
    /// geometry — except the brand icon <c>Assets/app.ico</c> (BR-001) in its three places: the main window's icon, the
    /// About dialog and the Welcome page's logo. A new raster reference fails here.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-TOK-068")]
    public void NoRasterAssetsInViews()
    {
        var source = Path.Combine(SpecTables.RepoRootPath(), "ide", "src");
        var hits = new List<string>();
        var scanned = 0;
        foreach (var project in (string[])["FreexComp.Ide", "FreexComp.Designer"])
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(source, project), "*", SearchOption.AllDirectories)
                .Where(f => Path.GetExtension(f) is ".axaml" or ".cs")
                .Where(f => !Path.GetRelativePath(source, f).Split(Path.DirectorySeparatorChar).Any(p => p is "obj" or "bin"))
                .Order(StringComparer.Ordinal))
            {
                scanned++;
                var name = Path.GetRelativePath(source, file).Replace(Path.DirectorySeparatorChar, '/');
                hits.AddRange(File.ReadLines(file).SelectMany(line => RasterReference().Matches(line).Select(m => $"{name}: {m.Value} in: {line.Trim()}")));
            }
        }

        Assert.True(scanned > 100, $"only {scanned} view sources seen below {source}");
        const string icon = "avares://FreexComp.Ide/Assets/app.ico";
        Assert.Collection(hits,
            hit => Assert.StartsWith($"FreexComp.Ide/MainWindow.axaml: {icon} in: Icon=\"{icon}\"", hit, StringComparison.Ordinal), // the window icon
            hit => Assert.StartsWith($"FreexComp.Ide/Views/AboutWindow.cs: {icon} in: using var stream = AssetLoader.Open(", hit, StringComparison.Ordinal),
            hit => Assert.StartsWith($"FreexComp.Ide/Views/WelcomeDocumentView.cs: {icon} in: using var icon = AssetLoader.Open(", hit, StringComparison.Ordinal));
    }

    /// <summary>Counts the draws of an editor's text view (a background renderer is drawn with every render of the view).</summary>
    private sealed class RenderCounter : IBackgroundRenderer
    {
        public int Count { get; private set; }

        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext drawingContext) => Count++;
    }

    /// <summary>The window as the user sees it now: layout done and one frame rendered.</summary>
    private static void RenderFrame()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// A window that comes to a monitor with another scale (here: the headless window's scale set to 150 % and back) is
    /// drawn again at the new scale without a restart: the design surface and — in the Code view — the editor each
    /// render once per change, and nothing of the user directory is written (UI-TOK-064).
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-TOK-069")]
    [Trait("Req", "UI-TOK-064")]
    public async Task ScaleChangeReRenders()
    {
        await using var h = await HeadlessHarness.StartAsync(DesignerViewTests.Files);
        var document = await DesignerViewTests.OpenFormAsync(h);
        var userFiles = UserFiles(h.UserDir);

        h.Click(AutomationIds.DocumentViewTab("MainForm.pas", "Design"));
        Assert.Equal(DocumentView.Design, document.ActiveView);
        var surface = h.Find<DesignSurfaceControl>(AutomationIds.Designer("MainForm"));
        RenderFrame();
        Assert.True(surface.RenderCount > 0, "the design surface was drawn");
        foreach (var scale in (double[])[1.5, 1.0])
        {
            var before = surface.RenderCount;
            RenderFrame();
            Assert.Equal(before, surface.RenderCount); // a frame without a change draws nothing again
            h.Window.SetRenderScaling(scale);
            RenderFrame();
            Assert.Equal(scale, h.Window.RenderScaling);
            Assert.Equal(before + 1, surface.RenderCount);
            RenderFrame();
            Assert.Equal(before + 1, surface.RenderCount); // drawn once, not on every frame
        }

        h.Click(AutomationIds.DocumentViewTab("MainForm.pas", "Code"));
        Assert.Equal(DocumentView.Code, document.ActiveView);
        var editor = new RenderCounter();
        h.Find<TextEditor>(AutomationIds.Editor("MainForm.pas")).TextArea.TextView.BackgroundRenderers.Add(editor);
        RenderFrame();
        Assert.True(editor.Count > 0, "the editor was drawn");
        foreach (var scale in (double[])[1.5, 1.0])
        {
            var before = editor.Count;
            RenderFrame();
            Assert.Equal(before, editor.Count);
            h.Window.SetRenderScaling(scale);
            RenderFrame();
            Assert.Equal(scale, h.Window.RenderScaling);
            Assert.Equal(before + 1, editor.Count);
            RenderFrame();
            Assert.Equal(before + 1, editor.Count);
        }

        Assert.Equal(userFiles, UserFiles(h.UserDir));
    }

    /// <summary>Every file below the user directory with its content.</summary>
    private static SortedDictionary<string, string> UserFiles(string userDir) => new(
        Directory.EnumerateFiles(userDir, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(userDir, f), f => Convert.ToHexString(File.ReadAllBytes(f)), StringComparer.Ordinal),
        StringComparer.Ordinal);
}
