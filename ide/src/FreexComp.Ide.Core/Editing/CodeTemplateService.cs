using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;

namespace FreexComp.Ide.Core.Editing;

/// <summary>Where an effective template comes from (code-templates.md §2, IDE-SET-082).</summary>
public enum CodeTemplateOriginKind
{
    /// <summary>The embedded built-ins (IDE-SET-080).</summary>
    BuiltIn,

    /// <summary>A file of the templates folder <c>R\templates\</c> (IDE-SET-081).</summary>
    Folder,

    /// <summary><c>R\templates.json</c>, the Options page's file (IDE-SET-080).</summary>
    User,
}

/// <summary>The origin of a template; <paramref name="FileName"/> = the folder file's name for <see cref="CodeTemplateOriginKind.Folder"/>.</summary>
public sealed record CodeTemplateOrigin(CodeTemplateOriginKind Kind, string? FileName = null);

/// <summary>
/// The code templates (settings.md §7, IDE-SET-080…083): the embedded built-ins, then the files of the templates folder
/// <c>R\templates\*.json</c> in ordinal file-name order, then <c>R\templates.json</c> (user-added templates, modified
/// templates with the same abbreviation and language, and <c>"deleted": true</c> tombstones); the last one wins.
/// Loaded on first use; <see cref="Start"/> reads the folder with reporting and watches it; saved atomically by <see cref="Save"/>.
/// UI thread only.
/// </summary>
public sealed class CodeTemplateService(IUserPaths paths, IFileSystem fileSystem)
{
    /// <summary>IDE-SET-083: the folder is re-read this long after the last watcher event.</summary>
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

    /// <summary>IDE-SET-083: a locked file is read again this long after a failed read …</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);

    /// <summary>… at most this many times.</summary>
    public const int Retries = 3;

    private const string FileName = "templates.json";
    private const string FolderName = "templates";
    private const string LogCategory = "Templates";
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly Lazy<IReadOnlyList<CodeTemplate>> BuiltInList = new(LoadBuiltIns);

    private readonly SortedDictionary<string, IReadOnlyList<CodeTemplate>> _folder = new(StringComparer.Ordinal); // the usable folder files
    private readonly Dictionary<string, int> _reads = new(StringComparer.OrdinalIgnoreCase); // the latest read of each file (stale retries stop)
    private readonly HashSet<string> _changedFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Language, string Abbreviation), CodeTemplateOrigin> _origins = [];
    private bool _folderRead;
    private IReadOnlyList<(CodeTemplate Template, bool Deleted)>? _user;
    private List<CodeTemplate>? _templates;
    private Watching? _watching;
    private IFileWatcher? _rootWatcher;
    private IFileWatcher? _folderWatcher;
    private IDisposable? _pending;
    private bool _resyncAll;

    private sealed record Watching(IFileWatcherFactory Watchers, IClock Clock, IUiDispatcher Dispatcher, IMessagesService Messages, ILog Log);

    /// <summary>The embedded built-in templates in their resource order.</summary>
    public static IReadOnlyList<CodeTemplate> BuiltIns => BuiltInList.Value;

    /// <summary>The templates changed (a save, or a change of the templates folder).</summary>
    public event EventHandler? Changed;

    /// <summary>The effective templates: built-ins (in order, replaced where overridden, without the deleted ones), then the added ones.</summary>
    public IReadOnlyList<CodeTemplate> Templates => _templates ??= Merge();

    private string FilePath => Path.Combine(paths.RoamingDirectory, FileName);

    /// <summary>The templates folder <c>R\templates</c> (IDE-SET-081).</summary>
    public string FolderPath => Path.Combine(paths.RoamingDirectory, FolderName);

    /// <summary>The template of <paramref name="language"/> with the abbreviation (case-insensitive); null when none.</summary>
    public CodeTemplate? Find(string abbreviation, string language = "pascal") =>
        Templates.FirstOrDefault(t => t.Language == language && t.Abbreviation.Equals(abbreviation, StringComparison.OrdinalIgnoreCase));

    /// <summary>The built-in with that abbreviation and language, or null.</summary>
    public static CodeTemplate? BuiltIn(string abbreviation, string language = "pascal") =>
        BuiltIns.FirstOrDefault(t => t.Language == language && t.Abbreviation.Equals(abbreviation, StringComparison.OrdinalIgnoreCase));

    /// <summary>Where the effective template with the abbreviation and language of <paramref name="template"/> comes from.</summary>
    public CodeTemplateOrigin OriginOf(CodeTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        _ = Templates;
        return _origins.GetValueOrDefault(Key(template), new CodeTemplateOrigin(CodeTemplateOriginKind.User));
    }

    /// <summary>
    /// IDE-SET-083: reads the templates folder now, reporting every file that cannot be used (Messages and the log), and
    /// watches it (and <c>R</c>, for the folder appearing or going) from then on. Called once at start-up.
    /// </summary>
    public void Start(IFileWatcherFactory watchers, IClock clock, IUiDispatcher dispatcher, IMessagesService messages, ILog log)
    {
        ArgumentNullException.ThrowIfNull(watchers);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(log);
        _watching = new Watching(watchers, clock, dispatcher, messages, log);
        _folderRead = false;
        _templates = null;
        EnsureFolderRead(); // templates.json stays read on first use, as before the folder existed
        SyncWatchers();
    }

    /// <summary>
    /// Replaces the effective templates with <paramref name="templates"/> and writes the differences from the built-ins and
    /// the folder files to <c>templates.json</c> (an unchanged one is not written; a missing one gets a tombstone).
    /// </summary>
    public void Save(IReadOnlyList<CodeTemplate> templates)
    {
        ArgumentNullException.ThrowIfNull(templates);
        var bases = BaseTemplates();
        var user = new List<(CodeTemplate, bool)>();
        foreach (var template in templates)
        {
            if (bases.Contains(template))
                continue; // an unchanged built-in or folder template
            user.Add((template, false));
        }

        foreach (var removed in bases.Where(b => !templates.Any(t => Key(t) == Key(b))))
            user.Add((removed with { Body = "" }, true));

        var entries = user.Select(e => Entry(e.Item1, e.Item2)).ToList();
        var json = JsonSerializer.Serialize(new Dictionary<string, object> { ["formatVersion"] = 1, ["templates"] = entries }, WriteOptions);
        fileSystem.CreateDirectory(paths.RoamingDirectory);
        var temp = FilePath + ".tmp";
        fileSystem.WriteAllBytes(temp, Encoding.UTF8.GetBytes(json.ReplaceLineEndings("\n") + "\n"));
        fileSystem.MoveFile(temp, FilePath, overwrite: true);
        _user = user;
        _templates = null;
        if (_watching is not null)
            SyncWatchers(); // R may exist only now
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static (string, string) Key(CodeTemplate t) => (t.Language, t.Abbreviation.ToUpperInvariant());

    private static Dictionary<string, object> Entry(CodeTemplate t, bool deleted) => new()
    {
        ["abbreviation"] = t.Abbreviation, ["description"] = t.Description, ["language"] = t.Language, ["body"] = t.Body, ["deleted"] = deleted,
    };

    /// <summary>The built-ins overridden by the folder files: what <c>templates.json</c> is the difference from.</summary>
    private List<CodeTemplate> BaseTemplates()
    {
        EnsureFolderRead();
        var result = BuiltIns.ToList();
        foreach (var template in _folder.Values.SelectMany(t => t))
        {
            var at = result.FindIndex(t => Key(t) == Key(template));
            if (at >= 0)
                result[at] = template;
            else
                result.Add(template);
        }

        return result;
    }

    private List<CodeTemplate> Merge()
    {
        EnsureFolderRead();
        _user ??= LoadUser();
        var result = new List<CodeTemplate>();
        _origins.Clear();

        void Apply(CodeTemplate template, CodeTemplateOrigin origin)
        {
            var at = result.FindIndex(t => Key(t) == Key(template));
            if (at >= 0)
                result[at] = template;
            else
                result.Add(template);
            _origins[Key(template)] = origin;
        }

        foreach (var template in BuiltIns)
            Apply(template, new CodeTemplateOrigin(CodeTemplateOriginKind.BuiltIn));
        foreach (var (file, templates) in _folder)
        {
            foreach (var template in templates)
                Apply(template, new CodeTemplateOrigin(CodeTemplateOriginKind.Folder, file));
        }

        foreach (var (template, deleted) in _user)
        {
            if (!deleted)
            {
                Apply(template, new CodeTemplateOrigin(CodeTemplateOriginKind.User));
            }
            else if (result.FindIndex(t => Key(t) == Key(template)) is var at and >= 0)
            {
                result.RemoveAt(at);
                _origins.Remove(Key(template));
            }
        }

        return result;
    }

    private IReadOnlyList<(CodeTemplate, bool)> LoadUser()
    {
        if (!fileSystem.FileExists(FilePath))
            return [];
        try
        {
            return Parse(fileSystem.ReadAllBytes(FilePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return []; // unreadable: the built-ins and the folder only
        }
    }

    private void EnsureFolderRead()
    {
        if (_folderRead)
            return;
        _folderRead = true;
        _folder.Clear();
        foreach (var name in FolderFiles())
            ReadFile(name, 0);
    }

    /// <summary>The names of the <c>*.json</c> files directly in the folder, in ordinal order; none when it does not exist.</summary>
    private List<string> FolderFiles()
    {
        try
        {
            if (!fileSystem.DirectoryExists(FolderPath))
                return [];
            return [.. fileSystem.EnumerateFiles(FolderPath, "*.json").Select(Path.GetFileName).OfType<string>().Where(IsTemplateFile)
                .Order(StringComparer.Ordinal)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool IsTemplateFile(string name) => Path.GetExtension(name).Equals(".json", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads one folder file: usable → its templates replace the file's earlier ones; unusable → the file's templates are
    /// dropped and it is reported; locked → read again after <see cref="RetryDelay"/> up to <see cref="Retries"/> times.
    /// </summary>
    private void ReadFile(string name, int attempt)
    {
        var read = attempt == 0 ? (_reads[name] = _reads.GetValueOrDefault(name) + 1) : _reads[name];
        var path = Path.Combine(FolderPath, name);
        byte[] bytes;
        try
        {
            bytes = fileSystem.ReadAllBytes(path);
        }
        catch (Exception ex) when (attempt < Retries && SharingRetry.IsSharingViolation(ex) && _watching is { } watching)
        {
            watching.Clock.Schedule(RetryDelay, () =>
            {
                if (_reads.GetValueOrDefault(name) != read)
                    return; // a later change of the file has read it again
                ReadFile(name, attempt + 1);
                Publish();
            });
            return;
        }
        catch (FileNotFoundException)
        {
            _folder.Remove(name);
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _folder.Remove(name);
            Report(name, Format(Strings.Msg_Templates_Unreadable, ex.Message), 1, 1);
            return;
        }

        var (templates, reason, line, column) = ParseFolderFile(bytes);
        if (templates is not null)
        {
            _folder[name] = templates;
        }
        else
        {
            _folder.Remove(name);
            Report(name, reason!, line, column);
        }
    }

    /// <summary>IDE-SET-083: one warning row in Messages (before the summary row) and the same text in the log.</summary>
    private void Report(string name, string reason, int line, int column)
    {
        if (_watching is not { } watching)
            return;
        var text = Format(Strings.Msg_Templates_FileSkipped, name, reason);
        var location = new MessageLocation(Path.Combine(FolderPath, name), line, column, line, column);
        watching.Messages.AddToolMessage(MessageItem.ToolRow(MessageSeverity.Warning, text, location));
        watching.Log.Warning(LogCategory, text);
    }

    private static string Format(string text, params object[] args) => string.Format(CultureInfo.CurrentCulture, text, args);

    /// <summary>IDE-SET-081, 083: a folder file with the <c>templates.json</c> schema and no tombstones; any problem rejects the whole file.</summary>
    private static (IReadOnlyList<CodeTemplate>? Templates, string? Reason, int Line, int Column) ParseFolderFile(byte[] bytes)
    {
        var span = bytes.AsSpan();
        if (span.StartsWith(Encoding.UTF8.Preamble))
            span = span[Encoding.UTF8.Preamble.Length..];
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(span.ToArray());
        }
        catch (JsonException ex)
        {
            var line = (int)(ex.LineNumber ?? 0) + 1;
            var column = (int)(ex.BytePositionInLine ?? 0) + 1;
            return (null, Format(Strings.Msg_Templates_InvalidJson, line, column), line, column);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("formatVersion", out var version) || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var v) || v != 1 || !root.TryGetProperty("templates", out var list) || list.ValueKind != JsonValueKind.Array
                || list.EnumerateArray().Any(e => e.ValueKind != JsonValueKind.Object))
                return Failed(Strings.Msg_Templates_WrongFormat);
            var result = new List<CodeTemplate>();
            foreach (var entry in list.EnumerateArray())
            {
                string Text(string name) => entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
                var abbreviation = Text("abbreviation");
                if (entry.TryGetProperty("deleted", out _))
                    return Failed(Format(Strings.Msg_Templates_DeletedNotAllowed, abbreviation));
                if (!CodeTemplateExpander.IsValidAbbreviation(abbreviation))
                    return Failed(Format(Strings.Msg_Templates_InvalidAbbreviation, abbreviation));
                var language = Text("language");
                if (language.Length == 0)
                    language = "pascal";
                if (language != "pascal")
                    return Failed(Format(Strings.Msg_Templates_UnknownLanguage, abbreviation, language));
                var body = Text("body");
                if (body.Length == 0)
                    return Failed(Format(Strings.Msg_Templates_EmptyBody, abbreviation));
                if (CodeTemplateExpander.FindSyntaxError(body) is { } error)
                    return Failed(SyntaxReason(abbreviation, error));
                result.Add(new CodeTemplate(abbreviation, Text("description"), language, body));
            }

            return (result, null, 0, 0);
        }

        static (IReadOnlyList<CodeTemplate>?, string?, int, int) Failed(string reason) => (null, reason, 1, 1);
    }

    /// <summary>The reason text of a body syntax error (IDE-SET-083).</summary>
    public static string SyntaxReason(string abbreviation, TemplateSyntaxError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Kind == TemplateSyntaxErrorKind.UnknownFunction
            ? Format(Strings.Msg_Templates_UnknownFunction, abbreviation, error.Name ?? "")
            : Format(Strings.Msg_Templates_Unterminated, abbreviation, error.Line, error.Column);
    }

    // ---- watching (IDE-SET-083) ----

    /// <summary>Watches <c>R</c> while it exists (the folder appearing, going or renamed) and the folder while it exists.</summary>
    private void SyncWatchers()
    {
        if (_watching is not { } watching)
            return;
        if (_rootWatcher is null && fileSystem.DirectoryExists(paths.RoamingDirectory))
            _rootWatcher = TryWatch(watching, paths.RoamingDirectory, OnRootChanged);
        var exists = fileSystem.DirectoryExists(FolderPath);
        if (exists && _folderWatcher is null)
        {
            _folderWatcher = TryWatch(watching, FolderPath, OnFolderChanged);
        }
        else if (!exists && _folderWatcher is not null)
        {
            _folderWatcher.Dispose();
            _folderWatcher = null;
        }
    }

    private IFileWatcher? TryWatch(Watching watching, string directory, Action<FileChange> changed)
    {
        try
        {
            var watcher = watching.Watchers.Watch(directory);
            watcher.Changed += (_, change) => watching.Dispatcher.Post(() => changed(change));
            watcher.Error += (_, error) => watching.Dispatcher.Post(() =>
            {
                watching.Log.Warning(LogCategory, $"Watcher error in {directory}; reading the templates folder again.", error);
                _resyncAll = true;
                ScheduleFlush();
            });
            return watcher;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            watching.Log.Warning(LogCategory, $"Cannot watch {directory}; the templates folder is read at start-up only.", ex);
            return null;
        }
    }

    private void OnRootChanged(FileChange change)
    {
        // Only the folder appearing, going or being renamed counts; a Changed event of the folder is its last-write time,
        // which every file write in it moves (the folder watcher reports those files).
        if (change.Kind != FileChangeKind.Changed
            && (IsPath(change.Path, FolderPath) || (change.OldPath is { } old && IsPath(old, FolderPath))))
        {
            _resyncAll = true;
            ScheduleFlush();
        }
    }

    private void OnFolderChanged(FileChange change)
    {
        foreach (var path in (string?[])[change.Path, change.OldPath])
        {
            if (path is not null && IsPath(Path.GetDirectoryName(Path.GetFullPath(path)), FolderPath) && Path.GetFileName(path) is { Length: > 0 } name
                && IsTemplateFile(name))
                _changedFiles.Add(name);
        }

        ScheduleFlush();
    }

    private static bool IsPath(string? a, string b) =>
        a is not null && StringComparer.OrdinalIgnoreCase.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)));

    private void ScheduleFlush()
    {
        if (_watching is not { } watching)
            return;
        _pending?.Dispose();
        _pending = watching.Clock.Schedule(Debounce, Flush);
    }

    /// <summary>The debounce elapsed: the changed files (or, after a folder change, the whole folder) are read again.</summary>
    private void Flush()
    {
        _pending = null;
        if (_resyncAll)
        {
            _resyncAll = false;
            _changedFiles.Clear();
            SyncWatchers();
            var files = FolderFiles();
            foreach (var gone in _folder.Keys.Where(k => !files.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList())
                _folder.Remove(gone);
            foreach (var name in files)
                ReadFile(name, 0);
        }
        else
        {
            foreach (var name in _changedFiles.Order(StringComparer.Ordinal).ToList())
            {
                if (fileSystem.FileExists(Path.Combine(FolderPath, name)))
                    ReadFile(name, 0);
                else
                {
                    _reads[name] = _reads.GetValueOrDefault(name) + 1; // a pending retry of the file stops
                    _folder.Remove(name);
                }
            }

            _changedFiles.Clear();
        }

        Publish();
    }

    /// <summary>The merged set changed: it replaces the old one at once (IDE-SET-083).</summary>
    private void Publish()
    {
        _templates = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static IReadOnlyList<(CodeTemplate, bool)> Parse(byte[] bytes)
    {
        var span = bytes.AsSpan();
        if (span.StartsWith(Encoding.UTF8.Preamble))
            span = span[Encoding.UTF8.Preamble.Length..];
        using var document = JsonDocument.Parse(span.ToArray());
        var list = new List<(CodeTemplate, bool)>();
        foreach (var entry in document.RootElement.GetProperty("templates").EnumerateArray())
        {
            string Text(string name) => entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
            var abbreviation = Text("abbreviation");
            if (!CodeTemplateExpander.IsIdentifier(abbreviation))
                continue;
            var language = Text("language");
            var deleted = entry.TryGetProperty("deleted", out var d) && d.ValueKind == JsonValueKind.True;
            list.Add((new CodeTemplate(abbreviation, Text("description"), language.Length > 0 ? language : "pascal", Text("body")), deleted));
        }

        return list;
    }

    private static IReadOnlyList<CodeTemplate> LoadBuiltIns()
    {
        using var stream = typeof(CodeTemplateService).Assembly.GetManifestResourceStream("BuiltInTemplates.json")
            ?? throw new InvalidOperationException("BuiltInTemplates.json is not embedded.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        // IDE-ED-153: the descriptions are resources Template_<abbreviation>.
        return [.. Parse(memory.ToArray()).Select(e => e.Item1 with
        {
            Description = Strings.ResourceManager.GetString("Template_" + e.Item1.Abbreviation, CultureInfo.CurrentCulture) ?? e.Item1.Description,
        })];
    }
}
