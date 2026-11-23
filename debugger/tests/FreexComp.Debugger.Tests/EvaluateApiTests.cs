using System.Text.Json;
using FreexComp.Debugger.Tests.TestSupport;

namespace FreexComp.Debugger.Tests;

/// <summary><c>EvaluateAsync</c> of the public session (debug-session DBG-412; Pascal values §9.6; DAP usage DBG-083).</summary>
[Trait("Category", "Integration")]
public sealed class EvaluateApiTests
{
    private static readonly TimeSpan Safety = SessionHarness.Safety;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Req", "DBG-412")]
    [Trait("Req", "DBG-183")]
    public async Task Immediate_IsLeaf()
    {
        await using var harness = await DemoSession.StoppedInRunAsync();

        // frameId null = the top frame of the stopping thread.
        var result = await harness.Session.EvaluateAsync("  Total * 2 + 1 ", null, EvaluateContext.Watch, Ct).WaitAsync(Safety, Ct);

        var value = result.Value;
        Assert.Equal("Total * 2 + 1", value.Name);          // as typed, trimmed
        Assert.Equal("49", value.Value);
        Assert.Equal("Integer", value.TypeName);
        Assert.Equal(ValueKind.Integer, value.Kind);
        Assert.Null(value.Children);
        Assert.Equal(0, value.ChildCount);
        Assert.False(value.CanModify);
        Assert.Null(value.EvaluateName);
        Assert.Null(value.Address);

        // The same in every context; the suffix formats this result only.
        foreach (var context in Enum.GetValues<EvaluateContext>())
            Assert.Equal("$00000031", (await harness.Session.EvaluateAsync("Total * 2 + 1,x", null, context, Ct).WaitAsync(Safety, Ct)).Value.Value);
        Assert.Equal("'Héllo!'", (await DemoSession.EvaluateAsync(harness, "S + '!'")).Value);
        Assert.Equal("True", (await DemoSession.EvaluateAsync(harness, "PTotal = @Total")).Value);

        // DBG-083: the user's text never reaches the adapter; only "&" + a root variable's declared name does.
        var sent = harness.SentRequests("evaluate")
            .Select(json => JsonDocument.Parse(json).RootElement.GetProperty("arguments").GetProperty("expression").GetString()).ToList();
        Assert.NotEmpty(sent);
        Assert.All(sent, expression => Assert.Matches("^&[A-Za-z_][A-Za-z0-9_]*$", expression));
        Assert.DoesNotContain("writeMemory", harness.SentCommands());
        Assert.Equal(DebugState.Stopped, harness.Session.State);
        await DemoSession.EndAsync(harness);
    }

    [Fact]
    [Trait("Req", "DBG-412")]
    [Trait("Req", "DBG-183")]
    public async Task Location_IsExpandable()
    {
        await using var harness = await DemoSession.StoppedInRunAsync(DemoSession.Stack(71));
        var session = harness.Session;
        var frames = (await session.GetStackTraceAsync(DemoSession.ThreadId, ct: Ct).WaitAsync(Safety, Ct)).Frames;

        var point = await DemoSession.EvaluateAsync(harness, "P", frames[0].Id);
        var element = await DemoSession.EvaluateAsync(harness, " nums[ 1 ] ", frames[0].Id);
        var pet = await DemoSession.EvaluateAsync(harness, "Pet");

        // A location is a full node: expandable through GetChildrenAsync, with the normalised text as EvaluateName.
        Assert.Equal("(X: 3; Y: 4)", point.Value);
        Assert.Equal("TPoint", point.TypeName);
        Assert.Equal(ValueKind.Record, point.Kind);
        Assert.Equal(2, point.ChildCount);
        Assert.Equal(1, point.Children!.Value.Epoch);
        Assert.Equal("P", point.EvaluateName);
        Assert.Equal(DemoMemory.AddressOf("P"), point.Address);
        Assert.False(point.CanModify);
        var fields = await session.GetChildrenAsync(point.Children.Value, ct: Ct).WaitAsync(Safety, Ct);
        Assert.Equal(["X = 3", "Y = 4"], fields.Select(f => f.Name + " = " + f.Value));
        Assert.Equal(["P.X", "P.Y"], fields.Select(f => f.EvaluateName));

        Assert.Equal("nums[ 1 ]", element.Name);
        Assert.Equal("20", element.Value);
        Assert.Equal("nums[1]", element.EvaluateName);
        Assert.True(element.CanModify);
        Assert.Null(element.Children);

        Assert.Matches(@"^TDog\(\$[0-9A-F]{16}\)$", pet.Value);
        Assert.Equal("TDog", pet.TypeName);
        var petFields = await session.GetChildrenAsync(pet.Children!.Value, ct: Ct).WaitAsync(Safety, Ct);
        Assert.Equal(["Name = 'Rex'", "Legs = 4"], petFields.Select(f => f.Name + " = " + f.Value));

        // The frame decides what an identifier means: the main block has no variable "P".
        var other = await Assert.ThrowsAsync<DebuggerException>(() => DemoSession.EvaluateAsync(harness, "P", frames[1].Id));
        Assert.Equal(DebugErrorCode.ExpressionEvaluation, other.Code);
        Assert.Equal("Undeclared identifier 'P'", other.Message);
        await DemoSession.EndAsync(harness);
    }

    [Fact]
    [Trait("Req", "DBG-412")]
    [Trait("Req", "DBG-408")]
    public async Task Errors_SyntaxAndEvaluation_SessionStaysUsable()
    {
        await using var harness = await DemoSession.StoppedInRunAsync();
        var sent = harness.SentCommands().Count;

        var syntax = await Assert.ThrowsAsync<DebuggerException>(() => DemoSession.EvaluateAsync(harness, "Total +"));
        var hover = await Assert.ThrowsAsync<DebuggerException>(
            () => harness.Session.EvaluateAsync("Total +", null, EvaluateContext.Hover, Ct).WaitAsync(Safety, Ct));
        var undeclared = await Assert.ThrowsAsync<DebuggerException>(() => DemoSession.EvaluateAsync(harness, "Foo"));
        var stale = await Assert.ThrowsAsync<DebuggerException>(() => DemoSession.EvaluateAsync(harness, "Total", frameId: 4711));

        Assert.Equal(DebugErrorCode.ExpressionSyntax, syntax.Code);
        Assert.Equal("Expression expected at column 8", syntax.Message);       // 1-based column
        Assert.Equal(DebugErrorCode.ExpressionSyntax, hover.Code);             // Hover reports the same way
        Assert.Equal(DebugErrorCode.ExpressionEvaluation, undeclared.Code);
        Assert.Equal("Undeclared identifier 'Foo'", undeclared.Message);
        Assert.Equal(DebugErrorCode.StaleReference, stale.Code);
        Assert.Equal(sent, harness.SentCommands().Count);                      // none of them sent anything
        Assert.Equal("24", (await DemoSession.EvaluateAsync(harness, "Total")).Value);
        await DemoSession.EndAsync(harness);
    }

    [Fact]
    [Trait("Req", "DBG-403")]
    public async Task WithoutTypeInformation_EvaluationFails_SyntaxIsStillChecked()
    {
        await using var harness = SessionHarness.Create(DemoSession.Scenario());
        var missing = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"), "none.pdi");
        await harness.LaunchAsync(new LaunchRequest { Program = SessionHarness.Program, TypeInfoPath = missing });
        await harness.WaitForEventAsync("stopped");

        var evaluation = await Assert.ThrowsAsync<DebuggerException>(() => DemoSession.EvaluateAsync(harness, "Total"));
        var syntax = await Assert.ThrowsAsync<DebuggerException>(() => DemoSession.EvaluateAsync(harness, "Total +"));

        Assert.False(harness.Session.HasTypeInfo);
        Assert.Equal(DebugErrorCode.ExpressionEvaluation, evaluation.Code);
        Assert.Equal("Type information is not available", evaluation.Message);
        Assert.Equal(DebugErrorCode.ExpressionSyntax, syntax.Code);
        await DemoSession.EndAsync(harness);
    }
}
