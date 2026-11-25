using FreexComp.Build;
using FreexComp.Compiler;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Tests.Debugging;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Documents;

/// <summary>
/// Protected library documents (library-source.md §2, I-232): a library source that is not a member of the open project
/// opens read-only with the <c>LibrarySource</c> info bar, whatever opened it; <c>Edit Anyway</c> unlocks the open
/// document only.
/// </summary>
[Trait("Phase", "8")]
public sealed class LibrarySourceTests
{
    private const string LibText = "unit LibUnit;\ninterface\nprocedure LibProc;\nimplementation\nprocedure LibProc;\nbegin\nend;\nend.\n";
    private const string Bar = TextDocumentViewModel.LibrarySourceInfoBarKind;
    private const string Command = "file.editLibrarySource";

    /// <summary>The library root of the bed (its run-time library directory) and a unit in it.</summary>
    private static string Lib(DebugTestBed bed, string name = "LibUnit.pas", string text = LibText) =>
        bed.File(Path.Combine(Assert.Single(bed.Services.LibrarySource.Roots), name), text);

    private static TextDocumentViewModel Active(DebugTestBed bed) => Assert.IsType<TextDocumentViewModel>(bed.Services.Documents.ActiveDocument);

    private static void AssertProtected(TextDocumentViewModel document)
    {
        Assert.True(document.IsLibraryProtected);
        Assert.True(document.IsReadOnly);
        Assert.True(document.Buffer.IsReadOnly);
        Assert.Equal(Bar, document.VisibleInfoBars[0].Kind);
    }

    private static void AssertWritable(TextDocumentViewModel document)
    {
        Assert.False(document.IsLibraryProtected);
        Assert.False(document.IsReadOnly);
        Assert.Empty(document.InfoBars);
    }

    [Theory(Timeout = TestDeadline.DebugTestTimeout)]
    [InlineData("FileOpen")]
    [InlineData("RecentFile")]
    [InlineData("SessionRestore")]
    [InlineData("GoToDeclaration")]
    [InlineData("Messages")]
    [InlineData("CallStack")]
    [InlineData("HandOver")]
    [Trait("Req", "IDE-LIB-002")]
    public Task EveryOpenPathProtectsLibraryFiles(string way) => TestUiThread.RunAsync(() => EveryOpenPathOnUiThread(way));

    private static async Task EveryOpenPathOnUiThread(string way)
    {
        using var bed = new DebugTestBed();
        var services = bed.Services;
        var commands = services.Commands;
        var lib = Lib(bed);
        switch (way)
        {
            case "FileOpen":
                await bed.OpenHelloAsync();
                ((FakeFileDialogService)services.FileDialogs).NextOpen(lib);
                await commands.ExecuteAsync("file.open");
                break;

            case "RecentFile":
            {
                // Opened once and unlocked: the recent entry opens it protected again (IDE-LIB-004).
                await bed.OpenHelloAsync();
                var first = await bed.OpenDocumentAsync(lib);
                await commands.ExecuteAsync(Command);
                AssertWritable(first);
                Assert.True(await services.Documents.CloseAsync(first));
                Assert.Contains(services.RecentItems.Files, f => string.Equals(f.Path, lib, StringComparison.OrdinalIgnoreCase));
                await commands.ExecuteAsync("file.openRecentFile", lib);
                break;
            }

            case "SessionRestore":
            {
                // The loose-file session (no project): the unlocked state is not stored.
                var first = await bed.OpenDocumentAsync(lib);
                await commands.ExecuteAsync(Command);
                AssertWritable(first);
                services.Session.Save();
                Assert.True(await services.Documents.CloseAllAsync());
                Assert.Equal(1, await services.Session.RestoreAsync());
                break;
            }

            case "GoToDeclaration":
            {
                var src = Path.Combine(SpecTables.RepoRootPath(), "rtl", "src");
                foreach (var file in Directory.GetFiles(src, "*.inc").Append(Path.Combine(src, "System.pas")).Append(Path.Combine(src, "unit-aliases.json")).Where(File.Exists))
                    Lib(bed, Path.GetFileName(file), File.ReadAllText(file));
                await bed.OpenHelloAsync();
                Assert.True(await services.Documents.CloseAllAsync());
                bed.File(bed.MainPath, "program Hello;\nuses LibUnit;\nbegin\n  LibProc;\nend.\n");
                var main = await bed.OpenDocumentAsync(bed.MainPath);
                main.Selection = TextSelection.At(main.Buffer.Text.IndexOf("LibProc", StringComparison.Ordinal) + 1);
                Assert.True(commands.CanExecute("search.goToDeclaration"));
                await commands.ExecuteAsync("search.goToDeclaration");
                Assert.Equal(5, Active(bed).ViewState.Caret.Line); // the routine (its implementation heading)
                break;
            }

            case "Messages":
            {
                await bed.OpenHelloAsync();
                bed.Engine.Enqueue(FakeBuildEngine.Failure(bed.ProjectPath,
                    FakeBuildEngine.Diag("E2003", DiagnosticSeverity.Error, "Undeclared identifier 'Foo'", lib, 3, 1, 9)));
                await services.Build.BuildAsync(BuildKind.Build);
                await services.Messages.NavigateToAsync(services.Messages.Items.First(i => i.Location?.FilePath == lib));
                break;
            }

            case "CallStack":
            {
                await bed.OpenHelloAsync();
                (string Routine, string Path, int Line)[] frames = [("Run", bed.UnitPath, 7), ("LibProc", lib, 6), ("Hello", bed.MainPath, 4)];
                bed.Sessions.UseScenario($$"""
                    { "steps": [
                      { "on": "configurationDone", "respond": {}, "thenEvents": [ {{DebugScenarios.StoppedEvent("entry")}} ] },
                      {{DebugScenarios.AtStop(frames)}},
                      {{DebugScenarios.NoScopes}},
                      {{DebugScenarios.Continue(DebugTestBed.Exit())}} ] }
                    """);
                var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                bed.Debug.Stopped += (_, _) => stopped.TrySetResult();
                await commands.ExecuteAsync("run.stepInto");
                await DebugTestBed.WaitAsync(stopped.Task, "the stop on entry");
                var stack = await bed.Debug.GetFramesAsync();
                Assert.NotNull(stack);
                await bed.Debug.GoToFrameAsync(stack.Frames[1]);
                Assert.Equal(lib, Active(bed).FilePath);
                AssertProtected(Active(bed));
                await commands.ExecuteAsync("run.start");
                await bed.WaitForStateAsync(IdeState.Idle);
                break;
            }

            case "HandOver":
                // I-213: the paths a second instance hands over open as if given on the command line.
                await bed.OpenHelloAsync();
                await StartupSequence.OpenPathsAsync(services, [lib]);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(way), way, null);
        }

        Assert.True(bed.Dialogs.Requests.Count == 0, string.Join("; ", bed.Dialogs.Requests.Select(r => r.MessageId + ": " + string.Join(" | ", r.Args))));
        var document = Assert.IsType<TextDocumentViewModel>(services.Documents.Documents.Single(d => string.Equals(d.FilePath, lib, StringComparison.OrdinalIgnoreCase)));
        Assert.Same(document, services.Documents.ActiveDocument);
        AssertProtected(document);
        Assert.True(commands.CanExecute(Command));
    }

    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "IDE-LIB-003")]
    [Trait("Req", "UI-ED-144")]
    public Task ProtectedDocumentShowsBarAndIsReadOnly() => TestUiThread.RunAsync(async () =>
    {
        using var bed = new DebugTestBed();
        await bed.OpenHelloAsync();
        // Mixed line endings: the load shows its own bar too; the library bar is above it.
        var document = await bed.OpenDocumentAsync(Lib(bed, text: "unit LibUnit;\r\ninterface\nimplementation\nend.\n"));

        Assert.Equal("This file is part of the library source installed with FreexComp. It is protected because a change affects every project that uses it.",
            Strings.Msg_File_LibrarySource);
        Assert.Equal("Edit Anyway", Strings.InfoBar_Action_EditAnyway);
        var bar = document.VisibleInfoBars[0];
        Assert.Equal((Bar, InfoBarSeverity.Info, Strings.Msg_File_LibrarySource, false, true),
            (bar.Kind, bar.Severity, bar.Text, bar.CanDismiss, bar.IsTopMost));
        // A project is open: Copy to Project comes first (UI-ED-147, IDE-LIB-037); Edit Anyway is the second action.
        Assert.Equal([("CopyToProject", Strings.InfoBar_Action_CopyToProject), ("EditAnyway", Strings.InfoBar_Action_EditAnyway)],
            bar.Actions.Select(a => (a.Name, a.Label)));
        Assert.Equal([Bar, TextDocumentViewModel.MixedLineEndingsInfoBarKind], document.VisibleInfoBars.Select(b => b.Kind));
        AssertProtected(document);

        // It stays the top-most bar and is never queued, however many newer bars arrive (UI-ED-144).
        foreach (var kind in (string[])[TextDocumentViewModel.EncodingInfoBarKind, TextDocumentViewModel.ExternalChangeInfoBarKind, TextDocumentViewModel.RecoveredInfoBarKind])
            document.ShowInfoBar(new InfoBarViewModel(kind, InfoBarSeverity.Warning, kind, [], null));
        Assert.Equal(DocumentViewModel.MaxVisibleInfoBars, document.VisibleInfoBars.Count);
        Assert.Equal([Bar, TextDocumentViewModel.EncodingInfoBarKind, TextDocumentViewModel.MixedLineEndingsInfoBarKind], document.VisibleInfoBars.Select(b => b.Kind));
        bar.Dismiss(); // no dismiss: nothing happens
        Assert.True(document.IsLibraryProtected);

        // The existing read-only presentation: an edit is refused with the status message; the text is unchanged.
        var text = document.Buffer.Text;
        Assert.False(document.TryInsertText(0, 0, "x"));
        Assert.Equal(Strings.Msg_Editor_ReadOnlyEdit, bed.Services.Status.Message);
        Assert.Equal(text, document.Buffer.Text);
        Assert.False(document.IsModified);
        Assert.True(bed.Dialogs.Requests.Count == 0, string.Join("; ", bed.Dialogs.Requests.Select(r => r.MessageId + ": " + string.Join(" | ", r.Args))));
    });

    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "IDE-LIB-004")]
    public Task EditAnywayUnlocksUntilClosed() => TestUiThread.RunAsync(async () =>
    {
        using var bed = new DebugTestBed();
        var commands = bed.Services.Commands;
        var documents = bed.Services.Documents;
        await bed.OpenHelloAsync();
        var lib = Lib(bed);

        // The command is enabled only while the active document is protected.
        Assert.False(commands.CanExecute(Command)); // a project member is active
        var document = await bed.OpenDocumentAsync(lib);
        Assert.True(commands.CanExecute(Command));
        documents.Activate(documents.Documents.First(d => d.FilePath == bed.MainPath));
        Assert.False(commands.CanExecute(Command));
        documents.Activate(document);
        var readOnlyChanges = 0;
        document.PropertyChanged += (_, e) => readOnlyChanges += e.PropertyName == nameof(TextDocumentViewModel.IsReadOnly) ? 1 : 0;

        // The command: no dialog, the bar and the read-only state go, the text can be edited.
        await commands.ExecuteAsync(Command);
        Assert.True(bed.Dialogs.Requests.Count == 0, string.Join("; ", bed.Dialogs.Requests.Select(r => r.MessageId + ": " + string.Join(" | ", r.Args))));
        AssertWritable(document);
        Assert.Equal(1, readOnlyChanges); // the tab's lock glyph follows this change
        Assert.False(commands.CanExecute(Command));
        Assert.True(document.TryInsertText(0, 0, "{x}"));
        Assert.True(document.IsModified);

        // Not stored: neither in the session nor in the settings; closing and reopening protects the file again.
        bed.Services.Session.Save();
        Assert.DoesNotContain("LibUnit", File.ReadAllText(bed.SessionPath), StringComparison.OrdinalIgnoreCase);
        document.Buffer.Undo();
        Assert.False(document.IsModified);
        Assert.True(await documents.CloseAsync(document));
        var reopened = await bed.OpenDocumentAsync(lib);
        Assert.NotSame(document, reopened);
        AssertProtected(reopened);

        // The bar's action does the same as the command.
        await Assert.Single(reopened.VisibleInfoBars[0].Actions, a => a.Name == "EditAnyway").ExecuteAsync();
        AssertWritable(reopened);
        Assert.False(commands.CanExecute(Command));
        Assert.True(bed.Dialogs.Requests.Count == 0, string.Join("; ", bed.Dialogs.Requests.Select(r => r.MessageId + ": " + string.Join(" | ", r.Args))));
    });

    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "IDE-LIB-005")]
    public Task DiskReadOnlyBarAppearsAfterEditAnyway() => TestUiThread.RunAsync(async () =>
    {
        using var bed = new DebugTestBed();
        await bed.OpenHelloAsync();
        var lib = Lib(bed);
        File.SetAttributes(lib, FileAttributes.ReadOnly);
        try
        {
            // While protected only the library bar is shown.
            var document = await bed.OpenDocumentAsync(lib);
            Assert.Equal([Bar], document.InfoBars.Select(b => b.Kind));
            AssertProtected(document);

            // Edit Anyway: the disk read-only bar takes its place; the buffer stays read-only.
            await bed.Services.Commands.ExecuteAsync(Command);
            Assert.False(document.IsLibraryProtected);
            var bar = Assert.Single(document.InfoBars);
            Assert.Equal((TextDocumentViewModel.ReadOnlyInfoBarKind, Strings.Msg_File_ReadOnly), (bar.Kind, bar.Text));
            Assert.True(document.IsReadOnly);
            Assert.False(document.TryInsertText(0, 0, "x"));
            Assert.False(bed.Services.Commands.CanExecute(Command));

            // Make Writable succeeds: now the buffer is writable.
            await bar.Actions.Single(a => a.Name == "MakeWritable").ExecuteAsync();
            AssertWritable(document);
            Assert.False(File.GetAttributes(lib).HasFlag(FileAttributes.ReadOnly));
            Assert.True(document.TryInsertText(0, 0, "x"));
            Assert.True(bed.Dialogs.Requests.Count == 0, string.Join("; ", bed.Dialogs.Requests.Select(r => r.MessageId + ": " + string.Join(" | ", r.Args))));
        }
        finally
        {
            File.SetAttributes(lib, FileAttributes.Normal);
        }
    });

    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "IDE-LIB-006")]
    public Task ProjectMemberIsNotProtected() => TestUiThread.RunAsync(async () =>
    {
        // The developers of a library open its project: the project directory is a library root.
        using var bed = new DebugTestBed();
        var services = bed.Services;
        services.Settings.Set(SettingsCatalog.ToolchainFrameworkDir, bed.Dir);
        await bed.OpenHelloAsync();
        Assert.True(services.LibrarySource.IsLibrarySource(bed.UnitPath));
        Assert.True(services.Projects.IsMember(bed.UnitPath));

        // A member is never protected; a file of the same root that is not a member is.
        foreach (var member in services.Documents.Documents.OfType<TextDocumentViewModel>().Append(await bed.OpenDocumentAsync(bed.UnitPath)).ToList())
            AssertWritable(member);
        var unit = await bed.OpenDocumentAsync(bed.UnitPath);
        Assert.False(services.Commands.CanExecute(Command));
        var loose = await bed.OpenDocumentAsync(bed.File(Path.Combine(bed.Dir, "Loose.pas"), LibText));
        AssertProtected(loose);

        // Protection is evaluated on open only: closing the project changes no open document …
        Assert.True(await services.Projects.CloseAsync());
        foreach (var open in services.Documents.Documents.OfType<TextDocumentViewModel>())
            Assert.Equal(ReferenceEquals(open, loose), open.IsLibraryProtected);
        Assert.False(unit.IsLibraryProtected);

        // … and the former member, opened now, is library source outside a project.
        Assert.True(await services.Documents.CloseAllAsync());
        AssertProtected(await bed.OpenDocumentAsync(bed.UnitPath));
    });

    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "IDE-LIB-007")]
    public Task BreakpointsAndNavigationWorkWhileProtected() => TestUiThread.RunAsync(async () =>
    {
        using var bed = new DebugTestBed();
        var services = bed.Services;
        var commands = services.Commands;
        await bed.OpenHelloAsync();
        var lib = Lib(bed);
        var document = await bed.OpenDocumentAsync(lib);
        AssertProtected(document);

        // Breakpoints: set, edit, remove.
        bed.Breakpoints.Toggle(lib, 6);
        var breakpoint = Assert.Single(bed.Breakpoints.All);
        Assert.Equal((lib, 6), (breakpoint.Path, breakpoint.Line));
        bed.Breakpoints.Update(breakpoint, enabled: false, condition: "1 = 1", hitCount: null);
        Assert.Equal((false, "1 = 1"), (bed.Breakpoints.Find(lib, 6)!.Enabled, bed.Breakpoints.Find(lib, 6)!.Condition));
        bed.Breakpoints.Toggle(lib, 6);
        Assert.Empty(bed.Breakpoints.All);

        // Navigation, selection and bookmarks.
        services.Navigation.GoToLine(document, 5, 11);
        Assert.Equal(new TextPosition(5, 11), document.ViewState.Caret);
        Assert.True(commands.CanExecute("search.bookmark.toggle"));
        await commands.ExecuteAsync("search.bookmark.toggle");
        Assert.Equal((lib, 5), (Assert.Single(services.Bookmarks.All).Path, services.Bookmarks.All[0].Line));
        Assert.True(commands.CanExecute("search.find"));
        AssertProtected(document); // none of it unlocked the document

        // Save is disabled (nothing is modified); Save As writes a copy, which is an ordinary writable file.
        Assert.False(commands.CanExecute("file.save"));
        Assert.True(commands.CanExecute("file.saveAs"));
        var copy = Path.Combine(bed.Dir, "LibCopy.pas");
        ((FakeFileDialogService)services.FileDialogs).NextSave(copy);
        await commands.ExecuteAsync("file.saveAs");
        Assert.Equal(copy, document.FilePath);
        Assert.Equal(LibText, File.ReadAllText(copy));
        Assert.Equal(LibText, File.ReadAllText(lib));
        Assert.False(document.IsLibraryProtected);
        Assert.False(document.IsReadOnly);
        Assert.DoesNotContain(document.InfoBars, b => b.Kind == Bar);

        // The copy reopens unprotected; the library file itself is still protected.
        Assert.True(await services.Documents.CloseAsync(document));
        Assert.False((await bed.OpenDocumentAsync(copy)).IsLibraryProtected);
        AssertProtected(await bed.OpenDocumentAsync(lib));
    });

    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "IDE-LIB-009")]
    public Task NonLibraryNonMemberFromMessagesHasNoLibraryBar() => TestUiThread.RunAsync(async () =>
    {
        using var bed = new DebugTestBed();
        var services = bed.Services;
        await bed.OpenHelloAsync();
        var other = bed.File(Path.Combine(Path.GetDirectoryName(bed.Dir)!, "elsewhere", "Other.pas"), LibText);
        Assert.False(services.LibrarySource.IsLibrarySource(other));
        Assert.False(services.Projects.IsMember(other));
        bed.Engine.Enqueue(FakeBuildEngine.Failure(bed.ProjectPath,
            FakeBuildEngine.Diag("E2003", DiagnosticSeverity.Error, "Undeclared identifier 'Foo'", other, 3, 1, 9),
            FakeBuildEngine.Diag("E2003", DiagnosticSeverity.Error, "Undeclared identifier 'Bar'", bed.UnitPath, 3, 1, 9)));
        await services.Build.BuildAsync(BuildKind.Build);

        // IDE-P3-065 as before: read-only, but neither the library bar nor the disk read-only bar.
        await services.Messages.NavigateToAsync(services.Messages.Items.First(i => i.Location?.FilePath == other));
        var document = Active(bed);
        Assert.Equal(other, document.FilePath);
        Assert.True(document.Buffer.IsReadOnly);
        Assert.False(document.IsLibraryProtected);
        Assert.Empty(document.InfoBars);
        Assert.False(services.Commands.CanExecute(Command));

        // A project member opened from Messages stays writable.
        await services.Messages.NavigateToAsync(services.Messages.Items.First(i => i.Location?.FilePath == bed.UnitPath));
        Assert.Equal(bed.UnitPath, Active(bed).FilePath);
        AssertWritable(Active(bed));
    });
}
