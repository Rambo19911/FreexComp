using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Settings;

namespace FreexComp.Ide.Core.Services;

public enum RecentKind
{
    /// <summary><c>projects</c>: <c>.pproj</c> / <c>.pgroup</c>.</summary>
    Project,

    /// <summary><c>files</c>: documents that are not members of the open project.</summary>
    File,
}

/// <summary>One <c>recent.json</c> entry; <paramref name="LastOpened"/> is UTC with seconds precision.</summary>
/// <param name="OpenAtExit">Projects only (IDE-P1-016): the project was open when an IDE last exited.</param>
public sealed record RecentItem(string Path, DateTimeOffset LastOpened, bool Pinned, bool OpenAtExit = false);

/// <summary>
/// The recent projects and files (settings.md Ā§6, phase1-shell.md Ā§13), kept in <c>R\recent.json</c>. UI thread only;
/// the small file is read and written synchronously.
/// </summary>
public interface IRecentItemsService
{
    /// <summary>Pinned entries first, then unpinned; each group by <see cref="RecentItem.LastOpened"/> descending.</summary>
    IReadOnlyList<RecentItem> Projects { get; }

    /// <inheritdoc cref="Projects"/>
    IReadOnlyList<RecentItem> Files { get; }

    /// <summary>Raised once per change of either list.</summary>
    event EventHandler? Changed;

    /// <summary>Records that <paramref name="path"/> was opened now: it moves to the front of its group (IDE-SET-071/072).</summary>
    void Add(RecentKind kind, string path);

    void SetPinned(RecentKind kind, string path, bool pinned);

    void Remove(RecentKind kind, string path);

    /// <summary><c>file.clearRecent</c> (IDE-SET-074): every unpinned entry of both lists.</summary>
    void ClearUnpinned();

    /// <summary>
    /// IDE-P1-016: the project <paramref name="path"/> is open (set when it opens) or was closed by the user (cleared);
    /// an exit leaves the flag set, so the next start-up can reopen it.
    /// </summary>
    void SetOpenAtExit(string path, bool open);

    /// <summary>IDE-P1-016: the flagged project with the newest <see cref="RecentItem.LastOpened"/>, or null.</summary>
    RecentItem? LastProject { get; }
}

/// <inheritdoc />
public sealed class RecentItemsService : IRecentItemsService
{
    public const int SupportedFormatVersion = 1;
    public const int MaxEntries = 20;
    private const string LogCategory = "recent";

    private readonly IFileSystem _fs;
    private readonly IClock _clock;
    private readonly ILog _log;
    private readonly string _path;
    private readonly Dictionary<RecentKind, List<RecentItem>> _lists = new() { [RecentKind.Project] = [], [RecentKind.File] = [] };

    /// <summary>Paths this instance removed, so the merge with the file does not bring them back.</summary>
    private readonly Dictionary<RecentKind, HashSet<string>> _removed = new()
    {
        [RecentKind.Project] = new(StringComparer.OrdinalIgnoreCase),
        [RecentKind.File] = new(StringComparer.OrdinalIgnoreCase),
    };

    /// <param name="safeMode"><c>--safe</c> (IDE-P1-012): the lists are read but the file is never written.</param>
    public RecentItemsService(IUserPaths paths, IFileSystem fileSystem, IClock clock, ILog log, bool safeMode = false)
    {
        _fs = fileSystem;
        _clock = clock;
        _log = log;
        _safeMode = safeMode;
        _path = Path.Combine(paths.RoamingDirectory, "recent.json");
    }

    private readonly bool _safeMode;

    public IReadOnlyList<RecentItem> Projects => _lists[RecentKind.Project];

    public RecentItem? LastProject => Projects.Where(p => p.OpenAtExit).MaxBy(p => p.LastOpened);

    public void SetOpenAtExit(string path, bool open)
    {
        ArgumentNullException.ThrowIfNull(path);
        var list = _lists[RecentKind.Project];
        var index = list.FindIndex(i => SamePath(i.Path, path));
        if (index < 0 || list[index].OpenAtExit == open)
            return;
        list[index] = list[index] with { OpenAtExit = open };
        Commit();
    }

    public IReadOnlyList<RecentItem> Files => _lists[RecentKind.File];

    public bool HasUnpinned => _lists.Values.Any(l => l.Any(i => !i.Pinned));

    public event EventHandler? Changed;

    /// <summary>Start-up read; a missing file means empty lists, an unreadable one too (with a log warning, IDE-SET-075).</summary>
    public void Load()
    {
        if (Read() is not { } read)
            return;
        foreach (var (kind, items) in read)
            _lists[kind].AddRange(items);
        Sort();
    }

    public void Add(RecentKind kind, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var full = Path.GetFullPath(path);
        var list = _lists[kind];
        var existing = list.FirstOrDefault(i => SamePath(i.Path, full));
        if (existing is null && list.Count(i => i.Pinned) >= MaxEntries)
            return; // IDE-SET-072: 20 pinned entries, the new unpinned one is not recorded
        if (existing is not null)
            list.Remove(existing);
        _removed[kind].Remove(full);
        var now = _clock.Now.ToUniversalTime();
        // First, so that it leads the entries opened within the same second.
        list.Insert(0, new RecentItem(full, new DateTimeOffset(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero),
            existing?.Pinned ?? false, existing?.OpenAtExit ?? false));
        Commit();
    }

    public void SetPinned(RecentKind kind, string path, bool pinned)
    {
        ArgumentNullException.ThrowIfNull(path);
        var list = _lists[kind];
        var index = list.FindIndex(i => SamePath(i.Path, path));
        if (index < 0 || list[index].Pinned == pinned)
            return;
        list[index] = list[index] with { Pinned = pinned };
        Commit();
    }

    public void Remove(RecentKind kind, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (_lists[kind].RemoveAll(i => SamePath(i.Path, path)) == 0)
            return;
        _removed[kind].Add(Path.GetFullPath(path));
        Commit();
    }

    public void ClearUnpinned()
    {
        if (!HasUnpinned)
            return;
        foreach (var (kind, list) in _lists)
        {
            foreach (var item in list.Where(i => !i.Pinned))
                _removed[kind].Add(item.Path);
            list.RemoveAll(i => !i.Pinned);
        }

        Commit();
    }

    /// <summary>
    /// <c>file.openRecentProject:&lt;path&gt;</c>, <c>file.openRecentFile:&lt;path&gt;</c> (a missing file asks
    /// <c>Msg.File.RecentNotFound</c>, IDE-P1-132) and <c>file.clearRecent</c>.
    /// </summary>
    public void RegisterCommands(ICommandService commands, IDialogService dialogs, Func<string, Task> openProject, Func<string, Task> openFile)
    {
        ArgumentNullException.ThrowIfNull(commands);
        commands.RegisterHandler("file.openRecentProject", new Handler(arg => !string.IsNullOrEmpty(arg),
            arg => OpenAsync(RecentKind.Project, arg!, dialogs, openProject)));
        commands.RegisterHandler("file.openRecentFile", new Handler(arg => !string.IsNullOrEmpty(arg),
            arg => OpenAsync(RecentKind.File, arg!, dialogs, openFile)));
        commands.RegisterHandler("file.clearRecent", new Handler(_ => HasUnpinned, _ =>
        {
            ClearUnpinned();
            return Task.CompletedTask;
        }));
        Changed += (_, _) => commands.InvalidateCanExecute();
    }

    private async Task OpenAsync(RecentKind kind, string path, IDialogService dialogs, Func<string, Task> open)
    {
        if (!_fs.FileExists(path))
        {
            if (await dialogs.ShowMessageAsync(new MessageRequest("Msg.File.RecentNotFound", [path])) == "Remove")
                Remove(kind, path);
            return;
        }

        await open(path);
    }

    private static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Merge with the file (another instance), order, cap, save at once, notify (IDE-SET-071, -072, -075).</summary>
    private void Commit()
    {
        if (Read() is { } disk)
        {
            foreach (var (kind, items) in disk)
            {
                var list = _lists[kind];
                foreach (var item in items.Where(i => !_removed[kind].Contains(i.Path)))
                {
                    var index = list.FindIndex(i => SamePath(i.Path, item.Path));
                    if (index < 0)
                        list.Add(item);
                    else if (item.LastOpened > list[index].LastOpened)
                        list[index] = item;
                }
            }
        }

        Sort();
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Pinned first, newest first within each group; unpinned entries beyond 20 are dropped oldest first.</summary>
    private void Sort()
    {
        foreach (var list in _lists.Values)
        {
            var sorted = list.OrderByDescending(i => i.Pinned).ThenByDescending(i => i.LastOpened).ToList();
            while (sorted.Count > MaxEntries && sorted.FindLastIndex(i => !i.Pinned) is var last and >= 0)
                sorted.RemoveAt(last);
            list.Clear();
            list.AddRange(sorted);
        }
    }

    /// <summary>The lists in the file; null when it is missing or unreadable (logged).</summary>
    private Dictionary<RecentKind, List<RecentItem>>? Read()
    {
        if (!_fs.FileExists(_path))
            return null;
        try
        {
            var bytes = _fs.ReadAllBytes(_path).AsSpan();
            if (bytes.StartsWith(Encoding.UTF8.Preamble))
                bytes = bytes[Encoding.UTF8.Preamble.Length..];
            if (JsonNode.Parse(bytes) is JsonObject root)
                return new() { [RecentKind.Project] = ReadList(root["projects"]), [RecentKind.File] = ReadList(root["files"]) };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // reported below
        }

        _log.Warning(LogCategory, $"{_path} is unreadable; treated as empty");
        return null;
    }

    private static List<RecentItem> ReadList(JsonNode? node)
    {
        var items = new List<RecentItem>();
        if (node is not JsonArray array)
            return items;
        foreach (var entry in array.OfType<JsonObject>())
        {
            if (LayoutStateJson.Str(entry["path"]) is not { Length: > 0 } path || !Path.IsPathFullyQualified(path)
                || LayoutStateJson.Str(entry["lastOpened"]) is not { } stamp
                || !DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var lastOpened))
                continue;
            if (items.Any(i => SamePath(i.Path, path)))
                continue;
            var pinned = entry["pinned"] is JsonValue value && value.GetValueKind() == JsonValueKind.True;
            var openAtExit = entry["openAtExit"] is JsonValue open && open.GetValueKind() == JsonValueKind.True;
            items.Add(new RecentItem(Path.GetFullPath(path), lastOpened.ToUniversalTime(), pinned, openAtExit));
        }

        return items;
    }

    private void Save()
    {
        if (_safeMode)
            return;
        var root = new JsonObject
        {
            ["formatVersion"] = SupportedFormatVersion,
            ["projects"] = WriteList(_lists[RecentKind.Project]),
            ["files"] = WriteList(_lists[RecentKind.File]),
        };
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, IndentSize = 2, NewLine = "\n", Encoder = SettingValues.Encoder }))
        {
            root.WriteTo(writer);
        }

        stream.WriteByte((byte)'\n');
        try
        {
            _fs.CreateDirectory(Path.GetDirectoryName(_path)!);
            AtomicFileWriter.Write(_fs, _path, stream.ToArray());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log.Error(LogCategory, $"saving {_path} failed: {e.Message}");
        }
    }

    private static JsonArray WriteList(List<RecentItem> items) =>
    [
        .. items.Select(i =>
        {
            var entry = new JsonObject
            {
                ["path"] = i.Path,
                ["lastOpened"] = i.LastOpened.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                ["pinned"] = i.Pinned,
            };
            if (i.OpenAtExit)
                entry["openAtExit"] = true; // IDE-P1-016; written only while set
            return (JsonNode)entry;
        }),
    ];

    private sealed class Handler(Func<string?, bool> canExecute, Func<string?, Task> execute) : ICommandHandler
    {
        public bool CanExecute(string? argument) => canExecute(argument);

        public Task ExecuteAsync(string? argument) => execute(argument);
    }
}
