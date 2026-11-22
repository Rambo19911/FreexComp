using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FreexComp.Debugger.Spike;

/// <summary>One table cell: pass/fail plus the evidence (adapter responses) it was decided on.</summary>
internal sealed record Row(bool Pass, string Evidence);

internal sealed record Stop(string? Reason, int? ThreadId, JsonElement Raw);

internal sealed record Frame(long Id, string Name, string? Path, int Line, JsonElement Raw);

internal static partial class Program
{
    private static string _adapter = "", _python = "", _src = "", _out = "";
    // column -> step -> row
    private static readonly Dictionary<string, Dictionary<string, Row>> Results = [];

    private static readonly string[] PartASteps = ["A1", "A2", "A3", "A4", "A5", "A6", "A7", "A8"];
    private static readonly string[] PartBSteps = ["B1", "B2", "B3", "B4", "B5", "B6", "B7", "B8", "B9"];
    private static readonly string[] InfoSteps = ["I1", "I2", "I3", "I4", "I5"];
    private static readonly Dictionary<string, string> StepText = new()
    {
        ["A1"] = "initialize: success within 10 s, supportsConfigurationDoneRequest",
        ["A2"] = "launch + setBreakpoints hello.pas:12 + configurationDone: verified, line 12",
        ["A3"] = "stopped within 15 s, reason breakpoint, threadId",
        ["A4"] = "stackTrace frame 0: source.path ends hello.pas, line 12",
        ["A5"] = "scopes + variables: A and B exist, A = 21",
        ["A6"] = "next: stopped reason step, frame 0 line 13",
        ["A7"] = "variables again: B = 42",
        ["A8"] = "continue: exited 0 (or terminated) within 15 s; disconnect answered",
        ["B1"] = "evaluate &A (watch, frame 0): hex address obtainable",
        ["B2"] = "readMemory 4 bytes at &A: 15 00 00 00 (gate step)",
        ["B3"] = "stepIn at line 12: stop in Twice, line 5",
        ["B4"] = "output event category stdout containing 42",
        ["B5"] = "loop variant: pause -> stopped within 5 s",
        ["B6"] = "loop variant: setBreakpoints line 8 while running -> stopped breakpoint",
        ["B7"] = "setFunctionBreakpoints Twice: stop with top frame in Twice",
        ["B8"] = "writeMemory 63 00 00 00 to &A at the A3 stop, continue: prints 198",
        ["B9"] = "RaiseException(0xE0504153): stopped reason exception, continue -> OS delivers it",
        ["I1"] = "(info) initCommands step-avoid-regexp accepted (no error on the console)",
        ["I2"] = "(info) setFunctionBreakpoints by linkage name hello.Twice (raise-hook style)",
        ["I3"] = "(info) stepIn landing on line 4 followed by next reaches line 5",
        ["I5"] = "(info) unhandled access violation: process ends by itself after continue",
        ["I4"] ="(info) handled non-continuable RaiseException under the adapter: except handler runs (exit 42)",
    };

    private static int Main(string[] args)
    {
        for (int i = 0; i + 1 < args.Length; i += 2)
        {
            switch (args[i])
            {
                case "--dap": _adapter = args[i + 1]; break;
                case "--python": _python = args[i + 1]; break;
                case "--src": _src = args[i + 1]; break;
                case "--out": _out = args[i + 1]; break;
            }
        }
        if (_adapter.Length == 0 || _python.Length == 0 || _src.Length == 0 || _out.Length == 0)
        {
            Console.Error.WriteLine("usage: SpikeRunner --dap <lldb-dap.exe> --python <dir> --src <spike dir> --out <build dir>");
            return 2;
        }

        // Part A on both containers (CodeView with the native PDB reader, as the product starts it: DBG-005),
        // plus CodeView without that variable for information. Part B on both containers.
        foreach (var (col, k, native) in new[] { ("cv", "cv", true), ("dw", "dw", true), ("cv-noenv", "cv", false) })
        {
            Results[col] = [];
            Guard(col, () => PartA(col, k, native));
        }
        foreach (var k in new[] { "cv", "dw" })
        {
            Guard(k, () => StepIn(k));
            Guard(k, () => FunctionBreakpoint(k, "Twice", "B7"));
            Guard(k, () => FunctionBreakpoint(k, "hello.Twice", "I2"));
            Guard(k, () => WriteMemory(k));
            Guard(k, () => Loop(k));
            Guard(k, () => OsException(k));
        }
        Guard("cv", HandledRaise);
        Guard("cv", AccessViolation);

        bool Gate(string col) => PartASteps.Append("B2").All(s => Results[col].TryGetValue(s, out var r) && r.Pass);
        string? chosen = Gate("cv") ? "cv" : Gate("dw") ? "dw" : null;

        Console.WriteLine();
        Console.WriteLine("Part A (gate; B2 is also a gate step)       CodeView  DWARF  CodeView-no-native-PDB-env");
        foreach (var s in PartASteps.Append("B2")) PrintRow(s, "cv", "dw", "cv-noenv");
        Console.WriteLine();
        Console.WriteLine("Part B (design assumptions)                 CodeView  DWARF");
        foreach (var s in PartBSteps.Concat(InfoSteps)) PrintRow(s, "cv", "dw");
        Console.WriteLine();
        Console.WriteLine("Evidence:");
        foreach (var s in PartASteps.Concat(PartBSteps).Concat(InfoSteps))
        {
            foreach (var col in new[] { "cv", "dw", "cv-noenv" })
            {
                if (Results[col].TryGetValue(s, out var r)) Console.WriteLine($"  {s} [{col}] {(r.Pass ? "PASS" : "FAIL")}: {r.Evidence}");
            }
        }
        Console.WriteLine();
        Console.WriteLine($"Gate CodeView: {(Gate("cv") ? "PASS" : "FAIL")}   Gate DWARF: {(Gate("dw") ? "PASS" : "FAIL")}");
        Console.WriteLine(chosen == null
            ? "DECISION: no container passed part A -> CLI fallback (architecture.md §10)"
            : $"DECISION: DebugContainer = {(chosen == "cv" ? "CodeView" : "Dwarf")}");
        Console.WriteLine($"DAP transcripts: {_out}\\*.dap.log (kept with -Keep or on failure)");
        return chosen == null ? 1 : 0;
    }

    private static void PrintRow(string step, params string[] cols)
    {
        var cells = cols.Select(c => Results[c].TryGetValue(step, out var r) ? (r.Pass ? "PASS" : "FAIL") : "-");
        Console.WriteLine($"  {step,-3} {Trunc(StepText[step], 38),-38}  " + string.Join("  ", cells.Select((c, i) => i == 0 ? $"{c,-8}" : $"{c,-5}")));
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";

    private static void Guard(string col, Action a)
    {
        try { a(); }
        catch (Exception ex) { Console.WriteLine($"[{col}] runner exception: {ex.GetType().Name}: {ex.Message}"); }
    }

    private static void Set(string col, string step, bool pass, string evidence)
    {
        Results[col][step] = new Row(pass, evidence);
        Console.WriteLine($"[{col}] {step} {(pass ? "PASS" : "FAIL")}  {Trunc(evidence, 220)}");
    }

    private static void NotReached(string col, string because, params string[] steps)
    {
        foreach (var s in steps) if (!Results[col].ContainsKey(s)) Set(col, s, false, "not reached: " + because);
    }

    // ------------------------------------------------------------------ messages (dap-usage.md §2, §3.1)

    private static JsonObject InitializeArgs() => new()
    {
        ["clientID"] = "freexcomp", ["clientName"] = "FreexComp", ["adapterID"] = "lldb-dap", ["locale"] = "en-US",
        ["linesStartAt1"] = true, ["columnsStartAt1"] = true, ["pathFormat"] = "path",
        ["supportsVariableType"] = true, ["supportsVariablePaging"] = false, ["supportsRunInTerminalRequest"] = false,
        ["supportsMemoryReferences"] = true, ["supportsProgressReporting"] = false, ["supportsInvalidatedEvent"] = false,
        ["supportsMemoryEvent"] = false, ["supportsArgsCanBeInterpretedByShell"] = false,
        ["supportsStartDebuggingRequest"] = false, ["supportsANSIStyling"] = false,
    };

    private static JsonObject LaunchArgs(string exe) => new()
    {
        ["program"] = exe,
        ["cwd"] = Path.GetDirectoryName(exe),
        ["stopOnEntry"] = false,
        ["enableAutoVariableSummaries"] = false,
        ["enableSyntheticChildDebugging"] = false,
        ["displayExtendedBacktrace"] = false,
        ["commandEscapePrefix"] = "`",
        ["initCommands"] = new JsonArray("settings set target.process.thread.step-avoid-regexp \"\""),
        ["sourceMap"] = new JsonArray(),
    };

    private static JsonObject SourceBreakpoints(string file, params int[] lines) => new()
    {
        ["source"] = new JsonObject { ["name"] = file, ["path"] = Path.Combine(_src, file) },
        ["breakpoints"] = new JsonArray([.. lines.Select(l => (JsonNode)new JsonObject { ["line"] = l })]),
        ["sourceModified"] = false,
    };

    private static string Exe(string program, string k) => Path.Combine(_out, $"{program}_{k}.exe");

    private static DapClient Start(string name, bool nativePdb) =>
        new(_adapter, _python, nativePdb, Path.Combine(_out, name + ".dap.log"));

    private static (bool Ok, string Evidence) Initialize(DapClient c)
    {
        var r = c.Request("initialize", InitializeArgs(), 10_000);
        bool ok = r != null && Dap.Bool(r, "success") && Dap.Bool(r, "body.supportsConfigurationDoneRequest");
        string caps = r == null ? "no response within 10 s" : string.Join(",",
            new[] { "supportsConfigurationDoneRequest", "supportsFunctionBreakpoints", "supportsReadMemoryRequest", "supportsWriteMemoryRequest",
                    "supportsSetVariable", "supportTerminateDebuggee", "supportsModulesRequest" }
            .Select(n => $"{n}={Dap.Bool(r, "body." + n).ToString().ToLowerInvariant()}"));
        return (ok, $"success={Dap.Bool(r, "success")}; {caps}");
    }

    /// <summary>Launch sequence of dap-usage.md §3: launch, await initialized, configure, setExceptionBreakpoints,
    /// configurationDone; the launch response is accepted before or after configurationDone (DBG-053).</summary>
    private static (bool Ok, string Evidence) Launch(DapClient c, string exe, Action configure)
    {
        int launchSeq = c.Send("launch", LaunchArgs(exe));
        var initialized = c.WaitEvent("initialized", 10_000);
        if (initialized == null)
        {
            var early = c.WaitResponse(launchSeq, 0);
            return (false, "no initialized event within 10 s; launch response: " + Dap.Raw(early));
        }
        configure();
        var ex = c.Request("setExceptionBreakpoints", new JsonObject { ["filters"] = new JsonArray() });
        var cd = c.Request("configurationDone");
        var lr = c.WaitResponse(launchSeq, 30_000);
        bool ok = Dap.Bool(cd, "success") && Dap.Bool(lr, "success");
        return (ok, $"setExceptionBreakpoints success={Dap.Bool(ex, "success")}, configurationDone success={Dap.Bool(cd, "success")}, " +
                    $"launch success={Dap.Bool(lr, "success")} {Dap.Str(lr, "message")}");
    }

    private static Stop? WaitStop(DapClient c, int ms)
    {
        var e = c.WaitEvent("stopped", ms);
        return e is JsonElement s ? new Stop(Dap.Str(s, "body.reason"), Dap.Int(s, "body.threadId"), s) : null;
    }

    private static Frame? TopFrame(DapClient c, int threadId)
    {
        var r = c.Request("stackTrace", new JsonObject { ["threadId"] = threadId, ["startFrame"] = 0, ["levels"] = 20 });
        var f = Dap.Arr(r, "body.stackFrames").FirstOrDefault();
        if (f.ValueKind != JsonValueKind.Object) return null;
        return new Frame(Dap.Long(f, "id") ?? 0, Dap.Str(f, "name") ?? "", Dap.Str(f, "source.path"), Dap.Int(f, "line") ?? 0, f);
    }

    private static string FrameText(Frame? f) => f == null ? "no frame" : $"frame0 name='{f.Name}' path='{f.Path}' line={f.Line}";

    /// <summary>Locals scope (presentationHint locals, else name Locals) and its variables (DBG-078).</summary>
    private static (Dictionary<string, string> Vars, long Ref, string Evidence) Locals(DapClient c, long frameId)
    {
        var sc = c.Request("scopes", new JsonObject { ["frameId"] = frameId });
        var scopes = Dap.Arr(sc, "body.scopes").ToArray();
        var loc = scopes.FirstOrDefault(s => Dap.Str(s, "presentationHint") == "locals"
                                             || string.Equals(Dap.Str(s, "name"), "Locals", StringComparison.OrdinalIgnoreCase));
        string names = string.Join("|", scopes.Select(s => $"{Dap.Str(s, "name")}({Dap.Str(s, "presentationHint")})"));
        var vars = new Dictionary<string, string>(StringComparer.Ordinal);
        if (loc.ValueKind != JsonValueKind.Object) return (vars, 0, $"scopes [{names}], no locals scope");
        long vref = Dap.Long(loc, "variablesReference") ?? 0;
        var vr = c.Request("variables", new JsonObject { ["variablesReference"] = vref });
        var list = Dap.Arr(vr, "body.variables").ToArray();
        foreach (var v in list) vars[Dap.Str(v, "name") ?? ""] = Dap.Str(v, "value") ?? "";
        string shown = string.Join(", ", list.Select(v => $"{Dap.Str(v, "name")}={Dap.Str(v, "value")} ({Dap.Str(v, "type")}, memRef={Dap.Str(v, "memoryReference") ?? "-"})"));
        return (vars, vref, $"scopes [{names}]; locals: {shown}");
    }

    /// <summary>DBG-082: address from memoryReference, else the first 0x token of result; normalised per DBG-086.</summary>
    private static string? AddressOf(DapClient c, long frameId, string name, out string evidence)
    {
        var r = c.Request("evaluate", new JsonObject { ["expression"] = "&" + name, ["frameId"] = frameId, ["context"] = "watch" });
        evidence = $"evaluate &{name}: success={Dap.Bool(r, "success")} result='{Dap.Str(r, "body.result")}' type='{Dap.Str(r, "body.type")}' " +
                   $"memoryReference='{Dap.Str(r, "body.memoryReference")}' {Dap.Str(r, "message")}";
        if (!Dap.Bool(r, "success")) return null;
        string? text = Dap.Str(r, "body.memoryReference");
        if (string.IsNullOrEmpty(text)) text = HexToken().Match(Dap.Str(r, "body.result") ?? "").Value;
        if (string.IsNullOrEmpty(text) || !text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return null;
        if (!ulong.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong a)) return null;
        return "0x" + a.ToString("X", CultureInfo.InvariantCulture);
    }

    [GeneratedRegex("0x[0-9A-Fa-f]+")]
    private static partial Regex HexToken();

    private static byte[]? ReadMemory(DapClient c, string address, int count, out string evidence)
    {
        var r = c.Request("readMemory", new JsonObject { ["memoryReference"] = address, ["offset"] = 0, ["count"] = count });
        evidence = $"readMemory {address} count {count}: success={Dap.Bool(r, "success")} address='{Dap.Str(r, "body.address")}' data='{Dap.Str(r, "body.data")}' {Dap.Str(r, "message")}";
        string? data = Dap.Str(r, "body.data");
        if (!Dap.Bool(r, "success") || data == null) return null;
        var bytes = Convert.FromBase64String(data);
        evidence += $" -> {Convert.ToHexString(bytes)}";
        return bytes;
    }

    /// <summary>Waits for exited/terminated; returns the exit code (null if no exited event) and evidence.</summary>
    private static (bool Ended, long? ExitCode, string Evidence) WaitEnd(DapClient c, int ms)
    {
        var first = c.WaitAnyEvent(["exited", "terminated"], ms);
        if (first == null) return (false, null, $"no exited/terminated within {ms} ms");
        long? code = Dap.Str(first, "event") == "exited" ? Dap.Long(first, "body.exitCode") : null;
        if (Dap.Str(first, "event") == "exited") c.WaitEvent("terminated", 5_000);
        else if (c.WaitEvent("exited", 0) is JsonElement late) code = Dap.Long(late, "body.exitCode");
        return (true, code, $"first end event '{Dap.Str(first, "event")}', exitCode={code?.ToString(CultureInfo.InvariantCulture) ?? "none"}");
    }

    private static void Disconnect(DapClient c, bool terminate)
    {
        var args = terminate ? new JsonObject { ["terminateDebuggee"] = true } : new JsonObject();
        c.Request("disconnect", args, 3_000);
    }

    // ------------------------------------------------------------------ Part A (+ B1, B2, B4 observed in the same session)

    private static void PartA(string col, string k, bool nativePdb)
    {
        using var c = Start($"A_{col}", nativePdb);
        var (initOk, initEv) = Initialize(c);
        Set(col, "A1", initOk, initEv);
        if (!initOk) { NotReached(col, "A1 failed", [.. PartASteps, "B1", "B2"]); return; }

        JsonElement? bpResp = null;
        var (launchOk, launchEv) = Launch(c, Exe("hello", k), () => bpResp = c.Request("setBreakpoints", SourceBreakpoints("hello.pas", 12)));
        var bp = Dap.Get(bpResp, "body.breakpoints.0");
        bool verified = Dap.Bool(bp, "verified") && Dap.Int(bp, "line") == 12;
        var stop = WaitStop(c, 15_000);
        string a2 = $"{launchEv}; setBreakpoints -> {Dap.Raw(bp)}";
        if (!verified && Dap.Int(bp, "id") is int id)
        {
            var later = c.EventsSnapshot().LastOrDefault(e => Dap.Str(e, "event") == "breakpoint" && Dap.Int(e, "body.breakpoint.id") == id);
            verified = Dap.Bool(later, "body.breakpoint.verified") && Dap.Int(later, "body.breakpoint.line") == 12;
            a2 += $"; later breakpoint event -> {Dap.Raw(later, "body.breakpoint")}";
        }
        Set(col, "A2", launchOk && verified, a2);
        Set(col, "A3", stop?.Reason == "breakpoint" && stop.ThreadId != null, stop == null ? "no stopped event within 15 s" : Dap.Raw(stop.Raw, "body"));
        if (stop?.ThreadId is not int tid) { NotReached(col, "no stop", "A4", "A5", "A6", "A7", "A8", "B1", "B2"); return; }

        var f = TopFrame(c, tid);
        Set(col, "A4", f != null && (f.Path ?? "").EndsWith("hello.pas", StringComparison.OrdinalIgnoreCase) && f.Line == 12, FrameText(f));
        if (f == null) { NotReached(col, "no frame", "A5", "A6", "A7", "A8", "B1", "B2"); return; }

        var (vars, _, lev) = Locals(c, f.Id);
        Set(col, "A5", vars.ContainsKey("A") && vars.ContainsKey("B") && vars["A"] == "21", lev);

        string? addr = AddressOf(c, f.Id, "A", out var b1);
        Set(col, "B1", addr != null, b1 + $" -> address {addr ?? "none"}");
        if (addr != null)
        {
            var bytes = ReadMemory(c, addr, 4, out var b2);
            Set(col, "B2", bytes != null && bytes.SequenceEqual(new byte[] { 0x15, 0, 0, 0 }), b2);
        }
        else NotReached(col, "B1 gave no address", "B2");

        c.Request("next", new JsonObject { ["threadId"] = tid });
        var s2 = WaitStop(c, 10_000);
        var f2 = s2?.ThreadId is int t2 ? TopFrame(c, t2) : null;
        Set(col, "A6", s2?.Reason == "step" && f2?.Line == 13, $"stopped reason={s2?.Reason ?? "none"}; {FrameText(f2)}");

        if (f2 != null)
        {
            var (vars2, _, lev2) = Locals(c, f2.Id);
            Set(col, "A7", vars2.TryGetValue("B", out var bv) && bv == "42", lev2);
        }
        else NotReached(col, "A6 gave no frame", "A7");

        var cont = c.Request("continue", new JsonObject { ["threadId"] = s2?.ThreadId ?? tid });
        var (ended, code, endEv) = WaitEnd(c, 15_000);
        var disc = c.Request("disconnect", new JsonObject(), 5_000);
        Set(col, "A8", Dap.Bool(cont, "success") && ended && (code == null || code == 0) && disc != null,
            $"continue success={Dap.Bool(cont, "success")}; {endEv}; disconnect answered={disc != null} success={Dap.Bool(disc, "success")}");

        string stdout = c.OutputText("stdout");
        Set(col, "B4", stdout.Contains("42", StringComparison.Ordinal),
            $"output categories seen [{string.Join(",", c.OutputCategories())}]; stdout text '{Escape(stdout)}'");
        string console = c.OutputText("console");
        Set(col, "I1", !console.Contains("error", StringComparison.OrdinalIgnoreCase), $"console output '{Trunc(Escape(console), 400)}'");
    }

    private static string Escape(string s) => s.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

    // ------------------------------------------------------------------ Part B sessions

    /// <summary>Starts a session on hello stopped at the line-12 breakpoint.</summary>
    private static (DapClient C, int Tid, Frame F)? StopAtLine12(string name, string k)
    {
        var c = Start(name, true);
        if (!Initialize(c).Ok) { Disconnect(c, true); c.Dispose(); return null; }
        Launch(c, Exe("hello", k), () => c.Request("setBreakpoints", SourceBreakpoints("hello.pas", 12)));
        var stop = WaitStop(c, 15_000);
        if (stop?.ThreadId is int tid && TopFrame(c, tid) is Frame f && f.Line == 12) return (c, tid, f);
        Disconnect(c, true);
        c.Dispose();
        return null;
    }

    private static void StepIn(string k)
    {
        var s = StopAtLine12($"B3_{k}", k);
        if (s == null) { NotReached(k, "no stop at line 12", "B3", "I3"); return; }
        var (c, tid, _) = s.Value;
        using (c)
        {
            c.Request("stepIn", new JsonObject { ["threadId"] = tid });
            var st = WaitStop(c, 10_000);
            var f = st?.ThreadId is int t ? TopFrame(c, t) : null;
            bool inTwice = f != null && f.Name.Contains("Twice", StringComparison.Ordinal);
            Set(k, "B3", inTwice && f!.Line == 5, $"stopped reason={st?.Reason ?? "none"}; {FrameText(f)}");
            if (inTwice && f!.Line == 4)
            {
                c.Request("next", new JsonObject { ["threadId"] = tid });
                var st2 = WaitStop(c, 10_000);
                var f2 = st2?.ThreadId is int t2 ? TopFrame(c, t2) : null;
                Set(k, "I3", f2?.Line == 5, $"after next: reason={st2?.Reason ?? "none"}; {FrameText(f2)}");
            }
            else Set(k, "I3", true, "not needed (stepIn did not land on line 4)");
            Disconnect(c, true);
        }
    }

    private static void FunctionBreakpoint(string k, string name, string step)
    {
        using var c = Start($"{step}_{k}", true);
        if (!Initialize(c).Ok) { NotReached(k, "initialize failed", step); return; }
        JsonElement? fb = null;
        var (ok, lev) = Launch(c, Exe("hello", k), () =>
            fb = c.Request("setFunctionBreakpoints", new JsonObject { ["breakpoints"] = new JsonArray(new JsonObject { ["name"] = name }) }));
        var st = WaitStop(c, 15_000);
        var f = st?.ThreadId is int t ? TopFrame(c, t) : null;
        Set(k, step, ok && st != null && f != null && f.Name.Contains("Twice", StringComparison.Ordinal),
            $"setFunctionBreakpoints '{name}' -> {Dap.Raw(fb, "body.breakpoints")}; stopped {(st == null ? "none" : Dap.Raw(st.Raw, "body"))}; {FrameText(f)}");
        Disconnect(c, true);
    }

    private static void WriteMemory(string k)
    {
        var s = StopAtLine12($"B8_{k}", k);
        if (s == null) { NotReached(k, "no stop at line 12", "B8"); return; }
        var (c, tid, f) = s.Value;
        using (c)
        {
            string? addr = AddressOf(c, f.Id, "A", out var aev);
            if (addr == null) { Set(k, "B8", false, aev); Disconnect(c, true); return; }
            var w = c.Request("writeMemory", new JsonObject
            {
                ["memoryReference"] = addr, ["offset"] = 0, ["allowPartial"] = false, ["data"] = Convert.ToBase64String([0x63, 0, 0, 0]),
            });
            bool written = Dap.Bool(w, "success") && (Dap.Int(w, "body.bytesWritten") ?? 4) == 4;
            string ev = $"writeMemory {addr} 'YwAAAA==': success={Dap.Bool(w, "success")} body={Dap.Raw(w, "body")} {Dap.Str(w, "message")}";
            if (!written)
            {
                var (_, vref, _) = Locals(c, f.Id);
                var sv = c.Request("setVariable", new JsonObject { ["variablesReference"] = vref, ["name"] = "A", ["value"] = "99" });
                ev += $"; fallback setVariable A=99: success={Dap.Bool(sv, "success")} {Dap.Raw(sv, "body")} {Dap.Str(sv, "message")}";
            }
            var back = ReadMemory(c, addr, 4, out var rev);
            ev += "; re-read: " + rev;
            c.Request("continue", new JsonObject { ["threadId"] = tid });
            var (ended, _, endEv) = WaitEnd(c, 15_000);
            string stdout = c.OutputText("stdout");
            Set(k, "B8", written && ended && stdout.Contains("198", StringComparison.Ordinal), $"{ev}; {endEv}; stdout '{Escape(stdout)}'");
            Disconnect(c, false);
        }
    }

    private static void Loop(string k)
    {
        using var c = Start($"B5B6_{k}", true);
        if (!Initialize(c).Ok) { NotReached(k, "initialize failed", "B5", "B6"); return; }
        var (ok, lev) = Launch(c, Exe("loop", k), () => { });
        var proc = c.WaitEvent("process", 5_000);
        var th = c.Request("threads");
        int? tid = Dap.Int(th, "body.threads.0.id");
        if (!ok || tid == null)
        {
            NotReached(k, $"launch ok={ok} ({lev}); threads {Dap.Raw(th)}", "B5", "B6");
            Disconnect(c, true);
            return;
        }
        c.Request("pause", new JsonObject { ["threadId"] = tid });
        var st = WaitStop(c, 5_000);
        Set(k, "B5", st != null, $"process event pid={Dap.Int(proc, "body.systemProcessId")}; threads {Dap.Raw(th, "body.threads", 120)}; stopped {(st == null ? "none within 5 s" : Dap.Raw(st.Raw, "body"))}");

        if (st != null)
        {
            var cr = c.Request("continue", new JsonObject { ["threadId"] = st.ThreadId ?? tid });
            if (!Dap.Bool(cr, "success")) { Set(k, "B6", false, "continue failed: " + Dap.Raw(cr)); Disconnect(c, true); return; }
        }
        // The debuggee is running now (either never stopped or continued).
        var bp = c.Request("setBreakpoints", SourceBreakpoints("loop.pas", 8));
        var st2 = WaitStop(c, 10_000);
        var f = st2?.ThreadId is int t ? TopFrame(c, t) : null;
        Set(k, "B6", st2?.Reason == "breakpoint" && f?.Line == 8,
            $"setBreakpoints while running -> success={Dap.Bool(bp, "success")} {Dap.Raw(bp, "body.breakpoints")}; stopped reason={st2?.Reason ?? "none within 10 s"}; {FrameText(f)}");
        Disconnect(c, true);
    }

    /// <summary>I4: Pascal-style raise (non-continuable RaiseException caught by an SEH filter) under the adapter.</summary>
    private static void HandledRaise()
    {
        string exe = Path.Combine(_out, "sehcatch.exe");
        string control;
        using (var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true })!)
        {
            if (!p.WaitForExit(10_000)) { p.Kill(true); control = "control run (no debugger): did not end within 10 s"; }
            else control = $"control run (no debugger): exit code {p.ExitCode}";
        }
        using var c = Start("I4", true);
        if (!Initialize(c).Ok) { Set("cv", "I4", false, control + "; initialize failed"); return; }
        Launch(c, exe, () => { });
        string ev = control;
        long? code = null;
        for (int i = 0; i < 5; i++)
        {
            var next = c.WaitAnyEvent(["stopped", "exited", "terminated"], 15_000);
            if (next == null) { ev += "; nothing within 15 s"; break; }
            string name = Dap.Str(next, "event")!;
            if (name == "stopped")
            {
                ev += $"; stop #{i + 1} {Dap.Raw(next, "body")} -> continue";
                c.Request("continue", new JsonObject { ["threadId"] = Dap.Int(next, "body.threadId") ?? 0 });
                continue;
            }
            code = name == "exited" ? Dap.Long(next, "body.exitCode") : (c.WaitEvent("exited", 0) is JsonElement x ? Dap.Long(x, "body.exitCode") : null);
            ev += $"; '{name}' exitCode={code?.ToString(CultureInfo.InvariantCulture) ?? "none"}" + (code is long v ? $" (0x{unchecked((uint)v):X8})" : "");
            break;
        }
        Set("cv", "I4", code == 42, ev + " (42 = handler ran; 1 = RaiseException returned)");
        Disconnect(c, true);
    }

    /// <summary>I5: unhandled access violation; what does continue do after the exception stop? (3 continues at most)</summary>
    private static void AccessViolation()
    {
        using var c = Start("I5", true);
        if (!Initialize(c).Ok) { Set("cv", "I5", false, "initialize failed"); return; }
        Launch(c, Path.Combine(_out, "av.exe"), () => { });
        string ev = "";
        int stops = 0;
        string end = "still stopped after 3 continues";
        for (int i = 0; i < 4; i++)
        {
            var next = c.WaitAnyEvent(["stopped", "exited", "terminated"], 15_000);
            if (next == null) { end = "nothing within 15 s"; break; }
            string name = Dap.Str(next, "event")!;
            if (name != "stopped")
            {
                long? code = name == "exited" ? Dap.Long(next, "body.exitCode") : null;
                end = $"'{name}' exitCode={code?.ToString(CultureInfo.InvariantCulture) ?? "none"}" + (code is long v ? $" (0x{unchecked((uint)v):X8})" : "");
                break;
            }
            stops++;
            if (i == 0) ev = $"stop #1 {Dap.Raw(next, "body")}";
            if (i == 3) break;
            c.Request("continue", new JsonObject { ["threadId"] = Dap.Int(next, "body.threadId") ?? 0 });
        }
        // Informational only: records the behaviour; "pass" = the process ends by itself after continue.
        Set("cv", "I5", !end.StartsWith("still", StringComparison.Ordinal), $"{ev}; exception stops seen={stops}; end: {end}");
        Disconnect(c, true);
    }

    private static void OsException(string k)
    {
        using var c = Start($"B9_{k}", true);
        if (!Initialize(c).Ok) { NotReached(k, "initialize failed", "B9"); return; }
        Launch(c, Exe("osexc", k), () => { });
        var st = WaitStop(c, 15_000);
        var f = st?.ThreadId is int t ? TopFrame(c, t) : null;
        string ev = $"first stop {(st == null ? "none within 15 s" : Dap.Raw(st.Raw, "body"))}; {FrameText(f)}";
        bool firstChance = st?.Reason == "exception";
        bool delivered = false;
        // continue; a second-chance stop (if any) is continued too, at most three times, then the process must end.
        for (int i = 0; i < 3 && st != null; i++)
        {
            c.Request("continue", new JsonObject { ["threadId"] = st.ThreadId ?? 0 });
            var next = c.WaitAnyEvent(["stopped", "exited", "terminated"], 15_000);
            if (next == null) { ev += "; nothing within 15 s after continue"; break; }
            string name = Dap.Str(next, "event")!;
            if (name == "stopped")
            {
                st = new Stop(Dap.Str(next, "body.reason"), Dap.Int(next, "body.threadId"), next.Value);
                ev += $"; continue #{i + 1} -> stopped again {Dap.Raw(next, "body")}";
                continue;
            }
            long? code = name == "exited" ? Dap.Long(next, "body.exitCode") : (c.WaitEvent("exited", 0) is JsonElement x ? Dap.Long(x, "body.exitCode") : null);
            delivered = code is long v && unchecked((uint)v) == 0xE0504153u;
            ev += $"; continue #{i + 1} -> '{name}' exitCode={code?.ToString(CultureInfo.InvariantCulture) ?? "none"}" +
                  (code is long v2 ? $" (0x{unchecked((uint)v2):X8})" : "");
            break;
        }
        Set(k, "B9", firstChance && delivered, ev);
        Disconnect(c, true);
    }
}
