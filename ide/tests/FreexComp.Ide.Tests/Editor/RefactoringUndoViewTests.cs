using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using AvaloniaEdit;
using FreexComp.Ide.Core.Refactoring;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Editor;

/// <summary>
/// Undo of a refactoring operation in the running shell (I-349, F13-003): the Edit › Refactor entry that names the
/// operation, and Ctrl+Z in the editor right after an operation taking back every file.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class RefactoringUndoViewTests
{
    private const string Title = "Rename 'Old' to 'New_Name'";

    private const string ProjectFile = """
        { "formatVersion": 1, "name": "App", "appType": "console", "mainSource": "App.pas",
          "files": [ { "path": "App.pas" }, { "path": "A.pas" }, { "path": "C.pas" } ] }
        """;

    private static string Unit(string name) =>
        $"unit {name};\r\n\r\ninterface\r\n\r\nprocedure Old;\r\n\r\nimplementation\r\n\r\nprocedure Old;\r\nbegin\r\nend;\r\n\r\nend.\r\n";

    /// <summary>The file edit that replaces every <c>Old</c> of <paramref name="baseText"/> by <c>New_Name</c>.</summary>
    private static FileEdit Rename(string path, string baseText)
    {
        var edits = new List<TextEdit>();
        for (var at = baseText.IndexOf("Old", StringComparison.Ordinal); at >= 0; at = baseText.IndexOf("Old", at + 3, StringComparison.Ordinal))
            edits.Add(new TextEdit(at, 3, "New_Name"));
        return new FileEdit(path, baseText, [.. edits]);
    }

    /// <summary>Returns when the engine's running operation has ended (its work on the thread pool included).</summary>
    private static async Task WhenIdleAsync(IRefactoringService engine)
    {
        if (engine.IsBusy)
        {
            var ended = new TaskCompletionSource();
            void OnChanged(object? sender, EventArgs e)
            {
                if (!engine.IsBusy)
                    ended.TrySetResult();
            }

            engine.Changed += OnChanged;
            await ended.Task;
            engine.Changed -= OnChanged;
        }

        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-REFAC-021")]
    [Trait("Req", "IDE-REFAC-024")]
    public async Task MenuLabelNamesTheOperationAndCtrlZUndoesAll()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles["App.pproj"] = ProjectFile;
            o.InitialFiles["App.pas"] = "program App;\r\n\r\nuses A, C;\r\n\r\nbegin\r\nend.\r\n";
            o.InitialFiles["A.pas"] = Unit("A");
            o.InitialFiles["C.pas"] = Unit("C");
            o.InitialFiles["Notes.txt"] = "notes\r\n";
        });
        var (aPath, cPath) = (Path.Combine(h.TempDir, "A.pas"), Path.Combine(h.TempDir, "C.pas"));
        Assert.True(await h.Services.Projects.OpenAsync(Path.Combine(h.TempDir, "App.pproj")));
        Dispatcher.UIThread.RunJobs();
        var engine = h.Services.Refactoring;
        MenuItem Item(string id) => h.Window.GetLogicalDescendants().OfType<MenuItem>().Single(m => AutomationIds.Get(m) == id);
        var undo = Item(AutomationIds.Menu("refactor.undo"));
        var refactor = Item(AutomationIds.SubMenu("Edit", "Refactor"));

        // Nothing to undo: the entry is disabled and has its own label; the submenu has no enabled entry.
        Assert.Equal("_Undo Refactoring", undo.Header);
        Assert.False(undo.Command!.CanExecute(null));
        Assert.False(refactor.IsEnabled);

        // An operation over the open A.pas and the closed C.pas.
        var a = Assert.IsType<TextDocumentViewModel>((await h.Services.Documents.OpenAsync(aPath)).Value);
        Dispatcher.UIThread.RunJobs();
        var cBytes = File.ReadAllBytes(cPath);
        var result = await engine.ApplyAsync(new WorkspaceEdit(Title, [Rename(cPath, Unit("C")), Rename(aPath, a.Buffer.Text)]), PreviewMode.Never);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(RefactoringOutcome.Applied, result.Outcome);
        Assert.Contains("procedure New_Name;", a.Buffer.Text, StringComparison.Ordinal);
        Assert.Contains("procedure New_Name;", File.ReadAllText(cPath), StringComparison.Ordinal);

        // The entry names the operation (mnemonic U; an underscore of the title is shown as such) and is enabled — and so
        // is the Refactor submenu, also while a document that is not Pascal is active.
        Assert.True((await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, "Notes.txt"))).Success);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Notes.txt", Path.GetFileName(h.Services.Documents.ActiveDocument!.FilePath));
        Assert.Equal("_Undo Rename 'Old' to 'New__Name'", undo.Header);
        Assert.True(undo.Command!.CanExecute(null));
        Assert.True(refactor.IsEnabled);

        // Ctrl+Z in the editor of A, right after the operation: A and the closed file are back.
        h.Services.Documents.Activate(a);
        Dispatcher.UIThread.RunJobs();
        Assert.True(h.Find<TextEditor>(AutomationIds.Editor("A.pas")).TextArea.Focus());
        Dispatcher.UIThread.RunJobs();
        h.Press("Ctrl+Z");
        await WhenIdleAsync(engine);

        Assert.Equal(Unit("A"), a.Buffer.Text);
        Assert.False(a.IsModified);
        Assert.False(a.Buffer.CanRedo);
        Assert.Equal(cBytes, File.ReadAllBytes(cPath));
        Assert.False(engine.CanUndo);
        Assert.Equal("Undid Rename 'Old' to 'New_Name' in 2 files", h.Services.Status.Message);
        Assert.Equal("_Undo Refactoring", undo.Header);
        Assert.False(undo.Command!.CanExecute(null));
        Assert.False(refactor.IsEnabled);
    }
}
