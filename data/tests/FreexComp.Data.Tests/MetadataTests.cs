using System.Text.Json;

namespace FreexComp.Data.Tests;

/// <summary>
/// The component metadata of the data area, <c>data/design/</c> (datasource-and-controls.md §5; task A-066): the files and
/// their index, the facts the specification fixes for the 13 components, and the published properties and events of the
/// Pascal source. The framework's metadata root is loaded first, as the IDE does.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MetadataTests
{
    /// <summary>One row of the component table of datasource-and-controls.md §5.</summary>
    public sealed record Row(string Class, string Unit, string Ancestor, string Kind, string Page, int Order, int Width, int Height, string Renderer)
    {
        public string Prefix => Class[1..];
    }

    public static readonly string[] Pages = ["Data Access", "Data Controls"];

    public static readonly string[] Files =
        ["DB.components.json", "SQLiteData.components.json", "MemData.components.json", "DBCtrls.components.json", "DBGrids.components.json"];

    public static readonly Row[] Rows =
    [
        new("TDataSource", "DB", "TComponent", "nonvisual", "Data Access", 10, 0, 0, "nonvisual"),
        new("TSQLiteConnection", "SQLiteData", "TComponent", "nonvisual", "Data Access", 20, 0, 0, "nonvisual"),
        new("TSQLiteQuery", "SQLiteData", "TDataSet", "nonvisual", "Data Access", 30, 0, 0, "nonvisual"),
        new("TSQLiteTable", "SQLiteData", "TSQLiteQuery", "nonvisual", "Data Access", 40, 0, 0, "nonvisual"),
        new("TMemTable", "MemData", "TDataSet", "nonvisual", "Data Access", 50, 0, 0, "nonvisual"),
        new("TDBGrid", "DBGrids", "TCustomControl", "wincontrol", "Data Controls", 10, 320, 120, "dbgrid"),
        new("TDBNavigator", "DBCtrls", "TCustomControl", "wincontrol", "Data Controls", 20, 240, 25, "dbnavigator"),
        new("TDBText", "DBCtrls", "TCustomLabel", "control", "Data Controls", 30, 65, 17, "label"),
        new("TDBEdit", "DBCtrls", "TCustomEdit", "wincontrol", "Data Controls", 40, 121, 23, "edit"),
        new("TDBMemo", "DBCtrls", "TCustomMemo", "wincontrol", "Data Controls", 50, 185, 89, "memo"),
        new("TDBCheckBox", "DBCtrls", "TCustomCheckBox", "wincontrol", "Data Controls", 60, 97, 17, "checkbox"),
        new("TDBComboBox", "DBCtrls", "TCustomComboBox", "wincontrol", "Data Controls", 70, 145, 23, "combobox"),
        new("TDBLookupComboBox", "DBCtrls", "TCustomComboBox", "wincontrol", "Data Controls", 80, 145, 23, "combobox"),
    ];

    /// <summary>The dataset events of dataset.md §4 with their signature type names (DAT-283).</summary>
    public static readonly (string Name, string Signature)[] DataSetEvents =
    [
        ("BeforeOpen", "TDataSetNotifyEvent"), ("AfterOpen", "TDataSetNotifyEvent"), ("BeforeClose", "TDataSetNotifyEvent"),
        ("AfterClose", "TDataSetNotifyEvent"), ("BeforeScroll", "TDataSetNotifyEvent"), ("AfterScroll", "TDataSetNotifyEvent"),
        ("BeforeInsert", "TDataSetNotifyEvent"), ("AfterInsert", "TDataSetNotifyEvent"), ("BeforeEdit", "TDataSetNotifyEvent"),
        ("AfterEdit", "TDataSetNotifyEvent"), ("BeforePost", "TDataSetNotifyEvent"), ("AfterPost", "TDataSetNotifyEvent"),
        ("BeforeCancel", "TDataSetNotifyEvent"), ("AfterCancel", "TDataSetNotifyEvent"), ("BeforeDelete", "TDataSetNotifyEvent"),
        ("AfterDelete", "TDataSetNotifyEvent"), ("BeforeRefresh", "TDataSetNotifyEvent"), ("AfterRefresh", "TDataSetNotifyEvent"),
        ("OnNewRecord", "TDataSetNotifyEvent"), ("OnPostError", "TDataSetErrorEvent"), ("OnDeleteError", "TDataSetErrorEvent"),
        ("OnEditError", "TDataSetErrorEvent"), ("OnCalcFields", "TDataSetNotifyEvent"), ("OnFilterRecord", "TFilterRecordEvent"),
    ];

    private const string DatabaseFilter = "SQLite databases (*.db;*.sqlite;*.sqlite3)|*.db;*.sqlite;*.sqlite3|All files (*.*)|*.*";

    public static string DesignDirectory => Path.Combine(DataCorpus.RepositoryRoot, "data", "design");

    public static string FrameworkDesignDirectory => Path.Combine(DataCorpus.RepositoryRoot, "ui-framework", "design");

    private static readonly Lazy<PascalSource> Source = new(() => new PascalSource(DataCorpus.SourceDirectory, DataCorpus.UiSourceDirectory, DataCorpus.RtlDirectory));

    private static MetadataCatalog Load() => MetadataCatalog.Load(FrameworkDesignDirectory, DesignDirectory);

    public static IEnumerable<TheoryDataRow<string>> Units() =>
        Files.Select(f => f[..f.IndexOf('.')]).Select(u => new TheoryDataRow<string>(u) { TestDisplayName = "metadata/" + u });

    [Fact]
    [Trait("Req", "DAT-280")]
    public void MetadataFiles_ValidJson_ListedInIndex()
    {
        var catalog = Load();

        var problems = MetadataChecks.FilesAndSchema(catalog, Pages, Files);
        problems.AddRange(MetadataChecks.SharedDeclsAgree(catalog));

        Assert.True(problems.Count == 0, MetadataChecks.Report(problems));
        Assert.Equal(Files, catalog.DataFiles.Select(f => f.Name));
    }

    /// <summary>The 13 components with the values of the specification table, and nothing else that can be created.</summary>
    [Fact]
    [Trait("Req", "DAT-280")]
    public void Metadata_Components_FollowSpecTable()
    {
        var catalog = Load();
        var problems = new List<string>();

        foreach (var row in Rows)
        {
            if (catalog.Find(row.Class) is not { File.IsData: true } c)
            {
                problems.Add($"{row.Class}: no entry in data/design");
                continue;
            }

            var palette = c.Json.TryGetProperty("palette", out var p) ? p : default;
            var size = c.Json.TryGetProperty("defaultSize", out var s) ? $"{s.GetProperty("width").GetInt32()} × {s.GetProperty("height").GetInt32()}" : "—";
            var actual = new[]
            {
                c.File.Name, c.Ancestor, c.Abstract ? "abstract" : "creatable", c.Text("kind"),
                palette.ValueKind == JsonValueKind.Object ? $"{palette.GetProperty("page").GetString()} / {palette.GetProperty("order").GetInt32()}" : "—",
                palette.ValueKind == JsonValueKind.Object ? palette.GetProperty("icon").GetString() : "—",
                size, c.Text("namePrefix"), c.Text("renderer"),
                c.Json.TryGetProperty("acceptsChildren", out var a) ? a.GetRawText() : "—",
            };
            var expected = new[]
            {
                row.Unit + ".components.json", row.Ancestor, "creatable", row.Kind, $"{row.Page} / {row.Order}", $"icons/{row.Class}.svg",
                row.Width == 0 ? "—" : $"{row.Width} × {row.Height}", row.Prefix, row.Renderer, "false",
            };
            if (!actual.SequenceEqual(expected))
            {
                problems.Add($"{row.Class}: metadata [{string.Join("; ", actual)}], specification [{string.Join("; ", expected)}]");
            }
        }

        var creatable = catalog.DataComponents.Where(c => !c.Abstract).Select(c => c.Class).Order(StringComparer.Ordinal);
        Assert.True(problems.Count == 0, MetadataChecks.Report(problems));
        Assert.Equal(Rows.Select(r => r.Class).Order(StringComparer.Ordinal), creatable);

        // the abstract dataset: the type of DataSet references and the ancestor of every dataset (design-time.md DAT-332)
        var dataSet = Assert.Single(catalog.DataComponents, c => c.Abstract);
        Assert.Equal(("TDataSet", "TComponent", "DB.components.json"), (dataSet.Class, dataSet.Ancestor, dataSet.File.Name));
        Assert.False(dataSet.Json.TryGetProperty("palette", out _));

        // a data-bound text does not size itself to its text
        Assert.False(Property(catalog, "TDBText", "AutoSize")["default"].GetBoolean());
    }

    [Fact]
    [Trait("Req", "DAT-281")]
    [Trait("Req", "DAT-282")]
    public void Metadata_PropertyTyping_FollowsSpec()
    {
        var catalog = Load();
        var problems = new List<string>();

        void Expect(string cls, string property, string json)
        {
            var actual = catalog.Find(cls) is { } c
                ? catalog.Effective(c, "properties").FirstOrDefault(p => p["name"].GetString() == property)
                : null;
            using var expected = JsonDocument.Parse(json);
            foreach (var key in expected.RootElement.EnumerateObject())
            {
                var value = actual is not null && actual.TryGetValue(key.Name, out var v) ? Compact(v) : "(none)";
                if (value != Compact(key.Value))
                {
                    problems.Add($"{cls}.{property}: {key.Name}: metadata {value}, specification {Compact(key.Value)}");
                }
            }
        }

        string[] bound = ["TDBGrid", "TDBNavigator", "TDBText", "TDBEdit", "TDBMemo", "TDBCheckBox", "TDBComboBox", "TDBLookupComboBox"];
        foreach (var cls in bound)
        {
            Expect(cls, "DataSource", """{"type":"TDataSource","kind":"component","category":"Data"}""");
        }

        foreach (var cls in bound.Except(["TDBGrid", "TDBNavigator"]))
        {
            Expect(cls, "DataField", """{"type":"string","category":"Data","editor":"fieldname","editorOptions":{"dataSource":"DataSource"}}""");
        }

        Expect("TDBLookupComboBox", "ListSource", """{"type":"TDataSource","kind":"component","category":"Data"}""");
        Expect("TDBLookupComboBox", "KeyField", """{"type":"string","category":"Data","editor":"fieldname","editorOptions":{"dataSource":"ListSource"}}""");
        Expect("TDBLookupComboBox", "ListField", """{"type":"string","category":"Data","editor":"fieldname","editorOptions":{"dataSource":"ListSource"}}""");
        Expect("TDataSource", "DataSet", """{"type":"TDataSet","kind":"component","category":"Data"}""");

        foreach (var cls in new[] { "TSQLiteQuery", "TSQLiteTable", "TMemTable" })
        {
            Expect(cls, "Active", """{"type":"Boolean","default":false,"category":"Data"}""");
            Expect(cls, "Filter", """{"type":"string","category":"Data"}""");
            Expect(cls, "FilterOptions", """{"type":"TFilterOptions","category":"Data"}""");
        }

        foreach (var cls in new[] { "TSQLiteQuery", "TSQLiteTable" })
        {
            Expect(cls, "Connection", """{"type":"TSQLiteConnection","kind":"component","category":"Data"}""");
            Expect(cls, "SQL", """{"type":"TStrings","category":"Data","editor":"strings"}""");
            Expect(cls, "UpdateTable", """{"type":"string","category":"Data","editor":"tablename","editorOptions":{"connection":"Connection"}}""");
            Expect(cls, "KeyFields", """{"type":"string","category":"Data","editor":"fieldname","editorOptions":{"dataSet":"."}}""");
        }

        Expect("TSQLiteTable", "TableName", """{"type":"string","category":"Data","editor":"tablename","editorOptions":{"connection":"Connection"}}""");
        Expect("TSQLiteTable", "IndexFieldNames", """{"type":"string","category":"Data","editor":"fieldname","editorOptions":{"dataSet":"."}}""");

        // the statement and its update rules are generated by the table: never stored, not shown
        foreach (var managed in new[] { "SQL", "UpdateTable", "KeyFields" })
        {
            Expect("TSQLiteTable", managed, """{"hidden":true,"stored":false}""");
        }

        Expect("TSQLiteConnection", "Database",
            """{"type":"string","category":"Data","editor":"filename","editorOptions":{"filter":"FILTER","relativeToProject":true}}""".Replace("FILTER", DatabaseFilter));
        Expect("TSQLiteConnection", "Connected", """{"type":"Boolean","default":false,"category":"Data"}""");
        Expect("TSQLiteConnection", "OpenMode", """{"type":"TSQLiteOpenMode","default":"omCreateReadWrite","category":"Data"}""");
        Expect("TSQLiteConnection", "Params", """{"type":"TStrings","category":"Data","editor":"strings"}""");
        Expect("TDBComboBox", "Items", """{"type":"TStrings","editor":"strings"}""");
        Expect("TDBNavigator", "Hints", """{"type":"TStrings","editor":"strings"}""");
        Expect("TDBNavigator", "VisibleButtons", """{"type":"TNavButtonSet"}""");
        Expect("TDBGrid", "Options", """{"type":"TDBGridOptions"}""");
        Expect("TDBText", "Alignment", """{"type":"TAlignment"}""");
        Expect("TDBGrid", "Columns", """{"type":"TDBGridColumns","kind":"collection","itemClass":"TColumn","category":"Data"}""");

        Assert.True(problems.Count == 0, MetadataChecks.Report(problems));

        // DAT-282: the parameter collection of a query is not design-time data
        foreach (var cls in new[] { "TSQLiteQuery", "TSQLiteTable" })
        {
            Assert.DoesNotContain(catalog.Effective(catalog.Find(cls)!, "properties"), p => p["name"].GetString() == "Params");
        }

        // the column class: an object type of the grid's file; its field name is picked through the grid's data source
        var grids = catalog.DataFiles.Single(f => f.Unit == "DBGrids");
        var column = grids.Section("types").Single(t => t.Name == "TColumn").Value;
        Assert.Equal("object", column.GetProperty("kind").GetString());
        var fieldName = catalog.ObjectProperties(column).Single(p => p["name"].GetString() == "FieldName");
        Assert.Equal("fieldname", fieldName["editor"].GetString());
        Assert.Equal("""{"dataSource":"DataSource"}""", Compact(fieldName["editorOptions"]));

        var verb = Assert.Single(MetadataCatalog.Array(catalog.Find("TDBGrid")!.Json, "designerVerbs"));
        Assert.Equal(("editColumns", "&Columns Editor…", "designer.editCollection", "Columns"),
            (verb.GetProperty("id").GetString(), verb.GetProperty("caption").GetString(), verb.GetProperty("command").GetString(), verb.GetProperty("arg").GetString()));

        // every property edited as a field, table or file name is a Data property
        foreach (var p in catalog.DataComponents.SelectMany(c => MetadataCatalog.Array(c.Json, "properties")))
        {
            if (p.TryGetProperty("editor", out var e) && e.GetString() is "fieldname" or "tablename" or "filename")
            {
                Assert.Equal("Data", p.GetProperty("category").GetString());
            }
        }
    }

    [Fact]
    [Trait("Req", "DAT-283")]
    public void Metadata_Events_FollowSpec()
    {
        var catalog = Load();

        foreach (var cls in new[] { "TSQLiteQuery", "TSQLiteTable", "TMemTable" })
        {
            Assert.Equal(DataSetEvents.OrderBy(e => e.Name, StringComparer.Ordinal),
                Events(catalog, cls).Select(e => (e.Name, e.Signature)).OrderBy(e => e.Name, StringComparer.Ordinal));
        }

        Assert.Equal([("OnDataChange", "TDataChangeEvent", true), ("OnStateChange", "TNotifyEvent", false), ("OnUpdateData", "TNotifyEvent", false)],
            Events(catalog, "TDataSource").OrderBy(e => e.Name, StringComparer.Ordinal));
        Assert.Equal(
            [("AfterConnect", "TNotifyEvent", true), ("AfterDisconnect", "TNotifyEvent", false), ("BeforeConnect", "TNotifyEvent", false), ("BeforeDisconnect", "TNotifyEvent", false)],
            Events(catalog, "TSQLiteConnection").OrderBy(e => e.Name, StringComparer.Ordinal));
        Assert.Contains(("OnClick", "TNavClick", true), Events(catalog, "TDBNavigator"));
        Assert.Contains(("OnCellClick", "TColumnEvent", true), Events(catalog, "TDBGrid"));
        foreach (var row in Rows)
        {
            Assert.True(Events(catalog, row.Class).Count(e => e.Default) <= 1, row.Class + ": more than one default event");
        }

        var signatures = catalog.DataFiles.SelectMany(f => f.Section("eventSignatures")).GroupBy(s => s.Name).ToDictionary(g => g.Key, g => g.First().Value.GetString());
        Assert.Equal("procedure(DataSet: TDataSet)", signatures["TDataSetNotifyEvent"]);
        Assert.Equal("procedure(DataSet: TDataSet; E: EDatabaseError; var Action: TDataAction)", signatures["TDataSetErrorEvent"]);
        Assert.Equal("procedure(DataSet: TDataSet; var Accept: Boolean)", signatures["TFilterRecordEvent"]);
        Assert.Equal("procedure(Sender: TObject; Field: TField)", signatures["TDataChangeEvent"]);
        Assert.Equal("procedure(Sender: TObject; Button: TNavigateBtn)", signatures["TNavClick"]);
        Assert.Equal("procedure(Column: TColumn)", signatures["TColumnEvent"]);
    }

    [Theory]
    [Trait("Req", "DAT-284")]
    [MemberData(nameof(Units))]
    public void MetadataMatchesSource(string unit)
    {
        var required = Rows.Where(r => r.Unit == unit).Select(r => r.Class).Concat(unit == "DB" ? ["TDataSet"] : []);

        var problems = MetadataChecks.MatchesSource(Load(), Source.Value, unit, required);

        Assert.True(problems.Count == 0, MetadataChecks.Report(problems));
    }

    /// <summary>User-visible text carries the reserved translation flag; the tab order uses "not assigned" as its default.</summary>
    [Fact]
    [Trait("Req", "DAT-284")]
    public void Metadata_LocalizableTextAndTabOrder()
    {
        var catalog = Load();

        foreach (var (cls, property) in new[] { ("TDBCheckBox", "Caption"), ("TDBNavigator", "Hints"), ("TDBComboBox", "Items"), ("TDBLookupComboBox", "NullValueText") })
        {
            Assert.True(Property(catalog, cls, property).TryGetValue("localizable", out var flag) && flag.GetBoolean(), $"{cls}.{property} is not localizable");
        }

        Assert.True(catalog.FindType("TColumn") is not null, "TColumn is not declared under types");
        var title = catalog.ObjectProperties(catalog.FindType("TColumn")!.Value).Single(p => p["name"].GetString() == "Title");
        Assert.True(title["localizable"].GetBoolean());
        foreach (var row in Rows.Where(r => r.Kind == "wincontrol"))
        {
            Assert.Equal(-1, Property(catalog, row.Class, "TabOrder")["default"].GetInt32());
        }
    }

    /// <summary>The comparison with the source reports what it is there to report (on a changed copy of the files).</summary>
    [Fact]
    [Trait("Req", "DAT-284")]
    public void MetadataMatchesSource_ReportsDifferences()
    {
        var temp = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            Assert.True(Directory.Exists(DesignDirectory), DesignDirectory + " does not exist");
            foreach (var f in Directory.EnumerateFiles(DesignDirectory, "*.json"))
            {
                File.Copy(f, Path.Combine(temp, Path.GetFileName(f)));
            }

            var file = Path.Combine(temp, "SQLiteData.components.json");
            var text = File.ReadAllText(file);
            (string Old, string New)[] changes =
            [
                ("{ \"name\": \"BusyTimeout\", \"type\": \"Integer\"", "{ \"name\": \"BusyTimeout\", \"type\": \"string\""),
                ("{ \"name\": \"ForeignKeys\", \"type\": \"Boolean\", \"default\": true", "{ \"name\": \"ForeignKeys\", \"type\": \"Boolean\", \"default\": false"),
                ("\"name\": \"LibraryName\"", "\"name\": \"Library\""),
                ("{ \"name\": \"AfterConnect\", \"signature\": \"TNotifyEvent\"", "{ \"name\": \"AfterConnect\", \"signature\": \"TDataSetNotifyEvent\""),
                ("\"omReadWrite\", \"omReadOnly\"", "\"omReadOnly\", \"omReadWrite\""),
                ("\"procedure(DataSet: TDataSet; var Accept: Boolean)\"", "\"procedure(DataSet: TDataSet; Accept: Boolean)\""),
            ];
            foreach (var (old, replacement) in changes)
            {
                Assert.Contains(old, text, StringComparison.Ordinal);
                text = text.Replace(old, replacement, StringComparison.Ordinal);
            }

            File.WriteAllText(file, text);

            var problems = MetadataChecks.MatchesSource(MetadataCatalog.Load(FrameworkDesignDirectory, temp), Source.Value, "SQLiteData", ["TSQLiteConnection", "TNoSuch"]);

            Assert.Contains("TNoSuch: missing (no entry in SQLiteData.components.json)", problems);
            Assert.Contains("TSQLiteConnection.BusyTimeout: type mismatch: metadata string, source Integer", problems);
            Assert.Contains("TSQLiteConnection.ForeignKeys: default: metadata false, source 'default' directive ordinal 1", problems);
            Assert.Contains("TSQLiteConnection.LibraryName: missing (metadata position 7 is Library)", problems);
            Assert.Contains("TSQLiteConnection.AfterConnect: signature mismatch: metadata TDataSetNotifyEvent, source TNotifyEvent", problems);
            Assert.Contains(problems, p => p.StartsWith("SQLiteData.components.json: types.TSQLiteOpenMode: values [omCreateReadWrite, omReadOnly, omReadWrite]", StringComparison.Ordinal));
            Assert.Contains(problems, p => p.StartsWith("SQLiteData.components.json: eventSignatures.TFilterRecordEvent: metadata \"procedure(DataSet: TDataSet; Accept: Boolean)\"", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    private static string Compact(JsonElement e) => JsonSerializer.Serialize(e);

    private static Dictionary<string, JsonElement> Property(MetadataCatalog catalog, string cls, string name)
    {
        var c = catalog.Find(cls);
        Assert.True(c is not null, cls + ": no metadata entry");
        var property = catalog.Effective(c, "properties").FirstOrDefault(p => p["name"].GetString() == name);
        Assert.True(property is not null, $"{cls}.{name}: no such property in the metadata");
        return property;
    }

    private static List<(string Name, string Signature, bool Default)> Events(MetadataCatalog catalog, string cls)
    {
        var c = catalog.Find(cls);
        Assert.True(c is not null, cls + ": no metadata entry");
        return [.. catalog.Effective(c, "events").Select(e => (e["name"].GetString()!, e["signature"].GetString()!, e.TryGetValue("default", out var d) && d.GetBoolean()))];
    }
}
