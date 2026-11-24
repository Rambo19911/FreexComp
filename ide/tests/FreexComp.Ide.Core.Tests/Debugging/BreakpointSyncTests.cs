using System.Globalization;
using System.Text.Json;
using FreexComp.Debugger;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Debugging;

/// <summary>
/// <c>IBreakpointService</c> ↔ debug session (I-140; the debug-session interface §6.1, IDE-ARCH-077), over real sessions
/// and the scripted adapter.
/// </summary>
[Trait("Phase", "6")]
[Trait("Category", "Integration")]
public sealed class BreakpointSyncTests
{
    /// <summary>What a <c>setBreakpoints</c> request asked for: the file and the lines.</summary>
    private static (string Path, string Lines) Sent(string argumentsJson)
    {
        var root = JsonDocument.Parse(argumentsJson).RootElement;
        return (root.GetProperty("source").GetProperty("path").GetString()!,
            string.Join(",", root.GetProperty("breakpoints").EnumerateArray().Select(b => b.GetProperty("line").GetInt32())));
    }

    private static string Verified(params (int Id, int Line)[] breakpoints) =>
        $$"""{ "on": "setBreakpoints", "respond": { "breakpoints": [ {{string.Join(", ", breakpoints.Select(b => $$"""{ "id": {{b.Id}}, "verified": true, "line": {{b.Line}} }"""))}} ] } }""";

    /// <summary>
    /// The launch carries every file's set with ids 1, 2, … in list order; while the session runs, adding, removing and
    /// disabling sends the complete set of that file; the ids end with the session.
    /// </summary>
    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "IDE-ARCH-077")]
    [Trait("Req", "DBG-200")]
    public Task SyncedToSession() => TestUiThread.RunAsync(SyncedToSessionOnUiThread);

    private static async Task SyncedToSessionOnUiThread()
    {
        using var bed = new DebugTestBed();
        await bed.OpenHelloAsync();
        var breakpoints = bed.Breakpoints;
        breakpoints.Toggle(bed.UnitPath, 7);
        breakpoints.Set(new BreakpointInfo(bed.UnitPath, 8, Enabled: false));
        breakpoints.Set(new BreakpointInfo(bed.MainPath, 4, Condition: "Count > 2", HitCount: BreakpointModel.HitCountOf(5)));
        Assert.All(breakpoints.All, b => Assert.Equal((0, null), (b.Id, b.State))); // no session: no ids, no state
        bed.Sessions.UseScenario($$"""
            { "steps": [ {{Verified((12, 4))}}, {{Verified((11, 7))}},
                         {{Verified((11, 7), (13, 9))}}, {{Verified((13, 9))}}, {{Verified((14, 8), (13, 9))}}, {{Verified((14, 8))}},
                         { "on": "setBreakpoints", "respond": { "breakpoints": [] } }, { "on": "setBreakpoints", "respond": { "breakpoints": [] } } ] }
            """);

        Assert.True(await bed.Debug.StartAsync());
        // The statuses of the launch come as events of the session's own thread: they are waited for, not assumed applied.
        await bed.WaitForBreakpointsAsync(() => breakpoints.All.All(b => b.State != BreakpointState.Pending), "the launch statuses");

        // Launch: one request per file (the session sends them by path); a disabled breakpoint is known to the session but
        // not sent to the adapter.
        Assert.Equal([(bed.MainPath, "4"), (bed.UnitPath, "7")], bed.Sessions.SentArguments("setBreakpoints").Select(Sent));
        Assert.Equal([1, 2, 3], breakpoints.All.Select(b => b.Id));
        Assert.Equal([BreakpointState.Verified, BreakpointState.Disabled, BreakpointState.Verified], breakpoints.All.Select(b => b.State));

        // Added while running: the next id, the file's complete set.
        breakpoints.Toggle(bed.UnitPath, 9);
        await DebugTestBed.WaitAsync(breakpoints.WhenSyncedAsync(), "breakpoints sent");
        Assert.Equal((bed.UnitPath, "7,9"), Sent(bed.Sessions.SentArguments("setBreakpoints")[2]));
        var added = breakpoints.Find(bed.UnitPath, 9)!;
        Assert.Equal((4, BreakpointState.Verified), (added.Id, added.State));

        // Removed while running.
        breakpoints.Toggle(bed.UnitPath, 7);
        await DebugTestBed.WaitAsync(breakpoints.WhenSyncedAsync(), "breakpoints sent");
        Assert.Equal((bed.UnitPath, "9"), Sent(bed.Sessions.SentArguments("setBreakpoints")[3]));
        Assert.Equal([2, 3, 4], breakpoints.All.Select(b => b.Id));

        // Enabled, then another disabled, while running.
        var line8 = breakpoints.Find(bed.UnitPath, 8)!;
        breakpoints.Update(line8, enabled: true, null, null);
        await DebugTestBed.WaitAsync(breakpoints.WhenSyncedAsync(), "breakpoints sent");
        Assert.Equal((bed.UnitPath, "8,9"), Sent(bed.Sessions.SentArguments("setBreakpoints")[4]));
        Assert.Equal(BreakpointState.Verified, line8.State);
        breakpoints.Update(added, enabled: false, null, null);
        await DebugTestBed.WaitAsync(breakpoints.WhenSyncedAsync(), "breakpoints sent");
        Assert.Equal((bed.UnitPath, "8"), Sent(bed.Sessions.SentArguments("setBreakpoints")[5]));
        Assert.Equal(BreakpointState.Disabled, added.State);

        // Delete all: every file's (now empty) set.
        breakpoints.RemoveAll();
        await DebugTestBed.WaitAsync(breakpoints.WhenSyncedAsync(), "breakpoints sent");
        Assert.Equal([(bed.UnitPath, ""), (bed.MainPath, "")], bed.Sessions.SentArguments("setBreakpoints").Skip(6).Select(Sent));
        Assert.Empty(breakpoints.All);

        // A breakpoint added during the session loses its id and state when the session ends; later ones are not sent.
        await bed.Debug.StopAsync();
        Assert.Contains("SCENARIO-COMPLETE", bed.Sessions.AdapterStderr());
        breakpoints.Toggle(bed.UnitPath, 7);
        Assert.True(breakpoints.WhenSyncedAsync().IsCompleted);
        Assert.Equal((0, null, 0), (breakpoints.All[0].Id, breakpoints.All[0].State, breakpoints.All[0].Hits));
        Assert.Equal(8, bed.Sessions.SentArguments("setBreakpoints").Count);
    }

    /// <summary>
    /// <c>BreakpointStatus</c> → glyph state, <c>ActualLine</c> (the breakpoint moves, <c>Msg.Debug.BreakpointMoved</c>)
    /// and <c>Hits</c>; an invalid breakpoint shows the unverified glyph with the debugger's message.
    /// </summary>
    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "UI-ED-021")]
    public Task StatusUpdatesGlyphLineAndHits() => TestUiThread.RunAsync(StatusUpdatesGlyphLineAndHitsOnUiThread);

    private static async Task StatusUpdatesGlyphLineAndHitsOnUiThread()
    {
        using var bed = new DebugTestBed();
        await bed.OpenHelloAsync();
        var document = await bed.OpenDocumentAsync(bed.UnitPath);
        var breakpoints = bed.Breakpoints;
        var status = new List<string?>();
        bed.Services.Status.Changed += (_, _) => status.Add(bed.Services.Status.Message);
        LineGlyphKind? Glyph(int line) => document.Decorations.GlyphAt(line)?.Kind;

        // Without a session: enabled disc, conditional disc, rings.
        breakpoints.Toggle(bed.UnitPath, 7);
        breakpoints.Toggle(bed.UnitPath, 3); // "procedure Run;" in the interface part: no code
        breakpoints.Set(new BreakpointInfo(bed.UnitPath, 8, Condition: "I > 1"));
        breakpoints.Set(new BreakpointInfo(bed.UnitPath, 9, Enabled: false, HitCount: BreakpointModel.HitCountOf(2)));
        Assert.Equal(
            [LineGlyphKind.Breakpoint, LineGlyphKind.Breakpoint, LineGlyphKind.BreakpointConditional, LineGlyphKind.BreakpointConditionalDisabled],
            new[] { 7, 3, 8, 9 }.Select(Glyph));
        Assert.Equal("Breakpoint at line 9, pass count: 2, disabled", document.Decorations.ToolTipAt(9));
        Assert.Equal("editor.breakpoint.line.bg", document.Decorations.LineBackgroundAt(7));
        Assert.Null(document.Decorations.LineBackgroundAt(9));
        var changes = 0;
        breakpoints.Changed += (_, _) => changes++;

        // The adapter binds line 3 to line 6 (the next line with code); the program stops at the breakpoint of line 7.
        bed.Sessions.UseScenario($$"""
            { "steps": [ { "on": "setBreakpoints", "respond": { "breakpoints": [ { "id": 21, "verified": true, "line": 7 }, { "id": 22, "verified": true, "line": 6 },
                                                                                { "id": 23, "verified": true, "line": 8 } ] } },
                         {{DebugTestBed.StopAtStart(bed.UnitPath, 7, """, "hitBreakpointIds": [21]""")}},
                         {{Verified((21, 7), (22, 6))}} ] }
            """);
        var stopped = new TaskCompletionSource<StopInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        bed.Debug.Stopped += (_, stop) => stopped.TrySetResult(stop);
        Assert.True(await bed.Debug.StartAsync());
        var stop = await CurrentStop(stopped.Task);

        // Line 7 verified and hit once; the stop names it by its IDE id.
        var line7 = breakpoints.Find(bed.UnitPath, 7)!;
        Assert.Equal((1, BreakpointState.Verified, 1), (line7.Id, line7.State, line7.Hits));
        Assert.Equal([1], stop.HitBreakpointIds);
        Assert.Equal(StopReason.Breakpoint, stop.Reason);

        // Line 3 moved to line 6: the glyph follows, the status bar says so.
        Assert.Null(breakpoints.Find(bed.UnitPath, 3));
        var moved = breakpoints.Find(bed.UnitPath, 6)!;
        Assert.Equal((2, BreakpointState.Verified, 6), (moved.Id, moved.State, moved.Line));
        Assert.Null(Glyph(3));
        Assert.Equal(LineGlyphKind.Breakpoint, Glyph(6));
        Assert.Contains(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Debug_BreakpointMoved, 6), status);

        // A condition with a syntax error, set during the session, is invalid: unverified ring, the debugger's message as
        // tooltip; it is not sent to the adapter.
        var invalid = breakpoints.Find(bed.UnitPath, 8)!;
        Assert.Equal((BreakpointState.Verified, LineGlyphKind.BreakpointConditional), (invalid.State, Glyph(8)));
        breakpoints.Update(invalid, enabled: true, "I >", null);
        await DebugTestBed.WaitAsync(breakpoints.WhenSyncedAsync(), "breakpoints sent");
        Assert.Equal((bed.UnitPath, "7,6"), Sent(bed.Sessions.SentArguments("setBreakpoints")[^1]));
        Assert.Equal(BreakpointState.Invalid, invalid.State);
        Assert.False(string.IsNullOrEmpty(invalid.Message));
        Assert.Equal(LineGlyphKind.BreakpointUnverified, Glyph(8));
        Assert.Equal(invalid.Message, document.Decorations.ToolTipAt(8));
        Assert.Null(document.Decorations.LineBackgroundAt(8));
        Assert.Equal(BreakpointState.Disabled, breakpoints.Find(bed.UnitPath, 9)!.State);
        Assert.Equal(LineGlyphKind.BreakpointConditionalDisabled, Glyph(9));
        Assert.True(changes > 0); // listeners (the session file, the Breakpoints window) are told

        // The session ends: states and hits are gone, the definitions stay; the moved marker is back on its stored line
        // (DBG-205: it moves for the duration of the session only).
        await bed.Debug.StopAsync();
        Assert.All(breakpoints.All, b => Assert.Equal((0, null, 0, null), (b.Id, b.State, b.Hits, b.Message)));
        Assert.Equal([7, 3, 8, 9], breakpoints.All.Select(b => b.Line));
        Assert.Equal((LineGlyphKind.Breakpoint, null), (Glyph(3), Glyph(6)));
        Assert.Equal(LineGlyphKind.BreakpointConditional, Glyph(8));
    }

    private static async Task<StopInfo> CurrentStop(Task<StopInfo> stopped)
    {
        await DebugTestBed.WaitAsync(stopped, "the stop");
        return await stopped;
    }
}
