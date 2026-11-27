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
    public IReadOnlyList<string> OtherLabels { get; internal set; } = [];

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

    public const string CodeTemplatesPage = "Editor.CodeTemplates";

    /// <summary>
    /// UI-TW-1601: <c>Editor.CodeTemplates#&lt;abbreviation&gt;</c> opens the Code Templates page with that template selected;
    /// <c>Editor.CodeTemplates#+</c> opens it and performs <c>Templates.Add</c>.
    /// </summary>
    public const string TemplateSuffix = "#";

    /// <summary>The <see cref="TemplateSuffix"/> argument that adds a template.</summary>
    public const string NewTemplate = "+";
    public const string KeyMappingsPage = "KeyMappings";
    public const string EnvironmentGeneralPage = "Environment.General";
    public const string EnvironmentFilesPage = "Environment.Files";
    public const string EnvironmentTrustPage = "Environment.Trust";
    public const string ToolchainPathsPage = "ToolchainPaths";
    public const string DesignerPage = "Designer";

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
    private readonly IDialogService _dialogs;
    private readonly Dictionary<string, OptionField> _fields = new(StringComparer.Ordinal);
    private readonly HashSet<OptionField> _availableFields = [];
    private OptionsPage? _selectedPage;
    private string _filter = "";
    private string? _filterEmptyText;

    /// <param name="pageId">The page to open (a <c>tools.options</c> argument; <see cref="TemplateSuffix"/> also selects or adds a template); null = the page last shown.</param>
    /// <param name="implementedPhase">Pages and fields of later phases are shown disabled.</param>
    /// <param name="actualTheme">The theme in effect: the Colors page edits its set first.</param>
    public OptionsViewModel(ISettingsService settings, IKeymapService keymap, ILayoutService layout, IDialogService dialogs, FilePrompts prompts,
        ThemeKind actualTheme, string? pageId, int implementedPhase, Build.IToolchainLocator? toolchain = null, Platform.IFileSystem? fileSystem = null,
        Editing.CodeTemplateService? templates = null, FreexComp.Build.ITrustStore? trustStore = null)
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
        _dialogs = dialogs;
        string? template = null; // UI-TW-1601
        if (pageId?.IndexOf(TemplateSuffix, StringComparison.Ordinal) is int hash and >= 0)
            (pageId, template) = (pageId[..hash], pageId[(hash + 1)..]);

        var general = Page(EditorGeneralPage, 1,
        [
            Integer("TabWidth", SettingsCatalog.EditorTabWidth, 1, () => Strings.Msg_Options_TabWidthRange),
            Check("InsertSpaces", SettingsCatalog.EditorInsertSpaces, 1),
            Check("AutoIndent", SettingsCatalog.EditorAutoIndent, 1),
            Check("AutoCloseBrackets", SettingsCatalog.EditorAutoCloseBrackets, 1),
            Check("WrapSelection", SettingsCatalog.EditorWrapSelection, 8), // IDE-ED-028 (I-313)
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
            FixedChoice("LiveDiagnosticsSeverities", SettingsCatalog.EditorLiveDiagnosticsSeverities, 8, "LiveDiagnostics", // IDE-ED-150 (I-324)
                ("errors", Strings.Options_LiveSeverities_Errors), ("errorsAndWarnings", Strings.Options_LiveSeverities_ErrorsAndWarnings),
                ("all", Strings.Options_LiveSeverities_All)),
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
        CodeTemplates = new CodeTemplatesPageViewModel(templates); // dialogs.md §9.3 (I-119)
        var templatesPage = Page(CodeTemplatesPage, 3, [Check("ExpandOnTab", SettingsCatalog.EditorTemplatesExpandOnTab, 3)]);
        templatesPage.OtherLabels = CodeTemplatesFieldLabels;
        Colors = new ColorsPageViewModel(settings, dialogs, actualTheme);
        var colors = new OptionsPage(ColorsPage, Strings.Options_Page_Colors, Strings.Options_Tree_Colors, 1, []) { OtherLabels = ColorsPageViewModel.FieldLabels };
        KeyMappings = new KeyMappingsPageViewModel(keymap, settings, dialogs, implementedPhase);
        var keys = new OptionsPage(KeyMappingsPage, Strings.Options_Page_KeyMappings, Strings.Options_Tree_KeyMappings, 1, [])
        {
            OtherLabels = KeyMappingsPageViewModel.FieldLabels,
        };
        // dialogs.md §9.6 / §9.7: the Phase 1 fields (later groups join with their phases).
        var environmentGeneral = Page(EnvironmentGeneralPage, 1,
        [
            FixedChoice("Theme", SettingsCatalog.AppearanceTheme, 1,
                ("system", Strings.Options_Theme_System), ("light", Strings.Options_Theme_Light), ("dark", Strings.Options_Theme_Dark)),
            FixedChoice("Language", SettingsCatalog.EnvironmentLanguage, 8, ("en", Strings.Options_Language_English)), // disabled until P8+
            Check("ShowFullPath", SettingsCatalog.AppearanceShowFullPathInTitle, 1),
            Check("ReopenLastProject", SettingsCatalog.EnvironmentReopenLastProject, 1, Strings.Options_Group_Startup),
            Check("RestoreOpenFiles", SettingsCatalog.EnvironmentRestoreOpenFiles, 1),
            Check("ShowWelcome", SettingsCatalog.EnvironmentShowWelcomeOnStartup, 1),
            FolderPath("DefaultProjectsDir", SettingsCatalog.EnvironmentDefaultProjectsDir, 1, prompts.BuiltInProjectsDirectory),
            Check("AutoSwitchLayout", SettingsCatalog.LayoutAutoSwitch, 5), // D-109 (I-130 automatic layout switching)
            // phase8-build-options.md IDE-TGT-003 (I-227); the other fields of the group join with their own tasks.
            Integer("BuildJobs", SettingsCatalog.BuildJobs, 8, () => Strings.Options_Range_BuildJobs, heading: Strings.Options_Group_Build,
                hint: Strings.Options_Hint_BuildJobs),
            Check("ContinueAfterFailure", SettingsCatalog.BuildContinueAfterFailure, 8), // project-manager.md IDE-PM-172 (I-276)
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
            // local-history.md IDE-HIST-007 (I-331, F11-059): the three limits only while the history is on.
            Check("HistoryEnabled", SettingsCatalog.HistoryEnabled, 8),
            Integer("HistoryRevisions", SettingsCatalog.HistoryMaxRevisions, 8, HistoryRange("HistoryRevisions", SettingsCatalog.HistoryMaxRevisions),
                enabledBy: "HistoryEnabled"),
            Integer("HistoryDays", SettingsCatalog.HistoryMaxAgeDays, 8, HistoryRange("HistoryDays", SettingsCatalog.HistoryMaxAgeDays),
                enabledBy: "HistoryEnabled"),
            Integer("HistorySize", SettingsCatalog.HistoryMaxSizeMb, 8, HistoryRange("HistorySize", SettingsCatalog.HistoryMaxSizeMb),
                enabledBy: "HistoryEnabled"),
        ]);
        // dialogs.md §9.10 (I-268): a custom page over the trust list, read now (IDE-TRUST-014, 015).
        Trust = new TrustedLocationsPageViewModel(trustStore);
        var environmentTrust = new OptionsPage(EnvironmentTrustPage, Strings.Options_Page_EnvironmentTrust, Strings.Options_Tree_EnvironmentTrust, 8, [])
        {
            OtherLabels = TrustedLocationsPageViewModel.FieldLabels,
        };
        // dialogs.md §9.8 (I-132).
        var designer = Page(DesignerPage, 5,
        [
            Check("ShowGrid", SettingsCatalog.DesignerShowGrid, 5),
            Check("SnapToGrid", SettingsCatalog.DesignerSnapToGrid, 5),
            Integer("GridSizeX", SettingsCatalog.DesignerGridSizeX, 5, () => Strings.Msg_Options_GridRange),
            Integer("GridSizeY", SettingsCatalog.DesignerGridSizeY, 5, () => Strings.Msg_Options_GridRange),
            Check("UseGuidelines", SettingsCatalog.DesignerUseGuidelines, 5),
            Integer("GuidelineDistance", SettingsCatalog.DesignerGuidelineSnapDistance, 5, () => Strings.Msg_Options_GuidelineRange),
            Integer("GuidelineSpacing", SettingsCatalog.DesignerGuidelineMargin, 5, () => Strings.Msg_Options_GuidelineSpacingRange,
                enabledBy: "UseGuidelines"), // UI-DSG-133, DSG-SUR-212
            NumberChoice("HandleSize", SettingsCatalog.DesignerHandleSize, 5, 5, 7, 9, 11),
            Check("ShowCaptions", SettingsCatalog.DesignerShowComponentCaptions, 5),
            FixedChoice("DefaultView", SettingsCatalog.DesignerDefaultView, 5,
                ("design", Strings.Options_DefaultView_Design), ("code", Strings.Options_DefaultView_Code)),
            FixedChoice("Arrangement", SettingsCatalog.ObjectInspectorArrangement, 5,
                ("category", Strings.Options_Arrangement_Category), ("name", Strings.Options_Arrangement_Name)),
            Check("ShowDescription", SettingsCatalog.ObjectInspectorShowDescription, 5),
            FixedChoice("PaletteView", SettingsCatalog.ToolPaletteView, 5,
                ("list", Strings.Options_PaletteView_List), ("icons", Strings.Options_PaletteView_Icons)),
        ]);
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
        Pages = [general, display, templatesPage, colors, keys, environmentGeneral, environmentFiles, environmentTrust, designer, toolchainPage];
        foreach (var page in Pages)
        {
            // phase8-delivery.md IDE-P8-002/003: a phase-8 page or field counts as implemented once it is delivered.
            page.IsLaterPhase = !Composition.Phase8Features.IsAvailable("options:" + page.Id, page.Phase, implementedPhase);
            foreach (var field in page.Fields.Where(f => Composition.Phase8Features.IsAvailable("options:" + page.Id + "." + f.Id, f.Phase, implementedPhase)))
            {
                _availableFields.Add(field);
            }
        }

        Rows = BuildRows([("Editor", [general, display, templatesPage]), (null, [colors, keys]), ("Environment", [environmentGeneral, environmentFiles, environmentTrust]), (null, [designer, toolchainPage])]);
        Colors.PendingChanged += (_, _) => Update();
        KeyMappings.PendingChanged += (_, _) => Update();
        CodeTemplates.PendingChanged += (_, _) => Update();
        Trust.PendingChanged += (_, _) => Update();
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
        if (template == NewTemplate)
            CodeTemplates.AddTemplate();
        else if (template is not null && CodeTemplates.Rows.FirstOrDefault(r => r.Abbreviation.Equals(template, StringComparison.OrdinalIgnoreCase)) is { } row)
            CodeTemplates.Selected = row;
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

    /// <summary>The Trusted Locations page (dialogs.md §9.10).</summary>
    public TrustedLocationsPageViewModel Trust { get; }

    /// <summary>The Code Templates page (dialogs.md §9.3).</summary>
    public CodeTemplatesPageViewModel CodeTemplates { get; }

    private static IReadOnlyList<string> CodeTemplatesFieldLabels =>
    [
        Strings.Options_Templates_Templates, Strings.Options_Templates_Abbreviation, Strings.Options_Templates_Description, Strings.Options_Templates_Body,
        Strings.Options_Templates_Add, Strings.Options_Templates_Delete, Strings.Options_Templates_Reset,
    ];

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
    public bool HasChanges => Colors.HasChanges || KeyMappings.HasChanges || CodeTemplates.HasChanges || Trust.HasChanges || _fields.Values.Any(f => !SettingValues.AreEqual(f.Value, Read(f.Key)));

    public bool HasErrors => Colors.HasErrors || CodeTemplates.HasErrors || _fields.Values.Any(f => f.Error is not null);

    /// <exception cref="KeyNotFoundException">Unknown field id.</exception>
    public OptionField Field(string id) => _fields[id];

    /// <summary>
    /// Registers <c>tools.options</c> (optional argument: the page id to open, e.g. from status-bar field R5) and
    /// <c>tools.keyMappings</c> (opens the Key Mappings page, UI-DLG-092).
    /// </summary>
    public static void RegisterCommands(ICommandService commands, ISettingsService settings, IKeymapService keymap, ILayoutService layout,
        IDialogService dialogs, FilePrompts prompts, IThemeService theme, int implementedPhase, Build.IToolchainLocator? toolchain = null,
        Platform.IFileSystem? fileSystem = null, Editing.CodeTemplateService? templates = null, FreexComp.Build.ITrustStore? trustStore = null)
    {
        ArgumentNullException.ThrowIfNull(commands);
        commands.RegisterHandler("tools.options", new OpenHandler(settings, keymap, layout, dialogs, prompts, theme, implementedPhase, null, toolchain, fileSystem, templates, trustStore));
        commands.RegisterHandler("tools.keyMappings", new OpenHandler(settings, keymap, layout, dialogs, prompts, theme, implementedPhase, KeyMappingsPage, toolchain, fileSystem, templates, trustStore));
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
    public Task ApplyAsync() => ApplyChangesAsync();

    /// <summary>False when the trust list could not be written (IDE-TRUST-017): the dialog stays open.</summary>
    private async Task<bool> ApplyChangesAsync()
    {
        if (HasErrors)
            return false;
        var changed = _fields.Values.Where(f => !SettingValues.AreEqual(f.Value, Read(f.Key))).ToList();
        if (changed.Count == 0 && !Colors.HasChanges && !KeyMappings.HasChanges && !CodeTemplates.HasChanges && !Trust.HasChanges)
            return true;
        if (Trust.HasChanges)
        {
            try
            {
                Trust.Apply(); // IDE-TRUST-017: Store.Remove for each removed entry
            }
            catch (IOException e)
            {
                Update();
                await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Trust.SaveFailed", [e.Message]));
                return false;
            }
        }

        using (_settings.BeginBatch())
        {
            foreach (var field in changed)
            {
                Write(field.Key, field.Value);
            }

            Colors.Apply();
        }

        KeyMappings.Apply(); // key-map changes go through IKeymapService (UI-DLG-093)
        CodeTemplates.Apply(); // templates.json (IDE-SET-080)
        Update();
        await _settings.FlushAsync();
        await _keymap.FlushAsync();
        return true;
    }

    private async Task OkAsync()
    {
        if (HasErrors)
            return;
        if (await ApplyChangesAsync())
            Close(true);
    }

    private void Update()
    {
        foreach (var field in _fields.Values)
        {
            field.IsEnabled = _availableFields.Contains(field)
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
    private OptionField FixedChoice(string id, SettingKey<string> key, int phase, params (string Value, string Display)[] choices) =>
        FixedChoice(id, key, phase, null, choices);

    /// <summary>A combo with a fixed list of stored values, enabled only while the check box <paramref name="enabledBy"/> is checked.</summary>
    private OptionField FixedChoice(string id, SettingKey<string> key, int phase, string? enabledBy, params (string Value, string Display)[] choices)
    {
        var field = new OptionField(id, Resource("Options_Field_" + id), key, OptionFieldKind.Choice, _settings.Get(key), phase, null) { EnabledBy = enabledBy };
        foreach (var (value, display) in choices)
        {
            field.Choices.Add(new OptionChoice(value, display));
        }

        return field;
    }

    /// <summary>A combo of an <c>int</c> key offering <paramref name="values"/> (shown as their digits).</summary>
    private OptionField NumberChoice(string id, SettingKey<int> key, int phase, params int[] values)
    {
        var field = new OptionField(id, Resource("Options_Field_" + id), key, OptionFieldKind.Choice, _settings.Get(key), phase, null);
        foreach (var value in values)
        {
            var text = value.ToString(CultureInfo.InvariantCulture);
            field.Choices.Add(new OptionChoice(text, text));
        }

        return field;
    }

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

    private OptionField Integer(string id, SettingKey<int> key, int phase, Func<string> rangeMessage, string? enabledBy = null, string? heading = null,
        string? hint = null) =>
        new(id, Resource("Options_Field_" + id), key, OptionFieldKind.Integer, _settings.Get(key), phase, rangeMessage)
        {
            EnabledBy = enabledBy, GroupHeading = heading, Hint = hint,
        };

    private OptionField Choice(string id, SettingKey<string> key, int phase)
    {
        var field = new OptionField(id, Resource("Options_Field_" + id), key, OptionFieldKind.Choice, _settings.Get(key), phase, null);
        field.Choices.Add(new OptionChoice("", Strings.Options_DefaultChoice));
        if (_settings.Get(key) is { Length: > 0 } current)
            field.Choices.Add(new OptionChoice(current, current));
        return field;
    }

    /// <summary><c>Msg.Options.HistoryRange</c>: the field's label (without access key and colon) and the key's range.</summary>
    private static Func<string> HistoryRange(string fieldId, SettingKey<int> key) => () =>
        string.Format(CultureInfo.CurrentCulture, Strings.Msg_Options_HistoryRange, Resource("Options_Field_" + fieldId).Replace("_", "", StringComparison.Ordinal).TrimEnd(':'),
            key.Ranges[0].Minimum, key.Ranges[0].Maximum);

    private static Func<string> DelayRange(SettingKey<int> key) => () =>
        string.Format(CultureInfo.CurrentCulture, Strings.Msg_Options_DelayRange, key.Ranges[0].Minimum, key.Ranges[0].Maximum);

    private static string Resource(string name) =>
        Strings.ResourceManager.GetString(name, CultureInfo.CurrentCulture) ?? throw new InvalidOperationException($"Missing resource {name}.");

    private sealed class OpenHandler(ISettingsService settings, IKeymapService keymap, ILayoutService layout, IDialogService dialogs, FilePrompts prompts,
        IThemeService theme, int implementedPhase, string? fixedPage, Build.IToolchainLocator? toolchain, Platform.IFileSystem? fileSystem, Editing.CodeTemplateService? templates,
        FreexComp.Build.ITrustStore? trustStore) : ICommandHandler
    {
        public bool CanExecute(string? argument) => true;

        public Task ExecuteAsync(string? argument) =>
            dialogs.ShowAsync(new OptionsViewModel(settings, keymap, layout, dialogs, prompts, theme.ActualTheme, fixedPage ?? argument, implementedPhase,
                toolchain, fileSystem, templates, trustStore));
    }
}
