using System.Globalization;
using System.Text;
using FreexComp.Ide.Core.Debugging;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Debugging;

/// <summary>The Event Log's rows in the debug service (I-144; tool-windows.md §15) — over a session and the scripted adapter.</summary>
[Trait("Phase", "6")]
[Trait("Category", "Integration")]
public sealed class DebugLogTests
{
    /// <summary>UI-TW-1300 / UI-TW-1303: at most 10 000 rows, the oldest are dropped; the keys keep counting from the last clear.</summary>
    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "UI-TW-1300")]
    [Trait("Req", "UI-TW-1303")]
    public Task KeepsTheNewestTenThousandRows() => TestUiThread.RunAsync(KeepsTheNewestTenThousandRowsOnUiThread);

    private static async Task KeepsTheNewestTenThousandRowsOnUiThread()
    {
        using var bed = new DebugTestBed();
        await bed.OpenHelloAsync();
        const int lines = DebugService.MaxLogRows + 50;
        var text = new StringBuilder();
        for (var i = 1; i <= lines; i++)
            text.Append(CultureInfo.InvariantCulture, $"line {i}\n");
        // The adapter's own console output is the debugger's (kind Debugger); then the program exits.
        bed.Sessions.UseScenario($$"""
            { "steps": [ { "on": "configurationDone", "respond": {}, "thenEvents": [
                { "event": "output", "body": { "category": "console", "output": {{DebugTestBed.Json(text.ToString())}} } }, {{DebugTestBed.Exit()}} ] } ] }
            """);
        var log = bed.Services.DebugShell!.EventLog;

        Assert.True(await bed.Debug.StartAsync());
        await bed.WaitForStateAsync(IdeState.Idle);

        var rows = bed.Debug.Log;
        Assert.Equal(DebugService.MaxLogRows, rows.Count);
        Assert.Equal(DebugLogKind.Process, rows[^1].Kind); // the newest row: the program's end
        Assert.Equal($"line {lines}", rows[^2].Message);
        Assert.All(rows.Take(rows.Count - 1), r => Assert.Equal(DebugLogKind.Debugger, r.Kind));
        // The oldest rows are gone; an index is never used twice.
        Assert.Equal(Enumerable.Range(rows[0].Index, rows.Count), rows.Select(r => r.Index));
        Assert.True(rows[0].Index > 50, $"first index {rows[0].Index}");
        Assert.DoesNotContain(rows, r => r.Message == "line 1");

        // The window shows exactly the rows kept, keyed by their index.
        Assert.Equal(rows.Count, log.Rows.Count);
        Assert.Equal(rows.Select(r => r.Index.ToString(CultureInfo.InvariantCulture)), log.Rows.Select(r => r.Key));
        Assert.Same(rows[0], log.Rows[0].Tag);
        Assert.Equal(DebugCellStyle.Warning, log.Rows[0].Cells[1].Style);

        // A new session starts the log (and its indexes) again.
        bed.Sessions.UseScenario("{}");
        Assert.True(await bed.Debug.StartAsync());
        await bed.WaitForLogAsync(() => bed.Debug.Log.Any(e => e.Kind == DebugLogKind.Process));
        Assert.Equal(0, bed.Debug.Log[0].Index);
        Assert.True(bed.Debug.Log.Count < 10);
        await bed.Debug.StopAsync();
    }
}
