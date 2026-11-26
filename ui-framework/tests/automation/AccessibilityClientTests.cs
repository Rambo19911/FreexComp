using System.Runtime.InteropServices;
using FreexComp.UiFramework.Tests;

// U-161: the tests of this project run one at a time (xUnit runs the test classes in parallel otherwise). With two
// probes started at once the client's ElementFromHandle failed with E_FAIL, and after RemoveAllEventHandlers of one
// test the focus-changed handler of the grid test received no further events, so its wait ran to the limit.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace FreexComp.UiFramework.Automation.Tests;

/// <summary>
/// U-155 (accessibility.md §9): the providers of framework controls and the annotations of native controls as a UI
/// Automation client sees them: the operating system's COM client (<c>IUIAutomation</c>, the one screen readers use;
/// interop slice in <c>UiaClient.cs</c>, decision Q-P8A-16). Each test builds <c>tests/gui/AccessibilityProbe.pas</c>, starts it (its form is shown
/// without activation), finds elements by <c>AutomationId</c> (= component name) under the probe's window, and closes
/// the probe. The tests only read elements of the probe process: no mouse, no input to other windows, no focus.
/// U-164: the form-cycle tests move the foreground between the probe and a foreground holder of the test
/// (<see cref="ForegroundTurns"/>) and hand it back to the window that had it before.
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
    private const uint WM_PROBE_LISTENING = WM_APP + 11;
    private const uint WM_PROBE_DETACHED = WM_APP + 12;
    private const uint WM_PROBE_BORDERLESS = WM_APP + 14;

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
        Assert.True(probe.WaitForLine("renamed"), probe.Output("the rename marker"));
        Assert.Equal("Renamed fruit list", list.CurrentName);
        Assert.Equal("Full name:", edit.CurrentName);                   // the caption of its label changed
        probe.Close();
    }

    /// <summary>
    /// Card F05-009 acceptance 4 (UIF-1425, UIF-1426): the probe opens and frees a second form 1 000 times; each tenth
    /// form is read by the client, which keeps the elements it took (the form, a panel and graphic controls) until
    /// the end. The GDI and USER object counts of the probe (bound of <c>HelloForm_NoGdiLeakOnResize</c>) and its count
    /// of live provider objects are the same before and after, and no provider object without its control is left.
    /// U-163: the client listens (<see cref="Listen"/>) before the first count, so the counts do not depend on whether
    /// a client of another process starts listening during the cycles (the system then creates the providers of the
    /// probe's live windows and objects of its own: before U-163 the test failed whenever that happened).
    /// U-164: the counts do not depend either on whether another process gives the probe the foreground during the
    /// cycles (<see cref="ThousandFormCycles"/>: warm-up with the foreground, counts with no window of the probe active).
    /// U-168: before the first count the client also asks every framework window of the probe for its provider
    /// (<see cref="ProvideEveryWindow"/>), and the first count must be exactly those providers.
    /// </summary>
    [Fact]
    [Trait("Req", "UIF-1425")]
    [Trait("Req", "UIF-1426")]
    [Trait("Req", "UIF-1440")]
    public void Uia_ProvidersReleasedOverThousandFormCycles()
    {
        Harness.RequireDesktop();
        using var probe = Probe.Start();
        Listen(probe);
        using var turns = new ForegroundTurns(probe);
        var failure = ThousandFormCycles(probe, turns, null);
        probe.RemoveHandlers();
        Assert.True(failure is null, failure);
        probe.Close();
    }

    /// <summary>
    /// U-163 (UIF-1425, UIF-1426, UIF-1440): the cycles of <see cref="Uia_ProvidersReleasedOverThousandFormCycles"/>
    /// with no listening client of the test during the first 500 forms and one from then on (the failure of the
    /// lead's run, deterministic: a client of another process started listening). Each half keeps its counts (the
    /// second one counted once the client listens), and across both no provider object is left without its control.
    /// U-168: the providers of the probe's framework windows exist before the first count (<see cref="ProvideEveryWindow"/>),
    /// so the first half does not depend on a client of another process that starts listening (the lead's full verify
    /// failed with <c>providers 1 -> providers 8</c> beside the data area's client test; reproduced by
    /// <see cref="Uia_ProvidersReleasedWhenAnotherProcessStartsListening"/>), and the provider count once the test's
    /// client listens must equal the first one.
    /// </summary>
    [Fact]
    [Trait("Req", "UIF-1425")]
    [Trait("Req", "UIF-1426")]
    [Trait("Req", "UIF-1440")]
    public void Uia_ProvidersReleasedWhenAClientStartsListening()
    {
        Harness.RequireDesktop();
        using var probe = Probe.Start();
        using var turns = new ForegroundTurns(probe);
        var failure = ThousandFormCycles(probe, turns, () => Listen(probe));
        probe.RemoveHandlers();
        Assert.True(failure is null, failure);
        probe.Close();
    }

    /// <summary>
    /// U-168 (UIF-1425, UIF-1426, UIF-1440): the cycles of <see cref="Uia_ProvidersReleasedWhenAClientStartsListening"/>
    /// while a client of another process (<see cref="OutsideListener"/>) starts listening before the 250th form, in the
    /// half in which the test's own client does not listen yet (the failure of the lead's full verify beside the data
    /// area's client test, deterministic). The counts are those of the other cycle tests.
    /// </summary>
    [Fact]
    [Trait("Req", "UIF-1425")]
    [Trait("Req", "UIF-1426")]
    [Trait("Req", "UIF-1440")]
    public void Uia_ProvidersReleasedWhenAnotherProcessStartsListening()
    {
        Harness.RequireDesktop();
        using var probe = Probe.Start();
        using var turns = new ForegroundTurns(probe);
        Gui.GuiProcess? outside = null;
        try
        {
            var failure = ThousandFormCycles(probe, turns, () => Listen(probe), round =>
            {
                if (round == 25)
                {
                    outside = OutsideListener.Start(probe);
                }
            });
            probe.RemoveHandlers();
            Assert.True(outside is not null, "the outside listener was not started");
            OutsideListener.Close(outside, probe.Remaining);
            Assert.True(failure is null, failure);
            probe.Close();
        }
        finally
        {
            outside?.Dispose();
        }
    }

    /// <summary>
    /// U-168: <c>tests/gui/OutsideListener.pas</c>, a UI Automation client of another process with a desktop
    /// focus-changed handler (what a client test of another area running beside this project has). It is closed with
    /// <c>WM_CLOSE</c>, so that it removes its handler as such a test does (a killed client leaves its registration to
    /// the system's clean-up, which the next test of this project would meet).
    /// </summary>
    internal static class OutsideListener
    {
        public static string Source => Path.Combine(UiCorpus.AreaDirectory, "tests", "gui", "OutsideListener.pas");

        /// <summary>
        /// Starts the listener (in the job object of the test host; the caller closes it with <see cref="Close"/> and
        /// disposes it: killed if it still runs) and waits until it has registered its handler and the probe's
        /// <c>UiaClientsAreListening</c> is True.
        /// </summary>
        public static Gui.GuiProcess Start(Probe probe)
        {
            var listener = Gui.StartGui(Gui.BuildProgram(Source), [], new Dictionary<string, string>());
            try
            {
                Assert.True(listener.WaitForOutputLine("listening", probe.Remaining),
                    $"the outside listener did not listen\n--- stdout\n{listener.StdOut}\n--- stderr\n{listener.StdErr}");
                Assert.True(
                    probe.WaitUntil(() => probe.Request(WM_PROBE_LISTENING, "listening ") == "listening True"),
                    "the probe does not see the outside listener");
                return listener;
            }
            catch
            {
                listener.Dispose();
                throw;
            }
        }

        /// <summary>Posts <c>WM_CLOSE</c> to the listener's window (its line <c>window &lt;hex&gt;</c>): it removes its handler and exits with 0.</summary>
        public static void Close(Gui.GuiProcess listener, TimeSpan limit)
        {
            var line = listener.StdOut.Split('\n').Select(l => l.TrimEnd('\r')).Single(l => l.StartsWith("window ", StringComparison.Ordinal));
            Gui.Post(new IntPtr(Convert.ToInt64(line[7..], 16)), Gui.WM_CLOSE);
            Assert.True(listener.WaitForExit(limit), listener.ExitFailure("the outside listener, after WM_CLOSE,"));
            Assert.Equal(0, listener.Process.ExitCode);
        }
    }

    /// <summary>
    /// U-164 (UIF-1425, UIF-1426, UIF-1440): the cycles of <see cref="Uia_ProvidersReleasedOverThousandFormCycles"/>
    /// while the foreground moves between the probe and another process (the failure of the lead's run beside the data
    /// area's client test, deterministic): the test's foreground holder hands the foreground to the probe before the
    /// first form and the 500th and takes it back before the 250th and the 750th, so that half of the second forms are
    /// activated and their edit takes the keyboard focus. The counts are those of the other cycle tests.
    /// </summary>
    [Fact]
    [Trait("Req", "UIF-1425")]
    [Trait("Req", "UIF-1426")]
    [Trait("Req", "UIF-1440")]
    public void Uia_ProvidersReleasedWhileTheForegroundMoves()
    {
        Harness.RequireDesktop();
        using var probe = Probe.Start();
        Listen(probe);
        using var turns = new ForegroundTurns(probe);
        var failure = ThousandFormCycles(probe, turns, null, round =>
        {
            if (round % 25 == 0)
            {
                if (round % 50 == 0)
                {
                    turns.ToProbe();
                }
                else
                {
                    turns.ToHolder();
                }
            }
        });
        probe.RemoveHandlers();
        Assert.True(failure is null, failure);
        probe.Close();
    }

    /// <summary>
    /// U-163: makes the test's client a listening one (a desktop focus-changed handler and a structure-changed
    /// handler on the probe's tree, removed by <see cref="Probe.RemoveHandlers"/>) and waits until the probe's
    /// <c>UiaClientsAreListening</c> is True.
    /// </summary>
    internal static void Listen(Probe probe)
    {
        probe.Client.AddFocusChangedEventHandler(ListeningSink.Prefetch(probe.Client), new ListeningSink());
        probe.Client.AddStructureChangedEventHandler(
            probe.Root, TreeScope.Element | TreeScope.Descendants, IntPtr.Zero, new EventSink());
        Assert.True(
            probe.WaitUntil(() => probe.Request(WM_PROBE_LISTENING, "listening ") == "listening True"),
            "the probe does not see a listening client");
    }

    /// <summary>
    /// U-168: the client asks every framework window of the probe (top-level windows of its thread, shown or hidden,
    /// and their child windows; class name <c>PN.</c>…) for its element, so that the provider of each one exists
    /// (UIF-1425: kept with its control). Those are the providers the system creates when a client of any process
    /// starts listening (UIF-1425, amended U-163); once they exist, a count of live providers no longer depends on
    /// whether a client listens. Returns how many windows were asked.
    /// </summary>
    internal static int ProvideEveryWindow(Probe probe)
    {
        var thread = ThreadWindows.ThreadOf(probe.Window);
        var windows = new List<IntPtr>();
        ThreadWindows.EnumThreadWindows(thread, (h, _) =>
        {
            windows.Add(h);
            return true;
        }, IntPtr.Zero);
        var asked = 0;
        foreach (var window in windows.SelectMany(w => new[] { w }.Concat(Gui.Descendants(w))))
        {
            if (Gui.ClassName(window).StartsWith("PN.", StringComparison.Ordinal))
            {
                Assert.NotNull(probe.Client.ElementFromHandle(window));
                asked++;
            }
        }

        return asked;
    }

    /// <summary>U-168: the top-level windows of a thread (<c>EnumThreadWindows</c>).</summary>
    private static class ThreadWindows
    {
        public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

        public static uint ThreadOf(IntPtr hwnd) => GetWindowThreadProcessId(hwnd, out _);

        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumThreadWindows(uint thread, EnumProc callback, IntPtr lParam);

        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    }

    /// <summary>
    /// The probe opens and frees a second form 1 000 times; each tenth form is read by the client, which keeps the
    /// elements it took (the form, a panel and graphic controls) until the end. Null when the GDI and USER object
    /// counts of the probe (bound of <c>HelloForm_NoGdiLeakOnResize</c>) and its count of live provider objects are
    /// the same before and after and no provider object without its control is left; else the failure. With
    /// <paramref name="midway"/> (U-163) the first 500 forms are compared, <paramref name="midway"/> runs, and the last
    /// 500 are compared with the counts taken after it and one more form. <paramref name="everyRound"/> (U-164) runs
    /// before each tenth form (argument: 0, 1, … 99).
    /// U-164, the same foreground conditions for every count: once the probe's thread has had the keyboard focus (another
    /// process gave it the foreground), the system keeps objects of its own for the thread (its text-services window,
    /// fonts: GDI +5, USER +11 measured, from then on constant), and while a window of the probe is the foreground a few
    /// more USER objects (+2). So before the first count the probe gets the foreground from the holder of
    /// <paramref name="turns"/> for one form and loses it again for one more, and each count is taken with the holder
    /// in front when a window of the probe has the foreground; then the foreground goes back to the window that had it
    /// before the test. The edits that take the focus are borderless (<c>WM_APP + 14</c> of the probe: the system's
    /// border animations of themed edits keep a varying number of objects, see the probe). The bounds are unchanged.
    /// U-168, the same providers at every count whether or not a client of any process listens: after the warm-up the
    /// client asks every framework window of the probe for its provider (<see cref="ProvideEveryWindow"/>; the providers
    /// the system creates when a client starts listening, kept with their controls), and the first count must be
    /// exactly one provider per framework window (no provider of a warm-up form left); with <paramref name="midway"/>
    /// the provider count taken after it must be the first one (listening adds none).
    /// </summary>
    private static string? ThousandFormCycles(Probe probe, ForegroundTurns turns, Action? midway, Action<int>? everyRound = null)
    {
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

        Counts Take()
        {
            if (turns.ProbeHasForeground)
            {
                turns.ToHolder();                                        // counted as the baseline is: no probe window active
            }

            Gui.WaitIdle(probe.Window);
            var counts = new Counts(probe.Request(WM_PROBE_COUNT, "providers "), probe.Request(WM_PROBE_DETACHED, "detached "),
                Gui.GuiResources(probe.App.Process, Gui.GR_GDIOBJECTS), Gui.GuiResources(probe.App.Process, Gui.GR_USEROBJECTS));
            turns.HandBack();
            return counts;
        }

        string? Compare(string what, Counts a, Counts b) =>
            a.Providers == b.Providers && b.Detached == "detached 0" && b.Gdi - a.Gdi <= 2 && b.User - a.User <= 2
                ? null
                : $"after {what} ({held.Count} elements held by the client): {a.Providers} -> {b.Providers}, "
                    + $"{a.Detached} -> {b.Detached}, GDI {a.Gdi} -> {b.Gdi}, USER {a.User} -> {b.User}";

        Round(0);                                                        // warm-up: first use of the client
        probe.Request(WM_PROBE_BORDERLESS, "borderless");
        turns.ToProbe();
        Round(0);                                                        // warm-up: a form activated, its edit focused
        turns.ToHolder();
        Round(0);                                                        // warm-up: a form of the inactive probe
        var windows = $"providers {ProvideEveryWindow(probe)}";        // U-168: from here on no listener adds one
        var before = Take();
        if (before.Providers != windows)
        {
            return $"before the cycles: {before.Providers}, but the probe has {windows[10..]} framework windows "
                + "(a provider of a freed warm-up form is left, or one of a persistent window is missing)";
        }

        for (var i = 0; i < 100; i++)
        {
            if (i == 50 && midway is not null)
            {
                if (Compare("the first 500 forms", before, Take()) is { } first)
                {
                    return first;
                }

                midway();
                Round(0);                                                // the first form with the client listening
                var listening = Take();
                if (listening.Providers != before.Providers)
                {
                    return $"once the client listens: {before.Providers} -> {listening.Providers}";
                }

                before = listening;
            }

            everyRound?.Invoke(i);
            Round(9);
        }

        var failure = Compare(midway is null ? "1000 forms" : "the last 500 forms", before, Take());
        GC.KeepAlive(held);
        return failure;
    }

    /// <summary>
    /// U-164: the foreground holder of a cycle test (<see cref="Gui.StartForegroundHolder"/>, killed on dispose) and the
    /// window that had the foreground before it, to which the foreground is handed back after each turn of the test.
    /// </summary>
    internal sealed class ForegroundTurns : IDisposable
    {
        private readonly Probe probe;
        private readonly Gui.GuiProcess holder;
        private readonly IntPtr previous;

        public ForegroundTurns(Probe probe)
        {
            this.probe = probe;
            previous = Gui.Foreground();
            holder = Gui.StartForegroundHolder();
            Window = Gui.WaitForMainWindow(holder, "FxcForegroundHolder", "Foreground Holder", TimeSpan.Zero);
        }

        /// <summary>The holder's window.</summary>
        public IntPtr Window { get; }

        /// <summary>Whether a window of the probe is the foreground window.</summary>
        public bool ProbeHasForeground => Gui.ProcessOf(Gui.Foreground()) == probe.App.Process.Id;

        /// <summary>The probe's main window becomes the foreground, handed over by the holder (so really activated).</summary>
        public void ToProbe()
        {
            if (Gui.Foreground() != Window)
            {
                ToHolder();                                             // only the holder hands it over: no other program is touched
            }

            Assert.True(Gui.BringToForeground(probe.Window, probe.Remaining), $"the probe did not get the foreground (it is {Gui.Foreground():X})");
        }

        /// <summary>The holder's window becomes the foreground: no window of the probe is active.</summary>
        public void ToHolder() =>
            Assert.True(Gui.BringToForeground(Window, probe.Remaining), $"the holder did not get the foreground (it is {Gui.Foreground():X})");

        /// <summary>
        /// While the holder has the foreground, it is handed back to the window that had it before the test (when that
        /// is a window of neither the probe nor the holder); otherwise nothing.
        /// </summary>
        public void HandBack()
        {
            var owner = Gui.ProcessOf(previous);
            if (owner != 0 && owner != probe.App.Process.Id && owner != holder.Process.Id)
            {
                Gui.HandOver(Window, previous);
            }
        }

        public void Dispose() => holder.Dispose();
    }

    /// <summary>The counts of the probe a leak check compares: its provider and detached-provider lines, GDI and USER objects.</summary>
    private sealed record Counts(string Providers, string Detached, int Gdi, int User);

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
        Assert.True(probe.WaitForLine("click PlainButton"), probe.Output("the click marker"));
        Assert.True(
            probe.WaitUntil(() => sink.Has(e => e.Kind == "event" && e.Id == UiaIds.InvokeInvokedEvent)),
            "no Invoke_Invoked event; seen: " + sink);
        probe.Request(WM_PROBE_COUNT, "providers ");                                    // every earlier post handled
        Assert.Equal(1, probe.Lines("click PlainButton"));
        probe.RemoveHandlers();
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
        Assert.True(probe.WaitForLine("click CheckButton"), probe.Output("the click marker"));
        Assert.True(
            probe.WaitUntil(() => sink.Has(e => e.Id == UiaIds.ToggleStateProperty && Equals(e.Value, UiaIds.ToggleOn))),
            "no ToggleState On event after Toggle; seen: " + sink);
        Assert.Equal(UiaIds.ToggleOn, toggle.CurrentToggleState);
        probe.Request(WM_PROBE_RELEASE, "released");
        Assert.True(
            probe.WaitUntil(() => sink.Has(e => e.Id == UiaIds.ToggleStateProperty && Equals(e.Value, UiaIds.ToggleOff))),
            "no ToggleState Off event after Down := False; seen: " + sink);
        Assert.Equal(UiaIds.ToggleOff, toggle.CurrentToggleState);
        Assert.Equal("1 0", sink.Values(UiaIds.ToggleStateProperty));                  // On, then Off: one event each
        probe.RemoveHandlers();
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
            probe.WaitUntil(() => labelSink.Has(e => e.Id == UiaIds.NameProperty && Equals(e.Value, "Changed label"))),
            "no Name event of the label; seen: " + labelSink);
        Assert.True(
            probe.WaitUntil(() => imageSink.Has(e => e.Id == UiaIds.IsEnabledProperty && Equals(e.Value, false))),
            "no IsEnabled event of the image; seen: " + imageSink);
        Assert.True(
            probe.WaitUntil(() => panelSink.Count(e => e.Kind == "structure") >= 1),
            "no structure-changed event of the panel after the insertion; seen: " + panelSink);
        Assert.Equal("Changed label", label.CurrentName);
        Assert.Equal(false, image.GetCurrentPropertyValue(UiaIds.IsEnabledProperty));
        Assert.NotNull(panel.FindFirst(TreeScope.Children, probe.IdCondition("InsertedLabel")));
        probe.Request(WM_PROBE_REMOVE, "removed");
        Assert.True(
            probe.WaitUntil(() => panelSink.Count(e => e.Kind == "structure") >= 2),
            "no structure-changed event of the panel after the removal; seen: " + panelSink);
        Assert.Null(panel.FindFirst(TreeScope.Children, probe.IdCondition("InsertedLabel")));
        Assert.All(panelSink.All(), e =>
        {
            Assert.Equal(UiaIds.ChildrenInvalidated, e.Id);
            Assert.Equal(panelId, e.Value as string);
        });
        probe.RemoveHandlers();
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
    /// U-163: a desktop focus-changed handler that only makes the client a listening one: it records nothing and
    /// never calls into the sender's process (the system fills the cache request's properties before the call; the
    /// request is the one of the data area's client test, whose listening made the cycles fail before U-163).
    /// </summary>
    [ComVisible(true)]
    internal sealed class ListeningSink : IUIAutomationFocusChangedEventHandler
    {
        public static IUIAutomationCacheRequest Prefetch(IUIAutomation client)
        {
            var request = client.CreateCacheRequest();
            request.AddProperty(UiaIds.ProcessIdProperty);
            request.AddProperty(UiaIds.ControlTypeProperty);
            request.AddProperty(UiaIds.NameProperty);
            request.TreeFilter = client.CreateTrueCondition();
            return request;
        }

        public void HandleFocusChangedEvent(IUIAutomationElement sender)
        {
        }
    }

    /// <summary>
    /// A started probe: its main window, the COM client and the window's element, the marker protocol of the program
    /// (internal: U-157's <see cref="AccessibilityGridClientTests"/> uses it too).
    /// U-161: one test with a probe lasts at most <see cref="Budget"/> (build, start and every wait included): every
    /// wait of the probe ends at that deadline, where a watchdog kills the probe so that a wait inside a call to it
    /// ends too. The probe is in the job object of the test host (<see cref="Gui.StartGui"/>, U-162) that kills it when the host ends (also when the host
    /// is stopped); <see cref="Dispose"/> kills it and removes the client's event handlers, also after a failure.
    /// </summary>
    internal sealed class Probe : IDisposable
    {
        /// <summary>
        /// The time a test with a probe may take from <see cref="Start"/> to <see cref="Close"/>: 0.4 of the shared net
        /// (48 s), so that a test of this project, its handler removal and the kill included, ends within 60 s (U-161).
        /// </summary>
        public static readonly TimeSpan Budget = Gui.SafetyNet * 0.4;

        /// <summary>How long the removal of the client's event handlers may take (1/24 of the shared net: 5 s).</summary>
        private static readonly TimeSpan RemovalLimit = Gui.SafetyNet / 24;

        private readonly DateTime deadline;
        private readonly Timer watchdog;
        private bool handlersRemoved;
        private int linesSeen;

        private Probe(Gui.GuiProcess app, IntPtr window, DateTime deadline)
        {
            App = app;
            Window = window;
            this.deadline = deadline;
            Client = (IUIAutomation)new CUIAutomation();
            Root = Client.ElementFromHandle(window);
            watchdog = new Timer(_ => App.Dispose(), null, Remaining, Timeout.InfiniteTimeSpan);
        }

        public Gui.GuiProcess App { get; }

        public IntPtr Window { get; }

        public IUIAutomation Client { get; }

        public IUIAutomationElement Root { get; }

        /// <summary>The time left until the deadline of the test (never negative).</summary>
        public TimeSpan Remaining => Until(deadline);

        public static Probe Start()
        {
            var deadline = DateTime.UtcNow + Budget;
            var exe = Gui.BuildProgram(ProbeSource);
            var app = Gui.StartGui(exe, [], new Dictionary<string, string>());
            try
            {
                var window = Gui.WaitForMainWindow(app, "PN.TForm", "Accessibility Probe", Until(deadline));
                if (window == IntPtr.Zero)
                {
                    Assert.Fail("the probe window was not found\n" + app.StdOut + app.StdErr);
                }

                // the client reads the window only once the probe is ready (U-161: never during its start)
                Assert.True(
                    app.WaitForOutputLine("ready " + window.ToString("X"), Until(deadline)),
                    $"the ready line did not come\n--- stdout\n{app.StdOut}\n--- stderr\n{app.StdErr}");
                return new Probe(app, window, deadline);
            }
            catch
            {
                app.Dispose();                                      // also when the client failed on the window
                throw;
            }
        }

        /// <summary>The element of the probe whose AutomationId is <paramref name="id"/> (the form itself included); null when none.</summary>
        public IUIAutomationElement? Find(string id) => Root.FindFirst(TreeScope.Element | TreeScope.Descendants, IdCondition(id));

        /// <summary>AutomationId = <paramref name="id"/>.</summary>
        public IUIAutomationCondition IdCondition(string id) => Client.CreatePropertyCondition(UiaIds.AutomationIdProperty, id);

        public void Post(uint message, nuint wParam = 0) => Gui.Post(Window, message, wParam);

        /// <summary>Re-evaluates <paramref name="condition"/> until it holds, the probe exited or the deadline passed.</summary>
        public bool WaitUntil(Func<bool> condition) =>
            Gui.WaitUntil(() => condition() || App.Process.HasExited, Remaining) && condition();

        /// <summary>Waits until the probe's stdout holds the line <paramref name="line"/> (at most until the deadline).</summary>
        public bool WaitForLine(string line) => App.WaitForOutputLine(line, Remaining);

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
            }, Remaining) && found is not null, Output($"a line '{prefix}…'"));
            return found!;
        }

        /// <summary>How many lines of the probe's stdout are exactly <paramref name="line"/>.</summary>
        public int Lines(string line) => App.StdOut.Split('\n').Count(l => l.TrimEnd('\r') == line);

        public string Output(string what) =>
            $"{what} did not come (test budget {Budget.TotalSeconds:F0} s, {Remaining.TotalSeconds:F1} s left, "
            + $"probe {(App.Process.HasExited ? "exited" : "running")})\n--- stdout\n{App.StdOut}\n--- stderr\n{App.StdErr}";

        /// <summary>
        /// Removes every event handler of the client (all of them are the test's own: the tests of the project run one
        /// at a time). The removal waits for handler calls under way, so it runs on a thread of its own and may take
        /// at most <see cref="RemovalLimit"/>.
        /// </summary>
        public void RemoveHandlers()
        {
            handlersRemoved = true;
            Assert.True(RemoveAllBounded(), $"RemoveAllEventHandlers did not return within {RemovalLimit.TotalSeconds:F0} s");
        }

        public void Close()
        {
            Gui.Post(Window, Gui.WM_CLOSE);
            Assert.True(
                App.WaitForExit(Remaining),
                App.ExitFailure($"the probe, after WM_CLOSE ({Remaining.TotalSeconds:F1} s of the test budget left),"));
            Assert.Equal(0, App.Process.ExitCode);
        }

        /// <summary>Kills the probe if it still runs, then removes the client's event handlers if the test did not.</summary>
        public void Dispose()
        {
            watchdog.Dispose();
            App.Dispose();
            if (!handlersRemoved)
            {
                handlersRemoved = true;
                RemoveAllBounded();                                 // after a failure: must not hide the test's own one
            }
        }

        private static TimeSpan Until(DateTime deadline)
        {
            var left = deadline - DateTime.UtcNow;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }

        private bool RemoveAllBounded()
        {
            var removal = new Thread(Client.RemoveAllEventHandlers) { IsBackground = true, Name = "client handler removal" };
            removal.Start();
            return removal.Join(RemovalLimit);
        }
    }
}
