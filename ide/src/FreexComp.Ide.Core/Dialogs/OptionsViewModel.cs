using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Layout;

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
    public const string KeyMappingsPage = "KeyMappings";
    public const string EnvironmentGeneralPage = "Environment.General";
    public const string EnvironmentFilesPage = "Environment.Files";
    public const string ToolchainPathsPage = "ToolchainPaths";

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
    private readonly IKeymapService _keymap;
    private readonly FilePrompts _prompts;
    private readonly int _implementedPhase;
    private readonly Dictionary<string, OptionField> _fields = new(StringComparer.Ordinal);
    private OptionsPage? _selectedPage;
    private string _filter = "";
    private string? _filterEmptyText;

    /// <param name="pageId">The page to open (a <c>tools.options</c> argument); null = the page last shown.</param>
    /// <param name="implementedPhase">Pages and fields of later phases are shown disabled.</param>
    /// <param name="actualTheme">The theme in effect: the Colors page edits its set first.</param>
    public OptionsViewModel(ISettingsService settings, IKeymapService keymap, ILayoutService layout, IDialogService dialogs, FilePrompts prompts,
        ThemeKind actualTheme, string? pageId, int implementedPhase, Build.IToolchainLocator? toolchain = null, Platform.IFileSystem? fileSystem = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(keymap);
        ArgumentNullException.ThrowIfNull(prompts);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(dialogs);
        _settings = settings;
        _keymap = keymap;
        _prompts = prompts;
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
            Check("CompletionAuto", SettingsCatalog.EditorCompletionAutoTrigger, 3, Strings.Options_Group_CodeAssistance),
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
        KeyMappings = new KeyMappingsPageViewModel(keymap, dialogs, implementedPhase);
        var keys = new OptionsPage(KeyMappingsPage, Strings.Options_Page_KeyMappings, Strings.Options_Tree_KeyMappings, 1, [])
        {
            OtherLabels = KeyMappingsPageViewModel.FieldLabels,
        };
        // dialogs.md §9.6 / §9.7: the Phase 1 fields (later groups join with their phases).
        var environmentGeneral = Page(EnvironmentGeneralPage, 1,
        [
            FixedChoice("Theme", SettingsCatalog.AppearanceTheme, 1,
                ("system", Strings.Options_Theme_System), ("light", Strings.Options_Theme_Light), ("dark", Strings.Options_Theme_Dark)),
            Check("ShowFullPath", SettingsCatalog.AppearanceShowFullPathInTitle, 1),
            Check("ReopenLastProject", SettingsCatalog.EnvironmentReopenLastProject, 1, Strings.Options_Group_Startup),
            Check("RestoreOpenFiles", SettingsCatalog.EnvironmentRestoreOpenFiles, 1),
            Check("ShowWelcome", SettingsCatalog.EnvironmentShowWelcomeOnStartup, 1),
            FolderPath("DefaultProjectsDir", SettingsCatalog.EnvironmentDefaultProjectsDir, 1, prompts.BuiltInProjectsDirectory),
        ]);
        var environmentFiles = Page(EnvironmentFilesPage, 1,
        [
            FixedChoice("DefaultEncoding", SettingsCatalog.FilesDefaultEncoding, 1,
                [.. new[] { "utf-8", "utf-8-bom", "utf-16le", "utf-16be", "windows-1252" }.Select(v => (v, Resource("Encoding_" + v.Replace('-', '_'))))]),
            FixedChoice("DefaultLineEnding", SettingsCatalog.FilesDefaultLineEnding, 1,
                ("crlf", Strings.Options_LineEnding_CrLf), ("lf", Strings.Options_LineEnding_Lf)),
            Check("TrimTrailing", SettingsCatalog.FilesTrimTrailingWhitespaceOnSave, 1),
            Check("FinalNewline", SettingsCatalog.FilesEnsureFinalNewline, 1),
            Check("DetectExternal", SettingsCatalog.FilesDetectExternalChanges, 1),
            Check("AutoReload", SettingsCatalog.EnvironmentAutoReloadUnmodified, 1, enabledBy: "DetectExternal"),
            Integer("RecoveryInterval", SettingsCatalog.EnvironmentRecoveryIntervalSeconds, 1, () => Strings.Msg_Options_RecoveryRange),
            Integer("LargeFile", SettingsCatalog.FilesLargeFileThresholdMb, 1, () => Strings.Msg_Options_LargeFileRange),
        ]);
        var designer = Page("Designer", 5, []);
        // dialogs.md §9.9 (I-093): the effective folder of each field is checked; the placeholder names the automatic one.
        var automatic = toolchain?.ResolveAutomatic();
        var toolchainPage = Page(ToolchainPathsPage, 3,
        [
            ToolchainFolder("LlvmBinDir", SettingsCatalog.ToolchainLlvmBinDir, 3, automatic?.LlvmBinDir, Build.ToolchainLocator.LlvmFiles, fileSystem,
                Strings.Options_Toolchain_Intro),
            ToolchainFolder("RtlDir", SettingsCatalog.ToolchainRtlDir, 3, automatic?.RtlDir, Build.ToolchainLocator.RtlFiles, fileSystem),
            ToolchainFolder("FrameworkDir", SettingsCatalog.ToolchainFrameworkDir, 5, automatic?.FrameworkDir, Build.ToolchainLocator.FrameworkFiles,
                fileSystem),
        ]);
        Pages = [general, display, templates, colors, keys, environmentGeneral, environmentFiles, designer, toolchainPage];
        foreach (var page in Pages)
        {
            page.IsLaterPhase = page.Phase > implementedPhase;
        }

        Rows = BuildRows([("Editor", [general, display, templates]), (null, [colors, keys]), ("Environment", [environmentGeneral, environmentFiles]), (null, [designer, toolchainPage])]);
        Colors.PendingChanged += (_, _) => Update();
        KeyMappings.PendingChanged += (_, _) => Update();
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

    /// <summary>The Key Mappings page (dialogs.md §9.5).</summary>
    public KeyMappingsPageViewModel KeyMappings { get; }

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
    public bool HasChanges => Colors.HasChanges || KeyMappings.HasChanges || _fields.Values.Any(f => !SettingValues.AreEqual(f.Value, Read(f.Key)));

    public bool HasErrors => Colors.HasErrors || _fields.Values.Any(f => f.Error is not null);

    /// <exception cref="KeyNotFoundException">Unknown field id.</exception>
    public OptionField Field(string id) => _fields[id];

    /// <summary>
    /// Registers <c>tools.options</c> (optional argument: the page id to open, e.g. from status-bar field R5) and
    /// <c>tools.keyMappings</c> (opens the Key Mappings page, UI-DLG-092).
    /// </summary>
    public static void RegisterCommands(ICommandService commands, ISettingsService settings, IKeymapService keymap, ILayoutService layout,
        IDialogService dialogs, FilePrompts prompts, IThemeService theme, int implementedPhase, Build.IToolchainLocator? toolchain = null,
        Platform.IFileSystem? fileSystem = null)
    {
        ArgumentNullException.ThrowIfNull(commands);
        commands.RegisterHandler("tools.options", new OpenHandler(settings, keymap, layout, dialogs, prompts, theme, implementedPhase, null, toolchain, fileSystem));
        commands.RegisterHandler("tools.keyMappings", new OpenHandler(settings, keymap, layout, dialogs, prompts, theme, implementedPhase, KeyMappingsPage, toolchain, fileSystem));
    }

    /// <summary>A row was selected: a group selects its first visible page (UI-DLG-090).</summary>
    public void SelectRow(OptionsTreeRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var page = row.Page ?? row.Children.FirstOrDefault(c => c.IsVisible)?.Page;
        if (page is not null && page.IsVisible)
            SelectedPage = page;
    }

    /// <summary>A path field's Browse button: the folder picker (<c>folder.&lt;fieldId&gt;</c>); a picked folder becomes the value.</summary>
    public async Task BrowseAsync(OptionField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (await _prompts.PickFolderAsync(field.Id, (string)field.Value) is { } folder)
            field.Value = folder;
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
        if (changed.Count == 0 && !Colors.HasChanges && !KeyMappings.HasChanges)
            return;
        using (_settings.BeginBatch())
        {
            foreach (var field in changed)
            {
                Write(field.Key, field.Value);
            }

            Colors.Apply();
        }

        KeyMappings.Apply(); // key-map changes go through IKeymapService (UI-DLG-093)
        Update();
        await _settings.FlushAsync();
        await _keymap.FlushAsync();
    }

    private async Task OkAsync()
    {
        if (HasErrors)
            return;
        await ApplyAsync();
        Close(true);
    }

    /// <summary>
    /// Phase 3 settings whose features are still open tasks (I-111 live diagnostics, I-114 completion, I-116 parameter info
    /// and hover, I-120 block completion): shown disabled like later-phase fields until those tasks land.
    /// </summary>
    public static readonly IReadOnlySet<string> PendingFeatureKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        Settings.SettingsCatalog.EditorAutoCloseBeginEnd.Name, Settings.SettingsCatalog.EditorCompletionAutoTrigger.Name,
        Settings.SettingsCatalog.EditorCompletionDelayMs.Name, Settings.SettingsCatalog.EditorParameterInfoAutoTrigger.Name,
        Settings.SettingsCatalog.EditorHoverEnabled.Name, Settings.SettingsCatalog.EditorHoverDelayMs.Name,
        Settings.SettingsCatalog.EditorLiveDiagnostics.Name, Settings.SettingsCatalog.EditorLiveDiagnosticsDelayMs.Name,
    };

    private void Update()
    {
        foreach (var field in _fields.Values)
        {
            field.IsEnabled = field.Phase <= _implementedPhase && !PendingFeatureKeys.Contains(field.Key.Name)
                && (field.EnabledBy is null || _fields[field.EnabledBy].Value is true);
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

    private OptionField Check(string id, SettingKey<bool> key, int phase, string? heading = null, string? enabledBy = null) =>
        new(id, Resource("Options_Field_" + id), key, OptionFieldKind.Check, _settings.Get(key), phase, null) { GroupHeading = heading, EnabledBy = enabledBy };

    /// <summary>A combo with a fixed list of stored values and their display texts.</summary>
    private OptionField FixedChoice(string id, SettingKey<string> key, int phase, params (string Value, string Display)[] choices)
    {
        var field = new OptionField(id, Resource("Options_Field_" + id), key, OptionFieldKind.Choice, _settings.Get(key), phase, null);
        foreach (var (value, display) in choices)
        {
            field.Choices.Add(new OptionChoice(value, display));
        }

        return field;
    }

    /// <summary>A folder field: empty or an absolute valid path (else <c>Msg.Project.LocationInvalid</c>), with Browse.</summary>
    /// <summary>
    /// A toolchain folder (dialogs.md §9.9): placeholder "(automatic: &lt;path or not found&gt;)"; an explicit folder must hold
    /// <paramref name="files"/>; an empty one reports the automatic result. Without a file system nothing is checked.
    /// </summary>
    private OptionField ToolchainFolder(string id, SettingKey<string> key, int phase, string? automatic, IReadOnlyList<string> files,
        Platform.IFileSystem? fileSystem, string? heading = null) =>
        new(id, Resource("Options_Field_" + id), key, OptionFieldKind.Path, _settings.Get(key), phase, () => Strings.Msg_Options_PathNotFound)
        {
            GroupHeading = heading,
            Placeholder = string.Format(CultureInfo.CurrentCulture, Strings.Options_Toolchain_Automatic, automatic ?? Strings.Options_Toolchain_NotFound),
            FolderCheck = fileSystem is null ? null
                : text => text.Length > 0 ? Build.ToolchainLocator.ValidateFolder(fileSystem, text, files)
                : automatic is null ? null
                : Build.ToolchainLocator.ValidateFolder(fileSystem, automatic, files),
        };

    private OptionField FolderPath(string id, SettingKey<string> key, int phase, string placeholder) =>
        new(id, Resource("Options_Field_" + id), key, OptionFieldKind.Path, _settings.Get(key), phase, () => Strings.Msg_Project_LocationInvalid)
        {
            Placeholder = placeholder,
            TextValidator = text => text.Length == 0 || Projects.NewProjectValidation.NormalizeLocation(text) is not null,
        };

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

    private sealed class OpenHandler(ISettingsService settings, IKeymapService keymap, ILayoutService layout, IDialogService dialogs, FilePrompts prompts,
        IThemeService theme, int implementedPhase, string? fixedPage, Build.IToolchainLocator? toolchain, Platform.IFileSystem? fileSystem) : ICommandHandler
    {
        public bool CanExecute(string? argument) => true;

        public Task ExecuteAsync(string? argument) =>
            dialogs.ShowAsync(new OptionsViewModel(settings, keymap, layout, dialogs, prompts, theme.ActualTheme, fixedPage ?? argument, implementedPhase,
                toolchain, fileSystem));
    }
}
