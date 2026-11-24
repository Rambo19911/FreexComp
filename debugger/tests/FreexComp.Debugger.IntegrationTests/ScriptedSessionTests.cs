using System.Diagnostics;
using System.Globalization;
using FreexComp.Debugger.IntegrationTests.TestSupport;

namespace FreexComp.Debugger.IntegrationTests;

/// <summary>
/// The scripted debug sessions of the debugger's scripted-test document: the complete session on the demo program
/// (the exit check of the debugger phase) and its companions. Real compiler, real debug adapter, real debuggee.
/// Every wait is on an event; time limits are safety nets only.
/// </summary>
[Collection(RealAdapterCollection.Name)]
[Trait("Kind", "Debug")]
[Trait("Category", "Integration")]
[Trait("Category", "Toolchain")]
public sealed class ScriptedSessionTests(IntegrationFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static void AssertStop(IDebugSession session, StopInfo stop, StopReason reason, string path, int line, int[]? hitBreakpointIds = null)
    {
        Assert.Equal(reason, stop.Reason);
        Assert.NotNull(stop.Location);
        Assert.Equal(path, stop.Location.Path, ignoreCase: true);
        Assert.Equal(line, stop.Location.Line);
        if (hitBreakpointIds is not null)
            Assert.Equal(hitBreakpointIds.Order(), stop.HitBreakpointIds.Order());
        Assert.Equal(DebugState.Stopped, session.State);
    }

    private static string Row(VariableInfo v) => $"{v.Name} = {v.Value} : {v.TypeName}";

    private static string Frame(StackFrameInfo frame) => $"{frame.Name}:{frame.Location?.Line}";

    private static bool IsAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    [Fact]
    [Trait("Req", "DBG-300")]
    [Trait("Req", "DBG-310")]
    [Trait("Req", "DBG-311")]
    [Trait("Req", "DBG-312")]
    [Trait("Req", "DBG-313")]
    [Trait("Req", "DBG-314")]
    [Trait("Req", "DBG-315")]
    [Trait("Req", "DBG-316")]
    [Trait("Req", "DBG-317")]
    [Trait("Req", "DBG-318")]
    [Trait("Req", "DBG-319")]
    [Trait("Req", "DBG-320")]
    [Trait("Req", "DBG-321")]
    public async Task Phase6_ScriptedSession_DbgDemo()
    {
        var program = fixture.Build("DbgDemo");
        Assert.True(File.Exists(program.TypeInfoPath), "The compiler wrote no type-information file: " + program.TypeInfoPath);
        var p = program.SourcePath;
        var recorder = new SessionRecorder(nameof(Phase6_ScriptedSession_DbgDemo));
        int? debuggee = null;
        await recorder.RunAsync(fixture, program, async session =>
        {
            async Task<IReadOnlyList<StackFrameInfo>> StackAsync(StopInfo at) =>
                (await session.GetStackTraceAsync(at.ThreadId, ct: Ct)).Frames;
            async Task<VariableInfo> WatchAsync(string expression, int frameId) =>
                (await session.EvaluateAsync(expression, frameId, EvaluateContext.Watch, Ct)).Value;

            // ---- Step 1: launch, stop at a plain breakpoint.
            await session.LaunchAsync(
                new LaunchRequest
                {
                    Program = program.ExePath,
                    StopOnEntry = false,
                    Breakpoints =
                    [
                        new SourceBreakpoints(p,
                        [
                            new BreakpointSpec { Id = 1, Line = 69 },
                            new BreakpointSpec { Id = 2, Line = 40, Condition = "N = 2" },
                        ]),
                    ],
                },
                Ct);
            Assert.True(session.HasTypeInfo);
            var stop = await recorder.WaitForStopAsync();
            AssertStop(session, stop, StopReason.Breakpoint, p, 69, [1]);
            Assert.Equal(1, stop.Epoch);
            Assert.Equal(1, session.CurrentStop?.Epoch);
            debuggee = session.ProcessId;
            Assert.NotNull(debuggee);

            var launchEvents = recorder.Events
                .Where(e => e.StartsWith("StateChanged", StringComparison.Ordinal) || e.StartsWith("Stopped", StringComparison.Ordinal)).ToList();
            Assert.Equal("StateChanged(Idle→Starting)", launchEvents[0]);
            Assert.True(
                launchEvents is [_, "StateChanged(Starting→Stopped)", _] or [_, "StateChanged(Starting→Running)", "StateChanged(Running→Stopped)", _],
                string.Join(" | ", launchEvents));
            Assert.StartsWith("Stopped(Breakpoint", launchEvents[^1], StringComparison.Ordinal);
            foreach (var (id, line) in ((int, int)[])[(1, 69), (2, 40)])
            {
                var status = recorder.LastStatus(id);
                Assert.NotNull(status);
                Assert.Equal(BreakpointState.Verified, status.State);
                Assert.Equal(line, status.ActualLine);
            }

            // ---- Step 2: threads and call stack.
            var threads = await session.GetThreadsAsync(Ct);
            Assert.NotEmpty(threads);
            Assert.Equal(stop.ThreadId, Assert.Single(threads, t => t.IsCurrent).Id);
            var frames = await StackAsync(stop);
            Assert.True(frames.Count >= 2, "frames: " + string.Join(", ", frames.Select(Frame)));
            Assert.Equal("Run", frames[0].Name);
            Assert.Equal(0, frames[0].Index);
            Assert.Equal(p, frames[0].Location?.Path, ignoreCase: true);
            Assert.Equal(69, frames[0].Location?.Line);
            Assert.True(frames[0].IsUserCode);
            Assert.Equal("DbgDemo", frames[1].Name);
            Assert.Equal(77, frames[1].Location?.Line);
            Assert.True(frames[1].IsUserCode);
            Assert.All(frames.Skip(2), frame => Assert.False(frame.IsUserCode, frame.Name));
            Assert.Equal("DbgDemo.exe", frames[0].Module, ignoreCase: true);
            var firstFrameId = frames[0].Id;

            // ---- Step 3: scopes and locals with Pascal formatting.
            var scopes = await session.GetScopesAsync(frames[0].Id, Ct);
            Assert.Equal(["Arguments", "Locals", "Globals"], scopes.Select(s => s.Name));
            Assert.Equal([ScopeKind.Arguments, ScopeKind.Locals, ScopeKind.Globals], scopes.Select(s => s.Kind));
            Assert.Empty(await session.GetChildrenAsync(scopes[0].Handle, ct: Ct));
            Assert.Empty(await session.GetChildrenAsync(scopes[2].Handle, ct: Ct));
            var localsHandle = scopes[1].Handle;
            var locals = await session.GetChildrenAsync(localsHandle, ct: Ct);

            // Name, value (text, or a pattern for the whole text), type, kind, number of children - in declaration order.
            (string Name, string Value, bool IsPattern, string Type, ValueKind Kind, int Children)[] expected =
            [
                ("I", @"-?\d+", true, "Integer", ValueKind.Integer, 0),      // loop variable after the loop: value not fixed
                ("Total", "0", false, "Integer", ValueKind.Integer, 0),
                ("Ch", "'A'", false, "Char", ValueKind.Char, 0),
                ("Ok", "True", false, "Boolean", ValueKind.Boolean, 0),
                ("Color", "clGreen", false, "TColor", ValueKind.Enum, 0),
                ("Colors", "[clRed, clBlue]", false, "TColors", ValueKind.Set, 0),
                ("S", "'Héllo'", false, "string", ValueKind.String, 0),
                ("P", "(X: 3; Y: 4)", false, "TPoint", ValueKind.Record, 2),
                ("Nums", "(10, 20, 30)", false, "array of Integer", ValueKind.DynamicArray, 3),
                ("Pet", @"TDog\(\$[0-9A-F]{16}\)", true, "TDog", ValueKind.ClassInstance, 2),
                ("PTotal", @"\$[0-9A-F]{16}", true, "^Integer", ValueKind.Pointer, 1),
            ];
            Assert.Equal(expected.Select(e => e.Name), locals.Select(l => l.Name));
            foreach (var (row, local) in expected.Zip(locals))
            {
                if (row.IsPattern)
                    Assert.Matches("^(?:" + row.Value + ")$", local.Value);
                else
                    Assert.Equal(row.Value, local.Value);
                Assert.Equal(row.Type, local.TypeName);
                Assert.Equal(row.Kind, local.Kind);
                Assert.Equal(row.Children, local.ChildCount);
                Assert.Equal(row.Children > 0, local.Children is not null);
            }

            VariableInfo Local(string name) => locals.Single(l => l.Name == name);
            async Task<IReadOnlyList<VariableInfo>> ChildrenAsync(string name) =>
                await session.GetChildrenAsync(Local(name).Children!.Value, ct: Ct);

            Assert.Equal(["X = 3 : Integer", "Y = 4 : Integer"], (await ChildrenAsync("P")).Select(Row));
            var nums = await ChildrenAsync("Nums");
            Assert.Equal(["[0] = 10 : Integer", "[1] = 20 : Integer", "[2] = 30 : Integer"], nums.Select(Row));
            var pet = await ChildrenAsync("Pet");
            Assert.Equal(["Name = 'Rex' : string", "Legs = 4 : Integer"], pet.Select(Row));
            var target = await ChildrenAsync("PTotal");
            Assert.Equal(["^ = 0 : Integer"], target.Select(Row));

            Assert.True(Local("Total").CanModify);
            Assert.False(Local("S").CanModify);
            Assert.Equal("Total", Local("Total").EvaluateName);
            Assert.Equal("Nums[1]", nums[1].EvaluateName);
            Assert.Equal("Pet.Name", pet[0].EvaluateName);
            Assert.Equal("PTotal^", target[0].EvaluateName);
            Assert.Equal(
                Local("Total").Address,
                ulong.Parse(Local("PTotal").Value.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));

            // ---- Step 4: step into.
            await session.StepIntoAsync(stop.ThreadId, Ct);
            stop = await recorder.WaitForStopAsync();
            AssertStop(session, stop, StopReason.Step, p, 37);
            Assert.Equal(2, stop.Epoch);
            frames = await StackAsync(stop);
            Assert.Equal(["Factorial:37", "Run:69", "DbgDemo:77"], frames.Take(3).Select(Frame));
            scopes = await session.GetScopesAsync(frames[0].Id, Ct);
            Assert.Equal(["N = 4 : Integer"], (await session.GetChildrenAsync(scopes[0].Handle, ct: Ct)).Select(Row));
            Assert.Equal(["Result"], (await session.GetChildrenAsync(scopes[1].Handle, ct: Ct)).Select(v => v.Name));
            var stale = await Assert.ThrowsAsync<DebuggerException>(() => session.GetScopesAsync(firstFrameId, Ct));
            Assert.Equal(DebugErrorCode.StaleReference, stale.Code);
            stale = await Assert.ThrowsAsync<DebuggerException>(() => session.GetChildrenAsync(localsHandle, ct: Ct));
            Assert.Equal(DebugErrorCode.StaleReference, stale.Code);

            // ---- Step 5: continue to a conditional breakpoint.
            recorder.ClearEvents();
            await session.ContinueAsync(Ct);
            stop = await recorder.WaitForStopAsync();
            var sinceContinue = recorder.Events;
            AssertStop(session, stop, StopReason.Breakpoint, p, 40, [2]);
            frames = await StackAsync(stop);
            Assert.Equal(["Factorial:40", "Factorial:40", "Factorial:40", "Run:69", "DbgDemo:77"], frames.Take(5).Select(Frame));
            Assert.Equal("2", (await WatchAsync("N", frames[0].Id)).Value);
            Assert.Equal("3", (await WatchAsync("N", frames[1].Id)).Value);
            Assert.Equal("4", (await WatchAsync("N", frames[2].Id)).Value);
            Assert.Equal("N * 10 + 1 = 21 : Integer", Row((await WatchAsync("N * 10 + 1", frames[0].Id)) with { Name = "N * 10 + 1" }));
            Assert.Equal("N = 2 = True : Boolean", Row((await WatchAsync("N = 2", frames[0].Id)) with { Name = "N = 2" }));
            Assert.Equal(3, recorder.LastStatus(2)?.HitCount);
            string[] ignored = ["Output(", "BreakpointChanged(", "ThreadChanged(", "ModuleLoaded("];
            var visible = sinceContinue.Where(e => !ignored.Any(prefix => e.StartsWith(prefix, StringComparison.Ordinal))).ToList();
            Assert.True(
                visible is ["StateChanged(Stopped→Running)", "Continued", "StateChanged(Running→Stopped)", var last]
                    && last.StartsWith("Stopped(Breakpoint", StringComparison.Ordinal),
                string.Join(" | ", visible));

            // ---- Step 6: step out.
            await session.StepOutAsync(stop.ThreadId, Ct);
            stop = await recorder.WaitForStopAsync();
            AssertStop(session, stop, StopReason.Step, p, 40);
            frames = await StackAsync(stop);
            Assert.Equal(["Factorial", "Factorial", "Run", "DbgDemo"], frames.Take(4).Select(f => f.Name));
            Assert.Equal("3", (await WatchAsync("N", frames[0].Id)).Value);

            // ---- Step 7: change breakpoints while stopped, continue.
            var statuses = await session.SetBreakpointsAsync(
                p, [new BreakpointSpec { Id = 1, Line = 69 }, new BreakpointSpec { Id = 3, Line = 71 }], Ct);
            Assert.Equal(
                [(1, BreakpointState.Verified, 69), (3, BreakpointState.Verified, 71)],
                statuses.Select(s => (s.Id, s.State, s.ActualLine)));
            await session.ContinueAsync(Ct);
            stop = await recorder.WaitForStopAsync();
            AssertStop(session, stop, StopReason.Breakpoint, p, 71, [3]);
            frames = await StackAsync(stop);
            Assert.Equal("Run", frames[0].Name);
            await recorder.WaitForStdoutAsync("Dog Rex\n");
            var run = frames[0].Id;

            // ---- Step 8: watches.
            (string Expression, string Value, string Type)[] watches =
            [
                ("Total", "24", "Integer"),
                ("Total * 2 + 1", "49", "Integer"),
                ("total", "24", "Integer"),
                ("S", "'Héllo'", "string"),
                ("Length(S)", "5", "Integer"),
                ("S[2]", "'é'", "Char"),
                ("Nums[1]", "20", "Integer"),
                ("High(Nums)", "2", "Integer"),
                ("P.X + P.Y", "7", "Integer"),
                ("Ord(Color)", "1", "Integer"),
                ("clRed in Colors", "True", "Boolean"),
                ("clGreen in Colors", "False", "Boolean"),
                ("Pet.Legs + Total", "28", "Integer"),
                ("Pet.Name", "'Rex'", "string"),
                ("PTotal^", "24", "Integer"),
                ("PTotal = @Total", "True", "Boolean"),
                ("Ok and (Ch = 'A')", "True", "Boolean"),
            ];
            foreach (var (expression, value, type) in watches)
            {
                var result = await WatchAsync(expression, run);
                Assert.Equal($"{expression} → {value} : {type}", $"{expression} → {result.Value} : {result.TypeName}");
            }

            (string Expression, DebugErrorCode Code, string Text)[] failing =
            [
                ("Nums[3]", DebugErrorCode.ExpressionEvaluation, "Index 3 out of bounds (0..2)"),
                ("Foo", DebugErrorCode.ExpressionEvaluation, "Undeclared identifier 'Foo'"),
                ("Total +", DebugErrorCode.ExpressionSyntax, "column 8"),
                ("Pet.Describe", DebugErrorCode.ExpressionEvaluation, "Undeclared identifier 'Describe'"),
            ];
            foreach (var (expression, code, text) in failing)
            {
                var error = await Assert.ThrowsAsync<DebuggerException>(() => WatchAsync(expression, run));
                Assert.Equal(code, error.Code);
                Assert.Contains(text, error.Message, StringComparison.Ordinal);
            }

            var totalAddress = (await WatchAsync("Total", run)).Address;
            Assert.NotNull(totalAddress);
            Assert.Equal([0x18, 0x00, 0x00, 0x00], await session.ReadMemoryAsync(totalAddress.Value, 4, Ct));

            // ---- Step 9: modify a variable, step over.
            var changed = await session.SetValueAsync("Total", "99", run, Ct);
            Assert.Equal("99", changed.Value);
            Assert.Equal("99", (await WatchAsync("PTotal^", run)).Value);
            await session.StepOverAsync(stop.ThreadId, Ct);
            stop = await recorder.WaitForStopAsync();
            AssertStop(session, stop, StopReason.Step, p, 72);
            await recorder.WaitForStdoutAsync("Dog Rex\n99\n");

            // ---- Step 10: step over to the routine's end.
            await session.StepOverAsync(stop.ThreadId, Ct);
            stop = await recorder.WaitForStopAsync();
            AssertStop(session, stop, StopReason.Step, p, 73);
            await session.StepOverAsync(stop.ThreadId, Ct);
            stop = await recorder.WaitForStopAsync();
            AssertStop(session, stop, StopReason.Step, p, 74);
            Assert.Equal("Run", (await StackAsync(stop))[0].Name);
            await recorder.WaitForStdoutAsync("Dog Rex\n99\ndone\n");

            // ---- Step 11: run to completion.
            await session.ContinueAsync(Ct);
            var terminated = await recorder.WaitForTerminatedAsync();
            Assert.Equal(TerminationCause.Exited, terminated.Cause);
            Assert.Equal(0, terminated.ExitCode);
            Assert.StartsWith("Terminated(Exited", recorder.Events[^1], StringComparison.Ordinal);
            Assert.Equal(DebugState.Terminated, session.State);
            Assert.Equal(0, session.ExitCode);
            Assert.Null(session.CurrentStop);
            Assert.Equal("Dog Rex\n99\ndone\n", recorder.StdoutSoFar);
            Assert.Equal("", recorder.StderrSoFar);
            var late = await Assert.ThrowsAsync<DebuggerException>(() => session.ContinueAsync(Ct));
            Assert.Equal(DebugErrorCode.InvalidState, late.Code);
        });

        // ---- Step 12: clean-up. The recorder disposed the session and saw the adapter's process tree end.
        Assert.False(IsAlive(debuggee!.Value), "The debuggee is still running.");
    }

    [Fact]
    [Trait("Req", "DBG-322")]
    public async Task Scripted_RunToLine_And_Pause()
    {
        var program = fixture.Build("Loop");
        var recorder = new SessionRecorder(nameof(Scripted_RunToLine_And_Pause));
        int? debuggee = null;
        await recorder.RunAsync(fixture, program, async session =>
        {
            // The program loops for ever: whenever the pause arrives, it is running.
            await session.LaunchAsync(new LaunchRequest { Program = program.ExePath }, Ct);
            debuggee = session.ProcessId;
            await session.PauseAsync(Ct);
            var stop = await recorder.WaitForStopAsync();
            Assert.Equal(StopReason.Pause, stop.Reason);
            Assert.Equal(DebugState.Stopped, session.State);

            await session.RunToLineAsync(program.SourcePath, 9, Ct);
            stop = await recorder.WaitForStopAsync();
            AssertStop(session, stop, StopReason.RunToLine, program.SourcePath, 9);

            await session.TerminateAsync(Ct);
            var terminated = await recorder.WaitForTerminatedAsync();
            Assert.Equal(TerminationCause.TerminatedByUser, terminated.Cause);
            Assert.Equal(DebugState.Terminated, session.State);
        });

        Assert.NotNull(debuggee);
        Assert.False(IsAlive(debuggee.Value), "The debuggee is still running.");
    }

    [Fact]
    [Trait("Req", "DBG-325")]
    public async Task Scripted_Terminate_While_Running()
    {
        var program = fixture.Build("Loop");
        var recorder = new SessionRecorder(nameof(Scripted_Terminate_While_Running));
        int? debuggee = null;
        await recorder.RunAsync(fixture, program, async session =>
        {
            await session.LaunchAsync(new LaunchRequest { Program = program.ExePath }, Ct);
            Assert.Equal(DebugState.Running, session.State);
            debuggee = session.ProcessId;

            await session.TerminateAsync(Ct).WaitAsync(IntegrationFixture.Safety, Ct);
            var terminated = await recorder.WaitForTerminatedAsync();
            Assert.Equal(TerminationCause.TerminatedByUser, terminated.Cause);
            Assert.Equal(DebugState.Terminated, session.State);
            Assert.DoesNotContain(recorder.Events, e => e.StartsWith("Stopped(", StringComparison.Ordinal));
        });

        Assert.NotNull(debuggee);
        Assert.False(IsAlive(debuggee.Value), "The debuggee is still running.");
    }

    [Fact]
    [Trait("Req", "DBG-323")]
    public async Task Scripted_LogPoint_And_HitCount()
    {
        var program = fixture.Build("DbgDemo");
        var p = program.SourcePath;
        var recorder = new SessionRecorder(nameof(Scripted_LogPoint_And_HitCount));
        await recorder.RunAsync(fixture, program, async session =>
        {
            await session.LaunchAsync(
                new LaunchRequest
                {
                    Program = program.ExePath,
                    Breakpoints =
                    [
                        new SourceBreakpoints(p,
                        [
                            new BreakpointSpec { Id = 1, Line = 66, LogMessage = "I={I} value={(I + 1) * 10}" },
                            new BreakpointSpec { Id = 2, Line = 62, HitCount = new HitCountSpec(HitCountMode.Equal, 1) },
                        ]),
                    ],
                },
                Ct);
            var stop = await recorder.WaitForStopAsync();
            AssertStop(session, stop, StopReason.Breakpoint, p, 62, [2]);

            // The log point is passed three times without a stop: the next thing that happens is the end.
            await session.ContinueAsync(Ct);
            var terminated = await recorder.WaitForTerminatedAsync();
            Assert.Equal(TerminationCause.Exited, terminated.Cause);
            Assert.Equal(0, terminated.ExitCode);
            Assert.Equal("I=0 value=10\nI=1 value=20\nI=2 value=30\n", recorder.OutputSoFar(OutputCategory.LogPoint));
            Assert.Single(recorder.Events, e => e.StartsWith("Stopped(", StringComparison.Ordinal));
            Assert.Equal(3, recorder.LastStatus(1)?.HitCount);
            Assert.Equal("Dog Rex\n24\ndone\n", recorder.StdoutSoFar);
        });
    }

    [Fact]
    [Trait("Req", "DBG-324")]
    public async Task Scripted_Exception_Notify_And_Ignore()
    {
        var program = fixture.Build("Raises");
        var p = program.SourcePath;

        async Task<List<StopInfo>> RunAsync(string run, string[] ignoredClasses)
        {
            var stops = new List<StopInfo>();
            var recorder = new SessionRecorder(nameof(Scripted_Exception_Notify_And_Ignore) + "." + run);
            await recorder.RunAsync(fixture, program, async session =>
            {
                await session.LaunchAsync(
                    new LaunchRequest { Program = program.ExePath, Exceptions = new ExceptionSettings { IgnoredClasses = ignoredClasses } },
                    Ct);

                // Continue after each stop until the program ends.
                while (true)
                {
                    var next = await recorder.WaitForMilestoneAsync();
                    if (next is not StopInfo stop)
                    {
                        var terminated = Assert.IsType<TerminatedEventArgs>(next);
                        Assert.Equal(TerminationCause.Exited, terminated.Cause);
                        Assert.Equal(0, terminated.ExitCode);
                        break;
                    }

                    Assert.Equal(DebugState.Stopped, session.State);
                    stops.Add(stop);
                    Assert.True(stops.Count <= 2, "More stops than exceptions: " + stop);
                    await session.ContinueAsync(Ct);
                }

                Assert.Equal("start\ncaught first\ncaught second\n", recorder.StdoutSoFar);
            });
            return stops;
        }

        static void AssertException(StopInfo stop, string path, int line, string className, string message)
        {
            Assert.Equal(StopReason.Exception, stop.Reason);
            Assert.NotNull(stop.Location);
            Assert.Equal(path, stop.Location.Path, ignoreCase: true);
            Assert.Equal(line, stop.Location.Line);
            Assert.NotNull(stop.Exception);
            Assert.Equal(ExceptionKind.Language, stop.Exception.Kind);
            Assert.Equal(className, stop.Exception.ClassName);
            Assert.Equal(message, stop.Exception.Message);
        }

        // Run 1: nothing ignored - both exceptions are announced, in order.
        var all = await RunAsync("run1", []);
        Assert.Equal(2, all.Count);
        AssertException(all[0], p, 12, "EFirst", "first");
        AssertException(all[1], p, 17, "ESecond", "second");

        // Run 2: the first class is ignored - only the second exception stops.
        var filtered = await RunAsync("run2", ["EFirst"]);
        AssertException(Assert.Single(filtered), p, 17, "ESecond", "second");
    }

    [Fact]
    [Trait("Req", "DBG-244")]
    public async Task Scripted_Exception_DescendantOfIgnoredClass_DoesNotStop()
    {
        // The program raises a class that descends from EAbort, one that descends from that one, and a plain
        // Exception. With the default list (EAbort) only the last one stops: the list covers descendants.
        var program = fixture.Build("QuietAbort");
        var stops = new List<StopInfo>();
        var recorder = new SessionRecorder(nameof(Scripted_Exception_DescendantOfIgnoredClass_DoesNotStop));
        await recorder.RunAsync(fixture, program, async session =>
        {
            await session.LaunchAsync(new LaunchRequest { Program = program.ExePath }, Ct);

            while (true)
            {
                var next = await recorder.WaitForMilestoneAsync();
                if (next is not StopInfo stop)
                {
                    var terminated = Assert.IsType<TerminatedEventArgs>(next);
                    Assert.Equal(TerminationCause.Exited, terminated.Cause);
                    Assert.Equal(0, terminated.ExitCode);
                    break;
                }

                stops.Add(stop);
                Assert.True(stops.Count <= 3, "More stops than exceptions: " + stop);
                await session.ContinueAsync(Ct);
            }

            Assert.Equal("start\ncaught quiet\ncaught quieter\ncaught loud\n", recorder.StdoutSoFar);
        });

        var only = Assert.Single(stops);
        Assert.Equal(StopReason.Exception, only.Reason);
        Assert.NotNull(only.Exception);
        Assert.Equal(ExceptionKind.Language, only.Exception.Kind);
        Assert.Equal("Exception", only.Exception.ClassName);
        Assert.Equal("loud", only.Exception.Message);
        Assert.NotNull(only.Location);
        Assert.Equal(program.SourcePath, only.Location.Path, ignoreCase: true);
        Assert.Equal(22, only.Location.Line);
    }

    [Fact]
    [Trait("Req", "DBG-326")]
    public async Task Scripted_NoDebugInfo()
    {
        var program = fixture.Build("DbgDemo", debugInfo: false);
        Assert.False(File.Exists(program.TypeInfoPath), "A program built without debug information has a type-information file: " + program.TypeInfoPath);
        var recorder = new SessionRecorder(nameof(Scripted_NoDebugInfo));
        await recorder.RunAsync(fixture, program, async session =>
        {
            await session.LaunchAsync(
                new LaunchRequest
                {
                    Program = program.ExePath,
                    Breakpoints = [new SourceBreakpoints(program.SourcePath, [new BreakpointSpec { Id = 1, Line = 69 }])],
                },
                Ct);
            Assert.False(session.HasTypeInfo);

            // Nothing can stop the program: the next thing that happens is its end.
            var terminated = await recorder.WaitForTerminatedAsync();
            Assert.Equal(TerminationCause.Exited, terminated.Cause);
            Assert.Equal(0, terminated.ExitCode);
            Assert.Contains("Type information file not found", recorder.OutputSoFar(OutputCategory.Debugger), StringComparison.Ordinal);
            Assert.DoesNotContain(recorder.StatusHistory(1), s => s.State == BreakpointState.Verified);
            Assert.DoesNotContain(recorder.Events, e => e.StartsWith("Stopped(", StringComparison.Ordinal));
            Assert.Equal("Dog Rex\n24\ndone\n", recorder.StdoutSoFar);
        });
    }

    /// <summary>The line of the first statement of a routine of a unit: the line after the <c>begin</c> of its implementation.</summary>
    private static int FirstStatementLine(string unitPath, string heading)
    {
        var lines = File.ReadAllLines(unitPath);
        // The heading occurs twice: in the interface section and at the implementation.
        var implementation = Array.FindLastIndex(lines, line => line == heading);
        Assert.True(implementation >= 0, $"'{heading}' not found in {unitPath}.");
        var begin = Array.FindIndex(lines, implementation, line => line == "begin");
        Assert.True(begin > implementation, $"No 'begin' after '{heading}' in {unitPath}.");
        return begin + 2;   // one-based line of the line after 'begin'
    }

    /// <summary>
    /// Step Into on a call of a runtime-library routine: with the library root among the user source roots the step
    /// ends at the routine's first statement in the library file; without it the step ends on the program's next line.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Phase", "8")]
    [Trait("Req", "DBG-221")]
    [Trait("Req", "DBG-222")]
    public async Task Phase8_StepIntoLibrarySource_StopsInRtlFile(bool libraryRootIsUserSource)
    {
        const int CallLine = 11;   // LibStep.pas: T := UpperCase(S);
        var program = fixture.Build("LibStep");
        var p = program.SourcePath;
        var unit = Path.Combine(fixture.RtlDirectory, "SysUtils.pas");
        var firstStatement = FirstStatementLine(unit, "function UpperCase(const S: string): string;");
        string[] roots = libraryRootIsUserSource ? [program.Directory, fixture.RtlDirectory] : [program.Directory];
        var recorder = new SessionRecorder(nameof(Phase8_StepIntoLibrarySource_StopsInRtlFile) + (libraryRootIsUserSource ? "-with-root" : "-without-root"));
        await recorder.RunAsync(
            fixture,
            program,
            async session =>
            {
                await session.LaunchAsync(
                    new LaunchRequest
                    {
                        Program = program.ExePath,
                        Breakpoints = [new SourceBreakpoints(p, [new BreakpointSpec { Id = 1, Line = CallLine }])],
                    },
                    Ct);
                var stop = await recorder.WaitForStopAsync();
                AssertStop(session, stop, StopReason.Breakpoint, p, CallLine, [1]);

                await session.StepIntoAsync(stop.ThreadId, Ct);
                stop = await recorder.WaitForStopAsync();
                if (libraryRootIsUserSource)
                {
                    AssertStop(session, stop, StopReason.Step, unit, firstStatement);
                    Assert.True(PathIsUnder(stop.Location!.Path, fixture.RtlDirectory), stop.Location.Path);
                    var frames = (await session.GetStackTraceAsync(stop.ThreadId, ct: Ct)).Frames;
                    Assert.Equal("UpperCase", frames[0].Name);
                    Assert.True(frames[0].IsUserCode);
                    Assert.Equal(p, frames[1].Location?.Path, ignoreCase: true);
                    Assert.Equal(CallLine, frames[1].Location?.Line);
                }
                else
                {
                    AssertStop(session, stop, StopReason.Step, p, CallLine + 1);
                }

                await session.ContinueAsync(Ct);
                var terminated = await recorder.WaitForTerminatedAsync();
                Assert.Equal(TerminationCause.Exited, terminated.Cause);
                Assert.Equal(0, terminated.ExitCode);
                Assert.Equal("STEP\n", recorder.StdoutSoFar);
                // One breakpoint stop and one step stop: nothing in between was shown.
                Assert.Equal(2, recorder.Events.Count(e => e.StartsWith("Stopped(", StringComparison.Ordinal)));
            },
            roots);
    }

    /// <summary>
    /// Inline variables of a routine (InlineScope.pas): each is listed under Locals and evaluable (watch and hover)
    /// only while stopped inside its declaration scope; the redeclaration of a name in a sibling block shows under
    /// the plain name with its own type; a variable declared in a finally part is visible while stopped inside that
    /// part, whose code is a subprogram of its own, together with the other variables of the routine.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DBG-142")]
    public async Task Phase8_InlineVariable_VisibleOnlyInsideItsBlock()
    {
        var program = fixture.Build("InlineScope");
        var p = program.SourcePath;
        var recorder = new SessionRecorder(nameof(Phase8_InlineVariable_VisibleOnlyInsideItsBlock));
        await recorder.RunAsync(fixture, program, async session =>
        {
            // Line of the stop -> the rows of Locals, and what "Tmp" and "Fin" evaluate to (null: not declared there).
            (int Line, string[] Locals, string? Tmp, string? Fin)[] stops =
            [
                (7, ["Total = 0 : Integer"], null, null),                                       // before the first block
                (11, ["Total = 3 : Integer", "Tmp = 6 : Integer"], "6 : Integer", null),        // inside the first block
                (16, ["Total = 9 : Integer", "Tmp = 'two' : string"], "'two' : string", null),  // inside the sibling block
                (22, ["Total = 10 : Integer", "Fin = 100 : Integer"], null, "100 : Integer"),   // inside the finally part
                (24, ["Total = 10 : Integer"], null, null),                                     // after all of them
            ];

            await session.LaunchAsync(
                new LaunchRequest
                {
                    Program = program.ExePath,
                    Breakpoints = [new SourceBreakpoints(p, [.. stops.Select((s, index) => new BreakpointSpec { Id = index + 1, Line = s.Line })])],
                },
                Ct);
            Assert.True(session.HasTypeInfo);

            for (var index = 0; index < stops.Length; index++)
            {
                var (line, expectedLocals, tmp, fin) = stops[index];
                var stop = await recorder.WaitForStopAsync();
                AssertStop(session, stop, StopReason.Breakpoint, p, line, [index + 1]);
                var frames = (await session.GetStackTraceAsync(stop.ThreadId, ct: Ct)).Frames;
                var scopes = await session.GetScopesAsync(frames[0].Id, Ct);
                Assert.Equal(["N = 3 : Integer"], (await session.GetChildrenAsync(scopes[0].Handle, ct: Ct)).Select(Row));
                var locals = await session.GetChildrenAsync(scopes[1].Handle, ct: Ct);
                // The first stop is before the assignment to Total: its value is not fixed.
                Assert.Equal(index == 0 ? ["Total"] : expectedLocals, locals.Select(l => index == 0 ? l.Name : Row(l)));

                foreach (var (name, expected) in ((string, string?)[])[("Tmp", tmp), ("Fin", fin)])
                {
                    // Watch and hover agree with the list, with the frame given and (null) in the top frame of the stop.
                    foreach (var context in (EvaluateContext[])[EvaluateContext.Watch, EvaluateContext.Hover])
                    {
                        foreach (var frameId in (int?[])[frames[0].Id, null])
                        {
                            if (expected is null)
                            {
                                var error = await Assert.ThrowsAsync<DebuggerException>(() => session.EvaluateAsync(name, frameId, context, Ct));
                                Assert.Equal(DebugErrorCode.ExpressionEvaluation, error.Code);
                                Assert.Equal($"Undeclared identifier '{name}'", error.Message);
                            }
                            else
                            {
                                var value = (await session.EvaluateAsync(name, frameId, context, Ct)).Value;
                                Assert.Equal(expected, $"{value.Value} : {value.TypeName}");
                                Assert.Equal(name, value.Name);
                            }
                        }
                    }
                }

                // The numbered name of the redeclaration is not an identifier anywhere.
                var numbered = await Assert.ThrowsAsync<DebuggerException>(() => session.EvaluateAsync("Tmp$2", frames[0].Id, EvaluateContext.Watch, Ct));
                Assert.Equal(DebugErrorCode.ExpressionSyntax, numbered.Code);
                Assert.DoesNotContain(locals, l => l.Name.Contains('$'));

                if (line == 22)
                {
                    // The finally part is a subprogram called by the routine: the routine's own frame is the next one.
                    Assert.Equal("Run", frames[1].Name);
                    Assert.Equal("InlineScope", frames[2].Name);
                    Assert.Equal("10", (await session.EvaluateAsync("Total", frames[0].Id, EvaluateContext.Watch, Ct)).Value.Value);
                    Assert.Equal("103", (await session.EvaluateAsync("Fin + N", frames[0].Id, EvaluateContext.Watch, Ct)).Value.Value);
                }

                await session.ContinueAsync(Ct);
            }

            var terminated = await recorder.WaitForTerminatedAsync();
            Assert.Equal(TerminationCause.Exited, terminated.Cause);
            Assert.Equal(0, terminated.ExitCode);
            Assert.Equal("two\n100\n10\n", recorder.StdoutSoFar);
        });
    }

    private static bool PathIsUnder(string path, string root) =>
        Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
