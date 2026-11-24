using System.Text;

namespace FreexComp.UiFramework.Tests;

/// <summary>Group C tests of the sample <c>ui-framework/samples/hello-form</c> (<c>sample-apps.md</c> §3); each test starts its own process.</summary>
[Trait("Kind", "Native")]
[Trait("Category", "Toolchain")]
[Trait("Category", "Integration")]
[Collection(DesktopCollection.Name)]
public sealed class HelloFormTests : IDisposable
{
    private readonly string temp = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", "hello-" + Guid.NewGuid().ToString("N"));

    public HelloFormTests() => Directory.CreateDirectory(temp);

    public static string SampleDirectory => Path.Combine(UiCorpus.AreaDirectory, "samples", "hello-form");

    public void Dispose() => Rtl.Tests.Harness.GoldenRunner.TryDelete(temp);

    /// <summary>§3.3: dump mode shows no window while it runs, exits 0 by itself, and the dump equals the hand-written expectation.</summary>
    [Fact]
    [Trait("Req", "UIF-310")]
    [Trait("Req", "UIF-312")]
    [Trait("Req", "UIF-986")]
    public void HelloForm_DumpLayout()
    {
        var exe = Gui.BuildSample(SampleDirectory);
        var dump = Path.Combine(temp, "hello.json");
        using var app = Gui.StartGui(exe, ["--dump-layout", dump], new Dictionary<string, string>());

        // U-139: watched until the process exits (an event), not for a fixed second: no window appears in the whole run
        Assert.Equal(IntPtr.Zero, Gui.WaitForMainWindow(app, "PN.TForm", "Hello Form"));
        Assert.True(app.WaitForExit(), app.ExitFailure("Hello.exe --dump-layout"));
        Assert.True(app.Process.ExitCode == 0, $"exit code {app.Process.ExitCode}\n--- stderr\n{app.StdErr}");
        var expected = File.ReadAllText(Path.Combine(SampleDirectory, "expected-layout.json"), Encoding.UTF8);
        var actual = File.ReadAllText(dump, Encoding.UTF8);
        Assert.True(LayoutHost.Compare(expected, actual) is null, $"{LayoutHost.Compare(expected, actual)}\n--- actual\n{actual}");
    }

    /// <summary>§3.1, the Phase 4 exit test: real windows of the sample driven by messages, steps 1 … 14.</summary>
    [Fact]
    [Trait("Category", "Foreground")]
    [Trait("Req", "UIF-980")]
    [Trait("Req", "UIF-280")]
    [Trait("Req", "UIF-231")]
    [Trait("Req", "UIF-270")]                // per-monitor-v2 aware: client and children in device pixels of the window's DPI
    [Trait("Req", "UIF-1205")]               // every geometry assertion is a formula scaled with the window's DPI
    public void HelloForm_EndToEnd()
    {
        Harness.RequireDesktop();
        var log = Path.Combine(temp, "events.log");
        using var app = Start(log);                                                                   // step 1
        var form = Gui.WaitForMainWindow(app, "PN.TForm", "Hello Form");                             // step 2
        Assert.True(form != IntPtr.Zero, "main window not found\n" + app.StdErr);
        var wasForeground = Gui.Foreground() == form;
        Assert.True(Gui.IsVisible(form));
        var dpi = Gui.Dpi(form);
        int E(int v) => Gui.Px(v, dpi);
        (int, int, int, int) Edges(int l, int t, int w, int h) => (E(l), E(t), E(l + w) - E(l), E(t + h) - E(t));

        Assert.Equal((E(400), E(240)), Gui.ClientRectOf(form));                                        // step 3

        var menu = Gui.Menu(form);                                                                     // step 4
        Assert.NotEqual(IntPtr.Zero, menu);
        Assert.Equal(1, Gui.MenuItemCount(menu));
        var fileMenu = Gui.SubMenu(menu, 0);
        Assert.Equal(1, Gui.MenuItemCount(fileMenu));
        Assert.Equal("E&xit\tCtrl+Q", Gui.MenuItemText(fileMenu, 0));

        var edit = Gui.FindDescendant(form, "EDIT");                                                   // step 5
        var greet = Gui.FindDescendant(form, "BUTTON", "&Greet");
        var clear = Gui.FindDescendant(form, "BUTTON", "C&lear");
        var result = Gui.FindDescendant(form, "STATIC");
        var check = Gui.FindDescendant(form, "BUTTON", "&Upper case");
        var list = Gui.FindDescendant(form, "LISTBOX");
        var status = Gui.FindDescendant(form, "msctls_statusbar32");
        foreach (var (name, h) in new[] { ("edit", edit), ("greet", greet), ("clear", clear), ("result", result), ("check", check), ("list", list), ("status", status) })
        {
            Assert.True(h != IntPtr.Zero, name + " not found");
            Assert.True(Gui.ParentOf(h) == form, name + " is not a direct child of the form");
        }

        Assert.Equal(Edges(16, 16, 200, 23), Gui.RectInParent(edit));                                   // step 6
        Assert.Equal(Edges(232, 15, 75, 25), Gui.RectInParent(greet));
        Assert.Equal(Edges(315, 15, 75, 25), Gui.RectInParent(clear));
        Assert.Equal(Edges(16, 52, 374, 17), Gui.RectInParent(result));
        Assert.Equal(Edges(16, 80, 150, 17), Gui.RectInParent(check));
        Assert.Equal(Edges(16, 108, 374, 90), Gui.RectInParent(list));
        Assert.Equal((0, E(221), E(400), E(240) - E(221)), Gui.RectInParent(status));

        Gui.SetText(edit, "World");                                                                    // step 7
        Gui.Command(form, greet, Gui.BN_CLICKED);
        Assert.Equal("Hello, World!", Gui.GetText(result));
        Assert.Equal(1, Gui.Send(list, Gui.LB_GETCOUNT, 0, 0));
        Assert.Equal("Hello, World!", Gui.ListBoxText(list, 0));
        Assert.Equal("1 greeting(s)", StatusText(status));

        Gui.Command(form, check, Gui.BN_CLICKED);                                                      // step 8
        Assert.Equal(1, Gui.Send(check, Gui.BM_GETCHECK, 0, 0));

        Gui.PostKey(edit, Gui.VK_RETURN);                                                              // step 9: Enter -> default button
        Assert.Equal("HELLO, WORLD!", Gui.WaitForText(result, "HELLO, WORLD!"));
        Assert.Equal(2, Gui.Send(list, Gui.LB_GETCOUNT, 0, 0));
        Assert.Equal("2 greeting(s)", StatusText(status));

        Gui.PostKey(edit, Gui.VK_ESCAPE);                                                              // step 10: Escape -> cancel button
        Assert.True(Gui.WaitUntil(() => Gui.Send(list, Gui.LB_GETCOUNT, 0, 0) == 0), "Escape did not clear the list");
        Assert.Equal("", Gui.GetText(edit));
        Assert.Equal("", Gui.GetText(result));
        Assert.Equal("Ready", StatusText(status));

        Gui.PostClick(greet, E(10), E(10));                                                            // step 11: mouse path
        Assert.Equal("Please enter a name.", Gui.WaitForText(result, "Please enter a name."));

        var outer = Gui.WindowSize(form);                                                              // step 12
        Gui.SetWindowSize(form, outer.Width + E(100), outer.Height + E(60));
        Gui.WaitIdle(app);
        Assert.Equal((E(400) + E(100), E(240) + E(60)), Gui.ClientRectOf(form));
        var (cw, ch) = Gui.ClientRectOf(form);
        Assert.True(Gui.WaitUntil(() => Gui.RectInParent(list).Height == Edges(16, 108, 374, 90).Item4 + E(60)), "the list did not follow the resize");
        Assert.Equal(Grow(Edges(16, 16, 200, 23), 0, 0, E(100), 0), Gui.RectInParent(edit));
        Assert.Equal(Grow(Edges(232, 15, 75, 25), E(100), 0, 0, 0), Gui.RectInParent(greet));
        Assert.Equal(Grow(Edges(315, 15, 75, 25), E(100), 0, 0, 0), Gui.RectInParent(clear));
        Assert.Equal(Grow(Edges(16, 52, 374, 17), 0, 0, E(100), 0), Gui.RectInParent(result));
        Assert.Equal(Edges(16, 80, 150, 17), Gui.RectInParent(check));
        Assert.Equal(Grow(Edges(16, 108, 374, 90), 0, 0, E(100), E(60)), Gui.RectInParent(list));
        var bar = Gui.RectInParent(status);
        Assert.Equal((0, cw, ch), (bar.Left, bar.Width, bar.Top + bar.Height));

        var exitId = Gui.MenuItemId(Gui.SubMenu(Gui.Menu(form), 0), 0);                               // step 13
        Gui.Post(form, Gui.WM_COMMAND, exitId, 0);
        Assert.True(app.WaitForExit(), app.ExitFailure("the program, after File > Exit,"));
        Assert.Equal(0, app.Process.ExitCode);
        Assert.Equal("", app.StdErr);

        var events = File.ReadAllLines(log).Where(l => !l.StartsWith("enter:", StringComparison.Ordinal)).ToList();   // step 14
        List<string> expected = ["OnCreate", "OnShow", $"OnResize {E(400)}x{E(240)}", "OnPaint", $"OnResize {E(400) + E(100)}x{E(240) + E(60)}", "OnCloseQuery", "OnClose", "OnHide", "OnDestroy"];
        if (wasForeground || events.Contains("OnActivate"))
        {
            expected.Insert(2, "OnActivate");                    // required when foreground at step 2, else optional; always before the first OnResize
        }

        Assert.True(expected.SequenceEqual(events), $"event log\n--- expected\n{string.Join("\n", expected)}\n--- actual\n{string.Join("\n", File.ReadAllLines(log))}");
    }

    /// <summary>§3.2: an exception in an event handler is reported on stderr and the program goes on; exit code 1 afterwards.</summary>
    [Fact]
    [Trait("Category", "Foreground")]
    [Trait("Req", "UIF-462")]
    [Trait("Req", "UIF-013")]
    public void HelloForm_ExceptionIsReported()
    {
        Harness.RequireDesktop();
        using var app = Start(null);
        var form = Gui.WaitForMainWindow(app, "PN.TForm", "Hello Form");
        Assert.True(form != IntPtr.Zero, "main window not found\n" + app.StdErr);
        var edit = Gui.FindDescendant(form, "EDIT");
        var greet = Gui.FindDescendant(form, "BUTTON", "&Greet");
        var result = Gui.FindDescendant(form, "STATIC");

        Gui.SetText(edit, "!raise");
        Gui.Command(form, greet, Gui.BN_CLICKED);
        Gui.WaitIdle(app);                                                                             // alive and responsive
        Assert.False(app.Process.HasExited);
        Assert.True(Gui.WaitUntil(() => app.StdErr.Split('\n').Contains("Hello: Exception: Test exception")), "stderr:\n" + app.StdErr);
        Assert.Equal("", Gui.GetText(result));

        Gui.Post(form, Gui.WM_CLOSE);
        Assert.True(app.WaitForExit(), app.ExitFailure("the program, after WM_CLOSE,"));
        Assert.Equal(1, app.Process.ExitCode);
    }

    /// <summary>
    /// §3.4: Tab moves the focus in TabOrder, skipping the static text (needs real focus: the foreground). U-126: another
    /// process takes the foreground first, as in a parallel run, and the harness must win it back; it never skips.
    /// </summary>
    [Fact]
    [Trait("Category", "Foreground")]
    [Trait("Req", "UIF-100")]
    [Trait("Req", "UIF-115")]
    public void HelloForm_TabOrder()
    {
        Harness.RequireDesktop();
        var log = Path.Combine(temp, "events.log");
        using var app = Start(log);
        var form = Gui.WaitForMainWindow(app, "PN.TForm", "Hello Form");
        Assert.True(form != IntPtr.Zero, "main window not found\n" + app.StdErr);
        using (Gui.StartForegroundHolder())
        {
            Assert.True(Gui.Foreground() != form, "the form still holds the foreground, so the case is not tested");
            Assert.True(Gui.BringToForeground(form), $"the foreground stays {Gui.Foreground():X} ('{Gui.GetText(Gui.Foreground())}'), not the form {form:X}: focus cannot be observed");
        }

        var edit = Gui.FindDescendant(form, "EDIT");
        Gui.WaitIdle(app);
        for (var i = 0; i < 3; i++)
        {
            Gui.PostKey(edit, Gui.VK_TAB);
        }

        List<string> expected = ["enter: NameEdit", "enter: GreetButton", "enter: ClearButton", "enter: UpperCheck"];
        List<string> Entered() => [.. ReadShared(log).Where(l => l.StartsWith("enter:", StringComparison.Ordinal))];
        Gui.WaitUntil(() => Entered().Count >= expected.Count);
        Assert.True(expected.SequenceEqual(Entered()), $"--- expected\n{string.Join("\n", expected)}\n--- actual\n{string.Join("\n", ReadShared(log))}");
        Gui.Post(form, Gui.WM_CLOSE);
        Assert.True(app.WaitForExit(), app.ExitFailure("the program, after WM_CLOSE,"));
        Assert.Equal(0, app.Process.ExitCode);
    }

    /// <summary>
    /// U-134, controls-standard.md UIF-726: main-menu keyboard access is Win32 default processing of the form window that
    /// our key pre-processing lets through. With the form in the foreground (taken from the foreground holder, U-127),
    /// Alt alone and F10 alone (system key down and up, as the keyboard driver posts them) put the form's thread into
    /// menu mode with the form as menu owner and no menu window open (the bar is selected), Escape leaves it; Alt+F (with
    /// its <c>WM_SYSCHAR</c>) opens the File drop-down (a visible menu window of the thread) and X then chooses E&amp;xit, which ends the program with exit code 0.
    /// </summary>
    [Fact]
    [Trait("Category", "Foreground")]
    [Trait("Req", "UIF-726")]
    public void HelloForm_MenuKeyboardAccess()
    {
        Harness.RequireDesktop();
        using var holder = Gui.StartForegroundHolder();
        var log = Path.Combine(temp, "events.log");
        using var app = Start(log);
        var form = Gui.WaitForMainWindow(app, "PN.TForm", "Hello Form");
        Assert.True(form != IntPtr.Zero, "main window not found\n" + app.StdErr);
        Gui.WaitIdle(app);
        var holderWindow = Gui.WaitForMainWindow(holder, "FxcForegroundHolder", "Foreground Holder");
        if (Gui.Foreground() != holderWindow)
        {
            Assert.True(Gui.BringToForeground(holderWindow), $"the holder did not get the foreground (it is {Gui.Foreground():X})");
        }

        Assert.True(Gui.BringToForeground(form), $"the form did not get the foreground (it is {Gui.Foreground():X})");
        Gui.WaitIdle(app);
        bool InMenu() => (Gui.GuiThreadFlags(form) & Gui.GUI_INMENUMODE) != 0 && Gui.MenuOwner(form) == form;
        bool InPopup() => Gui.OpenMenuWindows(form) > 0;
        Assert.False(InMenu(), "the form is in menu mode before any key");

        foreach (var (key, name) in new[] { (Gui.VK_MENU, "Alt"), (Gui.VK_F10, "F10") })
        {
            Gui.PostSysKey(form, key, altDown: key == Gui.VK_MENU);
            Assert.True(Gui.WaitUntil(InMenu), $"{name} did not enter menu mode (flags 0x{Gui.GuiThreadFlags(form):X}, menu owner {Gui.MenuOwner(form):X})");
            Assert.False(InPopup(), $"{name} opened a menu instead of selecting the menu bar");
            Gui.PostKey(form, Gui.VK_ESCAPE);
            Assert.True(Gui.WaitUntil(() => !InMenu()), $"Escape did not leave the menu mode entered by {name}");
        }

        Gui.PostSysKey(form, 'F', altDown: true, character: 'f');
        Assert.True(Gui.WaitUntil(InPopup), $"Alt+F did not open the File menu (flags 0x{Gui.GuiThreadFlags(form):X})");
        Gui.Post(form, Gui.WM_KEYDOWN, 'X', 1);
        Assert.True(app.WaitForExit(), app.ExitFailure("the program, after X in the File menu (Exit),"));
        Assert.Equal(0, app.Process.ExitCode);
        Assert.Contains("OnClose", ReadShared(log));
    }

    /// <summary>§3.5: the running sample has comctl32 version 6 loaded (RTL-870/871: the default GUI manifest).</summary>
    [Fact]
    [Trait("Category", "Foreground")]
    [Trait("Req", "UIF-402")]
    public void ComCtl32IsVersion6()
    {
        Harness.RequireDesktop();
        using var app = Start(null);
        var form = Gui.WaitForMainWindow(app, "PN.TForm", "Hello Form");
        Assert.True(form != IntPtr.Zero, "main window not found\n" + app.StdErr);
        app.Process.Refresh();
        var comctl = app.Process.Modules.Cast<System.Diagnostics.ProcessModule>()
            .Where(m => string.Equals(m.ModuleName, "comctl32.dll", StringComparison.OrdinalIgnoreCase))
            .Select(m => System.Diagnostics.FileVersionInfo.GetVersionInfo(m.FileName))
            .ToList();
        Assert.NotEmpty(comctl);
        Assert.All(comctl, v => Assert.True(v.FileMajorPart >= 6, $"{v.FileName} is version {v.FileVersion}"));
        Gui.Post(form, Gui.WM_CLOSE);
        Assert.True(app.WaitForExit(), app.ExitFailure("the program, after WM_CLOSE,"));
    }

    /// <summary>
    /// §3.6: 200 alternating resizes (±E(50)) grow neither the GDI nor the USER object count by more than 2.
    /// U-136: the measurement must not depend on activation changes made by other windows (a parallel test process, the
    /// holder of the previous test going away): a focus change of the themed <c>NameEdit</c> during the measurement adds
    /// the theme library's buffered border-animation buffers (+4 DC, +4 bitmap, +2 region, U-127), so the focus is first
    /// moved to <c>GreetButton</c> (Tab, as §3.7), and every <see cref="ResizeHandOverEvery"/> resizes the foreground holder
    /// takes the foreground and the form takes it back, so that this independence is tested on every run.
    /// </summary>
    [Fact]
    [Trait("Category", "Foreground")]
    [Trait("Req", "UIF-576")]
    public void HelloForm_NoGdiLeakOnResize()
    {
        Harness.RequireDesktop();
        using var holder = Gui.StartForegroundHolder();
        var holderWindow = Gui.WaitForMainWindow(holder, "FxcForegroundHolder", "Foreground Holder");
        var log = Path.Combine(temp, "events.log");
        using var app = Start(log);
        var form = Gui.WaitForMainWindow(app, "PN.TForm", "Hello Form");
        Assert.True(form != IntPtr.Zero, "main window not found\n" + app.StdErr);

        void HandOver(string when)
        {
            Assert.True(Gui.BringToForeground(holderWindow), $"{when}: the holder did not get the foreground (it is {Gui.Foreground():X})");
            Gui.WaitIdle(app);
            Assert.True(Gui.BringToForeground(form), $"{when}: the form did not get the foreground (it is {Gui.Foreground():X})");
            Gui.WaitIdle(app);
        }

        Assert.True(Gui.BringToForeground(form), $"the form did not get the foreground (it is {Gui.Foreground():X})");
        Gui.WaitIdle(app);
        Gui.PostKey(Gui.FindDescendant(form, "EDIT"), Gui.VK_TAB);
        Assert.True(Gui.WaitUntil(() => ReadShared(log).Contains("enter: GreetButton")), "Tab did not move the focus to GreetButton\n" + string.Join("\n", ReadShared(log)));
        var step = Gui.Px(50, Gui.Dpi(form));
        var (width, height) = Gui.WindowSize(form);
        // warm-up cycle (as UIF-576): one resize pair and a synchronous repaint, so objects created by the first paint
        // and the first layout pass exist before the baseline even when the machine is loaded; no hand-over here: one
        // would fill the edit's animation cache before the baseline and hide a focused NameEdit (U-136)
        Gui.SetWindowSize(form, width + step, height + step);
        Gui.SetWindowSize(form, width, height);
        Gui.Repaint(form);
        Gui.WaitIdle(app);
        var (gdi0, user0) = ResourceTests.Counts(app);
        var types0 = Gui.GdiTypeCounts(app.Process);
        for (var i = 0; i < 200; i++)
        {
            var delta = i % 2 == 0 ? step : 0;
            Gui.SetWindowSize(form, width + delta, height + delta);
            if (i % ResizeHandOverEvery == ResizeHandOverEvery / 2)
            {
                HandOver($"resize {i}");
            }
        }

        Gui.Repaint(form);
        Gui.WaitIdle(app);
        var (gdi1, user1) = ResourceTests.Counts(app);
        Assert.True(gdi1 - gdi0 <= 2 && user1 - user0 <= 2,
            $"GDI {gdi0} -> {gdi1}, USER {user0} -> {user1} after 200 resizes; GDI objects by type {types0} -> {Gui.GdiTypeCounts(app.Process)}");
        Assert.DoesNotContain("enter: NameEdit", ReadShared(log).SkipWhile(l => l != "enter: GreetButton"));
        Gui.Post(form, Gui.WM_CLOSE);
        Assert.True(app.WaitForExit(), app.ExitFailure("the program, after WM_CLOSE,"));
        Assert.Equal(0, app.Process.ExitCode);
    }

    /// <summary>U-136: <see cref="HelloForm_NoGdiLeakOnResize"/> hands the foreground over once per this many resizes (5 times).</summary>
    private const int ResizeHandOverEvery = 40;

    /// <summary>
    /// U-127: activation cycles (the form takes the foreground from the foreground holder, then the holder takes it
    /// back, both through <see cref="Gui.BringToForeground"/>) grow neither the GDI nor the USER object count by more
    /// than 2 at any point of <see cref="ActivationCycles"/> cycles (the maximum over the cycles, U-130) after a warm-up of
    /// <see cref="ActivationWarmUp"/> cycles; every cycle must really activate the form exactly once (one <c>OnActivate</c>
    /// per cycle in the event log, counted after the holder took the foreground back and the program went idle). The focus is first moved
    /// from <c>NameEdit</c> to <c>GreetButton</c> (Tab), so each activation focuses the default button (default-button
    /// styles, <c>WM_CTLCOLORBTN</c>, UIF-092 focus restore) and not the edit: a focus change of a themed EDIT animates
    /// its border through uxtheme's buffered-animation cache (a memory DC, bitmap and region per buffer), which grows
    /// by about 8 objects per back-to-back cycle to a bounded plateau that keeps oscillating by 8 (+70 … +90 objects,
    /// the same with a plain Win32 EDIT, none with an unthemed one); it is not a framework object and would make the
    /// count meaningless without wall-clock waits for the animation.
    /// </summary>
    [Fact]
    [Trait("Category", "Foreground")]
    [Trait("Req", "UIF-576")]
    public void HelloForm_NoGdiLeakOnActivate()
    {
        Harness.RequireDesktop();
        using var holder = Gui.StartForegroundHolder();
        var holderWindow = Gui.WaitForMainWindow(holder, "FxcForegroundHolder", "Foreground Holder");
        var log = Path.Combine(temp, "events.log");
        using var app = Start(log);
        var form = Gui.WaitForMainWindow(app, "PN.TForm", "Hello Form");
        Assert.True(form != IntPtr.Zero, "main window not found\n" + app.StdErr);
        Gui.WaitIdle(app);
        var (gdiStart, userStart) = ResourceTests.Counts(app);
        int Activations() => ReadShared(log).Count(l => l == "OnActivate");

        void Cycle(int i)
        {
            var before = Activations();
            Assert.True(Gui.BringToForeground(form), $"cycle {i}: the form did not get the foreground (it is {Gui.Foreground():X})");
            Gui.WaitIdle(app);
            Assert.True(Gui.WaitUntil(() => Activations() > before), $"cycle {i}: the form is the foreground but was not activated (no OnActivate)");
            Assert.True(Gui.BringToForeground(holderWindow), $"cycle {i}: the holder did not get the foreground back (it is {Gui.Foreground():X})");
            Gui.WaitIdle(app);
            var count = Activations() - before;                                                    // U-130: exactly one per cycle
            Assert.True(count == 1, $"cycle {i}: {count} OnActivate lines, expected exactly 1\n{string.Join("\n", ReadShared(log))}");
        }

        Assert.True(Gui.BringToForeground(holderWindow), $"the holder did not get the foreground (it is {Gui.Foreground():X})");
        Assert.True(Gui.BringToForeground(form), $"the form did not get the foreground (it is {Gui.Foreground():X})");
        Gui.WaitIdle(app);
        Gui.PostKey(Gui.FindDescendant(form, "EDIT"), Gui.VK_TAB);
        Assert.True(Gui.WaitUntil(() => ReadShared(log).Contains("enter: GreetButton")), "Tab did not move the focus to GreetButton\n" + string.Join("\n", ReadShared(log)));
        Assert.True(Gui.BringToForeground(holderWindow), $"the holder did not get the foreground (it is {Gui.Foreground():X})");
        Gui.WaitIdle(app);

        for (var i = 0; i < ActivationWarmUp; i++)
        {
            Cycle(i);
        }

        var (gdi0, user0) = ResourceTests.Counts(app);
        int gdiMax = gdi0, userMax = user0;
        for (var i = 0; i < ActivationCycles; i++)
        {
            Cycle(ActivationWarmUp + i);
            var (g, u) = ResourceTests.Counts(app);
            gdiMax = Math.Max(gdiMax, g);
            userMax = Math.Max(userMax, u);
        }

        var (gdi1, user1) = ResourceTests.Counts(app);
        Assert.True(gdiMax - gdi0 <= 2 && userMax - user0 <= 2,                                    // U-130: the max over the cycles (§3.7)
            $"over {ActivationCycles} activation cycles: GDI {gdi0} -> {gdi1} (max {gdiMax}), USER {user0} -> {user1} (max {userMax}); " +
            $"at start GDI {gdiStart}, USER {userStart}, before the warm-up of {ActivationWarmUp} cycles");
        Assert.DoesNotContain("enter: NameEdit", ReadShared(log).SkipWhile(l => l != "enter: GreetButton"));
        Gui.Post(form, Gui.WM_CLOSE);
        Assert.True(app.WaitForExit(), app.ExitFailure("the program, after WM_CLOSE,"));
        Assert.Equal(0, app.Process.ExitCode);
    }

    /// <summary>U-127: warm-up cycles of <see cref="HelloForm_NoGdiLeakOnActivate"/> (theme parts opened by the first).</summary>
    private const int ActivationWarmUp = 5;

    /// <summary>U-127: measured cycles of <see cref="HelloForm_NoGdiLeakOnActivate"/>.</summary>
    private const int ActivationCycles = 50;

    /// <summary>The event log while the program may still append to it.</summary>
    private static string[] ReadShared(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }
        catch (IOException)
        {
            return [];                                                // the program holds it exclusively this moment
        }
    }

    private static (int, int, int, int) Grow((int L, int T, int W, int H) r, int dx, int dy, int dw, int dh) => (r.L + dx, r.T + dy, r.W + dw, r.H + dh);

    /// <summary>
    /// The simple-mode text of the status bar through a target buffer. comctl32 6 answers <c>SB_GETTEXTW</c> for
    /// <c>SB_SIMPLEID</c> (255, sample-apps.md §3.1 step 7) with an empty text; the simple text is read back at part 0
    /// (lead note U-083…U-085).
    /// </summary>
    private static string StatusText(IntPtr status) => Gui.RemoteText(status, Gui.SB_GETTEXTW, 0);

    private static Gui.GuiProcess Start(string? log)
    {
        var exe = Gui.BuildSample(SampleDirectory);
        var env = new Dictionary<string, string>();
        if (log is not null)
        {
            env["HELLO_EVENT_LOG"] = log;
        }

        return Gui.StartGui(exe, [], env);
    }
}
