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
}
