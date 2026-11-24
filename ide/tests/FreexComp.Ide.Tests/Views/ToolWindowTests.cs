using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Tests.Fixtures.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class ToolWindowTests
{
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-007")]
    [Trait("Req", "UI-TW-002")]
    public async Task ShowCommandFocusesPrimaryControl()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.ImplementedPhase = 1; // the sample Messages is the only bottom window
            o.ToolWindows.Add(SampleToolViewModel.Definition("Messages", ToolGroupId.Bottom));
        });
        var tools = h.Services.ToolWindows;
        Assert.True(h.Find<Control>(AutomationIds.DockBottom).IsVisible); // Messages is in the Edit profile
        Assert.True(tools.Hide("Messages"));
        Dispatcher.UIThread.RunJobs();
        Assert.False(h.Find<Control>(AutomationIds.DockBottom).IsVisible);

        h.Press("Alt+5"); // view.messages

        Assert.True(h.Find<Control>(AutomationIds.DockBottom).IsVisible);
        Assert.Equal("Messages", h.Services.Layout.Layout.Bottom.ActiveTool);
        Assert.Equal("Tool.Messages.List", h.FocusedAutomationId);
        Assert.Equal("Messages", h.Services.FocusTracker.Current.ToolWindowName);
        Assert.True(tools.Get("Messages")!.IsActive); // caption shows the focused state
        Assert.Equal("MESSAGES", h.Find<TextBlock>("Tool.Messages.Caption").Text);
        Assert.Equal("Messages", AutomationProperties.GetName(h.Find<Button>("ToolTab.Messages")));

        h.Press("Shift+Escape"); // window.closeToolWindow hides the active tool window
        Assert.False(h.Find<Control>(AutomationIds.DockBottom).IsVisible);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-007")]
    [Trait("Req", "UI-WIN-016")]
    public async Task ProgrammaticActivationKeepsFocus()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.ImplementedPhase = 1; // the samples are the only bottom windows
            o.ToolWindows.Add(SampleToolViewModel.Definition("Messages", ToolGroupId.Bottom));
            o.ToolWindows.Add(SampleToolViewModel.Definition("Output", ToolGroupId.Bottom, titleKey: "Cmd_view_output"));
        });
        var tools = h.Services.ToolWindows;
        var plain = new Border { Focusable = true, Width = 20, Height = 10 };
        AutomationProperties.SetAutomationId(plain, "StatusBar.TestPlain");
        h.Find<Panel>(AutomationIds.StatusBar).Children.Add(plain);
        Dispatcher.UIThread.RunJobs();
        Assert.True(plain.Focus());
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Messages", h.Services.Layout.Layout.Bottom.ActiveTool);

        Assert.True(tools.Show("Output", focus: false));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Output", h.Services.Layout.Layout.Bottom.ActiveTool);
        Assert.Equal("StatusBar.TestPlain", h.FocusedAutomationId);

        tools.Hide("Messages");
        tools.Hide("Output");
        Dispatcher.UIThread.RunJobs();
        Assert.False(h.Find<Control>(AutomationIds.DockBottom).IsVisible);

        Assert.True(tools.Show("Messages", focus: false));
        Dispatcher.UIThread.RunJobs();
        Assert.True(h.Find<Control>(AutomationIds.DockBottom).IsVisible);
        Assert.Equal("Messages", h.Services.Layout.Layout.Bottom.ActiveTool);
        Assert.Equal("StatusBar.TestPlain", h.FocusedAutomationId);
        Assert.False(tools.Get("Messages")!.IsActive);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-A11Y-004")]
    [Trait("Req", "UI-TW-008")]
    public async Task EscapeReturnsFocusToEditor()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.ToolWindows.Add(SampleToolViewModel.Definition("Messages", ToolGroupId.Bottom)));

        h.Press("Alt+5");
        Assert.Equal("Tool.Messages.List", h.FocusedAutomationId);
        h.Press("Escape"); // the list has no local use for Escape
        Assert.Equal(AutomationIds.DocumentArea, h.FocusedAutomationId);
        Assert.True(h.Find<Control>(AutomationIds.DockBottom).IsVisible); // Escape does not hide

        // From the filter box Escape is local first: clear, then back to the list, then to the document.
        Assert.True(h.Find<TextBox>("Tool.Messages.Filter").Focus());
        h.Type("x");
        h.Press("Escape");
        Assert.Equal("Tool.Messages.Filter", h.FocusedAutomationId);
        h.Press("Escape");
        Assert.Equal("Tool.Messages.List", h.FocusedAutomationId);
        h.Press("Escape");
        Assert.Equal(AutomationIds.DocumentArea, h.FocusedAutomationId);
        Assert.Equal("minus", Assert.IsType<Icons.IdeIcon>(h.Find<Button>("Tool.Messages.Hide").Content).IconName);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-012")]
    [Trait("Req", "UI-TW-011")]
    [Trait("Req", "IDE-ARCH-057")]
    public async Task ColumnWidthsPersist()
    {
        byte[] saved;
        await using (var h = await HeadlessHarness.StartAsync(o => o.ToolWindows.Add(SampleGridToolViewModel.Definition("Messages"))))
        {
            var code = h.Find<Control>("Tool.Messages.Column.Code");
            Assert.Equal(72, code.Bounds.Width);

            h.Drag("Tool.Messages.Column.Code.Divider", 40, 0); // the user drags the header divider
            Assert.Equal(112, code.Bounds.Width);
            Assert.Equal(112, h.Services.ToolWindows.Get("Messages")!.ColumnWidth("Code"));
            Assert.Null(h.Services.ToolWindows.Get("Messages")!.ColumnWidth("File")); // never resized

            h.Window.Close();
            Dispatcher.UIThread.RunJobs();
            saved = File.ReadAllBytes(Path.Combine(h.UserDir, "roaming", "uistate.json"));
        }

        // architecture.md §8.1: toolWindows.<Name>.columns maps column ids to widths.
        Assert.Contains("\"toolWindows\": {\n    \"Messages\": {\n      \"columns\": {\n        \"Code\": 112\n      }\n    }\n  }",
            System.Text.Encoding.UTF8.GetString(saved), StringComparison.Ordinal);

        await using (var h = await HeadlessHarness.StartAsync(o =>
        {
            o.ToolWindows.Add(SampleGridToolViewModel.Definition("Messages"));
            o.UserFiles["roaming/uistate.json"] = saved;
        }))
        {
            Assert.Equal(112, h.Find<Control>("Tool.Messages.Column.Code").Bounds.Width);
            Assert.Equal(160, h.Find<Control>("Tool.Messages.Column.File").Bounds.Width);
        }
    }

    /// <summary>I-193: <see cref="ShowCommandFocusesPrimaryControl"/> with the product's Messages and the rest of the bottom dock.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "3")]
    [Trait("Req", "UI-TW-007")]
    [Trait("Req", "UI-TW-002")]
    public async Task ShowCommandFocusesPrimaryControlAtDefaultPhase()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var tools = h.Services.ToolWindows;
        foreach (var name in new[] { "Messages", "Output", "SearchResults" })
            Assert.True(tools.Hide(name));
        Dispatcher.UIThread.RunJobs();
        Assert.False(h.Find<Control>(AutomationIds.DockBottom).IsVisible);

        h.Press("Alt+5"); // view.messages

        Assert.True(h.Find<Control>(AutomationIds.DockBottom).IsVisible);
        Assert.Equal("Messages", h.Services.Layout.Layout.Bottom.ActiveTool);
        Assert.Equal("Tool.Messages", h.FocusedAutomationId); // no messages: the list is hidden (UI-TW-013), the window takes the focus
        Assert.Equal("Messages", h.Services.FocusTracker.Current.ToolWindowName);
        Assert.True(tools.Get("Messages")!.IsActive);
        Assert.Equal("MESSAGES", h.Find<TextBlock>("Tool.Messages.Caption").Text);
        Assert.Equal("Messages", AutomationProperties.GetName(h.Find<Button>("ToolTab.Messages")));

        h.Press("Shift+Escape"); // window.closeToolWindow hides the active tool window
        Assert.False(h.Find<Control>(AutomationIds.DockBottom).IsVisible);
    }

    /// <summary>I-193: <see cref="ProgrammaticActivationKeepsFocus"/> with the product's Messages and Output.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "3")]
    [Trait("Req", "UI-TW-007")]
    [Trait("Req", "UI-WIN-016")]
    public async Task ProgrammaticActivationKeepsFocusAtDefaultPhase()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var tools = h.Services.ToolWindows;
        var plain = new Border { Focusable = true, Width = 20, Height = 10 };
        AutomationProperties.SetAutomationId(plain, "StatusBar.TestPlain");
        h.Find<Panel>(AutomationIds.StatusBar).Children.Add(plain);
        Dispatcher.UIThread.RunJobs();
        Assert.True(plain.Focus());
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Messages", h.Services.Layout.Layout.Bottom.ActiveTool);

        Assert.True(tools.Show("Output", focus: false));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Output", h.Services.Layout.Layout.Bottom.ActiveTool);
        Assert.Equal("StatusBar.TestPlain", h.FocusedAutomationId);

        foreach (var name in new[] { "Messages", "Output", "SearchResults" })
            tools.Hide(name);
        Dispatcher.UIThread.RunJobs();
        Assert.False(h.Find<Control>(AutomationIds.DockBottom).IsVisible);

        Assert.True(tools.Show("Messages", focus: false));
        Dispatcher.UIThread.RunJobs();
        Assert.True(h.Find<Control>(AutomationIds.DockBottom).IsVisible);
        Assert.Equal("Messages", h.Services.Layout.Layout.Bottom.ActiveTool);
        Assert.Equal("StatusBar.TestPlain", h.FocusedAutomationId);
        Assert.False(tools.Get("Messages")!.IsActive);
    }

    /// <summary>I-226: UI-TW-006 at the product's phase 7, without a test override: every P1–P7 window is registered.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "7")]
    [Trait("Req", "UI-TW-006")]
    [Trait("Req", "UI-WIN-011")]
    [Trait("Req", "UI-TW-1401")]
    public async Task EveryWindowIsRegisteredAtProductPhase()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var tools = h.Services.ToolWindows;
        Assert.Equal(7, Core.Composition.ServiceGraph.ImplementedPhase);
        Assert.Equal(7, h.Services.ImplementedPhase);

        foreach (var name in new[] { "ProjectManager", "Structure", "Messages", "Output", "SearchResults", "ObjectInspector", "ToolPalette",
                     "Breakpoints", "CallStack", "Locals", "Watches", "Threads", "EventLog", "DataExplorer" })
            Assert.True(tools.IsRegistered(name), name);
        Assert.False(tools.IsRegistered("ToDo")); // P8+ (UI-TW-700)
        Assert.False(h.Services.Commands.CanExecute("view.todo"));
        Assert.Null(h.TryFind<Control>("Tool.DataExplorer")); // hidden by default
        Assert.True(h.Services.Commands.CanExecute("view.dataExplorer"));
        var view = h.Find<Menu>(AutomationIds.MenuBar).Items.Cast<MenuItem>().Single(m => AutomationIds.Get(m) == AutomationIds.TopMenu("View"));
        var explorer = view.Items.OfType<MenuItem>().Single(i => AutomationIds.Get(i) == AutomationIds.Menu("view.dataExplorer"));
        Assert.True(explorer.IsVisible);
        Assert.True(explorer.Command!.CanExecute(null));
        await h.ExecuteAsync("view.dataExplorer");
        Dispatcher.UIThread.RunJobs();
        Assert.True(h.Find<Control>("Tool.DataExplorer").IsEffectivelyVisible);
        Assert.True(h.Find<Control>("Tool.DataExplorer.Empty").IsEffectivelyVisible);
    }

    /// <summary>I-146: UI-TW-006 at phase 6 (the product's phase until I-226): the P1–P6 windows are registered, the P7 one is not.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "6")]
    [Trait("Req", "UI-TW-006")]
    [Trait("Req", "UI-WIN-011")]
    public async Task DebugWindowsAreRegisteredAtPhase6()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.ImplementedPhase = 6);
        var tools = h.Services.ToolWindows;
        Assert.Equal(6, h.Services.ImplementedPhase);

        foreach (var name in new[] { "ProjectManager", "Structure", "Messages", "Output", "SearchResults", "ObjectInspector", "ToolPalette",
                     "Breakpoints", "CallStack", "Locals", "Watches", "Threads", "EventLog" })
            Assert.True(tools.IsRegistered(name), name);
        Assert.False(tools.IsRegistered("DataExplorer"));
        Assert.False(tools.Show("DataExplorer", focus: true));
        Assert.True(h.Services.Commands.CanExecute("view.callStack"));
        Assert.True(h.Services.Commands.CanExecute("view.eventLog"));
        Assert.False(h.Services.Commands.CanExecute("view.dataExplorer"));
        Assert.True(tools.Show("CallStack", focus: false));
        Dispatcher.UIThread.RunJobs();
        Assert.True(h.Find<Control>("Tool.CallStack").IsEffectivelyVisible);
    }

    /// <summary>I-193, I-207: UI-TW-006 at phase 5 (the product's phase until I-146): the P1–P5 windows are registered, the P6+ ones are not.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "5")]
    [Trait("Req", "UI-TW-006")]
    [Trait("Req", "UI-WIN-011")]
    public async Task LaterPhaseWindowsAreNotRegisteredAtDefaultPhase()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.ImplementedPhase = 5);
        var tools = h.Services.ToolWindows;

        foreach (var name in new[] { "ProjectManager", "Structure", "Messages", "Output", "SearchResults", "ObjectInspector", "ToolPalette" })
            Assert.True(tools.IsRegistered(name), name);
        foreach (var name in new[] { "CallStack", "Locals", "Watches", "Breakpoints", "DataExplorer" })
        {
            Assert.False(tools.IsRegistered(name), name);
            Assert.False(tools.Show(name, focus: true), name);
        }

        Assert.True(h.Services.Commands.CanExecute("view.structure"));
        Assert.True(h.Services.Commands.CanExecute("view.objectInspector"));
        Assert.True(h.Services.Commands.CanExecute("view.toolPalette"));
        Assert.False(h.Services.Commands.CanExecute("view.callStack"));
        Assert.False(h.Services.Commands.CanExecute("view.dataExplorer"));
        var view = h.Find<Menu>(AutomationIds.MenuBar).Items.Cast<MenuItem>().Single(m => AutomationIds.Get(m) == AutomationIds.TopMenu("View"));
        var explorer = view.Items.OfType<MenuItem>().Single(i => AutomationIds.Get(i) == AutomationIds.Menu("view.dataExplorer"));
        Assert.True(explorer.IsVisible);
        Assert.False(explorer.IsEnabled && explorer.Command!.CanExecute(null)); // visible and disabled (UI-MENU-004)
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-006")]
    [Trait("Req", "UI-WIN-011")]
    public async Task LaterPhaseWindowsAreNotRegistered()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.ImplementedPhase = 1; // UI-TW-006 at the implemented phase 1
            o.ToolWindows.Add(SampleToolViewModel.Definition("ProjectManager", ToolGroupId.Left, phase: 1, titleKey: "Cmd_view_projectManager"));
            o.ToolWindows.Add(SampleToolViewModel.Definition("Structure", ToolGroupId.Left, phase: 3, titleKey: "Cmd_view_structure"));
        });
        var tools = h.Services.ToolWindows;

        Assert.True(tools.IsRegistered("ProjectManager"));
        Assert.False(tools.IsRegistered("Structure"));
        Assert.Null(tools.Get("Structure"));
        Assert.Equal(["ToolTab.ProjectManager"], h.FindAll("ToolTab.").Select(c => AutomationIds.Get(c)));
        Assert.False(tools.Show("Structure", focus: true));
        Assert.True(h.Services.Commands.CanExecute("view.projectManager"));
        Assert.False(h.Services.Commands.CanExecute("view.structure"));

        var view = h.Find<Menu>(AutomationIds.MenuBar).Items.Cast<MenuItem>().Single(m => AutomationIds.Get(m) == AutomationIds.TopMenu("View"));
        var structure = view.Items.OfType<MenuItem>().Single(i => AutomationIds.Get(i) == AutomationIds.Menu("view.structure"));
        Assert.True(structure.IsVisible);
        Assert.False(structure.IsEnabled && structure.Command!.CanExecute(null)); // visible and disabled (UI-MENU-004)
    }
}
