using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Designer.Model;
using FreexComp.Designer.Text;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// Saving and reloading a form document end to end (form-document.md §8, §9; D-085): a Design-view edit saved writes the
/// unit and the canonical <c>.form</c>, the reopened document has the same model; an external change of the <c>.form</c>
/// reloads silently or shows <c>InfoBar.ExternalChange</c> (DSG-FD-100).
/// </summary>
[Trait("Phase", "5")]
[Trait("Kind", "Ui")]
public sealed class DesignerSaveTests
{
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-FD-080")]
    [Trait("Req", "DSG-FD-090")]
    public async Task SaveWritesBothFilesAndReopensIdentically()
    {
        await using var h = await HeadlessHarness.StartAsync(ToolPaletteViewTests.Phase5);
        var document = await ToolPaletteViewTests.OpenDesignAsync(h);
        var designer = document.Designer;

        // Design-view edits: a label placed from the palette (keyboard), the button nudged by one pixel.
        h.Press("Alt+4");
        h.Type("label");
        h.Press("Enter");
        Assert.NotNull(designer.Model!.FindByName("Label1"));
        designer.Surface!.Selection.Set(["GreetButton"]);
        h.Press("Ctrl+Right");
        Assert.Equal(233, Left(designer.Model, "GreetButton"));
        Assert.True(document.IsModified);

        // Ctrl+S writes both files: the unit with the new field, the .form in canonical form (the writer's text of the model,
        // unchanged by a parse → write round trip).
        await h.ExecuteAsync("file.save"); // Ctrl+S
        await h.Services.FileChanges.WhenIdle();
        Dispatcher.UIThread.RunJobs();
        Assert.False(document.IsModified);
        Assert.False(document.FormText.IsModified);
        var pasPath = Path.Combine(h.TempDir, "MainForm.pas");
        var formPath = Path.Combine(h.TempDir, "MainForm.form");
        var unitOnDisk = File.ReadAllText(pasPath);
        var formOnDisk = File.ReadAllText(formPath);
        Assert.Equal(document.Buffer.Text, unitOnDisk);
        Assert.Contains("    Label1: TLabel;\r\n", unitOnDisk, StringComparison.Ordinal);
        Assert.Equal(FormWriter.Write(designer.Model!), formOnDisk);
        var reparsed = FormParser.Parse(formOnDisk, formPath, designer.Catalog);
        Assert.False(reparsed.HasSyntaxErrors);
        Assert.Equal(formOnDisk, FormWriter.Write(reparsed.Document!));
        Assert.Contains("    Left = 233\n", formOnDisk, StringComparison.Ordinal);
        Assert.Contains("  object Label1: TLabel\n", formOnDisk, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", formOnDisk, StringComparison.Ordinal); // canonical: LF only, although the file was CRLF (DSG-FD-050)

        // Close, reopen: the same model (same text, same objects in the same order) and the same unit.
        var names = designer.Model!.AllObjects().Select(o => (o.Name, o.TypeName)).ToList();
        await h.ExecuteAsync("file.close");
        Assert.DoesNotContain(document, h.Services.Documents.Documents);
        var reopened = await ToolPaletteViewTests.OpenDesignAsync(h);
        Assert.NotSame(document, reopened);
        Assert.Equal(unitOnDisk, reopened.Buffer.Text);
        Assert.Equal(formOnDisk, FormWriter.Write(reopened.Designer.Model!));
        Assert.Equal(names, reopened.Designer.Model!.AllObjects().Select(o => (o.Name, o.TypeName)).ToList());
        Assert.Equal(233, Left(reopened.Designer.Model!, "GreetButton"));
        Assert.False(reopened.IsModified);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-FD-100")]
    public async Task ExternalFormChangeReloads()
    {
        await using var h = await HeadlessHarness.StartAsync(ToolPaletteViewTests.Phase5);
        var document = await ToolPaletteViewTests.OpenDesignAsync(h);
        var designer = document.Designer;
        designer.Surface!.Selection.Set(["GreetButton"]);
        var original = designer.Model!;

        // Unmodified document: the changed .form reloads silently, the model is replaced, the selection kept by name.
        var changed = DesignerViewTests.FormText.ReplaceLineEndings("\r\n")
            .Replace("    Left = 232\r\n", "    Left = 240\r\n", StringComparison.Ordinal)
            .Replace("'&Greet'", "'&Greet me'", StringComparison.Ordinal);
        h.WriteFileExternally("MainForm.form", changed);
        await h.WaitForAsync(() => designer.Model is { } m && Left(m, "GreetButton") == 240, "MainForm.form reloaded");
        await h.Services.FileChanges.WhenIdle();
        Dispatcher.UIThread.RunJobs();
        Assert.NotSame(original, designer.Model);
        Assert.Equal(changed, document.FormText.Buffer.Text);
        Assert.Equal(["GreetButton"], designer.Surface!.Selection.Items.Select(o => o.Name));
        Assert.Same(designer.Model!.FindByName("GreetButton"), designer.Surface.Selection.Primary);
        Assert.Equal(DocumentView.Design, document.ActiveView);
        Assert.False(document.IsModified);
        Assert.Empty(document.InfoBars);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_File_ReloadedSilently, document.Title), h.Status.Message);

        // Modified document: no reload; InfoBar.ExternalChange (the "dirty" text) instead.
        h.Press("Ctrl+Right");
        Assert.Equal(241, Left(designer.Model!, "GreetButton"));
        Assert.True(document.IsModified);
        var again = changed.Replace("    Left = 240\r\n", "    Left = 300\r\n", StringComparison.Ordinal)
            .Replace("'&Greet me'", "'&Greet everyone'", StringComparison.Ordinal);
        h.WriteFileExternally("MainForm.form", again);
        await h.WaitForAsync(() => document.InfoBars.Any(b => b.Kind == TextDocumentViewModel.ExternalChangeInfoBarKind), "InfoBar.ExternalChange");
        await h.Services.FileChanges.WhenIdle();
        Dispatcher.UIThread.RunJobs();
        var bar = Assert.Single(document.InfoBars, b => b.Kind == TextDocumentViewModel.ExternalChangeInfoBarKind);
        Assert.Equal(Strings.Msg_File_ExternalChangeDirty, bar.Text);
        Assert.True(h.Find<Control>(AutomationIds.InfoBar("ExternalChange")).IsEffectivelyVisible);
        Assert.Equal(241, Left(designer.Model!, "GreetButton"));
        Assert.True(document.IsModified);

        // Reload and Discard My Changes: the disk version, selection still kept by name.
        h.Click(AutomationIds.InfoBarAction("ExternalChange", "ReloadAndDiscard"));
        await h.WaitForAsync(() => designer.Model is { } m && Left(m, "GreetButton") == 300, "MainForm.form reloaded on request");
        await h.Services.FileChanges.WhenIdle();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["GreetButton"], designer.Surface!.Selection.Items.Select(o => o.Name));
        Assert.Empty(document.InfoBars);
    }

    private static int Left(FormDocument model, string name) =>
        (int)((IntegerValue)PropertyAccess.GetEffective(model.FindByName(name)!, "Left")).Value;
}
