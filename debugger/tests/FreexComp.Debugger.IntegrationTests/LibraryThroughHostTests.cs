using FreexComp.Debugger.IntegrationTests.TestSupport;

namespace FreexComp.Debugger.IntegrationTests;

/// <summary>
/// Debugging a library through its host application: the program of the session is the host, the library's
/// type-information file is loaded besides, breakpoints in the library's units wait for the module. Real compiler,
/// real debug adapter, real debuggee; every wait is on an event.
/// </summary>
[Collection(RealAdapterCollection.Name)]
[Trait("Kind", "Debug")]
[Trait("Category", "Integration")]
[Trait("Category", "Toolchain")]
public sealed class LibraryThroughHostTests(IntegrationFixture fixture)
{
    private const int ScaleResultLine = 18;   // DllLibUnit.pas: Result := Product + 1;
    private const int RaiseLine = 25;         // DllLibUnit.pas: raise Exception.Create('inside library');
    private const int HostCallLine = 21;      // DllHost.pas: R := Scale(6, 7);
    private const int HostEndLine = 26;       // DllHost.pas: WriteLn('host end'); - the library has been freed
    private const string HostOutput = "host start\nloaded TRUE\nscaled 43\nguarded -5\nfreed TRUE\nhost end\n";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Row(VariableInfo v) => $"{v.Name} = {v.Value} : {v.TypeName}";

    private static string UnitPath(BuiltProgram library) => Path.Combine(library.Directory, "DllLibUnit.pas");

    private static LaunchRequest Request(BuiltProgram host, BuiltProgram library) => new()
    {
        Program = host.ExePath,
        Libraries = [library.ExePath],
        Breakpoints = [new SourceBreakpoints(UnitPath(library), [new BreakpointSpec { Id = 1, Line = ScaleResultLine }])],
    };

    private static void AssertLibraryException(StopInfo stop, string unit)
    {
        Assert.Equal(StopReason.Exception, stop.Reason);
        Assert.NotNull(stop.Exception);
        Assert.Equal(ExceptionKind.Language, stop.Exception.Kind);
        Assert.Equal("Exception", stop.Exception.ClassName);
        Assert.Equal("inside library", stop.Exception.Message);
        Assert.NotNull(stop.Location);
        Assert.Equal(unit, stop.Location.Path, ignoreCase: true);
        Assert.Equal(RaiseLine, stop.Location.Line);
    }

    /// <summary>The breakpoint was pending before the module came and bound when it did.</summary>
    private static void AssertBoundOnLoad(SessionRecorder recorder)
    {
        var history = recorder.StatusHistory(1).ToList();
        var bound = history.FindIndex(s => s.State == BreakpointState.Verified);
        Assert.True(bound > 0, "Statuses: " + string.Join(", ", history));
        Assert.All(history.Take(bound), s => Assert.Equal(BreakpointState.Pending, s.State));
        Assert.Equal(ScaleResultLine, history[bound].ActualLine);
    }

    [Fact]
    [Trait("Phase", "8")]
    public async Task Phase8_ScriptedSession_LibraryThroughHost()
    {
        var (host, library) = fixture.BuildLibraryWithHost();
        Assert.True(File.Exists(library.TypeInfoPath), "The compiler wrote no type-information file for the library: " + library.TypeInfoPath);
        var unit = UnitPath(library);
        var recorder = new SessionRecorder(nameof(Phase8_ScriptedSession_LibraryThroughHost));
        await recorder.RunAsync(fixture, host, async session =>
        {
            // ---- The breakpoint in the library unit is set before the launch: the module is loaded by the host later.
            // A second one, in the host, on a line it reaches after it has freed the library.
            var request = Request(host, library);
            await session.LaunchAsync(
                request with
                {
                    Breakpoints = [.. request.Breakpoints, new SourceBreakpoints(host.SourcePath, [new BreakpointSpec { Id = 2, Line = HostEndLine }])],
                },
                Ct);
            Assert.True(session.HasTypeInfo);
            var stop = await recorder.WaitForStopAsync();
            Assert.Equal(StopReason.Breakpoint, stop.Reason);
            Assert.Equal(unit, stop.Location?.Path, ignoreCase: true);
            Assert.Equal(ScaleResultLine, stop.Location?.Line);
            Assert.Equal([1], stop.HitBreakpointIds);
            Assert.Equal("host start\nloaded TRUE\n", recorder.StdoutSoFar);
            AssertBoundOnLoad(recorder);

            // ---- The stack: the library routine above the host's main block.
            var frames = (await session.GetStackTraceAsync(stop.ThreadId, ct: Ct)).Frames;
            Assert.True(frames.Count >= 2, "frames: " + string.Join(", ", frames.Select(f => f.Name)));
            Assert.Equal("Scale", frames[0].Name);
            Assert.True(frames[0].IsUserCode);
            Assert.Equal("DllLib.dll", frames[0].Module, ignoreCase: true);
            // Between the two the adapter may show the library's export entry, which has no source.
            var hostIndex = frames.ToList().FindIndex(f => f.Module.Equals("DllHost.exe", StringComparison.OrdinalIgnoreCase));
            Assert.True(hostIndex >= 1, "frames: " + string.Join(", ", frames.Select(f => f.Module + "!" + f.Name)));
            Assert.All(frames.Take(hostIndex).Skip(1), f => Assert.False(f.IsUserCode, f.Name));
            Assert.Equal("DllHost", frames[hostIndex].Name);
            Assert.Equal(host.SourcePath, frames[hostIndex].Location?.Path, ignoreCase: true);
            Assert.Equal(HostCallLine, frames[hostIndex].Location?.Line);
            Assert.True(frames[hostIndex].IsUserCode);

            // ---- Locals and parameters of the library routine, through the library's type information.
            var scopes = await session.GetScopesAsync(frames[0].Id, Ct);
            var arguments = await session.GetChildrenAsync(scopes.Single(s => s.Kind == ScopeKind.Arguments).Handle, ct: Ct);
            Assert.Equal(["Value = 6 : Integer", "Factor = 7 : Integer"], arguments.Select(Row));
            var locals = await session.GetChildrenAsync(scopes.Single(s => s.Kind == ScopeKind.Locals).Handle, ct: Ct);
            Assert.Contains("Product = 42 : Integer", locals.Select(Row));
            Assert.Equal("84", (await session.EvaluateAsync("Product * 2", frames[0].Id, EvaluateContext.Watch, Ct)).Value.Value);

            // ---- Step Out of the exported routine: back in the host, on the line of the call.
            await session.StepOutAsync(stop.ThreadId, Ct);
            stop = await recorder.WaitForStopAsync();
            Assert.Equal(StopReason.Step, stop.Reason);
            Assert.Equal(host.SourcePath, stop.Location?.Path, ignoreCase: true);
            Assert.Equal(HostCallLine, stop.Location?.Line);

            // ---- An exception raised and handled inside the library: seen through the library's own raise routine.
            await session.ContinueAsync(Ct);
            AssertLibraryException(await recorder.WaitForStopAsync(), unit);

            // ---- The host frees the library: the breakpoint in the library unit is unbound again, the process lives.
            await session.ContinueAsync(Ct);
            stop = await recorder.WaitForStopAsync();
            Assert.Equal(StopReason.Breakpoint, stop.Reason);
            Assert.Equal(host.SourcePath, stop.Location?.Path, ignoreCase: true);
            Assert.Equal(HostEndLine, stop.Location?.Line);
            Assert.Equal([2], stop.HitBreakpointIds);
            Assert.Equal(BreakpointState.Pending, recorder.LastStatus(1)?.State);
            Assert.Equal(1, recorder.LastStatus(1)?.HitCount);
            Assert.DoesNotContain((await session.GetStackTraceAsync(stop.ThreadId, ct: Ct)).Frames,
                f => f.Module.Equals("DllLib.dll", StringComparison.OrdinalIgnoreCase));

            // ---- The host ends.
            await session.ContinueAsync(Ct);
            var terminated = await recorder.WaitForTerminatedAsync();
            Assert.Equal(TerminationCause.Exited, terminated.Cause);
            Assert.Equal(0, terminated.ExitCode);
            Assert.Equal(HostOutput, recorder.StdoutSoFar);
        });
    }

    [Fact]
    [Trait("Phase", "8")]
    public async Task Scripted_Library_HostWithoutDebugInfo_StepOutRunsToNextUserStop()
    {
        var (host, library) = fixture.BuildLibraryWithHost(hostDebugInfo: false);
        Assert.False(File.Exists(host.TypeInfoPath), "A host built without debug information has a type-information file: " + host.TypeInfoPath);
        var unit = UnitPath(library);
        var recorder = new SessionRecorder(nameof(Scripted_Library_HostWithoutDebugInfo_StepOutRunsToNextUserStop));
        await recorder.RunAsync(fixture, host, async session =>
        {
            await session.LaunchAsync(Request(host, library), Ct);
            Assert.True(session.HasTypeInfo);
            var stop = await recorder.WaitForStopAsync();
            Assert.Equal(StopReason.Breakpoint, stop.Reason);
            Assert.Equal(unit, stop.Location?.Path, ignoreCase: true);
            Assert.Equal(ScaleResultLine, stop.Location?.Line);

            // The frames of the host have no source: none of them is user code.
            var frames = (await session.GetStackTraceAsync(stop.ThreadId, ct: Ct)).Frames;
            Assert.Equal("Scale", frames[0].Name);
            Assert.True(frames[0].IsUserCode);
            Assert.All(frames.Skip(1), frame => Assert.False(frame.IsUserCode, frame.Name));
            var locals = await session.GetChildrenAsync(
                (await session.GetScopesAsync(frames[0].Id, Ct)).Single(s => s.Kind == ScopeKind.Locals).Handle, ct: Ct);
            Assert.Contains("Product = 42 : Integer", locals.Select(Row));

            // Step Out of the exported routine does not stop in the host: the next stop is the next one in user code,
            // here the exception the library raises in the host's next call.
            await session.StepOutAsync(stop.ThreadId, Ct);
            AssertLibraryException(await recorder.WaitForStopAsync(), unit);
            Assert.DoesNotContain(recorder.Events, e => e.StartsWith("Stopped(Step", StringComparison.Ordinal));

            await session.ContinueAsync(Ct);
            var terminated = await recorder.WaitForTerminatedAsync();
            Assert.Equal(TerminationCause.Exited, terminated.Cause);
            Assert.Equal(0, terminated.ExitCode);
            Assert.Equal(HostOutput, recorder.StdoutSoFar);
            // No type-information file for the host is not worth a message when a library is debugged through it.
            Assert.DoesNotContain("Type information file not found", recorder.OutputSoFar(OutputCategory.Debugger), StringComparison.Ordinal);
        });
    }

    [Fact]
    [Trait("Phase", "8")]
    public async Task Scripted_Library_ImportedAtLoadTime_BreakpointBinds()
    {
        var (host, library) = fixture.BuildLibraryWithHost("DllHostStatic");
        var unit = UnitPath(library);
        var recorder = new SessionRecorder(nameof(Scripted_Library_ImportedAtLoadTime_BreakpointBinds));
        await recorder.RunAsync(fixture, host, async session =>
        {
            await session.LaunchAsync(Request(host, library), Ct);
            var stop = await recorder.WaitForStopAsync();
            Assert.Equal(StopReason.Breakpoint, stop.Reason);
            Assert.Equal(unit, stop.Location?.Path, ignoreCase: true);
            Assert.Equal(ScaleResultLine, stop.Location?.Line);
            Assert.Equal([1], stop.HitBreakpointIds);
            Assert.Equal(BreakpointState.Verified, recorder.LastStatus(1)?.State);
            Assert.Equal("host start\n", recorder.StdoutSoFar);

            await session.ContinueAsync(Ct);
            var terminated = await recorder.WaitForTerminatedAsync();
            Assert.Equal(TerminationCause.Exited, terminated.Cause);
            Assert.Equal(0, terminated.ExitCode);
            Assert.Equal("host start\nscaled 43\nhost end\n", recorder.StdoutSoFar);
        });
    }
}
