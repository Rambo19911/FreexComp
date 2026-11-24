using System.Text.Json;
using FreexComp.Debugger.Expressions;
using FreexComp.Debugger.Tests.TestSupport;

namespace FreexComp.Debugger.Tests;

/// <summary>
/// <c>SetValueAsync</c> (Pascal values §8.4, §8.5; DAP usage §5.8; debug-session DBG-413): conversion rules,
/// <c>writeMemory</c>, cache drop, the <c>setVariable</c> fallback, <c>NotModifiable</c>.
/// </summary>
public sealed class SetValueTests
{
    private static readonly TimeSpan Safety = SessionHarness.Safety;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<VariableInfo> SetAsync(SessionHarness harness, string target, string value) =>
        harness.Session.SetValueAsync(target, value, null, Ct).WaitAsync(Safety, Ct);

    /// <summary>The (reference, bytes) of every <c>writeMemory</c> request sent so far.</summary>
    private static List<string> Writes(SessionHarness harness) =>
        [.. harness.SentRequests("writeMemory").Select(json =>
        {
            var arguments = JsonDocument.Parse(json).RootElement.GetProperty("arguments");
            return arguments.GetProperty("memoryReference").GetString() + "="
                + Convert.ToHexString(Convert.FromBase64String(arguments.GetProperty("data").GetString()!));
        })];

    private static string Reference(string variable, int offset = 0) => "0x" + (DemoMemory.AddressOf(variable) + (ulong)offset).ToString("X");

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Req", "DBG-413")]
    [Trait("Req", "DBG-177")]
    [Trait("Req", "DBG-261")]
    public async Task Integer_Written_LittleEndian()
    {
        await using var harness = await DemoSession.StoppedInRunAsync();
        Assert.Equal("24", (await DemoSession.EvaluateAsync(harness, "PTotal^")).Value);
        var readsBefore = harness.SentRequests("readMemory").Count;

        var total = await SetAsync(harness, " Total ", "99");

        // The request of DAP usage §5.8: reference without leading zeros, offset 0, no partial write, base64 of 63 00 00 00.
        Assert.Equal(
            """{"memoryReference":"0x7F3A6FFB04","offset":0,"allowPartial":false,"data":"YwAAAA=="}""",
            harness.SentArguments("writeMemory"));
        // The node read again.
        Assert.Equal("Total", total.Name);
        Assert.Equal("99", total.Value);
        Assert.Equal("Integer", total.TypeName);
        Assert.Equal("Total", total.EvaluateName);
        Assert.True(total.CanModify);
        Assert.Equal(DemoMemory.AddressOf("Total"), total.Address);
        // What was read before the write is dropped: an alias of the same memory shows the new value.
        Assert.True(harness.SentRequests("readMemory").Count > readsBefore);
        Assert.Equal("99", (await DemoSession.EvaluateAsync(harness, "PTotal^")).Value);

        // The new value is an expression of the same evaluator and frame; the target any modifiable location.
        Assert.Equal("199", (await SetAsync(harness, "Nums[1]", "Total * 2 + 1")).Value);
        Assert.Equal("(10, 199, 30)", (await DemoSession.EvaluateAsync(harness, "Nums")).Value);
        Assert.Equal("-2", (await SetAsync(harness, "P.Y", "-2")).Value);
        Assert.Equal("7", (await SetAsync(harness, "PTotal^", "P.X + 4")).Value);
        Assert.Equal("5", (await SetAsync(harness, "Pet.Legs", "5")).Value);
        var writes = Writes(harness);
        Assert.Equal(Reference("Total") + "=63000000", writes[0]);
        Assert.EndsWith("=C7000000", writes[1]);
        Assert.Equal(Reference("P", 4) + "=FEFFFFFF", writes[2]);
        Assert.Equal(Reference("Total") + "=07000000", writes[3]);
        Assert.Equal(DebugState.Stopped, harness.Session.State);
        await DemoSession.EndAsync(harness);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Req", "DBG-177")]
    public async Task OutOfRange_Error()
    {
        await using var harness = await DemoSession.StoppedInRunAsync();

        var tooBig = await Assert.ThrowsAsync<DebuggerException>(() => SetAsync(harness, "Total", "3000000000"));
        var tooSmall = await Assert.ThrowsAsync<DebuggerException>(() => SetAsync(harness, "Total", "-2147483649"));
        var text = await Assert.ThrowsAsync<DebuggerException>(() => SetAsync(harness, "Total", "'abc'"));
        var real = await Assert.ThrowsAsync<DebuggerException>(() => SetAsync(harness, "Total", "1.5"));
        var syntax = await Assert.ThrowsAsync<DebuggerException>(() => SetAsync(harness, "Total", "1 +"));
        var undeclared = await Assert.ThrowsAsync<DebuggerException>(() => SetAsync(harness, "Total", "Foo"));

        Assert.Equal(DebugErrorCode.ExpressionEvaluation, tooBig.Code);
        Assert.Equal("Value out of range for Integer", tooBig.Message);
        Assert.Equal("Value out of range for Integer", tooSmall.Message);
        Assert.Equal("Incompatible types: string and Integer", text.Message);
        Assert.Equal("Incompatible types: Double and Integer", real.Message);
        Assert.Equal(DebugErrorCode.ExpressionSyntax, syntax.Code);
        Assert.Equal("Undeclared identifier 'Foo'", undeclared.Message);
        // Nothing was written; the limits themselves are fine.
        Assert.Empty(Writes(harness));
        Assert.Equal("2147483647", (await SetAsync(harness, "Total", "2147483647")).Value);
        Assert.Equal("-2147483648", (await SetAsync(harness, "Total", "-2147483648")).Value);
        await DemoSession.EndAsync(harness);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Req", "DBG-177")]
    public async Task Enum_Bool_Char()
    {
        await using var harness = await DemoSession.StoppedInRunAsync();

        Assert.Equal("clBlue", (await SetAsync(harness, "Color", "clBlue")).Value);
        Assert.Equal("clRed", (await SetAsync(harness, "Color", "Pred(clGreen)")).Value);
        Assert.Equal("False", (await SetAsync(harness, "Ok", "False")).Value);
        Assert.Equal("True", (await SetAsync(harness, "Ok", "Total = 24")).Value);
        Assert.Equal("'B'", (await SetAsync(harness, "Ch", "'B'")).Value);
        Assert.Equal("#13", (await SetAsync(harness, "Ch", "#13")).Value);
        Assert.Equal("'é'", (await SetAsync(harness, "Ch", "S[2]")).Value);
        Assert.Equal("nil", (await SetAsync(harness, "PTotal", "nil")).Value);
        Assert.Equal("$0000007F3A6FFB00", (await SetAsync(harness, "PTotal", "@I")).Value);
        Assert.Equal("$0000000000001000", (await SetAsync(harness, "PTotal", "$1000")).Value);

        Assert.Equal(
        [
            Reference("Color") + "=02", Reference("Color") + "=00",
            Reference("Ok") + "=00", Reference("Ok") + "=01",
            Reference("Ch") + "=4200", Reference("Ch") + "=0D00", Reference("Ch") + "=E900",
            Reference("PTotal") + "=0000000000000000", Reference("PTotal") + "=00FB6F3A7F000000", Reference("PTotal") + "=0010000000000000",
        ], Writes(harness));

        // An enum only from the same enum type, a Boolean only from a Boolean, a Char from a Char or a one-character string.
        async Task<string> RefusedAsync(string target, string value) =>
            (await Assert.ThrowsAsync<DebuggerException>(() => SetAsync(harness, target, value))).Message;
        Assert.Equal("Incompatible types: Integer and TColor", await RefusedAsync("Color", "2"));
        Assert.Equal("Incompatible types: Integer and Boolean", await RefusedAsync("Ok", "1"));
        Assert.Equal("Incompatible types: Integer and Char", await RefusedAsync("Ch", "65"));
        Assert.Equal("Incompatible types: string and Char", await RefusedAsync("Ch", "'ab'"));
        Assert.Equal("Incompatible types: Boolean and ^Integer", await RefusedAsync("PTotal", "True"));
        Assert.Equal(10, Writes(harness).Count);
        await DemoSession.EndAsync(harness);
    }

    [Theory]
    // integer → integer, range-checked against the target's size and signedness
    [InlineData("B", "255", "FF")]
    [InlineData("B", "256", "Value out of range for Byte")]
    [InlineData("B", "-1", "Value out of range for Byte")]
    [InlineData("Sh", "-128", "80")]
    [InlineData("Sh", "128", "Value out of range for ShortInt")]
    [InlineData("W", "65535", "FFFF")]
    [InlineData("C32", "4294967295", "FFFFFFFF")]
    [InlineData("C32", "4294967296", "Value out of range for Cardinal")]
    [InlineData("L", "-1", "FFFFFFFFFFFFFFFF")]
    [InlineData("L", "18446744073709551615", "Value out of range for Int64")]
    [InlineData("U", "18446744073709551615", "FFFFFFFFFFFFFFFF")]
    [InlineData("U", "-1", "Value out of range for UInt64")]
    [InlineData("B", "Sh + 10", "05")]
    // integer → float, float → float
    [InlineData("F", "1.5", "0000C03F")]
    [InlineData("F", "2", "00000040")]
    [InlineData("D", "2", "0000000000000040")]
    [InlineData("D", "F / 2", "000000000000E83F")]
    [InlineData("Cur", "12.5", "48E8010000000000")]
    [InlineData("Cur", "-1", "F0D8FFFFFFFFFFFF")]
    [InlineData("D", "'a'", "Incompatible types: Char and Double")]
    [InlineData("F", "True", "Incompatible types: Boolean and Single")]
    // enum only from the same enum type
    [InlineData("Col", "clRed", "00")]
    [InlineData("Big", "bgLast", "2C01")]
    [InlineData("Col", "bgLast", "Incompatible types: TBig and TColor")]
    // boolean ← boolean; char ← char or one-character string
    [InlineData("Flag", "not Flag", "00")]
    [InlineData("WC", "'z'", "7A00")]
    [InlineData("AC", "'z'", "7A")]
    [InlineData("AC", "WC", "E9")]
    [InlineData("AC", "Chr(300)", "Value out of range for AnsiChar")]
    [InlineData("WC", "Short[1]", "6800")]
    // pointer ← nil, integer or pointer
    [InlineData("PI", "nil", "0000000000000000")]
    [InlineData("PI", "4096", "0010000000000000")]
    [InlineData("PI", "PPI^", "C405030000000000")]
    [InlineData("Untyped", "@Pt", "4007030000000000")]
    [InlineData("PI", "Animal", "Incompatible types: TDog and ^Integer")]
    [InlineData("PI", "1.5", "Incompatible types: Double and ^Integer")]
    public async Task Conversion_Table(string target, string value, string expected)
    {
        var bed = ZooBed.Test();
        var (location, _, _, _) = await bed.Values.ResolveTargetAsync(target, bed.Frame, Ct);

        string actual;
        try
        {
            actual = Convert.ToHexString(await bed.Values.EvaluateForAssignmentAsync(value, location.Type, bed.Frame, Ct));
        }
        catch (DebuggerException e) when (e.Code == DebugErrorCode.ExpressionEvaluation)
        {
            actual = e.Message;
        }

        Assert.Equal(expected, actual);
        Assert.DoesNotContain(bed.Debuggee.Requests, request => request.StartsWith("write", StringComparison.Ordinal));
    }

    private const string SetVariableOnly =
        """{ "supportsConfigurationDoneRequest": true, "supportsReadMemoryRequest": true, "supportsSetVariable": true }""";

    private const string Scopes = """
        { "on": "scopes", "expectArguments": { "frameId": 524288 }, "respond": { "scopes": [
            { "name": "Locals", "presentationHint": "locals", "variablesReference": 11 },
            { "name": "Globals", "variablesReference": 12 } ] } }
        """;

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Req", "DBG-176")]
    public async Task Fallback_SetVariable()
    {
        await using var harness = await DemoSession.StoppedInRunAsync($$"""
            {{DemoSession.Stack(71)}},
            {{Scopes}},
            { "on": "variables", "expectArguments": { "variablesReference": 11 }, "respond": {{DemoMemory.LocalsResponseJson}} },
            { "on": "setVariable", "expectArguments": { "variablesReference": 11, "name": "Total", "value": "99" }, "respond": { "value": "99" } },
            {{Scopes}},
            { "on": "setVariable", "expectArguments": { "variablesReference": 11, "name": "Color", "value": "2" }, "respond": { "value": "2" } },
            {{Scopes}},
            { "on": "setVariable", "expectArguments": { "variablesReference": 11, "name": "Ch", "value": "66" }, "respond": { "value": "66" } },
            {{Scopes}},
            { "on": "setVariable", "expectArguments": { "variablesReference": 11, "name": "PTotal", "value": "0x1000" }, "respond": { "value": "0x1000" } },
            {{Scopes}},
            { "on": "setVariable", "expectArguments": { "variablesReference": 11, "name": "Ok", "value": "0" }, "fail": "variable is read-only" }
            """, capabilities: SetVariableOnly);
        var session = harness.Session;
        var frame = (await session.GetStackTraceAsync(DemoSession.ThreadId, ct: Ct).WaitAsync(Safety, Ct)).Frames[0];
        var locals = await session.GetChildrenAsync((await session.GetScopesAsync(frame.Id, Ct).WaitAsync(Safety, Ct))[1].Handle, ct: Ct).WaitAsync(Safety, Ct);

        // Without writeMemory only root scalars can be modified: through setVariable by name.
        Assert.Equal(["I", "Total", "Ch", "Ok", "Color", "PTotal"], locals.Where(v => v.CanModify).Select(v => v.Name));
        var point = locals.Single(v => v.Name == "P");
        Assert.All(await session.GetChildrenAsync(point.Children!.Value, ct: Ct).WaitAsync(Safety, Ct), field => Assert.False(field.CanModify));

        // The value text: decimal integer; enums, Booleans and characters as their ordinal; a pointer as 0x….
        // The declared spelling of the variable is sent, in the adapter's scope that lists it.
        Assert.Equal("total", (await session.SetValueAsync("total", "99", frame.Id, Ct).WaitAsync(Safety, Ct)).Name);
        await SetAsync(harness, "Color", "clBlue");
        await SetAsync(harness, "Ch", "'B'");
        await SetAsync(harness, "PTotal", "4096");
        var rejected = await Assert.ThrowsAsync<DebuggerException>(() => SetAsync(harness, "Ok", "False"));
        Assert.Equal(DebugErrorCode.RequestFailed, rejected.Code);
        Assert.Equal("variable is read-only", rejected.Message);

        // Anything that is not a root variable cannot be modified at all.
        foreach (var target in new[] { "P.X", "Nums[0]", "PTotal^", "Pet.Legs" })
        {
            var exception = await Assert.ThrowsAsync<DebuggerException>(() => SetAsync(harness, target, "1"));
            Assert.Equal(DebugErrorCode.NotModifiable, exception.Code);
        }

        Assert.DoesNotContain("writeMemory", harness.SentCommands());
        Assert.Equal(4 + 1, harness.SentRequests("setVariable").Count);
        await DemoSession.EndAsync(harness);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Req", "DBG-413")]
    [Trait("Req", "DBG-176")]
    public async Task String_NotModifiable()
    {
        await using var harness = await DemoSession.StoppedInRunAsync();

        // Strings, sets, arrays and records as a whole, instance references; and anything that is not a location.
        foreach (var target in new[] { "S", "Colors", "P", "Nums", "Pet", "Pet.Name", "Total + 1", "24", "@Total", "S[1]" })
        {
            var exception = await Assert.ThrowsAsync<DebuggerException>(() => SetAsync(harness, target, "1"));
            Assert.Equal(DebugErrorCode.NotModifiable, exception.Code);
            Assert.Equal($"'{target}' cannot be modified.", exception.Message);
        }

        // Errors in the target itself keep their own code.
        var undeclared = await Assert.ThrowsAsync<DebuggerException>(() => SetAsync(harness, "Foo", "1"));
        Assert.Equal(DebugErrorCode.ExpressionEvaluation, undeclared.Code);
        Assert.Empty(Writes(harness));
        Assert.DoesNotContain("setVariable", harness.SentCommands());
        await DemoSession.EndAsync(harness);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Req", "DBG-176")]
    public async Task NoWriteCapability_NothingIsModifiable_IncompleteWriteFails()
    {
        await using (var readOnly = await DemoSession.StoppedInRunAsync(
                         capabilities: """{ "supportsConfigurationDoneRequest": true, "supportsReadMemoryRequest": true }"""))
        {
            Assert.False((await DemoSession.EvaluateAsync(readOnly, "Total")).CanModify);
            var exception = await Assert.ThrowsAsync<DebuggerException>(() => SetAsync(readOnly, "Total", "1"));
            Assert.Equal(DebugErrorCode.NotModifiable, exception.Code);
            Assert.DoesNotContain("writeMemory", readOnly.SentCommands());
            Assert.DoesNotContain("setVariable", readOnly.SentCommands());
            await DemoSession.EndAsync(readOnly);
        }

        await using var harness = await DemoSession.StoppedInRunAsync("""
            { "on": "writeMemory", "respond": { "bytesWritten": 2 } },
            { "on": "writeMemory", "fail": "memory is not writable" }
            """);
        var incomplete = await Assert.ThrowsAsync<DebuggerException>(() => SetAsync(harness, "Total", "1"));
        var failed = await Assert.ThrowsAsync<DebuggerException>(() => SetAsync(harness, "Total", "1"));
        Assert.Equal(DebugErrorCode.RequestFailed, incomplete.Code);
        Assert.Equal("Memory write was incomplete.", incomplete.Message);
        Assert.Equal("memory is not writable", failed.Message);
        Assert.Equal(DebugState.Stopped, harness.Session.State);
        await DemoSession.EndAsync(harness);
    }
}
