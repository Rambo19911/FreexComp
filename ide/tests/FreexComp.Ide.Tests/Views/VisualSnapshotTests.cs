using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;
using FreexComp.Ide.Views;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// I-240: the visual check of a Phase 8 UI card — the real UI rendered with the Skia renderer in light and dark theme, at
/// scale 1 and 2 and in two window sizes, written under <c>artifacts/visual/</c> for inspection, plus the layout rules
/// that can be measured (<see cref="VisualSnapshot.LayoutDefects"/>). Each test runs its body in a child process that
/// renders with Skia (<see cref="SkiaProcess"/>).
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class VisualSnapshotTests
{
    private static readonly string[] Themes = ["light", "dark"];
    private static readonly double[] Scales = [1.0, 2.0];

    /// <summary>
    /// Tools › Options, page Environment › General (dialogs.md §9.6) at its stated size 820 × 580 — which is also
    /// its minimum (UI-DLG-005) — and enlarged to 1100 × 760, in three states: the top of the page with the disabled Language field (I-230),
    /// the Build group with the jobs field and its hint (I-227), and the Build group with a value outside 0…256 so that
    /// the range message shows.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-TGT-003")]
    [Trait("Req", "IDE-P8-002")]
    public async Task OptionsEnvironmentGeneral_RendersAllVariants_NoClippingOrOverlap()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(VisualSnapshotTests));
            return;
        }

        Size[] sizes = [new(820, 580), new(1100, 760)];
        (string Subject, string? Jobs, bool AtEnd)[] states =
        [
            ("options-environment-general", null, false),
            ("options-environment-general-build", null, true),
            ("options-environment-general-build-range", "257", true),
        ];
        foreach (var state in states)
        {
            if (Directory.Exists(VisualSnapshot.FolderOf(state.Subject)))
                Directory.Delete(VisualSnapshot.FolderOf(state.Subject), recursive: true); // no file of an earlier run counts
        }

        var files = new List<(string Subject, string Theme, double Scale, Size Size, SnapshotFile File)>();
        var defects = new List<(string Variant, string Defect)>();
        foreach (var theme in Themes)
        {
            await using var h = await HeadlessHarness.StartAsync(o => o.Settings["appearance.theme"] = theme);
            var running = h.Services.Commands.ExecuteAsync("tools.options", "Environment.General");
            var dlg = await h.WaitForDialogAsync("Options");
            try
            {
                var jobs = dlg.Find<NumericUpDown>("BuildJobs");
                var scroll = jobs.FindAncestorOfType<ScrollViewer>()!;
                Assert.False(dlg.Find<ComboBox>("Language").IsEnabled);
                Assert.True(jobs.IsEnabled);

                foreach (var state in states)
                {
                    jobs.Text = state.Jobs ?? "0";
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(state.Jobs is null ? null : "Enter a number of jobs from 0 to 256 (0 = automatic).", dlg.ErrorText("BuildJobs"));

                    foreach (var scale in Scales)
                    {
                        foreach (var size in sizes)
                        {
                            VisualSnapshot.Arrange(dlg.Window, size, scale);
                            if (state.AtEnd)
                                scroll.ScrollToEnd();
                            else
                                scroll.ScrollToHome();
                            Dispatcher.UIThread.RunJobs();

                            var variant = $"{state.Subject} {VisualSnapshot.FileName(theme, scale, size)}";
                            Assert.Equal(size, dlg.Window.ClientSize);
                            Assert.Equal(scale, dlg.Window.RenderScaling);
                            Assert.True(InViewPort(scroll, dlg.Find<Control>(state.AtEnd ? "BuildJobs" : "Language")), variant + ": the subject is not in view");
                            if (state.AtEnd)
                                Assert.True(InViewPort(scroll, dlg.Find<TextBlock>("BuildJobs.Hint")), variant + ": the hint is not in view");
                            defects.AddRange(VisualSnapshot.LayoutDefects(dlg.Window).Select(d => (variant, d)));
                            files.Add((state.Subject, theme, scale, size, VisualSnapshot.Capture(dlg.Window, state.Subject, theme)));
                        }
                    }
                }

            }
            finally
            {
                dlg.Click("Cancel"); // also after a failure, so that the failure is what the test reports
                await running;
            }
        }

        // 8 files per subject, each a real rendering of the expected pixel size (scale 2 = double).
        Assert.Equal(states.Length * 8, files.Count);
        foreach (var state in states)
        {
            Assert.Equal(8, Directory.GetFiles(VisualSnapshot.FolderOf(state.Subject), "*.png").Length);
        }

        foreach (var (_, _, scale, size, file) in files)
        {
            Assert.True(new FileInfo(file.Path).Length > 0, file.Path);
            var expected = ((int)(size.Width * scale), (int)(size.Height * scale));
            Assert.Equal(expected, (file.PixelWidth, file.PixelHeight));
            Assert.Equal(expected, VisualSnapshot.PngSize(file.Path));
            Assert.True(file.DistinctColors >= 16, $"{file.Path}: only {file.DistinctColors} colours — not a real rendering");
        }

        // The same variant is darker in the dark theme.
        foreach (var light in files.Where(f => f.Theme == "light"))
        {
            var dark = files.Single(f => f.Theme == "dark" && f.Subject == light.Subject && f.Scale == light.Scale && f.Size == light.Size);
            Assert.True(dark.File.MeanLuminance + 64 < light.File.MeanLuminance, $"{dark.File.Path}: {dark.File.MeanLuminance} against {light.File.MeanLuminance}");
        }

        Assert.True(defects.Count == 0, Report(defects));
    }

    /// <summary>
    /// I-241: every numeric field of the Options dialog shows the widest value it allows (the longer of the ends of its
    /// range) completely — on every page that has one, in both themes and at both scales; one PNG per page and variant
    /// under <c>options-&lt;page&gt;-widest-values</c>.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    public async Task OptionsNumericFields_ShowTheWidestAllowedValue()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(VisualSnapshotTests));
            return;
        }

        var size = new Size(820, 580);
        var defects = new List<(string Variant, string Defect)>();
        var checkedFields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var theme in Themes)
        {
            await using var h = await HeadlessHarness.StartAsync(o => o.Settings["appearance.theme"] = theme);
            var running = h.Services.Commands.ExecuteAsync("tools.options", "Environment.General");
            var dlg = await h.WaitForDialogAsync("Options");
            try
            {
                var model = (OptionsViewModel)dlg.Window.Model;
                foreach (var page in model.Pages.Where(p => p.Fields.Any(f => f.Kind == OptionFieldKind.Integer)))
                {
                    var subject = $"options-{page.Id.ToLowerInvariant().Replace('.', '-')}-widest-values";
                    if (theme == Themes[0] && Directory.Exists(VisualSnapshot.FolderOf(subject)))
                        Directory.Delete(VisualSnapshot.FolderOf(subject), recursive: true);
                    model.SelectRow(model.Rows.First(r => r.Page == page));
                    Dispatcher.UIThread.RunJobs();
                    NumericUpDown? first = null;
                    foreach (var field in page.Fields.Where(f => f.Kind == OptionFieldKind.Integer))
                    {
                        var widest = field.Key.Ranges.SelectMany(r => new[] { r.Minimum, r.Maximum })
                            .Select(v => ((int)v).ToString(System.Globalization.CultureInfo.InvariantCulture))
                            .OrderByDescending(t => t.Length).First();
                        var numeric = dlg.Find<NumericUpDown>(field.Id);
                        numeric.Text = widest;
                        Dispatcher.UIThread.RunJobs();
                        Assert.Null(field.Error);
                        Assert.Equal(widest, numeric.Text);
                        first ??= numeric;
                        checkedFields.Add(field.Id);
                    }

                    foreach (var scale in Scales)
                    {
                        VisualSnapshot.Arrange(dlg.Window, size, scale);
                        first!.BringIntoView();
                        var variant = $"{subject} {VisualSnapshot.FileName(theme, scale, size)}";
                        defects.AddRange(VisualSnapshot.LayoutDefects(dlg.Window).Select(d => (variant, d)));
                        var file = VisualSnapshot.Capture(dlg.Window, subject, theme);
                        Assert.Equal(((int)(size.Width * scale), (int)(size.Height * scale)), VisualSnapshot.PngSize(file.Path));
                    }
                }
            }
            finally
            {
                dlg.Click("Cancel");
                await running;
            }
        }

        // Every integer field of the dialog was filled, the two named in the bug among them.
        Assert.Superset(new HashSet<string> { "CompletionDelay", "BuildJobs", "TabWidth", "RightMargin", "RecoveryInterval", "GridSizeX" }, checkedFields);
        Assert.True(defects.Count == 0, Report(defects));
    }

    /// <summary>
    /// I-242: the labels of the footer buttons (OK, Cancel, Apply) are laid out completely inside their buttons, and the
    /// buttons inside the client area of the dialog, at both scales.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-003")]
    public async Task OptionsFooterButtons_LabelsAreNotCutOff()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(VisualSnapshotTests));
            return;
        }

        await using var h = await HeadlessHarness.StartAsync();
        var running = h.Services.Commands.ExecuteAsync("tools.options", "Environment.General");
        var dlg = await h.WaitForDialogAsync("Options");
        try
        {
            foreach (var scale in Scales)
            {
                VisualSnapshot.Arrange(dlg.Window, new Size(820, 580), scale);
                foreach (var id in new[] { "Ok", "Cancel", "Apply" })
                {
                    var button = dlg.Find<Button>(id);
                    Assert.Equal(26, button.Bounds.Height); // UI-DLG-003
                    Assert.True(button.Bounds.Width >= 80, $"{id}: {button.Bounds.Width} wide");
                    var label = Assert.Single(button.GetVisualDescendants().OfType<TextBlock>());
                    Assert.True(label.IsEffectivelyVisible);
                    Assert.Empty(VisualSnapshot.LayoutDefects(button));
                    var inWindow = new Rect(button.TranslatePoint(default, dlg.Window)!.Value, button.Bounds.Size);
                    Assert.True(new Rect(dlg.Window.ClientSize).Contains(inWindow), $"{id}: {inWindow} outside {dlg.Window.ClientSize}");
                }
            }
        }
        finally
        {
            dlg.Click("Cancel");
            await running;
        }
    }

    /// <summary>
    /// I-243: the browse button of <c>Default project folder</c> shows its "…" — laid out completely and drawn (more than
    /// one colour where the glyph is) in both themes — and has a tool tip and an accessible name.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    public async Task OptionsBrowseButton_ShowsItsGlyph()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(VisualSnapshotTests));
            return;
        }

        foreach (var theme in Themes)
        {
            await using var h = await HeadlessHarness.StartAsync(o => o.Settings["appearance.theme"] = theme);
            var running = h.Services.Commands.ExecuteAsync("tools.options", "Environment.General");
            var dlg = await h.WaitForDialogAsync("Options");
            try
            {
                var browse = dlg.Find<Button>("DefaultProjectsDir.Browse");
                Assert.Equal("…", browse.Content);
                Assert.Equal("Browse", Avalonia.Automation.AutomationProperties.GetName(browse));
                Assert.Equal("Browse", ToolTip.GetTip(browse));
                foreach (var scale in Scales)
                {
                    VisualSnapshot.Arrange(dlg.Window, new Size(820, 580), scale);
                    browse.BringIntoView();
                    Assert.Empty(VisualSnapshot.LayoutDefects(browse));
                    var glyph = Assert.Single(browse.GetVisualDescendants().OfType<TextBlock>());
                    var ink = VisualSnapshot.DistinctColors(dlg.Window, glyph);
                    Assert.True(ink >= 2, $"{theme} {scale}x: the glyph area is one flat colour");
                }
            }
            finally
            {
                dlg.Click("Cancel");
                await running;
            }
        }
    }

    /// <summary>
    /// I-244: the main window with the Messages pane at its minimum size 800 × 500 (main-window.md) and at
    /// 1920 × 1040 (standing in for a maximized window), in both themes and two states: the Welcome page in front and
    /// no messages, and the code editor in front after a build with more messages than the pane shows (the list
    /// scrolls). Every column
    /// header of the Messages pane, and every Line and Col cell, is laid out completely, inside every control above it
    /// up to the window; the last column keeps clear of the strip at the right edge in which the scroll bar of the list
    /// appears; the layout rules hold for the whole window. PNGs under <c>main-window-welcome-messages</c> and
    /// <c>main-window-messages-rows</c>.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-011")]
    public async Task MainWindow_MessagesPaneHeadersNotTruncated()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(VisualSnapshotTests));
            return;
        }

        const double scrollBarStrip = 16; // space.6: the width of the scroll bar of a list
        string[] subjects = ["main-window-welcome-messages", "main-window-messages-rows"];
        Size[] sizes = [new(800, 500), new(1920, 1040)];
        string[] columns = ["Severity", "Code", "Description", "File", "Line", "Col"];
        foreach (var subject in subjects)
        {
            if (Directory.Exists(VisualSnapshot.FolderOf(subject)))
                Directory.Delete(VisualSnapshot.FolderOf(subject), recursive: true);
        }

        var defects = new List<(string Variant, string Defect)>();
        var files = new List<(string Subject, string Theme, Size Size, SnapshotFile File)>();
        foreach (var theme in Themes)
        {
            await using var h = await BuildScenario.StartAsync(o =>
            {
                o.Settings["appearance.theme"] = theme;
                o.Settings.Remove("environment.showWelcomeOnStartup"); // starts like a first run: with the Welcome page
            }, implementedPhase: null);
            Assert.Equal((800.0, 500.0), (h.Window.MinWidth, h.Window.MinHeight));
            Assert.Equal("Messages", h.Services.Layout.Layout.Bottom.ActiveTool);
            var list = h.Find<ListBox>("Tool.Messages.List");

            foreach (var subject in subjects)
            {
                var withRows = subject == subjects[1];
                // The Welcome page in front for the first state, the code editor (the project's main source) for the second.
                var front = h.Services.Documents.Documents.Single(d => d is Core.ViewModels.WelcomeDocumentViewModel != withRows);
                h.Services.Documents.Activate(front);
                Dispatcher.UIThread.RunJobs();
                if (withRows)
                {
                    // 40 messages; the first with a long line and column number.
                    h.Build.Enqueue(FakeBuildEngine.Failure(BuildScenario.ProjectPath(h), [.. Enumerable.Range(0, 40).Select(i =>
                        FakeBuildEngine.Diag("E2003", FreexComp.Compiler.DiagnosticSeverity.Error, $"Undeclared identifier 'Foo{i}'",
                            BuildScenario.UnitPath(h), i == 0 ? 123456 : i + 1, i == 0 ? 1024 : 3, 3))]));
                    h.Press("Ctrl+Shift+B");
                    await h.WaitForAsync(() => h.Services.State.Current.State == Core.Models.IdeState.Idle && h.Services.Build.ResultText is not null,
                        "build finished");
                    Dispatcher.UIThread.RunJobs();
                }

                foreach (var size in sizes)
                {
                    VisualSnapshot.Arrange(h.Window, size, 1.0);
                    var variant = $"{subject} {VisualSnapshot.FileName(theme, 1.0, size)}";
                    Assert.Equal(size, h.Window.ClientSize);
                    Assert.True(h.Find<Control>(AutomationIds.DocumentTab("Welcome")).IsEffectivelyVisible, variant + ": no Welcome page");
                    Assert.Same(front, h.Services.Documents.ActiveDocument);
                    Assert.Equal(!withRows, h.Window.GetVisualDescendants().OfType<WelcomeDocumentView>().Any(v => v.IsEffectivelyVisible));
                    var listRight = list.TranslatePoint(new Point(list.Bounds.Width, 0), h.Window)!.Value.X;

                    foreach (var column in columns)
                    {
                        var cell = h.Find<Border>("Tool.Messages.Column." + column);
                        Assert.True(cell.IsEffectivelyVisible, $"{variant}: column {column} is not shown");
                        var label = Assert.IsType<TextBlock>(cell.Child);
                        Assert.Equal(column == "Severity", string.IsNullOrEmpty(label.Text));
                        // UI-TW-011: type.caption on bg.header, 24 high.
                        Assert.Equal(11, label.FontSize);
                        var strip = Assert.IsType<Grid>(cell.Parent);
                        Assert.Equal(24, strip.Bounds.Height);
                        Assert.Equal(Avalonia.Media.Color.Parse(theme == "light" ? "#ECEEEB" : "#262B29"),
                            Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(strip.Background).Color);
                        if (VisualSnapshot.HiddenPart(cell, h.Window) is { } hiddenCell)
                            defects.Add((variant, $"cut: header cell {column} — {hiddenCell}"));
                        if (column != "Severity")
                            AddCutText(defects, variant, $"header '{label.Text}'", label, h.Window, column == "Col" ? listRight - scrollBarStrip : listRight);
                    }

                    var cells = h.Window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible)
                        .Select(t => (Id: Avalonia.Automation.AutomationProperties.GetAutomationId(t) ?? "", Text: t))
                        .Where(c => c.Id.StartsWith("Tool.Messages.Row.", StringComparison.Ordinal)).ToList();
                    foreach (var (id, text) in cells.Where(c => c.Id.EndsWith(".Line", StringComparison.Ordinal) || c.Id.EndsWith(".Col", StringComparison.Ordinal)))
                        AddCutText(defects, variant, $"cell {id} '{text.Text}'", text, h.Window, id.EndsWith(".Col", StringComparison.Ordinal) ? listRight - scrollBarStrip : listRight);
                    if (withRows)
                    {
                        Assert.Contains(cells, c => c.Id == "Tool.Messages.Row.0.Line" && c.Text.Text == "123456");
                        Assert.Contains(cells, c => c.Id == "Tool.Messages.Row.0.Col" && c.Text.Text == "1024");
                        var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();
                        Assert.True(scroll.Extent.Height > scroll.Viewport.Height, variant + ": the list does not scroll");
                    }
                    else
                    {
                        Assert.Empty(cells);
                        Assert.True(h.Find<TextBlock>("Tool.Messages.Empty").IsEffectivelyVisible);
                    }

                    defects.AddRange(VisualSnapshot.LayoutDefects(h.Window).Select(d => (variant, d)));
                    files.Add((subject, theme, size, VisualSnapshot.Capture(h.Window, subject, theme)));
                }
            }
        }

        foreach (var subject in subjects)
            Assert.Equal(4, Directory.GetFiles(VisualSnapshot.FolderOf(subject), "*.png").Length);
        foreach (var (_, _, size, file) in files)
        {
            Assert.Equal(((int)size.Width, (int)size.Height), VisualSnapshot.PngSize(file.Path));
            Assert.True(file.DistinctColors >= 16, $"{file.Path}: only {file.DistinctColors} colours — not a real rendering");
        }

        foreach (var light in files.Where(f => f.Theme == "light"))
        {
            var dark = files.Single(f => f.Theme == "dark" && f.Subject == light.Subject && f.Size == light.Size);
            Assert.True(dark.File.MeanLuminance + 64 < light.File.MeanLuminance, $"{dark.File.Path}: {dark.File.MeanLuminance} against {light.File.MeanLuminance}");
        }

        Assert.True(defects.Count == 0, Report(defects));
    }

    /// <summary>
    /// Card F09-054 (phase8-ui-additions.md §1): the Project Manager in the main window at its minimum size 800 × 500 and at
    /// 1920 × 1040, light and dark, in four states — the missing-files and the unlisted-files info bars above the tree
    /// (UI-TW-107, UI-TW-123); a folder tree with remembered empty folders and a new folder being named in place
    /// (UI-TW-115, UI-TW-116); a row dragged over a folder with adorner and drop-target feedback (UI-TW-120, UI-TW-121);
    /// and the Move to Folder quick pick (UI-TW-124). Only the window size varies. Beside the general layout rules:
    /// every bar button and every bar text is shown completely, the bars span the tool window, the naming editor and
    /// the drag adorner lie inside the window, and no quick-pick row is cut.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-123")]
    [Trait("Req", "UI-TW-115")]
    [Trait("Req", "UI-TW-116")]
    [Trait("Req", "UI-TW-120")]
    [Trait("Req", "UI-TW-121")]
    [Trait("Req", "UI-TW-124")]
    public async Task ProjectManagerFileOperations_RenderAtMinimumAndLargeWindow()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(VisualSnapshotTests));
            return;
        }

        string[] subjects = ["project-manager-info-bars", "project-manager-folder-naming", "project-manager-drag-feedback", "project-manager-move-quick-pick"];
        Size[] sizes = [new(800, 500), new(1920, 1040)];
        string[] bars = ["Tool.ProjectManager.InfoBar.MissingFiles", "Tool.ProjectManager.InfoBar.UnlistedFiles"];
        foreach (var subject in subjects)
        {
            if (Directory.Exists(VisualSnapshot.FolderOf(subject)))
                Directory.Delete(VisualSnapshot.FolderOf(subject), recursive: true);
        }

        var defects = new List<(string Variant, string Defect)>();
        var files = new List<(string Subject, string Theme, Size Size, SnapshotFile File)>();
        foreach (var theme in Themes)
        {
            await using var h = await HeadlessHarness.StartAsync(o =>
            {
                o.Settings["appearance.theme"] = theme;
                o.InitialFiles[@"Hello\Hello.pproj"] = """
                    { "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas",
                      "files": [ { "path": "Hello.pas" }, { "path": "Unit1.pas" }, { "path": "notes.md" }, { "path": "lib/StrUtils2.pas" },
                                 { "path": "lib/net/HttpClient.pas" }, { "path": "Missing.pas" }, { "path": "lib/Gone.pas" } ] }
                    """;
                o.InitialFiles[@"Hello\Hello.pas"] = "program Hello;\nbegin\nend.\n";
                o.InitialFiles[@"Hello\Unit1.pas"] = "unit Unit1;\ninterface\nimplementation\nend.\n";
                o.InitialFiles[@"Hello\notes.md"] = "notes\n";
                o.InitialFiles[@"Hello\lib\StrUtils2.pas"] = "unit StrUtils2;\ninterface\nimplementation\nend.\n";
                o.InitialFiles[@"Hello\lib\net\HttpClient.pas"] = "unit HttpClient;\ninterface\nimplementation\nend.\n";
                o.InitialFiles[@"Hello\Extra.pas"] = "unit Extra;\ninterface\nimplementation\nend.\n"; // unlisted
                o.InitialFiles[@"Hello\lib\More.pas"] = "unit More;\ninterface\nimplementation\nend.\n"; // unlisted
                o.InitialFiles[@"Hello\assets\.keep"] = "";
                o.InitialFiles[@"Hello\.freexcomp\session.json"] = """
                    { "formatVersion": 1, "project": "Hello.pproj",
                      "projectManager": { "expanded": ["$project", "lib/"], "selected": null, "folders": ["assets/"], "ignoredFiles": [] } }
                    """;
            });
            Assert.True(await h.Services.Projects.OpenAsync(Path.Combine(h.TempDir, "Hello", "Hello.pproj")));
            var pm = h.Services.ProjectManager;
            await h.WaitForAsync(() => !pm.IsCheckingMissingFiles && pm.UnlistedFilesBar is not null, "missing-file and unlisted-file check done");
            Assert.True(h.Services.ToolWindows.Show(Core.ViewModels.ProjectManagerViewModel.ToolName, focus: true));
            Dispatcher.UIThread.RunJobs();
            var tree = h.Find<TreeView>("Tool.ProjectManager.Tree");
            var view = tree.FindAncestorOfType<ProjectManagerView>()!;

            foreach (var subject in subjects)
            {
                var naming = subject == subjects[1];
                var dragging = subject == subjects[2];
                var picking = subject == subjects[3];
                pm.Find("lib/")!.IsExpanded = !dragging;
                Dispatcher.UIThread.RunJobs();
                if (naming)
                {
                    // A new folder under lib/, its name being edited (all selected); the bars stay above the tree.
                    pm.Select(pm.Find("lib/")!);
                    Assert.True(await pm.NewFolderAsync());
                    Dispatcher.UIThread.RunJobs();
                }

                foreach (var size in sizes)
                {
                    VisualSnapshot.Arrange(h.Window, size, 1.0);
                    var variant = $"{subject} {VisualSnapshot.FileName(theme, 1.0, size)}";
                    Assert.Equal(size, h.Window.ClientSize);
                    Assert.True(tree.IsEffectivelyVisible, variant + ": the Project Manager is not shown");
                    var viewRight = view.TranslatePoint(new Point(view.Bounds.Width, 0), h.Window)!.Value.X;
                    Point RowPoint(string key)
                    {
                        var name = h.Find<TextBlock>("Tool.ProjectManager.Row." + key + ".Name");
                        return name.TranslatePoint(new Point(10, name.Bounds.Height / 2), h.Window)!.Value;
                    }

                    if (dragging)
                    {
                        // Hello.pas pressed, moved beyond the threshold and on over the (empty) folder row assets/ — rows
                        // that are in view also in the minimum window (lib/ is collapsed for this state).
                        // A click on another row first: a second press on the same spot (the next window size) would be a
                        // double-click, which opens the file instead of dragging it.
                        h.Window.MouseDown(RowPoint("lib/"), Avalonia.Input.MouseButton.Left);
                        h.Window.MouseUp(RowPoint("lib/"), Avalonia.Input.MouseButton.Left);
                        Dispatcher.UIThread.RunJobs();
                        var start = RowPoint("Hello.pas");
                        h.Window.MouseDown(start, Avalonia.Input.MouseButton.Left);
                        Dispatcher.UIThread.RunJobs();
                        Assert.Equal(["Hello.pas"], pm.Selection.Select(n => n.Key)); // the press reached the row
                        h.Window.MouseMove(start + new Point(0, -8), Avalonia.Input.RawInputModifiers.LeftMouseButton);
                        Dispatcher.UIThread.RunJobs();
                        Assert.Equal(["Hello.pas"], pm.DraggedRows.Select(n => n.Key)); // beyond the threshold: the drag has begun
                        h.Window.MouseMove(RowPoint("assets/"), Avalonia.Input.RawInputModifiers.LeftMouseButton);
                        Dispatcher.UIThread.RunJobs();
                        var adorner = h.Find<Border>("Tool.ProjectManager.DragAdorner");
                        Assert.Equal("Hello.pas", Avalonia.Automation.AutomationProperties.GetName(adorner));
                        Assert.Same(pm.Find("assets/"), pm.DropTarget);
                        Assert.Equal("assets, drop target", Avalonia.Automation.AutomationProperties.GetName(h.Find<TreeViewItem>("Tool.ProjectManager.Row.assets/")));
                        Assert.Equal(0.5, h.Find<TextBlock>("Tool.ProjectManager.Row.Hello.pas.Name").FindAncestorOfType<ProjectNodeView>()!.Opacity);
                        var corner = adorner.TranslatePoint(new Point(adorner.Bounds.Width, adorner.Bounds.Height), h.Window)!.Value;
                        if (corner.X > size.Width + 0.5 || corner.Y > size.Height + 0.5 || adorner.Bounds.Width < 40)
                            defects.Add((variant, $"cut: drag adorner ends at {corner} ({adorner.Bounds.Width:0.#} wide)"));
                    }

                    if (picking)
                    {
                        pm.Select(pm.Find("Unit1.pas")!);
                        Dispatcher.UIThread.RunJobs();
                        await h.ExecuteAsync("project.moveFile");
                        Dispatcher.UIThread.RunJobs();
                        var popup = Assert.IsType<Core.Popups.MoveToFolderViewModel>(h.Services.QuickPick.Current);
                        Assert.Equal(["assets/", "lib/", "lib/net/", "lib/NewFolder1/"], popup.Items.Select(i => i.Id)); // tree order; the root (the file's folder) is omitted
                        foreach (var item in popup.Items)
                        {
                            var cell = h.Find<TextBlock>($"QuickPick.Item.{item.Id}.Name");
                            Assert.True(cell.IsEffectivelyVisible, $"{variant}: quick-pick row {item.Id} is not shown");
                            if (VisualSnapshot.HiddenPart(cell, h.Window) is { } hiddenCell)
                                defects.Add((variant, $"cut: quick-pick row {item.Id} — {hiddenCell}"));
                            if (VisualSnapshot.TextWidth(cell) > cell.Bounds.Width + 0.5)
                                defects.Add((variant, $"cut: quick-pick row {item.Id} needs {VisualSnapshot.TextWidth(cell):0.#} wide, has {cell.Bounds.Width:0.#}"));
                        }

                        Assert.Equal(Core.Resources.Strings.QuickPick_MoveToFolder_Title,
                            Avalonia.Automation.AutomationProperties.GetName(h.Find<Control>("QuickPick.Input")));
                        Assert.Equal("Move to Folder", h.Find<TextBox>("QuickPick.Input").PlaceholderText); // the title (UI-TW-124)
                    }

                    foreach (var id in bars)
                    {
                        var bar = h.Find<InfoBarView>(id);
                        Assert.True(bar.IsEffectivelyVisible, $"{variant}: {id} is not shown");
                        Assert.Equal(view.Bounds.Width, bar.Bounds.Width);
                        if (VisualSnapshot.HiddenPart(bar, h.Window) is { } hiddenBar)
                            defects.Add((variant, $"cut: {id} — {hiddenBar}"));
                        var text = h.Find<TextBlock>(id + ".Text");
                        if (VisualSnapshot.HiddenPart(text, h.Window) is { } hiddenText)
                            defects.Add((variant, $"cut: text of {id} — {hiddenText}"));
                        if (text.TextLayout.TextLines.Any(l => l.HasCollapsed) || text.Bounds.Height + 0.5 < text.TextLayout.Height)
                            defects.Add((variant, $"cut: text of {id} is not shown completely"));
                        foreach (var button in bar.GetVisualDescendants().OfType<Button>())
                        {
                            var name = Avalonia.Automation.AutomationProperties.GetAutomationId(button);
                            if (VisualSnapshot.HiddenPart(button, h.Window) is { } hidden)
                                defects.Add((variant, $"cut: {name} — {hidden}"));
                            var right = button.TranslatePoint(new Point(button.Bounds.Width, 0), h.Window)!.Value.X;
                            if (right > viewRight + 0.5)
                                defects.Add((variant, $"cut: {name} ends at x = {right:0.#}, beyond the tool window ({viewRight:0.#})"));
                        }
                    }

                    // The unlisted-files bar directly below the missing-files bar, the tree below both (UI-TW-123).
                    double Top(Visual v) => v.TranslatePoint(default, h.Window)!.Value.Y;
                    var missing = h.Find<InfoBarView>(bars[0]);
                    var unlisted = h.Find<InfoBarView>(bars[1]);
                    Assert.Equal(Top(missing) + missing.Bounds.Height, Top(unlisted));
                    Assert.Equal(Top(unlisted) + unlisted.Bounds.Height, Top(tree));
                    Assert.Equal(["Tool.ProjectManager.InfoBar.UnlistedFiles.Action.AddToProject", "Tool.ProjectManager.InfoBar.UnlistedFiles.Action.Show",
                        "Tool.ProjectManager.InfoBar.UnlistedFiles.Action.Ignore"],
                        unlisted.GetVisualDescendants().OfType<Button>().Select(b => Avalonia.Automation.AutomationProperties.GetAutomationId(b)));

                    // The folder tree: a derived folder with a sub-folder, a remembered empty folder (no children).
                    // (For the quick pick the tree has scrolled to the selected file; for the drag lib/ is collapsed.)
                    Assert.Empty(pm.Find("assets/")!.Children);
                    if (!picking)
                    {
                        Assert.Equal(!dragging, h.TryFind<TreeViewItem>("Tool.ProjectManager.Row.lib/net/")?.IsEffectivelyVisible == true);
                        Assert.True(h.Find<TreeViewItem>("Tool.ProjectManager.Row.assets/").IsEffectivelyVisible, variant);
                    }
                    if (naming)
                    {
                        var editor = h.Find<TextBox>("Tool.ProjectManager.Row.lib/NewFolder1/.NameEditor");
                        Assert.True(editor.IsEffectivelyVisible, variant + ": no naming editor");
                        Assert.True(editor.IsFocused, variant + ": the naming editor has lost focus");
                        Assert.Equal("NewFolder1", editor.Text);
                        Assert.Equal((0, "NewFolder1".Length), (Math.Min(editor.SelectionStart, editor.SelectionEnd), Math.Max(editor.SelectionStart, editor.SelectionEnd)));
                        if (VisualSnapshot.HiddenPart(editor, h.Window) is { } hiddenEditor)
                            defects.Add((variant, $"cut: naming editor — {hiddenEditor}"));
                        var right = editor.TranslatePoint(new Point(editor.Bounds.Width, 0), h.Window)!.Value.X;
                        if (right > viewRight + 0.5 || editor.Bounds.Width < 60)
                            defects.Add((variant, $"cut: naming editor is {editor.Bounds.Width:0.#} wide and ends at x = {right:0.#} (tool window: {viewRight:0.#})"));
                    }
                    else
                    {
                        Assert.Null(h.TryFind<TextBox>("Tool.ProjectManager.Row.lib/NewFolder1/.NameEditor"));
                    }

                    defects.AddRange(VisualSnapshot.LayoutDefects(h.Window).Select(d => (variant, d)));
                    files.Add((subject, theme, size, VisualSnapshot.Capture(h.Window, subject, theme)));

                    if (dragging)
                    {
                        // Escape cancels the drag: nothing moves.
                        h.Press("Escape");
                        h.Window.MouseUp(RowPoint("assets/"), Avalonia.Input.MouseButton.Left);
                        Dispatcher.UIThread.RunJobs();
                        Assert.Null(h.TryFind<Control>("Tool.ProjectManager.DragAdorner"));
                        Assert.NotNull(pm.Find("Hello.pas"));
                    }

                    if (picking)
                    {
                        h.Services.QuickPick.Current!.Close();
                        Dispatcher.UIThread.RunJobs();
                    }
                }

                if (naming)
                {
                    // Escape keeps the provisional name; the editor is gone for the next state.
                    pm.Find("lib/NewFolder1/")!.Rename!.Cancel();
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(h.Services.ToolWindows.Show(Core.ViewModels.ProjectManagerViewModel.ToolName, focus: true));
                    Dispatcher.UIThread.RunJobs();
                }
            }
        }

        foreach (var subject in subjects)
            Assert.Equal(4, Directory.GetFiles(VisualSnapshot.FolderOf(subject), "*.png").Length);
        foreach (var (_, _, size, file) in files)
        {
            Assert.Equal(((int)size.Width, (int)size.Height), VisualSnapshot.PngSize(file.Path));
            Assert.True(file.DistinctColors >= 16, $"{file.Path}: only {file.DistinctColors} colours — not a real rendering");
        }

        foreach (var light in files.Where(f => f.Theme == "light"))
        {
            var dark = files.Single(f => f.Theme == "dark" && f.Subject == light.Subject && f.Size == light.Size);
            Assert.True(dark.File.MeanLuminance + 64 < light.File.MeanLuminance, $"{dark.File.Path}: {dark.File.MeanLuminance} against {light.File.MeanLuminance}");
        }

        Assert.True(defects.Count == 0, Report(defects));
    }

    /// <summary>
    /// I-245: the full-path tooltip of a Project Manager row (UI-TW-111) for a long path without spaces, in the main
    /// window at 800 × 500 and 1920 × 1040, light and dark: the tooltip lies inside the window, is at most 480 wide
    /// (design-tokens.md §8), shows the whole path and breaks it only after path separators. PNGs under
    /// <c>project-manager-path-tooltip</c>.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-111")]
    public async Task ProjectManagerPathTooltip_RendersAtMinimumAndLargeWindow()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(VisualSnapshotTests));
            return;
        }

        const string subject = "project-manager-path-tooltip";
        Size[] sizes = [new(800, 500), new(1920, 1040)];
        if (Directory.Exists(VisualSnapshot.FolderOf(subject)))
            Directory.Delete(VisualSnapshot.FolderOf(subject), recursive: true);
        var defects = new List<(string Variant, string Defect)>();
        var files = new List<(string Theme, Size Size, SnapshotFile File)>();
        foreach (var theme in Themes)
        {
            await using var h = await HeadlessHarness.StartAsync(o =>
            {
                o.Settings["appearance.theme"] = theme;
                ProjectManagerViewTests.DeepProject(o);
            });
            var path = Path.Combine(h.TempDir, ProjectManagerViewTests.DeepProjectDir, "Unit1.pas");
            Assert.True(await h.Services.Projects.OpenAsync(Path.Combine(h.TempDir, ProjectManagerViewTests.DeepProjectDir, "Hello.pproj")));
            var pm = h.Services.ProjectManager;
            await h.WaitForAsync(() => !pm.IsCheckingMissingFiles, "missing-file check done");
            Assert.True(h.Services.ToolWindows.Show(Core.ViewModels.ProjectManagerViewModel.ToolName, focus: true));
            Dispatcher.UIThread.RunJobs();

            foreach (var size in sizes)
            {
                VisualSnapshot.Arrange(h.Window, size, 1.0);
                var variant = $"{subject} {VisualSnapshot.FileName(theme, 1.0, size)}";
                var row = h.Find<TreeViewItem>("Tool.ProjectManager.Row.Unit1.pas");
                Assert.True(row.IsEffectivelyVisible, variant + ": the row is not shown");
                Assert.Equal(path, ToolTip.GetTip(row));
                // The pointer rests on the row; the tooltip is shown as after its delay.
                var name = h.Find<TextBlock>("Tool.ProjectManager.Row.Unit1.pas.Name");
                h.Window.MouseMove(name.TranslatePoint(new Point(10, name.Bounds.Height / 2), h.Window)!.Value);
                ToolTip.SetIsOpen(row, true);
                Dispatcher.UIThread.RunJobs();
                try
                {
                    var tip = ProjectManagerViewTests.OpenToolTip(h);
                    await h.WaitForAsync(() =>
                    {
                        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); // the fade-in (UI-TOK-051) runs on the render timer
                        return tip.Opacity == 1;
                    }, "tooltip faded in");
                    var block = Assert.Single(tip.GetVisualDescendants().OfType<TextBlock>());
                    var lines = ProjectManagerViewTests.ShownLines(block);
                    Assert.True(lines.Count > 1, variant + ": the path is on one line");
                    Assert.Equal(path, string.Concat(lines));
                    foreach (var line in lines.Take(lines.Count - 1).Where(l => !l.EndsWith('\\')))
                        defects.Add((variant, $"tooltip line '{line}' does not end at a path separator"));
                    if (lines[0].Length <= 3)
                        defects.Add((variant, $"tooltip starts with the line '{lines[0]}'"));
                    if (block.TextLayout.TextLines.Any(l => l.HasCollapsed || l.Width > block.Bounds.Width + 0.5))
                        defects.Add((variant, "cut: a tooltip line is wider than the tooltip"));
                    var area = new Rect(tip.TranslatePoint(default, h.Window)!.Value, tip.Bounds.Size);
                    if (tip.Bounds.Width > 480.5 || !new Rect(size).Contains(area))
                        defects.Add((variant, $"cut: the tooltip is at {area} in a window of {size}"));
                    defects.AddRange(VisualSnapshot.LayoutDefects(tip).Select(d => (variant, d)));
                    files.Add((theme, size, VisualSnapshot.Capture(h.Window, subject, theme)));
                    Assert.True(VisualSnapshot.DistinctColors(h.Window, block) >= 2, variant + ": the tooltip text is not drawn");
                }
                finally
                {
                    ToolTip.SetIsOpen(row, false);
                    Dispatcher.UIThread.RunJobs();
                }
            }
        }

        Assert.Equal(4, Directory.GetFiles(VisualSnapshot.FolderOf(subject), "*.png").Length);
        foreach (var (_, size, file) in files)
        {
            Assert.Equal(((int)size.Width, (int)size.Height), VisualSnapshot.PngSize(file.Path));
            Assert.True(file.DistinctColors >= 16, $"{file.Path}: only {file.DistinctColors} colours — not a real rendering");
        }

        foreach (var light in files.Where(f => f.Theme == "light"))
        {
            var dark = files.Single(f => f.Theme == "dark" && f.Size == light.Size);
            Assert.True(dark.File.MeanLuminance + 64 < light.File.MeanLuminance, $"{dark.File.Path}: {dark.File.MeanLuminance} against {light.File.MeanLuminance}");
        }

        Assert.True(defects.Count == 0, Report(defects));
    }

    /// <summary>
    /// Adds a defect when the text block is cut by a control above it, is arranged narrower than its whole text (also
    /// when it trims with an ellipsis), or reaches beyond <paramref name="rightLimit"/> (window coordinates).
    /// </summary>
    private static void AddCutText(List<(string Variant, string Defect)> defects, string variant, string what, TextBlock text, Window window, double rightLimit)
    {
        if (VisualSnapshot.HiddenPart(text, window) is { } hidden)
            defects.Add((variant, $"cut: {what} — {hidden}"));
        var needed = VisualSnapshot.TextWidth(text);
        if (needed > text.Bounds.Width + 0.5)
            defects.Add((variant, $"cut: {what} needs {needed:0.#} wide, has {text.Bounds.Width:0.#}"));
        var right = text.TranslatePoint(new Point(text.Bounds.Width, 0), window)!.Value.X;
        if (right > rightLimit + 0.5)
            defects.Add((variant, $"cut: {what} ends at x = {right:0.#}, in the scroll bar strip of the list (free up to {rightLimit:0.#})"));
    }

    /// <summary>The control lies completely inside the view port of the scroll viewer.</summary>
    private static bool InViewPort(ScrollViewer scroll, Control control) =>
        control.IsEffectivelyVisible
        && control.TranslatePoint(default, scroll) is { } origin
        && new Rect(scroll.Bounds.Size).Contains(new Rect(origin, control.Bounds.Size));

    /// <summary>One line per distinct defect with the number of variants showing it and the first of them.</summary>
    private static string Report(List<(string Variant, string Defect)> defects) => string.Join(Environment.NewLine,
        defects.GroupBy(d => d.Defect).Select(g => $"{g.Key} ({g.Count()} variants, first {g.First().Variant})"));
}
