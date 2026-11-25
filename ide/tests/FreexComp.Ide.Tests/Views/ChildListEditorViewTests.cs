using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Designer.Model;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.Views;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// The window of the Child List Editor with categories and the class picker (phase8-ui-additions.md §8,
/// UI-DLG-306…309; action-list-editor.md DSG-SUR-216…222; task D-117).
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class ChildListEditorViewTests
{
    private const string Prefix = "Dialog.ChildListEditor.";

    private static Color ColorOf(DialogDriver dlg, string key)
    {
        Assert.True(dlg.Window.TryFindResource(key, dlg.Window.ActualThemeVariant, out var brush), key);
        return Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
    }

    private static Color ColorOf(TextBlock text) => Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color;

    /// <summary>
    /// UI-DLG-306…309, DSG-SUR-218, 222 in the light and the dark theme. New is a split button whose button part creates
    /// the first class and whose arrow part, Alt+Down, opens the picker: a menu with <c>TAction</c>, the section headers
    /// <c>Edit</c> and <c>File</c> (not selectable, <c>type.caption</c>, <c>text.secondary</c>) and the class rows (display
    /// name, class name in <c>text.secondary</c>); type-ahead, Enter creates, Escape closes the picker only. The list is
    /// flat without a category and grouped with one: header rows with their AutomationIds, as high as a child row, with no
    /// focus and no selection — the arrow keys and the extended selection skip them; child rows indented, their
    /// screen-reader names prefixed with the category. Every interactive control has its AutomationId.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-306")]
    [Trait("Req", "UI-DLG-307")]
    [Trait("Req", "UI-DLG-308")]
    [Trait("Req", "UI-DLG-309")]
    [Trait("Req", "DSG-SUR-218")]
    [Trait("Req", "DSG-SUR-222")]
    public async Task GroupHeadersAndPickerRender()
    {
        var secondaryColors = new List<Color>();
        foreach (var theme in (string[])["light", "dark"])
        {
            await using var h = await HeadlessHarness.StartAsync(o =>
            {
                o.Settings["appearance.theme"] = theme;
                DesignerViewTests.EditorFiles(o);
            });
            var document = await DesignerViewTests.OpenFormAsync(h);
            h.Click(AutomationIds.DocumentViewTab("MainForm.pas", "Design"));
            Assert.Equal(DocumentView.Design, document.ActiveView);
            var model = document.Designer.Model!;
            document.Designer.Surface!.Selection.Select(model.FindByName("ActionList1")!);
            Dispatcher.UIThread.RunJobs();
            string[] Children() => [.. model.FindByName("ActionList1")!.Children.Select(c => c.Name)];

            var running = h.Services.Commands.ExecuteAsync("designer.editComponent");
            var dlg = await h.WaitForDialogAsync("ChildListEditor");
            try
            {
                var list = dlg.Find<ListBox>("Items");
                var editor = Assert.IsType<FreexComp.Ide.Core.Dialogs.ChildListEditorViewModel>(dlg.Window.Model);
                List<ListBoxItem> Rows() => [.. list.Items.OfType<ListBoxItem>()];
                string[] RowIds() => [.. Rows().Select(r => AutomationIds.Get(r) ?? "")];
                var secondary = ColorOf(dlg, "FxcTextSecondaryBrush");
                secondaryColors.Add(secondary);
                Assert.NotEqual(ColorOf(dlg, "FxcTextPrimaryBrush"), secondary);

                // UI-DLG-307: New is a split button; its menu is the picker — TAction, then the sections.
                var split = dlg.Find<SplitButton>("New");
                Assert.Equal("New", AutomationProperties.GetName(split));

                // The labels show their mnemonic as access text: no ampersand is drawn.
                Assert.Equal(("_New", "_Delete", "Move _Up", "Move Do_wn"),
                    (split.Content, dlg.Find<Button>("Delete").Content, dlg.Find<Button>("MoveUp").Content, dlg.Find<Button>("MoveDown").Content));
                Assert.Equal("_Items:", Assert.Single(dlg.Window.GetVisualDescendants().OfType<Label>(), l => ReferenceEquals(l.Target, list)).Content);
                Assert.DoesNotContain(dlg.Window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text?.Contains('&') == true);
                var picker = Assert.IsType<MenuFlyout>(split.Flyout);
                var entries = picker.Items.OfType<MenuItem>().ToList();
                Assert.Equal(
                [
                    Prefix + "New.TAction",
                    Prefix + "New.Group.Edit",
                    Prefix + "New.TEditCut", Prefix + "New.TEditCopy", Prefix + "New.TEditPaste", Prefix + "New.TEditSelectAll", Prefix + "New.TEditUndo", Prefix + "New.TEditDelete",
                    Prefix + "New.Group.File",
                    Prefix + "New.TFileOpen", Prefix + "New.TFileSaveAs", Prefix + "New.TFileExit",
                ], entries.Select(AutomationIds.Get));
                Assert.True(entries.Count <= ChildListEditorWindow.PickerVisibleRows);
                MenuItem Entry(string id) => entries.Single(i => AutomationIds.Get(i) == Prefix + "New." + id);

                // Section headers: caption type, neither selectable nor focusable.
                foreach (var header in (MenuItem[])[Entry("Group.Edit"), Entry("Group.File")])
                {
                    Assert.False(header.IsEnabled || header.Focusable || header.IsHitTestVisible);
                    Assert.Contains("caption", Assert.IsType<TextBlock>(header.Header).Classes);
                }

                Assert.Equal("Edit", Assert.IsType<TextBlock>(Entry("Group.Edit").Header).Text);

                // Class rows: the display name, then the class name in text.secondary after a gap of space.4; rows of
                // a section are indented.
                var copyRow = Assert.IsType<StackPanel>(Entry("TEditCopy").Header).Children.OfType<TextBlock>().ToList();
                Assert.Equal(["EditCopy", "TEditCopy"], copyRow.Select(t => t.Text));
                Assert.Equal(new Thickness(8, 0, 0, 0), copyRow[1].Margin);
                Assert.Equal(new Thickness(8, 0, 0, 0), copyRow[0].Padding);
                var actionRow = Assert.IsType<StackPanel>(Entry("TAction").Header).Children.OfType<TextBlock>().ToList();
                Assert.Equal(["Action", "TAction"], actionRow.Select(t => t.Text));
                Assert.Equal(new Thickness(0), actionRow[0].Padding);
                Assert.Equal("EditCopy TEditCopy", AutomationProperties.GetName(Entry("TEditCopy")));

                // UI-DLG-308: the picker is a popup of its own under the button, moved above it when there is no room.
                Assert.Equal(PlacementMode.BottomEdgeAlignedLeft, picker.Placement);
                Assert.True(picker.PlacementConstraintAdjustment.HasFlag(PopupPositionerConstraintAdjustment.FlipY));
                Assert.All((string[])["New", "Delete", "MoveUp", "MoveDown"], id => Assert.Null(VisualSnapshot.HiddenPart(dlg.Find<Control>(id), dlg.Window)));
                Assert.Equal((dlg.Window.MinWidth, dlg.Window.MinHeight), (dlg.Window.Width, dlg.Window.Height)); // shown at its minimum size

                // The button part creates the first class; without a category the list is flat, as before.
                dlg.Click("New");
                Assert.Equal(["Action1"], Children());
                Assert.False(picker.IsOpen);
                Assert.Equal([Prefix + "Item.Action1"], RowIds());
                Assert.Equal("Action1", Rows()[0].Content);
                Assert.Equal("Action1", AutomationProperties.GetName(Rows()[0]));

                // Alt+Down on the focused button opens the picker with the first class highlighted; a typed letter
                // moves to the next class whose display name starts with it; Enter creates it.
                dlg.Window.MouseMove(new Point(dlg.Window.Width - 1, 1)); // the pointer rests away from the menu
                split.Focus(NavigationMethod.Tab);
                dlg.Press("Alt+Down");
                Assert.True(picker.IsOpen);
                Assert.True(Entry("TAction").IsSelected, string.Join(",", entries.Where(e => e.IsSelected).Select(AutomationIds.Get)));
                Assert.Equal(secondary, ColorOf(Assert.IsType<TextBlock>(Entry("Group.Edit").Header))); // text.secondary, in the open menu
                Assert.Equal(secondary, ColorOf(Assert.IsType<TextBlock>(Entry("Group.File").Header)));
                Assert.Equal(secondary, ColorOf(copyRow[1]));
                Assert.All(entries, e => Assert.True(e.Bounds.Height > 0, AutomationIds.Get(e))); // twelve rows, none scrolled away
                TypeAhead(entries, "f");
                Assert.True(Entry("TFileOpen").IsSelected);
                TypeAhead(entries, "F");
                Assert.True(Entry("TFileSaveAs").IsSelected);
                TypeAhead(entries, "e");
                Assert.True(Entry("TEditCut").IsSelected);
                TypeAhead(entries, "x"); // no class starts with it
                Assert.True(Entry("TEditCut").IsSelected);
                TypeAhead(entries, "f");
                TypeAhead(entries, "f");
                TypeAhead(entries, "f");
                Assert.True(Entry("TFileExit").IsSelected);
                TypeAhead(entries, "f");
                Assert.True(Entry("TFileOpen").IsSelected); // wrapped around
                dlg.Press("Down");
                Assert.True(Entry("TFileSaveAs").IsSelected);
                dlg.Press("Up");
                dlg.Press("Up"); // the header File is skipped
                Assert.True(Entry("TEditDelete").IsSelected);
                dlg.Press("Down");
                Assert.True(Entry("TFileOpen").IsSelected);
                dlg.Press("Enter");
                Assert.False(picker.IsOpen);
                Assert.False(dlg.Window.IsClosed());
                Assert.Equal(["Action1", "FileOpen1"], Children());
                Assert.Equal("Dialog.ChildListEditor.Grid", dlg.FocusedAutomationId);

                // Escape closes the picker only: the dialog stays, nothing is created.
                split.Focus(NavigationMethod.Tab);
                dlg.Press("Alt+Down");
                Assert.True(picker.IsOpen);
                dlg.Press("Escape");
                Assert.False(picker.IsOpen);
                Assert.False(dlg.Window.IsClosed());
                Assert.Equal(["Action1", "FileOpen1"], Children());

                // Enter on the focused button is its button part (not the dialog's default button, Close).
                split.Focus(NavigationMethod.Tab);
                dlg.Press("Enter");
                Assert.False(dlg.Window.IsClosed());
                Assert.Equal(["Action1", "FileOpen1", "Action2"], Children());
                editor.Delete();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(["Action1", "FileOpen1"], Children());

                // A class of the section Edit: three groups now, in order of their first child, (No Category) first.
                Entry("TEditCopy").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(["Action1", "FileOpen1", "EditCopy1"], Children());
                Assert.Equal(
                [
                    Prefix + "Group.(none)", Prefix + "Item.Action1",
                    Prefix + "Group.File", Prefix + "Item.FileOpen1",
                    Prefix + "Group.Edit", Prefix + "Item.EditCopy1",
                ], RowIds());
                Assert.Equal(["(No Category)", "(No Category), Action1", "File", "File, FileOpen1 – Open…", "Edit", "Edit, EditCopy1 – Copy"],
                    Rows().Select(r => AutomationProperties.GetName(r)));
                Assert.Equal([Prefix + "Item.EditCopy1"], list.SelectedItems!.OfType<ListBoxItem>().Select(AutomationIds.Get)); // the new child

                // UI-DLG-306: header rows — caption type, text.secondary, left padding space.2, as high as a child row,
                // no hit test (no hover, no click selection), no focus; child rows indented by space.4.
                var headers = Rows().Where(r => r.Tag is not FormObject).ToList();
                var children = Rows().Where(r => r.Tag is FormObject).ToList();
                Assert.Equal(3, headers.Count);
                foreach (var header in headers)
                {
                    Assert.False(header.Focusable || header.IsHitTestVisible);
                    var caption = header.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("caption"));
                    Assert.Equal(secondary, ColorOf(caption));
                    Assert.Equal(4, header.Padding.Left);
                    Assert.Equal(children[0].Bounds.Height, header.Bounds.Height);
                    Assert.True(header.Bounds.Height > 0);
                }

                Assert.Equal("(No Category)", headers[0].GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("caption")).Text);
                Assert.All(children, c => Assert.Equal(new Thickness(8, 0, 0, 0), Assert.IsType<TextBlock>(c.Content).Margin));

                // DSG-SUR-218: the arrow keys skip the header rows.
                children[0].Focus(NavigationMethod.Tab);
                Dispatcher.UIThread.RunJobs();
                dlg.Press("Down");
                Assert.Equal(Prefix + "Item.FileOpen1", dlg.FocusedAutomationId);
                dlg.Press("Down");
                Assert.Equal(Prefix + "Item.EditCopy1", dlg.FocusedAutomationId);
                dlg.Press("Down");
                Assert.Equal(Prefix + "Item.EditCopy1", dlg.FocusedAutomationId); // the last child
                dlg.Press("Up");
                Assert.Equal(Prefix + "Item.FileOpen1", dlg.FocusedAutomationId);
                Assert.Equal([Prefix + "Item.FileOpen1"], list.SelectedItems!.OfType<ListBoxItem>().Select(AutomationIds.Get));
                dlg.Press("Up");
                dlg.Press("Up");
                Assert.Equal(Prefix + "Item.Action1", dlg.FocusedAutomationId); // the first child: the header above it takes no focus

                // … and the extended selection does not count them: a range across headers and Select All select
                // the children; a header inside the range shows no selection fill.
                dlg.Press("Shift+Down");
                Assert.Equal(["Action1", "FileOpen1"], editor.Selection.Select(o => o.Name));
                dlg.Press("Shift+Down");
                Assert.Equal(["Action1", "FileOpen1", "EditCopy1"], editor.Selection.Select(o => o.Name));
                Assert.Equal(Prefix + "Item.EditCopy1", dlg.FocusedAutomationId);
                Assert.All(children, c => Assert.True(c.IsSelected));
                foreach (var header in headers)
                {
                    var fill = header.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().First(c => c.Name == "PART_ContentPresenter").Background;
                    Assert.True(fill is null or ISolidColorBrush { Color.A: 0 }, AutomationIds.Get(header) + ": " + fill);
                }

                var childFill = children[0].GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().First(c => c.Name == "PART_ContentPresenter").Background;
                Assert.True(childFill is ISolidColorBrush { Color.A: > 0 }, "a selected child row is filled");
                dlg.Press("Shift+Up");
                Assert.Equal(["Action1", "FileOpen1"], editor.Selection.Select(o => o.Name));
                list.SelectAll();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(["Action1", "FileOpen1", "EditCopy1"], editor.Selection.Select(o => o.Name));
                Assert.False(dlg.Find<Control>("MoveUp").IsEnabled); // several children selected
                Assert.True(dlg.Find<Control>("Delete").IsEnabled);

                // Move Up / Move Down stay inside the group: the only child of a group cannot move.
                children[1].Focus(NavigationMethod.Tab);
                list.SelectedItems!.Clear();
                list.SelectedItems.Add(children[1]);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(["FileOpen1"], editor.Selection.Select(o => o.Name));
                Assert.False(dlg.Find<Control>("MoveUp").IsEnabled || dlg.Find<Control>("MoveDown").IsEnabled);

                // Every interactive control has a well-formed AutomationId; none is used twice.
                Assert.Empty(AutomationIdChecks.ControlsWithoutId(dlg.Window));
                Assert.Empty(AutomationIdChecks.DuplicateIds(dlg.Window));
            }
            finally
            {
                dlg.Window.Close();
                Dispatcher.UIThread.RunJobs();
                await running;
            }
        }

        Assert.NotEqual(secondaryColors[0], secondaryColors[1]); // the tokens of both themes resolved
    }

    /// <summary>A character typed while the highlighted class row of the open picker has the keyboard.</summary>
    private static void TypeAhead(IReadOnlyList<MenuItem> entries, string text)
    {
        var current = entries.Single(e => e.IsSelected);
        current.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text });
        Dispatcher.UIThread.RunJobs();
    }
}
