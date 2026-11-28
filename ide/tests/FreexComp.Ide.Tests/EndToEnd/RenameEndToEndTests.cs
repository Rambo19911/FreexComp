using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AvaloniaEdit;
using FreexComp.Ide.Core.Designer.CodeSync;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Refactoring;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.Tests.Views;

namespace FreexComp.Ide.Tests.EndToEnd;

/// <summary>
/// Card F13-001 acceptance 2 at IDE level (IDE-REFAC-063, I-386) with the real build engine, toolchain, framework and
/// framework metadata: a console program whose form unit embeds a form is renamed through <c>refactor.rename</c> (the
/// dialog, the Preview dialog with every change, Save All), built and run again — it prints what it printed; and
/// <c>refactor.undo</c> followed by Save All leaves every file of the project byte-identical.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "E2E")]
[Trait("Category", "Toolchain")]
public sealed class RenameEndToEndTests
{
    private const string ProjectFile = """
        { "formatVersion": 1, "name": "App", "appType": "console", "mainSource": "App.pas",
          "files": [ { "path": "App.pas" }, { "path": "UMain.pas", "form": "UMain.form" }, { "path": "UCaller.pas" } ] }
        """;

    /// <summary>Creates the form (its embedded form binds the two handlers), clicks the button through its event, calls the handler from another unit.</summary>
    private const string App = "program App;\r\n\r\nuses\r\n  UMain, UCaller;\r\n\r\nvar\r\n  F: TMainForm;\r\nbegin\r\n"
        + "  F := TMainForm.Create(nil);\r\n  if Assigned(F.Button1.OnClick) then\r\n    F.Button1.OnClick(F.Button1);\r\n"
        + "  Greet(F);\r\n  F.Free;\r\n  Writeln('done');\r\nend.\r\n";

    private const string UMain = "unit UMain;\r\n\r\ninterface\r\n\r\nuses\r\n  Classes, Controls, Forms, StdCtrls;\r\n\r\ntype\r\n"
        + "  TMainForm = class(TForm)\r\n    Button1: TButton;\r\n    procedure Button1Click(Sender: TObject);\r\n"
        + "    procedure FormCreate(Sender: TObject);\r\n  private\r\n  public\r\n  end;\r\n\r\nvar\r\n  MainForm: TMainForm;\r\n\r\n"
        + "implementation\r\n\r\n{$FORM UMain.form}\r\n\r\nprocedure TMainForm.Button1Click(Sender: TObject);\r\nbegin\r\n"
        + "  Writeln('click');\r\nend;\r\n\r\nprocedure TMainForm.FormCreate(Sender: TObject);\r\nbegin\r\n  Writeln('created');\r\nend;\r\n\r\nend.\r\n";

    private const string UCaller = "unit UCaller;\r\n\r\ninterface\r\n\r\nuses\r\n  UMain;\r\n\r\nprocedure Greet(F: TMainForm);\r\n\r\n"
        + "implementation\r\n\r\nprocedure Greet(F: TMainForm);\r\nbegin\r\n  F.Button1Click(nil);\r\nend;\r\n\r\nend.\r\n";

    /// <summary>The form in the layout of the designer's form writer (what Save All writes for it unchanged).</summary>
    private const string Form = "object MainForm: TMainForm\n  Left = 200\n  Top = 120\n  Caption = 'Rename'\n  ClientWidth = 320\n"
        + "  ClientHeight = 200\n  PixelsPerInch = 96\n  OnCreate = FormCreate\n  object Button1: TButton\n    Left = 16\n    Top = 16\n"
        + "    Width = 75\n    Height = 25\n    Caption = 'Go'\n    TabOrder = 0\n    OnClick = Button1Click\n  end\nend\n";

    private static readonly string[] SourceFiles = ["App.pproj", "App.pas", "UMain.pas", "UMain.form", "UCaller.pas"];

    private static async Task<HeadlessHarness> StartAsync()
    {
        var h = await Phase5Scenario.StartAsync(o =>
        {
            o.InitialFiles[@"App\App.pproj"] = ProjectFile;
            o.InitialFiles[@"App\App.pas"] = App;
            o.InitialFiles[@"App\UMain.pas"] = UMain;
            o.InitialFiles[@"App\UMain.form"] = Form;
            o.InitialFiles[@"App\UCaller.pas"] = UCaller;
        });
        Assert.True(await h.Services.Projects.OpenAsync(Abs(h, "App.pproj")));
        var pm = (ProjectManagerViewModel)h.Services.ToolWindows.Get(ProjectManagerViewModel.ToolName)!;
        await h.WaitForAsync(() => !pm.IsCheckingMissingFiles, "missing-file check done");
        return h;
    }

    private static string Abs(HeadlessHarness h, string relative) => Path.Combine(h.TempDir, "App", relative);

    private static Dictionary<string, byte[]> Files(HeadlessHarness h) => SourceFiles.ToDictionary(f => f, f => File.ReadAllBytes(Abs(h, f)));

    /// <summary>The form unit open (Code view), its form loaded by the designer exactly as the file is; its code editor.</summary>
    private static async Task<(FormDocumentViewModel Document, TextDocumentViewModel Code, TextEditor Editor)> OpenFormUnitAsync(HeadlessHarness h)
    {
        var document = Assert.IsType<FormDocumentViewModel>((await h.Services.Documents.OpenAsync(Abs(h, "UMain.pas"))).Value);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Form, document.Designer.CurrentFormText()); // the writer's layout: Save All rewrites the file unchanged
        Assert.Equal(CodeSyncState.Ok, document.Designer.CodeSync.State);
        var code = Assert.IsAssignableFrom<TextDocumentViewModel>(document.VisibleText);
        return (document, code, h.Find<TextEditor>("Editor.UMain.pas"));
    }

    /// <summary>
    /// F2 on <paramref name="target"/>, <paramref name="newName"/> typed, <c>Rename</c>; the Preview dialog applies every
    /// change. Returns the files the preview listed.
    /// </summary>
    private static async Task<IReadOnlyList<string>> RenameAsync(HeadlessHarness h, TextEditor editor, TextDocumentViewModel code, string target, string newName)
    {
        RenameDialogTests.CaretOn(editor, code, target);
        var (dlg, model) = await RenameDialogTests.F2Async(h);
        RenameDialogTests.Replace(dlg, newName);
        await h.WaitForAsync(() => model.Checking.IsCompleted && RenameDialogTests.Text(dlg, "Summary").EndsWith(" files", StringComparison.Ordinal),
            "the check of " + newName);
        await model.Checking;
        dlg.Click("Ok");
        var pd = await h.WaitForDialogAsync(RefactoringPreviewViewModel.DialogName);
        var preview = Assert.IsType<RefactoringPreviewViewModel>(pd.Window.Model);
        await RefactoringPreviewTests.DiffAsync(h, preview);
        var files = preview.Files.Select(f => Path.GetFileName(f.Path)).Order(StringComparer.OrdinalIgnoreCase).ToList();
        pd.Click("Ok");
        await h.WaitForAsync(() => !pd.Window.IsVisible && !h.Services.Refactoring.IsBusy, "the rename applied");
        if (preview.Diff is { } diff)
            await diff.Recomputing;
        await h.WaitForAsync(() => h.Services.CodeModel.Worker.IsIdle, "the code model is idle");
        return files;
    }

    private static async Task SaveAllAsync(HeadlessHarness h)
    {
        await h.ExecuteAsync("file.saveAll");
        await h.Services.FileChanges.WhenIdle();
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(h.Services.Documents.Documents, d => d.IsModified);
    }

    /// <summary>Builds with the real engine and runs; the program's own output (between its start and exit lines).</summary>
    private static async Task<string> BuildAndRunAsync(HeadlessHarness h, int run)
    {
        string Output() => h.TryFind<TextEditor>("Tool.Output.Text")?.Document.Text ?? "";
        string Diagnostics() => $"state {h.Services.State.Current.State}; messages: "
            + string.Join(" | ", h.Services.Messages.Items.Select(i => $"{i.Code} {i.Location?.FilePath}({i.Location?.Line}) {i.Text}"))
            + "; output: " + Output();
        static int Exits(string text) => text.Split("Program exited with code").Length - 1;
        var before = Output();
        await h.ExecuteAsync("run.startWithoutDebugging");
        try
        {
            await h.WaitForAsync(
                () => Exits(Output()) > (Output().StartsWith(before, StringComparison.Ordinal) ? Exits(before) : 0) || h.Services.Messages.ErrorCount > 0,
                $"build and run {run} finished");
        }
        catch (HarnessAssertionException e)
        {
            Assert.Fail(e.Message + " — " + Diagnostics());
        }

        Assert.True(h.Services.Messages.ErrorCount == 0, Diagnostics());
        await h.WaitForAsync(() => h.Services.State.Current.State == IdeState.Idle, "idle after the run");
        var output = Output();
        Assert.Contains("Program exited with code 0 after ", output[output.LastIndexOf("Program started:", StringComparison.Ordinal)..], StringComparison.Ordinal);
        var start = output.IndexOf('\n', output.LastIndexOf("Program started:", StringComparison.Ordinal)) + 1;
        return output[start..output.LastIndexOf("Program exited with code", StringComparison.Ordinal)];
    }

    /// <summary>
    /// IDE-REFAC-063: the rename of a method that is also an event handler of the embedded form (declaration, body, the
    /// call in another unit, the handler value of the form) and of the form class (the unit buffer, the form text of the
    /// open designer and the other units, in one operation; code sync stays Ok): saved, built and run, the program prints
    /// what it printed before.
    /// </summary>
    [AvaloniaTheory(Timeout = 120000)]
    [InlineData("Button1Click", "OkClick", new[] { "UCaller.pas", "UMain.form", "UMain.pas" })]
    [InlineData("TMainForm = class", "TMainWindow", new[] { "App.pas", "UCaller.pas", "UMain.form", "UMain.pas" })]
    [Trait("Req", "IDE-REFAC-063")]
    [Trait("Req", "IDE-REFAC-046")]
    public async Task RenamedProjectBuildsAndRunsWithUnchangedOutput(string target, string newName, string[] changed)
    {
        await using var h = await StartAsync();
        await TrustDialogTests.Guarded(h, async () =>
        {
            var output = await BuildAndRunAsync(h, 1);
            Assert.Equal("created\r\nclick\r\nclick\r\ndone\r\n", output.ReplaceLineEndings("\r\n"));
            var (document, code, editor) = await OpenFormUnitAsync(h);
            var oldName = target.Split(' ')[0];
            var before = Files(h);

            Assert.Equal(changed, (await RenameAsync(h, editor, code, target, newName)).ToArray());
            Assert.Equal(CodeSyncState.Ok, document.Designer.CodeSync.State);
            Assert.Empty(document.Designer.CodeSync.RepairBeforeEdit());
            await SaveAllAsync(h);

            // Every file of the project has the old name replaced by the new one, and only that.
            foreach (var (file, bytes) in before)
            {
                var text = System.Text.Encoding.UTF8.GetString(bytes);
                Assert.Equal(text.Replace(oldName, newName, StringComparison.Ordinal), File.ReadAllText(Abs(h, file)));
            }

            File.Delete(Abs(h, @"bin\Debug\App.exe"));
            Assert.Equal(output, await BuildAndRunAsync(h, 2));
            Assert.True(File.Exists(Abs(h, @"bin\Debug\App.exe")));
            Assert.Empty(h.Messages.Requests);
        });
    }

    /// <summary>
    /// IDE-REFAC-063: the form class renamed (unit buffer, form text, closed units) and saved; <c>refactor.undo</c> and
    /// Save All give back every file of the project byte for byte.
    /// </summary>
    [AvaloniaFact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-063")]
    public async Task UndoMakesEveryFileByteIdentical()
    {
        await using var h = await StartAsync();
        await TrustDialogTests.Guarded(h, async () =>
        {
            var (document, code, editor) = await OpenFormUnitAsync(h);
            var before = Files(h);
            await RenameAsync(h, editor, code, "TMainForm = class", "TMainWindow");
            await SaveAllAsync(h);
            Assert.NotEqual(before["UMain.form"], File.ReadAllBytes(Abs(h, "UMain.form")));
            Assert.NotEqual(before["App.pas"], File.ReadAllBytes(Abs(h, "App.pas")));

            await h.ExecuteAsync("refactor.undo");
            await h.WaitForAsync(() => !h.Services.Refactoring.IsBusy, "the undo ended");
            await SaveAllAsync(h);

            Assert.All(SourceFiles, file => Assert.Equal(before[file], File.ReadAllBytes(Abs(h, file))));
            Assert.Equal(CodeSyncState.Ok, document.Designer.CodeSync.State);
            Assert.False(h.Services.Refactoring.CanUndo);
            Assert.Empty(h.Messages.Requests);
            await h.WaitForAsync(() => h.Services.CodeModel.Worker.IsIdle, "the code model is idle");
        });
    }
}
