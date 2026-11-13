using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Input;
using FreexComp.Ide.Tests.Infrastructure;
using CoreGesture = FreexComp.Ide.Core.Commands.KeyGesture;
using KeyModifiers = Avalonia.Input.KeyModifiers;

namespace FreexComp.Ide.Tests.Input;

[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class KeyRoutingTests
{
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-KEY-004")]
    [Trait("Req", "IDE-ARCH-043")]
    public async Task InnermostScopeWins()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var duplicate = new Recorder();
        var nextDocument = new Recorder();
        h.Services.Commands.RegisterHandler("edit.duplicateLine", duplicate); // Editor scope, Ctrl+D
        h.Services.Commands.RegisterHandler("window.nextDocument", nextDocument); // Global
        h.Services.Keymap.SetGestures("window.nextDocument", [CoreGesture.Parse("Ctrl+D")]);
        var editor = AddFocusable(h, "StatusBar.TestEditor");
        CommandRouting.SetScope(editor, CommandScope.Editor);
        var plain = AddFocusable(h, "StatusBar.TestPlain");

        Focus(editor);
        Assert.Equal(CommandScope.Editor, h.Services.FocusTracker.Current.Scope);
        h.Press("Ctrl+D");
        Focus(plain);
        Assert.Equal(CommandScope.Global, h.Services.FocusTracker.Current.Scope);
        h.Press("Ctrl+D");

        Assert.Equal(1, duplicate.Calls);
        Assert.Equal(1, nextDocument.Calls);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-042")]
    [Trait("Req", "UI-CMD-010")]
    public async Task TextBoxKeepsEditingKeys()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var copy = new Recorder();
        var save = new Recorder();
        var target = new Target();
        h.Services.Commands.RegisterHandler("file.save", save);
        var host = AddFocusable(h, "StatusBar.TestTool");
        CommandRouting.SetScope(host, CommandScope.ToolList);
        CommandRouting.SetToolWindow(host, "Messages");
        host.DataContext = target;
        var box = new TextBox { Text = "abc" };
        AutomationProperties.SetAutomationId(box, "StatusBar.TestBox");
        h.Find<Panel>(AutomationIds.StatusBar).Children.Add(box);
        Dispatcher.UIThread.RunJobs();

        Focus(box);
        Assert.True(h.Services.FocusTracker.Current.IsTextInput);
        h.Press("Ctrl+A");
        h.Press("Ctrl+C");

        Assert.Empty(target.Executed); // not routed: left to the TextBox
        Assert.Equal("abc", box.SelectedText); // the TextBox handled Ctrl+A itself

        h.Press("Ctrl+S"); // global commands still work from a text input
        Assert.Equal(1, save.Calls);

        Focus(host);
        h.Press("Ctrl+C");
        Assert.Equal(["edit.copy"], target.Executed); // routed to the tool window's target
        Assert.Equal(0, copy.Calls);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-042")]
    [Trait("Req", "UI-CMD-010")]
    public async Task MenuEditCommandsActOnFocusedTextBox()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var box = new TextBox { Text = "abc", Width = 100 };
        AutomationProperties.SetAutomationId(box, "StatusBar.TestBox");
        h.Find<Panel>(AutomationIds.StatusBar).Children.Add(box);
        Dispatcher.UIThread.RunJobs();
        Focus(box);

        ClickMenu(h, "edit.selectAll");
        Assert.Equal("abc", box.SelectedText);

        ClickMenu(h, "edit.cut");
        await h.WaitForAsync(() => box.Text == "", "cut by the menu");

        ClickMenu(h, "edit.paste");
        await h.WaitForAsync(() => box.Text == "abc", "paste by the menu");

        h.Click(AutomationIds.ToolBarButton("edit.undo"));
        Assert.Equal("", box.Text);
        h.Click(AutomationIds.ToolBarButton("edit.redo"));
        Assert.Equal("abc", box.Text);

        box.SelectAll();
        Dispatcher.UIThread.RunJobs();
        ClickMenu(h, "edit.copy");
        box.Text = "";
        ClickMenu(h, "edit.paste");
        await h.WaitForAsync(() => box.Text == "abc", "paste of the copied text");
        Assert.Equal("StatusBar.TestBox", h.FocusedAutomationId); // the clicks left focus in the TextBox
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-KEY-007")]
    public async Task DisabledCommandGestureIsConsumed()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var plain = AddFocusable(h, "StatusBar.TestPlain");
        Focus(plain);
        var handled = TrackHandled(h);

        h.Press("Ctrl+Shift+B"); // project.build: later phase, disabled
        h.Press("Delete");       // edit.delete: disabled, but Delete falls through (UI-KEY-007)
        h.Press("Ctrl+Shift+F12"); // unbound

        Assert.Equal([true, false, false], handled);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-KEY-002")]
    public void PunctuationIsMatchedByTheTypedCharacter()
    {
        Assert.Equal(CoreGesture.Parse("Ctrl+/"), KeyMapping.FromKeyEvent(Key.Oem2, KeyModifiers.Control, "/"));
        Assert.Equal(CoreGesture.Parse("Ctrl+/"), KeyMapping.FromKeyEvent(Key.OemQuestion, KeyModifiers.Control, null));
        Assert.Equal(CoreGesture.Parse("Ctrl+-"), KeyMapping.FromKeyEvent(Key.Oem4, KeyModifiers.Control, "-")); // other layout
        Assert.Equal(CoreGesture.Parse("Ctrl+Shift+/"), KeyMapping.FromKeyEvent(Key.OemQuestion, KeyModifiers.Control | KeyModifiers.Shift, "?"));
        Assert.Equal(CoreGesture.Parse("F5"), KeyMapping.FromKeyEvent(Key.F5, KeyModifiers.None, null));
    }

    private static Control AddFocusable(HeadlessHarness h, string id)
    {
        var control = new Border { Focusable = true, Width = 20, Height = 10 };
        AutomationProperties.SetAutomationId(control, id);
        h.Find<Panel>(AutomationIds.StatusBar).Children.Add(control);
        Dispatcher.UIThread.RunJobs();
        return control;
    }

    /// <summary>Opens the Edit menu and clicks the item of <paramref name="commandId"/> with the mouse.</summary>
    private static void ClickMenu(HeadlessHarness h, string commandId)
    {
        h.Click(AutomationIds.TopMenu("Edit"));
        var top = h.Find<MenuItem>(AutomationIds.TopMenu("Edit"));
        Assert.True(top.IsSubMenuOpen);
        var item = top.Items.OfType<MenuItem>().Single(i => AutomationIds.Get(i) == AutomationIds.Menu(commandId));
        Assert.True(item.IsEffectivelyEnabled, $"{commandId} should be enabled (focus state: {h.Services.FocusTracker.Current})");
        var popup = TopLevel.GetTopLevel(item) ?? throw new InvalidOperationException("Edit menu popup not shown.");
        var point = item.TranslatePoint(new Avalonia.Point(item.Bounds.Width / 2, item.Bounds.Height / 2), popup)!.Value;
        popup.MouseDown(point, MouseButton.Left);
        popup.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.False(top.IsSubMenuOpen);
    }

    private static void Focus(Control control)
    {
        Assert.True(control.Focus());
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Records whether each KeyDown ended handled; the focused test control handles no keys itself.</summary>
    private static List<bool> TrackHandled(HeadlessHarness h)
    {
        var handled = new List<bool>();
        h.Window.AddHandler(InputElement.KeyDownEvent, (_, e) => handled.Add(e.Handled), RoutingStrategies.Bubble, handledEventsToo: true);
        return handled;
    }

    private sealed class Recorder : ICommandHandler
    {
        public int Calls { get; private set; }

        public bool CanExecute(string? argument) => true;

        public Task ExecuteAsync(string? argument)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class Target : ICommandTarget
    {
        public List<string> Executed { get; } = [];

        public bool CanExecute(string commandId, string? argument) => commandId == "edit.copy";

        public Task<bool> ExecuteAsync(string commandId, string? argument)
        {
            Executed.Add(commandId);
            return Task.FromResult(true);
        }
    }
}
