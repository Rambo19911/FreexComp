using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Settings;

namespace FreexComp.Ide.Core.Services;

/// <summary>
/// <c>tools.openLogFolder</c> / <c>tools.openSettingsFolder</c> (commands.md, owner <c>IShellService</c>; IDE-SET-093):
/// create the folder when it is missing, then open it in the OS file manager. Always enabled (menus.md).
/// </summary>
public static class ShellCommands
{
    private const string Category = "shell";

    public static void RegisterCommands(ICommandService commands, IShellService shell, IFileSystem fileSystem, IUserPaths paths, ILog log)
    {
        ArgumentNullException.ThrowIfNull(commands);
        commands.RegisterHandler("tools.openLogFolder", new Handler(() => Open(shell, fileSystem, log, paths.LogDirectory)));
        commands.RegisterHandler("tools.openSettingsFolder", new Handler(() => Open(shell, fileSystem, log, paths.RoamingDirectory)));
    }

    private static void Open(IShellService shell, IFileSystem fileSystem, ILog log, string directory)
    {
        try
        {
            fileSystem.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warning(Category, $"Cannot create {directory}: {ex.Message}");
        }

        if (!shell.OpenFolder(directory))
            log.Warning(Category, $"Cannot open {directory} in the file manager");
    }

    private sealed class Handler(Action execute) : ICommandHandler
    {
        public bool CanExecute(string? argument) => true;

        public Task ExecuteAsync(string? argument)
        {
            execute();
            return Task.CompletedTask;
        }
    }
}
