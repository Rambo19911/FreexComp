using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// The Templates tool window (I-328, F11-052; code-templates.md §5 UI-TW-1600…1602): the templates of the active document's
/// language with their origin, the filter, insertion at the caret as from Ctrl+J, Edit / New on the Options page, and the
/// refresh when the template set changes.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class TemplatesWindowTests
{
    private const string TeamFile = """
        { "formatVersion": 1, "templates": [
          { "abbreviation": "greet", "description": "Say hello", "language": "pascal", "body": "WriteLn('$text:Hi$');$end$" } ] }
        """;

    private const string UserFile = """
        { "formatVersion": 1, "templates": [
          { "abbreviation": "Mine", "description": "my own", "language": "pascal", "body": "Mine;" } ] }
        """;

    /// <summary>The product's phase (phase 8 is delivered card by card): the Templates window registers.</summary>
    private static Task<HeadlessHarness> StartAsync() =>
        BuildScenario.StartAsync(o =>
        {
            o.UserFiles[@"roaming\templates\team.json"] = Encoding.UTF8.GetBytes(TeamFile);
            o.UserFiles[@"roaming\templates.json"] = Encoding.UTF8.GetBytes(UserFile);
        }, implementedPhase: null);

    private static TemplatesViewModel View(HeadlessHarness h) => Assert.IsType<TemplatesViewModel>(h.Services.ToolWindows.Get(TemplatesViewModel.ToolName));

    private static string MainPath(HeadlessHarness h) => Path.Combine(h.TempDir, @"Hello\Hello.pas");

    private static string Row(DebugGridRow row) => string.Join(" | ", row.Cells.Select(c => c.Text));

    private static async Task<TextDocumentViewModel> OpenMainAsync(HeadlessHarness h)
    {
        var document = Assert.IsType<TextDocumentViewModel>((await h.Services.Documents.OpenAsync(MainPath(h))).Value);
        Dispatcher.UIThread.RunJobs();
        h.Services.Navigation.GoToLine(document, 4, 1); // the line of "end."
        Dispatcher.UIThread.RunJobs();
        return document;
    }

    private static async Task ShowAsync(HeadlessHarness h)
    {
        await h.ExecuteAsync("view.templates");
        await h.WaitForAsync(() => h.FocusedAutomationId == "Tool.Templates.List", "the grid has the focus");
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-1600")]
    [Trait("Req", "UI-TW-1601")]
    public async Task ListFilterAndInsert()
    {
        await using var h = await StartAsync();
        var view = View(h);
        Assert.Null(h.TryFind<Control>("Tool.Templates")); // hidden by default
        var main = await OpenMainAsync(h);

        // UI-TW-1600: view.templates shows the window (focus on the grid) in the right dock, with the Tool Palette.
        await ShowAsync(h);
        Assert.Equal(Strings.Cmd_view_templates, view.Title);
        Assert.Contains(TemplatesViewModel.ToolName, h.Services.Layout.Layout.RightLower.Tools);
        Assert.Equal(["Abbreviation", "Description", "Origin"], view.Columns.Select(c => c.Id));
        Assert.Equal([120d, 0d, 120d], view.Columns.Select(c => c.Width));
        var expected = h.Services.Templates.Templates.Select(t => t.Abbreviation).Order(StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(50, expected.Count); // 48 built-ins, one folder template, one of templates.json
        Assert.Equal(expected, view.Rows.Select(r => r.Key));
        Assert.Equal("greet | Say hello | team.json", Row(view.Rows.Single(r => r.Key == "greet")));
        Assert.Equal("Mine | my own | " + Strings.Templates_Origin_User, Row(view.Rows.Single(r => r.Key == "Mine")));
        Assert.Equal("begin | " + Strings.Template_begin + " | " + Strings.Templates_Origin_BuiltIn, Row(view.Rows.Single(r => r.Key == "begin")));
        Assert.Equal("greet", DebugScenario.Cell(h, "Templates", "greet", "Abbreviation"));

        // Tool bar, context menu, filter box.
        Assert.NotNull(h.TryFind<Control>("Tool.Templates.ToolBar.templates.new"));
        Assert.NotNull(h.TryFind<Control>("Tool.Templates.ToolBar.templates.edit"));
        Assert.Equal(["templates.insert", null, "templates.edit", "templates.new"], view.ContextCommands);
        Assert.Equal([Strings.Cmd_templates_insert, Strings.Cmd_templates_edit, Strings.Cmd_templates_new],
            view.BuildContextMenu().Where(m => m.CommandId is not null).Select(m => m.Label));
        var filter = h.Find<TextBox>("Tool.Templates.Filter");
        Assert.Equal(Strings.Templates_FilterPlaceholder, filter.PlaceholderText);
        Assert.Equal("Filter templates", Strings.Templates_FilterPlaceholder);

        // The filter matches abbreviation and description, case-insensitive; no match: the empty state.
        view.FilterText = "HELLO";
        await h.WaitForAsync(() => view.Rows.Count == 1, "the filter applied");
        Assert.Equal(["greet"], view.Rows.Select(r => r.Key));
        view.FilterText = "fore";
        await h.WaitForAsync(() => view.Rows.Count != 1, "the filter applied");
        Assert.All(view.Rows, r => Assert.True(r.Key.Contains("fore", StringComparison.OrdinalIgnoreCase)
            || r.Cells[1].Text.Contains("fore", StringComparison.OrdinalIgnoreCase)));
        view.FilterText = "zzzz";
        await h.WaitForAsync(() => view.IsEmpty, "the empty state");
        Assert.Equal(Strings.Msg_Templates_Empty, view.EmptyHeading);
        Assert.Equal("No templates match the filter.", Strings.Msg_Templates_Empty);
        Assert.True(h.Find<Control>("Tool.Templates.Empty").IsEffectivelyVisible);
        view.FilterText = "";
        await h.WaitForAsync(() => view.Rows.Count == 50, "the filter cleared");

        // UI-TW-1601: Enter inserts at the caret as Ctrl+J would; the editor gets the focus, in field mode.
        h.Click("Tool.Templates.Row.greet.Description");
        Assert.Equal(["greet"], view.SelectedKeys);
        h.Press("Enter"); // templates.insert
        await h.WaitForAsync(() => h.FocusedAutomationId == "Editor.Hello.pas", "the editor has the focus");
        Assert.Contains("WriteLn('Hi');end.", main.Buffer.Text, StringComparison.Ordinal);
        Assert.True(main.InTemplateFieldMode);
        Assert.Equal("Hi", main.Buffer.GetText(main.Selection.Start, main.Selection.End - main.Selection.Start));
        h.Press("Escape");

        // Double-click is the same default action; a selection becomes $selection$.
        main.Selection = new TextSelection(main.Buffer.Text.IndexOf("uses Unit1;", StringComparison.Ordinal), main.Buffer.Text.IndexOf("uses Unit1;", StringComparison.Ordinal) + 11);
        view.FilterText = "region"; // the grid draws the rows in view only
        await h.WaitForAsync(() => view.Rows.Count == 1, "the filter applied");
        h.DoubleClick("Tool.Templates.Row.region.Description");
        await h.WaitForAsync(() => h.FocusedAutomationId == "Editor.Hello.pas", "the editor has the focus");
        Assert.StartsWith("program Hello;\r\n{$REGION ''}\r\nuses Unit1;\r\n{$ENDREGION}\r\n", main.Buffer.Text, StringComparison.Ordinal);

        // No Pascal document active: all Pascal templates, insertion disabled.
        Assert.True(await h.Services.Documents.SaveAsync(main));
        foreach (var document in h.Services.Documents.Documents.ToList())
            Assert.True(await h.Services.Documents.CloseAsync(document));
        Dispatcher.UIThread.RunJobs();
        view.FilterText = "";
        await h.WaitForAsync(() => view.Rows.Count == 50, "the filter cleared");
        view.SelectedKeys = ["wl"];
        Assert.False(h.Services.Commands.CanExecute("templates.insert"));
        Assert.True(h.Services.Commands.CanExecute("templates.edit"));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-1601")]
    public async Task EditOpensOptionsPage()
    {
        await using var h = await StartAsync();
        var view = View(h);
        await ShowAsync(h);
        Assert.False(h.Services.Commands.CanExecute("templates.edit")); // nothing selected
        Assert.True(h.Services.Commands.CanExecute("templates.new"));

        // templates.edit: Options › Editor › Code Templates with that template selected.
        h.Click("Tool.Templates.Row.greet.Description");
        h.Click("Tool.Templates.ToolBar.templates.edit");
        var dlg = await h.WaitForDialogAsync("Options");
        var options = (OptionsViewModel)dlg.Window.Model;
        Assert.Equal(OptionsViewModel.CodeTemplatesPage, options.SelectedPage!.Id);
        Assert.Equal("greet", options.CodeTemplates.Selected!.Abbreviation);
        Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Options_Templates_Origin, "team.json"),
            options.CodeTemplates.Selected.OriginToolTip);
        Assert.False(options.HasChanges);
        dlg.Click("Cancel");
        await h.WaitForAsync(() => options.IsClosed, "the dialog closed");

        // templates.new: the page, and Templates.Add performed (new1 selected).
        await ShowAsync(h);
        h.Click("Tool.Templates.ToolBar.templates.new");
        dlg = await h.WaitForDialogAsync("Options");
        options = (OptionsViewModel)dlg.Window.Model;
        Assert.Equal(OptionsViewModel.CodeTemplatesPage, options.SelectedPage!.Id);
        Assert.Equal("new1", options.CodeTemplates.Selected!.Abbreviation);
        Assert.True(options.HasChanges);
        dlg.Click("Cancel");
        await h.WaitForAsync(() => options.IsClosed, "the dialog closed");
        Assert.DoesNotContain("new1", view.Rows.Select(r => r.Key));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-1602")]
    [Trait("Req", "IDE-SET-083")]
    public async Task RefreshesOnFolderChange()
    {
        await using var h = await StartAsync();
        var view = View(h);
        await ShowAsync(h);
        h.Click("Tool.Templates.Row.greet.Description");
        Assert.Equal(["greet"], view.SelectedKeys);

        // A changed folder file: the window shows the new set at once, the selection stays on its abbreviation.
        var folder = Path.Combine(h.UserDir, "roaming", "templates");
        var watchers = Assert.IsType<ManualFileWatcherFactory>(h.Services.FileWatchers);
        File.WriteAllText(Path.Combine(folder, "team.json"), """
            { "formatVersion": 1, "templates": [
              { "abbreviation": "greet", "description": "Greet the world", "language": "pascal", "body": "WriteLn('World');" },
              { "abbreviation": "extra", "description": "one more", "language": "pascal", "body": "Extra;" } ] }
            """);
        watchers.Raise(new FileChange(FileChangeKind.Changed, Path.Combine(folder, "team.json")));
        await h.WaitForAsync(() => view.Rows.Any(r => r.Key == "extra"), "the folder change shown");
        Assert.Equal("greet | Greet the world | team.json", Row(view.Rows.Single(r => r.Key == "greet")));
        Assert.Equal(["greet"], view.SelectedKeys);
        Assert.Equal("Greet the world", DebugScenario.Cell(h, "Templates", "greet", "Description"));

        // A dropped file adds its templates; an invalid one is reported in Messages (IDE-SET-083).
        File.WriteAllText(Path.Combine(folder, "bad.json"), "{ nope");
        watchers.Raise(new FileChange(FileChangeKind.Created, Path.Combine(folder, "bad.json")));
        await h.WaitForAsync(() => h.Services.Messages.Items.Count == 1, "the skipped file reported");
        Assert.StartsWith(string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Msg_Templates_FileSkipped, "bad.json", ""),
            h.Services.Messages.Items[0].Text, StringComparison.Ordinal);

        // Options OK (a save of templates.json) refreshes too.
        h.Services.Templates.Save([.. h.Services.Templates.Templates, new CodeTemplate("saved", "from options", "pascal", "Saved;")]);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("saved | from options | " + Strings.Templates_Origin_User, Row(view.Rows.Single(r => r.Key == "saved")));

        // The file deleted: its templates go, the selection with them.
        File.Delete(Path.Combine(folder, "team.json"));
        watchers.Raise(new FileChange(FileChangeKind.Deleted, Path.Combine(folder, "team.json")));
        await h.WaitForAsync(() => view.Rows.All(r => r.Key != "greet"), "the deleted file's templates gone");
        Assert.DoesNotContain("extra", view.Rows.Select(r => r.Key));
        Assert.Empty(view.SelectedKeys);
    }
}
