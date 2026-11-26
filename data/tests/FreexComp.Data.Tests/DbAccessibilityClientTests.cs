using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using FreexComp.UiFramework.Automation.Tests;
using FreexComp.UiFramework.Tests;

namespace FreexComp.Data.Tests;

/// <summary>
/// The UI Automation client tests of the data area run alone (U-161: two clients in one test host broke
/// <c>ElementFromHandle</c> and the focus-changed events): no other test of the assembly runs while one of them runs.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AutomationClients
{
    public const string Name = "UI Automation clients";
}

/// <summary>
/// A-083 (follow-up of ui-framework accessibility.md §12): the data grid and the navigator of
/// <c>data/tests/gui/DbAccessibilityProbe.pas</c> as the operating system's COM client (<c>IUIAutomation</c>, the one
/// screen readers use; the interop slice <c>UiaClient.cs</c> of the ui-framework client tests, linked) sees them. The
/// probe is built with opc as the golden programs are, started in the job object of the test host (it dies with the
/// host, U-162) and killed at the end of the test whatever happens; every wait ends at the test's deadline. The probe
/// sets the focus inside its own thread (no foreground change); keys reach the grid as messages the probe sends to its
/// own window; the focus-changed handler keeps only events of the probe's process. Nothing outside the probe is touched.
/// </summary>
[Collection(AutomationClients.Name)]
[Trait("Phase", "8")]
[Trait("Kind", "Native")]
[Trait("Category", "Foreground")]
[Trait("Category", "Integration")]
[Trait("Category", "Toolchain")]
public sealed class DbAccessibilityClientTests
{
    private const uint WM_APP = 0x8000;
    private const uint WM_CLOSE = 0x0010;
    private const uint WM_PROBE_STATE = WM_APP + 1;
    private const uint WM_PROBE_FOCUS = WM_APP + 2;
    private const uint WM_PROBE_KEY = WM_APP + 3;
    private const uint WM_PROBE_LISTENING = WM_APP + 4;
    private const uint WM_PROBE_CHANGE = WM_APP + 5;
    private const int VK_LEFT = 0x25;
    private const int VK_DOWN = 0x28;
    private const int TablePattern = 10012;                                          // UIA_TablePatternId
    private const int UIA_E_ELEMENTNOTENABLED = unchecked((int)0x80040200);

    /// <summary>The time the test may take from the probe's start to its exit (the build has its own limit).</summary>
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(60);

    private static string ProbeSource => Path.Combine(DataCorpus.RepositoryRoot, "data", "tests", "gui", "DbAccessibilityProbe.pas");

    /// <summary>
    /// The grid: DataGrid with the Grid and Table patterns, <c>GetItem(2, 1)</c> the cell 'Gamma' (DataItem, Value
    /// 'Gamma'), the column titles as HeaderItem elements; <c>SelectionItemPattern.Select</c> moves the dataset to the
    /// cell's record and selects its column. The navigator: ToolBar, each drawn button a Button named by its hint with
    /// the button identifier as AutomationId and the Invoke pattern; IsEnabled follows the dataset (Prior after First);
    /// Invoke clicks the button, a disabled button refuses. With the grid focused, keys sent to the grid raise
    /// focus-changed events on the new current cells, which the client receives in order.
    /// </summary>
    [Fact]
    public void Uia_DbGridCellsAndNavigatorButtons()
    {
        if (GoldenProgramRunner.MissingPrerequisites(DataCorpus.ToolchainDirectory, DataCorpus.OpcPath, DataCorpus.SqliteLibrary) is { } missing)
        {
            Assert.Fail(missing);
        }

        var ct = TestContext.Current.CancellationToken;
        var work = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", "data-uia-" + Guid.NewGuid().ToString("N"));
        var exe = Build(work, ct);
        using var probe = ProbeProcess.Start(exe, DateTime.UtcNow + Budget);
        var client = (IUIAutomation)new CUIAutomation();
        try
        {
            var root = client.ElementFromHandle(probe.Window);
            IUIAutomationElement? Find(IUIAutomationElement under, int property, string value) =>
                under.FindFirst(TreeScope.Descendants, client.CreatePropertyCondition(property, value));

            // the grid
            var grid = Find(root, UiaIds.AutomationIdProperty, "Grid");
            Assert.NotNull(grid);
            Assert.Equal(UiaIds.DataGrid, grid.CurrentControlType);
            Assert.NotNull(grid.GetCurrentPattern(TablePattern));
            var gridPattern = (IUIAutomationGridPattern?)grid.GetCurrentPattern(UiaIds.GridPattern);
            Assert.NotNull(gridPattern);
            Assert.Equal(5, gridPattern.CurrentRowCount);
            Assert.Equal(2, gridPattern.CurrentColumnCount);
            var cell = gridPattern.GetItem(2, 1);
            Assert.Equal("Gamma", cell.CurrentName);
            Assert.Equal(UiaIds.DataItem, cell.CurrentControlType);
            Assert.Equal("FreexComp", cell.CurrentFrameworkId);
            var value = (IUIAutomationValuePattern?)cell.GetCurrentPattern(UiaIds.ValuePattern);
            Assert.NotNull(value);
            Assert.Equal("Gamma", value.CurrentValue);
            Assert.Equal("3", gridPattern.GetItem(2, 0).CurrentName);
            var title = Find(grid, UiaIds.NameProperty, "Label");
            Assert.NotNull(title);
            Assert.Equal(UiaIds.HeaderItem, title.CurrentControlType);

            var selection = (IUIAutomationSelectionItemPattern?)cell.GetCurrentPattern(UiaIds.SelectionItemPattern);
            Assert.NotNull(selection);
            Assert.Equal(0, selection.CurrentIsSelected);
            Assert.Equal("state 1 0 dsBrowse", probe.Request(WM_PROBE_STATE, "state "));
            selection.Select();
            Assert.Equal("state 3 1 dsBrowse", probe.Request(WM_PROBE_STATE, "state "));
            Assert.NotEqual(0, selection.CurrentIsSelected);

            // the navigator
            var nav = Find(root, UiaIds.AutomationIdProperty, "Nav");
            Assert.NotNull(nav);
            Assert.Equal(UiaIds.ToolBar, nav.CurrentControlType);
            var first = Find(nav, UiaIds.NameProperty, "First record");
            var prior = Find(nav, UiaIds.NameProperty, "Prior record");
            var post = Find(nav, UiaIds.NameProperty, "Post edit");
            Assert.NotNull(first);
            Assert.NotNull(prior);
            Assert.NotNull(post);
            Assert.Equal(UiaIds.Button, prior.CurrentControlType);
            Assert.Equal("nbPrior", prior.CurrentAutomationId);
            Assert.True(prior.GetCurrentPropertyValue(UiaIds.IsEnabledProperty) is true, "Prior record enabled in the middle");
            Assert.True(post.GetCurrentPropertyValue(UiaIds.IsEnabledProperty) is false, "Post edit disabled while browsing");
            var invokeFirst = (IUIAutomationInvokePattern?)first.GetCurrentPattern(UiaIds.InvokePattern);
            Assert.NotNull(invokeFirst);
            invokeFirst.Invoke();
            Assert.Equal("state 1 1 dsBrowse", probe.Request(WM_PROBE_STATE, "state "));
            Assert.True(prior.GetCurrentPropertyValue(UiaIds.IsEnabledProperty) is false, "Prior record disabled at the first record");
            var invokePrior = (IUIAutomationInvokePattern?)prior.GetCurrentPattern(UiaIds.InvokePattern);
            Assert.NotNull(invokePrior);
            var refused = Assert.Throws<COMException>(invokePrior.Invoke);
            Assert.Equal(UIA_E_ELEMENTNOTENABLED, refused.HResult);

            // focus-changed events of the grid's cells
            Assert.Equal("focused grid True", probe.Request(WM_PROBE_FOCUS, "focused grid "));
            var sink = new FocusSink(probe.Process.Id, probe.Changed);
            client.AddFocusChangedEventHandler(FocusSink.Prefetch(client), sink);
            Assert.Equal("state 2 1 dsBrowse", probe.Request(WM_PROBE_KEY, "state ", VK_DOWN));
            Assert.Equal("state 2 0 dsBrowse", probe.Request(WM_PROBE_KEY, "state ", VK_LEFT));
            Assert.True(probe.WaitUntil(() => sink.Cells().Contains("2")), "no focus-changed event on cell (0, 1); seen: " + sink);
            Assert.Equal("Beta 2", string.Join(" ", sink.Cells()));
            Assert.True(RemoveHandlers(client), "RemoveAllEventHandlers did not return within 5 s");
            probe.Close();
        }
        finally
        {
            probe.Dispose();
            RemoveHandlers(client);                                                  // after a failure: bounded
            GoldenProgramRunner.TryDelete(work);
        }
    }

    /// <summary>
    /// A-084 (DAT-880, UIF-1445): a navigator button raises IsEnabled property-changed when the dataset's state changes
    /// what the button may do: Prior becomes enabled after Next and disabled again after First, Post becomes enabled
    /// when Edit puts the dataset into dsEdit and disabled again after Cancel (each button invoked by the client).
    /// </summary>
    [Fact]
    [Trait("Req", "DAT-880")]
    public void Uia_NavigatorButtonIsEnabledChangeRaised()
    {
        Session((probe, client, root) =>
        {
            var nav = Find(client, root, UiaIds.AutomationIdProperty, "Nav");
            IUIAutomationElement Button(string name) => Find(client, nav, UiaIds.NameProperty, name);
            void Invoke(string name) => ((IUIAutomationInvokePattern)Button(name).GetCurrentPattern(UiaIds.InvokePattern)!).Invoke();

            var (prior, post) = (Button("Prior record"), Button("Post edit"));
            Assert.True(prior.GetCurrentPropertyValue(UiaIds.IsEnabledProperty) is false, "Prior record disabled at the first record");
            Assert.True(post.GetCurrentPropertyValue(UiaIds.IsEnabledProperty) is false, "Post edit disabled while browsing");
            var (priorSink, postSink) = (new EventSink(probe.Changed), new EventSink(probe.Changed));
            client.AddPropertyChangedEventHandlerNativeArray(prior, TreeScope.Element, IntPtr.Zero, priorSink, [UiaIds.IsEnabledProperty], 1);
            client.AddPropertyChangedEventHandlerNativeArray(post, TreeScope.Element, IntPtr.Zero, postSink, [UiaIds.IsEnabledProperty], 1);
            Listen(probe);

            Invoke("Next record");
            Assert.Equal("state 2 0 dsBrowse", probe.Request(WM_PROBE_STATE, "state "));
            Assert.True(probe.WaitUntil(() => priorSink.Values(UiaIds.IsEnabledProperty) == "True"), "no IsEnabled True of Prior record; seen: " + priorSink);
            Invoke("Edit record");
            Assert.Equal("state 2 0 dsEdit", probe.Request(WM_PROBE_STATE, "state "));
            Assert.True(probe.WaitUntil(() => postSink.Values(UiaIds.IsEnabledProperty) == "True"), "no IsEnabled True of Post edit; seen: " + postSink);
            Invoke("Cancel edit");
            Assert.Equal("state 2 0 dsBrowse", probe.Request(WM_PROBE_STATE, "state "));
            Assert.True(probe.WaitUntil(() => postSink.Values(UiaIds.IsEnabledProperty) == "True False"), "no IsEnabled False of Post edit; seen: " + postSink);
            Invoke("First record");
            Assert.Equal("state 1 0 dsBrowse", probe.Request(WM_PROBE_STATE, "state "));
            Assert.True(probe.WaitUntil(() => priorSink.Values(UiaIds.IsEnabledProperty) == "True False"), "no IsEnabled False of Prior record; seen: " + priorSink);
            Assert.True(post.GetCurrentPropertyValue(UiaIds.IsEnabledProperty) is false, "Post edit disabled after Cancel");
            Assert.True(prior.GetCurrentPropertyValue(UiaIds.IsEnabledProperty) is false, "Prior record disabled after First");
        });
    }

    /// <summary>
    /// A-084 (DAT-880, UIF-1446): a change of the navigator's <c>VisibleButtons</c> invalidates its children
    /// (structure changed, children invalidated, with the navigator's runtime id): hiding the editing buttons removes
    /// them from the navigator's children, showing them again brings them back.
    /// </summary>
    [Fact]
    [Trait("Req", "DAT-880")]
    public void Uia_NavigatorVisibleButtonsInvalidateChildren()
    {
        Session((probe, client, root) =>
        {
            var nav = Find(client, root, UiaIds.AutomationIdProperty, "Nav");
            IUIAutomationElement? Button(string name) =>
                nav.FindFirst(TreeScope.Children, client.CreatePropertyCondition(UiaIds.NameProperty, name));
            var navId = string.Join(",", nav.GetRuntimeId());
            Assert.NotNull(Button("Delete record"));
            var sink = new EventSink(probe.Changed);
            client.AddStructureChangedEventHandler(nav, TreeScope.Element, IntPtr.Zero, sink);
            Listen(probe);

            Assert.Equal("changed 1", probe.Request(WM_PROBE_CHANGE, "changed ", 1));
            Assert.True(probe.WaitUntil(() => sink.Count("structure") >= 1), "no structure-changed event of the navigator after hiding buttons; seen: " + sink);
            Assert.Null(Button("Delete record"));
            Assert.NotNull(Button("Last record"));
            Assert.Equal("changed 2", probe.Request(WM_PROBE_CHANGE, "changed ", 2));
            Assert.True(probe.WaitUntil(() => sink.Count("structure") >= 2), "no structure-changed event of the navigator after showing buttons; seen: " + sink);
            Assert.NotNull(Button("Delete record"));
            Assert.All(sink.All(), e =>
            {
                Assert.Equal("structure", e.Kind);
                Assert.Equal(UiaIds.ChildrenInvalidated, e.Id);
                Assert.Equal(navId, e.Value as string);
            });
        });
    }

    /// <summary>
    /// A-084 (DAT-880, UIF-1446): the grid invalidates its children when its shown columns change (the second column
    /// hidden, then shown again: ColumnCount 1, then 2) and when the rows of its row window change (the current record
    /// deleted: RowCount 4), each time structure changed, children invalidated, with the grid's runtime id.
    /// </summary>
    [Fact]
    [Trait("Req", "DAT-880")]
    public void Uia_DbGridColumnAndRowChangesInvalidateChildren()
    {
        Session((probe, client, root) =>
        {
            var grid = Find(client, root, UiaIds.AutomationIdProperty, "Grid");
            var pattern = (IUIAutomationGridPattern)grid.GetCurrentPattern(UiaIds.GridPattern)!;
            Assert.Equal(2, pattern.CurrentColumnCount);
            Assert.Equal(5, pattern.CurrentRowCount);
            var gridId = string.Join(",", grid.GetRuntimeId());
            var sink = new EventSink(probe.Changed);
            client.AddStructureChangedEventHandler(grid, TreeScope.Element, IntPtr.Zero, sink);
            Listen(probe);

            Assert.Equal("changed 3", probe.Request(WM_PROBE_CHANGE, "changed ", 3));
            Assert.True(probe.WaitUntil(() => sink.Count("structure") >= 1), "no structure-changed event of the grid after hiding a column; seen: " + sink);
            Assert.Equal(1, pattern.CurrentColumnCount);
            Assert.Equal("changed 4", probe.Request(WM_PROBE_CHANGE, "changed ", 4));
            Assert.True(probe.WaitUntil(() => sink.Count("structure") >= 2), "no structure-changed event of the grid after showing a column; seen: " + sink);
            Assert.Equal(2, pattern.CurrentColumnCount);
            Assert.Equal("changed 5", probe.Request(WM_PROBE_CHANGE, "changed ", 5));
            Assert.True(probe.WaitUntil(() => sink.Count("structure") >= 3), "no structure-changed event of the grid after deleting a record; seen: " + sink);
            Assert.Equal(4, pattern.CurrentRowCount);
            Assert.All(sink.All(), e =>
            {
                Assert.Equal("structure", e.Kind);
                Assert.Equal(UiaIds.ChildrenInvalidated, e.Id);
                Assert.Equal(gridId, e.Value as string);
            });
        });
    }

    /// <summary>
    /// Builds and starts the probe, runs <paramref name="body"/> with a client and the probe's form element, then
    /// removes the client's handlers (bounded) and closes the probe; the probe is killed and the work folder deleted
    /// whatever happens.
    /// </summary>
    private static void Session(Action<ProbeProcess, IUIAutomation, IUIAutomationElement> body)
    {
        if (GoldenProgramRunner.MissingPrerequisites(DataCorpus.ToolchainDirectory, DataCorpus.OpcPath, DataCorpus.SqliteLibrary) is { } missing)
        {
            Assert.Fail(missing);
        }

        var ct = TestContext.Current.CancellationToken;
        var work = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", "data-uia-" + Guid.NewGuid().ToString("N"));
        var exe = Build(work, ct);
        using var probe = ProbeProcess.Start(exe, DateTime.UtcNow + Budget);
        var client = (IUIAutomation)new CUIAutomation();
        try
        {
            body(probe, client, client.ElementFromHandle(probe.Window));
            Assert.True(RemoveHandlers(client), "RemoveAllEventHandlers did not return within 5 s");
            probe.Close();
        }
        finally
        {
            probe.Dispose();
            RemoveHandlers(client);                                                  // after a failure: bounded
            GoldenProgramRunner.TryDelete(work);
        }
    }

    /// <summary>The first descendant of <paramref name="under"/> whose <paramref name="property"/> is <paramref name="value"/>; fails when there is none.</summary>
    private static IUIAutomationElement Find(IUIAutomation client, IUIAutomationElement under, int property, string value)
    {
        var found = under.FindFirst(TreeScope.Descendants, client.CreatePropertyCondition(property, value));
        Assert.True(found is not null, $"no element with {value}");
        return found;
    }

    /// <summary>
    /// Waits until the probe sees a listening client (<c>UiaClientsAreListening</c>, the condition of UIF-1440 for
    /// raising events): at most 100 requests, each answered by the probe.
    /// </summary>
    private static void Listen(ProbeProcess probe) =>
        Assert.True(
            Enumerable.Range(0, 100).Any(_ => probe.Request(WM_PROBE_LISTENING, "listening ") == "listening True"),
            "the probe does not see a listening client");

    /// <summary>Compiles the probe as the golden runner compiles a program (data, its test library and the UI framework on the unit path).</summary>
    private static string Build(string work, CancellationToken ct)
    {
        var bin = Directory.CreateDirectory(Path.Combine(work, "bin")).FullName;
        var exe = Path.Combine(bin, "DbAccessibilityProbe.exe");
        List<string> args =
        [
            ProbeSource, "-U", DataCorpus.SourceDirectory, "-U", DataCorpus.LibDirectory, "-U", DataCorpus.UiSourceDirectory,
            "-o", exe, "--obj-dir", Path.Combine(work, "obj"),
            "--rtl", DataCorpus.RtlDirectory, "--toolchain", DataCorpus.ToolchainDirectory,
        ];
        var opc = GoldenProgramRunner.RunProcessAsync(DataCorpus.OpcPath, args, Path.GetDirectoryName(ProbeSource)!, GoldenProgramRunner.CompileLimit, ct)
            .GetAwaiter().GetResult();
        Assert.True(opc.Stopped is null, "opc " + opc.Stopped);
        Assert.True(opc.ExitCode == 0, $"opc exit code {opc.ExitCode}\n{opc.StdOut}{opc.StdErr}");
        return exe;
    }

    /// <summary>Removes every event handler of the client on a thread of its own (the removal waits for handler calls under way): at most 5 s.</summary>
    private static bool RemoveHandlers(IUIAutomation client)
    {
        var removal = new Thread(client.RemoveAllEventHandlers) { IsBackground = true, Name = "client handler removal" };
        removal.Start();
        return removal.Join(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// The started probe: its stdout read line by line on a thread of its own, its window from the ready line, the
    /// deadline of the test. Every wait is woken by a new line, the probe's exit or a focus event (<see cref="Changed"/>),
    /// and ends at the deadline, where a watchdog kills the probe so that a client call into it ends too.
    /// </summary>
    private sealed class ProbeProcess : IDisposable
    {
        private readonly object gate = new();
        private readonly List<string> lines = [];
        private readonly StringBuilder errors = new();
        private readonly DateTime deadline;
        private readonly Timer watchdog;
        private readonly Thread reader;
        private readonly Thread errorReader;
        private bool ended;
        private int linesSeen;

        private ProbeProcess(Process process, DateTime deadline)
        {
            Process = process;
            this.deadline = deadline;
            reader = new Thread(() => Read(process.StandardOutput, lines)) { IsBackground = true, Name = "probe stdout" };
            errorReader = new Thread(() => ReadErrors(process.StandardError)) { IsBackground = true, Name = "probe stderr" };
            reader.Start();
            errorReader.Start();
            watchdog = new Timer(_ => Kill(), null, Remaining, Timeout.InfiniteTimeSpan);
        }

        public Process Process { get; }

        public IntPtr Window { get; private set; }

        private TimeSpan Remaining
        {
            get
            {
                var left = deadline - DateTime.UtcNow;
                return left > TimeSpan.Zero ? left : TimeSpan.Zero;
            }
        }

        public static ProbeProcess Start(string exe, DateTime deadline)
        {
            var psi = new ProcessStartInfo(exe)
            {
                WorkingDirectory = Path.GetDirectoryName(exe)!,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            psi.Environment["FXC_UI_NONINTERACTIVE"] = "1";
            var process = Process.Start(psi)!;
            Gui.AssignToHostJob(process);                                           // U-162: dies with the test host
            var probe = new ProbeProcess(process, deadline);
            try
            {
                string? ready = null;
                Assert.True(probe.WaitUntil(() => (ready = probe.FindLine("ready ", 0)) is not null), probe.Output("the ready line"));
                probe.Window = (IntPtr)long.Parse(ready!["ready ".Length..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return probe;
            }
            catch
            {
                probe.Dispose();
                throw;
            }
        }

        /// <summary>Wakes every wait (a focus event arrived).</summary>
        public void Changed()
        {
            lock (gate)
            {
                Monitor.PulseAll(gate);
            }
        }

        /// <summary>Re-evaluates <paramref name="condition"/> on every change until it holds, the probe ended or the deadline passed.</summary>
        public bool WaitUntil(Func<bool> condition)
        {
            lock (gate)
            {
                while (!condition())
                {
                    var left = Remaining;
                    if (ended || left == TimeSpan.Zero)
                    {
                        return condition();
                    }

                    Monitor.Wait(gate, left);
                }

                return true;
            }
        }

        /// <summary>Posts a marker message to the probe's form and returns the next new stdout line that starts with <paramref name="prefix"/>.</summary>
        public string Request(uint message, string prefix, nuint wParam = 0)
        {
            Assert.True(PostMessageW(Window, message, wParam, 0), "PostMessage failed");
            string? found = null;
            Assert.True(WaitUntil(() => (found = FindLine(prefix, linesSeen)) is not null), Output($"a line '{prefix}…'"));
            return found!;
        }

        /// <summary>Asks the probe to close and waits for its exit code 0.</summary>
        public void Close()
        {
            Assert.True(PostMessageW(Window, WM_CLOSE, 0, 0), "PostMessage WM_CLOSE failed");
            Assert.True(WaitUntil(() => ended), Output("the probe's exit"));
            Assert.True(Process.WaitForExit(Remaining), Output("the probe's exit"));
            Assert.Equal(0, Process.ExitCode);
        }

        public void Dispose()
        {
            watchdog.Dispose();
            Kill();
            Process.Dispose();
        }

        private string Output(string what)
        {
            lock (gate)
            {
                return $"{what} did not come (budget {Budget.TotalSeconds:F0} s, {Remaining.TotalSeconds:F1} s left, probe {(ended ? "ended" : "running")})\n"
                    + $"--- stdout\n{string.Join("\n", lines)}\n--- stderr\n{errors}";
            }
        }

        /// <summary>The first line from index <paramref name="from"/> that starts with <paramref name="prefix"/> (it and the lines before it count as seen).</summary>
        private string? FindLine(string prefix, int from)
        {
            lock (gate)
            {
                for (var i = from; i < lines.Count; i++)
                {
                    if (lines[i].StartsWith(prefix, StringComparison.Ordinal))
                    {
                        linesSeen = i + 1;
                        return lines[i];
                    }
                }

                return null;
            }
        }

        private void Read(StreamReader stream, List<string> target)
        {
            try
            {
                while (stream.ReadLine() is { } line)
                {
                    lock (gate)
                    {
                        target.Add(line.TrimEnd('\r'));
                        Monitor.PulseAll(gate);
                    }
                }
            }
            catch (IOException)
            {
                // the probe was killed
            }
            finally
            {
                lock (gate)
                {
                    ended = true;
                    Monitor.PulseAll(gate);
                }
            }
        }

        private void ReadErrors(StreamReader stream)
        {
            try
            {
                while (stream.ReadLine() is { } line)
                {
                    lock (gate)
                    {
                        errors.Append(line).Append('\n');
                    }
                }
            }
            catch (IOException)
            {
                // the probe was killed
            }
        }

        private void Kill()
        {
            try
            {
                if (!Process.HasExited)
                {
                    Process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // exited meanwhile
            }
        }

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessageW(IntPtr hwnd, uint msg, nuint wParam, nint lParam);
    }

    /// <summary>
    /// The client's focus-changed handler (a COM callable wrapper the system calls on its own threads). Focus-changed
    /// events come from the whole desktop; U-161: the handler keeps only those whose sender belongs to the probe's
    /// process, reading the sender's process id, control type and name from the cache the system filled before the call
    /// (<see cref="Prefetch"/>), so it never calls into another process; each kept event wakes the test's waits.
    /// </summary>
    [ComVisible(true)]
    private sealed class FocusSink(int processId, Action changed) : IUIAutomationFocusChangedEventHandler
    {
        private readonly List<(string Name, int Type)> events = [];
        private int others;

        /// <summary>The cache request of the handler: process id, control type and name of the sender itself.</summary>
        public static IUIAutomationCacheRequest Prefetch(IUIAutomation client)
        {
            var request = client.CreateCacheRequest();
            request.AddProperty(UiaIds.ProcessIdProperty);
            request.AddProperty(UiaIds.ControlTypeProperty);
            request.AddProperty(UiaIds.NameProperty);
            request.TreeFilter = client.CreateTrueCondition();                      // every sender, control view or not
            return request;
        }

        public void HandleFocusChangedEvent(IUIAutomationElement sender)
        {
            (string, int)? e;
            try
            {
                e = sender.GetCachedPropertyValue(UiaIds.ProcessIdProperty) is int pid && pid == processId
                    ? (sender.GetCachedPropertyValue(UiaIds.NameProperty) as string ?? "", sender.GetCachedPropertyValue(UiaIds.ControlTypeProperty) is int type ? type : 0)
                    : null;
            }
            catch (COMException)
            {
                e = null;                                                           // nothing cached: not one of the probe's
            }

            lock (events)
            {
                if (e is { } own)
                {
                    events.Add(own);
                }
                else
                {
                    others++;
                }
            }

            if (e is not null)
            {
                changed();
            }
        }

        /// <summary>The names of the probe's grid cells (DataItem senders) that received the focus, in order.</summary>
        public string[] Cells()
        {
            lock (events)
            {
                return [.. events.Where(e => e.Type == UiaIds.DataItem).Select(e => e.Name)];
            }
        }

        public override string ToString()
        {
            lock (events)
            {
                return string.Join("; ", events.Select(e => $"{UiaIds.Name(e.Type)} '{e.Name}'"))
                    + $" (and {others} of other processes)";
            }
        }
    }

    /// <summary>One event a client handler received: its kind (property, structure), the property or change-type id, and the new value (structure: the runtime id, comma-separated).</summary>
    private sealed record UiaEvent(string Kind, int Id, object? Value);

    /// <summary>
    /// The client's property-changed and structure-changed handler (a COM callable wrapper the system calls on its own
    /// threads): it records what arrives and wakes the test's waits.
    /// </summary>
    [ComVisible(true)]
    private sealed class EventSink(Action changed) : IUIAutomationPropertyChangedEventHandler, IUIAutomationStructureChangedEventHandler
    {
        private readonly List<UiaEvent> events = [];

        public void HandlePropertyChangedEvent(IUIAutomationElement sender, int propertyId, object? newValue) =>
            Add(new("property", propertyId, newValue));

        public void HandleStructureChangedEvent(IUIAutomationElement sender, int changeType, int[]? runtimeId) =>
            Add(new("structure", changeType, runtimeId is null ? null : string.Join(",", runtimeId)));

        public UiaEvent[] All()
        {
            lock (events)
            {
                return [.. events];
            }
        }

        public int Count(string kind) => All().Count(e => e.Kind == kind);

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

            changed();
        }
    }
}
