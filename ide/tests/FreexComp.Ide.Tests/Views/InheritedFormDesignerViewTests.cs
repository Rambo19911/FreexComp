using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using FreexComp.Designer.Rendering;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Tests.Designer;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Designer;
using FreexComp.Ide.Tests.Infrastructure;
using Point = Avalonia.Point;
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

    /// <summary>
    /// DSG-SUR-242, 229 (D-134 item 1): an inherited button dragged inside its inherited panel stores <c>Left</c> /
    /// <c>Top</c> overrides; <c>Revert to Inherited</c> is then enabled in the design-surface context menu and in the
    /// Structure window's, and the Structure menu's item removes the overrides (one undo unit).
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-SUR-242")]
    [Trait("Req", "DSG-SUR-229")]
    public async Task RevertToInherited_AfterMove_EnabledInSurfaceAndStructureMenus()
    {
        await using var h = await StartAsync();
        var (document, control) = await OpenDescendantAsync(h);
        var surface = document.Designer.Surface!;
        var model = document.Designer.Model!;
        var cancel = model.FindByName("CancelButton")!;
        Assert.Empty(cancel.Properties);

        // The drag: press on the button, move 40 to the left in steps, release.
        var b = surface.Geometry.GetBounds(cancel);
        var (x, y) = (b.Left + b.Width / 2.0, b.Top + b.Height / 2.0);
        Pointer(h, control, x, y, p => h.Window.MouseDown(p, MouseButton.Left));
        foreach (var step in new[] { 5, 20, 40 })
        {
            Pointer(h, control, x - step, y, p => h.Window.MouseMove(p));
        }

        Pointer(h, control, x - 40, y, p => h.Window.MouseUp(p, MouseButton.Left));
        Assert.Equal(["CancelButton"], surface.Selection.Items.Select(o => o.Name));
        Assert.NotNull(model.FindByName("CancelButton")!.FindProperty("Left"));
        Assert.Equal("ButtonPanel", model.FindByName("CancelButton")!.Parent!.Name);

        // The surface's context menu.
        b = surface.Geometry.GetBounds(model.FindByName("CancelButton")!);
        (x, y) = (b.Left + b.Width / 2.0, b.Top + b.Height / 2.0);
        Pointer(h, control, x, y, p => h.Window.MouseDown(p, MouseButton.Right));
        Pointer(h, control, x, y, p => h.Window.MouseUp(p, MouseButton.Right));
        var menu = Assert.IsType<ContextMenu>(control.ContextMenu);
        Assert.True(menu.IsOpen);
        Assert.True(Item(menu, "Context.designer.revertToInherited").IsEffectivelyEnabled);
        menu.Close();
        Dispatcher.UIThread.RunJobs();

        // The Structure window's context menu: enabled too; its item reverts the move.
        await h.ExecuteAsync("view.structure");
        Dispatcher.UIThread.RunJobs();
        h.RightClick(StructureRow + "CancelButton");
        var list = h.Find<ListBox>("Tool.Structure.List");
        menu = Assert.IsType<ContextMenu>(list.ContextMenu);
        var revert = Item(menu, "Context.designer.revertToInherited");
        Assert.True(revert.IsEffectivelyEnabled);
        menu.Close();
        revert.Command!.Execute(revert.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(model.FindByName("CancelButton")!.Properties);
        Assert.Equal("Undo_Designer_RevertToInherited", document.Designer.Undo.UndoTitleKey);
    }

    /// <summary>
    /// DSG-SUR-245, 242 (D-133): Revert to Inherited from the Structure window's context menu, with the surface's enabled
    /// rules — disabled on a new component and on an inherited one without overrides; on an inherited control with an
    /// override it removes the override, on the inherited root its own overrides (its new children stay), each in one
    /// "Revert to Inherited" undo unit; afterwards the item is disabled.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-SUR-245")]
    [Trait("Req", "DSG-SUR-242")]
    public async Task StructureContextMenu_RevertToInherited()
    {
        await using var h = await StartAsync();
        var (document, _) = await OpenDescendantAsync(h);
        var designer = document.Designer;
        await h.ExecuteAsync("view.structure");
        Dispatcher.UIThread.RunJobs();
        var list = h.Find<ListBox>("Tool.Structure.List");
        var original = designer.CurrentFormText();
        Assert.Contains("    inherited OkButton: TButton\n      Caption = 'Apply'\n    end\n", original, StringComparison.Ordinal);
        Assert.Contains("  Caption = 'Main'\n", original, StringComparison.Ordinal);

        (bool Enabled, MenuItem Item) Revert(string name)
        {
            h.RightClick(StructureRow + name);
            Assert.Equal([name], designer.Surface!.Selection.Items.Select(o => o.Name));
            var menu = Assert.IsType<ContextMenu>(list.ContextMenu);
            Assert.True(menu.IsOpen);
            var item = Item(menu, "Context.designer.revertToInherited");
            var enabled = item.IsEffectivelyEnabled;
            Assert.Equal(designer.Surface.CanExecute(FreexComp.Designer.Surface.Commands.RevertToInherited), enabled);
            menu.Close();
            Dispatcher.UIThread.RunJobs();
            return (enabled, item);
        }

        Assert.False(Revert("HelpButton").Enabled);
        Assert.False(Revert("CancelButton").Enabled);

        // An inherited control with an override: its block goes (the panel's block stays for the new child).
        var (enabled, revert) = Revert("OkButton");
        Assert.True(enabled);
        revert.Command!.Execute(revert.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(original.Replace("    inherited OkButton: TButton\n      Caption = 'Apply'\n    end\n", "", StringComparison.Ordinal),
            designer.CurrentFormText());
        Assert.Equal((1, "Undo_Designer_RevertToInherited"), (designer.Undo.UndoCount, designer.Undo.UndoTitleKey));
        Assert.False(Revert("OkButton").Enabled);

        // The inherited root: its own caption override goes; the new children and their lines stay.
        (enabled, revert) = Revert("MainForm");
        Assert.True(enabled);
        revert.Command!.Execute(revert.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(original.Replace("    inherited OkButton: TButton\n      Caption = 'Apply'\n    end\n", "", StringComparison.Ordinal)
            .Replace("  Caption = 'Main'\n", "", StringComparison.Ordinal), designer.CurrentFormText());
        Assert.Equal((2, "Undo_Designer_RevertToInherited"), (designer.Undo.UndoCount, designer.Undo.UndoTitleKey));
        Assert.NotNull(designer.Model!.FindByName("HelpButton"));
        Assert.NotNull(designer.Model.FindByName("NameEdit"));
        Assert.False(Revert("MainForm").Enabled);
    }

    /// <summary>
    /// DSG-SUR-245, 238 (D-134 item 2): the Structure window's context menu offers the surface commands with the same
    /// enabled rules — for an inherited component (Copy and Select All enabled, Cut and Delete refused) and for a new one;
    /// the Delete key in the Structure window on an inherited component shows the surface's refusal message and deletes
    /// nothing.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-SUR-245")]
    [Trait("Req", "DSG-SUR-238")]
    public async Task StructureMenu_SameEnabledRulesAsSurface_DeleteKeyShowsMessage()
    {
        await using var h = await StartAsync();
        var (document, control) = await OpenDescendantAsync(h);
        var surface = document.Designer.Surface!;
        var model = document.Designer.Model!;
        await h.ExecuteAsync("view.structure");
        Dispatcher.UIThread.RunJobs();
        var list = h.Find<ListBox>("Tool.Structure.List");

        foreach (var name in new[] { "CancelButton", "HelpButton" })
        {
            var b = surface.Geometry.GetBounds(model.FindByName(name)!);
            var (x, y) = (b.Left + b.Width / 2.0, b.Top + b.Height / 2.0);
            control.Focus();
            Dispatcher.UIThread.RunJobs();
            Pointer(h, control, x, y, p => h.Window.MouseDown(p, MouseButton.Right));
            Pointer(h, control, x, y, p => h.Window.MouseUp(p, MouseButton.Right));
            Assert.Equal([name], surface.Selection.Items.Select(o => o.Name));
            var menu = Assert.IsType<ContextMenu>(control.ContextMenu);
            var onSurface = Enabled(menu);
            menu.Close();
            Dispatcher.UIThread.RunJobs();

            h.Click(StructureRow + name); // the tree has the keyboard focus, as after a click in the real window
            Assert.True(list.IsKeyboardFocusWithin);
            h.RightClick(StructureRow + name);
            Assert.Equal([name], surface.Selection.Items.Select(o => o.Name));
            menu = Assert.IsType<ContextMenu>(list.ContextMenu);
            var inStructure = Enabled(menu);
            menu.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(onSurface, inStructure);
            Assert.True(inStructure["Context.edit.copy"], name);
            Assert.True(inStructure["Context.edit.selectAll"], name);
            Assert.Equal(name == "HelpButton", inStructure["Context.edit.delete"]);
            Assert.Equal(name == "HelpButton", inStructure["Context.edit.cut"]);
        }

        // The Delete key on the inherited component: on the surface, then in the Structure window — the same message.
        surface.Selection.Set(["CancelButton"]);
        control.Focus();
        Dispatcher.UIThread.RunJobs();
        h.Press("Delete");
        var message = h.Services.Status.Message;
        Assert.Contains("TBaseDialog", message, StringComparison.Ordinal);
        h.Services.Status.ShowMessage("-", TimeSpan.FromMinutes(1));
        h.Click(StructureRow + "CancelButton");
        Assert.True(list.IsKeyboardFocusWithin);
        h.Press("Delete");
        Assert.Equal(message, h.Services.Status.Message);
        Assert.NotNull(model.FindByName("CancelButton"));
        Assert.False(document.IsModified);
    }

    /// <summary>
    /// DSG-FD-134, DSG-SUR-030 (D-135): the ancestor of an open descendant leaves the ancestor catalog while a new button
    /// inside an inherited panel is selected: the re-merged read-only <c>F0112</c> document selects its root; afterwards a
    /// component without layout bounds selected from the Structure window, the context-menu key, Tab and the surface's
    /// automation elements raise no exception (nothing to scroll into view; the menu opens at the form).
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-FD-134")]
    [Trait("Req", "DSG-SUR-030")]
    public async Task Remerge_AncestorRemovedWithNewComponentSelected_SelectionHandlersDoNotThrow()
    {
        await using var h = await StartAsync();
        var (document, control) = await OpenDescendantAsync(h);
        var surface = document.Designer.Surface!;
        surface.Selection.Set(["HelpButton"]);
        Dispatcher.UIThread.RunJobs();

        var ancestors = Assert.IsType<FreexComp.Designer.Model.FrameCatalog>(((DocumentService)h.Services.Documents).DesignAncestors);
        ancestors.Remove("TBaseDialog");
        h.Clock.Advance(TimeSpan.FromSeconds(1)); // the read-only state follows the model 300 ms later (DSG-CS-010)
        Dispatcher.UIThread.RunJobs();
        var model = document.Designer.Model!;
        Assert.Equal(["F0112"], model.Diagnostics.Select(d => d.Code));
        Assert.True(document.Designer.Surface!.IsReadOnly);
        Assert.Equal([model.Root], surface.Selection.Items);
        var help = model.FindByName("HelpButton")!;
        Assert.False(surface.Geometry.HasBounds(help));

        // A component without bounds selected from outside (the Structure window).
        await h.ExecuteAsync("view.structure");
        Dispatcher.UIThread.RunJobs();
        h.Click(StructureRow + "HelpButton");
        Assert.Equal([help], surface.Selection.Items);

        // The context-menu key opens the menu at the form; Tab moves on; the automation elements have their bounds.
        control.Focus();
        Dispatcher.UIThread.RunJobs();
        h.Press("Shift+F10");
        Assert.True(Assert.IsType<ContextMenu>(control.ContextMenu).IsOpen);
        var frame = surface.Geometry.FrameRect;
        Assert.Equal(new Point(frame.Left + 8, frame.Top + 8), control.ContextMenuPoint);
        control.ContextMenu!.Close();
        Dispatcher.UIThread.RunJobs();
        control.Focus();
        h.Press("Tab");
        void Visit(AutomationPeer peer)
        {
            _ = peer.GetBoundingRectangle();
            foreach (var child in peer.GetChildren())
            {
                Visit(child);
            }
        }

        Visit(ControlAutomationPeer.CreatePeerForElement(control));
        Assert.False(document.IsModified);
    }

    private static Task<HeadlessHarness> StartAsync() => HeadlessHarness.StartAsync(o =>
    {
        o.InitialFiles["MainForm.pas"] = InheritedDesign.Unit;
        o.InitialFiles["MainForm.form"] = InheritedDesign.Form.ReplaceLineEndings("\r\n");
    });

    /// <summary><see cref="InheritedDesign.Form"/> in the Design view with its ancestor known; the surface focused.</summary>
    private static async Task<(FormDocumentViewModel Document, DesignSurfaceControl Control)> OpenDescendantAsync(HeadlessHarness h)
    {
        var documents = (DocumentService)h.Services.Documents;
        documents.DesignCatalog = FrameDesign.Catalog;
        documents.DesignAncestors = InheritedDesign.Ancestors();
        var document = Assert.IsType<FormDocumentViewModel>((await documents.OpenAsync(Path.Combine(h.TempDir, "MainForm.pas"))).Value);
        Dispatcher.UIThread.RunJobs();
        h.Click(AutomationIds.DocumentViewTab("MainForm.pas", "Design"));
        Assert.Equal(DocumentView.Design, document.ActiveView);
        Assert.True(document.Designer.Model!.Root.IsInheritedRoot);
        return (document, h.Find<DesignSurfaceControl>(AutomationIds.Designer("MainForm")));
    }

    /// <summary>A pointer event at a surface point (main-window coordinates), then the queued jobs.</summary>
    private static void Pointer(HeadlessHarness h, DesignSurfaceControl control, double x, double y, Action<Point> send)
    {
        send(control.TranslatePoint(new Point(x, y), h.Window)!.Value);
        Dispatcher.UIThread.RunJobs();
    }

    private static MenuItem Item(ContextMenu menu, string id) =>
        Items(menu.Items.OfType<MenuItem>()).Single(i => AutomationIds.Get(i) == id);

    private static IEnumerable<MenuItem> Items(IEnumerable<MenuItem> items) =>
        items.SelectMany(i => new[] { i }.Concat(Items(i.Items.OfType<MenuItem>())));

    /// <summary>Every item of the menu and its submenus by automation id: enabled or not.</summary>
    private static Dictionary<string, bool> Enabled(ContextMenu menu) =>
        Items(menu.Items.OfType<MenuItem>()).ToDictionary(i => AutomationIds.Get(i)!, i => i.IsEffectivelyEnabled);

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
