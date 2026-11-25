using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Build;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The Project Options page <c>Build Events</c> as the user sees it (dialogs.md §8.7, UI-DLG-299…305; card F09-007).</summary>
public sealed partial class ProjectOptionsDialogTests
{
    /// <summary>Hello.pproj with project-level commands; Release has its own, Debug has none.</summary>
    internal static void UseEvents(HarnessOptions o, string preBuild = "\"tools\\\\gen.exe --out src\\\\Generated.pas\"") =>
        o.InitialFiles[@"Hello\Hello.pproj"] = $$"""
            { "formatVersion": 1, "name": "Hello", "appType": "console", "mainSource": "Hello.pas",
              "configurations": { "Debug": { "optimization": 0, "debugInfo": true, "defines": [], "outputDir": "bin/Debug" },
                                  "Release": { "optimization": 2, "debugInfo": false, "defines": [], "outputDir": "bin/Release",
                                               "events": { "postBuild": [ "tools\\sign.cmd \"$(OutputPath)\"" ], "postBuildWhen": "always" } } },
              "files": [ { "path": "Hello.pas" }, { "path": "Unit1.pas" } ],
              "events": { "preBuild": [ {{preBuild}} ], "postBuild": [ "copy /y \"$(OutputPath)\" \"$(ProjectDir)\\deploy\"" ] } }
            """;

    /// <summary>Chooses an entry of the configuration bar by its text ("All configurations", "Release", "Debug (active)").</summary>
    internal static void ChooseConfiguration(DialogDriver dlg, string title)
    {
        var bar = dlg.Find<ComboBox>("Configuration");
        bar.SelectedIndex = bar.Items.Cast<string>().ToList().IndexOf(title);
        Assert.True(bar.SelectedIndex >= 0, title);
        Dispatcher.UIThread.RunJobs();
    }

    private static string? Mnemonic(Control labelled)
    {
        var text = labelled switch
        {
            Label { Content: string s } => s,
            ContentControl { Content: string s } => s,
            _ => null,
        };
        return text is not null && text.IndexOf('_', StringComparison.Ordinal) is var i and >= 0 ? text.Substring(i + 1, 1) : null;
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-DLG-299")]
    [Trait("Req", "UI-DLG-300")]
    [Trait("Req", "UI-DLG-301")]
    [Trait("Req", "UI-DLG-302")]
    [Trait("Req", "UI-DLG-303")]
    [Trait("Req", "UI-DLG-304")]
    [Trait("Req", "UI-DLG-305")]
    [Trait("Req", "IDE-EVT-020")]
    [Trait("Req", "IDE-EVT-024")]
    public async Task BuildEventsPage_LayoutKeyboardAndIds()
    {
        await using var h = await BuildScenario.StartAsync(o => UseEvents(o));
        var trustFile = Path.Combine(h.Services.UserPaths.RoamingDirectory, "trust.json");
        var (dlg, opening) = await OpenAsync(h);
        try
        {
            ShowPage(dlg, "BuildEvents");

            // --- All configurations (the initial choice): ids, order, texts; no Notice, no OwnEvents / Inherited. ---
            var bar = dlg.Find<ComboBox>("Configuration");
            Assert.True(bar.IsEnabled);
            Assert.Equal([Strings.ProjectOptions_AllConfigurations, "Debug (active)", "Release"], bar.Items.Cast<string>());
            Assert.Equal(0, bar.SelectedIndex);
            Assert.False(dlg.Find<Button>("NewConfiguration").IsEnabled);
            Assert.False(dlg.Find<Button>("DeleteConfiguration").IsEnabled);
            Assert.Null(dlg.TryFind<Control>("Notice"));
            var hint = dlg.Find<TextBlock>("Hint");
            var trustHint = dlg.Find<TextBlock>("TrustHint");
            var pre = dlg.Find<TextBox>("PreBuild");
            var post = dlg.Find<TextBox>("PostBuild");
            var when = dlg.Find<ComboBox>("PostBuildWhen");
            var insert = dlg.Find<Button>("InsertMacro");
            Assert.Equal("Each line is one command. Commands run in the project folder, one after another. A command that ends with an exit code other than 0 stops the build.", hint.Text);
            Assert.Equal("FreexComp asks before it runs commands that are new or have changed.", trustHint.Text);
            Assert.False(dlg.Find<CheckBox>("OwnEvents").IsVisible);
            Assert.False(dlg.Find<TextBlock>("Inherited").IsVisible);
            Assert.Equal(@"tools\gen.exe --out src\Generated.pas", pre.Text);
            Assert.Equal(@"copy /y ""$(OutputPath)"" ""$(ProjectDir)\deploy""", post.Text);
            foreach (var box in (TextBox[])[pre, post])
            {
                Assert.True(box.IsEffectivelyEnabled && box.IsEffectivelyVisible && !box.IsReadOnly);
                Assert.True(box.AcceptsReturn);
                Assert.False(box.AcceptsTab);
                Assert.Equal(Avalonia.Media.TextWrapping.NoWrap, box.TextWrapping);
                Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, ScrollViewer.GetHorizontalScrollBarVisibility(box));
                Assert.True(box.Bounds.Height >= 3 * 18, $"a list field is {box.Bounds.Height} high"); // never lower than three rows
            }

            Assert.Equal(pre.Bounds.Height, post.Bounds.Height, 0.5); // the two lists share the free height equally
            Assert.Equal(["Only after a successful build", "Also after a failed build"], when.Items.Cast<string>());
            Assert.Equal(0, when.SelectedIndex);
            Assert.True(insert.IsEffectivelyEnabled);

            // UI-DLG-299: top to bottom; the menu button stands at the right end of the PostBuildWhen row.
            double Top(Control c) => c.TranslatePoint(default, dlg.Window)!.Value.Y;
            Control[] order = [hint, pre, post, when, trustHint];
            Assert.Equal(order.Select(Top).Order(), order.Select(Top));
            // The menu button belongs to the PostBuildWhen row (with the test font the combo may flow to a second line of it).
            Assert.True(Top(insert) >= Top(post) + post.Bounds.Height && Top(insert) < Top(trustHint), "InsertMacro stands in the PostBuildWhen row");
            Assert.Equal(pre.TranslatePoint(new Point(pre.Bounds.Width, 0), dlg.Window)!.Value.X,
                insert.TranslatePoint(new Point(insert.Bounds.Width, 0), dlg.Window)!.Value.X, 0.5); // right-aligned with the fields

            // UI-DLG-304: mnemonics B, A, R, M (and U below), none of the dialog's own.
            var labels = dlg.Window.GetVisualDescendants().OfType<Label>().Where(l => l.Target is not null).ToList();
            Assert.Equal("B", Mnemonic(labels.Single(l => l.Target == pre)));
            Assert.Equal("A", Mnemonic(labels.Single(l => l.Target == post)));
            Assert.Equal("R", Mnemonic(labels.Single(l => l.Target == when)));
            Assert.Equal("M", Mnemonic(insert));
            Assert.Equal("U", Mnemonic(dlg.Find<CheckBox>("OwnEvents")));

            // --- Keyboard (IDE-EVT-020): Enter adds a line and does not close; Tab leaves the field. ---
            dlg.Type("PreBuild", "");
            Assert.Equal(dlg.FullId("PreBuild"), dlg.FocusedAutomationId);
            dlg.Press("Enter");
            Assert.False(dlg.Window.Model.IsClosed);
            Assert.True(dlg.Window.IsVisible);
            Assert.Equal(@"tools\gen.exe --out src\Generated.pas" + Environment.NewLine, pre.Text);
            dlg.Type("PreBuild", "echo second");
            Assert.Equal(@"tools\gen.exe --out src\Generated.pas" + Environment.NewLine + "echo second", pre.Text);
            dlg.Press("Tab");
            Assert.Equal(dlg.FullId("PostBuild"), dlg.FocusedAutomationId);
            Assert.DoesNotContain('\t', pre.Text!);
            dlg.Press("Shift+Tab");
            Assert.Equal(dlg.FullId("PreBuild"), dlg.FocusedAutomationId);
            dlg.Press("Ctrl+A");
            Assert.Equal((0, pre.Text!.Length), (Math.Min(pre.SelectionStart, pre.SelectionEnd), Math.Max(pre.SelectionStart, pre.SelectionEnd)));

            // --- InsertMacro (UI-DLG-302): one item per macro with its id; the choice goes to the last focused list. ---
            dlg.Type("PostBuild", " ");
            var menu = Assert.IsType<MenuFlyout>(insert.Flyout);
            Assert.Equal(PlacementMode.Bottom, menu.Placement); // below the button
            var items = menu.Items.OfType<MenuItem>().ToList();
            Assert.Equal(EventMacros.Names.Select(n => dlg.FullId("InsertMacro." + n)), items.Select(i => AutomationIds.Get(i)));
            Assert.Equal("$(OutputPath) Full path of the built file", AutomationProperties.GetName(items[^1]));
            dlg.Click("InsertMacro");
            Assert.True(menu.IsOpen);
            dlg.Press("Escape"); // closes the menu, not the dialog
            Assert.False(menu.IsOpen);
            Assert.False(dlg.Window.Model.IsClosed);
            dlg.Click("InsertMacro");
            Assert.True(menu.IsOpen);
            items[2].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            menu.Hide();
            Dispatcher.UIThread.RunJobs();
            Assert.EndsWith(" $(Config)", post.Text, StringComparison.Ordinal);
            Assert.Equal(post.Text!.Length, post.CaretIndex);
            Assert.Equal(dlg.FullId("PostBuild"), dlg.FocusedAutomationId);

            // --- Unknown macro (UI-DLG-303): the line under its field, the dot on the page row, OK and Apply disabled. ---
            Assert.Null(dlg.ErrorText("PreBuild"));
            SetText(dlg, "PreBuild", "echo $(OutDir)");
            Assert.Equal("Unknown macro $(OutDir). Choose one from Insert Macro, or write $$( for the text $(.", dlg.ErrorText("PreBuild"));
            var error = dlg.Find<TextBlock>("Error.PreBuild");
            Assert.True(Top(error) >= Top(pre) + pre.Bounds.Height - 0.5 && Top(error) < Top(post), "the message stands directly under its field");
            Assert.True(dlg.Find<Control>("Page.BuildEvents.Error").IsVisible);
            Assert.False(dlg.Find<Button>("Ok").IsEffectivelyEnabled);
            Assert.False(dlg.Find<Button>("Apply").IsEffectivelyEnabled);
            SetText(dlg, "PreBuild", "echo $$(OutDir) $(ProjectName)");
            Assert.Null(dlg.ErrorText("PreBuild"));
            Assert.False(dlg.Find<Control>("Page.BuildEvents.Error").IsVisible);
            Assert.True(dlg.Find<Button>("Apply").IsEffectivelyEnabled);

            // --- A configuration without its own commands: OwnEvents unchecked, the inherited line, read-only fields. ---
            ChooseConfiguration(dlg, "Debug (active)");
            Assert.True(dlg.Find<Button>("NewConfiguration").IsEnabled);
            var own = dlg.Find<CheckBox>("OwnEvents");
            var inherited = dlg.Find<TextBlock>("Inherited");
            (pre, post, when, insert) = (dlg.Find<TextBox>("PreBuild"), dlg.Find<TextBox>("PostBuild"), dlg.Find<ComboBox>("PostBuildWhen"), dlg.Find<Button>("InsertMacro"));
            Assert.True(own.IsEffectivelyVisible);
            Assert.False(own.IsChecked);
            Assert.True(inherited.IsEffectivelyVisible);
            Assert.Equal("ⓘ This configuration uses the commands of All configurations.", inherited.Text);
            Assert.True(Top(dlg.Find<TextBlock>("Hint")) < Top(own) && Top(own) < Top(inherited) && Top(inherited) < Top(pre));
            Assert.Equal("echo $$(OutDir) $(ProjectName)", pre.Text); // the working copy of the project-level list
            Assert.True(pre.IsReadOnly && post.IsReadOnly);
            Assert.True(pre.IsEffectivelyEnabled, "read-only text stays selectable"); // UI-DLG-301
            Assert.Same(Brush(dlg, "FxcTextSecondaryBrush"), pre.Foreground);
            Assert.False(when.IsEffectivelyEnabled);
            Assert.False(insert.IsEffectivelyEnabled);
            Assert.Contains(dlg.Window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "(Debug)"); // UI-DLG-085

            // Checking makes the fields editable with the same commands; unchecking shows the inherited ones again.
            own.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.False(inherited.IsVisible);
            Assert.False(pre.IsReadOnly || post.IsReadOnly);
            Assert.Same(Brush(dlg, "FxcTextPrimaryBrush"), pre.Foreground);
            Assert.True(when.IsEffectivelyEnabled && insert.IsEffectivelyEnabled);
            Assert.Equal("echo $$(OutDir) $(ProjectName)", pre.Text);
            SetText(dlg, "PreBuild", "echo debug only");

            // --- A configuration with its own commands. ---
            ChooseConfiguration(dlg, "Release");
            Assert.True(dlg.Find<CheckBox>("OwnEvents").IsChecked);
            Assert.False(dlg.Find<TextBlock>("Inherited").IsVisible);
            Assert.Equal(("", @"tools\sign.cmd ""$(OutputPath)""", 1),
                (dlg.Find<TextBox>("PreBuild").Text, dlg.Find<TextBox>("PostBuild").Text, dlg.Find<ComboBox>("PostBuildWhen").SelectedIndex));
            ChooseConfiguration(dlg, "Debug (active)");
            Assert.Equal("echo debug only", dlg.Find<TextBox>("PreBuild").Text);

            // --- Apply writes the project file and nothing else (IDE-EVT-018). ---
            dlg.Click("Apply");
            await h.WaitForAsync(() => !dlg.Find<Button>("Apply").IsEffectivelyEnabled, "applied");
            var json = JsonDocument.Parse(File.ReadAllText(BuildScenario.ProjectPath(h))).RootElement;
            Assert.Equal(["echo $$(OutDir) $(ProjectName)"], json.GetProperty("events").GetProperty("preBuild").EnumerateArray().Select(e => e.GetString()));
            Assert.Equal(["echo debug only"],
                json.GetProperty("configurations").GetProperty("Debug").GetProperty("events").GetProperty("preBuild").EnumerateArray().Select(e => e.GetString()));
            Assert.False(File.Exists(trustFile));
        }
        finally
        {
            if (!dlg.Window.Model.IsClosed)
                dlg.Click("Cancel");
            await opening;
        }
    }

    private static object? Brush(DialogDriver dlg, string key) =>
        dlg.Window.TryFindResource(key, dlg.Window.ActualThemeVariant, out var brush) ? brush : throw new InvalidOperationException(key);
}
