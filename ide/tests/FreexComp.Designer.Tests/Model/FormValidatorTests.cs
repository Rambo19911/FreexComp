using FreexComp.Designer.Catalog;
using FreexComp.Designer.Model;
using FreexComp.Designer.Tests.Catalog;
using FreexComp.Designer.Text;

namespace FreexComp.Designer.Tests.Model;

/// <summary>DSG-FD-070: references, parents and numeric limits (D-018).</summary>
public sealed class FormValidatorTests
{
    private static readonly ComponentCatalog Catalog = TestCatalog.Instance;

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-070")]
    [Trait("Req", "DSG-FD-028")]
    public void ReportsUnresolvedReferencesAndBadParents()
    {
        const string File = "unresolved-reference.form";
        var result = FormParser.Parse(System.IO.File.ReadAllText(TestPaths.Combine("forms/semantic/" + File)), File, Catalog);

        // Parsing runs the validator: its diagnostics are part of the parse result, in source order.
        Assert.Equal(
        [
            "unresolved-reference.form(7,10): warning F0105: Component MissingMenu not found",
            "unresolved-reference.form(15,16): warning F0103: Invalid value for property TabOrder: value out of range -1..32767",
            "unresolved-reference.form(22,17): warning F0103: Invalid value for property PopupMenu: 'NameEdit' is not a TPopupMenu",
            "unresolved-reference.form(24,5): warning F0103: Invalid value for property Parent: TButton cannot be a child of TLabel",
            "unresolved-reference.form(29,19): warning F0105: Component NoSuchPopup not found",
            "unresolved-reference.form(38,18): warning F0103: Invalid value for property BevelWidth: value out of range 1..32767",
            "unresolved-reference.form(39,20): warning F0103: Invalid value for property Font.Charset: value out of range 0..255",
        ], result.Diagnostics.Select(d => d.ToString()));
        var document = result.Document!;
        Assert.Equal(result.Diagnostics, document.Diagnostics);
        Assert.Equal(("Menu", 7, 21), (result.Diagnostics[0].PropertyPath, result.Diagnostics[0].EndLine, result.Diagnostics[0].EndCol));
        Assert.Equal(("MainForm", "InnerButton", "Parent"), (result.Diagnostics[0].ObjectName, result.Diagnostics[3].ObjectName, result.Diagnostics[3].PropertyPath));

        // DSG-FD-028 / DSG-FD-070: nothing is preserved; the values stay as written and editable.
        Assert.All(document.AllObjects(), o => Assert.False(o.IsPreserved));
        Assert.All(document.AllObjects().SelectMany(o => o.Properties), p => Assert.False(p.IsPreserved));
        Assert.Equal(new IdentValue("MissingMenu"), document.Root.FindProperty("Menu")!.Value);
        Assert.Equal(new IntegerValue(-5, false), document.FindByName("NameEdit")!.FindProperty("TabOrder")!.Value);
        Assert.Same(document.FindByName("InfoLabel"), document.FindByName("InnerButton")!.Parent);

        // Validating again replaces the list with the same content (no duplicates).
        FormValidator.Validate(document, Catalog);
        Assert.Equal(result.Diagnostics, document.Diagnostics);

        // childClasses / parentClasses (component-metadata.md §2.1).
        var restricted = RestrictedCatalog();
        var pages = FormParser.Parse("""
            object F: TForm
              object Pages: TPageControl
                object Sheet1: TTabSheet
                end
                object Stray: TButton
                end
              end
              object Orphan: TTabSheet
              end
              object Timer1: TTimer
                object Clock: TLabel
                end
              end
              object Header1: THeader
                Sections = <
                  item
                    Width = -1
                    Partner = Nowhere
                  end
                  item
                    Width = 10
                    Partner = Header1
                  end
                >
              end
            end
            """, "pages.form", restricted);
        Assert.Equal(
        [
            "pages.form(5,5): warning F0103: Invalid value for property Parent: TButton cannot be a child of TPageControl",
            "pages.form(8,3): warning F0103: Invalid value for property Parent: TTabSheet cannot be a child of TForm",
            "pages.form(11,5): warning F0103: Invalid value for property Parent: TLabel cannot be a child of TTimer",
            "pages.form(15,16): warning F0103: Invalid value for property Sections.Width: value out of range 0..2147483647",
            "pages.form(15,16): warning F0105: Component Nowhere not found",
        ], pages.Diagnostics.Select(d => d.ToString()));
        Assert.Equal("Sections.Width", pages.Diagnostics[3].PropertyPath);

        // Parse diagnostics and validation diagnostics are merged in source order; preserved objects are not validated.
        var mixed = FormParser.Parse("""
            object F: TForm
              Menu = Nowhere
              object X: TUnknown
                Menu = Nowhere
              end
              object B: TButton
                Flat = True
                TabOrder = -2
              end
            end
            """, "mixed.form", Catalog);
        Assert.Equal(["F0105", "F0101", "F0102", "F0103"], mixed.Diagnostics.Select(d => d.Code));
    }

    /// <summary>The test metadata plus a page control that takes only tab sheets and a tab sheet that needs one.</summary>
    internal static ComponentCatalog RestrictedCatalog()
    {
        var files = new CatalogLoaderTests.MemoryFiles
        {
            [@"C:\extra\index.json"] = """{ "formatVersion": 1, "files": ["Pages.components.json"] }""",
            [@"C:\extra\Pages.components.json"] = """
                {
                  "formatVersion": 1,
                  "unit": "ComCtrls",
                  "types": {
                    "THeaderSection": { "kind": "object", "properties": [
                      { "name": "Width", "type": "Integer", "default": 50, "min": 0 },
                      { "name": "Partner", "type": "THeader", "kind": "component" }
                    ] }
                  },
                  "components": [
                    { "class": "THeader", "ancestor": "TWinControl", "kind": "wincontrol", "defaultSize": { "width": 200, "height": 20 },
                      "properties": [
                        { "name": "Sections", "type": "THeaderSections", "kind": "collection", "itemClass": "THeaderSection", "category": "Data" }
                      ] },
                    { "class": "TPageControl", "ancestor": "TWinControl", "kind": "container", "acceptsChildren": true,
                      "defaultSize": { "width": 289, "height": 193 }, "childClasses": ["TTabSheet"] },
                    { "class": "TTabSheet", "ancestor": "TWinControl", "kind": "container", "acceptsChildren": true,
                      "defaultSize": { "width": 281, "height": 165 }, "parentClasses": ["TPageControl"] }
                  ]
                }
                """,
        };
        var result = CatalogLoader.Load([TestPaths.MetadataIndex, @"C:\extra\index.json"],
            path => files.Read(path) ?? (System.IO.File.Exists(path) ? System.IO.File.ReadAllBytes(path) : null));
        Assert.Empty(result.Problems);
        return result.Catalog;
    }
}
