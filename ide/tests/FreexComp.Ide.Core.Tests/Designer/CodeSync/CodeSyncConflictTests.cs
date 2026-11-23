using FreexComp.Designer.Surface;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;
using SurfaceCommands = FreexComp.Designer.Surface.Commands;

namespace FreexComp.Ide.Core.Tests.Designer.CodeSync;

/// <summary>
/// Code sync refusing an edit (DSG-CS-004, DSG-CS-102, D-065 … D-071, I-220): every refusal is a
/// <c>CodeSyncConflict</c>; the form change is then not made (or, on redo, kept without code edits).
/// </summary>
[Trait("Phase", "5")]
public sealed class CodeSyncConflictTests
{
    private const string Conflict = "Msg.Designer.CodeSyncConflict";

    private static void Insert(ITextBuffer buffer, string before, string text) =>
        buffer.ApplyEdits([new TextEdit(buffer.Text.IndexOf(before, StringComparison.Ordinal), 0, text)]);

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-CS-102")]
    public Task RedoAfterExternalNameClashKeepsForm() => TestUiThread.RunAsync(RedoAfterExternalNameClashKeepsFormOnUiThread);

    private static async Task RedoAfterExternalNameClashKeepsFormOnUiThread()
    {
        var f = new DesignerFixture();
        var dialogs = (FakeDialogService)f.Services.Dialogs;
        var document = await f.OpenAsync();
        var designer = document.Designer;

        // The default handler of OkButton is created, then undone.
        DesignerFixture.Select(designer, "OkButton");
        designer.Surface!.Execute(SurfaceCommands.CreateDefaultHandler);
        Assert.Contains("procedure TMainForm.OkButtonClick(Sender: TObject);", document.Buffer.Text, StringComparison.Ordinal);
        Assert.True(document.ShowView(DocumentView.Design));
        designer.Surface!.Execute(SurfaceCommands.Undo);
        Assert.DoesNotContain("OkButtonClick", document.Buffer.Text, StringComparison.Ordinal);
        Assert.Null(DesignerFixture.Stored(designer, "OkButton", "OnClick"));

        // The user declares a field of that name; redo cannot create the handler: the form change stays, no code edits.
        Insert(document.Buffer, "  private\r\n", "    OkButtonClick: Integer;\r\n");
        var unit = document.Buffer.Text;
        designer.Surface!.Execute(SurfaceCommands.Redo);
        Assert.Equal("OkButtonClick", DesignerFixture.Stored(designer, "OkButton", "OnClick"));
        Assert.Equal(unit, document.Buffer.Text);
        Assert.Empty(designer.Undo.Peek!.CodeEdits);
        Assert.Empty(dialogs.Requests); // DSG-CS-102: a redo keeps the form part silently; the save repairs the class
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-CS-010")]
    [Trait("Req", "DSG-CS-004")]
    public Task EditInsideRecomputeWindowIsRefused() => TestUiThread.RunAsync(EditInsideRecomputeWindowIsRefusedOnUiThread);

    private static async Task EditInsideRecomputeWindowIsRefusedOnUiThread()
    {
        var f = new DesignerFixture();
        var dialogs = (FakeDialogService)f.Services.Dialogs;
        dialogs.Answer(Conflict, "OK");
        var document = await f.OpenAsync();
        var designer = document.Designer;
        var surface = designer.Surface!;
        DesignerFixture.Select(designer, "OkButton");
        surface.Execute(SurfaceCommands.Copy);

        // The unit gets a syntax error; within the 300 ms the surface has not become read-only yet.
        Insert(document.Buffer, "  private\r\n", "    ) ;\r\n");
        var unit = document.Buffer.Text;
        Assert.False(surface.IsReadOnly);
        var components = designer.Model!.Root.Children.Count;

        // Placing: refused with the error dialog, nothing created.
        designer.Host.Arm("TButton", sticky: false);
        var client = surface.Geometry.FormClientRect;
        surface.OnPointer(new PointerInput(PointerKind.Down, client.Left + 300, client.Top + 200, PointerButton.Left, InputModifiers.None));
        surface.OnPointer(new PointerInput(PointerKind.Up, client.Left + 300, client.Top + 200, PointerButton.Left, InputModifiers.None));
        Assert.Null(designer.Model!.FindByName("Button1"));
        Assert.Single(dialogs.Requests);

        // Pasting and deleting: refused the same way.
        surface.Execute(SurfaceCommands.Paste);
        Assert.Equal(components, designer.Model!.Root.Children.Count);
        DesignerFixture.Select(designer, "OkButton");
        surface.Execute(SurfaceCommands.Delete);
        Assert.NotNull(designer.Model!.FindByName("OkButton"));

        Assert.Equal(3, dialogs.Requests.Count);
        Assert.All(dialogs.Requests, r => Assert.Equal(Conflict, r.MessageId));
        Assert.All(dialogs.Requests, r => Assert.False(string.IsNullOrWhiteSpace(Assert.Single(r.Args))));
        Assert.Equal(unit, document.Buffer.Text);
        Assert.Equal(0, designer.Undo.UndoCount);
    }
}
