using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Services;

/// <summary>architecture.md §4 (Phase 1 gate N3): the Help commands are owned by <see cref="IHelpService"/>.</summary>
[Trait("Phase", "1")]
public sealed class HelpServiceTests
{
    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-MENU-031")]
    public Task HelpCommandsGoThroughTheHelpService() => TestUiThread.RunAsync(() => HelpCommandsGoThroughTheHelpServiceOnUiThread()); // I-209

    private async Task HelpCommandsGoThroughTheHelpServiceOnUiThread()
    {
        var dialogs = new FakeDialogService();
        var shell = new FakeShellService();
        var services = TestComposition.Build(o => { o.Dialogs = dialogs; o.Shell = shell; });
        IHelpService help = services.Help;

        help.ShowKeyboardShortcuts();
        var shortcuts = Assert.IsType<GeneratedTextDocumentViewModel>(services.Documents.ActiveDocument);
        Assert.Equal(KeyboardShortcutsDocument.Kind, shortcuts.Kind);
        Assert.True(services.Documents.Close(shortcuts));
        await services.Commands.ExecuteAsync("help.keyboardShortcuts");
        Assert.IsType<GeneratedTextDocumentViewModel>(services.Documents.ActiveDocument);

        dialogs.Script("About", vm => vm.PressCancelAsync());
        dialogs.Script("Licenses", vm => vm.PressCancelAsync());
        await help.ShowAboutAsync();
        await help.ShowLicensesAsync();
        await services.Commands.ExecuteAsync("help.about");
        await services.Commands.ExecuteAsync("help.licenses");
        Assert.Equal(["About", "Licenses", "About", "Licenses"], dialogs.Dialogs.Select(d => d.Name));
        Assert.IsType<AboutViewModel>(dialogs.Dialogs[0]);
        Assert.Empty(shell.Calls); // UI-MENU-031: no Help item hands anything to the OS shell (no browser, no URL)
    }

    /// <summary>The pages of the repository's <c>help</c> folder: (id = relative path without extension, <c>/</c>-separated; file).</summary>
    private static List<(string Id, string File)> RepositoryPages()
    {
        var root = Path.Combine(SpecTables.RepoRootPath(), "help");
        return [.. Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories)
            .Select(f => (Path.ChangeExtension(Path.GetRelativePath(root, f), null).Replace('\\', '/'), f))];
    }

    /// <summary>The first <c># </c> heading of a page.</summary>
    private static string Heading(string file) => File.ReadLines(file).First(l => l.StartsWith("# ", StringComparison.Ordinal))[2..].Trim();

    /// <summary>A composition whose IDE directory holds the repository's help pages below <c>help</c> (as the build deploys them).</summary>
    private static Composition.IdeServices BuildWithHelp(FakeDialogService dialogs, FakeShellService? shell = null) =>
        BuildWithPages(dialogs, shell, [.. RepositoryPages().Select(p => (p.Id, File.ReadAllBytes(p.File)))]).Services;

    /// <summary>A composition whose IDE directory holds these pages (id, bytes) below <c>help</c>; also its file system and that folder.</summary>
    private static (Composition.IdeServices Services, InMemoryFileSystem Files, string HelpDir) BuildWithPages(
        FakeDialogService dialogs, FakeShellService? shell, params (string Id, byte[] Content)[] pages)
    {
        var files = new InMemoryFileSystem();
        var ide = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"), "ide");
        foreach (var (id, content) in pages)
        {
            var path = Path.Combine(ide, "help", id.Replace('/', '\\') + ".md");
            files.CreateDirectory(Path.GetDirectoryName(path)!);
            files.WriteAllBytes(path, content);
        }

        return (TestComposition.Build(o =>
        {
            o.Dialogs = dialogs;
            o.Shell = shell;
            o.FileSystem = files;
            o.ToolchainEnvironment = new ToolchainEnvironment(null, ide, []);
        }), files, Path.Combine(ide, "help"));
    }

    /// <summary>
    /// UI-DLG-280: a page's row text is its first <c># </c> heading, else its id; its id is the path below <c>help</c>
    /// without the extension, <c>/</c>-separated; the pages are sorted by title, then id, both ordinal and
    /// case-insensitive; the folder is read every time the pages are asked for.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-DLG-280")]
    public Task PagesAreSortedByTitleThenId() => TestUiThread.RunAsync(() =>
    {
        static byte[] Page(string text) => System.Text.Encoding.UTF8.GetBytes(text);
        var (services, files, helpDir) = BuildWithPages(new FakeDialogService(), null,
            ("zeta", Page("# alpha\n")),                               // the same title but for its case: ordered by id
            ("Beta", Page("# Alpha\n")),
            ("sub/dir/alpha", Page("intro\n\n# Alpha\n# Second\n")), // the first "# " heading, wherever it is
            ("no-heading", Page("## Only a second-level heading\n#NoSpace\n")),
            ("b-page", Page("\uFEFF# _Underscore\n")),                 // a byte-order mark is not part of the title
            ("a-page", Page("#  Émile  \n")));                         // ordinal: after every ASCII title
        Assert.Equal(
        [
            ("Beta", "Alpha"),
            ("sub/dir/alpha", "Alpha"),
            ("zeta", "alpha"),
            ("no-heading", "no-heading"),
            ("b-page", "_Underscore"),
            ("a-page", "Émile"),
        ], services.Help.Pages.Select(p => (p.Id, p.Title)));

        // Read from the disk on every call: a page added later is listed.
        files.WriteAllBytes(Path.Combine(helpDir, "added.md"), Page("# Added\n"));
        Assert.Equal("added", services.Help.Pages[0].Id);
        Assert.Equal(7, services.Help.Pages.Count);
        return Task.CompletedTask;
    });

    [Fact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-MENU-031")]
    [Trait("Req", "UI-DLG-280")]
    [Trait("Req", "UI-DLG-282")]
    public Task ContentsListsEveryPage() => TestUiThread.RunAsync(() => ContentsListsEveryPageOnUiThread()); // I-257

    private async Task ContentsListsEveryPageOnUiThread()
    {
        var expected = RepositoryPages().Select(p => (p.Id, Title: Heading(p.File)))
            .OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.True(expected.Count >= 3, "the repository has help pages");
        var dialogs = new FakeDialogService();
        var services = BuildWithHelp(dialogs);
        Assert.Equal(expected, services.Help.Pages.Select(p => (p.Id, p.Title)));

        // Help > Contents... lists them, the first selected; Cancel opens nothing.
        dialogs.Script(HelpContentsViewModel.DialogName, vm =>
        {
            var contents = Assert.IsType<HelpContentsViewModel>(vm);
            Assert.Equal(expected, contents.Pages.Select(p => (p.Id, p.Title)));
            Assert.Same(contents.Pages[0], contents.Selected);
            return vm.PressCancelAsync();
        });
        Assert.True(services.Commands.CanExecute("help.contents"));
        await services.Commands.ExecuteAsync("help.contents");
        Assert.Single(dialogs.Dialogs);
        Assert.Null(services.Documents.ActiveDocument);

        // No page installed: the list is empty and Open is disabled.
        var empty = TestComposition.Build(o => o.Dialogs = dialogs);
        Assert.Empty(empty.Help.Pages);
        dialogs.Script(HelpContentsViewModel.DialogName, async vm =>
        {
            var contents = Assert.IsType<HelpContentsViewModel>(vm);
            Assert.Empty(contents.Pages);
            Assert.False(contents.DefaultButton!.IsEnabled);
            await vm.PressDefaultAsync();
            Assert.False(vm.IsClosed);
            await vm.PressCancelAsync();
        });
        await empty.Commands.ExecuteAsync("help.contents");
        Assert.Null(empty.Documents.ActiveDocument);
    }

    [Fact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-MENU-031")]
    [Trait("Req", "UI-MENU-032")]
    [Trait("Req", "UI-DLG-281")]
    public Task OpenPageIsReadOnly() => TestUiThread.RunAsync(() => OpenPageIsReadOnlyOnUiThread()); // I-257

    private async Task OpenPageIsReadOnlyOnUiThread()
    {
        var dialogs = new FakeDialogService();
        var shell = new FakeShellService();
        var services = BuildWithHelp(dialogs, shell);
        var (id, file) = RepositoryPages().Single(p => p.Id == "library-source");
        dialogs.Script(HelpContentsViewModel.DialogName, vm =>
        {
            var contents = (HelpContentsViewModel)vm;
            contents.Selected = contents.Pages.Single(p => p.Id == id);
            return vm.PressDefaultAsync();
        });

        await services.Commands.ExecuteAsync("help.contents");
        var page = Assert.IsType<GeneratedTextDocumentViewModel>(services.Documents.ActiveDocument);
        Assert.Equal((Heading(file), null, false, true), (page.Title, page.FilePath, page.IsModified, page.IsReadOnly));
        Assert.Equal("help." + id, page.Kind);
        Assert.True(page.Content.Buffer.IsReadOnly);
        // UI-MENU-032: the text shown is derived from the page (its tables aligned, no delimiter row), line by line.
        var shown = HelpPageFormatter.Format(File.ReadAllText(file));
        Assert.Equal(shown.Text, page.Text);
        Assert.Equal(shown.Lines, page.HelpLines);
        Assert.Contains(HelpLineKind.TableHeader, page.HelpLines!);
        Assert.DoesNotContain("|---", page.Text, StringComparison.Ordinal);
        Assert.StartsWith("# Library source\r\n", page.Text, StringComparison.Ordinal);
        Assert.False(services.Commands.CanExecute("file.save")); // not a file document: nothing to save, nothing to overwrite
        Assert.False(services.Commands.CanExecute("file.saveAs"));

        // The same page again: its tab is reused; another page gets its own tab.
        await services.Commands.ExecuteAsync("help.contents");
        Assert.Same(page, Assert.Single(services.Documents.Documents));
        services.Help.OpenPage(services.Help.Pages.First(p => p.Id != id));
        var other = Assert.IsType<GeneratedTextDocumentViewModel>(services.Documents.ActiveDocument);
        Assert.NotSame(page, other);
        Assert.True(other.IsReadOnly);
        Assert.Equal(2, services.Documents.Documents.Count);
        Assert.Empty(shell.Calls); // UI-MENU-031: no browser, no network resource
    }
}
