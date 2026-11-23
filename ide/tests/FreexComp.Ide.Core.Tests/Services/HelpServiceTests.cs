using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Services;
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
}
