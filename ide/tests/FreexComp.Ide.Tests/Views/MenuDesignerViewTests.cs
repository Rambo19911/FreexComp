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
        foreach (var field in (string[])["Tree", "Tree.Empty", "Caption", "ShortCut", "Grid", "Grid.Page.Properties", "Grid.Page.Events",
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
        Assert.Equal(["Copy"], Nodes(dlg, dlg.Find<TreeViewItem>("Node.Edit")));
        dlg.Type("ShortCut", "Ctrl+C");
        dlg.Press("Enter");
        Assert.Equal(16451, ((IntegerValue)Item(d, "Copy").FindProperty("ShortCut")!.Value).Value);
        Assert.Equal("Ctrl+C", ((TextBlock)((DockPanel)dlg.Find<TreeViewItem>("Node.Copy").Header!).Children[0]).Text);

        // New Separator (button): the separator is drawn as a rule; Ctrl+Up moves it up.
        ClickNode(dlg, "Copy");
        dlg.Click("NewSeparator");
        Assert.Equal(["Copy", "N1"], Nodes(dlg, dlg.Find<TreeViewItem>("Node.Edit")));
        Assert.IsType<Border>(dlg.Find<TreeViewItem>("Node.N1").Header);
        ClickNode(dlg, "N1");
        dlg.Press("Ctrl+Up");
        Assert.Equal(["N1", "Copy"], Nodes(dlg, dlg.Find<TreeViewItem>("Node.Edit")));
        Assert.Equal("Dialog.MenuDesigner.Node.N1", dlg.FocusedAutomationId);

        // Delete on an item without sub-items deletes at once; the next sibling is selected.
        dlg.Press("Delete");
        Assert.Equal(["Copy"], Nodes(dlg, dlg.Find<TreeViewItem>("Node.Edit")));
        Assert.Null(d.Designer.Model!.FindByName("N1"));

        // F2 focuses Caption with its text selected; Enter in the tree creates and assigns the OnClick handler.
        ClickNode(dlg, "Copy");
        dlg.Press("F2");
        var caption = dlg.Find<TextBox>("Caption");
        Assert.Equal("Dialog.MenuDesigner.Caption", dlg.FocusedAutomationId);
        Assert.Equal((0, "&Copy".Length), (caption.SelectionStart, caption.SelectionEnd));
        ClickNode(dlg, "Copy");
        dlg.Press("Enter");
        Assert.False(dlg.Window.IsClosed());
        Assert.Equal("CopyClick", ((IdentValue)Item(d, "Copy").FindProperty("OnClick")!.Value).Name);
        Assert.Contains("procedure TMainForm.CopyClick(Sender: TObject);", d.Buffer.Text, StringComparison.Ordinal);

        // The Events page tab shows the handler in the embedded grid.
        dlg.Click("Grid.Page.Events");
        Assert.Equal("CopyClick", dlg.Find<TextBlock>("Grid.Row.OnClick.Value").Text);

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
    /// Designer on <c>MainMenu1</c>, runs <paramref name="drive"/> in it and closes it with Escape.
    /// </summary>
    private static async Task DesignAsync(Func<HeadlessHarness, FormDocumentViewModel, DialogDriver, Task> drive, string? form = null, string? unit = null)
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.ToolWindows.Add(SampleToolViewModel.Definition("ToolPalette", ToolGroupId.RightLower, 5, "Cmd_view_toolPalette"));
            o.InitialFiles["MainForm.pas"] = (unit ?? Unit).ReplaceLineEndings("\r\n");
            o.InitialFiles["MainForm.form"] = (form ?? Form).ReplaceLineEndings("\r\n");
        });
        var d = await DesignerViewTests.OpenFormAsync(h);
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

    /// <summary>The caption text block of a node (a separator has none).</summary>
    private static TextBlock CaptionOf(DialogDriver dlg, string name) =>
        (TextBlock)((DockPanel)dlg.Find<TreeViewItem>("Node." + name).Header!).Children[1];

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
        foreach (var node in (string[])["Open", "Copy"])
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
        InsertItem(dlg, "Copy", "&O " + words);
        var third = Item(d, "MainMenu1").Children[2].Name;
        Assert.EndsWith(", duplicate access key", NameOf(dlg, third), StringComparison.Ordinal);
        Assert.Equal((TextTrimming.CharacterEllipsis, TextWrapping.NoWrap), (text.TextTrimming, text.TextWrapping));
        Assert.Contains(words, (string)ToolTip.GetTip(warning)!, StringComparison.Ordinal);
        Assert.Equal(stripHeight, strip.Bounds.Height);
        var right = warning.TranslatePoint(new Avalonia.Point(warning.Bounds.Width, 0), dlg.Window)!.Value.X;
        var closeLeft = close.TranslatePoint(new Avalonia.Point(0, 0), dlg.Window)!.Value.X;
        Assert.True(right <= closeLeft - 8, $"the line ends at {right}, Close starts at {closeLeft}");
        Assert.True(text.Bounds.Height <= 18, $"one line: {text.Bounds.Height}");

        // Corrected captions: the third leaves the warning, the second ends it.
        Retype(dlg, third, "&Paste");
        Assert.Equal(Expected, text.Text);
        Assert.Null(MarkOf(dlg, third)); // a name derived from a caption is kept (UIF-727): only generated names follow
        Retype(dlg, "Copy", "&Copy");
        Assert.False(warning.IsVisible);
        Assert.Equal(["Open", "Copy", third], Nodes(dlg));
        Assert.Equal(["Open", "Copy", "Paste"], Nodes(dlg).Select(node => NameOf(dlg, node)));
        Assert.All(Nodes(dlg), node =>
        {
            Assert.Null(MarkOf(dlg, node));
            Assert.Null(ToolTip.GetTip((Control)dlg.Find<TreeViewItem>("Node." + node).Header!));
        });
        Assert.Equal(stripHeight, strip.Bounds.Height);
        return Task.CompletedTask;
    });
}
