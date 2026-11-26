using System.Runtime.InteropServices;
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
    private const uint WM_PROBE_RELEASE = WM_APP + 5;
    private const uint WM_PROBE_CHANGE = WM_APP + 6;
    private const uint WM_PROBE_REMOVE = WM_APP + 7;

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
        ("NotesRich", UiaIds.Document, true),
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

    /// <summary>
    /// Card F05-009 acceptance 2 (UIF-1430, UIF-1444): the framework-drawn button is a tool button; its Invoke pattern
    /// clicks it once (the probe prints the OnClick marker once) and raises Invoke_Invoked for a listening client.
    /// </summary>
    [Fact]
    [Trait("Req", "UIF-1430")]
    [Trait("Req", "UIF-1444")]
    public void Uia_InvokeToolButtonFiresOnClick()
    {
        Harness.RequireDesktop();
        using var probe = Probe.Start();
        var button = probe.Find("PlainButton");
        Assert.NotNull(button);
        var sink = new EventSink();
        probe.Client.AddAutomationEventHandler(UiaIds.InvokeInvokedEvent, button, TreeScope.Element, IntPtr.Zero, sink);
        Assert.Null(button.GetCurrentPattern(UiaIds.TogglePattern));                    // a plain button: no Toggle
        var invoke = (IUIAutomationInvokePattern)button.GetCurrentPattern(UiaIds.InvokePattern)!;
        invoke.Invoke();
        Assert.True(probe.App.WaitForOutputLine("click PlainButton"), probe.Output("the click marker"));
        Assert.True(
            Gui.WaitUntil(() => sink.Has(e => e.Kind == "event" && e.Id == UiaIds.InvokeInvokedEvent)),
            "no Invoke_Invoked event; seen: " + sink);
        probe.Request(WM_PROBE_COUNT, "providers ");                                    // every earlier post handled
        Assert.Equal(1, probe.Lines("click PlainButton"));
        probe.Client.RemoveAllEventHandlers();
        probe.Close();
    }

    /// <summary>
    /// Card F05-009 acceptance 3 (UIF-1431, UIF-1440, UIF-1445): the framework-drawn check is a <c>tbsCheck</c> tool
    /// button; a property-changed handler on ToggleState observes Off to On after Toggle and On to Off after the
    /// probe releases the button itself.
    /// </summary>
    [Fact]
    [Trait("Req", "UIF-1431")]
    [Trait("Req", "UIF-1440")]
    [Trait("Req", "UIF-1445")]
    public void Uia_ToggleRaisesToggleStatePropertyChanged()
    {
        Harness.RequireDesktop();
        using var probe = Probe.Start();
        var check = probe.Find("CheckButton");
        Assert.NotNull(check);
        Assert.Null(check.GetCurrentPattern(UiaIds.InvokePattern));                     // a check button: no Invoke
        var toggle = (IUIAutomationTogglePattern)check.GetCurrentPattern(UiaIds.TogglePattern)!;
        Assert.Equal(UiaIds.ToggleOff, toggle.CurrentToggleState);
        var sink = new EventSink();
        probe.Client.AddPropertyChangedEventHandlerNativeArray(
            check, TreeScope.Element, IntPtr.Zero, sink, [UiaIds.ToggleStateProperty], 1);
        toggle.Toggle();
        Assert.True(probe.App.WaitForOutputLine("click CheckButton"), probe.Output("the click marker"));
        Assert.True(
            Gui.WaitUntil(() => sink.Has(e => e.Id == UiaIds.ToggleStateProperty && Equals(e.Value, UiaIds.ToggleOn))),
            "no ToggleState On event after Toggle; seen: " + sink);
        Assert.Equal(UiaIds.ToggleOn, toggle.CurrentToggleState);
        probe.Request(WM_PROBE_RELEASE, "released");
        Assert.True(
            Gui.WaitUntil(() => sink.Has(e => e.Id == UiaIds.ToggleStateProperty && Equals(e.Value, UiaIds.ToggleOff))),
            "no ToggleState Off event after Down := False; seen: " + sink);
        Assert.Equal(UiaIds.ToggleOff, toggle.CurrentToggleState);
        Assert.Equal("1 0", sink.Values(UiaIds.ToggleStateProperty));                  // On, then Off: one event each
        probe.Client.RemoveAllEventHandlers();
        probe.Close();
    }

    /// <summary>
    /// The patterns of native controls that the framework does not implement (UIF-1432): the system's providers
    /// supply them. One control of each such pattern the probe contains, with a value of the pattern read through the
    /// client, and the system's framework id (the framework added nothing to them).
    /// </summary>
    [Fact]
    [Trait("Req", "UIF-1432")]
    public void Uia_SystemPatternsOfNativeControls()
    {
        Harness.RequireDesktop();
        using var probe = Probe.Start();
        (string Id, int Pattern, string Name, int Property, object? Expected)[] rows =
        [
            ("OkButton", UiaIds.InvokePattern, "Invoke", 0, null),
            ("RememberCheck", UiaIds.TogglePattern, "Toggle", UiaIds.ToggleStateProperty, UiaIds.ToggleOff),
            ("OptionRadio", UiaIds.SelectionItemPattern, "SelectionItem", UiaIds.SelectionItemIsSelectedProperty, false),
            ("NameEdit", UiaIds.ValuePattern, "Value", UiaIds.ValueValueProperty, "NameEdit"),
            ("VolumeTrack", UiaIds.RangeValuePattern, "RangeValue", UiaIds.RangeValueValueProperty, 0.0),
            ("FruitList", UiaIds.SelectionPattern, "Selection", 0, null),
            ("ColourCombo", UiaIds.ExpandCollapsePattern, "ExpandCollapse", UiaIds.ExpandCollapseStateProperty, 0),
            ("NotesRich", UiaIds.TextPattern, "Text", 0, null),
        ];
        var problems = new List<string>();
        foreach (var (id, pattern, name, property, expected) in rows)
        {
            var e = probe.Find(id);
            if (e is null)
            {
                problems.Add($"{id}: not found by AutomationId");
                continue;
            }

            if (e.CurrentFrameworkId != "Win32")
            {
                problems.Add($"{id}: framework id {e.CurrentFrameworkId}, expected the system's Win32");
            }

            if (e.GetCurrentPattern(pattern) is null)
            {
                problems.Add($"{id}: no {name} pattern");
                continue;
            }

            if (property != 0)
            {
                var value = e.GetCurrentPropertyValue(property);
                if (!Equals(value, expected))
                {
                    problems.Add($"{id}: {name} property {property} = {value ?? "null"} ({value?.GetType().Name}), expected {expected}");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        probe.Close();
    }

    /// <summary>
    /// UIF-1445, UIF-1446: the name of a framework-drawn label raises Name property-changed when its caption changes,
    /// a disabled image raises IsEnabled property-changed, and a label inserted into or removed from a panel raises
    /// structure-changed (children invalidated) on the panel, with the panel's runtime id.
    /// </summary>
    [Fact]
    [Trait("Req", "UIF-1445")]
    [Trait("Req", "UIF-1446")]
    public void Uia_NameChangeRaisesEvent()
    {
        Harness.RequireDesktop();
        using var probe = Probe.Start();
        var (label, image, panel) = (probe.Find("BoxLabel"), probe.Find("LogoImage"), probe.Find("InfoPanel"));
        if (label is null || image is null || panel is null)
        {
            Assert.Fail($"by AutomationId: BoxLabel {label is not null}, LogoImage {image is not null}, InfoPanel {panel is not null}");
            return;
        }

        Assert.Equal("In the box", label.CurrentName);
        Assert.Equal(true, image.GetCurrentPropertyValue(UiaIds.IsEnabledProperty));
        var panelId = string.Join(",", panel.GetRuntimeId());
        var (labelSink, imageSink, panelSink) = (new EventSink(), new EventSink(), new EventSink());
        probe.Client.AddPropertyChangedEventHandlerNativeArray(
            label, TreeScope.Element, IntPtr.Zero, labelSink, [UiaIds.NameProperty], 1);
        probe.Client.AddPropertyChangedEventHandlerNativeArray(
            image, TreeScope.Element, IntPtr.Zero, imageSink, [UiaIds.IsEnabledProperty], 1);
        probe.Client.AddStructureChangedEventHandler(panel, TreeScope.Element, IntPtr.Zero, panelSink);
        probe.Request(WM_PROBE_CHANGE, "changed");
        Assert.True(
            Gui.WaitUntil(() => labelSink.Has(e => e.Id == UiaIds.NameProperty && Equals(e.Value, "Changed label"))),
            "no Name event of the label; seen: " + labelSink);
        Assert.True(
            Gui.WaitUntil(() => imageSink.Has(e => e.Id == UiaIds.IsEnabledProperty && Equals(e.Value, false))),
            "no IsEnabled event of the image; seen: " + imageSink);
        Assert.True(
            Gui.WaitUntil(() => panelSink.Count(e => e.Kind == "structure") >= 1),
            "no structure-changed event of the panel after the insertion; seen: " + panelSink);
        Assert.Equal("Changed label", label.CurrentName);
        Assert.Equal(false, image.GetCurrentPropertyValue(UiaIds.IsEnabledProperty));
        Assert.NotNull(panel.FindFirst(TreeScope.Children, probe.IdCondition("InsertedLabel")));
        probe.Request(WM_PROBE_REMOVE, "removed");
        Assert.True(
            Gui.WaitUntil(() => panelSink.Count(e => e.Kind == "structure") >= 2),
            "no structure-changed event of the panel after the removal; seen: " + panelSink);
        Assert.Null(panel.FindFirst(TreeScope.Children, probe.IdCondition("InsertedLabel")));
        Assert.All(panelSink.All(), e =>
        {
            Assert.Equal(UiaIds.ChildrenInvalidated, e.Id);
            Assert.Equal(panelId, e.Value as string);
        });
        probe.Client.RemoveAllEventHandlers();
        probe.Close();
    }

    /// <summary>
    /// One event a client handler received: its kind (event, property, structure), the event, property or
    /// change-type id, and the new value (structure: the runtime id, comma-separated).
    /// </summary>
    private sealed record UiaEvent(string Kind, int Id, object? Value);

    /// <summary>
    /// The client's callback object for the three handler interfaces (handed to the system as a COM callable
    /// wrapper). The system calls it on its own threads; it only records what arrives.
    /// </summary>
    [ComVisible(true)]
    private sealed class EventSink :
        IUIAutomationEventHandler, IUIAutomationPropertyChangedEventHandler, IUIAutomationStructureChangedEventHandler
    {
        private readonly List<UiaEvent> events = [];

        public void HandleAutomationEvent(IUIAutomationElement sender, int eventId) => Add(new("event", eventId, null));

        public void HandlePropertyChangedEvent(IUIAutomationElement sender, int propertyId, object? newValue) =>
            Add(new("property", propertyId, newValue));

        public void HandleStructureChangedEvent(IUIAutomationElement sender, int changeType, int[]? runtimeId) =>
            Add(new("structure", changeType, runtimeId is null ? null : string.Join(",", runtimeId)));

        public bool Has(Func<UiaEvent, bool> match) => All().Any(match);

        public int Count(Func<UiaEvent, bool> match) => All().Count(match);

        public UiaEvent[] All()
        {
            lock (events)
            {
                return [.. events];
            }
        }

        /// <summary>The new values of the property events of <paramref name="propertyId"/>, space-separated.</summary>
        public string Values(int propertyId) =>
            string.Join(" ", All().Where(e => e.Kind == "property" && e.Id == propertyId).Select(e => e.Value));

        public override string ToString() => string.Join("; ", All().Select(e => $"{e.Kind} {e.Id} {e.Value}"));

        private void Add(UiaEvent e)
        {
            lock (events)
            {
                events.Add(e);
            }
        }
    }

    /// <summary>
    /// A started probe: its main window, the COM client and the window's element, the marker protocol of the program
    /// (internal: U-157's <see cref="AccessibilityGridClientTests"/> uses it too).
    /// </summary>
    internal sealed class Probe : IDisposable
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

        /// <summary>How many lines of the probe's stdout are exactly <paramref name="line"/>.</summary>
        public int Lines(string line) => App.StdOut.Split('\n').Count(l => l.TrimEnd('\r') == line);

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
