using FreexComp.Debugger.Tests.TestSupport;

namespace FreexComp.Debugger.Tests;

/// <summary>The type-information file in a launch (debug-session DBG-402, DBG-403).</summary>
[Trait("Category", "Integration")]
public sealed class TypeInfoSessionTests
{
    private static readonly TimeSpan Safety = SessionHarness.Safety;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Req", "DBG-403")]
    public async Task TypeInfo_Missing_OutputEventAndSessionWorks()
    {
        await using var harness = SessionHarness.Create("{}");

        // No TypeInfoPath: the program path with the extension replaced by ".pdi".
        await harness.LaunchAsync(new LaunchRequest { Program = SessionHarness.Program });

        var expected = Path.ChangeExtension(SessionHarness.Program, ".pdi");
        Assert.False(harness.Session.HasTypeInfo);
        Assert.Equal(DebugState.Running, harness.Session.State);
        await harness.Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
        await harness.WaitForEventAsync("terminated");
        var output = Assert.Single(harness.Events.Snapshot(), e => e.StartsWith("output", StringComparison.Ordinal));
        Assert.Equal(
            "output Debugger " + System.Text.Json.JsonSerializer.Serialize($"Type information file not found: {expected}. Values are shown unformatted."),
            output);
        harness.AssertScenarioComplete();
    }

    [Fact]
    [Trait("Req", "DBG-403")]
    public async Task TypeInfo_ExplicitPath_Loaded()
    {
        await using var harness = SessionHarness.Create("{}");
        Assert.False(harness.Session.HasTypeInfo);

        await harness.LaunchAsync(new LaunchRequest { Program = SessionHarness.Program, TypeInfoPath = PdiFixture.DbgDemoPath });

        Assert.True(harness.Session.HasTypeInfo);
        await harness.Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
        await harness.WaitForEventAsync("terminated");
        Assert.DoesNotContain(harness.Events.Snapshot(), e => e.StartsWith("output", StringComparison.Ordinal));
    }

    [Theory]
    [Trait("Req", "DBG-402")]
    [Trait("Req", "DBG-403")]
    [InlineData("{ \"formatVersion\": 2 }", "unsupported formatVersion 2")]
    [InlineData("not json", "malformed JSON")]
    public async Task TypeInfo_Invalid_LaunchFaults(string content, string detail)
    {
        var path = PdiFixture.WriteTemp("Broken.pdi", content);
        try
        {
            await using var harness = SessionHarness.Create("{}");

            var exception = await Assert.ThrowsAsync<DebuggerException>(
                () => harness.LaunchAsync(new LaunchRequest { Program = SessionHarness.Program, TypeInfoPath = path }));

            Assert.Equal(DebugErrorCode.TypeInfoInvalid, exception.Code);
            Assert.StartsWith($"Type information file is invalid: {path}: {detail}", exception.Message);
            Assert.False(harness.Session.HasTypeInfo);
            Assert.Equal(DebugState.Terminated, harness.Session.State);
            // The Terminated event precedes the fault; the adapter was never started.
            Assert.Equal(
                ["state Idle->Starting", "state Starting->Terminated", $"terminated LaunchFailed null {exception.Message}"],
                harness.Events.Snapshot());
            Assert.Null(harness.AdapterProcessId);
        }
        finally
        {
            PdiFixture.DeleteDirectoryOf(path);
        }
    }
}
