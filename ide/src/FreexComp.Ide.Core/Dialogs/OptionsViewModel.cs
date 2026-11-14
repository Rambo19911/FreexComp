using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>One page of the Options dialog (UI-DLG-090): its fields, and whether it belongs to a later phase.</summary>
public sealed class OptionsPage : ObservableObject
{
    private bool _isVisible = true;

    internal OptionsPage(string id, string title, string treeTitle, int phase, IReadOnlyList<OptionField> fields)
    {
        Id = id;
        Title = title;
        TreeTitle = treeTitle;
        Phase = phase;
        Fields = fields;
    }

    /// <summary><c>Editor.General</c>, …: the row id <c>Dialog.Options.Page.&lt;Id&gt;</c>.</summary>
    public string Id { get; }

    /// <summary>The title above the content ("Editor › General").</summary>
    public string Title { get; }

    /// <summary>The tree label ("General").</summary>
    public string TreeTitle { get; }

    public int Phase { get; }

    /// <summary>A later-phase page: content disabled, note <c>Msg.Options.LaterVersion</c> on top (UI-DLG-090).</summary>
    public bool IsLaterPhase { get; internal set; }

    public IReadOnlyList<OptionField> Fields { get; }

    /// <summary>Labels of fields that are not <see cref="OptionField"/>s (Colors page), matched by the filter.</summary>
    public IReadOnlyList<string> OtherLabels { get; init; } = [];

    /// <summary>False while the filter hides the page (UI-DLG-091).</summary>
    public bool IsVisible
    {
        get => _isVisible;
        internal set => SetProperty(ref _isVisible, value);
    }
}

/// <summary>A row of the page tree: a group (<see cref="Page"/> null, <see cref="Children"/>) or a page.</summary>
public sealed class OptionsTreeRow : ObservableObject
{
    private bool _isVisible = true;

    internal OptionsTreeRow(string id, string title, int level, OptionsPage? page, IReadOnlyList<OptionsTreeRow> children)
    {
        Id = id;
        Title = title;
        Level = level;
        Page = page;
        Children = children;
    }

    public string Id { get; }

    public string Title { get; }

    /// <summary>0 = top level, 1 = inside a group.</summary>
    public int Level { get; }

    public OptionsPage? Page { get; }

    public IReadOnlyList<OptionsTreeRow> Children { get; }

    public bool IsGroup => Page is null;

    public bool IsVisible
    {
        get => _isVisible;
        internal set => SetProperty(ref _isVisible, value);
    }
}

/// <summary>
/// The IDE Options dialog (dialogs.md §9): page tree with filter (UI-DLG-090/091), pages of settings fields, working-copy
/// semantics (UI-DLG-093): fields edit pending values; <c>OK</c>/<c>Apply</c> write the changed ones through
/// <see cref="ISettingsService"/> in one batch followed by <see cref="ISettingsService.FlushAsync"/>; <c>Cancel</c> discards.
/// The last page is remembered in UI state <c>dialogs.Options.page</c>, the size in <c>dialogs.Options.size</c>.
/// </summary>
public sealed class OptionsViewModel : DialogViewModel<bool>
{
    public const string DialogName = "Options";
    public const string EditorGeneralPage = "Editor.General";
    public const string EditorDisplayPage = "Editor.Display";
    public const string ColorsPage = "Colors";

    /// <summary>UI-DLG-096: the fixed Pascal sample of the previews (Editor › Display shows the first 6 lines).</summary>
    public static readonly IReadOnlyList<string> PreviewSample =
    [
        "unit Sample;  // line comment",
        "{$IFDEF DEBUG} { block comment } {$ENDIF}",
        "interface",
        "type TShape = class(TObject) end;",
        "const Max = $FF; Pi2 = 6.28; Tab = #9;",
        "procedure Show(const S: string = 'text');",
        "implementation",
        "label Done;",
        "procedure Show(const S: string); begin asm nop end; goto Done; Done: end;",
        "end. ¤",
    ];

    private readonly ISettingsService _settings;
    private readonly ILayoutService _layout;
    private readonly int _implementedPhase;
    private readonly Dictionary<string, OptionField> _fields = new(StringComparer.Ordinal);
    private OptionsPage? _selectedPage;
    private string _filter = "";
    private string? _filterEmptyText;

    /// <param name="pageId">The page to open (a <c>tools.options</c> argument); null = the page last shown.</param>
    /// <param name="implementedPhase">Pages and fields of later phases are shown disabled.</param>
    /// <param name="actualTheme">The theme in effect: the Colors page edits its set first.</param>
    public OptionsViewModel(ISettingsService settings, ILayoutService layout, IDialogService dialogs, ThemeKind actualTheme, string? pageId,
        int implementedPhase)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(dialogs);
        _settings = settings;
        _layout = layout;
        _implementedPhase = implementedPhase;

        var general = Page(EditorGeneralPage, 1,
        [
            Integer("TabWidth", SettingsCatalog.EditorTabWidth, 1, () => Strings.Msg_Options_TabWidthRange),
            Check("InsertSpaces", SettingsCatalog.EditorInsertSpaces, 1),
            Check("AutoIndent", SettingsCatalog.EditorAutoIndent, 1),
            Check("AutoCloseBrackets", SettingsCatalog.EditorAutoCloseBrackets, 1),
            Check("AutoCloseBeginEnd", SettingsCatalog.EditorAutoCloseBeginEnd, 3),
            Check("SmartHome", SettingsCatalog.EditorSmartHome, 1),
            Check("CaretPastEol", SettingsCatalog.EditorCaretPastEndOfLine, 1),
            Check("RectangularSelection", SettingsCatalog.EditorRectangularSelection, 1),
            Check("ScrollPastEnd", SettingsCatalog.EditorScrollPastEnd, 1),
            Check("MouseWheelZoom", SettingsCatalog.EditorMouseWheelZoom, 1),
            Check("CompletionAuto", SettingsCatalog.EditorCompletionAutoTrigger, 3, Strings.Options_Group_CodeInsight),
            Integer("CompletionDelay", SettingsCatalog.EditorCompletionDelayMs, 3, DelayRange(SettingsCatalog.EditorCompletionDelayMs), enabledBy: "CompletionAuto"),
            Check("ParameterInfoAuto", SettingsCatalog.EditorParameterInfoAutoTrigger, 3),
            Check("HoverEnabled", SettingsCatalog.EditorHoverEnabled, 3),
            Integer("HoverDelay", SettingsCatalog.EditorHoverDelayMs, 3, DelayRange(SettingsCatalog.EditorHoverDelayMs)),
            Check("LiveDiagnostics", SettingsCatalog.EditorLiveDiagnostics, 3),
            Integer("LiveDiagnosticsDelay", SettingsCatalog.EditorLiveDiagnosticsDelayMs, 3, DelayRange(SettingsCatalog.EditorLiveDiagnosticsDelayMs)),
        ]);
        var display = Page(EditorDisplayPage, 1,
        [
            Choice("FontFamily", SettingsCatalog.EditorFontFamily, 1),
            Integer("FontSize", SettingsCatalog.EditorFontSize, 1, () => Strings.Msg_Options_FontSizeRange),
            Check("ShowLineNumbers", SettingsCatalog.EditorShowLineNumbers, 1),
            Check("ShowFoldMargin", SettingsCatalog.EditorShowFoldMargin, 1),
            Check("ShowChangeBar", SettingsCatalog.EditorShowChangeBar, 1),
            Check("HighlightCurrentLine", SettingsCatalog.EditorHighlightCurrentLine, 1),
            Check("HighlightBrackets", SettingsCatalog.EditorHighlightMatchingBrackets, 1),
            Check("HighlightOccurrences", SettingsCatalog.EditorHighlightWordOccurrences, 1),
            Check("ShowIndentGuides", SettingsCatalog.EditorShowIndentGuides, 1),
            Check("ShowWhitespace", SettingsCatalog.EditorShowWhitespace, 1),
            Check("WordWrap", SettingsCatalog.EditorWordWrap, 1),
            Check("CaretBlink", SettingsCatalog.EditorCaretBlink, 1),
            Integer("RightMargin", SettingsCatalog.EditorRightMargin, 1, () => Strings.Msg_Options_RightMarginRange),
        ]);
        var templates = Page("Editor.CodeTemplates", 3, []);
        Colors = new ColorsPageViewModel(settings, dialogs, actualTheme);
        var colors = new OptionsPage(ColorsPage, Strings.Options_Page_Colors, Strings.Options_Tree_Colors, 1, []) { OtherLabels = ColorsPageViewModel.FieldLabels };
        var designer = Page("Designer", 5, []);
        Pages = [general, display, templates, colors, designer];
        foreach (var page in Pages)
        {
            page.IsLaterPhase = page.Phase > implementedPhase;
        }

        Rows = BuildRows([("Editor", [general, display, templates]), (null, [colors, designer])]);
        Colors.PendingChanged += (_, _) => Update();
        Colors.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ColorsPageViewModel.ColorError))
                Update();
        };

        foreach (var field in Pages.SelectMany(p => p.Fields))
        {
            _fields.Add(field.Id, field);
            field.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(OptionField.Value) or nameof(OptionField.Error))
                    Update();
            };
        }

        Ok = new DialogButtonViewModel("Ok", Strings.Button_OK, () => OkAsync()) { IsDefault = true, IsAccent = true };
        Apply = new DialogButtonViewModel("Apply", Strings.Button_Apply, () => ApplyAsync()) { IsEnabled = false };
        Buttons = [Ok, new DialogButtonViewModel("Cancel", Strings.Button_Cancel, () => Close(false)) { IsCancel = true }, Apply];
        foreach (var button in Buttons)
        {
            button.AccessLabel = button.Label; // OK, Cancel and Apply carry no mnemonic (UI-DLG-004; Apply: see report)
        }

        var initial = pageId ?? layout.GetDialogValue(DialogName, "page");
        _selectedPage = Pages.FirstOrDefault(p => p.Id == initial) ?? general;
        Update();
    }

    public override string Name => DialogName;

    public override string Title => Strings.Options_Title;

    public override double Width => 820;

    public override double? Height => 580;

    public override bool CanResize => true;

    public override (double Width, double Height)? StoredSize
    {
        get
        {
            var parts = _layout.GetDialogValue(DialogName, "size")?.Split(',');
            return parts is [var w, var h]
                   && double.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out var width)
                   && double.TryParse(h, NumberStyles.Float, CultureInfo.InvariantCulture, out var height)
                ? (width, height)
                : null;
        }

        set
        {
            if (value is { } size)
                _layout.SetDialogValue(DialogName, "size", string.Create(CultureInfo.InvariantCulture, $"{size.Width:0},{size.Height:0}"));
        }
    }

    public override IReadOnlyList<DialogButtonViewModel> Buttons { get; }

    public DialogButtonViewModel Ok { get; }

    public DialogButtonViewModel Apply { get; }

    public IReadOnlyList<OptionsPage> Pages { get; }

    /// <summary>The Colors page (dialogs.md §9.4).</summary>
    public ColorsPageViewModel Colors { get; }

    /// <summary>The page tree in display order: group rows followed by their pages.</summary>
    public IReadOnlyList<OptionsTreeRow> Rows { get; }

    /// <summary>The page shown on the right; null while the filter hides every page.</summary>
    public OptionsPage? SelectedPage
    {
        get => _selectedPage;
        private set
        {
            if (SetProperty(ref _selectedPage, value) && value is not null)
                _layout.SetDialogValue(DialogName, "page", value.Id);
        }
    }

    /// <summary>The filter box (UI-DLG-091): case-insensitive substring of page titles and field labels.</summary>
    public string Filter
    {
        get => _filter;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (SetProperty(ref _filter, value))
                ApplyFilter();
        }
    }

    /// <summary><c>Msg.Options.FilterEmpty</c> while no page matches, else null.</summary>
    public string? FilterEmptyText
    {
        get => _filterEmptyText;
        private set => SetProperty(ref _filterEmptyText, value);
    }

    /// <summary>True while a pending value differs from its setting.</summary>
    public bool HasChanges => Colors.HasChanges || _fields.Values.Any(f => !SettingValues.AreEqual(f.Value, Read(f.Key)));

    public bool HasErrors => Colors.HasErrors || _fields.Values.Any(f => f.Error is not null);

    /// <exception cref="KeyNotFoundException">Unknown field id.</exception>
    public OptionField Field(string id) => _fields[id];

    /// <summary>Registers <c>tools.options</c> (optional argument: the page id to open, e.g. from status-bar field R5).</summary>
    public static void RegisterCommands(ICommandService commands, ISettingsService settings, ILayoutService layout, IDialogService dialogs,
        IThemeService theme, int implementedPhase)
    {
        ArgumentNullException.ThrowIfNull(commands);
        commands.RegisterHandler("tools.options", new OpenHandler(settings, layout, dialogs, theme, implementedPhase));
    }

    /// <summary>A row was selected: a group selects its first visible page (UI-DLG-090).</summary>
    public void SelectRow(OptionsTreeRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var page = row.Page ?? row.Children.FirstOrDefault(c => c.IsVisible)?.Page;
        if (page is not null && page.IsVisible)
            SelectedPage = page;
    }

    /// <summary>The installed monospaced font families (from the view; the toolkit's font list is a view concern).</summary>
    public void SetFontFamilies(IEnumerable<string> families)
    {
        ArgumentNullException.ThrowIfNull(families);
        var field = Field("FontFamily");
        var current = (string)field.Value;
        var names = families.Where(f => f.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (current.Length > 0 && !names.Contains(current, StringComparer.OrdinalIgnoreCase))
            names.Add(current);
        field.Choices.Clear();
        field.Choices.Add(new OptionChoice("", Strings.Options_DefaultChoice));
        foreach (var name in names.Order(StringComparer.CurrentCultureIgnoreCase))
        {
            field.Choices.Add(new OptionChoice(name, name));
        }
    }

    /// <summary><c>Apply</c>: the changed pending values in one settings batch, then a flush (UI-DLG-093).</summary>
    public async Task ApplyAsync()
    {
        if (HasErrors)
            return;
        var changed = _fields.Values.Where(f => !SettingValues.AreEqual(f.Value, Read(f.Key))).ToList();
        if (changed.Count == 0 && !Colors.HasChanges)
            return;
        using (_settings.BeginBatch())
        {
            foreach (var field in changed)
            {
                Write(field.Key, field.Value);
            }

            Colors.Apply();
        }

        Update();
        await _settings.FlushAsync();
    }

    private async Task OkAsync()
    {
        if (HasErrors)
            return;
        await ApplyAsync();
        Close(true);
    }

    private void Update()
    {
        foreach (var field in _fields.Values)
        {
            field.IsEnabled = field.Phase <= _implementedPhase && (field.EnabledBy is null || _fields[field.EnabledBy].Value is true);
        }

        Ok.IsEnabled = !HasErrors;
        Apply.IsEnabled = !HasErrors && HasChanges;
        OnPropertyChanged(nameof(HasChanges));
    }

    private void ApplyFilter()
    {
        var text = _filter.Trim();
        bool Matches(string s) => text.Length > 0 && s.Contains(text, StringComparison.CurrentCultureIgnoreCase);
        foreach (var page in Pages)
        {
            foreach (var field in page.Fields)
            {
                field.IsHighlighted = Matches(field.LabelText);
            }

            page.IsVisible = text.Length == 0 || Matches(page.Title) || Matches(page.TreeTitle) || page.Fields.Any(f => f.IsHighlighted)
                || page.OtherLabels.Any(l => Matches(l.Replace("_", "", StringComparison.Ordinal)));
        }

        foreach (var row in Rows)
        {
            row.IsVisible = row.Page?.IsVisible ?? row.Children.Any(c => c.Page!.IsVisible);
        }

        if (Pages.All(p => !p.IsVisible))
        {
            FilterEmptyText = string.Format(CultureInfo.CurrentCulture, Strings.Msg_Options_FilterEmpty, text);
            SelectedPage = null;
            return;
        }

        FilterEmptyText = null;
        if (SelectedPage is not { IsVisible: true })
            SelectedPage = Rows.First(r => r.Page is { IsVisible: true }).Page;
    }

    private object Read(ISettingKey key) => key switch
    {
        SettingKey<bool> b => _settings.Get(b),
        SettingKey<int> i => _settings.Get(i),
        SettingKey<string> s => _settings.Get(s),
        _ => throw new NotSupportedException(key.Name),
    };

    private void Write(ISettingKey key, object value)
    {
        switch (key)
        {
            case SettingKey<bool> b:
                _settings.Set(b, (bool)value);
                break;
            case SettingKey<int> i:
                _settings.Set(i, (int)value);
                break;
            case SettingKey<string> s:
                _settings.Set(s, (string)value);
                break;
            default:
                throw new NotSupportedException(key.Name);
        }
    }

    private static IReadOnlyList<OptionsTreeRow> BuildRows(IEnumerable<(string? Group, OptionsPage[] Pages)> groups)
    {
        var rows = new List<OptionsTreeRow>();
        foreach (var (group, pages) in groups)
        {
            if (group is null)
            {
                rows.AddRange(pages.Select(p => new OptionsTreeRow(p.Id, p.TreeTitle, 0, p, [])));
                continue;
            }

            var children = pages.Select(p => new OptionsTreeRow(p.Id, p.TreeTitle, 1, p, [])).ToList();
            rows.Add(new OptionsTreeRow(group, Resource("Options_Tree_" + group), 0, null, children));
            rows.AddRange(children);
        }

        return rows;
    }

    private static OptionsPage Page(string id, int phase, IReadOnlyList<OptionField> fields)
    {
        var key = id.Replace('.', '_');
        return new OptionsPage(id, Resource("Options_Page_" + key), Resource("Options_Tree_" + key), phase, fields);
    }

    private OptionField Check(string id, SettingKey<bool> key, int phase, string? heading = null) =>
        new(id, Resource("Options_Field_" + id), key, OptionFieldKind.Check, _settings.Get(key), phase, null) { GroupHeading = heading };

    private OptionField Integer(string id, SettingKey<int> key, int phase, Func<string> rangeMessage, string? enabledBy = null) =>
        new(id, Resource("Options_Field_" + id), key, OptionFieldKind.Integer, _settings.Get(key), phase, rangeMessage) { EnabledBy = enabledBy };

    private OptionField Choice(string id, SettingKey<string> key, int phase)
    {
        var field = new OptionField(id, Resource("Options_Field_" + id), key, OptionFieldKind.Choice, _settings.Get(key), phase, null);
        field.Choices.Add(new OptionChoice("", Strings.Options_DefaultChoice));
        if (_settings.Get(key) is { Length: > 0 } current)
            field.Choices.Add(new OptionChoice(current, current));
        return field;
    }

    private static Func<string> DelayRange(SettingKey<int> key) => () =>
        string.Format(CultureInfo.CurrentCulture, Strings.Msg_Options_DelayRange, key.Ranges[0].Minimum, key.Ranges[0].Maximum);

    private static string Resource(string name) =>
        Strings.ResourceManager.GetString(name, CultureInfo.CurrentCulture) ?? throw new InvalidOperationException($"Missing resource {name}.");

    private sealed class OpenHandler(ISettingsService settings, ILayoutService layout, IDialogService dialogs, IThemeService theme, int implementedPhase)
        : ICommandHandler
    {
        public bool CanExecute(string? argument) => true;

        public Task ExecuteAsync(string? argument) =>
            dialogs.ShowAsync(new OptionsViewModel(settings, layout, dialogs, theme.ActualTheme, argument, implementedPhase));
    }
}
