using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Designer.Model;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Icons;
using FreexComp.Ide.Tests.Fixtures.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.Views;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The Menu Designer window (dialogs.md §26, UI-DLG-250 … 253; DSG-SUR-197, 199; D-098).</summary>
[Trait("Phase", "5")]
[Trait("Kind", "Ui")]
public sealed class MenuDesignerViewTests
{
    private const string Unit = """
        unit MainForm;

        interface

        uses
          Classes, Controls, Forms, Menus;

        type
          TMainForm = class(TForm)
            MainMenu1: TMainMenu;
          private
          public
          end;

        var
          MainForm: TMainForm;

        implementation

        {$FORM MainForm.form}

        end.

        """;

    private const string Form = """
        object MainForm: TMainForm
          Left = 200
          Top = 120
          Caption = 'Hello Form'
          ClientWidth = 400
          ClientHeight = 300
          Menu = MainMenu1
          PixelsPerInch = 96
          object MainMenu1: TMainMenu
            DesignLeft = 320
            DesignTop = 120
          end
        end

        """;

    private static IReadOnlyList<string> Nodes(DialogDriver dlg, TreeViewItem? parent = null) =>
        [.. (parent?.Items ?? dlg.Find<TreeView>("Tree").Items).Cast<TreeViewItem>().Select(i => AutomationIds.Get(i)!["Dialog.MenuDesigner.Node.".Length..])];

    private static FormObject Item(FormDocumentViewModel d, string name) => d.Designer.Model!.FindByName(name)!;

    /// <summary>Clicks a node's own header (a node's bounds include its sub-items), near its start: a long caption ends outside the tree.</summary>
    private static void ClickNode(DialogDriver dlg, string name)
    {
        var header = (Control)dlg.Find<TreeViewItem>("Node." + name).Header!;
        var point = header.TranslatePoint(new Avalonia.Point(Math.Min(header.Bounds.Width / 2, 12), header.Bounds.Height / 2), dlg.Window)!.Value;
        dlg.Window.MouseDown(point, Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        dlg.Window.MouseUp(point, Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-250")]
    [Trait("Req", "UI-DLG-251")]
    [Trait("Req", "UI-DLG-252")]
    [Trait("Req", "DSG-SUR-197")]
    public async Task KeyboardAndAutomationIds()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.ToolWindows.Add(SampleToolViewModel.Definition("ToolPalette", ToolGroupId.RightLower, 5, "Cmd_view_toolPalette"));
            o.InitialFiles["MainForm.pas"] = Unit.ReplaceLineEndings("\r\n");
            o.InitialFiles["MainForm.form"] = Form.ReplaceLineEndings("\r\n");
        });
        var d = await DesignerViewTests.OpenFormAsync(h);
        h.Click(AutomationIds.DocumentViewTab("MainForm.pas", "Design"));
        d.Designer.Surface!.Selection.Select(Item(d, "MainMenu1"));
        Dispatcher.UIThread.RunJobs();

        // designer.editComponent opens the dialog (awaited at the end: it completes when the dialog closes).
        var command = h.Services.Commands.ExecuteAsync("designer.editComponent");
        var dlg = await h.WaitForDialogAsync("MenuDesigner");
        try
        {
            await DriveAsync(h, d, dlg);
        }
        catch
        {
            dlg.Window.Close(); // the failure itself is reported, not the dialog left open
            throw;
        }

        await command;
    }

    private static async Task DriveAsync(HeadlessHarness h, FormDocumentViewModel d, DialogDriver dlg)
    {
        Assert.Equal("Menu Designer – MainMenu1", dlg.Window.Title);
        Assert.Equal((720, 460), (dlg.Window.Width, dlg.Window.Height));
        Assert.True(dlg.Window.CanResize);
        foreach (var field in (string[])["Tree", "Tree.Empty", "Caption", "ShortCut", "Image", "Grid", "Grid.Page.Properties", "Grid.Page.Events",
            "NewItem", "InsertBefore", "NewSubItem", "NewSeparator", "Delete", "MoveUp", "MoveDown", "Warning", "Close"])
        {
            Assert.True(dlg.TryFind<Control>(field) is not null, field);
        }

        Assert.True(dlg.Find<TextBlock>("Tree.Empty").IsVisible); // empty menu: the placeholder
        Assert.False(dlg.Find<Button>("InsertBefore").IsEnabled);
        Assert.False(dlg.Find<TextBox>("Caption").IsEnabled);
        Assert.Equal("Dialog.MenuDesigner.Tree", dlg.FocusedAutomationId);

        // Insert = New Item: the new node is selected, the focus moves to Caption (UI-DLG-250); Enter commits and renames.
        dlg.Press("Insert");
        Assert.Equal(["MenuItem1"], Nodes(dlg));
        Assert.Equal("Dialog.MenuDesigner.Caption", dlg.FocusedAutomationId);
        dlg.Type("Caption", "&Edit");
        dlg.Press("Enter");
        Assert.False(dlg.Window.IsClosed()); // Enter in Caption is not Close
        Assert.Equal(["Edit"], Nodes(dlg));
        Assert.Equal("&Edit", ((TextBlock)((DockPanel)dlg.Find<TreeViewItem>("Node.Edit").Header!).Children[1]).Text);
        Assert.NotNull(dlg.TryFind<Control>("Grid.Row.Name")); // the embedded grid shows the selected item

        // Ctrl+Right = New Sub-Item; ShortCut commits on Enter and shows right-aligned in the node.
        ClickNode(dlg, "Edit");
        dlg.Press("Ctrl+Right");
        Assert.Equal("Dialog.MenuDesigner.Caption", dlg.FocusedAutomationId);
        dlg.Type("Caption", "&Copy");
        dlg.Press("Enter");
        Assert.Equal(["Copy1"], Nodes(dlg, dlg.Find<TreeViewItem>("Node.Edit")));
        dlg.Type("ShortCut", "Ctrl+C");
        dlg.Press("Enter");
        Assert.Equal(16451, ((IntegerValue)Item(d, "Copy1").FindProperty("ShortCut")!.Value).Value);
        Assert.Equal("Ctrl+C", ((TextBlock)((DockPanel)dlg.Find<TreeViewItem>("Node.Copy1").Header!).Children[0]).Text);

        // New Separator (button): the separator is drawn as a rule; Ctrl+Up moves it up.
        ClickNode(dlg, "Copy1");
        dlg.Click("NewSeparator");
        Assert.Equal(["Copy1", "N1"], Nodes(dlg, dlg.Find<TreeViewItem>("Node.Edit")));
        Assert.IsType<Border>(dlg.Find<TreeViewItem>("Node.N1").Header);
        ClickNode(dlg, "N1");
        dlg.Press("Ctrl+Up");
        Assert.Equal(["N1", "Copy1"], Nodes(dlg, dlg.Find<TreeViewItem>("Node.Edit")));
        Assert.Equal("Dialog.MenuDesigner.Node.N1", dlg.FocusedAutomationId);

        // Delete on an item without sub-items deletes at once; the next sibling is selected.
        dlg.Press("Delete");
        Assert.Equal(["Copy1"], Nodes(dlg, dlg.Find<TreeViewItem>("Node.Edit")));
        Assert.Null(d.Designer.Model!.FindByName("N1"));

        // F2 focuses Caption with its text selected; Enter in the tree creates and assigns the OnClick handler.
        ClickNode(dlg, "Copy1");
        dlg.Press("F2");
        var caption = dlg.Find<TextBox>("Caption");
        Assert.Equal("Dialog.MenuDesigner.Caption", dlg.FocusedAutomationId);
        Assert.Equal((0, "&Copy".Length), (caption.SelectionStart, caption.SelectionEnd));
        ClickNode(dlg, "Copy1");
        dlg.Press("Enter");
        Assert.False(dlg.Window.IsClosed());
        Assert.Equal("Copy1Click", ((IdentValue)Item(d, "Copy1").FindProperty("OnClick")!.Value).Name);
        Assert.Contains("procedure TMainForm.Copy1Click(Sender: TObject);", d.Buffer.Text, StringComparison.Ordinal);

        // The Events page tab shows the handler in the embedded grid.
        dlg.Click("Grid.Page.Events");
        Assert.Equal("Copy1Click", dlg.Find<TextBlock>("Grid.Row.OnClick.Value").Text);

        // Delete on an item with sub-items asks first (UI-DLG-251); Cancel keeps it.
        ClickNode(dlg, "Edit");
        dlg.Press("Delete");
        var question = await h.WaitForDialogAsync("Message");
        question.Click("Cancel");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(d.Designer.Model!.FindByName("Edit"));
        Assert.True(question.Window.IsClosed());
        await h.WaitForAsync(() => dlg.FocusedAutomationId == "Dialog.MenuDesigner.Node.Edit", "focus back on the tree node"); // after the message box

        // Escape is Close: the operations stay applied, each its own undo unit (UI-DLG-252).
        var undo = d.Designer.Undo.UndoCount;
        dlg.Press("Escape");
        Dispatcher.UIThread.RunJobs();
        Assert.True(dlg.Window.IsClosed());
        Assert.Equal(undo, d.Designer.Undo.UndoCount);
        Assert.True(undo >= 7);
    }
    /// <summary>UI-DLG-252 (D-112): the dialog remembers its size between openings (<c>dialogs.MenuDesigner.size</c>), nothing else.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-252")]
    [Trait("Req", "UI-DLG-005")]
    public async Task RemembersSize()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.ToolWindows.Add(SampleToolViewModel.Definition("ToolPalette", ToolGroupId.RightLower, 5, "Cmd_view_toolPalette"));
            o.InitialFiles["MainForm.pas"] = Unit.ReplaceLineEndings("\r\n");
            o.InitialFiles["MainForm.form"] = Form.ReplaceLineEndings("\r\n");
        });
        var d = await DesignerViewTests.OpenFormAsync(h);
        h.Click(AutomationIds.DocumentViewTab("MainForm.pas", "Design"));
        d.Designer.Surface!.Selection.Select(Item(d, "MainMenu1"));
        Dispatcher.UIThread.RunJobs();
        Assert.Null(h.Services.Layout.GetDialogValue("MenuDesigner", "size"));

        async Task<(double, double)> OpenAndCloseAsync((double Width, double Height)? resizeTo)
        {
            var command = h.Services.Commands.ExecuteAsync("designer.editComponent");
            var dlg = await h.WaitForDialogAsync("MenuDesigner");
            var opened = (dlg.Window.Width, dlg.Window.Height);
            if (resizeTo is { } size)
            {
                dlg.Window.Width = size.Width;
                dlg.Window.Height = size.Height;
            }

            dlg.Press("Escape");
            await h.WaitForAsync(() => command.IsCompleted, "Menu Designer closed");
            await command;
            return opened;
        }

        Assert.Equal((720.0, 460.0), await OpenAndCloseAsync((800, 520)));
        Assert.Equal("800,520", h.Services.Layout.GetDialogValue("MenuDesigner", "size"));
        Assert.Equal((800.0, 520.0), await OpenAndCloseAsync(null));
    }

    // ---- Phase 8, F05-086 (docs/spec/designer/phase8/menu-designer-collection-editors.md) ---------------------------

    /// <summary>
    /// Opens the form of these tests (or <paramref name="form"/> / <paramref name="unit"/>) in the Design view and the Menu
    /// Designer on <c>MainMenu1</c>, runs <paramref name="drive"/> in it and closes it with Escape. <paramref name="configure"/>
    /// changes the harness options (the metadata to load).
    /// </summary>
    private static async Task DesignAsync(Func<HeadlessHarness, FormDocumentViewModel, DialogDriver, Task> drive, string? form = null, string? unit = null,
        Action<HarnessOptions>? configure = null)
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.ToolWindows.Add(SampleToolViewModel.Definition("ToolPalette", ToolGroupId.RightLower, 5, "Cmd_view_toolPalette"));
            o.InitialFiles["MainForm.pas"] = (unit ?? Unit).ReplaceLineEndings("\r\n");
            o.InitialFiles["MainForm.form"] = (form ?? Form).ReplaceLineEndings("\r\n");
            configure?.Invoke(o);
        });
        // With options of its own the test keeps the metadata the harness loaded; else the test metadata, as the other tests.
        var d = configure is null ? await DesignerViewTests.OpenFormAsync(h)
            : Assert.IsType<FormDocumentViewModel>((await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, "MainForm.pas"))).Value);
        Dispatcher.UIThread.RunJobs();
        h.Click(AutomationIds.DocumentViewTab("MainForm.pas", "Design"));
        d.Designer.Surface!.Selection.Select(Item(d, "MainMenu1"));
        Dispatcher.UIThread.RunJobs();
        var command = h.Services.Commands.ExecuteAsync("designer.editComponent");
        var dlg = await h.WaitForDialogAsync("MenuDesigner");
        try
        {
            await drive(h, d, dlg);
            Assert.False(dlg.Window.IsClosed()); // no operation of these tests leaves the dialog
        }
        catch
        {
            dlg.Window.Close(); // the failure itself is reported, not the dialog left open
            throw;
        }

        dlg.Press("Escape");
        await h.WaitForAsync(() => command.IsCompleted, "Menu Designer closed");
        await command;
    }

    /// <summary>The caption text block of a node (a separator has none): beside the shortcut, with the warning mark in a panel of their own.</summary>
    private static TextBlock CaptionOf(DialogDriver dlg, string name) =>
        ((DockPanel)dlg.Find<TreeViewItem>("Node." + name).Header!).Children[1] switch
        {
            TextBlock caption => caption,
            var marked => ((Panel)marked).Children.OfType<TextBlock>().Single(),
        };

    /// <summary>The warning mark of a node (UI-DLG-340), or null.</summary>
    private static IdeIcon? MarkOf(DialogDriver dlg, string name) =>
        ((Control)dlg.Find<TreeViewItem>("Node." + name).Header!).GetVisualDescendants().OfType<IdeIcon>().FirstOrDefault(i => i.IconName == "warning-corner");

    private static string? NameOf(DialogDriver dlg, string node) => AutomationProperties.GetName(dlg.Find<TreeViewItem>("Node." + node));

    /// <summary>F2 on the node, then the caption typed over the selected text and committed with Enter.</summary>
    private static void Retype(DialogDriver dlg, string node, string caption)
    {
        ClickNode(dlg, node);
        dlg.Press("F2");
        Assert.Equal("Dialog.MenuDesigner.Caption", dlg.FocusedAutomationId);
        dlg.Window.KeyTextInput(caption);
        Dispatcher.UIThread.RunJobs();
        dlg.Press("Enter");
    }

    /// <summary>New Item after <paramref name="after"/> (null: the tree as it is focused) with that caption.</summary>
    private static void InsertItem(DialogDriver dlg, string? after, string caption, string gesture = "Insert")
    {
        if (after is not null)
        {
            ClickNode(dlg, after);
        }

        dlg.Press(gesture);
        Assert.Equal("Dialog.MenuDesigner.Caption", dlg.FocusedAutomationId);
        dlg.Type("Caption", caption);
        dlg.Press("Enter");
    }

    /// <summary>
    /// D-155 (UI-DLG-340, DSG-SUR-252): the warning line on the button strip and the mark on the nodes appear with the second
    /// item of the same access key and go when the caption is corrected; the line is one trimmed line (the strip keeps its height).
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-DLG-340")]
    [Trait("Req", "DSG-SUR-252")]
    public Task DuplicateAcceleratorShownAndCleared() => DesignAsync((_, d, dlg) =>
    {
        var warning = dlg.Find<Control>("Warning");
        var text = warning.GetVisualDescendants().OfType<TextBlock>().Single();
        var close = dlg.Find<Button>("Close");
        var strip = close.GetVisualAncestors().OfType<Border>().First();
        var stripHeight = strip.Bounds.Height;
        Assert.False(warning.IsVisible); // hidden at first: it takes no room
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(warning));
        Assert.DoesNotContain(warning.GetVisualDescendants().Prepend(warning).OfType<InputElement>(), c => c.Focusable); // not a tab stop

        InsertItem(dlg, null, "&Open");
        Assert.False(warning.IsVisible);
        Assert.Null(MarkOf(dlg, "Open"));
        Assert.Equal("Open", NameOf(dlg, "Open"));

        // The second caption with the key O: the line with the text, the mark and the name suffix on both nodes.
        InsertItem(dlg, "Open", "C&opy");
        const string Expected = "O is the access key of more than one item in MainMenu1: Open, Copy.";
        Assert.True(warning.IsEffectivelyVisible);
        Assert.Equal(Expected, text.Text);
        Assert.Equal(Expected, AutomationProperties.GetName(warning));
        Assert.Equal(Expected, ToolTip.GetTip(warning)); // the full text, also when the line is trimmed
        var icon = warning.GetVisualDescendants().OfType<IdeIcon>().Single();
        Assert.Equal(("warning", 16.0, 16.0), (icon.IconName, icon.Bounds.Width, icon.Bounds.Height));
        foreach (var node in (string[])["Open", "Copy1"])
        {
            Assert.EndsWith(", duplicate access key", NameOf(dlg, node), StringComparison.Ordinal);
            var mark = MarkOf(dlg, node)!;
            var caption = CaptionOf(dlg, node);
            Assert.True(mark.IsEffectivelyVisible);
            Assert.Equal((10.0, 10.0), (mark.Bounds.Width, mark.Bounds.Height));
            Assert.Equal(caption.Bounds.Right + 4, mark.Bounds.Left); // after the caption text, space.2
            Assert.Equal(Expected, ToolTip.GetTip((Control)dlg.Find<TreeViewItem>("Node." + node).Header!)); // on the row, not on the sub-items
        }

        Assert.Equal("Open, duplicate access key", NameOf(dlg, "Open"));
        Assert.Equal(stripHeight, strip.Bounds.Height);
        Assert.True(warning.Bounds.Height <= close.Bounds.Height);

        // A long text is one line trimmed before Close: the strip does not grow, the tooltip has the whole text.
        var words = string.Join(" ", Enumerable.Repeat("option", 60));
        InsertItem(dlg, "Copy1", "&O " + words);
        var third = Item(d, "MainMenu1").Children[2].Name;
        Assert.EndsWith(", duplicate access key", NameOf(dlg, third), StringComparison.Ordinal);
        Assert.Equal((TextTrimming.CharacterEllipsis, TextWrapping.NoWrap), (text.TextTrimming, text.TextWrapping));
        Assert.Contains(words, (string)ToolTip.GetTip(warning)!, StringComparison.Ordinal);
        Assert.Equal(stripHeight, strip.Bounds.Height);

        // A caption longer than the tree is cut with an ellipsis: the tree does not scroll sideways, the mark stays in view.
        var tree = dlg.Find<TreeView>("Tree");
        var treeRight = tree.TranslatePoint(new Point(tree.Bounds.Width, 0), dlg.Window)!.Value.X;
        var longMark = MarkOf(dlg, third)!;
        Assert.Equal(TextTrimming.CharacterEllipsis, CaptionOf(dlg, third).TextTrimming);
        Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, ScrollViewer.GetHorizontalScrollBarVisibility(tree));
        Assert.Equal(0, tree.GetVisualDescendants().OfType<ScrollViewer>().First().Offset.X);
        Assert.True(longMark.IsEffectivelyVisible);
        Assert.Equal(10, longMark.Bounds.Width);
        Assert.True(longMark.TranslatePoint(new Point(longMark.Bounds.Width, 0), dlg.Window)!.Value.X <= treeRight - 16, "the mark is inside the tree, clear of its scroll bar");

        var right = warning.TranslatePoint(new Avalonia.Point(warning.Bounds.Width, 0), dlg.Window)!.Value.X;
        var closeLeft = close.TranslatePoint(new Avalonia.Point(0, 0), dlg.Window)!.Value.X;
        Assert.True(right <= closeLeft - 8, $"the line ends at {right}, Close starts at {closeLeft}");
        Assert.True(text.Bounds.Height <= 18, $"one line: {text.Bounds.Height}");

        // Corrected captions: the third leaves the warning, the second ends it.
        Retype(dlg, third, "&Paste");
        Assert.Equal(Expected, text.Text);
        Assert.Null(MarkOf(dlg, third)); // a name derived from a caption is kept (UIF-727): only generated names follow
        Retype(dlg, "Copy1", "&Copy");
        Assert.False(warning.IsVisible);
        Assert.Equal(["Open", "Copy1", third], Nodes(dlg));
        Assert.Equal(["Open", "Copy", "Paste"], Nodes(dlg).Select(node => NameOf(dlg, node)));
        Assert.All(Nodes(dlg), node =>
        {
            Assert.Null(MarkOf(dlg, node));
            Assert.Null(ToolTip.GetTip((Control)dlg.Find<TreeViewItem>("Node." + node).Header!));
        });
        Assert.Equal(stripHeight, strip.Bounds.Height);
        return Task.CompletedTask;
    });

    /// <summary>A designed menu: File (Open…, a separator, Exit) and Help (About FreexComp).</summary>
    private const string MenuForm = """
        object MainForm: TMainForm
          Left = 200
          Top = 120
          Caption = 'Hello Form'
          ClientWidth = 400
          ClientHeight = 300
          Menu = MainMenu1
          PixelsPerInch = 96
          object MainMenu1: TMainMenu
            DesignLeft = 320
            DesignTop = 120
            object FileMenu: TMenuItem
              Caption = '&File'
              object FileOpen: TMenuItem
                Caption = '&Open…'
                ShortCut = 16463
              end
              object N1: TMenuItem
                Caption = '-'
              end
              object FileExit: TMenuItem
                Caption = 'E&xit'
              end
            end
            object HelpMenu: TMenuItem
              Caption = '&Help'
              object HelpAbout: TMenuItem
                Caption = '&About FreexComp'
              end
            end
          end
        end

        """;

    private static readonly string MenuUnit = Unit.Replace("    MainMenu1: TMainMenu;",
        "    MainMenu1: TMainMenu;\n    FileMenu: TMenuItem;\n    FileOpen: TMenuItem;\n    N1: TMenuItem;\n    FileExit: TMenuItem;\n"
        + "    HelpMenu: TMenuItem;\n    HelpAbout: TMenuItem;", StringComparison.Ordinal);

    /// <summary>The row of a node: its own line of the tree, without its sub-items.</summary>
    private static Border RowOf(DialogDriver dlg, string name) =>
        dlg.Find<TreeViewItem>("Node." + name).GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_LayoutRoot");

    /// <summary>A point of a node's row at <paramref name="fraction"/> of its height, near the start of its caption (window coordinates).</summary>
    private static Point RowPoint(DialogDriver dlg, string name, double fraction)
    {
        var row = RowOf(dlg, name);
        var header = (Control)dlg.Find<TreeViewItem>("Node." + name).Header!;
        return new Point(header.TranslatePoint(new Point(12, 0), dlg.Window)!.Value.X,
            row.TranslatePoint(new Point(0, row.Bounds.Height * fraction), dlg.Window)!.Value.Y);
    }

    private static void PointerDown(DialogDriver dlg, Point at)
    {
        dlg.Window.MouseDown(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void PointerTo(DialogDriver dlg, Point at)
    {
        dlg.Window.MouseMove(at, RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();
    }

    private static void PointerUp(DialogDriver dlg, Point at)
    {
        dlg.Window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Presses on the row of <paramref name="node"/> and moves 6 DIP: the drag of that item has begun.</summary>
    private static void BeginDrag(DialogDriver dlg, string node)
    {
        var start = RowPoint(dlg, node, 0.5);
        PointerDown(dlg, start);
        PointerTo(dlg, start + new Point(6, 0));
    }

    private static Border Line(DialogDriver dlg) => dlg.Find<Border>("InsertionLine");

    /// <summary>The outline of the "into" target (UI-DLG-341): the other element of the layer that holds the insertion line.</summary>
    private static Border Outline(DialogDriver dlg) => ((Canvas)Line(dlg).Parent!).Children.OfType<Border>().Single(b => b != Line(dlg));

    /// <summary>The node highlighted as the "into" target (UI-DLG-341), or null.</summary>
    private static string? Highlighted(DialogDriver dlg) =>
        dlg.Find<TreeView>("Tree").GetVisualDescendants().OfType<TreeViewItem>().Where(i => i.Classes.Contains("dropTarget"))
            .Select(i => AutomationIds.Get(i)!["Dialog.MenuDesigner.Node.".Length..]).SingleOrDefault();

    private static IBrush? Brush(DialogDriver dlg, string key) =>
        dlg.Window.TryFindResource(key, dlg.Window.ActualThemeVariant, out var value) ? value as IBrush : null;

    /// <summary>
    /// D-156, card F05-086 acceptance 2 (DSG-SUR-253, 254, 256; UI-DLG-341): an item dragged into the upper quarter of a
    /// sibling's row shows the insertion line and lands before it — in the tree, the model and the saved form.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-253")]
    [Trait("Req", "DSG-SUR-254")]
    [Trait("Req", "DSG-SUR-256")]
    [Trait("Req", "UI-DLG-341")]
    public Task DragReordersSiblings() => DesignAsync(async (h, d, dlg) =>
    {
        var tree = dlg.Find<TreeView>("Tree");
        var line = Line(dlg);
        var file = dlg.Find<TreeViewItem>("Node.FileMenu");
        var undo = d.Designer.Undo.UndoCount;
        Assert.False(line.IsVisible);
        Assert.Equal(["FileOpen", "N1", "FileExit"], Nodes(dlg, file));

        // As opened: the focus is on the node, so Caption shows no selected text; the shortcut text ends before the room of
        // the tree's scroll bar (space.6).
        Assert.Equal("Dialog.MenuDesigner.Node.FileMenu", dlg.FocusedAutomationId);
        Assert.Equal("", dlg.Find<TextBox>("Caption").SelectedText);
        var shortcut = (TextBlock)((DockPanel)dlg.Find<TreeViewItem>("Node.FileOpen").Header!).Children[0];
        Assert.Equal("Ctrl+O", shortcut.Text);
        Assert.True(shortcut.TranslatePoint(new Point(shortcut.Bounds.Width, 0), dlg.Window)!.Value.X
            <= tree.TranslatePoint(new Point(tree.Bounds.Width, 0), dlg.Window)!.Value.X - 16, "the shortcut text is clear of the scroll bar");

        // A press selects (as before); a move of less than 4 DIP with the button held is no drag. (On another node than
        // the one dragged next: two presses at one place are a double-click, which makes the OnClick handler.)
        var start = RowPoint(dlg, "FileOpen", 0.5);
        PointerDown(dlg, start);
        Assert.Same(dlg.Find<TreeViewItem>("Node.FileOpen"), tree.SelectedItem);
        PointerTo(dlg, start + new Point(3, 3));
        PointerTo(dlg, start + new Point(-3, 3));
        Assert.False(line.IsVisible);
        Assert.Null(tree.Cursor);
        PointerUp(dlg, start + new Point(3, 3));
        Assert.Equal(undo, d.Designer.Undo.UndoCount);
        Assert.Equal(["FileOpen", "N1", "FileExit"], Nodes(dlg, file));
        Assert.Equal("Dialog.MenuDesigner.Node.FileOpen", dlg.FocusedAutomationId);

        // Press on Exit, move into the upper quarter of Open…: the insertion line at the upper edge of that row, named.
        BeginDrag(dlg, "FileExit");
        PointerTo(dlg, RowPoint(dlg, "FileOpen", 0.1));
        Assert.True(line.IsEffectivelyVisible);
        Assert.Equal("before Open…", AutomationProperties.GetName(line));
        Assert.Null(Highlighted(dlg));
        Assert.Null(tree.Cursor);
        var row = RowOf(dlg, "FileOpen");
        var rowTop = row.TranslatePoint(default, dlg.Window)!.Value.Y;
        var lineAt = line.TranslatePoint(default, dlg.Window)!.Value;
        Assert.Equal(2, line.Bounds.Height);
        Assert.Equal(rowTop - 1, lineAt.Y); // centred on the upper edge of the row
        var caption = CaptionOf(dlg, "FileOpen");
        Assert.Equal(caption.TranslatePoint(default, dlg.Window)!.Value.X, lineAt.X); // from the indent of the target level …
        var frame = tree.TranslatePoint(new Point(tree.Bounds.Width, 0), dlg.Window)!.Value.X;
        Assert.Equal(frame, lineAt.X + line.Bounds.Width); // … to the right edge of the tree
        Assert.Same(Brush(dlg, "FxcAccentDefaultBrush"), line.Background);

        // The lower quarter of the same row: after it; a separator row: upper half before, lower half after.
        PointerTo(dlg, RowPoint(dlg, "FileOpen", 0.9));
        Assert.Equal("after Open…", AutomationProperties.GetName(line));
        Assert.Equal(rowTop + row.Bounds.Height - 1, line.TranslatePoint(default, dlg.Window)!.Value.Y);
        PointerTo(dlg, RowPoint(dlg, "N1", 0.4));
        Assert.Equal("before N1", AutomationProperties.GetName(line));
        PointerTo(dlg, RowPoint(dlg, "N1", 0.6));
        Assert.Equal("after N1", AutomationProperties.GetName(line));
        Assert.Equal(undo, d.Designer.Undo.UndoCount); // nothing changes before the release

        // Released in the upper quarter of Open…: Exit is first, selected and focused; one "Order" unit.
        PointerTo(dlg, RowPoint(dlg, "FileOpen", 0.1));
        PointerUp(dlg, RowPoint(dlg, "FileOpen", 0.1));
        Assert.False(line.IsVisible);
        file = dlg.Find<TreeViewItem>("Node.FileMenu");
        Assert.Equal(["FileExit", "FileOpen", "N1"], Nodes(dlg, file));
        Assert.Equal(["FileExit", "FileOpen", "N1"], Item(d, "FileMenu").Children.Select(c => c.Name));
        Assert.Same(dlg.Find<TreeViewItem>("Node.FileExit"), tree.SelectedItem);
        Assert.Equal("Dialog.MenuDesigner.Node.FileExit", dlg.FocusedAutomationId);
        Assert.Equal((undo + 1, FreexComp.Designer.Surface.UndoTitles.Order), (d.Designer.Undo.UndoCount, d.Designer.Undo.UndoTitleKey));
        Assert.True(d.IsModified);

        // Saved: the object FileExit stands before FileOpen in the form text.
        Assert.True(await h.Services.Documents.SaveAsync(d));
        var saved = File.ReadAllText(Path.Combine(h.TempDir, "MainForm.form"));
        Assert.True(saved.IndexOf("object FileExit: TMenuItem", StringComparison.Ordinal) < saved.IndexOf("object FileOpen: TMenuItem", StringComparison.Ordinal), saved);

        // Below the last row: after the last top-level item — the line under its last sub-item, at the top-level indent.
        BeginDrag(dlg, "FileExit");
        var below = tree.TranslatePoint(new Point(40, tree.Bounds.Height - 6), dlg.Window)!.Value;
        PointerTo(dlg, below);
        Assert.True(line.IsEffectivelyVisible);
        Assert.Equal("after Help", AutomationProperties.GetName(line));
        var last = RowOf(dlg, "HelpAbout");
        Assert.Equal(last.TranslatePoint(new Point(0, last.Bounds.Height), dlg.Window)!.Value.Y - 1, line.TranslatePoint(default, dlg.Window)!.Value.Y);
        Assert.Equal(CaptionOf(dlg, "HelpMenu").TranslatePoint(default, dlg.Window)!.Value.X, line.TranslatePoint(default, dlg.Window)!.Value.X);
        PointerUp(dlg, below);
        Assert.Equal(["FileMenu", "HelpMenu", "FileExit"], Nodes(dlg));
        Assert.Equal(undo + 2, d.Designer.Undo.UndoCount);

        // A release at the item's present place records nothing.
        BeginDrag(dlg, "FileExit");
        PointerTo(dlg, below);
        Assert.Equal("after Exit", AutomationProperties.GetName(line));
        Assert.Null(tree.Cursor);
        PointerUp(dlg, below);
        Assert.Equal(undo + 2, d.Designer.Undo.UndoCount);
    }, MenuForm, MenuUnit);

    /// <summary>
    /// D-156 (DSG-SUR-254, 255; UI-DLG-341): the middle half of a row drops into the item (highlight, no line); an item's
    /// own sub-tree and the outside of the tree are invalid ("no" cursor, nothing changes); a separator row has no "into".
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-254")]
    [Trait("Req", "DSG-SUR-255")]
    [Trait("Req", "UI-DLG-341")]
    public Task DragIntoItemAndIntoOwnSubtree() => DesignAsync((_, d, dlg) =>
    {
        var tree = dlg.Find<TreeView>("Tree");
        var line = Line(dlg);
        var undo = d.Designer.Undo.UndoCount;
        dlg.Find<TreeViewItem>("Node.HelpMenu").IsExpanded = false;
        Dispatcher.UIThread.RunJobs();
        var captionAt = CaptionOf(dlg, "HelpMenu").TranslatePoint(default, dlg.Window)!.Value;

        // The middle half of Help: the row is highlighted (bg.selected.inactive, 1 px accent border), no line.
        BeginDrag(dlg, "FileExit");
        PointerTo(dlg, RowPoint(dlg, "HelpMenu", 0.5));
        Assert.Equal("HelpMenu", Highlighted(dlg));
        Assert.False(line.IsVisible);
        Assert.Null(tree.Cursor);
        var row = RowOf(dlg, "HelpMenu");
        var outline = Outline(dlg);
        Assert.Same(Brush(dlg, "FxcBgSelectedInactiveBrush"), row.Background);
        Assert.True(outline.IsEffectivelyVisible);
        Assert.Same(Brush(dlg, "FxcAccentDefaultBrush"), outline.BorderBrush);
        Assert.Equal(new Thickness(1), outline.BorderThickness);
        Assert.Equal(new Rect(row.TranslatePoint(default, dlg.Window)!.Value, row.Bounds.Size),
            new Rect(outline.TranslatePoint(default, dlg.Window)!.Value, outline.Bounds.Size)); // around the row
        Assert.Equal(captionAt, CaptionOf(dlg, "HelpMenu").TranslatePoint(default, dlg.Window)!.Value); // the text does not shift

        // Released: Exit is the last sub-item of Help, Help is expanded, Exit selected and focused.
        PointerUp(dlg, RowPoint(dlg, "HelpMenu", 0.5));
        Assert.Null(Highlighted(dlg));
        Assert.False(outline.IsVisible);
        var help = dlg.Find<TreeViewItem>("Node.HelpMenu");
        Assert.True(help.IsExpanded);
        Assert.Equal(["HelpAbout", "FileExit"], Nodes(dlg, help));
        Assert.Equal(["FileOpen", "N1"], Nodes(dlg, dlg.Find<TreeViewItem>("Node.FileMenu")));
        Assert.Equal("Dialog.MenuDesigner.Node.FileExit", dlg.FocusedAutomationId);
        Assert.Equal((undo + 1, FreexComp.Designer.Surface.UndoTitles.Order), (d.Designer.Undo.UndoCount, d.Designer.Undo.UndoTitleKey));

        // File over its own sub-item, in every zone: no line, no highlight, the "no" cursor; a release changes nothing.
        BeginDrag(dlg, "FileMenu");
        foreach (var fraction in (double[])[0.1, 0.5, 0.9])
        {
            PointerTo(dlg, RowPoint(dlg, "FileOpen", fraction));
            Assert.False(line.IsVisible);
            Assert.Null(Highlighted(dlg));
            Assert.NotNull(tree.Cursor);
        }

        // Its own row: before / after are its present place (valid), into itself is not.
        PointerTo(dlg, RowPoint(dlg, "FileMenu", 0.5));
        Assert.NotNull(tree.Cursor);
        PointerTo(dlg, RowPoint(dlg, "FileMenu", 0.1));
        Assert.Null(tree.Cursor);
        Assert.Equal("before File", AutomationProperties.GetName(line));
        PointerTo(dlg, RowPoint(dlg, "FileOpen", 0.5));
        PointerUp(dlg, RowPoint(dlg, "FileOpen", 0.5));
        Assert.Null(tree.Cursor);
        Assert.Equal(undo + 1, d.Designer.Undo.UndoCount);
        Assert.Equal(["FileMenu", "HelpMenu"], Nodes(dlg));

        // A separator row has no "into": upper half before, lower half after.
        BeginDrag(dlg, "HelpAbout");
        PointerTo(dlg, RowPoint(dlg, "N1", 0.45));
        Assert.Null(Highlighted(dlg));
        Assert.Equal("before N1", AutomationProperties.GetName(line));
        PointerTo(dlg, RowPoint(dlg, "N1", 0.55));
        Assert.Null(Highlighted(dlg));
        Assert.True(line.IsVisible);
        Assert.Equal("after N1", AutomationProperties.GetName(line));

        // Outside the tree there is no position: the "no" cursor, a release there changes nothing.
        var outside = dlg.Find<TextBox>("Caption").TranslatePoint(new Point(20, 10), dlg.Window)!.Value;
        PointerTo(dlg, outside);
        Assert.False(line.IsVisible);
        Assert.NotNull(tree.Cursor);
        PointerUp(dlg, outside);
        Assert.Null(tree.Cursor);
        Assert.Equal(undo + 1, d.Designer.Undo.UndoCount);
        Assert.Equal(["HelpAbout", "FileExit"], Nodes(dlg, dlg.Find<TreeViewItem>("Node.HelpMenu")));
        return Task.CompletedTask;
    }, MenuForm, MenuUnit);

    /// <summary>
    /// D-156 (DSG-SUR-255): Escape during a drag cancels the drag only — the key is consumed and the dialog stays open; the
    /// mouse wheel scrolls the tree during a drag (DSG-SUR-254).
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-255")]
    [Trait("Req", "DSG-SUR-254")]
    public Task EscapeCancelsDragAndKeepsDialogOpen()
    {
        // A menu longer than the tree: thirty items under File.
        var items = string.Concat(Enumerable.Range(1, 30).Select(i => $"      object Item{i}: TMenuItem\n        Caption = 'Item {i}'\n      end\n"));
        var form = MenuForm.ReplaceLineEndings("\n").Replace("      object FileOpen: TMenuItem\n", items + "      object FileOpen: TMenuItem\n", StringComparison.Ordinal);
        var unit = MenuUnit.Replace("    FileMenu: TMenuItem;", "    FileMenu: TMenuItem;\n" + string.Concat(Enumerable.Range(1, 30).Select(i => $"    Item{i}: TMenuItem;\n")).TrimEnd('\n'),
            StringComparison.Ordinal);
        return DesignAsync((_, d, dlg) =>
        {
            var tree = dlg.Find<TreeView>("Tree");
            var line = Line(dlg);
            var scroll = tree.GetVisualDescendants().OfType<ScrollViewer>().First();
            var undo = d.Designer.Undo.UndoCount;
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);

            BeginDrag(dlg, "Item2");
            var at = RowPoint(dlg, "Item4", 0.1);
            PointerTo(dlg, at);
            Assert.True(line.IsVisible);
            Assert.Equal("before Item 4", AutomationProperties.GetName(line));

            // The wheel scrolls the tree during the drag; the position follows the row that is now under the pointer.
            Assert.Equal(0, scroll.Offset.Y);
            dlg.Window.MouseWheel(at, new Vector(0, -1));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(50, scroll.Offset.Y);
            var target = line.IsVisible ? AutomationProperties.GetName(line)! : "into " + Highlighted(dlg);
            Assert.Matches("^((before|after) Item |into Item)[5-8]$", target); // two rows further down

            // Escape cancels the drag only: no line, the dialog stays open, the release changes nothing.
            dlg.Press("Escape");
            Assert.False(dlg.Window.IsClosed());
            Assert.False(line.IsVisible);
            Assert.Null(tree.Cursor);
            PointerTo(dlg, RowPoint(dlg, "Item6", 0.1));
            Assert.False(line.IsVisible);
            PointerUp(dlg, RowPoint(dlg, "Item6", 0.1));
            Assert.Equal(undo, d.Designer.Undo.UndoCount);
            Assert.Equal("Item1", Item(d, "FileMenu").Children[0].Name);
            Assert.Equal("Item2", Item(d, "FileMenu").Children[1].Name);

            // A new drag works as before; without a drag Escape is Close (pressed by the caller).
            BeginDrag(dlg, "Item7");
            PointerTo(dlg, RowPoint(dlg, "Item6", 0.1));
            Assert.Equal("before Item 6", AutomationProperties.GetName(line));
            PointerUp(dlg, RowPoint(dlg, "Item6", 0.1));
            Assert.Equal(undo + 1, d.Designer.Undo.UndoCount);
            Assert.Equal(["Item5", "Item7", "Item6"], Item(d, "FileMenu").Children.Skip(4).Take(3).Select(c => c.Name));
            return Task.CompletedTask;
        }, form, unit);
    }

    /// <summary>
    /// D-156 (DSG-SUR-257): <c>Ctrl+Shift+Left</c> moves the node out, <c>Ctrl+Shift+Right</c> moves it in; the focus stays on
    /// the moved node. The keys act in the tree only: a text field keeps their own meaning (word selection).
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-257")]
    public Task KeyboardMoveOutAndIn() => DesignAsync((_, d, dlg) =>
    {
        var undo = d.Designer.Undo.UndoCount;
        IReadOnlyList<string> FileItems() => Nodes(dlg, dlg.Find<TreeViewItem>("Node.FileMenu"));

        // Move out: Exit becomes the sibling directly after File.
        ClickNode(dlg, "FileExit");
        dlg.Press("Ctrl+Shift+Left");
        Assert.Equal(["FileMenu", "FileExit", "HelpMenu"], Nodes(dlg));
        Assert.Equal(["FileOpen", "N1"], FileItems());
        Assert.Equal("Dialog.MenuDesigner.Node.FileExit", dlg.FocusedAutomationId);
        Assert.Equal((undo + 1, FreexComp.Designer.Surface.UndoTitles.Order), (d.Designer.Undo.UndoCount, d.Designer.Undo.UndoTitleKey));
        dlg.Press("Ctrl+Shift+Left"); // at the top level: nothing
        Assert.Equal(undo + 1, d.Designer.Undo.UndoCount);

        // Move in: the last sub-item of the sibling before it; not into a separator.
        dlg.Press("Ctrl+Shift+Right");
        Assert.Equal(["FileMenu", "HelpMenu"], Nodes(dlg));
        Assert.Equal(["FileOpen", "N1", "FileExit"], FileItems());
        Assert.Equal("Dialog.MenuDesigner.Node.FileExit", dlg.FocusedAutomationId);
        Assert.Equal(undo + 2, d.Designer.Undo.UndoCount);
        dlg.Press("Ctrl+Shift+Right"); // the sibling before it is a separator: nothing
        Assert.Equal(undo + 2, d.Designer.Undo.UndoCount);
        Assert.Equal(["FileOpen", "N1", "FileExit"], FileItems());

        // With Ctrl+Up they reach every place: Exit above the separator, then into Open….
        dlg.Press("Ctrl+Up");
        dlg.Press("Ctrl+Shift+Right");
        Assert.Equal(["FileOpen", "N1"], FileItems());
        Assert.Equal(["FileExit"], Nodes(dlg, dlg.Find<TreeViewItem>("Node.FileOpen")));
        Assert.True(dlg.Find<TreeViewItem>("Node.FileOpen").IsExpanded);
        Assert.Equal("Dialog.MenuDesigner.Node.FileExit", dlg.FocusedAutomationId);
        Assert.Equal(undo + 4, d.Designer.Undo.UndoCount);

        // In the Caption field the two keys keep their own meaning: they select by word and move nothing.
        ClickNode(dlg, "HelpAbout");
        dlg.Press("F2");
        var caption = dlg.Find<TextBox>("Caption");
        Assert.Equal("Dialog.MenuDesigner.Caption", dlg.FocusedAutomationId);
        caption.SelectionStart = caption.SelectionEnd = caption.CaretIndex = caption.Text!.Length;
        dlg.Press("Ctrl+Shift+Left");
        Assert.Equal("FreexComp", caption.SelectedText);
        dlg.Press("Ctrl+Shift+Right");
        Assert.Equal("", caption.SelectedText);
        Assert.Equal("Dialog.MenuDesigner.Caption", dlg.FocusedAutomationId);
        Assert.Equal(undo + 4, d.Designer.Undo.UndoCount);
        Assert.Equal(["HelpAbout"], Nodes(dlg, dlg.Find<TreeViewItem>("Node.HelpMenu")));
        return Task.CompletedTask;
    }, MenuForm, MenuUnit);

    /// <summary>An empty main menu and an image list of four 16 × 16 cells (<c>TestData/imagelists/alpha4.bin</c>), not assigned yet.</summary>
    private static string ImageListForm()
    {
        var bytes = File.ReadAllBytes(Path.Combine(FreexComp.Ide.Tests.EndToEnd.LayoutRoundTrip.RepoRoot, "ide", "tests", "TestData", "imagelists", "alpha4.bin"));
        var hex = string.Concat(bytes.Chunk(32).Select(line => "      " + Convert.ToHexString(line) + "\n"));
        return Form.ReplaceLineEndings("\n").Replace("    DesignTop = 120\n  end\n",
            "    DesignTop = 120\n  end\n  object ImageList1: TImageList\n    DesignLeft = 360\n    DesignTop = 120\n    Bitmap = {\n" + hex + "    }\n  end\n",
            StringComparison.Ordinal);
    }

    private static readonly string ImageListUnit = Unit.Replace("Classes, Controls, Forms, Menus;", "Classes, Controls, Forms, Menus, ImgList;", StringComparison.Ordinal)
        .Replace("    MainMenu1: TMainMenu;", "    MainMenu1: TMainMenu;\n    ImageList1: TImageList;", StringComparison.Ordinal);

    /// <summary>The component metadata of the UI framework instead of the test metadata (image lists, <c>Images</c>, <c>ImageIndex</c>).</summary>
    private static void FrameworkMetadata(HarnessOptions o)
    {
        o.ComponentMetadata = false;
        o.Settings["toolchain.frameworkDesignDir"] = FreexComp.Ide.Tests.EndToEnd.LayoutRoundTrip.FrameworkDesignDir;
    }

    /// <summary>Types over the selected text of the focused Caption field and commits with Enter.</summary>
    private static void TypeCaption(DialogDriver dlg, string caption)
    {
        Assert.Equal("Dialog.MenuDesigner.Caption", dlg.FocusedAutomationId);
        dlg.Window.KeyTextInput(caption);
        Dispatcher.UIThread.RunJobs();
        dlg.Press("Enter");
    }

    /// <summary>
    /// D-157 (DSG-SUR-258, 259; UI-DLG-342, 343): a menu is typed as Insert, text, Enter — Enter in Caption returns the focus
    /// to the node; "-" makes a separator; the Image field lists the pictures of the menu's image list and stores ImageIndex.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-258")]
    [Trait("Req", "DSG-SUR-259")]
    [Trait("Req", "UI-DLG-342")]
    [Trait("Req", "UI-DLG-343")]
    public Task CardKeysAndImageIndex() => DesignAsync((_, d, dlg) =>
    {
        var image = dlg.Find<ComboBox>("Image");
        var shortCut = dlg.Find<ComboBox>("ShortCut");
        var surface = d.Designer.Surface!;
        IReadOnlyList<MenuImageChoice> Entries() => [.. image.Items.Cast<MenuImageChoice>()];
        string? Stored(string item, string property) => Item(d, item).FindProperty(property)?.Value switch
        {
            IntegerValue i => i.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            StringValue s => s.Value,
            IdentValue id => id.Name,
            _ => null,
        };

        // The empty menu: Image is disabled; without an Images value its tooltip says so, also while disabled.
        Assert.False(image.IsEnabled);
        Assert.Equal("Assign an image list to the Images property of MainMenu1 to choose images here.", ToolTip.GetTip(image));
        Assert.True(ToolTip.GetShowOnDisabled(image));

        // Insert, text, Enter: Enter commits and the focus is on the node of the item, so the next key acts at once.
        dlg.Press("Insert");
        TypeCaption(dlg, "&Open…");
        Assert.Equal("Dialog.MenuDesigner.Node.Open", dlg.FocusedAutomationId);
        Assert.Equal("&Open…", CaptionOf(dlg, "Open").Text); // the & is stored as typed and shown in the node
        Assert.Equal("&Open…", Stored("Open", "Caption"));

        // "-" makes a separator: the node is a rule, the name N1, ShortCut and Image are disabled.
        dlg.Press("Insert");
        TypeCaption(dlg, "-");
        Assert.Equal(["Open", "N1"], Nodes(dlg));
        Assert.Equal("Dialog.MenuDesigner.Node.N1", dlg.FocusedAutomationId);
        Assert.IsType<Border>(dlg.Find<TreeViewItem>("Node.N1").Header);
        Assert.False(shortCut.IsEnabled || image.IsEnabled);
        Assert.Null(ToolTip.GetTip(image)); // a separator has no image: no hint to assign a list

        dlg.Press("Insert");
        TypeCaption(dlg, "E&xit");
        Assert.Equal(["Open", "N1", "Exit1"], Nodes(dlg));
        Assert.Equal("Dialog.MenuDesigner.Node.Exit1", dlg.FocusedAutomationId);

        // The arrows and Enter act on the node at once: up to the separator and back, Enter makes the OnClick handler.
        dlg.Press("Up");
        Assert.Equal("Dialog.MenuDesigner.Node.N1", dlg.FocusedAutomationId);
        dlg.Press("Down");
        dlg.Press("Enter");
        Assert.False(dlg.Window.IsClosed());
        Assert.Equal("Exit1Click", Stored("Exit1", "OnClick"));

        // Ctrl+Right after Enter: a sub-item at once.
        dlg.Press("Ctrl+Right");
        TypeCaption(dlg, "&Now");
        Assert.Equal(["Now"], Nodes(dlg, dlg.Find<TreeViewItem>("Node.Exit1")));
        Assert.Equal("Dialog.MenuDesigner.Node.Now", dlg.FocusedAutomationId);

        // Another text on a separator makes it an ordinary item again (F2 is the key from a node to Caption).
        dlg.Press("Up");
        dlg.Press("Up");
        Assert.Equal("Dialog.MenuDesigner.Node.N1", dlg.FocusedAutomationId);
        dlg.Press("F2");
        TypeCaption(dlg, "&Save");
        Assert.Equal(["Open", "Save", "Exit1"], Nodes(dlg));
        Assert.IsType<DockPanel>(dlg.Find<TreeViewItem>("Node.Save").Header);
        Assert.True(shortCut.IsEnabled);
        Assert.Equal("Dialog.MenuDesigner.Node.Save", dlg.FocusedAutomationId);

        // Focus loss keeps committing without moving the focus to the node: Tab goes on to ShortCut.
        dlg.Press("F2");
        dlg.Window.KeyTextInput("Save &As…");
        Dispatcher.UIThread.RunJobs();
        dlg.Press("Tab");
        Assert.Equal("Save &As…", Stored("Save", "Caption"));
        Assert.Equal("Dialog.MenuDesigner.ShortCut", dlg.FocusedAutomationId);

        // The image list assigned to MainMenu1.Images: Image is enabled; Tab from ShortCut reaches it.
        Assert.True(surface.Commit(FreexComp.Designer.Surface.UndoTitles.Property,
            [new FreexComp.Designer.Editing.SetProperty("MainMenu1", "Images", new IdentValue("ImageList1"))]));
        Dispatcher.UIThread.RunJobs();
        Assert.True(image.IsEnabled);
        Assert.Null(ToolTip.GetTip(image));
        Assert.Equal("Dialog.MenuDesigner.ShortCut", dlg.FocusedAutomationId);
        dlg.Press("Tab");
        Assert.Equal("Dialog.MenuDesigner.Image", dlg.FocusedAutomationId);

        // The entries: (None), then 0 … 3 with the cell's picture in a 16 × 16 box, space.3, the number.
        Assert.Equal([-1, 0, 1, 2, 3], Entries().Select(e => e.Index));
        image.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        var containers = Entries().Select(e => Assert.IsType<ComboBoxItem>(image.ContainerFromItem(e))).ToList();
        Assert.Equal(["Image.Item.None", "Image.Item.0", "Image.Item.1", "Image.Item.2", "Image.Item.3"],
            containers.Select(c => AutomationIds.Get(c)!["Dialog.MenuDesigner.".Length..]));
        Assert.Equal(["(None)", "Image 0", "Image 1", "Image 2", "Image 3"], containers.Select(AutomationProperties.GetName));
        Assert.Empty(containers[0].GetVisualDescendants().OfType<Image>()); // (None) has no picture
        Assert.Equal("(None)", containers[0].GetVisualDescendants().OfType<TextBlock>().Single().Text);
        for (var i = 1; i < containers.Count; i++)
        {
            var picture = containers[i].GetVisualDescendants().OfType<Image>().Single();
            var box = (Control)picture.Parent!;
            var number = containers[i].GetVisualDescendants().OfType<TextBlock>().Single();
            Assert.Equal((16.0, 16.0), (box.Bounds.Width, box.Bounds.Height));
            Assert.Equal(new Size(16, 16), picture.Bounds.Size);
            Assert.Equal(new PixelSize(16, 16), Assert.IsAssignableFrom<Avalonia.Media.Imaging.Bitmap>(picture.Source).PixelSize);
            Assert.Equal((i - 1).ToString(System.Globalization.CultureInfo.InvariantCulture), number.Text);
            Assert.Equal(box.Bounds.Right + 6, number.Bounds.Left); // space.3
        }

        // Choosing 2 stores ImageIndex = 2 as one "Property" unit; the closed field shows the entry the same way.
        var undo = d.Designer.Undo.UndoCount;
        image.SelectedItem = Entries()[3];
        image.IsDropDownOpen = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("2", Stored("Save", "ImageIndex"));
        Assert.Equal((undo + 1, FreexComp.Designer.Surface.UndoTitles.Property), (d.Designer.Undo.UndoCount, d.Designer.Undo.UndoTitleKey));
        Assert.Equal(2, ((MenuImageChoice)image.SelectedItem!).Index);
        Assert.Single(image.GetVisualDescendants().OfType<Image>());
        Assert.Equal("2", image.GetVisualDescendants().OfType<TextBlock>().Single(t => t.IsEffectivelyVisible).Text);

        // (None) removes the property.
        image.SelectedItem = Entries()[0];
        Dispatcher.UIThread.RunJobs();
        Assert.Null(Stored("Save", "ImageIndex"));
        Assert.Equal(undo + 2, d.Designer.Undo.UndoCount);
        Assert.Empty(image.GetVisualDescendants().OfType<Image>());

        // A stored index outside the list is one more entry with the number alone, selected.
        Assert.True(surface.Commit(FreexComp.Designer.Surface.UndoTitles.Property,
            [new FreexComp.Designer.Editing.SetProperty("Save", "ImageIndex", new IntegerValue(9, false))]));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([-1, 0, 1, 2, 3, 9], Entries().Select(e => e.Index));
        Assert.Equal(9, ((MenuImageChoice)image.SelectedItem!).Index);
        Assert.Empty(image.GetVisualDescendants().OfType<Image>());
        Assert.Equal("9", image.GetVisualDescendants().OfType<TextBlock>().Single(t => t.IsEffectivelyVisible).Text);

        // Images that cannot be read here: disabled with the reason; the stored number is still shown.
        Assert.True(surface.Commit(FreexComp.Designer.Surface.UndoTitles.Property,
            [new FreexComp.Designer.Editing.SetProperty("MainMenu1", "Images", new IdentValue("Missing1"))]));
        Dispatcher.UIThread.RunJobs();
        Assert.False(image.IsEnabled);
        Assert.Equal("The images of Missing1 cannot be shown here. Set ImageIndex in the property grid.", ToolTip.GetTip(image));
        Assert.Equal(9, ((MenuImageChoice)image.SelectedItem!).Index);
        return Task.CompletedTask;
    }, ImageListForm(), ImageListUnit, FrameworkMetadata);
}
