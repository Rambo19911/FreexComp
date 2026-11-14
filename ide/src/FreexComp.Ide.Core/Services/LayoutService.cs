using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.ToolWindows;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Services;

/// <inheritdoc />
/// <remarks>
/// The file is kept as a JSON tree so members owned by other features or by newer versions are written back unchanged
/// (architecture.md §8 common rules); this service rewrites only <c>window</c>, <c>activeProfile</c>, the built-in
/// profiles and <c>toolWindows.&lt;Name&gt;.columns</c>. The file is small, so it is read and written synchronously on the UI
/// thread (IDE-ARCH-081 allows that for the settings-sized files): saves are debounced 2 s (UI-WIN-051 (b)) and run at
/// once on <see cref="Save"/>.
/// </remarks>
public sealed class LayoutService : ILayoutService
{
    public const int SupportedFormatVersion = 1;
    public const double MinColumnWidth = 32;

    /// <summary>UI-DLG-009: entries kept per text-field history.</summary>
    public const int MaxHistory = 20;
    private const string Category = "uistate";
    private const string FileName = "uistate.json";
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MessageDuration = TimeSpan.FromSeconds(5); // UI-MSG-003 "Status"

    private readonly IFileSystem _fs;
    private readonly IClock _clock;
    private readonly ILog _log;
    private readonly IStatusService _status;
    private readonly string _directory;
    private readonly string _path;
    private readonly HashSet<string> _knownTools = [.. ToolWindowService.ShowCommands.Keys];
    private JsonObject _root = [];
    private Func<string, ToolWindowViewModel?>? _resolve;
    private WindowPlacement? _windowPlacement;
    private IDisposable? _saveTimer;
    private bool _readOnly;

    public LayoutService(IUserPaths paths, IFileSystem fileSystem, IClock clock, ILog log, IStatusService status, ICommandService commands)
    {
        _fs = fileSystem;
        _clock = clock;
        _log = log;
        _status = status;
        _directory = paths.RoamingDirectory;
        _path = Path.Combine(_directory, FileName);
        Layout = new LayoutViewModel(LayoutState.CreateDefault(), name => _resolve?.Invoke(name));
        Layout.StateChanged += (_, _) => ScheduleSave();
        commands.RegisterHandler("window.nextPane", new ActionHandler(() => Layout.CyclePanes(1))); // UI-A11Y-003
        commands.RegisterHandler("window.previousPane", new ActionHandler(() => Layout.CyclePanes(-1)));
        commands.RegisterHandler("window.focusEditor", new ActionHandler(() => Layout.FocusPane(Pane.Documents))); // UI-A11Y-004
        commands.RegisterHandler("window.resetLayout", new ActionHandler(Layout.ResetProfile)); // UI-WIN-015, no confirmation
        // menus.md "not session (exclusive)": Edit is the only profile before I-130, so choosing it changes nothing.
        commands.RegisterHandler("window.layout.edit", new ActionHandler(() => { }, () => Layout.ActiveProfile == LayoutProfiles.Edit));
    }

    public LayoutViewModel Layout { get; }

    public WindowPlacement? WindowPlacement
    {
        get => _windowPlacement;
        set
        {
            if (_windowPlacement != value)
            {
                _windowPlacement = value;
                ScheduleSave();
            }
        }
    }

    /// <summary><c>--safe</c> (IDE-P1-012), instead of <see cref="Load"/>: the default layout; the file is neither read nor written.</summary>
    public void LoadSafeMode() => _readOnly = true;

    /// <summary>Synchronous load at start-up (IDE-ARCH-015 step 7); a missing file means first start.</summary>
    public void Load()
    {
        if (!_fs.FileExists(_path))
        {
            return;
        }

        JsonObject? root;
        try
        {
            var bytes = _fs.ReadAllBytes(_path).AsSpan();
            if (bytes.StartsWith(Encoding.UTF8.Preamble))
            {
                bytes = bytes[Encoding.UTF8.Preamble.Length..];
            }

            root = JsonNode.Parse(bytes) as JsonObject;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            root = null;
        }

        if (root is null)
        {
            HandleCorruptFile();
            return;
        }

        if (LayoutStateJson.Num(root["formatVersion"]) is > SupportedFormatVersion and var version)
        {
            _readOnly = true;
            _log.Warning(Category, $"uistate.json has newer formatVersion {version}; changes will not be saved");
        }

        _root = root;
        _windowPlacement = LayoutStateJson.ReadWindow(root["window"]);
        var state = Layout.State;
        var profiles = root["profiles"] as JsonObject;
        foreach (var name in LayoutProfiles.BuiltIn)
        {
            state.Profiles[name] = LayoutStateJson.ReadProfile(name, profiles?[name], _knownTools) ?? LayoutProfiles.Create(name);
        }

        state.ActiveProfile = root["activeProfile"] is JsonValue active && active.GetValueKind() == JsonValueKind.String
                              && LayoutProfiles.BuiltIn.Contains(active.GetValue<string>())
            ? active.GetValue<string>()
            : LayoutProfiles.Edit;
        Layout.Reload(notify: false);
    }

    public double? GetColumnWidth(string toolWindow, string columnId) =>
        _root["toolWindows"] is JsonObject tools && tools[toolWindow] is JsonObject tool && tool["columns"] is JsonObject columns
            ? LayoutStateJson.Num(columns[columnId])
            : null;

    public void SetColumnWidth(string toolWindow, string columnId, double width)
    {
        width = Math.Max(MinColumnWidth, width);
        if (GetColumnWidth(toolWindow, columnId) == width)
        {
            return;
        }

        var tools = Child(_root, "toolWindows");
        var columns = Child(Child(tools, toolWindow), "columns");
        columns[columnId] = width;
        ScheduleSave();
    }

    public string? GetLastFolder(string promptId) =>
        _root["dialogs"] is JsonObject dialogs && dialogs["lastFolder"] is JsonObject folders
            && folders[promptId] is JsonValue value && value.TryGetValue(out string? folder)
            ? folder
            : null;

    public void SetLastFolder(string promptId, string folder)
    {
        if (GetLastFolder(promptId) == folder)
        {
            return;
        }

        Child(Child(_root, "dialogs"), "lastFolder")[promptId] = folder;
        ScheduleSave();
    }

    public string? GetDialogValue(string dialogName, string key) =>
        _root["dialogs"] is JsonObject dialogs && dialogs[dialogName] is JsonObject dialog
            && dialog[key] is JsonValue value && value.TryGetValue(out string? text)
            ? text
            : null;

    public void SetDialogValue(string dialogName, string key, string value)
    {
        if (GetDialogValue(dialogName, key) == value)
        {
            return;
        }

        Child(Child(_root, "dialogs"), dialogName)[key] = value;
        ScheduleSave();
    }

    public IReadOnlyList<string> GetDialogHistory(string dialogName, string field) =>
        _root["dialogs"] is JsonObject dialogs && dialogs[dialogName] is JsonObject dialog
            && dialog["history"] is JsonObject history && history[field] is JsonArray entries
            ? [.. entries.OfType<JsonValue>().Select(v => v.TryGetValue(out string? s) ? s : null).OfType<string>().Take(MaxHistory)]
            : [];

    public void AddDialogHistory(string dialogName, string field, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var old = GetDialogHistory(dialogName, field);
        if (old.Count > 0 && old[0] == text)
        {
            return;
        }

        var entries = new JsonArray();
        foreach (var entry in new[] { text }.Concat(old.Where(e => e != text)).Take(MaxHistory))
        {
            entries.Add(entry);
        }

        Child(Child(Child(_root, "dialogs"), dialogName), "history")[field] = entries;
        ScheduleSave();
    }

    public bool? GetUiFlag(string section, string key) =>
        _root[section] is JsonObject values && values[key] is JsonValue value && value.TryGetValue(out bool flag) ? flag : null;

    public void SetUiFlag(string section, string key, bool value)
    {
        if (GetUiFlag(section, key) == value)
        {
            return;
        }

        Child(_root, section)[key] = value;
        ScheduleSave();
    }

    public void AttachToolWindows(Func<string, ToolWindowViewModel?> resolve)
    {
        _resolve = resolve;
        Layout.Refresh();
    }

    public void Save()
    {
        _saveTimer?.Dispose();
        _saveTimer = null;
        if (_readOnly)
        {
            return;
        }

        var temp = _path + ".tmp";
        try
        {
            var bytes = Serialize();
            _fs.CreateDirectory(_directory);
            _fs.WriteAllBytes(temp, bytes);
            _fs.MoveFile(temp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            _log.Error(Category, $"saving {_path} failed: {ex.Message}");
        }
    }

    /// <summary>Returns <paramref name="parent"/>[<paramref name="name"/>] as an object, replacing a member of another shape.</summary>
    private static JsonObject Child(JsonObject parent, string name)
    {
        if (parent[name] is JsonObject child)
        {
            return child;
        }

        child = [];
        parent[name] = child;
        return child;
    }

    private byte[] Serialize()
    {
        // formatVersion first; the other known sections keep their place, or follow each other in a new file.
        _root.Remove("formatVersion");
        _root.Insert(0, "formatVersion", SupportedFormatVersion);
        if (_windowPlacement is { } placement)
        {
            Put("window", LayoutStateJson.WriteWindow(placement), after: "formatVersion");
        }
        else
        {
            _root.Remove("window");
        }

        Put("activeProfile", Layout.State.ActiveProfile, after: _root.ContainsKey("window") ? "window" : "formatVersion");
        Put("profiles", _root["profiles"] as JsonObject ?? [], after: "activeProfile");
        var profiles = (JsonObject)_root["profiles"]!;
        foreach (var name in LayoutProfiles.BuiltIn)
        {
            profiles[name] = LayoutStateJson.WriteProfile(Layout.State.Profiles[name]);
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, IndentSize = 2, NewLine = "\n", Encoder = SettingValues.Encoder }))
        {
            _root.WriteTo(writer);
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    /// <summary>Replaces the member in place, or inserts it right after <paramref name="after"/>.</summary>
    private void Put(string name, JsonNode? value, string after)
    {
        if (_root.ContainsKey(name))
        {
            if (!ReferenceEquals(_root[name], value))
            {
                _root[name] = value;
            }

            return;
        }

        _root.Insert(_root.IndexOf(after) + 1, name, value);
    }

    private void HandleCorruptFile()
    {
        var corruptName = $"uistate.corrupt-{_clock.Now.DateTime:yyyyMMdd-HHmmss}.json";
        try
        {
            _fs.MoveFile(_path, Path.Combine(_directory, corruptName), overwrite: false);
            _log.Warning(Category, $"uistate.json is unreadable; renamed to {corruptName}, using the default layout");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _readOnly = true;
            _log.Warning(Category, $"uistate.json is unreadable and could not be renamed ({ex.Message}); changes will not be saved");
        }

        _status.ShowMessage(Strings.Msg_App_LayoutCorrupt, MessageDuration); // UI-WIN-054
    }

    private void ScheduleSave()
    {
        _saveTimer?.Dispose();
        _saveTimer = _clock.Schedule(SaveDelay, Save);
    }

    private sealed class ActionHandler(Action action, Func<bool>? isChecked = null) : ICommandHandler
    {
        public bool CanExecute(string? argument) => true;

        public bool IsChecked(string? argument) => isChecked?.Invoke() ?? false;

        public Task ExecuteAsync(string? argument)
        {
            action();
            return Task.CompletedTask;
        }
    }
}
