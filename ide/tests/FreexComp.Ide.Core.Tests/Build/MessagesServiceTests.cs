using FreexComp.Build;
using FreexComp.Compiler;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Build;

/// <summary>Position tracking and navigation of Messages rows (phase3-build-run.md §7.1).</summary>
[Trait("Phase", "3")]
public sealed class MessagesServiceTests
{
    private const string UnitText = "unit Unit1;\ninterface\nimplementation\nvar Foo1: Integer;\nbegin\n  Foo := 1;\nend.\n";

    private static Diagnostic Error(string message, int line, int column, int length, string path = BuildTestBed.UnitPath) =>
        FakeBuildEngine.Diag("E2003", DiagnosticSeverity.Error, message, path, line, column, length);

    private static async Task<(BuildTestBed Bed, TextDocumentViewModel Unit)> StartAsync()
    {
        var bed = new BuildTestBed();
        await bed.OpenHelloAsync();
        bed.File(BuildTestBed.UnitPath, UnitText);
        var unit = await bed.OpenDocumentAsync(BuildTestBed.UnitPath);
        return (bed, unit);
    }

    private static void Expect(TextDocumentViewModel document, int line, int column, int selectedLength)
    {
        var position = document.Buffer.GetPosition(document.Selection.Caret);
        Assert.Equal(new TextPosition(line, column), document.Buffer.GetPosition(document.Selection.Start));
        Assert.Equal(selectedLength, document.Selection.End - document.Selection.Start);
        Assert.Equal(line, position.Line);
    }

    /// <summary>IDE-P3-064/065: a row of an open file is anchored; edits above move the navigation target.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-064")]
    [Trait("Req", "IDE-P3-065")]
    [Trait("Req", "IDE-P3-068")]
    public Task AnchorsFollowEdits() => TestUiThread.RunAsync(() => AnchorsFollowEditsOnUiThread()); // I-209

    private async Task AnchorsFollowEditsOnUiThread()
    {
        var (bed, unit) = await StartAsync();
        bed.Engine.Enqueue(FakeBuildEngine.Failure(BuildTestBed.ProjectPath, Error("Undeclared identifier 'Foo'", 6, 3, 3)));
        await bed.Services.Build.BuildAsync(BuildKind.Build);
        var messages = bed.Services.Messages;
        var row = messages.Items[0];
        Assert.NotNull(row.Anchor);

        await messages.NavigateToAsync(row);
        Expect(unit, 6, 3, 3);
        Assert.Same(unit, bed.Services.Documents.ActiveDocument);

        // A new line above line 6: the target follows to line 7.
        var lineStart = unit.Buffer.GetLine(6).Offset;
        BuildTestBed.Type(unit, "\n", lineStart);
        unit.Selection = TextSelection.At(0);
        await messages.NavigateToAsync(row);
        Expect(unit, 7, 3, 3);
        Assert.Same(row, messages.Items[0]); // IDE-P3-068: editing the file keeps its rows until the next build
        Assert.Equal(2, messages.Items.Count);

        // A row of a file opened after the build is anchored on open (its text is the disk text).
        var main = Error("Missing ';'", 2, 5, 1, BuildTestBed.MainPath);
        bed.Services.Documents.Close(bed.Services.Documents.Documents.First(d => d.FilePath == BuildTestBed.MainPath));
        bed.Engine.Enqueue(FakeBuildEngine.Failure(BuildTestBed.ProjectPath, main));
        bed.Services.Settings.Set(Core.Settings.SettingsCatalog.EnvironmentAutoSaveBeforeBuild, "none");
        await bed.Services.Build.BuildAsync(BuildKind.Build);
        var mainRow = messages.Items[0];
        Assert.DoesNotContain(row, messages.Items); // the next build replaced the rows
        Assert.Null(mainRow.Anchor);
        var opened = await bed.OpenDocumentAsync(BuildTestBed.MainPath);
        Assert.NotNull(mainRow.Anchor);
        BuildTestBed.Type(opened, "{x}\n");
        await messages.NavigateToAsync(mainRow);
        Expect(opened, 3, 5, 1);
    }

    /// <summary>IDE-P3-064: the buffer changed between build start and the row's arrival → original position + LineStale.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-064")]
    public Task StaleLineNotice() => TestUiThread.RunAsync(() => StaleLineNoticeOnUiThread()); // I-209

    private async Task StaleLineNoticeOnUiThread()
    {
        var (bed, unit) = await StartAsync();
        var held = bed.Engine.Enqueue(FakeBuildEngine.Failure(BuildTestBed.ProjectPath, Error("Undeclared identifier 'Foo'", 6, 3, 3)).Held());
        var running = bed.Services.Build.BuildAsync(BuildKind.Build);
        await held.Reached;
        BuildTestBed.Type(unit, "\n\n", 0); // edited while the build runs, before the row arrives (still in the pump)
        held.Release();
        await running;

        var row = bed.Services.Messages.Items[0];
        Assert.Null(row.Anchor);
        Assert.NotNull(row.BufferVersionAtBuild);
        var status = new List<string?>();
        bed.Services.Status.Changed += (_, _) => status.Add(bed.Services.Status.Message);
        await bed.Services.Messages.NavigateToAsync(row);
        Expect(unit, 6, 3, 3); // the original line and column
        Assert.Contains(Strings.Msg_Messages_LineStale, status);

        // A missing file: SourceMissing, nothing opened.
        bed.Fs.DeleteFile(BuildTestBed.MainPath);
        bed.Services.Documents.Close(bed.Services.Documents.Documents.First(d => d.FilePath == BuildTestBed.MainPath));
        bed.Engine.Enqueue(FakeBuildEngine.Failure(BuildTestBed.ProjectPath, Error("x", 1, 1, 1, BuildTestBed.MainPath)));
        bed.Services.Settings.Set(Core.Settings.SettingsCatalog.EnvironmentAutoSaveBeforeBuild, "none");
        await bed.Services.Build.BuildAsync(BuildKind.Build);
        await bed.Services.Messages.NavigateToAsync(bed.Services.Messages.Items[0]);
        Assert.Contains(Strings.Msg_Messages_SourceMissing, status);
        Assert.DoesNotContain(bed.Services.Documents.Documents, d => d.FilePath == BuildTestBed.MainPath);
    }

    /// <summary>IDE-P3-066: Next/Previous skip rows without position and the summary, wrap at both ends.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-066")]
    public Task NextPreviousWrapAndSkipSummary() => TestUiThread.RunAsync(() => NextPreviousWrapAndSkipSummaryOnUiThread()); // I-209

    private async Task NextPreviousWrapAndSkipSummaryOnUiThread()
    {
        var (bed, unit) = await StartAsync();
        var tool = FakeBuildEngine.Diag("E4005", DiagnosticSeverity.Error, "lld-link failed", BuildTestBed.ProjectPath);
        bed.Engine.Enqueue(FakeBuildEngine.Failure(BuildTestBed.ProjectPath, Error("first", 4, 5, 4), tool, Error("second", 6, 3, 3)));
        await bed.Services.Build.BuildAsync(BuildKind.Build);
        var messages = bed.Services.Messages;
        Assert.Equal(4, messages.Items.Count);
        Assert.Null(messages.Current);

        // Previous without a current row goes to the last navigable row; Next wraps to the first.
        await messages.NavigateNextAsync(forward: false);
        Assert.Equal("second", messages.Current!.Text);
        Expect(unit, 6, 3, 3);
        await messages.NavigateNextAsync(forward: true);
        Assert.Equal("first", messages.Current!.Text);
        Expect(unit, 4, 5, 4);
        await messages.NavigateNextAsync(forward: true);
        Assert.Equal("second", messages.Current!.Text);
        await messages.NavigateNextAsync(forward: true);
        Assert.Equal("first", messages.Current!.Text);
        await messages.NavigateNextAsync(forward: false);
        Assert.Equal("second", messages.Current!.Text);

        // A new build forgets the current row: Next goes to the first again.
        bed.Engine.Enqueue(FakeBuildEngine.Failure(BuildTestBed.ProjectPath, Error("first", 4, 5, 4), Error("second", 6, 3, 3)));
        await bed.Services.Build.BuildAsync(BuildKind.Build);
        Assert.Null(messages.Current);
        await messages.NavigateNextAsync(forward: true);
        Assert.Equal("first", messages.Current!.Text);

        // Clear empties the list and counts (IDE-P3-067); Next does nothing.
        messages.Clear();
        Assert.Equal((0, 0, 0, 0), (messages.Items.Count, messages.ErrorCount, messages.WarningCount, messages.HintCount));
        await messages.NavigateNextAsync(forward: true);
        Assert.Null(messages.Current);
    }
}
