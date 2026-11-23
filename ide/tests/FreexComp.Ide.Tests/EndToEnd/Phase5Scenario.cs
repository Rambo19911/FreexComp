using System.Text;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using FreexComp.Designer.Model;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Designer;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.EndToEnd;

/// <summary>
/// The Phase 5 scenario (layout-engine.md DSG-LAY-063, testing.md §5.4 <c>E2E_Phase5_DesignSaveCompile_LayoutIdentical</c>)
/// through the headless harness with the real build engine, the real toolchain and the real framework metadata: a new
/// <c>gui-pascal</c> project, controls placed and edited on the design surface and in the Object Inspector, the form resized
/// by its handle, saved, built, run with <c>--dump-layout</c>, and the runtime dump compared with the designer's layout.
/// </summary>
internal static class Phase5Scenario
{
    public const string ProjectName = "Designed";

    public const string FormName = "MainForm";

    private const string UnitFile = FormName + ".pas";

    public static string ProjectDir(HeadlessHarness h) => Path.Combine(h.TempDir, ProjectName);

    public static string UnitPath(HeadlessHarness h) => Path.Combine(ProjectDir(h), UnitFile);

    public static string FormPath(HeadlessHarness h) => Path.Combine(ProjectDir(h), FormName + ".form");

    /// <summary>
    /// A harness at the product's phase with the real build (<see cref="HarnessOptions.RealBuild"/>) and the real framework
    /// metadata (setting <c>toolchain.frameworkDesignDir</c>, DSG-TP-001 step 1); <paramref name="files"/> are written under
    /// <see cref="HeadlessHarness.TempDir"/> before start. Fails when the toolchain is missing (testing.md rule 5).
    /// </summary>
    public static async Task<HeadlessHarness> StartAsync(Action<HarnessOptions>? configure = null)
    {
        var h = await HeadlessHarness.StartAsync(o =>
        {
            o.RealBuild = true;
            o.ComponentMetadata = false;
            o.Settings["toolchain.frameworkDesignDir"] = LayoutRoundTrip.FrameworkDesignDir;
            configure?.Invoke(o);
        });
        var toolchain = h.Services.Toolchain.Resolve();
        if (toolchain.LlvmBinDir is null || toolchain.RtlDir is null || toolchain.FrameworkDir is null)
            Assert.Fail("LLVM toolchain, RTL or framework not found (run scripts/bootstrap.ps1)"); // testing.md: fails, never skips
        Assert.Equal(MetadataLoadState.Loaded, h.Services.Metadata!.State);
        return h;
    }

    /// <summary>File › New › Project with the <c>gui-pascal</c> template, then the form unit in the Design view.</summary>
    public static async Task<Session> CreateProjectAndDesignAsync(HeadlessHarness h)
    {
        Assert.Equal(NewProjectOutcome.Created, await h.Services.Projects.CreateProjectAsync(new NewProjectRequest("gui-pascal", ProjectName, h.TempDir)));
        return await OpenDesignAsync(h, UnitPath(h));
    }

    /// <summary>Opens <paramref name="unitPath"/> and switches to its Design view by the view tab.</summary>
    public static async Task<Session> OpenDesignAsync(HeadlessHarness h, string unitPath)
    {
        var document = Assert.IsType<FormDocumentViewModel>((await h.Services.Documents.OpenAsync(unitPath)).Value);
        Dispatcher.UIThread.RunJobs();
        h.Click(AutomationIds.DocumentViewTab(Path.GetFileName(unitPath), "Design"));
        Assert.Equal(DocumentView.Design, document.ActiveView);
        var formName = document.Designer.Model!.Root.Name;
        return new Session(h, document, h.Find<DesignSurfaceControl>(AutomationIds.Designer(formName)));
    }

    /// <summary>
    /// DSG-LAY-063: a <c>TPanel</c> (<c>alTop</c>), a <c>TLabel</c> (default <c>AutoSize</c>, caption edited), a <c>TEdit</c>,
    /// a <c>TComboBox</c>, a <c>TButton</c> anchored bottom-right, a <c>TGroupBox</c> holding two <c>TCheckBox</c>es; the form
    /// resized by its SE handle; with <paramref name="handler"/> the button's default handler is created by a double-click
    /// (code sync through <c>SyntaxEdits</c>). Asserts the model after each step.
    /// </summary>
    public static async Task DesignFormAsync(Session s, bool handler)
    {
        var h = s.H;
        await h.ExecuteAsync("view.toolPalette");

        s.Place("TPanel", 8, 8);
        s.Inspect("Align", "alTop");
        Assert.Equal("alTop", ((IdentValue)s.Object("Panel1").FindProperty("Align")!.Value).Name);

        s.Place("TLabel", 16, 64);
        s.Inspect("Caption", "Your name:");
        Assert.Equal(new StringValue("Your name:"), s.Object("Label1").FindProperty("Caption")!.Value);
        Assert.Null(s.Object("Label1").FindProperty("AutoSize")); // the default (True) stays unstored

        s.Place("TEdit", 96, 60);
        s.Place("TComboBox", 96, 96);
        s.Place("TButton", 296, 248);
        s.Inspect("Anchors", "[akRight, akBottom]");
        Assert.Contains("Anchors = [akRight, akBottom]", s.Document.Designer.CurrentFormText(), StringComparison.Ordinal);

        s.Place("TGroupBox", 16, 136);
        var group = s.Surface.Geometry.GetBounds(s.Object("GroupBox1"));
        s.PlaceAt("TCheckBox", group.Left + 12, group.Top + 20);
        s.PlaceAt("TCheckBox", group.Left + 12, group.Top + 52);
        Assert.Equal(["CheckBox1", "CheckBox2"], s.Object("GroupBox1").Children.Select(c => c.Name));

        // The form selected by a click on its empty client area, then resized by dragging its SE handle.
        var client = s.Surface.Geometry.FormClientRect;
        s.Click(client.Right - 4, client.Bottom - 4);
        Assert.Equal([FormName], s.Selected);
        var (width, height) = (s.Prop(FormName, "ClientWidth"), s.Prop(FormName, "ClientHeight"));
        var buttonLeft = s.Prop("Button1", "Left");
        var frame = s.Surface.Geometry.FrameRect;
        var (x, y) = (frame.Right - 1, frame.Bottom - 1);
        s.Down(x, y);
        s.Move(x + 30, y + 20);
        s.Move(x + 64, y + 40);
        s.Up(x + 64, y + 40);
        Assert.True(s.Prop(FormName, "ClientWidth") > width && s.Prop(FormName, "ClientHeight") > height,
            $"form not resized: {s.Prop(FormName, "ClientWidth")} x {s.Prop(FormName, "ClientHeight")}");
        Assert.True(s.Prop("Button1", "Left") > buttonLeft, "the bottom-right anchored button did not follow the resize");

        if (handler)
        {
            var (bx, by) = s.Center("Button1");
            s.DoubleClick(bx, by);
            Assert.Equal(DocumentView.Code, s.Document.ActiveView);
            Assert.Contains("procedure TMainForm.Button1Click(Sender: TObject);\r\nbegin\r\n", s.Document.Buffer.Text, StringComparison.Ordinal);
            h.Press("F7");
            Assert.Equal(DocumentView.Design, s.Document.ActiveView);
        }
    }

    /// <summary>Ctrl+S on the form document: the unit and the canonical <c>.form</c> are written.</summary>
    public static async Task SaveAsync(Session s)
    {
        await s.H.ExecuteAsync("file.save");
        await s.H.Services.FileChanges.WhenIdle();
        Dispatcher.UIThread.RunJobs();
        Assert.False(s.Document.IsModified);
    }

    /// <summary>Ctrl+Shift+B with the real engine; asserts no error. Returns the exe.</summary>
    public static async Task<string> BuildAsync(HeadlessHarness h)
    {
        var states = new List<IdeState>();
        h.Services.State.Changed += (_, _) => states.Add(h.Services.State.Current.State);
        h.Press("Ctrl+Shift+B");
        await h.WaitForAsync(() => states.Contains(IdeState.Building) && h.Services.State.Current.State == IdeState.Idle, "build finished");
        Dispatcher.UIThread.RunJobs();
        Assert.True(h.Services.Messages.ErrorCount == 0,
            "Build errors: " + string.Join(" | ", h.Services.Messages.Items.Select(i => $"{i.Code} {i.Location} {i.Text}")));
        var exe = Path.Combine(ProjectDir(h), "bin", "Debug", ProjectName + ".exe");
        Assert.True(File.Exists(exe), "no exe: " + exe);
        return exe;
    }

    /// <summary>The whole round trip: design, save, build, dump; the runtime layout equals the designer's.</summary>
    public static async Task DesignSaveBuildCompareAsync(HeadlessHarness h, bool handler)
    {
        var s = await CreateProjectAndDesignAsync(h);
        await DesignFormAsync(s, handler);
        await SaveAsync(s);
        var formText = File.ReadAllText(FormPath(h), Encoding.UTF8);
        Assert.Contains("object Panel1: TPanel", formText, StringComparison.Ordinal);
        Assert.Contains("    object CheckBox2: TCheckBox", formText, StringComparison.Ordinal);
        if (handler)
            Assert.Contains("OnClick = Button1Click", formText, StringComparison.Ordinal);

        var exe = await BuildAsync(h);
        var runtime = await LayoutRoundTrip.RunDumpAsync(exe, h.TempDir);
        Assert.Equal([FormName], runtime.Forms.Select(f => f.Name));
        var designer = LayoutRoundTrip.DesignerDump(ProjectDir(h), runtime);
        LayoutRoundTrip.AssertEqual(designer, runtime, ProjectName);
    }

    /// <summary>The surface of an open Design view, driven by pointer and keyboard.</summary>
    internal sealed record Session(HeadlessHarness H, FormDocumentViewModel Document, DesignSurfaceControl Control)
    {
        public FreexComp.Designer.Surface.DesignSurfaceController Surface => Document.Designer.Surface!;

        public FormObject Object(string name) => Document.Designer.Model!.FindByName(name)
            ?? throw new InvalidOperationException("no component " + name);

        public int Prop(string name, string property) =>
            (int)((IntegerValue)PropertyAccess.GetEffective(Object(name), property)).Value;

        public IReadOnlyList<string> Selected => [.. Surface.Selection.Items.Select(o => o.Name)];

        public (double X, double Y) Center(string name)
        {
            var b = Surface.Geometry.GetBounds(Object(name));
            return (b.Left + b.Width / 2.0, b.Top + b.Height / 2.0);
        }

        /// <summary>Arms <paramref name="className"/> by a click on its Tool Palette item, then clicks the form client point.</summary>
        public void Place(string className, double clientX, double clientY)
        {
            var client = Surface.Geometry.FormClientRect;
            PlaceAt(className, client.Left + clientX, client.Top + clientY);
        }

        /// <summary>Arms <paramref name="className"/> on the palette (search, click on the item), then clicks the surface point; the new component is selected.</summary>
        public void PlaceAt(string className, double x, double y)
        {
            var before = Document.Designer.Model!.AllObjects().Count();
            H.Press("Alt+4"); // the palette's search box: filtered, the item is in view
            H.Type(className[1..]);
            H.Click("Tool.ToolPalette.Item." + className);
            Assert.Equal(className, Document.Designer.Host.ArmedPaletteClass);
            Click(x, y);
            Assert.Equal(before + 1, Document.Designer.Model!.AllObjects().Count());
            Assert.Equal(className, Assert.Single(Surface.Selection.Items).TypeName);
        }

        /// <summary>F4, the row <paramref name="path"/> of the selected component, the value typed, Enter (DSG-OI-032/033).</summary>
        public void Inspect(string path, string value)
        {
            H.Press("F4");
            H.Press("Home");
            for (var i = 0; i < 300 && CurrentRow() != path; i++)
                H.Press("Down");
            Assert.Equal(path, CurrentRow());
            H.Type(value); // a printable character enters edit mode with that character (DSG-OI-032)
            H.Press("Enter");
            Assert.Null(H.TryFind<Avalonia.Controls.TextBox>("Tool.ObjectInspector.Row." + path + ".Editor"));
        }

        private string? CurrentRow() =>
            H.FindAll("Tool.ObjectInspector.Row.").FirstOrDefault(c => c.Classes.Contains("current")) is { } row
                ? AutomationIds.Get(row)!["Tool.ObjectInspector.Row.".Length..]
                : null;

        public Point At(double x, double y) => Control.TranslatePoint(new Point(x, y), H.Window)!.Value;

        public void Down(double x, double y) => Run(() => H.Window.MouseDown(At(x, y), MouseButton.Left));

        public void Move(double x, double y) => Run(() => H.Window.MouseMove(At(x, y)));

        public void Up(double x, double y) => Run(() => H.Window.MouseUp(At(x, y), MouseButton.Left));

        public void Click(double x, double y)
        {
            Down(x, y);
            Up(x, y);
        }

        /// <summary>A left double-click (the second press may replace the surface by the Code view).</summary>
        public void DoubleClick(double x, double y)
        {
            var at = At(x, y);
            for (var i = 0; i < 2; i++)
            {
                H.Window.MouseDown(at, MouseButton.Left);
                H.Window.MouseUp(at, MouseButton.Left);
            }

            Dispatcher.UIThread.RunJobs();
        }

        private static void Run(Action action)
        {
            action();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
