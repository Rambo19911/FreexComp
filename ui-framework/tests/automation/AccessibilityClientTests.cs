using FreexComp.UiFramework.Tests;

namespace FreexComp.UiFramework.Automation.Tests;

/// <summary>
/// U-155 (ui-automation.md §9): the providers of framework controls and the annotations of native controls as a UI
/// Automation client sees them: the operating system's COM client (<c>IUIAutomation</c>, the one screen readers use;
/// interop slice in <c>UiaClient.cs</c>, decision Q-P8A-16). Each test builds <c>tests/gui/AccessibilityProbe.pas</c>, starts it (its form is shown
/// without activation), finds elements by <c>AutomationId</c> (= component name) under the probe's window, and closes
/// the probe. The tests only read elements of the probe process: no mouse, no input to other windows, no focus.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Native")]
[Trait("Category", "Foreground")]
[Trait("Category", "Integration")]
[Trait("Category", "Toolchain")]
public sealed class AccessibilityClientTests
{
    private const uint WM_APP = 0x8000;
    private const uint WM_PROBE_RENAME = WM_APP + 1;
    private const uint WM_PROBE_CYCLE = WM_APP + 2;
    private const uint WM_PROBE_FREE = WM_APP + 3;
    private const uint WM_PROBE_COUNT = WM_APP + 4;

    public static string ProbeSource => Path.Combine(UiCorpus.AreaDirectory, "tests", "gui", "AccessibilityProbe.pas");

    /// <summary>
    /// One row per control of the probe: its component name, the control type of UIF-1417 (native controls: the
    /// type the system's provider reports, which the framework leaves alone unless <c>AccessibleRole</c> is set) and
    /// whether it is a content element (UIF-1419).
    /// </summary>
    public static readonly (string Id, int Type, bool Content)[] ProbeControls =
    [
        ("ProbeForm", UiaIds.Window, true),
        ("ProbeToolBar", UiaIds.ToolBar, true),
        ("PlainButton", UiaIds.Button, true),
        ("CheckButton", UiaIds.Button, true),
        ("NameLabel", UiaIds.Text, true),
        ("OptionsGroup", UiaIds.Group, true),
        ("InfoPanel", UiaIds.Pane, true),
        ("LogoImage", UiaIds.Image, true),
        ("ScrollArea", UiaIds.Pane, true),
        ("BoxLabel", UiaIds.Text, true),
        ("GeneralSheet", UiaIds.Pane, true),
        ("DataGrid", UiaIds.DataGrid, true),
        ("AddressFrame", UiaIds.Pane, true),
        ("StreetLabel", UiaIds.Text, true),
        ("SideSplitter", UiaIds.Thumb, false),
        ("NameEdit", UiaIds.Edit, true),
        ("NotesMemo", UiaIds.Edit, true),
        ("OkButton", UiaIds.Button, true),
        ("RememberCheck", UiaIds.CheckBox, true),
        ("OptionRadio", UiaIds.RadioButton, true),
        ("FruitList", UiaIds.List, true),
        ("ColourStatic", UiaIds.Text, true),
        ("ColourCombo", UiaIds.ComboBox, true),
        ("PositionScroll", UiaIds.ScrollBar, true),
        ("ProbePages", UiaIds.Tab, true),
        ("ProgressGauge", UiaIds.ProgressBar, true),
        ("VolumeTrack", UiaIds.Slider, true),
        ("FileList", UiaIds.List, true),
        ("FolderTree", UiaIds.Tree, true),
        ("ProbeStatus", UiaIds.StatusBar, true),
    ];

    /// <summary>Probe controls that are no elements of the tree (UIF-1419: shapes, bevels and tool bar separators).</summary>
    public static readonly string[] NotElements = ["DecorShape", "DecorBevel", "SeparatorButton"];

    /// <summary>
    /// Card F05-009 acceptance 1 (UIF-1411, UIF-1417, UIF-1419): every control element of the probe that is a content
    /// element has a non-empty name and the control type of UIF-1417; shapes, bevels and separators are no elements.
    /// </summary>
    [Fact]
    [Trait("Req", "UIF-1411")]
    [Trait("Req", "UIF-1417")]
    [Trait("Req", "UIF-1419")]
    public void Uia_EveryContentElementHasANameAndItsControlType()
    {
        Harness.RequireDesktop();
        using var probe = Probe.Start();
        var problems = new List<string>();
        foreach (var (id, type, content) in ProbeControls)
        {
            var e = probe.Find(id);
            if (e is null)
            {
                problems.Add($"{id}: not found by AutomationId");
                continue;
            }

            var isContent = e.CurrentIsContentElement != 0;
            if (isContent != content)
            {
                problems.Add($"{id}: IsContentElement {isContent}, expected {content}");
            }

            if (isContent && string.IsNullOrEmpty(e.CurrentName))
            {
                problems.Add($"{id}: a content element without a name");
            }

            if (e.CurrentControlType != type)
            {
                problems.Add($"{id}: control type {UiaIds.Name(e.CurrentControlType)}, expected {UiaIds.Name(type)}");
            }
        }

        foreach (var id in NotElements)
        {
            if (probe.Find(id) is not null)
            {
                problems.Add($"{id}: an element, expected none (UIF-1419)");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        probe.Close();
    }

    /// <summary>
    /// UIF-1414…1416: native controls keep the system's providers and carry the annotated name (only when it comes
    /// from AccessibleName or an associated label), help text and automation id; a change in the program re-annotates.
    /// Stop rule of UIF-1414: a failure of the name here is reported to the lead, not worked around.
    /// </summary>
    [Fact]
    [Trait("Req", "UIF-1414")]
    [Trait("Req", "UIF-1415")]
    [Trait("Req", "UIF-1416")]
    public void Uia_NativeControlsCarryNamesAndIds()
    {
        Harness.RequireDesktop();
        using var probe = Probe.Start();
        var (edit, list, button) = (probe.Find("NameEdit"), probe.Find("FruitList"), probe.Find("OkButton"));
        if (edit is null || list is null || button is null)
        {
            Assert.Fail($"by AutomationId: NameEdit {edit is not null}, FruitList {list is not null}, OkButton {button is not null}");
            return;
        }

        Assert.Equal("Win32", edit.CurrentFrameworkId);
        Assert.Equal(UiaIds.Edit, edit.CurrentControlType);
        Assert.Equal("Name:", edit.CurrentName);                        // only its associated label names it
        Assert.Equal("Fruit list", list.CurrentName);                   // AccessibleName
        Assert.Equal("OK", button.CurrentName);                         // its own text, from the system
        Assert.Equal("Confirms the dialog", button.CurrentHelpText);    // AccessibleDescription
        probe.Post(WM_PROBE_RENAME);
        Assert.True(probe.App.WaitForOutputLine("renamed"), probe.Output("the rename marker"));
        Assert.Equal("Renamed fruit list", list.CurrentName);
        Assert.Equal("Full name:", edit.CurrentName);                   // the caption of its label changed
        probe.Close();
    }

    /// <summary>
    /// Card F05-009 acceptance 4 (UIF-1425, UIF-1426): the probe opens and frees a second form 1 000 times; each tenth
    /// form is read by the client, which keeps the elements it took (the form, a panel and graphic controls) until
    /// the end. The GDI and USER object counts of the probe (bound of <c>HelloForm_NoGdiLeakOnResize</c>) and its count
    /// of live provider objects are the same before and after.
    /// </summary>
    [Fact]
    [Trait("Req", "UIF-1425")]
    [Trait("Req", "UIF-1426")]
    public void Uia_ProvidersReleasedOverThousandFormCycles()
    {
        Harness.RequireDesktop();
        using var probe = Probe.Start();
        var held = new List<IUIAutomationElement>();

        void Round(int unheld)
        {
            var line = probe.Request(WM_PROBE_CYCLE, "held ", (nuint)unheld);
            var handle = new IntPtr(Convert.ToInt64(line.Split(' ')[2], 16));
            var form = probe.Client.ElementFromHandle(handle);
            held.Add(form);
            foreach (var id in new[] { "SecondLabel", "SecondPanel", "InnerLabel" })
            {
                var e = form.FindFirst(TreeScope.Descendants, probe.IdCondition(id));
                Assert.NotNull(e);
                Assert.False(string.IsNullOrEmpty(e.CurrentName), $"{id} has no name");
                held.Add(e);
            }

            probe.Request(WM_PROBE_FREE, "freed ");
        }

        Round(0);                                                        // warm-up: first use of the client
        Gui.WaitIdle(probe.Window);
        var providers0 = probe.Request(WM_PROBE_COUNT, "providers ");
        var gdi0 = Gui.GuiResources(probe.App.Process, Gui.GR_GDIOBJECTS);
        var user0 = Gui.GuiResources(probe.App.Process, Gui.GR_USEROBJECTS);
        for (var i = 0; i < 100; i++)
        {
            Round(9);
        }

        Gui.WaitIdle(probe.Window);
        var providers1 = probe.Request(WM_PROBE_COUNT, "providers ");
        var gdi1 = Gui.GuiResources(probe.App.Process, Gui.GR_GDIOBJECTS);
        var user1 = Gui.GuiResources(probe.App.Process, Gui.GR_USEROBJECTS);
        Assert.True(providers0 == providers1 && gdi1 - gdi0 <= 2 && user1 - user0 <= 2,
            $"after 1000 forms ({held.Count} elements held by the client): {providers0} -> {providers1}, GDI {gdi0} -> {gdi1}, USER {user0} -> {user1}");
        GC.KeepAlive(held);
        probe.Close();
    }

    /// <summary>A started probe: its main window, the COM client and the window's element, the marker protocol of the program.</summary>
    private sealed class Probe : IDisposable
    {
        private int linesSeen;

        private Probe(Gui.GuiProcess app, IntPtr window)
        {
            App = app;
            Window = window;
            Client = (IUIAutomation)new CUIAutomation();
            Root = Client.ElementFromHandle(window);
        }

        public Gui.GuiProcess App { get; }

        public IntPtr Window { get; }

        public IUIAutomation Client { get; }

        public IUIAutomationElement Root { get; }

        public static Probe Start()
        {
            var exe = Gui.BuildProgram(ProbeSource);
            var app = Gui.StartGui(exe, [], new Dictionary<string, string>());
            var window = Gui.WaitForMainWindow(app, "PN.TForm", "Accessibility Probe");
            if (window == IntPtr.Zero)
            {
                var output = app.StdOut + app.StdErr;
                app.Dispose();
                Assert.Fail("the probe window was not found\n" + output);
            }

            var probe = new Probe(app, window);
            Assert.True(app.WaitForOutputLine("ready " + window.ToString("X")), probe.Output("the ready line"));
            return probe;
        }

        /// <summary>The element of the probe whose AutomationId is <paramref name="id"/> (the form itself included); null when none.</summary>
        public IUIAutomationElement? Find(string id) => Root.FindFirst(TreeScope.Element | TreeScope.Descendants, IdCondition(id));

        /// <summary>AutomationId = <paramref name="id"/>.</summary>
        public IUIAutomationCondition IdCondition(string id) => Client.CreatePropertyCondition(UiaIds.AutomationIdProperty, id);

        public void Post(uint message, nuint wParam = 0) => Gui.Post(Window, message, wParam);

        /// <summary>Posts a marker message and returns the next new stdout line that starts with <paramref name="prefix"/>.</summary>
        public string Request(uint message, string prefix, nuint wParam = 0)
        {
            Post(message, wParam);
            string? found = null;
            Assert.True(Gui.WaitUntil(() =>
            {
                var lines = App.StdOut.Split('\n');
                for (var i = linesSeen; i < lines.Length - 1; i++)
                {
                    if (lines[i].StartsWith(prefix, StringComparison.Ordinal))
                    {
                        found = lines[i].TrimEnd('\r');
                        linesSeen = i + 1;
                        return true;
                    }
                }

                return App.Process.HasExited;
            }) && found is not null, Output($"a line '{prefix}…'"));
            return found!;
        }

        public string Output(string what) => $"{what} did not come\n--- stdout\n{App.StdOut}\n--- stderr\n{App.StdErr}";

        public void Close()
        {
            Gui.Post(Window, Gui.WM_CLOSE);
            Assert.True(App.WaitForExit(), App.ExitFailure("the probe, after WM_CLOSE,"));
            Assert.Equal(0, App.Process.ExitCode);
        }

        public void Dispose() => App.Dispose();
    }
}
