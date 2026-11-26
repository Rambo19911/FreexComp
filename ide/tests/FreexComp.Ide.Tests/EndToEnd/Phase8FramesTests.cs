using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Designer.Model;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.EndToEnd;

/// <summary>
/// Card F05-006 acceptance with the real build engine, toolchain and framework metadata (frames-designer.md §10; D-120): a
/// frame designed in the IDE, two instances placed on the main form through the Tool Palette's <c>Frames…</c> item, one of
/// them with overridden properties; saved, built, run with <c>--dump-layout</c>, and the runtime layout compared with the
/// designer's.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "E2E")]
[Trait("Category", "Toolchain")]
public sealed class Phase8FramesTests
{
    [AvaloniaFact(Timeout = 120000)]
    [Trait("Req", "DSG-CS-112")]
    [Trait("Req", "DSG-CS-114")]
    [Trait("Req", "DSG-CS-115")]
    [Trait("Req", "DSG-TP-064")]
    [Trait("Req", "DSG-FD-103")]
    public async Task E2E_Phase8_Frames_DesignSaveCompile_LayoutIdentical()
    {
        await using var h = await Phase5Scenario.StartAsync();
        var main = await Phase5Scenario.CreateProjectAndDesignAsync(h);
        var dir = Phase5Scenario.ProjectDir(h);

        // File › New › Frame: Frame1 in the Design view; its size set on the root, then a label, an edit and a panel holding a label.
        await h.ExecuteAsync("file.newFrame");
        var frameUnit = Path.Combine(dir, "Frame1.pas");
        Assert.Equal(frameUnit, h.Services.Documents.ActiveDocument!.FilePath);
        var frame = await Phase5Scenario.OpenDesignAsync(h, frameUnit);
        await h.ExecuteAsync("view.toolPalette");
        var client = frame.Surface.Geometry.FormClientRect;
        frame.Click(client.Right - 4, client.Bottom - 4);
        Assert.Equal(["Frame1"], frame.Selected);
        frame.Inspect("Width", "300");
        frame.Click(client.Right - 4, client.Bottom - 4); // back on the surface: F4 toggles between it and the inspector
        frame.Inspect("Height", "120");
        frame.Place("TLabel", 8, 8);
        frame.Inspect("Caption", "Street");
        frame.Place("TEdit", 64, 4);
        frame.Place("TPanel", 8, 40);
        var panel = frame.Surface.Geometry.GetBounds(frame.Object("Panel1"));
        frame.PlaceAt("TLabel", panel.Left + 8, panel.Top + 8);
        Assert.Equal(["Label2"], frame.Object("Panel1").Children.Select(c => c.Name));
        await Phase5Scenario.SaveAsync(frame);
        Assert.Contains("RegisterClass(TFrame1);", File.ReadAllText(frameUnit, Encoding.UTF8), StringComparison.Ordinal);

        // The main form: two instances through Frames… (the picker lists Frame1); each adds no second uses entry.
        main = await Phase5Scenario.OpenDesignAsync(h, Phase5Scenario.UnitPath(h));
        PlaceFrame(h, main, 8, 8);
        PlaceFrame(h, main, 8, 150);
        Assert.True(main.Object("Frame11").IsFrameInstance);
        Assert.True(main.Object("Frame12").IsFrameInstance);
        var unitText = main.Document.Buffer.Text;
        Assert.Contains("    Frame11: TFrame1;\r\n    Frame12: TFrame1;\r\n", unitText, StringComparison.Ordinal);
        Assert.Contains("  Classes, Controls, Forms, Frame1;\r\n", unitText, StringComparison.Ordinal);

        // The first instance's label: a click selects the inherited object; its caption and position are overridden.
        var inherited = main.Object("Frame11").Children.Single(c => c.Name == "Label1");
        Assert.True(inherited.IsInherited);
        var bounds = main.Surface.Geometry.GetBounds(inherited);
        var (x, y) = (bounds.Left + bounds.Width / 2.0, bounds.Top + bounds.Height / 2.0);
        main.Click(x, y);
        Assert.Same(inherited, Assert.Single(main.Surface.Selection.Items));
        main.Inspect("Caption", "Billing");
        var form = main.Surface.Geometry.FormClientRect;
        main.Click(form.Right - 4, form.Bottom - 4); // elsewhere first: a second press on the same spot is a double-click
        main.Click(x, y);
        main.Inspect("Left", "24");
        await Phase5Scenario.SaveAsync(main);
        var formText = File.ReadAllText(Phase5Scenario.FormPath(h), Encoding.UTF8);
        Assert.Contains("  inline Frame11: TFrame1\n", formText, StringComparison.Ordinal);
        Assert.Contains("    inherited Label1: TLabel\n      Left = 24\n", formText, StringComparison.Ordinal); // override blocks only
        Assert.Contains("      Caption = 'Billing'\n    end\n  end\n", formText, StringComparison.Ordinal);
        Assert.Contains("  inline Frame12: TFrame1\n", formText, StringComparison.Ordinal);
        Assert.DoesNotContain("Edit1", formText, StringComparison.Ordinal); // the frame's components are not copied

        // Build, run with --dump-layout: the runtime layout equals the designer's.
        var exe = await Phase5Scenario.BuildAsync(h);
        var runtime = await LayoutRoundTrip.RunDumpAsync(exe, h.TempDir);
        Assert.Equal([Phase5Scenario.FormName], runtime.Forms.Select(f => f.Name));
        var designer = LayoutRoundTrip.DesignerDump(dir, runtime);
        LayoutRoundTrip.AssertEqual(designer, runtime, "frames");
    }

    /// <summary>Frames… on the palette (search cleared), TFrame1 chosen in the picker, a click on the form client point.</summary>
    private static void PlaceFrame(HeadlessHarness h, Phase5Scenario.Session s, double clientX, double clientY)
    {
        if (h.TryFind<Avalonia.Controls.Button>("Tool.ToolPalette.Filter.Clear") is { IsEffectivelyVisible: true })
        {
            h.Click("Tool.ToolPalette.Filter.Clear");
        }

        var before = s.Document.Designer.Model!.AllObjects().Count(o => o.IsFrameInstance);
        h.Click("Tool.ToolPalette.Item.Frames");
        h.Click("Tool.ToolPalette.FramePicker.TFrame1");
        Assert.Equal("TFrame1", s.Document.Designer.Host.ArmedPaletteClass);
        var client = s.Surface.Geometry.FormClientRect;
        s.Click(client.Left + clientX, client.Top + clientY);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(before + 1, s.Document.Designer.Model!.AllObjects().Count(o => o.IsFrameInstance));
        Assert.Equal(DocumentView.Design, s.Document.ActiveView);
    }
}
