using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Designer;
using FreexComp.Ide.Tests.EndToEnd;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// Backlog card F11-008: a form is always saved in 96-DPI logical units (form-format.md §2 rule 3), whatever the
/// scale of the display it was designed on.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class FormSaveScaleTests
{
    /// <summary>
    /// The same design session (a control dragged, a control placed, the form resized by its handle, Ctrl+S) on a
    /// 100 % and on a 200 % display writes byte-identical <c>.form</c> files.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TOK-063")]
    public async Task SameDesignAtScale1And2SavesIdenticalFormBytes()
    {
        var at100 = await DesignAndSaveAsync(1.0);
        var at200 = await DesignAndSaveAsync(2.0);

        Assert.Equal(at100, at200);
        var text = System.Text.Encoding.UTF8.GetString(at100).ReplaceLineEndings("\n");
        Assert.Contains("  PixelsPerInch = 96\n", text, StringComparison.Ordinal);
        Assert.Contains("    Left = 248\n    Top = 24\n", text, StringComparison.Ordinal); // the drag, in logical pixels
        Assert.Contains("  object Label1: TLabel\n    Left = 40\n    Top = 120\n", text, StringComparison.Ordinal); // the placement
        Assert.Contains("  ClientWidth = 464\n  ClientHeight = 344\n", text, StringComparison.Ordinal); // the resize
    }

    private static async Task<byte[]> DesignAndSaveAsync(double scale)
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            DesignerViewTests.Files(o);
            o.Scale = scale;
        });
        Assert.Equal(scale, h.Window.RenderScaling);
        var document = await DesignerViewTests.OpenFormAsync(h);
        h.Click(AutomationIds.DocumentViewTab("MainForm.pas", "Design"));
        Assert.Equal(DocumentView.Design, document.ActiveView);
        Dispatcher.UIThread.RunJobs();
        var s = new Phase5Scenario.Session(h, document, h.Find<DesignSurfaceControl>(AutomationIds.Designer("MainForm")));

        // A drag of the button by (13, 6) surface units, snapped to the grid.
        var (x, y) = s.Center("GreetButton");
        s.Down(x, y);
        s.Move(x + 6, y + 3);
        s.Move(x + 13, y + 6);
        s.Up(x + 13, y + 6);

        // A label placed by a click with the armed palette item.
        var client = s.Surface.Geometry.FormClientRect;
        document.Designer.Host.Arm("TLabel", sticky: false);
        s.Click(client.Left + 40, client.Top + 120);
        Assert.Equal(["Label1"], s.Selected);

        // The form selected by a click on its empty client area, then resized by its SE handle.
        s.Click(client.Right - 4, client.Bottom - 4);
        Assert.Equal(["MainForm"], s.Selected);
        var frame = s.Surface.Geometry.FrameRect;
        s.Down(frame.Right - 1, frame.Bottom - 1);
        s.Move(frame.Right + 29, frame.Bottom + 19);
        s.Move(frame.Right + 63, frame.Bottom + 39);
        s.Up(frame.Right + 63, frame.Bottom + 39);

        await Phase5Scenario.SaveAsync(s);
        return File.ReadAllBytes(Path.Combine(h.TempDir, "MainForm.form"));
    }
}
