using System.Text.Json.Nodes;
using FreexComp.Debugger;
using FreexComp.Ide.Core.Debugging;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Tests.Build;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Debugging;

/// <summary>Breakpoints in <c>session.json</c> (I-134, I-140; architecture.md §8.2, IDE-ARCH-073, 076, 077).</summary>
[Trait("Phase", "6")]
public sealed class BreakpointPersistenceTests
{
    private const string SessionPath = BuildTestBed.Dir + @"\.freexcomp\session.json";

    private static BuildTestBed Bed() => new(o => o.ImplementedPhase = 6);

    private static JsonArray Stored(BuildTestBed bed) => (JsonArray)JsonNode.Parse(bed.Text(SessionPath))!["breakpoints"]!;

    /// <summary>
    /// Breakpoints are written with the project session (debounced, on close), follow edits of an open document, and come
    /// back when the project opens again — clamped to the last line when the file got shorter.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-076")]
    [Trait("Req", "IDE-ARCH-073")]
    public Task SessionRoundTrip() => TestUiThread.RunAsync(SessionRoundTripOnUiThread);

    private static async Task SessionRoundTripOnUiThread()
    {
        var bed = Bed();
        await bed.OpenHelloAsync();
        var breakpoints = bed.Services.Breakpoints!;
        var unit = await bed.OpenDocumentAsync(BuildTestBed.UnitPath);
        breakpoints.Toggle(BuildTestBed.UnitPath, 3);
        breakpoints.Set(new BreakpointInfo(BuildTestBed.MainPath, 4, Enabled: false, Condition: "X = 1"));

        // IDE-ARCH-071 (c): written 5 s after the change; paths relative to the project with '/'.
        bed.Clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(
            """[{"path":"Unit1.pas","line":3,"enabled":true,"condition":null,"hitCount":null,"logMessage":null},{"path":"Hello.pas","line":4,"enabled":false,"condition":"X = 1","hitCount":null,"logMessage":null}]""",
            Stored(bed).ToJsonString());

        // A line inserted above moves the breakpoint of the open document; the stored line follows.
        BuildTestBed.Type(unit, "// first\n");
        Assert.Equal(4, breakpoints.Find(BuildTestBed.UnitPath, 4)!.Line);
        Assert.Null(breakpoints.Find(BuildTestBed.UnitPath, 3));
        bed.Clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(4, (int)Stored(bed)[0]!["line"]!);

        // Closing the project writes the session and clears the list (breakpoints belong to the project).
        Assert.True(await bed.Services.Documents.SaveAllAsync());
        Assert.True(await bed.Services.Projects.CloseAsync());
        Assert.Empty(breakpoints.All);
        Assert.Equal(2, Stored(bed).Count);

        // Unit1.pas gets shorter on disk: its breakpoint is clamped to the last line when the document opens.
        bed.File(BuildTestBed.UnitPath, "unit Unit1;\nend.");
        Assert.True(await bed.Services.Projects.OpenAsync(BuildTestBed.ProjectPath));
        Assert.Equal(
            [(BuildTestBed.UnitPath, true, null), (BuildTestBed.MainPath, false, "X = 1")],
            breakpoints.All.Select(b => (b.Path, b.Enabled, b.Condition)));
        var reopened = await bed.OpenDocumentAsync(BuildTestBed.UnitPath);
        Assert.Equal([2, 4], breakpoints.All.Select(b => b.Line));
        Assert.Equal(FreexComp.Ide.Core.Editing.LineGlyphKind.Breakpoint, reopened.Decorations.GlyphAt(2)?.Kind);
        var main = await bed.OpenDocumentAsync(BuildTestBed.MainPath);
        Assert.Equal(FreexComp.Ide.Core.Editing.LineGlyphKind.BreakpointConditionalDisabled, main.Decorations.GlyphAt(4)?.Kind);

        // Before Phase 6 the member is kept as read (IDE-ARCH-076).
        Assert.True(await bed.Services.Projects.CloseAsync());
        var before = Stored(bed).ToJsonString();
        var phase5 = new BuildTestBed(o => o.ImplementedPhase = 5);
        phase5.Fs.CreateDirectory(Path.GetDirectoryName(SessionPath)!);
        phase5.File(SessionPath, bed.Text(SessionPath));
        await phase5.OpenHelloAsync();
        Assert.Null(phase5.Services.Breakpoints);
        Assert.True(await phase5.Services.Projects.CloseAsync());
        Assert.Equal(before, Stored(phase5).ToJsonString());
    }

    /// <summary>
    /// IDE-ARCH-077: an entry is <c>path</c> plus the <c>BreakpointSpec</c> members in camelCase without the id — pass count
    /// n ↔ <c>hitCount { greaterOrEqual, n + 1 }</c>; other modes and <c>logMessage</c> are kept; a legacy
    /// <c>passCount</c> is read and never written.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-077")]
    public Task UsesBreakpointSpecNames() => TestUiThread.RunAsync(UsesBreakpointSpecNamesOnUiThread);

    private static async Task UsesBreakpointSpecNamesOnUiThread()
    {
        var bed = Bed();
        bed.Fs.CreateDirectory(Path.GetDirectoryName(SessionPath)!);
        bed.File(SessionPath, """
            { "formatVersion": 1, "documents": [],
              "breakpoints": [
                { "path": "Unit1.pas", "line": 2, "enabled": true, "condition": null, "hitCount": { "mode": "greaterOrEqual", "value": 6 }, "logMessage": null },
                { "path": "Unit1.pas", "line": 3, "enabled": false, "condition": "I > 10", "hitCount": { "mode": "multiple", "value": 4 }, "logMessage": "I = {I}" },
                { "path": "Hello.pas", "line": 1, "passCount": 2 },
                { "path": "Hello.pas", "line": 2, "hitCount": { "mode": "equal", "value": 3 } },
                { "path": "Hello.pas", "line": 3, "hitCount": { "mode": "sometimes", "value": 3 }, "id": 77 },
                { "path": "Hello.pas", "line": 0 }, { "line": 4 }, "garbage" ],
              "custom": 5 }
            """);
        await bed.OpenHelloAsync();
        var all = bed.Services.Breakpoints!.All;

        Assert.Equal(
            [
                new BreakpointInfo(BuildTestBed.UnitPath, 2, true, null, new HitCountSpec(HitCountMode.GreaterOrEqual, 6)),
                new BreakpointInfo(BuildTestBed.UnitPath, 3, false, "I > 10", new HitCountSpec(HitCountMode.Multiple, 4), "I = {I}"),
                new BreakpointInfo(BuildTestBed.MainPath, 1, true, null, new HitCountSpec(HitCountMode.GreaterOrEqual, 3)), // legacy passCount 2
                new BreakpointInfo(BuildTestBed.MainPath, 2, true, null, new HitCountSpec(HitCountMode.Equal, 3)),
                new BreakpointInfo(BuildTestBed.MainPath, 3), // an unknown mode is no hit count
            ],
            all.Select(b => b.ToInfo()));
        // The pass count is the number of hits to skip; only GreaterOrEqual has one.
        Assert.Equal([5, 0, 2, 0, 0], all.Select(b => b.PassCount));
        Assert.All(all, b => Assert.Equal(0, b.Id)); // ids exist only during a debug session
        Assert.Equal(new HitCountSpec(HitCountMode.GreaterOrEqual, 6), BreakpointModel.HitCountOf(5));
        Assert.Null(BreakpointModel.HitCountOf(0));

        // Written back with the spec's names; no id, no passCount; other members of the file stay.
        bed.Services.Session.Save();
        var root = JsonNode.Parse(bed.Text(SessionPath))!;
        Assert.Equal(5, (int)root["custom"]!);
        Assert.Equal(
            """[{"path":"Unit1.pas","line":2,"enabled":true,"condition":null,"hitCount":{"mode":"greaterOrEqual","value":6},"logMessage":null},"""
            + """{"path":"Unit1.pas","line":3,"enabled":false,"condition":"I GT 10","hitCount":{"mode":"multiple","value":4},"logMessage":"I = {I}"},""".Replace("GT", "\\" + "u003E", StringComparison.Ordinal) // the default encoder escapes the greater-than sign
            + """{"path":"Hello.pas","line":1,"enabled":true,"condition":null,"hitCount":{"mode":"greaterOrEqual","value":3},"logMessage":null},"""
            + """{"path":"Hello.pas","line":2,"enabled":true,"condition":null,"hitCount":{"mode":"equal","value":3},"logMessage":null},"""
            + """{"path":"Hello.pas","line":3,"enabled":true,"condition":null,"hitCount":null,"logMessage":null}]""",
            Stored(bed).ToJsonString());

        // The spec handed to the debugger carries the same members plus the session's id.
        var spec = all[1].ToSpec();
        Assert.Equal((0, 3, false, "I > 10", new HitCountSpec(HitCountMode.Multiple, 4), "I = {I}"),
            (spec.Id, spec.Line, spec.Enabled, spec.Condition, spec.HitCount, spec.LogMessage));
    }
}
