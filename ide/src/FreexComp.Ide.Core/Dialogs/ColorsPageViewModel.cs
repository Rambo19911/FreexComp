using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Themes;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>A row of the Colors page's element list: a group heading or one editor colour token.</summary>
public sealed class ColorElement : ObservableObject
{
    private bool _isOverridden;

    internal ColorElement(string? token, string label)
    {
        Token = token;
        Label = label;
    }

    /// <summary>The token (<c>syntax.keyword</c>); null for a group heading ("Syntax", "Editor").</summary>
    public string? Token { get; }

    public string Label { get; }

    public bool IsGroup => Token is null;

    /// <summary><c>syntax.*</c>: has Bold/Italic (style key).</summary>
    public bool IsSyntax => Token?.StartsWith("syntax.", StringComparison.Ordinal) == true;

    /// <summary>Foreground tokens show the contrast against <c>bg.editor</c> (dialogs.md §9.4).</summary>
    public bool IsForeground => IsSyntax || Token is "editor.lineNumber" or "editor.lineNumber.current" or "editor.caret";

    /// <summary>The pending colour or style differs from the theme default (row in <c>type.bodyStrong</c>).</summary>
    public bool IsOverridden
    {
        get => _isOverridden;
        internal set => SetProperty(ref _isOverridden, value);
    }
}

/// <summary>
/// The Colors page (dialogs.md §9.4): a working copy of every <c>colors.&lt;theme&gt;.&lt;token&gt;[.style]</c> key of
/// both themes. <see cref="Theme"/> selects which set is edited (it does not switch the IDE theme); the element,
/// colour, Bold/Italic and contrast fields act on <see cref="SelectedElement"/> in that set. Pending values become
/// settings only through <see cref="OptionsViewModel"/>'s OK/Apply.
/// </summary>
public sealed class ColorsPageViewModel : ObservableObject
{
    public const string LightTheme = "light";
    public const string DarkTheme = "dark";

    /// <summary><c>bg.editor</c> (design-tokens.md §2.1; not user-overridable, IDE-SET-040).</summary>
    private static readonly Dictionary<string, string> EditorBackgrounds = new(StringComparer.Ordinal) { [LightTheme] = "#FFFFFF", [DarkTheme] = "#171A19" };

    private readonly ISettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly Dictionary<string, string> _pending = new(StringComparer.Ordinal);
    private string _theme;
    private ColorElement? _selectedElement;
    private string _colorText = "";
    private string? _colorError;

    internal ColorsPageViewModel(ISettingsService settings, IDialogService dialogs, ThemeKind actualTheme)
    {
        _settings = settings;
        _dialogs = dialogs;
        _theme = actualTheme == ThemeKind.Dark ? DarkTheme : LightTheme;
        foreach (var key in Keys)
        {
            _pending[key.Name] = settings.Get(key);
        }

        Elements =
        [
            new ColorElement(null, Strings.Options_Colors_GroupSyntax),
            .. SettingsCatalog.EditorColorTokens.Where(t => t.Style is not null).Select(t => new ColorElement(t.Token, Label(t.Token))),
            new ColorElement(null, Strings.Options_Colors_GroupEditor),
            .. SettingsCatalog.EditorColorTokens.Where(t => t.Style is null).Select(t => new ColorElement(t.Token, Label(t.Token))),
        ];
        _selectedElement = Elements[1];
        Refresh();
    }

    /// <summary>Raised when a pending colour or style changes (the preview redraws, the dialog updates its buttons).</summary>
    public event EventHandler? PendingChanged;

    public IReadOnlyList<string> Themes { get; } = [LightTheme, DarkTheme];

    /// <summary>The theme whose set is edited and previewed: <c>light</c> or <c>dark</c>.</summary>
    public string Theme
    {
        get => _theme;
        set
        {
            if (value is not (LightTheme or DarkTheme))
                throw new ArgumentException("light or dark", nameof(value));
            if (SetProperty(ref _theme, value))
                Refresh();
        }
    }

    public bool IsDarkTheme => _theme == DarkTheme;

    /// <summary><c>bg.editor</c> of the selected theme (preview background, contrast reference).</summary>
    public string EditorBackground => EditorBackgrounds[_theme];

    public IReadOnlyList<ColorElement> Elements { get; }

    public ColorElement? SelectedElement
    {
        get => _selectedElement;
        set
        {
            if (value is { IsGroup: true })
                value = Elements[Elements.ToList().IndexOf(value) + 1];
            if (SetProperty(ref _selectedElement, value))
                Refresh();
        }
    }

    /// <summary>The hex input <c>#RRGGBB</c> of the selected element; a valid text becomes the pending colour.</summary>
    public string ColorText
    {
        get => _colorText;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!SetProperty(ref _colorText, value) || SelectedElement?.Token is not { } token)
                return;
            if (IsColor(value))
            {
                ColorError = null;
                SetPending(ColorKeyName(token), value.ToUpperInvariant());
            }
            else
            {
                ColorError = Strings.Msg_Options_ColorInvalid;
            }
        }
    }

    public string? ColorError
    {
        get => _colorError;
        private set => SetProperty(ref _colorError, value);
    }

    public bool CanEditStyle => SelectedElement?.IsSyntax == true;

    public bool Bold
    {
        get => Style() is "bold" or "bolditalic";
        set => SetStyle(value, Italic);
    }

    public bool Italic
    {
        get => Style() is "italic" or "bolditalic";
        set => SetStyle(Bold, value);
    }

    public bool ShowContrast => SelectedElement?.IsForeground == true;

    public double Contrast => SelectedElement?.Token is { } token ? ColorContrast.Ratio(ColorOf(_theme, token), EditorBackground) : 0;

    /// <summary><c>n.n : 1</c>.</summary>
    public string ContrastText => string.Format(CultureInfo.CurrentCulture, Strings.Options_Colors_ContrastValue, Contrast);

    /// <summary>Below 4.5 the contrast is shown as a warning (not blocking, IDE-SET-041).</summary>
    public bool IsContrastLow => ShowContrast && Contrast < 4.5;

    public bool CanResetElement => SelectedElement?.IsOverridden == true;

    public bool HasErrors => ColorError is not null;

    /// <summary>True while a pending colour or style differs from its setting.</summary>
    public bool HasChanges => Keys.Any(k => _pending[k.Name] != _settings.Get(k));

    /// <summary>UI-DLG-095: row 1 ten greys from black to white, rows 2–6 ten hues at five lightness steps.</summary>
    public static IReadOnlyList<string> Palette { get; } = BuildPalette();

    /// <summary>The theme default of the selected element (the palette's "Default" entry).</summary>
    public string? DefaultColor => SelectedElement?.Token is { } token ? DefaultOf(_theme, token) : null;

    /// <summary>The pending colour of <paramref name="token"/> in <paramref name="theme"/>.</summary>
    public string ColorOf(string theme, string token) => _pending[$"colors.{theme}.{token}"];

    /// <summary>The pending style of a syntax token in <paramref name="theme"/>; <c>regular</c> for decoration tokens.</summary>
    public string StyleOf(string theme, string token) => _pending.GetValueOrDefault($"colors.{theme}.{token}.style", "regular");

    /// <summary>A palette swatch or "Default" was picked.</summary>
    public void PickColor(string color) => ColorText = color;

    /// <summary>A token was clicked in the preview: its element is selected.</summary>
    public void SelectToken(string token)
    {
        if (Elements.FirstOrDefault(e => e.Token == token) is { } element)
            SelectedElement = element;
    }

    /// <summary><c>Reset Element</c>: the element's colour (and style) go back to the theme default (pending).</summary>
    public void ResetElement()
    {
        if (SelectedElement?.Token is not { } token)
            return;
        SetPending(ColorKeyName(token), DefaultOf(_theme, token));
        if (SelectedElement.IsSyntax)
            SetPending(ColorKeyName(token) + ".style", (string)Key(ColorKeyName(token) + ".style").DefaultValue);
        Refresh();
    }

    /// <summary><c>Reset All…</c>: asks <c>Msg.Options.ResetColorsConfirm</c>, then resets the theme's whole set (pending).</summary>
    public async Task ResetAllAsync()
    {
        var themeName = IsDarkTheme ? Strings.Options_Theme_Dark : Strings.Options_Theme_Light;
        if (await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Options.ResetColorsConfirm", [themeName])) != "Reset")
            return;
        foreach (var key in Keys.Where(k => k.Name.StartsWith($"colors.{_theme}.", StringComparison.Ordinal)))
        {
            SetPending(key.Name, (string)key.DefaultValue);
        }

        Refresh();
    }

    /// <summary>Writes the changed pending values (inside the dialog's settings batch); a default value is a reset.</summary>
    internal void Apply()
    {
        foreach (var key in Keys.Where(k => _pending[k.Name] != _settings.Get(k)))
        {
            if (_pending[key.Name] == key.Default)
                _settings.Reset(key.Name);
            else
                _settings.Set(key, _pending[key.Name]);
        }

        Refresh();
    }

    /// <summary>Labels of the page's fields, for the dialog filter (UI-DLG-091).</summary>
    internal static IReadOnlyList<string> FieldLabels =>
    [
        Strings.Options_Colors_Theme, Strings.Options_Colors_Element, Strings.Options_Colors_Color, Strings.Options_Colors_Bold,
        Strings.Options_Colors_Italic, Strings.Options_Colors_Contrast, Strings.Options_Colors_ResetElement, Strings.Options_Colors_ResetAll,
        Strings.Options_Preview,
    ];

    private static IEnumerable<SettingKey<string>> Keys =>
        from theme in new[] { LightTheme, DarkTheme }
        from t in SettingsCatalog.EditorColorTokens
        from key in t.Style is null
            ? [SettingsCatalog.ColorKey(theme, t.Token)]
            : new[] { SettingsCatalog.ColorKey(theme, t.Token), SettingsCatalog.StyleKey(theme, t.Token) }
        select key;

    private static SettingKey<string> Key(string name) => Keys.First(k => k.Name == name);

    private static string DefaultOf(string theme, string token) => SettingsCatalog.ColorKey(theme, token).Default;

    private static string Label(string token) =>
        Strings.ResourceManager.GetString("Options_Color_" + token.Replace('.', '_'), CultureInfo.CurrentCulture)
        ?? throw new InvalidOperationException($"Missing label for {token}.");

    private static bool IsColor(string s) => s.Length == 7 && s[0] == '#' && s.Skip(1).All(char.IsAsciiHexDigit);

    private string ColorKeyName(string token) => $"colors.{_theme}.{token}";

    private string Style() => SelectedElement?.Token is { } token ? StyleOf(_theme, token) : "regular";

    private void SetStyle(bool bold, bool italic)
    {
        if (SelectedElement is not { IsSyntax: true, Token: { } token })
            return;
        SetPending(ColorKeyName(token) + ".style", (bold, italic) switch
        {
            (true, true) => "bolditalic",
            (true, false) => "bold",
            (false, true) => "italic",
            _ => "regular",
        });
        Refresh();
    }

    private void SetPending(string keyName, string value)
    {
        if (_pending[keyName] == value)
            return;
        _pending[keyName] = value;
        UpdateOverridden();
        OnPropertyChanged(nameof(Contrast));
        OnPropertyChanged(nameof(ContrastText));
        OnPropertyChanged(nameof(IsContrastLow));
        OnPropertyChanged(nameof(CanResetElement));
        PendingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateOverridden()
    {
        foreach (var element in Elements.Where(e => !e.IsGroup))
        {
            var name = $"colors.{_theme}.{element.Token}";
            element.IsOverridden = _pending[name] != DefaultOf(_theme, element.Token!)
                || (element.IsSyntax && _pending[name + ".style"] != (string)Key(name + ".style").DefaultValue);
        }
    }

    /// <summary>The selected element or theme changed (or a reset): every field shows the pending values again.</summary>
    private void Refresh()
    {
        UpdateOverridden();
        if (SelectedElement?.Token is { } token)
        {
            SetProperty(ref _colorText, ColorOf(_theme, token), nameof(ColorText));
            ColorError = null;
        }

        OnPropertyChanged(string.Empty);
        PendingChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string[] BuildPalette()
    {
        var colors = new List<string>();
        for (var i = 0; i < 10; i++)
        {
            var v = (int)Math.Round(i * 255 / 9.0);
            colors.Add(Hex(v, v, v));
        }

        // red, orange, amber, yellow-green, green, teal, cyan, blue, violet, magenta
        double[] hues = [0, 28, 45, 80, 125, 170, 190, 215, 270, 315];
        double[] lightness = [0.25, 0.38, 0.5, 0.65, 0.8];
        foreach (var l in lightness)
        {
            foreach (var h in hues)
            {
                colors.Add(FromHsl(h, 0.7, l));
            }
        }

        return [.. colors];
    }

    private static string FromHsl(double h, double s, double l)
    {
        var c = (1 - Math.Abs((2 * l) - 1)) * s;
        var x = c * (1 - Math.Abs((h / 60 % 2) - 1));
        var m = l - (c / 2);
        var (r, g, b) = (h / 60) switch
        {
            < 1 => (c, x, 0.0),
            < 2 => (x, c, 0.0),
            < 3 => (0.0, c, x),
            < 4 => (0.0, x, c),
            < 5 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return Hex((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
    }

    private static string Hex(int r, int g, int b) => string.Create(CultureInfo.InvariantCulture, $"#{r:X2}{g:X2}{b:X2}");
}
