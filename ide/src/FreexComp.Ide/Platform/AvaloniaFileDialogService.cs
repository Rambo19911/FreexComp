using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FreexComp.Ide.Core.Services;

namespace FreexComp.Ide.Platform;

/// <summary>
/// <see cref="IFileDialogService"/> over the main window's <see cref="TopLevel.StorageProvider"/> (UI-DLG-050;
/// <c>OpenFilePickerAsync</c>, <c>SaveFilePickerAsync</c>, <c>OpenFolderPickerAsync</c> of Avalonia 12.1). Without a
/// main window every picker answers "canceled". Items without a local path are dropped.
/// </summary>
public sealed class AvaloniaFileDialogService(Func<TopLevel?> topLevel) : IFileDialogService
{
    public async Task<IReadOnlyList<string>> PickOpenFilesAsync(FilePickerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (topLevel()?.StorageProvider is not { } storage)
            return [];

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = request.Title,
            AllowMultiple = request.AllowMultiple,
            FileTypeFilter = FileTypes(request),
            SuggestedStartLocation = await StartLocationAsync(storage, request),
        });
        return [.. files.Select(f => f.TryGetLocalPath()).OfType<string>()];
    }

    public async Task<string?> PickSaveFileAsync(FilePickerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (topLevel()?.StorageProvider is not { } storage)
            return null;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = request.Title,
            FileTypeChoices = FileTypes(request),
            DefaultExtension = request.DefaultExtension,
            SuggestedFileName = request.SuggestedName,
            ShowOverwritePrompt = true, // UI-DLG-052: the OS asks
            SuggestedStartLocation = await StartLocationAsync(storage, request),
        });
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickFolderAsync(FilePickerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (topLevel()?.StorageProvider is not { } storage)
            return null;

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = request.Title,
            AllowMultiple = false,
            SuggestedStartLocation = await StartLocationAsync(storage, request),
        });
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    private static List<FilePickerFileType>? FileTypes(FilePickerRequest request) =>
        request.Filters.Count == 0 ? null : [.. request.Filters.Select(f => new FilePickerFileType(f.Label) { Patterns = f.Patterns })];

    private static async Task<IStorageFolder?> StartLocationAsync(IStorageProvider storage, FilePickerRequest request) =>
        request.InitialFolder is { } folder ? await storage.TryGetFolderFromPathAsync(folder) : null;
}
