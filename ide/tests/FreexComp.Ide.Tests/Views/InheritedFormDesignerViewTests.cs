using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using FreexComp.Designer.Rendering;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Tests.Designer;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Designer;
using FreexComp.Ide.Tests.Infrastructure;
using Rect = Avalonia.Rect;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// A descendant form in the Design view (form-inheritance-designer.md §12.1, UI-DSG-141…146, DSG-SUR-245; D-131): the
/// inherited marker, the tooltip line, the Structure window's markers and root suffix, the instance selector and the
/// surface's element names, in the light and the dark theme.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class InheritedFormDesignerViewTests
{
    private const string StructureRow = "Tool.Structure.Row.Form/";

    private static string Inherited(string name) => name + string.Format(CultureInfo.CurrentCulture, Strings.Designer_InheritedSuffix, "TBaseDialog");

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DSG-141")]
    [Trait("Req", "UI-DSG-142")]
    [Trait("Req", "UI-DSG-143")]
    [Trait("Req", "UI-DSG-144")]
    [Trait("Req", "UI-DSG-146")]
    [Trait("Req", "DSG-SUR-245")]
    public async Task MarkersTooltipStructureSuffixRender()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles["MainForm.pas"] = InheritedDesign.Unit;
            o.InitialFiles["MainForm.form"] = InheritedDesign.Form.ReplaceLineEndings("\r\n");
        });
        var documents = (DocumentService)h.Services.Documents;
        documents.DesignCatalog = FrameDesign.Catalog;
        documents.DesignAncestors = InheritedDesign.Ancestors();
        var document = Assert.IsType<FormDocumentViewModel>((await documents.OpenAsync(Path.Combine(h.TempDir, "MainForm.pas"))).Value);
        Dispatcher.UIThread.RunJobs();
        h.Click(AutomationIds.DocumentViewTab("MainForm.pas", "Design"));
        Assert.Equal(DocumentView.Design, document.ActiveView);
        var control = h.Find<DesignSurfaceControl>(AutomationIds.Designer("MainForm"));
        var surface = document.Designer.Surface!;
        var model = document.Designer.Model!;
        Assert.True(model.Root.IsInheritedRoot);
        surface.Selection.Set(["OkButton"]);
        await h.ExecuteAsync("view.structure");
        await h.ExecuteAsync("view.objectInspector");
        Dispatcher.UIThread.RunJobs();

        FreexComp.Designer.Rect Bounds(string name) => surface.Geometry.GetBounds(model.FindByName(name)!);
        Rect Marker(string name) => new(Bounds(name).Left, Bounds(name).Top, 6, 6);
        string[] inherited = ["TitleLabel", "ButtonPanel", "OkButton", "CancelButton"];
        string[] added = ["HelpButton", "NameEdit"];
        foreach (var theme in new[] { "view.theme.light", "view.theme.dark" })
        {
            await h.ExecuteAsync(theme);
            var palette = theme.EndsWith("light", StringComparison.Ordinal) ? SurfacePalette.Light : SurfacePalette.Dark;
            Assert.Same(palette, surface.Palette);

            // UI-DSG-141: a 6 × 6 marker in designer.handle.secondary at the top-left of each inherited control only.
            var markers = Fills(control).Where(f => f.Color == ToColor(palette.HandleSecondary) && f.Rect.Size == new Size(6, 6)).Select(f => f.Rect).ToList();
            foreach (var name in inherited)
            {
                Assert.Single(markers, m => m == Marker(name));
            }

            foreach (var name in added)
            {
                Assert.DoesNotContain(Marker(name), markers);
            }

            Assert.DoesNotContain(markers, m => m.TopLeft == new Avalonia.Point(surface.Geometry.FormClientRect.Left, surface.Geometry.FormClientRect.Top)
                || m.TopLeft == new Avalonia.Point(surface.Geometry.FrameRect.Left, surface.Geometry.FrameRect.Top));

            // UI-DSG-144: the root row ends with the ancestor suffix in text.secondary; the screen-reader name has it too.
            var suffix = "  " + string.Format(CultureInfo.CurrentCulture, Strings.Designer_InheritsSuffix, "TBaseDialog");
            Assert.Equal("  (inherits TBaseDialog)", suffix); // the rendered gap of UI-DSG-144 is unchanged
            var root = h.Find<Control>(StructureRow + "MainForm");
            Assert.Equal("MainForm: TMainForm" + suffix, AutomationProperties.GetName(root));
            var runs = Text(root).Inlines!.OfType<Run>().ToList();
            Assert.Equal(["MainForm: TMainForm", suffix], runs.Select(r => r.Text));
            Assert.Same(Resource(h, "FxcTextSecondaryBrush"), runs[1].Foreground);
            Assert.NotNull(Resource(h, "FxcTextSecondaryBrush"));

            // UI-DSG-143: inherited rows in text.secondary with the inherited suffix; new components as usual.
            var row = h.Find<Control>(StructureRow + "OkButton");
            Assert.Equal(Inherited("OkButton: TButton"), AutomationProperties.GetName(row));
            Assert.Same(Resource(h, "FxcTextSecondaryBrush"), Text(row).Foreground);
            var own = h.Find<Control>(StructureRow + "HelpButton");
            Assert.Equal("HelpButton: TButton", AutomationProperties.GetName(own));
            Assert.Same(Resource(h, "FxcTextPrimaryBrush"), Text(own).Foreground);

            // ... and the instance selector's entry of the selected inherited component.
            var selector = h.Find<ComboBox>("Tool.ObjectInspector.InstanceSelector");
            Assert.Equal("OkButton: TButton", selector.SelectedItem as string);
            var entry = selector.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "OkButton: TButton");
            Assert.Same(Resource(h, "FxcTextSecondaryBrush"), entry.Foreground);
            Assert.Equal(Inherited("OkButton: TButton"), AutomationProperties.GetName(entry));
        }

        // UI-DSG-142: the tooltip of an inherited control ends with "Inherited from TBaseDialog"; a new control has none.
        var ok = Bounds("OkButton");
        var tip = Assert.IsType<FreexComp.Designer.Surface.InheritedControlToolTip>(
            surface.GetInheritedControlToolTip(ok.Left + ok.Width / 2, ok.Top + ok.Height / 2));
        var lines = DesignSurfaceControl.BuildToolTip(tip).Children.OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Equal(["OkButton: TButton", string.Format(CultureInfo.CurrentCulture, Strings.Designer_InheritedFromTooltip, "TBaseDialog")], lines);
        var help = Bounds("HelpButton");
        Assert.Null(surface.GetInheritedControlToolTip(help.Left + help.Width / 2, help.Top + help.Height / 2));

        // UI-DSG-146: the surface's element of an inherited control names its ancestor; the marker is no element.
        var peers = new Dictionary<string, string>();
        void Collect(AutomationPeer peer)
        {
            peers[peer.GetAutomationId() ?? ""] = peer.GetName();
            foreach (var child in peer.GetChildren())
            {
                Collect(child);
            }
        }

        Collect(ControlAutomationPeer.CreatePeerForElement(control));
        Assert.Equal(Inherited("OkButton"), peers["Designer.MainForm.Control.OkButton"]);
        Assert.Equal(Inherited("ButtonPanel"), peers["Designer.MainForm.Control.ButtonPanel"]);
        Assert.Equal("NameEdit", peers["Designer.MainForm.Control.NameEdit"]);
        Assert.DoesNotContain(peers.Keys, id => id.Contains("Marker", StringComparison.Ordinal));

        // DSG-SUR-245: the Structure window's menu offers the surface commands with the same enabled rules.
        Assert.False(surface.CanExecute(FreexComp.Designer.Surface.Commands.Delete));
    }

    private static Color ToColor(Rgba c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    private static TextBlock Text(Control row) => row.GetVisualDescendants().OfType<TextBlock>().Last();

    private static object? Resource(HeadlessHarness h, string key) =>
        h.Window.TryFindResource(key, h.Window.ActualThemeVariant, out var value) ? value : null;

    /// <summary>The filled shapes the control renders, in control coordinates and drawing order.</summary>
    private static List<(Rect Rect, Color Color)> Fills(DesignSurfaceControl control)
    {
        var group = new DrawingGroup();
        using (var context = group.Open())
        {
            control.Render(context);
        }

        var fills = new List<(Rect, Color)>();
        Walk(group, Matrix.Identity, fills);
        return fills;
    }

    private static void Walk(Drawing drawing, Matrix transform, List<(Rect, Color)> fills)
    {
        switch (drawing)
        {
            case DrawingGroup group:
                var inner = (group.Transform?.Value ?? Matrix.Identity) * transform;
                foreach (var child in group.Children)
                {
                    Walk(child, inner, fills);
                }

                break;
            case GeometryDrawing { Geometry: { } geometry, Brush: ISolidColorBrush brush }:
                fills.Add((geometry.Bounds.TransformToAABB(transform), brush.Color));
                break;
        }
    }
}
