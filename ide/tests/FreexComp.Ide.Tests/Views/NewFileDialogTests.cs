using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The New File dialog (dialogs.md §4, UI-DLG-040…042) and <c>file.newUnit</c> without a project (IDE-P1-033).</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class NewFileDialogTests
{
    /// <summary>Sets a field's text; the TextBox raises <c>TextChanged</c> through the dispatcher.</summary>
    private static void SetText(DialogDriver dlg, string field, string text)
    {
        dlg.Find<TextBox>(field).Text = text;
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-040")]
    [Trait("Req", "UI-DLG-041")]
    [Trait("Req", "UI-DLG-042")]
    [Trait("Req", "IDE-P1-033")]
    [Trait("Req", "IDE-P1-079")]
    [Trait("Req", "IDE-PM-031")]
    public async Task UnitNameUniqueness()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles[@"Hello\Hello.pproj"] = """
                { "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas",
                  "files": [ { "path": "Hello.pas" }, { "path": "Unit1.pas" }, { "path": "lib/Unit3.pas" } ] }
                """;
            o.InitialFiles[@"Hello\Hello.pas"] = "program Hello;\nbegin\nend.\n";
            o.InitialFiles[@"Hello\Unit1.pas"] = "unit Unit1;\ninterface\nimplementation\nend.\n";
            o.InitialFiles[@"Hello\lib\Unit3.pas"] = "unit Unit3;\ninterface\nimplementation\nend.\n";
            o.InitialFiles[@"Hello\Unit2.pas"] = "unit Unit2;\ninterface\nimplementation\nend.\n"; // on disk, not in the project
            o.InitialFiles[@"Notes\readme.txt"] = "notes\n";
            o.InitialFiles[@"Notes\Untitled1.txt"] = "taken\n";
        });
        var dir = Path.Combine(h.TempDir, "Hello");
        Assert.True(await h.Services.Projects.OpenAsync(Path.Combine(dir, "Hello.pproj")));

        // Unit<n>: Unit1 is a project unit, Unit2 a file in the folder, Unit3 a project unit elsewhere → Unit4.
        var running = h.Services.Commands.ExecuteAsync("file.newFile");
        var dlg = await h.WaitForDialogAsync("NewFile");
        Assert.Equal("New File", dlg.Window.Title);
        Assert.Equal((520.0, false), (dlg.Window.Width, dlg.Window.CanResize));
        var types = dlg.Find<ListBox>("Types");
        Assert.Equal(["Dialog.NewFile.Types.Unit", "Dialog.NewFile.Types.TextFile"],
            Enumerable.Range(0, types.ItemCount).Select(i => AutomationIds.Get(types.ContainerFromIndex(i)!)));
        Assert.True(((ListBoxItem)types.ContainerFromIndex(0)!).IsSelected);
        var name = dlg.Find<TextBox>("Name");
        Assert.Equal("Unit4", name.Text);
        Assert.Equal("Dialog.NewFile.Name", dlg.FocusedAutomationId);
        Assert.True(dlg.Find<TextBlock>("Extension").IsEffectivelyVisible);
        Assert.Equal(".pas", dlg.Find<TextBlock>("Extension").Text);
        Assert.Equal(dir, dlg.Find<TextBox>("Folder").Text);
        var add = dlg.Find<CheckBox>("AddToProject");
        Assert.True(add.IsEffectivelyVisible);
        Assert.True(add.IsChecked);
        Assert.Equal("_Add to project Hello", add.Content);
        var ok = dlg.Find<Button>("Ok");
        Assert.True(ok.IsEnabled);
        Assert.Null(dlg.ErrorText("Name"));

        // Unit names are unique in the project (case-insensitive), file names in the folder.
        SetText(dlg, "Name", "unit3");
        Assert.Equal("The project already contains a unit named unit3. Unit names must be unique within a project.", dlg.ErrorText("Name"));
        Assert.Contains("invalid", name.Classes);
        Assert.False(ok.IsEnabled);
        SetText(dlg, "Name", "Unit1");
        Assert.Equal("The project already contains a unit named Unit1. Unit names must be unique within a project.", dlg.ErrorText("Name"));
        SetText(dlg, "Name", "Unit2");
        Assert.Equal("A file named Unit2.pas already exists in this folder.", dlg.ErrorText("Name"));
        SetText(dlg, "Name", "begin");
        Assert.Equal("begin is a reserved word and cannot be used as a project name.", dlg.ErrorText("Name"));
        SetText(dlg, "Name", "2nd");
        Assert.Equal("A unit file name is a valid identifier followed by .pas.", dlg.ErrorText("Name"));
        SetText(dlg, "Name", "");
        Assert.Equal("A unit file name is a valid identifier followed by .pas.", dlg.ErrorText("Name"));
        dlg.Press("Enter"); // nothing happens while invalid
        Assert.True(dlg.Window.IsVisible);

        // Folder: empty / relative → LocationInvalid; missing → PathNotFound; another folder changes the file clash.
        SetText(dlg, "Name", "Unit2");
        SetText(dlg, "Folder", "");
        Assert.Equal("The folder path is not valid.", dlg.ErrorText("Folder"));
        SetText(dlg, "Folder", @"relative\folder");
        Assert.Equal("The folder path is not valid.", dlg.ErrorText("Folder"));
        SetText(dlg, "Folder", Path.Combine(dir, "nope"));
        Assert.Equal("The folder does not exist.", dlg.ErrorText("Folder"));
        SetText(dlg, "Folder", Path.Combine(dir, "lib"));
        Assert.Null(dlg.ErrorText("Folder"));
        Assert.Null(dlg.ErrorText("Name")); // lib has no Unit2.pas
        Assert.True(ok.IsEnabled);

        // Create: the unit skeleton on disk (CRLF, UTF-8 by default), added to the project, opened with the editor focused.
        SetText(dlg, "Folder", dir);
        SetText(dlg, "Name", "Tools");
        dlg.Click("Ok");
        await running;
        var tools = Path.Combine(dir, "Tools.pas");
        Assert.Equal("unit Tools;\r\n\r\ninterface\r\n\r\nimplementation\r\n\r\nend.\r\n", File.ReadAllText(tools));
        Assert.Contains("\"Tools.pas\"", File.ReadAllText(Path.Combine(dir, "Hello.pproj")), StringComparison.Ordinal);
        Assert.True(h.Services.Projects.IsMember(tools));
        Assert.Equal(tools, h.Services.Documents.ActiveDocument?.FilePath);
        Assert.False(h.Services.Documents.ActiveDocument!.IsModified);
        await h.WaitForAsync(() => h.FocusedAutomationId == AutomationIds.Editor("Tools.pas"), "editor focused");

        // Changing the type recomputes the default name while the user has not edited it.
        running = h.Services.Commands.ExecuteAsync("file.newFile");
        dlg = await h.WaitForDialogAsync("NewFile");
        Assert.Equal("Unit4", dlg.Find<TextBox>("Name").Text);
        dlg.Click("Types.TextFile");
        Assert.Equal("Untitled1.txt", dlg.Find<TextBox>("Name").Text);
        Assert.False(dlg.Find<TextBlock>("Extension").IsEffectivelyVisible);
        SetText(dlg, "Name", "a:b");
        Assert.Equal("A file name cannot be empty or contain any of these characters: \\ / : * ? \" < > |", dlg.ErrorText("Name"));
        dlg.Click("Types.Unit");
        Assert.Equal("a:b", dlg.Find<TextBox>("Name").Text); // edited: kept
        Assert.Equal("A unit file name is a valid identifier followed by .pas.", dlg.ErrorText("Name"));

        // A write failure shows Msg.File.SaveFailed and keeps the dialog open; nothing is created.
        SetText(dlg, "Name", "Fails");
        h.FileSystem.FailWhen = call => call.StartsWith("WriteAllBytesDurable", StringComparison.Ordinal) && call.Contains("Fails.pas", StringComparison.Ordinal);
        h.Messages.Answer("Msg.File.SaveFailed", "Cancel");
        dlg.Click("Ok");
        await h.WaitForAsync(() => h.Messages.Requests.Any(r => r.MessageId == "Msg.File.SaveFailed"), "SaveFailed shown");
        Assert.True(dlg.Window.IsVisible);
        Assert.False(File.Exists(Path.Combine(dir, "Fails.pas")));
        Assert.DoesNotContain("Fails.pas", File.ReadAllText(Path.Combine(dir, "Hello.pproj")), StringComparison.Ordinal);
        h.FileSystem.FailWhen = null;
        dlg.Press("Escape");
        await running;

        // Without a project: file.newUnit makes untitled Unit<N>.pas documents (smallest N not open).
        Assert.True(await h.Services.Projects.CloseAsync());
        await h.ExecuteAsync("file.newUnit");
        await h.ExecuteAsync("file.newUnit");
        Assert.Equal(["Unit1.pas", "Unit2.pas"], h.Services.Documents.Documents.OfType<TextDocumentViewModel>().Select(d => d.Title));
        Assert.All(h.Services.Documents.Documents, d => Assert.True(d.IsModified && d.FilePath is null));
        h.Services.Documents.Close(h.Services.Documents.Documents[0]);
        await h.ExecuteAsync("file.newUnit");
        Assert.Equal("Unit1.pas", h.Services.Documents.ActiveDocument?.Title);
        Assert.Equal("unit Unit1;\r\n\r\ninterface\r\n\r\nimplementation\r\n\r\nend.\r\n",
            ((TextDocumentViewModel)h.Services.Documents.ActiveDocument!).Buffer.Text);

        // file.newFile without a project: no Add to project; the folder of the active document; a text file gets .txt.
        var notes = Path.Combine(h.TempDir, "Notes");
        Assert.True((await h.Services.Documents.OpenAsync(Path.Combine(notes, "readme.txt"))).Success);
        running = h.Services.Commands.ExecuteAsync("file.newFile");
        dlg = await h.WaitForDialogAsync("NewFile");
        Assert.False(dlg.Find<CheckBox>("AddToProject").IsVisible);
        Assert.Equal(notes, dlg.Find<TextBox>("Folder").Text);
        Assert.Equal("Unit1", dlg.Find<TextBox>("Name").Text); // an untitled document is not a file in the folder
        dlg.Click("Types.TextFile");
        Assert.Equal("Untitled2.txt", dlg.Find<TextBox>("Name").Text); // Untitled1.txt exists there
        SetText(dlg, "Name", "readme");
        Assert.Equal("A file named readme.txt already exists in this folder.", dlg.ErrorText("Name"));
        SetText(dlg, "Name", "todo");
        dlg.Click("Ok"); // focus is on the type list
        await running;
        var todo = Path.Combine(notes, "todo.txt");
        Assert.Equal("", File.ReadAllText(todo));
        Assert.Equal(todo, h.Services.Documents.ActiveDocument?.FilePath);
        Assert.Null(h.Services.Projects.Project);
    }
}
