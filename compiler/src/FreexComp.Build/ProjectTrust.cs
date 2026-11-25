using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FreexComp.Compiler;
using FreexComp.Compiler.Text;

namespace FreexComp.Build;

/// <summary>The per-user list of trusted folders and projects (project-trust.md CMP-1236).</summary>
public interface ITrustStore
{
    /// <summary>Never throws; an unreadable list is empty and carries <see cref="TrustSnapshot.ReadProblem"/>.</summary>
    TrustSnapshot Read();

    /// <summary>Adds the project entry, or replaces the hash of the existing entry of that path.</summary>
    /// <exception cref="IOException">The list could not be written.</exception>
    void TrustProject(string projectPath, string commandsHash);

    /// <summary>Adds the folder entry unless an equal one exists.</summary>
    /// <exception cref="IOException">The list could not be written.</exception>
    void TrustFolder(string folderPath);

    /// <summary>Removes one folder or project entry (kind and path); returns whether it existed.</summary>
    /// <exception cref="IOException">The list could not be written.</exception>
    bool Remove(TrustEntry entry);
}

/// <param name="Path">Absolute directory (<see cref="TrustEntryKind.Folder"/>) or project file (<see cref="TrustEntryKind.Project"/>).</param>
/// <param name="CommandsHash"><see cref="TrustEntryKind.Project"/> only: <see cref="ProjectTrust.CommandsHash"/> of the commands it was trusted with.</param>
public sealed record TrustEntry(TrustEntryKind Kind, string Path, string? CommandsHash);

public enum TrustEntryKind { Folder, Project }

/// <param name="Entries">Folders first, then projects, each in file order.</param>
/// <param name="ReadProblem">null, or why the list could not be read (CMP-1232); <paramref name="Entries"/> is then empty.</param>
public sealed record TrustSnapshot(ImmutableArray<TrustEntry> Entries, string? ReadProblem);

public enum TrustState { NoCommands, TrustedFolder, TrustedProject, Changed, Untrusted }

/// <summary>A project of a build that has commands and is not trusted (CMP-1238); <paramref name="State"/> is Untrusted or Changed.</summary>
/// <param name="PreBuild">The project-level pre-build list.</param>
/// <param name="PostBuild">The project-level post-build list.</param>
public sealed record UntrustedProject(string ProjectPath, TrustState State, ImmutableArray<string> PreBuild, ImmutableArray<string> PostBuild)
{
    /// <summary>The non-empty lists of <see cref="ProjectTrust.CommandLists"/>, in that order (build-events.md CMP-1255).</summary>
    public ImmutableArray<CommandList> Lists { get; init; } = [];
}

/// <summary>One command list of a project (build-events.md CMP-1253).</summary>
/// <param name="Name"><c>preBuild</c>, <c>postBuild</c>, <c>configurations/&lt;Name&gt;/preBuild</c> or <c>configurations/&lt;Name&gt;/postBuild</c>.</param>
/// <param name="Configuration">The configuration as spelled in the file; null = project level.</param>
/// <param name="PostBuild">Whether it is a post-build list.</param>
/// <param name="Commands">The commands as stored.</param>
public sealed record CommandList(string Name, string? Configuration, bool PostBuild, ImmutableArray<string> Commands);

/// <summary>Trust in the command lists of a project (project-trust.md CMP-1230, 1233, 1235, 1237, 1238).</summary>
public static class ProjectTrust
{
    /// <summary>
    /// CMP-1230: <c>&lt;dir&gt;\roaming\trust.json</c> when FREEXCOMP_USER_DIR is set and non-empty, else
    /// <c>%APPDATA%\FreexComp\trust.json</c>.
    /// </summary>
    /// <param name="environment">Name of an environment variable → its value or null.</param>
    public static string DefaultStorePath(Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (environment("FREEXCOMP_USER_DIR") is { Length: > 0 } userDirectory)
        {
            return Path.GetFullPath(Path.Combine(userDirectory, "roaming", "trust.json"));
        }

        var roaming = environment("APPDATA") is { Length: > 0 } appData ? appData : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.GetFullPath(Path.Combine(roaming, "FreexComp", "trust.json"));
    }

    /// <summary>
    /// The command lists of the project in their fixed order (build-events.md CMP-1253): the project-level <c>preBuild</c> and
    /// <c>postBuild</c> (always, also when empty), then for every configuration that has an <c>events</c> key, in file order, its
    /// <c>preBuild</c> and its <c>postBuild</c>.
    /// </summary>
    public static ImmutableArray<CommandList> CommandLists(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        static ImmutableArray<string> Of(ImmutableArray<string> commands) => commands.IsDefault ? [] : commands;
        var lists = ImmutableArray.CreateBuilder<CommandList>();
        lists.Add(new CommandList("preBuild", null, false, Of(project.Events.PreBuild)));
        lists.Add(new CommandList("postBuild", null, true, Of(project.Events.PostBuild)));
        foreach (var name in project.ConfigurationNames)
        {
            if (project.Configurations[name].Events is { } events)
            {
                lists.Add(new CommandList($"configurations/{name}/preBuild", name, false, Of(events.PreBuild)));
                lists.Add(new CommandList($"configurations/{name}/postBuild", name, true, Of(events.PostBuild)));
            }
        }

        return lists.ToImmutable();
    }

    /// <summary>Whether at least one command list of the project (<see cref="CommandLists"/>) is non-empty.</summary>
    public static bool HasCommands(Project project) => CommandLists(project).Any(list => !list.Commands.IsEmpty);

    /// <summary>
    /// CMP-1233, CMP-1253: <c>sha256:</c> + lower-case hex SHA-256 of the line <c>freexcomp-commands-v1</c>, then per list of
    /// <see cref="CommandLists"/> its name, its number of commands and, per command, its UTF-8 byte length and its text; every item
    /// ends with one LF. A project without configuration events has the hash it had before those lists existed (CMP-1254).
    /// </summary>
    public static string CommandsHash(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var buffer = new MemoryStream();
        void Item(string text)
        {
            buffer.Write(Encoding.UTF8.GetBytes(text));
            buffer.WriteByte((byte)'\n');
        }

        Item("freexcomp-commands-v1");
        foreach (var (name, _, _, list) in CommandLists(project))
        {
            Item(name);
            Item(list.Length.ToString(CultureInfo.InvariantCulture));
            foreach (var command in list)
            {
                Item(Encoding.UTF8.GetByteCount(command).ToString(CultureInfo.InvariantCulture));
                Item(command);
            }
        }

        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray()));
    }

    /// <summary>CMP-1237: folder trust first (hashes are not looked at), then the project entry of that path.</summary>
    public static TrustState Evaluate(Project project, string projectPath, TrustSnapshot trust)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(projectPath);
        ArgumentNullException.ThrowIfNull(trust);
        if (!HasCommands(project))
        {
            return TrustState.NoCommands;
        }

        var path = Canonical(projectPath);
        var directory = Path.GetDirectoryName(path) ?? path;
        var entries = trust.Entries.IsDefault ? [] : trust.Entries;
        if (entries.Any(e => e.Kind == TrustEntryKind.Folder && IsInside(directory, e.Path)))
        {
            return TrustState.TrustedFolder;
        }

        if (entries.FirstOrDefault(e => e.Kind == TrustEntryKind.Project && SamePath(e.Path, path)) is not { } entry)
        {
            return TrustState.Untrusted;
        }

        return string.Equals(entry.CommandsHash, CommandsHash(project), StringComparison.Ordinal) ? TrustState.TrustedProject : TrustState.Changed;
    }

    /// <summary>
    /// CMP-1238: in build order, every project a <c>Build</c> of that project or group would build whose state is Untrusted or
    /// Changed. A project file that cannot be loaded is skipped (the build reports it).
    /// </summary>
    /// <param name="store">null → nothing is trusted.</param>
    public static ImmutableArray<UntrustedProject> FindUntrusted(IFileSystem fs, string projectOrGroupPath, string? projectInGroup, ITrustStore? store)
    {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(projectOrGroupPath);
        var plan = ProjectGraph.Plan(fs, PathUtil.Normalize(projectOrGroupPath), projectInGroup, run: false);
        var result = ImmutableArray.CreateBuilder<UntrustedProject>();
        TrustSnapshot? trust = null;
        foreach (var path in plan.Order)
        {
            var load = plan.Loads[path];
            if (load.Value is not { } project || load.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) || !HasCommands(project))
            {
                continue;
            }

            trust ??= store?.Read() ?? new TrustSnapshot([], null);
            if (Evaluate(project, path, trust) is var state and (TrustState.Untrusted or TrustState.Changed))
            {
                result.Add(new UntrustedProject(path, state, project.Events.PreBuild, project.Events.PostBuild)
                {
                    Lists = [.. CommandLists(project).Where(list => !list.Commands.IsEmpty)],
                });
            }
        }

        return result.ToImmutable();
    }

    /// <summary>The form paths have in the trust list (CMP-1231): full, <c>\</c>, no trailing separator except a drive root.</summary>
    /// <exception cref="ArgumentException">The path is not fully qualified.</exception>
    internal static string Canonical(string path)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException($"'{path}' is not a full path", nameof(path));
        }

        var full = Path.GetFullPath(path);
        return full.Length > (Path.GetPathRoot(full)?.Length ?? 0) ? full.TrimEnd('\\') : full;
    }

    /// <summary>CMP-1235: full paths, ordinal ignoring case; a path that is not a full path equals nothing.</summary>
    internal static bool SamePath(string a, string b) =>
        Path.IsPathFullyQualified(a) && Path.IsPathFullyQualified(b) && string.Equals(Canonical(a), Canonical(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>CMP-1235: the directory equals the folder or starts with the folder followed by <c>\</c> (component boundary).</summary>
    private static bool IsInside(string directory, string folder)
    {
        if (!Path.IsPathFullyQualified(folder))
        {
            return false;
        }

        var prefix = Canonical(folder).TrimEnd('\\') + @"\";
        return (Canonical(directory).TrimEnd('\\') + @"\").StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// The trust list in a file (project-trust.md CMP-1231, 1232, 1234): read-modify-write of the whole file at each change, no lock
/// between operations, the last writer wins.
/// </summary>
public sealed class FileTrustStore : ITrustStore
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IFileSystem _fs;

    public FileTrustStore(IFileSystem fs, string filePath)
    {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(filePath);
        _fs = fs;
        FilePath = filePath;
    }

    public string FilePath { get; }

    public TrustSnapshot Read()
    {
        var (content, problem) = Load();
        return new TrustSnapshot(
            [.. content.Folders.Select(f => new TrustEntry(TrustEntryKind.Folder, f, null)),
             .. content.Projects.Select(p => new TrustEntry(TrustEntryKind.Project, p.Path, p.Commands))],
            problem);
    }

    public void TrustProject(string projectPath, string commandsHash)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        ArgumentNullException.ThrowIfNull(commandsHash);
        if (!IsHash(commandsHash))
        {
            throw new ArgumentException($"'{commandsHash}' is not a command hash", nameof(commandsHash));
        }

        var path = ProjectTrust.Canonical(projectPath);
        var (content, _) = Load();
        var index = content.Projects.FindIndex(p => ProjectTrust.SamePath(p.Path, path));
        if (index < 0)
        {
            content.Projects.Add(new ProjectEntry(path, commandsHash, []));
        }
        else if (content.Projects[index].Commands != commandsHash)
        {
            content.Projects[index] = content.Projects[index] with { Commands = commandsHash };
        }
        else
        {
            return;
        }

        Save(content);
    }

    public void TrustFolder(string folderPath)
    {
        ArgumentNullException.ThrowIfNull(folderPath);
        var path = ProjectTrust.Canonical(folderPath);
        var (content, _) = Load();
        if (content.Folders.Any(f => ProjectTrust.SamePath(f, path)))
        {
            return;
        }

        content.Folders.Add(path);
        Save(content);
    }

    public bool Remove(TrustEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var (content, _) = Load();
        var removed = entry.Kind == TrustEntryKind.Folder
            ? content.Folders.RemoveAll(f => ProjectTrust.SamePath(f, entry.Path))
            : content.Projects.RemoveAll(p => ProjectTrust.SamePath(p.Path, entry.Path));
        if (removed == 0)
        {
            return false;
        }

        Save(content);
        return true;
    }

    private static bool IsHash(string value) =>
        value.Length == 71 && value.StartsWith("sha256:", StringComparison.Ordinal) && value.AsSpan(7).IndexOfAnyExcept("0123456789abcdef") < 0;

    /// <summary>CMP-1232: never throws; anything wrong → an empty content and the reason.</summary>
    private (Content Content, string? Problem) Load()
    {
        string text;
        try
        {
            if (!_fs.FileExists(FilePath))
            {
                return (new Content(), null);
            }

            text = _fs.ReadAllText(FilePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return (new Content(), e.Message);
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var content = new Content();
            return Parse(document.RootElement, content) is { } problem ? (new Content(), problem) : (content, null);
        }
        catch (JsonException)
        {
            return (new Content(), "invalid JSON");
        }
    }

    /// <returns>null, or the reason the file does not count.</returns>
    private static string? Parse(JsonElement root, Content content)
    {
        const string Invalid = "invalid entry";
        if (root.ValueKind != JsonValueKind.Object)
        {
            return Invalid;
        }

        var hasVersion = false;
        foreach (var property in root.EnumerateObject())
        {
            switch (property.Name)
            {
                case "formatVersion":
                    if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt64(out var version))
                    {
                        return Invalid;
                    }

                    if (version != 1)
                    {
                        return "unsupported format version " + version.ToString(CultureInfo.InvariantCulture);
                    }

                    hasVersion = true;
                    break;
                case "folders":
                    if (property.Value.ValueKind != JsonValueKind.Array)
                    {
                        return Invalid;
                    }

                    foreach (var folder in property.Value.EnumerateArray())
                    {
                        if (folder.ValueKind != JsonValueKind.String || folder.GetString() is not { } path || !Path.IsPathFullyQualified(path))
                        {
                            return Invalid;
                        }

                        if (!content.Folders.Any(f => ProjectTrust.SamePath(f, path)))
                        {
                            content.Folders.Add(path);
                        }
                    }

                    break;
                case "projects":
                    if (property.Value.ValueKind != JsonValueKind.Array)
                    {
                        return Invalid;
                    }

                    foreach (var project in property.Value.EnumerateArray())
                    {
                        if (ParseProject(project) is not { } entry)
                        {
                            return Invalid;
                        }

                        if (!content.Projects.Any(p => ProjectTrust.SamePath(p.Path, entry.Path)))
                        {
                            content.Projects.Add(entry);
                        }
                    }

                    break;
                default:
                    content.Unknown.Add((property.Name, property.Value.Clone()));
                    break;
            }
        }

        return hasVersion ? null : Invalid;
    }

    private static ProjectEntry? ParseProject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? path = null, commands = null;
        var unknown = new List<(string Name, JsonElement Value)>();
        foreach (var property in element.EnumerateObject())
        {
            switch (property.Name)
            {
                case "path" when property.Value.ValueKind == JsonValueKind.String:
                    path = property.Value.GetString();
                    break;
                case "commands" when property.Value.ValueKind == JsonValueKind.String:
                    commands = property.Value.GetString();
                    break;
                case "path" or "commands":
                    return null;
                default:
                    unknown.Add((property.Name, property.Value.Clone()));
                    break;
            }
        }

        return path is not null && Path.IsPathFullyQualified(path) && commands is not null && IsHash(commands) ? new ProjectEntry(path, commands, unknown) : null;
    }

    /// <summary>CMP-1231, 1234: the canonical text, written through the file system's atomic replace (temporary file in the same directory).</summary>
    private void Save(Content content)
    {
        var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, WriterOptions))
        {
            w.WriteStartObject();
            w.WriteNumber("formatVersion", 1);
            w.WriteStartArray("folders");
            foreach (var folder in content.Folders)
            {
                w.WriteStringValue(folder);
            }

            w.WriteEndArray();
            w.WriteStartArray("projects");
            foreach (var project in content.Projects)
            {
                w.WriteStartObject();
                w.WriteString("path", project.Path);
                w.WriteString("commands", project.Commands);
                WriteUnknown(w, project.Unknown);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            WriteUnknown(w, content.Unknown);
            w.WriteEndObject();
        }

        buffer.WriteByte((byte)'\n');
        try
        {
            _fs.WriteAllBytes(FilePath, buffer.ToArray());
        }
        catch (Exception e) when (e is UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new IOException(e.Message, e);
        }
    }

    private static void WriteUnknown(Utf8JsonWriter w, List<(string Name, JsonElement Value)> unknown)
    {
        foreach (var (name, value) in unknown)
        {
            w.WritePropertyName(name);
            value.WriteTo(w);
        }
    }

    private sealed record ProjectEntry(string Path, string Commands, List<(string Name, JsonElement Value)> Unknown);

    private sealed class Content
    {
        public List<string> Folders { get; } = [];

        public List<ProjectEntry> Projects { get; } = [];

        public List<(string Name, JsonElement Value)> Unknown { get; } = [];
    }
}
