namespace FreexComp.Ide.Core.Platform;

/// <summary>OS shell operations (IDE-ARCH-012). Each returns false when the shell refused or failed.</summary>
public interface IShellService
{
    /// <summary>Opens <paramref name="directory"/> in the OS file manager (<c>tools.openLogFolder</c>, …).</summary>
    bool OpenFolder(string directory);

    /// <summary>Opens the containing folder with <paramref name="filePath"/> selected (<c>file.showInExplorer</c>).</summary>
    bool RevealFile(string filePath);

    /// <summary>Moves a file to the Recycle Bin (IDE-PM-061).</summary>
    bool MoveToRecycleBin(string path);

    /// <summary>
    /// <see cref="MoveToRecycleBin(string)"/> that also gives the OS reason of a failure (<c>Msg.Project.DeleteFailed</c>);
    /// <paramref name="reason"/> is empty on success.
    /// </summary>
    bool MoveToRecycleBin(string path, out string reason);

    /// <summary>
    /// IDE-PM-141: starts the command interpreter (<c>ComSpec</c>, else <c>%SystemRoot%\System32\cmd.exe</c>) in its own
    /// console window with <paramref name="directory"/> as the working directory, no arguments and the IDE's
    /// environment; the window outlives the IDE.
    /// </summary>
    bool OpenConsole(string directory) => OpenConsole(directory, out _);

    /// <summary><see cref="OpenConsole(string)"/> that also gives the OS reason of a failure (<c>Msg.File.ConsoleFailed</c>).</summary>
    bool OpenConsole(string directory, out string reason);
}
