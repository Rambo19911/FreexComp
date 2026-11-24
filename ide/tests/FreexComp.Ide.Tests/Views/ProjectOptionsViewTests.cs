using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>Project Options on a derived configuration (I-249, UI-DLG-277…279): bold labels, Revert buttons, the defines fields.</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class ProjectOptionsViewTests
{
    /// <summary>Hello with <c>Release-Demo</c> (inherits Release, own debug information and a define) made the active configuration.</summary>
    internal static async Task AddDerivedAsync(HeadlessHarness h)
    {
        var project = ConfigurationEdits.AddDerived(h.Services.Projects.Definition!, "Release-Demo", "Release");
        var derived = project.Configurations["Release-Demo"] with { InheritsDebugInfo = false, DebugInfo = true, Defines = ["DEMO"] };
        project = project with { Configurations = project.Configurations.SetItem("Release-Demo", derived), ActiveConfiguration = "Release-Demo", Defines = ["APP"] };
        Assert.True(await h.Services.Projects.ApplyOptionsAsync(project));
    }

    internal static void ShowPage(DialogDriver dlg, string pageId)
    {
        var pages = dlg.Find<ListBox>("Pages");
        pages.SelectedItem = pages.Items.Cast<Core.Dialogs.ProjectOptionsPage>().Single(p => p.Id == pageId);
        Dispatcher.UIThread.RunJobs();
    }

    internal static Label LabelOf(DialogDriver dlg, Control target) =>
        dlg.Window.GetVisualDescendants().OfType<Label>().Single(l => ReferenceEquals(l.Target, target));

    private static double Left(Control control, DialogDriver dlg) => control.TranslatePoint(default, dlg.Window)!.Value.X;

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-277")]
    [Trait("Req", "UI-DLG-278")]
    [Trait("Req", "UI-DLG-279")]
    [Trait("Req", "IDE-CFG-020")]
    [Trait("Req", "IDE-CFG-021")]
    [Trait("Req", "IDE-CFG-023")]
    [Trait("Req", "IDE-CFG-024")]
    [Trait("Req", "IDE-CFG-026")]
    public async Task RevertButtonVisibleOnlyForOwnValues_BoldLabel()
    {
        await using var h = await BuildScenario.StartAsync();
        await AddDerivedAsync(h);
        var opening = h.Services.Commands.ExecuteAsync("project.options");
        var dlg = await h.WaitForDialogAsync("ProjectOptions");
        ShowPage(dlg, "Compiler");
        Assert.Equal("Release-Demo (active)", dlg.Find<ComboBox>("Configuration").SelectedItem);

        // Inherited: regular label, no Revert, help text names the supplier. Own: bold label, Revert, "Set in this configuration".
        var optimization = dlg.Find<ComboBox>("Optimization");
        var debugInfo = dlg.Find<CheckBox>("DebugInfo");
        var label = LabelOf(dlg, optimization);
        Assert.Equal((2, FontWeight.Normal, "Inherited from Release"), (optimization.SelectedIndex, label.FontWeight, AutomationProperties.GetHelpText(optimization)));
        Assert.Equal((true, FontWeight.SemiBold, Strings.Msg_ProjectOptions_ValueOwn), (debugInfo.IsChecked, debugInfo.FontWeight, AutomationProperties.GetHelpText(debugInfo)));
        var optimizationRevert = dlg.Find<Button>("Optimization.Revert");
        var debugInfoRevert = dlg.Find<Button>("DebugInfo.Revert");
        Assert.False(optimizationRevert.IsEffectivelyVisible);
        Assert.True(debugInfoRevert.IsEffectivelyVisible);
        Assert.Equal((26.0, 26.0, Strings.ProjectOptions_Revert, Strings.ProjectOptions_Revert),
            (debugInfoRevert.Bounds.Width, debugInfoRevert.Bounds.Height, (string?)ToolTip.GetTip(debugInfoRevert), AutomationProperties.GetName(debugInfoRevert)));
        Assert.Contains(dlg.Window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "(Release-Demo, inherits Release)" && t.FontWeight == FontWeight.Normal);

        // A change makes the value own: the label turns bold without moving, the field keeps its width, Revert appears.
        var (labelLeft, fieldLeft, fieldWidth) = (Left(label, dlg), Left(optimization, dlg), optimization.Bounds.Width);
        optimization.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal((FontWeight.SemiBold, Strings.Msg_ProjectOptions_ValueOwn), (label.FontWeight, AutomationProperties.GetHelpText(optimization)));
        Assert.True(optimizationRevert.IsEffectivelyVisible);
        Assert.Equal((labelLeft, fieldLeft, fieldWidth), (Left(label, dlg), Left(optimization, dlg), optimization.Bounds.Width));
        // The button is in the tab order directly after its field.
        optimization.Focus(Avalonia.Input.NavigationMethod.Tab);
        dlg.Press("Tab");
        Assert.Equal("Dialog.ProjectOptions.Optimization.Revert", dlg.FocusedAutomationId);

        // Revert: the inherited value shows again; the button hides; Apply is enabled.
        dlg.Click("Optimization.Revert");
        Assert.Equal((2, FontWeight.Normal, "Inherited from Release"), (optimization.SelectedIndex, label.FontWeight, AutomationProperties.GetHelpText(optimization)));
        Assert.False(optimizationRevert.IsEffectivelyVisible);
        Assert.Equal(fieldWidth, optimization.Bounds.Width);
        dlg.Click("DebugInfo.Revert");
        Assert.Equal((false, FontWeight.Normal, "Inherited from Release"), (debugInfo.IsChecked, debugInfo.FontWeight, AutomationProperties.GetHelpText(debugInfo)));
        Assert.False(debugInfoRevert.IsEffectivelyVisible);
        Assert.True(dlg.Find<Button>("Apply").IsEffectivelyEnabled);

        // Defines: the inherited list above the own one, the hint, Revert empties the own list.
        ShowPage(dlg, "ConditionalDefines");
        var config = dlg.Find<TextBox>("ConfigDefines");
        Assert.Equal(("APP", "DEMO", "APP; DEMO"), (dlg.Find<SelectableTextBlock>("InheritedDefines").Text, config.Text, dlg.Find<SelectableTextBlock>("Effective").Text));
        Assert.Equal(TextWrapping.Wrap, dlg.Find<SelectableTextBlock>("Effective").TextWrapping);
        Assert.Equal(Strings.ProjectOptions_DefinesInheritHint, dlg.Find<TextBlock>("ConfigDefines.Hint").Text);
        Assert.Equal(FontWeight.SemiBold, LabelOf(dlg, config).FontWeight);
        Assert.Null(dlg.TryFind<Button>("InheritedDefines.Revert"));
        Assert.Null(dlg.TryFind<Button>("Effective.Revert"));
        config.Text = "!;DEMO";
        Dispatcher.UIThread.RunJobs();
        Assert.Null(dlg.ErrorText("ConfigDefines"));
        Assert.Equal("DEMO", dlg.Find<SelectableTextBlock>("Effective").Text);
        dlg.Click("ConfigDefines.Revert");
        Assert.Equal(("", "APP", FontWeight.Normal), (config.Text, dlg.Find<SelectableTextBlock>("Effective").Text, LabelOf(dlg, config).FontWeight));
        Assert.False(dlg.Find<Button>("ConfigDefines.Revert").IsEffectivelyVisible);

        // Output: never inherited — no Revert; a folder shared with another configuration warns and OK stays enabled.
        ShowPage(dlg, "Output");
        Assert.Null(dlg.TryFind<Button>("OutputDir.Revert"));
        Assert.Equal("bin/Release-Demo", dlg.Find<TextBox>("OutputDir").Text);
        dlg.Find<TextBox>("OutputDir").Text = "bin/Release";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("⚠ " + string.Format(Strings.Msg_ProjectOptions_OutputDirShared, "Release"), dlg.ErrorText("OutputDir"));
        Assert.True(dlg.Find<Button>("Ok").IsEffectivelyEnabled);
        Assert.DoesNotContain("invalid", dlg.Find<TextBox>("OutputDir").Classes);

        // A root configuration: the layout of Phases 3–7 — no Revert buttons, no bold label, no inherited list.
        ShowPage(dlg, "Compiler");
        dlg.Find<ComboBox>("Configuration").SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Release", dlg.Find<ComboBox>("Configuration").SelectedItem);
        Assert.Null(dlg.TryFind<Button>("Optimization.Revert"));
        Assert.Null(dlg.TryFind<Button>("DebugInfo.Revert"));
        Assert.Equal((FontWeight.Normal, ""), (LabelOf(dlg, dlg.Find<ComboBox>("Optimization")).FontWeight, AutomationProperties.GetHelpText(dlg.Find<ComboBox>("Optimization"))));
        Assert.True(dlg.Find<ComboBox>("Optimization").Bounds.Width > fieldWidth); // no slot is reserved
        ShowPage(dlg, "ConditionalDefines");
        Assert.Null(dlg.TryFind<SelectableTextBlock>("InheritedDefines"));
        Assert.Null(dlg.TryFind<TextBlock>("ConfigDefines.Hint"));

        dlg.Click("Cancel");
        await h.WaitForAsync(() => !dlg.Window.IsVisible, "dialog closed");
        await opening;
    }

    /// <summary>
    /// I-252: the Search Paths page per configuration. The selector starts with All configurations (the project-level
    /// list); a configuration shows the rows it inherits read-only with their origin above its own rows, and the check
    /// box Do not inherit hides them.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-082")]
    [Trait("Req", "IDE-CFG-001")]
    public async Task SearchPathsPage_InheritedRowsReadOnly_DoNotInherit()
    {
        await using var h = await BuildScenario.StartAsync();
        await AddDerivedAsync(h);
        var project = h.Services.Projects.Definition!;
        project = project with
        {
            SearchPaths = ["lib"],
            Configurations = project.Configurations
                .SetItem("Release", project.Configurations["Release"] with { SearchPaths = ["rel"] })
                .SetItem("Release-Demo", project.Configurations["Release-Demo"] with { SearchPaths = ["demo"] }),
        };
        Assert.True(await h.Services.Projects.ApplyOptionsAsync(project));
        var dir = System.IO.Path.GetDirectoryName(BuildScenario.ProjectPath(h))!;
        foreach (var folder in (string[])["lib", "rel", "demo"])
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dir, folder)); // existing folders: no warning glyph
        var opening = h.Services.Commands.ExecuteAsync("project.options");
        var dlg = await h.WaitForDialogAsync("ProjectOptions");
        ShowPage(dlg, "SearchPaths");
        var configuration = dlg.Find<ComboBox>("Configuration");
        ListBox List() => dlg.Find<ListBox>("SearchPaths");
        string[] Paths() => [.. List().Items.Cast<Core.Dialogs.SearchPathRow>().Select(r => r.Path)];
        bool[] Enabled() => [.. ((string[])["Edit", "Remove", "MoveUp", "MoveDown"]).Select(id => dlg.Find<Button>("SearchPaths." + id).IsEffectivelyEnabled)];

        // All configurations: the project-level list; no check box; New and Delete wait for a configuration.
        Assert.True(configuration.IsEnabled);
        Assert.Equal([Strings.ProjectOptions_AllConfigurations, "Debug", "Release", "Release-Demo (active)"], configuration.Items.Cast<string>());
        Assert.Equal(0, configuration.SelectedIndex);
        Assert.Equal(["lib"], Paths());
        Assert.False(dlg.Find<CheckBox>("SearchPaths.DoNotInherit").IsEffectivelyVisible);
        Assert.False(dlg.Find<Button>("NewConfiguration").IsEffectivelyEnabled);
        Assert.False(dlg.Find<Button>("DeleteConfiguration").IsEffectivelyEnabled);

        // A configuration: inherited rows first, in text.tertiary with their origin; they cannot be edited.
        configuration.SelectedIndex = 3;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["lib", "rel", "demo"], Paths());
        Assert.True(dlg.Find<Button>("NewConfiguration").IsEffectivelyEnabled);
        var doNotInherit = dlg.Find<CheckBox>("SearchPaths.DoNotInherit");
        Assert.Equal((true, false, "Do not _inherit"), (doNotInherit.IsEffectivelyVisible, doNotInherit.IsChecked, doNotInherit.Content));
        Assert.Contains(dlg.Window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "(Release-Demo, inherits Release)");
        var items = List().GetVisualDescendants().OfType<ListBoxItem>().ToList();
        Assert.Equal(["Inherited from Project", "Inherited from Release", ""], items.Select(AutomationProperties.GetHelpText));
        Assert.True(dlg.Window.TryFindResource("FxcTextTertiaryBrush", dlg.Window.ActualThemeVariant, out var tertiary));
        string[] Cells(ListBoxItem item) => [.. item.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text!)];
        Assert.Equal(["lib", "Project"], Cells(items[0]));
        Assert.Equal(["rel", "Release"], Cells(items[1]));
        Assert.Equal(["demo"], Cells(items[2]));
        Assert.All(items[1].GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsVisible), t => Assert.Same(tertiary, t.Foreground));
        Assert.NotSame(tertiary, items[2].GetVisualDescendants().OfType<TextBlock>().Last().Foreground);
        List().SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([false, false, false, false], Enabled());
        List().Focus();
        dlg.Press("Delete");
        dlg.Press("F2");
        Assert.Equal(["lib", "rel", "demo"], Paths());
        Assert.False(dlg.Find<TextBox>("SearchPaths.Editor").IsVisible);
        Assert.False(dlg.Find<Button>("Apply").IsEffectivelyEnabled);

        // Own rows: added after the inherited ones, moved among themselves only.
        List().SelectedIndex = 2;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([true, true, false, false], Enabled());
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dir, "src"));
        h.FileDialogs.NextFolder(System.IO.Path.Combine(dir, "src"));
        dlg.Click("SearchPaths.Add");
        await h.WaitForAsync(() => List().ItemCount == 4, "path added");
        Assert.Equal(["lib", "rel", "demo", "src"], Paths());
        Assert.Equal(3, List().SelectedIndex);
        Assert.Equal([true, true, true, false], Enabled());
        dlg.Click("SearchPaths.MoveUp");
        Assert.Equal(["lib", "rel", "src", "demo"], Paths());
        Assert.Equal(2, List().SelectedIndex);
        Assert.Equal([true, true, false, true], Enabled());

        // Do not inherit hides the inherited rows; the own rows stay.
        doNotInherit.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["src", "demo"], Paths());
        dlg.Click("Apply");
        await h.WaitForAsync(() => !dlg.Find<Button>("Apply").IsEffectivelyEnabled, "applied");
        Assert.Equal(["!", "src", "demo"], h.Services.Projects.Definition!.Configurations["Release-Demo"].SearchPaths);
        Assert.Equal(["lib"], h.Services.Projects.Definition!.SearchPaths);
        doNotInherit.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["lib", "rel", "src", "demo"], Paths());

        // The other pages keep the configuration; back on this page it is still the one shown.
        ShowPage(dlg, "Compiler");
        Assert.Equal((3, "Release-Demo (active)"), (configuration.ItemCount, configuration.SelectedItem));
        ShowPage(dlg, "SearchPaths");
        Assert.Equal((4, 3), (configuration.ItemCount, configuration.SelectedIndex));
        Assert.Equal(["lib", "rel", "src", "demo"], Paths());
        configuration.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["lib"], Paths());
        Assert.False(dlg.Find<CheckBox>("SearchPaths.DoNotInherit").IsEffectivelyVisible);

        dlg.Click("Cancel");
        await h.WaitForAsync(() => !dlg.Window.IsVisible, "dialog closed");
        await opening;
    }

    /// <summary>
    /// The access keys shown on the Search Paths page with a configuration selected — the configuration bar, the page
    /// and the dialog buttons — are unique (UI-DLG-004): Alt+letter reaches one control.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-004")]
    public async Task SearchPathsPage_AccessKeysAreUnique()
    {
        await using var h = await BuildScenario.StartAsync();
        var opening = h.Services.Commands.ExecuteAsync("project.options");
        var dlg = await h.WaitForDialogAsync("ProjectOptions");
        ShowPage(dlg, "SearchPaths");
        dlg.Find<ComboBox>("Configuration").SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        Assert.True(dlg.Find<CheckBox>("SearchPaths.DoNotInherit").IsEffectivelyVisible);

        var keys = new List<(char Key, string Text)>();
        foreach (var control in dlg.Window.GetVisualDescendants().OfType<ContentControl>().Where(c => c.IsEffectivelyVisible && c is Button or CheckBox or Label))
        {
            var text = control.Content as string ?? (control.Content as Avalonia.Controls.Primitives.AccessText)?.Text;
            var at = text?.IndexOf('_', StringComparison.Ordinal) ?? -1;
            if (at >= 0 && at + 1 < text!.Length)
                keys.Add((char.ToUpperInvariant(text[at + 1]), text));
        }

        dlg.Click("Cancel");
        await h.WaitForAsync(() => !dlg.Window.IsVisible, "dialog closed");
        await opening;

        Assert.Contains(keys, k => k.Text == Strings.ProjectOptions_DeleteConfiguration);
        Assert.Contains(keys, k => k.Text == Strings.ProjectOptions_MoveDown);
        Assert.Contains(keys, k => k.Text == Strings.ProjectOptions_SearchPathsDoNotInherit);
        Assert.Contains(keys, k => k.Text == Strings.ProjectOptions_SearchPaths);
        var clashes = keys.GroupBy(k => k.Key).Where(g => g.Count() > 1).Select(g => $"Alt+{g.Key}: {string.Join(", ", g.Select(k => k.Text))}").ToList();
        Assert.True(clashes.Count == 0, string.Join("; ", clashes));
        Assert.Equal("Move D_own", Strings.ProjectOptions_MoveDown);
    }
}
