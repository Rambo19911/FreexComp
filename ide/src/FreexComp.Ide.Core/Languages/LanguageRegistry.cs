namespace FreexComp.Ide.Core.Languages;

/// <summary>Maps file extensions to languages (architecture.md §4, §11). Unknown extensions are Plain Text.</summary>
public interface ILanguageRegistry
{
    /// <summary>Registered languages in registration order, Plain Text last.</summary>
    IReadOnlyList<ILanguageDefinition> Languages { get; }

    ILanguageDefinition PlainText { get; }

    /// <summary>The language of <paramref name="path"/> by its extension (case-insensitive); Plain Text when none matches.</summary>
    ILanguageDefinition ForPath(string path);

    ILanguageDefinition? GetById(string id);
}

public sealed class LanguageRegistry : ILanguageRegistry
{
    private readonly Dictionary<string, ILanguageDefinition> _byExtension = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Phase 1 built-ins.</summary>
    public LanguageRegistry()
        : this([new PascalLanguage(), new FormLanguage(), new JsonLanguage()])
    {
    }

    public LanguageRegistry(IEnumerable<ILanguageDefinition> languages)
    {
        var plain = new PlainTextLanguage();
        var all = languages.ToList();
        foreach (var language in all)
        {
            foreach (var extension in language.Extensions)
            {
                if (!_byExtension.TryAdd(extension, language))
                    throw new ArgumentException($"Extension {extension} is registered twice.", nameof(languages));
            }
        }
        all.Add(plain);
        Languages = all;
        PlainText = plain;
    }

    public IReadOnlyList<ILanguageDefinition> Languages { get; }

    public ILanguageDefinition PlainText { get; }

    public ILanguageDefinition ForPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string extension = Path.GetExtension(path);
        return extension.Length > 0 && _byExtension.TryGetValue(extension, out var language) ? language : PlainText;
    }

    public ILanguageDefinition? GetById(string id) => Languages.FirstOrDefault(l => l.Id == id);
}
