using System.Collections.Immutable;
using FreexComp.Compiler;
using FreexComp.Ide.Core.Text;
using FreexComp.LanguageService;
using IFileSystem = FreexComp.Ide.Core.Platform.IFileSystem;

namespace FreexComp.Ide.Core.Refactoring;

/// <summary>
/// The text edits of one file (IDE-REFAC-001): <see cref="Edits"/> have offsets into <see cref="BaseText"/>, the text
/// they were computed against (UTF-16 units). <see cref="Path"/> is a full path, compared ordinal-ignore-case.
/// </summary>
public sealed record FileEdit(string Path, string BaseText, ImmutableArray<TextEdit> Edits);

/// <summary>
/// A titled set of file edits (IDE-REFAC-001). <see cref="Title"/> is the operation's name as shown to the user,
/// without a trailing ellipsis. The inserted texts are used verbatim: the producer chooses the line break.
/// </summary>
public sealed record WorkspaceEdit(string Title, ImmutableArray<FileEdit> Files)
{
    /// <summary>
    /// IDE-REFAC-003: the edits of the language service as a workspace edit — one file edit per document edit, in the
    /// service's order, every change with its span and text. The base text is the text the service computed the changes
    /// against: for a source of <paramref name="compilation"/> its syntax tree's text (an open document's buffer text as
    /// the workspace was synchronised with it, else the file); for a form file (CMP-753), which the service reads from
    /// disk, the file decoded with <see cref="EncodingDetector"/>, without a byte-order mark. Everything runs off the
    /// calling thread. A file that cannot be read (a form deleted or locked since the service read it) faults the task
    /// with a <see cref="WorkspaceEditReadException"/> — an <see cref="IOException"/> that names the file and carries the
    /// system's reason. A file rename of the service is not part of a workspace edit.
    /// </summary>
    public static Task<WorkspaceEdit> FromLanguageService(string title, IEnumerable<DocumentEdit> edits, Compilation compilation,
        IFileSystem fileSystem, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(edits);
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(fileSystem);
        var list = edits.ToList();
        return Task.Run(() =>
        {
            var files = ImmutableArray.CreateBuilder<FileEdit>(list.Count);
            foreach (var edit in list)
            {
                ct.ThrowIfCancellationRequested();
                string baseText;
                try
                {
                    baseText = IsFormFile(edit.Path)
                        ? EncodingDetector.Detect(fileSystem.ReadAllBytes(edit.Path)).Text
                        : compilation.GetSyntaxTree(edit.Path).Text.Text;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    throw new WorkspaceEditReadException(edit.Path, e);
                }

                files.Add(new FileEdit(edit.Path, baseText, [.. edit.Changes.Select(c => new TextEdit(c.Span.Start, c.Span.Length, c.NewText))]));
            }

            return new WorkspaceEdit(title, files.ToImmutable());
        }, ct);
    }

    /// <summary>
    /// The one kind of file the language service edits without a syntax tree: a form (<c>.form</c>, the extension of the
    /// form language). Every other path is a source the compilation parsed.
    /// </summary>
    private static bool IsFormFile(string path) => string.Equals(Path.GetExtension(path), ".form", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// IDE-REFAC-001: the edit without the edits that replace a text by the same text and without the files that have
    /// no edit left. Edits outside the base text are kept: they make the edit invalid (<see cref="FindDefect"/>).
    /// </summary>
    internal WorkspaceEdit Normalize()
    {
        var files = ImmutableArray.CreateBuilder<FileEdit>();
        foreach (var file in Files)
        {
            var edits = file.Edits.Where(e => !IsNoOp(file.BaseText, e)).ToImmutableArray();
            if (edits.Length > 0)
                files.Add(file with { Edits = edits });
        }

        return this with { Files = files.ToImmutable() };
    }

    /// <summary>
    /// IDE-REFAC-002: the first defect of a normalised edit — two file edits for one path, an edit outside the base
    /// text, two edits of one file whose ranges intersect or that start at the same offset — as the offending path and
    /// what is wrong there; null when the edit is valid.
    /// </summary>
    internal (string Path, string Problem)? FindDefect()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Files)
        {
            if (!paths.Add(file.Path))
                return (file.Path, "the path is listed twice");
            if (file.Edits.Where(e => !IsInside(file.BaseText, e)).Select(e => (TextEdit?)e).FirstOrDefault() is { } outside)
                return (file.Path, $"edit {outside.Offset}+{outside.Length} is outside the base text (length {file.BaseText.Length})");
            var ordered = file.Edits.OrderBy(e => e.Offset).ThenBy(e => e.Length).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                var (previous, next) = (ordered[i - 1], ordered[i]);
                if (next.Offset == previous.Offset || next.Offset < previous.Offset + previous.Length)
                    return (file.Path, $"edits {previous.Offset}+{previous.Length} and {next.Offset}+{next.Length} overlap");
            }
        }

        return null;
    }

    private static bool IsInside(string text, TextEdit edit) =>
        edit.NewText is not null && edit.Offset >= 0 && edit.Length >= 0 && edit.Offset <= text.Length - edit.Length;

    private static bool IsNoOp(string text, TextEdit edit) =>
        IsInside(text, edit) && edit.Length == edit.NewText.Length
        && string.CompareOrdinal(text, edit.Offset, edit.NewText, 0, edit.Length) == 0;
}

/// <summary>When the Preview dialog is shown before a workspace edit is applied (IDE-REFAC-007).</summary>
public enum PreviewMode
{
    /// <summary>Shown unless the edit changes exactly one file and that file is the buffer of the active document's visible text view.</summary>
    Auto,

    /// <summary>Always shown.</summary>
    Always,

    /// <summary>Never shown: the caller showed the changes itself.</summary>
    Never,
}

/// <summary>How an operation of the refactoring engine ended (IDE-REFAC-012).</summary>
public enum RefactoringOutcome
{
    /// <summary>Every file of the edit has its new text.</summary>
    Applied,

    /// <summary>The edit had nothing to change (IDE-REFAC-001).</summary>
    NoChanges,

    /// <summary>The user cancelled; nothing changed.</summary>
    Canceled,

    /// <summary>One or more files may not be edited now (IDE-REFAC-006); nothing changed.</summary>
    Blocked,

    /// <summary>The edit was invalid (IDE-REFAC-002) or a write failed (IDE-REFAC-008); nothing changed.</summary>
    Failed,
}

/// <summary>Why a file blocks an operation (IDE-REFAC-006; <see cref="ChangedAfterwards"/> blocks an undo, IDE-REFAC-021).</summary>
public enum BlockReason
{
    NotFound,
    LibrarySource,
    OutsideProject,
    NotWritable,
    ChangedOnDisk,
    ChangedSinceComputed,
    ChangedAfterwards,
}

/// <summary>A file that blocks an operation, with the first reason that applies to it.</summary>
public sealed record BlockedFile(string Path, BlockReason Reason);

/// <summary>
/// The result of an operation (IDE-REFAC-012): the number of edits and files applied (0 unless <see cref="Outcome"/> is
/// <see cref="RefactoringOutcome.Applied"/>), the blocking files in path order, and the system's reason of a failed write.
/// </summary>
public sealed record RefactoringResult(RefactoringOutcome Outcome, int EditCount, int FileCount,
    IReadOnlyList<BlockedFile> Blocked, string? Error);
