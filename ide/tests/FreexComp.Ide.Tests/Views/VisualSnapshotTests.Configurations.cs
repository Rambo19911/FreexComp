using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>Visual subjects of configuration inheritance (F09-004: I-247…I-249).</summary>
public sealed partial class VisualSnapshotTests
{
    /// <summary>32 characters, the longest name a configuration can have (UI-DLG-083).</summary>
    private const string LongConfigurationName = "Customer-Edition-With-Long-NameX";

    /// <summary>Eight configurations; the 32-character one at inheritance level 3 (UI-DLG-276).</summary>
    private const string FilledConfigurations = """
        "Debug": { "optimization": 0, "debugInfo": true, "defines": ["DEBUG"], "outputDir": "bin/Debug" },
        "Release": { "optimization": 2, "debugInfo": false, "defines": ["RELEASE"], "outputDir": "bin/Release" },
        "Release-Demo": { "inherits": "Release", "debugInfo": true, "defines": ["DEMO", "TRIAL_PERIOD", "NO_EXPORT", "SPLASH_SCREEN"] },
        "Profiling": { "inherits": "Release-Demo", "optimization": 1, "defines": ["PROFILE"] },
        "Customer-Edition-With-Long-NameX": { "inherits": "Profiling", "defines": ["CUSTOMER"], "searchPaths": ["vendor"], "outputDir": "out/customer", "rtti": "published" },
        "Staging": { "inherits": "Release" },
        "Debug-Trace": { "inherits": "Debug", "defines": ["TRACE"] },
        "Nightly": { "optimization": 1, "debugInfo": true, "defines": [], "outputDir": "bin/Nightly" }
        """;

    private static void ConfigurationProject(HarnessOptions o, string theme, string configurations, string active)
    {
        o.Settings["appearance.theme"] = theme;
        o.ImplementedPhase = 7;
        o.InitialFiles[@"Hello\Hello.pproj"] = $$"""
            { "formatVersion": 1, "name": "Hello", "appType": "console", "mainSource": "Hello.pas",
              "defines": ["APP", "WINDOWS_ONLY"],
              "configurations": { {{configurations}} },
              "activeConfiguration": "{{active}}",
              "files": [ { "path": "Hello.pas" } ] }
            """;
        o.InitialFiles[@"Hello\Hello.pas"] = "program Hello;\nbegin\nend.\n";
    }

    private static string ManyConfigurations() => string.Join(",\n", Enumerable.Range(1, 20).Select(i =>
        i == 1 ? "\"Base\": { \"optimization\": 0, \"debugInfo\": true, \"defines\": [], \"outputDir\": \"bin/Base\" }"
        : $"\"Variant-{i:00}\": {{ \"inherits\": \"{(i % 3 == 0 ? $"Variant-{i - 1:00}" : "Base")}\" }}"));

    /// <summary>
    /// The dialogs of configuration inheritance (UI-DLG-270…279), light and dark, at scale 1.0 and 2.0. Configuration
    /// Manager at its minimum 560 × 420 and at 900 × 700: <c>configuration-manager</c> (the two default configurations),
    /// <c>configuration-manager-filled</c> (eight configurations in a three-level chain, a 32-character name at level 3,
    /// an inherited configuration selected so that Delete is disabled with its reason) and
    /// <c>configuration-manager-many</c> (twenty: the list scrolls). The sub-dialogs at their one size:
    /// <c>configuration-manager-rename</c> and <c>new-configuration-inherit</c>. Project Options at 760 × 520 and
    /// 1100 × 760 on a derived configuration: <c>project-options-compiler-derived</c> (one inherited and one own field with
    /// Revert) and <c>project-options-defines-derived</c>. Rules: the layout rules hold for every window (nothing
    /// clipped, nothing overlapping); the header cells stand above their columns; the active row draws its bullet; a
    /// trimmed name has its tooltip; bold and regular labels have the same left edge; the Revert button is drawn inside
    /// the page.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-DLG-270")]
    [Trait("Req", "UI-DLG-271")]
    [Trait("Req", "UI-DLG-272")]
    [Trait("Req", "UI-DLG-274")]
    [Trait("Req", "UI-DLG-275")]
    [Trait("Req", "UI-DLG-276")]
    [Trait("Req", "UI-DLG-277")]
    [Trait("Req", "UI-DLG-278")]
    [Trait("Req", "UI-DLG-279")]
    public async Task ConfigurationDialogs_RenderAllVariants_NoClippingOrOverlap()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(VisualSnapshotTests));
            return;
        }

        const string twoDefault = """
            "Debug": { "optimization": 0, "debugInfo": true, "defines": [], "outputDir": "bin/Debug" },
            "Release": { "optimization": 2, "debugInfo": false, "defines": [], "outputDir": "bin/Release" }
            """;
        (string Subject, string Configurations, string Active, string Selected)[] managers =
        [
            ("configuration-manager", twoDefault, "Debug", "Debug"),
            ("configuration-manager-filled", FilledConfigurations, "Release-Demo", "Release"),
            ("configuration-manager-many", ManyConfigurations(), "Variant-02", "Variant-12"),
        ];
        (string Subject, string Page)[] pages = [("project-options-compiler-derived", "Compiler"), ("project-options-defines-derived", "ConditionalDefines")];
        const string rename = "configuration-manager-rename", create = "new-configuration-inherit";
        Size[] managerSizes = [new(560, 420), new(900, 700)];
        Size[] optionSizes = [new(760, 520), new(1100, 760)];
        foreach (var subject in managers.Select(m => m.Subject).Concat(pages.Select(p => p.Subject)).Append(rename).Append(create))
        {
            if (Directory.Exists(VisualSnapshot.FolderOf(subject)))
                Directory.Delete(VisualSnapshot.FolderOf(subject), recursive: true);
        }

        var defects = new List<(string Variant, string Defect)>();
        var files = new List<(string Subject, string Theme, Size Size, double Scale, SnapshotFile File)>();

        void Shoot(DialogDriver dlg, string subject, string theme, Size? size, double scale, Action<string>? check = null)
        {
            if (size is { } s)
                VisualSnapshot.Arrange(dlg.Window, s, scale);
            else
                dlg.Window.SetRenderScaling(scale);
            Dispatcher.UIThread.RunJobs();
            var client = dlg.Window.ClientSize;
            var variant = $"{subject} {VisualSnapshot.FileName(theme, scale, client)}";
            if (size is not null)
                Assert.Equal(size, client);
            check?.Invoke(variant);
            defects.AddRange(VisualSnapshot.LayoutDefects(dlg.Window).Select(d => (variant, d)));
            files.Add((subject, theme, client, scale, VisualSnapshot.Capture(dlg.Window, subject, theme)));
        }

        double X(Control control, Window window) => control.TranslatePoint(default, window)!.Value.X;

        foreach (var theme in Themes)
        {
            // ---- Configuration Manager ----
            foreach (var manager in managers)
            {
                await using var h = await HeadlessHarness.StartAsync(o => ConfigurationProject(o, theme, manager.Configurations, manager.Active));
                Assert.True(await h.Services.Projects.OpenAsync(System.IO.Path.Combine(h.TempDir, "Hello", "Hello.pproj")));
                var running = h.Services.Commands.ExecuteAsync("project.configurationManager");
                var dlg = await h.WaitForDialogAsync("ConfigurationManager");
                try
                {
                    var list = dlg.Find<ListBox>("Configurations");
                    list.SelectedItem = list.Items.OfType<ListBoxItem>().Single(i => AutomationIds.Get(i) == "Dialog.ConfigurationManager.Configurations.Row." + manager.Selected);
                    Dispatcher.UIThread.RunJobs();
                    var delete = dlg.Find<Button>("Delete");
                    if (manager.Subject == "configuration-manager-filled")
                    {
                        Assert.False(delete.IsEffectivelyEnabled);
                        Assert.Equal(string.Format(Strings.Msg_ProjectOptions_ConfigDeleteInherited, "Release", "Release-Demo"), ToolTip.GetTip(delete));
                        Assert.Equal("all (no parent)", dlg.Find<SelectableTextBlock>("OwnValues").Text);
                    }

                    void Check(string variant)
                    {
                        var rows = list.Items.OfType<ListBoxItem>().ToList();
                        var shown = rows.Where(r => r.IsEffectivelyVisible && VisualSnapshot.HiddenPart(r, dlg.Window) is null).ToList();
                        Assert.NotEmpty(shown);
                        // The header cells stand above their columns (also beside a vertical scroll bar).
                        var parentHeader = dlg.Find<TextBlock>("Column.Parent");
                        var nameHeader = dlg.Find<TextBlock>("Column.Name");
                        foreach (var row in shown)
                        {
                            var cells = row.GetVisualDescendants().OfType<TextBlock>().ToList();
                            var core = (Core.Dialogs.ConfigurationRow)row.Tag!;
                            if (Math.Abs(X(cells[^1], dlg.Window) - X(parentHeader, dlg.Window)) > 0.5)
                                defects.Add((variant, $"misaligned: '{core.Name}' parent cell at x = {X(cells[^1], dlg.Window):0.#}, header at {X(parentHeader, dlg.Window):0.#}"));
                            if (Math.Abs(X(cells[0], dlg.Window) - core.Indent - X(nameHeader, dlg.Window)) > 0.5)
                                defects.Add((variant, $"misaligned: '{core.Name}' name at x = {X(cells[0], dlg.Window):0.#} with indent {core.Indent}, header at {X(nameHeader, dlg.Window):0.#}"));
                            // A name that does not fit ends with an ellipsis and is shown in full in the row tooltip.
                            Assert.Equal(TextTrimming.CharacterEllipsis, cells[0].TextTrimming);
                            Assert.Equal(core.Name, ToolTip.GetTip(row));
                            var bullet = row.GetVisualDescendants().OfType<Ellipse>().Single();
                            Assert.Equal(core.IsActive, bullet.IsVisible);
                            if (core.IsActive && VisualSnapshot.DistinctColors(dlg.Window, bullet) < 2)
                                defects.Add((variant, "the bullet of the active configuration is not drawn"));
                        }

                        // Never a horizontal scroll bar; the buttons keep their width at the right edge.
                        var scroller = list.GetVisualDescendants().OfType<ScrollViewer>().First();
                        if (scroller.Extent.Width > scroller.Viewport.Width + 0.5)
                            defects.Add((variant, $"the list scrolls horizontally: {scroller.Extent.Width:0.#} in {scroller.Viewport.Width:0.#}"));
                        foreach (var id in (string[])["New", "Rename", "Delete", "SetActive"])
                        {
                            var button = dlg.Find<Button>(id);
                            if (Math.Abs(button.Bounds.Width - 96) > 0.5)
                                defects.Add((variant, $"button {id} is {button.Bounds.Width:0.#} wide"));
                            if (VisualSnapshot.HiddenPart(button, dlg.Window) is { } hidden)
                                defects.Add((variant, $"cut: {id} — {hidden}"));
                        }

                        if (manager.Subject == "configuration-manager-many" && dlg.Window.ClientSize == managerSizes[0] && scroller.Extent.Height <= scroller.Viewport.Height)
                            defects.Add((variant, "twenty configurations do not scroll"));
                        foreach (var id in (string[])["Parent", "Parent.Hint", "OwnValues"])
                        {
                            if (VisualSnapshot.HiddenPart(dlg.Find<Control>(id), dlg.Window) is { } hidden)
                                defects.Add((variant, $"cut: {id} — {hidden}"));
                        }
                    }

                    foreach (var size in managerSizes)
                    {
                        foreach (var scale in Scales)
                            Shoot(dlg, manager.Subject, theme, size, scale, Check);
                    }

                    if (manager.Subject == "configuration-manager-filled")
                    {
                        // The 32-character name at level 3 is trimmed at the minimum size…
                        VisualSnapshot.Arrange(dlg.Window, managerSizes[0], 1.0);
                        var longRow = list.Items.OfType<ListBoxItem>().Single(i => ((Core.Dialogs.ConfigurationRow)i.Tag!).Name == LongConfigurationName);
                        Assert.Equal(32, LongConfigurationName.Length);
                        Assert.Equal(3, ((Core.Dialogs.ConfigurationRow)longRow.Tag!).Level);

                        // …and the sub-dialogs open over it: Rename with that name, New from the selected row.
                        list.SelectedItem = longRow;
                        Dispatcher.UIThread.RunJobs();
                        dlg.Click("Rename");
                        var sub = await h.WaitForDialogAsync("RenameConfiguration");
                        Assert.Equal(LongConfigurationName, sub.Find<TextBox>("Name").Text);
                        foreach (var scale in Scales)
                            Shoot(sub, rename, theme, null, scale);
                        Assert.Equal(360, sub.Window.ClientSize.Width);
                        sub.Click("Cancel");
                        await h.WaitForAsync(() => !sub.Window.IsVisible, "Rename Configuration closed");

                        dlg.Click("New");
                        sub = await h.WaitForDialogAsync("NewConfiguration");
                        sub.Find<TextBox>("Name").Text = "Customer-Edition-Trial";
                        Dispatcher.UIThread.RunJobs();
                        var inherit = sub.Find<CheckBox>("Inherit");
                        Assert.True(inherit.IsChecked);
                        Assert.Equal(LongConfigurationName, sub.Find<ComboBox>("CopyFrom").SelectedItem);
                        foreach (var scale in Scales)
                        {
                            Shoot(sub, create, theme, null, scale, variant =>
                            {
                                if (VisualSnapshot.HiddenPart(inherit, sub.Window) is { } hidden)
                                    defects.Add((variant, $"cut: Inherit — {hidden}"));
                                if (Math.Abs(X(inherit, sub.Window) - X(sub.Find<ComboBox>("CopyFrom"), sub.Window)) > 0.5)
                                    defects.Add((variant, "the Inherit check box is not aligned with the field column"));
                            });
                        }

                        Assert.Equal(360, sub.Window.ClientSize.Width);
                        sub.Click("Cancel");
                        await h.WaitForAsync(() => !sub.Window.IsVisible, "New Configuration closed");
                    }
                }
                finally
                {
                    dlg.Click("Cancel"); // also after a failure, so that the failure is what the test reports
                    await running;
                }
            }

            // ---- Project Options on a derived configuration ----
            {
                await using var h = await HeadlessHarness.StartAsync(o => ConfigurationProject(o, theme, FilledConfigurations, "Release-Demo"));
                Assert.True(await h.Services.Projects.OpenAsync(System.IO.Path.Combine(h.TempDir, "Hello", "Hello.pproj")));
                var running = h.Services.Commands.ExecuteAsync("project.options");
                var dlg = await h.WaitForDialogAsync("ProjectOptions");
                try
                {
                    foreach (var (subject, page) in pages)
                    {
                        ProjectOptionsViewTests.ShowPage(dlg, page);
                        Assert.Equal("Release-Demo (active)", dlg.Find<ComboBox>("Configuration").SelectedItem);
                        void Check(string variant)
                        {
                            Control regular, bold;
                            Button revert;
                            if (page == "Compiler")
                            {
                                // Optimization is inherited (regular label, no Revert); debug information is own (bold, Revert).
                                var optimization = dlg.Find<ComboBox>("Optimization");
                                regular = ProjectOptionsViewTests.LabelOf(dlg, optimization);
                                bold = dlg.Find<CheckBox>("DebugInfo");
                                revert = dlg.Find<Button>("DebugInfo.Revert");
                                Assert.Equal((FontWeight.Normal, FontWeight.SemiBold), (((Label)regular).FontWeight, ((CheckBox)bold).FontWeight));
                                Assert.False(dlg.Find<Button>("Optimization.Revert").IsEffectivelyVisible);
                                // The reserved slot: the field ends before the column of the Revert buttons.
                                var fieldRight = X(optimization, dlg.Window) + optimization.Bounds.Width;
                                if (fieldRight > X(revert, dlg.Window) - 8 + 0.5)
                                    defects.Add((variant, $"the Optimization field ends at x = {fieldRight:0.#}, the Revert column starts at {X(revert, dlg.Window):0.#}"));
                            }
                            else
                            {
                                var config = dlg.Find<TextBox>("ConfigDefines");
                                regular = ProjectOptionsViewTests.LabelOf(dlg, dlg.Find<TextBox>("ProjectDefines"));
                                bold = ProjectOptionsViewTests.LabelOf(dlg, config);
                                revert = dlg.Find<Button>("ConfigDefines.Revert");
                                Assert.Equal((FontWeight.Normal, FontWeight.SemiBold), (((Label)regular).FontWeight, ((Label)bold).FontWeight));
                                Assert.Equal("APP; WINDOWS_ONLY; RELEASE", dlg.Find<SelectableTextBlock>("InheritedDefines").Text);
                                Assert.Equal("APP; WINDOWS_ONLY; RELEASE; DEMO; TRIAL_PERIOD; NO_EXPORT; SPLASH_SCREEN", dlg.Find<SelectableTextBlock>("Effective").Text);
                                foreach (var id in (string[])["InheritedDefines", "Effective", "ConfigDefines.Hint"])
                                {
                                    if (VisualSnapshot.HiddenPart(dlg.Find<Control>(id), dlg.Window) is { } hidden)
                                        defects.Add((variant, $"cut: {id} — {hidden}"));
                                }
                            }

                            // Bold and regular labels have the same left edge.
                            if (Math.Abs(X(regular, dlg.Window) - X(bold, dlg.Window)) > 0.5)
                                defects.Add((variant, $"left edges differ: regular label at x = {X(regular, dlg.Window):0.#}, bold label at {X(bold, dlg.Window):0.#}"));
                            Assert.True(revert.IsEffectivelyVisible, variant + ": no Revert button");
                            if (VisualSnapshot.HiddenPart(revert, dlg.Window) is { } cut)
                                defects.Add((variant, $"cut: Revert — {cut}"));
                            if (VisualSnapshot.DistinctColors(dlg.Window, revert) < 3)
                                defects.Add((variant, "the Revert button does not draw its glyph"));
                            if (Math.Abs(revert.Bounds.Width - 26) > 0.5 || Math.Abs(revert.Bounds.Height - 26) > 0.5)
                                defects.Add((variant, $"the Revert button is {revert.Bounds.Size}"));
                        }

                        foreach (var size in optionSizes)
                        {
                            foreach (var scale in Scales)
                                Shoot(dlg, subject, theme, size, scale, Check);
                        }
                    }
                }
                finally
                {
                    dlg.Click("Cancel");
                    await running;
                }
            }
        }

        // 2 themes × 2 sizes × 2 scales for the resizable dialogs, 2 themes × 2 scales for the sub-dialogs.
        foreach (var subject in managers.Select(m => m.Subject).Concat(pages.Select(p => p.Subject)))
            Assert.Equal(8, Directory.GetFiles(VisualSnapshot.FolderOf(subject), "*.png").Length);
        foreach (var subject in (string[])[rename, create])
            Assert.Equal(4, Directory.GetFiles(VisualSnapshot.FolderOf(subject), "*.png").Length);
        foreach (var (_, _, size, scale, file) in files)
        {
            Assert.Equal(((int)(size.Width * scale), (int)(size.Height * scale)), VisualSnapshot.PngSize(file.Path));
            Assert.True(file.DistinctColors >= 16, $"{file.Path}: only {file.DistinctColors} colours — not a real rendering");
        }

        foreach (var light in files.Where(f => f.Theme == "light"))
        {
            var dark = files.Single(f => f.Theme == "dark" && f.Subject == light.Subject && f.Size == light.Size && f.Scale == light.Scale);
            Assert.True(dark.File.MeanLuminance + 64 < light.File.MeanLuminance, $"{dark.File.Path}: {dark.File.MeanLuminance} against {light.File.MeanLuminance}");
        }

        Assert.True(defects.Count == 0, Report(defects));
    }
}
