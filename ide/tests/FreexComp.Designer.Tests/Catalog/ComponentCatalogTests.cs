using FreexComp.Designer.Catalog;
using FreexComp.Designer.Inspector;
using FreexComp.Designer.Model;
using static FreexComp.Designer.Tests.Catalog.CatalogLoaderTests;

namespace FreexComp.Designer.Tests.Catalog;

/// <summary>The metadata schema additions of component-metadata.md (D-095).</summary>
public sealed class ComponentCatalogTests
{
    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-TP-002")]
    [Trait("Req", "DSG-TP-006")]
    [Trait("Req", "DSG-TP-007")]
    [Trait("Req", "DSG-TP-021")]
    public void LoadsSchemaAdditions()
    {
        var files = new MemoryFiles
        {
            [@"C:\ui\index.json"] = """
                { "formatVersion": 1, "palettePages": ["Standard", "Additional", "Standard"], "files": ["Base.components.json"] }
                """,
            [@"C:\ui\Base.components.json"] = """
                {
                  "formatVersion": 1,
                  "unit": "Base",
                  "components": [
                    { "class": "TControl", "abstract": true, "properties": [
                        { "name": "Left", "type": "Integer", "category": "Layout" },
                        { "name": "Caption", "type": "string", "default": "", "localizable": true },
                        { "name": "Hint", "type": "string", "default": "" }
                    ] },
                    { "class": "TBook", "ancestor": "TControl", "kind": "container", "acceptsChildren": true,
                      "palette": { "page": "Standard", "order": 1 }, "childClasses": ["TPage"] },
                    { "class": "TSubBook", "ancestor": "TBook", "palette": { "page": "Standard", "order": 2 } },
                    { "class": "TPage", "ancestor": "TControl", "kind": "container", "parentClasses": ["TBook"],
                      "properties": [ { "name": "Left", "type": "Integer", "hidden": true, "stored": false } ] },
                    { "class": "TSubPage", "ancestor": "TPage" }
                  ]
                }
                """,
            [@"C:\data\index.json"] = """
                { "formatVersion": 1, "palettePages": ["Data Access", "Additional"], "files": ["Db.components.json"] }
                """,
            [@"C:\data\Db.components.json"] = """
                {
                  "formatVersion": 1,
                  "unit": "Db",
                  "components": [
                    { "class": "TGridLink", "ancestor": "TControl", "palette": { "page": "Data Controls", "order": 1 },
                      "properties": [
                        { "name": "DataField", "type": "string", "default": "", "editor": "fieldname",
                          "editorOptions": { "dataSource": "Source" } },
                        { "name": "TableName", "type": "string", "default": "", "editor": "tablename" },
                        { "name": "FileName", "type": "string", "default": "", "editor": "filename",
                          "editorOptions": { "filter": "Data (*.db)|*.db", "relativeToProject": true } }
                      ] }
                  ]
                }
                """,
        };

        var result = CatalogLoader.Load([@"C:\ui\index.json", @"C:\data\index.json"], files.Read);
        Assert.Empty(result.Problems);
        var catalog = result.Catalog;

        // palettePages: the roots' lists concatenated in root order, duplicates ignored (component-metadata.md §1).
        Assert.Equal(["Standard", "Additional", "Data Access"], catalog.PalettePages);

        // The built-in TComponent: the root of every chain, abstract, contributes Tag (Int64, 0, Data).
        var root = catalog.Find("TComponent")!;
        Assert.True(root.IsAbstract);
        var tag = Assert.Single(root.Properties);
        Assert.Equal(("Tag", "Int64", (object?)0L, "Data"), (tag.Name, tag.Type, tag.Default, tag.Category));
        Assert.Equal("TComponent", catalog.Find("TControl")!.Ancestor);
        Assert.Equal(["Tag", "Left", "Caption", "Hint"], catalog.Find("TBook")!.Properties.Select(p => p.Name));
        Assert.True(catalog.IsAssignable("TGridLink", "TComponent")); // a later root uses an earlier root's classes

        // stored: default true; false on a redeclaration keeps the position of the first declaration.
        Assert.True(catalog.Find("TBook")!.FindProperty("Left")!.Stored);
        var pageLeft = catalog.Find("TPage")!.FindProperty("Left")!;
        Assert.False(pageLeft.Stored);
        Assert.Equal(1, catalog.Find("TPage")!.Properties.IndexOf(pageLeft));
        Assert.False(catalog.Find("TSubPage")!.FindProperty("Left")!.Stored); // property keys are inherited

        // childClasses / parentClasses are read and not inherited (DSG-TP-006).
        Assert.Equal(["TPage"], catalog.Find("TBook")!.ChildClasses);
        Assert.Empty(catalog.Find("TSubBook")!.ChildClasses);
        Assert.Equal(["TBook"], catalog.Find("TPage")!.ParentClasses);
        Assert.Empty(catalog.Find("TSubPage")!.ParentClasses);

        // localizable is read (reserved, ignored by the IDE); default false.
        Assert.True(catalog.Find("TBook")!.FindProperty("Caption")!.Localizable);
        Assert.False(catalog.Find("TBook")!.FindProperty("Hint")!.Localizable);

        // editorOptions: an object of options converted like defaults; absent → null.
        var link = catalog.Find("TGridLink")!;
        Assert.Equal("Source", link.FindProperty("DataField")!.EditorOptions!["dataSource"]);
        Assert.Null(link.FindProperty("TableName")!.EditorOptions);
        var fileOptions = link.FindProperty("FileName")!.EditorOptions!;
        Assert.Equal("Data (*.db)|*.db", fileOptions["filter"]);
        Assert.Equal(true, fileOptions["relativeToProject"]);

        // fieldname / tablename are plain text before Phase 8 (component-metadata.md §3).
        var converter = new ValueConverter(catalog);
        foreach (var name in (string[])["DataField", "TableName"])
        {
            var property = link.FindProperty(name)!;
            var parsed = converter.Parse(property, "Id;Name", null);
            Assert.True(parsed.Succeeded);
            Assert.Equal(new StringValue("Id;Name"), parsed.Value);
            Assert.Equal("Id;Name", converter.Format(property, new StringValue("Id;Name")));
        }
    }

    // ---- Phase 8: the component key "baseline" (alignment-guidelines.md DSG-SUR-207, 210; D-115) ----

    private static CatalogLoadResult LoadBaselines(string components)
    {
        var files = new MemoryFiles
        {
            [@"C:\ui\index.json"] = """{ "formatVersion": 1, "files": ["Base.components.json"] }""",
            [@"C:\ui\Base.components.json"] = "{ \"formatVersion\": 1, \"unit\": \"Base\", \"components\": [\n" + components + "\n] }",
        };
        return CatalogLoader.Load([@"C:\ui\index.json"], files.Read);
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-207")]
    public void BaselineKey_ParsedAndInherited()
    {
        var result = LoadBaselines("""
            { "class": "TPlain" },
            { "class": "TNone", "baseline": "none" },
            { "class": "TTop", "baseline": { "align": "top", "inset": 3 } },
            { "class": "TTopDefault", "baseline": { "align": "top" } },
            { "class": "TTopMax", "baseline": { "inset": 255, "align": "top" } },
            { "class": "TCenter", "baseline": { "align": "center" } },
            { "class": "TTopChild", "ancestor": "TTop" },
            { "class": "TTopGrandChild", "ancestor": "TTopChild", "renderer": "edit" },
            { "class": "TOverride", "ancestor": "TTopChild", "baseline": { "align": "center" } },
            { "class": "TOverrideChild", "ancestor": "TOverride" },
            { "class": "TSuppressed", "ancestor": "TCenter", "baseline": "none" },
            { "class": "TPlainChild", "ancestor": "TPlain" }
            """);
        Assert.Empty(result.Problems);
        BaselineRule? Rule(string name) => result.Catalog.Find(name)!.Baseline;

        // Absent in the whole chain: no rule (the design renderer decides). The built-in root has none.
        Assert.Null(Rule("TPlain"));
        Assert.Null(Rule("TPlainChild"));
        Assert.Null(result.Catalog.Find("TComponent")!.Baseline);

        // The three shapes; inset is optional (0) and goes up to 255.
        Assert.Equal(new BaselineRule(BaselineAlign.None), Rule("TNone"));
        Assert.Equal(new BaselineRule(BaselineAlign.Top, 3), Rule("TTop"));
        Assert.Equal(new BaselineRule(BaselineAlign.Top, 0), Rule("TTopDefault"));
        Assert.Equal(new BaselineRule(BaselineAlign.Top, 255), Rule("TTopMax"));
        Assert.Equal(new BaselineRule(BaselineAlign.Center), Rule("TCenter"));

        // Inherited when omitted (the nearest ancestor's), whatever else the class declares; a descendant overrides.
        Assert.Equal(Rule("TTop"), Rule("TTopChild"));
        Assert.Equal(Rule("TTop"), Rule("TTopGrandChild"));
        Assert.Equal("edit", result.Catalog.Find("TTopGrandChild")!.RendererId);
        Assert.Equal(new BaselineRule(BaselineAlign.Center), Rule("TOverride"));
        Assert.Equal(new BaselineRule(BaselineAlign.Center), Rule("TOverrideChild"));
        Assert.Equal(new BaselineRule(BaselineAlign.None), Rule("TSuppressed"));
    }

    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-210")]
    [InlineData("5")]                                               // a number
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("[\"top\"]")]
    [InlineData("\"top\"")]                                         // the only string is "none"
    [InlineData("\"None\"")]
    [InlineData("{ \"align\": \"bottom\" }")]                       // unknown align
    [InlineData("{ \"align\": 1 }")]
    [InlineData("{ }")]                                             // no align
    [InlineData("{ \"inset\": 3 }")]
    [InlineData("{ \"align\": \"top\", \"inset\": 300 }")]          // inset out of range
    [InlineData("{ \"align\": \"top\", \"inset\": -1 }")]
    [InlineData("{ \"align\": \"top\", \"inset\": 1.5 }")]
    [InlineData("{ \"align\": \"top\", \"inset\": \"3\" }")]
    [InlineData("{ \"align\": \"top\", \"inset\": 3, \"extra\": 1 }")] // an unknown member
    [InlineData("{ \"align\": \"center\", \"inset\": 3 }")]         // center has no inset
    public void BaselineKey_InvalidValueIsAProblemAndIgnored(string value)
    {
        var result = LoadBaselines($$"""
            { "class": "TBase", "baseline": { "align": "top", "inset": 2 } },
            { "class": "TBroken", "ancestor": "TBase", "renderer": "edit",
              "baseline": {{value}} },
            { "class": "TBrokenChild", "ancestor": "TBroken" },
            { "class": "TAlone", "baseline": {{value}} },
            { "class": "TFine", "ancestor": "TBase", "baseline": "none" }
            """);

        // One problem per class with the invalid value, naming the file, the class and the key, at the class's place.
        Assert.Equal(2, result.Problems.Length);
        Assert.All(result.Problems, p => Assert.Equal(@"C:\ui\Base.components.json", p.File));
        Assert.Equal(["invalid \"baseline\" of TBroken (ignored)", "invalid \"baseline\" of TAlone (ignored)"], result.Problems.Select(p => p.Message));
        Assert.Equal([3, 6], result.Problems.Select(p => p.Line));

        // The class is loaded and behaves as if the key were absent: it inherits, and so do its descendants.
        var broken = result.Catalog.Find("TBroken")!;
        Assert.Equal("edit", broken.RendererId);
        Assert.Equal(new BaselineRule(BaselineAlign.Top, 2), broken.Baseline);
        Assert.Equal(new BaselineRule(BaselineAlign.Top, 2), result.Catalog.Find("TBrokenChild")!.Baseline);
        Assert.Null(result.Catalog.Find("TAlone")!.Baseline);
        Assert.Equal(new BaselineRule(BaselineAlign.None), result.Catalog.Find("TFine")!.Baseline);
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-209")]
    [Trait("Req", "DSG-SUR-211")]
    public void BaselineKey_IsDesignOnlyAndNotInTheBuiltInMetadata()
    {
        // DSG-SUR-211: the key adds nothing a class publishes — with it or without it the class has the same
        // properties, events and other values; only the rule differs. (Nothing in Pascal source corresponds to it.)
        const string Members = """ "kind": "control", "properties": [ { "name": "Left", "type": "Integer", "default": 0 } ], "events": [ { "name": "OnClick", "signature": "TNotifyEvent" } ] """;
        var with = LoadBaselines("{ \"class\": \"TGauge\", \"baseline\": { \"align\": \"center\" }," + Members + "}");
        var without = LoadBaselines("{ \"class\": \"TGauge\"," + Members + "}");
        Assert.Equal(without.Problems, with.Problems);
        var (a, b) = (with.Catalog.Find("TGauge")!, without.Catalog.Find("TGauge")!);
        Assert.Equal(b.Properties.Select(p => p.Name), a.Properties.Select(p => p.Name));
        Assert.Equal(b.Events, a.Events);
        Assert.Equal(b, a with { Baseline = null, Properties = b.Properties, Events = b.Events, ChildClasses = b.ChildClasses,
            ParentClasses = b.ParentClasses, DesignerVerbs = b.DesignerVerbs });
        Assert.Null(b.Baseline);

        // DSG-SUR-209: the built-in metadata does not use the key (its renderers give the baselines).
        foreach (var area in (string[])["ui-framework", "data"])
        {
            var folder = Path.Combine(Layout.LayoutHarness.RepoRoot, area, "design");
            var files = Directory.GetFiles(folder, "*.json");
            Assert.NotEmpty(files);
            Assert.All(files, f => Assert.DoesNotContain("\"baseline\"", File.ReadAllText(f), StringComparison.Ordinal));
        }

        Assert.All(Layout.LayoutHarness.Catalog.All, c => Assert.Null(c.Baseline));
    }
}
