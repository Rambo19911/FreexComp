using System.Collections.Immutable;

namespace FreexComp.Ide.Core.History;

/// <summary>IDE-HIST-003: how a revision came to be.</summary>
public enum HistoryKind
{
    /// <summary><c>save</c>: the bytes a save wrote.</summary>
    Save,

    /// <summary><c>original</c>: the file's content before its first recorded save.</summary>
    Original,

    /// <summary><c>beforeRevert</c>: the buffer before a revert (I-333).</summary>
    BeforeRevert,
}

/// <summary>IDE-HIST-002: the part of a document an entry holds.</summary>
public enum HistoryRole
{
    /// <summary>The unit (<c>.pas</c>) of a form document.</summary>
    Unit,

    /// <summary>The form text of a form document with a unit.</summary>
    Form,

    /// <summary>The single file of every other document.</summary>
    Text,
}

/// <summary>IDE-HIST-003: one part of a revision: its blob (<see cref="Hash"/>) and raw size in bytes.</summary>
public sealed record HistoryEntry(HistoryRole Role, string Hash, long Size);

/// <summary>IDE-HIST-008: one revision of a history key (<see cref="DocumentPath"/>, the key's full path).</summary>
public sealed record HistoryRevision(int Id, DateTimeOffset TimeUtc, HistoryKind Kind, string? Label, ImmutableArray<HistoryEntry> Entries)
{
    /// <summary>The full path of the history key (IDE-HIST-002) the revision belongs to.</summary>
    public string DocumentPath { get; init; } = "";

    /// <summary>The entry of <paramref name="role"/>, or null.</summary>
    public HistoryEntry? Entry(HistoryRole role) => Entries.FirstOrDefault(e => e.Role == role);
}

/// <summary>
/// What a save hands to <see cref="LocalHistoryService.RecordSaveAsync"/> for one part: <see cref="Written"/> = the bytes
/// written, or null for a part the save did not rewrite (its bytes are read from <see cref="Path"/>, and they are also its
/// content before the save); <see cref="Before"/> = the file's bytes before the write, null when it did not exist or was
/// not read (no original is recorded then).
/// </summary>
public sealed record HistoryPart(HistoryRole Role, string Path, byte[]? Written, byte[]? Before);

/// <summary>Arguments of <see cref="LocalHistoryService.Changed"/>.</summary>
public sealed class HistoryChangedEventArgs(string documentPath) : EventArgs
{
    /// <summary>The full path of the history key whose revisions changed.</summary>
    public string DocumentPath { get; } = documentPath;
}

/// <summary>UI-TW-1621: whether a path has a local history, and if not, why.</summary>
public enum HistoryAvailability
{
    Available,
    NoDocument,
    NotInProject,
    Disabled,
}
