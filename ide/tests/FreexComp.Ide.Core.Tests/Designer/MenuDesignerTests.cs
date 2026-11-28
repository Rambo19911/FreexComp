using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;
using FreexComp.Designer.Surface;
using FreexComp.Designer.Text;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Tests.Designer.CodeSync;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;
using DesignCommandIds = FreexComp.Designer.Surface.Commands;

namespace FreexComp.Ide.Core.Tests.Designer;

/// <summary>The Menu Designer (design-surface.md DSG-SUR-197, DSG-SUR-199; dialogs.md §26; D-098) on the test metadata.</summary>
[Trait("Phase", "5")]
public sealed class MenuDesignerTests
{
    internal static readonly string Unit = string.Join("\r\n",
        "unit MainForm;",
        "",
        "interface",
        "",
        "uses",
        "  Classes, Controls, Forms, Menus;",
        "",
        "type",
        "  TMainForm = class(TForm)",
        "    MainMenu1: TMainMenu;",
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

    internal const string Form = """
        object MainForm: TMainForm
          Left = 200
          Top = 120
          Caption = 'Hello Form'
          ClientWidth = 400
          ClientHeight = 300
          Menu = MainMenu1
          PixelsPerInch = 96
          object MainMenu1: TMainMenu
            DesignLeft = 320
            DesignTop = 120
          end
        end

        """;

    private sealed record Session(DesignerFixture Fixture, FakeDialogService Dialogs, FormDocumentViewModel Document)
    {
        public FormDesignerViewModel Designer => Document.Designer;

        public string Code => Document.Buffer.Text;

        public string FormText => Designer.CurrentFormText();

        public FormObject Item(string name) => DesignerFixture.Object(Designer, name);

        public IReadOnlyList<string> Children(string name) => [.. Item(name).Children.Select(c => c.Name)];

        /// <summary>Opens the Menu Designer on MainMenu1 (designer.editComponent) and runs <paramref name="drive"/> in it, then Close.</summary>
        public async Task<MenuDesignerViewModel> DesignAsync(Func<MenuDesignerViewModel, Task> drive, string menu = "MainMenu1")
        {
            MenuDesignerViewModel? shown = null;
            System.Runtime.ExceptionServices.ExceptionDispatchInfo? failure = null;
            Dialogs.Script(MenuDesignerViewModel.DialogName, async dialog =>
            {
                shown = Assert.IsType<MenuDesignerViewModel>(dialog);
                try
                {
                    await drive(shown);
                    Assert.False(shown.IsClosed); // no operation leaves the dialog (DSG-SUR-197)
                }
                catch (Exception e)
                {
                    failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e); // the command would report it as an internal error
                }

                await shown.PressCancelAsync(); // Escape = Close (UI-DLG-252)
            });
            DesignerFixture.Select(Designer, menu);
            await Fixture.Services.Commands.ExecuteAsync(DesignCommandIds.EditComponent);
            failure?.Throw();
            return shown ?? throw new InvalidOperationException("The Menu Designer did not open.");
        }

        public void Undo() => Designer.Surface!.Execute(DesignCommandIds.Undo);
    }

    private static async Task<Session> OpenAsync(string? form = null)
    {
        var dialogs = new FakeDialogService();
        var f = new DesignerFixture(o => o.Dialogs = dialogs);
        var document = await f.OpenAsync(Unit, form ?? Form);
        return new Session(f, dialogs, document);
    }

    /// <summary>The grid's Name row: rename the selected item (one "Rename" unit with its field).</summary>
    private static void RenameInGrid(MenuDesignerViewModel md, string name)
    {
        md.Grid.Page = InspectorPage.Properties;
        md.Grid.SetCurrent("Name");
        Assert.True(md.Grid.BeginEdit());
        md.Grid.EditText = name;
        Assert.True(md.Grid.Commit(), md.Grid.EditError);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-SUR-197")]
    [Trait("Req", "UI-DLG-250")]
    [Trait("Req", "UI-DLG-251")]
    public Task NewInsertSubItemSeparatorDeleteMove() => TestUiThread.RunAsync(NewInsertSubItemSeparatorDeleteMoveOnUiThread);

    private static async Task NewInsertSubItemSeparatorDeleteMoveOnUiThread()
    {
        var s = await OpenAsync();
        await s.DesignAsync(async md =>
        {
            var focus = 0;
            md.CaptionFocusRequested += (_, _) => focus++;
            Assert.True(md.IsEmpty);
            Assert.Null(md.Selected);
            Assert.True(md.CanNewItem);
            Assert.False(md.CanInsertBefore || md.CanNewSubItem || md.CanDelete || md.CanMoveUp || md.CanMoveDown || md.IsCaptionEnabled);

            // New Item with nothing selected: appended to the top level; then after the selected item, same level.
            md.NewItem();
            Assert.Equal(["MenuItem1"], s.Children("MainMenu1"));
            Assert.Equal("MenuItem1", md.Selected!.Name);
            md.NewItem();
            Assert.Equal(["MenuItem1", "MenuItem2"], s.Children("MainMenu1"));
            Assert.Equal(2, focus); // UI-DLG-250: Caption gets focus

            // Insert Before: before the selected item.
            md.Select("MenuItem1");
            md.InsertBefore();
            Assert.Equal(["MenuItem3", "MenuItem1", "MenuItem2"], s.Children("MainMenu1"));

            // New Sub-Item: appended to the selected item's children.
            md.Select("MenuItem1");
            md.NewSubItem();
            Assert.Equal(["MenuItem4"], s.Children("MenuItem1"));
            Assert.Equal(4, focus);

            // New Separator: after the selected item, Caption '-', named N<k>; focus stays in the tree.
            md.NewSeparator();
            Assert.Equal(["MenuItem4", "N1"], s.Children("MenuItem1"));
            Assert.Equal("-", ((StringValue)s.Item("N1").FindProperty("Caption")!.Value).Value);
            Assert.Equal(4, focus);
            var separator = Assert.Single(md.Nodes, n => n.Name == "MenuItem1").Children[1];
            Assert.True(separator.IsSeparator);
            Assert.False(md.CanNewSubItem); // a separator gets no sub-items
            Assert.False(md.IsShortCutEnabled is false && md.Selected!.Children.Count > 0);

            // Move Up / Move Down among siblings.
            md.MoveUp();
            Assert.Equal(["N1", "MenuItem4"], s.Children("MenuItem1"));
            Assert.False(md.CanMoveUp);
            md.MoveDown();
            Assert.Equal(["MenuItem4", "N1"], s.Children("MenuItem1"));
            Assert.False(md.CanMoveDown);

            // The tree: creation order, sub-items as children, the empty caption shows the name.
            Assert.Equal(["MenuItem3", "MenuItem1", "MenuItem2"], md.Nodes.Select(n => n.Name));
            Assert.All(md.Nodes, n => Assert.True(n.IsCaptionEmpty && n.Text == n.Name));
            Assert.Equal(["MenuItem3", "MenuItem1", "MenuItem4", "N1", "MenuItem2"], md.Items.Select(i => i.Name));

            // ShortCut is disabled for an item with sub-items.
            md.Select("MenuItem1");
            Assert.False(md.IsShortCutEnabled);
            Assert.True(md.IsCaptionEnabled);

            // Delete on an item with sub-items asks first (Cancel is the default); then the item and its sub-items go.
            s.Dialogs.Enqueue("Msg.MenuDesigner.DeleteWithSubItems", "Cancel");
            await md.DeleteAsync();
            Assert.Equal(["MenuItem3", "MenuItem1", "MenuItem2"], s.Children("MainMenu1"));
            Assert.Equal(["MenuItem1", "2"], s.Dialogs.Requests[^1].Args);
            s.Dialogs.Enqueue("Msg.MenuDesigner.DeleteWithSubItems", "Delete");
            await md.DeleteAsync();
            Assert.Equal(["MenuItem3", "MenuItem2"], s.Children("MainMenu1"));
            Assert.Null(s.Designer.Model!.FindByName("N1"));
            Assert.Equal("MenuItem2", md.Selected!.Name); // the following sibling

            // Without sub-items Delete asks nothing; the last item: the preceding one is selected.
            var asked = s.Dialogs.Requests.Count;
            await md.DeleteAsync();
            Assert.Equal(asked, s.Dialogs.Requests.Count);
            Assert.Equal(["MenuItem3"], s.Children("MainMenu1"));
            Assert.Equal("MenuItem3", md.Selected!.Name);
        });
        Assert.Equal(["MainMenu1"], s.Designer.Surface!.Selection.Items.Select(o => o.Name)); // the surface selection is kept
    }

    /// <summary>UI-DLG-253: for a <c>TPopupMenu</c> the tree's top level is the popup's item list; the dialog is the same.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-253")]
    public Task PopupMenuTopLevelIsItsItemList() => TestUiThread.RunAsync(PopupMenuTopLevelIsItsItemListOnUiThread);

    private static async Task PopupMenuTopLevelIsItsItemListOnUiThread()
    {
        var form = Form.Replace("""
              object MainMenu1: TMainMenu
                DesignLeft = 320
                DesignTop = 120
              end
            """, """
              object MainMenu1: TMainMenu
                DesignLeft = 320
                DesignTop = 120
              end
              object PopupMenu1: TPopupMenu
                DesignLeft = 360
                DesignTop = 120
                object Open1: TMenuItem
                  Caption = '&Open'
                end
              end
            """, StringComparison.Ordinal);
        Assert.Contains("PopupMenu1", form, StringComparison.Ordinal);
        var unit = Unit.Replace("    MainMenu1: TMainMenu;", "    MainMenu1: TMainMenu;\r\n    PopupMenu1: TPopupMenu;\r\n    Open1: TMenuItem;", StringComparison.Ordinal);
        var dialogs = new FakeDialogService();
        var f = new DesignerFixture(o => o.Dialogs = dialogs);
        var s = new Session(f, dialogs, await f.OpenAsync(unit, form));
        var shown = await s.DesignAsync(md =>
        {
            Assert.Equal("PopupMenu1", md.Menu.Name);
            Assert.Equal(["Open1"], md.Nodes.Select(n => n.Name)); // the popup's items are the top level
            Assert.Equal("&Open", md.Nodes[0].Text);
            md.Select("Open1");
            md.NewItem(); // same level: the popup's item list
            Assert.Equal(["Open1", "MenuItem1"], s.Children("PopupMenu1"));
            md.NewSubItem();
            Assert.Equal(["MenuItem2"], s.Children("MenuItem1"));
            Assert.Empty(s.Children("MainMenu1")); // the main menu is untouched
            return Task.CompletedTask;
        }, "PopupMenu1");
        Assert.Equal(MenuDesignerViewModel.DialogName, shown.Name); // the same dialog as for a main menu
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-SUR-197")]
    [Trait("Req", "UIF-727")]
    public Task CaptionCommitRenamesGeneratedName() => TestUiThread.RunAsync(CaptionCommitRenamesGeneratedNameOnUiThread);

    private static async Task CaptionCommitRenamesGeneratedNameOnUiThread()
    {
        var s = await OpenAsync();
        await s.DesignAsync(md =>
        {
            // The generated name follows the caption without non-identifier characters, in the same "Property" unit.
            md.NewItem();
            md.CaptionText = "&Edit";
            Assert.True(md.CommitCaption());
            Assert.Equal("Edit", md.Selected!.Name);
            Assert.Equal("&Edit", md.Nodes[0].Caption);
            Assert.Equal((2, UndoTitles.Property), (s.Designer.Undo.UndoCount, s.Designer.Undo.UndoTitleKey));
            Assert.Contains("    Edit: TMenuItem;", s.Code, StringComparison.Ordinal);
            Assert.DoesNotContain("MenuItem1", s.Code, StringComparison.Ordinal);

            // Taken: a numeric suffix.
            md.NewItem();
            md.CaptionText = "E&dit";
            Assert.True(md.CommitCaption());
            Assert.Equal("Edit1", md.Selected!.Name);

            // A reserved word is taken too ("file").
            md.NewItem();
            md.CaptionText = "&File";
            Assert.True(md.CommitCaption());
            Assert.Equal("File1", md.Selected!.Name);

            // A caption with spaces and punctuation; a leading digit is dropped.
            md.NewItem();
            md.CaptionText = "1 Save &As…";
            Assert.True(md.CommitCaption());
            Assert.Equal("SaveAs", md.Selected!.Name);

            // A hand-given name is kept.
            RenameInGrid(md, "Hand");
            md.CaptionText = "Other";
            Assert.True(md.CommitCaption());
            Assert.Equal("Hand", md.Selected!.Name);
            Assert.Equal("Other", md.Nodes[^1].Caption);

            // A separator caption on a generated item: N<k>.
            md.NewItem();
            md.CaptionText = "-";
            Assert.True(md.CommitCaption());
            Assert.Equal("N1", md.Selected!.Name);
            Assert.True(md.Nodes[^1].IsSeparator);

            // One undo step restores caption and name.
            s.Undo();
            Assert.Equal("MenuItem1", s.Children("MainMenu1")[^1]);
            Assert.Null(s.Item("MenuItem1").FindProperty("Caption"));
            return Task.CompletedTask;
        });
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-SUR-197")]
    [Trait("Req", "DSG-CS-040")]
    public Task ItemsGetFieldsAndHandlersInFormClass() => TestUiThread.RunAsync(ItemsGetFieldsAndHandlersInFormClassOnUiThread);

    private static async Task ItemsGetFieldsAndHandlersInFormClassOnUiThread()
    {
        var s = await OpenAsync();
        await s.DesignAsync(md =>
        {
            md.NewItem();
            md.NewSubItem();
            md.NewSeparator();
            Assert.Contains("    MenuItem1: TMenuItem;", s.Code, StringComparison.Ordinal);
            Assert.Contains("    MenuItem2: TMenuItem;", s.Code, StringComparison.Ordinal);
            Assert.Contains("    N1: TMenuItem;", s.Code, StringComparison.Ordinal);

            // Enter / double-click: the OnClick handler is created and assigned; the dialog stays, the Design view stays.
            md.Select("MenuItem2");
            md.ActivateSelected();
            Assert.Equal("MenuItem2Click", ((IdentValue)s.Item("MenuItem2").FindProperty("OnClick")!.Value).Name);
            Assert.Contains("    procedure MenuItem2Click(Sender: TObject);", s.Code, StringComparison.Ordinal);
            Assert.Contains("procedure TMainForm.MenuItem2Click(Sender: TObject);", s.Code, StringComparison.Ordinal);
            Assert.Equal(DocumentView.Design, s.Document.ActiveView);
            md.Grid.Page = InspectorPage.Events;
            Assert.Equal("MenuItem2Click", Assert.Single(md.Grid.EventRows, r => r.Name == "OnClick").Value);

            // Again: the existing handler is kept (no second method, no unit).
            var units = s.Designer.Undo.UndoCount;
            md.ActivateSelected();
            Assert.Equal(units, s.Designer.Undo.UndoCount);

            // The Events page of the embedded grid creates handlers too, without leaving the dialog.
            md.Select("MenuItem1");
            md.Grid.SetCurrent("OnClick");
            Assert.True(md.Grid.BeginEdit());
            md.Grid.EditText = "OpenClick";
            Assert.True(md.Grid.Commit());
            Assert.Contains("procedure TMainForm.OpenClick(Sender: TObject);", s.Code, StringComparison.Ordinal);

            // Delete removes the fields of the item and its sub-items.
            s.Dialogs.Enqueue("Msg.MenuDesigner.DeleteWithSubItems", "Delete");
            return md.DeleteAsync();
        });
        Assert.DoesNotContain("MenuItem1: TMenuItem", s.Code, StringComparison.Ordinal);
        Assert.DoesNotContain("MenuItem2: TMenuItem", s.Code, StringComparison.Ordinal);
        Assert.DoesNotContain("N1: TMenuItem", s.Code, StringComparison.Ordinal);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-SUR-199")]
    [Trait("Req", "UI-DLG-252")]
    public Task EachOperationIsOneUndoUnit() => TestUiThread.RunAsync(EachOperationIsOneUndoUnitOnUiThread);

    private static async Task EachOperationIsOneUndoUnitOnUiThread()
    {
        var s = await OpenAsync();
        var form = s.FormText;
        var code = s.Code;
        var titles = new List<(string Key, string? Argument)>();
        void Step(Action action)
        {
            var before = s.Designer.Undo.UndoCount;
            action();
            Assert.Equal(before + 1, s.Designer.Undo.UndoCount);
            titles.Add((s.Designer.Undo.UndoTitleKey!, s.Designer.Undo.Peek!.TitleArgument));
        }

        await s.DesignAsync(async md =>
        {
            Step(md.NewItem);
            Step(() => { md.CaptionText = "&File"; md.CommitCaption(); });
            Step(md.NewSubItem);
            Step(md.NewSeparator);
            Step(md.MoveUp);
            md.Select("MenuItem1");
            Step(() => { md.ShortCutText = "Ctrl+Q"; Assert.True(md.CommitShortCut()); });
            Step(md.ActivateSelected);
            Step(() => RenameInGrid(md, "FileExit"));

            // The separator's field is the last one, directly before the handler declaration; it has no sub-items,
            // so no question.
            md.Select("N1");
            var before = s.Designer.Undo.UndoCount;
            await md.DeleteAsync();
            Assert.Equal(before + 1, s.Designer.Undo.UndoCount);
            titles.Add((s.Designer.Undo.UndoTitleKey!, null));
        });

        Assert.Equal(
        [
            (UndoTitles.Add, "TMenuItem"), (UndoTitles.Property, null), (UndoTitles.Add, "TMenuItem"), (UndoTitles.Add, "TMenuItem"),
            (UndoTitles.Order, null), (UndoTitles.Property, null), (UndoTitles.Event, null), (UndoTitles.Rename, null), (UndoTitles.Delete, null),
        ], titles);

        // Close only closed: Edit › Undo steps back through the operations one by one, code included.
        for (var i = titles.Count; i > 0; i--)
        {
            Assert.Equal(i, s.Designer.Undo.UndoCount);
            s.Undo();
        }

        Assert.Equal(form, s.FormText);
        Assert.Equal(code, s.Code);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-SUR-197")]
    [Trait("Req", "DSG-FD-040")]
    public Task WrittenFormLoadsInRuntimeShape() => TestUiThread.RunAsync(WrittenFormLoadsInRuntimeShapeOnUiThread);

    private static async Task WrittenFormLoadsInRuntimeShapeOnUiThread()
    {
        var s = await OpenAsync();
        await s.DesignAsync(md =>
        {
            md.NewItem();
            md.CaptionText = "&File";
            md.CommitCaption();
            RenameInGrid(md, "FileMenu");
            md.NewSubItem();
            md.CaptionText = "E&xit";
            md.CommitCaption();
            RenameInGrid(md, "FileExit");
            md.ShortCutText = "Ctrl+Q";
            Assert.True(md.CommitShortCut());
            md.ActivateSelected();
            return Task.CompletedTask;
        });

        Assert.True(await s.Fixture.Services.Documents.SaveAsync(s.Document));
        var golden = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "forms", "canonical", "menu-designed.form"));
        Assert.Equal(golden, s.Fixture.Disk(DesignerFixture.FormPath));

        // The runtime shape (sample-apps.md §1.2): the items nested under the menu, no design geometry on the items.
        var parsed = FormParser.Parse(golden, "menu-designed.form", CodeSyncTests.Catalog);
        Assert.Empty(parsed.Diagnostics);
        var menu = parsed.Document!.FindByName("MainMenu1")!;
        Assert.Equal(["FileMenu"], menu.Children.Select(c => c.Name));
        Assert.Equal(["FileExit"], menu.Children[0].Children.Select(c => c.Name));
        Assert.NotNull(menu.FindProperty("DesignLeft"));
        Assert.All(menu.Children.SelectMany(c => c.Children.Prepend(c)), item =>
        {
            Assert.Equal("TMenuItem", item.TypeName);
            Assert.Null(item.FindProperty("DesignLeft"));
            Assert.Null(item.FindProperty("Left"));
        });
        Assert.Contains("    FileMenu: TMenuItem;", s.Code, StringComparison.Ordinal);
        Assert.Contains("    FileExit: TMenuItem;", s.Code, StringComparison.Ordinal);
        Assert.Contains("procedure TMainForm.FileExitClick(Sender: TObject);", s.Code, StringComparison.Ordinal);
    }

    // ---- Phase 8, F05-086 (docs/spec/designer/phase8/menu-designer-collection-editors.md) ---------------------------

    /// <summary>New Item — or New Sub-Item of the selected item — with that caption; the item (its name follows the caption).</summary>
    private static FormObject Add(MenuDesignerViewModel md, string caption, bool sub = false)
    {
        if (sub)
        {
            md.NewSubItem();
        }
        else
        {
            md.NewItem();
        }

        md.CaptionText = caption;
        Assert.True(md.CommitCaption());
        return md.Selected!;
    }

    /// <summary>
    /// D-155, card F05-086 acceptance 3 (DSG-SUR-251, 252): two items with the same access key in one level give exactly one
    /// warning; a third joins it; another level is unrelated; the current warning follows the selection.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-251")]
    [Trait("Req", "DSG-SUR-252")]
    public Task DuplicateAcceleratorWarnsOnce() => TestUiThread.RunAsync(DuplicateAcceleratorWarnsOnceOnUiThread);

    private static async Task DuplicateAcceleratorWarnsOnceOnUiThread()
    {
        var s = await OpenAsync();
        await s.DesignAsync(md =>
        {
            Assert.Empty(md.Warnings);
            Assert.Null(md.CurrentWarning);
            Assert.Equal("", md.WarningText);

            // &Open… and C&opy under File: exactly one warning (parent File, key O, both items in level order).
            var file = Add(md, "&File");
            var open = Add(md, "&Open…", sub: true);
            Assert.Empty(md.Warnings);
            var copy = Add(md, "C&opy && Paste"); // lower case: one key with the upper case O; "&&" is a literal ampersand
            var warning = Assert.Single(md.Warnings);
            Assert.Same(file, warning.Parent);
            Assert.Equal('O', warning.Key);
            Assert.Equal([open, copy], warning.Items);
            Assert.Equal("O is the access key of more than one item in File: Open…, Copy & Paste.", warning.Text);
            Assert.Same(warning, md.CurrentWarning);
            Assert.Equal(warning.Text, md.WarningText); // one warning: no suffix
            Assert.Null(md.Nodes[0].Warning); // the mark is on the items of the warning only
            Assert.All(md.Nodes[0].Children, n => Assert.Equal(warning.Text, n.Warning));

            // A third item with that key joins the warning: still one.
            var options = Add(md, "&Options");
            warning = Assert.Single(md.Warnings);
            Assert.Equal([open, copy, options], warning.Items);
            Assert.Equal("O is the access key of more than one item in File: Open…, Copy & Paste, Options.", md.WarningText);

            // Items with stored Visible = False or Enabled = False count like any other (both may change at run time).
            Assert.True(s.Designer.Surface!.Commit(UndoTitles.Property,
                [new SetProperty(open.Name, "Visible", new IdentValue("False")), new SetProperty(copy.Name, "Enabled", new IdentValue("False"))]));
            Assert.Equal([open, copy, options], Assert.Single(md.Warnings).Items);

            // The same key in another level is unrelated — until that level has a second item with it.
            md.Select(file);
            var edit = Add(md, "&Edit");
            var obj = Add(md, "&Object", sub: true);
            Assert.Single(md.Warnings);
            Assert.Null(md.Nodes[1].Children[0].Warning);
            var show = Add(md, "Sh&ow");
            Assert.Equal(2, md.Warnings.Count);
            Assert.Equal([file, edit], md.Warnings.Select(w => w.Parent)); // the levels in tree pre-order
            Assert.Equal([obj, show], md.Warnings[1].Items);

            // The current warning is the one the selected item takes part in, else the first; "(+1 more)" for the other.
            Assert.Same(show, md.Selected);
            Assert.Same(md.Warnings[1], md.CurrentWarning);
            Assert.Equal("O is the access key of more than one item in Edit: Object, Show. (+1 more)", md.WarningText);
            md.Select(open);
            Assert.Same(md.Warnings[0], md.CurrentWarning);
            Assert.Equal("O is the access key of more than one item in File: Open…, Copy & Paste, Options. (+1 more)", md.WarningText);
            md.Select(edit); // takes part in none: the first of the list
            Assert.Same(md.Warnings[0], md.CurrentWarning);

            // The top level names the menu component; it comes first in the list. Separators have no key.
            var format = Add(md, "&Format");
            md.NewSeparator();
            md.NewSeparator();
            Assert.Equal(3, md.Warnings.Count);
            Assert.Same(md.Menu, md.Warnings[0].Parent);
            Assert.Equal([file, format], md.Warnings[0].Items);
            md.Select(format);
            Assert.Equal("F is the access key of more than one item in MainMenu1: File, Format. (+2 more)", md.WarningText);

            // A parent without a caption is named by its component name.
            md.NewItem();
            var plain = md.Selected!;
            Add(md, "&Zoom In", sub: true);
            Add(md, "&Zoom Out");
            Assert.Equal($"Z is the access key of more than one item in {plain.Name}: Zoom In, Zoom Out. (+3 more)", md.WarningText);

            // Changing a caption clears the warning; Undo brings it back.
            md.Select(format);
            md.CaptionText = "Fo&rmat";
            Assert.True(md.CommitCaption());
            Assert.Equal(3, md.Warnings.Count);
            Assert.DoesNotContain(md.Warnings, w => ReferenceEquals(w.Parent, md.Menu));
            Assert.Null(md.Nodes.Single(n => ReferenceEquals(n.Item, format)).Warning);
            s.Undo();
            Assert.Equal(4, md.Warnings.Count);
            Assert.Same(md.Menu, md.Warnings[0].Parent);
            Assert.NotNull(md.Nodes.Single(n => n.Name == format.Name).Warning);
            return Task.CompletedTask;
        });

        // The list is computed when the dialog opens: the first top-level item is selected and takes part in the first warning.
        await s.DesignAsync(md =>
        {
            Assert.Equal(4, md.Warnings.Count);
            Assert.Equal("F is the access key of more than one item in MainMenu1: File, Format. (+3 more)", md.WarningText);
            return Task.CompletedTask;
        });

        // Advisory only: nothing of it is stored in the form.
        Assert.DoesNotContain("access key", s.FormText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>D-155 (DSG-SUR-250): the access key of a caption — the first marked character, upper case; a doubled ampersand is a literal one.</summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-250")]
    [InlineData("&File", "F")]
    [InlineData("Fi&le", "L")]
    [InlineData("&&File", "")]
    [InlineData("A&&B &C", "C")]
    [InlineData("&A &B", "A")]
    [InlineData("End&", "")]
    [InlineData("&1 Recent", "1")]
    [InlineData("", "")]
    [InlineData("-", "")]
    [InlineData("&é", "É")]
    [InlineData("&&&Save", "S")]
    [InlineData("Plain", "")]
    [InlineData("&", "")]
    [InlineData("&&", "")]
    public void AccessKeyOfCaption(string caption, string expected) =>
        Assert.Equal(expected, MenuDesignerViewModel.AccessKey(caption)?.ToString() ?? "");
}
