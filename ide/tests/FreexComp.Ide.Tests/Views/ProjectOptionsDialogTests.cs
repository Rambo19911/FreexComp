using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The Project Options dialog (I-107, dialogs.md §8).</summary>
[Trait("Phase", "3")]
[Trait("Kind", "Ui")]
public sealed class ProjectOptionsDialogTests
{
    private const string Extra = """
        , "searchPaths": ["lib"], "defines": ["P1"], "custom": { "kept": [1, 2] },
          "events": { "preBuild": ["echo pre"], "postBuild": [] }
        """;

    private static async Task<(DialogDriver Dialog, Task Opening)> OpenAsync(HeadlessHarness h)
    {
        var opening = h.Services.Commands.ExecuteAsync("project.options");
        return (await h.WaitForDialogAsync("ProjectOptions"), opening);
    }

    private static void ShowPage(DialogDriver dlg, string pageId)
    {
        var pages = dlg.Find<ListBox>("Pages");
        pages.SelectedItem = pages.Items.Cast<Core.Dialogs.ProjectOptionsPage>().Single(p => p.Id == pageId);
        Dispatcher.UIThread.RunJobs();
    }

    private static void SetText(DialogDriver dlg, string field, string text)
    {
        dlg.Find<TextBox>(field).Text = text;
        Dispatcher.UIThread.RunJobs();
    }

    private static JsonElement ReadProject(HeadlessHarness h) => JsonDocument.Parse(File.ReadAllText(BuildScenario.ProjectPath(h))).RootElement;

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-080")]
    [Trait("Req", "UI-DLG-081")]
    [Trait("Req", "UI-DLG-085")]
    public async Task RoundTripsPprojKeys()
    {
        await using var h = await BuildScenario.StartAsync(projectExtra: Extra);
        var dir = Path.GetDirectoryName(BuildScenario.ProjectPath(h))!;
        var (dlg, opening) = await OpenAsync(h);
        Assert.Equal(("Project Options — Hello", 760.0, 520.0, true), (dlg.Window.Title, dlg.Window.Width, dlg.Window.Height, dlg.Window.CanResize));
        Assert.Equal(7, dlg.FindAll("Dialog.ProjectOptions.Page.").Count(c => c is ListBoxItem));

        // Application: name, read-only facts, disabled app types stay visible.
        Assert.Equal("Hello", dlg.Find<TextBox>("Name").Text);
        Assert.Equal(BuildScenario.ProjectPath(h), dlg.Find<SelectableTextBlock>("ProjectFile").Text);
        Assert.Equal("Object Pascal", dlg.Find<TextBlock>("Language").Text);
        Assert.Equal(4, dlg.Find<ComboBox>("AppType").ItemCount);
        Assert.False(dlg.Find<ComboBox>("Configuration").IsEnabled);
        Assert.Equal(Strings.ProjectOptions_AllConfigurations, dlg.Find<ComboBox>("Configuration").SelectedItem);
        SetText(dlg, "Name", "Hello2");

        // Compiler [cfg]: Debug is edited; the label names it.
        ShowPage(dlg, "Compiler");
        Assert.True(dlg.Find<ComboBox>("Configuration").IsEnabled);
        Assert.Equal("Debug (active)", dlg.Find<ComboBox>("Configuration").SelectedItem);
        Assert.Contains(Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(dlg.Window).OfType<TextBlock>(), t => t.Text == "(Debug)");
        dlg.Find<ComboBox>("Optimization").SelectedIndex = 1;
        dlg.Find<CheckBox>("DebugInfo").IsChecked = false;
        Dispatcher.UIThread.RunJobs();

        // Search paths: the picked folder inside the project is relative; a duplicate is dropped; a missing one only warns.
        ShowPage(dlg, "SearchPaths");
        Directory.CreateDirectory(Path.Combine(dir, @"src\units"));
        h.FileDialogs.NextFolder(Path.Combine(dir, @"src\units"));
        dlg.Click("SearchPaths.Add");
        await h.WaitForAsync(() => dlg.Find<ListBox>("SearchPaths").ItemCount == 2, "path added");
        h.FileDialogs.NextFolder(Path.Combine(dir, "LIB"));
        dlg.Click("SearchPaths.Add");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, dlg.Find<ListBox>("SearchPaths").ItemCount);
        Assert.True(dlg.Find<Button>("Ok").IsEffectivelyEnabled);

        // Defines: split, trimmed, de-duplicated; effective list joins both.
        ShowPage(dlg, "ConditionalDefines");
        SetText(dlg, "ProjectDefines", " A; B;a;; ");
        SetText(dlg, "ConfigDefines", "DEBUG;B;X");
        Assert.Equal("A; B; DEBUG; X", dlg.Find<TextBlock>("Effective").Text);

        // Output [cfg]: the executable follows the folder and the new name.
        ShowPage(dlg, "Output");
        SetText(dlg, "OutputDir", @"out\dbg");
        Assert.Equal(Path.Combine(dir, @"out\dbg\Hello2.exe"), dlg.Find<TextBlock>("OutputFile").Text);
        Assert.Equal(Path.Combine(dir, @"obj\Debug") + "\\", dlg.Find<TextBlock>("ObjDir").Text);

        // Run: the fields of Run Parameters.
        ShowPage(dlg, "Run");
        SetText(dlg, "Arguments", "-q");

        // Build Events: read-only with the notice.
        ShowPage(dlg, "BuildEvents");
        Assert.Equal("echo pre", dlg.Find<TextBox>("PreBuild").Text);
        Assert.False(dlg.Find<TextBox>("PreBuild").IsEnabled);
        Assert.Equal(Strings.Msg_ProjectOptions_EventsNotSupported, dlg.Find<TextBlock>("Notice").Text);

        dlg.Click("Ok");
        await h.WaitForAsync(() => !dlg.Window.IsVisible, "dialog closed");
        await opening;

        var json = ReadProject(h);
        Assert.Equal("Hello2", json.GetProperty("name").GetString());
        Assert.Equal(["lib", "src/units"], json.GetProperty("searchPaths").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["A", "B"], json.GetProperty("defines").EnumerateArray().Select(e => e.GetString()));
        var debug = json.GetProperty("configurations").GetProperty("Debug");
        Assert.Equal((1, false, "out/dbg"), (debug.GetProperty("optimization").GetInt32(), debug.GetProperty("debugInfo").GetBoolean(),
            debug.GetProperty("outputDir").GetString()));
        Assert.Equal(["DEBUG", "B", "X"], debug.GetProperty("defines").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(2, json.GetProperty("configurations").GetProperty("Release").GetProperty("optimization").GetInt32());
        Assert.Equal("-q", json.GetProperty("run").GetProperty("arguments").GetString());
        // Keys the dialog does not show are untouched.
        Assert.Equal(1, json.GetProperty("formatVersion").GetInt32());
        Assert.Equal([1, 2], json.GetProperty("custom").GetProperty("kept").EnumerateArray().Select(e => e.GetInt32()));
        Assert.Equal(2, json.GetProperty("files").GetArrayLength());
        Assert.Equal("echo pre", json.GetProperty("events").GetProperty("preBuild")[0].GetString());
        Assert.Equal("Hello.pproj", Path.GetFileName(BuildScenario.ProjectPath(h)));

        // Reopening shows the stored values and the last page.
        (dlg, opening) = await OpenAsync(h);
        Assert.Equal("BuildEvents", ((Core.Dialogs.ProjectOptionsPage)dlg.Find<ListBox>("Pages").SelectedItem!).Id);
        ShowPage(dlg, "Output");
        Assert.Equal("out/dbg", dlg.Find<TextBox>("OutputDir").Text);
        dlg.Press("Escape");
        await h.WaitForAsync(() => !dlg.Window.IsVisible, "dialog closed");
        await opening;
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-081")]
    [Trait("Req", "UI-DLG-086")]
    public async Task ApplyOkCancelSemantics()
    {
        await using var h = await BuildScenario.StartAsync();
        var (dlg, opening) = await OpenAsync(h);
        ShowPage(dlg, "Application");
        var apply = dlg.Find<Button>("Apply");
        Assert.False(apply.IsEffectivelyEnabled);

        // Apply writes and stays open; it is disabled again until the next change.
        SetText(dlg, "Name", "Applied");
        Assert.True(apply.IsEffectivelyEnabled);
        dlg.Click("Apply");
        await h.WaitForAsync(() => ReadProject(h).GetProperty("name").GetString() == "Applied", "applied");
        Assert.True(dlg.Window.IsVisible);
        Assert.False(apply.IsEffectivelyEnabled);
        Assert.Equal("Applied", h.Services.Projects.Project!.Name);

        // An invalid field disables OK and Apply and marks its page.
        SetText(dlg, "Name", "1bad");
        Assert.NotNull(dlg.ErrorText("Name"));
        Assert.False(dlg.Find<Button>("Ok").IsEffectivelyEnabled);
        Assert.False(apply.IsEffectivelyEnabled);
        Assert.True(dlg.Find<Avalonia.Controls.Shapes.Ellipse>("Page.Application.Error").IsVisible);
        ShowPage(dlg, "Output");
        SetText(dlg, "OutputDir", "");
        Assert.Equal(Strings.Msg_ProjectOptions_OutputDirInvalid, dlg.ErrorText("OutputDir"));
        Assert.True(dlg.Find<Avalonia.Controls.Shapes.Ellipse>("Page.Output.Error").IsVisible);
        SetText(dlg, "OutputDir", "bin/Debug");
        ShowPage(dlg, "ConditionalDefines");
        SetText(dlg, "ProjectDefines", "OK;9no");
        Assert.Equal(string.Format(Strings.Msg_ProjectOptions_DefineInvalid, "9no"), dlg.ErrorText("ProjectDefines"));
        SetText(dlg, "ProjectDefines", "");

        // Cancel discards the working copy; what Apply wrote stays.
        ShowPage(dlg, "Application");
        SetText(dlg, "Name", "Discarded");
        Assert.True(dlg.Find<Button>("Ok").IsEffectivelyEnabled);
        dlg.Press("Escape");
        await h.WaitForAsync(() => !dlg.Window.IsVisible, "dialog canceled");
        await opening;
        Assert.Equal("Applied", ReadProject(h).GetProperty("name").GetString());
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-082")]
    [Trait("Req", "UI-DLG-083")]
    [Trait("Req", "UI-DLG-084")]
    public async Task ConfigurationNewAndDelete()
    {
        await using var h = await BuildScenario.StartAsync();
        var (dlg, opening) = await OpenAsync(h);
        ShowPage(dlg, "Compiler");
        var combo = dlg.Find<ComboBox>("Configuration");
        Assert.Equal(["Debug (active)", "Release"], combo.Items.Cast<string>());

        // New…: the name is validated; the copy starts from the selected configuration.
        dlg.Click("NewConfiguration");
        var sub = await h.WaitForDialogAsync("NewConfiguration");
        Assert.Equal("Debug", sub.Find<ComboBox>("CopyFrom").SelectedItem);
        sub.Find<TextBox>("Name").Text = "bad name";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Strings.Msg_ProjectOptions_ConfigNameInvalid, sub.ErrorText("Name"));
        Assert.False(sub.Find<Button>("Ok").IsEffectivelyEnabled);
        sub.Find<TextBox>("Name").Text = "release";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(string.Format(Strings.Msg_ProjectOptions_ConfigNameExists, "release"), sub.ErrorText("Name"));
        sub.Find<TextBox>("Name").Text = "Profile";
        Dispatcher.UIThread.RunJobs();
        sub.Press("Enter");
        await h.WaitForAsync(() => !sub.Window.IsVisible, "sub-dialog closed");
        await h.WaitForAsync(() => dlg.Find<ComboBox>("Configuration").SelectedItem as string == "Profile", "new configuration selected");
        Assert.Equal(["Debug (active)", "Release", "Profile"], dlg.Find<ComboBox>("Configuration").Items.Cast<string>());
        Assert.False(dlg.Find<CheckBox>("DebugInfo").IsChecked == false); // copied from Debug
        Assert.Equal(0, dlg.Find<ComboBox>("Optimization").SelectedIndex);

        // Delete asks; deleting the active configuration makes the first remaining one active.
        dlg.Find<ComboBox>("Configuration").SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        h.Messages.Answer("Msg.ProjectOptions.ConfigDeleteConfirm", "Delete");
        dlg.Click("DeleteConfiguration");
        await h.WaitForAsync(() => dlg.Find<ComboBox>("Configuration").ItemCount == 2, "Debug deleted");
        Assert.Equal(["Release (active)", "Profile"], dlg.Find<ComboBox>("Configuration").Items.Cast<string>());
        Assert.Equal(["Debug"], h.Messages.Requests.Where(r => r.MessageId == "Msg.ProjectOptions.ConfigDeleteConfirm").Select(r => r.Args[0]));
        dlg.Find<ComboBox>("Configuration").SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        dlg.Click("DeleteConfiguration");
        await h.WaitForAsync(() => dlg.Find<ComboBox>("Configuration").ItemCount == 1, "Profile deleted");
        // The last one cannot be deleted.
        Assert.False(dlg.Find<Button>("DeleteConfiguration").IsEffectivelyEnabled);
        Assert.Equal(Strings.Msg_ProjectOptions_ConfigDeleteLast, ToolTip.GetTip(dlg.Find<Button>("DeleteConfiguration")));

        dlg.Click("Ok");
        await h.WaitForAsync(() => !dlg.Window.IsVisible, "dialog closed");
        await opening;
        var json = ReadProject(h);
        Assert.Equal(["Release"], json.GetProperty("configurations").EnumerateObject().Select(p => p.Name));
        Assert.Equal("Release", json.GetProperty("activeConfiguration").GetString());
        Assert.Equal("Release", h.Services.Projects.Project!.ActiveConfiguration);
    }
}
