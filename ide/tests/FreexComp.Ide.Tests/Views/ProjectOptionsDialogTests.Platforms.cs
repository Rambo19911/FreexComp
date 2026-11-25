using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

public sealed partial class ProjectOptionsDialogTests
{
    /// <summary>
    /// IDE-PLAT-016: the read-only <c>Platforms</c> row of the Application page lists the project's platforms by label
    /// (UI-TB-022), comma-separated, the active one followed by <c> (active)</c>; the dialog changes neither the list
    /// nor the active platform.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "IDE-PLAT-016")]
    public async Task PlatformsRowListsPlatformsAndMarksTheActive()
    {
        // A project with one platform.
        await using (var one = await BuildScenario.StartAsync())
        {
            var (first, opened) = await OpenAsync(one);
            Assert.Equal("Win64 (active)", first.Find<TextBlock>("Platforms").Text);
            first.Click("Cancel");
            await opened;
        }

        // Two listed platforms; the active one is not installed.
        await using var h = await BuildScenario.StartAsync(projectExtra: """
            , "platforms": ["win64", "win32"], "activePlatform": "win32"
            """);
        var (dlg, opening) = await OpenAsync(h);
        var row = dlg.Find<TextBlock>("Platforms");
        Assert.Equal("Win64, Win32 (not installed) (active)", row.Text);
        Assert.Equal(row.Text, ToolTip.GetTip(row));
        Assert.True(row.IsEffectivelyVisible);

        // OK after another change: the platform keys of the file are as they were.
        SetText(dlg, "Name", "Hello2");
        dlg.Click("Ok");
        await opening;
        var saved = ReadProject(h);
        Assert.Equal("Hello2", saved.GetProperty("name").GetString());
        Assert.Equal(["win64", "win32"], saved.GetProperty("platforms").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("win32", saved.GetProperty("activePlatform").GetString());

        // The row follows the active platform.
        await h.ExecuteAsync("project.setPlatform", "win64");
        Dispatcher.UIThread.RunJobs();
        (dlg, opening) = await OpenAsync(h);
        Assert.Equal("Win64 (active), Win32 (not installed)", dlg.Find<TextBlock>("Platforms").Text);
        dlg.Click("Cancel");
        await opening;
    }
}
