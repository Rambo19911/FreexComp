using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Designer.Model;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.EndToEnd;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.Tests.Views;

namespace FreexComp.Ide.Tests;

/// <summary>
/// Phase 7 IDE acceptance (I-150, testing.md §5.4): in a new Windows application a connection, a query, a data source, a
/// grid and a navigator are dropped from the Tool Palette and wired in the Object Inspector (component references, the
/// database file picked relative to the project, the SQL text, the grid columns); the form is saved, closed and reopened:
/// the form and its unit are identical. Real main window, real files, the shipped framework and data-access metadata.
/// </summary>
[Trait("Phase", "7")]
[Trait("Kind", "Ui")]
[Trait("Category", "Integration")]
public sealed class Phase7AcceptanceTests
{
    private const string Row = "Tool.ObjectInspector.Row.";

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DAT-330")]
    [Trait("Req", "DAT-332")]
    [Trait("Req", "DAT-333")]
    [Trait("Req", "DAT-334")]
    [Trait("Req", "DAT-337")]
    [Trait("Req", "DAT-338")]
    public async Task DesignDataFormSaveReopen()
    {
        await using var h = await DataDesignTests.StartAsync(unit: null);
        Assert.Equal(NewProjectOutcome.Created, await h.Services.Projects.CreateProjectAsync(new NewProjectRequest("gui-pascal", "DataApp", h.TempDir)));
        var projectDir = Path.Combine(h.TempDir, "DataApp");
        var unitPath = Path.Combine(projectDir, "MainForm.pas");
        var formPath = Path.Combine(projectDir, "MainForm.form");
        var s = await Phase5Scenario.OpenDesignAsync(h, unitPath);
        await h.ExecuteAsync("view.toolPalette");

        // The connection: its database is picked with the file-name editor and stored relative to the project directory.
        s.Place("TSQLiteConnection", 16, 200);
        Directory.CreateDirectory(Path.Combine(projectDir, "data"));
        h.FileDialogs.NextOpen(Path.Combine(projectDir, "data", "contacts.db"));
        DataDesignTests.GoToRow(h, "Database");
        h.Click(Row + "Database.Button");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new StringValue(@"data\contacts.db"), s.Object("SQLiteConnection1").FindProperty("Database")!.Value);
        Assert.Equal(@"data\contacts.db", h.Find<TextBlock>(Row + "Database.Value").Text);
        Assert.Equal(projectDir, h.FileDialogs.Requests[^1].InitialFolder);

        // The query: its connection chosen by name, its SQL typed in the strings editor.
        s.Place("TSQLiteQuery", 64, 200);
        s.Inspect("Connection", "SQLiteConnection1");
        DataDesignTests.GoToRow(h, "SQL");
        h.Press("Enter");
        var strings = await h.WaitForDialogAsync("StringsEditor");
        strings.Type("Text", "SELECT id, name, email FROM contacts");
        strings.Press("Ctrl+Enter");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["SELECT id, name, email FROM contacts"], ((StringListValue)s.Object("SQLiteQuery1").FindProperty("SQL")!.Value).Lines);

        // The data source on the query; the grid and the navigator on the data source.
        s.Place("TDataSource", 112, 200);
        s.Inspect("DataSet", "SQLiteQuery1");
        s.Place("TDBGrid", 16, 48);
        s.Inspect("DataSource", "DataSource1");
        DataDesignTests.GoToRow(h, "Columns");
        h.Click(Row + "Columns.Button");
        var columns = await h.WaitForDialogAsync("CollectionEditor");
        columns.Press("Insert");
        columns.Press("F4");
        var grid = ((Core.Dialogs.CollectionEditorViewModel)columns.Window.Model).Grid;
        for (var i = 0; i < 10 && grid.CurrentPath != "FieldName"; i++)
        {
            columns.Press("Down");
        }

        columns.Press("F2");
        columns.Type("Grid.Row.FieldName.Editor", "name");
        columns.Press("Enter");
        columns.Click("Ok");
        Dispatcher.UIThread.RunJobs();
        s.Place("TDBNavigator", 16, 16);
        s.Inspect("DataSource", "DataSource1");
        Assert.True(s.Document.IsModified);

        // Ctrl+S writes the unit (fields and units of the dropped components) and the canonical form.
        await Phase5Scenario.SaveAsync(s);
        var unit = File.ReadAllText(unitPath, Encoding.UTF8);
        var form = File.ReadAllText(formPath, Encoding.UTF8);
        Assert.Contains(string.Concat(new[]
        {
            "SQLiteConnection1: TSQLiteConnection", "SQLiteQuery1: TSQLiteQuery", "DataSource1: TDataSource", "DBGrid1: TDBGrid", "DBNavigator1: TDBNavigator",
        }.Select(field => "    " + field + ";\r\n")), unit, StringComparison.Ordinal);
        var uses = unit[unit.IndexOf("uses", StringComparison.Ordinal)..unit.IndexOf("type", StringComparison.Ordinal)];
        foreach (var name in (string[])["SQLiteData", "DB", "DBGrids", "DBCtrls"])
        {
            Assert.Matches(@"[ ,]" + name + "[,;]", uses);
        }

        var normalized = form.ReplaceLineEndings("\n");
        foreach (var expected in (string[])
                 [
                     "  object SQLiteConnection1: TSQLiteConnection\n",
                     "    Database = 'data\\contacts.db'\n",
                     "  object SQLiteQuery1: TSQLiteQuery\n",
                     "    Connection = SQLiteConnection1\n",
                     "    SQL = (\n      'SELECT id, name, email FROM contacts'\n    )\n",
                     "  object DataSource1: TDataSource\n",
                     "    DataSet = SQLiteQuery1\n",
                     "  object DBGrid1: TDBGrid\n",
                     "    Columns = <\n      item\n        FieldName = 'name'\n      end\n    >\n",
                     "  object DBNavigator1: TDBNavigator\n",
                 ])
        {
            Assert.Contains(expected, normalized, StringComparison.Ordinal);
        }

        Assert.Equal(2, normalized.Split("    DataSource = DataSource1\n").Length - 1);

        // Close and reopen: no problems, the same objects and values, and the same form and unit text.
        Assert.True(await h.Services.Documents.CloseAsync(s.Document));
        Dispatcher.UIThread.RunJobs();
        var reopened = await Phase5Scenario.OpenDesignAsync(h, unitPath);
        var designer = reopened.Document.Designer;
        Assert.False(reopened.Document.IsModified);
        Assert.Empty(designer.Problems);
        Assert.DoesNotContain(designer.Model!.AllObjects(), o => o.Info is null || o.IsPreserved);
        Assert.Equal(["MainForm", "SQLiteConnection1", "SQLiteQuery1", "DataSource1", "DBGrid1", "DBNavigator1"],
            designer.Model.AllObjects().Select(o => o.Name));
        Assert.Equal(form, designer.CurrentFormText());
        Assert.Equal(unit, reopened.Document.Buffer.Text);
        Assert.Equal(new IdentValue("SQLiteQuery1"), reopened.Object("DataSource1").FindProperty("DataSet")!.Value);

        // A change and its undo, saved again: both files byte-identical to the first save.
        reopened.Place("TMemTable", 160, 200);
        reopened.Surface.Execute(FreexComp.Designer.Surface.Commands.Undo);
        await h.ExecuteAsync("file.save");
        await h.Services.FileChanges.WhenIdle();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(form, File.ReadAllText(formPath, Encoding.UTF8));
        Assert.Equal(unit, File.ReadAllText(unitPath, Encoding.UTF8));
    }
}
