using System.Globalization;
using FreexComp.Ide.Core.CodeModel;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.LanguageService;
using IFileSystem = FreexComp.Ide.Core.Platform.IFileSystem;

namespace FreexComp.Ide.Core.Refactoring;

/// <summary>
/// The rename of one new name (IDE-REFAC-043): <paramref name="Source"/> and <paramref name="Position"/> are the snapshot
/// and the place the target was found at, <paramref name="CurrentName"/> its name as the service gave it.
/// </summary>
public sealed record RenameRequest(Document Source, int Position, string CurrentName, string NewName, bool Overloads, bool Forms);

/// <summary>
/// The plan of a <see cref="RenameRequest"/> (IDE-REFAC-043): the service's result, and on success its edits as a workspace
/// edit titled <c>Rename 'A' to 'B'</c> (null otherwise) with the number of changes and files of the normalized edit.
/// </summary>
public sealed record RenamePlan(RenameResult Result, WorkspaceEdit? Edit, int EditCount, int FileCount);

/// <summary>What the name entry of a rename answers (IDE-REFAC-042): the new name, the overload choice and its plan.</summary>
public sealed record RenameDialogResult(string NewName, bool Overloads, RenamePlan Plan);

/// <summary>
/// The language-service side of Rename Symbol (IDE-REFAC-041, 043; I-379): the facts of the target and the plan of a new
/// name, each on the code-model worker (request kind <see cref="RequestKind"/>, so a newer request replaces a queued one).
/// UI thread.
/// </summary>
public sealed class RenamePlanner
{
    /// <summary>The worker's request kind of every rename query.</summary>
    public const string RequestKind = "rename";

    private readonly IDocumentService _documents;
    private readonly CodeModelService _codeModel;
    private readonly IFileSystem _fileSystem;

    public RenamePlanner(IDocumentService documents, CodeModelService codeModel, IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(codeModel);
        ArgumentNullException.ThrowIfNull(fileSystem);
        _documents = documents;
        _codeModel = codeModel;
        _fileSystem = fileSystem;
    }

    /// <summary>IDE-REFAC-041: <c>RenameService.GetRenameInfo</c> at <paramref name="position"/> of the snapshot.</summary>
    public Task<RenameInfo> GetInfoAsync(Document source, int position, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        return _codeModel.Worker.RunAsync(RequestKind, token => RenameService.GetRenameInfo(source, position, token), ct);
    }

    /// <summary>
    /// IDE-REFAC-043: (1) here, the current form text of every open form document; (2) on the worker, the rename with
    /// those texts; (3) on success, off this thread, its edits as a workspace edit whose form base texts are those texts.
    /// Computed on the request's snapshot only: a text changed since is the engine's to catch (IDE-REFAC-006).
    /// </summary>
    public async Task<RenamePlan> PlanAsync(RenameRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var formTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (request.Forms)
        {
            foreach (var form in _documents.Documents.OfType<FormDocumentViewModel>())
            {
                if (form.FormText.FilePath is { } path)
                    formTexts[path] = form.Designer.CurrentFormText();
            }
        }

        var options = new RenameOptions { Forms = request.Forms, FormTexts = formTexts, Overloads = request.Overloads };
        var result = await _codeModel.Worker.RunAsync(RequestKind,
            token => RenameService.Rename(request.Source, request.Position, request.NewName, options, token), ct);
        if (!result.Success)
            return new RenamePlan(result, null, 0, 0);

        var title = string.Format(CultureInfo.CurrentCulture, Strings.Refactoring_Rename_Title, request.CurrentName, request.NewName);
        var edit = await WorkspaceEdit.FromLanguageService(title, result.Edits, request.Source.Compilation, _fileSystem, ct, formTexts);
        var normalized = edit.Normalize();
        return new RenamePlan(result, edit, normalized.Files.Sum(f => f.Edits.Length), normalized.Files.Length);
    }
}
