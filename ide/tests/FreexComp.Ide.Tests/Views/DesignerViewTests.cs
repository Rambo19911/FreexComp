using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.Model;
using FreexComp.Designer.Rendering;
using FreexComp.Designer.Surface;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Designer;
using FreexComp.Ide.Tests.Fixtures.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;
using FreexComp.Ide.Views;
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
    internal const string Unit = """
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

    internal const string FormText = """
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

        public void RightClick(double x, double y)
        {
            Run(() => H.Window.MouseDown(At(x, y), MouseButton.Right));
            Run(() => H.Window.MouseUp(At(x, y), MouseButton.Right));
        }

        public IReadOnlyList<string> Selected => [.. Surface.Selection.Items.Select(o => o.Name)];

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
        var document = await OpenFormAsync(h);
        h.Click(AutomationIds.DocumentViewTab("MainForm.pas", "Design"));
        Assert.Equal(DocumentView.Design, document.ActiveView);
        var control = h.Find<DesignSurfaceControl>(AutomationIds.Designer("MainForm"));
        Assert.True(control.IsKeyboardFocusWithin);
        return new Session(h, document, control);
    }

    /// <summary>Opens <c>MainForm.pas</c> (Code view) with the test component metadata.</summary>
    internal static async Task<FormDocumentViewModel> OpenFormAsync(HeadlessHarness h)
    {
        ((DocumentService)h.Services.Documents).DesignCatalog = Catalog.Value;
        var document = Assert.IsType<FormDocumentViewModel>((await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, "MainForm.pas"))).Value);
        Dispatcher.UIThread.RunJobs();
        return document;
    }

    internal static void Files(HarnessOptions o)
    {
        o.InitialFiles["MainForm.pas"] = Unit.ReplaceLineEndings("\r\n");
        o.InitialFiles["MainForm.form"] = FormText.ReplaceLineEndings("\r\n");
    }

    /// <summary>The form files at Phase 5 with sample Object Inspector and Tool Palette tool windows (I-207 has not switched the phase yet).</summary>
    internal static void Phase5(HarnessOptions o)
    {
        Files(o);
        o.ImplementedPhase = 5;
        o.ToolWindows.Add(SampleToolViewModel.Definition("ObjectInspector", ToolGroupId.RightUpper, 5, "Cmd_view_objectInspector"));
        o.ToolWindows.Add(SampleToolViewModel.Definition("ToolPalette", ToolGroupId.RightLower, 5, "Cmd_view_toolPalette"));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-A11Y-010")]
    [Trait("Req", "DSG-TP-033")]
    [Trait("Req", "DSG-TP-042")]
    public async Task PlaceButtonFromPaletteWithKeyboardOnly()
    {
        // The real Object Inspector and Tool Palette (ImplementedPhase 5 without the sample tool windows).
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            Files(o);
            o.ImplementedPhase = 5;
        });
        var s = await OpenDesignAsync(h);

        // Alt+4 focuses the palette's search box; typing filters; Enter places the first match at the default position.
        h.Press("Alt+4");
        var search = h.Find<TextBox>("Tool.ToolPalette.Filter");
        Assert.True(search.IsKeyboardFocusWithin);
        h.Type("button");
        h.Press("Enter");
        var button = s.Document.Designer.Model!.FindByName("Button1");
        Assert.NotNull(button);
        Assert.Equal("TButton", button.TypeName);
        Assert.Equal(["Button1"], s.Selected);
        Assert.Contains("    Button1: TButton;\r\n", s.Document.Buffer.Text, StringComparison.Ordinal);
        var form = Assert.Single(ControlAutomationPeer.CreatePeerForElement(s.Control).GetChildren(), p => p.GetAutomationId() == "Designer.MainForm.Form");
        Assert.Contains(form.GetChildren(), p => p.GetAutomationId() == AutomationIds.DesignerControl("MainForm", "Button1"));
        Assert.True(s.Control.IsKeyboardFocusWithin); // focus back on the surface
        Assert.Equal("", search.Text ?? "");

        // In the list: Down moves into it, Enter (toolPalette.placeSelected) places the current item.
        h.Press("Alt+4");
        h.Type("memo");
        h.Press("Down");
        Assert.True(h.Find<Control>("Tool.ToolPalette.List").IsKeyboardFocusWithin);
        h.Press("Enter");
        Assert.NotNull(s.Document.Designer.Model!.FindByName("Memo1"));
        Assert.Contains("    Memo1: TMemo;\r\n", s.Document.Buffer.Text, StringComparison.Ordinal);
        Assert.True(s.Control.IsKeyboardFocusWithin);
        Assert.Null(s.Document.Designer.Host.ArmedPaletteClass);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-SUR-042")]
    [Trait("Req", "DSG-SUR-161")]
    [Trait("Req", "DSG-CS-063")]
    public async Task DoubleClickCreatesHandlerAndShowsCode()
    {
        await using var h = await HeadlessHarness.StartAsync(Phase5);
        var s = await OpenDesignAsync(h);

        // A double-click on the edit creates its default handler (OnChange): declaration, stub, assignment; the Code view
        // opens with the caret in the stub (line "begin" + 1, column 3) and focus in the editor.
        var (x, y) = s.Center("NameEdit");
        DoubleClick(h, s.At(x, y));
        Assert.Equal(DocumentView.Code, s.Document.ActiveView);
        var text = s.Document.Buffer.Text;
        Assert.Contains("    procedure NameEditChange(Sender: TObject);\r\n", text, StringComparison.Ordinal);
        Assert.Contains("procedure TMainForm.NameEditChange(Sender: TObject);\r\nbegin\r\n  \r\nend;\r\n", text, StringComparison.Ordinal);
        Assert.Equal("NameEditChange", Assert.IsType<IdentValue>(s.Object("NameEdit").FindProperty("OnChange")!.Value).Name);
        var lines = text.Split("\r\n").ToList();
        var stubLine = lines.IndexOf("procedure TMainForm.NameEditChange(Sender: TObject);") + 1;
        Assert.Equal((stubLine + 2, 3), (s.Document.ViewState.Caret.Line, s.Document.ViewState.Caret.Column));
        Assert.True(h.Find<Control>(AutomationIds.Editor("MainForm.pas")).IsKeyboardFocusWithin);

        // A double-click on the button with an assigned OnClick only navigates: no text change, caret in its body.
        h.Press("F7");
        Assert.Equal(DocumentView.Design, s.Document.ActiveView);
        (x, y) = s.Center("GreetButton");
        DoubleClick(h, s.At(x, y));
        Assert.Equal(DocumentView.Code, s.Document.ActiveView);
        Assert.Equal(text, s.Document.Buffer.Text);
        var clickLine = lines.IndexOf("procedure TMainForm.GreetButtonClick(Sender: TObject);") + 1;
        Assert.Equal(clickLine + 2, s.Document.ViewState.Caret.Line);

        // One undo in the designer removes the handler assignment created by the double-click (the "Event" unit).
        h.Press("F7");
        h.Press("Ctrl+Z");
        Assert.Null(s.Object("NameEdit").FindProperty("OnChange"));
    }

    /// <summary>A left double-click at a window point (the second press may replace the surface by the Code view).</summary>
    private static void DoubleClick(HeadlessHarness h, Point at)
    {
        for (var i = 0; i < 2; i++)
        {
            h.Window.MouseDown(at, MouseButton.Left);
            h.Window.MouseUp(at, MouseButton.Left);
        }

        Dispatcher.UIThread.RunJobs();
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

    private static string ViewTab(string view) => AutomationIds.DocumentViewTab("MainForm.pas", view);

    /// <summary>The info bars shown in the window (their AutomationIds), in visual order.</summary>
    private static List<string> InfoBars(HeadlessHarness h) =>
        [.. h.Window.GetVisualDescendants().OfType<InfoBarView>().Where(b => b.IsEffectivelyVisible).Select(b => AutomationIds.Get(b)!)];

    private static string InfoBarText(HeadlessHarness h, string kind) =>
        h.Find<TextBlock>(AutomationIds.InfoBar(kind) + ".Text").Text ?? "";

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DSG-001")]
    [Trait("Req", "UI-DSG-002")]
    public async Task ViewSwitcherShowsThreeTabsOnlyForFormUnits()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            Files(o);
            o.InitialFiles["Plain.pas"] = "unit Plain;\r\n\r\ninterface\r\n\r\nimplementation\r\n\r\nend.\r\n";
        });
        var document = await OpenFormAsync(h);

        // One document tab, three view tabs in the order Code, Design, Text; Code active.
        Assert.Single(h.Services.Documents.Documents);
        var tabs = h.FindAll("Document.MainForm.pas.ViewTabs.").Select(c => AutomationIds.Get(c)!).ToList();
        Assert.Equal([ViewTab("Code"), ViewTab("Design"), ViewTab("Text")], tabs);
        Assert.True(h.Find<ToggleButton>(ViewTab("Code")).IsChecked);

        // The Design view: the surface, no info bar for a sound unit and form.
        h.Click(ViewTab("Design"));
        Assert.Equal(DocumentView.Design, document.ActiveView);
        Assert.NotNull(h.TryFind<DesignSurfaceControl>(AutomationIds.Designer("MainForm")));
        Assert.Empty(InfoBars(h));

        // The Text view edits the .form text with the form highlighting (design-tokens.md §3.1: language "form").
        h.Click(ViewTab("Text"));
        Assert.Equal(DocumentView.Text, document.ActiveView);
        Assert.Equal("form", document.FormText.Language.Id);
        var editor = h.Window.GetVisualDescendants().OfType<FreexComp.Ide.Editor.CodeEditorView>().Single(e => e.IsEffectivelyVisible);
        Assert.Same(document.FormText, editor.DataContext);
        Assert.StartsWith("object MainForm: TMainForm", editor.Editor.Text, StringComparison.Ordinal);

        // A unit without a companion form has no switcher.
        Assert.IsNotType<FormDocumentViewModel>((await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, "Plain.pas"))).Value);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(h.FindAll("Document.Plain.pas.ViewTabs"));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-KEY-001")]
    public async Task F7TogglesCodeAndDesign()
    {
        await using var h = await HeadlessHarness.StartAsync(Files);
        var document = await OpenFormAsync(h);

        // Code → Design: the surface has focus; F7 from the surface (Designer scope) goes back to the code with focus in it.
        h.Press("F7");
        Assert.Equal(DocumentView.Design, document.ActiveView);
        var surface = h.Find<DesignSurfaceControl>(AutomationIds.Designer("MainForm"));
        Assert.True(surface.IsKeyboardFocusWithin);
        h.Press("F7");
        Assert.Equal(DocumentView.Code, document.ActiveView);
        Assert.True(h.Find<Control>(AutomationIds.Editor("MainForm.pas")).IsKeyboardFocusWithin);

        // From the Text view F7 goes to the Design view.
        h.Click(ViewTab("Text"));
        h.Press("F7");
        Assert.Equal(DocumentView.Design, document.ActiveView);
        Assert.True(h.Find<ToggleButton>(ViewTab("Design")).IsChecked);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-FD-101")]
    public async Task TextViewSyntaxErrorKeepsTextViewWithInfoBar()
    {
        await using var h = await HeadlessHarness.StartAsync(Files);
        var document = await OpenFormAsync(h);
        h.Click(ViewTab("Text"));
        var form = document.FormBuffer;
        form.ReplaceAll(form.Text.Replace("  Left = 200", "  Left = = 200", StringComparison.Ordinal), resetUndo: false);
        Dispatcher.UIThread.RunJobs();

        // Leaving the Text view is refused: the Text view stays with InfoBar.FormSyntax (error style) above the editor.
        h.Click(ViewTab("Design"));
        Assert.Equal(DocumentView.Text, document.ActiveView);
        Assert.True(h.Find<ToggleButton>(ViewTab("Text")).IsChecked);
        Assert.Equal([AutomationIds.InfoBar("FormSyntax")], InfoBars(h));
        Assert.StartsWith("The form file cannot be read: ", InfoBarText(h, "FormSyntax"), StringComparison.Ordinal);
        Assert.Equal(2, document.FormText.ViewState.Caret.Line);
        h.Press("F7");
        Assert.Equal(DocumentView.Text, document.ActiveView);

        // Fixed: the switch succeeds and the bar is gone.
        form.ReplaceAll(form.Text.Replace("  Left = = 200", "  Left = 210", StringComparison.Ordinal), resetUndo: false);
        Dispatcher.UIThread.RunJobs();
        h.Click(ViewTab("Design"));
        Assert.Equal(DocumentView.Design, document.ActiveView);
        Assert.Empty(InfoBars(h));
        Assert.Equal(210, (int)((IntegerValue)PropertyAccess.GetEffective(document.Designer.Model!.Root, "Left")).Value);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DSG-004")]
    [Trait("Req", "UI-DSG-092")]
    [Trait("Req", "DSG-CS-110")]
    public async Task ReadOnlyInfoBarWhenUnitHasErrors()
    {
        await using var h = await HeadlessHarness.StartAsync(Files);
        var document = await OpenFormAsync(h);
        var unit = document.Buffer;
        var original = unit.Text;
        unit.ReplaceAll(original.Replace("    GreetButton: TButton;", "    GreetButton: ;", StringComparison.Ordinal), resetUndo: false);
        Dispatcher.UIThread.RunJobs();

        // The Design view of a unit with syntax errors: InfoBar.UnitErrors above the backdrop (outside the scroll area), read-only.
        h.Click(ViewTab("Design"));
        Assert.Equal(DocumentView.Design, document.ActiveView);
        Assert.Equal([AutomationIds.InfoBar("UnitErrors")], InfoBars(h));
        Assert.Equal(Strings.Msg_Designer_UnitHasErrors, InfoBarText(h, "UnitErrors"));
        var bar = h.Find<InfoBarView>(AutomationIds.InfoBar("UnitErrors"));
        Assert.Null(bar.FindAncestorOfType<ScrollViewer>());
        var surface = h.Find<DesignSurfaceControl>(AutomationIds.Designer("MainForm"));
        Assert.NotNull(surface.FindAncestorOfType<ScrollViewer>());
        Assert.True(bar.TranslatePoint(default, h.Window)!.Value.Y < surface.FindAncestorOfType<ScrollViewer>()!.TranslatePoint(default, h.Window)!.Value.Y);
        Assert.True(document.Designer.Surface!.IsReadOnly);
        document.Designer.Surface.Selection.Set(["GreetButton"]);
        h.Press("Delete");
        Assert.NotNull(document.Designer.Model!.FindByName("GreetButton"));

        // Go to First Error: the Code view with the caret on the error line.
        h.Click(AutomationIds.InfoBarAction("UnitErrors", "GoToFirstError"));
        Assert.Equal(DocumentView.Code, document.ActiveView);
        var errorLine = unit.Text.Split("\r\n").ToList().FindIndex(l => l == "    GreetButton: ;") + 1;
        Assert.Equal(errorLine, document.ViewState.Caret.Line);

        // The class renamed: InfoBar.FormClassNotFound instead; View Code goes to the Code view.
        unit.ReplaceAll(original.Replace("  TMainForm = class(TForm)", "  TOtherForm = class(TForm)", StringComparison.Ordinal), resetUndo: false);
        Dispatcher.UIThread.RunJobs();
        h.Click(ViewTab("Design"));
        Assert.Equal([AutomationIds.InfoBar("FormClassNotFound")], InfoBars(h));
        Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Msg_Designer_FormClassNotFound, "TMainForm"),
            InfoBarText(h, "FormClassNotFound"));
        Assert.True(document.Designer.Surface!.IsReadOnly);
        h.Click(AutomationIds.InfoBarAction("FormClassNotFound", "ViewCode"));
        Assert.Equal(DocumentView.Code, document.ActiveView);

        // Sound again: no bar, editable.
        unit.ReplaceAll(original, resetUndo: false);
        Dispatcher.UIThread.RunJobs();
        h.Click(ViewTab("Design"));
        Assert.Empty(InfoBars(h));
        Assert.False(document.Designer.Surface!.IsReadOnly);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DSG-080")]
    [Trait("Req", "UI-DSG-081")]
    [Trait("Req", "DSG-SUR-043")]
    public async Task ContextMenuListsCommandsWithAutomationIds()
    {
        await using var h = await HeadlessHarness.StartAsync(Phase5);
        var s = await OpenDesignAsync(h);

        // Right-click on an unselected control selects it, then opens the menu (designer.md §8 order).
        var (x, y) = s.Center("GreetButton");
        s.RightClick(x, y);
        Assert.Equal(["GreetButton"], s.Selected);
        var menu = Assert.IsType<ContextMenu>(s.Control.ContextMenu);
        Assert.True(menu.IsOpen);
        static string Id(object item) => item is Separator ? "-" : AutomationIds.Get((Control)item)!;
        Assert.Equal(
        [
            "Context.edit.cut", "Context.edit.copy", "Context.edit.paste", "Context.edit.delete", "Context.edit.selectAll",
            "Context.designer.selectParent", "-",
            "Context.Designer.Align", "Context.Designer.Size", "Context.Designer.Order", "-",
            "Context.designer.tabOrder", "Context.designer.lockControls", "-",
            "Context.designer.toggleGrid", "Context.designer.toggleSnapToGrid", "Context.designer.toggleGuidelines", "-",
            "Context.designer.createDefaultHandler", "Context.view.toggleFormCode", "Context.view.formAsText",
        ], menu.Items.Cast<object>().Select(Id));
        MenuItem Item(string id) => menu.Items.OfType<MenuItem>().Single(i => AutomationIds.Get(i) == id);
        Assert.Equal(
        [
            "Context.designer.alignLeft", "Context.designer.alignRight", "Context.designer.alignTop", "Context.designer.alignBottom",
            "Context.designer.alignHCenter", "Context.designer.alignVCenter", "-",
            "Context.designer.centerHInParent", "Context.designer.centerVInParent", "-",
            "Context.designer.spaceHEqually", "Context.designer.spaceVEqually", "-", "Context.designer.alignToGrid",
        ], Item("Context.Designer.Align").Items.Cast<object>().Select(Id));
        Assert.Equal(["Context.designer.sameWidth", "Context.designer.sameHeight", "Context.designer.sameSize", "-", "Context.designer.sizeToGrid"],
            Item("Context.Designer.Size").Items.Cast<object>().Select(Id));
        Assert.Equal(["Context.designer.bringToFront", "Context.designer.sendToBack"], Item("Context.Designer.Order").Items.Cast<object>().Select(Id));

        // Enabled-when (design-surface.md §13) and check marks; the labels are the main menu's.
        Assert.True(Item("Context.edit.cut").IsEffectivelyEnabled);
        Assert.True(Item("Context.designer.selectParent").IsEffectivelyEnabled);
        Assert.True(Item("Context.designer.createDefaultHandler").IsEffectivelyEnabled);
        Assert.True(Item("Context.designer.toggleGrid").IsChecked);
        Assert.False(Item("Context.designer.lockControls").IsChecked);
        Assert.Equal("_Lock Controls", Item("Context.designer.lockControls").Header);

        // An item runs its command: Show Grid off (the setting), the check mark follows.
        Item("Context.designer.toggleGrid").Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(h.Services.Settings.Get(SettingsCatalog.DesignerShowGrid));
        Assert.False(Item("Context.designer.toggleGrid").IsChecked);
        menu.Close();
        Dispatcher.UIThread.RunJobs();

        // Right-click on the backdrop selects the form.
        var frame = s.Surface.Geometry.FrameRect;
        s.RightClick(frame.Right + 8, frame.Bottom + 8);
        Assert.Equal(["MainForm"], s.Selected);
        Assert.True(Assert.IsType<ContextMenu>(s.Control.ContextMenu).IsOpen);
        s.Control.ContextMenu!.Close();
        Dispatcher.UIThread.RunJobs();

        // Shift+F10 opens it at the primary selection's top-left + (8, 8).
        s.Surface.Selection.Set(["GreetButton"]);
        s.Control.Focus();
        h.Press("Shift+F10");
        Assert.True(Assert.IsType<ContextMenu>(s.Control.ContextMenu).IsOpen);
        var b = s.Surface.Geometry.GetBounds(s.Object("GreetButton"));
        Assert.Equal(new Point(b.Left + 8, b.Top + 8), s.Control.ContextMenuPoint);
        s.Control.ContextMenu!.Close();
    }

    /// <summary>
    /// UI-DSG-082 (D-097): for a component with designer verbs the context menu starts with the verb group (metadata
    /// captions, <c>Context.Verb.&lt;id&gt;</c>), then a separator, then the standard items; a verb item runs its command.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DSG-082")]
    [Trait("Req", "DSG-SUR-195")]
    public async Task ContextMenuShowsVerbGroupFirst()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            Phase5(o);
            o.InitialFiles["MainForm.pas"] = Unit
                .Replace("GreetButton: TButton;", "GreetButton: TButton;\n    StatusBar1: TStatusBar;", StringComparison.Ordinal)
                .Replace("StdCtrls;", "StdCtrls, ComCtrls;", StringComparison.Ordinal)
                .ReplaceLineEndings("\r\n");
            var form = FormText.ReplaceLineEndings("\n");
            var at = form.TrimEnd().LastIndexOf("end", StringComparison.Ordinal);
            o.InitialFiles["MainForm.form"] = (form[..at]
                + "  object StatusBar1: TStatusBar\n    Left = 0\n    Top = 281\n    Width = 400\n    Height = 19\n    TabOrder = 2\n"
                + "    Panels = <\n      item\n        Text = 'Ready'\n      end\n    >\n  end\n" + form[at..]).ReplaceLineEndings("\r\n");
        });
        var s = await OpenDesignAsync(h);
        static string Id(object item) => item is Separator ? "-" : AutomationIds.Get((Control)item)!;

        var (x, y) = s.Center("StatusBar1");
        s.RightClick(x, y);
        Assert.Equal(["StatusBar1"], s.Selected);
        var menu = Assert.IsType<ContextMenu>(s.Control.ContextMenu);
        Assert.True(menu.IsOpen);
        var ids = menu.Items.Cast<object>().Select(Id).ToList();
        Assert.Equal(["Context.Verb.editPanels", "-", "Context.edit.cut"], ids.Take(3));
        var verb = menu.Items.OfType<MenuItem>().First();
        Assert.Equal("_Panels Editor…", verb.Header);
        Assert.True(verb.IsEffectivelyEnabled);

        // The verb runs designer.editCollection on Panels: the collection editor opens; Cancel applies nothing.
        menu.Close();
        verb.Command!.Execute(verb.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        var dlg = await h.WaitForDialogAsync("CollectionEditor");
        Assert.Equal("Edit StatusBar1.Panels", dlg.Window.Title);
        dlg.Press("Escape");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, s.Document.Designer.Undo.UndoCount);

        // A component without verbs: the standard menu only.
        (x, y) = s.Center("GreetButton");
        s.RightClick(x, y);
        Assert.Equal("Context.edit.cut", Id(Assert.IsType<ContextMenu>(s.Control.ContextMenu).Items[0]!));
        s.Control.ContextMenu!.Close();
    }

    /// <summary>The form files with a status bar, and a main menu and an action list in the tray (D-099, D-111).</summary>
    internal static void EditorFiles(HarnessOptions o)
    {
        Phase5(o);
        o.InitialFiles["MainForm.pas"] = Unit
            .Replace("GreetButton: TButton;", "GreetButton: TButton;\n    StatusBar1: TStatusBar;\n    MainMenu1: TMainMenu;\n    ActionList1: TActionList;", StringComparison.Ordinal)
            .Replace("StdCtrls;", "StdCtrls, ComCtrls, Menus, ActnList;", StringComparison.Ordinal)
            .ReplaceLineEndings("\r\n");
        var form = FormText.ReplaceLineEndings("\n");
        var at = form.TrimEnd().LastIndexOf("end", StringComparison.Ordinal);
        o.InitialFiles["MainForm.form"] = (form[..at]
            + "  object StatusBar1: TStatusBar\n    Left = 0\n    Top = 281\n    Width = 400\n    Height = 19\n    TabOrder = 2\n  end\n"
            + "  object MainMenu1: TMainMenu\n    DesignLeft = 8\n    DesignTop = 8\n  end\n"
            + "  object ActionList1: TActionList\n    DesignLeft = 88\n    DesignTop = 8\n  end\n" + form[at..]).ReplaceLineEndings("\r\n");
    }

    /// <summary>
    /// DSG-SUR-160, DSG-SUR-196 (D-099): a double-click on a component whose class has a <c>componentEditor</c> opens that
    /// editor instead of creating a handler — the Child List Editor for an action list (its keys, ids and the
    /// <c>OnExecute</c> handler on Enter, dialogs.md §27), the Menu Designer for a menu — on the tray tile and on its
    /// Structure node; Close only closes.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-SUR-160")]
    [Trait("Req", "DSG-SUR-196")]
    [Trait("Req", "DSG-SUR-198")]
    [Trait("Req", "UI-DLG-260")]
    [Trait("Req", "UI-DLG-261")]
    public async Task DoubleClickOpensComponentEditor()
    {
        await using var h = await HeadlessHarness.StartAsync(EditorFiles);
        var s = await OpenDesignAsync(h);

        // The action list's tray tile: the Child List Editor opens (640 × 400, resizable, "Edit ActionList1").
        var (x, y) = s.Center("ActionList1");
        DoubleClick(h, s.At(x, y));
        Assert.Equal(["ActionList1"], s.Selected);
        var dlg = await h.WaitForDialogAsync("ChildListEditor");
        try
        {
            Assert.Equal(DocumentView.Design, s.Document.ActiveView);
            Assert.Equal("Edit ActionList1", dlg.Window.Title);
            Assert.Equal((640, 400), (dlg.Window.Width, dlg.Window.Height));
            Assert.True(dlg.Window.CanResize);
            foreach (var field in (string[])["Items", "Items.Empty", "New", "Delete", "MoveUp", "MoveDown", "Grid", "Grid.Page.Properties",
                "Grid.Page.Events", "Close"])
            {
                Assert.True(dlg.TryFind<Control>(field) is not null, field);
            }

            Assert.True(dlg.Find<TextBlock>("Items.Empty").IsVisible);
            Assert.Equal("No items. Choose New to create one.", dlg.Find<TextBlock>("Items.Empty").Text);
            Assert.False(dlg.Find<Button>("Delete").IsEnabled);
            Assert.Equal("Dialog.ChildListEditor.Items", dlg.FocusedAutomationId);

            // Insert = New: the new action is listed and selected; the focus moves to the grid on its Caption row.
            dlg.Press("Insert");
            Assert.NotNull(dlg.TryFind<ListBoxItem>("Item.Action1"));
            Assert.Equal("Dialog.ChildListEditor.Grid", dlg.FocusedAutomationId);
            Assert.NotNull(dlg.TryFind<Control>("Grid.Row.Caption"));
            Assert.Contains("    Action1: TAction;", s.Document.Buffer.Text, StringComparison.Ordinal);
            dlg.Click("New");
            Assert.NotNull(dlg.TryFind<ListBoxItem>("Item.Action2"));
            Assert.False(dlg.Window.IsClosed());

            // Ctrl+Up in the list moves the selected action; Delete deletes it.
            dlg.Find<ListBoxItem>("Item.Action2").Focus(NavigationMethod.Tab);
            dlg.Press("Ctrl+Up");
            Assert.Equal(["Action2", "Action1"], s.Object("ActionList1").Children.Select(c => c.Name));
            Assert.Equal("Dialog.ChildListEditor.Item.Action2", dlg.FocusedAutomationId);

            // Enter on a row creates and assigns OnExecute without closing; the Events page shows the method.
            dlg.Press("Enter");
            Assert.False(dlg.Window.IsClosed());
            Assert.Equal("Action2Execute", ((IdentValue)s.Object("Action2").FindProperty("OnExecute")!.Value).Name);
            Assert.Equal("Action2Execute", dlg.Find<TextBlock>("Grid.Row.OnExecute.Value").Text);
            Assert.Equal(DocumentView.Design, s.Document.ActiveView);

            dlg.Find<ListBoxItem>("Item.Action2").Focus(NavigationMethod.Tab);
            dlg.Press("Delete");
            Assert.Equal(["Action1"], s.Object("ActionList1").Children.Select(c => c.Name));
        }
        catch
        {
            dlg.Window.Close();
            throw;
        }

        // Escape is Close: the operations stay applied.
        var undo = s.Document.Designer.Undo.UndoCount;
        dlg.Press("Escape");
        Dispatcher.UIThread.RunJobs();
        Assert.True(dlg.Window.IsClosed());
        Assert.Equal(undo, s.Document.Designer.Undo.UndoCount);
        Assert.Null(s.Object("Action1").FindProperty("OnExecute"));

        // The menu's tray tile: the Menu Designer (no handler is created on the menu).
        (x, y) = s.Center("MainMenu1");
        DoubleClick(h, s.At(x, y));
        var menu = await h.WaitForDialogAsync("MenuDesigner");
        menu.Press("Escape");
        Dispatcher.UIThread.RunJobs();
        Assert.True(menu.Window.IsClosed());
        Assert.Equal(undo, s.Document.Designer.Undo.UndoCount);

        // The Structure node of the action list: double-click opens its editor too.
        await h.ExecuteAsync("view.structure");
        h.DoubleClick("Tool.Structure.Row.Form/ActionList1");
        var again = await h.WaitForDialogAsync("ChildListEditor");
        Assert.Equal(["ActionList1"], s.Selected);
        again.Press("Escape");
        Dispatcher.UIThread.RunJobs();
        Assert.True(again.Window.IsClosed());
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DSG-130")]
    public async Task StatusBarShowsSelectionGeometry()
    {
        await using var h = await HeadlessHarness.StartAsync(Phase5);
        var s = await OpenDesignAsync(h);

        // The form selected: its position and client size; the text-editor fields are hidden, Language shows Form.
        Assert.Equal("MainForm: TMainForm — 200, 120 — 400 × 300", h.Status.Field("DesignerSelection"));
        foreach (var field in (string[])["CaretPosition", "Selection", "InsertMode", "Indent", "LineEnding", "Encoding"])
        {
            Assert.Null(h.Status.Field(field));
        }

        Assert.Equal("Form", h.Status.Field("Language"));

        // One control: name, class, left, top, width, height; the field follows moves.
        var (x, y) = s.Center("GreetButton");
        s.Click(x, y);
        Assert.Equal("GreetButton: TButton — 232, 15 — 75 × 25", h.Status.Field("DesignerSelection"));
        h.Press("Right");
        Assert.Equal("GreetButton: TButton — 240, 15 — 75 × 25", h.Status.Field("DesignerSelection"));

        // Several: the count.
        var (nx, ny) = s.Center("NameEdit");
        s.Down(nx, ny, RawInputModifiers.Shift);
        s.Up(nx, ny, RawInputModifiers.Shift);
        Assert.Equal(2, s.Surface.Selection.Items.Count);
        Assert.Equal("2 components selected", h.Status.Field("DesignerSelection"));

        // The Code view: the editor fields come back.
        h.Press("F7");
        Assert.Null(h.Status.Field("DesignerSelection"));
        Assert.NotNull(h.Status.Field("CaretPosition"));
        Assert.NotEqual("Form", h.Status.Field("Language"));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-WIN-012")]
    [Trait("Req", "UI-WIN-014")]
    public async Task DesignLayoutProfileActivates()
    {
        await using var h = await HeadlessHarness.StartAsync(Phase5);
        var layout = h.Services.Layout.Layout;
        var document = await OpenFormAsync(h);
        Assert.Equal(LayoutProfiles.Edit, layout.ActiveProfile);
        Assert.False(h.Find<Control>(AutomationIds.DockRight).IsVisible);

        // Entering the Design view activates the Design profile: Object Inspector and Tool Palette in the right dock.
        h.Click(ViewTab("Design"));
        Assert.Equal(LayoutProfiles.Design, layout.ActiveProfile);
        Assert.True(h.Find<Control>(AutomationIds.DockRight).IsVisible);
        Assert.Equal("ObjectInspector", layout.RightUpper.ActiveTool);
        Assert.Equal("ToolPalette", layout.RightLower.ActiveTool);
        Assert.Equal("Structure", layout.Left.ActiveTool);
        Assert.True(h.Services.Commands.Get("window.layout.design").IsChecked);
        Assert.False(h.Services.Commands.Get("window.layout.edit").IsChecked);

        // Focus stays in the document (UI-WIN-014).
        Assert.True(h.Find<DesignSurfaceControl>(AutomationIds.Designer("MainForm")).IsKeyboardFocusWithin);

        // Back to the code: Edit again.
        h.Press("F7");
        Assert.Equal(DocumentView.Code, document.ActiveView);
        Assert.Equal(LayoutProfiles.Edit, layout.ActiveProfile);
        Assert.False(h.Find<Control>(AutomationIds.DockRight).IsVisible);
        Assert.True(h.Find<Control>(AutomationIds.Editor("MainForm.pas")).IsKeyboardFocusWithin);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-CMD-011")]
    public async Task UndoFromObjectInspectorFocusUndoesDesignerAction()
    {
        await using var h = await HeadlessHarness.StartAsync(Phase5);
        var s = await OpenDesignAsync(h);
        var (x, y) = s.Center("GreetButton");
        s.Click(x, y);
        h.Press("Delete");
        Assert.Null(s.Document.Designer.Model!.FindByName("GreetButton"));

        // Focus in the Object Inspector: Ctrl+Z undoes the designer action of the active document; focus stays there.
        var inspector = h.Find<Control>(AutomationIds.Tool("ObjectInspector"));
        h.Click("Tool.ObjectInspector.List");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("ObjectInspector", h.Services.FocusTracker.Current.ToolWindowName);
        Assert.True(h.Services.Commands.CanExecute("edit.undo"));
        h.Press("Ctrl+Z");
        Assert.NotNull(s.Document.Designer.Model!.FindByName("GreetButton"));
        Assert.Contains("    GreetButton: TButton;\r\n", s.Document.Buffer.Text, StringComparison.Ordinal);
        Assert.True(inspector.IsKeyboardFocusWithin);

        // Redo from there as well.
        await h.ExecuteAsync("edit.redo");
        Assert.Null(s.Document.Designer.Model!.FindByName("GreetButton"));
        Assert.True(inspector.IsKeyboardFocusWithin);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DSG-091")]
    [Trait("Req", "DSG-FD-071")]
    public async Task FormErrorsInfoBarShowsProblems()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            Phase5(o);
            o.InitialFiles["MainForm.form"] = FormText.ReplaceLineEndings("\n")
                .Replace("    TabOrder = 0\n", "    TabOrder = 0\n    Shade = 3\n", StringComparison.Ordinal).ReplaceLineEndings("\r\n");
        });
        var document = await OpenFormAsync(h);
        h.Click(ViewTab("Design"));

        // One unknown property: InfoBar.FormErrors (warning) with the singular text; the component stays editable around it.
        Assert.Equal([AutomationIds.InfoBar("FormErrors")], InfoBars(h));
        Assert.Equal(Strings.Msg_Designer_FormParseErrorsOne, InfoBarText(h, "FormErrors"));
        Assert.False(document.Designer.Surface!.IsReadOnly);

        // Show Problems: the Messages tool window lists the form's diagnostics (only those), at the .form file.
        h.Click(AutomationIds.InfoBarAction("FormErrors", "ShowProblems"));
        Assert.Equal("Messages", h.Services.Layout.Layout.Bottom.ActiveTool);
        var item = Assert.Single(h.Services.Messages.Items);
        Assert.Equal(("F0102", Core.Build.MessageSeverity.Warning), (item.Code, item.Severity));
        Assert.Equal(Path.Combine(h.TempDir, "MainForm.form"), item.Location!.FilePath);
        Assert.Equal(14, item.Location.Line);
        Assert.Equal(DocumentView.Design, document.ActiveView);

        // View as Text: the Text view; back in the Design view the bar is still there; × dismisses it.
        h.Click(AutomationIds.InfoBarAction("FormErrors", "ViewAsText"));
        Assert.Equal(DocumentView.Text, document.ActiveView);
        h.Click(ViewTab("Design"));
        Assert.Equal([AutomationIds.InfoBar("FormErrors")], InfoBars(h));
        document.Designer.DesignInfoBars.Single().Dismiss();
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(InfoBars(h));
    }

    /// <summary>
    /// DSG-FD-071 (D-109): a double-click on a form problem in Messages opens the form's Text view at the diagnostic span of
    /// the last parsed text; once the model has changed, it selects the object in the Design view instead.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-FD-071")]
    public async Task FormProblemClickOpensTextView()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            Phase5(o);
            o.InitialFiles["MainForm.form"] = FormText.ReplaceLineEndings("\n")
                .Replace("    TabOrder = 0\n", "    TabOrder = 0\n    Shade = 3\n", StringComparison.Ordinal).ReplaceLineEndings("\r\n");
        });
        var document = await OpenFormAsync(h);
        h.Click(ViewTab("Design"));
        h.Click(AutomationIds.InfoBarAction("FormErrors", "ShowProblems"));
        Dispatcher.UIThread.RunJobs();

        // The model is unchanged: the Text view of the form, caret and selection on the diagnostic span, editor focused.
        h.DoubleClick("Tool.Messages.Row.0.Description");
        await h.WaitForAsync(() => document.ActiveView == DocumentView.Text, "Text view");
        Assert.Same(document, h.Services.Documents.ActiveDocument);
        var text = document.FormText;
        var item = Assert.Single(h.Services.Messages.Items);
        Assert.Equal(new Core.Text.TextPosition(14, item.Location!.Column), text.ViewState.Caret);
        Assert.False(text.Selection.IsEmpty);
        Assert.StartsWith("Shade", text.Buffer.GetText(text.Selection.Start, text.Selection.End - text.Selection.Start), StringComparison.Ordinal);

        // The model changes (a designer edit): the same row now selects the object in the Design view.
        h.Click(ViewTab("Design"));
        var designer = document.Designer;
        designer.Surface!.Selection.Select(designer.Model!.FindByName("GreetButton")!);
        Assert.True(designer.Surface.Commit("Undo_Designer_Property",
            [new FreexComp.Designer.Editing.SetProperty("GreetButton", "Tag", new IntegerValue(7, false))]));
        Dispatcher.UIThread.RunJobs();
        h.DoubleClick("Tool.Messages.Row.0.Description");
        await h.WaitForAsync(() => designer.Surface!.Selection.Items.Select(o => o.Name).SequenceEqual(["NameEdit"]), "NameEdit selected");
        Assert.Equal(DocumentView.Design, document.ActiveView);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-CS-035")]
    public async Task FieldTypeMismatchFixDeclaration()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            Phase5(o);
            o.InitialFiles["MainForm.pas"] = Unit.Replace("    NameEdit: TEdit;", "    NameEdit: TMemo;", StringComparison.Ordinal).ReplaceLineEndings("\r\n");
        });
        var document = await OpenFormAsync(h);
        h.Click(ViewTab("Design"));

        // The field is declared with another type: InfoBar.FieldTypeMismatch; the form stays editable.
        Assert.Equal([AutomationIds.InfoBar("FieldTypeMismatch")], InfoBars(h));
        Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Msg_Designer_FieldTypeMismatch, "NameEdit", "TMemo", "TEdit"),
            InfoBarText(h, "FieldTypeMismatch"));
        Assert.False(document.Designer.Surface!.IsReadOnly);

        // Fix Declaration: the declaration takes the form's type (code sync), the bar goes; the Design view stays.
        h.Click(AutomationIds.InfoBarAction("FieldTypeMismatch", "FixDeclaration"));
        Assert.Contains("    NameEdit: TEdit;\r\n", document.Buffer.Text, StringComparison.Ordinal);
        Assert.Empty(InfoBars(h));
        Assert.Equal(DocumentView.Design, document.ActiveView);

        // A new mismatch typed in the code: Ignore hides the bar for this model.
        document.Buffer.ReplaceAll(document.Buffer.Text.Replace("    GreetButton: TButton;", "    GreetButton: TLabel;", StringComparison.Ordinal), resetUndo: false);
        Dispatcher.UIThread.RunJobs();
        h.Press("F7");
        h.Press("F7");
        Assert.Equal(DocumentView.Design, document.ActiveView);
        Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Msg_Designer_FieldTypeMismatch, "GreetButton", "TLabel", "TButton"),
            InfoBarText(h, "FieldTypeMismatch"));
        h.Click(AutomationIds.InfoBarAction("FieldTypeMismatch", "Ignore"));
        Assert.Empty(InfoBars(h));
        Assert.Contains("    GreetButton: TLabel;", document.Buffer.Text, StringComparison.Ordinal);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-FD-027")]
    [Trait("Req", "UI-DSG-092")]
    public async Task FormReadOnlyKindInfoBar()
    {
        const string inheritedChild = "  inherited Base1: TButton\r\n    Caption = 'Base'\r\n  end\r\nend\r\n";
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            Phase5(o);
            var form = FormText.ReplaceLineEndings("\r\n");
            o.InitialFiles["MainForm.form"] = form[..form.LastIndexOf("end\r\n", StringComparison.Ordinal)] + inheritedChild;
        });
        var document = await OpenFormAsync(h);
        h.Click(ViewTab("Design"));

        // An inherited component: InfoBar.FormReadOnlyKind (info); the item is preserved, the rest of the form editable.
        Assert.Equal([AutomationIds.InfoBar("FormReadOnlyKind")], InfoBars(h));
        Assert.Equal(Strings.Msg_Designer_InheritedNotSupported, InfoBarText(h, "FormReadOnlyKind"));
        Assert.True(document.Designer.Model!.FindByName("Base1")!.IsPreserved);
        Assert.False(document.Designer.Surface!.IsReadOnly);
        Assert.Equal(InfoBarSeverity.Info, document.Designer.DesignInfoBars.Single().Severity);

        // View as Text goes to the Text view.
        h.Click(AutomationIds.InfoBarAction("FormReadOnlyKind", "ViewAsText"));
        Assert.Equal(DocumentView.Text, document.ActiveView);

        // An inherited root: the whole form is read-only (DSG-FD-027) with the same bar.
        var root = "inherited MainForm: TMainForm\r\n  Caption = 'Derived'\r\nend\r\n";
        document.FormBuffer.ReplaceAll(root, resetUndo: false);
        Dispatcher.UIThread.RunJobs();
        h.Click(ViewTab("Design"));
        Assert.Equal(DocumentView.Design, document.ActiveView);
        Assert.Equal([AutomationIds.InfoBar("FormReadOnlyKind")], InfoBars(h));
        Assert.True(document.Designer.Surface!.IsReadOnly);
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
