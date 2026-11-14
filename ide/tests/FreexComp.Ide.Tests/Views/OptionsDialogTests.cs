using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Editor;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.Views;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The IDE Options dialog frame and the Editor › General / Editor › Display pages (dialogs.md §9, §9.1, §9.2).</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class OptionsDialogTests
{
    /// <summary>dialogs.md §9.1: field id, label (mnemonic as <c>_</c>), key, enabled in P1.</summary>
    private static readonly (string Field, string Label, ISettingKey Key, bool Enabled)[] GeneralFields =
    [
        ("TabWidth", "_Tab width:", SettingsCatalog.EditorTabWidth, true),
        ("InsertSpaces", "Insert _spaces instead of tabs", SettingsCatalog.EditorInsertSpaces, true),
        ("AutoIndent", "_Automatic indentation", SettingsCatalog.EditorAutoIndent, true),
        ("AutoCloseBrackets", "Close _brackets and quotes automatically", SettingsCatalog.EditorAutoCloseBrackets, true),
        ("AutoCloseBeginEnd", "Complete begin with _end", SettingsCatalog.EditorAutoCloseBeginEnd, false),
        ("SmartHome", "_Home key goes to first non-blank character", SettingsCatalog.EditorSmartHome, true),
        ("CaretPastEol", "Allow caret beyond end of _line", SettingsCatalog.EditorCaretPastEndOfLine, true),
        ("RectangularSelection", "_Column selection with Alt", SettingsCatalog.EditorRectangularSelection, true),
        ("ScrollPastEnd", "Scroll _past the last line", SettingsCatalog.EditorScrollPastEnd, true),
        ("MouseWheelZoom", "_Zoom with Ctrl+mouse wheel", SettingsCatalog.EditorMouseWheelZoom, true),
        ("CompletionAuto", "Show c_ompletions while typing", SettingsCatalog.EditorCompletionAutoTrigger, false),
        ("CompletionDelay", "_Delay (ms):", SettingsCatalog.EditorCompletionDelayMs, false),
        ("ParameterInfoAuto", "Show parameter in_fo after (", SettingsCatalog.EditorParameterInfoAutoTrigger, false),
        ("HoverEnabled", "Show symbol information on ho_ver", SettingsCatalog.EditorHoverEnabled, false),
        ("HoverDelay", "Hover dela_y (ms):", SettingsCatalog.EditorHoverDelayMs, false),
        ("LiveDiagnostics", "_Underline errors while typing", SettingsCatalog.EditorLiveDiagnostics, false),
        ("LiveDiagnosticsDelay", "Error chec_k delay (ms):", SettingsCatalog.EditorLiveDiagnosticsDelayMs, false),
    ];

    /// <summary>dialogs.md §9.2 (all P1).</summary>
    private static readonly (string Field, string Label, ISettingKey Key, bool Enabled)[] DisplayFields =
    [
        ("FontFamily", "_Font:", SettingsCatalog.EditorFontFamily, true),
        ("FontSize", "_Size:", SettingsCatalog.EditorFontSize, true),
        ("ShowLineNumbers", "Line _numbers", SettingsCatalog.EditorShowLineNumbers, true),
        ("ShowFoldMargin", "F_olding markers", SettingsCatalog.EditorShowFoldMargin, true),
        ("ShowChangeBar", "_Change bar", SettingsCatalog.EditorShowChangeBar, true),
        ("HighlightCurrentLine", "_Highlight current line", SettingsCatalog.EditorHighlightCurrentLine, true),
        ("HighlightBrackets", "Highlight matching _brackets", SettingsCatalog.EditorHighlightMatchingBrackets, true),
        ("HighlightOccurrences", "Highlight occurrences of the _word at the caret", SettingsCatalog.EditorHighlightWordOccurrences, true),
        ("ShowIndentGuides", "_Indentation guides", SettingsCatalog.EditorShowIndentGuides, true),
        ("ShowWhitespace", "Show whi_tespace", SettingsCatalog.EditorShowWhitespace, true),
        ("WordWrap", "Wor_d wrap in newly opened files", SettingsCatalog.EditorWordWrap, true),
        ("CaretBlink", "Blinking c_aret", SettingsCatalog.EditorCaretBlink, true),
        ("RightMargin", "_Right margin column:", SettingsCatalog.EditorRightMargin, true),
    ];

    /// <summary>dialogs.md §9.6, Phase 1 fields.</summary>
    private static readonly (string Field, string Label, ISettingKey Key, bool Enabled)[] EnvironmentGeneralFields =
    [
        ("Theme", "_Theme:", SettingsCatalog.AppearanceTheme, true),
        ("ShowFullPath", "Show full _path in title bar", SettingsCatalog.AppearanceShowFullPathInTitle, true),
        ("ReopenLastProject", "_Reopen the last project at start-up", SettingsCatalog.EnvironmentReopenLastProject, true),
        ("RestoreOpenFiles", "Restore _open files", SettingsCatalog.EnvironmentRestoreOpenFiles, true),
        ("ShowWelcome", "Show _Welcome page at start-up", SettingsCatalog.EnvironmentShowWelcomeOnStartup, true),
        ("DefaultProjectsDir", "_Default project folder:", SettingsCatalog.EnvironmentDefaultProjectsDir, true),
    ];

    /// <summary>dialogs.md §9.7 (all P1).</summary>
    private static readonly (string Field, string Label, ISettingKey Key, bool Enabled)[] EnvironmentFilesFields =
    [
        ("DefaultEncoding", "_Encoding of new files:", SettingsCatalog.FilesDefaultEncoding, true),
        ("DefaultLineEnding", "_Line endings of new files:", SettingsCatalog.FilesDefaultLineEnding, true),
        ("TrimTrailing", "_Trim trailing whitespace on save", SettingsCatalog.FilesTrimTrailingWhitespaceOnSave, true),
        ("FinalNewline", "End files with a line _break", SettingsCatalog.FilesEnsureFinalNewline, true),
        ("DetectExternal", "_Detect changes made outside FreexComp", SettingsCatalog.FilesDetectExternalChanges, true),
        ("AutoReload", "_Reload unmodified files that change on disk", SettingsCatalog.EnvironmentAutoReloadUnmodified, true),
        ("RecoveryInterval", "Save recovery copies e_very (seconds, 0 = off):", SettingsCatalog.EnvironmentRecoveryIntervalSeconds, true),
        ("LargeFile", "Treat files lar_ger than (MB) as large:", SettingsCatalog.FilesLargeFileThresholdMb, true),
    ];

    private static List<string> ChoiceTexts(DialogDriver dlg, string field) =>
        [.. ((ComboBox)dlg.Find<Control>(field)).Items.Cast<OptionChoice>().Select(c => c.Display)];

    /// <summary>Checks each field's control, label, key, value and enabled state against the settings.</summary>
    private static void CheckFields(DialogDriver dlg, ISettingsService settings, (string Field, string Label, ISettingKey Key, bool Enabled)[] fields)
    {
        var model = (OptionsViewModel)dlg.Window.Model;
        foreach (var (field, label, key, enabled) in fields)
        {
            Assert.Same(SettingsCatalog.All.Single(k => k.Name == key.Name), model.Field(field).Key);
            var control = dlg.Find<Control>(field);
            Assert.True(control.IsEffectivelyVisible, field);
            Assert.Equal(enabled, control.IsEffectivelyEnabled);
            Assert.Equal(label, LabelOf(dlg, field).Content);
            switch (key)
            {
                case SettingKey<bool> b:
                    Assert.Equal(settings.Get(b), ((CheckBox)control).IsChecked);
                    break;
                case SettingKey<int> i:
                    Assert.Equal(settings.Get(i).ToString(System.Globalization.CultureInfo.InvariantCulture), FieldText(dlg, field));
                    break;
                case SettingKey<string> s when control is ComboBox combo:
                    Assert.Equal(settings.Get(s), ((OptionChoice)combo.SelectedItem!).Value);
                    break;
                case SettingKey<string> s:
                    Assert.Equal(settings.Get(s), ((TextBox)control).Text);
                    break;
            }

            Assert.Equal(!settings.IsDefault(key.Name), dlg.TryFind<Button>(field + ".Reset")?.IsEffectivelyVisible == true);
        }
    }

    private static void SetText(DialogDriver dlg, string field, string text)
    {
        var control = dlg.Find<Control>(field);
        if (control is NumericUpDown numeric)
            numeric.Text = text;
        else
            ((TextBox)control).Text = text;
        Dispatcher.UIThread.RunJobs();
    }

    private static string? FieldText(DialogDriver dlg, string field) =>
        dlg.Find<Control>(field) is NumericUpDown numeric ? numeric.Text : dlg.Find<TextBox>(field).Text;

    /// <summary>The label of a field: the <see cref="Label"/> targeting it, or the check box itself.</summary>
    private static ContentControl LabelOf(DialogDriver dlg, string field)
    {
        var control = dlg.Find<Control>(field);
        return (ContentControl?)(control as CheckBox)
            ??Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(dlg.Window).OfType<Label>().Single(l => l.Target == control);
    }

    private static bool IsHighlighted(DialogDriver dlg, string field) =>
        LabelOf(dlg, field).Background is ISolidColorBrush brush
        && dlg.Window.TryFindResource("FxcEditorFindMatchBgBrush", dlg.Window.ActualThemeVariant, out var expected)
        && expected is ISolidColorBrush find && brush.Color == find.Color;

    private static List<string> VisiblePages(DialogDriver dlg) =>
        [.. dlg.FindAll("Dialog.Options.Page.").Where(c => c.IsEffectivelyVisible).Select(c => AutomationIds.Get(c)!["Dialog.Options.Page.".Length..])];

    private static string PageTitle(DialogDriver dlg) => dlg.Find<TextBlock>("PageTitle").Text!;

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-090")]
    [Trait("Req", "UI-DLG-092")]
    [Trait("Req", "UI-DLG-094")]
    public async Task EveryFieldBoundToCatalogKey()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.Settings["editor.tabWidth"] = 4;
            o.Settings["editor.wordWrap"] = true;
        });
        var settings = h.Services.Settings;

        var running = h.Services.Commands.ExecuteAsync("tools.options");
        var dlg = await h.WaitForDialogAsync("Options");
        var model = (OptionsViewModel)dlg.Window.Model;
        Assert.Equal("Options", dlg.Window.Title);
        Assert.Equal((820.0, 580.0, true), (dlg.Window.Width, dlg.Window.Height, dlg.Window.CanResize));
        Assert.Equal((820.0, 580.0), (dlg.Window.MinWidth, dlg.Window.MinHeight));
        Assert.Equal(["Editor", "Editor.General", "Editor.Display", "Editor.CodeTemplates", "Colors", "KeyMappings", "Environment", "Environment.General", "Environment.Files", "Designer", "ToolchainPaths"], VisiblePages(dlg));
        Assert.Equal("Editor › General", PageTitle(dlg)); // first time: Editor.General (UI-DLG-092)
        Assert.Equal("Dialog.Options.Filter", dlg.FocusedAutomationId);

        // Every field: its control, label, catalog key, value from the settings and enabled state (later phases disabled).
        void CheckPage((string Field, string Label, ISettingKey Key, bool Enabled)[] fields)
        {
            foreach (var (field, label, key, enabled) in fields)
            {
                var vm = model.Field(field);
                Assert.Same(key, vm.Key);
                Assert.Same(SettingsCatalog.All.Single(k => k.Name == key.Name), vm.Key);
                var control = dlg.Find<Control>(field);
                Assert.True(control.IsEffectivelyVisible, field);
                Assert.Equal(enabled, control.IsEffectivelyEnabled);
                Assert.Equal(label, LabelOf(dlg, field).Content);
                switch (key)
                {
                    case SettingKey<bool> b:
                        Assert.Equal(settings.Get(b), ((CheckBox)control).IsChecked);
                        break;
                    case SettingKey<int> i:
                        Assert.Equal(settings.Get(i).ToString(System.Globalization.CultureInfo.InvariantCulture), FieldText(dlg, field));
                        break;
                    case SettingKey<string> s:
                        Assert.Equal(settings.Get(s), ((OptionChoice)((ComboBox)control).SelectedItem!).Value);
                        break;
                }

                // UI-DLG-094: ↺ only where the value differs from the key's default.
                var reset = dlg.TryFind<Button>(field + ".Reset");
                Assert.Equal(!settings.IsDefault(key.Name), reset?.IsEffectivelyVisible == true);
            }
        }

        CheckPage(GeneralFields);
        Assert.Equal("Reset to default: 2", ToolTip.GetTip(dlg.Find<Button>("TabWidth.Reset")));
        dlg.Click("Page.Editor.Display");
        Assert.Equal("Editor › Display", PageTitle(dlg));
        CheckPage(DisplayFields);
        Assert.Equal("(Default)", ((OptionChoice)((ComboBox)dlg.Find<Control>("FontFamily")).Items[0]!).Display);
        Assert.IsType<TextEditor>(dlg.Find<Control>("Preview"));

        // Changing every enabled field in the UI and applying writes exactly its own key.
        foreach (var (field, _, key, enabled) in DisplayFields.Concat(GeneralFields).Where(f => f.Enabled))
        {
            dlg.Click(GeneralFields.Any(f => f.Field == field) ? "Page.Editor.General" : "Page.Editor.Display");
            switch (key)
            {
                case SettingKey<bool>:
                    dlg.Click(field);
                    break;
                case SettingKey<int> i:
                    SetText(dlg, field, (settings.Get(i) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case SettingKey<string>:
                    model.Field(field).Value = "Courier New"; // the installed-font list depends on the machine
                    break;
            }
        }

        dlg.Click("Apply");
        foreach (var (field, _, key, enabled) in DisplayFields.Concat(GeneralFields))
        {
            var changed = key switch
            {
                SettingKey<bool> b => settings.Get(b) != b.Default,
                SettingKey<int> i => settings.Get(i) != i.Default,
                SettingKey<string> s => settings.Get(s) != s.Default,
                _ => throw new InvalidOperationException(key.Name),
            };
            // tabWidth and wordWrap started away from their default, so changing them by one step leaves them changed
            // (tabWidth 5) or brings them back (wordWrap false).
            var expected = enabled && key.Name != SettingsCatalog.EditorWordWrap.Name;
            Assert.True(expected == changed, field);
        }

        Assert.Equal(5, settings.Get(SettingsCatalog.EditorTabWidth));
        dlg.Press("Escape");
        await running;

        // UI-DLG-092: the page last shown; tools.options with a page id opens that page (status bar R5).
        running = h.Services.Commands.ExecuteAsync("tools.options");
        dlg = await h.WaitForDialogAsync("Options");
        Assert.Equal("Editor › General", PageTitle(dlg));
        dlg.Click("Page.Editor.Display");
        dlg.Press("Escape");
        await running;
        running = h.Services.Commands.ExecuteAsync("tools.options");
        dlg = await h.WaitForDialogAsync("Options");
        Assert.Equal("Editor › Display", PageTitle(dlg));
        dlg.Press("Escape");
        await running;
        running = h.Services.Commands.ExecuteAsync("tools.options", OptionsViewModel.EditorGeneralPage);
        dlg = await h.WaitForDialogAsync("Options");
        Assert.Equal("Editor › General", PageTitle(dlg));

        // Selecting a group row selects its first child; a later-phase page shows the note and no editable content.
        dlg.Click("Page.Designer");
        Assert.Equal("Designer", PageTitle(dlg));
        Assert.Equal("These options take effect in a later version.", dlg.Find<TextBlock>("LaterVersion").Text);
        Assert.True(dlg.Find<TextBlock>("LaterVersion").IsEffectivelyVisible);
        dlg.Click("Page.Editor");
        Assert.Equal("Editor › General", PageTitle(dlg));
        Assert.False(dlg.Find<TextBlock>("LaterVersion").IsEffectivelyVisible);
        dlg.Press("Escape");
        await running;
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-091")]
    public async Task FilterHidesPagesAndHighlights()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var running = h.Services.Commands.ExecuteAsync("tools.options");
        var dlg = await h.WaitForDialogAsync("Options");
        Assert.Null(dlg.TryFind<TextBlock>("FilterEmpty") is { IsEffectivelyVisible: true } e ? e : null);

        // A field label matches: only its page stays (with its group); the hidden current page gives way to it.
        dlg.Type("Filter", "MARGIN");
        Assert.Equal(["Editor", "Editor.Display"], VisiblePages(dlg));
        Assert.Equal("Editor › Display", PageTitle(dlg));
        Assert.True(IsHighlighted(dlg, "RightMargin"));
        Assert.False(IsHighlighted(dlg, "FontSize"));

        // Several pages: "line" matches labels on both editor pages and Environment › Files; the current page stays selected.
        SetText(dlg, "Filter", "line");
        Assert.Equal(["Editor", "Editor.General", "Editor.Display", "Environment", "Environment.Files"], VisiblePages(dlg));
        Assert.Equal("Editor › Display", PageTitle(dlg));
        Assert.True(IsHighlighted(dlg, "ShowLineNumbers"));
        Assert.True(IsHighlighted(dlg, "HighlightCurrentLine"));
        Assert.False(IsHighlighted(dlg, "RightMargin"));
        dlg.Click("Page.Editor.General");
        Assert.True(IsHighlighted(dlg, "CaretPastEol"));
        Assert.True(IsHighlighted(dlg, "ScrollPastEnd"));
        Assert.False(IsHighlighted(dlg, "TabWidth"));

        // A page title matches: the page is listed, no label is highlighted.
        SetText(dlg, "Filter", "templates");
        Assert.Equal(["Editor", "Editor.CodeTemplates"], VisiblePages(dlg));
        Assert.Equal("Editor › Code Templates", PageTitle(dlg));

        // No match: the tree shows Msg.Options.FilterEmpty.
        SetText(dlg, "Filter", "zzz");
        Assert.Empty(VisiblePages(dlg));
        Assert.Equal("No option matches zzz", dlg.Find<TextBlock>("FilterEmpty").Text);
        Assert.True(dlg.Find<TextBlock>("FilterEmpty").IsEffectivelyVisible);

        // Cleared: every page is back, nothing is highlighted.
        SetText(dlg, "Filter", "");
        Assert.Equal(["Editor", "Editor.General", "Editor.Display", "Editor.CodeTemplates", "Colors", "KeyMappings", "Environment", "Environment.General", "Environment.Files", "Designer", "ToolchainPaths"], VisiblePages(dlg));
        Assert.False(dlg.Find<TextBlock>("FilterEmpty").IsEffectivelyVisible);
        dlg.Click("Page.Editor.Display");
        Assert.False(IsHighlighted(dlg, "ShowLineNumbers"));
        dlg.Press("Escape");
        await running;
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-093")]
    [Trait("Req", "UI-DLG-081")]
    [Trait("Req", "IDE-SET-025")]
    [Trait("Req", "IDE-SET-026")]
    public async Task ApplyIsLiveAndBatched()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.InitialFiles["Demo.pas"] = "unit Demo;\ninterface\nimplementation\nend.\n");
        var settings = h.Services.Settings;
        Assert.True((await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, "Demo.pas"))).Success);
        Dispatcher.UIThread.RunJobs();
        var probe = (IEditorTestProbe)Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(h.Find<TextEditor>(AutomationIds.Editor("Demo.pas")))
            .OfType<CodeEditorView>().Single();
        var gutter = probe.GutterWidth;
        var events = new List<string>();
        settings.Changed += (_, e) => events.Add(e.Key);
        settings.BatchCompleted += (_, _) => events.Add("(batch)");
        var settingsFile = Path.Combine(h.UserDir, "roaming", "settings.json");

        var running = h.Services.Commands.ExecuteAsync("tools.options", "Editor.Display");
        var dlg = await h.WaitForDialogAsync("Options");
        var apply = dlg.Find<Button>("Apply");
        var ok = dlg.Find<Button>("Ok");
        Assert.False(apply.IsEnabled); // nothing changed yet
        Assert.True(ok.IsEnabled);

        // Pending changes: the preview follows them at once; settings and open editors do not.
        dlg.Click("ShowLineNumbers");
        SetText(dlg, "FontSize", "16");
        Assert.True(apply.IsEnabled);
        var preview = (TextEditor)dlg.Find<Control>("Preview");
        Assert.False(preview.ShowLineNumbers);
        Assert.Equal(16, preview.FontSize);
        Assert.Equal(6, preview.Document.LineCount);
        Assert.True(preview.IsReadOnly);
        Assert.Empty(events);
        Assert.True(settings.Get(SettingsCatalog.EditorShowLineNumbers));
        Assert.Equal(gutter, probe.GutterWidth);

        // Invalid value: message under the field; OK and Apply disabled.
        SetText(dlg, "FontSize", "99");
        Assert.Equal("Enter a size from 6 to 72.", dlg.ErrorText("FontSize"));
        Assert.False(ok.IsEnabled);
        Assert.False(apply.IsEnabled);
        SetText(dlg, "FontSize", "x");
        Assert.Equal("Enter a size from 6 to 72.", dlg.ErrorText("FontSize"));
        SetText(dlg, "FontSize", "16");
        Assert.Null(dlg.ErrorText("FontSize"));
        Assert.True(ok.IsEnabled);

        // Apply: one batch (one event per changed key, then BatchCompleted), flushed to disk, live in the editor.
        dlg.Click("Apply");
        Assert.Equal(["editor.fontSize", "editor.showLineNumbers", "(batch)"], events);
        Assert.False(settings.Get(SettingsCatalog.EditorShowLineNumbers));
        Assert.Equal(16, settings.Get(SettingsCatalog.EditorFontSize));
        Assert.True(probe.GutterWidth < gutter);
        await h.WaitForAsync(() => File.Exists(settingsFile) && File.ReadAllText(settingsFile).Contains("\"editor.fontSize\": 16", StringComparison.Ordinal),
            "settings flushed");
        Assert.False(apply.IsEnabled); // disabled again after applying
        Assert.True(dlg.Window.IsVisible);

        // Cancel discards pending changes; what Apply wrote stays.
        dlg.Click("Page.Editor.General");
        SetText(dlg, "TabWidth", "8");
        events.Clear();
        dlg.Click("Cancel");
        await running;
        Assert.Empty(events);
        Assert.Equal(2, settings.Get(SettingsCatalog.EditorTabWidth));
        Assert.Equal(16, settings.Get(SettingsCatalog.EditorFontSize));

        // Reopened: fields show the applied values; ↺ resets a field to its default; OK applies and closes.
        running = h.Services.Commands.ExecuteAsync("tools.options");
        dlg = await h.WaitForDialogAsync("Options");
        Assert.Equal("Editor › General", PageTitle(dlg));
        dlg.Click("Page.Editor.Display");
        Assert.False(dlg.Find<CheckBox>("ShowLineNumbers").IsChecked);
        dlg.Click("ShowLineNumbers.Reset");
        Assert.True(dlg.Find<CheckBox>("ShowLineNumbers").IsChecked);
        Assert.False(dlg.TryFind<Button>("ShowLineNumbers.Reset")?.IsEffectivelyVisible == true);
        dlg.Click("FontSize.Reset");
        Assert.Equal("13", FieldText(dlg, "FontSize"));
        dlg.Click("Ok");
        await running;
        Assert.False(dlg.Window.IsVisible);
        Assert.Equal(["editor.fontSize", "editor.showLineNumbers", "(batch)"], events);
        Assert.True(settings.IsDefault(SettingsCatalog.EditorShowLineNumbers.Name));
        Assert.Equal(gutter, probe.GutterWidth);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-090")]
    [Trait("Req", "UI-DLG-093")]
    [Trait("Req", "UI-DLG-094")]
    public async Task EnvironmentPagesBindSettings()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.Settings["appearance.theme"] = "light";
            o.Settings["files.largeFileThresholdMb"] = 20;
        });
        var settings = h.Services.Settings;

        var running = h.Services.Commands.ExecuteAsync("tools.options", "Environment.General");
        var dlg = await h.WaitForDialogAsync("Options");
        Assert.Equal(["Editor", "Editor.General", "Editor.Display", "Editor.CodeTemplates", "Colors", "KeyMappings", "Environment",
            "Environment.General", "Environment.Files", "Designer", "ToolchainPaths"], VisiblePages(dlg));

        // Environment › General: every P1 field bound to its key; the theme combo lists the three values.
        Assert.Equal("Environment › General", PageTitle(dlg));
        CheckFields(dlg, settings, EnvironmentGeneralFields);
        Assert.Equal(["Follow system", "Light", "Dark"], ChoiceTexts(dlg, "Theme"));
        Assert.Equal("Start-up", dlg.Window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Start-up").Text);

        // Default project folder: placeholder = the built-in default; a relative path is refused; Browse picks a folder.
        var folder = dlg.Find<TextBox>("DefaultProjectsDir");
        Assert.Equal(Path.Combine(h.Services.UserPaths.DocumentsDirectory, "FreexComp Projects"), folder.PlaceholderText);
        SetText(dlg, "DefaultProjectsDir", @"relative\folder");
        Assert.Equal("The folder path is not valid.", dlg.ErrorText("DefaultProjectsDir"));
        Assert.False(dlg.Find<Button>("Ok").IsEnabled);
        h.FileDialogs.NextFolder(h.TempDir);
        dlg.Click("DefaultProjectsDir.Browse");
        await h.WaitForAsync(() => folder.Text == h.TempDir, "folder picked");
        Assert.Equal("folder.DefaultProjectsDir", h.FileDialogs.Requests.Last().PromptId);
        Assert.Null(dlg.ErrorText("DefaultProjectsDir"));
        Assert.True(dlg.Find<Button>("Ok").IsEnabled);

        // Change the other fields in the UI.
        ((ComboBox)dlg.Find<Control>("Theme")).SelectedIndex = 2; // Dark
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Core.Platform.ThemeKind.Light, h.Services.Theme.ActualTheme); // pending: not applied yet
        foreach (var check in new[] { "ShowFullPath", "ReopenLastProject", "RestoreOpenFiles", "ShowWelcome" })
            dlg.Click(check);

        // Environment › Files: every field bound; Reload depends on Detect; the recovery interval is validated.
        dlg.Click("Page.Environment.Files");
        Assert.Equal("Environment › Files", PageTitle(dlg));
        CheckFields(dlg, settings, EnvironmentFilesFields);
        Assert.Equal(["UTF-8", "UTF-8 with BOM", "UTF-16 LE", "UTF-16 BE", "Western (Windows-1252)"], ChoiceTexts(dlg, "DefaultEncoding"));
        Assert.Equal(["Windows (CRLF)", "Unix (LF)"], ChoiceTexts(dlg, "DefaultLineEnding"));
        dlg.Click("AutoReload");
        dlg.Click("DetectExternal");
        Assert.False(dlg.Find<CheckBox>("AutoReload").IsEffectivelyEnabled);
        SetText(dlg, "RecoveryInterval", "3");
        Assert.Equal("Enter 0 to turn recovery copies off, or an interval from 5 to 600 seconds.", dlg.ErrorText("RecoveryInterval"));
        SetText(dlg, "RecoveryInterval", "0");
        Assert.Null(dlg.ErrorText("RecoveryInterval"));
        SetText(dlg, "LargeFile", "201");
        Assert.Equal("Enter a size from 1 to 200 MB.", dlg.ErrorText("LargeFile"));
        SetText(dlg, "LargeFile", "50");
        ((ComboBox)dlg.Find<Control>("DefaultEncoding")).SelectedIndex = 4;
        ((ComboBox)dlg.Find<Control>("DefaultLineEnding")).SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        dlg.Click("TrimTrailing");
        dlg.Click("FinalNewline");

        // Toolchain Paths: Phase 3 page, shown with the later-version note.
        dlg.Click("Page.ToolchainPaths");
        Assert.Equal("Toolchain Paths", PageTitle(dlg));
        Assert.True(dlg.Find<TextBlock>("LaterVersion").IsEffectivelyVisible);

        // OK writes every changed key; the theme then applies.
        dlg.Click("Ok");
        await running;
        Assert.Equal("dark", settings.Get(SettingsCatalog.AppearanceTheme));
        Assert.Equal(Core.Platform.ThemeKind.Dark, h.Services.Theme.ActualTheme);
        Assert.True(settings.Get(SettingsCatalog.AppearanceShowFullPathInTitle));
        Assert.False(settings.Get(SettingsCatalog.EnvironmentReopenLastProject));
        Assert.False(settings.Get(SettingsCatalog.EnvironmentRestoreOpenFiles));
        Assert.True(settings.Get(SettingsCatalog.EnvironmentShowWelcomeOnStartup)); // preset false in the harness
        Assert.Equal(h.TempDir, settings.Get(SettingsCatalog.EnvironmentDefaultProjectsDir));
        Assert.Equal("windows-1252", settings.Get(SettingsCatalog.FilesDefaultEncoding));
        Assert.Equal("lf", settings.Get(SettingsCatalog.FilesDefaultLineEnding));
        Assert.True(settings.Get(SettingsCatalog.FilesTrimTrailingWhitespaceOnSave));
        Assert.False(settings.Get(SettingsCatalog.FilesEnsureFinalNewline));
        Assert.False(settings.Get(SettingsCatalog.FilesDetectExternalChanges));
        Assert.False(settings.Get(SettingsCatalog.EnvironmentAutoReloadUnmodified));
        Assert.Equal(0, settings.Get(SettingsCatalog.EnvironmentRecoveryIntervalSeconds));
        Assert.Equal(50, settings.Get(SettingsCatalog.FilesLargeFileThresholdMb));
    }
}
