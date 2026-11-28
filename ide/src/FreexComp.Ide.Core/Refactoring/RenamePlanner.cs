using System.Globalization;
using FreexComp.Compiler.Text;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.CodeModel;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Text;
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
public sealed record RenamePlan(RenameResult Result, WorkspaceEdit? Edit, int EditCount, int FileCount)
{
    /// <summary>
    /// IDE-REFAC-059 (I-384): the form file whose designer renames this component (the dialog's note), or null — set by the
    /// dialog's check; the refactoring judges the chosen plan again.
    /// </summary>
    public string? DesignerForm { get; init; }
}

/// <summary>What the name entry of a rename answers (IDE-REFAC-042): the new name, the overload choice and its plan.</summary>
public sealed record RenameDialogResult(string NewName, bool Overloads, RenamePlan Plan);

/// <summary>
/// The language-service side of Rename Symbol (IDE-REFAC-041, 043; I-379): the facts of the target and the plan of a new
/// name, each on the code-model worker (request kind <see cref="RequestKind"/>, so a newer request replaces a queued one).
/// UI thread. After a rename it lists the occurrences the plan did not rename in Messages (IDE-REFAC-061, I-385).
/// </summary>
public sealed class RenamePlanner
{
    /// <summary>The worker's request kind of every rename query.</summary>
    public const string RequestKind = "rename";

    private readonly IDocumentService _documents;
    private readonly CodeModelService _codeModel;
    /// <summary>How long the status text of IDE-REFAC-061 stays (UI-MSG-003 "Status").</summary>
    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(5);

    private readonly IFileSystem _fileSystem;
    private readonly IMessagesService _messages;
    private readonly IStatusService _status;

    public RenamePlanner(IDocumentService documents, CodeModelService codeModel, IFileSystem fileSystem, IMessagesService messages, IStatusService status)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(codeModel);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(status);
        _documents = documents;
        _codeModel = codeModel;
        _fileSystem = fileSystem;
        _messages = messages;
        _status = status;
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

    /// <summary>
    /// IDE-REFAC-061: after a rename was applied — <paramref name="applied"/> are its file edits, offsets into their base
    /// texts (the texts the service computed on) — one warning row per unrenamed occurrence of <paramref name="result"/> in
    /// Messages, in the service's order, at its place in the renamed text; one more row without a location when the list is
    /// capped; then the status text <c>Msg.Rename.UnrenamedStatus</c> with <paramref name="title"/>. Messages is neither opened
    /// nor focused. Nothing when the plan has no unrenamed occurrence. UI thread.
    /// </summary>
    public void ReportUnrenamed(RenameResult result, string oldName, string newName, string title, IEnumerable<FileEdit> applied)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(applied);
        if (result.Unrenamed.IsEmpty)
            return;
        var edits = applied.GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.SelectMany(f => f.Edits).ToList(), StringComparer.OrdinalIgnoreCase);
        foreach (var occurrence in result.Unrenamed)
        {
            var text = string.Format(CultureInfo.CurrentCulture,
                occurrence.Kind == UnrenamedKind.InactiveCode ? Strings.Msg_Rename_UnrenamedInactive : Strings.Msg_Rename_UnrenamedUnresolved,
                oldName, newName);
            _messages.AddToolMessage(MessageItem.ToolRow(MessageSeverity.Warning, text,
                Renamed(occurrence.Location, edits.GetValueOrDefault(occurrence.Location.Path))));
        }

        if (result.UnrenamedCapped)
        {
            _messages.AddToolMessage(MessageItem.ToolRow(MessageSeverity.Warning,
                string.Format(CultureInfo.CurrentCulture, Strings.Msg_Rename_UnrenamedMore, RenameService.MaxUnrenamed), null));
        }

        _status.ShowMessage(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Rename_UnrenamedStatus, title, result.Unrenamed.Length), StatusDuration);
    }

    /// <summary>
    /// The 1-based place of an unrenamed name in the text after <paramref name="edits"/> (null: its file was not edited).
    /// A rename replaces names by names, never a line break: the line stays, the column moves by the edits that lie before
    /// it on its line.
    /// </summary>
    private static MessageLocation? Renamed(Location location, List<TextEdit>? edits)
    {
        if (!location.HasPosition)
            return MessagesService.Map(location);
        var start = location.Span.Start;
        var lineStart = start - location.LineSpan.Start.Column;
        var shift = edits?.Where(e => e.Offset >= lineStart && e.Offset + e.Length <= start).Sum(e => e.NewText.Length - e.Length) ?? 0;
        var line = location.LineSpan.Start.Line + 1;
        var column = location.LineSpan.Start.Column + 1 + shift;
        return new MessageLocation(location.Path, line, column, line, column + location.Span.Length);
    }
}
