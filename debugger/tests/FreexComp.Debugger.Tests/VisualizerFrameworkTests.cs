using System.Collections.Concurrent;
using System.Text.Json;
using FreexComp.Debugger.Tests.TestSupport;
using FreexComp.Debugger.Values;
using FreexComp.Debugger.Values.TypeInfo;
using FreexComp.Debugger.Values.Visualizers;

namespace FreexComp.Debugger.Tests;

/// <summary>
/// The value visualiser framework (visualizers.md §3, DBG-500…514; debug-session DBG-427): registry and name patterns,
/// selection, one entry point for every value, laziness, the <c>[Raw]</c> child, nested summaries, raw mode, read
/// budget, time limit on the session clock, error isolation and once-logging.
/// </summary>
public sealed class VisualizerFrameworkTests
{
    private static readonly TimeSpan Safety = SessionHarness.Safety;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Plain(string variable, string className = "TList<Integer>") => VisualizerBed.PlainList(variable, className);

    private static string Failed(string id, string reason, string type = "TList<Integer>") =>
        $"Visualizer '{id}' failed for {type}: {reason}. Raw values are shown.";

    private static string[] Lines(IEnumerable<VariableInfo> infos) => [.. infos.Select(c => c.Name + " = " + c.Value)];

    // ---- the session model (card acceptance 1 and 2) ----

    /// <summary>
    /// A session over the FakeAdapter stopped in <c>Test</c> of the bench program, with the given visualisers; with
    /// <paramref name="browse"/> the adapter also answers one stack trace, the scopes and the Locals list.
    /// </summary>
    private static async Task<SessionHarness> StoppedInTestAsync(VisualizerRegistry registry, string pdiPath, bool browse)
    {
        var debuggee = VisualizerBed.NewDebuggee();
        var variables = string.Join(", ", debuggee.Variables.Select(v => $"\"{v.Key}\": \"0x{v.Value:X}\""));
        var listed = string.Join(", ", VisualizerBed.VariableNames.Select(name =>
            $$"""{ "name": "{{name}}", "value": "?", "type": "?", "variablesReference": 0 }"""));
        var frame = $$"""
            { "on": "stackTrace", "respond": { "stackFrames": [ { "id": 524288, "name": "Test", "line": {{VisualizerBed.StopLine}}, "column": 5,
                "source": { "path": "{{VisualizerBed.UnitFile.Replace(@"\", @"\\", StringComparison.Ordinal)}}" } } ] } }
            """;
        var browsing = $$"""
            , {{frame}},
                { "on": "scopes", "respond": { "scopes": [
                    { "name": "Locals", "presentationHint": "locals", "variablesReference": 1, "expensive": false },
                    { "name": "Globals", "variablesReference": 2, "expensive": false } ] } },
                { "on": "variables", "expectArguments": { "variablesReference": 1 }, "respond": { "variables": [ {{listed}} ] } }
            """;
        var scenario = $$"""
            { "variables": { {{variables}} },
              "memory": {{debuggee.Memory.ScenarioJson()}},
              "steps": [
                { "on": "configurationDone", "respond": {}, "thenEvents": [ { "event": "stopped", "body": { "reason": "step", "threadId": 1 } } ] },
                { "on": "threads", "respond": { "threads": [ { "id": 1, "name": "main" } ] } },
                {{frame}}{{(browse ? browsing : "")}} ] }
            """;
        var harness = SessionHarness.Create(scenario, options => options with { Visualizers = registry });
        await harness.LaunchAsync(new LaunchRequest
        {
            Program = SessionHarness.Program, TypeInfoPath = pdiPath, Exceptions = SessionHarness.NoLanguageExceptions,
        });
        await harness.WaitForEventAsync("stopped");
        return harness;
    }

    /// <summary>Ends the session; every event before "terminated" has been delivered then.</summary>
    private static async Task<string[]> EndAndGetDebuggerOutputAsync(SessionHarness harness)
    {
        await harness.Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
        await harness.WaitForEventAsync("terminated");
        harness.AssertScenarioComplete();
        const string prefix = "output Debugger ";
        return [.. harness.Events.Snapshot().Where(e => e.StartsWith(prefix, StringComparison.Ordinal))
            .Select(e => JsonSerializer.Deserialize<string>(e[prefix.Length..])!)];
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Req", "DBG-500")]
    [Trait("Req", "DBG-501")]
    [Trait("Req", "DBG-504")]
    [Trait("Req", "DBG-505")]
    [Trait("Req", "DBG-506")]
    [Trait("Req", "DBG-427")]
    public async Task RegisteredVisualizer_SummaryAndChildren_ReturnedBySessionModel()
    {
        var visualizer = new ListVisualizer();
        var pdiPath = PdiFixture.WriteTemp("Vis.pdi", VisualizerBed.PdiJson);
        try
        {
            await using var harness = await StoppedInTestAsync(new VisualizerRegistry([VisualizerBed.Pattern(visualizer, "TList<*>")]), pdiPath, browse: true);
            var session = harness.Session;
            var frames = (await session.GetStackTraceAsync(1, ct: Ct).WaitAsync(Safety, Ct)).Frames;
            var scopes = await session.GetScopesAsync(frames[0].Id, Ct).WaitAsync(Safety, Ct);
            var locals = await session.GetChildrenAsync(scopes[1].Handle, ct: Ct).WaitAsync(Safety, Ct);

            // Locals scope: the visualiser's summary; name, type, kind, expression, address and modifiability as plain.
            var l = locals.Single(v => v.Name == "L");
            Assert.Equal("Count = 3", l.Value);
            Assert.Equal("TList<Integer>", l.TypeName);
            Assert.Equal(ValueKind.ClassInstance, l.Kind);
            Assert.Equal("L", l.EvaluateName);
            Assert.Equal(VisualizerBed.AddressOf("L"), l.Address);
            Assert.False(l.CanModify);
            Assert.Equal(4, l.ChildCount);               // three elements and [Raw]
            Assert.Equal("Count = 1", locals.Single(v => v.Name == "L2").Value);
            Assert.Equal("Count = 2", locals.Single(v => v.Name == "P").Value);
            // Not a TList<...>: plain.
            var n = locals.Single(v => v.Name == "N");
            Assert.Equal(Plain("N", "TList"), n.Value);
            Assert.Equal(2, n.ChildCount);
            // A parent's one-line summary never calls a child's ChildrenAsync; nothing is decoded for children yet.
            Assert.Equal("(Tag: 9; List: " + Plain("L") + ")", locals.Single(v => v.Name == "Hd").Value);
            Assert.DoesNotContain(visualizer.Calls, c => c.StartsWith("ChildAt", StringComparison.Ordinal) || c.EndsWith("Hd.List", StringComparison.Ordinal));

            // Children: only the indices asked for are made.
            var second = Assert.Single(await session.GetChildrenAsync(l.Children!.Value, 1, 1, Ct).WaitAsync(Safety, Ct));
            Assert.Equal("[1] = 20", second.Name + " = " + second.Value);
            Assert.Equal(["ChildAt L 1"], visualizer.Calls.Where(c => c.StartsWith("ChildAt", StringComparison.Ordinal)));
            var children = await session.GetChildrenAsync(l.Children!.Value, ct: Ct).WaitAsync(Safety, Ct);
            Assert.Equal(["[0] = 10", "[1] = 20", "[2] = 30", "[Raw] = " + Plain("L")], Lines(children));
            Assert.Equal(["L.FItems[0]", "L.FItems[1]", "L.FItems[2]", "L,r"], children.Select(c => c.EvaluateName));
            Assert.Equal(1, visualizer.Calls.Count(c => c == "ChildAt L 1"));    // a child keeps its handle

            // EvaluateAsync in all three contexts: the same summary and children. The visualiser's reads go through its
            // context (counted per call, DBG-509); the adapter sees none of them again: the stop's memory blocks serve them.
            var memoryRequests = harness.SentRequests("readMemory").Count;
            foreach (var context in Enum.GetValues<EvaluateContext>())
            {
                var result = (await session.EvaluateAsync("L", null, context, Ct).WaitAsync(Safety, Ct)).Value;
                Assert.Equal("Count = 3", result.Value);
                Assert.Equal(4, result.ChildCount);
                Assert.Equal(Lines(children), Lines(await session.GetChildrenAsync(result.Children!.Value, ct: Ct).WaitAsync(Safety, Ct)));
            }

            Assert.Equal(memoryRequests, harness.SentRequests("readMemory").Count);
            Assert.Empty(await EndAndGetDebuggerOutputAsync(harness));
        }
        finally
        {
            PdiFixture.DeleteDirectoryOf(pdiPath);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Req", "DBG-511")]
    [Trait("Req", "DBG-512")]
    [Trait("Req", "DBG-427")]
    public async Task ThrowingVisualizer_FallsBackToPlain_LogsOnce()
    {
        var visualizer = new ListVisualizer { SummaryThrows = "boom" };
        var pdiPath = PdiFixture.WriteTemp("Vis.pdi", VisualizerBed.PdiJson);
        try
        {
            await using var harness = await StoppedInTestAsync(new VisualizerRegistry([VisualizerBed.Pattern(visualizer, "TList<*>")]), pdiPath, browse: false);
            var session = harness.Session;

            // Two values of the type: both plain, with their plain children and no [Raw] child; no call faults.
            foreach (var name in new[] { "L", "L2" })
            {
                var value = (await session.EvaluateAsync(name, null, EvaluateContext.Watch, Ct).WaitAsync(Safety, Ct)).Value;
                Assert.Equal(Plain(name), value.Value);
                Assert.Equal(2, value.ChildCount);
                var fields = await session.GetChildrenAsync(value.Children!.Value, ct: Ct).WaitAsync(Safety, Ct);
                Assert.Equal(["FItems", "FCount"], fields.Select(f => f.Name));
            }

            Assert.Equal(2, visualizer.Calls.Count(c => c.StartsWith("Summary", StringComparison.Ordinal)));
            // One Output(Debugger) line for the first failure only.
            Assert.Equal([Failed("test.list", "boom")], await EndAndGetDebuggerOutputAsync(harness));
        }
        finally
        {
            PdiFixture.DeleteDirectoryOf(pdiPath);
        }
    }

    // ---- registry and selection ----

    [Fact]
    [Trait("Req", "DBG-506")]
    public async Task RawChild_ShowsTheUnderlyingFields()
    {
        var bed = new VisualizerBed([VisualizerBed.Pattern(new ListVisualizer(), "TList<*>")]);
        var l = await bed.EvalAsync("L");

        var children = await bed.ChildrenAsync(l);
        var raw = children[^1];
        Assert.Equal("[Raw]", raw.Name);
        Assert.Equal(Plain("L"), raw.Value);
        Assert.Equal("TList<Integer>", raw.TypeName);
        Assert.Equal("L,r", raw.EvaluateName);
        Assert.False(raw.CanModify);
        Assert.Equal(2, raw.ChildCount);
        // Its children are the plain fields.
        var fields = await bed.ChildrenAsync(raw);
        Assert.Equal(["FItems = (10, 20, 30)", "FCount = 3"], Lines(fields));
        Assert.Equal(["L.FItems", "L.FCount"], fields.Select(f => f.EvaluateName));
        Assert.Equal(["[0] = 10", "[1] = 20", "[2] = 30"], await bed.ChildLinesAsync(fields[0]));

        // Paging runs over the list including [Raw]; the same child keeps its handle.
        Assert.Equal(raw, Assert.Single(await bed.ChildrenAsync(l, 3, 1)));
        Assert.Empty(await bed.ChildrenAsync(l, 4, 1));
        // Its expression is a raw watch of the same value.
        Assert.Equal(raw.Value, (await bed.EvalAsync(raw.EvaluateName!)).Value);

        // A visualiser without children of its own: the plain children, then [Raw].
        var points = new VisualizerBed([new(new FixedVisualizer("pt", "point"), null, "TPoint", null, false, false)]);
        Assert.Equal(["X = 5", "Y = 6", "[Raw] = (X: 5; Y: 6)"], await points.ChildLinesAsync(await points.EvalAsync("Pt")));
        Assert.Empty(bed.Log);
    }

    [Fact]
    [Trait("Req", "DBG-502")]
    public async Task GenericPattern_MatchesEveryInstance_AndNothingElse()
    {
        var bed = new VisualizerBed([VisualizerBed.Pattern(new ListVisualizer(), "TList<*>")]);

        Assert.Equal("Count = 3", (await bed.EvalAsync("L")).Value);
        Assert.Equal("Count = 2", (await bed.EvalAsync("P")).Value);          // TList<TPair<string, Integer>>
        Assert.Equal(Plain("N", "TList"), (await bed.EvalAsync("N")).Value);
        Assert.Equal(Plain("H", "TListHelper"), (await bed.EvalAsync("H")).Value);
        Assert.Equal(Plain("O", "TObjectList<TAnimal>"), (await bed.EvalAsync("O")).Value);
        // The first '<' is closed before the end of the name: not an instance of TList.
        Assert.Equal(Plain("Q", "TList<Integer>.TNode<Byte>"), (await bed.EvalAsync("Q")).Value);
        Assert.Equal("42", (await bed.EvalAsync("Num")).Value);
        Assert.Equal("nil", (await bed.EvalAsync("NilL")).Value);

        // Case-insensitive, with the unit when the registration names one.
        var lower = new VisualizerBed([VisualizerBed.Pattern(new ListVisualizer(), "tlist<*>", unit: "gen")]);
        Assert.Equal("Count = 3", (await lower.EvalAsync("L")).Value);
        var otherUnit = new VisualizerBed([VisualizerBed.Pattern(new ListVisualizer(), "TList<*>", unit: "Other")]);
        Assert.Equal(Plain("L"), (await otherUnit.EvalAsync("L")).Value);
        // An exact name matches that name only.
        var exact = new VisualizerBed([VisualizerBed.Pattern(new ListVisualizer(), "TList<TPair<string, Integer>>")]);
        Assert.Equal("Count = 2", (await exact.EvalAsync("P")).Value);
        Assert.Equal(Plain("L"), (await exact.EvalAsync("L")).Value);
        // Kinds: the type's kind must be in the set; with a pattern both must hold.
        var kinds = new VisualizerBed([new(new ListVisualizer(), null, "TList<*>", new HashSet<PdiTypeKind> { PdiTypeKind.Record }, false, false)]);
        Assert.Equal(Plain("L"), (await kinds.EvalAsync("L")).Value);
    }

    [Fact]
    [Trait("Req", "DBG-501")]
    [Trait("Req", "DBG-502")]
    public void InvalidPatterns_Rejected()
    {
        var visualizer = new ListVisualizer();
        foreach (var pattern in new[] { "*", "TList*", "TList<*", "*<*>", "<*>", "TList<*>.TNode", "TDict<string, *>", "TList<**>", "", " " })
            Assert.Throws<ArgumentException>(() => new VisualizerRegistry([VisualizerBed.Pattern(visualizer, pattern)]));
        // Neither a pattern nor kinds.
        Assert.Throws<ArgumentException>(() => new VisualizerRegistry([new(visualizer, null, null, null, false, false)]));
        // An id occurs once per table.
        Assert.Throws<ArgumentException>(() => new VisualizerRegistry(
            [VisualizerBed.Pattern(visualizer, "TList<*>"), VisualizerBed.Pattern(new ListVisualizer(), "TStack<*>")]));

        var valid = new VisualizerRegistry(
        [
            VisualizerBed.Pattern(new ListVisualizer("a"), "TList<*>"),
            VisualizerBed.Pattern(new ListVisualizer("b"), "TList<Integer>"),
            VisualizerBed.Pattern(new ListVisualizer("c"), "TOuter.TInner"),
            VisualizerBed.Pattern(new ListVisualizer("d"), "TOuter.TList<*>"),
            new(new ListVisualizer("e"), null, null, new HashSet<PdiTypeKind> { PdiTypeKind.String }, true, true),
        ]);
        Assert.Equal(["a", "b", "c", "d", "e"], valid.Registrations.Select(r => r.Visualizer.Id));
        // The built-in table holds the string visualiser, first (DBG-515).
        Assert.Equal(["string"], VisualizerRegistry.BuiltIn.Registrations.Select(r => r.Visualizer.Id));
    }

    [Fact]
    [Trait("Req", "DBG-503")]
    public async Task DescendantUsesTheAncestorsVisualizer_ExactNameWinsOverPattern()
    {
        var bed = new VisualizerBed(
        [
            new(new ListVisualizer("k") { Prefix = "k:", Accepts = t => t.Name is "TList" or "TList<Integer>" },
                null, null, new HashSet<PdiTypeKind> { PdiTypeKind.Class }, false, false),
            VisualizerBed.Pattern(new ListVisualizer("a") { Prefix = "a:" }, "TList<*>"),
            VisualizerBed.Pattern(new ListVisualizer("d") { Prefix = "d:", Accepts = _ => false }, "TList<Integer>"),
            VisualizerBed.Pattern(new ListVisualizer("b") { Prefix = "b:" }, "TList<Integer>"),
            VisualizerBed.Pattern(new ListVisualizer("c") { Prefix = "c:" }, "TNamedList"),
        ]);

        // Exact name before pattern before kinds only, whatever the table order; Matches refines (d declines).
        Assert.Equal("b:Count = 3", (await bed.EvalAsync("L")).Value);
        Assert.Equal("a:Count = 2", (await bed.EvalAsync("P")).Value);
        Assert.Equal("k:Count = 1", (await bed.EvalAsync("N")).Value);
        // A descendant without a visualiser of its own takes its ancestor's; the type name stays its own.
        var sorted = await bed.EvalAsync("S");
        Assert.Equal("b:Count = 2", sorted.Value);
        Assert.Equal("TSortedList", sorted.TypeName);
        // A descendant with its own exact registration takes that.
        Assert.Equal("c:Count = 1", (await bed.EvalAsync("NL")).Value);
    }

    [Fact]
    [Trait("Req", "DBG-503")]
    public async Task AliasResolvedBeforeSelection()
    {
        var point = new FixedVisualizer("pt", "point");
        var bed = new VisualizerBed([new(point, null, "TPoint", null, false, false)]);
        // TPos is an alias of TPoint: the aliased type is the next candidate.
        Assert.Equal("point", (await bed.EvalAsync("Pos")).Value);
        Assert.Equal("point", (await bed.EvalAsync("Pt")).Value);

        // The alias itself is the first candidate.
        var both = new VisualizerBed([new(new FixedVisualizer("pt", "point"), null, "TPoint", null, false, false),
            new(new FixedVisualizer("pos", "pos"), null, "TPos", null, false, false)]);
        Assert.Equal("pos", (await both.EvalAsync("Pos")).Value);
        Assert.Equal("point", (await both.EvalAsync("Pt")).Value);

        // Pointers are not dereferenced for selection: the visualiser of T applies to P^, not to P.
        var lists = new VisualizerBed([VisualizerBed.Pattern(new ListVisualizer(), "TList<*>")]);
        var pointer = await lists.EvalAsync("PL");
        Assert.Equal(VisualizerBed.Address(VisualizerBed.AddressOf("L")), pointer.Value);
        Assert.Equal(["^ = Count = 3"], await lists.ChildLinesAsync(pointer));
        Assert.Equal("Count = 3", (await lists.EvalAsync("PL^")).Value);
    }

    // ---- failures ----

    [Fact]
    [Trait("Req", "DBG-511")]
    [Trait("Req", "DBG-512")]
    public async Task ThirdFailure_TurnsTheVisualizerOffForTheSession()
    {
        var visualizer = new ListVisualizer { SummaryThrows = "boom" };
        var bed = new VisualizerBed([VisualizerBed.Pattern(visualizer, "TList<*>")]);

        for (var i = 0; i < 3; i++)
            Assert.Equal(Plain("L"), (await bed.EvalAsync("L")).Value);
        Assert.Equal(
            [Failed("test.list", "boom"), "Visualizer 'test.list' was turned off for this debug session after 3 failures."],
            bed.Log);

        // Off for the rest of the session: a new stop does not reset the counters; the visualiser is not called again.
        bed.NewStop(2);
        Assert.Equal(Plain("L"), (await bed.EvalAsync("L")).Value);
        Assert.Equal(Plain("L2"), (await bed.EvalAsync("L2")).Value);
        Assert.Equal(3, visualizer.Calls.Count);
        Assert.Equal(2, bed.Log.Count);
    }

    [Fact]
    [Trait("Req", "DBG-511")]
    [Trait("Req", "DBG-512")]
    public async Task ChildFailure_IsAnErrorChild_SiblingsUnaffected()
    {
        var bed = new VisualizerBed([VisualizerBed.Pattern(new ListVisualizer { ChildThrows = 1 }, "TList<*>")]);
        var l = await bed.EvalAsync("L");

        Assert.Equal("Count = 3", l.Value);
        var children = await bed.ChildrenAsync(l);
        Assert.Equal(["[0] = 10", "[1] = <visualizer failed: bad element>", "[2] = 30", "[Raw] = " + Plain("L")], Lines(children));
        Assert.Equal(ValueKind.Error, children[1].Kind);
        Assert.Null(children[1].Children);
        Assert.Null(children[1].EvaluateName);
        Assert.False(children[1].CanModify);
        Assert.Equal(ValueKind.Integer, children[2].Kind);
        Assert.Equal([Failed("test.list", "bad element")], bed.Log);
    }

    [Fact]
    [Trait("Req", "DBG-510")]
    public async Task Timeout_OnTheSessionClock_FallsBackToPlain()
    {
        var clock = new ManualTimeProvider();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bed = new VisualizerBed([VisualizerBed.Pattern(new ListVisualizer { BlockSummary = entered }, "TList<*>")], time: clock);
        var limit = clock.WhenTimerCreated(TimeSpan.FromSeconds(2));

        var evaluation = bed.EvalAsync("L");
        await entered.Task.WaitAsync(Safety, Ct);
        await limit.WaitAsync(Safety, Ct);
        clock.Advance(TimeSpan.FromSeconds(1.5));
        Assert.False(evaluation.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(0.5));
        var l = await evaluation.WaitAsync(Safety, Ct);

        Assert.Equal(Plain("L"), l.Value);
        Assert.Equal(2, l.ChildCount);
        Assert.Equal([Failed("test.list", "timed out after 2 s")], bed.Log);
        Assert.Equal(0, clock.ActiveTimers);

        // VisualizerTimeout is the limit.
        var longer = new ManualTimeProvider();
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var patient = new VisualizerBed([VisualizerBed.Pattern(new ListVisualizer { BlockSummary = waiting }, "TList<*>")],
            new ValueFormatOptions { VisualizerTimeout = TimeSpan.FromSeconds(5) }, longer);
        var five = longer.WhenTimerCreated(TimeSpan.FromSeconds(5));
        var slow = patient.EvalAsync("L");
        await waiting.Task.WaitAsync(Safety, Ct);
        await five.WaitAsync(Safety, Ct);
        longer.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(Plain("L"), (await slow.WaitAsync(Safety, Ct)).Value);
        Assert.Equal([Failed("test.list", "timed out after 5 s")], patient.Log);
    }

    [Fact]
    [Trait("Req", "DBG-510")]
    public async Task CallerCancellation_IsNotAFailure()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var visualizer = new ListVisualizer { BlockSummary = entered };
        var bed = new VisualizerBed([VisualizerBed.Pattern(visualizer, "TList<*>")]);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var evaluation = bed.EvalAsync("L", caller.Token);
        await entered.Task.WaitAsync(Safety, Ct);
        await caller.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => evaluation.WaitAsync(Safety, Ct));
        Assert.Empty(bed.Log);
        Assert.Equal(0, bed.Host.Failures("test.list"));
    }

    [Fact]
    [Trait("Req", "DBG-509")]
    public async Task ReadBudgetExceeded_FallsBackToPlain()
    {
        async Task<(string Value, string[] Log, int Reads)> RunAsync(ListVisualizer visualizer)
        {
            var bed = new VisualizerBed([VisualizerBed.Pattern(visualizer, "TList<*>")]);
            var before = bed.Debuggee.Memory.Reads.Count;
            var value = (await bed.EvalAsync("L")).Value;
            return (value, [.. bed.Log], bed.Debuggee.Memory.Reads.Count - before);
        }

        // 64 reads in one call (FCount and 63 more) are within the budget; each reached the debuggee once.
        var within = await RunAsync(new ListVisualizer { ExtraReads = 63 });
        Assert.Equal("Count = 3", within.Value);
        Assert.Empty(within.Log);
        // The 65th throws inside the visualiser: plain, logged.
        var beyond = await RunAsync(new ListVisualizer { ExtraReads = 64 });
        Assert.Equal(Plain("L"), beyond.Value);
        Assert.Equal([Failed("test.list", "read budget exceeded")], beyond.Log);
        // Catching the exception does not help: the call is a failure.
        var swallowed = await RunAsync(new ListVisualizer { ExtraReads = 64, SwallowContextErrors = true });
        Assert.Equal(Plain("L"), swallowed.Value);
        Assert.Equal([Failed("test.list", "read budget exceeded")], swallowed.Log);
        // At most 1 MiB in total: FCount's 4 bytes and 1 MiB − 4 are fine, one byte more is not.
        Assert.Equal("Count = 3", (await RunAsync(new ListVisualizer { BigRead = (1 << 20) - 4 })).Value);
        var big = await RunAsync(new ListVisualizer { BigRead = (1 << 20) - 3 });
        Assert.Equal(Plain("L"), big.Value);
        Assert.Equal([Failed("test.list", "read budget exceeded")], big.Log);
        // Each call through the context reached the debuggee once, and the read beyond the budget is never sent.
        var plainBed = new VisualizerBed([]);
        var plainBefore = plainBed.Debuggee.Memory.Reads.Count;
        await plainBed.EvalAsync("L");
        Assert.Equal(plainBed.Debuggee.Memory.Reads.Count - plainBefore + 64, beyond.Reads);
    }

    // ---- raw mode, options, nested form ----

    [Fact]
    [Trait("Req", "DBG-508")]
    [Trait("Req", "DBG-427")]
    public async Task RawSuffix_AndRawSubtree_UseNoVisualizer()
    {
        var list = new ListVisualizer();
        var holder = new FixedVisualizer("holder", "holder");
        var integers = new FixedVisualizer("t.int", "never shown");
        var bed = new VisualizerBed(
        [
            VisualizerBed.Pattern(list, "TList<*>"),
            new(holder, null, "THolder", null, false, false),
            new(integers, null, null, new HashSet<PdiTypeKind> { PdiTypeKind.Integer }, Transparent: true, SummarizesNested: true),
        ]);

        // The suffix: plain value, plain children, no [Raw].
        var raw = await bed.EvalAsync("L,r");
        Assert.Equal("L,r", raw.Name);
        Assert.Equal("L", raw.EvaluateName);
        Assert.Equal(Plain("L"), raw.Value);
        Assert.Equal(["FItems = (10, 20, 30)", "FCount = 3"], await bed.ChildLinesAsync(raw));
        // ... for the whole subtree.
        var rawHolder = await bed.EvalAsync("Hd,r");
        Assert.Equal("(Tag: 9; List: " + Plain("L") + ")", rawHolder.Value);
        var rawList = (await bed.ChildrenAsync(rawHolder))[1];
        Assert.Equal(Plain("L"), rawList.Value);
        Assert.Equal(["FItems = (10, 20, 30)", "FCount = 3"], await bed.ChildLinesAsync(rawList));

        // Without the suffix the holder and its list are visualised; below [Raw] nothing is.
        var hd = await bed.EvalAsync("Hd");
        Assert.Equal("holder", hd.Value);
        var children = await bed.ChildrenAsync(hd);
        Assert.Equal(["Tag = 9", "List = Count = 3", "[Raw] = (Tag: 9; List: " + Plain("L") + ")"], Lines(children));
        Assert.Equal("Hd,r", children[2].EvaluateName);
        var belowRaw = await bed.ChildrenAsync(children[2]);
        Assert.Equal(["Tag = 9", "List = " + Plain("L")], Lines(belowRaw));
        Assert.Equal(["FItems = (10, 20, 30)", "FCount = 3"], await bed.ChildLinesAsync(belowRaw[1]));

        // A transparent visualiser still applies in raw mode, and its summary is never asked for: the plain one is shown.
        Assert.Equal("42", (await bed.EvalAsync("Num")).Value);
        Assert.Equal("42", (await bed.EvalAsync("Num,r")).Value);
        Assert.Empty(integers.Calls);
        Assert.Empty(bed.Log);
    }

    [Fact]
    [Trait("Req", "DBG-508")]
    [Trait("Req", "DBG-514")]
    public async Task UseVisualizersOff_GivesThePlainRendering()
    {
        // The defaults keep the plain rendering of every value no visualiser is registered for.
        var defaults = new ValueFormatOptions();
        Assert.True(defaults.UseVisualizers);
        Assert.Equal(TimeSpan.FromSeconds(2), defaults.VisualizerTimeout);
        // A session's table is the built-in one: only the transparent string visualiser, which keeps every plain summary (DBG-515).
        var sessionTable = new DebugSessionOptions { AdapterPath = @"C:\x\adapter.exe" }.Visualizers;
        Assert.Same(VisualizerRegistry.BuiltIn, sessionTable);
        Assert.All(sessionTable.Registrations, r => Assert.True(r.Transparent));

        var visualizer = new ListVisualizer();
        var bed = new VisualizerBed([VisualizerBed.Pattern(visualizer, "TList<*>", summarizesNested: true)],
            new ValueFormatOptions { UseVisualizers = false });
        var l = await bed.EvalAsync("L");
        Assert.Equal(Plain("L"), l.Value);
        Assert.Equal(["FItems = (10, 20, 30)", "FCount = 3"], await bed.ChildLinesAsync(l));
        Assert.Equal("(Tag: 9; List: " + Plain("L") + ")", (await bed.EvalAsync("Hd")).Value);
        Assert.Contains(await bed.LocalsAsync(), v => v.Name == "S" && v.Value == Plain("S", "TSortedList"));
        Assert.Empty(visualizer.Calls);
        Assert.Empty(bed.Log);
    }

    [Fact]
    [Trait("Req", "DBG-507")]
    [Trait("Req", "DBG-504")]
    public async Task NestedSummary_PlainUnlessSummarizesNested()
    {
        // Not SummarizesNested: the plain nested form, and the visualiser is not called for it.
        var notNested = new ListVisualizer();
        var plain = new VisualizerBed([VisualizerBed.Pattern(notNested, "TList<*>")]);
        Assert.Equal("(Tag: 9; List: " + Plain("L") + ")", (await plain.EvalAsync("Hd")).Value);
        Assert.Empty(notNested.Calls);

        // SummarizesNested: its nested summary; a parent's summary never asks for a child's children.
        var nested = new ListVisualizer();
        var bed = new VisualizerBed([VisualizerBed.Pattern(nested, "TList<*>", summarizesNested: true)]);
        Assert.Equal("(Tag: 9; List: #3)", (await bed.EvalAsync("Hd")).Value);
        Assert.Equal(["Nested Hd.List"], nested.Calls);

        // A second pointer level in a nested summary is refused: the plain nested form, one failure.
        var deep = new VisualizerBed([VisualizerBed.Pattern(new ListVisualizer { NestedReadsElements = true }, "TList<*>", summarizesNested: true)]);
        Assert.Equal("(Tag: 9; List: " + Plain("L") + ")", (await deep.EvalAsync("Hd")).Value);
        Assert.Equal([Failed("test.list", "more than one pointer level in a nested summary")], deep.Log);
        // Outside a nested summary the same visualiser reads as deep as it likes.
        Assert.Equal("Count = 3", (await deep.EvalAsync("L")).Value);
        Assert.Single(deep.Log);
    }
}

/// <summary>A visualiser with a fixed summary and no children of its own (the plain children are used).</summary>
internal sealed class FixedVisualizer(string id, string summary) : IValueVisualizer
{
    public string Id => id;

    public ConcurrentQueue<string> Calls { get; } = new();

    public bool Matches(PdiType type) => true;

    public Task<string> SummaryAsync(ValueNode value, VisualizerContext context, CancellationToken ct)
    {
        Calls.Enqueue("Summary " + value.EvaluateName);
        return Task.FromResult(summary);
    }

    public Task<VisualizerChildren?> ChildrenAsync(ValueNode value, VisualizerContext context, CancellationToken ct)
    {
        Calls.Enqueue("Children " + value.EvaluateName);
        return Task.FromResult<VisualizerChildren?>(null);
    }

    public ITextSource? TextSource(ValueNode value, VisualizerContext context) => null;
}
