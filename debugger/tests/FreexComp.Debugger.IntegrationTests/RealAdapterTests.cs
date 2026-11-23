using System.Diagnostics;
using System.Globalization;
using FreexComp.Debugger.IntegrationTests.TestSupport;

namespace FreexComp.Debugger.IntegrationTests;

/// <summary>
/// Sessions against the real debug adapter and a debuggee built with the Pascal compiler (scripted test §2, §3):
/// launch, breakpoint, call stack, natural exit, and the Pascal formatting of the demo program's locals.
/// </summary>
[Collection(RealAdapterCollection.Name)]
[Trait("Kind", "Debug")]
[Trait("Category", "Integration")]
[Trait("Category", "Toolchain")]
public sealed class RealAdapterTests(IntegrationFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static void AssertStop(StopInfo stop, StopReason reason, string path, int line, int[]? hitBreakpointIds = null)
    {
        Assert.Equal(reason, stop.Reason);
        Assert.NotNull(stop.Location);
        Assert.Equal(path, stop.Location.Path, ignoreCase: true);
        Assert.Equal(line, stop.Location.Line);
        if (hitBreakpointIds is not null)
            Assert.Equal(hitBreakpointIds.Order(), stop.HitBreakpointIds.Order());
    }

    private static LaunchRequest LaunchAtLine69(BuiltProgram program) => new()
    {
        Program = program.ExePath,
        Breakpoints = [new SourceBreakpoints(program.SourcePath, [new BreakpointSpec { Id = 1, Line = 69 }])],
    };

    [Fact]
    [Trait("Req", "DBG-310")]
    [Trait("Req", "DBG-311")]
    [Trait("Req", "DBG-320")]
    [Trait("Req", "DBG-321")]
    public async Task Breakpoint_Stack_Exit()
    {
        var program = fixture.Build("DbgDemo");
        var recorder = new SessionRecorder(nameof(Breakpoint_Stack_Exit));
        int? debuggee = null;
        await recorder.RunAsync(fixture, program, async session =>
        {
            // Launch; stop at a plain breakpoint.
            await session.LaunchAsync(LaunchAtLine69(program), Ct);
            Assert.True(session.HasTypeInfo);
            var stop = await recorder.WaitForStopAsync();
            AssertStop(stop, StopReason.Breakpoint, program.SourcePath, 69, [1]);
            Assert.Equal(1, stop.Epoch);
            Assert.Equal(DebugState.Stopped, session.State);
            Assert.Same(stop, session.CurrentStop);
            debuggee = session.ProcessId;
            Assert.NotNull(debuggee);

            // The events up to the stop: Starting, then Stopped directly or through Running; StateChanged right before Stopped.
            var events = recorder.Events
                .Where(e => e.StartsWith("StateChanged", StringComparison.Ordinal) || e.StartsWith("Stopped", StringComparison.Ordinal)).ToList();
            Assert.Equal("StateChanged(Idle→Starting)", events[0]);
            Assert.True(
                events is [_, "StateChanged(Starting→Stopped)", _] or [_, "StateChanged(Starting→Running)", "StateChanged(Running→Stopped)", _],
                string.Join(" | ", events));
            Assert.StartsWith("Stopped(Breakpoint", events[^1], StringComparison.Ordinal);
            var status = recorder.LastStatus(1);
            Assert.NotNull(status);
            Assert.Equal(BreakpointState.Verified, status.State);
            Assert.Equal(69, status.ActualLine);

            // Threads and call stack.
            var threads = await session.GetThreadsAsync(Ct);
            Assert.Equal(stop.ThreadId, Assert.Single(threads, t => t.IsCurrent).Id);
            var frames = (await session.GetStackTraceAsync(stop.ThreadId, ct: Ct)).Frames;
            Assert.True(frames.Count >= 2, "frames: " + string.Join(", ", frames.Select(f => f.Name)));
            Assert.Equal("Run", frames[0].Name);
            Assert.Equal(0, frames[0].Index);
            Assert.NotNull(frames[0].Location);
            Assert.Equal(program.SourcePath, frames[0].Location!.Path, ignoreCase: true);
            Assert.Equal(69, frames[0].Location!.Line);
            Assert.True(frames[0].IsUserCode);
            Assert.Equal("DbgDemo", frames[1].Name);
            Assert.Equal(77, frames[1].Location?.Line);
            Assert.True(frames[1].IsUserCode);
            Assert.All(frames.Skip(2), frame => Assert.False(frame.IsUserCode, frame.Name));
            Assert.Equal("DbgDemo.exe", frames[0].Module, ignoreCase: true);

            // Run to completion.
            await session.ContinueAsync(Ct);
            var terminated = await recorder.WaitForTerminatedAsync();
            Assert.Equal(TerminationCause.Exited, terminated.Cause);
            Assert.Equal(0, terminated.ExitCode);
            Assert.StartsWith("Terminated(Exited", recorder.Events[^1], StringComparison.Ordinal);
            Assert.Equal(DebugState.Terminated, session.State);
            Assert.Equal(0, session.ExitCode);
            Assert.Null(session.CurrentStop);
            Assert.Equal("Dog Rex\n24\ndone\n", recorder.StdoutSoFar);
            Assert.Equal("", recorder.StderrSoFar);
            var late = await Assert.ThrowsAsync<DebuggerException>(() => session.ContinueAsync(Ct));
            Assert.Equal(DebugErrorCode.InvalidState, late.Code);
        });

        // After disposal nothing of the session is alive (the recorder checked the adapter's process tree).
        Assert.False(IsAlive(debuggee!.Value), "The debuggee is still running.");
    }

    [Fact]
    [Trait("Req", "DBG-312")]
    public async Task Locals_DbgDemo_Line69()
    {
        var program = fixture.Build("DbgDemo");
        var recorder = new SessionRecorder(nameof(Locals_DbgDemo_Line69));
        await recorder.RunAsync(fixture, program, async session =>
        {
            await session.LaunchAsync(LaunchAtLine69(program), Ct);
            var stop = await recorder.WaitForStopAsync();
            AssertStop(stop, StopReason.Breakpoint, program.SourcePath, 69, [1]);
            var frame = (await session.GetStackTraceAsync(stop.ThreadId, ct: Ct)).Frames[0];

            var scopes = await session.GetScopesAsync(frame.Id, Ct);
            Assert.Equal([ScopeKind.Arguments, ScopeKind.Locals, ScopeKind.Globals], scopes.Select(s => s.Kind));
            Assert.Equal(["Arguments", "Locals", "Globals"], scopes.Select(s => s.Name));
            Assert.Empty(await session.GetChildrenAsync(scopes[0].Handle, ct: Ct));
            Assert.Empty(await session.GetChildrenAsync(scopes[2].Handle, ct: Ct));
            var locals = await session.GetChildrenAsync(scopes[1].Handle, ct: Ct);

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
            static string Row(VariableInfo v) => $"{v.Name} = {v.Value} : {v.TypeName}";

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
            // The pointer holds the address of Total.
            Assert.Equal(
                Local("Total").Address,
                ulong.Parse(Local("PTotal").Value.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));

            await session.TerminateAsync(Ct);
            Assert.Equal(TerminationCause.TerminatedByUser, (await recorder.WaitForTerminatedAsync()).Cause);
        });
    }

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
}
