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
}
