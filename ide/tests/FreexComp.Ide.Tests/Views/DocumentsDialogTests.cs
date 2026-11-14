using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class DocumentsDialogTests
{
    private static async Task<TextDocumentViewModel> OpenAsync(HeadlessHarness h, string name) =>
        Assert.IsType<TextDocumentViewModel>((await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, name))).Value);

    private static void CtrlClick(DialogDriver dlg, string id)
    {
        var control = dlg.Find<Control>(id);
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), dlg.Window)!.Value;
        dlg.Window.MouseDown(point, MouseButton.Left, RawInputModifiers.Control);
        dlg.Window.MouseUp(point, MouseButton.Left, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
    }

    private static string RowName(DialogDriver dlg, string fileName) =>
        dlg.Find<Control>("Row." + fileName).GetLogicalDescendants().OfType<TextBlock>().First().Text!;

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-004")]
    [Trait("Req", "UI-DLG-005")]
    public async Task ActivateAndCloseSelected()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles["A.pas"] = "a";
            o.InitialFiles["B.pas"] = "b";
            o.InitialFiles[Path.Combine("lib", "C.pas")] = "c";
        });
        var a = await OpenAsync(h, "A.pas");
        var b = await OpenAsync(h, "B.pas");
        var c = await OpenAsync(h, Path.Combine("lib", "C.pas"));
        b.Buffer.ApplyEdits([new TextEdit(1, 0, "2")]);
        Dispatcher.UIThread.RunJobs();

        // Rows in tab order with name and folder; the modified one carries the dot; the active one is selected.
        var running = h.Services.Commands.ExecuteAsync("window.documents");
        var dlg = await h.WaitForDialogAsync("Documents");
        Assert.Equal("Documents", dlg.Window.Title);
        Assert.Equal((600.0, 380.0, true), (dlg.Window.Width, dlg.Window.Height, dlg.Window.CanResize));
        Assert.Equal(["Dialog.Documents.Row.A.pas", "Dialog.Documents.Row.B.pas", "Dialog.Documents.Row.C.pas"],
            dlg.FindAll("Dialog.Documents.Row.").Select(AutomationIds.Get));
        Assert.Equal(["A.pas", "● B.pas", "C.pas"], new[] { "A.pas", "B.pas", "C.pas" }.Select(n => RowName(dlg, n)));
        Assert.Contains(dlg.Find<Control>("Row.C.pas").GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == Path.Combine(h.TempDir, "lib"));
        Assert.True(dlg.Find<ListBoxItem>("Row.C.pas").IsSelected);
        Assert.True(dlg.Find<Button>("Activate").IsEnabled);
        Assert.False(dlg.Find<Button>("Save").IsEnabled);   // C is not modified

        // Save the selected modified document: the dialog stays open and the dot goes.
        dlg.Click("Row.B.pas");
        Assert.True(dlg.Find<Button>("Save").IsEnabled);
        dlg.Click("Save");
        await h.WaitForAsync(() => !b.IsModified, "B saved");
        Assert.True(dlg.Window.IsVisible);
        Assert.Equal("B.pas", RowName(dlg, "B.pas"));
        Assert.Equal("b2\r\n", File.ReadAllText(Path.Combine(h.TempDir, "B.pas")));

        // Two rows selected: Activate needs exactly one; Delete closes both; their rows disappear.
        CtrlClick(dlg, "Row.A.pas");
        Assert.False(dlg.Find<Button>("Activate").IsEnabled);
        dlg.Press("Delete");
        await h.WaitForAsync(() => h.Services.Documents.Documents.Count == 1, "A and B closed");
        Assert.True(dlg.Window.IsVisible);
        Assert.Equal(["Dialog.Documents.Row.C.pas"], dlg.FindAll("Dialog.Documents.Row.").Select(AutomationIds.Get));
        Assert.DoesNotContain(a, h.Services.Documents.Documents);

        // Done (Escape) closes the dialog.
        dlg.Press("Escape");
        await h.WaitForAsync(() => running.IsCompleted, "dialog closed");
        await running;
        Assert.Empty(h.Window.OwnedWindows);

        // Enter on a row activates it and closes the dialog.
        var reopened = await OpenAsync(h, "A.pas");
        h.Services.Documents.Activate(c);
        running = h.Services.Commands.ExecuteAsync("window.documents");
        dlg = await h.WaitForDialogAsync("Documents");
        dlg.Click("Row.A.pas");
        dlg.Press("Enter");
        await h.WaitForAsync(() => running.IsCompleted, "dialog closed");
        await running;
        Assert.Same(reopened, h.Services.Documents.ActiveDocument);
        Assert.Empty(h.Window.OwnedWindows);
    }
}
