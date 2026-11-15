using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.Views;

namespace FreexComp.Ide.Tests.Views;

/// <summary>Layout, button strip, field behaviour and accessible names shared by every dialog (dialogs.md §1, accessibility.md §4).</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class DialogLayoutTests
{
    /// <summary>(command, dialog name) of the Phase 1 dialogs that open without further set-up.</summary>
    private static readonly (string Command, string Dialog)[] Dialogs =
    [
        ("file.newProject", "NewProject"), ("file.newFile", "NewFile"), ("tools.options", "Options"), ("help.about", "About"),
        ("search.goToLine", "GoToLine"), ("window.documents", "Documents"),
    ];

    private static async Task<HeadlessHarness> StartWithDocumentAsync()
    {
        var h = await HeadlessHarness.StartAsync(o => o.InitialFiles["A.pas"] = "unit A;\ninterface\nimplementation\nend.\n");
        await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, "A.pas"));
        Dispatcher.UIThread.RunJobs();
        return h;
    }

    /// <summary>Opens each dialog, runs <paramref name="check"/> on it and cancels it.</summary>
    private static async Task ForEachDialogAsync(HeadlessHarness h, Action<DialogDriver> check)
    {
        foreach (var (command, name) in Dialogs)
        {
            var running = h.Services.Commands.ExecuteAsync(command);
            var dialog = await h.WaitForDialogAsync(name);
            Dispatcher.UIThread.RunJobs();
            try
            {
                check(dialog);
            }
            finally
            {
                dialog.Press("Escape");
                await running;
                await h.WaitForAsync(() => h.Window.OwnedWindows.Count == 0, name + " closed");
            }
        }
    }

    private static Color Token(HeadlessHarness h, string brush) => Assert.IsAssignableFrom<ISolidColorBrush>(
        Application.Current!.TryGetResource(brush, h.Window.ActualThemeVariant, out var value) ? value : null).Color;

    private static Rect InWindow(DialogDriver d, Control c) => new(c.TranslatePoint(default, d.Window)!.Value, c.Bounds.Size);

    /// <summary>
    /// UI-DLG-002: OS title bar with the dialog's own title (no product-name suffix), bg.panel background, outer padding
    /// space.6; labels in one left column sized to the widest label, fields beside them in the second column.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-002")]
    public async Task DialogFrameAndLabelColumn()
    {
        await using var h = await StartWithDocumentAsync();
        await ForEachDialogAsync(h, d =>
        {
            Assert.False(d.Window.Title!.EndsWith("FreexComp", StringComparison.Ordinal) && d.Name != "About", $"{d.Name}: {d.Window.Title}");
            Assert.DoesNotContain(" - ", d.Window.Title, StringComparison.Ordinal);
            Assert.Equal(Token(h, "FxcBgPanelBrush"), Assert.IsAssignableFrom<ISolidColorBrush>(d.Window.Background).Color);
            var body = ((DockPanel)d.Window.Content!).Children.Last();
            Assert.Equal(Assert.IsType<Thickness>(Application.Current!.FindResource("FxcSpace6All")), body.Margin);
        });

        // The label column of New Project: Name and Location labels left-aligned in one column, fields start together.
        var running = h.Services.Commands.ExecuteAsync("file.newProject");
        var project = await h.WaitForDialogAsync("NewProject");
        Dispatcher.UIThread.RunJobs();
        var labels = project.Window.GetVisualDescendants().OfType<Label>().Where(l => l.Target is TextBox).ToList();
        Assert.Equal(2, labels.Count);
        Assert.Single(labels.Select(l => InWindow(project, l).Left).Distinct());
        var name = InWindow(project, project.Find<TextBox>("Name"));
        var location = InWindow(project, project.Find<TextBox>("Location"));
        Assert.Equal(name.Left, location.Left);
        Assert.True(name.Left >= labels.Max(l => InWindow(project, l).Right), "fields start right of the widest label");
        Assert.True(location.Top - name.Bottom >= 8, "row gap space.4");
        project.Press("Escape");
        await running;
    }

    /// <summary>
    /// UI-DLG-003: the button strip at the bottom over a 1 px border.subtle line; default (accent) first, Cancel after the
    /// secondary actions, Apply last; buttons at least 80 × 26 with gap space.4; auxiliary buttons on the left.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-003")]
    public async Task ButtonStripOrderAndMetrics()
    {
        await using var h = await StartWithDocumentAsync();
        await ForEachDialogAsync(h, d =>
        {
            var strip = Assert.IsType<Border>(((DockPanel)d.Window.Content!).Children[0]);
            Assert.Equal(Avalonia.Controls.Dock.Bottom, DockPanel.GetDock(strip));
            Assert.Equal(new Thickness(0, 1, 0, 0), strip.BorderThickness);
            Assert.Equal(Token(h, "FxcBorderSubtleBrush"), Assert.IsAssignableFrom<ISolidColorBrush>(strip.BorderBrush).Color);

            var panels = ((Grid)strip.Child!).Children.OfType<StackPanel>().ToList();
            var left = panels.Single(p => p.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Left);
            var right = panels.Single(p => p.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Right);
            Assert.All(panels, p => Assert.Equal(8, p.Spacing));
            var buttons = right.Children.OfType<Button>().OrderBy(b => InWindow(d, b).Left).ToList();
            Assert.NotEmpty(buttons);
            Assert.Contains("accent", buttons[0].Classes); // the default action first
            var ids = buttons.Select(b => AutomationIds.Get(b)!.Split('.')[^1]).ToList();
            if (ids.Contains("Cancel"))
                Assert.Equal(ids.Contains("Apply") ? ids.Count - 2 : ids.Count - 1, ids.IndexOf("Cancel"));
            if (ids.Contains("Apply"))
                Assert.Equal("Apply", ids[^1]);
            Assert.All(right.Children.OfType<Button>().Concat(left.Children.OfType<Button>()).Where(b => b.IsVisible), b =>
            {
                Assert.True(b.Bounds.Width >= 80, $"{AutomationIds.Get(b)} {b.Bounds.Width} wide");
                Assert.Equal(26, b.Bounds.Height);
            });
            Assert.True(InWindow(d, right).Right >= InWindow(d, strip).Right - 16 - 0.5, "right-aligned");
            Assert.True(InWindow(d, left).Left <= InWindow(d, strip).Left + 16, "auxiliary buttons left-aligned"); // headless glyphs are wider, so no overlap check
        });
    }

    /// <summary>UI-DLG-009: a text input selects all its text when it gets focus by keyboard (Tab) or by its mnemonic.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-009")]
    public async Task TextInputSelectsAllOnKeyboardFocus()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var running = h.Services.Commands.ExecuteAsync("file.newProject");
        var d = await h.WaitForDialogAsync("NewProject");
        await h.WaitForAsync(() => d.FocusedAutomationId == "Dialog.NewProject.Name", "name focused");
        var name = d.Find<TextBox>("Name");
        var location = d.Find<TextBox>("Location");

        d.Press("Tab"); // Name → Location
        Assert.Equal("Dialog.NewProject.Location", d.FocusedAutomationId);
        Assert.Equal(location.Text, location.SelectedText);
        location.SelectionStart = location.SelectionEnd = 0;

        d.Press("Alt+N"); // the Name label's mnemonic
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Dialog.NewProject.Name", d.FocusedAutomationId);
        Assert.Equal(name.Text, name.SelectedText);

        location.Focus(Avalonia.Input.NavigationMethod.Pointer); // a click places the caret instead
        location.SelectionStart = location.SelectionEnd = 0;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("", location.SelectedText);

        d.Press("Escape");
        await running;
    }

    /// <summary>
    /// UI-A11Y-022: input fields are labelled by their label (<c>LabeledBy</c>); a validation message is the invalid
    /// field's <c>HelpText</c>, and goes away with the message.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-A11Y-022")]
    public async Task FieldsAreLabelledAndExposeValidation()
    {
        await using var h = await StartWithDocumentAsync();
        await ForEachDialogAsync(h, d =>
        {
            foreach (var field in d.Window.GetVisualDescendants().OfType<Control>().Where(c => c is TextBox or ComboBox or NumericUpDown && c.IsEffectivelyVisible && c.TemplatedParent is null))
            {
                if (field is TextBox { PlaceholderText.Length: > 0 } && AutomationProperties.GetLabeledBy(field) is null)
                    continue; // a filter box without a label (its name is the placeholder)
                var label = AutomationProperties.GetLabeledBy(field);
                Assert.True(label is Label { IsEffectivelyVisible: true }, $"{d.Name}: {AutomationIds.Get(field)} has no LabeledBy");
            }
        });

        var running = h.Services.Commands.ExecuteAsync("file.newProject");
        var project = await h.WaitForDialogAsync("NewProject");
        var name = project.Find<TextBox>("Name");
        Assert.Same(project.Window.GetVisualDescendants().OfType<Label>().Single(l => l.Target == name), AutomationProperties.GetLabeledBy(name));
        Assert.Null(AutomationProperties.GetHelpText(name));
        name.Text = "1bad";
        project.Find<TextBox>("Location").Focus(Avalonia.Input.NavigationMethod.Tab); // leaving the field validates it
        await h.WaitForAsync(() => project.ErrorText("Name") is not null, "name message");
        Assert.Equal(project.ErrorText("Name"), AutomationProperties.GetHelpText(name));
        name.Text = "Good";
        await h.WaitForAsync(() => project.ErrorText("Name") is null, "message gone");
        Assert.Null(AutomationProperties.GetHelpText(name));
        project.Press("Escape");
        await running;
    }

    private static string? NameOf(Control control) =>
        ControlAutomationPeer.CreatePeerForElement(control).GetName() is { Length: > 0 } name ? name : null;

    private static readonly Type[] Interactive = [typeof(Button), typeof(MenuItem), typeof(TextBox), typeof(ComboBox), typeof(ListBox), typeof(TreeView), typeof(TabItem), typeof(Slider)];

    private static List<string> Unnamed(Visual root) =>
        [.. new[] { root }.Concat(root.GetVisualDescendants()).Concat(((ILogical)root).GetLogicalDescendants()).OfType<Control>().Distinct()
            .Where(c => c.TemplatedParent is null && Interactive.Any(t => t.IsInstanceOfType(c)) && c is not MenuItem { Header: "-" or null } and not Separator)
            .Where(c => NameOf(c) is null)
            .Select(c => AutomationIds.Get(c) ?? c.GetType().Name)];

    /// <summary>UI-A11Y-020: every interactive element has a human-readable automation name besides its AutomationId (main window and dialogs).</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-A11Y-020")]
    public async Task InteractiveElementsHaveNames()
    {
        await using var h = await StartWithDocumentAsync();
        var missing = Unnamed(h.Window);
        Assert.True(missing.Count == 0, "Main window controls without a name: " + string.Join(", ", missing));
        Assert.Equal(Core.Resources.Strings.ResourceManager.GetString("Cmd_file_saveAll", Core.Resources.Strings.Culture), AutomationProperties.GetName(h.Find<Button>(AutomationIds.ToolBarButton("file.saveAll")))); // icon-only: the command title

        await ForEachDialogAsync(h, d =>
        {
            var unnamed = Unnamed(d.Window);
            Assert.True(unnamed.Count == 0, $"{d.Name}: controls without a name: {string.Join(", ", unnamed)}");
        });
    }

    /// <summary>Every resource text as a pattern: placeholders match anything, mnemonic markers are dropped.</summary>
    private static List<System.Text.RegularExpressions.Regex> ResourcePatterns()
    {
        var set = Core.Resources.Strings.ResourceManager.GetResourceSet(System.Globalization.CultureInfo.InvariantCulture, true, true)!;
        return [.. set.Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Value!)
            .SelectMany(v => v.Split('\n').Append(v)) // a paragraph, or the whole multi-paragraph text in one block
            .Select(v => v.Replace("&", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal).Trim().TrimEnd(':'))
            .Where(v => System.Text.RegularExpressions.Regex.Replace(v, @"\{\d+(:[^}]*)?}", "").Any(char.IsLetter)) // "{0}" alone would match anything
            .Distinct(StringComparer.Ordinal)
            .Select(v => new System.Text.RegularExpressions.Regex(
                "^" + System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Escape(v), @"\\\{\d+(:[^}]*)?}", ".*") + "$",
                System.Text.RegularExpressions.RegexOptions.Singleline))];
    }

    /// <summary>Resource texts that are only placeholders and punctuation, with one group per placeholder.</summary>
    private static List<System.Text.RegularExpressions.Regex> ResourceFormats()
    {
        var set = Core.Resources.Strings.ResourceManager.GetResourceSet(System.Globalization.CultureInfo.InvariantCulture, true, true)!;
        return [.. set.Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Value!)
            .Where(v => System.Text.RegularExpressions.Regex.Replace(v, @"\{\d+(:[^}]*)?}", "") is var literal
                && v.Contains('{', StringComparison.Ordinal) && !literal.Any(char.IsLetter) && literal.Trim().Length > 0)
            .Distinct(StringComparer.Ordinal)
            .SelectMany(v => new[] { "(.+?)", "(.+)" }.Select(group => new System.Text.RegularExpressions.Regex( // lazy and greedy splits
                "^" + System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Escape(v), @"\\\{\d+(:[^}]*)?}", group) + "$")))];
    }

    /// <summary>The user-visible texts of a window: text blocks, string content and headers, tooltips, the title.</summary>
    private static IEnumerable<string> VisibleTexts(Window window) =>
        new[] { window.Title ?? "" }
            .Concat(window.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible).SelectMany(c => new[]
            {
                (c as TextBlock)?.Text, (c as ContentControl)?.Content as string, (c as Avalonia.Controls.Primitives.HeaderedSelectingItemsControl)?.Header as string,
                (c as MenuItem)?.Header as string, ToolTip.GetTip(c) as string,
            }))
            .OfType<string>()
            .Select(s => s.Replace("_", "", StringComparison.Ordinal).Trim().TrimStart('▾', '▸', '●', ' ').TrimEnd(':')) // expander and modified glyphs are symbols; a label's colon is layout
            .Where(s => s.Length > 0 && s.Any(char.IsLetter));

    /// <summary>
    /// IDE-ARCH-005: every user-visible string comes from <c>Strings.resx</c> — each text shown in the main window and the
    /// Phase 1 dialogs is a resource text (or a resource format filled in), apart from data: file names, paths, the
    /// product version, project-template texts and the text of documents.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-005")]
    public async Task VisibleTextsComeFromResources()
    {
        await using var h = await StartWithDocumentAsync();
        var patterns = ResourcePatterns();
        // Data: project-template names and descriptions come from the template files (project-manager.md), not from resources.
        var templateTexts = h.Services.Projects.Templates.SelectMany(p => new[] { p.Name, p.Description }).ToHashSet(StringComparer.Ordinal);
        bool IsData(string s) => templateTexts.Contains(s) || System.Text.RegularExpressions.Regex.IsMatch(s, @"^\.[a-z]+$") /* a file extension */ || s.Contains("A.pas", StringComparison.Ordinal) || s.Contains(":\\", StringComparison.Ordinal)
            || s.StartsWith("unit A", StringComparison.Ordinal) || s.StartsWith(".NET ", StringComparison.Ordinal);
        // A tool-window caption shows its resource title upper-case (UI-TW-002).
        // A pure format ("{0} ({1})") is filled with resource texts or shortcuts.
        var formats = ResourceFormats();
        bool IsGesture(string s) => System.Text.RegularExpressions.Regex.IsMatch(s, @"^((Ctrl|Shift|Alt)\+)*[A-Za-z0-9]+$");
        bool Plain(string s) => patterns.Any(p => p.IsMatch(s));
        bool FromResource(string s) => Plain(s)
            || formats.Any(f => f.Match(s) is { Success: true } m && m.Groups.Values.Skip(1).All(g => IsGesture(g.Value.Trim()) || Plain(g.Value.Trim())))
            || (s == s.ToUpperInvariant() && patterns.Any(p => new System.Text.RegularExpressions.Regex(p.ToString(), System.Text.RegularExpressions.RegexOptions.IgnoreCase).IsMatch(s)));
        List<string> Foreign(Window w) => [.. VisibleTexts(w).Where(s => !IsData(s) && !FromResource(s)).Distinct()];

        Assert.DoesNotContain(patterns, p => p.IsMatch("Zqxv is no resource text")); // the check can fail
        Assert.Contains("File", VisibleTexts(h.Window)); // and sees the menu bar
        var main = Foreign(h.Window);
        Assert.True(main.Count == 0, "Main window texts not from Strings.resx: " + string.Join(" | ", main));
        await ForEachDialogAsync(h, d =>
        {
            var foreign = Foreign(d.Window);
            Assert.True(foreign.Count == 0, $"{d.Name}: texts not from Strings.resx: {string.Join(" | ", foreign)}");
        });
    }
}