using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AvaloniaEdit;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.EndToEnd;

/// <summary>
/// Card F09-004 user story with the real build engine and toolchain (phase8-configurations.md §7): a configuration that
/// inherits from another one is created, given a define of its own, made active, built and run.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "E2E")]
[Trait("Category", "Toolchain")]
public sealed class Phase8ConfigurationTests
{
    [AvaloniaFact(Timeout = 120000)]
    [Trait("Req", "IDE-CFG-006")]
    [Trait("Req", "IDE-CFG-015")]
    [Trait("Req", "IDE-CFG-027")]
    public async Task E2E_Phase8_DerivedConfiguration_BuildsAndRuns()
    {
        const string Program = "program Hello;\r\n\r\nbegin\r\n{$IFDEF DEMO}\r\n  WriteLn('demo edition');\r\n{$ELSE}\r\n  WriteLn('full edition');\r\n{$ENDIF}\r\nend.\r\n";
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.RealBuild = true;
            o.InitialFiles[@"Hello\Hello.pproj"] = """
                { "formatVersion": 1, "name": "Hello", "appType": "console", "mainSource": "Hello.pas",
                  "configurations": { "Debug": { "optimization": 0, "debugInfo": true, "defines": [], "outputDir": "bin/Debug" },
                                      "Release": { "optimization": 2, "debugInfo": false, "defines": [], "outputDir": "bin/Release" } },
                  "files": [ { "path": "Hello.pas" } ] }
                """;
            o.InitialFiles[@"Hello\Hello.pas"] = Program;
        });
        var toolchain = h.Services.Toolchain.Resolve();
        if (toolchain.LlvmBinDir is null || toolchain.RtlDir is null)
            Assert.Fail("LLVM toolchain not found (run scripts/bootstrap.ps1)"); // testing.md: fails, never skips
        string Abs(string relative) => Path.Combine(h.TempDir, "Hello", relative);
        Assert.True(await h.Services.Projects.OpenAsync(Abs("Hello.pproj")));
        var pm = (ProjectManagerViewModel)h.Services.ToolWindows.Get(ProjectManagerViewModel.ToolName)!;
        await h.WaitForAsync(() => !pm.IsCheckingMissingFiles, "missing-file check done");
        string Output() => h.TryFind<TextEditor>("Tool.Output.Text")?.Document.Text ?? "";

        // Configuration Manager: New from Release (Inherit is checked), Set Active, OK.
        var opening = h.Services.Commands.ExecuteAsync("project.configurationManager");
        var manager = await h.WaitForDialogAsync("ConfigurationManager");
        var list = manager.Find<ListBox>("Configurations");
        list.SelectedItem = list.Items.OfType<ListBoxItem>().Single(i => AutomationIds.Get(i) == "Dialog.ConfigurationManager.Configurations.Row.Release");
        Dispatcher.UIThread.RunJobs();
        manager.Click("New");
        var sub = await h.WaitForDialogAsync("NewConfiguration");
        Assert.Equal("Release", sub.Find<ComboBox>("CopyFrom").SelectedItem);
        Assert.True(sub.Find<CheckBox>("Inherit").IsChecked);
        sub.Find<TextBox>("Name").Text = "Release-Demo";
        Dispatcher.UIThread.RunJobs();
        sub.Click("Ok");
        await h.WaitForAsync(() => !sub.Window.IsVisible, "New Configuration closed");
        await h.WaitForAsync(() => manager.TryFind<ListBoxItem>("Configurations.Row.Release-Demo") is { IsSelected: true }, "the new row is selected");
        manager.Click("SetActive");
        manager.Click("Ok");
        await h.WaitForAsync(() => !manager.Window.IsVisible, "Configuration Manager closed");
        await opening;
        Assert.Equal("Release-Demo", h.Services.Projects.Project!.ActiveConfiguration);

        // Project Options: the define DEMO of the derived configuration.
        opening = h.Services.Commands.ExecuteAsync("project.options");
        var options = await h.WaitForDialogAsync("ProjectOptions");
        var pages = options.Find<ListBox>("Pages");
        pages.SelectedItem = pages.Items.Cast<Core.Dialogs.ProjectOptionsPage>().Single(p => p.Id == "ConditionalDefines");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Release-Demo (active)", options.Find<ComboBox>("Configuration").SelectedItem);
        options.Find<TextBox>("ConfigDefines").Text = "DEMO";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("DEMO", options.Find<SelectableTextBlock>("Effective").Text);
        options.Click("Ok");
        await h.WaitForAsync(() => !options.Window.IsVisible, "Project Options closed");
        await opening;

        // The project file holds exactly the link and the define for it.
        var json = JsonDocument.Parse(File.ReadAllText(Abs("Hello.pproj"))).RootElement;
        Assert.Equal("""{"inherits":"Release","defines":["DEMO"]}""", JsonSerializer.Serialize(json.GetProperty("configurations").GetProperty("Release-Demo")));
        Assert.Equal("Release-Demo", json.GetProperty("activeConfiguration").GetString());

        // Build and run: the {$IFDEF DEMO} text, from the configuration's own output folder.
        string Diagnostics() => $"state {h.Services.State.Current.State}; messages: "
            + string.Join(" | ", h.Services.Messages.Items.Select(i => $"{i.Code} {i.Text}"))
            + "; log: " + string.Join(" | ", h.Log.Lines.Where(l => l.StartsWith("ERROR ", StringComparison.Ordinal)))
            + "; output: " + Output();
        await h.ExecuteAsync("run.startWithoutDebugging");
        try
        {
            await h.WaitForAsync(() => Output().Contains("Program exited with code", StringComparison.Ordinal) || h.Services.Messages.ErrorCount > 0, "build and run finished");
        }
        catch (HarnessAssertionException e)
        {
            Assert.Fail(e.Message + " — " + Diagnostics());
        }

        Assert.True(h.Services.Messages.ErrorCount == 0, Diagnostics());
        await h.WaitForAsync(() => h.Services.State.Current.State == IdeState.Idle, "idle after the run");
        Assert.Contains("demo edition", Output(), StringComparison.Ordinal);
        Assert.DoesNotContain("full edition", Output(), StringComparison.Ordinal);
        Assert.Contains("Program exited with code 0 after ", Output(), StringComparison.Ordinal);
        Assert.True(File.Exists(Abs(@"bin\Release-Demo\Hello.exe")));
        Assert.False(Directory.Exists(Abs(@"bin\Release")));
        Assert.Empty(h.Messages.Requests);
    }
}
