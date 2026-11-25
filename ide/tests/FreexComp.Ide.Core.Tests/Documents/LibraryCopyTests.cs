using System.Globalization;
using System.Text;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Tests.Build;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Documents;

/// <summary>
/// <c>file.copyLibraryUnitToProject</c> and the <c>Copy to Project</c> action of the library bar (library-local-copy.md
/// §3, §4; I-271): a library unit is copied into the project directory and added to the project; nothing is ever
/// overwritten; all or nothing.
/// </summary>
[Trait("Phase", "8")]
public sealed class LibraryCopyTests
{
    private const string Command = LibraryCopyService.CommandId;
    private const string Bar = TextDocumentViewModel.LibrarySourceInfoBarKind;
    private const string Root = @"G:\rtl";
    private const string LibPath = Root + @"\LibUnit.pas";
    private const string TargetPath = BuildTestBed.Dir + @"\LibUnit.pas";
    private const string LicenceLine = "// Licence: a line every library source starts with";
    private const string LibText = LicenceLine + "\r\nunit LibUnit;\r\ninterface\r\nprocedure LibProc;\r\nimplementation\r\nprocedure LibProc;\r\nbegin\r\nend;\r\nend.\r\n";

    private static string Format(string text, params string[] args) => string.Format(CultureInfo.CurrentCulture, text, args);

    private static InfoBarAction? CopyAction(TextDocumentViewModel document) =>
        document.InfoBars.FirstOrDefault(b => b.Kind == Bar)?.Actions.FirstOrDefault(a => a.Name == LibraryCopyService.ActionName);

    /// <summary>The open project's entries as <c>path|form</c>.</summary>
    private static string[] Entries(BuildTestBed bed) => [.. bed.Services.Projects.Project!.Files.Select(f => f.Path + "|" + f.Form)];

    /// <summary>A bed whose services see the in-memory files through a recording (and failing) decorator.</summary>
    private static (BuildTestBed Bed, RecordingFileSystem Recording) RecordingBed()
    {
        RecordingFileSystem? recording = null;
        var bed = new BuildTestBed(o => o.FileSystem = recording = new RecordingFileSystem(o.FileSystem));
        return (bed, recording!);
    }

    private static bool IsWrite(string call) =>
        !call.StartsWith("FileExists ", StringComparison.Ordinal) && !call.StartsWith("DirectoryExists ", StringComparison.Ordinal)
        && !call.StartsWith("ReadAllBytes ", StringComparison.Ordinal) && !call.StartsWith("Enumerate", StringComparison.Ordinal)
        && !call.StartsWith("Get", StringComparison.Ordinal) && !call.StartsWith("IsReadOnly ", StringComparison.Ordinal)
        && !call.StartsWith("CanOpenForWrite ", StringComparison.Ordinal);

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-LIB-026")]
    [Trait("Req", "IDE-LIB-028")]
    [Trait("Req", "IDE-LIB-031")]
    [Trait("Req", "IDE-LIB-032")]
    [Trait("Req", "IDE-LIB-033")]
    [Trait("Req", "IDE-LIB-036")]
    [Trait("Req", "IDE-LIB-037")]
    [Trait("Req", "IDE-LIB-038")]
    public Task CopyAddsTheUnitToTheProjectAndOpensIt() => TestUiThread.RunAsync(async () =>
    {
        var bed = new BuildTestBed();
        var services = bed.Services;
        await bed.OpenHelloAsync();
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(LibText)];
        bed.Fs.CreateDirectory(Root);
        bed.Fs.WriteAllBytes(LibPath, bytes);
        bed.Fs.SetReadOnly(LibPath, true);
        var library = await bed.OpenDocumentAsync(LibPath);
        library.ViewState.Caret = new TextPosition(6, 11);
        library.ViewState.TopLine = 3;
        library.ViewState.VerticalOffset = 36;

        // The bar: the text is unchanged; Copy to Project first, Edit Anyway second (IDE-LIB-037, 038).
        Assert.Equal("Copy to Project", Strings.InfoBar_Action_CopyToProject);
        Assert.Equal("Edit a Copy in the Project", Strings.Cmd_file_copyLibraryUnitToProject);
        var bar = library.VisibleInfoBars[0];
        Assert.Equal((Bar, Strings.Msg_File_LibrarySource, false), (bar.Kind, bar.Text, bar.CanDismiss));
        Assert.Equal([("CopyToProject", "Copy to Project", true, true), ("EditAnyway", "Edit Anyway", true, true)],
            bar.Actions.Select(a => (a.Name, a.Label, a.IsVisible, a.IsEnabled)));
        Assert.True(services.Commands.CanExecute(Command));
        var projectFile = bed.Text(BuildTestBed.ProjectPath);

        await services.Commands.ExecuteAsync(Command);

        // IDE-LIB-028, 031: <project dir>\<file name>, byte for byte (the licence line stays), not read-only.
        Assert.Equal(bytes, bed.Fs.ReadAllBytes(TargetPath));
        Assert.StartsWith(LicenceLine, bed.Text(TargetPath).TrimStart('\uFEFF'), StringComparison.Ordinal);
        Assert.False(bed.Fs.IsReadOnly(TargetPath));
        Assert.True(bed.Fs.IsReadOnly(LibPath));
        Assert.Equal(bytes, bed.Fs.ReadAllBytes(LibPath));

        // IDE-LIB-032: a member by reference, the project file saved.
        Assert.Equal(["Hello.pas|", "Unit1.pas|", "LibUnit.pas|"], Entries(bed));
        Assert.True(services.Projects.IsMember(TargetPath));
        Assert.NotEqual(projectFile, bed.Text(BuildTestBed.ProjectPath));
        Assert.Contains("\"LibUnit.pas\"", bed.Text(BuildTestBed.ProjectPath), StringComparison.Ordinal);
        Assert.False(services.Projects.Project!.IsModified);

        // IDE-LIB-033: the copy is the active document, writable, at the same position, without bar or lock.
        var copy = Assert.IsType<TextDocumentViewModel>(services.Documents.ActiveDocument);
        Assert.Equal(TargetPath, copy.FilePath);
        Assert.Equal((new TextPosition(6, 11), 3, 36.0), (copy.ViewState.Caret, copy.ViewState.TopLine, copy.ViewState.VerticalOffset));
        Assert.False(copy.IsReadOnly);
        Assert.False(copy.Buffer.IsReadOnly);
        Assert.False(copy.IsLibraryProtected);
        Assert.Empty(copy.InfoBars);
        Assert.False(copy.IsModified);
        Assert.Equal(library.Buffer.Text, copy.Buffer.Text);

        // The library document stays open and stays as it was.
        Assert.Contains(library, services.Documents.Documents);
        Assert.True(library.IsLibraryProtected);
        Assert.True(library.Buffer.IsReadOnly);
        Assert.Equal(new TextPosition(6, 11), library.ViewState.Caret);

        Assert.Equal("LibUnit.pas was copied into the project. This project is now built from the copy.", services.Status.Message);
        // IDE-LIB-036: the log has source and target.
        Assert.Contains(bed.Log.Lines, l => l.Contains(Command, StringComparison.Ordinal) && l.Contains(LibPath, StringComparison.Ordinal)
            && l.Contains(TargetPath, StringComparison.Ordinal));

        // The copy is an ordinary project file: nothing to copy from it. The library document offers the action still;
        // running it again opens the project's file (IDE-LIB-030).
        Assert.False(services.Commands.CanExecute(Command));
        services.Documents.Activate(library);
        Assert.True(services.Commands.CanExecute(Command));
        await CopyAction(library)!.ExecuteAsync();
        Assert.Same(copy, services.Documents.ActiveDocument);
        Assert.Equal("The project already has its own LibUnit.pas.", services.Status.Message);
        Assert.Equal(["Hello.pas|", "Unit1.pas|", "LibUnit.pas|"], Entries(bed));
        Assert.Empty(bed.Dialogs.Requests);
    });

    [Theory(Timeout = 30000)]
    [InlineData("Protected")]
    [InlineData("NoProject")]
    [InlineData("MemberFile")]
    [InlineData("NonLibraryFile")]
    [InlineData("IncFile")]
    [InlineData("Building")]
    [InlineData("Debugging")]
    [InlineData("Unlocked")]
    [Trait("Req", "IDE-LIB-027")]
    [Trait("Req", "IDE-LIB-037")]
    public Task EnablementFollowsProjectStateAndFileKind(string situation) => TestUiThread.RunAsync(async () =>
    {
        var bed = new BuildTestBed();
        var services = bed.Services;
        var commands = services.Commands;
        bed.File(LibPath, LibText);
        bed.File(Root + @"\Tables.inc", "const Zero = 0;\r\n");
        bed.File(@"C:\other\Loose.pas", LibText);

        (bool Visible, bool Enabled)? Action() => CopyAction((TextDocumentViewModel)services.Documents.ActiveDocument!) is { } a ? (a.IsVisible, a.IsEnabled) : null;

        switch (situation)
        {
            case "Protected":
                await bed.OpenHelloAsync();
                await bed.OpenDocumentAsync(LibPath);
                Assert.True(commands.CanExecute(Command));
                Assert.Equal((true, true), Action());
                break;

            case "NoProject":
            {
                // Without a project the action is absent; it appears when a project is opened and goes when it is closed.
                var library = await bed.OpenDocumentAsync(LibPath);
                Assert.True(library.IsLibraryProtected);
                Assert.False(commands.CanExecute(Command));
                Assert.Equal((false, true), Action());
                await bed.OpenHelloAsync();
                services.Documents.Activate(library);
                Assert.True(commands.CanExecute(Command));
                Assert.Equal((true, true), Action());
                services.Documents.Activate(library);
                Assert.True(await services.Projects.CloseAsync());
                library = await bed.OpenDocumentAsync(LibPath);
                Assert.False(commands.CanExecute(Command));
                Assert.Equal((false, true), Action());
                break;
            }

            case "MemberFile":
            {
                // The library file becomes a member while its protected document is open (IDE-LIB-006: the document
                // keeps its state): nothing to copy.
                await bed.OpenHelloAsync();
                var library = await bed.OpenDocumentAsync(LibPath);
                Assert.Equal(1, await services.Projects.AddExistingFilesAsync([LibPath]));
                services.Documents.Activate(library);
                Assert.True(library.IsLibraryProtected);
                Assert.False(commands.CanExecute(Command));
                Assert.Equal((false, true), Action());
                break;
            }

            case "NonLibraryFile":
                await bed.OpenHelloAsync();
                await bed.OpenDocumentAsync(@"C:\other\Loose.pas");
                Assert.False(commands.CanExecute(Command));
                Assert.Null(Action());
                await bed.OpenDocumentAsync(BuildTestBed.UnitPath);
                Assert.False(commands.CanExecute(Command));
                break;

            case "IncFile":
            {
                await bed.OpenHelloAsync();
                var include = await bed.OpenDocumentAsync(Root + @"\Tables.inc");
                Assert.True(include.IsLibraryProtected);
                Assert.False(commands.CanExecute(Command));
                Assert.Equal((false, true), Action());
                Assert.Equal(["EditAnyway"], include.InfoBars[0].Actions.Where(a => a.IsVisible).Select(a => a.Name));
                break;
            }

            case "Building":
                await bed.OpenHelloAsync();
                await bed.OpenDocumentAsync(LibPath);
                Assert.True(services.State.TryTransition(new IdeStateInfo(IdeState.Building, BuildKind.Build, false, null, null)));
                Assert.False(commands.CanExecute(Command));
                Assert.Equal((true, false), Action()); // shown, disabled
                await CopyAction((TextDocumentViewModel)services.Documents.ActiveDocument!)!.ExecuteAsync(); // nothing happens
                Assert.False(bed.Fs.FileExists(TargetPath));
                Assert.True(services.State.TryTransition(IdeStateInfo.Idle));
                Assert.True(commands.CanExecute(Command));
                Assert.Equal((true, true), Action());
                break;

            case "Debugging":
                await bed.OpenHelloAsync();
                await bed.OpenDocumentAsync(LibPath);
                Assert.True(services.State.TryTransition(new IdeStateInfo(IdeState.DebugRunning, null, false, null, null)));
                Assert.False(commands.CanExecute(Command));
                Assert.Equal((true, false), Action());
                Assert.True(services.State.TryTransition(new IdeStateInfo(IdeState.Paused, null, false, null, null)));
                Assert.False(commands.CanExecute(Command));
                Assert.Equal((true, false), Action());
                services.State.ForceIdle();
                Assert.True(commands.CanExecute(Command));
                Assert.Equal((true, true), Action());
                break;

            case "Unlocked":
            {
                // After Edit Anyway the bar is gone; the command stays available (IDE-LIB-027, 037).
                await bed.OpenHelloAsync();
                var library = await bed.OpenDocumentAsync(LibPath);
                await commands.ExecuteAsync("file.editLibrarySource");
                Assert.False(library.IsLibraryProtected);
                Assert.Null(Action());
                Assert.True(commands.CanExecute(Command));
                await commands.ExecuteAsync(Command);
                Assert.Equal(TargetPath, services.Documents.ActiveDocument!.FilePath);
                Assert.False(library.IsLibraryProtected); // stays as it was: unlocked
                Assert.False(library.IsReadOnly);
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(situation));
        }
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-LIB-029")]
    public Task UnitWithIncludeDirectiveIsRefused() => TestUiThread.RunAsync(async () =>
    {
        var bed = new BuildTestBed();
        var services = bed.Services;
        await bed.OpenHelloAsync();
        bed.File(Root + @"\Tables.inc", "const Zero = 0;\r\n");
        var body = "\r\ninterface\r\nimplementation\r\nend.\r\n";
        bed.File(Root + @"\Short.pas", "unit Short;\r\ninterface\r\n{$I Tables.inc}\r\nimplementation\r\nend.\r\n");
        bed.File(Root + @"\Long.pas", "unit Long;\r\ninterface\r\n{$include 'Tables.inc'}\r\nimplementation\r\nend.\r\n");
        bed.File(Root + @"\Switches.pas", "unit Switches;\r\n{$I-}\r\n{$I+}\r\n// {$I Tables.inc}\r\n(* {$INCLUDE Tables.inc} *)" + body);
        var files = bed.Fs.AllFiles;
        var projectFile = bed.Text(BuildTestBed.ProjectPath);

        foreach (var name in (string[])["Short.pas", "Long.pas"])
        {
            var library = await bed.OpenDocumentAsync(Path.Combine(Root, name));
            Assert.True(services.Commands.CanExecute(Command)); // refused when run, not disabled
            await services.Commands.ExecuteAsync(Command);

            var text = name + " cannot be copied into the project because it includes other files.";
            Assert.Equal(Format(Strings.Msg_File_LibraryCopyHasIncludes, name), text);
            Assert.Equal(text, services.Status.Message);
            Assert.Contains(bed.Log.Lines, l => l.Contains(text, StringComparison.Ordinal));
            Assert.Same(library, services.Documents.ActiveDocument);
            Assert.Equal(files, bed.Fs.AllFiles); // nothing written
            Assert.Equal(projectFile, bed.Text(BuildTestBed.ProjectPath));
            Assert.Equal(["Hello.pas|", "Unit1.pas|"], Entries(bed));
        }

        // The switches {$I+} / {$I-} and a directive inside a comment are no include directives.
        await bed.OpenDocumentAsync(Root + @"\Switches.pas");
        await services.Commands.ExecuteAsync(Command);
        Assert.Equal(BuildTestBed.Dir + @"\Switches.pas", services.Documents.ActiveDocument!.FilePath);
        Assert.Equal(bed.Fs.ReadAllBytes(Root + @"\Switches.pas"), bed.Fs.ReadAllBytes(BuildTestBed.Dir + @"\Switches.pas"));
        Assert.Equal(["Hello.pas|", "Unit1.pas|", "Switches.pas|"], Entries(bed));
        Assert.Empty(bed.Dialogs.Requests);
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-LIB-030")]
    public Task ExistingTargetIsNeverOverwritten() => TestUiThread.RunAsync(async () =>
    {
        var bed = new BuildTestBed();
        var services = bed.Services;
        await bed.OpenHelloAsync();
        Assert.True(await services.Documents.CloseAllAsync());

        // The target is a member (the project's own Unit1.pas): it is opened; the status bar says so; no question.
        var unit1 = bed.Fs.ReadAllBytes(BuildTestBed.UnitPath);
        bed.File(Root + @"\Unit1.pas", LibText.Replace("LibUnit", "Unit1", StringComparison.Ordinal));
        await bed.OpenDocumentAsync(Root + @"\Unit1.pas");
        await services.Commands.ExecuteAsync(Command);
        Assert.Equal(BuildTestBed.UnitPath, services.Documents.ActiveDocument!.FilePath);
        Assert.Equal("The project already has its own Unit1.pas.", services.Status.Message);
        Assert.Contains(bed.Log.Lines, l => l.Contains(services.Status.Message!, StringComparison.Ordinal)); // UI-ED-149
        Assert.Equal(unit1, bed.Fs.ReadAllBytes(BuildTestBed.UnitPath));
        Assert.Equal(["Hello.pas|", "Unit1.pas|"], Entries(bed));
        Assert.Empty(bed.Dialogs.Requests);

        // The target exists and is not a member: the question. Cancel does nothing.
        var local = Encoding.UTF8.GetBytes("unit LibUnit;\r\ninterface\r\n// the project's own\r\nimplementation\r\nend.\r\n");
        bed.Fs.WriteAllBytes(TargetPath, local);
        bed.File(LibPath, LibText);
        var library = await bed.OpenDocumentAsync(LibPath);
        var files = bed.Fs.AllFiles;
        var projectFile = bed.Text(BuildTestBed.ProjectPath);
        bed.Dialogs.Enqueue("Msg.File.LibraryCopyExists", "Cancel", "AddAndOpen");
        await services.Commands.ExecuteAsync(Command);
        var question = Assert.Single(bed.Dialogs.Requests);
        Assert.Equal(("Msg.File.LibraryCopyExists", "LibUnit.pas"), (question.MessageId, Assert.Single(question.Args)));
        Assert.Equal("The project folder already contains {0}. It will not be replaced.\nAdd the existing file to the project and open it?",
            Strings.Msg_File_LibraryCopyExists);
        Assert.Equal((Core.Dialogs.MessageKind.Question, "AddAndOpen", "Cancel"),
            (Core.Dialogs.MessageDefinitions.Get(question.MessageId).Kind, Core.Dialogs.MessageDefinitions.Get(question.MessageId).Buttons[0],
                Core.Dialogs.MessageDefinitions.Get(question.MessageId).Buttons[1]));
        Assert.Equal("Add and Open", Core.Dialogs.MessageDefinitions.ButtonLabel("AddAndOpen"));
        Assert.Same(library, services.Documents.ActiveDocument);
        Assert.Equal(local, bed.Fs.ReadAllBytes(TargetPath));
        Assert.Equal(files, bed.Fs.AllFiles);
        Assert.Equal(projectFile, bed.Text(BuildTestBed.ProjectPath));
        Assert.Equal(["Hello.pas|", "Unit1.pas|"], Entries(bed));

        // Add and Open adds the existing file without copying.
        await services.Commands.ExecuteAsync(Command);
        Assert.Equal(2, bed.Dialogs.Requests.Count);
        Assert.Equal(local, bed.Fs.ReadAllBytes(TargetPath));
        Assert.Equal(files, bed.Fs.AllFiles);
        Assert.Equal(["Hello.pas|", "Unit1.pas|", "LibUnit.pas|"], Entries(bed));
        var opened = Assert.IsType<TextDocumentViewModel>(services.Documents.ActiveDocument);
        Assert.Equal(TargetPath, opened.FilePath);
        Assert.Contains("// the project's own", opened.Buffer.Text, StringComparison.Ordinal);
        Assert.False(opened.IsReadOnly);
        Assert.True(library.IsLibraryProtected);
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-LIB-031")]
    [Trait("Req", "IDE-LIB-032")]
    public Task CompanionFormIsCopiedWithItsUnit() => TestUiThread.RunAsync(async () =>
    {
        var bed = new BuildTestBed();
        var services = bed.Services;
        await bed.OpenHelloAsync();
        const string unit = "unit LibForm;\r\ninterface\r\n{$FORM LibForm.form}\r\nimplementation\r\nend.\r\n";
        const string form = "object LibForm: TForm\r\n  Caption = 'Library'\r\nend\r\n";
        bed.File(Root + @"\LibForm.pas", unit);
        bed.File(Root + @"\LibForm.form", form);
        // A form beside a unit that does not name it is not its companion.
        bed.File(LibPath, LibText);
        bed.File(Root + @"\LibUnit.form", form);
        var targetUnit = BuildTestBed.Dir + @"\LibForm.pas";
        var targetForm = BuildTestBed.Dir + @"\LibForm.form";

        // A file of the form's name in the project directory: the whole action is refused, nothing is written.
        bed.File(targetForm, "object Mine: TForm\r\nend\r\n");
        var files = bed.Fs.AllFiles;
        var library = Assert.IsAssignableFrom<TextDocumentViewModel>((await services.Documents.OpenAsync(Root + @"\LibForm.pas")).Value);
        Assert.True(library.IsLibraryProtected);
        await services.Commands.ExecuteAsync(Command);
        Assert.Equal(files, bed.Fs.AllFiles);
        Assert.Equal("object Mine: TForm\r\nend\r\n", bed.Text(targetForm));
        Assert.Equal(["Hello.pas|", "Unit1.pas|"], Entries(bed));
        Assert.Same(library, services.Documents.ActiveDocument);
        Assert.Equal("The project already has its own LibForm.form.", services.Status.Message);
        Assert.Empty(bed.Dialogs.Requests);

        // Without it: unit and form are copied; the entry has the form member.
        bed.Fs.DeleteFile(targetForm);
        await services.Commands.ExecuteAsync(Command);
        Assert.Equal(bed.Fs.ReadAllBytes(Root + @"\LibForm.pas"), bed.Fs.ReadAllBytes(targetUnit));
        Assert.Equal(bed.Fs.ReadAllBytes(Root + @"\LibForm.form"), bed.Fs.ReadAllBytes(targetForm));
        Assert.Equal(["Hello.pas|", "Unit1.pas|", "LibForm.pas|LibForm.form"], Entries(bed));
        Assert.Contains("\"form\": \"LibForm.form\"", bed.Text(BuildTestBed.ProjectPath), StringComparison.Ordinal);
        Assert.Equal(targetUnit, services.Documents.ActiveDocument!.FilePath);
        Assert.Equal("LibForm.pas was copied into the project. This project is now built from the copy.", services.Status.Message);

        await services.Documents.OpenAsync(LibPath);
        await services.Commands.ExecuteAsync(Command);
        Assert.True(bed.Fs.FileExists(TargetPath));
        Assert.False(bed.Fs.FileExists(BuildTestBed.Dir + @"\LibUnit.form"));
        Assert.Equal(["Hello.pas|", "Unit1.pas|", "LibForm.pas|LibForm.form", "LibUnit.pas|"], Entries(bed));
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-LIB-031")]
    public Task UnsavedLibraryDocumentIsRefused() => TestUiThread.RunAsync(async () =>
    {
        var bed = new BuildTestBed();
        var services = bed.Services;
        await bed.OpenHelloAsync();
        bed.File(LibPath, LibText);
        var library = await bed.OpenDocumentAsync(LibPath);
        await services.Commands.ExecuteAsync("file.editLibrarySource");
        BuildTestBed.Type(library, "// changed\r\n");
        Assert.True(library.IsModified);
        var files = bed.Fs.AllFiles;

        Assert.True(services.Commands.CanExecute(Command));
        await services.Commands.ExecuteAsync(Command);

        Assert.Equal("Save or revert LibUnit.pas before copying it into the project.", services.Status.Message);
        Assert.Contains(bed.Log.Lines, l => l.Contains(services.Status.Message!, StringComparison.Ordinal)); // UI-ED-149
        Assert.Equal(files, bed.Fs.AllFiles);
        Assert.Equal(["Hello.pas|", "Unit1.pas|"], Entries(bed));
        Assert.Same(library, services.Documents.ActiveDocument);
        Assert.True(library.IsModified);

        // Saved: the file on disk — with the change — is copied.
        Assert.True(await services.Documents.SaveAsync(library));
        await services.Commands.ExecuteAsync(Command);
        Assert.Equal(bed.Fs.ReadAllBytes(LibPath), bed.Fs.ReadAllBytes(TargetPath));
        Assert.StartsWith("// changed", bed.Text(TargetPath), StringComparison.Ordinal);
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-LIB-032")]
    public Task FailureLeavesNothingBehind() => TestUiThread.RunAsync(async () =>
    {
        var (bed, recording) = RecordingBed();
        var services = bed.Services;
        await bed.OpenHelloAsync();
        const string unit = "unit LibForm;\r\ninterface\r\n{$FORM LibForm.form}\r\nimplementation\r\nend.\r\n";
        bed.File(Root + @"\LibForm.pas", unit);
        bed.File(Root + @"\LibForm.form", "object LibForm: TForm\r\nend\r\n");
        var targetUnit = BuildTestBed.Dir + @"\LibForm.pas";
        var targetForm = BuildTestBed.Dir + @"\LibForm.form";
        var library = Assert.IsAssignableFrom<TextDocumentViewModel>((await services.Documents.OpenAsync(Root + @"\LibForm.pas")).Value);
        var files = bed.Fs.AllFiles;
        var projectFile = bed.Text(BuildTestBed.ProjectPath);
        bed.Dialogs.Answer("Msg.File.LibraryCopyFailed", "OK");

        void AssertNothingHappened(string systemText)
        {
            var failed = bed.Dialogs.Requests[^1];
            Assert.Equal(("Msg.File.LibraryCopyFailed", "LibForm.pas", systemText), (failed.MessageId, failed.Args[0], failed.Args[1]));
            Assert.Equal(2, failed.Args.Count);
            Assert.Equal(files, bed.Fs.AllFiles);
            Assert.Equal(projectFile, bed.Text(BuildTestBed.ProjectPath));
            Assert.Equal(["Hello.pas|", "Unit1.pas|"], Entries(bed));
            Assert.False(services.Projects.IsMember(targetUnit));
            Assert.Same(library, services.Documents.ActiveDocument);
            Assert.True(library.IsLibraryProtected);
        }

        Assert.Equal("{0} could not be copied into the project.\n{1}", Strings.Msg_File_LibraryCopyFailed);
        Assert.Equal(Core.Dialogs.MessageKind.Error, Core.Dialogs.MessageDefinitions.Get("Msg.File.LibraryCopyFailed").Kind);

        // The unit cannot be written: no entry, no file.
        recording.FailWith = call => call == "WriteAllBytes " + targetUnit ? new IOException("The disk is full.") : null;
        await services.Commands.ExecuteAsync(Command);
        Assert.Single(bed.Dialogs.Requests);
        AssertNothingHappened("The disk is full.");

        // The form cannot be written: the unit the action created is deleted again.
        recording.FailWith = call => call == "WriteAllBytes " + targetForm ? new UnauthorizedAccessException("Access is denied.") : null;
        await services.Commands.ExecuteAsync(Command);
        Assert.Equal(2, bed.Dialogs.Requests.Count);
        AssertNothingHappened("Access is denied.");

        // The library file cannot be read: nothing is created.
        recording.FailWith = call => call == "ReadAllBytes " + Root + @"\LibForm.form" ? new IOException("The file is in use.") : null;
        await services.Commands.ExecuteAsync(Command);
        Assert.Equal(3, bed.Dialogs.Requests.Count);
        AssertNothingHappened("The file is in use.");

        // Adding fails (the project file cannot be saved): both created files are deleted again.
        recording.FailWith = call => IsWrite(call) && call.Contains("Hello.pproj", StringComparison.OrdinalIgnoreCase) ? new IOException("The project file is locked.") : null;
        await services.Commands.ExecuteAsync(Command);
        Assert.Equal(4, bed.Dialogs.Requests.Count);
        AssertNothingHappened("The project file is locked.");
        Assert.Contains(recording.Calls, c => c == "DeleteFile " + targetUnit);
        Assert.Contains(recording.Calls, c => c == "DeleteFile " + targetForm);

        // Nothing fails: it works after all.
        recording.FailWith = null;
        await services.Commands.ExecuteAsync(Command);
        Assert.Equal(4, bed.Dialogs.Requests.Count);
        Assert.Equal(["Hello.pas|", "Unit1.pas|", "LibForm.pas|LibForm.form"], Entries(bed));
        Assert.Equal(targetUnit, services.Documents.ActiveDocument!.FilePath);
    });

    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-LIB-034")]
    public Task GoToDeclarationFollowsTheCopy() => TestUiThread.RunAsync(async () =>
    {
        var bed = new BuildTestBed();
        var services = bed.Services;
        var src = Path.Combine(SpecTables.RepoRootPath(), "rtl", "src");
        foreach (var file in Directory.GetFiles(src, "*.inc").Append(Path.Combine(src, "System.pas")).Append(Path.Combine(src, "unit-aliases.json")).Where(File.Exists))
            bed.File(Path.Combine(Root, Path.GetFileName(file)), File.ReadAllText(file));
        bed.File(LibPath, LibText);
        await bed.OpenHelloAsync();
        Assert.True(await services.Documents.CloseAllAsync());
        bed.File(BuildTestBed.MainPath, "program Hello;\nuses LibUnit;\nbegin\n  LibProc;\nend.\n");
        var main = await bed.OpenDocumentAsync(BuildTestBed.MainPath);

        async Task<TextDocumentViewModel> GoAsync()
        {
            services.Documents.Activate(main);
            main.Selection = TextSelection.At(main.Buffer.Text.IndexOf("LibProc", StringComparison.Ordinal) + 1);
            Assert.True(services.Commands.CanExecute("search.goToDeclaration"));
            await services.Commands.ExecuteAsync("search.goToDeclaration");
            return Assert.IsType<TextDocumentViewModel>(services.Documents.ActiveDocument);
        }

        // Before: the library file, protected.
        var library = await GoAsync();
        Assert.Equal(LibPath, library.FilePath);
        Assert.True(library.IsLibraryProtected);

        // After Copy to Project: the copy.
        await services.Commands.ExecuteAsync(Command);
        var copy = Assert.IsType<TextDocumentViewModel>(services.Documents.ActiveDocument);
        Assert.Equal(TargetPath, copy.FilePath);
        copy.ViewState.Caret = new TextPosition(1, 1);
        Assert.Same(copy, await GoAsync());
        Assert.Equal(6, copy.ViewState.Caret.Line); // the routine (its implementation heading)
        Assert.False(copy.IsLibraryProtected);

        // After removing it from the project and from the disk: the library file again; nothing else to undo.
        bed.Dialogs.Answer("Msg.Project.RemoveFileConfirm", "Remove");
        Assert.True(await services.Projects.RemoveFromProjectAsync(["LibUnit.pas"]));
        if (services.Documents.Documents.Contains(copy))
            Assert.True(await services.Documents.CloseAsync(copy));
        bed.Fs.DeleteFile(TargetPath);
        Assert.Equal(["Hello.pas|", "Unit1.pas|"], Entries(bed));
        Assert.Same(library, await GoAsync());
        Assert.True(library.IsLibraryProtected);
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-LIB-035")]
    public Task NothingIsWrittenUnderALibraryRoot() => TestUiThread.RunAsync(async () =>
    {
        var (bed, recording) = RecordingBed();
        var services = bed.Services;
        await bed.OpenHelloAsync();
        bed.File(BuildTestBed.MainPath, "program Hello;\nuses Unit1, LibUnit;\nbegin\nend.\n");
        bed.File(LibPath, LibText);
        bed.File(Root + @"\LibForm.pas", "unit LibForm;\r\ninterface\r\n{$FORM LibForm.form}\r\nimplementation\r\nend.\r\n");
        bed.File(Root + @"\LibForm.form", "object LibForm: TForm\r\nend\r\n");
        bed.File(Root + @"\Inc.pas", "unit Inc;\r\ninterface\r\n{$I Tables.inc}\r\nimplementation\r\nend.\r\n");
        bed.Fs.SetReadOnly(LibPath, true);
        var before = bed.Fs.AllFiles.Where(f => f.StartsWith(Root + @"\", StringComparison.OrdinalIgnoreCase)).ToDictionary(f => f, f => bed.Fs.ReadAllBytes(f));
        bed.Dialogs.Answer("Msg.File.LibraryCopyExists", "AddAndOpen");

        // Every outcome: copied, copied with a form, refused (includes), already a member, existing file added.
        foreach (var name in (string[])["LibUnit.pas", "LibForm.pas", "Inc.pas", "LibUnit.pas"])
        {
            await services.Documents.OpenAsync(Path.Combine(Root, name));
            await services.Commands.ExecuteAsync(Command);
        }

        bed.File(Root + @"\Other.pas", "unit Other;\r\ninterface\r\nimplementation\r\nend.\r\n");
        before[Root + @"\Other.pas"] = bed.Fs.ReadAllBytes(Root + @"\Other.pas");
        bed.File(BuildTestBed.Dir + @"\Other.pas", "unit Other;\r\ninterface\r\nimplementation\r\nend.\r\n");
        await services.Documents.OpenAsync(Root + @"\Other.pas");
        await services.Commands.ExecuteAsync(Command);
        Assert.Equal(["Hello.pas|", "Unit1.pas|", "LibUnit.pas|", "LibForm.pas|LibForm.form", "Other.pas|"], Entries(bed));

        // The file system write log has no call on a path under the library root.
        Assert.DoesNotContain(recording.Calls, call => IsWrite(call) && call.Contains(Root + @"\", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(recording.Calls, call => call == "WriteAllBytes " + TargetPath); // the log does see the writes
        Assert.Equal(before.Keys.Order(), bed.Fs.AllFiles.Where(f => f.StartsWith(Root + @"\", StringComparison.OrdinalIgnoreCase)).Order());
        Assert.All(before, pair => Assert.Equal(pair.Value, bed.Fs.ReadAllBytes(pair.Key)));
        Assert.True(bed.Fs.IsReadOnly(LibPath));

        // Not a refactoring: no uses clause and no search path changed.
        Assert.Equal("program Hello;\nuses Unit1, LibUnit;\nbegin\nend.\n", bed.Text(BuildTestBed.MainPath));
        Assert.Matches(@"""searchPaths"": \[\s*\]", bed.Text(BuildTestBed.ProjectPath));
    });
}
