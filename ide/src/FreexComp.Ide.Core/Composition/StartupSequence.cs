using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;

namespace FreexComp.Ide.Core.Composition;

/// <summary>
/// IDE-ARCH-015 step 9, run once after the main window is shown: the component metadata load (started, not awaited;
/// DSG-TP-003), the recovery check (IDE-P1-123), then the start-up
/// content in the order of IDE-P1-015 — command-line paths (IDE-P1-010), else the last project (IDE-P1-016), else the
/// Welcome page (UI-WIN-071) followed by the loose-file session.
/// </summary>
public static class StartupSequence
{
    private const string LogCategory = "startup";

    public static async Task RunAsync(IdeServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = services.Options;
        // DSG-TP-003 (P5): the component metadata loads in the background; forms opened before it finished wait for it.
        _ = services.Metadata?.LoadAsync();
        await services.Recovery.RecoverAsync(); // does nothing with --safe

        if (options.Paths.Count > 0)
        {
            await OpenPathsAsync(services, options.Paths);
            return;
        }

        // IDE-P1-016: the flagged project, if its file still exists; otherwise on to the next step without a message.
        if (!options.Safe && services.Settings.Get(SettingsCatalog.EnvironmentReopenLastProject)
            && services.RecentItems.LastProject is { } last && services.FileSystem.FileExists(last.Path)
            && await services.Projects.OpenAsync(last.Path))
            return;

        if (services.Settings.Get(SettingsCatalog.EnvironmentShowWelcomeOnStartup))
            services.Documents.ShowWelcomePage();
        if (services.Projects.Project is null)
            await services.Session.RestoreAsync(services.Settings.Get(SettingsCatalog.EnvironmentRestoreOpenFiles)); // the loose-file session
    }

    /// <summary>
    /// IDE-P1-010 (command line) and IDE-P1-031 (files dropped onto the main window): the first project opens as the
    /// project, groups are refused, other files open as documents, in the given order; problems are reported per path.
    /// </summary>
    public static async Task OpenPathsAsync(IdeServices services, IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(paths);
        var projectSeen = false;
        foreach (var path in paths)
        {
            var extension = Path.GetExtension(path);
            if (!services.FileSystem.FileExists(path))
            {
                await services.Dialogs.ShowMessageAsync(new MessageRequest("Msg.File.NotFound", [path]));
            }
            else if (extension.Equals(".pgroup", StringComparison.OrdinalIgnoreCase))
            {
                await services.Dialogs.ShowMessageAsync(new MessageRequest("Msg.Project.OpenFailed", [path, Strings.Reason_GroupsNotSupported]));
            }
            else if (extension.Equals(".pproj", StringComparison.OrdinalIgnoreCase))
            {
                if (projectSeen)
                {
                    services.Log.Warning(LogCategory, $"{path}: only the first project of the list is opened; ignored");
                    continue;
                }

                projectSeen = true;
                await services.Projects.OpenAsync(path);
            }
            else
            {
                var opened = await services.Documents.OpenInDefaultViewAsync(path); // I-218
                if (opened.NeedsMessage)
                    await services.Dialogs.ShowMessageAsync(new MessageRequest(opened.MessageId!, opened.Args));
            }
        }
    }
}
