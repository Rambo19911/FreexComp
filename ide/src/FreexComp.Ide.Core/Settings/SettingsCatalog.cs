using static FreexComp.Ide.Core.Settings.SettingKey;

namespace FreexComp.Ide.Core.Settings;

/// <summary>
/// Every setting of settings.md §4 (IDE-SET-030), including keys of later phases (no consumer yet).
/// Code reads settings through these fields only (IDE-SET-031). The per-token color keys of §4.3 are
/// generated from <see cref="EditorColorTokens"/> and reached through <see cref="ColorKey"/> / <see cref="StyleKey"/>.
/// </summary>
public static class SettingsCatalog
{
    // 4.1 Appearance
    public static readonly SettingKey<string> AppearanceTheme = Enum("appearance.theme", "system", "system", "light", "dark");
    public static readonly SettingKey<bool> AppearanceShowStatusBar = Bool("appearance.showStatusBar", true);
    public static readonly SettingKey<bool> AppearanceToolbarStandard = Bool("appearance.toolbar.standard", true);
    public static readonly SettingKey<bool> AppearanceToolbarBuild = Bool("appearance.toolbar.build", true);
    public static readonly SettingKey<bool> AppearanceToolbarDebug = Bool("appearance.toolbar.debug", true);
    public static readonly SettingKey<bool> AppearanceToolbarDesigner = Bool("appearance.toolbar.designer", true);
    public static readonly SettingKey<bool> AppearanceShowFullPathInTitle = Bool("appearance.showFullPathInTitle", false);

    // 4.2 Editor
    public static readonly SettingKey<string> EditorFontFamily = String("editor.fontFamily", "");
    public static readonly SettingKey<int> EditorFontSize = Int("editor.fontSize", 13, new SettingRange(6, 72));
    public static readonly SettingKey<bool> EditorCaretBlink = Bool("editor.caretBlink", true);
    public static readonly SettingKey<int> EditorRightMargin = Int("editor.rightMargin", 120, new SettingRange(0, 400));
    public static readonly SettingKey<bool> EditorShowLineNumbers = Bool("editor.showLineNumbers", true);
    public static readonly SettingKey<bool> EditorShowFoldMargin = Bool("editor.showFoldMargin", true);
    public static readonly SettingKey<bool> EditorShowChangeBar = Bool("editor.showChangeBar", true);
    public static readonly SettingKey<bool> EditorHighlightCurrentLine = Bool("editor.highlightCurrentLine", true);
    public static readonly SettingKey<bool> EditorHighlightMatchingBrackets = Bool("editor.highlightMatchingBrackets", true);
    public static readonly SettingKey<bool> EditorHighlightWordOccurrences = Bool("editor.highlightWordOccurrences", true);
    public static readonly SettingKey<bool> EditorShowIndentGuides = Bool("editor.showIndentGuides", true);
    public static readonly SettingKey<bool> EditorShowWhitespace = Bool("editor.showWhitespace", false);
    public static readonly SettingKey<bool> EditorWordWrap = Bool("editor.wordWrap", false);
    public static readonly SettingKey<int> EditorTabWidth = Int("editor.tabWidth", 2, new SettingRange(1, 16));
    public static readonly SettingKey<bool> EditorInsertSpaces = Bool("editor.insertSpaces", true);
    public static readonly SettingKey<bool> EditorAutoIndent = Bool("editor.autoIndent", true);
    public static readonly SettingKey<bool> EditorAutoCloseBrackets = Bool("editor.autoCloseBrackets", true);
    public static readonly SettingKey<bool> EditorWrapSelection = Bool("editor.wrapSelection", true); // IDE-ED-028 (P8, F11-047)
    public static readonly SettingKey<bool> EditorAutoCloseBeginEnd = Bool("editor.autoCloseBeginEnd", true);
    public static readonly SettingKey<bool> EditorSmartHome = Bool("editor.smartHome", true);
    public static readonly SettingKey<bool> EditorCaretPastEndOfLine = Bool("editor.caretPastEndOfLine", false);
    public static readonly SettingKey<bool> EditorRectangularSelection = Bool("editor.rectangularSelection", true);
    public static readonly SettingKey<bool> EditorScrollPastEnd = Bool("editor.scrollPastEnd", false);
    public static readonly SettingKey<bool> EditorMouseWheelZoom = Bool("editor.mouseWheelZoom", true);
    public static readonly SettingKey<bool> EditorCompletionAutoTrigger = Bool("editor.completion.autoTrigger", true);
    public static readonly SettingKey<int> EditorCompletionDelayMs = Int("editor.completion.delayMs", 250, new SettingRange(0, 2000));
    public static readonly SettingKey<bool> EditorParameterInfoAutoTrigger = Bool("editor.parameterInfo.autoTrigger", true);
    public static readonly SettingKey<bool> EditorHoverEnabled = Bool("editor.hover.enabled", true);
    public static readonly SettingKey<int> EditorHoverDelayMs = Int("editor.hover.delayMs", 500, new SettingRange(100, 3000));
    public static readonly SettingKey<bool> EditorLiveDiagnostics = Bool("editor.liveDiagnostics", true);
    public static readonly SettingKey<int> EditorLiveDiagnosticsDelayMs = Int("editor.liveDiagnostics.delayMs", 500, new SettingRange(100, 5000));
    public static readonly SettingKey<bool> EditorTemplatesExpandOnTab = Bool("editor.templates.expandOnTab", true);

    // 4.4 Environment and files
    public static readonly SettingKey<bool> EnvironmentReopenLastProject = Bool("environment.reopenLastProject", true);
    public static readonly SettingKey<bool> EnvironmentRestoreOpenFiles = Bool("environment.restoreOpenFiles", true);
    public static readonly SettingKey<bool> EnvironmentShowWelcomeOnStartup = Bool("environment.showWelcomeOnStartup", true);
    public static readonly SettingKey<string> EnvironmentDefaultProjectsDir = String("environment.defaultProjectsDir", "");
    public static readonly SettingKey<string> EnvironmentAutoSaveBeforeBuild = Enum("environment.autoSaveBeforeBuild", "all", "all", "prompt", "none");
    public static readonly SettingKey<bool> EnvironmentAutoReloadUnmodified = Bool("environment.autoReloadUnmodified", true);
    public static readonly SettingKey<int> EnvironmentRecoveryIntervalSeconds =
        Int("environment.recoveryIntervalSeconds", 30, new SettingRange(0, 0), new SettingRange(5, 600));
    public static readonly SettingKey<string> EnvironmentLanguage = String("environment.language", "en");
    public static readonly SettingKey<bool> LayoutAutoSwitch = Bool("layout.autoSwitch", true);
    public static readonly SettingKey<string> KeyboardPreset = Enum("keyboard.preset", "default", "default", "classic"); // phase8-ui-additions.md D3
    public static readonly SettingKey<string> FilesDefaultEncoding =
        Enum("files.defaultEncoding", "utf-8", "utf-8", "utf-8-bom", "utf-16le", "utf-16be", "windows-1252");
    public static readonly SettingKey<string> FilesDefaultLineEnding = Enum("files.defaultLineEnding", "crlf", "crlf", "lf");
    public static readonly SettingKey<bool> FilesTrimTrailingWhitespaceOnSave = Bool("files.trimTrailingWhitespaceOnSave", false);
    public static readonly SettingKey<bool> FilesEnsureFinalNewline = Bool("files.ensureFinalNewline", true);
    public static readonly SettingKey<bool> FilesDetectExternalChanges = Bool("files.detectExternalChanges", true);
    public static readonly SettingKey<int> FilesLargeFileThresholdMb = Int("files.largeFileThresholdMb", 10, new SettingRange(1, 200));

    // 4.5 Build, run, tool windows
    public static readonly SettingKey<string> ToolchainLlvmBinDir = String("toolchain.llvmBinDir", "");
    public static readonly SettingKey<string> ToolchainRtlDir = String("toolchain.rtlDir", "");
    public static readonly SettingKey<string> ToolchainFrameworkDir = String("toolchain.frameworkDir", "");
    public static readonly SettingKey<string> ToolchainFrameworkDesignDir = String("toolchain.frameworkDesignDir", "");
    public static readonly SettingKey<string> ToolchainDataDesignDir = String("toolchain.dataDesignDir", "");
    public static readonly SettingKey<bool> BuildShowCommandLines = Bool("build.showCommandLines", false);
    public static readonly SettingKey<int> BuildJobs = Int("build.jobs", 0, new SettingRange(0, 256)); // IDE-TGT-001 (P8, F01-010): 0 = automatic
    public static readonly SettingKey<bool> BuildContinueAfterFailure = Bool("build.continueAfterFailure", false); // IDE-PM-172 (P8, F09-009)
    public static readonly SettingKey<string> RunConsoleMode = Enum("run.consoleMode", "external", "external", "output");
    public static readonly SettingKey<bool> MessagesShowErrors = Bool("messages.showErrors", true);
    public static readonly SettingKey<bool> MessagesShowWarnings = Bool("messages.showWarnings", true);
    public static readonly SettingKey<bool> MessagesShowHints = Bool("messages.showHints", true);
    public static readonly SettingKey<bool> MessagesAutoShowOnError = Bool("messages.autoShowOnError", true);
    public static readonly SettingKey<int> OutputMaxLines = Int("output.maxLines", 10000, new SettingRange(1000, 1000000));
    public static readonly SettingKey<bool> OutputWordWrap = Bool("output.wordWrap", false);
    public static readonly SettingKey<bool> OutputClearOnBuild = Bool("output.clearOnBuild", true);
    public static readonly SettingKey<bool> OutputClearOnRun = Bool("output.clearOnRun", true);
    public static readonly SettingKey<bool> OutputAutoShowOnRun = Bool("output.autoShowOnRun", true);
    public static readonly SettingKey<bool> DebuggerBreakOnExceptions = Bool("debugger.breakOnExceptions", true);
    public static readonly SettingKey<IReadOnlyList<string>> DebuggerIgnoredExceptions = StringArray("debugger.ignoredExceptions", "EAbort");
    public static readonly SettingKey<bool> DebuggerEvaluateTooltips = Bool("debugger.evaluateTooltips", true);

    // 4.6 Designer (P5)
    public static readonly SettingKey<int> DesignerGridSizeX = Int("designer.gridSizeX", 8, new SettingRange(2, 64));
    public static readonly SettingKey<int> DesignerGridSizeY = Int("designer.gridSizeY", 8, new SettingRange(2, 64));
    public static readonly SettingKey<bool> DesignerShowGrid = Bool("designer.showGrid", true);
    public static readonly SettingKey<bool> DesignerSnapToGrid = Bool("designer.snapToGrid", true);
    public static readonly SettingKey<bool> DesignerUseGuidelines = Bool("designer.useGuidelines", true);
    public static readonly SettingKey<int> DesignerGuidelineSnapDistance = Int("designer.guidelineSnapDistance", 4, new SettingRange(1, 16));
    public static readonly SettingKey<bool> DesignerShowComponentCaptions = Bool("designer.showComponentCaptions", true);
    public static readonly SettingKey<int> DesignerHandleSize =
        Int("designer.handleSize", 7, new SettingRange(5, 5), new SettingRange(7, 7), new SettingRange(9, 9), new SettingRange(11, 11));
    public static readonly SettingKey<string> DesignerDefaultView = Enum("designer.defaultView", "design", "design", "code");
    public static readonly SettingKey<string> ObjectInspectorArrangement = Enum("objectInspector.arrangement", "category", "category", "name");
    public static readonly SettingKey<bool> ObjectInspectorShowDescription = Bool("objectInspector.showDescription", true);
    public static readonly SettingKey<string> ToolPaletteView = Enum("toolPalette.view", "list", "list", "icons");
    public static readonly SettingKey<IReadOnlyList<string>> ToolPaletteCollapsedPages = StringArray("toolPalette.collapsedPages");
    public static readonly SettingKey<int> DesignerGuidelineMargin = Int("designer.guidelineMargin", 8, new SettingRange(0, 64));
    public static readonly SettingKey<bool> DesignerLockControls = Bool("designer.lockControls", false);
    public static readonly SettingKey<bool> DesignerRemoveEmptyHandlersOnSave = Bool("designer.removeEmptyHandlersOnSave", true);
    public static readonly SettingKey<double> ObjectInspectorNameColumnRatio =
        Number("objectInspector.nameColumnRatio", 0.45, new SettingRange(0.2, 0.8));

    /// <summary>
    /// The <c>syntax.*</c> and <c>editor.*</c> tokens of design-tokens.md §3.1–§3.2 with their Light/Dark values;
    /// <c>Style</c> (§3.1 only; <c>semi-bold</c> → <c>bold</c>) is null for decoration tokens.
    /// </summary>
    public static readonly IReadOnlyList<(string Token, string Light, string Dark, string? Style)> EditorColorTokens =
    [
        ("syntax.text", "#1C211E", "#E4E8E5", "regular"),
        ("syntax.keyword", "#0B5C8A", "#6CB6F0", "bold"),
        ("syntax.string", "#A0431A", "#E8A27C", "regular"),
        ("syntax.char", "#A82A64", "#F08FB8", "regular"),
        ("syntax.number", "#6F45A8", "#C3A6F0", "regular"),
        ("syntax.comment", "#5E6E63", "#8A9B90", "italic"),
        ("syntax.directive", "#8A5A00", "#E5B454", "regular"),
        ("syntax.symbol", "#3B4440", "#C3CBC6", "regular"),
        ("syntax.type", "#0C6B5E", "#5AD0BE", "regular"),
        ("syntax.constant", "#6F45A8", "#C3A6F0", "regular"),
        ("syntax.routine", "#1C211E", "#E4E8E5", "regular"),
        ("syntax.label", "#8A5A00", "#E5B454", "regular"),
        ("syntax.asm", "#515A54", "#A9B2AC", "regular"),
        ("syntax.attribute", "#6B4FA0", "#B9A3E3", "regular"),
        ("syntax.invalid", "#B3261E", "#F2867E", "regular"),
        ("syntax.inactive", "#6F7972", "#838E87", "regular"),
        ("editor.currentLine.bg", "#F2F6F4", "#1E2321", null),
        ("editor.currentLine.border", "#DFE2DD", "#2D3330", null),
        ("editor.selection.bg", "#BFE3DC", "#245A52", null),
        ("editor.selection.inactive.bg", "#E1E4DF", "#333A37", null),
        ("editor.caret", "#1C211E", "#E4E8E5", null),
        ("editor.lineNumber", "#737D76", "#838E87", null),
        ("editor.lineNumber.current", "#1C211E", "#E4E8E5", null),
        ("editor.bracketMatch.bg", "#E3F2EF", "#1A3632", null),
        ("editor.bracketMatch.border", "#0F7B6C", "#3FC1AE", null),
        ("editor.wordHighlight.bg", "#EDEFEA", "#2A302D", null),
        ("editor.findMatch.bg", "#FFE9A8", "#5A4A12", null),
        ("editor.findMatch.current.bg", "#FFCB57", "#8A6D10", null),
        ("editor.indentGuide", "#E4E8E3", "#2A302D", null),
        ("editor.indentGuide.active", "#B4BAB3", "#4A534E", null),
        ("editor.whitespace", "#C9CEC7", "#3A423E", null),
        ("editor.foldMarker", "#737D76", "#838E87", null),
        ("editor.foldedText.bg", "#ECEEEB", "#262B29", null),
        ("editor.changeBar.modified", "#C28A00", "#E5B454", null),
        ("editor.changeBar.saved", "#2E7D32", "#7CC883", null),
        ("editor.breakpoint", "#C23A31", "#E8695F", null),
        ("editor.breakpoint.disabled", "#9AA29A", "#66706A", null),
        ("editor.breakpoint.line.bg", "#FBE9E7", "#3D1F1C", null),
        ("editor.glyph.onFill", "#FFFFFF", "#FFFFFF", null),
        ("editor.execution", "#C28A00", "#E5B454", null),
        ("editor.execution.line.bg", "#FFF1C2", "#4A3C12", null),
        ("editor.callFrame.line.bg", "#E7F4E8", "#1C3320", null),
        ("editor.bookmark", "#1E5FA8", "#7FB4EC", null),
        ("editor.squiggle.error", "#B3261E", "#F2867E", null),
        ("editor.squiggle.warning", "#8A5A00", "#E5B454", null),
        ("editor.squiggle.hint", "#6B4FA0", "#B9A3E3", null),
        ("editor.ruler", "#ECEEEB", "#262B29", null),
    ];

    private static readonly string[] Themes = ["light", "dark"];
    private static readonly string[] Styles = ["regular", "bold", "italic", "bolditalic"];

    private static readonly Dictionary<string, SettingKey<string>> ColorKeys = BuildColorKeys();

    /// <summary>Every key: the fields above, then the generated color and style keys.</summary>
    public static readonly IReadOnlyList<ISettingKey> All =
    [
        .. typeof(SettingsCatalog).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => typeof(ISettingKey).IsAssignableFrom(f.FieldType))
            .Select(f => (ISettingKey)f.GetValue(null)!),
        .. ColorKeys.Values,
    ];

    /// <summary><c>colors.&lt;theme&gt;.&lt;token&gt;</c>; <paramref name="theme"/> is <c>light</c> or <c>dark</c>.</summary>
    public static SettingKey<string> ColorKey(string theme, string token) => ColorKeys[$"colors.{theme}.{token}"];

    /// <summary><c>colors.&lt;theme&gt;.&lt;syntaxToken&gt;.style</c>.</summary>
    public static SettingKey<string> StyleKey(string theme, string syntaxToken) => ColorKeys[$"colors.{theme}.{syntaxToken}.style"];

    private static Dictionary<string, SettingKey<string>> BuildColorKeys()
    {
        var keys = new Dictionary<string, SettingKey<string>>(StringComparer.Ordinal);
        foreach (var theme in Themes)
        {
            foreach (var (token, light, dark, style) in EditorColorTokens)
            {
                var color = Color($"colors.{theme}.{token}", theme == "light" ? light : dark);
                keys.Add(color.Name, color);
                if (style is not null)
                {
                    var styleKey = Enum($"colors.{theme}.{token}.style", style, Styles);
                    keys.Add(styleKey.Name, styleKey);
                }
            }
        }

        return keys;
    }
}
