using FreexComp.Designer.Model;
using FreexComp.Designer.Surface;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;
using DesignCommandIds = FreexComp.Designer.Surface.Commands;

namespace FreexComp.Ide.Core.Tests.Designer;

/// <summary>The Child List Editor (design-surface.md DSG-SUR-198, DSG-SUR-199; dialogs.md §27; D-099) on the test metadata.</summary>
[Trait("Phase", "5")]
public sealed class ChildListEditorTests
{
    internal static readonly string Unit = string.Join("\r\n",
        "unit MainForm;",
        "",
        "interface",
        "",
        "uses",
        "  Classes, Controls, Forms, ActnList;",
        "",
        "type",
        "  TMainForm = class(TForm)",
        "    ActionList1: TActionList;",
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
          PixelsPerInch = 96
          object ActionList1: TActionList
            DesignLeft = 320
            DesignTop = 120
          end
        end

        """;

    /// <summary>The item classes of an action list in picker order (DSG-SUR-216): <c>TAction</c>, section Edit, section File.</summary>
    private static readonly string[] ActionClasses =
    [
        "TAction", "TEditCut", "TEditCopy", "TEditPaste", "TEditSelectAll", "TEditUndo", "TEditDelete", "TFileOpen", "TFileSaveAs", "TFileExit",
    ];

    private sealed record Session(DesignerFixture Fixture, FakeDialogService Dialogs, FormDocumentViewModel Document)
    {
        public FormDesignerViewModel Designer => Document.Designer;

        public string Code => Document.Buffer.Text;

        public string FormText => Designer.CurrentFormText();

        public FormObject Item(string name) => DesignerFixture.Object(Designer, name);

        public IReadOnlyList<string> Children(string name) => [.. Item(name).Children.Select(c => c.Name)];

        /// <summary>Opens the Child List Editor on ActionList1 (designer.editComponent), runs <paramref name="drive"/> in it, then Close.</summary>
        public async Task<ChildListEditorViewModel> EditAsync(Func<ChildListEditorViewModel, Task> drive)
        {
            ChildListEditorViewModel? shown = null;
            System.Runtime.ExceptionServices.ExceptionDispatchInfo? failure = null;
            Dialogs.Script(ChildListEditorViewModel.DialogName, async dialog =>
            {
                shown = Assert.IsType<ChildListEditorViewModel>(dialog);
                try
                {
                    await drive(shown);
                    Assert.False(shown.IsClosed); // no operation leaves the dialog (DSG-SUR-198)
                }
                catch (Exception e)
                {
                    failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e);
                }

                await shown.PressCancelAsync(); // Escape = Close (UI-DLG-261)
            });
            DesignerFixture.Select(Designer, "ActionList1");
            await Fixture.Services.Commands.ExecuteAsync(DesignCommandIds.EditComponent);
            failure?.Throw();
            return shown ?? throw new InvalidOperationException("The Child List Editor did not open.");
        }

        public void Undo() => Designer.Surface!.Execute(DesignCommandIds.Undo);

        public void Redo() => Designer.Surface!.Execute(DesignCommandIds.Redo);

        /// <summary>Saves the document and returns the <c>.form</c> text on disk.</summary>
        public async Task<string> SaveAsync()
        {
            Assert.True(await Fixture.Services.Documents.SaveAsync(Document));
            return Fixture.Disk(DesignerFixture.FormPath);
        }

        /// <summary>The interface <c>uses</c> clause of the unit: the text between <c>uses</c> and its semicolon.</summary>
        public string InterfaceUses
        {
            get
            {
                var start = Code.IndexOf("\r\nuses\r\n", StringComparison.Ordinal);
                var end = Code.IndexOf(';', start);
                Assert.True(start >= 0 && end > start && end < Code.IndexOf("\r\ntype\r\n", StringComparison.Ordinal));
                return Code[(start + 8)..end].Trim();
            }
        }
    }

    private static async Task<Session> OpenAsync(string? unit = null, string? form = null)
    {
        var dialogs = new FakeDialogService();
        var f = new DesignerFixture(o => o.Dialogs = dialogs);
        var document = await f.OpenAsync(unit ?? Unit, form ?? Form);
        return new Session(f, dialogs, document);
    }

    /// <summary>A property row of the embedded grid committed with <paramref name="text"/>.</summary>
    private static void SetInGrid(ChildListEditorViewModel editor, string property, string text)
    {
        editor.Grid.Page = InspectorPage.Properties;
        editor.Grid.SetCurrent(property);
        Assert.True(editor.Grid.BeginEdit());
        editor.Grid.EditText = text;
        Assert.True(editor.Grid.Commit(), editor.Grid.EditError);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-SUR-198")]
    [Trait("Req", "DSG-SUR-196")]
    public Task NewDeleteMoveActions() => TestUiThread.RunAsync(NewDeleteMoveActionsOnUiThread);

    private static async Task NewDeleteMoveActionsOnUiThread()
    {
        var s = await OpenAsync();
        var editor = await s.EditAsync(async e =>
        {
            Assert.Equal("Edit ActionList1", e.Title);
            Assert.Equal((640.0, (double?)400), (e.Width, e.Height));
            Assert.True(e.CanResize);
            var focus = new List<string>();
            e.GridFocusRequested += (_, path) => focus.Add(path);

            // Empty: the placeholder, New only; the item classes of the metadata (TAction first, then the standard
            // actions), so New has its class picker and its button part creates the first class.
            Assert.True(e.IsEmpty);
            Assert.Empty(e.Selection);
            Assert.Equal(ActionClasses, e.ItemClasses);
            Assert.True(e.HasClassChoice);
            Assert.True(e.CanNew);
            Assert.False(e.CanDelete || e.CanMoveUp || e.CanMoveDown);

            // New appends, selects the new child and asks for the grid's Caption row (the class has one).
            e.New();
            e.New();
            e.New();
            Assert.Equal(["Action1", "Action2", "Action3"], s.Children("ActionList1"));
            Assert.Equal(["Action3"], e.Selection.Select(o => o.Name));
            Assert.Equal(["Caption", "Caption", "Caption"], focus);
            Assert.Equal("Caption", e.Grid.CurrentPath);
            Assert.Contains("    Action1: TAction;", s.Code, StringComparison.Ordinal);

            // Row text: the name, then " – {Caption}" when the caption is not empty; the list updates from the grid.
            Assert.Equal(["Action1", "Action2", "Action3"], e.Rows.Select(r => r.Text));
            SetInGrid(e, "Caption", "&Open…");
            Assert.Equal("Action3 – &Open…", e.Rows[2].Text);

            // Move Up / Move Down need exactly one selected child.
            Assert.True(e.CanMoveUp);
            Assert.False(e.CanMoveDown);
            e.MoveUp();
            Assert.Equal(["Action1", "Action3", "Action2"], s.Children("ActionList1"));
            e.MoveUp();
            Assert.Equal(["Action3", "Action1", "Action2"], s.Children("ActionList1"));
            Assert.False(e.CanMoveUp);
            e.MoveDown();
            Assert.Equal(["Action1", "Action3", "Action2"], s.Children("ActionList1"));
            e.Select("Action1", "Action2");
            Assert.False(e.CanMoveUp || e.CanMoveDown);

            // Several selected: the grid shows their common properties; Delete removes all of them (fields included).
            Assert.Equal(["Action1", "Action2"], e.Selection.Select(o => o.Name));
            Assert.Contains(e.Grid.Rows, r => r.Path == "Caption");
            Assert.True(e.CanDelete);
            e.Delete();
            Assert.Equal(["Action3"], s.Children("ActionList1"));
            Assert.DoesNotContain("Action1: TAction", s.Code, StringComparison.Ordinal);
            Assert.DoesNotContain("Action2: TAction", s.Code, StringComparison.Ordinal);
            Assert.Equal(["Action3"], e.Selection.Select(o => o.Name)); // the remaining neighbour

            e.Delete();
            Assert.True(e.IsEmpty);
            Assert.Empty(e.Selection);
            await Task.CompletedTask;
        });
        Assert.True(editor.IsClosed);
        Assert.Equal(["ActionList1"], s.Designer.Surface!.Selection.Items.Select(o => o.Name)); // the surface selection is kept
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-SUR-198")]
    [Trait("Req", "UI-DLG-260")]
    public Task DoubleClickCreatesOnExecuteHandler() => TestUiThread.RunAsync(DoubleClickCreatesOnExecuteHandlerOnUiThread);

    private static async Task DoubleClickCreatesOnExecuteHandlerOnUiThread()
    {
        var s = await OpenAsync();
        await s.EditAsync(e =>
        {
            e.New();
            SetInGrid(e, "Name", "FileOpen");

            // Enter / double-click on a row: the default handler (OnExecute) is created and assigned; the dialog and the
            // Design view stay; the Events page shows the method name.
            e.Activate(s.Item("FileOpen"));
            Assert.Equal("FileOpenExecute", ((IdentValue)s.Item("FileOpen").FindProperty("OnExecute")!.Value).Name);
            Assert.Contains("    procedure FileOpenExecute(Sender: TObject);", s.Code, StringComparison.Ordinal);
            Assert.Contains("procedure TMainForm.FileOpenExecute(Sender: TObject);", s.Code, StringComparison.Ordinal);
            Assert.Equal(DocumentView.Design, s.Document.ActiveView);
            Assert.Equal(InspectorPage.Events, e.Grid.Page);
            Assert.Equal("FileOpenExecute", Assert.Single(e.Grid.EventRows, r => r.Name == "OnExecute").Value);

            // Again: the existing handler is kept (no second method, no unit).
            var units = s.Designer.Undo.UndoCount;
            e.Activate(s.Item("FileOpen"));
            Assert.Equal(units, s.Designer.Undo.UndoCount);
            return Task.CompletedTask;
        });
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-SUR-199")]
    [Trait("Req", "UI-DLG-261")]
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

        await s.EditAsync(e =>
        {
            Step(() => e.New());
            Step(() => e.New());
            Step(() => SetInGrid(e, "Caption", "&Save"));
            Step(e.MoveUp);
            Step(() => e.Activate(e.Selection[0]));
            Step(() => SetInGrid(e, "Name", "FileSave"));
            e.Select("Action1");
            Step(e.Delete);
            return Task.CompletedTask;
        });

        Assert.Equal(
        [
            (UndoTitles.Add, "TAction"), (UndoTitles.Add, "TAction"), (UndoTitles.Property, null), (UndoTitles.Order, null),
            (UndoTitles.Event, null), (UndoTitles.Rename, null), (UndoTitles.Delete, null),
        ], titles.Select(t => t.Key == UndoTitles.Add ? t : (t.Key, null)));

        // Close only closed: Edit › Undo steps back through the operations one by one, code included.
        for (var i = titles.Count; i > 0; i--)
        {
            Assert.Equal(i, s.Designer.Undo.UndoCount);
            s.Undo();
        }

        Assert.Equal(form, s.FormText);
        Assert.Equal(code, s.Code);
    }

    /// <summary>The names of the objects nested directly in <c>ActionList1</c> in the order of a <c>.form</c> text.</summary>
    private static string[] ActionsInForm(string form) =>
        [.. form.Split('\n').Where(l => l.StartsWith("    object ", StringComparison.Ordinal)).Select(l => l[11..l.IndexOf(':', StringComparison.Ordinal)])];

    [Fact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-216")]
    [Trait("Req", "DSG-SUR-221")]
    public Task StandardActionPicker_ListsClassesBySection() => TestUiThread.RunAsync(StandardActionPickerOnUiThread);

    private static async Task StandardActionPickerOnUiThread()
    {
        var s = await OpenAsync();
        await s.EditAsync(e =>
        {
            // TAction (no initial Category) first; then one section per initial Category in catalog order, each behind
            // its header row; a class row shows the class name without its leading T.
            Assert.Equal(ActionClasses, e.ItemClasses);
            Assert.True(e.HasClassChoice);
            Assert.Equal(
            [
                ("TAction", "Action", null),
                (null, "Edit", "Edit"),
                ("TEditCut", "EditCut", "Edit"), ("TEditCopy", "EditCopy", "Edit"), ("TEditPaste", "EditPaste", "Edit"),
                ("TEditSelectAll", "EditSelectAll", "Edit"), ("TEditUndo", "EditUndo", "Edit"), ("TEditDelete", "EditDelete", "Edit"),
                (null, "File", "File"),
                ("TFileOpen", "FileOpen", "File"), ("TFileSaveAs", "FileSaveAs", "File"), ("TFileExit", "FileExit", "File"),
            ], e.PickerRows.Select(r => (r.ClassName, r.Text, r.Category)));
            Assert.Equal([1, 8], e.PickerRows.Select((r, i) => (r, i)).Where(p => p.r.IsHeader).Select(p => p.i));

            // The button part of New (no class named) creates the first class of the list; a header is no class.
            e.New();
            Assert.Equal("TAction", s.Item("Action1").TypeName);
            e.New("Edit");
            e.New("TForm");
            Assert.Equal(["Action1"], s.Children("ActionList1"));
            return Task.CompletedTask;
        });

        // DSG-SUR-221: the editor never interprets class names — a class of the form that the catalog does not know
        // stays a preserved child, shown in the group of its Category, read-only.
        var legacy = await OpenAsync(
            Unit.Replace("    ActionList1: TActionList;\r\n", "    ActionList1: TActionList;\r\n    Legacy1: TLegacyAction;\r\n    Action1: TAction;\r\n", StringComparison.Ordinal),
            Form.Replace("    DesignTop = 120\n", "    DesignTop = 120\n    object Legacy1: TLegacyAction\n      Category = 'Edit'\n    end\n    object Action1: TAction\n    end\n", StringComparison.Ordinal));
        var form = legacy.FormText;
        await legacy.EditAsync(e =>
        {
            Assert.True(legacy.Item("Legacy1").IsPreserved);
            Assert.Equal([("(No Category)", true), ("Action1", false), ("Edit", true), ("Legacy1", false)], e.Rows.Select(r => (r.Text, r.IsHeader)));
            e.Select("Legacy1");
            Assert.Equal(["Legacy1"], e.Selection.Select(o => o.Name));
            Assert.False(e.CanDelete);
            e.Delete();
            Assert.Equal(["Legacy1", "Action1"], legacy.Children("ActionList1"));
            return Task.CompletedTask;
        });
        Assert.Equal(form, legacy.FormText);
    }

    [Fact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-215")]
    [Trait("Req", "DSG-SUR-220")]
    public Task NewStandardAction_WritesFormFieldAndUses() => TestUiThread.RunAsync(NewStandardActionOnUiThread);

    private static async Task NewStandardActionOnUiThread()
    {
        var s = await OpenAsync();
        var (form, code) = (s.FormText, s.Code);
        Assert.Equal("Classes, Controls, Forms, ActnList", s.InterfaceUses);
        var undo = s.Designer.Undo.UndoCount;
        await s.EditAsync(e =>
        {
            var focus = new List<string>();
            e.GridFocusRequested += (_, path) => focus.Add(path);
            e.New("TEditCopy");

            // Appended as the last child, selected, shown in the group of its Category; the grid's Caption row is current.
            Assert.Equal(["EditCopy1"], s.Children("ActionList1"));
            Assert.Equal(["EditCopy1"], e.Selection.Select(o => o.Name));
            Assert.Equal(["Caption"], focus);
            Assert.Equal([("Edit", true), ("EditCopy1 – &Copy", false)], e.Rows.Select(r => (r.Text, r.IsHeader)));
            return Task.CompletedTask;
        });

        // One "Add TEditCopy" unit: the object with its initial values, the field and the uses entry.
        Assert.Equal(undo + 1, s.Designer.Undo.UndoCount);
        Assert.Equal((UndoTitles.Add, "TEditCopy"), (s.Designer.Undo.UndoTitleKey, s.Designer.Undo.Peek!.TitleArgument));
        const string Saved = """
            object MainForm: TMainForm
              Left = 200
              Top = 120
              Caption = 'Hello Form'
              ClientWidth = 400
              ClientHeight = 300
              PixelsPerInch = 96
              object ActionList1: TActionList
                DesignLeft = 320
                DesignTop = 120
                object EditCopy1: TEditCopy
                  Caption = '&Copy'
                  Category = 'Edit'
                  Hint = 'Copy'
                  ShortCut = 16451
                end
              end
            end

            """;
        Assert.Equal(Saved.Replace("\r\n", "\n", StringComparison.Ordinal), await s.SaveAsync());
        Assert.Contains("    ActionList1: TActionList;\r\n    EditCopy1: TEditCopy;\r\n", s.Code, StringComparison.Ordinal);
        Assert.Equal("Classes, Controls, Forms, ActnList, StdActns", s.InterfaceUses);
        var (formAfter, codeAfter) = (s.FormText, s.Code);

        // One undo step removes all three; redo restores them.
        s.Undo();
        Assert.Equal(undo, s.Designer.Undo.UndoCount);
        Assert.Equal(form, s.FormText);
        Assert.Equal(code, s.Code);
        Assert.Empty(s.Children("ActionList1"));
        s.Redo();
        Assert.Equal(formAfter, s.FormText);
        Assert.Equal(codeAfter, s.Code);
        Assert.Equal(["EditCopy1"], s.Children("ActionList1"));
    }

    [Fact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-215")]
    public Task NewPlainAction_WritesFormAndField() => TestUiThread.RunAsync(NewPlainActionOnUiThread);

    private static async Task NewPlainActionOnUiThread()
    {
        var s = await OpenAsync();
        var code = s.Code;
        await s.EditAsync(e =>
        {
            e.New("TAction");
            Assert.Equal(["Action1"], e.Rows.Select(r => r.Text)); // no category: a flat list
            return Task.CompletedTask;
        });

        // TAction has no initial values: the object alone, nested in the action list; its field; the uses clause as it was.
        const string Saved = """
            object MainForm: TMainForm
              Left = 200
              Top = 120
              Caption = 'Hello Form'
              ClientWidth = 400
              ClientHeight = 300
              PixelsPerInch = 96
              object ActionList1: TActionList
                DesignLeft = 320
                DesignTop = 120
                object Action1: TAction
                end
              end
            end

            """;
        Assert.Equal(Saved.Replace("\r\n", "\n", StringComparison.Ordinal), await s.SaveAsync());
        Assert.Equal(code.Replace("    ActionList1: TActionList;\r\n", "    ActionList1: TActionList;\r\n    Action1: TAction;\r\n", StringComparison.Ordinal), s.Code);
        Assert.Equal("Classes, Controls, Forms, ActnList", s.InterfaceUses);
    }

    [Fact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-217")]
    [Trait("Req", "DSG-SUR-218")]
    [Trait("Req", "DSG-SUR-219")]
    [Trait("Req", "DSG-SUR-220")]
    public Task Categories_GroupListAndMoveWithinGroup() => TestUiThread.RunAsync(CategoriesOnUiThread);

    private static async Task CategoriesOnUiThread()
    {
        var s = await OpenAsync();
        await s.EditAsync(e =>
        {
            static (string Text, bool IsHeader)[] Shape(ChildListEditorViewModel editor) => [.. editor.Rows.Select(r => (r.Text, r.IsHeader))];

            // No child has a category: the flat list of before.
            e.New("TAction");
            e.New("TAction");
            Assert.False(e.IsGrouped);
            Assert.Equal([("Action1", false), ("Action2", false)], Shape(e));
            Assert.All(e.Rows, r => Assert.Null(r.Category));
            Assert.Equal("Action2", e.Rows[1].AccessibleName);

            // One Category edit in the grid regroups at once and keeps the selection: (No Category) first.
            SetInGrid(e, "Category", "Edit");
            Assert.True(e.IsGrouped);
            Assert.Equal(["Action2"], e.Selection.Select(o => o.Name));
            Assert.Equal([("(No Category)", true), ("Action1", false), ("Edit", true), ("Action2", false)], Shape(e));
            Assert.Equal(["(none)", "Action1", "Edit", "Action2"], e.Rows.Select(r => r.Name));
            Assert.Equal(["", "", "Edit", "Edit"], e.Rows.Select(r => r.Category));
            Assert.Equal(["(No Category)", "(No Category), Action1", "Edit", "Edit, Action2"], e.Rows.Select(r => r.AccessibleName));

            // New children are appended as the last child and shown in the group of their initial Category.
            e.New("TEditCopy");
            e.New("TFileOpen");
            e.New("TEditCut");
            e.New("TAction");
            Assert.Equal(["Action1", "Action2", "EditCopy1", "FileOpen1", "EditCut1", "Action3"], s.Children("ActionList1"));
            Assert.Equal(["Action3"], e.Selection.Select(o => o.Name));
            Assert.Equal(
            [
                ("(No Category)", true), ("Action1", false), ("Action3", false),
                ("Edit", true), ("Action2", false), ("EditCopy1 – &Copy", false), ("EditCut1 – Cu&t", false),
                ("File", true), ("FileOpen1 – &Open…", false),
            ], Shape(e));
            Assert.Equal("Edit, EditCut1 – Cut", e.Rows[6].AccessibleName);

            // Group order = order of the first child with the value; the no-category group stays first. The
            // comparison is ordinal: "edit" is another group than "Edit".
            e.Select("Action1");
            SetInGrid(e, "Category", "File");
            Assert.Equal(["(No Category)", "File", "Edit"], e.Rows.Where(r => r.IsHeader).Select(r => r.Text));
            Assert.Equal(["Action3", "Action1", "FileOpen1 – &Open…", "Action2", "EditCopy1 – &Copy", "EditCut1 – Cu&t"],
                e.Rows.Where(r => !r.IsHeader).Select(r => r.Text));
            e.Select("Action3");
            SetInGrid(e, "Category", "edit");
            Assert.Equal(["File", "Edit", "edit"], e.Rows.Where(r => r.IsHeader).Select(r => r.Text));
            SetInGrid(e, "Category", "");
            Assert.Equal(["(No Category)", "File", "Edit"], e.Rows.Where(r => r.IsHeader).Select(r => r.Text));

            // A header row is no child: selecting works on children only, the only child of a group cannot move.
            Assert.Equal(["Action3"], e.Selection.Select(o => o.Name));
            Assert.False(e.CanMoveUp || e.CanMoveDown);

            // Move Up / Move Down exchange the child with its neighbour of the same group in child order — one
            // "Order" unit each; disabled at the group's first / last row although other children lie before / after.
            e.Select("EditCut1");
            Assert.True(e.CanMoveUp);
            Assert.False(e.CanMoveDown); // Action3 follows, in another group
            var units = s.Designer.Undo.UndoCount;
            e.MoveUp();
            Assert.Equal(["Action1", "Action2", "EditCut1", "FileOpen1", "EditCopy1", "Action3"], s.Children("ActionList1"));
            e.MoveUp();
            Assert.Equal(["Action1", "EditCut1", "Action2", "FileOpen1", "EditCopy1", "Action3"], s.Children("ActionList1"));
            Assert.Equal(units + 2, s.Designer.Undo.UndoCount);
            Assert.Equal(UndoTitles.Order, s.Designer.Undo.UndoTitleKey);
            Assert.Equal(["EditCut1"], e.Selection.Select(o => o.Name));
            Assert.False(e.CanMoveUp); // Action1 lies before it, in the group File
            Assert.True(e.CanMoveDown);
            e.MoveUp();
            Assert.Equal(units + 2, s.Designer.Undo.UndoCount);
            Assert.Equal(["Action3", "Action1", "FileOpen1 – &Open…", "EditCut1 – Cu&t", "Action2", "EditCopy1 – &Copy"],
                e.Rows.Where(r => !r.IsHeader).Select(r => r.Text));

            // Across a child of another group: FileOpen1 and Action1 change places, the children between keep theirs.
            e.Select("FileOpen1");
            Assert.True(e.CanMoveUp);
            Assert.False(e.CanMoveDown);
            e.MoveUp();
            Assert.Equal(["FileOpen1", "EditCut1", "Action2", "Action1", "EditCopy1", "Action3"], s.Children("ActionList1"));
            Assert.Equal(units + 3, s.Designer.Undo.UndoCount);
            s.Undo();
            Assert.Equal(["Action1", "EditCut1", "Action2", "FileOpen1", "EditCopy1", "Action3"], s.Children("ActionList1"));
            return Task.CompletedTask;
        });

        // Grouping is a view: the .form holds the component's child order (after the two moves of EditCut1).
        Assert.Equal(["Action1", "EditCut1", "Action2", "FileOpen1", "EditCopy1", "Action3"], ActionsInForm(await s.SaveAsync()));

        // Without any category the list is flat again and Move works on neighbours as before.
        await s.EditAsync(e =>
        {
            foreach (var name in (string[])["Action1", "EditCut1", "Action2", "FileOpen1", "EditCopy1"])
            {
                e.Select(name);
                SetInGrid(e, "Category", "");
            }

            Assert.False(e.IsGrouped);
            Assert.DoesNotContain(e.Rows, r => r.IsHeader);
            e.Select("FileOpen1");
            e.MoveDown();
            Assert.Equal(["Action1", "EditCut1", "Action2", "EditCopy1", "FileOpen1", "Action3"], s.Children("ActionList1"));
            return Task.CompletedTask;
        });
    }
}
