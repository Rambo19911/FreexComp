using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Designer.Inspector;
using FreexComp.Designer.Model;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.TestSupport;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Tests.Fixtures.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.Views;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// The Object Inspector tool window (object-inspector.md, ui/designer.md §11; D-081, D-082): keyboard editing, Boolean
/// and enum shortcuts, the page switch, inline errors, the Events page, the empty state and the strings editor.
/// </summary>
[Trait("Phase", "5")]
[Trait("Kind", "Ui")]
public sealed class ObjectInspectorViewTests
{
    private const string Grid = "Tool.ObjectInspector.Grid";

    /// <summary>The form files at Phase 5 with the real Object Inspector (and a sample Tool Palette, D-083).</summary>
    private static void Phase5(HarnessOptions o)
    {
        DesignerViewTests.Files(o);
        o.ToolWindows.Add(SampleToolViewModel.Definition("ToolPalette", ToolGroupId.RightLower, 5, "Cmd_view_toolPalette"));
    }

    private static string Row(string path) => "Tool.ObjectInspector.Row." + path;

    private static async Task<FormDocumentViewModel> OpenDesignAsync(HeadlessHarness h, string select)
    {
        var document = await DesignerViewTests.OpenFormAsync(h);
        h.Click(AutomationIds.DocumentViewTab("MainForm.pas", "Design"));
        Assert.Equal(DocumentView.Design, document.ActiveView);
        document.Designer.Surface!.Selection.Select(document.Designer.Model!.FindByName(select)!);
        Dispatcher.UIThread.RunJobs();
        return document;
    }

    /// <summary>The path of the current row (the row element with the class <c>current</c>).</summary>
    private static string? CurrentRow(HeadlessHarness h) =>
        h.FindAll("Tool.ObjectInspector.Row.").FirstOrDefault(c => c.Classes.Contains("current")) is { } row
            ? AutomationIds.Get(row)!["Tool.ObjectInspector.Row.".Length..]
            : null;

    /// <summary>Home, then Down until <paramref name="path"/> is the current row.</summary>
    private static void GoTo(HeadlessHarness h, string path)
    {
        h.Press("Home");
        for (var i = 0; i < 60 && CurrentRow(h) != path; i++)
        {
            h.Press("Down");
        }

        Assert.Equal(path, CurrentRow(h));
    }

    private static string ValueText(HeadlessHarness h, string path) => h.Find<TextBlock>(Row(path) + ".Value").Text ?? "";

    private static FormValue? Stored(FormDocumentViewModel d, string obj, string path) => d.Designer.Model!.FindByName(obj)!.FindProperty(path)?.Value;

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-030")]
    [Trait("Req", "DSG-OI-032")]
    [Trait("Req", "DSG-OI-033")]
    [Trait("Req", "UI-DSG-107")]
    public async Task EditCaptionWithKeyboard()
    {
        await using var h = await HeadlessHarness.StartAsync(Phase5);
        var d = await OpenDesignAsync(h, "GreetButton");

        h.Press("F4");
        Assert.Equal("ObjectInspector", h.Services.FocusTracker.Current.ToolWindowName);
        Assert.True(h.Find<Control>(Grid).IsFocused);
        Assert.Equal("GreetButton: TButton", h.Find<ComboBox>("Tool.ObjectInspector.InstanceSelector").SelectedItem as string);

        GoTo(h, "Caption");
        Assert.Equal("&Greet", ValueText(h, "Caption"));
        h.Press("Enter");
        var editor = h.Find<TextBox>(Row("Caption") + ".Editor");
        Assert.True(editor.IsFocused);
        Assert.Equal("&Greet", editor.Text);
        Assert.Equal(editor.Text!.Length, editor.SelectionEnd - editor.SelectionStart); // F2/Enter select all
        h.Type("Hello");
        h.Press("Enter");

        Assert.Equal(new StringValue("Hello"), Stored(d, "GreetButton", "Caption"));
        Assert.Equal(1, d.Designer.Undo.UndoCount);
        Assert.Null(h.TryFind<TextBox>(Row("Caption") + ".Editor"));
        Assert.Equal("Hello", ValueText(h, "Caption"));
        Assert.True(h.Find<Control>(Grid).IsFocused);
        Assert.Equal("Caption", CurrentRow(h));

        // Typing a character on a text row replaces the value; Escape cancels (nothing changes).
        h.Type("X");
        Assert.Equal("X", h.Find<TextBox>(Row("Caption") + ".Editor").Text);
        h.Press("Escape");
        Assert.Null(h.TryFind<TextBox>(Row("Caption") + ".Editor"));
        Assert.Equal(new StringValue("Hello"), Stored(d, "GreetButton", "Caption"));
        Assert.Equal(1, d.Designer.Undo.UndoCount);
        Assert.True(h.Find<Control>(Grid).IsFocused);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-032")]
    [Trait("Req", "UI-DSG-106")]
    public async Task BooleanToggleAndEnumCycle()
    {
        await using var h = await HeadlessHarness.StartAsync(Phase5);
        var d = await OpenDesignAsync(h, "NameEdit");
        h.Press("F4");

        // Space on a Boolean row toggles and commits.
        GoTo(h, "AutoSelect");
        Assert.Equal("True", ValueText(h, "AutoSelect"));
        h.Press("Space");
        Assert.Equal(new IdentValue("False"), Stored(d, "NameEdit", "AutoSelect"));
        Assert.Equal("False", ValueText(h, "AutoSelect"));
        h.Press("Space");
        Assert.Equal("True", ValueText(h, "AutoSelect"));
        Assert.Equal(2, d.Designer.Undo.UndoCount);

        // Double-click on an enum value cycles to the next member and commits; it wraps.
        Assert.Equal("ecNormal", ValueText(h, "CharCase"));
        GoTo(h, "CharCase"); // in view (the right upper group is small)
        h.DoubleClick(Row("CharCase") + ".Value");
        Assert.Equal(new IdentValue("ecUpperCase"), Stored(d, "NameEdit", "CharCase"));
        h.DoubleClick(Row("CharCase") + ".Value");
        Assert.Equal("ecLowerCase", ValueText(h, "CharCase"));
        h.DoubleClick(Row("CharCase") + ".Value");
        Assert.Equal("ecNormal", ValueText(h, "CharCase"));
        Assert.Equal(5, d.Designer.Undo.UndoCount);
        Assert.Equal("CharCase", CurrentRow(h));

        // Double-click on a Boolean value toggles it.
        h.DoubleClick(Row("AutoSelect") + ".Value");
        Assert.Equal("False", ValueText(h, "AutoSelect"));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-003")]
    [Trait("Req", "UI-TW-300")]
    [Trait("Req", "UI-TW-302")]
    public async Task CtrlTabSwitchesPages()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            Phase5(o);
            o.InitialFiles["Other.pas"] = "unit Other;\r\n\r\ninterface\r\n\r\nimplementation\r\n\r\nend.\r\n";
        });
        await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, "Other.pas"));
        var d = await OpenDesignAsync(h, "GreetButton");
        h.Press("F4");
        Assert.Contains("active", h.Find<Control>("Tool.ObjectInspector.Tab.Properties").Classes);

        // Inside the inspector Ctrl+Tab is objectInspector.togglePage, not the document switch.
        h.Press("Ctrl+Tab");
        Assert.Same(d, h.Services.Documents.ActiveDocument);
        Assert.Contains("active", h.Find<Control>("Tool.ObjectInspector.Tab.Events").Classes);
        Assert.DoesNotContain("active", h.Find<Control>("Tool.ObjectInspector.Tab.Properties").Classes);
        Assert.Equal("GreetButtonClick", ValueText(h, "OnClick"));
        Assert.Null(h.TryFind<Control>(Row("Caption")));
        Assert.True(h.Find<Control>(Grid).IsFocused);

        h.Press("Ctrl+Tab");
        Assert.Same(d, h.Services.Documents.ActiveDocument);
        Assert.Contains("active", h.Find<Control>("Tool.ObjectInspector.Tab.Properties").Classes);
        Assert.Equal("&Greet", ValueText(h, "Caption"));

        // The tabs and the commands select a page explicitly.
        h.Click("Tool.ObjectInspector.Tab.Events");
        Assert.Contains("active", h.Find<Control>("Tool.ObjectInspector.Tab.Events").Classes);
        await h.ExecuteAsync("objectInspector.showProperties");
        Assert.Contains("active", h.Find<Control>("Tool.ObjectInspector.Tab.Properties").Classes);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DSG-109")]
    [Trait("Req", "DSG-OI-035")]
    public async Task InvalidValueShowsInlineError()
    {
        await using var h = await HeadlessHarness.StartAsync(Phase5);
        var d = await OpenDesignAsync(h, "GreetButton");
        h.Press("F4");
        GoTo(h, "Left");
        h.Press("Enter");
        h.Type("1+");
        h.Press("Enter");

        var error = h.Find<TextBlock>(Row("Left") + ".Error");
        Assert.True(error.IsEffectivelyVisible);
        Assert.Equal(Strings.Msg_Designer_ValueInvalidExpression, error.Text);
        var editor = h.Find<TextBox>(Row("Left") + ".Editor");
        Assert.True(editor.IsFocused);
        Assert.Contains("invalid", editor.Classes);
        Assert.Equal(new IntegerValue(232, false), Stored(d, "GreetButton", "Left"));
        Assert.Equal(0, d.Designer.Undo.UndoCount);

        // A valid entry commits and the error goes.
        editor.SelectAll();
        h.Type("200+8");
        h.Press("Enter");
        Assert.Null(h.TryFind<TextBlock>(Row("Left") + ".Error"));
        Assert.Equal("208", ValueText(h, "Left"));

        // Escape cancels a refused entry.
        h.Press("Enter");
        h.Type("x");
        h.Press("Enter");
        Assert.NotNull(h.TryFind<TextBlock>(Row("Left") + ".Error"));
        h.Press("Escape");
        Assert.Null(h.TryFind<TextBlock>(Row("Left") + ".Error"));
        Assert.Null(h.TryFind<TextBox>(Row("Left") + ".Editor"));
        Assert.Equal("208", ValueText(h, "Left"));
        Assert.Equal(1, d.Designer.Undo.UndoCount);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-035")]
    public async Task OneEscapeCancelsRefusedEntry()
    {
        await using var h = await HeadlessHarness.StartAsync(Phase5);
        var d = await OpenDesignAsync(h, "GreetButton");
        h.Press("F4");
        GoTo(h, "Left");
        h.Press("Enter");
        h.Type("x");
        h.Press("Enter");
        Assert.NotNull(h.TryFind<TextBlock>(Row("Left") + ".Error"));

        // One press: the editor and its error are gone, the value is unchanged, and the press did not also leave the
        // Object Inspector (an Escape on a row not being edited would move the focus to the design surface).
        h.Press("Escape");
        Assert.Null(h.TryFind<TextBox>(Row("Left") + ".Editor"));
        Assert.Null(h.TryFind<TextBlock>(Row("Left") + ".Error"));
        Assert.Equal("232", ValueText(h, "Left"));
        Assert.Equal("ObjectInspector", h.Services.FocusTracker.Current.ToolWindowName);
        Assert.Equal("Left", CurrentRow(h));
        Assert.Equal(["GreetButton"], d.Designer.Surface!.Selection.Items.Select(o => o.Name));
        Assert.Equal(0, d.Designer.Undo.UndoCount);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-083")]
    [Trait("Req", "UI-DSG-111")]
    public async Task EventsPageDoubleClickCreatesHandler()
    {
        await using var h = await HeadlessHarness.StartAsync(Phase5);
        var d = await OpenDesignAsync(h, "NameEdit");
        h.Press("F4");
        h.Click("Tool.ObjectInspector.Tab.Events");
        Assert.Equal("", ValueText(h, "OnChange"));

        h.DoubleClick(Row("OnChange") + ".Value");

        Assert.Equal(new IdentValue("NameEditChange"), Stored(d, "NameEdit", "OnChange"));
        Assert.Equal(DocumentView.Code, d.ActiveView);
        Assert.Contains("procedure TMainForm.NameEditChange(Sender: TObject);", d.Buffer.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// tool-windows.md §4 (what the shell fixes): UI-TW-301 the regions top to bottom and the description pane following
    /// <c>objectInspector.showDescription</c>; UI-TW-302 F4 / Alt+3 return to the row last focused, Escape on a row that is
    /// not being edited returns to the design surface; UI-TW-303 a filter without match; UI-TW-300 the rows follow the selection.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-300")]
    [Trait("Req", "UI-TW-301")]
    [Trait("Req", "UI-TW-302")]
    [Trait("Req", "UI-TW-303")]
    public async Task RegionsFocusAndFilterEmpty()
    {
        await using var h = await HeadlessHarness.StartAsync(Phase5);
        var d = await OpenDesignAsync(h, "GreetButton");
        h.Press("F4");

        // Regions: instance selector, then the page tabs with the filter box, then the grid, then the description pane.
        double Top(string id) => h.Find<Control>(id).TranslatePoint(default, h.Window)!.Value.Y;
        Assert.True(Top("Tool.ObjectInspector.InstanceSelector") < Top("Tool.ObjectInspector.Tab.Properties"));
        Assert.True(Top("Tool.ObjectInspector.Tab.Properties") < Top(Grid));
        Assert.True(Top("Tool.ObjectInspector.Filter") < Top(Grid));
        Assert.True(Top(Grid) < Top("Tool.ObjectInspector.Description"));
        Assert.True(h.Find<Control>("Tool.ObjectInspector.Description").IsEffectivelyVisible);
        h.Services.Settings.Set(SettingsCatalog.ObjectInspectorShowDescription, false);
        Dispatcher.UIThread.RunJobs();
        Assert.False(h.TryFind<Control>("Tool.ObjectInspector.Description")?.IsEffectivelyVisible == true);
        h.Services.Settings.Set(SettingsCatalog.ObjectInspectorShowDescription, true);
        Dispatcher.UIThread.RunJobs();

        // Escape on a row that is not being edited: the design surface gets the focus; F4 and Alt+3 come back to that row.
        GoTo(h, "Caption");
        h.Press("Escape");
        Assert.NotEqual("ObjectInspector", h.Services.FocusTracker.Current.ToolWindowName);
        Assert.False(h.Find<Control>(Grid).IsKeyboardFocusWithin);
        h.Press("F4");
        Assert.True(h.Find<Control>(Grid).IsFocused);
        Assert.Equal("Caption", CurrentRow(h));
        h.Press("Escape");
        h.Press("Alt+3");
        Assert.Equal("ObjectInspector", h.Services.FocusTracker.Current.ToolWindowName);
        Assert.Equal("Caption", CurrentRow(h));

        // The rows are the selection's: another component, other rows.
        d.Designer.Surface!.Selection.Select(d.Designer.Model!.FindByName("NameEdit")!);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("NameEdit: TEdit", h.Find<ComboBox>("Tool.ObjectInspector.InstanceSelector").SelectedItem as string);
        Assert.NotNull(h.TryFind<Control>(Row("Text")));

        // A filter without a match: Msg.Inspector.FilterEmpty.
        var filter = h.Find<TextBox>("Tool.ObjectInspector.Filter");
        filter.Text = "zzz";
        Dispatcher.UIThread.RunJobs(); // the text box raises TextChanged through the dispatcher
        h.Clock.Advance(Core.ViewModels.ToolWindowViewModel.FilterDelay);
        Dispatcher.UIThread.RunJobs();
        var empty = h.Find<TextBlock>("Tool.ObjectInspector.Empty");
        var dbg = (ObjectInspectorViewModel)h.Services.ToolWindows.Get("ObjectInspector")!;
        Assert.True(empty.IsEffectivelyVisible);
        Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Msg_Inspector_FilterEmpty, "zzz"), empty.Text);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-001")]
    [Trait("Req", "UI-DSG-112")]
    [Trait("Req", "UI-TW-303")]
    public async Task EmptyStateWithoutDesigner()
    {
        await using var h = await HeadlessHarness.StartAsync(Phase5);
        var d = await DesignerViewTests.OpenFormAsync(h);
        await h.ExecuteAsync("view.objectInspector");

        var empty = h.Find<TextBlock>("Tool.ObjectInspector.Empty");
        Assert.True(empty.IsEffectivelyVisible);
        Assert.Equal(Strings.Msg_Inspector_NoSelection, empty.Text);
        Assert.False(h.Find<ComboBox>("Tool.ObjectInspector.InstanceSelector").IsEnabled);
        Assert.False(h.Find<TextBox>("Tool.ObjectInspector.Filter").IsEnabled);
        Assert.Empty(h.FindAll("Tool.ObjectInspector.Row."));

        h.Click(AutomationIds.DocumentViewTab("MainForm.pas", "Design"));
        Assert.False(h.Find<TextBlock>("Tool.ObjectInspector.Empty").IsEffectivelyVisible);
        Assert.True(h.Find<ComboBox>("Tool.ObjectInspector.InstanceSelector").IsEnabled);
        Assert.Equal("MainForm: TMainForm", h.Find<ComboBox>("Tool.ObjectInspector.InstanceSelector").SelectedItem as string);
        Assert.NotEmpty(h.FindAll("Tool.ObjectInspector.Row."));

        // The instance selector lists the form first, then the others by name; choosing one selects it.
        var selector = h.Find<ComboBox>("Tool.ObjectInspector.InstanceSelector");
        Assert.Equal(["MainForm: TMainForm", "GreetButton: TButton", "NameEdit: TEdit"], selector.Items.Cast<string>());
        selector.SelectedIndex = 2;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["NameEdit"], d.Designer.Surface!.Selection.Items.Select(o => o.Name));

        h.Click(AutomationIds.DocumentViewTab("MainForm.pas", "Code"));
        Assert.True(h.Find<TextBlock>("Tool.ObjectInspector.Empty").IsEffectivelyVisible);
        Assert.Empty(h.FindAll("Tool.ObjectInspector.Row."));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-055")]
    public async Task StringsEditorDialogCommits()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            Phase5(o);
            o.InitialFiles["MainForm.pas"] = DesignerViewTests.Unit.Replace("GreetButton: TButton;", "GreetButton: TButton;\n    Memo1: TMemo;", StringComparison.Ordinal)
                .ReplaceLineEndings("\r\n");
            var form = DesignerViewTests.FormText.ReplaceLineEndings("\n");
            var at = form.TrimEnd().LastIndexOf("end", StringComparison.Ordinal);
            o.InitialFiles["MainForm.form"] = (form[..at] + "  object Memo1: TMemo\n    Left = 16\n    Top = 60\n    Width = 185\n    Height = 89\n    TabOrder = 2\n  end\n" + form[at..])
                .ReplaceLineEndings("\r\n");
        });
        var d = await OpenDesignAsync(h, "Memo1");
        h.Press("F4");
        GoTo(h, "Lines");
        Assert.Equal("(empty)", ValueText(h, "Lines"));

        // Enter opens the dialog; Ctrl+Enter is OK, Enter inserts a line break.
        h.Press("Enter");
        var dlg = await h.WaitForDialogAsync("StringsEditor");
        Assert.Equal("Edit Lines – Memo1", dlg.Window.Title);
        Assert.Equal("0 lines", dlg.Find<TextBlock>("LineCount").Text);
        dlg.Type("Text", "first");
        dlg.Press("Enter");
        dlg.Type("Text", "second\r\n");
        Assert.Equal("2 lines", dlg.Find<TextBlock>("LineCount").Text);
        dlg.Press("Ctrl+Enter");
        Dispatcher.UIThread.RunJobs();

        var lines = Assert.IsType<StringListValue>(Stored(d, "Memo1", "Lines"));
        Assert.Equal(["first", "second"], lines.Lines);
        Assert.Equal(1, d.Designer.Undo.UndoCount);
        Assert.Equal("(2 lines)", ValueText(h, "Lines"));

        // Unchanged text and Cancel change nothing.
        h.Find<Control>(Grid).Focus();
        h.Press("Enter");
        dlg = await h.WaitForDialogAsync("StringsEditor");
        Assert.Equal("first\r\nsecond", dlg.Find<TextBox>("Text").Text);
        Assert.Equal("2 lines", dlg.Find<TextBlock>("LineCount").Text);
        dlg.Click("Ok");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, d.Designer.Undo.UndoCount);

        h.Find<Control>(Grid).Focus();
        h.DoubleClick(Row("Lines") + ".Value");
        dlg = await h.WaitForDialogAsync("StringsEditor");
        dlg.Type("Text", "\r\nthird");
        Assert.Equal("3 lines", dlg.Find<TextBlock>("LineCount").Text);
        dlg.Click("Cancel");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["first", "second"], Assert.IsType<StringListValue>(Stored(d, "Memo1", "Lines")).Lines);
        Assert.Equal(1, d.Designer.Undo.UndoCount);

        // Emptied: the property is removed (an empty list is the default).
        h.Find<Control>(Grid).Focus();
        h.Press("Enter");
        dlg = await h.WaitForDialogAsync("StringsEditor");
        dlg.Find<TextBox>("Text").Text = "";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("0 lines", dlg.Find<TextBlock>("LineCount").Text);
        dlg.Click("Ok");
        Dispatcher.UIThread.RunJobs();
        Assert.Null(Stored(d, "Memo1", "Lines"));
        Assert.Equal(2, d.Designer.Undo.UndoCount);
    }
    private static FakeClipboard Clipboard(HeadlessHarness h) => (FakeClipboard)((DocumentService)h.Services.Documents).Clipboard!;

    private static ObjectInspectorViewModel Inspector(HeadlessHarness h) => (ObjectInspectorViewModel)h.Find<Control>("Tool.ObjectInspector").DataContext!;

    /// <summary>
    /// UI-DSG-106 (I-219): the 14 × 14 Boolean check box of a row is drawn whole — every part of it lies inside the box,
    /// and the box lies inside its value cell and row (the real IDE showed only a corner stub).
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DSG-106")]
    public async Task BooleanCheckFitsRow()
    {
        await using var h = await HeadlessHarness.StartAsync(Phase5);
        await OpenDesignAsync(h, "NameEdit");
        h.Press("F4");
        GoTo(h, "AutoSelect");
        var box = h.Find<CheckBox>(Row("AutoSelect") + ".Check");
        var row = h.Find<Control>(Row("AutoSelect"));
        Assert.Equal(new Size(14, 14), box.Bounds.Size);
        var boxRect = new Rect(box.TranslatePoint(default, row)!.Value, box.Bounds.Size);
        Assert.True(new Rect(row.Bounds.Size).Contains(boxRect), $"check box {boxRect} outside its row {row.Bounds.Size}");
        var parts = box.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0 && c.Bounds.Height > 0).ToList();
        Assert.NotEmpty(parts);
        foreach (var part in parts)
        {
            var r = new Rect(part.TranslatePoint(default, box)!.Value, part.Bounds.Size);
            Assert.True(r.X >= -0.5 && r.Y >= -0.5 && r.Right <= 14.5 && r.Bottom <= 14.5, $"{part.GetType().Name} '{part.Name}' {r} sticks out of the 14 × 14 box");
        }

        // The box shows the state: a visible mark when True.
        Assert.True(box.IsChecked);
    }

    private static string MenuId(object item) => item is Separator ? "-" : AutomationIds.Get((Control)item)!;

    private static MenuItem MenuItem(ContextMenu menu, string id) => menu.Items.OfType<MenuItem>().Single(i => AutomationIds.Get(i) == id);

    private static ContextMenu OpenGridMenu(HeadlessHarness h)
    {
        var menu = Assert.IsType<ContextMenu>(h.Find<Control>(Grid).ContextMenu);
        Assert.True(menu.IsOpen);
        return menu;
    }

    private static void CloseMenu(ContextMenu menu)
    {
        menu.Close();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// DSG-OI-037 (D-109): Ctrl+C (or Ctrl+Insert) on a row not in edit mode copies its value text; Ctrl+V (or Shift+Insert)
    /// on a text-editable row enters edit mode with the clipboard text, not yet committed. Other rows are not pasted into;
    /// on the Events page the handler name is copied.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-037")]
    public async Task RowCopyPaste()
    {
        await using var h = await HeadlessHarness.StartAsync(Phase5);
        var d = await OpenDesignAsync(h, "GreetButton");
        var clipboard = Clipboard(h);
        h.Press("F4");

        GoTo(h, "Caption");
        h.Press("Ctrl+C");
        Assert.Equal("&Greet", clipboard.Text);
        GoTo(h, "Width");
        h.Press("Ctrl+Insert");
        Assert.Equal("75", clipboard.Text);
        Assert.Null(h.TryFind<TextBox>(Row("Width") + ".Editor"));

        // Paste enters edit mode with the clipboard text; Enter commits it.
        clipboard.Text = "Hello";
        GoTo(h, "Caption");
        h.Press("Ctrl+V");
        var editor = h.Find<TextBox>(Row("Caption") + ".Editor");
        Assert.Equal("Hello", editor.Text);
        Assert.True(editor.IsFocused);
        Assert.Equal(new StringValue("&Greet"), Stored(d, "GreetButton", "Caption"));
        Assert.Equal(0, d.Designer.Undo.UndoCount);
        h.Press("Enter");
        Assert.Equal(new StringValue("Hello"), Stored(d, "GreetButton", "Caption"));
        Assert.Equal(1, d.Designer.Undo.UndoCount);

        // Shift+Insert pastes too; Escape cancels the pasted entry.
        clipboard.Text = "120";
        GoTo(h, "Width");
        h.Press("Shift+Insert");
        Assert.Equal("120", h.Find<TextBox>(Row("Width") + ".Editor").Text);
        h.Press("Escape");
        Assert.Null(h.TryFind<TextBox>(Row("Width") + ".Editor"));
        Assert.Equal(new IntegerValue(75, false), Stored(d, "GreetButton", "Width"));
        Assert.Equal(1, d.Designer.Undo.UndoCount);

        // An object row is not edited as text: nothing is pasted.
        GoTo(h, "Font");
        h.Press("Ctrl+V");
        Assert.Null(h.TryFind<TextBox>(Row("Font") + ".Editor"));
        Assert.Equal(1, d.Designer.Undo.UndoCount);

        // The Events page copies the handler name.
        h.Press("Ctrl+Tab");
        GoTo(h, "OnClick");
        h.Press("Ctrl+C");
        Assert.Equal("GreetButtonClick", clipboard.Text);
    }

    /// <summary>
    /// DSG-OI-039 (D-109): a right-click makes the row current and opens the row context menu in the spec order; its
    /// items run their commands (copy value and name, reset, arrangement, description); Shift+F10 opens it by keyboard.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-039")]
    public async Task RowContextMenu()
    {
        await using var h = await HeadlessHarness.StartAsync(Phase5);
        var d = await OpenDesignAsync(h, "GreetButton");
        var clipboard = Clipboard(h);
        h.Press("F4");
        GoTo(h, "Width");
        h.Press("Up");
        Assert.NotEqual("Width", CurrentRow(h));

        h.RightClick(Row("Width"));
        Assert.Equal("Width", CurrentRow(h));
        var menu = OpenGridMenu(h);
        Assert.Equal("Tool.ObjectInspector.ContextMenu", AutomationIds.Get(menu));
        Assert.Equal(
        [
            "Context.objectInspector.resetToDefault", "Context.edit.copy", "Context.objectInspector.copyPropertyName", "-",
            "Context.objectInspector.arrangeByCategory", "Context.objectInspector.arrangeByName", "Context.objectInspector.toggleDescription",
            "Context.objectInspector.expandAll", "Context.objectInspector.collapseAll",
        ], menu.Items.Cast<object>().Select(MenuId));
        Assert.Equal("Copy Value", MenuItem(menu, "Context.edit.copy").Header);
        Assert.False(MenuItem(menu, "Context.objectInspector.resetToDefault").IsEffectivelyEnabled); // geometry has no reset
        Assert.True(MenuItem(menu, "Context.objectInspector.arrangeByCategory").IsChecked);
        Assert.False(MenuItem(menu, "Context.objectInspector.arrangeByName").IsChecked);
        Assert.True(MenuItem(menu, "Context.objectInspector.toggleDescription").IsChecked);

        MenuItem(menu, "Context.edit.copy").Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("75", clipboard.Text);
        MenuItem(menu, "Context.objectInspector.copyPropertyName").Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Width", clipboard.Text);
        CloseMenu(menu);

        // On a stored Caption the reset is enabled and removes the value.
        GoTo(h, "Caption");
        h.Press("Down");
        h.RightClick(Row("Caption"));
        Assert.Equal("Caption", CurrentRow(h));
        menu = OpenGridMenu(h);
        var reset = MenuItem(menu, "Context.objectInspector.resetToDefault");
        Assert.True(reset.IsEffectivelyEnabled);
        reset.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(Stored(d, "GreetButton", "Caption"));
        Assert.Equal(1, d.Designer.Undo.UndoCount);
        CloseMenu(menu);

        // Arrange by Name (an action item) switches the arrangement and its toolbar toggle.
        GoTo(h, "Width");
        h.RightClick(Row("Width"));
        menu = OpenGridMenu(h);
        MenuItem(menu, "Context.objectInspector.arrangeByName").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("name", h.Services.Settings.Get(SettingsCatalog.ObjectInspectorArrangement));
        Assert.True(h.Find<ToggleButton>("Tool.ObjectInspector.ToolBar.objectInspector.toggleCategorized:name").IsChecked);
        CloseMenu(menu);

        // Show Description off hides the pane.
        h.RightClick(Row("Width"));
        menu = OpenGridMenu(h);
        MenuItem(menu, "Context.objectInspector.toggleDescription").Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(h.Find<Control>("Tool.ObjectInspector.Description").IsVisible);
        CloseMenu(menu);

        // Shift+F10 opens the menu for the current row.
        h.Find<Control>(Grid).Focus();
        h.Press("Shift+F10");
        Assert.Equal("Width", CurrentRow(h));
        CloseMenu(OpenGridMenu(h));
    }

    /// <summary>
    /// DSG-OI-032 (D-109): Alt+Down opens the row's value list (enum members, compatible handlers) at the current value;
    /// Up/Down move, Enter enters and commits, Escape closes without a change. A text row without a list enters edit mode.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-032")]
    public async Task AltDownOpensValueList()
    {
        await using var h = await HeadlessHarness.StartAsync(Phase5);
        var d = await OpenDesignAsync(h, "NameEdit");
        h.Press("F4");

        GoTo(h, "CharCase");
        h.Press("Alt+Down");
        var list = h.Find<ListBox>(Row("CharCase") + ".List");
        Assert.True(list.IsEffectivelyVisible);
        Assert.Equal(["ecNormal", "ecUpperCase", "ecLowerCase"], list.ItemsSource!.Cast<string>());
        Assert.Equal("ecNormal", list.SelectedItem);
        Assert.True(list.IsKeyboardFocusWithin, h.FocusedAutomationId + " / " + h.Window.FocusManager?.GetFocusedElement());
        h.Press("Down");
        h.Press("Enter");
        Assert.Equal(new IdentValue("ecUpperCase"), Stored(d, "NameEdit", "CharCase"));
        Assert.Equal(1, d.Designer.Undo.UndoCount);
        Assert.Null(h.TryFind<ListBox>(Row("CharCase") + ".List")); // closed
        Assert.True(h.Find<Control>(Grid).IsFocused);

        // Escape closes the list; nothing changes.
        h.Press("Alt+Down");
        Assert.Equal("ecUpperCase", list.SelectedItem);
        h.Press("Down");
        h.Press("Escape");
        Assert.Null(h.TryFind<ListBox>(Row("CharCase") + ".List")); // closed
        Assert.Equal(new IdentValue("ecUpperCase"), Stored(d, "NameEdit", "CharCase"));
        Assert.Equal(1, d.Designer.Undo.UndoCount);

        // A text row without a list: Alt+Down enters edit mode.
        GoTo(h, "Text");
        h.Press("Alt+Down");
        Assert.NotNull(h.TryFind<TextBox>(Row("Text") + ".Editor"));
        h.Press("Escape");

        // The Events page lists the compatible handlers of the form.
        h.Press("Ctrl+Tab");
        GoTo(h, "OnChange");
        h.Press("Alt+Down");
        list = h.Find<ListBox>(Row("OnChange") + ".List");
        Assert.Equal(["GreetButtonClick"], list.ItemsSource!.Cast<string>());
        h.Press("Enter");
        Assert.Equal("GreetButtonClick", ValueText(h, "OnChange"));
    }

    /// <summary>
    /// D-109: the page, the collapsed categories, the expanded rows (per class name) and the name/value divider ratio
    /// (dragged) are written to <c>session.json</c> (<c>objectInspector.*</c>) and restored from it.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-003")]
    [Trait("Req", "DSG-OI-013")]
    [Trait("Req", "UI-DSG-103")]
    public async Task SessionStateRestored()
    {
        await using var h = await HeadlessHarness.StartAsync(Phase5);
        var d = await OpenDesignAsync(h, "GreetButton");
        var inspector = Inspector(h);
        h.Press("F4");

        // Font expanded (Right), another category collapsed, the divider dragged 30 DIP to the right.
        GoTo(h, "Font");
        h.Press("Right");
        Assert.NotNull(h.TryFind<Control>(Row("Font.Name")));
        string? fontCategory = null;
        string? category = null;
        foreach (var row in inspector.Rows)
        {
            if (row.Kind == PropertyRowKind.Category)
            {
                category = row.Path;
            }
            else if (row.Path == "Font")
            {
                fontCategory = category;
            }
        }

        var other = inspector.Rows.First(r => r.Kind == PropertyRowKind.Category && r.Path != fontCategory).Path;
        inspector.ToggleCategory(other); // the click on a category row is covered by D-081
        Dispatcher.UIThread.RunJobs();
        Assert.StartsWith("▸", h.Find<Border>("Tool.ObjectInspector.Category." + other).Child is TextBlock t ? t.Text : "", StringComparison.Ordinal);

        var before = inspector.NameColumnRatio;
        Assert.Equal(ObjectInspectorViewModel.DefaultNameColumnRatio, before);
        h.Drag(Row("Font") + ".Divider", 30, 0);
        var ratio = inspector.NameColumnRatio;
        Assert.True(ratio > before + 0.05, $"ratio {ratio}");
        var fontGrid = (Grid)h.Find<Border>(Row("Font")).Child!;
        Assert.Equal(ratio, fontGrid.ColumnDefinitions[0].Width.Value, 6);
        h.Press("Ctrl+Tab");
        Assert.Equal(InspectorPage.Events, inspector.Page);

        // Expanded rows are kept per class: the edit shows Font collapsed.
        h.Press("Ctrl+Tab");
        d.Designer.Surface!.Selection.Select(d.Designer.Model!.FindByName("NameEdit")!);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(h.TryFind<Control>(Row("Font.Name")));
        d.Designer.Surface.Selection.Select(d.Designer.Model.FindByName("GreetButton")!);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(h.TryFind<Control>(Row("Font.Name")));
        h.Press("Ctrl+Tab");

        h.Services.Session.Save();
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(h.Services.Session.SessionPath))!["objectInspector"]!;
        Assert.Equal("events", (string?)json["page"]);
        Assert.Equal([other], json["collapsedCategories"]!.AsArray().Select(n => (string?)n));
        Assert.Equal(["Font"], json["expandedRows"]!["TButton"]!.AsArray().Select(n => (string?)n));
        Assert.Equal(Math.Round(ratio, 4), (double)json["nameColumnRatio"]!, 6);

        // Everything changed back, then the session is read again: the saved state returns.
        h.Press("Ctrl+Tab");
        inspector.ExpandAll(false);
        inspector.ToggleCategory(other);
        inspector.NameColumnRatio = ObjectInspectorViewModel.DefaultNameColumnRatio;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(InspectorPage.Properties, inspector.Page);
        Assert.Null(h.TryFind<Control>(Row("Font.Name")));

        await h.Services.Session.RestoreAsync(restoreDocuments: false);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(InspectorPage.Events, inspector.Page);
        Assert.Equal(Math.Round(ratio, 4), inspector.NameColumnRatio, 6);
        h.Press("Ctrl+Tab");
        Assert.NotNull(h.TryFind<Control>(Row("Font.Name")));
        Assert.StartsWith("▸", h.Find<Border>("Tool.ObjectInspector.Category." + other).Child is TextBlock c ? c.Text : "", StringComparison.Ordinal);
        Assert.Equal(Math.Round(ratio, 4), ((Grid)h.Find<Border>(Row("Font")).Child!).ColumnDefinitions[0].Width.Value, 6);
    }

    /// <summary>
    /// UI-DSG-113 (D-109): below 260 DIP the arrangement buttons leave the tab strip and the caption's ⋯ menu offers the
    /// two arrangements; wide again, the buttons return and the menu button goes.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DSG-113")]
    public async Task NarrowCaptionMenu()
    {
        await using var h = await HeadlessHarness.StartAsync(Phase5);
        await OpenDesignAsync(h, "GreetButton");
        var view = h.Window.GetVisualDescendants().OfType<ObjectInspectorView>().Single();
        const string ByName = "Tool.ObjectInspector.ToolBar.objectInspector.toggleCategorized:name";

        view.Width = 300;
        Dispatcher.UIThread.RunJobs();
        Assert.True(h.Find<ToggleButton>(ByName).IsEffectivelyVisible);
        Assert.False(h.Find<Button>("Tool.ObjectInspector.Menu").IsVisible);

        view.Width = 230;
        Dispatcher.UIThread.RunJobs();
        Assert.False(h.Find<ToggleButton>(ByName).IsEffectivelyVisible);
        var button = h.Find<Button>("Tool.ObjectInspector.Menu");
        Assert.True(button.IsEffectivelyVisible);
        h.Click("Tool.ObjectInspector.Menu");
        var menu = Assert.IsType<ContextMenu>(button.ContextMenu);
        Assert.True(menu.IsOpen);
        Assert.Equal(["Context.objectInspector.arrangeByCategory", "Context.objectInspector.arrangeByName"], menu.Items.Cast<object>().Select(MenuId));
        Assert.True(MenuItem(menu, "Context.objectInspector.arrangeByCategory").IsChecked);
        MenuItem(menu, "Context.objectInspector.arrangeByName").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("name", h.Services.Settings.Get(SettingsCatalog.ObjectInspectorArrangement));
        CloseMenu(menu);

        view.Width = 300;
        Dispatcher.UIThread.RunJobs();
        Assert.True(h.Find<ToggleButton>(ByName).IsEffectivelyVisible);
        Assert.True(h.Find<ToggleButton>(ByName).IsChecked);
        Assert.False(button.IsVisible);
    }
    /// <summary>UI-DSG-100, UI-DSG-110 (D-109): the splitter above the description pane resizes it; the pane keeps at least 44 DIP.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DSG-100")]
    public async Task DescriptionSplitterResizes()
    {
        await using var h = await HeadlessHarness.StartAsync(Phase5);
        await OpenDesignAsync(h, "GreetButton");
        var description = h.Find<Border>("Tool.ObjectInspector.Description");
        Assert.Equal(58, description.Bounds.Height);

        h.Drag("Tool.ObjectInspector.Splitter", 0, -30);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(88, description.Bounds.Height);

        h.Drag("Tool.ObjectInspector.Splitter", 0, 100);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(44, description.Bounds.Height);
    }
}
