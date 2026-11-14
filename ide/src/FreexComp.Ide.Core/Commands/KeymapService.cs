using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Settings;

namespace FreexComp.Ide.Core.Commands;

/// <summary><c>R\keymap.json</c> (settings.md §5). Saves are debounced 500 ms and atomic, like <c>settings.json</c>.</summary>
public sealed class KeymapService : IKeymapService
{
    private const string Category = "keymap";
    private const string FileName = "keymap.json";
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);
    private static readonly JsonSerializerOptions StringOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>UI-KEY-008 gestures that the keymap can express.</summary>
    private static readonly HashSet<KeyGesture> Reserved =
        [KeyGesture.Parse("Alt+F4"), KeyGesture.Parse("Alt+Space"), KeyGesture.Parse("Ctrl+Alt+Delete"), KeyGesture.Parse("Ctrl+Escape")];

    private readonly IFileSystem _fs;
    private readonly IClock _clock;
    private readonly ILog _log;
    private readonly string _directory;
    private readonly string _path;
    private readonly HashSet<string> _commandIds = CommandCatalog.All.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);

    // Differences from the defaults, per command id, gestures in user order (IDE-SET-060).
    private readonly Dictionary<string, List<KeyGesture>> _added = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<KeyGesture>> _removed = new(StringComparer.Ordinal);

    // Entries of unknown command ids, written back unchanged (IDE-SET-064).
    private readonly List<(string Command, List<string> Keys)> _unknownAdded = [];
    private readonly List<(string Command, List<string> Keys)> _unknownRemoved = [];
    private IDisposable? _saveTimer;
    private bool _dirty;
    private bool _readOnly;

    public KeymapService(IUserPaths paths, IFileSystem fileSystem, IClock clock, ILog log)
    {
        _fs = fileSystem;
        _clock = clock;
        _log = log;
        _directory = paths.RoamingDirectory;
        _path = Path.Combine(_directory, FileName);
        Current = DefaultKeymap.Create();
    }

    public event EventHandler? Changed;

    /// <summary>A UI-KEY-008 gesture that cannot be re-bound (<c>Alt+F4</c>, <c>Alt+Space</c>, <c>Ctrl+Alt+Delete</c>, <c>Ctrl+Escape</c>).</summary>
    public static bool IsReserved(KeyGesture gesture) => Reserved.Contains(gesture);

    public Keymap Current { get; private set; }

    /// <summary>The most recently started save (tests await it after advancing the clock).</summary>
    internal Task LastSaveTask { get; private set; } = Task.CompletedTask;

    /// <summary><c>--safe</c> (IDE-P1-012), instead of <see cref="Load"/>: the default keymap; the file is neither read nor written.</summary>
    public void LoadSafeMode() => _readOnly = true;

    /// <summary>Synchronous load at start-up; a missing file means defaults.</summary>
    public void Load()
    {
        if (!_fs.FileExists(_path))
        {
            return;
        }

        JsonDocument? document = null;
        try
        {
            var bytes = _fs.ReadAllBytes(_path);
            var span = bytes.AsSpan();
            if (span.StartsWith(Encoding.UTF8.Preamble))
            {
                span = span[Encoding.UTF8.Preamble.Length..];
            }

            document = JsonDocument.Parse(span.ToArray());
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                document = null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            document = null;
        }

        if (document is null)
        {
            HandleCorruptFile();
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.TryGetProperty("formatVersion", out var version) && version.TryGetInt32(out var v) && v > 1)
            {
                _readOnly = true;
                _log.Warning(Category, $"keymap.json has newer formatVersion {v}; changes will not be saved");
            }

            foreach (var (command, keys) in Entries(root, "removed"))
            {
                if (!_commandIds.Contains(command))
                {
                    _unknownRemoved.Add((command, keys));
                    continue;
                }

                foreach (var gesture in ParseKeys(command, keys))
                {
                    Add(_removed, command, gesture);
                }
            }

            foreach (var (command, keys) in Entries(root, "bindings"))
            {
                if (!_commandIds.Contains(command))
                {
                    _unknownAdded.Add((command, keys));
                    continue;
                }

                foreach (var gesture in ParseKeys(command, keys))
                {
                    Add(_added, command, gesture);
                }
            }
        }

        Current = Build(logConflicts: true);
    }

    public IReadOnlyList<KeyGesture> GetGestures(string commandId) => Current.GetGestures(commandId);

    public string? FindCommand(KeyGesture gesture, CommandScope focusScope, string? toolWindow) =>
        Current.FindCommand(gesture, focusScope, toolWindow);

    public KeyBinding? FindBinding(KeyGesture gesture, CommandScope focusScope, string? toolWindow) =>
        Current.FindBinding(gesture, focusScope, toolWindow);

    public void SetGestures(string commandId, IReadOnlyList<KeyGesture> gestures)
    {
        if (!_commandIds.Contains(commandId))
        {
            throw new ArgumentException($"Unknown command id '{commandId}'.", nameof(commandId));
        }

        // A reserved default (file.exit's Alt+F4) may stay; it cannot be given to anything else.
        var defaults = Defaults(commandId);
        if (gestures.FirstOrDefault(g => Reserved.Contains(g) && !defaults.Contains(g)) is { Key: not null } reserved)
        {
            throw new ArgumentException($"{reserved} is reserved and cannot be assigned.", nameof(gestures));
        }

        // UI-KEY-021: a gesture already bound at the same scope level moves to this command.
        var places = Places(commandId);
        foreach (var gesture in gestures.Where(g => !Current.GetGestures(commandId).Contains(g)))
        {
            var losers = Current.Bindings
                .Where(b => b.Gesture == gesture && b.CommandId != commandId
                            && places.Any(p => SameLevel(p.Scope, b.Scope) && p.ToolWindow == b.ToolWindow))
                .Select(b => b.CommandId)
                .Distinct()
                .ToList();
            foreach (var loser in losers)
            {
                StoreDifferences(loser, [.. Current.GetGestures(loser).Where(g => g != gesture)]);
            }
        }

        StoreDifferences(commandId, gestures);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ResetAll()
    {
        _added.Clear();
        _removed.Clear();
        Current = DefaultKeymap.Create();
        MarkDirty();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task FlushAsync()
    {
        _saveTimer?.Dispose();
        _saveTimer = null;
        StartSave();
        return LastSaveTask;
    }

    private static void Add(Dictionary<string, List<KeyGesture>> map, string command, KeyGesture gesture)
    {
        if (!map.TryGetValue(command, out var list))
        {
            map[command] = list = [];
        }

        if (!list.Contains(gesture))
        {
            list.Add(gesture);
        }
    }

    private static bool SameLevel(CommandScope a, CommandScope b) => Level(a) == Level(b);

    private static CommandScope Level(CommandScope scope) => scope == CommandScope.Focus ? CommandScope.Global : scope;

    private static IReadOnlyList<KeyGesture> Defaults(string commandId) =>
        [.. DefaultKeymap.Bindings.Where(b => b.CommandId == commandId).Select(b => b.Gesture).Distinct()];

    /// <summary>
    /// Where a user binding of the command applies (IDE-SET-063): the places of its default bindings, or else
    /// every scope of commands.md with no tool window.
    /// </summary>
    private static List<(CommandScope Scope, string? ToolWindow)> Places(string commandId)
    {
        var places = DefaultKeymap.Bindings.Where(b => b.CommandId == commandId).Select(b => (b.Scope, b.ToolWindow)).Distinct().ToList();
        return places.Count > 0 ? places : [.. CommandCatalog.Get(commandId).Scopes.Select(s => (s, (string?)null))];
    }

    private static IEnumerable<(string Command, List<string> Keys)> Entries(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var entry in array.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.Object
                && entry.TryGetProperty("command", out var command) && command.ValueKind == JsonValueKind.String
                && entry.TryGetProperty("keys", out var keys) && keys.ValueKind == JsonValueKind.Array)
            {
                yield return (command.GetString()!, [.. keys.EnumerateArray().Where(k => k.ValueKind == JsonValueKind.String).Select(k => k.GetString()!)]);
            }
        }
    }

    private IEnumerable<KeyGesture> ParseKeys(string command, IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            if (key.Contains(", ", StringComparison.Ordinal))
            {
                _log.Warning(Category, $"'{key}' for {command} is a chord; chords are not supported yet (ignored)");
            }
            else if (!KeyGesture.TryParse(key, out var gesture))
            {
                _log.Warning(Category, $"'{key}' for {command} is not a valid gesture (dropped)");
            }
            else if (Reserved.Contains(gesture))
            {
                _log.Warning(Category, $"'{key}' for {command} is reserved (dropped)");
            }
            else
            {
                yield return gesture;
            }
        }
    }

    private void StoreDifferences(string commandId, IReadOnlyList<KeyGesture> gestures)
    {
        var defaults = Defaults(commandId);
        var added = gestures.Where(g => !defaults.Contains(g)).Distinct().ToList();
        var removed = defaults.Where(g => !gestures.Contains(g)).ToList();
        Store(_added, commandId, added);
        Store(_removed, commandId, removed);
        Current = Build(logConflicts: false);
        MarkDirty();
    }

    private static void Store(Dictionary<string, List<KeyGesture>> map, string commandId, List<KeyGesture> gestures)
    {
        if (gestures.Count == 0)
        {
            map.Remove(commandId);
        }
        else
        {
            map[commandId] = gestures;
        }
    }

    /// <summary>Defaults − removed + bindings; a binding beats a default, a later binding an earlier one (IDE-SET-064).</summary>
    private Keymap Build(bool logConflicts)
    {
        var bindings = DefaultKeymap.Bindings
            .Where(b => !(_removed.TryGetValue(b.CommandId, out var r) && r.Contains(b.Gesture)))
            .ToList();
        var userBindings = new List<KeyBinding>();

        foreach (var (command, gestures) in _added.ToList())
        {
            foreach (var gesture in gestures.ToList())
            {
                foreach (var (scope, tool) in Places(command))
                {
                    var losers = bindings.Where(b => b.Gesture == gesture && b.CommandId != command
                                                     && SameLevel(b.Scope, scope) && b.ToolWindow == tool).ToList();
                    foreach (var loser in losers)
                    {
                        bindings.Remove(loser);
                        if (userBindings.Remove(loser))
                        {
                            _added[loser.CommandId].Remove(loser.Gesture); // an earlier binding lost: not written back
                        }

                        if (logConflicts)
                        {
                            _log.Warning(Category, $"{gesture} is bound to both {loser.CommandId} and {command}; {command} keeps it");
                        }
                    }

                    var binding = new KeyBinding(gesture, scope, tool, command);
                    bindings.Add(binding);
                    userBindings.Add(binding);
                }
            }
        }

        foreach (var empty in _added.Where(p => p.Value.Count == 0).Select(p => p.Key).ToList())
        {
            _added.Remove(empty);
        }

        return new Keymap(bindings);
    }

    private void HandleCorruptFile()
    {
        var corruptName = $"keymap.corrupt-{_clock.Now.DateTime:yyyyMMdd-HHmmss}.json";
        try
        {
            _fs.MoveFile(_path, Path.Combine(_directory, corruptName), overwrite: false);
            _log.Error(Category, $"keymap.json is unreadable; renamed to {corruptName}, using the default keymap");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _readOnly = true;
            _log.Error(Category, $"keymap.json is unreadable and could not be renamed ({ex.Message}); changes will not be saved");
        }
    }

    private void MarkDirty()
    {
        _dirty = true;
        _saveTimer?.Dispose();
        _saveTimer = _clock.Schedule(SaveDelay, () =>
        {
            _saveTimer = null;
            StartSave();
        });
    }

    private void StartSave()
    {
        if (_readOnly || !_dirty)
        {
            return;
        }

        _dirty = false;
        var bytes = Serialize();
        LastSaveTask = LastSaveTask.ContinueWith(_ => SaveAsync(bytes), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
    }

    private async Task SaveAsync(byte[] bytes)
    {
        var temp = _path + ".tmp";
        try
        {
            // I-171: a reader holding keymap.json during the replace is waited for (SharingRetry).
            await SharingRetry.RunAsync(_clock, () =>
            {
                _fs.CreateDirectory(_directory);
                _fs.WriteAllBytes(temp, bytes);
                _fs.MoveFile(temp, _path, overwrite: true);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Error(Category, $"saving {_path} failed: {ex.Message}");
        }
    }

    /// <summary>IDE-SET-061: sorted by command (ordinal), keys in user order, one entry per line.</summary>
    private byte[] Serialize()
    {
        static IEnumerable<(string Command, IEnumerable<string> Keys)> Merge(
            Dictionary<string, List<KeyGesture>> known, List<(string Command, List<string> Keys)> unknown) =>
            known.Select(p => (p.Key, p.Value.Select(g => g.ToString())))
                 .Concat(unknown.Select(u => (u.Command, (IEnumerable<string>)u.Keys)))
                 .OrderBy(e => e.Item1, StringComparer.Ordinal);

        static string Q(string s) => JsonSerializer.Serialize(s, StringOptions);

        static void Section(StringBuilder sb, string name, IEnumerable<(string Command, IEnumerable<string> Keys)> entries, bool last)
        {
            var lines = entries.Select(e => $"    {{ \"command\": {Q(e.Command)}, \"keys\": [{string.Join(", ", e.Keys.Select(Q))}] }}").ToList();
            sb.Append("  \"").Append(name).Append("\": ");
            sb.Append(lines.Count == 0 ? "[]" : "[\n" + string.Join(",\n", lines) + "\n  ]");
            sb.Append(last ? "\n" : ",\n");
        }

        var sb = new StringBuilder("{\n  \"formatVersion\": 1,\n");
        Section(sb, "bindings", Merge(_added, _unknownAdded), last: false);
        Section(sb, "removed", Merge(_removed, _unknownRemoved), last: true);
        sb.Append("}\n");
        return new UTF8Encoding(false).GetBytes(sb.ToString());
    }
}
