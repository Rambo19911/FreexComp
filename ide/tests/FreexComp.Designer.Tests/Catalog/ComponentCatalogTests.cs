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
}
