using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.Model;
using FreexComp.Designer.Rendering;
using FreexComp.Designer.Surface;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Designer;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;
using Point = Avalonia.Point;
using Rect = Avalonia.Rect;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// The Design view's surface control (ui/designer.md, design-surface.md; D-078): pointer and keyboard input reach the
/// surface controller, cursors, scrolling, automation peers, the system clipboard and the theme.
/// </summary>
[Trait("Phase", "5")]
[Trait("Kind", "Ui")]
public sealed class DesignerViewTests
{
    private const string Unit = """
        unit MainForm;

        interface

        uses
          Classes, Controls, Forms, StdCtrls;

        type
          TMainForm = class(TForm)
            NameEdit: TEdit;
            GreetButton: TButton;
            procedure GreetButtonClick(Sender: TObject);
          private
          public
          end;

        var
          MainForm: TMainForm;

        implementation

        {$FORM MainForm.form}

        procedure TMainForm.GreetButtonClick(Sender: TObject);
        begin

        end;

        end.

        """;

    private const string FormText = """
        object MainForm: TMainForm
          Left = 200
          Top = 120
          Caption = 'MainForm'
          ClientWidth = 400
          ClientHeight = 300
          PixelsPerInch = 96
          object NameEdit: TEdit
            Left = 16
            Top = 16
            Width = 200
            Height = 23
            TabOrder = 0
          end
          object GreetButton: TButton
            Left = 232
            Top = 15
            Width = 75
            Height = 25
            Caption = '&Greet'
            TabOrder = 1
            OnClick = GreetButtonClick
          end
        end

        """;

    private static readonly Lazy<ComponentCatalog> Catalog = new(() =>
        CatalogLoader.Load([Path.Combine(AppContext.BaseDirectory, "TestData", "metadata", "index.json")]).Catalog);

    private sealed record Session(HeadlessHarness H, FormDocumentViewModel Document, DesignSurfaceControl Control)
    {
        public DesignSurfaceController Surface => Document.Designer.Surface!;

        public FakeClipboard Clipboard => (FakeClipboard)((DocumentService)H.Services.Documents).Clipboard!;

        public FormObject Object(string name) => Document.Designer.Model!.FindByName(name)!;

        public int Prop(string name, string property) =>
            (int)((IntegerValue)PropertyAccess.GetEffective(Object(name), property)).Value;

        /// <summary>A surface point in main-window coordinates.</summary>
        public Point At(double x, double y) => Control.TranslatePoint(new Point(x, y), H.Window)!.Value;

        public void Down(double x, double y, RawInputModifiers m = RawInputModifiers.None) => Run(() => H.Window.MouseDown(At(x, y), MouseButton.Left, m));

        public void Move(double x, double y, RawInputModifiers m = RawInputModifiers.None) => Run(() => H.Window.MouseMove(At(x, y), m));

        public void Up(double x, double y, RawInputModifiers m = RawInputModifiers.None) => Run(() => H.Window.MouseUp(At(x, y), MouseButton.Left, m));

        public void Click(double x, double y)
        {
            Down(x, y);
            Up(x, y);
        }

        /// <summary>The centre of a component on the surface.</summary>
        public (double X, double Y) Center(string name)
        {
            var b = Surface.Geometry.GetBounds(Object(name));
            return (b.Left + b.Width / 2.0, b.Top + b.Height / 2.0);
        }

        private static void Run(Action action)
        {
            action();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static async Task<Session> OpenDesignAsync(HeadlessHarness h)
    {
        ((DocumentService)h.Services.Documents).DesignCatalog = Catalog.Value;
        var document = Assert.IsType<FormDocumentViewModel>((await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, "MainForm.pas"))).Value);
        Dispatcher.UIThread.RunJobs();
        h.Click(AutomationIds.DocumentViewTab("MainForm.pas", "Design"));
        Assert.Equal(DocumentView.Design, document.ActiveView);
        var control = h.Find<DesignSurfaceControl>(AutomationIds.Designer("MainForm"));
        Assert.True(control.IsKeyboardFocusWithin);
        return new Session(h, document, control);
    }

    private static void Files(HarnessOptions o)
    {
        o.InitialFiles["MainForm.pas"] = Unit.ReplaceLineEndings("\r\n");
        o.InitialFiles["MainForm.form"] = FormText.ReplaceLineEndings("\r\n");
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DSG-036")]
    [Trait("Req", "UI-DSG-003")]
    [Trait("Req", "DSG-SUR-057")]
    public async Task MouseDragMovesControl()
    {
        await using var h = await HeadlessHarness.StartAsync(Files);
        var s = await OpenDesignAsync(h);

        // UI-DSG-003: the control is as large as frame + tray + 16 margin, so the backdrop scrolls when the view is smaller.
        var g = s.Surface.Geometry;
        Assert.Equal(Math.Max(g.FrameRect.Right, g.TrayRect.Right) + 16, s.Control.DesiredSize.Width);
        Assert.Equal(g.TrayRect.Bottom + 16, s.Control.DesiredSize.Height);

        // Cursors (UI-DSG-036): arrow over an unselected control; SizeAll over the selected one.
        var (x, y) = s.Center("GreetButton");
        s.Move(x, y);
        Assert.Equal(CursorKind.Arrow, s.Control.CursorKind);
        s.Down(x, y);
        Assert.Equal(["GreetButton"], s.Surface.Selection.Items.Select(o => o.Name));
        Assert.Equal(CursorKind.SizeAll, s.Control.CursorKind);

        // A drag moves it, snapped to the grid; one undo unit; the .form text follows.
        s.Move(x + 6, y + 3);
        s.Move(x + 13, y + 6);
        Assert.Equal(InteractionState.Moving, s.Surface.State);
        s.Up(x + 13, y + 6);
        Assert.Equal(InteractionState.Idle, s.Surface.State);
        Assert.Equal(248, s.Prop("GreetButton", "Left"));
        Assert.Equal(24, s.Prop("GreetButton", "Top"));
        Assert.Contains("    Left = 248\n    Top = 24\n", s.Document.Designer.CurrentFormText().ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.True(s.Document.IsModified);

        // Over a handle: the size cursor.
        var se = s.Surface.Geometry.GetBounds(s.Object("GreetButton"));
        s.Move(se.Right, se.Bottom);
        Assert.Equal(CursorKind.SizeNWSE, s.Control.CursorKind);

        // Ctrl-drag: the copy badge while Ctrl is held; Ctrl at release copies (DSG-SUR-057) with a field in the unit.
        (x, y) = s.Center("GreetButton");
        s.Down(x, y);
        s.Move(x, y + 40);
        s.Move(x, y + 80, RawInputModifiers.Control);
        Assert.Equal(CursorKind.SizeAll, s.Control.CursorKind);
        Assert.True(s.Control.ShowsCopyBadge);
        s.Up(x, y + 80, RawInputModifiers.Control);
        Assert.False(s.Control.ShowsCopyBadge);
        Assert.Equal(248, s.Prop("GreetButton", "Left"));
        Assert.Equal(24, s.Prop("GreetButton", "Top"));
        Assert.Equal(["GreetButton1"], s.Surface.Selection.Items.Select(o => o.Name));
        Assert.Contains("    GreetButton1: TButton;\r\n", s.Document.Buffer.Text, StringComparison.Ordinal);

        // An armed palette item: crosshair; a click places the component there.
        s.Document.Designer.Host.Arm("TLabel", sticky: false);
        s.Move(40, 300);
        Assert.Equal(CursorKind.Cross, s.Control.CursorKind);
        s.Click(40, 300);
        Assert.Equal(["Label1"], s.Surface.Selection.Items.Select(o => o.Name));
        Assert.Null(s.Document.Designer.Host.ArmedPaletteClass);
        Assert.Equal(CursorKind.SizeNWSE, s.Control.CursorKind); // the pointer is on the new selection's NW handle
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-SUR-100")]
    [Trait("Req", "DSG-SUR-102")]
    [Trait("Req", "DSG-SUR-110")]
    [Trait("Req", "UI-KEY-007")]
    public async Task KeyboardNudgeAndResize()
    {
        await using var h = await HeadlessHarness.StartAsync(Files);
        var s = await OpenDesignAsync(h);
        var (x, y) = s.Center("GreetButton");
        s.Click(x, y);
        Assert.Equal(["GreetButton"], s.Surface.Selection.Items.Select(o => o.Name));

        // Arrows move by a grid step, Ctrl+arrow by 1 px; Shift resizes by a grid step, Shift+Alt by 1 px.
        h.Press("Right");
        Assert.Equal(240, s.Prop("GreetButton", "Left"));
        h.Press("Ctrl+Right");
        Assert.Equal(241, s.Prop("GreetButton", "Left"));
        h.Press("Shift+Down");
        Assert.Equal(33, s.Prop("GreetButton", "Height"));
        h.Press("Shift+Alt+Right");
        Assert.Equal(76, s.Prop("GreetButton", "Width"));
        Assert.True(s.Control.IsKeyboardFocusWithin);

        // Tab cycles the selection, Escape selects the parent (the keys fall through to the surface, UI-KEY-007).
        h.Press("Tab");
        Assert.Equal(["NameEdit"], s.Surface.Selection.Items.Select(o => o.Name));
        h.Press("Escape");
        Assert.Equal(["MainForm"], s.Surface.Selection.Items.Select(o => o.Name));
        h.Press("Tab");
        h.Press("Tab");
        Assert.Equal(["GreetButton"], s.Surface.Selection.Items.Select(o => o.Name));

        // Delete and Ctrl+Z through the edit commands, routed to the focused surface.
        h.Press("Delete");
        Assert.Null(s.Document.Designer.Model!.FindByName("GreetButton"));
        h.Press("Ctrl+Z");
        Assert.NotNull(s.Document.Designer.Model!.FindByName("GreetButton"));
        Assert.Equal(76, s.Prop("GreetButton", "Width"));

        // Ctrl+C reaches the system clipboard with both formats (DSG-SUR-110); Ctrl+V pastes a copy.
        s.Surface.Selection.Set(["GreetButton"]);
        h.Press("Ctrl+C");
        Assert.StartsWith("object GreetButton: TButton", s.Clipboard.Text, StringComparison.Ordinal);
        Assert.StartsWith("; parent-origin ", s.Clipboard.Formats[ClipboardFormats.Components], StringComparison.Ordinal);
        h.Press("Ctrl+V");
        Assert.Equal(["GreetButton1"], s.Surface.Selection.Items.Select(o => o.Name));

        // Another program's copy of form text is read when pasting.
        s.Clipboard.Text = "object Hello: TLabel\n  Left = 8\n  Top = 8\n  Caption = 'Hi'\nend\n";
        h.Press("Ctrl+V");
        Assert.Equal(["Hello"], s.Surface.Selection.Items.Select(o => o.Name));
        Assert.Contains("    Hello: TLabel;\r\n", s.Document.Buffer.Text, StringComparison.Ordinal);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-A11Y-035")]
    public async Task AutomationPeersExposeControlsAndHandles()
    {
        await using var h = await HeadlessHarness.StartAsync(Files);
        var s = await OpenDesignAsync(h);
        var peer = ControlAutomationPeer.CreatePeerForElement(s.Control);
        Assert.Equal("Designer.MainForm", peer.GetAutomationId());

        static AutomationPeer Child(AutomationPeer parent, string id) =>
            Assert.Single(parent.GetChildren(), p => p.GetAutomationId() == id);

        var form = Child(peer, "Designer.MainForm.Form");
        Assert.Same(peer, form.GetParent());
        Child(peer, "Designer.MainForm.Tray");
        var button = Child(form, "Designer.MainForm.Control.GreetButton");
        Child(form, "Designer.MainForm.Control.NameEdit");
        Assert.Equal("GreetButton", button.GetName());
        Assert.Same(form, button.GetParent());

        // Bounds: the surface rect, in the coordinates of the control's own peer; selection state through the selection item pattern.
        var b = s.Surface.Geometry.GetBounds(s.Object("GreetButton"));
        var origin = peer.GetBoundingRectangle().TopLeft;
        var bounds = button.GetBoundingRectangle();
        Assert.Equal(origin.X + b.Left, bounds.X, 3);
        Assert.Equal(origin.Y + b.Top, bounds.Y, 3);
        Assert.Equal(b.Width, bounds.Width, 3);
        var item = Assert.IsAssignableFrom<ISelectionItemProvider>(button);
        Assert.False(item.IsSelected);
        static IEnumerable<string> Handles(AutomationPeer surface) =>
            surface.GetChildren().Select(p => p.GetAutomationId()!).Where(id => id.Contains(".Handle.", StringComparison.Ordinal))
                .Select(id => id["Designer.MainForm.Handle.".Length..]);
        Assert.Equal(["MainForm"], s.Surface.Selection.Items.Select(o => o.Name));
        Assert.Equal(["E", "SE", "S"], Handles(peer)); // the form's own handles (UI-DSG-014)

        // Selecting it (through the pattern) shows the eight handles of the primary.
        item.Select();
        Assert.True(item.IsSelected);
        Assert.Equal(["GreetButton"], s.Surface.Selection.Items.Select(o => o.Name));
        Assert.Equal(["NW", "N", "NE", "E", "SE", "S", "SW", "W"], Handles(peer));
        var se = Child(peer, "Designer.MainForm.Handle.SE").GetBoundingRectangle();
        Assert.True(se.Contains(origin + new Point(b.Right - 1, b.Bottom - 1)));

        // A placed component gets its peer.
        s.Surface.PlaceAtDefaultPosition("TPanel");
        Dispatcher.UIThread.RunJobs();
        Child(Child(peer, "Designer.MainForm.Form"), "Designer.MainForm.Control.Panel1");
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DSG-012")]
    [Trait("Req", "UI-TOK-004")]
    public async Task ThemeSwitchKeepsFormColors()
    {
        await using var h = await HeadlessHarness.StartAsync(Files);
        var s = await OpenDesignAsync(h);
        var g = s.Surface.Geometry;
        var client = new Rect(g.FormClientRect.Left, g.FormClientRect.Top, g.FormClientRect.Width, g.FormClientRect.Height);
        var button = g.GetBounds(s.Object("GreetButton"));
        var buttonRect = new Rect(button.Left, button.Top, button.Width, button.Height);

        await h.ExecuteAsync("view.theme.light");
        Assert.Same(SurfacePalette.Light, s.Surface.Palette);
        var light = Fills(s.Control);
        await h.ExecuteAsync("view.theme.dark");
        Assert.Same(SurfacePalette.Dark, s.Surface.Palette);
        var dark = Fills(s.Control);

        // The backdrop is the IDE's colour; the form, its client area and its controls look the same in both themes.
        Assert.Equal(ToColor(SurfacePalette.Light.Backdrop), light[0].Color);
        Assert.Equal(ToColor(SurfacePalette.Dark.Backdrop), dark[0].Color);
        Assert.Equal(ColorAt(light, client), ColorAt(dark, client));
        Assert.Equal(ColorAt(light, buttonRect), ColorAt(dark, buttonRect));
        Assert.Equal(ToColor(DesignPalette.Light.Face), ColorAt(light, client));
    }

    private static Color ToColor(Rgba c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    private static Color ColorAt(List<(Rect Rect, Color Color)> fills, Rect rect) => Assert.Single(fills, f => f.Rect == rect).Color;

    /// <summary>The solid rectangle fills the control renders, in control coordinates and drawing order.</summary>
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
            case GeometryDrawing { Geometry: { } geometry, Brush: ISolidColorBrush brush, Pen: null }:
                fills.Add((geometry.Bounds.TransformToAABB(transform), brush.Color));
                break;
        }
    }
}
