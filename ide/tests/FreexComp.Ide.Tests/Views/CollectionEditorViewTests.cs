using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Designer.Model;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Fixtures.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.Views;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The collection editor window (dialogs.md §25, UI-DLG-240 … 243; D-096), opened from the Object Inspector.</summary>
[Trait("Phase", "5")]
[Trait("Kind", "Ui")]
public sealed class CollectionEditorViewTests
{
    private static string Row(string path) => "Tool.ObjectInspector.Row." + path;

    private static string? CurrentRow(HeadlessHarness h) =>
        h.FindAll("Tool.ObjectInspector.Row.").FirstOrDefault(c => c.Classes.Contains("current")) is { } row
            ? AutomationIds.Get(row)!["Tool.ObjectInspector.Row.".Length..]
            : null;

    private static string Item(DialogDriver dlg, int index) => (string)dlg.Find<ListBoxItem>("Item." + index).Content!;

    private static CollectionValue? Panels(FormDocumentViewModel d) =>
        d.Designer.Model!.FindByName("StatusBar1")!.FindProperty("Panels")?.Value as CollectionValue;

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-240")]
    [Trait("Req", "UI-DLG-241")]
    [Trait("Req", "DSG-OI-110")]
    public async Task KeyboardAndAutomationIds()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            DesignerViewTests.Files(o);
            o.ImplementedPhase = 5;
            o.ToolWindows.Add(SampleToolViewModel.Definition("ToolPalette", ToolGroupId.RightLower, 5, "Cmd_view_toolPalette"));
            o.InitialFiles["MainForm.pas"] = DesignerViewTests.Unit
                .Replace("GreetButton: TButton;", "GreetButton: TButton;\n    StatusBar1: TStatusBar;", StringComparison.Ordinal)
                .Replace("StdCtrls;", "StdCtrls, ComCtrls;", StringComparison.Ordinal)
                .ReplaceLineEndings("\r\n");
            var form = DesignerViewTests.FormText.ReplaceLineEndings("\n");
            var at = form.TrimEnd().LastIndexOf("end", StringComparison.Ordinal);
            o.InitialFiles["MainForm.form"] = (form[..at]
                + "  object StatusBar1: TStatusBar\n    Left = 0\n    Top = 281\n    Width = 400\n    Height = 19\n    TabOrder = 2\n"
                + "    Panels = <\n      item\n        Text = 'Ready'\n      end\n    >\n  end\n" + form[at..]).ReplaceLineEndings("\r\n");
        });
        var d = await DesignerViewTests.OpenFormAsync(h);
        h.Click(AutomationIds.DocumentViewTab("MainForm.pas", "Design"));
        d.Designer.Surface!.Selection.Select(d.Designer.Model!.FindByName("StatusBar1")!);
        Dispatcher.UIThread.RunJobs();
        h.Press("F4");
        h.Press("Home");
        for (var i = 0; i < 60 && CurrentRow(h) != "Panels"; i++)
        {
            h.Press("Down");
        }

        Assert.Equal("Panels", CurrentRow(h));
        Assert.Equal("(1 item)", h.Find<TextBlock>(Row("Panels") + ".Value").Text);

        // The … button opens the dialog: title, size, initial focus on the list with the first item selected.
        Assert.True(h.Find<Button>(Row("Panels") + ".Button").IsEnabled);
        h.Click(Row("Panels") + ".Button");
        var dlg = await h.WaitForDialogAsync("CollectionEditor");
        Assert.Equal("Edit StatusBar1.Panels", dlg.Window.Title);
        Assert.Equal((640, 400), (dlg.Window.Width, dlg.Window.Height));
        Assert.True(dlg.Window.CanResize);
        Assert.StartsWith("Dialog.CollectionEditor.Item", dlg.FocusedAutomationId ?? "", StringComparison.Ordinal);
        Assert.Equal("0 – Ready", Item(dlg, 0));
        Assert.Equal(0, dlg.Find<ListBox>("Items").SelectedIndex);
        foreach (var field in (string[])["Add", "Delete", "MoveUp", "MoveDown", "Grid", "Grid.Row.Alignment", "Grid.Row.Text", "Ok", "Cancel"])
        {
            Assert.NotNull(dlg.TryFind<Control>(field) ?? dlg.TryFind<Control>("Button." + field));
        }

        Assert.False(dlg.Find<Button>("MoveUp").IsEnabled);

        // Insert adds and selects a new item; Ctrl+Up moves it; Delete removes it (the list keeps focus).
        dlg.Press("Insert");
        Assert.Equal("1 – StatusPanel", Item(dlg, 1));
        Assert.Equal(1, dlg.Find<ListBox>("Items").SelectedIndex);
        dlg.Press("Ctrl+Up");
        Assert.Equal("0 – StatusPanel", Item(dlg, 0));
        Assert.Equal("1 – Ready", Item(dlg, 1));
        Assert.Equal(0, dlg.Find<ListBox>("Items").SelectedIndex);
        Assert.True(dlg.Find<Button>("MoveDown").IsEnabled);

        // F4 moves to the grid; Down, F2 edits; Enter commits the value and keeps the dialog open; the list text follows.
        dlg.Press("F4");
        Assert.Equal("Dialog.CollectionEditor.Grid", dlg.FocusedAutomationId);
        dlg.Press("Down");
        dlg.Press("Down");
        dlg.Press("F2");
        Assert.Equal("Dialog.CollectionEditor.Grid.Row.Text.Editor", dlg.FocusedAutomationId);
        dlg.Type("Grid.Row.Text.Editor", "Ln 1");
        dlg.Press("Enter");
        Assert.False(dlg.Window.IsClosed());
        Assert.Equal("0 – Ln 1", Item(dlg, 0));

        // Escape in an editor cancels that edit only.
        dlg.Press("F2");
        dlg.Type("Grid.Row.Text.Editor", "zzz");
        dlg.Press("Escape");
        Assert.False(dlg.Window.IsClosed());
        Assert.Equal("0 – Ln 1", Item(dlg, 0));
        Assert.Single(Panels(d)!.Items); // the form is untouched while the dialog is open (DSG-OI-116)

        // Enter outside an editor is OK: one undo unit.
        dlg.Press("Enter");
        Dispatcher.UIThread.RunJobs();
        Assert.True(dlg.Window.IsClosed());
        Assert.Equal(2, Panels(d)!.Items.Length);
        Assert.Equal(1, d.Designer.Undo.UndoCount);
        Assert.Equal("(2 items)", h.Find<TextBlock>(Row("Panels") + ".Value").Text);
    }
}

internal static class DialogWindowTestExtensions
{
    /// <summary>The window was closed (no longer shown).</summary>
    public static bool IsClosed(this DialogWindow window) => !window.IsVisible;
}
