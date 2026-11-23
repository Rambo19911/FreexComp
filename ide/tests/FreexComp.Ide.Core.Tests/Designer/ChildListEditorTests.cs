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
    }

    private static async Task<Session> OpenAsync()
    {
        var dialogs = new FakeDialogService();
        var f = new DesignerFixture(o => o.Dialogs = dialogs);
        var document = await f.OpenAsync(Unit, Form);
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

            // Empty: the placeholder, New only; one item class (TAction), so New is a plain button.
            Assert.True(e.IsEmpty);
            Assert.Empty(e.Selection);
            Assert.Equal(["TAction"], e.ItemClasses);
            Assert.False(e.HasClassChoice);
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
}
