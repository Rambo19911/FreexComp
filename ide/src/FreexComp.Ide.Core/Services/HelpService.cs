using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Layout;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;

namespace FreexComp.Ide.Core.Services;

/// <summary>
/// About, licenses, the keyboard-shortcut document and the user help pages (architecture.md §4; owner of
/// <c>help.about</c>, <c>help.licenses</c>, <c>help.keyboardShortcuts</c>, <c>help.contents</c>, commands.md §2.11). Nothing
/// here opens a browser or a network resource (UI-MENU-031). UI thread only.
/// </summary>
public interface IHelpService
{
    /// <summary><c>help.about</c>: the About dialog; its <c>Third-Party Licenses...</c> button opens the licenses after it closed.</summary>
    Task ShowAboutAsync();

    /// <summary><c>help.licenses</c>: the Third-Party Licenses dialog from the embedded notices.</summary>
    Task ShowLicensesAsync();

    /// <summary><c>help.keyboardShortcuts</c> (UI-MENU-030): the generated document of the current keymap.</summary>
    void ShowKeyboardShortcuts();

    /// <summary>
    /// The installed help pages (UI-DLG-280): every <c>*.md</c> below <c>help</c> in the IDE's directory, sorted by title,
    /// then id (both ordinal, case-insensitive).
    /// Read from the disk on every call; empty when the folder is missing.
    /// </summary>
    IReadOnlyList<HelpPage> Pages { get; }

    /// <summary><c>help.contents</c>: the Help Contents dialog; Open shows the chosen page (<see cref="OpenPage"/>).</summary>
    Task ShowContentsAsync();

    /// <summary>
    /// Shows a page as the active document (UI-MENU-032): a read-only generated document (the text
    /// <see cref="HelpPageFormatter"/> derives from its Markdown, kind <see cref="HelpPage.Kind"/>, titled with the page
    /// title); the page's tab is reused when it is already open. The file is only read.
    /// </summary>
    void OpenPage(HelpPage page);
}

/// <summary>
/// A user help page. <paramref name="Id"/>: its path below the help folder without the extension, <c>/</c>-separated
/// (<c>library-source</c>); <paramref name="Title"/>: its first <c># </c> heading, else the id.
/// </summary>
public sealed record HelpPage(string Id, string Title, string Path)
{
    /// <summary>Prefix of the generated-document kind of every help page.</summary>
    public const string KindPrefix = "help.";

    /// <summary>The generated-document kind: <c>help.&lt;Id&gt;</c>.</summary>
    public string Kind => KindPrefix + Id;
}

public sealed class HelpService(
    IDialogService dialogs, IUserPaths paths, IClipboardService clipboard, ILayoutService layout, IDocumentService documents,
    IKeymapService keymap, IFileSystem fileSystem, string helpDirectory) : IHelpService
{
    /// <summary>The About dialog's Toolchain value (dialogs.md §15); null = <c>Msg.About.ToolchainNotFound</c>.</summary>
    public Func<string?> ToolchainText { get; set; } = () => null;

    public async Task ShowAboutAsync()
    {
        if (await dialogs.ShowAsync(new AboutViewModel(paths, clipboard, ToolchainText())) == AboutResult.ShowLicenses)
            await ShowLicensesAsync();
    }

    public async Task ShowLicensesAsync() => await dialogs.ShowAsync(new LicensesViewModel(ThirdPartyNotices.LoadEmbedded(), layout));

    public void ShowKeyboardShortcuts() =>
        documents.ShowGenerated(KeyboardShortcutsDocument.Kind, Strings.Cmd_help_keyboardShortcuts, KeyboardShortcutsDocument.Build(keymap));

    public IReadOnlyList<HelpPage> Pages
    {
        get
        {
            var pages = new List<HelpPage>();
            if (fileSystem.DirectoryExists(helpDirectory))
                Collect(helpDirectory, pages);
            return [.. pages.OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase)];
        }
    }

    public async Task ShowContentsAsync()
    {
        if (await dialogs.ShowAsync(new HelpContentsViewModel(Pages, layout)) is { } page)
            OpenPage(page);
    }

    public void OpenPage(HelpPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var shown = HelpPageFormatter.Format(ReadText(page.Path));
        documents.ShowGenerated(page.Kind, page.Title, shown.Text, shown.Lines);
    }

    private void Collect(string directory, List<HelpPage> pages)
    {
        foreach (var file in fileSystem.EnumerateFiles(directory, "*.md"))
        {
            var id = Path.ChangeExtension(Path.GetRelativePath(helpDirectory, file), null).Replace('\\', '/');
            var heading = ReadText(file).Split("\r\n").FirstOrDefault(l => l.StartsWith("# ", StringComparison.Ordinal));
            pages.Add(new HelpPage(id, heading is null ? id : heading[2..].Trim(), file));
        }

        foreach (var child in fileSystem.EnumerateDirectories(directory))
            Collect(child, pages);
    }

    /// <summary>The page text: UTF-8 (a byte-order mark is dropped), lines ending in CR LF like every generated document.</summary>
    private string ReadText(string path) =>
        System.Text.Encoding.UTF8.GetString(fileSystem.ReadAllBytes(path)).TrimStart('\uFEFF').ReplaceLineEndings("\r\n");

    public void RegisterCommands(ICommandService commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        commands.RegisterHandler("help.contents", new Handler(ShowContentsAsync));
        commands.RegisterHandler("help.about", new Handler(ShowAboutAsync));
        commands.RegisterHandler("help.licenses", new Handler(ShowLicensesAsync));
        commands.RegisterHandler("help.keyboardShortcuts", new Handler(() =>
        {
            ShowKeyboardShortcuts();
            return Task.CompletedTask;
        }));
    }

    private sealed class Handler(Func<Task> execute) : ICommandHandler
    {
        public bool CanExecute(string? argument) => true;

        public Task ExecuteAsync(string? argument) => execute();
    }
}
