using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Layout;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;

namespace FreexComp.Ide.Core.Services;

/// <summary>
/// About, licenses and the keyboard-shortcut document (architecture.md §4; owner of <c>help.about</c>,
/// <c>help.licenses</c>, <c>help.keyboardShortcuts</c>, commands.md §2.11). Nothing here opens a browser or a network
/// resource (UI-MENU-031). UI thread only.
/// </summary>
public interface IHelpService
{
    /// <summary><c>help.about</c>: the About dialog; its <c>Third-Party Licenses...</c> button opens the licenses after it closed.</summary>
    Task ShowAboutAsync();

    /// <summary><c>help.licenses</c>: the Third-Party Licenses dialog from the embedded notices.</summary>
    Task ShowLicensesAsync();

    /// <summary><c>help.keyboardShortcuts</c> (UI-MENU-030): the generated document of the current keymap.</summary>
    void ShowKeyboardShortcuts();
}

public sealed class HelpService(
    IDialogService dialogs, IUserPaths paths, IClipboardService clipboard, ILayoutService layout, IDocumentService documents,
    IKeymapService keymap) : IHelpService
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

    public void RegisterCommands(ICommandService commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
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
