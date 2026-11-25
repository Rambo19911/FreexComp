using FreexComp.Ide.Core.Editing;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Editor;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.Views;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The IDE Options dialog frame and the Editor › General / Editor › Display pages (dialogs.md §9, §9.1, §9.2).</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed partial class OptionsDialogTests
{
    /// <summary>dialogs.md §9.1: field id, label (mnemonic as <c>_</c>), key, enabled at the product's phase (pending tasks: disabled).</summary>
    private static readonly (string Field, string Label, ISettingKey Key, bool Enabled)[] GeneralFields =
    [
        ("TabWidth", "_Tab width:", SettingsCatalog.EditorTabWidth, true),
        ("InsertSpaces", "Insert _spaces instead of tabs", SettingsCatalog.EditorInsertSpaces, true),
        ("AutoIndent", "_Automatic indentation", SettingsCatalog.EditorAutoIndent, true),
        ("AutoCloseBrackets", "Close _brackets and quotes automatically", SettingsCatalog.EditorAutoCloseBrackets, true),
        ("AutoCloseBeginEnd", "Complete begin with _end", SettingsCatalog.EditorAutoCloseBeginEnd, true),
        ("SmartHome", "_Home key goes to first non-blank character", SettingsCatalog.EditorSmartHome, true),
        ("CaretPastEol", "Allow caret beyond end of _line", SettingsCatalog.EditorCaretPastEndOfLine, true),
        ("RectangularSelection", "_Column selection with Alt", SettingsCatalog.EditorRectangularSelection, true),
        ("ScrollPastEnd", "Scroll _past the last line", SettingsCatalog.EditorScrollPastEnd, true),
        ("MouseWheelZoom", "_Zoom with Ctrl+mouse wheel", SettingsCatalog.EditorMouseWheelZoom, true),
        ("CompletionAuto", "Show c_ompletions while typing", SettingsCatalog.EditorCompletionAutoTrigger, true), // I-114
        ("CompletionDelay", "_Delay (ms):", SettingsCatalog.EditorCompletionDelayMs, true),
        ("ParameterInfoAuto", "Show parameter in_fo after (", SettingsCatalog.EditorParameterInfoAutoTrigger, true), // I-116
        ("HoverEnabled", "Show symbol information on ho_ver", SettingsCatalog.EditorHoverEnabled, true),
        ("HoverDelay", "Hover dela_y (ms):", SettingsCatalog.EditorHoverDelayMs, true),
        ("LiveDiagnostics", "_Underline errors while typing", SettingsCatalog.EditorLiveDiagnostics, true),
        ("LiveDiagnosticsDelay", "Error chec_k delay (ms):", SettingsCatalog.EditorLiveDiagnosticsDelayMs, true),
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
                case SettingKey<int> i when control is ComboBox numbers:
                    Assert.Equal(settings.Get(i).ToString(System.Globalization.CultureInfo.InvariantCulture), ((OptionChoice)numbers.SelectedItem!).Value);
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
        Assert.Equal(["Editor", "Editor.General", "Editor.Display", "Editor.CodeTemplates", "Colors", "KeyMappings", "Environment", "Environment.General", "Environment.Files", "Environment.Trust", "Designer", "ToolchainPaths"], VisiblePages(dlg)); // Environment.Trust: I-268
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

        // Changing every enabled field in the UI and applying writes exactly its own key (check boxes last: one may
        // disable the field it enables, e.g. LiveDiagnostics → LiveDiagnosticsDelay).
        foreach (var (field, _, key, enabled) in DisplayFields.Concat(GeneralFields).Where(f => f.Enabled).OrderBy(f => f.Key is SettingKey<bool>))
        {
            dlg.Click(GeneralFields.Any(f => f.Field == field) ? "Page.Editor.General" : "Page.Editor.Display");
            switch (key)
            {
                case SettingKey<bool>:
                    dlg.Find<Control>(field).BringIntoView(); // the last rows of the page are below the fold
                    Dispatcher.UIThread.RunJobs();
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
        Assert.Equal("Code assistance", ((OptionsViewModel)dlg.Window.Model).Field("CompletionAuto").GroupHeading); // menus.md N4

        // Selecting a group row selects its first child; at the product's phase the Designer page is live (the note of a
        // later-phase page: AutoSwitchLayoutBinds at Phase 4).
        dlg.Click("Page.Designer");
        Assert.Equal("Designer", PageTitle(dlg));
        Assert.False(dlg.Find<TextBlock>("LaterVersion").IsEffectivelyVisible);
        Assert.True(dlg.Find<Control>("ShowGrid").IsEffectivelyEnabled);
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

        // Several pages: "line" matches labels on both editor pages, Environment › Files and Designer ("guidelines"); the current page stays selected.
        SetText(dlg, "Filter", "line");
        Assert.Equal(["Editor", "Editor.General", "Editor.Display", "Environment", "Environment.Files", "Designer"], VisiblePages(dlg));
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
        Assert.Equal(["Editor", "Editor.General", "Editor.Display", "Editor.CodeTemplates", "Colors", "KeyMappings", "Environment", "Environment.General", "Environment.Files", "Environment.Trust", "Designer", "ToolchainPaths"], VisiblePages(dlg)); // Environment.Trust: I-268
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
        // Apply started the save (seen in the recorded calls, not by reading the file: a read that overlaps the save's
        // replace of settings.json makes that replace fail, I-169); once it has finished the file holds the new value.
        await h.WaitForAsync(() => h.FileSystem.Calls.Any(c => c.StartsWith("MoveFile ", StringComparison.Ordinal) &&
            c.EndsWith(" -> " + settingsFile, StringComparison.OrdinalIgnoreCase)), "settings flushed");
        await settings.FlushAsync();
        Assert.Contains("\"editor.fontSize\": 16", File.ReadAllText(settingsFile), StringComparison.Ordinal);
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
            "Environment.General", "Environment.Files", "Environment.Trust", "Designer", "ToolchainPaths"], VisiblePages(dlg)); // Environment.Trust: I-268

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

        // Toolchain Paths: a Phase 3 page, editable since the implemented phase is 3 (no later-version note).
        dlg.Click("Page.ToolchainPaths");
        Assert.Equal("Toolchain Paths", PageTitle(dlg));
        Assert.False(dlg.Find<TextBlock>("LaterVersion").IsEffectivelyVisible);

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

    /// <summary>
    /// dialogs.md §9.3 (I-200): the Abbreviation column is sortable — a header click reverses the alphabetical order, a second
    /// click restores it; the selection stays on its row and an added row is sorted in on the next click.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "3")]
    [Trait("Req", "IDE-SET-080")]
    public async Task CodeTemplatesPageSortsByColumn()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var running = h.Services.Commands.ExecuteAsync("tools.options", OptionsViewModel.CodeTemplatesPage);
        var dlg = await h.WaitForDialogAsync("Options");
        var model = ((OptionsViewModel)dlg.Window.Model).CodeTemplates;
        var ascending = CodeTemplateService.BuiltIns.Select(t => t.Abbreviation).Order(StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(ascending, model.Rows.Select(r => r.Abbreviation));
        Assert.False(model.SortDescending);
        var header = dlg.Find<Button>("Templates.Column.Abbreviation");
        Assert.Equal("Abbreviation", Avalonia.Automation.AutomationProperties.GetName(header));
        model.Selected = model.Rows.Single(r => r.Abbreviation == "wl");

        dlg.Click("Templates.Column.Abbreviation");
        Assert.True(model.SortDescending);
        Assert.Equal(ascending.AsEnumerable().Reverse(), model.Rows.Select(r => r.Abbreviation));
        Assert.Equal("wl", model.Selected!.Abbreviation);
        Assert.Same(model.Selected, dlg.Find<ListBox>("Templates").SelectedItem);
        Assert.False(dlg.Find<Button>("Apply").IsEnabled); // sorting changes no template

        dlg.Click("Templates.Add"); // appended at the end, then sorted in by the next click
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("new1", model.Rows[^1].Abbreviation);
        dlg.Click("Templates.Column.Abbreviation");
        Assert.False(model.SortDescending);
        Assert.Equal(ascending.Append("new1").Order(StringComparer.OrdinalIgnoreCase), model.Rows.Select(r => r.Abbreviation));
        Assert.Equal("new1", model.Selected!.Abbreviation);

        dlg.Click("Cancel");
        await running;
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-080")]
    public async Task CodeTemplatesPageEditsTemplatesJson()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var running = h.Services.Commands.ExecuteAsync("tools.options", OptionsViewModel.CodeTemplatesPage);
        var dlg = await h.WaitForDialogAsync("Options");
        var model = ((OptionsViewModel)dlg.Window.Model).CodeTemplates;
        Assert.Equal("Editor › Code Templates", PageTitle(dlg));

        // The grid: the built-ins in alphabetical order; selecting a row fills the fields.
        var list = dlg.Find<ListBox>("Templates");
        Assert.Equal([.. CodeTemplateService.BuiltIns.Select(t => t.Abbreviation).Order(StringComparer.OrdinalIgnoreCase)], model.Rows.Select(r => r.Abbreviation));
        model.Selected = model.Rows.Single(r => r.Abbreviation == "wl");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("wl", dlg.Find<TextBox>("Abbreviation").Text);
        Assert.Equal("WriteLn($end$);", dlg.Find<TextBox>("Body").Text);
        Assert.False(dlg.Find<Button>("Templates.Reset").IsEnabled); // an unmodified built-in

        // Editing a built-in enables Reset; Reset restores it.
        dlg.Find<TextBox>("Body").Text = "WriteLn('$end$');";
        Dispatcher.UIThread.RunJobs();
        Assert.True(dlg.Find<Button>("Templates.Reset").IsEnabled);
        Assert.True(dlg.Find<Button>("Apply").IsEnabled);
        dlg.Click("Templates.Reset");
        Assert.Equal("WriteLn($end$);", dlg.Find<TextBox>("Body").Text);
        dlg.Find<TextBox>("Body").Text = "WriteLn('$end$');";

        // Add appends new1 and selects it; invalid or duplicate abbreviations and an empty body are errors.
        dlg.Click("Templates.Add");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("new1", model.Selected!.Abbreviation);
        Assert.Same(model.Selected, list.SelectedItem);
        var abbreviation = dlg.Find<TextBox>("Abbreviation");
        abbreviation.Text = "9x";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Strings.Msg_Options_TemplateAbbrevInvalid, dlg.Find<TextBlock>("Error.Abbreviation").Text);
        Assert.False(dlg.Find<Button>("Ok").IsEnabled);
        abbreviation.Text = "WL";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Strings.Msg_Options_TemplateAbbrevExists, dlg.Find<TextBlock>("Error.Abbreviation").Text);
        abbreviation.Text = "hello";
        dlg.Find<TextBox>("Description").Text = "greeting";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Strings.Msg_Options_TemplateBodyEmpty, dlg.Find<TextBlock>("Error.Body").Text);
        dlg.Find<TextBox>("Body").Text = "WriteLn('Hello');";
        Dispatcher.UIThread.RunJobs();
        Assert.True(string.IsNullOrEmpty(dlg.Find<TextBlock>("Error.Abbreviation").Text));
        Assert.True(dlg.Find<Button>("Ok").IsEnabled);

        // Delete removes a built-in without confirmation.
        model.Selected = model.Rows.Single(r => r.Abbreviation == "trye");
        dlg.Click("Templates.Delete");
        Assert.DoesNotContain(model.Rows, r => r.Abbreviation == "trye");

        // OK writes the differences: the added template, the modified built-in, a tombstone.
        dlg.Click("Ok");
        await running;
        var path = Path.Combine(h.Services.UserPaths.RoamingDirectory, "templates.json");
        using var json = System.Text.Json.JsonDocument.Parse(h.Services.FileSystem.ReadAllBytes(path));
        var entries = json.RootElement.GetProperty("templates").EnumerateArray()
            .Select(e => (e.GetProperty("abbreviation").GetString(), e.GetProperty("body").GetString(), e.GetProperty("deleted").GetBoolean())).ToList();
        Assert.Equal([("wl", "WriteLn('$end$');", false), ("hello", "WriteLn('Hello');", false), ("trye", "", true)], entries);
        Assert.Equal(1, json.RootElement.GetProperty("formatVersion").GetInt32());

        // The page shows the saved state next time.
        running = h.Services.Commands.ExecuteAsync("tools.options", OptionsViewModel.CodeTemplatesPage);
        dlg = await h.WaitForDialogAsync("Options");
        model = ((OptionsViewModel)dlg.Window.Model).CodeTemplates;
        Assert.Contains(model.Rows, r => r.Abbreviation == "hello" && r.Description == "greeting");
        Assert.DoesNotContain(model.Rows, r => r.Abbreviation == "trye");
        Assert.True(dlg.Find<CheckBox>("ExpandOnTab").IsChecked);
        dlg.Press("Escape");
        await running;
    }

    /// <summary>dialogs.md §9.8 (all P5).</summary>
    private static readonly (string Field, string Label, ISettingKey Key, bool Enabled)[] DesignerFields =
    [
        ("ShowGrid", "Show _grid", SettingsCatalog.DesignerShowGrid, true),
        ("SnapToGrid", "_Snap to grid", SettingsCatalog.DesignerSnapToGrid, true),
        ("GridSizeX", "Grid size _X:", SettingsCatalog.DesignerGridSizeX, true),
        ("GridSizeY", "Grid size _Y:", SettingsCatalog.DesignerGridSizeY, true),
        ("UseGuidelines", "Use _alignment guidelines", SettingsCatalog.DesignerUseGuidelines, true),
        ("GuidelineDistance", "Guideline snap _distance:", SettingsCatalog.DesignerGuidelineSnapDistance, true),
        ("HandleSize", "Selection _handle size:", SettingsCatalog.DesignerHandleSize, true),
        ("ShowCaptions", "Show _names of non-visual components", SettingsCatalog.DesignerShowComponentCaptions, true),
        ("DefaultView", "_Open forms in:", SettingsCatalog.DesignerDefaultView, true),
        ("Arrangement", "Arrange _properties by:", SettingsCatalog.ObjectInspectorArrangement, true),
        ("ShowDescription", "Show property d_escription pane", SettingsCatalog.ObjectInspectorShowDescription, true),
        ("PaletteView", "_Tool Palette shows:", SettingsCatalog.ToolPaletteView, true),
    ];

    /// <summary>
    /// dialogs.md §9.8 (I-132): at Phase 5 every Designer field is bound to its key and enabled, the combos offer the
    /// listed values, range errors use the page's messages, and OK writes the keys; the designer reads them live through
    /// its settings (<c>designer.showComponentCaptions</c> defaults to true, the catalog default).
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "5")]
    [Trait("Req", "UI-DLG-094")]
    public async Task DesignerPageBindsSettings()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var settings = h.Services.Settings;
        var designer = new Core.Designer.SettingsDesignerSettings(settings);
        Assert.True(designer.ShowComponentCaptions);
        Assert.True(new Core.Designer.DefaultDesignerSettings().ShowComponentCaptions);

        var running = h.Services.Commands.ExecuteAsync("tools.options", "Designer");
        var dlg = await h.WaitForDialogAsync("Options");
        Assert.Equal("Designer", PageTitle(dlg));
        Assert.False(dlg.Find<TextBlock>("LaterVersion").IsEffectivelyVisible);
        CheckFields(dlg, settings, DesignerFields);
        Assert.Equal(["5", "7", "9", "11"], ChoiceTexts(dlg, "HandleSize"));
        Assert.Equal(["Design view", "Code view"], ChoiceTexts(dlg, "DefaultView"));
        Assert.Equal(["Category", "Name"], ChoiceTexts(dlg, "Arrangement"));
        Assert.Equal(["List with names", "Icons only"], ChoiceTexts(dlg, "PaletteView"));

        // Range checks.
        SetText(dlg, "GridSizeX", "1");
        Assert.Equal("Enter a grid size from 2 to 64.", dlg.ErrorText("GridSizeX"));
        SetText(dlg, "GuidelineDistance", "17");
        Assert.Equal("Enter a distance from 1 to 16.", dlg.ErrorText("GuidelineDistance"));
        Assert.False(dlg.Find<Button>("Ok").IsEnabled);

        // Every field changed in the UI; OK writes exactly these keys.
        SetText(dlg, "GridSizeX", "16");
        SetText(dlg, "GridSizeY", "4");
        SetText(dlg, "GuidelineDistance", "6");
        foreach (var check in (string[])["ShowGrid", "SnapToGrid", "UseGuidelines", "ShowCaptions", "ShowDescription"])
        {
            dlg.Find<Control>(check).BringIntoView();
            Dispatcher.UIThread.RunJobs();
            dlg.Click(check);
        }

        ((ComboBox)dlg.Find<Control>("HandleSize")).SelectedIndex = 2;
        ((ComboBox)dlg.Find<Control>("DefaultView")).SelectedIndex = 1;
        ((ComboBox)dlg.Find<Control>("Arrangement")).SelectedIndex = 1;
        ((ComboBox)dlg.Find<Control>("PaletteView")).SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        dlg.Click("Ok");
        await running;
        Assert.False(settings.Get(SettingsCatalog.DesignerShowGrid));
        Assert.False(settings.Get(SettingsCatalog.DesignerSnapToGrid));
        Assert.Equal(16, settings.Get(SettingsCatalog.DesignerGridSizeX));
        Assert.Equal(4, settings.Get(SettingsCatalog.DesignerGridSizeY));
        Assert.False(settings.Get(SettingsCatalog.DesignerUseGuidelines));
        Assert.Equal(6, settings.Get(SettingsCatalog.DesignerGuidelineSnapDistance));
        Assert.Equal(9, settings.Get(SettingsCatalog.DesignerHandleSize));
        Assert.False(settings.Get(SettingsCatalog.DesignerShowComponentCaptions));
        Assert.Equal("code", settings.Get(SettingsCatalog.DesignerDefaultView));
        Assert.Equal("name", settings.Get(SettingsCatalog.ObjectInspectorArrangement));
        Assert.False(settings.Get(SettingsCatalog.ObjectInspectorShowDescription));
        Assert.Equal("icons", settings.Get(SettingsCatalog.ToolPaletteView));

        // The designer reads them (no fixed defaults any more).
        Assert.Equal((false, false, 16, 4, false, 6, 9, false),
            (designer.ShowGrid, designer.SnapToGrid, designer.GridSizeX, designer.GridSizeY, designer.UseGuidelines,
                designer.GuidelineSnapDistance, designer.HandleSize, designer.ShowComponentCaptions));
        settings.Set(SettingsCatalog.DesignerGuidelineMargin, 12);
        Assert.Equal(12, designer.GuidelineMargin);
    }
    /// <summary>
    /// dialogs.md §9.6 (D-109): <c>AutoSwitchLayout</c> on Environment › General is bound to <c>layout.autoSwitch</c>; it is
    /// disabled before Phase 5 and editable from Phase 5; OK writes the key.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "5")]
    [Trait("Req", "UI-DLG-094")]
    public async Task AutoSwitchLayoutBinds()
    {
        // Before Phase 5 (phase gating, UI-DLG-090): the field is disabled and a later-phase page shows the note.
        await using (var early = await HeadlessHarness.StartAsync(o => o.ImplementedPhase = 4))
        {
            var running = early.Services.Commands.ExecuteAsync("tools.options", "Environment.General");
            var dlg = await early.WaitForDialogAsync("Options");
            CheckFields(dlg, early.Services.Settings,
                [("AutoSwitchLayout", "Switch window la_yout automatically", SettingsCatalog.LayoutAutoSwitch, false)]);
            dlg.Click("Page.Designer");
            Assert.Equal("These options take effect in a later version.", dlg.Find<TextBlock>("LaterVersion").Text);
            Assert.True(dlg.Find<TextBlock>("LaterVersion").IsEffectivelyVisible);
            Assert.False(dlg.Find<Control>("ShowGrid").IsEffectivelyEnabled);
            dlg.Click("Cancel");
            await running;
        }

        await using var h = await HeadlessHarness.StartAsync();
        var settings = h.Services.Settings;
        var shown = h.Services.Commands.ExecuteAsync("tools.options", "Environment.General");
        var options = await h.WaitForDialogAsync("Options");
        CheckFields(options, settings, [("AutoSwitchLayout", "Switch window la_yout automatically", SettingsCatalog.LayoutAutoSwitch, true)]);
        Assert.True(options.Find<CheckBox>("AutoSwitchLayout").IsChecked);
        options.Find<Control>("AutoSwitchLayout").BringIntoView();
        Dispatcher.UIThread.RunJobs();
        options.Click("AutoSwitchLayout");
        Assert.True(settings.Get(SettingsCatalog.LayoutAutoSwitch)); // pending until OK
        options.Click("Ok");
        await shown;
        Assert.False(settings.Get(SettingsCatalog.LayoutAutoSwitch));
    }

    /// <summary>
    /// phase8-delivery.md IDE-P8-002, 003 (I-230): the phase-8 field <c>Language</c> of Environment › General (dialogs.md
    /// §9.6, "disabled until P8+") is shown disabled; delivered as <c>options:Environment.General.Language</c> it is
    /// enabled — but not in a composition pinned below phase 7.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "IDE-P8-002")]
    [Trait("Req", "IDE-P8-003")]
    public async Task DeliveredPhase8FieldIsEnabled()
    {
        (string, string, ISettingKey, bool)[] Language(bool enabled) => [("Language", "_Language:", SettingsCatalog.EnvironmentLanguage, enabled)];

        async Task CheckAsync(HeadlessHarness harness, bool enabled)
        {
            var running = harness.Services.Commands.ExecuteAsync("tools.options", "Environment.General");
            var dlg = await harness.WaitForDialogAsync("Options");
            CheckFields(dlg, harness.Services.Settings, Language(enabled));
            Assert.Equal(["English"], ChoiceTexts(dlg, "Language"));
            Assert.True(dlg.Find<Control>("Theme").IsEffectivelyEnabled); // the page itself is not a later-phase page
            Assert.False(dlg.Find<TextBlock>("LaterVersion").IsEffectivelyVisible);
            dlg.Click("Cancel");
            await running;
        }

        await using var h = await HeadlessHarness.StartAsync();
        Assert.DoesNotContain("options:Environment.General.Language", Core.Composition.Phase8Features.Delivered);
        await CheckAsync(h, enabled: false); // not delivered: as before

        using (Core.Composition.Phase8Features.WithDelivered("options:Environment.General.Language"))
        {
            await CheckAsync(h, enabled: true);
            await using var early = await HeadlessHarness.StartAsync(o => o.ImplementedPhase = 5);
            await CheckAsync(early, enabled: false);
        }

        await CheckAsync(h, enabled: false); // the scope ended
    }

    /// <summary>
    /// IDE-TGT-003 (F01-010, I-227): Environment › General, group "Build", has the numeric field <c>BuildJobs</c>
    /// (<c>Parallel build &amp;jobs:</c>, 0…256) bound to <c>build.jobs</c> with the hint "0 = automatic (one per processor)";
    /// it is delivered (<c>options:Environment.General.BuildJobs</c>), so it is enabled in the product's composition and
    /// disabled in one pinned below phase 7; a value outside the range blocks OK; OK writes the key.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "IDE-TGT-003")]
    public async Task BuildJobsFieldEditsSetting()
    {
        (string, string, ISettingKey, bool)[] Jobs(bool enabled) => [("BuildJobs", "Parallel build _jobs:", SettingsCatalog.BuildJobs, enabled)];
        Assert.Contains("options:Environment.General.BuildJobs", Core.Composition.Phase8Features.Delivered);

        await using (var early = await HeadlessHarness.StartAsync(o => o.ImplementedPhase = 5))
        {
            var pinned = early.Services.Commands.ExecuteAsync("tools.options", "Environment.General");
            var disabled = await early.WaitForDialogAsync("Options");
            CheckFields(disabled, early.Services.Settings, Jobs(false));
            disabled.Click("Cancel");
            await pinned;
        }

        await using var h = await HeadlessHarness.StartAsync();
        var settings = h.Services.Settings;
        var running = h.Services.Commands.ExecuteAsync("tools.options", "Environment.General");
        var dlg = await h.WaitForDialogAsync("Options");
        var model = (OptionsViewModel)dlg.Window.Model;

        CheckFields(dlg, settings, Jobs(true));
        Assert.Equal(OptionFieldKind.Integer, model.Field("BuildJobs").Kind);
        Assert.IsType<NumericUpDown>(dlg.Find<Control>("BuildJobs"));
        Assert.Equal("0", FieldText(dlg, "BuildJobs"));
        var hint = dlg.Find<TextBlock>("BuildJobs.Hint");
        Assert.Equal("0 = automatic (one per processor)", hint.Text);
        Assert.True(hint.IsEffectivelyVisible);

        // The field is the page's last one, under the group heading "Build".
        var page = model.Pages.Single(p => p.Id == OptionsViewModel.EnvironmentGeneralPage);
        Assert.Same(model.Field("BuildJobs"), page.Fields[^1]);
        Assert.Equal("Build", model.Field("BuildJobs").GroupHeading);
        Assert.Single(dlg.Window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Build" && t.IsEffectivelyVisible);

        // 0…256: a value outside the range shows the inline message and blocks OK and Apply.
        foreach (var bad in new[] { "257", "-1", "x" })
        {
            SetText(dlg, "BuildJobs", bad);
            Assert.Equal("Enter a number of jobs from 0 to 256 (0 = automatic).", dlg.ErrorText("BuildJobs"));
            Assert.False(dlg.Find<Button>("Ok").IsEnabled);
            Assert.False(dlg.Find<Button>("Apply").IsEnabled);
        }

        SetText(dlg, "BuildJobs", "256");
        Assert.Null(dlg.ErrorText("BuildJobs"));
        Assert.True(dlg.Find<Button>("Ok").IsEnabled);
        SetText(dlg, "BuildJobs", "8");
        Assert.Null(dlg.ErrorText("BuildJobs"));
        Assert.Equal(0, settings.Get(SettingsCatalog.BuildJobs)); // pending until OK
        Assert.True(dlg.Find<Button>("BuildJobs.Reset").IsEffectivelyVisible);

        dlg.Click("Ok");
        await running;
        Assert.Equal(8, settings.Get(SettingsCatalog.BuildJobs));

        // The stored value is shown the next time; Reset returns to automatic.
        var again = h.Services.Commands.ExecuteAsync("tools.options", "Environment.General");
        var second = await h.WaitForDialogAsync("Options");
        CheckFields(second, settings, Jobs(true));
        Assert.Equal("8", FieldText(second, "BuildJobs"));
        ((OptionsViewModel)second.Window.Model).Field("BuildJobs").Reset();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("0", FieldText(second, "BuildJobs"));
        second.Click("Ok");
        await again;
        Assert.Equal(0, settings.Get(SettingsCatalog.BuildJobs));
    }

    /// <summary>
    /// DSG-SUR-212, UI-DSG-133 (D-116): Options › Designer has the field <c>GuidelineSpacing</c> for
    /// <c>designer.guidelineMargin</c> directly after <c>GuidelineDistance</c>; 0–64, else the range message; enabled
    /// only while <c>UseGuidelines</c> is checked; OK writes the key and the designer's settings read it live.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-212")]
    [Trait("Req", "UI-DSG-133")]
    public async Task GuidelineSpacingFieldEditsSetting()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var settings = h.Services.Settings;
        var designer = new Core.Designer.SettingsDesignerSettings(settings);
        Assert.Equal((8, 8), (settings.Get(SettingsCatalog.DesignerGuidelineMargin), designer.GuidelineMargin));

        var running = h.Services.Commands.ExecuteAsync("tools.options", "Designer");
        var dlg = await h.WaitForDialogAsync("Options");
        CheckFields(dlg, settings, [("GuidelineSpacing", "Guideline spa_cing:", SettingsCatalog.DesignerGuidelineMargin, true)]);
        var model = (OptionsViewModel)dlg.Window.Model;
        var page = model.Pages.Single(p => p.Fields.Any(f => f.Id == "GuidelineSpacing"));
        var ids = page.Fields.Select(f => f.Id).ToList();
        Assert.Equal(ids.IndexOf("GuidelineDistance") + 1, ids.IndexOf("GuidelineSpacing"));
        Assert.True(dlg.Find<Control>("GuidelineDistance").Bounds.Height > 0);

        // Its mnemonic is free on the page.
        var mnemonics = page.Fields.Select(f => f.Label).Where(l => l.Contains('_', StringComparison.Ordinal))
            .Select(l => char.ToUpperInvariant(l[l.IndexOf('_', StringComparison.Ordinal) + 1])).ToList();
        Assert.Equal(mnemonics.Count, mnemonics.Distinct().Count());

        // The range: 0–64.
        SetText(dlg, "GuidelineSpacing", "65");
        Assert.Equal("Enter a distance from 0 to 64.", dlg.ErrorText("GuidelineSpacing"));
        Assert.False(dlg.Find<Button>("Ok").IsEnabled);
        SetText(dlg, "GuidelineSpacing", "-1");
        Assert.Equal("Enter a distance from 0 to 64.", dlg.ErrorText("GuidelineSpacing"));
        SetText(dlg, "GuidelineSpacing", "64");
        Assert.True(string.IsNullOrEmpty(dlg.ErrorText("GuidelineSpacing")));
        SetText(dlg, "GuidelineSpacing", "0");
        Assert.True(string.IsNullOrEmpty(dlg.ErrorText("GuidelineSpacing")));
        Assert.True(dlg.Find<Button>("Ok").IsEnabled);

        // Enabled only while UseGuidelines is checked.
        dlg.Find<Control>("UseGuidelines").BringIntoView();
        Dispatcher.UIThread.RunJobs();
        dlg.Click("UseGuidelines");
        Assert.False(((CheckBox)dlg.Find<Control>("UseGuidelines")).IsChecked);
        Assert.False(dlg.Find<Control>("GuidelineSpacing").IsEffectivelyEnabled);
        dlg.Click("UseGuidelines");
        Assert.True(dlg.Find<Control>("GuidelineSpacing").IsEffectivelyEnabled);

        // OK writes the key; the designer reads it (per pointer move, as the other snap settings).
        SetText(dlg, "GuidelineSpacing", "12");
        dlg.Click("Ok");
        await running;
        Assert.Equal(12, settings.Get(SettingsCatalog.DesignerGuidelineMargin));
        Assert.Equal(12, designer.GuidelineMargin);
        Assert.True(settings.Get(SettingsCatalog.DesignerUseGuidelines));

        // The page shows the stored value next time.
        running = h.Services.Commands.ExecuteAsync("tools.options", "Designer");
        dlg = await h.WaitForDialogAsync("Options");
        Assert.Equal("12", FieldText(dlg, "GuidelineSpacing"));
        dlg.Press("Escape");
        await running;
    }
}
