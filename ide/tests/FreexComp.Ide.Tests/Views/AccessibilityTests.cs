using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Tests.Fixtures.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class AccessibilityTests
{
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-A11Y-003")]
    public async Task PaneCycleOrder()
    {
        await using var h = await StartWithThreeDocks();

        h.Press("F6"); // nothing focused yet: the cycle starts at the document area
        Assert.Equal(AutomationIds.DocumentArea, h.FocusedAutomationId);
        h.Press("F6");
        Assert.Equal("Tool.ProjectManager.List", h.FocusedAutomationId);
        h.Press("F6");
        Assert.Equal("Tool.Messages.List", h.FocusedAutomationId);
        h.Press("F6");
        Assert.Equal("Tool.Sample.List", h.FocusedAutomationId);
        h.Press("F6"); // UI-WIN-042: the status bar is one stop after the right dock
        Assert.Equal("StatusBar.State", h.FocusedAutomationId);
        h.Press("F6"); // wrap
        Assert.Equal(AutomationIds.DocumentArea, h.FocusedAutomationId);
        h.Press("Shift+F6");
        Assert.Equal("StatusBar.State", h.FocusedAutomationId);
        h.Press("Shift+F6");
        Assert.Equal("Tool.Sample.List", h.FocusedAutomationId);
        h.Press("Shift+F6");
        Assert.Equal("Tool.Messages.List", h.FocusedAutomationId);

        h.Services.ToolWindows.Hide("Messages"); // hidden docks are skipped
        Dispatcher.UIThread.RunJobs();
        h.Press("Alt+1");
        Assert.Equal("Tool.ProjectManager.List", h.FocusedAutomationId);
        h.Press("F6");
        Assert.Equal("Tool.Sample.List", h.FocusedAutomationId);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-A11Y-004")]
    [Trait("Req", "UI-A11Y-005")]
    public async Task EscapeReturnsFocusToEditor()
    {
        await using var h = await StartWithThreeDocks();

        h.Press("Alt+5");
        Assert.Equal("Tool.Messages.List", h.FocusedAutomationId);
        h.Press("Alt+0"); // window.focusEditor: the active document (the document area while none is open)
        Assert.Equal(AutomationIds.DocumentArea, h.FocusedAutomationId);

        h.Press("Alt+1");
        Assert.Equal("Tool.ProjectManager.List", h.FocusedAutomationId);
        h.Press("Escape");
        Assert.Equal(AutomationIds.DocumentArea, h.FocusedAutomationId);

        h.Press("Alt+5");
        h.Press("Alt+5"); // the same shortcut again: back to the document, the window stays
        Assert.Equal(AutomationIds.DocumentArea, h.FocusedAutomationId);
        Assert.True(h.Find<Control>(AutomationIds.DockBottom).IsVisible);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-A11Y-002")]
    [Trait("Req", "UI-TB-008")]
    public async Task MenuBarOperableByKeyboard()
    {
        await using var h = await StartWithThreeDocks();
        h.Press("Alt+5");
        Assert.Equal("Tool.Messages.List", h.FocusedAutomationId);

        PressAltAlone(h); // focuses the menu bar
        Assert.Equal(AutomationIds.TopMenu("File"), h.FocusedAutomationId);
        h.Press("Right");
        Assert.Equal(AutomationIds.TopMenu("Edit"), h.FocusedAutomationId);
        h.Press("Escape"); // top level: back to the previously focused element
        Assert.Equal("Tool.Messages.List", h.FocusedAutomationId);

        // Alt+V, L (Toolbars), S (Standard): mnemonics open and execute.
        Assert.True(h.Services.Settings.Get(SettingsCatalog.AppearanceToolbarStandard));
        h.Press("Alt+V");
        Assert.True(h.Find<MenuItem>(AutomationIds.TopMenu("View")).IsSubMenuOpen);
        h.Press("L");
        h.Press("S");
        Assert.False(h.Services.Settings.Get(SettingsCatalog.AppearanceToolbarStandard));
        Assert.False(h.Find<MenuItem>(AutomationIds.TopMenu("View")).IsSubMenuOpen);
        Assert.Equal("Tool.Messages.List", h.FocusedAutomationId); // the executed item closed the menu: focus is back
        await h.ExecuteAsync("view.toolbar.standard"); // the Standard toolbar back for the next part

        // Toolbar row (UI-TB-008): Alt, then Ctrl+Tab → first button; Right → next; Enter runs; Escape → the document.
        // Disabled buttons cannot take focus (accepted deviation), so two Phase 1 commands get handlers here.
        var newFile = new Recorder();
        h.Services.Commands.RegisterHandler("file.newFile", newFile);
        h.Services.Commands.RegisterHandler("file.openProject", new Recorder());
        h.Services.Commands.InvalidateCanExecute();
        Dispatcher.UIThread.RunJobs();
        PressAltAlone(h);
        Assert.Equal(AutomationIds.TopMenu("File"), h.FocusedAutomationId);
        h.Press("Ctrl+Tab");
        Assert.Equal(AutomationIds.ToolBarButton("file.newFile"), h.FocusedAutomationId);
        h.Press("Right");
        Assert.Equal(AutomationIds.ToolBarButton("file.openProject"), h.FocusedAutomationId);
        h.Press("Left");
        Assert.Equal(AutomationIds.ToolBarButton("file.newFile"), h.FocusedAutomationId);
        h.Press("Enter");
        Assert.Equal(1, newFile.Calls);
        h.Press("Escape");
        Assert.Equal(AutomationIds.DocumentArea, h.FocusedAutomationId);
        Assert.False(h.Find<Button>(AutomationIds.ToolBarButton("file.newFile")).Focusable); // a click again leaves focus alone
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-A11Y-013")]
    public async Task FocusNeverLostWhenElementDisappears()
    {
        await using var h = await StartWithThreeDocks();

        h.Press("Alt+5");
        Assert.Equal("Tool.Messages.List", h.FocusedAutomationId);
        h.Press("Shift+Escape"); // the focused tool window disappears
        Assert.Equal(AutomationIds.DocumentArea, h.FocusedAutomationId);

        var transient = new Border { Focusable = true, Width = 20, Height = 10 };
        AutomationProperties.SetAutomationId(transient, "StatusBar.TestPlain");
        var bar = h.Find<Panel>(AutomationIds.StatusBar);
        bar.Children.Add(transient);
        Dispatcher.UIThread.RunJobs();
        Assert.True(transient.Focus());
        Dispatcher.UIThread.RunJobs();
        bar.Children.Remove(transient); // any focused element removed
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(AutomationIds.DocumentArea, h.FocusedAutomationId);
    }

    /// <summary>Project Manager (left), Messages (bottom) and a sample tool window shown in the right upper group.</summary>
    private static async Task<HeadlessHarness> StartWithThreeDocks()
    {
        var h = await HeadlessHarness.StartAsync(o =>
        {
            o.ToolWindows.Add(SampleToolViewModel.Definition("ProjectManager", ToolGroupId.Left, titleKey: "Cmd_view_projectManager"));
            o.ToolWindows.Add(SampleToolViewModel.Definition("Messages", ToolGroupId.Bottom));
            o.ToolWindows.Add(SampleToolViewModel.Definition("Sample", ToolGroupId.RightUpper));
        });
        h.Services.ToolWindows.Show("Sample", focus: false);
        Dispatcher.UIThread.RunJobs();
        return h;
    }

    private sealed class Recorder : Core.Commands.ICommandHandler
    {
        public int Calls { get; private set; }

        public bool CanExecute(string? argument) => true;

        public Task ExecuteAsync(string? argument)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private static void PressAltAlone(HeadlessHarness h)
    {
        h.Window.KeyPress(Key.LeftAlt, RawInputModifiers.Alt, PhysicalKey.AltLeft, null);
        h.Window.KeyRelease(Key.LeftAlt, RawInputModifiers.None, PhysicalKey.AltLeft, null);
        Dispatcher.UIThread.RunJobs();
    }
}
