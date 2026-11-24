using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The Configuration Manager window (I-248, UI-DLG-271…276): every operation by keyboard only.</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class ConfigurationManagerViewTests
{
    private static string[] Rows(DialogDriver dlg) =>
        [.. dlg.Find<ListBox>("Configurations").Items.OfType<ListBoxItem>().Select(i => AutomationProperties.GetName(i)!)];

    private static void TypeText(DialogDriver dlg, string text)
    {
        dlg.Window.KeyTextInput(text);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-271")]
    [Trait("Req", "UI-DLG-272")]
    [Trait("Req", "UI-DLG-273")]
    [Trait("Req", "UI-DLG-274")]
    [Trait("Req", "UI-DLG-275")]
    [Trait("Req", "UI-DLG-276")]
    [Trait("Req", "IDE-CFG-010")]
    [Trait("Req", "IDE-CFG-012")]
    [Trait("Req", "IDE-CFG-013")]
    public async Task KeyboardOnlyNewRenameReparentDelete()
    {
        await using var h = await BuildScenario.StartAsync(implementedPhase: 7);
        var opening = h.Services.Commands.ExecuteAsync("project.configurationManager");
        var dlg = await h.WaitForDialogAsync("ConfigurationManager");
        Assert.Equal(("Configuration Manager — Hello", 560.0, 420.0, true, 560.0, 420.0),
            (dlg.Window.Title, dlg.Window.Width, dlg.Window.Height, dlg.Window.CanResize, dlg.Window.MinWidth, dlg.Window.MinHeight));

        // Initial focus: the list, on the active configuration. Set Active is disabled for it.
        await h.WaitForAsync(() => dlg.FocusedAutomationId == "Dialog.ConfigurationManager.Configurations.Row.Debug", "focus on the active row");
        Assert.Equal(["Debug (active)", "Release"], Rows(dlg));
        Assert.False(dlg.Find<Button>("SetActive").IsEffectivelyEnabled);
        Assert.Equal(["(none)", "Release"], dlg.Find<ComboBox>("Parent").Items.Cast<string>());
        Assert.Equal("all (no parent)", dlg.Find<SelectableTextBlock>("OwnValues").Text);

        // Down, then Insert = New: the sub-dialog starts from the selected row with Inherit checked.
        dlg.Press("Down");
        Assert.Equal("Dialog.ConfigurationManager.Configurations.Row.Release", dlg.FocusedAutomationId);
        dlg.Press("Insert");
        var sub = await h.WaitForDialogAsync("NewConfiguration");
        Assert.Equal("Release", sub.Find<ComboBox>("CopyFrom").SelectedItem);
        Assert.True(sub.Find<CheckBox>("Inherit").IsChecked);
        await h.WaitForAsync(() => sub.FocusedAutomationId == "Dialog.NewConfiguration.Name", "focus on Name");
        TypeText(sub, "Release-Demo");
        sub.Press("Enter");
        await h.WaitForAsync(() => !sub.Window.IsVisible, "New Configuration closed");
        await h.WaitForAsync(() => Rows(dlg).Length == 3, "row added");
        Assert.Equal(["Debug (active)", "Release", "Release-Demo"], Rows(dlg));
        await h.WaitForAsync(() => dlg.FocusedAutomationId == "Dialog.ConfigurationManager.Configurations.Row.Release-Demo", "the new row is selected and focused");
        Assert.Equal("none", dlg.Find<SelectableTextBlock>("OwnValues").Text);
        Assert.Equal("Release", dlg.Find<ComboBox>("Parent").SelectedItem);

        // F2 = Rename: the name is pre-filled and fully selected, so typing replaces it; an invalid name blocks Enter.
        dlg.Press("F2");
        sub = await h.WaitForDialogAsync("RenameConfiguration");
        Assert.Equal(("Rename Configuration", 360.0, false), (sub.Window.Title, sub.Window.Width, sub.Window.CanResize));
        await h.WaitForAsync(() => sub.FocusedAutomationId == "Dialog.RenameConfiguration.Name", "focus on Name");
        Assert.Equal("Release-Demo", sub.Find<TextBox>("Name").Text);
        Assert.False(sub.Find<Button>("Ok").IsEffectivelyEnabled); // unchanged
        TypeText(sub, "debug");
        Assert.Equal("debug", sub.Find<TextBox>("Name").Text);
        Assert.Equal(string.Format(Strings.Msg_ProjectOptions_ConfigNameExists, "debug"), sub.ErrorText("Name"));
        sub.Press("Enter");
        Assert.True(sub.Window.IsVisible);
        sub.Press("Ctrl+A");
        TypeText(sub, "Demo");
        Assert.Null(sub.ErrorText("Name"));
        sub.Press("Enter");
        await h.WaitForAsync(() => !sub.Window.IsVisible, "Rename Configuration closed");
        await h.WaitForAsync(() => Rows(dlg).Contains("Demo"), "row renamed");
        await h.WaitForAsync(() => dlg.FocusedAutomationId == "Dialog.ConfigurationManager.Configurations.Row.Demo", "the renamed row keeps the focus");

        // Alt+I = the parent combo; choosing Debug re-parents and the row moves under it.
        dlg.Press("Alt+I");
        Assert.Equal("Dialog.ConfigurationManager.Parent", dlg.FocusedAutomationId);
        Assert.Equal(["(none)", "Debug", "Release"], dlg.Find<ComboBox>("Parent").Items.Cast<string>());
        dlg.Press("Up");
        await h.WaitForAsync(() => Rows(dlg).SequenceEqual(["Debug (active)", "Demo", "Release"]), "re-parented under Debug");
        Assert.Equal("Debug", dlg.Find<ComboBox>("Parent").SelectedItem);

        // Alt+C = the list; Debug is inherited: Delete is disabled and tells why.
        dlg.Press("Alt+C");
        await h.WaitForAsync(() => dlg.FocusedAutomationId?.StartsWith("Dialog.ConfigurationManager.Configurations", StringComparison.Ordinal) == true, "focus in the list");
        dlg.Press("Home");
        var delete = dlg.Find<Button>("Delete");
        Assert.False(delete.IsEffectivelyEnabled);
        var reason = string.Format(Strings.Msg_ProjectOptions_ConfigDeleteInherited, "Debug", "Demo");
        Assert.Equal((reason, reason, true), ((string?)ToolTip.GetTip(delete), AutomationProperties.GetHelpText(delete), ToolTip.GetShowOnDisabled(delete)));
        dlg.Press("Delete");
        Assert.Empty(h.Messages.Requests);

        // Alt+A = Set Active on Release; then Delete key on Demo asks and removes it.
        dlg.Press("End");
        Assert.Equal("Dialog.ConfigurationManager.Configurations.Row.Release", dlg.FocusedAutomationId);
        dlg.Press("Alt+A");
        await h.WaitForAsync(() => Rows(dlg).SequenceEqual(["Debug", "Demo", "Release (active)"]), "Release active");
        dlg.Press("Alt+C");
        dlg.Press("Up");
        Assert.Equal("Dialog.ConfigurationManager.Configurations.Row.Demo", dlg.FocusedAutomationId);
        h.Messages.Answer("Msg.ProjectOptions.ConfigDeleteConfirm", "Delete");
        dlg.Press("Delete");
        await h.WaitForAsync(() => Rows(dlg).Length == 2, "Demo deleted");
        Assert.Equal(["Demo"], h.Messages.Requests.Select(r => r.Args[0]));
        Assert.True(delete.IsEffectivelyEnabled); // nothing is inherited any more
        Assert.Null(ToolTip.GetTip(delete));

        // Enter in the list = OK: the working copy is applied.
        await h.WaitForAsync(() => dlg.FocusedAutomationId?.StartsWith("Dialog.ConfigurationManager.Configurations", StringComparison.Ordinal) == true, "focus back in the list");
        dlg.Press("Enter");
        await h.WaitForAsync(() => !dlg.Window.IsVisible, "dialog closed");
        await opening;
        var json = JsonDocument.Parse(File.ReadAllText(BuildScenario.ProjectPath(h))).RootElement;
        Assert.Equal(["Debug", "Release"], json.GetProperty("configurations").EnumerateObject().Select(p => p.Name));
        Assert.Equal("Release", json.GetProperty("activeConfiguration").GetString());
        Assert.Equal("Release", h.Services.Projects.Project!.ActiveConfiguration);
    }
}
