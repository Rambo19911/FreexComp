using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
/// Frame instances in the Design view (phase8-ui-additions.md §12, UI-DSG-134…136; D-119): the dashed instance outline,
/// the normal handles of a selected inherited control, and the inherited marker in the Structure tool window and the
/// Object Inspector's instance selector, in the light and the dark theme.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class FrameDesignerViewTests
{
    private const string StructureRow = "Tool.Structure.Row.Form/";

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DSG-134")]
    [Trait("Req", "UI-DSG-135")]
    [Trait("Req", "UI-DSG-136")]
    [Trait("Req", "DSG-SUR-233")]
    [Trait("Req", "DSG-SUR-235")]
    public async Task InstanceOutlineAndInheritedMarkersRender()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles["MainForm.pas"] = FrameDesign.Unit;
            o.InitialFiles["MainForm.form"] = FrameDesign.Form.ReplaceLineEndings("\r\n");
        });
        var documents = (DocumentService)h.Services.Documents;
        documents.DesignCatalog = FrameDesign.Catalog;
        documents.DesignFrames = FrameDesign.Frames();
        var document = Assert.IsType<FormDocumentViewModel>((await documents.OpenAsync(Path.Combine(h.TempDir, "MainForm.pas"))).Value);
        Dispatcher.UIThread.RunJobs();
        h.Click(AutomationIds.DocumentViewTab("MainForm.pas", "Design"));
        Assert.Equal(DocumentView.Design, document.ActiveView);
        var control = h.Find<DesignSurfaceControl>(AutomationIds.Designer("MainForm"));
        var surface = document.Designer.Surface!;
        var model = document.Designer.Model!;
        surface.Selection.Set(["BillingAddress.StreetEdit"]);
        await h.ExecuteAsync("view.structure");
        await h.ExecuteAsync("view.objectInspector");
        Dispatcher.UIThread.RunJobs();

        var instance = Inner(surface.Geometry.GetBounds(model.FindByName("BillingAddress")!));
        var edit = Inner(surface.Geometry.GetBounds(model.FindByQualifiedName("BillingAddress.StreetEdit")!));
        var panel = Inner(surface.Geometry.GetBounds(model.FindByName("OwnPanel")!));
        foreach (var theme in new[] { "view.theme.light", "view.theme.dark" })
        {
            await h.ExecuteAsync(theme);
            var palette = theme.EndsWith("light", StringComparison.Ordinal) ? SurfacePalette.Light : SurfacePalette.Dark;
            Assert.Same(palette, surface.Palette);
            var strokes = Strokes(control);

            // UI-DSG-134: one dashed (2 on, 2 off) 1 px outline in designer.handle.secondary on the instance's bounds only.
            var outline = Assert.Single(strokes, s => s.Rect == instance);
            Assert.Equal((ToColor(palette.HandleSecondary), 1d, "2 2"), (outline.Color, outline.Thickness, string.Join(' ', outline.Dashes)));
            Assert.DoesNotContain(strokes, s => s.Rect == panel && s.Dashes.Count > 0); // an ordinary panel: its own border only

            // UI-DSG-135: the selected inherited control has the solid primary outline of an ordinary control.
            var selected = Assert.Single(strokes, s => s.Rect == edit && s.Color == ToColor(palette.HandleBorder));
            Assert.Equal("", string.Join(' ', selected.Dashes));
            Assert.DoesNotContain(strokes, s => s.Rect == edit && s.Dashes.Count > 0);

            // UI-DSG-136: Structure rows of inherited objects in text.secondary, the screen-reader name naming the frame.
            var row = h.Find<Control>(StructureRow + "BillingAddress.StreetEdit");
            Assert.Equal("StreetEdit: TEdit" + string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Designer_InheritedSuffix, "TAddressFrame"),
                AutomationProperties.GetName(row));
            Assert.Same(Resource(h, "FxcTextSecondaryBrush"), Text(row).Foreground);
            Assert.NotNull(Resource(h, "FxcTextSecondaryBrush"));
            var ownRow = h.Find<Control>(StructureRow + "OwnPanel");
            Assert.Equal("OwnPanel: TPanel", AutomationProperties.GetName(ownRow));
            Assert.Same(Resource(h, "FxcTextPrimaryBrush"), Text(ownRow).Foreground);
            Assert.Same(Resource(h, "FxcTextPrimaryBrush"), Text(h.Find<Control>(StructureRow + "BillingAddress")).Foreground);

            // ... and the instance selector's entry of the selected inherited object.
            var selector = h.Find<ComboBox>("Tool.ObjectInspector.InstanceSelector");
            Assert.Equal("BillingAddress.StreetEdit: TEdit", selector.SelectedItem as string);
            var entry = selector.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "BillingAddress.StreetEdit: TEdit");
            Assert.Same(Resource(h, "FxcTextSecondaryBrush"), entry.Foreground);
            Assert.EndsWith(", inherited from TAddressFrame", AutomationProperties.GetName(entry), StringComparison.Ordinal);
        }

        // The surface's virtual elements of inherited controls are named by their qualified name (unique per form).
        var ids = new List<string>();
        void Collect(AutomationPeer peer)
        {
            ids.Add(peer.GetAutomationId() ?? "");
            foreach (var child in peer.GetChildren())
            {
                Collect(child);
            }
        }

        Collect(ControlAutomationPeer.CreatePeerForElement(control));
        Assert.Contains("Designer.MainForm.Control.BillingAddress", ids);
        Assert.Contains("Designer.MainForm.Control.BillingAddress.StreetEdit", ids);
        Assert.Contains("Designer.MainForm.Control.BillingAddress.CityLabel", ids);
        Assert.Contains("Designer.MainForm.Control.OwnPanel", ids);
    }

    /// <summary>The rectangle a 1 px pen strokes for a surface rectangle (its centre line).</summary>
    private static Rect Inner(FreexComp.Designer.Rect r) => new(r.Left + 0.5, r.Top + 0.5, r.Width - 1, r.Height - 1);

    private static Color ToColor(Rgba c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    private static TextBlock Text(Control row) => row.GetVisualDescendants().OfType<TextBlock>().Last();

    private static object? Resource(HeadlessHarness h, string key) =>
        h.Window.TryFindResource(key, h.Window.ActualThemeVariant, out var value) ? value : null;

    /// <summary>The stroked shapes the control renders, in control coordinates and drawing order.</summary>
    private static List<(Rect Rect, Color Color, double Thickness, IReadOnlyList<double> Dashes)> Strokes(DesignSurfaceControl control)
    {
        var group = new DrawingGroup();
        using (var context = group.Open())
        {
            control.Render(context);
        }

        var strokes = new List<(Rect, Color, double, IReadOnlyList<double>)>();
        Walk(group, Matrix.Identity, strokes);
        return strokes;
    }

    private static void Walk(Drawing drawing, Matrix transform, List<(Rect, Color, double, IReadOnlyList<double>)> strokes)
    {
        switch (drawing)
        {
            case DrawingGroup group:
                var inner = (group.Transform?.Value ?? Matrix.Identity) * transform;
                foreach (var child in group.Children)
                {
                    Walk(child, inner, strokes);
                }

                break;
            case GeometryDrawing { Geometry: { } geometry, Pen: { Brush: ISolidColorBrush brush } pen }:
                strokes.Add((geometry.Bounds.TransformToAABB(transform), brush.Color, pen.Thickness, [.. pen.DashStyle?.Dashes ?? []]));
                break;
        }
    }
}
