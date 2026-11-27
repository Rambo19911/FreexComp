namespace FreexComp.Ide.Core.Settings;

/// <summary>
/// The per-user directories of the IDE (settings.md §1). The only place that computes them (IDE-SET-003).
/// Directories are not created here: they are created on first write (IDE-SET-001).
/// </summary>
public interface IUserPaths
{
    /// <summary>Roaming directory <c>R</c>.</summary>
    string RoamingDirectory { get; }

    /// <summary>Local directory <c>L</c>.</summary>
    string LocalDirectory { get; }

    /// <summary><c>L\logs</c> (IDE-SET-090).</summary>
    string LogDirectory { get; }

    /// <summary>
    /// The user's Documents folder, last fallback of the picker initial folder (UI-DLG-051) and parent of the default
    /// projects folder; <c>&lt;user-dir&gt;\documents</c> when the user directory is overridden or portable (INS-021).
    /// Asked for on first use.
    /// </summary>
    string DocumentsDirectory { get; }
}
