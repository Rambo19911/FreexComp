using FreexComp.Designer.Catalog;
using FreexComp.Designer.Model;
using FreexComp.Designer.Rendering;
using FreexComp.Designer.Text;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Tests.Build;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Designer;

/// <summary>
/// Design-time support for the data-access components (docs/spec/data/design-time.md §2, DAT-330 … 339; I-147 … I-150)
/// over the shipped metadata: the framework root followed by <c>data/design</c>.
/// </summary>
[Trait("Phase", "7")]
public sealed class DataDesignTests
{
    /// <summary>A form unit without components: the form class and its <c>uses</c> clause.</summary>
    internal static readonly string EmptyUnit = string.Join("\r\n",
        "unit MainForm;",
        "",
        "interface",
        "",
        "uses",
        "  Classes, Controls, Forms;",
        "",
        "type",
        "  TMainForm = class(TForm)",
        "  private",
        "  public",
        "  end;",
        "",
        "var",
        "  MainForm: TMainForm;",
        "",
        "implementation",
        "",
        "{$FORM MainForm.form}",
        "",
        "end.",
        "");

    internal const string EmptyForm = "object MainForm: TMainForm\n  Caption = 'MainForm'\n  ClientWidth = 400\n  ClientHeight = 300\nend\n";

    /// <summary>A connection, three datasets, a data source, a grid, a lookup combo box and a plain button.</summary>
    private static readonly string DataUnit = EmptyUnit
        .Replace("  Classes, Controls, Forms;", "  Classes, Controls, Forms, StdCtrls, DB, SQLiteData, MemData, DBCtrls, DBGrids;", StringComparison.Ordinal)
        .Replace("  TMainForm = class(TForm)\r\n", "  TMainForm = class(TForm)\r\n" + string.Concat(new[]
        {
            "SQLiteConnection1: TSQLiteConnection", "SQLiteQuery1: TSQLiteQuery", "SQLiteTable1: TSQLiteTable", "MemTable1: TMemTable",
            "DataSource1: TDataSource", "DBGrid1: TDBGrid", "DBLookupComboBox1: TDBLookupComboBox", "Button1: TButton",
        }.Select(field => "    " + field + ";\r\n")), StringComparison.Ordinal);

    private const string DataForm = """
        object MainForm: TMainForm
          Caption = 'MainForm'
          ClientWidth = 400
          ClientHeight = 300
          object SQLiteConnection1: TSQLiteConnection
            DesignLeft = 16
            DesignTop = 240
          end
          object SQLiteQuery1: TSQLiteQuery
            DesignLeft = 56
            DesignTop = 240
          end
          object SQLiteTable1: TSQLiteTable
            DesignLeft = 96
            DesignTop = 240
          end
          object MemTable1: TMemTable
            DesignLeft = 136
            DesignTop = 240
          end
          object DataSource1: TDataSource
            DesignLeft = 176
            DesignTop = 240
          end
          object DBGrid1: TDBGrid
            Left = 8
            Top = 8
            Width = 320
            Height = 120
            TabOrder = 0
          end
          object DBLookupComboBox1: TDBLookupComboBox
            Left = 8
            Top = 140
            Width = 145
            Height = 23
            TabOrder = 1
          end
          object Button1: TButton
            Left = 8
            Top = 180
            Width = 75
            Height = 25
            Caption = 'Button1'
            TabOrder = 2
          end
        end

        """;

    private static readonly Lazy<ComponentCatalog> Loaded = new(() =>
    {
        var result = CatalogLoader.Load([Path.Combine(FrameworkDesignDir, "index.json"), Path.Combine(DataDesignDir, "index.json")]);
        Assert.Empty(result.Problems);
        return result.Catalog;
    });

    internal static string FrameworkDesignDir => Path.Combine(SpecTables.RepoRootPath(), "ui-framework", "design");

    internal static string DataDesignDir => Path.Combine(SpecTables.RepoRootPath(), "data", "design");

    /// <summary>The shipped catalog: the framework root, then the data-access root (component-metadata.md load order).</summary>
    internal static ComponentCatalog Catalog => Loaded.Value;

    private static DesignerFixture Fixture(Action<TestCompositionOptions>? configure = null)
    {
        var f = new DesignerFixture(configure);
        ((DocumentService)f.Services.Documents).DesignCatalog = Catalog;
        return f;
    }

    private static string? Stored(FormDesignerViewModel designer, string name, string path) => DesignerFixture.Stored(designer, name, path);

    private static bool Enter(ObjectInspectorViewModel inspector, string path, string text)
    {
        inspector.CurrentPath = path;
        Assert.True(inspector.BeginEdit());
        inspector.Grid.EditText = text;
        return inspector.Commit();
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DAT-332")]
    [Trait("Req", "DAT-336")]
    public Task ComponentReferenceListsCompatibleComponents() => TestUiThread.RunAsync(ComponentReferenceListsCompatibleComponentsOnUiThread);

    private static async Task ComponentReferenceListsCompatibleComponentsOnUiThread()
    {
        var f = Fixture();
        var document = await f.OpenAsync(DataUnit, DataForm.ReplaceLineEndings("\n"));
        var designer = document.Designer;
        Assert.DoesNotContain(designer.Model!.AllObjects(), o => o.Info is null);
        var inspector = new ObjectInspectorViewModel(f.Services.Status);
        inspector.Bind(designer);

        // DataSet: every component whose class is TDataSet or has it among its metadata ancestors
        // (TSQLiteTable → TSQLiteQuery → TDataSet, TMemTable → TDataSet); nothing else, not the edited component.
        DesignerFixture.Select(designer, "DataSource1");
        Assert.Equal(["(none)", "MemTable1", "SQLiteQuery1", "SQLiteTable1"], inspector.ValueList("DataSet"));

        // Connection: the connection only; DataSource / ListSource: the data source only.
        DesignerFixture.Select(designer, "SQLiteQuery1");
        Assert.Equal(["(none)", "SQLiteConnection1"], inspector.ValueList("Connection"));
        DesignerFixture.Select(designer, "SQLiteTable1"); // inherited from TSQLiteQuery
        Assert.Equal(["(none)", "SQLiteConnection1"], inspector.ValueList("Connection"));
        DesignerFixture.Select(designer, "DBGrid1");
        Assert.Equal(["(none)", "DataSource1"], inspector.ValueList("DataSource"));
        DesignerFixture.Select(designer, "DBLookupComboBox1");
        Assert.Equal(["(none)", "DataSource1"], inspector.ValueList("DataSource"));
        Assert.Equal(["(none)", "DataSource1"], inspector.ValueList("ListSource"));

        // A listed name is stored as a reference; a component of another class is refused.
        DesignerFixture.Select(designer, "DataSource1");
        Assert.True(Enter(inspector, "DataSet", "SQLiteTable1"));
        Assert.Equal("SQLiteTable1", Stored(designer, "DataSource1", "DataSet"));
        Assert.False(Enter(inspector, "DataSet", "SQLiteConnection1"));
        inspector.Grid.Cancel();
        Assert.Equal("SQLiteTable1", Stored(designer, "DataSource1", "DataSet"));
        DesignerFixture.Select(designer, "SQLiteTable1");
        Assert.True(Enter(inspector, "Connection", "SQLiteConnection1"));
        DesignerFixture.Select(designer, "DBGrid1");
        Assert.True(Enter(inspector, "DataSource", "DataSource1"));

        // DAT-336: Active / Connected are plain Boolean rows stored in the form.
        DesignerFixture.Select(designer, "SQLiteConnection1");
        Assert.True(Enter(inspector, "Connected", "True"));
        DesignerFixture.Select(designer, "SQLiteTable1");
        Assert.True(Enter(inspector, "Active", "True"));
        var text = designer.CurrentFormText();
        Assert.Contains("    DataSet = SQLiteTable1\n", text, StringComparison.Ordinal);
        Assert.Contains("    Connection = SQLiteConnection1\n", text, StringComparison.Ordinal);
        Assert.Contains("    DataSource = DataSource1\n", text, StringComparison.Ordinal);
        Assert.Contains("    Connected = True\n", text, StringComparison.Ordinal);
        Assert.Contains("    Active = True\n", text, StringComparison.Ordinal);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DAT-333")]
    [Trait("Req", "DAT-334")]
    [Trait("Req", "DAT-335")]
    public Task DatabaseFileNameIsProjectRelative() => TestUiThread.RunAsync(DatabaseFileNameIsProjectRelativeOnUiThread);

    private static async Task DatabaseFileNameIsProjectRelativeOnUiThread()
    {
        var pickers = new FakeFileDialogService();
        var f = Fixture(o => o.FileDialogs = pickers);
        f.File(DesignerFixture.Dir + @"\P.pproj", """
            { "formatVersion": 1, "name": "P", "appType": "console", "mainSource": "P.pas",
              "files": [ { "path": "P.pas" }, { "path": "MainForm.pas" } ] }
            """);
        f.File(DesignerFixture.Dir + @"\P.pas", "program P;\nuses MainForm;\nbegin\nend.\n");
        f.File(DesignerFixture.UnitPath, DataUnit);
        f.File(DesignerFixture.FormPath, DataForm.ReplaceLineEndings("\n"));
        f.Memory.CreateDirectory(DesignerFixture.Dir + @"\data");
        Assert.True(await f.Services.Projects.OpenAsync(DesignerFixture.Dir + @"\P.pproj"));
        var document = await f.OpenAsync(DataUnit, DataForm.ReplaceLineEndings("\n"));
        var designer = document.Designer;
        var inspector = new ObjectInspectorViewModel(f.Services.Status, null, f.Services.Dialogs, f.Services.FilePrompts);
        inspector.Bind(designer);
        DesignerFixture.Select(designer, "SQLiteConnection1");
        inspector.CurrentPath = "Database";
        var row = inspector.Rows.Single(r => r.Path == "Database");

        // Text + the file button: the row is edited as text and has the button of the standard file-name editor.
        Assert.True(PropertyGridViewModel.IsFileNameRow(row));
        Assert.True(inspector.Grid.ShowsButton(row));
        Assert.False(PropertyGridViewModel.HasEditorButton(row)); // not a dialog-only row: typing stays possible

        // A file inside the project directory is stored relative to it.
        pickers.NextOpen(DesignerFixture.Dir + @"\data\contacts.db");
        await inspector.Grid.EditInDialogAsync();
        Assert.Equal(@"data\contacts.db", Stored(designer, "SQLiteConnection1", "Database"));
        Assert.Equal(1, designer.Undo.UndoCount);
        var request = Assert.Single(pickers.Requests);
        Assert.Equal("propertyFile", request.PromptId);
        Assert.False(request.AllowMultiple);
        Assert.Equal(DesignerFixture.Dir, request.InitialFolder); // nothing stored yet: the project directory
        Assert.Equal(["SQLite databases (*.db;*.sqlite;*.sqlite3)", "All files (*.*)"], request.Filters.Select(x => x.Label));
        Assert.Equal(["*.db", "*.sqlite", "*.sqlite3"], request.Filters[0].Patterns);

        // The next prompt starts in the folder of the stored file.
        pickers.NextOpen(DesignerFixture.Dir + @"\app.sqlite");
        await inspector.Grid.EditInDialogAsync();
        Assert.Equal("app.sqlite", Stored(designer, "SQLiteConnection1", "Database"));
        Assert.Equal(DesignerFixture.Dir + @"\data", pickers.Requests[1].InitialFolder);

        // Outside the project directory (also a sibling folder sharing its prefix): the absolute path.
        pickers.NextOpen(@"D:\shared\other.sqlite3");
        await inspector.Grid.EditInDialogAsync();
        Assert.Equal(@"D:\shared\other.sqlite3", Stored(designer, "SQLiteConnection1", "Database"));
        pickers.NextOpen(DesignerFixture.Dir + @"2\x.db");
        await inspector.Grid.EditInDialogAsync();
        Assert.Equal(DesignerFixture.Dir + @"2\x.db", Stored(designer, "SQLiteConnection1", "Database"));

        // Cancel changes nothing; typing a value by hand is always allowed.
        var units = designer.Undo.UndoCount;
        pickers.NextCancel();
        await inspector.Grid.EditInDialogAsync();
        Assert.Equal(DesignerFixture.Dir + @"2\x.db", Stored(designer, "SQLiteConnection1", "Database"));
        Assert.Equal(units, designer.Undo.UndoCount);
        Assert.True(Enter(inspector, "Database", ":memory:"));
        Assert.Equal(":memory:", Stored(designer, "SQLiteConnection1", "Database"));
        await inspector.ActivateCurrentAsync(); // Enter on the row edits the text, it does not open the picker
        Assert.True(inspector.Grid.IsEditing);
        inspector.Grid.Cancel();
        Assert.Equal(5, pickers.Requests.Count);

        // The pure rule (object-inspector.md §5.1 `editor: "filename"`).
        Assert.Equal(@"db\a.db", PropertyGridViewModel.StoredFileName(@"C:\p\db\a.db", @"C:\p", relativeToProject: true));
        Assert.Equal(@"db\a.db", PropertyGridViewModel.StoredFileName(@"c:\P\db\a.db", @"C:\p\", relativeToProject: true));
        Assert.Equal(@"C:\p\db\a.db", PropertyGridViewModel.StoredFileName(@"C:\p\db\a.db", @"C:\p", relativeToProject: false));
        Assert.Equal(@"C:\p\db\a.db", PropertyGridViewModel.StoredFileName(@"C:\p\db\a.db", null, relativeToProject: true));
        Assert.Equal(@"C:\q\a.db", PropertyGridViewModel.StoredFileName(@"C:\q\a.db", @"C:\p", relativeToProject: true));

        // DAT-334: Params / SQL are standard string-list rows; DAT-335: the name editors are plain text rows.
        Assert.True(PropertyGridViewModel.IsStringsRow(inspector.Rows.Single(r => r.Path == "Params")));
        DesignerFixture.Select(designer, "SQLiteQuery1");
        Assert.True(PropertyGridViewModel.IsStringsRow(inspector.Rows.Single(r => r.Path == "SQL")));
        Assert.False(inspector.Grid.ShowsButton(inspector.Rows.Single(r => r.Path == "UpdateTable")));
        Assert.True(Enter(inspector, "UpdateTable", "contacts"));
        Assert.True(Enter(inspector, "KeyFields", "id;name"));
        DesignerFixture.Select(designer, "SQLiteTable1");
        Assert.True(Enter(inspector, "TableName", "contacts"));
        Assert.True(Enter(inspector, "IndexFieldNames", "name"));
        DesignerFixture.Select(designer, "DBLookupComboBox1");
        Assert.True(Enter(inspector, "DataField", "city_id"));
        Assert.True(Enter(inspector, "KeyField", "id"));
        Assert.True(Enter(inspector, "ListField", "name"));
        Assert.Equal("contacts", Stored(designer, "SQLiteTable1", "TableName"));
        Assert.Equal("city_id", Stored(designer, "DBLookupComboBox1", "DataField"));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DAT-339")]
    public void RendersDbGridHeaderAndNavigatorButtons()
    {
        var registry = RendererRegistry.CreateDefault();
        Assert.Equal("dbgrid", registry.Resolve("dbgrid").Id);
        Assert.Equal("dbnavigator", registry.Resolve("dbnavigator").Id);
        Assert.Equal("dbgrid", Catalog.Find("TDBGrid")!.RendererId);
        Assert.Equal("dbnavigator", Catalog.Find("TDBNavigator")!.RendererId);

        var document = FormParser.Parse("""
            object F: TForm
              ClientWidth = 400
              ClientHeight = 400
              object Grid: TDBGrid
                Left = 8
                Top = 8
                Width = 320
                Height = 120
                Columns = <
                  item
                    FieldName = 'name'
                    Width = 100
                  end
                  item
                    FieldName = 'email'
                    Title = 'E-mail'
                  end
                  item
                    FieldName = 'secret'
                    Visible = False
                  end
                >
              end
              object Plain: TDBGrid
                Left = 8
                Top = 136
                Width = 200
                Height = 60
                Options = [dgEditing, dgColLines, dgRowLines]
                Columns = <
                  item
                    FieldName = 'id'
                  end
                >
              end
              object DBGrid1: TDBGrid
                Left = 8
                Top = 200
                Width = 320
                Height = 120
              end
              object Nav: TDBNavigator
                Left = 8
                Top = 330
                Width = 240
                Height = 25
              end
              object Three: TDBNavigator
                Left = 8
                Top = 360
                Width = 100
                Height = 25
                VisibleButtons = [nbNext, nbFirst, nbPrior]
              end
              object NoButtons: TDBNavigator
                Left = 120
                Top = 360
                Width = 100
                Height = 25
                VisibleButtons = []
              end
            end

            """.ReplaceLineEndings("\n"), "data.form", Catalog).Document!;

        List<string> Ops(string name, int? width = null, int? height = null)
        {
            var obj = document.FindByName(name)!;
            var bounds = PropertyAccess.GetBounds(obj);
            var ctx = new RenderContext(obj, width ?? bounds.Width, height ?? bounds.Height);
            var canvas = new RecordingCanvas();
            registry.Resolve(ctx.Info.RendererId).Render(ctx, canvas);
            Assert.All(canvas.Ops.Where(op => op.Extent is not null), op =>
                Assert.True(op.Extent!.Value.Left >= 0 && op.Extent.Value.Top >= 0 && op.Extent.Value.Right <= ctx.Width && op.Extent.Value.Bottom <= ctx.Height,
                    name + " draws outside its bounds: " + op.Text)); // DSG-RND-005
            return [.. canvas.Ops.Select(op => op.Text)];
        }

        // The grid: field frame; the title row (font line height 15 + 4) over the 12-wide indicator column and the visible
        // persistent columns: the stored width, else max(title length) × the width of "0" (6) + 4; titles left-aligned
        // with 2 px padding; the grid lines inside the cells (DAT-261, 262, 264).
        var grid = Ops("Grid");
        Assert.Equal(
        [
            "FillRect 0,0,320,120 #FFFFFFFF",
            "DrawRect 0,0,320,120 #7A7A7AFF 1 Solid",
            "PushClip 1,1,318,118",
            "FillRect 1,1,152,19 #F0F0F0FF",
            "DrawLine 12.5,1,12.5,20 #ADADADFF 1 Solid",
            "DrawText \"name\" 15,1,96,18 \"Segoe UI\" 12 #000000FF Left Center",
            "DrawLine 112.5,1,112.5,20 #ADADADFF 1 Solid",
            "DrawText \"E-mail\" 115,1,36,18 \"Segoe UI\" 12 #000000FF Left Center",
            "DrawLine 152.5,1,152.5,20 #ADADADFF 1 Solid",
            "DrawLine 1,19.5,153,19.5 #ADADADFF 1 Solid",
            "PopClip",
        ], grid);

        // Without dgTitles there is no title row; without dgIndicator no indicator column: the frame only.
        Assert.Equal(["FillRect 0,0,200,60 #FFFFFFFF", "DrawRect 0,0,200,60 #7A7A7AFF 1 Solid"], Ops("Plain"));

        // No persistent column: the empty grid with the component name centred.
        Assert.Equal(
        [
            "FillRect 0,0,320,120 #FFFFFFFF",
            "DrawRect 0,0,320,120 #7A7A7AFF 1 Solid",
            "DrawText \"DBGrid1\" 1,1,318,118 \"Segoe UI\" 12 #6D6D6DFF Center Center",
        ], Ops("DBGrid1"));

        // The navigator: one button per VisibleButtons member, in enumeration order, ClientWidth div count wide, the last
        // one takes the remainder; full height (DAT-256).
        var nav = Ops("Nav");
        Assert.Equal([.. Enumerable.Range(0, 10).Select(i => $"DrawRect {i * 24},0,24,25 #ADADADFF 1 Solid")], nav.Where(op => op.StartsWith("DrawRect", StringComparison.Ordinal)));
        Assert.Equal(10, nav.Count(op => op.StartsWith("FillRect", StringComparison.Ordinal)));
        var three = Ops("Three");
        Assert.Equal(["DrawRect 0,0,33,25 #ADADADFF 1 Solid", "DrawRect 33,0,33,25 #ADADADFF 1 Solid", "DrawRect 66,0,34,25 #ADADADFF 1 Solid"],
            three.Where(op => op.StartsWith("DrawRect", StringComparison.Ordinal)));
        // First, Prior, Next in enumeration order whatever the stored order: bar + left triangle, left triangle, right triangle.
        Assert.Equal(3, three.Count(op => op.StartsWith("FillPolygon", StringComparison.Ordinal)));
        Assert.Equal(["FillRect 0,0,100,25 #F0F0F0FF"], Ops("NoButtons"));

        // Each of the ten buttons has a glyph; degenerate sizes draw nothing outside the bounds and never throw.
        Assert.True(nav.Count > 30, string.Join("\n", nav));
        foreach (var name in (string[])["Grid", "DBGrid1", "Nav", "Three"])
        {
            foreach (var (w, h) in ((int, int)[])[(0, 0), (1, 1), (3, 200), (200, 3), (7, 7)])
            {
                _ = Ops(name, w, h);
            }
        }

        Assert.Empty(Ops("Nav", 0, 25));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DAT-338")]
    [Trait("Req", "DAT-331")]
    public Task DropAddsUnitAndField() => TestUiThread.RunAsync(DropAddsUnitAndFieldOnUiThread);

    private static async Task DropAddsUnitAndFieldOnUiThread()
    {
        var f = Fixture();
        var document = await f.OpenAsync(EmptyUnit, EmptyForm);
        var designer = document.Designer;
        var surface = designer.Surface!;
        string Uses() => document.Buffer.Text.Split("\r\n").Single(l => l.StartsWith("  Classes,", StringComparison.Ordinal));

        // A data-aware control adds its own unit only: the unit of the referenced types (DB) is not needed.
        surface.PlaceAtDefaultPosition("TDBGrid");
        Assert.Equal("  Classes, Controls, Forms, DBGrids;", Uses());
        Assert.Contains("  TMainForm = class(TForm)\r\n    DBGrid1: TDBGrid;\r\n", document.Buffer.Text, StringComparison.Ordinal);
        var afterGrid = document.Buffer.Text;

        // DB is added when a DB component is dropped; a unit already used is not added twice.
        surface.PlaceAtDefaultPosition("TDataSource");
        Assert.Equal("  Classes, Controls, Forms, DBGrids, DB;", Uses());
        surface.PlaceAtDefaultPosition("TSQLiteConnection");
        surface.PlaceAtDefaultPosition("TSQLiteQuery");
        surface.PlaceAtDefaultPosition("TSQLiteTable");
        Assert.Equal("  Classes, Controls, Forms, DBGrids, DB, SQLiteData;", Uses());
        surface.PlaceAtDefaultPosition("TMemTable");
        surface.PlaceAtDefaultPosition("TDBNavigator");
        surface.PlaceAtDefaultPosition("TDBEdit");
        Assert.Equal("  Classes, Controls, Forms, DBGrids, DB, SQLiteData, MemData, DBCtrls;", Uses());
        Assert.Contains(string.Concat(new[]
        {
            "DBGrid1: TDBGrid", "DataSource1: TDataSource", "SQLiteConnection1: TSQLiteConnection", "SQLiteQuery1: TSQLiteQuery",
            "SQLiteTable1: TSQLiteTable", "MemTable1: TMemTable", "DBNavigator1: TDBNavigator", "DBEdit1: TDBEdit",
        }.Select(field => "    " + field + ";\r\n")), document.Buffer.Text, StringComparison.Ordinal);

        // DAT-331: the non-visual components are tray tiles positioned by DesignLeft / DesignTop; the controls have bounds.
        foreach (var name in (string[])["DataSource1", "SQLiteConnection1", "SQLiteQuery1", "SQLiteTable1", "MemTable1"])
        {
            var obj = DesignerFixture.Object(designer, name);
            Assert.Equal(ComponentKind.NonVisual, obj.Info!.Kind);
            Assert.NotNull(obj.FindProperty("DesignLeft"));
            Assert.NotNull(obj.FindProperty("DesignTop"));
            Assert.Null(obj.FindProperty("Width"));
        }

        Assert.Equal(("320", "120"), (Stored(designer, "DBGrid1", "Width"), Stored(designer, "DBGrid1", "Height")));
        Assert.Equal(("240", "25"), (Stored(designer, "DBNavigator1", "Width"), Stored(designer, "DBNavigator1", "Height")));

        // Each drop is one undo unit that removes the field and the uses entry again.
        Assert.Equal(8, designer.Undo.UndoCount);
        for (var i = 0; i < 7; i++)
        {
            surface.Execute(FreexComp.Designer.Surface.Commands.Undo);
        }

        Assert.Equal(afterGrid, document.Buffer.Text);
        surface.Execute(FreexComp.Designer.Surface.Commands.Undo);
        Assert.Equal(EmptyUnit, document.Buffer.Text);
    }

    /// <summary>
    /// IDE-P3-010, IDE-SET-050: the data-access directory (the parent of <c>toolchain.dataDesignDir</c>, else the automatic
    /// search) contributes <c>&lt;dir&gt;\src</c> to the build request after the framework's.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-010")]
    [Trait("Req", "IDE-SET-050")]
    public Task BuildRequestSearchesDataSources() => TestUiThread.RunAsync(BuildRequestSearchesDataSourcesOnUiThread);

    private static async Task BuildRequestSearchesDataSourcesOnUiThread()
    {
        const string ide = @"C:\repo\ide\src\FreexComp.Ide\bin";
        var bed = new BuildTestBed(o => o.ToolchainEnvironment = new Core.Build.ToolchainEnvironment(@"D:\home", ide, []));
        var locator = bed.Services.Toolchain;
        Assert.Null(locator.Resolve().DataDir);

        // nearest ancestor containing data\design\index.json → <IDE dir>\data → %FREEXCOMP_HOME%\data → the setting's parent
        bed.File(@"C:\repo\data\design\index.json", "{}");
        Assert.Equal(@"C:\repo\data", locator.Resolve().DataDir);
        bed.File(ide + @"\data\design\index.json", "{}");
        Assert.Equal(ide + @"\data", locator.Resolve().DataDir);
        bed.File(@"D:\home\data\design\index.json", "{}");
        Assert.Equal(@"D:\home\data", locator.Resolve().DataDir);
        Assert.Equal(@"D:\home\data", locator.ResolveAutomatic().DataDir);
        bed.Services.Settings.Set(SettingsCatalog.ToolchainDataDesignDir, @"E:\lib\data\design\");
        Assert.Equal(@"E:\lib\data", locator.Resolve().DataDir);
        Assert.Equal(@"D:\home\data", locator.ResolveAutomatic().DataDir);

        // The request: framework sources, then data-access sources.
        bed.File(@"D:\home\ui-framework\design\index.json", "{}");
        await bed.OpenHelloAsync();
        bed.Engine.Enqueue(new FakeBuild([], FakeBuildEngine.Result(BuildTestBed.ProjectPath, success: true)));
        Assert.NotNull(await bed.Services.Build.BuildAsync(Core.Build.BuildKind.Build));
        Assert.Equal([@"D:\home\ui-framework\src", @"E:\lib\data\src"], Assert.Single(bed.Engine.Requests).ExtraSearchPaths);
    }
}
