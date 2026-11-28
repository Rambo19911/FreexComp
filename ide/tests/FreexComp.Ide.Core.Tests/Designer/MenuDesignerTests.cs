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
    [Trait("Req", "UIF-727")]
    public Task CaptionNamedAfterSystemRoutineGetsSuffix() => TestUiThread.RunAsync(CaptionNamedAfterSystemRoutineGetsSuffixOnUiThread);

    /// <summary>D-163: a field named like a <c>System</c> routine would hide it in every method of the form (<c>Exit;</c> → E2060).</summary>
    private static async Task CaptionNamedAfterSystemRoutineGetsSuffixOnUiThread()
    {
        var s = await OpenAsync();
        await s.DesignAsync(md =>
        {
            foreach (var (caption, name) in new[] { ("E&xit", "Exit1"), ("&Break", "Break1"), ("&Write", "Write1"), ("&Halt", "Halt1") })
            {
                md.NewItem();
                md.CaptionText = caption;
                Assert.True(md.CommitCaption());
                Assert.Equal(name, md.Selected!.Name);
                Assert.Contains($"    {name}: TMenuItem;", s.Code, StringComparison.Ordinal);
            }

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

    /// <summary>The names of the objects of the saved form text, in the order in which they are written.</summary>
    private static List<string> WrittenOrder(string formText, params string[] names) =>
        [.. names.OrderBy(n => formText.IndexOf($"object {n}: TMenuItem", StringComparison.Ordinal))];

    /// <summary>
    /// D-156 (DSG-SUR-255, 256): <c>MoveTo</c> before / after / into — the order in the model and in the saved form, one
    /// "Order" unit each (the kind of Move Up), the sub-items move with their item, refused and present places record nothing.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-255")]
    [Trait("Req", "DSG-SUR-256")]
    public Task MoveToReordersAndReparentsAsOneOrderUnit() => TestUiThread.RunAsync(MoveToReordersAndReparentsAsOneOrderUnitOnUiThread);

    private static async Task MoveToReordersAndReparentsAsOneOrderUnitOnUiThread()
    {
        var s = await OpenAsync();
        await s.DesignAsync(md =>
        {
            var file = Add(md, "&File");
            var open = Add(md, "&Open…", sub: true);
            md.ShortCutText = "Ctrl+O";
            Assert.True(md.CommitShortCut());
            md.NewSeparator();
            var rule = md.Selected!;
            var exit = Add(md, "E&xit");
            md.Select(file);
            var help = Add(md, "&Help");
            var about = Add(md, "&About", sub: true);
            Assert.Equal(["Open", "N1", "Exit1"], s.Children(file.Name));
            var code = s.Code;
            var units = s.Designer.Undo.UndoCount;

            void Moved(FormObject item, FormObject target, MenuDropPosition position)
            {
                Assert.True(md.CanMoveTo(item, target, position));
                Assert.True(md.MoveTo(item, target, position));
                Assert.Equal((++units, UndoTitles.Order), (s.Designer.Undo.UndoCount, s.Designer.Undo.UndoTitleKey)); // one unit, titled as Move Up
                Assert.Same(item, md.Selected); // the moved item is selected
            }

            void Unmoved(FormObject item, FormObject target, MenuDropPosition position, bool valid)
            {
                var form = s.FormText;
                Assert.Equal(valid, md.CanMoveTo(item, target, position));
                Assert.False(md.MoveTo(item, target, position));
                Assert.Equal(units, s.Designer.Undo.UndoCount);
                Assert.Equal(form, s.FormText);
            }

            // Before: Exit in front of Open; Undo restores the place, the selection stays; the document is modified.
            md.Select(open);
            Moved(exit, open, MenuDropPosition.Before);
            Assert.Equal(["Exit1", "Open", "N1"], s.Children(file.Name));
            Assert.True(s.Document.IsModified);
            s.Undo();
            units--;
            Assert.Equal(["Open", "N1", "Exit1"], s.Children(file.Name));
            Assert.Same(exit, md.Selected);
            Assert.Equal(["MainMenu1"], s.Designer.Surface!.Selection.Items.Select(o => o.Name)); // the surface selection is kept

            // After, upwards and downwards within the level (the index counts after the removal).
            Moved(open, exit, MenuDropPosition.After);
            Assert.Equal(["N1", "Exit1", "Open"], s.Children(file.Name));
            Moved(rule, exit, MenuDropPosition.After);
            Assert.Equal(["Exit1", "N1", "Open"], s.Children(file.Name));
            Moved(open, exit, MenuDropPosition.After);
            Assert.Equal(["Exit1", "Open", "N1"], s.Children(file.Name));

            // Into: the last sub-item of another item; before / after an item of another level.
            Moved(open, help, MenuDropPosition.Into);
            Assert.Equal(["Exit1", "N1"], s.Children(file.Name));
            Assert.Equal(["About", "Open"], s.Children(help.Name));
            Moved(exit, about, MenuDropPosition.Before);
            Assert.Equal(["N1"], s.Children(file.Name));
            Assert.Equal(["Exit1", "About", "Open"], s.Children(help.Name));

            // The menu component as target: the end of the top level; then before the first top-level item.
            Moved(about, md.Menu, MenuDropPosition.Into);
            Assert.Equal([file.Name, "Help", "About"], s.Children("MainMenu1"));
            Moved(about, file, MenuDropPosition.Before);
            Assert.Equal(["About", file.Name, "Help"], s.Children("MainMenu1"));

            // The sub-items move with their item.
            Moved(help, file, MenuDropPosition.Into);
            Assert.Equal(["About", file.Name], s.Children("MainMenu1"));
            Assert.Equal(["N1", "Help"], s.Children(file.Name));
            Assert.Equal(["Exit1", "Open"], s.Children(help.Name));

            // An item that gets its first sub-item this way keeps its stored ShortCut (as with New Sub-Item).
            Moved(about, open, MenuDropPosition.Into);
            Assert.Equal(16463, ((IntegerValue)open.FindProperty("ShortCut")!.Value).Value);
            Assert.Equal(["About"], s.Children(open.Name));

            // Refused: into itself, into or beside one of its own sub-items, into a separator, beside the menu component.
            Unmoved(file, file, MenuDropPosition.Into, valid: false);
            Unmoved(file, help, MenuDropPosition.Into, valid: false);
            Unmoved(file, about, MenuDropPosition.Before, valid: false);
            Unmoved(file, about, MenuDropPosition.After, valid: false);
            Unmoved(about, rule, MenuDropPosition.Into, valid: false);
            Unmoved(about, md.Menu, MenuDropPosition.Before, valid: false);
            Unmoved(about, md.Menu, MenuDropPosition.After, valid: false);
            Unmoved(md.Menu, file, MenuDropPosition.Into, valid: false);

            // The present place is valid and records nothing.
            Unmoved(rule, rule, MenuDropPosition.Before, valid: true);
            Unmoved(rule, rule, MenuDropPosition.After, valid: true);
            Unmoved(rule, help, MenuDropPosition.Before, valid: true);
            Unmoved(help, rule, MenuDropPosition.After, valid: true);
            Unmoved(help, file, MenuDropPosition.Into, valid: true);
            Unmoved(file, md.Menu, MenuDropPosition.Into, valid: true);

            // Captions, names, fields and the unit's code are unchanged by all of it.
            Assert.Equal(code, s.Code);
            Assert.Equal(["File1", "N1", "Help", "Exit1", "Open", "About"], md.Items.Select(i => i.Name));
            return Task.CompletedTask;
        });

        // In the saved form every object's text stands at its new place (creation order = display order, DSG-SUR-197).
        Assert.True(await s.Fixture.Services.Documents.SaveAsync(s.Document));
        var saved = s.Fixture.Disk(DesignerFixture.FormPath);
        Assert.Equal(["File1", "N1", "Help", "Exit1", "Open", "About"], WrittenOrder(saved, "About", "Exit1", "File1", "Help", "N1", "Open"));
        var parsed = FormParser.Parse(saved, "MainForm.form", CodeSyncTests.Catalog);
        Assert.Empty(parsed.Diagnostics);
        Assert.Equal(["About"], parsed.Document!.FindByName("Open")!.Children.Select(c => c.Name));
    }

    /// <summary>An ancestor form with a main menu: File (Open) and Edit.</summary>
    private const string BaseMenuForm = """
        object BaseMenuForm: TBaseMenuForm
          Left = 200
          Top = 120
          Caption = 'Base'
          ClientWidth = 400
          ClientHeight = 300
          Menu = MainMenu1
          PixelsPerInch = 96
          object MainMenu1: TMainMenu
            DesignLeft = 320
            DesignTop = 120
            object FileMenu: TMenuItem
              Caption = '&File'
              object FileOpen: TMenuItem
                Caption = '&Open'
              end
            end
            object EditMenu: TMenuItem
              Caption = '&Edit'
            end
          end
        end

        """;

    /// <summary>The descendant: Exit under the inherited File, Help and Tools after the inherited top-level items.</summary>
    private const string DescendantMenuForm = """
        inherited MainForm: TMainForm
          inherited MainMenu1: TMainMenu
            inherited FileMenu: TMenuItem
              object FileExit: TMenuItem
                Caption = 'E&xit'
              end
            end
            object HelpMenu: TMenuItem
              Caption = '&Help'
            end
            object ToolsMenu: TMenuItem
              Caption = '&Tools'
            end
          end
        end

        """;

    private static async Task<Session> OpenDescendantAsync()
    {
        const string BasePath = @"C:\p\BaseMenuUnit.form";
        var parsed = FormParser.Parse(BaseMenuForm.ReplaceLineEndings("\n"), BasePath, CodeSyncTests.Catalog, "TForm");
        Assert.Empty(parsed.Diagnostics);
        var ancestors = new AncestorCatalog();
        ancestors.Set(new AncestorDefinition("TBaseMenuForm", "BaseMenuUnit", BasePath, parsed.Document!));
        var dialogs = new FakeDialogService();
        var f = new DesignerFixture(o => o.Dialogs = dialogs);
        ((FreexComp.Ide.Core.Documents.DocumentService)f.Services.Documents).DesignAncestors = ancestors;
        var unit = Unit.Replace("Classes, Controls, Forms, Menus;", "Classes, Controls, Forms, Menus, BaseMenuUnit;", StringComparison.Ordinal)
            .Replace("TMainForm = class(TForm)", "TMainForm = class(TBaseMenuForm)", StringComparison.Ordinal)
            .Replace("    MainMenu1: TMainMenu;", "    FileExit: TMenuItem;\r\n    HelpMenu: TMenuItem;\r\n    ToolsMenu: TMenuItem;", StringComparison.Ordinal);
        var document = await f.OpenAsync(unit, DescendantMenuForm.ReplaceLineEndings("\n"));
        Assert.True(document.Designer.Model!.Root.IsInheritedRoot);
        return new Session(f, dialogs, document);
    }

    /// <summary>
    /// D-156 (DSG-SUR-257): move out (the sibling directly after the parent) and move in (the last sub-item of the sibling
    /// before), their enabled rules, one "Order" unit each; fixed (inherited) items do not move, and nothing goes before them.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-257")]
    [Trait("Req", "DSG-SUR-253")]
    public Task MoveOutAndMoveIn() => TestUiThread.RunAsync(MoveOutAndMoveInOnUiThread);

    private static async Task MoveOutAndMoveInOnUiThread()
    {
        var s = await OpenAsync();
        await s.DesignAsync(md =>
        {
            Assert.False(md.CanMoveOut || md.CanMoveIn); // nothing selected
            var file = Add(md, "&File");
            var open = Add(md, "&Open…", sub: true);
            md.NewSeparator();
            var exit = Add(md, "E&xit");
            md.Select(file);
            var help = Add(md, "&Help");
            var units = s.Designer.Undo.UndoCount;

            void OneOrderUnit(Action move)
            {
                var selected = md.Selected;
                move();
                Assert.Equal((++units, UndoTitles.Order), (s.Designer.Undo.UndoCount, s.Designer.Undo.UndoTitleKey));
                Assert.Same(selected, md.Selected);
            }

            void Nothing(Action move)
            {
                move();
                Assert.Equal(units, s.Designer.Undo.UndoCount);
            }

            // Move out: the sibling directly after the parent. Not at the top level.
            md.Select(exit);
            Assert.True(md.CanMoveOut);
            Assert.False(md.CanMoveIn); // the sibling before it is a separator
            Nothing(md.MoveIn);
            OneOrderUnit(md.MoveOut);
            Assert.Equal([file.Name, "Exit1", "Help"], s.Children("MainMenu1"));
            Assert.Equal(["Open", "N1"], s.Children(file.Name));
            Assert.False(md.CanMoveOut);
            Nothing(md.MoveOut);

            // Move in: the last sub-item of the sibling before it. Not for the first of a level.
            Assert.True(md.CanMoveIn);
            OneOrderUnit(md.MoveIn);
            Assert.Equal([file.Name, "Help"], s.Children("MainMenu1"));
            Assert.Equal(["Open", "N1", "Exit1"], s.Children(file.Name));
            md.Select(open);
            Assert.False(md.CanMoveIn);
            Nothing(md.MoveIn);
            md.Select(file);
            Assert.False(md.CanMoveIn || md.CanMoveOut);

            // An item with sub-items moves with them; two levels in, two levels out.
            md.Select(help);
            Add(md, "&About", sub: true);
            units = s.Designer.Undo.UndoCount;
            md.Select(help);
            OneOrderUnit(md.MoveIn);
            OneOrderUnit(md.MoveIn);
            Assert.Equal([file.Name], s.Children("MainMenu1"));
            Assert.Equal(["Open", "N1", "Exit1"], s.Children(file.Name));
            Assert.Equal(["Help"], s.Children("Exit1"));
            Assert.Equal(["About"], s.Children("Help"));
            OneOrderUnit(md.MoveOut);
            Assert.Equal(["Open", "N1", "Exit1", "Help"], s.Children(file.Name));
            OneOrderUnit(md.MoveOut);
            Assert.Equal([file.Name, "Help"], s.Children("MainMenu1"));

            // Undo steps back one move at a time.
            s.Undo();
            Assert.Equal(["Open", "N1", "Exit1", "Help"], s.Children(file.Name));
            Assert.Same(help, md.Selected);
            Assert.True(md.CanMoveOut);
            return Task.CompletedTask;
        });

        // Fixed items (inherited from the ancestor form) do not move: the four moves and Move Up / Move Down are disabled.
        var d = await OpenDescendantAsync();
        await d.DesignAsync(md =>
        {
            Assert.Equal(["FileMenu", "FileOpen", "FileExit", "EditMenu", "HelpMenu", "ToolsMenu"], md.Items.Select(i => i.Name));
            var units = d.Designer.Undo.UndoCount;
            foreach (var name in (string[])["FileMenu", "FileOpen", "EditMenu"])
            {
                md.Select(name);
                Assert.True(md.Selected!.IsInherited);
                Assert.False(md.CanMoveUp || md.CanMoveDown || md.CanMoveOut || md.CanMoveIn, name);
                Assert.False(md.CanMove(md.Selected)); // no drag: the press stays a selection
                Assert.False(md.CanMoveTo(md.Selected, d.Item("ToolsMenu"), MenuDropPosition.After));
                Assert.False(md.MoveTo(md.Selected, d.Item("ToolsMenu"), MenuDropPosition.After));
                md.MoveUp();
                md.MoveDown();
                md.MoveOut();
                md.MoveIn();
            }

            Assert.Equal(units, d.Designer.Undo.UndoCount);

            // The descendant's own items move, but not before the inherited ones of a level: the form refuses that place.
            md.Select("HelpMenu");
            Assert.True(md.CanMove(md.Selected!));
            Assert.False(md.CanMoveUp); // EditMenu before it is inherited
            Assert.True(md.CanMoveDown);
            md.MoveUp();
            Assert.True(md.CanMoveTo(md.Selected!, d.Item("FileMenu"), MenuDropPosition.Before)); // a valid position …
            Assert.False(md.MoveTo(md.Selected!, d.Item("FileMenu"), MenuDropPosition.Before)); // … that changes nothing
            md.Select("FileExit");
            Assert.False(md.CanMoveUp);
            Assert.False(md.CanMoveOut); // directly after File is before the inherited Edit
            md.MoveOut();
            Assert.Equal(units, d.Designer.Undo.UndoCount);
            Assert.Equal(["FileMenu", "EditMenu", "HelpMenu", "ToolsMenu"], d.Children("MainMenu1"));

            // Among themselves and into an inherited item they move as usual.
            md.Select("ToolsMenu");
            Assert.True(md.CanMoveUp);
            md.MoveUp();
            Assert.Equal(["FileMenu", "EditMenu", "ToolsMenu", "HelpMenu"], d.Children("MainMenu1"));
            Assert.True(md.CanMoveIn);
            md.MoveIn();
            Assert.Equal(["FileMenu", "EditMenu", "HelpMenu"], d.Children("MainMenu1"));
            Assert.Equal(["ToolsMenu"], d.Children("EditMenu"));
            Assert.Equal((units + 2, UndoTitles.Order), (d.Designer.Undo.UndoCount, d.Designer.Undo.UndoTitleKey));
            Assert.True(md.CanMoveOut); // after Edit: behind the inherited items
            md.MoveOut();
            Assert.Equal(["FileMenu", "EditMenu", "ToolsMenu", "HelpMenu"], d.Children("MainMenu1"));
            return Task.CompletedTask;
        });
    }

    /// <summary>D-156 (DSG-SUR-254): the zones of a row — quarters around the middle half; a separator row has no "into".</summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-254")]
    [InlineData("&Open", 0.0, MenuDropPosition.Before)]
    [InlineData("&Open", 0.24, MenuDropPosition.Before)]
    [InlineData("&Open", 0.25, MenuDropPosition.Into)]
    [InlineData("&Open", 0.5, MenuDropPosition.Into)]
    [InlineData("&Open", 0.74, MenuDropPosition.Into)]
    [InlineData("&Open", 0.75, MenuDropPosition.After)]
    [InlineData("&Open", 1.0, MenuDropPosition.After)]
    [InlineData("", 0.5, MenuDropPosition.Into)]
    [InlineData("-", 0.0, MenuDropPosition.Before)]
    [InlineData("-", 0.49, MenuDropPosition.Before)]
    [InlineData("-", 0.5, MenuDropPosition.After)]
    [InlineData("-", 0.99, MenuDropPosition.After)]
    public void DropPositionOfRowZones(string caption, double fraction, MenuDropPosition expected)
    {
        var form = Form.ReplaceLineEndings("\n").Replace("    DesignTop = 120\n", $"    DesignTop = 120\n    object Item1: TMenuItem\n      Caption = '{caption}'\n    end\n", StringComparison.Ordinal);
        var parsed = FormParser.Parse(form, "MainForm.form", CodeSyncTests.Catalog);
        Assert.Empty(parsed.Diagnostics);
        Assert.Equal(expected, MenuDesignerViewModel.DropPositionAt(parsed.Document!.FindByName("Item1")!, fraction));
    }

    /// <summary>The component metadata of the UI framework (menus with <c>Images</c>, items with <c>ImageIndex</c> and <c>Action</c>, image lists, actions).</summary>
    private static readonly Lazy<FreexComp.Designer.Catalog.ComponentCatalog> Framework = new(() =>
    {
        var result = FreexComp.Designer.Catalog.CatalogLoader.Load([Path.Combine(DataDesignTests.FrameworkDesignDir, "index.json")]);
        Assert.Empty(result.Problems);
        return result.Catalog;
    });

    private static async Task<Session> OpenWithFrameworkAsync(string unit, string form)
    {
        var dialogs = new FakeDialogService();
        var f = new DesignerFixture(o => o.Dialogs = dialogs);
        ((FreexComp.Ide.Core.Documents.DocumentService)f.Services.Documents).DesignCatalog = Framework.Value;
        return new Session(f, dialogs, await f.OpenAsync(unit, form.ReplaceLineEndings("\n")));
    }

    /// <summary>A binary value in the form syntax: hex digits, 32 bytes a line.</summary>
    internal static string HexLines(byte[] bytes, string indent) =>
        string.Concat(bytes.Chunk(32).Select(line => indent + Convert.ToHexString(line) + "\n"));

    /// <summary>The image-list fixture of four 16 × 16 cells (<c>TestData/imagelists/alpha4.bin</c>).</summary>
    internal static byte[] FourCells => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "imagelists", "alpha4.bin"));

    /// <summary>A menu with two items and a separator; three image lists: four cells, no <c>Bitmap</c> value, an undecodable one.</summary>
    private static string ImageForm => """
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
            object FileOpen: TMenuItem
              Caption = '&Open'
            end
            object FileSave: TMenuItem
              Caption = '&Save'
              ImageIndex = 9
            end
            object N1: TMenuItem
              Caption = '-'
            end
          end
          object ImageList1: TImageList
            DesignLeft = 360
            DesignTop = 120
            Bitmap = {
              $HEX$
            }
          end
          object EmptyList: TImageList
            DesignLeft = 400
            DesignTop = 120
          end
          object BrokenList: TImageList
            DesignLeft = 440
            DesignTop = 120
            Bitmap = {
              0001
            }
          end
        end

        """.ReplaceLineEndings("\n").Replace("      $HEX$\n", HexLines(FourCells, "      "), StringComparison.Ordinal);

    private static readonly string ImageUnit = Unit.Replace("Classes, Controls, Forms, Menus;", "Classes, Controls, Forms, Menus, ImgList;", StringComparison.Ordinal)
        .Replace("    MainMenu1: TMainMenu;", "    MainMenu1: TMainMenu;\r\n    FileOpen: TMenuItem;\r\n    FileSave: TMenuItem;\r\n    N1: TMenuItem;\r\n"
            + "    ImageList1: TImageList;\r\n    EmptyList: TImageList;\r\n    BrokenList: TImageList;", StringComparison.Ordinal);

    /// <summary>
    /// D-157 (DSG-SUR-258): the <c>Image</c> field follows the menu's <c>Images</c> — the cells of a readable image list,
    /// <c>(None)</c>, the stored number when it is not among them; disabled with its reason otherwise; a choice is one
    /// "Property" unit.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-258")]
    public Task ImageChoicesFollowTheMenuImages() => TestUiThread.RunAsync(ImageChoicesFollowTheMenuImagesOnUiThread);

    private static async Task ImageChoicesFollowTheMenuImagesOnUiThread()
    {
        var s = await OpenWithFrameworkAsync(ImageUnit, ImageForm);
        var cells = ImageListData.Decode(FourCells).Data!.Cells;
        Assert.Equal(4, cells.Length);
        await s.DesignAsync(md =>
        {
            var surface = s.Designer.Surface!;
            void Images(FormValue? value) => Assert.True(surface.Commit(UndoTitles.Property,
                [value is null ? new RemoveProperty("MainMenu1", "Images") : new SetProperty("MainMenu1", "Images", value)]));
            string? Stored(string item) => DesignerFixture.Stored(s.Designer, item, "ImageIndex");
            IEnumerable<int> Indices() => md.ImageChoices.Select(c => c.Index);

            // No Images value: disabled, the tooltip says what to do; the stored number is still shown.
            md.Select("FileOpen");
            Assert.False(md.IsImageEnabled);
            Assert.Equal("Assign an image list to the Images property of MainMenu1 to choose images here.", md.ImageTip);
            Assert.Equal([-1], Indices());
            Assert.Equal((-1, "(None)", "(None)"), (md.SelectedImage.Index, md.SelectedImage.Text, md.SelectedImage.AutomationName));
            var units = s.Designer.Undo.UndoCount;
            Assert.False(md.CommitImage(2));
            Assert.Equal(units, s.Designer.Undo.UndoCount);
            md.Select("FileSave");
            Assert.Equal([-1, 9], Indices());
            Assert.Equal((9, "9", "Image 9"), (md.SelectedImage.Index, md.SelectedImage.Text, md.SelectedImage.AutomationName));
            Assert.Null(md.SelectedImage.Cell);

            // A readable source: (None), one entry per cell with its picture; index 9 of four cells is one more entry, selected.
            Images(new IdentValue("ImageList1"));
            Assert.True(md.IsImageEnabled);
            Assert.Null(md.ImageTip);
            Assert.Equal([-1, 0, 1, 2, 3, 9], Indices());
            Assert.Equal(["(None)", "0", "1", "2", "3", "9"], md.ImageChoices.Select(c => c.Text));
            Assert.Null(md.ImageChoices[0].Cell);
            Assert.Equal(cells.ToArray(), md.ImageChoices.Skip(1).Take(4).Select(c => c.Cell!).ToArray());
            Assert.Null(md.ImageChoices[5].Cell);
            Assert.Equal(9, md.SelectedImage.Index);
            md.Select("FileOpen");
            Assert.Equal([-1, 0, 1, 2, 3], Indices());
            Assert.Equal(-1, md.SelectedImage.Index);
            var choices = md.ImageChoices;
            md.Select("N1");
            md.Select("FileOpen");
            Assert.Same(choices, md.ImageChoices); // unchanged entries are not made again

            // Choosing an entry is one "Property" unit; the same value records nothing; (None) removes the property.
            units = s.Designer.Undo.UndoCount;
            Assert.True(md.CommitImage(2));
            Assert.Equal("2", Stored("FileOpen"));
            Assert.Equal((units + 1, UndoTitles.Property), (s.Designer.Undo.UndoCount, s.Designer.Undo.UndoTitleKey));
            Assert.Equal(2, md.SelectedImage.Index);
            Assert.True(md.CommitImage(2));
            Assert.Equal(units + 1, s.Designer.Undo.UndoCount);
            Assert.True(md.CommitImage(-1));
            Assert.Null(Stored("FileOpen"));
            Assert.Equal((units + 2, UndoTitles.Property), (s.Designer.Undo.UndoCount, s.Designer.Undo.UndoTitleKey));
            Assert.True(md.CommitImage(-1));
            Assert.Equal(units + 2, s.Designer.Undo.UndoCount);
            s.Undo();
            Assert.Equal("2", Stored("FileOpen"));
            Assert.Equal(2, md.SelectedImage.Index); // the field follows a change behind the dialog
            Assert.False(md.CommitImage(7)); // no such entry
            md.Select("FileSave");
            Assert.True(md.CommitImage(1));
            Assert.Equal([-1, 0, 1, 2, 3], Indices()); // the stored number is among the entries now
            s.Undo();
            Assert.Equal([-1, 0, 1, 2, 3, 9], Indices());

            // A separator has no image and no shortcut.
            md.Select("N1");
            Assert.False(md.IsImageEnabled || md.IsShortCutEnabled);
            Assert.Null(md.ImageTip);

            // An image list without a Bitmap value is a readable source with no cells.
            md.Select("FileOpen");
            Images(new IdentValue("EmptyList"));
            Assert.True(md.IsImageEnabled);
            Assert.Null(md.ImageTip);
            Assert.Equal([-1, 2], Indices());

            // Not a readable source — an undecodable Bitmap, a name not in the document, a list of another form, a component
            // that is no image list: disabled, the tooltip names the stored value; the stored number is still shown.
            foreach (var (value, text) in ((FormValue, string)[])
                [
                    (new IdentValue("BrokenList"), "BrokenList"), (new IdentValue("Missing1"), "Missing1"),
                    (new QualifiedIdentValue(["DataModule1", "ImageList1"]), "DataModule1.ImageList1"), (new IdentValue("MainMenu1"), "MainMenu1"),
                ])
            {
                Images(value);
                Assert.False(md.IsImageEnabled, text);
                Assert.Equal($"The images of {text} cannot be shown here. Set ImageIndex in the property grid.", md.ImageTip);
                Assert.Equal([-1, 2], Indices());
                Assert.Equal(2, md.SelectedImage.Index);
                Assert.False(md.CommitImage(-1));
            }

            // The entries follow the image list itself: deleted — no pictures; Undo — the four cells again.
            Images(new IdentValue("ImageList1"));
            Assert.Equal([-1, 0, 1, 2, 3], Indices());
            Assert.True(surface.DeleteObjects([s.Item("ImageList1")], s.Item("MainMenu1")));
            Assert.False(md.IsImageEnabled);
            Assert.NotNull(md.ImageTip);
            Assert.DoesNotContain(md.ImageChoices, c => c.Cell is not null);
            s.Undo();
            Assert.True(md.IsImageEnabled);
            Assert.Equal(cells.ToArray(), md.ImageChoices.Skip(1).Select(c => c.Cell!).ToArray());
            s.Undo(); // the Images assignment
            Assert.False(md.IsImageEnabled);
            return Task.CompletedTask;
        });
    }

    private const string ActionForm = """
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
            object FileOpen: TMenuItem
              Caption = '&Open'
            end
          end
          object ActionList1: TActionList
            DesignLeft = 360
            DesignTop = 120
            object OpenAction: TAction
              Caption = '&Open…'
            end
            object ExitAction: TAction
              Caption = 'E&xit'
            end
          end
        end

        """;

    /// <summary>
    /// D-157, card F05-086 scope "link to action": the <c>Action</c> row of the embedded grid lists the actions of the form's
    /// action list; choosing one stores <c>Action = &lt;name&gt;</c> as one unit.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-197")]
    public Task ActionRowLinksAnAction() => TestUiThread.RunAsync(ActionRowLinksAnActionOnUiThread);

    private static async Task ActionRowLinksAnActionOnUiThread()
    {
        var unit = Unit.Replace("Classes, Controls, Forms, Menus;", "Classes, Controls, Forms, Menus, ActnList;", StringComparison.Ordinal)
            .Replace("    MainMenu1: TMainMenu;", "    MainMenu1: TMainMenu;\r\n    FileOpen: TMenuItem;\r\n    ActionList1: TActionList;\r\n"
                + "    OpenAction: TAction;\r\n    ExitAction: TAction;", StringComparison.Ordinal);
        var s = await OpenWithFrameworkAsync(unit, ActionForm);
        await s.DesignAsync(md =>
        {
            Assert.Equal("FileOpen", md.Selected!.Name);
            md.Grid.Page = InspectorPage.Properties;
            Assert.Contains(md.Grid.Rows, r => r.Kind == FreexComp.Designer.Inspector.PropertyRowKind.Property && r.Path == "Action");
            Assert.Equal(["(none)", "ExitAction", "OpenAction"], md.Grid.ValueList("Action")); // the actions, not the list or the menu
            var units = s.Designer.Undo.UndoCount;

            void Choose(string entry)
            {
                md.Grid.SetCurrent("Action");
                Assert.True(md.Grid.BeginEdit());
                md.Grid.EditText = entry;
                Assert.True(md.Grid.Commit(), md.Grid.EditError);
            }

            Choose("OpenAction");
            Assert.Equal("OpenAction", ((IdentValue)s.Item("FileOpen").FindProperty("Action")!.Value).Name);
            Assert.Equal((units + 1, UndoTitles.Property), (s.Designer.Undo.UndoCount, s.Designer.Undo.UndoTitleKey));
            Assert.Contains("      Action = OpenAction\n", s.FormText.ReplaceLineEndings("\n"), StringComparison.Ordinal);
            Assert.Equal("OpenAction", md.Grid.Rows.Single(r => r.Kind == FreexComp.Designer.Inspector.PropertyRowKind.Property && r.Path == "Action").Display);

            // (none) removes the link; Undo restores it.
            Choose("(none)");
            Assert.Null(s.Item("FileOpen").FindProperty("Action"));
            Assert.Equal(units + 2, s.Designer.Undo.UndoCount);
            s.Undo();
            Assert.Equal("OpenAction", ((IdentValue)s.Item("FileOpen").FindProperty("Action")!.Value).Name);
            return Task.CompletedTask;
        });
    }
}
