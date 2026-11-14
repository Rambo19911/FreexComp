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
        [SettingsCatalog.EditorTabWidth.Name] = nameof(TabWidth),
        [SettingsCatalog.EditorInsertSpaces.Name] = nameof(InsertSpaces),
        [SettingsCatalog.EditorAutoIndent.Name] = nameof(AutoIndent),
        [SettingsCatalog.EditorSmartHome.Name] = nameof(SmartHome),
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
            else if (e.Key.StartsWith("colors.", StringComparison.Ordinal) && e.Key.EndsWith(".style", StringComparison.Ordinal))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TokenStyle)));
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

    /// <summary>editor-features.md §2: <c>editor.tabWidth</c>.</summary>
    public int TabWidth => Get(SettingsCatalog.EditorTabWidth);

    public bool InsertSpaces => Get(SettingsCatalog.EditorInsertSpaces);

    public bool AutoIndent => Get(SettingsCatalog.EditorAutoIndent);

    public bool SmartHome => Get(SettingsCatalog.EditorSmartHome);

    /// <summary>
    /// The style of a syntax token in a theme: <c>colors.&lt;theme&gt;.&lt;token&gt;.style</c> (<c>regular</c>,
    /// <c>bold</c>, <c>italic</c>, <c>bolditalic</c>; settings.md §4.3); <c>regular</c> for tokens without a style key.
    /// Changes raise <see cref="PropertyChanged"/> with this method's name.
    /// </summary>
    public string TokenStyle(bool dark, string token)
    {
        var entry = SettingsCatalog.EditorColorTokens.FirstOrDefault(t => t.Token == token);
        return entry.Style is null ? "regular" : Get(SettingsCatalog.StyleKey(dark ? "dark" : "light", token));
    }

    private T Get<T>(SettingKey<T> key) where T : notnull => _settings is null ? key.Default : _settings.Get(key);
}
