using System.Text;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Inspector;
using FreexComp.Designer.Model;
using FreexComp.Designer.Surface;
using FreexComp.Designer.Text;
using DesignCommandIds = FreexComp.Designer.Surface.Commands;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Documents;
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

    /// <summary>
    /// D-100 (DAT-337): the generic collection editor and the <c>editColumns</c> verb on <c>TDBGrid.Columns</c> with the
    /// shipped metadata (the framework root, then <c>data/design</c>); a missing data root fails the test.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Phase", "7")]
    [Trait("Req", "DAT-337")]
    [Trait("Req", "DSG-OI-110")]
    public Task EditsDbGridColumnsFromDataMetadata() => TestUiThread.RunAsync(EditsDbGridColumnsFromDataMetadataOnUiThread);

    private static async Task EditsDbGridColumnsFromDataMetadataOnUiThread()
    {
        Assert.True(File.Exists(Path.Combine(DataDesignTests.DataDesignDir, "index.json")), "data metadata not found: " + DataDesignTests.DataDesignDir);
        var catalog = DataDesignTests.Catalog;

        // The metadata: a collection property with the item class TColumn and the verb that edits it.
        var grid = catalog.Find("TDBGrid")!;
        var columns = grid.FindProperty("Columns")!;
        Assert.Equal((PropertyKind.Collection, "TColumn"), (columns.Kind, columns.ItemClass));
        var verb = Assert.Single(grid.DesignerVerbs);
        Assert.Equal(("editColumns", "&Columns Editor…", "designer.editCollection", "Columns"), (verb.Id, verb.Caption, verb.Command, verb.Arg));

        var dialogs = new FakeDialogService();
        var f = new DesignerFixture(o => o.Dialogs = dialogs);
        ((DocumentService)f.Services.Documents).DesignCatalog = catalog;
        var unit = DataDesignTests.EmptyUnit
            .Replace("  Classes, Controls, Forms;", "  Classes, Controls, Forms, DB, DBGrids;", StringComparison.Ordinal)
            .Replace("  TMainForm = class(TForm)\r\n", "  TMainForm = class(TForm)\r\n    DataSource1: TDataSource;\r\n    DBGrid1: TDBGrid;\r\n", StringComparison.Ordinal);
        var form = DataDesignTests.EmptyForm[..DataDesignTests.EmptyForm.LastIndexOf("end", StringComparison.Ordinal)]
            + "  object DataSource1: TDataSource\n    DesignLeft = 16\n    DesignTop = 240\n  end\n"
            + "  object DBGrid1: TDBGrid\n    Left = 16\n    Top = 16\n    Width = 320\n    Height = 120\n    DataSource = DataSource1\n    TabOrder = 0\n  end\nend\n";
        var designer = (await f.OpenAsync(unit, form)).Designer;
        var commands = f.Services.Commands;
        CollectionValue? Columns() => DesignerFixture.Object(designer, "DBGrid1").FindProperty("Columns")?.Value as CollectionValue;

        // The context menu of the grid starts with the verb; a component without the property cannot run its command.
        DesignerFixture.Select(designer, "DataSource1");
        Assert.False(commands.CanExecute(DesignCommandIds.EditCollection, "Columns"));
        DesignerFixture.Select(designer, "DBGrid1");
        var menu = designer.ContextMenu!();
        Assert.Equal(("editColumns", "designer.editCollection", "Columns", true), (menu[0].VerbId, menu[0].CommandId, menu[0].Argument, menu[0].IsEnabled));
        Assert.True(menu[1].IsSeparator);

        // The verb opens the collection editor on the empty collection; the items are TColumn objects of the metadata.
        dialogs.Script(CollectionEditorViewModel.DialogName, async dialog =>
        {
            var editor = Assert.IsType<CollectionEditorViewModel>(dialog);
            Assert.Equal("Edit DBGrid1.Columns", editor.Title);
            Assert.True(editor.IsEmpty);
            editor.Add();
            Assert.Equal(["0 – Column"], editor.ItemTexts); // DSG-OI-111: no string value yet → the class name without T
            Assert.Equal(["Alignment", "FieldName", "ReadOnly", "Title", "Visible", "Width"], editor.Grid.Rows.Select(r => r.Path).Order(StringComparer.Ordinal));
            Assert.False(Enter(editor.Grid, "Width", "-2")); // min -1
            editor.Grid.Cancel();
            Assert.True(Enter(editor.Grid, "FieldName", "name"));
            Assert.True(Enter(editor.Grid, "Width", "100"));
            Assert.True(Enter(editor.Grid, "ReadOnly", "False")); // the default: not stored
            editor.Add();
            Assert.True(Enter(editor.Grid, "Title", "E-mail"));
            Assert.True(Enter(editor.Grid, "Alignment", "taCenter"));
            Assert.True(Enter(editor.Grid, "Visible", "False"));
            Assert.Equal(["0 – name", "1 – E-mail"], editor.ItemTexts);
            editor.Add();
            Assert.True(Enter(editor.Grid, "FieldName", "id"));
            editor.MoveUp();
            editor.MoveUp();
            Assert.Equal(["0 – id", "1 – name", "2 – E-mail"], editor.ItemTexts);
            Assert.Null(Columns()); // the form is untouched while the dialog is open
            await editor.PressDefaultAsync();
        });
        await commands.ExecuteAsync(DesignCommandIds.EditCollection, "Columns");
        Assert.Equal(3, Columns()!.Items.Length);
        Assert.Equal((1, UndoTitles.Property), (designer.Undo.UndoCount, designer.Undo.UndoTitleKey));

        // The canonical text (form-format.md §2.4): items in order, properties in the item class's declaration order, defaults dropped.
        const string written = """
              object DBGrid1: TDBGrid
                Left = 16
                Top = 16
                Width = 320
                Height = 120
                DataSource = DataSource1
                Columns = <
                  item
                    FieldName = 'id'
                  end
                  item
                    FieldName = 'name'
                    Width = 100
                  end
                  item
                    Title = 'E-mail'
                    Alignment = taCenter
                    Visible = False
                  end
                >
                TabOrder = 0
              end

            """;
        Assert.Contains(written.ReplaceLineEndings("\n"), FormWriter.Write(designer.Model!), StringComparison.Ordinal);

        // The Object Inspector row opens the same editor; deleting every item removes the property (one more unit).
        var inspector = new ObjectInspectorViewModel(f.Services.Status, null, dialogs);
        inspector.Bind(designer);
        inspector.CurrentPath = "Columns";
        Assert.Equal("(3 items)", inspector.Rows.Single(r => r.Path == "Columns").Display);
        Assert.True(inspector.CanEditCollection);
        dialogs.Script(CollectionEditorViewModel.DialogName, async dialog =>
        {
            var editor = (CollectionEditorViewModel)dialog;
            editor.Select([0, 1, 2]);
            editor.Delete();
            Assert.True(editor.IsEmpty);
            await editor.PressDefaultAsync();
        });
        await inspector.EditCollectionAsync();
        Assert.Null(Columns());
        Assert.Equal(2, designer.Undo.UndoCount);

        // Undo restores the three columns.
        designer.Surface!.Execute("edit.undo");
        Assert.Equal(3, Columns()!.Items.Length);
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

    /// <summary>
    /// UI-DLG-243: OK is always enabled, also for an empty collection; an invalid value in a grid editor blocks it only while
    /// that editor is open. UI-DLG-242: a <c>TStrings</c> row of the embedded grid opens <c>Dialog.StringsEditor</c> on top.
    /// </summary>
    [Fact]
    [Trait("Req", "DSG-OI-113")]
    [Trait("Req", "DSG-OI-035")]
    public void InvalidItemValueReportsMessage()
    {
        // An invalid item value left by focus loss: refused (the item keeps its value) and its message is reported.
        var status = new RecordingStatusService();
        var document = Parse();
        var editor = new CollectionEditorViewModel(CodeSyncTests.Catalog, document, document.FindByName("StatusBar1")!, "Panels", status: status);
        editor.Grid.SetCurrent("Width");
        Assert.True(editor.Grid.BeginEdit());
        editor.Grid.EditText = "wide";
        Assert.False(editor.Grid.Commit(InspectorCommitCause.FocusLost));
        Assert.False(editor.Grid.IsEditing);
        var message = Assert.Single(status.Messages).Text;
        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.Equal(Editor(Parse()).Model.Items[0].FindProperty("Width")!.Value, editor.Model.Items[0].FindProperty("Width")!.Value);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-242")]
    [Trait("Req", "UI-DLG-243")]
    public Task OkBlockedOnlyByAnOpenInvalidEditor() => TestUiThread.RunAsync(OkBlockedOnlyByAnOpenInvalidEditorOnUiThread);

    private static async Task OkBlockedOnlyByAnOpenInvalidEditorOnUiThread()
    {
        var editor = Editor(Parse());
        Assert.True(editor.Ok.IsEnabled);

        // An invalid entry: the inline error, OK blocked while the editor stays open.
        editor.Grid.SetCurrent("Width");
        Assert.True(editor.Grid.BeginEdit());
        Assert.True(editor.Ok.IsEnabled); // an open editor alone does not block it
        editor.Grid.EditText = "wide";
        Assert.False(editor.Grid.Commit());
        Assert.NotNull(editor.Grid.EditError);
        Assert.False(editor.Ok.IsEnabled);
        await editor.Ok.PressAsync();
        Assert.False(editor.IsClosed);
        editor.Grid.Cancel(); // Escape: the editor closes, OK is available again
        Assert.True(editor.Ok.IsEnabled);

        // An empty collection is valid.
        editor.Select([0, 1]);
        editor.Delete();
        Assert.True(editor.IsEmpty);
        Assert.True(editor.Ok.IsEnabled);
        await editor.Ok.PressAsync();
        Assert.True(editor.IsClosed);

        // A TStrings item property: its row opens the strings editor over this dialog, OK sets the lines.
        var dialogs = new FakeDialogService();
        IReadOnlyList<string>? shown = null;
        dialogs.Script(StringsEditorViewModel.DialogName, vm =>
        {
            var strings = (StringsEditorViewModel)vm;
            shown = Split(strings.Text);
            strings.Text = "one\ntwo";
            return strings.Ok.PressAsync();
        });
        var catalog = StringsCatalog();
        var document = FormParser.Parse(
            "object MainForm: TForm\n  object Holder1: THolder\n    Rows = <\n      item\n        Caption = 'A'\n      end\n    >\n  end\nend\n",
            "MainForm.form", catalog).Document!;
        var rows = new CollectionEditorViewModel(catalog, document, document.FindByName("Holder1")!, "Rows", dialogs);
        rows.Grid.SetCurrent("Lines");
        Assert.True(PropertyGridViewModel.HasEditorButton(rows.Grid.CurrentRow!));
        await rows.Grid.EditStringsAsync();
        Assert.Equal(StringsEditorViewModel.DialogName, Assert.Single(dialogs.Dialogs).Name);
        Assert.Empty(shown!); // no lines yet
        Assert.False(rows.IsClosed); // the collection editor stays open under it
        await rows.Ok.PressAsync();
        new FormEditor(document, catalog).Apply(rows.Result!);
        var text = FormWriter.Write(document);
        Assert.Contains("'one'", text, StringComparison.Ordinal);
        Assert.Contains("'two'", text, StringComparison.Ordinal);
    }

    private static List<string> Split(string text) => [.. StringsEditorViewModel.Split(text)];

    /// <summary>A holder whose collection items have a <c>TStrings</c> property.</summary>
    private static ComponentCatalog StringsCatalog()
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [@"C:\s\index.json"] = """{ "formatVersion": 1, "files": ["S.components.json"] }""",
            [@"C:\s\S.components.json"] = """
                {
                  "formatVersion": 1,
                  "unit": "S",
                  "types": {
                    "TRowItem": { "kind": "object", "properties": [
                      { "name": "Caption", "type": "string", "default": "" },
                      { "name": "Lines", "type": "TStrings", "editor": "strings" }
                    ] }
                  },
                  "components": [
                    { "class": "TForm", "kind": "form", "acceptsChildren": true },
                    { "class": "THolder", "kind": "nonvisual",
                      "properties": [ { "name": "Rows", "type": "TRowItems", "kind": "collection", "itemClass": "TRowItem" } ] }
                  ]
                }
                """,
        };
        var result = CatalogLoader.Load([@"C:\s\index.json"], path => files.TryGetValue(path, out var t) ? Encoding.UTF8.GetBytes(t) : null);
        Assert.Empty(result.Problems);
        return result.Catalog;
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
