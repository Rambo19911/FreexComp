using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.Model;
using FreexComp.Designer.Rendering;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Tests.EndToEnd;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.Views;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// Design-time support for the data-access components in the main window (docs/spec/data/design-time.md §2; I-147,
/// I-148) with the shipped metadata: the framework root, then <c>data/design</c> (setting <c>toolchain.dataDesignDir</c>).
/// </summary>
[Trait("Phase", "7")]
[Trait("Kind", "Ui")]
public sealed class DataDesignTests
{
    private const string Item = "Tool.ToolPalette.Item.";
    private const string Page = "Tool.ToolPalette.Page.";

    /// <summary>A form unit without components.</summary>
    internal const string Unit = """
        unit MainForm;

        interface

        uses
          Classes, Controls, Forms;

        type
          TMainForm = class(TForm)
          private
          public
          end;

        var
          MainForm: TMainForm;

        implementation

        {$FORM MainForm.form}

        end.

        """;

    internal const string Form = """
        object MainForm: TMainForm
          Caption = 'MainForm'
          ClientWidth = 400
          ClientHeight = 300
        end

        """;

    internal static string DataDesignDir => Path.Combine(LayoutRoundTrip.RepoRoot, "data", "design");

    /// <summary>A harness whose component metadata is the shipped framework root followed by the shipped data-access root.</summary>
    internal static async Task<HeadlessHarness> StartAsync(string? unit = Unit, string form = Form, Action<HarnessOptions>? configure = null)
    {
        var h = await HeadlessHarness.StartAsync(o =>
        {
            o.ComponentMetadata = false;
            o.Settings["toolchain.frameworkDesignDir"] = LayoutRoundTrip.FrameworkDesignDir;
            o.Settings["toolchain.dataDesignDir"] = DataDesignDir;
            if (unit is not null)
            {
                o.InitialFiles["MainForm.pas"] = unit.ReplaceLineEndings("\r\n");
                o.InitialFiles["MainForm.form"] = form.ReplaceLineEndings("\n");
            }

            configure?.Invoke(o);
        });
        Assert.Equal(MetadataLoadState.Loaded, h.Services.Metadata!.State);
        return h;
    }

    /// <summary>The ids of the visible page headers and items of the Tool Palette, in order.</summary>
    private static List<string> Shown(HeadlessHarness h) =>
        [.. h.FindAll("Tool.ToolPalette.").Where(c => c.IsEffectivelyVisible).Select(c => AutomationIds.Get(c)!)
            .Where(id => id.StartsWith(Item, StringComparison.Ordinal) || id.StartsWith(Page, StringComparison.Ordinal))
            .Select(id => id["Tool.ToolPalette.".Length..])];

    private static string? CurrentRow(HeadlessHarness h) =>
        h.FindAll("Tool.ObjectInspector.Row.").FirstOrDefault(c => c.Classes.Contains("current")) is { } row
            ? AutomationIds.Get(row)!["Tool.ObjectInspector.Row.".Length..]
            : null;

    /// <summary>F4 (unless the Object Inspector has the focus), then the row <paramref name="path"/> of the selected component made current with the keyboard.</summary>
    internal static void GoToRow(HeadlessHarness h, string path)
    {
        if (!h.Find<Control>("Tool.ObjectInspector.Grid").IsKeyboardFocusWithin)
        {
            h.Press("F4");
        }

        h.Press("Home");
        for (var i = 0; i < 300 && CurrentRow(h) != path; i++)
        {
            h.Press("Down");
        }

        Assert.Equal(path, CurrentRow(h));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DAT-330")]
    [Trait("Req", "DAT-331")]
    public async Task PaletteShowsDataPages()
    {
        await using var h = await StartAsync();
        var catalog = h.Services.Metadata!.Catalog;
        Assert.Equal(13, catalog.All.Count(c => c.Palette is { Page: "Data Access" or "Data Controls" }));
        var s = await Phase5Scenario.OpenDesignAsync(h, Path.Combine(h.TempDir, "MainForm.pas"));
        await h.ExecuteAsync("view.toolPalette");

        // The pages of the data root follow the framework's, in the order of its index; the items by their palette order.
        var shown = Shown(h);
        Assert.Equal(["Page.Standard", "Page.Additional", "Page.Win32", "Page.System", "Page.Dialogs", "Page.Data Access", "Page.Data Controls"],
            shown.Where(id => id.StartsWith("Page.", StringComparison.Ordinal)));
        var access = shown.IndexOf("Page.Data Access");
        var controls = shown.IndexOf("Page.Data Controls");
        Assert.Equal(["Item.TDataSource", "Item.TSQLiteConnection", "Item.TSQLiteQuery", "Item.TSQLiteTable", "Item.TMemTable"],
            shown.Skip(access + 1).Take(controls - access - 1));
        Assert.Equal(["Item.TDBGrid", "Item.TDBNavigator", "Item.TDBText", "Item.TDBEdit", "Item.TDBMemo", "Item.TDBCheckBox", "Item.TDBComboBox",
            "Item.TDBLookupComboBox"], shown.Skip(controls + 1));
        Assert.DoesNotContain("Item.TDataSet", shown); // abstract: a reference type only

        // Every data component shows its own icon (data/design/icons/<Class>.svg).
        foreach (var id in shown.Skip(access + 1).Where(id => id.StartsWith("Item.", StringComparison.Ordinal)))
        {
            var icon = h.Find<Control>("Tool.ToolPalette." + id).GetVisualDescendants().OfType<PaletteIcon>().Single();
            Assert.False(icon.IsGeneric, id + " shows the generic icon");
        }

        // DAT-331: a non-visual data component dropped on the form is a tray tile at DesignLeft / DesignTop.
        // (a click on the form: the next free tray slot, tool-palette.md DSG-TP-043).
        s.Place("TDataSource", 48, 200);
        Assert.Equal(["DataSource1"], s.Selected);
        var source = s.Object("DataSource1");
        Assert.Equal(ComponentKind.NonVisual, source.Info!.Kind);
        Assert.Equal((8, 8), (s.Prop("DataSource1", "DesignLeft"), s.Prop("DataSource1", "DesignTop")));
        Assert.Null(source.FindProperty("Left"));
        s.Place("TSQLiteQuery", 96, 200);
        Assert.Equal((88, 8), (s.Prop("SQLiteQuery1", "DesignLeft"), s.Prop("SQLiteQuery1", "DesignTop")));
        var tile = s.Surface.Geometry.GetBounds(source);
        var other = s.Surface.Geometry.GetBounds(s.Object("SQLiteQuery1"));
        Assert.True(s.Surface.Geometry.IsTrayItem(source));
        Assert.Equal((other.Width, other.Height), (tile.Width, tile.Height));
        Assert.Equal((tile.Left + 80, tile.Top), (other.Left, other.Top));
        var (x, y) = s.Center("DataSource1");
        s.Click(x, y);
        Assert.Equal(["DataSource1"], s.Selected);

        // A data-aware control is placed with its default size and drawn by its design renderer.
        s.Place("TDBGrid", 16, 16);
        Assert.Equal((16, 16, 320, 120), (s.Prop("DBGrid1", "Left"), s.Prop("DBGrid1", "Top"), s.Prop("DBGrid1", "Width"), s.Prop("DBGrid1", "Height")));
        Assert.Equal("dbgrid", RendererRegistry.CreateDefault().Resolve(s.Object("DBGrid1").Info!.RendererId).Id);
        Assert.Contains("    DataSource1: TDataSource;\r\n    SQLiteQuery1: TSQLiteQuery;\r\n    DBGrid1: TDBGrid;\r\n", s.Document.Buffer.Text, StringComparison.Ordinal);
        Assert.Contains("  Classes, Controls, Forms, DB, SQLiteData, DBGrids;", s.Document.Buffer.Text, StringComparison.Ordinal);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DAT-337")]
    [Trait("Req", "DSG-OI-116")]
    public async Task ColumnsCollectionEditor()
    {
        const string fields = "  TMainForm = class(TForm)\n    DataSource1: TDataSource;\n    DBGrid1: TDBGrid;\n";
        const string objects = """
              object DataSource1: TDataSource
                DesignLeft = 16
                DesignTop = 240
              end
              object DBGrid1: TDBGrid
                Left = 16
                Top = 16
                Width = 320
                Height = 120
                DataSource = DataSource1
                TabOrder = 0
              end
            end

            """;
        var unit = Unit.ReplaceLineEndings("\n").Replace("  TMainForm = class(TForm)\n", fields, StringComparison.Ordinal)
            .Replace("Classes, Controls, Forms;", "Classes, Controls, Forms, DB, DBGrids;", StringComparison.Ordinal);
        var form = Form.ReplaceLineEndings("\n");
        form = form[..form.LastIndexOf("end", StringComparison.Ordinal)] + objects.ReplaceLineEndings("\n");
        await using var h = await StartAsync(unit, form);
        var s = await Phase5Scenario.OpenDesignAsync(h, Path.Combine(h.TempDir, "MainForm.pas"));
        var designer = s.Document.Designer;
        CollectionValue? Columns() => s.Object("DBGrid1").FindProperty("Columns")?.Value as CollectionValue;
        string Item(DialogDriver dialog, int index) => (string)dialog.Find<ListBoxItem>("Item." + index).Content!;

        // The Columns row of the Object Inspector: "(empty)" with the … button, which opens the standard collection editor.
        var (x, y) = s.Center("DBGrid1");
        s.Click(x, y);
        Assert.Equal(["DBGrid1"], s.Selected);
        GoToRow(h, "Columns");
        Assert.Equal("(empty)", h.Find<TextBlock>("Tool.ObjectInspector.Row.Columns.Value").Text);
        h.Click("Tool.ObjectInspector.Row.Columns.Button");
        var dlg = await h.WaitForDialogAsync("CollectionEditor");
        Assert.Equal("Edit DBGrid1.Columns", dlg.Window.Title);
        Assert.Null(dlg.TryFind<ListBoxItem>("Item.0"));
        Assert.Equal("Dialog.CollectionEditor.Items", dlg.FocusedAutomationId); // UI-DLG-240: the list, also when it is empty

        // Insert adds a TColumn; its properties come from the object class TColumn of the metadata.
        dlg.Press("Insert");
        Assert.NotNull(dlg.TryFind<ListBoxItem>("Item.0"));
        foreach (var property in (string[])["Alignment", "FieldName", "ReadOnly", "Title", "Visible", "Width"])
        {
            Assert.NotNull(dlg.TryFind<Control>("Grid.Row." + property));
        }

        var grid = ((Core.Dialogs.CollectionEditorViewModel)dlg.Window.Model).Grid;
        void Edit(string path, string text)
        {
            if (dlg.FocusedAutomationId != "Dialog.CollectionEditor.Grid")
            {
                dlg.Press("F4"); // from the list to the grid
            }

            Assert.Equal("Dialog.CollectionEditor.Grid", dlg.FocusedAutomationId);
            dlg.Press("Home");
            for (var i = 0; i < 10 && grid.CurrentPath != path; i++)
            {
                dlg.Press("Down");
            }

            Assert.Equal(path, grid.CurrentPath);
            dlg.Press("F2");
            dlg.Find<TextBox>("Grid.Row." + path + ".Editor").SelectAll(); // typing replaces the shown value
            dlg.Window.KeyTextInput(text);
            Dispatcher.UIThread.RunJobs();
            dlg.Press("Enter");
            Assert.False(grid.IsEditing, path + ": " + grid.EditText + " – " + grid.EditError);
        }

        Edit("FieldName", "name"); // the field-name editor is a plain text row in Phase 7 (DAT-335)
        Edit("Width", "100");
        dlg.Click("Add"); // the Add button; Shift+Tab goes back to the list (the new item is selected)
        Assert.Equal("Dialog.CollectionEditor.Add", dlg.FocusedAutomationId);
        dlg.Press("Shift+Tab");
        Edit("FieldName", "email");
        Edit("Title", "E-mail");
        Assert.NotNull(dlg.TryFind<ListBoxItem>("Item.1"));
        _ = Item(dlg, 1);
        Assert.Null(Columns()); // the form is untouched while the dialog is open (DSG-OI-116)

        // OK: one undo unit; the items are stored as a collection and the design renderer shows their titles.
        dlg.Click("Ok");
        Dispatcher.UIThread.RunJobs();
        Assert.False(dlg.Window.IsVisible);
        Assert.Equal(2, Columns()!.Items.Length);
        Assert.Equal(1, designer.Undo.UndoCount);
        Assert.Equal("(2 items)", h.Find<TextBlock>("Tool.ObjectInspector.Row.Columns.Value").Text);
        Assert.Contains(
            "    Columns = <\n      item\n        FieldName = 'name'\n        Width = 100\n      end\n      item\n        FieldName = 'email'\n        Title = 'E-mail'\n      end\n    >\n",
            designer.CurrentFormText().ReplaceLineEndings("\n"), StringComparison.Ordinal);
        var canvas = new RecordingCanvas();
        var ctx = RenderContext.FromLayout(s.Object("DBGrid1"), s.Surface.Layout);
        RendererRegistry.CreateDefault().Resolve(ctx.Info.RendererId).Render(ctx, canvas);
        Assert.Equal(["\"name\"", "\"E-mail\""], canvas.Ops.Where(op => op.Kind == "DrawText").Select(op => op.Text.Split(' ')[1]));

        // The designer verb "Columns Editor…" of the grid's context menu opens the same editor on Columns.
        s.Click(x, y);
        Dispatcher.UIThread.RunJobs();
        h.Window.MouseDown(s.At(x, y), MouseButton.Right);
        h.Window.MouseUp(s.At(x, y), MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
        var menu = Assert.IsType<ContextMenu>(s.Control.ContextMenu);
        var verb = menu.Items.OfType<MenuItem>().First();
        Assert.Equal("Context.Verb.editColumns", AutomationIds.Get(verb));
        Assert.Equal("_Columns Editor…", verb.Header);
        menu.Close();
        verb.Command!.Execute(verb.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        dlg = await h.WaitForDialogAsync("CollectionEditor");
        Assert.Equal("Edit DBGrid1.Columns", dlg.Window.Title);
        Assert.NotNull(dlg.TryFind<ListBoxItem>("Item.1"));
        dlg.Press("Escape");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, designer.Undo.UndoCount);
    }
}
