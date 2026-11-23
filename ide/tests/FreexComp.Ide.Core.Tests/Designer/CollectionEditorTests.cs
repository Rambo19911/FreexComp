using System.Text;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Inspector;
using FreexComp.Designer.Model;
using FreexComp.Designer.Surface;
using FreexComp.Designer.Text;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Tests.Designer.CodeSync;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Designer;

/// <summary>The collection editor and its embedded grid (object-inspector.md §5.7, DSG-OI-110 … 116; D-096).</summary>
[Trait("Phase", "5")]
public sealed class CollectionEditorTests
{
    /// <summary>A status bar with two panels: the first with text, the second with an unknown property and hex text.</summary>
    private const string Form = """
        object MainForm: TMainForm
          Caption = 'MainForm'
          ClientWidth = 400
          ClientHeight = 300
          object StatusBar1: TStatusBar
            Left = 0
            Top = 281
            Width = 400
            Height = 19
            Panels = <
              item
                Text = 'Ready'
                Width = 120
              end
              item
                Mystery = 'kept'
                Width = $60
                Bevel = pbRaised
              end
            >
          end
        end

        """;

    private static FormDocument Parse(string text = Form) =>
        FormParser.Parse(text, "MainForm.form", CodeSyncTests.Catalog).Document ?? throw new InvalidOperationException("did not parse");

    private static CollectionEditorViewModel Editor(FormDocument document) =>
        new(CodeSyncTests.Catalog, document, document.FindByName("StatusBar1")!, "Panels");

    /// <summary>Edits the grid's current row as text (Enter).</summary>
    private static bool Enter(PropertyGridViewModel grid, string path, string text)
    {
        grid.SetCurrent(path);
        Assert.True(grid.BeginEdit());
        grid.EditText = text;
        return grid.Commit();
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-111")]
    [Trait("Req", "DSG-OI-112")]
    public void AddDeleteMoveItems()
    {
        var editor = Editor(Parse());
        Assert.Equal("Edit StatusBar1.Panels", editor.Title);
        Assert.Equal(["0 – Ready", "1 – StatusPanel"], editor.ItemTexts);
        Assert.Equal([0], editor.SelectedIndices); // the first item is selected
        Assert.False(editor.CanMoveUp);
        Assert.True(editor.CanMoveDown);

        // Add appends a default item and selects it.
        editor.Add();
        Assert.Equal(["0 – Ready", "1 – StatusPanel", "2 – StatusPanel"], editor.ItemTexts);
        Assert.Equal([2], editor.SelectedIndices);
        Assert.Empty(editor.Model.Items[2].Properties);
        Assert.True(editor.CanMoveUp);
        Assert.False(editor.CanMoveDown);

        // Move Up / Move Down move the one selected item by one position.
        var added = editor.Model.Items[2];
        editor.MoveUp();
        Assert.Equal([1], editor.SelectedIndices);
        Assert.Same(added, editor.Model.Items[1]);
        editor.MoveUp();
        Assert.Equal(["0 – StatusPanel", "1 – Ready", "2 – StatusPanel"], editor.ItemTexts);
        Assert.False(editor.CanMoveUp);
        editor.MoveUp(); // cannot act
        Assert.Same(added, editor.Model.Items[0]);
        editor.MoveDown();
        Assert.Same(added, editor.Model.Items[1]);

        // Multi-selection: Delete only; it removes all and selects the following item (else the previous).
        editor.Select([0, 1]);
        Assert.True(editor.CanDelete);
        Assert.False(editor.CanMoveUp);
        Assert.False(editor.CanMoveDown);
        editor.Delete();
        Assert.Equal(["0 – StatusPanel"], editor.ItemTexts);
        Assert.Equal([0], editor.SelectedIndices);
        editor.Add();
        editor.Delete(); // the last item: the previous one is selected
        Assert.Equal([0], editor.SelectedIndices);
        editor.Delete();
        Assert.True(editor.IsEmpty);
        Assert.Empty(editor.SelectedIndices);
        Assert.False(editor.CanDelete);
        Assert.Equal("No items. Choose Add to create one.", editor.EmptyText);

        // A middle item: the following one takes its place and is selected.
        var middle = Editor(Parse());
        middle.Add();
        var following = middle.Model.Items[2];
        middle.Select([1]);
        middle.Delete();
        Assert.Equal([1], middle.SelectedIndices);
        Assert.Same(following, middle.Model.Items[1]);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-113")]
    [Trait("Req", "UI-DLG-241")]
    public void EmbeddedGridEditsItemProperties()
    {
        var document = Parse();
        var editor = Editor(document);
        var grid = editor.Grid;

        // Declaration order, no Name row, no categories; bold = stored.
        Assert.Equal(["Alignment", "Bevel", "Text", "Width"], grid.Rows.Select(r => r.Path));
        Assert.All(grid.Rows, r => Assert.Equal(PropertyRowKind.Property, r.Kind));
        Assert.Equal([false, false, true, true], grid.Rows.Select(r => r.IsBold));
        Assert.Equal(["Ready", "120"], grid.Rows.Skip(2).Select(r => r.Display));

        // The Object Inspector's converters: an invalid integer keeps the editor open with the message, nothing applied.
        Assert.False(Enter(grid, "Width", "wide"));
        Assert.True(grid.IsEditing);
        Assert.Equal(DesignerMessages.Format(ValueMessages.InvalidExpression), grid.EditError);
        grid.Cancel();
        Assert.True(Enter(grid, "Width", "80"));
        Assert.Equal("80", grid.Rows.Single(r => r.Path == "Width").Display);

        // The list text follows the grid (UI-DLG-241); an empty text falls back to the class name.
        Assert.True(Enter(grid, "Text", "Busy"));
        Assert.Equal("0 – Busy", editor.ItemTexts[0]);
        Assert.True(Enter(grid, "Text", ""));
        Assert.Equal("0 – StatusPanel", editor.ItemTexts[0]);

        // Enum values cycle; reset to default removes the stored value.
        grid.SetCurrent("Alignment");
        Assert.True(grid.CycleValue());
        Assert.Equal("taRightJustify", grid.Rows.Single(r => r.Path == "Alignment").Display);
        Assert.Equal(["taLeftJustify", "taRightJustify", "taCenter"], grid.ValueList("Alignment"));
        Assert.True(grid.CanResetToDefault);
        grid.ResetToDefault();
        Assert.False(grid.Rows.Single(r => r.Path == "Alignment").IsBold);

        // The other item: its own values; several items selected: an empty grid.
        editor.Select([1]);
        Assert.Equal("pbRaised", grid.Rows.Single(r => r.Path == "Bevel").Display);
        Assert.Equal("96", grid.Rows.Single(r => r.Path == "Width").Display);
        editor.Select([0, 1]);
        Assert.Empty(grid.Rows);

        // The form is not touched while the dialog is open (DSG-OI-116).
        Assert.Equal(new StringValue("Ready"), ((CollectionValue)document.FindByName("StatusBar1")!.FindProperty("Panels")!.Value).Items[0].Properties[0].Value);

        // A nested collection row inside the grid is read-only.
        var nested = NestedCatalog();
        var nestedDocument = FormParser.Parse("object F: TForm\n  object H: THolder\n    Outer = <\n      item\n      end\n    >\n  end\nend\n", "f.form", nested).Document!;
        var holder = new CollectionEditorViewModel(nested, nestedDocument, nestedDocument.FindByName("H")!, "Outer");
        var inner = holder.Grid.Rows.Single(r => r.Path == "Inner");
        Assert.True(inner.IsReadOnly);
        Assert.Equal(["Caption", "Inner"], holder.Grid.Rows.Select(r => r.Path));
        holder.Grid.SetCurrent("Inner");
        Assert.False(holder.Grid.BeginEdit());
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-114")]
    [Trait("Req", "DSG-OI-116")]
    [Trait("Req", "DSG-OI-110")]
    public Task OkIsOneUndoUnitCancelDiscards() => TestUiThread.RunAsync(OkIsOneUndoUnitCancelDiscardsOnUiThread);

    private static async Task OkIsOneUndoUnitCancelDiscardsOnUiThread()
    {
        var dialogs = new FakeDialogService();
        var f = new DesignerFixture(o => o.Dialogs = dialogs);
        var unit = DesignerFixture.Unit.Replace("    Panel1: TPanel;", "    Panel1: TPanel;\r\n    StatusBar1: TStatusBar;", StringComparison.Ordinal)
            .Replace("StdCtrls, ExtCtrls;", "StdCtrls, ExtCtrls, ComCtrls;", StringComparison.Ordinal);
        var form = DesignerFixture.Form[..DesignerFixture.Form.TrimEnd().LastIndexOf("end", StringComparison.Ordinal)]
            + "  object StatusBar1: TStatusBar\n    Left = 0\n    Top = 281\n    Width = 400\n    Height = 19\n    Panels = <\n      item\n        Text = 'Ready'\n      end\n    >\n  end\nend\n";
        var document = await f.OpenAsync(unit, form);
        var designer = document.Designer;
        var inspector = new ObjectInspectorViewModel(f.Services.Status, null, dialogs);
        inspector.Bind(designer);
        DesignerFixture.Select(designer, "StatusBar1");
        inspector.CurrentPath = "Panels";
        Assert.Equal("(1 item)", inspector.Rows.Single(r => r.Path == "Panels").Display);
        Assert.True(inspector.CanEditCollection);
        CollectionValue Panels() => (CollectionValue)DesignerFixture.Object(designer, "StatusBar1").FindProperty("Panels")!.Value;

        // OK after Add: one unit "Property"; the form is unchanged while the dialog is open.
        dialogs.Script(CollectionEditorViewModel.DialogName, async dialog =>
        {
            var editor = Assert.IsType<CollectionEditorViewModel>(dialog);
            Assert.Equal("Edit StatusBar1.Panels", editor.Title);
            editor.Add();
            Assert.True(Enter(editor.Grid, "Text", "Ln 1"));
            Assert.Single(Panels().Items);
            await editor.PressDefaultAsync();
        });
        await inspector.ActivateCurrentAsync(); // Enter on the row
        Assert.Equal(2, Panels().Items.Length);
        Assert.Equal((1, UndoTitles.Property), (designer.Undo.UndoCount, designer.Undo.UndoTitleKey));
        Assert.Equal("(2 items)", inspector.Rows.Single(r => r.Path == "Panels").Display);

        // Cancel discards; OK without a change adds no unit.
        dialogs.Script(CollectionEditorViewModel.DialogName, async dialog =>
        {
            var editor = (CollectionEditorViewModel)dialog;
            editor.Delete();
            await editor.PressCancelAsync();
        });
        await inspector.EditCollectionAsync();
        dialogs.Script(CollectionEditorViewModel.DialogName, dialog => ((CollectionEditorViewModel)dialog).PressDefaultAsync());
        await inspector.EditCollectionAsync();
        Assert.Equal(2, Panels().Items.Length);
        Assert.Equal(1, designer.Undo.UndoCount);

        // Undo restores the original collection.
        designer.Surface!.Execute("edit.undo");
        Assert.Single(Panels().Items);

        // Not for a multi-selection.
        DesignerFixture.Select(designer, "Panel1", "StatusBar1");
        inspector.CurrentPath = "Panels";
        Assert.False(inspector.CanEditCollection);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-115")]
    [Trait("Req", "DSG-OI-114")]
    public void WritesCanonicalCollectionAndRemovesEmpty()
    {
        var document = Parse();
        var editor = Editor(document);
        Assert.True(Enter(editor.Grid, "Width", "50")); // the default: not written
        Assert.True(Enter(editor.Grid, "Alignment", "taCenter"));
        editor.Add();
        Assert.True(Enter(editor.Grid, "Width", "75"));
        Assert.True(Enter(editor.Grid, "Text", "Ln 1, Col 1"));
        editor.Model.Move(2, -1);
        Apply(document, editor);
        var golden = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "forms", "canonical", "collection-edited.form"));
        Assert.Equal(golden.ReplaceLineEndings("\n"), FormWriter.Write(document));

        // An emptied collection removes the property (empty = default).
        editor = Editor(document);
        while (!editor.IsEmpty)
        {
            editor.Delete();
        }

        Assert.IsType<RemoveProperty>(editor.Model.CommitChange());
        Apply(document, editor);
        Assert.Null(document.FindByName("StatusBar1")!.FindProperty("Panels"));
        Assert.DoesNotContain("Panels", FormWriter.Write(document), StringComparison.Ordinal);

        // A property that was never stored and stays empty: no change.
        Assert.Null(Editor(document).Model.CommitChange());
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-115")]
    public void PreservesUnknownItemProperties()
    {
        var document = Parse();
        var editor = Editor(document);
        // Known item properties in declaration order, the unknown one after them, values as read.
        Assert.Contains("      item\n        Bevel = pbRaised\n        Width = $60\n        Mystery = 'kept'\n      end\n",
            FormWriter.Write(Single(editor, 1)), StringComparison.Ordinal);
        editor.Select([1]);
        Assert.Contains(editor.Grid.Rows, r => r.Path == "Mystery" && r.Source == PropertyRowSource.Unknown && r.IsReadOnly);

        // Editing the first item keeps the second untouched: original value text ($00000060) and the unknown property.
        editor.Select([0]);
        Assert.True(Enter(editor.Grid, "Text", "Go"));
        Apply(document, editor);
        var text = FormWriter.Write(document);
        Assert.Contains("      item\n        Bevel = pbRaised\n        Width = $60\n        Mystery = 'kept'\n      end\n", text, StringComparison.Ordinal);

        // Editing the item itself keeps its unknown property after the known ones.
        editor = Editor(document);
        editor.Select([1]);
        Assert.True(Enter(editor.Grid, "Text", "Second"));
        Apply(document, editor);
        Assert.Contains("      item\n        Bevel = pbRaised\n        Text = 'Second'\n        Width = $60\n        Mystery = 'kept'\n      end\n",
            FormWriter.Write(document), StringComparison.Ordinal);
    }

    /// <summary>A form whose status bar holds only item <paramref name="index"/> of the editor's working copy.</summary>
    private static FormDocument Single(CollectionEditorViewModel editor, int index)
    {
        var document = Parse();
        new FormEditor(document, CodeSyncTests.Catalog).Apply(
            new SetProperty("StatusBar1", "Panels", new CollectionValue([editor.Model.ToValue().Items[index]])));
        return document;
    }

    private static void Apply(FormDocument document, CollectionEditorViewModel editor)
    {
        var change = editor.Model.CommitChange();
        Assert.NotNull(change);
        new FormEditor(document, CodeSyncTests.Catalog).Apply(change);
    }

    /// <summary>A holder whose collection items have a nested collection.</summary>
    private static ComponentCatalog NestedCatalog()
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [@"C:\n\index.json"] = """{ "formatVersion": 1, "files": ["N.components.json"] }""",
            [@"C:\n\N.components.json"] = """
                {
                  "formatVersion": 1,
                  "unit": "N",
                  "types": {
                    "TInnerItem": { "kind": "object", "properties": [ { "name": "Value", "type": "Integer", "default": 0 } ] },
                    "TOuterItem": { "kind": "object", "properties": [
                      { "name": "Caption", "type": "string", "default": "" },
                      { "name": "Inner", "type": "TInnerItems", "kind": "collection", "itemClass": "TInnerItem" }
                    ] }
                  },
                  "components": [
                    { "class": "TForm", "kind": "form", "acceptsChildren": true },
                    { "class": "THolder", "kind": "nonvisual",
                      "properties": [ { "name": "Outer", "type": "TOuterItems", "kind": "collection", "itemClass": "TOuterItem" } ] }
                  ]
                }
                """,
        };
        var result = CatalogLoader.Load([@"C:\n\index.json"], path => files.TryGetValue(path, out var t) ? Encoding.UTF8.GetBytes(t) : null);
        Assert.Empty(result.Problems);
        return result.Catalog;
    }
}
