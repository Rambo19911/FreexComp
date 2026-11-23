using System.Text;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.ViewModels;

/// <summary>The Structure view model on the code-model worker (I-124, I-210).</summary>
[Trait("Phase", "3")]
[Trait("Kind", "Unit")]
public sealed class StructureViewModelTests
{
    private const string Path = @"C:\p\A.pas";

    /// <summary>
    /// I-210: the caret of the view state may be stale against the buffer (an edit the editor has not answered yet, a
    /// parse result that arrives later): caret tracking clamps it to the buffer like every other caret consumer, instead
    /// of throwing from the timer or the parse result.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-TW-205")]
    public Task StaleCaretAfterEditIsClamped() => TestUiThread.RunAsync(StaleCaretAfterEditIsClampedOnUiThread);

    private static async Task StaleCaretAfterEditIsClampedOnUiThread()
    {
        var fs = new InMemoryFileSystem();
        var clock = new ManualClock();
        var services = TestComposition.Build(o =>
        {
            o.FileSystem = fs;
            o.Clock = clock;
            o.ImplementedPhase = 3;
        });
        fs.CreateDirectory(@"C:\p");
        fs.WriteAllBytes(Path, Encoding.UTF8.GetBytes("unit A;\r\ninterface\r\nprocedure P;\r\nimplementation\r\nprocedure P;\r\nbegin\r\nend;\r\nend.\r\n"));
        var model = (StructureViewModel)services.ToolWindows.Get(StructureViewModel.ToolName)!;

        var refreshed = Refreshed(model);
        var document = Assert.IsType<TextDocumentViewModel>((await services.Documents.OpenAsync(Path)).Value);
        await refreshed; // parsed on document switch
        Assert.NotEmpty(model.Rows);

        // The caret rests on line 6; the text shrinks to one line before the view state follows.
        const string oneLine = "unit A; interface procedure P; implementation procedure P; begin end; end.";
        var column = oneLine.IndexOf("begin", StringComparison.Ordinal) + 1;
        document.ViewState.Caret = new TextPosition(6, column);
        document.Buffer.ReplaceAll(oneLine, resetUndo: false);

        // The track timer fires on the old tree with the stale caret: clamped to the last line, nothing thrown.
        clock.Advance(StructureViewModel.TrackDelay);

        // The parse result of the new text tracks with the same stale caret: the routine at the clamped position.
        refreshed = Refreshed(model);
        clock.Advance(StructureViewModel.RefreshDelay);
        await refreshed;
        Assert.Equal("Routines/P#1", model.SelectedRow?.Node.Key); // the implementation, not the interface heading
        Assert.True(model.SelectedRow!.IsTracked);
    }

    /// <summary>
    /// UI-TW-208: the filter matches the row text (a row stays when a descendant matches). IDE-ED-133: the expansion state
    /// is kept per open document by node key; another document has its own.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-TW-208")]
    [Trait("Req", "IDE-ED-133")]
    public Task FilterAndExpansionPerDocument() => TestUiThread.RunAsync(FilterAndExpansionPerDocumentOnUiThread);

    private static async Task FilterAndExpansionPerDocumentOnUiThread()
    {
        var fs = new InMemoryFileSystem();
        var clock = new ManualClock();
        var services = TestComposition.Build(o =>
        {
            o.FileSystem = fs;
            o.Clock = clock;
        });
        fs.CreateDirectory(@"C:\p");
        const string unit = "unit {0};\r\ninterface\r\nimplementation\r\nprocedure Alpha;\r\nbegin\r\nend;\r\nprocedure Beta;\r\nbegin\r\nend;\r\nend.\r\n";
        fs.WriteAllBytes(@"C:\p\A.pas", Encoding.UTF8.GetBytes(string.Format(System.Globalization.CultureInfo.InvariantCulture, unit, "A")));
        fs.WriteAllBytes(@"C:\p\B.pas", Encoding.UTF8.GetBytes(string.Format(System.Globalization.CultureInfo.InvariantCulture, unit, "B")));
        var model = (StructureViewModel)services.ToolWindows.Get(StructureViewModel.ToolName)!;
        List<string> Keys() => [.. model.Rows.Select(r => r.Node.Key)];

        var refreshed = Refreshed(model);
        var a = Assert.IsType<TextDocumentViewModel>((await services.Documents.OpenAsync(@"C:\p\A.pas")).Value);
        await refreshed;
        Assert.Equal(["Routines", "Routines/Alpha", "Routines/Beta"], Keys());

        // The filter matches the row text, case-insensitively; the group stays for its matching child.
        model.FilterText = "alp";
        clock.Advance(ToolWindowViewModel.FilterDelay);
        Assert.Equal(["Routines", "Routines/Alpha"], Keys());
        model.FilterText = "";
        clock.Advance(ToolWindowViewModel.FilterDelay);
        Assert.Equal(["Routines", "Routines/Alpha", "Routines/Beta"], Keys());

        // A collapses its group; B (opened next) has its own state; back on A the group is still collapsed.
        model.SetExpanded(model.Rows[0], false);
        Assert.Equal(["Routines"], Keys());
        refreshed = Refreshed(model);
        var b = Assert.IsType<TextDocumentViewModel>((await services.Documents.OpenAsync(@"C:\p\B.pas")).Value);
        await refreshed;
        Assert.Equal(["Routines", "Routines/Alpha", "Routines/Beta"], Keys());
        refreshed = Refreshed(model);
        services.Documents.Activate(a);
        await refreshed;
        Assert.Equal(["Routines"], Keys());

        // Closed and opened again: a new document, the default state.
        Assert.True(services.Documents.Close(a));
        refreshed = Refreshed(model);
        await services.Documents.OpenAsync(@"C:\p\A.pas");
        await refreshed;
        Assert.Equal(["Routines", "Routines/Alpha", "Routines/Beta"], Keys());
        Assert.NotNull(b);
    }

    private static Task Refreshed(StructureViewModel model)
    {
        var done = new TaskCompletionSource();
        void OnRefreshed(object? sender, EventArgs e)
        {
            model.Refreshed -= OnRefreshed;
            done.TrySetResult();
        }

        model.Refreshed += OnRefreshed;
        return done.Task;
    }
}
