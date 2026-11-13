using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class UnsavedChangesDialogTests
{
    private static async Task<TextDocumentViewModel> OpenModifiedAsync(HeadlessHarness h, string name, string added)
    {
        var result = await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, name));
        var document = Assert.IsType<TextDocumentViewModel>(result.Value);
        document.Buffer.ApplyEdits([new TextEdit(document.Buffer.Length, 0, added)]);
        return document;
    }

    private static bool IsChecked(DialogDriver dlg, string fileName) => dlg.Find<CheckBox>("File." + fileName).IsChecked == true;

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-061")]
    [Trait("Req", "UI-DLG-062")]
    [Trait("Req", "UI-DLG-004")]
    public async Task MultiFileSelection()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles["A.pas"] = "a";
            o.InitialFiles["B.pas"] = "b";
            o.InitialFiles["C.pas"] = "c";
        });
        await OpenModifiedAsync(h, "A.pas", "1");
        await OpenModifiedAsync(h, "B.pas", "2");
        await OpenModifiedAsync(h, "C.pas", "3");

        var closing = h.Services.Commands.ExecuteAsync("file.closeAll");
        var dlg = await h.WaitForDialogAsync("UnsavedChanges");
        Assert.Equal("Save Changes", dlg.Window.Title);
        Assert.Equal("Save changes to the following 3 files?", dlg.Find<TextBlock>("Text").Text);
        Assert.Equal((520.0, 320.0, true), (dlg.Window.Width, dlg.Window.Height, dlg.Window.CanResize));
        Assert.Equal(["Dialog.UnsavedChanges.File.A.pas", "Dialog.UnsavedChanges.File.B.pas", "Dialog.UnsavedChanges.File.C.pas"],
            dlg.FindAll("Dialog.UnsavedChanges.File.").Select(AutomationIds.Get));
        var row = dlg.Find<CheckBox>("File.A.pas");
        Assert.Equal("A.pas", AutomationProperties.GetName(row));
        Assert.Contains(row.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == h.TempDir);        // the folder column
        Assert.True(IsChecked(dlg, "A.pas") && IsChecked(dlg, "B.pas") && IsChecked(dlg, "C.pas")); // all checked initially
        Assert.Equal("Dialog.UnsavedChanges.File.A.pas", dlg.FocusedAutomationId);                   // the list has focus

        // Space toggles the focused row; Ctrl+A checks all.
        dlg.Press("Space");
        Assert.False(IsChecked(dlg, "A.pas"));
        dlg.Press("Tab");
        dlg.Press("Space");
        Assert.False(IsChecked(dlg, "B.pas"));
        dlg.Press("Ctrl+A");
        Assert.True(IsChecked(dlg, "A.pas") && IsChecked(dlg, "B.pas") && IsChecked(dlg, "C.pas"));

        // Nothing checked: Save Selected is disabled.
        dlg.Click("File.A.pas");
        dlg.Click("File.B.pas");
        dlg.Click("File.C.pas");
        Assert.False(IsChecked(dlg, "A.pas") || IsChecked(dlg, "B.pas") || IsChecked(dlg, "C.pas"));
        Assert.False(dlg.Find<Button>("Ok").IsEnabled);
        dlg.Click("File.B.pas");
        Assert.True(dlg.Find<Button>("Ok").IsEnabled);

        // Enter on a row = Save Selected: B is saved, A and C are discarded, all three close.
        Assert.Equal("Dialog.UnsavedChanges.File.B.pas", dlg.FocusedAutomationId);
        dlg.Press("Enter");
        await closing;
        Assert.Empty(h.Services.Documents.Documents);
        Assert.Equal("a", File.ReadAllText(Path.Combine(h.TempDir, "A.pas")));
        Assert.Equal("b2\r\n", File.ReadAllText(Path.Combine(h.TempDir, "B.pas"))); // files.ensureFinalNewline
        Assert.Equal("c", File.ReadAllText(Path.Combine(h.TempDir, "C.pas")));

        // Escape cancels the whole operation: nothing is saved or closed.
        await OpenModifiedAsync(h, "A.pas", "1");
        await OpenModifiedAsync(h, "C.pas", "3");
        closing = h.Services.Commands.ExecuteAsync("file.closeAll");
        dlg = await h.WaitForDialogAsync("UnsavedChanges");
        Assert.Equal("Save changes to the following 2 files?", dlg.Find<TextBlock>("Text").Text);
        dlg.Press("Escape");
        await closing;
        Assert.Equal(2, h.Services.Documents.Documents.Count);
        Assert.Equal("a", File.ReadAllText(Path.Combine(h.TempDir, "A.pas")));

        // Don't Save (Alt+N) discards both.
        closing = h.Services.Commands.ExecuteAsync("file.closeAll");
        dlg = await h.WaitForDialogAsync("UnsavedChanges");
        dlg.Press("Alt+N");
        await closing;
        Assert.Empty(h.Services.Documents.Documents);
        Assert.Equal("c", File.ReadAllText(Path.Combine(h.TempDir, "C.pas")));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-061")]
    [Trait("Req", "UI-A11Y-035")]
    public async Task SameNameFilesHaveDistinctRows()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles[Path.Combine("lib", "Unit1.pas")] = "a";
            o.InitialFiles[Path.Combine("src", "Unit1.pas")] = "b";
            o.InitialFiles["Other.pas"] = "c";
        });
        await OpenModifiedAsync(h, Path.Combine("lib", "Unit1.pas"), "1");
        await OpenModifiedAsync(h, Path.Combine("src", "Unit1.pas"), "2");
        await OpenModifiedAsync(h, "Other.pas", "3");

        var closing = h.Services.Commands.ExecuteAsync("file.closeAll");
        var dlg = await h.WaitForDialogAsync("UnsavedChanges");
        Assert.Equal(["Dialog.UnsavedChanges.File.Unit1.pas lib", "Dialog.UnsavedChanges.File.Unit1.pas src", "Dialog.UnsavedChanges.File.Other.pas"],
            dlg.FindAll("Dialog.UnsavedChanges.File.").Select(AutomationIds.Get));
        Assert.Equal("Unit1.pas", AutomationProperties.GetName(dlg.Find<CheckBox>("File.Unit1.pas src")));
        dlg.Press("Escape");
        await closing;
        Assert.Equal(3, h.Services.Documents.Documents.Count);
    }
}
