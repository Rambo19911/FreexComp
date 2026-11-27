using System.Collections.Immutable;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;

namespace FreexComp.Ide.Core.History;

/// <summary>
/// The local history store (local-history.md §1, IDE-HIST-001…008; F11-059): every save of a file in the open project's
/// folder becomes a revision under <c>&lt;projectDir&gt;/.freexcomp/history/</c> (gzip blobs named by the SHA-256 of their
/// bytes, one JSON revision list per history key), with an <c>original</c> before the first one and the retention limits
/// applied after each recording. Recording runs after the save on a background task, one at a time
/// (<see cref="Pending"/>); a failure never reaches the save and is reported once per session in the status bar.
/// </summary>
public sealed class LocalHistoryService
{
    public const string FolderName = "history";
    public const int FormatVersion = 1;
    public const int MaxLabelLength = 80;
    private const string LogCategory = "History";
    private const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
    private static readonly TimeSpan StatusMessageDuration = TimeSpan.FromSeconds(5); // UI-MSG-003 "Status"

    private readonly IFileSystem _fileSystem;
    private readonly IProjectService _projects;
    private readonly ISettingsService _settings;
    private readonly IClock _clock;
    private readonly IUiDispatcher _dispatcher;
    private readonly IStatusService _status;
    private readonly ILog _log;
    private readonly Lock _gate = new();
    private readonly Lock _pendingGate = new();
    private int _failureShown;

    public LocalHistoryService(IFileSystem fileSystem, IProjectService projects, ISettingsService settings, IClock clock, IUiDispatcher dispatcher,
        IStatusService status, ILog log)
    {
        _fileSystem = fileSystem;
        _projects = projects;
        _settings = settings;
        _clock = clock;
        _dispatcher = dispatcher;
        _status = status;
        _log = log;
    }

    /// <summary>The recordings started so far (each waits for the one before it); never faults.</summary>
    public Task Pending { get; private set; } = Task.CompletedTask;

    /// <summary>The revisions of a history key changed (recorded, removed by retention, labelled); raised on the UI thread.</summary>
    public event EventHandler<HistoryChangedEventArgs>? Changed;

    /// <summary>The history folder of a project folder.</summary>
    public static string HistoryFolder(string projectDirectory) =>
        Path.Combine(projectDirectory, SessionService.StateFolderName, FolderName);

    /// <summary>UI-TW-1621: whether <paramref name="documentPath"/> has a history now.</summary>
    public HistoryAvailability Availability(string? documentPath) =>
        documentPath is null ? HistoryAvailability.NoDocument
        : Locate(documentPath) is null ? HistoryAvailability.NotInProject
        : !_settings.Get(SettingsCatalog.HistoryEnabled) ? HistoryAvailability.Disabled
        : HistoryAvailability.Available;

    /// <summary>IDE-HIST-004: a save of <paramref name="documentPath"/> is recorded (history on, the file in the project folder).</summary>
    public bool IsTracked(string documentPath) => Availability(documentPath) == HistoryAvailability.Available;

    /// <summary>
    /// True when the key has no revision list yet, so its next save may need the file's bytes before the write (the
    /// <c>original</c>); false on any failure. Called before the write, off the UI thread.
    /// </summary>
    public bool WantsOriginal(string documentPath)
    {
        try
        {
            return Locate(documentPath) is { } location && !_fileSystem.FileExists(location.ListPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>IDE-HIST-008: the revisions of the key <paramref name="documentPath"/>, newest first; empty when there are none or the list is unreadable.</summary>
    public IReadOnlyList<HistoryRevision> GetRevisions(string documentPath)
    {
        ArgumentNullException.ThrowIfNull(documentPath);
        if (Locate(documentPath) is not { } location)
            return [];
        try
        {
            if (!_fileSystem.FileExists(location.ListPath))
                return [];
            return [.. Parse(_fileSystem.ReadAllBytes(location.ListPath), location.FullPath).Revisions.AsEnumerable().Reverse()];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or InvalidDataException)
        {
            return [];
        }
    }

    /// <summary>IDE-HIST-008: the raw bytes of the entry <paramref name="role"/> of <paramref name="revision"/>.</summary>
    /// <exception cref="InvalidOperationException">The revision has no such entry, or its file is not in the open project.</exception>
    /// <exception cref="IOException">The blob is missing or unreadable.</exception>
    /// <exception cref="InvalidDataException">The blob is corrupt.</exception>
    public byte[] ReadEntry(HistoryRevision revision, HistoryRole role)
    {
        ArgumentNullException.ThrowIfNull(revision);
        var entry = revision.Entry(role) ?? throw new InvalidOperationException($"The revision has no {role} entry.");
        var location = Locate(revision.DocumentPath) ?? throw new InvalidOperationException("The file is not in the open project.");
        var bytes = Decompress(_fileSystem.ReadAllBytes(BlobPath(location.HistoryDirectory, entry.Hash)));
        if (Hash(bytes) != entry.Hash)
            throw new InvalidDataException("The stored revision does not match its hash.");
        return bytes;
    }

    /// <summary>
    /// IDE-HIST-004, 005: records the save of the key <paramref name="documentPath"/> on a background task after the ones
    /// before it; the task never faults. Settings, project and time are those of the call.
    /// </summary>
    public Task RecordSaveAsync(string documentPath, IReadOnlyList<HistoryPart> parts) => Enqueue(documentPath, parts, HistoryKind.Save);

    /// <summary>
    /// IDE-HIST-020 (I-333): records the buffer content of the key <paramref name="documentPath"/> before a revert as kind
    /// <c>beforeRevert</c> (every part's <see cref="HistoryPart.Written"/> = the bytes the buffer would write now); nothing
    /// when it equals the newest revision. No <c>original</c> is recorded; retention applies as after a save. Never faults.
    /// </summary>
    public Task RecordBeforeRevertAsync(string documentPath, IReadOnlyList<HistoryPart> parts) => Enqueue(documentPath, parts, HistoryKind.BeforeRevert);

    private Task Enqueue(string documentPath, IReadOnlyList<HistoryPart> parts, HistoryKind kind)
    {
        ArgumentNullException.ThrowIfNull(documentPath);
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Count == 0 || !IsTracked(documentPath) || Locate(documentPath) is not { } location)
            return Task.CompletedTask;
        var limits = new Limits(_settings.Get(SettingsCatalog.HistoryMaxRevisions), _settings.Get(SettingsCatalog.HistoryMaxAgeDays),
            _settings.Get(SettingsCatalog.HistoryMaxSizeMb));
        var now = _clock.Now;
        lock (_pendingGate)
        {
            var previous = Pending;
            Pending = Task.Run(async () =>
            {
                await previous.ConfigureAwait(false);
                Record(location, parts, limits, now, kind);
            });
            return Pending;
        }
    }

    /// <summary>IDE-HIST-008, UI-TW-1624: sets the label (trimmed, at most 80 characters; empty = none) of a revision.</summary>
    public void SetLabel(HistoryRevision revision, string? label)
    {
        ArgumentNullException.ThrowIfNull(revision);
        var text = label?.Trim() ?? "";
        if (text.Length > MaxLabelLength)
            text = text[..MaxLabelLength].TrimEnd();
        if (Locate(revision.DocumentPath) is not { } location)
            return;
        try
        {
            lock (_gate)
            {
                var list = Load(location);
                var index = list.Revisions.FindIndex(r => r.Id == revision.Id);
                if (index < 0)
                    return;
                list.Revisions[index] = list.Revisions[index] with { Label = text.Length == 0 ? null : text };
                Save(location, list);
            }
        }
        catch (Exception ex) when (IsHistoryFailure(ex))
        {
            ReportFailure(ex);
            return;
        }

        RaiseChanged([location.FullPath]);
    }

    private void Record(Location location, IReadOnlyList<HistoryPart> parts, Limits limits, DateTimeOffset now, HistoryKind kind)
    {
        try
        {
            // The parts not rewritten are read from disk; their content before the save is the same.
            var contents = parts.Select(p => (p.Role, Bytes: p.Written ?? _fileSystem.ReadAllBytes(p.Path), Before: p.Written is null ? null : p.Before,
                Unchanged: p.Written is null)).ToList();
            var entries = contents.Select(c => new HistoryEntry(c.Role, Hash(c.Bytes), c.Bytes.Length)).ToImmutableArray();
            HashSet<string> changed;
            lock (_gate)
            {
                var list = Load(location);
                var recorded = false;
                if (kind == HistoryKind.Save && list.Revisions.Count == 0 && contents.All(c => c.Unchanged || c.Before is not null))
                {
                    var before = contents.Select(c => (c.Role, Bytes: c.Unchanged ? c.Bytes : c.Before!)).ToList();
                    var beforeEntries = before.Select(c => new HistoryEntry(c.Role, Hash(c.Bytes), c.Bytes.Length)).ToImmutableArray();
                    if (!SameEntries(beforeEntries, entries))
                    {
                        foreach (var (_, bytes) in before)
                            StoreBlob(location.HistoryDirectory, bytes);
                        list.Revisions.Add(new HistoryRevision(1, now.AddMilliseconds(-1), HistoryKind.Original, null, beforeEntries));
                        recorded = true;
                    }
                }

                if (list.Revisions.Count == 0 || !SameEntries(list.Revisions[^1].Entries, entries))
                {
                    foreach (var content in contents)
                        StoreBlob(location.HistoryDirectory, content.Bytes);
                    var id = list.Revisions.Count == 0 ? 1 : list.Revisions.Max(r => r.Id) + 1;
                    list.Revisions.Add(new HistoryRevision(id, now, kind, null, entries));
                    recorded = true;
                }

                if (!recorded)
                    return;
                Save(location, list);
                changed = Retain(location, limits, now);
                changed.Add(location.FullPath);
            }

            RaiseChanged(changed);
        }
        catch (Exception ex) when (IsHistoryFailure(ex))
        {
            ReportFailure(ex);
        }
    }

    /// <summary>
    /// IDE-HIST-006 over every list of the project: count and age per key (labelled exempt), then the size of the referenced
    /// blobs (oldest first, unlabelled before labelled), never a key's newest revision; then unreferenced blobs are deleted.
    /// Returns the full paths of the keys whose lists changed.
    /// </summary>
    private HashSet<string> Retain(Location location, Limits limits, DateTimeOffset now)
    {
        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filesDirectory = Path.Combine(location.HistoryDirectory, "files");
        var lists = new List<(Location Location, RevisionList List, bool Changed)>();
        foreach (var path in _fileSystem.EnumerateFiles(filesDirectory, "*.json"))
        {
            var key = Location.ForList(location, path);
            if (key is null)
                continue;
            var list = Load(key);
            if (list.Revisions.Count > 0)
                lists.Add((key with { FullPath = Path.GetFullPath(Path.Combine(location.ProjectDirectory, list.RelativePath)) }, list, false));
        }

        var maxAge = TimeSpan.FromDays(limits.MaxAgeDays);
        for (var i = 0; i < lists.Count; i++)
        {
            var revisions = lists[i].List.Revisions;
            var newest = revisions[^1];
            var unlabelled = revisions.Where(r => r.Label is null).ToList();
            var removed = unlabelled.Take(Math.Max(0, unlabelled.Count - limits.MaxRevisions))
                .Concat(unlabelled.Where(r => now - r.TimeUtc > maxAge))
                .Where(r => r != newest)
                .ToHashSet();
            if (removed.Count > 0)
            {
                revisions.RemoveAll(removed.Contains);
                lists[i] = lists[i] with { Changed = true };
            }
        }

        // Size: each referenced blob counted once, at its compressed size.
        var references = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in lists.SelectMany(l => l.List.Revisions).SelectMany(r => r.Entries))
            references[entry.Hash] = references.GetValueOrDefault(entry.Hash) + 1;
        var sizes = references.Keys.ToDictionary(h => h, h => BlobSize(location.HistoryDirectory, h), StringComparer.Ordinal);
        var total = sizes.Values.Sum();
        var limit = limits.MaxSizeMb * 1024L * 1024L;
        while (total > limit)
        {
            var candidates = lists.Select((l, index) => (Index: index, l.List))
                .SelectMany(l => l.List.Revisions.Take(l.List.Revisions.Count - 1).Select(r => (l.Index, Revision: r)))
                .OrderBy(c => c.Revision.Label is not null)
                .ThenBy(c => c.Revision.TimeUtc)
                .ThenBy(c => c.Revision.Id)
                .ToList();
            if (candidates.Count == 0)
                break;
            var (index, revision) = candidates[0];
            lists[index].List.Revisions.Remove(revision);
            lists[index] = lists[index] with { Changed = true };
            foreach (var entry in revision.Entries)
            {
                if (--references[entry.Hash] == 0)
                {
                    references.Remove(entry.Hash);
                    total -= sizes[entry.Hash];
                }
            }
        }

        foreach (var (key, list, listChanged) in lists)
        {
            if (!listChanged)
                continue;
            Save(key, list);
            changed.Add(key.FullPath);
        }

        DeleteUnreferencedBlobs(location.HistoryDirectory, references.Keys.ToHashSet(StringComparer.Ordinal));
        return changed;
    }

    private void DeleteUnreferencedBlobs(string historyDirectory, HashSet<string> referenced)
    {
        var objects = Path.Combine(historyDirectory, "objects");
        if (!_fileSystem.DirectoryExists(objects))
            return;
        foreach (var folder in _fileSystem.EnumerateDirectories(objects))
        {
            var prefix = Path.GetFileName(folder);
            foreach (var blob in _fileSystem.EnumerateFiles(folder, "*.gz"))
            {
                if (!referenced.Contains(prefix + Path.GetFileNameWithoutExtension(blob)))
                    _fileSystem.DeleteFile(blob);
            }

            if (_fileSystem.EnumerateFiles(folder, "*").Count == 0 && _fileSystem.EnumerateDirectories(folder).Count == 0)
                _fileSystem.DeleteDirectory(folder);
        }
    }

    private long BlobSize(string historyDirectory, string hash)
    {
        var path = BlobPath(historyDirectory, hash);
        return _fileSystem.FileExists(path) ? _fileSystem.GetLength(path) : 0;
    }

    private void StoreBlob(string historyDirectory, byte[] bytes)
    {
        var path = BlobPath(historyDirectory, Hash(bytes));
        if (_fileSystem.FileExists(path))
            return;
        _fileSystem.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFileWriter.Write(_fileSystem, path, Compress(bytes));
    }

    /// <summary>The list of a key; a list that cannot be parsed is renamed to <c>&lt;k&gt;.json.corrupt</c> and counts as empty.</summary>
    private RevisionList Load(Location location)
    {
        if (!_fileSystem.FileExists(location.ListPath))
            return new RevisionList(location.RelativePath, []);
        var bytes = _fileSystem.ReadAllBytes(location.ListPath);
        try
        {
            return Parse(bytes, location.FullPath);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidDataException)
        {
            _log.Warning(LogCategory, $"Corrupt history list {location.ListPath} renamed: {ex.Message}");
            _fileSystem.MoveFile(location.ListPath, location.ListPath + ".corrupt", overwrite: true);
            return new RevisionList(location.RelativePath, []);
        }
    }

    private void Save(Location location, RevisionList list)
    {
        _fileSystem.CreateDirectory(Path.GetDirectoryName(location.ListPath)!);
        AtomicFileWriter.Write(_fileSystem, location.ListPath, Serialize(list));
    }

    private static RevisionList Parse(byte[] bytes, string documentPath)
    {
        var root = JsonNode.Parse(bytes) as JsonObject ?? throw new InvalidDataException("Not a JSON object.");
        if (Int(root["formatVersion"]) != FormatVersion)
            throw new InvalidDataException("Unknown format version.");
        var relative = String(root["path"]) ?? throw new InvalidDataException("No path.");
        var revisions = new List<HistoryRevision>();
        foreach (var node in root["revisions"] as JsonArray ?? throw new InvalidDataException("No revisions."))
        {
            var item = node as JsonObject ?? throw new InvalidDataException("A revision is not an object.");
            var kind = String(item["kind"]) switch
            {
                "save" => HistoryKind.Save,
                "original" => HistoryKind.Original,
                "beforeRevert" => HistoryKind.BeforeRevert,
                _ => throw new InvalidDataException("Unknown kind."),
            };
            var time = DateTimeOffset.ParseExact(String(item["timeUtc"]) ?? throw new InvalidDataException("No time."), TimeFormat,
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            var label = item["label"] is null ? null : String(item["label"]) ?? throw new InvalidDataException("Bad label.");
            var entries = ImmutableArray.CreateBuilder<HistoryEntry>();
            foreach (var entryNode in item["entries"] as JsonArray ?? throw new InvalidDataException("No entries."))
            {
                var entry = entryNode as JsonObject ?? throw new InvalidDataException("An entry is not an object.");
                var role = String(entry["role"]) switch
                {
                    "unit" => HistoryRole.Unit,
                    "form" => HistoryRole.Form,
                    "text" => HistoryRole.Text,
                    _ => throw new InvalidDataException("Unknown role."),
                };
                var hash = String(entry["hash"]);
                if (hash is not { Length: 64 } || !hash.All(char.IsAsciiHexDigitLower))
                    throw new InvalidDataException("Bad hash.");
                entries.Add(new HistoryEntry(role, hash, Long(entry["size"])));
            }

            if (entries.Count == 0)
                throw new InvalidDataException("A revision without entries.");
            revisions.Add(new HistoryRevision(Int(item["id"]), time, kind, label, entries.ToImmutable()) { DocumentPath = documentPath });
        }

        return new RevisionList(relative, revisions);
    }

    private static byte[] Serialize(RevisionList list)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("formatVersion", FormatVersion);
            writer.WriteString("path", list.RelativePath);
            writer.WriteStartArray("revisions");
            foreach (var revision in list.Revisions)
            {
                writer.WriteStartObject();
                writer.WriteNumber("id", revision.Id);
                writer.WriteString("timeUtc", revision.TimeUtc.UtcDateTime.ToString(TimeFormat, CultureInfo.InvariantCulture));
                writer.WriteString("kind", revision.Kind switch
                {
                    HistoryKind.Original => "original",
                    HistoryKind.BeforeRevert => "beforeRevert",
                    _ => "save",
                });
                if (revision.Label is null)
                    writer.WriteNull("label");
                else
                    writer.WriteString("label", revision.Label);
                writer.WriteStartArray("entries");
                foreach (var entry in revision.Entries)
                {
                    writer.WriteStartObject();
                    writer.WriteString("role", entry.Role switch
                    {
                        HistoryRole.Unit => "unit",
                        HistoryRole.Form => "form",
                        _ => "text",
                    });
                    writer.WriteString("hash", entry.Hash);
                    writer.WriteNumber("size", entry.Size);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static int Int(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<int>(out var number) ? number : throw new InvalidDataException("Not an integer.");

    private static long Long(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<long>(out var number) && number >= 0 ? number : throw new InvalidDataException("Not a size.");

    private static string? String(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static bool SameEntries(ImmutableArray<HistoryEntry> a, ImmutableArray<HistoryEntry> b) => a.SequenceEqual(b);

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string BlobPath(string historyDirectory, string hash) =>
        Path.Combine(historyDirectory, "objects", hash[..2], hash[2..] + ".gz");

    private static byte[] Compress(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(bytes);
        return output.ToArray();
    }

    private static byte[] Decompress(byte[] bytes)
    {
        using var gzip = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>IDE-HIST-001, 004: the key of a file inside the open project's folder and outside its <c>.freexcomp</c> folder, else null.</summary>
    private Location? Locate(string documentPath)
    {
        if (_projects.Project is not { } project || string.IsNullOrEmpty(documentPath))
            return null;
        string fullPath, projectDirectory;
        try
        {
            fullPath = Path.GetFullPath(documentPath);
            projectDirectory = Path.GetFullPath(project.Directory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        var relative = Path.GetRelativePath(projectDirectory, fullPath);
        if (relative == "." || Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return null;
        relative = relative.Replace(Path.DirectorySeparatorChar, '/');
        var first = relative.Split('/')[0];
        if (string.Equals(first, SessionService.StateFolderName, StringComparison.OrdinalIgnoreCase))
            return null;
        var historyDirectory = HistoryFolder(projectDirectory);
        var key = Hash(Encoding.UTF8.GetBytes(relative.ToUpperInvariant()));
        return new Location(projectDirectory, historyDirectory, fullPath, relative, Path.Combine(historyDirectory, "files", key + ".json"));
    }

    private static bool IsHistoryFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or JsonException or FormatException or InvalidDataException or NotSupportedException;

    /// <summary>IDE-HIST-005: <c>Msg.History.WriteFailed</c> once per session; every failure is logged.</summary>
    private void ReportFailure(Exception ex)
    {
        _log.Warning(LogCategory, "Local history could not be written.", ex);
        if (Interlocked.Exchange(ref _failureShown, 1) == 1)
            return;
        var text = string.Format(CultureInfo.CurrentCulture, Strings.Msg_History_WriteFailed, ex.Message);
        _dispatcher.Post(() => _status.ShowMessage(text, StatusMessageDuration));
    }

    private void RaiseChanged(IEnumerable<string> paths)
    {
        var list = paths.ToList();
        _dispatcher.Post(() =>
        {
            foreach (var path in list)
                Changed?.Invoke(this, new HistoryChangedEventArgs(path));
        });
    }

    private sealed record Limits(int MaxRevisions, int MaxAgeDays, int MaxSizeMb);

    private sealed record RevisionList(string RelativePath, List<HistoryRevision> Revisions);

    /// <summary>A history key: its project, history folder, full and relative path, and list file.</summary>
    private sealed record Location(string ProjectDirectory, string HistoryDirectory, string FullPath, string RelativePath, string ListPath)
    {
        /// <summary>The location of a list file found in the folder (its path is read from the list itself).</summary>
        public static Location? ForList(Location project, string listPath) =>
            listPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? new Location(project.ProjectDirectory, project.HistoryDirectory, "", "", listPath)
                : null;
    }
}
