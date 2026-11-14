using System.Text.Json;
using FreexComp.Ide.Core.Logging;

namespace FreexComp.Ide.Core.Projects;

/// <summary>One file of a template: <see cref="Source"/> in the template folder, <see cref="Target"/> with placeholders.</summary>
public sealed record ProjectTemplateFile(string Source, string Target);

/// <summary>A new-project template (<c>template.json</c>, project-format.md §4).</summary>
public sealed class ProjectTemplate
{
    private readonly Func<string, string> _readSource;

    public ProjectTemplate(string id, string name, string description, string language, string appType,
        IReadOnlyList<ProjectTemplateFile> files, Func<string, string> readSource)
    {
        Id = id;
        Name = name;
        Description = description;
        Language = language;
        AppType = appType;
        Files = files;
        _readSource = readSource;
    }

    public string Id { get; }

    public string Name { get; }

    public string Description { get; }

    /// <summary>Language id (<c>pascal</c>).</summary>
    public string Language { get; }

    public string AppType { get; }

    public IReadOnlyList<ProjectTemplateFile> Files { get; }

    /// <summary>Text of a source file of the template, placeholders not yet substituted.</summary>
    public string ReadSource(string source) => _readSource(source);

    /// <summary>Substitutes <c>$(ProjectName)</c>, <c>$(UnitName)</c> = <c>Unit1</c>, <c>$(FormName)</c> = <c>MainForm</c> (IDE-PM-023).</summary>
    public static string Expand(string text, string projectName) =>
        text.Replace("$(ProjectName)", projectName, StringComparison.Ordinal)
            .Replace("$(UnitName)", "Unit1", StringComparison.Ordinal)
            .Replace("$(FormName)", "MainForm", StringComparison.Ordinal);
}

/// <summary>
/// The templates embedded in <c>FreexComp.Ide.Core</c> under <c>Templates/&lt;id&gt;/</c> (IDE-PM-020): an invalid
/// <c>template.json</c> or a missing source file skips the template with a log error. Sorted with
/// <c>console-pascal</c> first, then by name (dialogs.md §3).
/// </summary>
public static class ProjectTemplates
{
    private const string LogCategory = "project";
    private const string ConsoleId = "console-pascal";

    public static IReadOnlyList<ProjectTemplate> LoadEmbedded(ILog log)
    {
        var assembly = typeof(ProjectTemplates).Assembly;
        var names = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("Templates", StringComparison.Ordinal))
            .ToDictionary(n => n.Replace('\\', '/'), StringComparer.Ordinal);
        return Load(names.Keys, name =>
        {
            if (!names.TryGetValue(name, out var resource) || assembly.GetManifestResourceStream(resource) is not { } stream)
                return null;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }, log);
    }

    /// <param name="resourceNames">Names like <c>Templates/&lt;id&gt;/template.json</c>.</param>
    /// <param name="read">Text of a resource, null when it does not exist.</param>
    public static IReadOnlyList<ProjectTemplate> Load(IEnumerable<string> resourceNames, Func<string, string?> read, ILog log)
    {
        ArgumentNullException.ThrowIfNull(resourceNames);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(log);
        var templates = new List<ProjectTemplate>();
        foreach (var name in resourceNames.Where(n => n.EndsWith("/template.json", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            var folder = name[..^"template.json".Length];
            try
            {
                templates.Add(Parse(read(name) ?? "", source => read(folder + source)
                    ?? throw new InvalidDataException($"missing source file '{source}'")));
            }
            catch (Exception e) when (e is JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException)
            {
                log.Error(LogCategory, $"template {name} skipped: {e.Message}");
            }
        }

        return [.. templates.OrderBy(t => t.Id == ConsoleId ? 0 : 1).ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static ProjectTemplate Parse(string json, Func<string, string> readSource)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        string Text(string key) => root.GetProperty(key).GetString() ?? throw new InvalidDataException($"'{key}' is null");
        var files = root.GetProperty("files").EnumerateArray()
            .Select(f => new ProjectTemplateFile(
                f.GetProperty("source").GetString() ?? throw new InvalidDataException("'source' is null"),
                f.GetProperty("target").GetString() ?? throw new InvalidDataException("'target' is null")))
            .ToList();
        foreach (var file in files)
        {
            _ = readSource(file.Source); // a missing source file makes the template invalid now, not at creation
        }

        return new ProjectTemplate(Text("id"), Text("name"), Text("description"), Text("language"), Text("appType"), files, readSource);
    }
}
