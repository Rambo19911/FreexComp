using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.Views;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The window of the dialog <c>ProjectDependencies</c> (phase8-ui-additions.md §10, UI-DLG-310…315; task I-277).</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class ProjectDependenciesViewTests
{
    private const string Prefix = "Dialog.ProjectDependencies.";

    private static void Project(HarnessOptions o, string name, string dependencies = "")
    {
        o.InitialFiles[$@"{name}\{name}.pproj"] = $$"""
            { "formatVersion": 1, "name": "{{name}}", "mainSource": "{{name}}.pas",
              "files": [ { "path": "{{name}}.pas" } ], "dependencies": [{{dependencies}}] }
            """;
        o.InitialFiles[$@"{name}\{name}.pas"] = $"program {name};\nbegin\nend.\n";
    }

    private static Color ColorOf(DialogDriver dlg, string key)
    {
        Assert.True(dlg.Window.TryFindResource(key, dlg.Window.ActualThemeVariant, out var brush), key);
        return Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
    }

    private static Color ColorOf(TextBlock text) => Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color;

    /// <summary>
    /// UI-DLG-310…315 in the light and the dark theme: 520 × 360, resizable down to 400 × 260; the check list with one row
    /// per project (AutomationId <c>Dialog.ProjectDependencies.Item.&lt;ProjectName&gt;</c>), the first focus on it and
    /// Space toggling the focused row; a dependency outside the group in <c>text.disabled</c> with its suffix and a usable
    /// check box; the inline error line (<c>status.error</c>, at most three lines, a live region) that disables OK; the
    /// placeholder without rows. Every interactive control has its AutomationId.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-310")]
    [Trait("Req", "UI-DLG-311")]
    [Trait("Req", "UI-DLG-312")]
    [Trait("Req", "UI-DLG-313")]
    [Trait("Req", "UI-DLG-314")]
    [Trait("Req", "UI-DLG-315")]
    public async Task RowsErrorLineAndGreyedEntriesRender()
    {
        var disabledColors = new List<Color>();
        foreach (var theme in (string[])["light", "dark"])
        {
            await using var h = await HeadlessHarness.StartAsync(o =>
            {
                o.Settings["appearance.theme"] = theme;
                Project(o, "App", "\"../CoreLib/CoreLib.pproj\", \"D:/common/Shared.pproj\"");
                Project(o, "CoreLib");
                Project(o, "Tools", "\"../App/App.pproj\"");
                o.InitialFiles["All.pgroup"] =
                    """{ "formatVersion": 1, "projects": ["App/App.pproj", "CoreLib/CoreLib.pproj", "Tools/Tools.pproj"], "activeProject": "App/App.pproj" }""";
            });
            Assert.True(await h.Services.Projects.OpenAsync(Path.Combine(h.TempDir, "All.pgroup")));
            Dispatcher.UIThread.RunJobs();

            var running = h.Services.Commands.ExecuteAsync("project.dependencies");
            var dlg = await h.WaitForDialogAsync("ProjectDependencies");
            try
            {
                Assert.Equal(("Dependencies of App", 520.0, 360.0, true, ProjectDependenciesWindow.MinimumWidth, ProjectDependenciesWindow.MinimumHeight),
                    (dlg.Window.Title, dlg.Window.Width, dlg.Window.Height, dlg.Window.CanResize, dlg.Window.MinWidth, dlg.Window.MinHeight));
                Assert.Equal((400.0, 260.0), (ProjectDependenciesWindow.MinimumWidth, ProjectDependenciesWindow.MinimumHeight));

                // The list: its label, one row per project, the first focus on its first row.
                var list = dlg.Find<ListBox>("Projects");
                var label = Assert.Single(dlg.Window.GetVisualDescendants().OfType<Label>(), l => ReferenceEquals(l.Target, list));
                Assert.Equal("_Projects that are built before App:", label.Content);
                var items = list.Items.OfType<ListBoxItem>().ToList();
                Assert.Equal([Prefix + "Item.CoreLib", Prefix + "Item.Tools", Prefix + "Item.Shared"], items.Select(AutomationIds.Get));
                Assert.Equal(["CoreLib", "Tools", "Shared (not in the group)"], items.Select(i => AutomationProperties.GetName(i)));
                await h.WaitForAsync(() => dlg.FocusedAutomationId == Prefix + "Item.CoreLib", "focus on the first row");
                Assert.True(items[0].Bounds.Height > 0);
                Assert.True(list.MinHeight >= ProjectDependenciesWindow.MinimumRows * items[0].Bounds.Height,
                    $"the list keeps three rows: minimum {list.MinHeight}, row {items[0].Bounds.Height}");
                Assert.False(dlg.Find<TextBlock>("Projects.Placeholder").IsVisible);

                // Check boxes show the project's dependencies; names and paths in their tokens.
                CheckBox Check(string name) => dlg.Find<CheckBox>($"Item.{name}.Check");
                Assert.Equal([true, false, true], new[] { "CoreLib", "Tools", "Shared" }.Select(n => Check(n).IsChecked));
                Assert.Equal(("CoreLib", "../CoreLib/CoreLib.pproj"), (dlg.Find<TextBlock>("Item.CoreLib.Name").Text, dlg.Find<TextBlock>("Item.CoreLib.Path").Text));
                Assert.Equal(ColorOf(dlg, "FxcTextPrimaryBrush"), ColorOf(dlg.Find<TextBlock>("Item.CoreLib.Name")));
                Assert.Equal(ColorOf(dlg, "FxcTextSecondaryBrush"), ColorOf(dlg.Find<TextBlock>("Item.CoreLib.Path")));
                Assert.Equal(TextTrimming.PrefixCharacterEllipsis, dlg.Find<TextBlock>("Item.CoreLib.Path").TextTrimming);
                Assert.Equal(Path.Combine(h.TempDir, "CoreLib", "CoreLib.pproj"), ToolTip.GetTip(dlg.Find<TextBlock>("Item.CoreLib.Path")));
                Assert.Null(dlg.TryFind<TextBlock>("Item.CoreLib.Suffix"));

                // UI-DLG-311: the dependency outside the group is greyed, its suffix follows, its check box works.
                var disabled = ColorOf(dlg, "FxcTextDisabledBrush");
                disabledColors.Add(disabled);
                Assert.NotEqual(ColorOf(dlg, "FxcTextPrimaryBrush"), disabled);
                Assert.Equal(("Shared", "D:/common/Shared.pproj", "(not in the group)"),
                    (dlg.Find<TextBlock>("Item.Shared.Name").Text, dlg.Find<TextBlock>("Item.Shared.Path").Text, dlg.Find<TextBlock>("Item.Shared.Suffix").Text));
                Assert.All((string[])["Name", "Path", "Suffix"], part => Assert.Equal(disabled, ColorOf(dlg.Find<TextBlock>("Item.Shared." + part))));
                Assert.True(Check("Shared").IsEffectivelyEnabled);
                Assert.True(dlg.Find<TextBlock>("Item.Shared.Suffix").IsEffectivelyVisible);

                // No cycle: no error line, OK enabled.
                var error = dlg.Find<TextBlock>("Error");
                Assert.False(error.IsEffectivelyVisible);
                Assert.True(dlg.Find<Button>("Ok").IsEffectivelyEnabled);
                Assert.Equal((ProjectDependenciesWindow.ErrorMaxLines, TextWrapping.Wrap, TextTrimming.CharacterEllipsis),
                    (error.MaxLines, error.TextWrapping, error.TextTrimming));
                Assert.Equal(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(error));

                // Down, Space: Tools is ticked — Tools depends on App, so the error line appears and OK is disabled.
                dlg.Press("Down");
                Assert.Equal(Prefix + "Item.Tools", dlg.FocusedAutomationId);
                dlg.Press("Space");
                Assert.True(Check("Tools").IsChecked);
                Assert.True(error.IsEffectivelyVisible);
                Assert.Equal("This selection creates a circular dependency:\nApp \u2192 Tools \u2192 App", error.Text);
                Assert.Equal(error.Text, ToolTip.GetTip(error));
                Assert.Equal(ColorOf(dlg, "FxcStatusErrorBrush"), ColorOf(error));
                Assert.Single(dlg.Window.GetVisualDescendants().OfType<Icons.IdeIcon>(), i => i.IconName == "error" && i.IsEffectivelyVisible);
                Assert.False(dlg.Find<Button>("Ok").IsEffectivelyEnabled);
                dlg.Press("Enter"); // the default button is disabled
                Assert.True(dlg.Window.IsVisible);

                // At the minimum size, with the error line: nothing is cut and the list still has its three rows.
                VisualSnapshot.Arrange(dlg.Window, new Avalonia.Size(ProjectDependenciesWindow.MinimumWidth, ProjectDependenciesWindow.MinimumHeight), 1);
                Assert.Equal(new Avalonia.Size(400, 260), dlg.Window.ClientSize);
                Assert.All(new Control[] { list, error, dlg.Find<Button>("Ok"), dlg.Find<Button>("Cancel"), items[^1] },
                    control => Assert.Null(VisualSnapshot.HiddenPart(control, dlg.Window)));
                Assert.True(list.Bounds.Height >= list.MinHeight, $"list {list.Bounds.Height}, minimum {list.MinHeight}");
                VisualSnapshot.Arrange(dlg.Window, new Avalonia.Size(520, 360), 1);

                // Every interactive control has a well-formed AutomationId; none is used twice.
                Assert.Empty(AutomationIdChecks.ControlsWithoutId(dlg.Window));
                var ids = AutomationIdChecks.AllIds(dlg.Window);
                Assert.All(ids, id => Assert.True(AutomationIds.IsWellFormed(id), id));
                Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());

                // Space again (or a click on the box): the cycle is gone.
                dlg.Press("Space");
                Assert.False(Check("Tools").IsChecked);
                Assert.False(error.IsEffectivelyVisible);
                Assert.True(dlg.Find<Button>("Ok").IsEffectivelyEnabled);
                Check("Shared").IsChecked = false;
                Dispatcher.UIThread.RunJobs();
                Assert.False(error.IsEffectivelyVisible);
            }
            finally
            {
                dlg.Click("Cancel"); // also after a failure, so that the failure is what the test reports
                await running;
            }

            Assert.Contains("D:/common/Shared.pproj", await File.ReadAllTextAsync(Path.Combine(h.TempDir, "App", "App.pproj"), TestContext.Current.CancellationToken),
                StringComparison.Ordinal); // Cancel wrote nothing

            // A project opened alone: the placeholder in place of rows.
            Assert.True(await h.Services.Projects.OpenAsync(Path.Combine(h.TempDir, "CoreLib", "CoreLib.pproj")));
            Dispatcher.UIThread.RunJobs();
            var alone = h.Services.Commands.ExecuteAsync("project.dependencies");
            var empty = await h.WaitForDialogAsync("ProjectDependencies");
            try
            {
                Assert.Equal("Dependencies of CoreLib", empty.Window.Title);
                Assert.Equal(0, empty.Find<ListBox>("Projects").ItemCount);
                var placeholder = empty.Find<TextBlock>("Projects.Placeholder");
                Assert.True(placeholder.IsEffectivelyVisible);
                Assert.Equal("There are no other projects in the group. Add a project to the group to make it a dependency.", placeholder.Text);
                Assert.Equal(ColorOf(empty, "FxcTextSecondaryBrush"), ColorOf(placeholder));
                Assert.Empty(AutomationIdChecks.ControlsWithoutId(empty.Window));
            }
            finally
            {
                empty.Click("Cancel");
                await alone;
            }
        }

        // The tokens resolve per theme: text.disabled differs between light and dark.
        Assert.Equal(2, disabledColors.Distinct().Count());
    }
}
