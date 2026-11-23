using FreexComp.Designer.Editing;
using FreexComp.Designer.Inspector;
using FreexComp.Designer.Model;
using FreexComp.Designer.Text;

namespace FreexComp.Designer.Tests.Inspector;

/// <summary>Rows of preserved components and unknown properties, and rows re-read after a change (object-inspector.md §1, §9; D-087).</summary>
[Trait("Phase", "5")]
public sealed class PropertyGridRuleTests
{
    private const string Text = """
        object F: TForm
          ClientWidth = 400
          ClientHeight = 300
          object X: TFancyWidget
            Left = 10
            Top = 10
            Width = 50
            Height = 30
            Glow = 'blue'
            Shade.Depth = 7
            object Child: TButton
              Left = 1
              Top = 1
              Width = 10
              Height = 10
            end
          end
          object B: TButton
            Left = 100
            Top = 10
            Width = 75
            Height = 25
            Caption = 'Go'
            Sparkle = 42
            Halo.Size = 3
            TabOrder = 0
          end
        end

        """;

    private static FormDocument Load(string text) =>
        FormParser.Parse(text.Replace("\r\n", "\n"), "rules.form", TestCatalog.Instance).Document!;

    private static PropertyGridModel Grid(FormDocument document, string name)
    {
        var grid = new PropertyGridModel(TestCatalog.Instance);
        grid.SetSelection([document.FindByName(name)!]);
        return grid;
    }

    [Fact]
    [Trait("Req", "DSG-OI-005")]
    public void PreservedComponentShowsNameClassNameAndRawTopLevelProperties()
    {
        var document = Load(Text);
        Assert.True(document.FindByName("X")!.IsPreserved);
        var grid = Grid(document, "X");
        var rows = grid.Rows.Where(r => r.Kind == PropertyRowKind.Property).ToList();

        // Name, the read-only pseudo row ClassName and, read-only, every property found at its top level with its raw text.
        Assert.Contains(rows, r => r is { Path: "Name", Source: PropertyRowSource.Name, Display: "X" });
        Assert.Contains(rows, r => r is { Path: "ClassName", Source: PropertyRowSource.ClassName, Display: "TFancyWidget" });
        Assert.Contains(rows, r => r is { Path: "Glow", Source: PropertyRowSource.Unknown, Display: "'blue'" });
        Assert.Contains(rows, r => r is { Path: "Left", Display: "10" });
        Assert.Contains(rows, r => r is { Path: "Shade.Depth", Display: "7" });
        Assert.All(rows.Where(r => r.Source != PropertyRowSource.Name), r => Assert.True(r.IsReadOnly, r.Path));
        Assert.DoesNotContain(rows, r => r.Path.StartsWith("Child", StringComparison.Ordinal)); // nested objects are not properties
    }

    [Fact]
    [Trait("Req", "DSG-OI-006")]
    public void UnknownPropertiesOfAKnownComponentComeLastInCategoryUnknown()
    {
        var document = Load(Text);
        var grid = Grid(document, "B");
        var categories = grid.Rows.Where(r => r.Kind == PropertyRowKind.Category).Select(r => r.Name).ToList();
        Assert.Equal(PropertyGridModel.UnknownCategory, categories[^1]);
        Assert.True(categories.IndexOf("Misc") < categories.IndexOf("Unknown"));

        var unknown = grid.Rows.SkipWhile(r => r is not { Kind: PropertyRowKind.Category, Name: "Unknown" }).Skip(1).ToList();
        Assert.Equal(["Halo.Size", "Sparkle"], unknown.Select(r => r.Path)); // by name inside the category
        Assert.Equal(["3", "42"], unknown.Select(r => r.Display));
        Assert.All(unknown, r => Assert.True(r is { IsReadOnly: true, Source: PropertyRowSource.Unknown }, r.Path));

        // Known properties stay in their categories and stay editable.
        var caption = grid.Rows.Single(r => r.Path == "Caption");
        Assert.Equal("Appearance", caption.Category);
        Assert.False(caption.IsReadOnly);

    }

    [Fact]
    [Trait("Req", "DSG-OI-094")]
    public void RowsAreReReadAfterACommitThatChangesOtherRows()
    {
        var document = Load("""
            object F: TForm
              ClientWidth = 400
              ClientHeight = 300
              object P: TPanel
                Left = 10
                Top = 20
                Width = 100
                Height = 50
                TabOrder = 0
              end
            end

            """);
        var grid = Grid(document, "P");
        string Display(string path) => grid.Rows.Single(r => r.Path == path).Display;
        Assert.Equal(("10", "100"), (Display("Left"), Display("Width")));

        // Align = alTop (committed with the bounds the layout gives it): Left, Top and Width rows change without a rebuild.
        var editor = new FormEditor(document, TestCatalog.Instance);
        editor.Apply([
            new SetProperty("P", "Align", new IdentValue("alTop")),
            new SetProperty("P", "Left", new IntegerValue(0, false)),
            new SetProperty("P", "Top", new IntegerValue(0, false)),
            new SetProperty("P", "Width", new IntegerValue(400, false)),
        ]);
        Assert.Equal(("alTop", "0", "0", "400"), (Display("Align"), Display("Left"), Display("Top"), Display("Width")));
    }
}
