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
}
