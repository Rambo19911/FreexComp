using System.Text;
using FreexComp.Debugger.Expressions;
using FreexComp.Debugger.Tests.TestSupport;
using FreexComp.Debugger.Values;
using FreexComp.Debugger.Values.Visualizers;
using B = FreexComp.Debugger.Tests.TestSupport.StringBed;

namespace FreexComp.Debugger.Tests;

/// <summary>
/// The built-in string visualiser and text reading (visualizers.md §4, DBG-515…523; debug-session DBG-428…430): every
/// string kind keeps its plain summary and gains a text handle; details, chunked reading with its boundaries, partial
/// and unreadable memory, stale handles and the state rule.
/// </summary>
public sealed class StringVisualizerTests
{
    private static readonly TimeSpan Safety = SessionHarness.Safety;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Everything of a row but its handles (which are numbered per service).</summary>
    private static string Shape(VariableInfo v) =>
        $"{v.Name} = {v.Value} : {v.TypeName} {v.Kind} children {v.ChildCount} modify {v.CanModify} expr {v.EvaluateName} at {v.Address}";

    // ---- summaries and text handles ----

    [Theory]
    [Trait("Req", "DBG-515")]
    [Trait("Req", "DBG-516")]
    [Trait("Req", "DBG-521")]
    [Trait("Req", "DBG-523")]
    [Trait("Req", "DBG-428")]
    // expression, its summary ("$P" / "$PA": the address the pointer holds), its text (null: no text handle)
    [InlineData("U", "'Grüße ✓ 😀'", B.UText)]
    [InlineData("NilU", "''", "")]
    [InlineData("A8", "'Grüße'", "Grüße")]
    [InlineData("A1252", "'€a'", "€a")]
    [InlineData("Sh", "'short'", "short")]
    [InlineData("P", "$P 'Grüße ✓ 😀'", B.UText)]
    [InlineData("NilP", "nil", null)]
    [InlineData("PA", "$PA 'GrÃ¼Ã'#159'e'", "GrÃ¼Ã\u009Fe")]
    [InlineData("CA", "'abc'", "abc")]
    [InlineData("DA", "'hello'", "hello")]
    [InlineData("AA", "'xy'", "xy")]
    [InlineData("U + '!'", "'Grüße ✓ 😀!'", B.UText + "!")]
    [InlineData("Num", "42", null)]
    [InlineData("Pt", "(X: 1; Y: 2)", null)]
    public async Task EveryKind_SummaryUnchanged_TextHandlePresent(string expression, string summary, string? text)
    {
        var bed = new B();
        var plain = new B(new VisualizerRegistry([]), bed.Debuggee);
        summary = summary.Replace("$PA", B.Address(B.PayloadOf("A8")), StringComparison.Ordinal)
            .Replace("$P", B.Address(B.PayloadOf("U")), StringComparison.Ordinal);

        // Watch / Evaluate: the plain summary, everything as without any visualiser, plus the text handle.
        var info = await bed.EvalAsync(expression);
        var withoutVisualizers = await plain.EvalAsync(expression);
        Assert.Equal(summary, info.Value);
        Assert.Equal(Shape(withoutVisualizers), Shape(info));
        Assert.Null(withoutVisualizers.Text);
        if (text is null)
        {
            Assert.Null(info.Text);
            return;
        }

        var handle = B.TextOf(info);
        Assert.Equal(1, handle.Epoch);
        Assert.Equal(text, (await bed.ReadAllAsync(info)).Text);
        // The same in the Locals list (not for a computed value, which is no variable).
        if (!expression.Contains('+', StringComparison.Ordinal))
        {
            var local = (await bed.LocalsAsync()).Single(v => v.Name == expression);
            Assert.Equal(Shape(info), Shape(local));
            Assert.Equal(text, (await bed.ReadAllAsync(local)).Text);
        }

        // The children of a character array are unchanged; a text handle has none.
        if (info.Children is { } children)
        {
            Assert.Equal(
                (await plain.Values.GetChildrenAsync(withoutVisualizers.Children!.Value, 0, 0, Ct)).Select(Shape),
                (await bed.Values.GetChildrenAsync(children, 0, 0, Ct)).Select(Shape));
        }

        Assert.Empty(await bed.Values.GetChildrenAsync(handle, 0, 0, Ct));
        Assert.Empty(bed.Log);
    }

    [Fact]
    [Trait("Req", "DBG-515")]
    [Trait("Req", "DBG-516")]
    public async Task EveryKind_RawSuffixAndNestedStrings_KeepTheirForms()
    {
        var bed = new B();
        // The visualiser is transparent: the raw suffix changes nothing and keeps the text.
        var raw = await bed.EvalAsync("U,r");
        Assert.Equal("'Grüße ✓ 😀'", raw.Value);
        Assert.Equal(B.UText, (await bed.ReadAllAsync(raw)).Text);
        // Off for the session: still transparent.
        var off = new B(debuggee: bed.Debuggee, options: new ValueFormatOptions { UseVisualizers = false });
        Assert.NotNull((await off.EvalAsync("U")).Text);
        // An unreadable string is an error node: no handle.
        bed.Debuggee.Memory.PutPointer(B.AddressOf("K"), 0x7FFF_0000);
        var broken = new B(debuggee: bed.Debuggee);
        var error = await broken.EvalAsync("K");
        Assert.Equal(ValueKind.Error, error.Kind);
        Assert.Null(error.Text);
    }

    // ---- details ----

    [Fact]
    [Trait("Req", "DBG-517")]
    [Trait("Req", "DBG-518")]
    [Trait("Req", "DBG-522")]
    [Trait("Req", "DBG-429")]
    public async Task Details_LengthReferenceCountEncoding()
    {
        var bed = new B();
        async Task<TextDetails> Of(string expression) => await bed.DetailsAsync(await bed.EvalAsync(expression));

        // Heap block with reference count 2; a literal's block is constant (-1); nil has none.
        Assert.Equal(new TextDetails(TextKind.Unicode, 10, 2, 1200, "UTF-16", 2, B.PayloadOf("U"), null), await Of("U"));
        Assert.Equal(new TextDetails(TextKind.Unicode, 5, -1, 1200, "UTF-16", 2, B.PayloadOf("K"), null), await Of("K"));
        Assert.Equal(new TextDetails(TextKind.Unicode, 0, null, 1200, "UTF-16", 2, null, null), await Of("NilU"));

        // Byte strings: length in bytes, the header's code page and its encoding text.
        Assert.Equal(new TextDetails(TextKind.Ansi, 7, 1, 65001, "UTF-8 (65001)", 1, B.PayloadOf("A8"), null), await Of("A8"));
        Assert.Equal(new TextDetails(TextKind.Ansi, 2, 1, 1252, "Windows-1252 (1252)", 1, B.PayloadOf("A1252"), null), await Of("A1252"));
        Assert.Equal(new TextDetails(TextKind.Ansi, 4, 1, 1200, "UTF-16 (1200)", 1, B.PayloadOf("A1200"), null), await Of("A1200"));
        Assert.Equal(new TextDetails(TextKind.Ansi, 3, 1, 65535, "raw bytes (65535)", 1, B.PayloadOf("A65535"), null), await Of("A65535"));
        Assert.Equal(new TextDetails(TextKind.Ansi, 3, 1, 437, "code page 437", 1, B.PayloadOf("A437"), null), await Of("A437"));
        Assert.Equal(new TextDetails(TextKind.Ansi, 0, null, null, "UTF-8 (65001)", 1, null, null), await Of("NilA"));

        // Short string: the declared maximum; the bytes follow the length byte.
        Assert.Equal(new TextDetails(TextKind.Short, 5, null, null, "UTF-8", 1, B.AddressOf("Sh") + 1, 10), await Of("Sh"));

        // Pointers: the length by scanning for the terminator (beyond the display limit too); none: -1.
        Assert.Equal(new TextDetails(TextKind.PChar, 10, null, null, "UTF-16", 2, B.PayloadOf("U"), null), await Of("P"));
        Assert.Equal(new TextDetails(TextKind.PChar, 3000, null, null, "UTF-16", 2, B.PayloadOf("LongP"), null), await Of("LongP"));
        Assert.Equal(new TextDetails(TextKind.PChar, -1, null, null, "UTF-16", 2, B.PayloadOf("NoTermP"), null), await Of("NoTermP"));
        Assert.Equal(new TextDetails(TextKind.PAnsiChar, 7, null, null, "8-bit", 1, B.PayloadOf("A8"), null), await Of("PA"));

        // Character arrays: the elements before the first #0.
        Assert.Equal(new TextDetails(TextKind.CharArray, 3, null, null, "UTF-16", 2, B.AddressOf("CA"), null), await Of("CA"));
        Assert.Equal(new TextDetails(TextKind.CharArray, 5, null, null, "UTF-16", 2, B.PayloadOf("DA"), null), await Of("DA"));
        Assert.Equal(new TextDetails(TextKind.CharArray, 2, null, null, "8-bit", 1, B.AddressOf("AA"), null), await Of("AA"));

        // Computed: the value itself.
        Assert.Equal(new TextDetails(TextKind.Computed, 11, null, null, "UTF-16", 2, null, null), await Of("U + '!'"));

        // A scan reads in 64 KiB pieces: one read for the 3000 units after the display read.
        var reads = bed.Debuggee.Memory.Reads;
        reads.Clear();
        var longP = await bed.EvalAsync("LongP");
        var displayReads = reads.Count;
        Assert.Equal(3000, (await bed.DetailsAsync(longP)).Length);
        Assert.Equal([$"{B.PayloadOf("LongP"):X}+65536"], reads.Skip(displayReads).Take(1));
    }

    [Fact]
    [Trait("Req", "DBG-524")]
    [Trait("Req", "DBG-517")]
    public async Task Details_ReferenceCountAtTheSidecarOffset()
    {
        // A sidecar that names another place for the reference count: the count is read there.
        var abi = PdiFixture.Abi.Replace("\"charSize\": 2 }", "\"charSize\": 2, \"refCountOffset\": -24, \"refCountSize\": 8 }", StringComparison.Ordinal);
        var pdi = PdiFixture.Parse(B.PdiJson.Replace(PdiFixture.Abi, abi, StringComparison.Ordinal));
        Assert.Equal(-24, pdi.Abi.UnicodeString.RefCountOffset);
        var debuggee = B.NewDebuggee();
        debuggee.Memory.PutInt64(B.PayloadOf("U") - 24, 7);
        var values = new ValueService(pdi, debuggee, new ValueFormatOptions(), canWriteMemory: true);
        values.Reset(1);
        var frame = new FrameContext(DemoMemory.AdapterFrameId, pdi.Units.SelectMany(u => u.Routines).Single(), B.StopLine);
        var info = await values.EvaluateAsync("U", frame, allowFormatSuffix: true, expandable: true, Ct);

        Assert.Equal(7, (await values.GetTextDetailsAsync(B.TextOf(info), Ct)).RefCount);
    }

    // ---- reading ----

    [Fact]
    [Trait("Req", "DBG-519")]
    [Trait("Req", "DBG-430")]
    public async Task ReadText_ChunksClampAndNext_SurrogatePairAndUtf8SequenceKept()
    {
        var debuggee = B.NewDebuggee();
        var memory = debuggee.Memory;
        // Units a b D83D DE00 c d; then a lone surrogate and #0 kept verbatim.
        B.PutUnicode(memory, B.PayloadOf("U"), "ab😀cd", refCount: 1);
        B.PutUnicode(memory, B.PayloadOf("K"), "x\uD83Dy\0z", refCount: 1);
        // UTF-8 "aü€😀": sequences of 1, 2, 3 and 4 bytes.
        var utf8 = Encoding.UTF8.GetBytes("aü€😀");
        B.PutAnsi(memory, B.PayloadOf("A8"), utf8, 65001, refCount: 1);
        memory.Put(B.AddressOf("Sh"), [2, 0xC3, 0xBC, 0, 0, 0, 0, 0, 0, 0, 0]);
        var bed = new B(debuggee: debuggee);
        var u = await bed.EvalAsync("U");

        // Never between the two units of a pair: the chunk ends one unit earlier.
        Assert.Equal(new TextChunk(0, 2, "ab", false, false), await bed.ReadAsync(u, 0, 3));
        Assert.Equal(new TextChunk(2, 5, "😀c", false, false), await bed.ReadAsync(u, 2, 3));
        Assert.Equal(new TextChunk(5, 6, "d", true, false), await bed.ReadAsync(u, 5, 3));
        Assert.Equal(new TextChunk(6, 6, "", true, false), await bed.ReadAsync(u, 6, 3));
        Assert.Equal(new TextChunk(60, 60, "", true, false), await bed.ReadAsync(u, 60, 3));
        // A chunk of one unit at a pair takes the whole pair (it could not progress otherwise).
        Assert.Equal(new TextChunk(2, 4, "😀", false, false), await bed.ReadAsync(u, 2, 1));
        // A pair that the end of the chunk does not cut is kept, the second unit alone too.
        Assert.Equal(new TextChunk(1, 4, "b😀", false, false), await bed.ReadAsync(u, 1, 3));
        Assert.Equal(new TextChunk(3, 4, "\uDE00", false, false), await bed.ReadAsync(u, 3, 1));

        // count is clamped to 1 … 65 536.
        Assert.Equal(new TextChunk(0, 1, "a", false, false), await bed.ReadAsync(u, 0, 0));
        Assert.Equal(new TextChunk(0, 1, "a", false, false), await bed.ReadAsync(u, 0, -5));
        Assert.Equal(new TextChunk(0, 6, "ab😀cd", true, false), await bed.ReadAsync(u, 0, int.MaxValue));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => bed.ReadAsync(u, -1, 10));

        // Unpaired surrogates and #0 come verbatim, one unit at a time too.
        var k = await bed.EvalAsync("K");
        Assert.Equal("x\uD83Dy\0z", (await bed.ReadAllAsync(k)).Text);
        Assert.Equal(["x", "\uD83D", "y", "\0", "z"], (await bed.ReadAllAsync(k, 1)).Chunks.Select(c => c.Text));

        // UTF-8: never inside a sequence; a chunk that is only the start of one takes the whole sequence.
        var a = await bed.EvalAsync("A8");
        Assert.Equal(new TextChunk(0, 1, "a", false, false), await bed.ReadAsync(a, 0, 2));
        Assert.Equal(new TextChunk(1, 3, "ü", false, false), await bed.ReadAsync(a, 1, 2));
        Assert.Equal(new TextChunk(3, 6, "€", false, false), await bed.ReadAsync(a, 3, 4));
        Assert.Equal(new TextChunk(6, 10, "😀", true, false), await bed.ReadAsync(a, 6, 1));
        foreach (var count in (int[])[1, 2, 3, 5])
            Assert.Equal("aü€😀", (await bed.ReadAllAsync(a, count)).Text);
        var sh = await bed.EvalAsync("Sh");
        Assert.Equal(new TextChunk(0, 2, "ü", true, false), await bed.ReadAsync(sh, 0, 1));

        // Pointers and character arrays end at #0.
        var ca = await bed.EvalAsync("CA");
        Assert.Equal(new TextChunk(0, 3, "abc", true, false), await bed.ReadAsync(ca, 0, 100));
        Assert.Equal(new TextChunk(0, 3, "abc", true, false), await bed.ReadAsync(ca, 0, 3));
        var p = await bed.EvalAsync("P");
        Assert.Equal(new TextChunk(0, 6, "ab😀cd", true, false), await bed.ReadAsync(p, 0, 100));
        Assert.Equal(new TextChunk(0, 2, "ab", false, false), await bed.ReadAsync(p, 0, 3));
        var longP = await bed.EvalAsync("LongP");
        var (longText, longChunks) = await bed.ReadAllAsync(longP, 1000);
        Assert.Equal(new string('y', 3000), longText);
        Assert.Equal([1000L, 2000, 3000], longChunks.Select(c => c.Next));
        Assert.Equal([false, false, true], longChunks.Select(c => c.AtEnd));
    }

    [Fact]
    [Trait("Req", "DBG-520")]
    [Trait("Req", "DBG-430")]
    public async Task ReadText_PartlyReadable_Truncated()
    {
        var debuggee = B.NewDebuggee();
        // The header says 1000 units; only the first 384 (768 bytes, three blocks) are readable.
        var payload = B.PayloadOf("U");
        debuggee.Memory.Remove(payload, 0x1F00);
        debuggee.Memory.PutInt32(payload - 12, 1000).Put(payload, [.. new string('w', 384).SelectMany(c => new[] { (byte)c, (byte)0 })]);
        var bed = new B(debuggee: debuggee);
        var u = await bed.EvalAsync("U");

        Assert.Equal(new TextChunk(0, 384, new string('w', 384), true, true), await bed.ReadAsync(u, 0, 1000));
        Assert.Equal(new TextChunk(380, 384, "wwww", true, true), await bed.ReadAsync(u, 380, 100));
        Assert.Equal(new TextChunk(0, 100, new string('w', 100), false, false), await bed.ReadAsync(u, 0, 100));

        // A pointer whose memory ends before a terminator: the readable prefix, truncated.
        var noTerm = await bed.EvalAsync("NoTermP");
        Assert.Equal(new TextChunk(0, 100, new string('q', 100), false, false), await bed.ReadAsync(noTerm, 0, 100));
        Assert.Equal(new TextChunk(0, 128, new string('q', 128), true, true), await bed.ReadAsync(noTerm, 0, 1000));
    }

    [Fact]
    [Trait("Req", "DBG-520")]
    [Trait("Req", "DBG-430")]
    public async Task ReadText_Unreadable_MemoryUnreadable()
    {
        var debuggee = B.NewDebuggee();
        var payload = B.PayloadOf("U");
        debuggee.Memory.Remove(payload, 0x1F00);
        debuggee.Memory.PutInt32(payload - 12, 1000).Put(payload, [.. new string('w', 384).SelectMany(c => new[] { (byte)c, (byte)0 })]);
        var bed = new B(debuggee: debuggee);
        var u = await bed.EvalAsync("U");

        // Nothing readable at the start of the chunk.
        var error = await Assert.ThrowsAsync<DebuggerException>(() => bed.ReadAsync(u, 384, 10));
        Assert.Equal(DebugErrorCode.MemoryUnreadable, error.Code);
        Assert.Equal("<unreadable memory at " + B.Address(payload + 768) + ">", error.Message);
        error = await Assert.ThrowsAsync<DebuggerException>(() => bed.ReadAsync(u, 900, 10));
        Assert.Equal("<unreadable memory at " + B.Address(payload + 1800) + ">", error.Message);
        // The end of the text is not unreadable: it is the end.
        Assert.Equal(new TextChunk(1000, 1000, "", true, false), await bed.ReadAsync(u, 1000, 10));
    }

    [Fact]
    [Trait("Req", "DBG-429")]
    [Trait("Req", "DBG-430")]
    [Trait("Req", "DBG-516")]
    public async Task ReadText_StaleHandle_StaleReference()
    {
        var bed = new B();
        var u = await bed.EvalAsync("U");
        var ca = await bed.EvalAsync("CA");
        Assert.Equal(B.UText, (await bed.ReadAllAsync(u)).Text);

        // A handle that is no text handle (the children of a character array) or an unknown one.
        foreach (var handle in new[] { ca.Children!.Value, new VariableHandle(1, 999) })
        {
            Assert.Equal(DebugErrorCode.StaleReference,
                (await Assert.ThrowsAsync<DebuggerException>(() => bed.Values.ReadTextAsync(handle, 0, 10, Ct))).Code);
            Assert.Equal(DebugErrorCode.StaleReference,
                (await Assert.ThrowsAsync<DebuggerException>(() => bed.Values.GetTextDetailsAsync(handle, Ct))).Code);
        }

        // The next stop: every text handle of the previous one is stale, before a negative start is looked at.
        bed.Values.Reset(2);
        var read = await Assert.ThrowsAsync<DebuggerException>(() => bed.ReadAsync(u, 0, 10));
        Assert.Equal(DebugErrorCode.StaleReference, read.Code);
        Assert.Equal(Messages.StaleHandle, read.Message);
        Assert.Equal(DebugErrorCode.StaleReference, (await Assert.ThrowsAsync<DebuggerException>(() => bed.ReadAsync(u, -1, 10))).Code);
        Assert.Equal(DebugErrorCode.StaleReference, (await Assert.ThrowsAsync<DebuggerException>(() => bed.DetailsAsync(u))).Code);
        // A handle of the new stop works.
        var again = await bed.EvalAsync("U");
        Assert.Equal(2, B.TextOf(again).Epoch);
        Assert.Equal(B.UText, (await bed.ReadAllAsync(again)).Text);
    }

    /// <summary>A session over the FakeAdapter stopped in <c>Test</c> of the bench program; it answers one stack trace and one continue.</summary>
    private static async Task<SessionHarness> StoppedInTestAsync(string pdiPath)
    {
        var debuggee = B.NewDebuggee();
        var variables = string.Join(", ", debuggee.Variables.Select(v => $"\"{v.Key}\": \"0x{v.Value:X}\""));
        var scenario = $$"""
            { "variables": { {{variables}} },
              "memory": {{debuggee.Memory.ScenarioJson()}},
              "steps": [
                { "on": "configurationDone", "respond": {}, "thenEvents": [ { "event": "stopped", "body": { "reason": "step", "threadId": 1 } } ] },
                { "on": "threads", "respond": { "threads": [ { "id": 1, "name": "main" } ] } },
                { "on": "stackTrace", "respond": { "stackFrames": [ { "id": 524288, "name": "Test", "line": {{B.StopLine}}, "column": 5,
                  "source": { "path": "{{B.UnitFile.Replace(@"\", @"\\", StringComparison.Ordinal)}}" } } ] } },
                { "on": "continue", "respond": { "allThreadsContinued": true } } ] }
            """;
        var harness = SessionHarness.Create(scenario);
        await harness.LaunchAsync(new LaunchRequest
        {
            Program = SessionHarness.Program, TypeInfoPath = pdiPath, Exceptions = SessionHarness.NoLanguageExceptions,
        });
        await harness.WaitForEventAsync("stopped");
        return harness;
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Req", "DBG-429")]
    [Trait("Req", "DBG-430")]
    [Trait("Req", "DBG-428")]
    [Trait("Req", "DBG-515")]
    public async Task ReadText_NotStopped_InvalidState()
    {
        // Before a launch: no text call is allowed.
        await using (var idle = new DebugSessionFactory().Create(new DebugSessionOptions { AdapterPath = @"C:\nowhere\lldb-dap.exe" }))
        {
            Assert.Equal(DebugErrorCode.InvalidState,
                (await Assert.ThrowsAsync<DebuggerException>(() => idle.ReadTextAsync(new VariableHandle(1, 1), 0, 10, Ct))).Code);
            Assert.Equal(DebugErrorCode.InvalidState,
                (await Assert.ThrowsAsync<DebuggerException>(() => idle.GetTextDetailsAsync(new VariableHandle(1, 1), Ct))).Code);
        }

        var pdiPath = PdiFixture.WriteTemp("Str.pdi", B.PdiJson);
        try
        {
            await using var harness = await StoppedInTestAsync(pdiPath);
            var session = harness.Session;
            // Stopped: the session's built-in table gives the text handle; details and text come through the session.
            var u = (await session.EvaluateAsync("U", null, EvaluateContext.Watch, Ct).WaitAsync(Safety, Ct)).Value;
            Assert.Equal("'Grüße ✓ 😀'", u.Value);
            var text = B.TextOf(u);
            Assert.Equal(new TextDetails(TextKind.Unicode, 10, 2, 1200, "UTF-16", 2, B.PayloadOf("U"), null),
                await session.GetTextDetailsAsync(text, Ct).WaitAsync(Safety, Ct));
            Assert.Equal(new TextChunk(0, 10, B.UText, true, false), await session.ReadTextAsync(text, 0, 100, Ct).WaitAsync(Safety, Ct));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => session.ReadTextAsync(text, -1, 100, Ct).WaitAsync(Safety, Ct));

            // Running: refused before anything is read.
            await session.ContinueAsync(Ct).WaitAsync(Safety, Ct);
            Assert.Equal(DebugState.Running, session.State);
            var refused = await Assert.ThrowsAsync<DebuggerException>(() => session.ReadTextAsync(text, 0, 100, Ct).WaitAsync(Safety, Ct));
            Assert.Equal(DebugErrorCode.InvalidState, refused.Code);
            Assert.Equal(DebugErrorCode.InvalidState,
                (await Assert.ThrowsAsync<DebuggerException>(() => session.GetTextDetailsAsync(text, Ct).WaitAsync(Safety, Ct))).Code);

            await session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
            await harness.WaitForEventAsync("terminated");
            harness.AssertScenarioComplete();
        }
        finally
        {
            PdiFixture.DeleteDirectoryOf(pdiPath);
        }
    }

    [Fact]
    [Trait("Req", "DBG-519")]
    [Trait("Req", "DBG-430")]
    public async Task ReadText_MegabyteString_ReadsOnlyTheRequestedChunk()
    {
        const int Units = 1_048_576;
        var debuggee = B.NewDebuggee();
        var payload = 0x1000_0000UL;
        var units = new char[Units];
        for (var i = 0; i < Units; i++)
            units[i] = (char)('a' + i % 26);
        var big = new string(units);
        debuggee.Memory.PutInt32(payload - 16, 1).PutInt32(payload - 12, Units)
            .Put(payload, [.. Utf16Bytes(big), 0, 0]);
        debuggee.Memory.PutPointer(B.AddressOf("U"), payload);
        var bed = new B(debuggee: debuggee);
        var u = await bed.EvalAsync("U");
        Assert.Equal("'" + big[..1024] + "'… (Length: 1048576)", u.Value);
        Assert.Equal(Units, (await bed.DetailsAsync(u)).Length);

        // Chunk 3 of 16: only its own bytes and at most one block more are read.
        var reads = bed.Debuggee.Memory.Reads;
        reads.Clear();
        var chunk = await bed.ReadAsync(u, 2 * 65536, 65536);
        Assert.Equal(new TextChunk(131072, 196608, big.Substring(131072, 65536), false, false), chunk);
        var read = reads.Select(r => r.Split('+')).Select(r => (Address: Convert.ToUInt64(r[0], 16), Count: int.Parse(r[1], System.Globalization.CultureInfo.InvariantCulture))).ToList();
        Assert.True(read.Sum(r => (long)r.Count) <= 131072 + MemoryReader.BlockSize, string.Join(", ", reads));
        Assert.All(read, r => Assert.True(r.Address >= payload + 262144 && r.Address + (ulong)r.Count <= payload + 393216 + MemoryReader.BlockSize, $"{r.Address:X}+{r.Count}"));

        // The last chunk ends the text.
        Assert.Equal(new TextChunk(15 * 65536, Units, big[(15 * 65536)..], true, false), await bed.ReadAsync(u, 15 * 65536, 65536));
    }

    private static byte[] Utf16Bytes(string text) => Encoding.Unicode.GetBytes(text);
}
