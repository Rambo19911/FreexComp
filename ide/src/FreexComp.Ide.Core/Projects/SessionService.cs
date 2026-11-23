using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using IClock = FreexComp.Ide.Core.Platform.IClock;
using IdeFileSystem = FreexComp.Ide.Core.Platform.IFileSystem;
using FreexComp.Ide.Core.Layout;

namespace FreexComp.Ide.Core.Projects;

/// <summary>
/// The session (architecture.md §8.2): <c>&lt;project&gt;\.freexcomp\session.json</c> while a project is open, else
/// <c>L\session-noproject.json</c> (IDE-ARCH-075). UI thread only; the files are small and read/written synchronously.
/// </summary>
public interface ISessionService
{
    /// <summary>The file of the current state: the open project's session, else the loose-file session.</summary>
    string SessionPath { get; }

    /// <summary>
    /// Reads <see cref="SessionPath"/> (once per project open, IDE-ARCH-071): Project Manager expansion and selection, and
    /// with <paramref name="restoreDocuments"/> the documents with caret, selection, scroll and folds, then the active
    /// one (IDE-ARCH-073 tolerances). Never shows a dialog (IDE-ARCH-072). Returns the number of documents opened.
    /// </summary>
    Task<int> RestoreAsync(bool restoreDocuments = true);

    /// <summary>Writes <see cref="SessionPath"/> now (project close, exit); a failure is logged once per project open.</summary>
    void Save();
}

/// <inheritdoc />
/// <remarks>
/// Members owned by later phases (bookmarks, breakpoints, watches, Object Inspector, run) and unknown members are kept
/// as read and written back unchanged (IDE-ARCH-076). While a project is open, a change of a stored value schedules a
/// write 5 s after the last change (IDE-ARCH-071 (c)).
/// </remarks>
public sealed class SessionService : ISessionService
{
    public const int SupportedFormatVersion = 1;
    public const string StateFolderName = ".freexcomp";
    private const string LogCategory = "session";
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(5);

    private readonly IdeFileSystem _fs;
    private readonly IDocumentService _documents;
    private readonly IProjectService _projects;
    private readonly ProjectManagerViewModel _projectManager;
    private readonly ILayoutService _layout;
    private readonly IUserPaths _paths;
    private readonly IClock _clock;
    private readonly ILog _log;
    private readonly bool _safeMode;
    private JsonObject? _root;
    private string? _rootPath;
    private bool _readOnly;
    private bool _writeFailureLogged;
    private IDisposable? _saveTimer;

    /// <param name="safeMode"><c>--safe</c> (IDE-P1-012): nothing is restored and no session file is written.</param>
    public SessionService(IdeFileSystem fileSystem, IDocumentService documents, IProjectService projects, ProjectManagerViewModel projectManager,
        ILayoutService layout, IUserPaths paths, IClock clock, ILog log, bool safeMode = false)
    {
        _safeMode = safeMode;
        _fs = fileSystem;
        _documents = documents;
        _projects = projects;
        _projectManager = projectManager;
        _layout = layout;
        _paths = paths;
        _clock = clock;
        _log = log;
        documents.DocumentOpened += (_, e) =>
        {
            if (e.Document is TextDocumentViewModel text)
                text.ViewState.PropertyChanged += OnStoredValueChanged;
            if (e.Document is FormDocumentViewModel form)
                form.ActiveViewChanged += OnBookmarksChanged; // the stored view (IDE-ARCH-070)
            ScheduleSave();
        };
        documents.DocumentClosed += (_, e) =>
        {
            if (e.Document is TextDocumentViewModel text)
                text.ViewState.PropertyChanged -= OnStoredValueChanged;
            if (e.Document is FormDocumentViewModel form)
                form.ActiveViewChanged -= OnBookmarksChanged;
            ScheduleSave();
        };
        documents.ActiveDocumentChanged += (_, _) => ScheduleSave();
        projectManager.SelectionChanged += (_, _) => ScheduleSave();
    }

    /// <summary>IDE-ED-114 (P3): the bookmarks stored in and restored from the session's <c>bookmarks</c> member.</summary>
    public Editing.BookmarkService? Bookmarks
    {
        get => _bookmarks;
        set
        {
            if (_bookmarks is not null)
                _bookmarks.Changed -= OnBookmarksChanged;
            _bookmarks = value;
            if (value is not null)
                value.Changed += OnBookmarksChanged;
        }
    }

    private Editing.BookmarkService? _bookmarks;

    private void OnBookmarksChanged(object? sender, EventArgs e) => ScheduleSave();

    /// <summary>
    /// P6 (IDE-ARCH-076, IDE-ARCH-077): the breakpoints stored in and restored from the project session's
    /// <c>breakpoints</c> member; null before Phase 6, when the member is kept as read.
    /// </summary>
    public Debugging.IBreakpointService? Breakpoints
    {
        get => _breakpoints;
        set
        {
            if (_breakpoints is not null)
                _breakpoints.Changed -= OnBookmarksChanged;
            _breakpoints = value;
            if (value is not null)
                value.Changed += OnBookmarksChanged;
        }
    }

    private Debugging.IBreakpointService? _breakpoints;

    /// <summary>
    /// D-109: the Object Inspector whose page, collapsed categories, expanded rows and name column ratio are stored in and
    /// restored from the session's <c>objectInspector</c> member (DSG-OI-003, DSG-OI-013, UI-DSG-103).
    /// </summary>
    public Designer.ObjectInspectorViewModel? ObjectInspector
    {
        get => _inspector;
        set
        {
            if (_inspector is not null)
                _inspector.SessionStateChanged -= OnBookmarksChanged;
            _inspector = value;
            if (value is not null)
                value.SessionStateChanged += OnBookmarksChanged;
        }
    }

    private Designer.ObjectInspectorViewModel? _inspector;

    public string SessionPath => _projects.Project is { } project
        ? Path.Combine(project.Directory, StateFolderName, "session.json")
        : Path.Combine(_paths.LocalDirectory, "session-noproject.json");

    public async Task<int> RestoreAsync(bool restoreDocuments = true)
    {
        if (_safeMode)
            return 0;
        _writeFailureLogged = false;
        var root = Load(SessionPath);
        if (_projects.Project is not null && root["projectManager"] is JsonObject tree)
        {
            var expanded = tree["expanded"] is JsonArray keys ? keys.Select(LayoutStateJson.Str).OfType<string>().ToList() : null;
            if (expanded is not null)
                _projectManager.RestoreSessionState(expanded, LayoutStateJson.Str(tree["selected"]));
        }

        // IDE-ED-114: before the documents open, so they anchor the bookmarks as they come.
        _bookmarks?.Load(root["bookmarks"] is JsonArray marks
            ? marks.OfType<JsonObject>()
                .Select(m => (Path: Resolve(LayoutStateJson.Str(m["path"])), Line: LayoutStateJson.Int(m["line"]), Number: LayoutStateJson.Int(m["number"])))
                .Where(m => m.Path is not null && m.Line is not null)
                .Select(m => new Editing.BookmarkInfo(m.Path!, m.Line!.Value, m.Number))
                .ToList()
            : []);

        // Breakpoints are per project (UI-TW-800); like the bookmarks, before the documents open.
        _breakpoints?.Load(_projects.Project is not null && root["breakpoints"] is JsonArray stops
            ? [.. stops.OfType<JsonObject>().Select(ReadBreakpoint).OfType<Models.BreakpointInfo>()]
            : []);

        if (_inspector is not null && root["objectInspector"] is JsonObject inspector)
            _inspector.RestoreSessionState(ReadInspector(inspector));

        if (!restoreDocuments || root["documents"] is not JsonArray entries)
            return 0;

        var activePath = Resolve(LayoutStateJson.Str(root["activeDocument"]));
        DocumentViewModel? active = null;
        var restored = 0;
        foreach (var entry in entries.OfType<JsonObject>().ToList())
        {
            // IDE-ARCH-073: a file that no longer exists is skipped silently.
            if (Resolve(LayoutStateJson.Str(entry["path"])) is not { } path || !_fs.FileExists(path))
                continue;
            var opened = await _documents.OpenAsync(path);
            if (opened.Value is not { } document)
            {
                _log.Warning(LogCategory, $"{path}: not restored ({opened.MessageId})");
                continue;
            }

            restored++;
            if (document is TextDocumentViewModel text)
                Apply(entry, text);
            if (document is FormDocumentViewModel form && ViewOf(LayoutStateJson.Str(entry["view"])) is { } view && view != form.ActiveView
                && !form.ShowView(view))
            {
                _log.Warning(LogCategory, $"{path}: the stored {view} view could not be shown");
            }

            if (string.Equals(path, activePath, StringComparison.OrdinalIgnoreCase))
                active = document;
        }

        if (active is not null)
            _documents.Activate(active);
        return restored;
    }

    public void Save()
    {
        _saveTimer?.Dispose();
        _saveTimer = null;
        if (_safeMode)
            return;
        var path = SessionPath;
        var root = _rootPath == path ? _root! : Load(path);
        if (_readOnly)
            return;

        var project = _projects.Project;
        var stored = _documents.Documents.Where(d => d.FilePath is not null && (project is null || _projects.IsMember(d.FilePath))).ToList();
        Put(root, "formatVersion", SupportedFormatVersion);
        Put(root, "project", project is null ? null : Path.GetFileName(project.FilePath));
        Put(root, "activeProfile", _layout.Layout.State.ActiveProfile);
        Put(root, "documents", new JsonArray([.. stored.Select(Write)]));
        Put(root, "activeDocument", _documents.ActiveDocument is { } active && stored.Contains(active) ? ToSessionPath(active.FilePath!) : null);
        if (_bookmarks is not null && (_bookmarks.All.Count > 0 || root.ContainsKey("bookmarks")))
        {
            Put(root, "bookmarks", new JsonArray([.. _bookmarks.All.Select(b => (JsonNode)new JsonObject
            {
                ["path"] = ToSessionPath(b.Path),
                ["line"] = b.Line,
                ["number"] = b.Number,
            })]));
        }
        if (_breakpoints is not null && project is not null && (_breakpoints.All.Count > 0 || root.ContainsKey("breakpoints")))
            Put(root, "breakpoints", new JsonArray([.. _breakpoints.All.Select(WriteBreakpoint)]));
        if (_inspector is not null)
            Put(root, "objectInspector", WriteInspector(_inspector.SessionState));
        if (project is not null)
        {
            Put(root, "projectManager", new JsonObject
            {
                ["expanded"] = new JsonArray([.. _projectManager.ExpandedKeys().Select(k => (JsonNode)k)]),
                ["selected"] = _projectManager.Selection.FirstOrDefault()?.Key,
            });
        }

        try
        {
            var directory = Path.GetDirectoryName(path)!;
            if (!_fs.DirectoryExists(directory))
            {
                _fs.CreateDirectory(directory);
                if (project is not null) // IDE-ARCH-074: only when the state folder is created
                    AtomicFileWriter.Write(_fs, Path.Combine(directory, ".gitignore"), "*\n"u8.ToArray());
            }

            AtomicFileWriter.Write(_fs, path, Serialize(root));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (!_writeFailureLogged)
                _log.Warning(LogCategory, $"{path}: not written ({e.Message})");
            _writeFailureLogged = true;
        }
    }

    /// <summary>The session object of <paramref name="path"/>; a missing or unreadable file gives an empty one (IDE-ARCH-072).</summary>
    private JsonObject Load(string path)
    {
        JsonObject? root = null;
        _readOnly = false;
        if (_fs.FileExists(path))
        {
            try
            {
                var bytes = _fs.ReadAllBytes(path).AsSpan();
                if (bytes.StartsWith(Encoding.UTF8.Preamble))
                    bytes = bytes[Encoding.UTF8.Preamble.Length..];
                root = JsonNode.Parse(bytes) as JsonObject;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
                root = null;
            }

            if (root is null)
                _log.Warning(LogCategory, $"{path} is unreadable; starting without the session");
            else if (LayoutStateJson.Num(root["formatVersion"]) is > SupportedFormatVersion and var version)
            {
                _readOnly = true;
                _log.Warning(LogCategory, $"{path} has newer formatVersion {version}; it will not be overwritten");
            }
        }

        _root = root ?? [];
        _rootPath = path;
        return _root;
    }

    /// <summary>IDE-ARCH-070/073: caret, selection, scroll and folds of one entry, clamped to the text.</summary>
    private static void Apply(JsonObject entry, TextDocumentViewModel document)
    {
        var buffer = document.Buffer;
        var state = document.ViewState;
        if (Position(buffer, entry["caret"]) is { } caret)
            state.Caret = caret;
        if (entry["selection"] is JsonObject selection && Position(buffer, selection["anchor"]) is { } anchor
            && Position(buffer, selection["active"]) is { } activeEnd)
        {
            state.Caret = activeEnd;
            state.SelectionAnchor = anchor == activeEnd ? null : anchor;
        }

        if (entry["scroll"] is JsonObject scroll)
        {
            if (LayoutStateJson.Int(scroll["line"]) is { } line)
                state.TopLine = Math.Clamp(line, 1, buffer.LineCount);
            if (LayoutStateJson.Num(scroll["x"]) is { } x && x >= 0)
                state.HorizontalOffset = x;
        }

        // Fold lines that no longer start a region are dropped by the folding model.
        if (entry["folds"] is JsonArray folds)
            state.FoldedLines = [.. folds.Select(LayoutStateJson.Int).OfType<int>().Distinct().Order()];

        // IDE-ED-039/040: zoom as a percentage of editor.fontSize, word wrap per document.
        if (LayoutStateJson.Int(entry["zoom"]) is > 0 and var zoom)
            document.SetZoomPercent(zoom);
        if (entry["wordWrap"] is JsonValue wrap && wrap.TryGetValue(out bool wordWrap))
            state.WordWrap = wordWrap;
    }

    private static TextPosition? Position(ITextBuffer buffer, JsonNode? node)
    {
        if (node is not JsonObject json || LayoutStateJson.Int(json["line"]) is not { } line || LayoutStateJson.Int(json["column"]) is not { } column)
            return null;
        if (line > buffer.LineCount)
            return buffer.GetPosition(buffer.Length);
        return buffer.GetPosition(buffer.GetOffset(Math.Max(1, line), Math.Max(1, column)));
    }

    private JsonObject Write(DocumentViewModel document)
    {
        var view = document is FormDocumentViewModel form ? form.ActiveView : DocumentView.Code;
        var json = new JsonObject { ["path"] = ToSessionPath(document.FilePath!), ["view"] = view switch
        {
            DocumentView.Design => "design",
            DocumentView.Text => "text",
            _ => "code",
        } };
        if (document is not TextDocumentViewModel text)
            return json;
        var state = text.ViewState;
        json["caret"] = WritePosition(state.Caret);
        json["selection"] = state.SelectionAnchor is { } anchor && anchor != state.Caret
            ? new JsonObject { ["anchor"] = WritePosition(anchor), ["active"] = WritePosition(state.Caret) }
            : null;
        json["scroll"] = new JsonObject { ["line"] = state.TopLine, ["x"] = state.HorizontalOffset };
        json["folds"] = new JsonArray([.. state.FoldedLines.Select(l => (JsonNode)l)]);
        json["zoom"] = text.ZoomPercent;
        json["wordWrap"] = state.WordWrap;
        return json;
    }

    /// <summary>IDE-ARCH-070: <c>view</c> ∈ <c>code</c>, <c>design</c>, <c>text</c>; anything else = none.</summary>
    private static DocumentView? ViewOf(string? text) => text switch
    {
        "code" => DocumentView.Code,
        "design" => DocumentView.Design,
        "text" => DocumentView.Text,
        _ => null,
    };

    private static JsonObject WritePosition(TextPosition position) => new() { ["line"] = position.Line, ["column"] = position.Column };

    /// <summary>IDE-ARCH-070: relative to the project directory with '/' when inside it, otherwise absolute.</summary>
    private string ToSessionPath(string fullPath)
    {
        if (_projects.Project is { } project)
        {
            var relative = Path.GetRelativePath(project.Directory, fullPath);
            if (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return relative.Replace(Path.DirectorySeparatorChar, '/');
        }

        return fullPath;
    }

    private string? Resolve(string? sessionPath)
    {
        if (string.IsNullOrEmpty(sessionPath))
            return null;
        try
        {
            if (Path.IsPathFullyQualified(sessionPath))
                return Path.GetFullPath(sessionPath);
            return _projects.Project is { } project
                ? Path.GetFullPath(Path.Combine(project.Directory, sessionPath.Replace('/', Path.DirectorySeparatorChar)))
                : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// IDE-ARCH-077: <c>path</c> plus the <c>BreakpointSpec</c> members in camelCase; the id is not stored. A legacy
    /// <c>passCount: n</c> is read as <c>hitCount = { greaterOrEqual, n + 1 }</c> when n &gt; 0.
    /// </summary>
    private Models.BreakpointInfo? ReadBreakpoint(JsonObject json)
    {
        if (Resolve(LayoutStateJson.Str(json["path"])) is not { } path || LayoutStateJson.Int(json["line"]) is not { } line || line < 1)
            return null;
        FreexComp.Debugger.HitCountSpec? hitCount = null;
        if (json["hitCount"] is JsonObject hit && LayoutStateJson.Int(hit["value"]) is >= 1 and var value)
        {
            FreexComp.Debugger.HitCountMode? mode = LayoutStateJson.Str(hit["mode"]) switch
            {
                "equal" => FreexComp.Debugger.HitCountMode.Equal,
                "greaterOrEqual" => FreexComp.Debugger.HitCountMode.GreaterOrEqual,
                "multiple" => FreexComp.Debugger.HitCountMode.Multiple,
                _ => null,
            };
            if (mode is { } known)
                hitCount = new FreexComp.Debugger.HitCountSpec(known, value);
        }
        else if (LayoutStateJson.Int(json["passCount"]) is > 0 and var passCount)
        {
            hitCount = Models.BreakpointModel.HitCountOf(passCount);
        }

        var enabled = json["enabled"] is not JsonValue flag || !flag.TryGetValue(out bool on) || on;
        return new Models.BreakpointInfo(path, line, enabled, LayoutStateJson.Str(json["condition"]), hitCount, LayoutStateJson.Str(json["logMessage"]));
    }

    private JsonNode WriteBreakpoint(Models.BreakpointModel breakpoint) => new JsonObject
    {
        ["path"] = ToSessionPath(breakpoint.Path),
        ["line"] = breakpoint.Line,
        ["enabled"] = breakpoint.Enabled,
        ["condition"] = breakpoint.Condition,
        ["hitCount"] = breakpoint.HitCount is { } hitCount
            ? new JsonObject
            {
                ["mode"] = hitCount.Mode switch
                {
                    FreexComp.Debugger.HitCountMode.Equal => "equal",
                    FreexComp.Debugger.HitCountMode.Multiple => "multiple",
                    _ => "greaterOrEqual",
                },
                ["value"] = hitCount.Value,
            }
            : null,
        ["logMessage"] = breakpoint.LogMessage,
    };

    /// <summary>The <c>objectInspector</c> member (object-inspector.md §12 session keys; unknown values fall back to the defaults).</summary>
    private static Designer.ObjectInspectorSessionState ReadInspector(JsonObject json)
    {
        static IReadOnlyList<string> TextList(JsonNode? node) =>
            node is JsonArray array ? [.. array.Select(LayoutStateJson.Str).OfType<string>()] : [];
        var expanded = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (json["expandedRows"] is JsonObject rows)
        {
            foreach (var (className, paths) in rows)
                expanded[className] = TextList(paths);
        }

        var ratio = json["nameColumnRatio"] is JsonValue value && value.TryGetValue(out double r) ? r : Designer.ObjectInspectorViewModel.DefaultNameColumnRatio;
        return new Designer.ObjectInspectorSessionState(
            LayoutStateJson.Str(json["page"]) == "events" ? Designer.InspectorPage.Events : Designer.InspectorPage.Properties,
            TextList(json["collapsedCategories"]), expanded, ratio);
    }

    private static JsonObject WriteInspector(Designer.ObjectInspectorSessionState state)
    {
        var rows = new JsonObject();
        foreach (var (className, paths) in state.ExpandedRows)
            rows[className] = new JsonArray([.. paths.Select(p => (JsonNode)p)]);
        return new JsonObject
        {
            ["page"] = state.Page == Designer.InspectorPage.Events ? "events" : "properties",
            ["collapsedCategories"] = new JsonArray([.. state.CollapsedCategories.Select(c => (JsonNode)c)]),
            ["expandedRows"] = rows,
            ["nameColumnRatio"] = Math.Round(state.NameColumnRatio, 4),
        };
    }

    /// <summary>Replaces a member in place (keeping its position) or appends it.</summary>
    private static void Put(JsonObject root, string name, JsonNode? value)
    {
        if (name == "formatVersion")
        {
            root.Remove(name);
            root.Insert(0, name, value);
            return;
        }

        root[name] = value;
    }

    private static byte[] Serialize(JsonObject root)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, IndentSize = 2, NewLine = "\n", Encoder = SettingValues.Encoder }))
        {
            root.WriteTo(writer);
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    private void OnStoredValueChanged(object? sender, PropertyChangedEventArgs e) => ScheduleSave();

    private void ScheduleSave()
    {
        if (_projects.Project is null)
            return;
        _saveTimer?.Dispose();
        _saveTimer = _clock.Schedule(SaveDelay, () =>
        {
            _saveTimer = null;
            if (_projects.Project is not null)
                Save();
        });
    }
}
