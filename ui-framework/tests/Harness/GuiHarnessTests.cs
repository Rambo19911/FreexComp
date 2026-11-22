namespace FreexComp.UiFramework.Tests;

/// <summary>
/// U-106: every facility of <c>sample-apps.md</c> §2 driven against the plain Win32 program <c>tests/gui/GuiProbe.pas</c>
/// (no framework unit involved, so the harness is tested independently of what it later tests).
/// </summary>
[Trait("Kind", "Native")]
[Trait("Category", "Toolchain")]
[Trait("Category", "Integration")]
[Trait("Category", "Foreground")]
[Collection(DesktopCollection.Name)]
public sealed class GuiHarnessTests
{
    [Fact]
    [Trait("Req", "UIF-980")]
    [Trait("Req", "UIF-1208")]               // a session that is never closed is killed (tree) on dispose
    public void GuiHarness_SelfTest()
    {
        Harness.RequireDesktop();
        Assert.Equal(1, Gui.Px(1, 96));
        Assert.Equal(13, Gui.Px(10, 120));                       // 12.5 rounds half away from zero
        Assert.Equal(-13, Gui.Px(-10, 120));
        Assert.Equal(500, Gui.Px(400, 120));

        var exe = Gui.BuildProgram(Path.Combine(UiCorpus.AreaDirectory, "tests", "gui", "GuiProbe.pas"));
        int pid;
        using (var app = Gui.StartGui(exe, ["first arg"], new Dictionary<string, string> { ["PROBE_VALUE"] = "42" }))
        {
            pid = app.Process.Id;
            Assert.Equal(IntPtr.Zero, Gui.WaitForMainWindow(app, "FxcGuiProbe", "Wrong Title", TimeSpan.FromMilliseconds(300)));
            var main = Gui.WaitForMainWindow(app, "FxcGuiProbe", "Probe Window");
            Assert.NotEqual(IntPtr.Zero, main);
            Assert.Equal(main, app.MainWindow);
            var dpi = Gui.Dpi(main);
            Assert.InRange(dpi, 96, 960);

            Assert.Equal((Gui.Px(400, dpi), Gui.Px(260, dpi)), Gui.ClientRectOf(main));
            var edit = Gui.FindDescendant(main, "edit");               // class names compare case-insensitively
            var push = Gui.FindDescendant(main, "BUTTON", "&Push");
            var info = Gui.FindDescendant(main, "Static");
            var status = Gui.FindDescendant(main, "msctls_statusbar32");
            Assert.All(new[] { edit, push, info, status }, h => Assert.NotEqual(IntPtr.Zero, h));
            Assert.Equal(IntPtr.Zero, Gui.FindDescendant(main, "BUTTON", "&Other"));
            Assert.Equal((Gui.Px(10, dpi), Gui.Px(50, dpi), Gui.Px(370, dpi) - Gui.Px(10, dpi), Gui.Px(70, dpi) - Gui.Px(50, dpi)), Gui.RectInParent(info));
            Assert.Equal((Gui.Px(170, dpi), Gui.Px(10, dpi), Gui.Px(245, dpi) - Gui.Px(170, dpi), Gui.Px(35, dpi) - Gui.Px(10, dpi)), Gui.RectInParent(push));

            Gui.SetText(edit, "typed text");
            Assert.Equal("typed text", Gui.GetText(edit));
            Assert.Equal("status text", Gui.RemoteText(status, Gui.SB_GETTEXTW, 0));

            Gui.Command(main, push, Gui.BN_CLICKED);
            Assert.Equal("cmd102,0", Gui.GetText(info));               // sent: handled before Command returns
            Gui.PostKey(main, Gui.VK_RETURN);
            Gui.PostClick(main, 12, 34);
            Gui.WaitIdle(app);
            Assert.Equal("cmd102,0;kd13;ku13;ld12,34;lu12,34", Gui.WaitForText(info, "cmd102,0;kd13;ku13;ld12,34;lu12,34"));

            Gui.Post(main, Gui.WM_CLOSE);
            Assert.True(app.WaitForExit(TimeSpan.FromSeconds(5)), "the probe closes on WM_CLOSE");
            Assert.Equal(7, app.Process.ExitCode);
            Assert.Equal("probe env=1 value=42 arg=first arg", app.StdOut.Trim());
            Assert.Equal("probe: close", app.StdErr.Trim());
        }

        // kill on dispose: a second instance that is never closed does not outlive its session
        Gui.GuiProcess other;
        using (other = Gui.StartGui(exe, [], new Dictionary<string, string>()))
        {
            Assert.NotEqual(IntPtr.Zero, Gui.WaitForMainWindow(other, "FxcGuiProbe", "Probe Window"));
            Assert.NotEqual(pid, other.Process.Id);
        }

        Assert.True(other.Process.HasExited);
    }

    /// <summary>
    /// U-126: <c>BringToForeground</c> takes the foreground back from another process that holds it (the foreground
    /// lock refuses a plain <c>SetForegroundWindow</c> from the test process then), three times in a row.
    /// </summary>
    [Fact]
    [Trait("Req", "UIF-1203")]
    public void GuiHarness_BringToForegroundFromAnotherProcess()
    {
        Harness.RequireDesktop();
        var exe = Gui.BuildProgram(Path.Combine(UiCorpus.AreaDirectory, "tests", "gui", "GuiProbe.pas"));
        using var app = Gui.StartGui(exe, [], new Dictionary<string, string>());
        var main = Gui.WaitForMainWindow(app, "FxcGuiProbe", "Probe Window");
        Assert.NotEqual(IntPtr.Zero, main);
        for (var round = 1; round <= 3; round++)
        {
            using var holder = Gui.StartForegroundHolder();
            Assert.True(Gui.Foreground() != main, $"round {round}: the probe still holds the foreground, so the case is not tested");
            Assert.True(Gui.BringToForeground(main), $"round {round}: the foreground stays {Gui.Foreground():X} ('{Gui.GetText(Gui.Foreground())}'), not the probe {main:X}");
            Assert.Equal(main, Gui.Foreground());
        }

        Gui.Post(main, Gui.WM_CLOSE);
        Assert.True(app.WaitForExit(TimeSpan.FromSeconds(5)));
    }
}
