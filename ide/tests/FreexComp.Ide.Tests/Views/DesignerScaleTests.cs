using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Designer;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The design surface shows forms at IDE scale (design-tokens.md UI-TOK-063, ui/designer.md).</summary>
[Trait("Phase", "5")]
[Trait("Kind", "Ui")]
public sealed class DesignerScaleTests
{
    /// <summary>
    /// UI-TOK-063: a form stored with <c>PixelsPerInch = 96</c> and <c>ClientWidth = 400</c> occupies 400 DIP on the
    /// surface; its controls keep their stored sizes in DIP; the
    /// surface control's extent is the frame plus the surface margin, also in DIP.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TOK-063")]
    public async Task FormAt96PpiShowsOneToOneInDip()
    {
        await using var h = await HeadlessHarness.StartAsync(DesignerViewTests.Files);
        var document = await DesignerViewTests.OpenFormAsync(h);
        h.Click(AutomationIds.DocumentViewTab("MainForm.pas", "Design"));
        Assert.Equal(DocumentView.Design, document.ActiveView);
        Dispatcher.UIThread.RunJobs();
        var control = h.Find<DesignSurfaceControl>(AutomationIds.Designer("MainForm"));
        var geometry = document.Designer.Surface!.Geometry;
        var model = document.Designer.Model!;

        Assert.Equal(400, geometry.FormClientRect.Width);
        Assert.Equal(300, geometry.FormClientRect.Height);
        var edit = geometry.GetBounds(model.FindByName("NameEdit")!);
        Assert.Equal((200, 23), (edit.Width, edit.Height));
        Assert.Equal(geometry.FormClientRect.Left + 16, edit.Left);
        Assert.Equal(geometry.FrameRect.Right + DesignSurfaceControl.SurfaceMargin, control.DesiredSize.Width);
    }
}
