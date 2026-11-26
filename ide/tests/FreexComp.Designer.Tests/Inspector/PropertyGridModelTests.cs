using FreexComp.Designer.Inspector;
using FreexComp.Designer.Model;
using FreexComp.Designer.Text;

namespace FreexComp.Designer.Tests.Inspector;

/// <summary>The Object Inspector's property-grid model (object-inspector.md §1, §2, §3, §6, §9; D-060).</summary>
[Trait("Phase", "5")]
public sealed class PropertyGridModelTests
{
    private const string MixedForm = """
        object Form1: TForm
          Left = 200
          Top = 120
          ClientWidth = 400
          ClientHeight = 300
          PixelsPerInch = 96
          object Edit1: TEdit
            Left = 8
            Top = 8
            Width = 121
            Height = 23
            TabOrder = 0
          end
          object CheckBox1: TCheckBox
            Left = 8
            Top = 40
            Width = 97
            Height = 17
            Caption = 'Check'
            TabOrder = 1
          end
          object Button1: TButton
            Left = 0
            Top = 72
            Width = 75
            Height = 25
            Anchors = [akLeft, akTop, akRight]
            Caption = 'One'
            Default = True
            Font.Height = -16
            ParentFont = False
            TabOrder = 2
          end
          object Button2: TButton
            Left = 96
            Top = 72
            Width = 75
            Height = 30
            Caption = 'Two'
            TabOrder = 3
          end
          object Button3: TButton
            Left = 96
            Top = 120
            Width = 75
            Height = 25
            TabOrder = 4
          end
        end

        """;

    private static FormDocument Load(string text)
    {
        var result = FormParser.Parse(text.Replace("\r\n", "\n"), "test.form", TestCatalog.Instance);
        Assert.NotNull(result.Document);
        Assert.Empty(result.Document.Diagnostics);
        return result.Document;
    }

    private static FormDocument Hello() => Load(File.ReadAllText(TestPaths.Combine("forms/canonical/hello.form")));

    private static PropertyGridModel Grid(FormDocument document, params string[] names)
    {
        var grid = new PropertyGridModel(TestCatalog.Instance);
        grid.SetSelection([.. names.Select(n => document.FindByName(n)!)]);
        return grid;
    }

    private static PropertyRow Row(PropertyGridModel grid, string path) =>
        grid.Rows.Single(r => r.Kind == PropertyRowKind.Property && string.Equals(r.Path, path, StringComparison.Ordinal));

    private static List<string> Categories(PropertyGridModel grid) =>
        [.. grid.Rows.Where(r => r.Kind == PropertyRowKind.Category).Select(r => r.Name)];

    private static List<string> Paths(PropertyGridModel grid) =>
        [.. grid.Rows.Where(r => r.Kind == PropertyRowKind.Property).Select(r => r.Path)];

    [Fact, Trait("Req", "DSG-OI-004"), Trait("Req", "DSG-OI-010"), Trait("Req", "DSG-OI-011"), Trait("Req", "DSG-OI-013"), Trait("Req", "DSG-OI-014")]
    public void RowsFromMetadataCategorisedAndByName()
    {
        var document = Hello();
        var grid = Grid(document, "GreetButton");

        // Category arrangement: fixed category order, empty categories omitted, rows by name inside.
        Assert.Equal(["Appearance", "Behavior", "Data", "Layout", "Misc"], Categories(grid));
        Assert.Equal(PropertyRowKind.Category, grid.Rows[0].Kind);
        var appearance = grid.Rows.Skip(1).TakeWhile(r => r.Kind == PropertyRowKind.Property).Select(r => r.Path).ToList();
        Assert.Equal(["Caption", "Cursor", "Font", "ParentFont", "WordWrap"], appearance);
        // Misc holds Name and TControl's accessibility properties (U-155), by name like every category.
        var misc = grid.Rows.SkipWhile(r => !(r.Kind == PropertyRowKind.Category && r.Name == "Misc")).Skip(1)
            .TakeWhile(r => r.Kind == PropertyRowKind.Property).Select(r => r.Path).ToList();
        Assert.Equal(["AccessibleDescription", "AccessibleName", "AccessibleRole", "Name"], misc);
        Assert.Equal("arDefault", Row(grid, "AccessibleRole").Display);
        Assert.Equal("GreetButton", Row(grid, "Name").Display);
        Assert.Equal("&Greet", Row(grid, "Caption").Display);
        Assert.Equal("(TFont)", Row(grid, "Font").Display);
        Assert.True(Row(grid, "Font").IsExpandable);
        Assert.False(Row(grid, "Font").IsExpanded);
        Assert.DoesNotContain("Font.Height", Paths(grid));

        // Name arrangement: no category rows, everything by name.
        grid.Arrangement = PropertyArrangement.Name;
        Assert.Empty(Categories(grid));
        var byName = Paths(grid);
        Assert.Equal(byName.Order(StringComparer.OrdinalIgnoreCase), byName);
        Assert.Contains("Name", byName);
        Assert.Contains("Tag", byName);

        // Sub-rows in declaration order, one level deeper; set members as Boolean sub-rows.
        grid.ExpandedPaths.Add("Font");
        grid.ExpandedPaths.Add("Anchors");
        var rows = grid.Rows.ToList();
        var font = rows.FindIndex(r => r.Path == "Font");
        Assert.True(rows[font].IsExpanded);
        Assert.Equal(["Font.Charset", "Font.Color", "Font.Height", "Font.Name", "Font.Orientation", "Font.Pitch", "Font.Quality", "Font.Style", "Font.Size"],
            rows.Skip(font + 1).Take(9).Select(r => r.Path));
        Assert.All(rows.Skip(font + 1).Take(9), r => Assert.Equal(1, r.Depth));
        Assert.Equal("Size", Row(grid, "Font.Size").Name);
        var anchors = rows.FindIndex(r => r.Path == "Anchors");
        Assert.Equal("[akLeft, akTop]", rows[anchors].Display);
        Assert.Equal(["Anchors.akLeft", "Anchors.akTop", "Anchors.akRight", "Anchors.akBottom"], rows.Skip(anchors + 1).Take(4).Select(r => r.Path));
        Assert.Equal(["True", "True", "False", "False"], rows.Skip(anchors + 1).Take(4).Select(r => r.Display));

        // Collapsed category: its header stays, its rows go.
        grid.Arrangement = PropertyArrangement.Category;
        grid.CollapsedCategories.Add("Appearance");
        Assert.Contains("Appearance", Categories(grid));
        Assert.DoesNotContain("Caption", Paths(grid));
        Assert.False(grid.Rows.Single(r => r.Kind == PropertyRowKind.Category && r.Name == "Appearance").IsExpanded);

        // The root: Left/Top and ClientWidth/ClientHeight, never Width/Height; hidden properties are not shown.
        var root = Grid(document, "MainForm");
        var rootPaths = Paths(root);
        Assert.Contains("Left", rootPaths);
        Assert.Contains("Top", rootPaths);
        Assert.Contains("ClientWidth", rootPaths);
        Assert.Contains("ClientHeight", rootPaths);
        Assert.DoesNotContain("Width", rootPaths);
        Assert.DoesNotContain("Height", rootPaths);
        Assert.DoesNotContain("Align", rootPaths);
        Assert.DoesNotContain("Anchors", rootPaths);
    }

    [Fact, Trait("Req", "DSG-OI-020")]
    public void FilterMatchesPathSubstring()
    {
        var grid = Grid(Hello(), "GreetButton");

        grid.Filter = "SIZE";
        Assert.Equal(["Appearance"], Categories(grid));
        Assert.Equal(["Font", "Font.Size"], Paths(grid));
        Assert.True(Row(grid, "Font").IsExpanded); // forced open for the match …
        Assert.Empty(grid.ExpandedPaths);           // … without changing the remembered state
        Assert.False(grid.IsFilterEmpty);

        grid.Filter = "font";
        Assert.Equal(["Font", "Font.Charset", "Font.Color", "Font.Height", "Font.Name", "Font.Orientation", "Font.Pitch", "Font.Quality",
            "Font.Style", "Font.Style.fsBold", "Font.Style.fsItalic", "Font.Style.fsUnderline", "Font.Style.fsStrikeOut", "Font.Size", "ParentFont"],
            Paths(grid));
        Assert.Equal(2, Row(grid, "Font.Style.fsBold").Depth);

        grid.Filter = "anchors.akr";
        Assert.Equal(["Layout"], Categories(grid));
        Assert.Equal(["Anchors", "Anchors.akRight"], Paths(grid));

        grid.CollapsedCategories.Add("Layout");
        Assert.Equal(["Anchors", "Anchors.akRight"], Paths(grid)); // a match shows through a collapsed category

        grid.Filter = "zzz";
        Assert.Empty(grid.Rows);
        Assert.True(grid.IsFilterEmpty);

        grid.Filter = "";
        Assert.False(grid.IsFilterEmpty);
        Assert.Contains("Caption", Paths(grid));
    }

    [Fact, Trait("Req", "DSG-OI-090"), Trait("Req", "DSG-OI-091"), Trait("Req", "DSG-OI-072")]
    public void MultiSelectionIntersection()
    {
        var document = Load(MixedForm);

        // TEdit.Alignment (TAlignment) and TCheckBox.Alignment (TLeftRight): same name, other type → not common.
        var grid = Grid(document, "Edit1", "CheckBox1");
        var paths = Paths(grid);
        Assert.DoesNotContain("Alignment", paths);
        Assert.DoesNotContain("Caption", paths);   // TEdit has none
        Assert.DoesNotContain("Text", paths);      // TCheckBox has none
        Assert.Contains("Font", paths);
        Assert.Contains("TabStop", paths);
        Assert.Equal("8", Row(grid, "Left").Display);       // equal for both
        Assert.Equal("", Row(grid, "Top").Display);        // 8 vs 40
        Assert.Equal("True", Row(grid, "TabStop").Display); // both default True
        Assert.Equal("", Row(grid, "Name").Display);        // DSG-OI-072
        Assert.True(Row(grid, "Name").IsReadOnly);

        // Booleans that differ show blank and the indeterminate state; categories come from the primary.
        var buttons = Grid(document, "Button2", "Button1");
        Assert.Equal("", Row(buttons, "Default").Display);
        Assert.True(Row(buttons, "Default").IsMixed);
        Assert.False(Row(buttons, "Cancel").IsMixed);
        Assert.Equal("False", Row(buttons, "Cancel").Display);
        Assert.Equal("", Row(buttons, "Caption").Display);
        Assert.Equal("75", Row(buttons, "Width").Display);
        buttons.ExpandedPaths.Add("Font");
        Assert.Equal("", Row(buttons, "Font.Height").Display);
        Assert.Equal("Segoe UI", Row(buttons, "Font.Name").Display);
    }

    [Fact, Trait("Req", "DSG-OI-060"), Trait("Req", "DSG-OI-061"), Trait("Req", "DSG-OI-050"), Trait("Req", "DSG-OI-052")]
    public void BoldRule()
    {
        var document = Load(MixedForm);

        var one = Grid(document, "Button1");
        one.ExpandedPaths.Add("Font");
        one.ExpandedPaths.Add("Anchors");
        Assert.True(Row(one, "Caption").IsBold);          // stored
        Assert.False(Row(one, "Cursor").IsBold);          // default
        Assert.False(Row(one, "Name").IsBold);            // never
        Assert.False(Row(one, "Left").IsBold);            // always written, 0
        Assert.True(Row(one, "Top").IsBold);              // always written, ≠ 0
        Assert.False(Row(one, "Width").IsBold);           // = defaultSize width
        Assert.False(Row(one, "Height").IsBold);          // = defaultSize height
        Assert.True(Row(one, "TabOrder").IsBold);
        Assert.True(Row(one, "Font").IsBold);             // a sub-property is stored
        Assert.True(Row(one, "Font.Height").IsBold);
        Assert.False(Row(one, "Font.Name").IsBold);
        Assert.True(Row(one, "Anchors").IsBold);
        Assert.False(Row(one, "Anchors.akLeft").IsBold);  // member of the default set and of the value
        Assert.True(Row(one, "Anchors.akRight").IsBold);  // differs from the default set's membership
        Assert.False(Row(one, "Anchors.akBottom").IsBold);

        var two = Grid(document, "Button2");
        Assert.True(Row(two, "Left").IsBold);
        Assert.True(Row(two, "Height").IsBold);           // 30 ≠ 25
        Assert.False(Row(two, "Font").IsBold);

        var root = Grid(document, "Form1");
        Assert.True(Row(root, "Left").IsBold);
        Assert.False(Row(root, "ClientWidth").IsBold);    // = defaultSize 400 × 300
        Assert.False(Row(root, "ClientHeight").IsBold);

        // Multi-selection: bold only when bold for every selected component.
        var both = Grid(document, "Button1", "Button2");
        Assert.True(Row(both, "Caption").IsBold);         // stored in both
        Assert.True(Row(both, "Top").IsBold);
        Assert.False(Row(both, "Left").IsBold);           // Button1: 0
        Assert.False(Row(both, "Default").IsBold);        // Button2: default
        var withDefault = Grid(document, "Button1", "Button3");
        Assert.False(Row(withDefault, "Caption").IsBold); // Button3 does not store it
    }

    /// <summary>
    /// UI-TW-304 (I-212): the row key is the property path; a category row's key is <c>$cat.&lt;Category&gt;</c>, so the
    /// category <c>Layout</c> and the property <c>Layout</c> of a label (category Appearance) have different keys.
    /// </summary>
    [Fact]
    [Trait("Req", "UI-TW-304")]
    public void CategoryRowKeysDoNotCollideWithPropertyPaths()
    {
        var document = Load("""
            object Form1: TForm
              ClientWidth = 400
              ClientHeight = 300
              PixelsPerInch = 96
              object Label1: TLabel
                Left = 8
                Top = 8
                Caption = 'Hi'
              end
            end

            """);
        var grid = Grid(document, "Label1");
        grid.ExpandedPaths.Add("Font");
        var rows = grid.Rows;
        var category = Assert.Single(rows, r => r.Kind == PropertyRowKind.Category && r.Name == "Layout");
        Assert.Equal("$cat.Layout", category.Key);
        var property = Row(grid, "Layout");
        Assert.Equal("Layout", property.Key);
        Assert.Equal("Appearance", property.Category);
        Assert.Equal("Font.Size", Row(grid, "Font.Size").Key);
        Assert.Equal(rows.Count, rows.Select(r => r.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(rows.Where(r => r.Kind == PropertyRowKind.Category), r => Assert.Equal(PropertyRow.CategoryKeyPrefix + r.Name, r.Key));
    }
}
