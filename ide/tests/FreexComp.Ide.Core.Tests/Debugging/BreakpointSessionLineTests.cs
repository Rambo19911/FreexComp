using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using FreexComp.Debugger;
using FreexComp.Ide.Core.Debugging;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Tests.Build;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Debugging;

/// <summary>
/// I-222: what a debug session does to the lines of breakpoints (the debugger's features DBG-205, DBG-208) and to the
/// chain of breakpoint requests — over a stub session, so every status arrives exactly when the test reports it.
/// </summary>
[Trait("Phase", "6")]
[Trait("Kind", "Unit")]
public sealed class BreakpointSessionLineTests
{
    private const string SessionPath = BuildTestBed.Dir + @"\.freexcomp\session.json";
    private const string Unit = BuildTestBed.UnitPath;
    private const string Main = BuildTestBed.MainPath;

    private static async Task<(BuildTestBed Bed, BreakpointService Breakpoints, TextDocumentViewModel Document)> StartAsync()
    {
        var bed = new BuildTestBed(o => o.ImplementedPhase = 6);
        await bed.OpenHelloAsync();
        bed.File(Unit, "unit Unit1;\ninterface\nprocedure Run;\nimplementation\nprocedure Run;\nbegin\n  WriteLn('a');\n  WriteLn('b');\n  WriteLn('c');\nend;\nend.\n");
        var document = await bed.OpenDocumentAsync(Unit);
        return (bed, Assert.IsType<BreakpointService>(bed.Services.Breakpoints), document);
    }

    private static int[] StoredLines(BuildTestBed bed)
    {
        bed.Services.Session.Save();
        return [.. ((JsonArray)JsonNode.Parse(bed.Text(SessionPath))!["breakpoints"]!).Select(b => (int)b!["line"]!)];
    }

    private static BreakpointStatus Verified(int id, int line, int hits = 0) => new(id, BreakpointState.Verified, line, hits, null);

    /// <summary>
    /// DBG-205: <c>ActualLine</c> moves the gutter marker for the duration of the session only — the stored line is
    /// unchanged, it is what the session file gets, and the marker returns to it when the session ends (in an open
    /// document and in a file that is not open).
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "DBG-205")]
    public Task MovedMarkerIsSessionOnly() => TestUiThread.RunAsync(MovedMarkerIsSessionOnlyOnUiThread);

    private static async Task MovedMarkerIsSessionOnlyOnUiThread()
    {
        var (bed, breakpoints, document) = await StartAsync();
        var (session, stub) = StubSession.Create();
        var status = new List<string?>();
        bed.Services.Status.Changed += (_, _) => status.Add(bed.Services.Status.Message);
        breakpoints.Toggle(Unit, 3); // "procedure Run;" of the interface part: no code
        breakpoints.Toggle(Main, 1); // Hello.pas is not open
        breakpoints.AttachSession(session);

        stub.Report(Verified(1, 6));
        stub.Report(Verified(2, 3));

        // During the session the markers are where the debugger bound the breakpoints.
        var moved = breakpoints.Find(Unit, 6)!;
        Assert.Equal((1, 6, 3), (moved.Id, moved.Line, moved.StoredLine));
        Assert.Null(breakpoints.Find(Unit, 3));
        Assert.Equal((null, LineGlyphKind.Breakpoint), (document.Decorations.GlyphAt(3)?.Kind, document.Decorations.GlyphAt(6)?.Kind));
        Assert.Equal([(6, 3), (3, 1)], breakpoints.All.Select(b => (b.Line, b.StoredLine)));
        Assert.Contains(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Debug_BreakpointMoved, 6), status);

        // The session file keeps the stored lines, the definition too; the session is told the lines it reported.
        Assert.Equal([3, 1], StoredLines(bed));
        Assert.Equal(3, moved.ToInfo().Line);
        breakpoints.Update(moved, enabled: true, "I > 1", null);
        await breakpoints.WhenSyncedAsync();
        Assert.Equal((Unit, "6"), stub.Sent[^1]);

        // A line typed above moves both the marker and the stored line with the text.
        BuildTestBed.Type(document, "// first\n");
        Assert.Equal([(7, 4), (3, 1)], breakpoints.All.Select(b => (b.Line, b.StoredLine)));
        Assert.Equal([4, 1], StoredLines(bed));

        // The session ends: the markers are back on the stored lines.
        breakpoints.DetachSession();
        Assert.Equal([(4, 4), (1, 1)], breakpoints.All.Select(b => (b.Line, b.StoredLine)));
        Assert.Equal((LineGlyphKind.BreakpointConditional, null), (document.Decorations.GlyphAt(4)?.Kind, document.Decorations.GlyphAt(7)?.Kind));
        Assert.NotNull(breakpoints.Find(Unit, 4));
        Assert.Equal([4, 1], StoredLines(bed));
    }

    /// <summary>
    /// DBG-208: after an edit during a session the session keeps the lines of the program that runs — a status that names
    /// such a line moves no marker and shows no "moved" message, and a later request for the file sends those lines, not
    /// the edited ones.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "DBG-208")]
    public Task EditsDuringSessionKeepSessionLines() => TestUiThread.RunAsync(EditsDuringSessionKeepSessionLinesOnUiThread);

    private static async Task EditsDuringSessionKeepSessionLinesOnUiThread()
    {
        var (bed, breakpoints, document) = await StartAsync();
        var (session, stub) = StubSession.Create();
        var status = new List<string?>();
        bed.Services.Status.Changed += (_, _) => status.Add(bed.Services.Status.Message);
        breakpoints.Toggle(Unit, 7);
        breakpoints.Toggle(Unit, 9);
        var launch = breakpoints.AttachSession(session);
        Assert.Equal([7, 9], Assert.Single(launch).Breakpoints.Select(b => b.Line));
        stub.Report(Verified(1, 7));
        stub.Report(Verified(2, 9));

        // A line typed above line 7 during the session: the markers follow the text.
        BuildTestBed.Type(document, "// first\n");
        Assert.Equal([8, 10], breakpoints.All.Select(b => b.Line));

        // The next hit reports the breakpoint at line 7 of the program that runs: the marker stays on the statement.
        stub.Report(Verified(1, 7, hits: 1));
        var first = breakpoints.Find(Unit, 8)!;
        Assert.Equal((1, 8, 1), (first.Id, first.Line, first.Hits));
        Assert.Null(breakpoints.Find(Unit, 7));
        Assert.Equal(LineGlyphKind.Breakpoint, document.Decorations.GlyphAt(8)?.Kind);
        Assert.DoesNotContain(status, m => m is not null && m.StartsWith(Strings.Msg_Debug_BreakpointMoved[..10], StringComparison.Ordinal));

        // Even a line the debugger moves a breakpoint to is not shown in an edited document: its numbers are the program's.
        stub.Report(Verified(2, 11));
        Assert.Equal([8, 10], breakpoints.All.Select(b => b.Line));
        Assert.DoesNotContain(status, m => m is not null && m.StartsWith(Strings.Msg_Debug_BreakpointMoved[..10], StringComparison.Ordinal));

        // A change of the file's set sends the session's lines, not the edited ones.
        breakpoints.Update(first, enabled: true, "I > 1", null);
        await breakpoints.WhenSyncedAsync();
        Assert.Equal((Unit, "7,11"), stub.Sent[^1]);

        // After the session the breakpoints are where the text is.
        breakpoints.DetachSession();
        Assert.Equal([8, 10], breakpoints.All.Select(b => b.Line));
        Assert.Equal([8, 10], StoredLines(bed));
    }

    /// <summary>
    /// The chain of breakpoint requests survives a request that fails unexpectedly: the failure is logged, the requests
    /// after it are sent, and <c>WhenSyncedAsync</c> completes.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-077")]
    public Task SyncChainSurvivesUnexpectedFailure() => TestUiThread.RunAsync(SyncChainSurvivesUnexpectedFailureOnUiThread);

    private static async Task SyncChainSurvivesUnexpectedFailureOnUiThread()
    {
        var (bed, breakpoints, _) = await StartAsync();
        var (session, stub) = StubSession.Create();
        breakpoints.AttachSession(session);

        stub.Failure = new InvalidOperationException("broken pipe");
        breakpoints.Toggle(Unit, 7);
        await breakpoints.WhenSyncedAsync(); // not faulted
        Assert.Contains(bed.Log.Lines, l => l.StartsWith("ERROR breakpoints:", StringComparison.Ordinal) && l.Contains("broken pipe", StringComparison.Ordinal));

        stub.Failure = null;
        breakpoints.Toggle(Unit, 8);
        await breakpoints.WhenSyncedAsync();
        Assert.Equal([(Unit, "7"), (Unit, "7,8")], stub.Sent);
        Assert.True(breakpoints.WhenSyncedAsync().IsCompletedSuccessfully);
        breakpoints.DetachSession();
    }

    /// <summary>
    /// DBG-232 / DBG-065: "Run to Cursor" without a session stops at the cursor line whatever is there — on a line whose
    /// breakpoint is disabled or conditional a temporary breakpoint joins the launch set after it (the later one of a
    /// line wins in the session) and the breakpoint it displaces shows no state of its own meanwhile; an enabled
    /// unconditional breakpoint stops by itself.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "DBG-232")]
    public Task RunToCursorBypassesBreakpointOnTheLine() => TestUiThread.RunAsync(RunToCursorBypassesBreakpointOnTheLineOnUiThread);

    private static async Task RunToCursorBypassesBreakpointOnTheLineOnUiThread()
    {
        var (_, breakpoints, document) = await StartAsync();
        static string Describe(SourceBreakpoints set) =>
            string.Join(" ", set.Breakpoints.Select(b => $"{b.Id}:{b.Line}:{(b.Enabled ? "on" : "off")}:{b.Condition}{(b.HitCount is null ? "" : "#")}"));

        // Disabled.
        breakpoints.Set(new BreakpointInfo(Unit, 8, Enabled: false));
        var (session, _) = StubSession.Create();
        Assert.Equal("1:8:off: 2:8:on:", Describe(Assert.Single(breakpoints.AttachSession(session, (Unit, 8)))));
        breakpoints.DetachSession();

        // Conditional, and one with a pass count: the session reports the displaced breakpoint as a duplicate — not shown.
        breakpoints.Set(new BreakpointInfo(Unit, 8, Condition: "I > 1"));
        breakpoints.Set(new BreakpointInfo(Unit, 9, HitCount: BreakpointModel.HitCountOf(3)));
        var (second, stub) = StubSession.Create();
        Assert.Equal("1:8:on:I > 1 2:9:on:# 3:8:on:", Describe(Assert.Single(breakpoints.AttachSession(second, (Unit, 8)))));
        stub.Report(new BreakpointStatus(1, BreakpointState.Invalid, 8, 0, "Duplicate breakpoint"));
        var conditional = breakpoints.Find(Unit, 8)!;
        Assert.Equal((BreakpointState.Pending, null), (conditional.State, conditional.Message));
        Assert.Equal(LineGlyphKind.BreakpointConditional, document.Decorations.GlyphAt(8)?.Kind);

        // The first stop takes the temporary breakpoint away: the file's set is the user's again, with its states.
        stub.Answer = specs => [.. specs.Select(s => Verified(s.Id, s.Line))];
        breakpoints.ClearTemporary();
        await breakpoints.WhenSyncedAsync();
        Assert.Equal((Unit, "8,9"), stub.Sent[^1]);
        Assert.Equal(BreakpointState.Verified, conditional.State);
        breakpoints.DetachSession();

        // Enabled and unconditional: it stops there itself, no temporary breakpoint.
        breakpoints.RemoveAll();
        breakpoints.Toggle(Unit, 8);
        var (third, _) = StubSession.Create();
        Assert.Equal("1:8:on:", Describe(Assert.Single(breakpoints.AttachSession(third, (Unit, 8)))));
        breakpoints.DetachSession();
    }
}

/// <summary>
/// A debug session that only takes breakpoints: <c>SetBreakpointsAsync</c> records the file and its lines and answers
/// with <see cref="Answer"/> (default: no statuses) or fails with <see cref="Failure"/>; <see cref="Report"/> raises
/// <c>BreakpointChanged</c> on the calling thread. Every other member is not supported.
/// </summary>
public class StubSession : DispatchProxy
{
    private EventHandler<BreakpointChangedEventArgs>? _changed;

    public List<(string Path, string Lines)> Sent { get; } = [];

    public Func<IReadOnlyList<BreakpointSpec>, IReadOnlyList<BreakpointStatus>>? Answer { get; set; }

    public Exception? Failure { get; set; }

    public static (IDebugSession Session, StubSession Stub) Create()
    {
        var session = Create<IDebugSession, StubSession>();
        return (session, (StubSession)(object)session);
    }

    public void Report(BreakpointStatus status) => _changed?.Invoke(this, new BreakpointChangedEventArgs(status));

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        ArgumentNullException.ThrowIfNull(args);
        switch (targetMethod.Name)
        {
            case "add_BreakpointChanged":
                _changed += (EventHandler<BreakpointChangedEventArgs>)args[0]!;
                return null;
            case "remove_BreakpointChanged":
                _changed -= (EventHandler<BreakpointChangedEventArgs>)args[0]!;
                return null;
            case nameof(IDebugSession.SetBreakpointsAsync):
                var specs = (IReadOnlyList<BreakpointSpec>)args[1]!;
                Sent.Add(((string)args[0]!, string.Join(",", specs.Where(s => s.Enabled).Select(s => s.Line))));
                return Failure is { } failure
                    ? Task.FromException<IReadOnlyList<BreakpointStatus>>(failure)
                    : Task.FromResult(Answer?.Invoke(specs) ?? []);
            default:
                throw new NotSupportedException(targetMethod.Name);
        }
    }
}
