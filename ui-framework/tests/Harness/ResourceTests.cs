namespace FreexComp.UiFramework.Tests;

/// <summary>Group E (testing.md §3.6): GDI and USER object counts of a child process around a stress loop (U-112).</summary>
[Trait("Kind", "Native")]
[Trait("Category", "Toolchain")]
[Trait("Category", "Foreground")]
[Collection(DesktopCollection.Name)]
public sealed class ResourceTests
{
    /// <summary>
    /// UIF-576: <c>tests/gui/Gdi_NoLeak.pas</c> creates, paints and destroys 200 forms with one control of every Phase 4
    /// class; GDI and USER counts after the warm-up cycle and at the end differ by at most 2 (each).
    /// </summary>
    [Fact]
    [Trait("Req", "UIF-576")]
    public void Gdi_NoLeak()
    {
        Harness.RequireDesktop();
        var exe = Gui.BuildProgram(Path.Combine(UiCorpus.AreaDirectory, "tests", "gui", "Gdi_NoLeak.pas"));
        using var app = Gui.StartGui(exe, [], new Dictionary<string, string>());
        Assert.True(app.WaitForOutputLine("warm"), "no 'warm' line\n--- stdout\n" + app.StdOut + "\n--- stderr\n" + app.StdErr);
        var (gdi0, user0) = Counts(app);
        app.SendLine("go");
        Assert.True(app.WaitForOutputLine("done"), "no 'done' line\n--- stdout\n" + app.StdOut + "\n--- stderr\n" + app.StdErr);
        var (gdi1, user1) = Counts(app);
        app.SendLine("end");
        Assert.True(app.WaitForExit(), app.ExitFailure("Gdi_NoLeak.exe, after 'end',"));
        Assert.Equal(0, app.Process.ExitCode);
        Assert.Equal("", app.StdErr);
        Assert.True(gdi1 - gdi0 <= 2 && user1 - user0 <= 2, $"GDI {gdi0} -> {gdi1}, USER {user0} -> {user1} after 200 cycles");
    }

    internal static (int Gdi, int User) Counts(Gui.GuiProcess app) =>
        (Gui.GuiResources(app.Process, Gui.GR_GDIOBJECTS), Gui.GuiResources(app.Process, Gui.GR_USEROBJECTS));
}
