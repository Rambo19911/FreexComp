using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Designer.Model;
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
        o.ImplementedPhase = 5;
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

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-001")]
    [Trait("Req", "UI-DSG-112")]
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
}
