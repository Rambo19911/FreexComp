using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

public sealed partial class RenameDialogTests
{
    /// <summary>
    /// IDE-REFAC-057, 058 (I-382): F2 on a unit's heading opens the dialog in unit mode — title <c>Rename Unit</c>, no overload
    /// check box, the file line with the new file name while the name is valid; <c>Rename</c> shows the Preview dialog in
    /// confirm mode with the note of the file rename and without check boxes; <c>Cancel</c> there changes nothing.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-REFAC-057")]
    [Trait("Req", "IDE-REFAC-058")]
    [Trait("Req", "UI-DLG-387")]
    public async Task UnitMode()
    {
        var (h, editor, doc) = await StartAsync();
        await using var _ = h;
        await TrustDialogTests.Guarded(h, async () =>
        {
            var files = Directory.GetFiles(Path.Combine(h.TempDir, "Hello"), "*.p*").Order(StringComparer.Ordinal).Select(File.ReadAllText).ToList();
            CaretOn(editor, doc, "Unit1");
            var (dlg, model) = await F2Async(h);
            Assert.Equal("Rename Unit", dlg.Window.Title);
            Assert.Equal(520, dlg.Window.Width);
            Assert.Equal("Unit1", dlg.Find<TextBox>("NewName").Text);
            Assert.Null(dlg.TryFind<CheckBox>("Overloads"));
            Assert.False(dlg.Find<TextBlock>("File").IsVisible); // the name is unchanged: no file line yet

            Replace(dlg, "Customers");
            await SummaryAsync(h, dlg, "4 changes in 4 files");
            Assert.True(dlg.Find<TextBlock>("File").IsVisible);
            Assert.Equal("The file is renamed to Customers.pas.", Text(dlg, "File"));
            Assert.Equal("Dialog.RenameSymbol.File", AutomationIds.Get(dlg.Find<TextBlock>("File")));

            // A name the Project Manager refuses (a file of that name exists): its message, no file line.
            Replace(dlg, "Unit2");
            await h.WaitForAsync(() => dlg.ErrorText("NewName") is not null, "the existing file reported");
            Assert.Equal("A file named Unit2.pas already exists in this folder.", dlg.ErrorText("NewName"));
            Assert.False(dlg.Find<TextBlock>("File").IsVisible);
            Assert.False(dlg.Find<Button>("Ok").IsEnabled);

            Replace(dlg, "Customers");
            await SummaryAsync(h, dlg, "4 changes in 4 files");
            dlg.Click("Ok");

            // The Preview dialog in confirm mode: every change, no check box, the note of the file rename.
            var pd = await h.WaitForDialogAsync(RefactoringPreviewViewModel.DialogName);
            var preview = Assert.IsType<RefactoringPreviewViewModel>(pd.Window.Model);
            await RefactoringPreviewTests.DiffAsync(h, preview);
            Assert.True(preview.IsConfirm);
            Assert.Equal("Preview — Rename 'Unit1' to 'Customers'", pd.Window.Title);
            Assert.Equal("The file Unit1.pas is renamed to Customers.pas. Open documents among the changed files are saved.",
                pd.Find<TextBlock>("Note").Text);
            Assert.DoesNotContain(AutomationIdChecks.AllIds(pd.Window), id => id.EndsWith(".Check", StringComparison.Ordinal));
            Assert.Equal("Changes: 4, files: 4", pd.Find<TextBlock>("Summary").Text);

            pd.Press("Escape");
            await h.WaitForAsync(() => !pd.Window.IsVisible && !h.Services.Refactoring.IsBusy, "the preview closed");
            Dispatcher.UIThread.RunJobs();

            // Nothing changed: the buffer, every file, the project.
            Assert.Equal(Unit1, doc.Buffer.Text);
            Assert.False(doc.IsModified);
            Assert.Equal(files, Directory.GetFiles(Path.Combine(h.TempDir, "Hello"), "*.p*").Order(StringComparer.Ordinal).Select(File.ReadAllText));
            Assert.False(File.Exists(PathOf(h, "Customers.pas")));
            await SettleAsync(h, model, preview);
        });
    }
}
