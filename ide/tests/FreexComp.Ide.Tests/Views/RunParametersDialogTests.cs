using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The Run Parameters dialog (I-108, dialogs.md §22).</summary>
[Trait("Phase", "3")]
[Trait("Kind", "Ui")]
public sealed class RunParametersDialogTests
{
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-220")]
    public async Task WritesProjectKeys()
    {
        await using var h = await BuildScenario.StartAsync();
        var projectPath = BuildScenario.ProjectPath(h);
        var projectDir = Path.GetDirectoryName(projectPath)!;
        Assert.True(h.Services.Commands.CanExecute("run.parameters"));

        var opening = h.Services.Commands.ExecuteAsync("run.parameters");
        var dlg = await h.WaitForDialogAsync("RunParameters");
        Assert.Equal(("Run Parameters — Hello", 560.0, false), (dlg.Window.Title, dlg.Window.Width, dlg.Window.CanResize));
        var exe = Path.Combine(projectDir, @"bin\Debug\Hello.exe");
        Assert.Equal(exe, dlg.Find<TextBlock>("CommandLine").Text);
        Assert.Equal(Strings.Msg_RunParameters_WorkingDirHint, dlg.Find<TextBlock>("WorkingDir.Hint").Text);

        // Arguments are verbatim and the command line follows them live.
        dlg.Find<TextBox>("Arguments").Text = "--verbose \"in put.txt\"";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(exe + " --verbose \"in put.txt\"", dlg.Find<TextBlock>("CommandLine").Text);

        // Invalid path syntax blocks OK; a missing folder only warns.
        var working = dlg.Find<TextBox>("WorkingDir");
        working.Text = "a|b";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Strings.Msg_Project_LocationInvalid, dlg.ErrorText("WorkingDir"));
        Assert.False(dlg.Find<Button>("Ok").IsEffectivelyEnabled);
        working.Text = @"data\in";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Strings.Msg_ProjectOptions_SearchPathMissing, dlg.ErrorText("WorkingDir"));
        Assert.True(dlg.Find<Button>("Ok").IsEffectivelyEnabled);

        // Browse inside the project directory stores the folder relative with '/'.
        Directory.CreateDirectory(Path.Combine(projectDir, @"data\run"));
        h.FileDialogs.NextFolder(Path.Combine(projectDir, @"data\run"));
        dlg.Click("WorkingDir.Browse");
        await h.WaitForAsync(() => working.Text == "data/run", "browsed folder stored relative");
        Assert.Null(dlg.ErrorText("WorkingDir"));

        // OK writes both keys; nothing is started.
        dlg.Click("Ok");
        await h.WaitForAsync(() => !dlg.Window.IsVisible, "dialog closed");
        await opening;
        var definition = h.Services.Projects.Definition!;
        Assert.Equal(("--verbose \"in put.txt\"", "data/run"), (definition.Run.Arguments, definition.Run.WorkingDir));
        var text = File.ReadAllText(projectPath);
        Assert.Contains("\"workingDir\": \"data/run\"", text, StringComparison.Ordinal);
        Assert.Contains("--verbose", text, StringComparison.Ordinal);
        Assert.Empty(h.Processes.Started);

        // Cancel discards; reopening shows the stored values.
        opening = h.Services.Commands.ExecuteAsync("run.parameters");
        dlg = await h.WaitForDialogAsync("RunParameters");
        Assert.Equal("data/run", dlg.Find<TextBox>("WorkingDir").Text);
        dlg.Find<TextBox>("Arguments").Text = "changed";
        dlg.Press("Escape");
        await h.WaitForAsync(() => !dlg.Window.IsVisible, "dialog canceled");
        await opening;
        Assert.Equal("--verbose \"in put.txt\"", h.Services.Projects.Definition!.Run.Arguments);

        // No project: disabled.
        Assert.True(await h.Services.Projects.CloseAsync());
        Assert.False(h.Services.Commands.CanExecute("run.parameters"));
    }

    /// <summary>
    /// IDE-TGT-012: Run Parameters shows <c>HostApplication</c> under the same rule as Project Options — enabled for a
    /// library project (where the dialog is available to set it), disabled with its value kept for other types.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "IDE-TGT-012")]
    public async Task HostApplicationField()
    {
        const string Run = """, "run": { "arguments": "-v", "workingDir": "", "hostApplication": "tools/Loader.exe" }""";
        await using var h = await BuildScenario.StartAsync(projectExtra: Run, implementedPhase: null);
        var projectPath = BuildScenario.ProjectPath(h);
        var dir = Path.GetDirectoryName(projectPath)!;

        // A console project: disabled, the value shown and kept.
        var opening = h.Services.Commands.ExecuteAsync("run.parameters");
        var dlg = await h.WaitForDialogAsync("RunParameters");
        Assert.Equal("tools/Loader.exe", dlg.Find<TextBox>("HostApplication").Text);
        Assert.False(dlg.Find<TextBox>("HostApplication").IsEffectivelyEnabled);
        Assert.False(dlg.Find<Button>("HostApplication.Browse").IsEffectivelyEnabled);
        Assert.Equal(Path.Combine(dir, @"bin\Debug\Hello.exe") + " -v", dlg.Find<TextBlock>("CommandLine").Text);
        Assert.Equal("Dialog.RunParameters.Arguments", dlg.FocusedAutomationId); // the initial focus is unchanged
        dlg.Click("Ok");
        await h.WaitForAsync(() => !dlg.Window.IsVisible, "dialog closed");
        await opening;
        Assert.Equal("tools/Loader.exe", h.Services.Projects.Definition!.Run.HostApplication);

        // The same project as a library: the command stays available, the field is enabled.
        Assert.True(await h.Services.Projects.ApplyOptionsAsync(h.Services.Projects.Definition with { AppType = "library" }));
        Assert.True(h.Services.Commands.CanExecute("run.parameters"));
        opening = h.Services.Commands.ExecuteAsync("run.parameters");
        dlg = await h.WaitForDialogAsync("RunParameters");
        var host = dlg.Find<TextBox>("HostApplication");
        Assert.True(host.IsEffectivelyEnabled);
        Assert.Equal(Path.Combine(dir, @"tools\Loader.exe") + " -v", dlg.Find<TextBlock>("CommandLine").Text);

        host.Text = "x<y.exe";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Strings.Msg_Project_LocationInvalid, dlg.ErrorText("HostApplication"));
        Assert.False(dlg.Find<Button>("Ok").IsEffectivelyEnabled);

        Directory.CreateDirectory(Path.Combine(h.TempDir, "Elsewhere"));
        var outside = Path.Combine(h.TempDir, "Elsewhere", "Host.exe");
        File.WriteAllText(outside, "");
        h.FileDialogs.NextOpen(outside);
        dlg.Click("HostApplication.Browse");
        await h.WaitForAsync(() => host.Text == outside, "a host outside the project directory is stored absolute");
        Assert.Equal(["*.exe"], Assert.Single(h.FileDialogs.Requests[^1].Filters).Patterns);
        Assert.Equal("RunParameters.HostApplication", h.FileDialogs.Requests[^1].PromptId);
        Assert.Null(dlg.ErrorText("HostApplication"));
        Assert.Equal(outside + " -v", dlg.Find<TextBlock>("CommandLine").Text);

        // Cleared: nothing to run, the preview shows the arguments only.
        host.Text = "";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("-v", dlg.Find<TextBlock>("CommandLine").Text);
        host.Text = "host/Loader.exe";
        Dispatcher.UIThread.RunJobs();

        dlg.Click("Ok");
        await h.WaitForAsync(() => !dlg.Window.IsVisible, "dialog closed");
        await opening;
        Assert.Equal("host/Loader.exe", h.Services.Projects.Definition!.Run.HostApplication);
        Assert.Contains("\"hostApplication\": \"host/Loader.exe\"", File.ReadAllText(projectPath), StringComparison.Ordinal);
        Assert.Empty(h.Processes.Started);
    }
}
