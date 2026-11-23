using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using AvaloniaEdit;
using FreexComp.Ide.Core.Popups;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The Go to File popup (I-125, dialogs.md §13).</summary>
[Trait("Phase", "3")]
[Trait("Kind", "Ui")]
public sealed class GoToFilePopupTests
{
    private static readonly string[] Files = ["Domain.pas", "MainForm.pas", "Other.pas", "MainForm.form", "src/Unit_Main.pas", "Mxaxixn.pas"];

    internal static Task<HeadlessHarness> StartWithProjectAsync() => HeadlessHarness.StartAsync(o =>
    {
        o.ImplementedPhase = 3;
        o.InitialFiles[@"App\App.pproj"] = $$"""
            { "formatVersion": 1, "name": "App", "appType": "console", "mainSource": "App.pas",
              "configurations": { "Debug": { "optimization": 0, "debugInfo": true, "defines": [], "outputDir": "bin/Debug" } },
              "files": [ { "path": "App.pas" }, {{string.Join(", ", Files.Select(f => $"{{ \"path\": \"{f}\" }}"))}} ] }
            """;
        o.InitialFiles[@"App\App.pas"] = "program App;\r\nbegin\r\nend.\r\n";
        foreach (var file in Files)
            o.InitialFiles[Path.Combine("App", file)] = "unit X;\r\ninterface\r\nimplementation\r\nend.\r\n";
    });

    private static List<string> Items(HeadlessHarness h) =>
        h.TryFind<ListBox>("GoToFile.List") is { IsVisible: true } list
            ? [.. (list.ItemsSource ?? Array.Empty<object>()).OfType<QuickPickItem>().Select(i => i.Id)]
            : [];

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-130")]
    [Trait("Req", "UI-DLG-131")]
    [Trait("Req", "UI-DLG-132")]
    [Trait("Req", "IDE-ED-102")]
    public async Task RankingAndLineSuffix()
    {
        await using var h = await StartWithProjectAsync();
        var dir = Path.Combine(h.TempDir, "App");
        Assert.True(await h.Services.Projects.OpenAsync(Path.Combine(dir, "App.pproj")));
        Assert.True((await h.Services.Documents.OpenAsync(Path.Combine(dir, "Other.pas"))).Success);
        Assert.True((await h.Services.Documents.OpenAsync(Path.Combine(dir, "Domain.pas"))).Success);

        // Empty input: open documents most recent first (opening the project opened App.pas), then the project files in project order.
        h.Press("Ctrl+P");
        await h.WaitForAsync(() => h.FocusedAutomationId == "GoToFile.Input", "popup open with focus in the input");
        Assert.Equal(560, h.Find<Control>("GoToFile").Bounds.Width);
        Assert.Equal(["Domain.pas", "Other.pas", "App.pas", "MainForm.pas", "MainForm.form", "src/Unit_Main.pas", "Mxaxixn.pas"], Items(h));

        // Ranking: prefix (shorter first) > word start > contiguous substring > other subsequence; the name, not the path.
        h.Type("main");
        await h.WaitForAsync(() => Items(h).Count == 5, "filtered");
        Assert.Equal(["MainForm.pas", "MainForm.form", "src/Unit_Main.pas", "Domain.pas", "Mxaxixn.pas"], Items(h));
        var name = h.Find<TextBlock>("GoToFile.Item.MainForm.pas.Name");
        var bold = name.Inlines!.OfType<Run>().Where(r => r.FontWeight == FontWeight.Bold).Select(r => r.Text);
        Assert.Equal("Main", string.Concat(bold));
        Assert.Equal(dir, h.Find<TextBlock>("GoToFile.Item.MainForm.pas.Directory").Text);

        // No match: one disabled row.
        h.Type("zz");
        await h.WaitForAsync(() => Items(h).Count == 0, "no match");
        Assert.Equal("No file matches mainzz", h.Find<TextBlock>("GoToFile.NoMatch").Text);

        // A trailing :<line> is not part of the filter; Enter opens the selected file at that line.
        var input = h.Find<TextBox>("GoToFile.Input");
        input.Text = "unit_main:3";
        await h.WaitForAsync(() => Items(h).SequenceEqual(["src/Unit_Main.pas"]), "line suffix ignored by the filter");
        h.Press("Enter");
        await h.WaitForAsync(() => h.FocusedAutomationId == "Editor.Unit_Main.pas", "file opened");
        Assert.Null(h.TryFind<Control>("GoToFile.Input"));
        Assert.Equal(3, h.Find<TextEditor>("Editor.Unit_Main.pas").TextArea.Caret.Line);

        // Down moves the selection; Escape closes without opening.
        h.Press("Ctrl+P");
        await h.WaitForAsync(() => h.FocusedAutomationId == "GoToFile.Input", "popup open again");
        Assert.Equal("Unit_Main.pas", h.Services.Documents.MostRecentlyUsed[0].Title);
        h.Press("Down");
        h.Press("Escape");
        await h.WaitForAsync(() => h.TryFind<Control>("GoToFile.Input") is null, "popup closed");
        Assert.Equal("Editor.Unit_Main.pas", h.FocusedAutomationId);
        Assert.Equal(["App.pas", "Other.pas", "Domain.pas", "Unit_Main.pas"], h.Services.Documents.Documents.Select(d => d.Title));
    }
}
