using FreexComp.Ide.Core.Services;

namespace FreexComp.Ide.TestSupport;

/// <summary>
/// <see cref="IFileDialogService"/> answered from a queue (IDE-TEST-015): <see cref="NextOpen"/>, <see cref="NextSave"/>,
/// <see cref="NextFolder"/>, <see cref="NextCancel"/>, one answer per picker request, in order. Every request is
/// recorded in <see cref="Requests"/>. A request with no queued answer, or of another kind than the answer, throws.
/// </summary>
public sealed class FakeFileDialogService : IFileDialogService
{
    private readonly Queue<Answer> _answers = new();

    public List<FilePickerRequest> Requests { get; } = [];

    /// <summary>The next open picker returns <paramref name="paths"/>.</summary>
    public void NextOpen(params string[] paths) => _answers.Enqueue(new Answer(Kind.Open, paths));

    /// <summary>The next save picker returns <paramref name="path"/>.</summary>
    public void NextSave(string path) => _answers.Enqueue(new Answer(Kind.Save, [path]));

    /// <summary>The next folder picker returns <paramref name="path"/>.</summary>
    public void NextFolder(string path) => _answers.Enqueue(new Answer(Kind.Folder, [path]));

    /// <summary>The next picker of any kind is canceled.</summary>
    public void NextCancel() => _answers.Enqueue(new Answer(null, []));

    public Task<IReadOnlyList<string>> PickOpenFilesAsync(FilePickerRequest request) =>
        Task.FromResult<IReadOnlyList<string>>(Take(request, Kind.Open));

    public Task<string?> PickSaveFileAsync(FilePickerRequest request) => Task.FromResult(Take(request, Kind.Save).FirstOrDefault());

    public Task<string?> PickFolderAsync(FilePickerRequest request) => Task.FromResult(Take(request, Kind.Folder).FirstOrDefault());

    private string[] Take(FilePickerRequest request, Kind kind)
    {
        ArgumentNullException.ThrowIfNull(request);
        Requests.Add(request);
        if (!_answers.TryDequeue(out var answer))
            throw new InvalidOperationException($"Unscripted {kind} picker {request.PromptId}.");
        if (answer.Kind is { } expected && expected != kind)
            throw new InvalidOperationException($"{kind} picker {request.PromptId} got a scripted {expected} answer.");
        return answer.Paths;
    }

    private enum Kind
    {
        Open,
        Save,
        Folder,
    }

    private sealed record Answer(Kind? Kind, string[] Paths);
}
