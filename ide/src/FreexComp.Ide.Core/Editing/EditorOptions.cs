using System.ComponentModel;
using FreexComp.Ide.Core.Settings;

namespace FreexComp.Ide.Core.Editing;

/// <summary>
/// The editor settings the code editor view draws with (settings.md §4.2, editor.md §2 and §6). One instance follows
/// <see cref="ISettingsService"/> and raises <see cref="PropertyChanged"/> per changed setting, so open editors update
/// at once. <see cref="Default"/> holds the catalog defaults (documents created outside the document service).
/// </summary>
public sealed class EditorOptions : INotifyPropertyChanged
{
    private static readonly Dictionary<string, string> Properties = new(StringComparer.Ordinal)
    {
        [SettingsCatalog.EditorShowLineNumbers.Name] = nameof(ShowLineNumbers),
        [SettingsCatalog.EditorShowFoldMargin.Name] = nameof(ShowFoldMargin),
        [SettingsCatalog.EditorShowChangeBar.Name] = nameof(ShowChangeBar),
        [SettingsCatalog.EditorHighlightCurrentLine.Name] = nameof(HighlightCurrentLine),
        [SettingsCatalog.EditorRightMargin.Name] = nameof(RightMargin),
        [SettingsCatalog.EditorShowWhitespace.Name] = nameof(ShowWhitespace),
    };

    private readonly ISettingsService? _settings;

    public EditorOptions(ISettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        settings.Changed += (_, e) =>
        {
            if (Properties.TryGetValue(e.Key, out var property))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        };
    }

    private EditorOptions()
    {
    }

    public static EditorOptions Default { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool ShowLineNumbers => Get(SettingsCatalog.EditorShowLineNumbers);

    public bool ShowFoldMargin => Get(SettingsCatalog.EditorShowFoldMargin);

    public bool ShowChangeBar => Get(SettingsCatalog.EditorShowChangeBar);

    public bool HighlightCurrentLine => Get(SettingsCatalog.EditorHighlightCurrentLine);

    /// <summary>Column of the right-margin ruler; 0 hides it (UI-ED-057).</summary>
    public int RightMargin => Get(SettingsCatalog.EditorRightMargin);

    public bool ShowWhitespace => Get(SettingsCatalog.EditorShowWhitespace);

    private T Get<T>(SettingKey<T> key) where T : notnull => _settings is null ? key.Default : _settings.Get(key);
}
