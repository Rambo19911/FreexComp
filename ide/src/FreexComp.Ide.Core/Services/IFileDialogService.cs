namespace FreexComp.Ide.Core.Services;

/// <summary>One file-type filter of a picker: display name and <c>*.ext</c> patterns (dialogs.md §5).</summary>
public sealed record FileFilter(string Name, IReadOnlyList<string> Patterns)
{
    /// <summary>The text shown in the picker: <c>Source files (*.pas;*.inc;*.form)</c>.</summary>
    public string Label => $"{Name} ({string.Join(';', Patterns)})";
}

/// <summary>
/// What a picker is asked to show (dialogs.md §5): the prompt id (key of <c>dialogs.lastFolder</c>), title, filters
/// (first = default; empty for folder pickers), initial folder (null = the OS default), and for save pickers the
/// suggested file name and the default extension without the dot.
/// </summary>
public sealed record FilePickerRequest(
    string PromptId,
    string Title,
    IReadOnlyList<FileFilter> Filters,
    string? InitialFolder,
    bool AllowMultiple = false,
    string? SuggestedName = null,
    string? DefaultExtension = null);

/// <summary>
/// The operating system's file and folder pickers (UI-DLG-050, IDE-ARCH-012): implemented over Avalonia's
/// <c>StorageProvider</c> in <c>FreexComp.Ide/Platform</c>; tests use <c>FakeFileDialogService</c>. Results are
/// absolute local paths. UI thread only. The prompts themselves (filters, initial folders) are <see cref="FilePrompts"/>.
/// </summary>
public interface IFileDialogService
{
    /// <summary>The chosen files; empty when canceled.</summary>
    Task<IReadOnlyList<string>> PickOpenFilesAsync(FilePickerRequest request);

    /// <summary>The chosen path (the OS asks about overwriting, UI-DLG-052); null when canceled.</summary>
    Task<string?> PickSaveFileAsync(FilePickerRequest request);

    /// <summary>The chosen folder; null when canceled.</summary>
    Task<string?> PickFolderAsync(FilePickerRequest request);
}
