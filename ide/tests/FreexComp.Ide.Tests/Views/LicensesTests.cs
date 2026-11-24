using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The Third-Party Licenses dialog as the user sees it (dialogs.md §16; card F02-002, I-231).</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class LicensesTests
{
    /// <summary>
    /// F02-002, third acceptance check: <c>help.licenses</c> shows a row for the UI toolkit, the editor control, LLVM and
    /// SQLite; selecting each row puts its heading (<c>name version — license</c>) and its own, non-empty license text
    /// into the text pane.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-160")]
    [Trait("Req", "UI-DLG-161")]
    public async Task ListsToolkitEditorLlvmAndSqlite()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var running = h.Services.Commands.ExecuteAsync("help.licenses");
        var dlg = await h.WaitForDialogAsync("Licenses");

        const string prefix = "Dialog.Licenses.Component.";
        var rows = dlg.FindAll(prefix).Select(c => AutomationIds.Get(c)![prefix.Length..]).ToList();
        Assert.Contains("Avalonia", rows);
        Assert.Contains("AvaloniaEdit", rows);
        Assert.Contains("SQLite", rows);
        var llvm = Assert.Single(rows, r => r.StartsWith("LLVM", StringComparison.Ordinal));

        var notices = ThirdPartyNotices.LoadEmbedded();
        var shown = new List<string>();
        foreach (var (name, marker) in new[]
                 {
                     ("Avalonia", "MIT License"), ("AvaloniaEdit", "MIT License"), (llvm, "Apache License"), ("SQLite", "public domain"),
                 })
        {
            dlg.Find<Control>("Component." + name).BringIntoView();
            Dispatcher.UIThread.RunJobs();
            dlg.Click("Component." + name);

            var notice = notices.Single(n => n.Name == name);
            var text = dlg.Find<SelectableTextBlock>("Text").Text ?? "";
            var heading = $"{notice.Name} {notice.Version} — {notice.License}{Environment.NewLine}{Environment.NewLine}";
            Assert.StartsWith(heading, text, StringComparison.Ordinal);
            var body = text[heading.Length..];
            Assert.False(string.IsNullOrWhiteSpace(body), name + " has an empty text pane");
            Assert.Equal(notice.Text, body);
            Assert.Contains(marker, body, StringComparison.Ordinal);
            shown.Add(text);
        }

        Assert.Equal(4, shown.Distinct(StringComparer.Ordinal).Count()); // each selection changed the pane

        dlg.Click("Close");
        await running;
    }
}
