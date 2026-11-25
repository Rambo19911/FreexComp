using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The Output tool window (I-102, tool-windows.md §7, IDE-P3-075).</summary>
[Trait("Phase", "3")]
[Trait("Kind", "Ui")]
public sealed class OutputViewTests
{
    private static Task<HeadlessHarness> StartAsync(Action<HarnessOptions>? configure = null) =>
        HeadlessHarness.StartAsync(o =>
        {
            o.ImplementedPhase = 3; // the Output tool window is a Phase 3 window
            configure?.Invoke(o);
        });

    private static TextEditor Text(HeadlessHarness h) => h.Find<TextEditor>("Tool.Output.Text");

    private static async Task AppendAsync(HeadlessHarness h, string channel, string text, OutputKind kind = OutputKind.Normal)
    {
        var before = h.Services.Output.GetLines(channel).Count;
        h.Services.Output.Append(channel, text, kind);
        await h.WaitForAsync(() => h.Services.Output.GetLines(channel).Count != before, "output flushed");
        Dispatcher.UIThread.RunJobs();
    }

    private static ScrollViewer Scroller(TextEditor editor) =>
        editor.GetVisualDescendants().OfType<ScrollViewer>().First();

    private static bool AtEnd(TextEditor editor)
    {
        var s = Scroller(editor);
        return s.Offset.Y + s.Viewport.Height >= s.Extent.Height - 1;
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-500")]
    [Trait("Req", "UI-TW-501")]
    [Trait("Req", "UI-TW-505")]
    [Trait("Req", "UI-TW-506")]
    [Trait("Req", "UI-TW-507")]
    [Trait("Req", "UI-TW-508")]
    public async Task ChannelsAndLineLimit()
    {
        await using var h = await StartAsync(o => o.Settings["output.maxLines"] = 1000);
        h.Press("Alt+6"); // view.output
        await h.WaitForAsync(() => h.FocusedAutomationId == "Tool.Output.Text", "Output text focused");
        var text = Text(h);
        Assert.True(text.IsReadOnly);
        var empty = h.Find<Control>("Tool.Output.Empty");
        Assert.True(empty.IsVisible); // UI-TW-507: overlaid, the text control stays
        Assert.Equal(Strings.Msg_Output_Empty, Assert.IsType<TextBlock>(empty).Text);

        var combo = h.Find<ComboBox>("Tool.Output.Channel");
        Assert.Equal(["Build", "Program"], combo.Items.Cast<object>().Select(i => i.ToString()));
        Assert.Equal(0, combo.SelectedIndex);

        await AppendAsync(h, OutputChannels.Build, "Build started", OutputKind.Ide);
        await AppendAsync(h, OutputChannels.Program, "Hello", OutputKind.Normal);
        Assert.Equal("Build started", text.Text);
        Assert.False(empty.IsVisible);

        combo.SelectedIndex = 1; // output.selectChannel
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Hello", text.Text);
        await h.ExecuteAsync("output.selectChannel", OutputChannels.Build);
        Assert.Equal(0, combo.SelectedIndex);
        Assert.Equal("Build started", text.Text);
        await h.ExecuteAsync("output.selectChannel", OutputChannels.Program);

        // UI-TW-505: the limit drops the oldest 10 % and puts the notice first, once.
        var many = string.Join("\n", Enumerable.Range(1, 1000).Select(i => "line " + i.ToString(CultureInfo.InvariantCulture)));
        await AppendAsync(h, OutputChannels.Program, many);
        var lines = text.Text.Split("\n");
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Output_Truncated, 1000), lines[0]);
        Assert.Equal("line 1000", lines[^1]);
        Assert.True(lines.Length <= 1001);

        // UI-TW-506: Clear empties the visible channel only; Copy All copies it whole.
        await h.ExecuteAsync("output.copyAll");
        Assert.Equal(string.Join("\r\n", h.Services.Output.GetLines(OutputChannels.Program).Select(l => l.Text)), await h.Services.Clipboard.GetTextAsync());
        Assert.True(h.Services.Commands.CanExecute("output.clear"));
        h.Click("Tool.Output.ToolBar.output.clear");
        Assert.Equal("", text.Text);
        Assert.True(empty.IsVisible);
        Assert.Single(h.Services.Output.GetLines(OutputChannels.Build));

        // UI-TW-506: Word Wrap follows output.wordWrap.
        Assert.False(text.WordWrap);
        await h.ExecuteAsync("output.toggleWordWrap");
        Assert.True(h.Services.Settings.Get(Core.Settings.SettingsCatalog.OutputWordWrap));
        Assert.True(text.WordWrap);
        Assert.True(h.Find<ToggleButton>("Tool.Output.ToolBar.output.toggleWordWrap").IsChecked);
    }

    /// <summary>UI-TW-500 (I-186): Ctrl+F opens the find panel over the Output text (find only); Ctrl+wheel zooms it.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-500")]
    public async Task FindPanelAndZoom()
    {
        await using var h = await StartAsync();
        Assert.True(h.Services.ToolWindows.Show("Output", focus: true));
        Dispatcher.UIThread.RunJobs();
        var text = Text(h);
        await AppendAsync(h, OutputChannels.Build, "alpha\nbeta error\ngamma error");
        text.TextArea.Focus();
        text.TextArea.Caret.Offset = 0;
        await h.WaitForAsync(() => h.FocusedAutomationId == "Tool.Output.Text", "Output text focused");

        // Ctrl+F: the panel opens with the search box focused; Enter finds the next match in the Output text.
        h.Press("Ctrl+F");
        await h.WaitForAsync(() => h.FocusedAutomationId == "Tool.Output.Text.FindPanel.SearchText", "search box focused");
        Assert.True(h.Find<Control>("Tool.Output.Text.FindPanel").IsVisible);
        h.Type("error");
        h.Press("Enter");
        await h.WaitForAsync(() => text.SelectedText == "error", "first match selected");
        Assert.Equal(2, text.TextArea.Caret.Line);
        h.Press("Enter");
        await h.WaitForAsync(() => text.TextArea.Caret.Line == 3, "second match");
        Assert.Equal("error", text.SelectedText);
        Assert.Contains(Strings.FindPanel_SearchText, AutomationProperties(h, "Tool.Output.Text.FindPanel.SearchText"));

        // Escape closes the panel and returns to the text; the editor's find state is untouched.
        h.Press("Escape");
        await h.WaitForAsync(() => h.FocusedAutomationId == "Tool.Output.Text", "back in the text");
        Assert.False(h.Find<Control>("Tool.Output.Text.FindPanel").IsVisible);
        Assert.Equal("beta error\ngamma error", text.Text.Split('\n', 2)[1]); // nothing was edited

        // Ctrl+wheel zooms by 1 pt per notch within 6…72; plain wheel scrolls (no zoom).
        var size = text.FontSize;
        var point = text.TextArea.TextView.TranslatePoint(new Avalonia.Point(20, 10), h.Window)!.Value;
        h.Window.MouseWheel(point, new Avalonia.Vector(0, 1), Avalonia.Input.RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(size + 1, text.FontSize);
        h.Window.MouseWheel(point, new Avalonia.Vector(0, -1), Avalonia.Input.RawInputModifiers.Control);
        h.Window.MouseWheel(point, new Avalonia.Vector(0, -1), Avalonia.Input.RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(size - 1, text.FontSize);
        h.Window.MouseWheel(point, new Avalonia.Vector(0, 1));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(size - 1, text.FontSize);

        // editor.mouseWheelZoom off: Ctrl+wheel does not zoom.
        h.Services.Settings.Set(Core.Settings.SettingsCatalog.EditorMouseWheelZoom, false);
        h.Window.MouseWheel(point, new Avalonia.Vector(0, 1), Avalonia.Input.RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(size - 1, text.FontSize);
    }

    private static string AutomationProperties(HeadlessHarness h, string id) =>
        Avalonia.Automation.AutomationProperties.GetName(h.Find<Control>(id)) ?? "";

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-504")]
    public async Task AutoScrollStopsWhenScrolledUp()
    {
        await using var h = await StartAsync();
        Assert.True(h.Services.ToolWindows.Show("Output", focus: true));
        Dispatcher.UIThread.RunJobs();
        var text = Text(h);
        await AppendAsync(h, OutputChannels.Build, string.Join("\n", Enumerable.Range(1, 300).Select(i => $"a{i}")));
        await h.WaitForAsync(() => AtEnd(text) && Scroller(text).Offset.Y > 0, "follows the end");

        // Scrolling up stops following.
        Scroller(text).Offset = new Avalonia.Vector(0, 0);
        Dispatcher.UIThread.RunJobs();
        await AppendAsync(h, OutputChannels.Build, "more");
        Assert.Equal(0, Scroller(text).Offset.Y);

        // Ctrl+End resumes.
        text.TextArea.Focus();
        h.Press("Ctrl+End");
        await h.WaitForAsync(() => AtEnd(text), "back at the end");
        await AppendAsync(h, OutputChannels.Build, string.Join("\n", Enumerable.Range(1, 50).Select(i => $"b{i}")));
        await h.WaitForAsync(() => AtEnd(text), "follows again");

        // Scroll Lock disables following entirely.
        await h.ExecuteAsync("output.toggleScrollLock");
        Assert.True(h.Services.Commands.Get("output.toggleScrollLock").IsChecked);
        var offset = Scroller(text).Offset.Y;
        await AppendAsync(h, OutputChannels.Build, string.Join("\n", Enumerable.Range(1, 50).Select(i => $"c{i}")));
        Assert.Equal(offset, Scroller(text).Offset.Y);
        Assert.False(AtEnd(text));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-075")]
    [Trait("Req", "UI-TW-503")]
    [Trait("Req", "UI-TW-502")]
    public async Task DiagnosticLineNavigates()
    {
        await using var h = await StartAsync(o => o.InitialFiles["Unit1.pas"] = "unit Unit1;\r\ninterface\r\nvar X: Integer;\r\nimplementation\r\nend.\r\n");
        var path = Path.Combine(h.TempDir, "Unit1.pas");
        Assert.True(h.Services.ToolWindows.Show("Output", focus: true));
        Dispatcher.UIThread.RunJobs();
        await AppendAsync(h, OutputChannels.Build, "Build started", OutputKind.Ide);
        await AppendAsync(h, OutputChannels.Build, $"{path}(3,5): warning W1002: Variable 'X' is declared but never used");
        await AppendAsync(h, OutputChannels.Build, $"{path}(4,2): error E2003: Undeclared identifier 'Foo'", OutputKind.Error);
        var output = Assert.IsType<OutputViewModel>(h.Services.ToolWindows.Get("Output"));
        Assert.Equal(["text.primary", "status.warning", "status.error"], output.Lines.Select(l => OutputViewModel.ColorToken(OutputChannels.Build, l)));

        // Enter with the caret in a diagnostic line opens the file at line:col and focuses the editor.
        var text = Text(h);
        text.TextArea.Focus();
        text.TextArea.Caret.Line = 2;
        text.TextArea.Caret.Column = 10;
        Dispatcher.UIThread.RunJobs();
        h.Press("Enter");
        await h.WaitForAsync(() => h.Services.Documents.ActiveDocument is TextDocumentViewModel { Title: "Unit1.pas" }, "Unit1.pas active");
        var doc = Assert.IsType<TextDocumentViewModel>(h.Services.Documents.ActiveDocument);
        await h.WaitForAsync(() => h.FocusedAutomationId == "Editor.Unit1.pas", "editor focused");
        Assert.Equal(new TextPosition(3, 5), doc.ViewState.Caret);

        // Double-click on another diagnostic line navigates too; a plain line does nothing.
        Assert.True(h.Services.ToolWindows.Show("Output", focus: true));
        Dispatcher.UIThread.RunJobs();
        Assert.True(await output.NavigateToLineAsync(3));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new TextPosition(4, 2), doc.ViewState.Caret);
        Assert.False(await output.NavigateToLineAsync(1));

        // A double-click below the text puts the caret into the last line: a missing file reports Msg.Messages.SourceMissing.
        await AppendAsync(h, OutputChannels.Build, $"{Path.Combine(h.TempDir, "Gone.pas")}(1,1): error E2003: x", OutputKind.Error);
        h.DoubleClick("Tool.Output.Text");
        await h.WaitForAsync(() => h.Services.Status.Message == Strings.Msg_Messages_SourceMissing, "source missing reported");
        Assert.Equal(4, text.TextArea.Caret.Line);
    }

    /// <summary>
    /// I-282: the selected channel ("Build", "Program") is fully visible in the combo box of the tool bar at every
    /// scale factor (UI-TOK-062); the combo box keeps the height of design-tokens.md §8.
    /// </summary>
    [AvaloniaTheory(Timeout = 30000)]
    [Trait("Req", "UI-TW-501")]
    [Trait("Req", "UI-TOK-062")]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public async Task SourceCombo_SelectedTextIsNotClipped(double scale)
    {
        await using var h = await StartAsync(o => o.Scale = scale);
        Assert.Equal(scale, h.Window.RenderScaling);
        h.Press("Alt+6"); // view.output
        await h.WaitForAsync(() => h.FocusedAutomationId == "Tool.Output.Text", "Output text focused");
        var combo = h.Find<ComboBox>("Tool.Output.Channel");
        Assert.Equal(["Build", "Program"], combo.Items.Cast<object>().Select(i => i.ToString()));

        foreach (var index in new[] { 0, 1 })
        {
            combo.SelectedIndex = index;
            Dispatcher.UIThread.RunJobs();
            ComboBoxChecks.AssertSelectedTextNotClipped(combo);
        }

        Assert.Equal(24, combo.Bounds.Height); // design-tokens.md §8: a combo box of a tool bar
    }
}
