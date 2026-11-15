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
        [SettingsCatalog.EditorAutoCloseBrackets.Name] = nameof(AutoCloseBrackets),
        [SettingsCatalog.EditorRectangularSelection.Name] = nameof(RectangularSelection),
        [SettingsCatalog.EditorHighlightMatchingBrackets.Name] = nameof(HighlightMatchingBrackets),
        [SettingsCatalog.EditorHighlightWordOccurrences.Name] = nameof(HighlightWordOccurrences),
        [SettingsCatalog.EditorShowIndentGuides.Name] = nameof(ShowIndentGuides),
        [SettingsCatalog.EditorFontSize.Name] = nameof(FontSize),
        [SettingsCatalog.EditorFontFamily.Name] = nameof(FontFamily),
        [SettingsCatalog.EditorWordWrap.Name] = nameof(WordWrap),
        [SettingsCatalog.EditorMouseWheelZoom.Name] = nameof(MouseWheelZoom),
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

    /// <summary>IDE-ED-020/023: <c>editor.autoCloseBrackets</c>.</summary>
    public bool AutoCloseBrackets => Get(SettingsCatalog.EditorAutoCloseBrackets);

    /// <summary>IDE-ED-024 block completion on Enter (Pascal).</summary>
    public bool AutoCloseBeginEnd => Get(SettingsCatalog.EditorAutoCloseBeginEnd);

    /// <summary>IDE-ED-026: <c>editor.rectangularSelection</c> (Alt+drag, Alt+Shift+Arrows).</summary>
    public bool RectangularSelection => Get(SettingsCatalog.EditorRectangularSelection);

    /// <summary>UI-ED-053: <c>editor.highlightMatchingBrackets</c>.</summary>
    public bool HighlightMatchingBrackets => Get(SettingsCatalog.EditorHighlightMatchingBrackets);

    /// <summary>UI-ED-054: <c>editor.highlightWordOccurrences</c>.</summary>
    public bool HighlightWordOccurrences => Get(SettingsCatalog.EditorHighlightWordOccurrences);

    /// <summary>UI-ED-056: <c>editor.showIndentGuides</c>.</summary>
    public bool ShowIndentGuides => Get(SettingsCatalog.EditorShowIndentGuides);

    /// <summary>IDE-ED-039: <c>editor.fontSize</c>, the unzoomed font size of every editor (6 to 72).</summary>
    public int FontSize => Get(SettingsCatalog.EditorFontSize);

    /// <summary><c>editor.fontFamily</c>; empty = the theme's monospace font (<c>font.mono</c>).</summary>
    public string FontFamily => Get(SettingsCatalog.EditorFontFamily);

    /// <summary>IDE-ED-040: <c>editor.wordWrap</c>, the word wrap of newly opened documents.</summary>
    public bool WordWrap => Get(SettingsCatalog.EditorWordWrap);

    /// <summary>UI-ED-061: <c>editor.mouseWheelZoom</c>, Ctrl+wheel zooms.</summary>
    public bool MouseWheelZoom => Get(SettingsCatalog.EditorMouseWheelZoom);

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
