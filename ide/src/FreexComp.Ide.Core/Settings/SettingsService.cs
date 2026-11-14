using System.Text;
using System.Text.Json;
using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Platform;

namespace FreexComp.Ide.Core.Settings;

/// <summary>
/// <c>R\settings.json</c> (settings.md §2–§3). State is touched on the UI thread only; saves run on the thread
/// pool, one after the other, on an immutable snapshot (IDE-SET-029).
/// </summary>
public sealed class SettingsService : ISettingsService
{
    public const int SupportedFormatVersion = 1;
    private const string Category = "settings";
    private const string FileName = "settings.json";
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly IFileSystem _fs;
    private readonly IClock _clock;
    private readonly ILog _log;
    private readonly IReadOnlyList<ISettingKey> _keys;
    private readonly Dictionary<string, ISettingKey> _keysByName;
    private readonly string _directory;
    private readonly string _path;

    private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);
    private readonly HashSet<string> _changedInSession = new(StringComparer.Ordinal);
    private readonly List<(ISettingKey Key, object OldValue)> _batchChanges = [];
    private readonly object _versionGate = new();
    private Dictionary<string, JsonElement> _loadedMembers = new(StringComparer.Ordinal);
    private IDisposable? _saveTimer;
    private int _batchDepth;
    private bool _readOnly;
    private long _version;
    private long _savedVersion;

    public SettingsService(IUserPaths paths, IFileSystem fileSystem, IClock clock, ILog log, IEnumerable<ISettingKey> keys)
    {
        _fs = fileSystem;
        _clock = clock;
        _log = log;
        _keys = [.. keys];
        _keysByName = _keys.ToDictionary(k => k.Name, StringComparer.Ordinal);
        _directory = paths.RoamingDirectory;
        _path = Path.Combine(_directory, FileName);
    }

    public event EventHandler<SettingChangedEventArgs>? Changed;

    public event EventHandler? BatchCompleted;

    public string? CorruptFileName { get; private set; }

    /// <summary>The most recently started save (tests await it after advancing the clock).</summary>
    internal Task LastSaveTask { get; private set; } = Task.CompletedTask;

    /// <summary><c>--safe</c> (IDE-P1-012), instead of <see cref="Load"/>: default values; the file is neither read nor written.</summary>
    public void LoadSafeMode() => _readOnly = true;

    /// <summary>Synchronous initial load (IDE-SET-020); call once, before any other member.</summary>
    public void Load()
    {
        if (!_fs.FileExists(_path))
        {
            return;
        }

        Dictionary<string, JsonElement>? members;
        int? formatVersion;
        try
        {
            (members, formatVersion) = Parse(_fs.ReadAllBytes(_path));
        }
        catch (IOException)
        {
            members = null;
            formatVersion = null;
        }
        catch (UnauthorizedAccessException)
        {
            members = null;
            formatVersion = null;
        }

        if (members is null)
        {
            HandleCorruptFile();
            return;
        }

        if (formatVersion > SupportedFormatVersion)
        {
            _readOnly = true;
            _log.Warning(Category, $"settings.json has newer formatVersion {formatVersion}; changes will not be saved");
        }

        _loadedMembers = members;
        foreach (var (name, element) in members)
        {
            if (!_keysByName.TryGetValue(name, out var key))
            {
                continue;
            }

            if (SettingValues.TryRead(key, element, out var value))
            {
                _values[name] = value;
            }
            else
            {
                _values.Remove(name);
                _log.Warning(Category, $"invalid value for '{name}', using default");
            }
        }
    }

    public T Get<T>(SettingKey<T> key) where T : notnull =>
        _values.TryGetValue(key.Name, out var value) ? (T)value : key.Default;

    public void Set<T>(SettingKey<T> key, T value) where T : notnull
    {
        if (!key.IsValid(value))
        {
            throw new ArgumentException($"Invalid value for setting '{key.Name}'.", nameof(value));
        }

        SetCore(key, SettingValues.Normalize(key, value));
    }

    public bool IsDefault(string keyName) =>
        !_keysByName.TryGetValue(keyName, out var key) || SettingValues.AreEqual(Current(key), key.DefaultValue);

    public void Reset(string keyName)
    {
        if (_keysByName.TryGetValue(keyName, out var key))
        {
            SetCore(key, key.DefaultValue);
        }
    }

    public void ResetAll(string keyPrefix)
    {
        using (BeginBatch())
        {
            foreach (var key in _keys.Where(k => k.Name.StartsWith(keyPrefix, StringComparison.Ordinal)))
            {
                SetCore(key, key.DefaultValue);
            }
        }
    }

    public IDisposable BeginBatch()
    {
        _batchDepth++;
        return new Batch(this);
    }

    public Task FlushAsync()
    {
        _saveTimer?.Dispose();
        _saveTimer = null;
        StartSave();
        return LastSaveTask;
    }

    private static (Dictionary<string, JsonElement>? Members, int? FormatVersion) Parse(byte[] bytes)
    {
        var span = bytes.AsSpan();
        if (span.StartsWith(Encoding.UTF8.Preamble))
        {
            span = span[Encoding.UTF8.Preamble.Length..];
        }

        try
        {
            using var document = JsonDocument.Parse(span.ToArray());
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return (null, null);
            }

            var members = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            int? formatVersion = null;
            foreach (var member in document.RootElement.EnumerateObject())
            {
                if (member.Name == "formatVersion")
                {
                    formatVersion = member.Value.ValueKind == JsonValueKind.Number && member.Value.TryGetInt32(out var v) ? v : null;
                    continue;
                }

                members[member.Name] = member.Value.Clone(); // IDE-SET-023: last duplicate wins
            }

            return (members, formatVersion);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private object Current(ISettingKey key) => _values.TryGetValue(key.Name, out var v) ? v : key.DefaultValue;

    private void HandleCorruptFile()
    {
        var corruptName = $"settings.corrupt-{_clock.Now.DateTime:yyyyMMdd-HHmmss}.json"; // Now carries the local offset
        try
        {
            _fs.MoveFile(_path, Path.Combine(_directory, corruptName), overwrite: false);
            CorruptFileName = corruptName;
            _log.Warning(Category, $"settings.json is unreadable; renamed to {corruptName}, using defaults");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CorruptFileName = FileName;
            _readOnly = true;
            _log.Warning(Category, $"settings.json is unreadable and could not be renamed ({ex.Message}); changes will not be saved");
        }
    }

    private void SetCore(ISettingKey key, object value)
    {
        var old = Current(key);
        if (SettingValues.AreEqual(old, value))
        {
            return;
        }

        _values[key.Name] = value;
        _changedInSession.Add(key.Name);
        lock (_versionGate)
        {
            _version++;
        }

        ScheduleSave();

        if (_batchDepth > 0)
        {
            if (!_batchChanges.Any(c => c.Key.Name == key.Name))
            {
                _batchChanges.Add((key, old));
            }

            return;
        }

        Changed?.Invoke(this, new SettingChangedEventArgs(key.Name, old, value));
    }

    private void EndBatch()
    {
        if (--_batchDepth > 0)
        {
            return;
        }

        var changes = _batchChanges.ToList();
        _batchChanges.Clear();
        foreach (var (key, old) in changes)
        {
            var current = Current(key);
            if (!SettingValues.AreEqual(old, current))
            {
                Changed?.Invoke(this, new SettingChangedEventArgs(key.Name, old, current));
            }
        }

        BatchCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void ScheduleSave()
    {
        _saveTimer?.Dispose();
        _saveTimer = _clock.Schedule(SaveDelay, () =>
        {
            _saveTimer = null;
            StartSave();
        });
    }

    private void StartSave()
    {
        long version;
        lock (_versionGate)
        {
            if (_readOnly || _version == _savedVersion)
            {
                return;
            }

            version = _version;
        }

        var snapshot = new Snapshot(
            version,
            [.. _keys],
            _changedInSession.ToDictionary(n => n, n => Current(_keysByName[n]), StringComparer.Ordinal),
            _loadedMembers);
        LastSaveTask = LastSaveTask.ContinueWith(
            _ => Save(snapshot), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    private void Save(Snapshot snapshot)
    {
        var tempPath = _path + ".tmp";
        try
        {
            // IDE-SET-028: start from what is on disk now, so other instances' changes survive.
            var members = snapshot.LoadedMembers;
            if (_fs.FileExists(_path))
            {
                var (onDisk, formatVersion) = Parse(_fs.ReadAllBytes(_path));
                if (formatVersion > SupportedFormatVersion)
                {
                    _log.Warning(Category, $"settings.json has newer formatVersion {formatVersion}; changes will not be saved");
                    return;
                }

                members = onDisk ?? members;
            }

            var bytes = Serialize(snapshot, members);
            _fs.CreateDirectory(_directory);
            _fs.WriteAllBytes(tempPath, bytes);
            _fs.MoveFile(tempPath, _path, overwrite: true);
            lock (_versionGate)
            {
                _savedVersion = Math.Max(_savedVersion, snapshot.Version);
            }
        }
        catch (Exception ex)
        {
            // IDE-SET-027: logged, retried at the next change, never a dialog.
            _log.Error(Category, $"saving {_path} failed: {ex.Message}");
            try
            {
                _fs.DeleteFile(tempPath);
            }
            catch (Exception)
            {
            }
        }
    }

    private static byte[] Serialize(Snapshot snapshot, IReadOnlyDictionary<string, JsonElement> members)
    {
        var keys = snapshot.Keys.ToDictionary(k => k.Name, StringComparer.Ordinal);
        var output = new SortedDictionary<string, Action<Utf8JsonWriter>>(StringComparer.Ordinal);

        foreach (var (name, element) in members)
        {
            if (!keys.TryGetValue(name, out var key))
            {
                output[name] = w => element.WriteTo(w);               // unknown: preserved (IDE-SET-013)
            }
            else if (!snapshot.Changed.ContainsKey(name) && SettingValues.TryRead(key, element, out var value)
                     && !SettingValues.AreEqual(value, key.DefaultValue))
            {
                output[name] = w => SettingValues.Write(w, key, value); // invalid members are dropped (IDE-SET-022)
            }
        }

        foreach (var (name, value) in snapshot.Changed)
        {
            var key = keys[name];
            if (SettingValues.AreEqual(value, key.DefaultValue))
            {
                output.Remove(name);                                    // IDE-SET-012
            }
            else
            {
                output[name] = w => SettingValues.Write(w, key, value);
            }
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
            IndentSize = 2,
            NewLine = "\n",
            Encoder = SettingValues.Encoder,
        }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("formatVersion", SupportedFormatVersion);
            foreach (var (name, write) in output)
            {
                writer.WritePropertyName(name);
                write(writer);
            }

            writer.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    private sealed record Snapshot(
        long Version,
        IReadOnlyList<ISettingKey> Keys,
        IReadOnlyDictionary<string, object> Changed,
        IReadOnlyDictionary<string, JsonElement> LoadedMembers);

    private sealed class Batch(SettingsService owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                owner.EndBatch();
            }
        }
    }
}
