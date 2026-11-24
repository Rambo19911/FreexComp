using FreexComp.Compiler.Tests.Emit;

namespace FreexComp.Compiler.Tests.Lowering;

/// <summary>
/// C-301 (LANG-687, CMP-1204): the result temporary of a <c>safecall</c> call, checked on the emitted module of one routine.
/// </summary>
[Trait("Phase", "8")]
public sealed class SafecallLoweringTests
{
    private const string Source = """
        unit M;
        interface
        type
          IBar = interface
            function GetValue: Integer; safecall;
            function GetName(N: Integer): string; safecall;
          end;
        procedure Names(const Obj: IBar; var Last: string);
        procedure Value(const Obj: IBar; var V: Integer);
        implementation
        procedure Names(const Obj: IBar; var Last: string);
        var I: Integer;
        begin
          for I := 1 to 3 do
            Last := Obj.GetName(I);
        end;
        procedure Value(const Obj: IBar; var V: Integer);
        var Local: Integer;
        begin
          Local := 1;
          Local := Obj.GetValue;
          V := Obj.GetValue;
        end;
        end.
        """;

    /// <summary>The lines of the definition of <paramref name="routine"/> in the module text.</summary>
    private static List<string> Body(string routine)
    {
        var lines = IrSnapshotTests.Emit(Source).Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var start = lines.FindIndex(l => l.StartsWith("define ", StringComparison.Ordinal) && l.Contains($"@\"M.{routine}\"(", StringComparison.Ordinal));
        Assert.True(start >= 0, routine + " is not defined");
        return lines.GetRange(start, lines.FindIndex(start, l => l == "}") - start);
    }

    /// <summary>The first line at or after <paramref name="from"/> that contains every one of <paramref name="parts"/>.</summary>
    private static int Find(List<string> body, int from, params string[] parts)
    {
        var index = body.FindIndex(from, l => parts.All(p => l.Contains(p, StringComparison.Ordinal)));
        Assert.True(index >= 0, "no line with " + string.Join(" + ", parts) + " after line " + from + ":\n" + string.Join("\n", body));
        return index;
    }

    [Fact]
    [Trait("Req", "LANG-687")]
    public void ManagedResultTemporaryClearedBeforeEachCall()
    {
        var body = Body("Names");

        // inside the loop: _UStrClr(@tmp) → the call with @tmp as last argument → the check → _UStrAsg(@Last, tmp)
        var loop = Find(body, 0, "for.body");
        var call = Find(body, loop, " i32 %", "ptr %$tmp", ")");
        var temporary = body[call][body[call].LastIndexOf("%$tmp", StringComparison.Ordinal)..].TrimEnd(')');
        var clear = Find(body, loop, "@\"System._UStrClr\"(ptr " + temporary + ")");
        Assert.True(clear < call, "the temporary is finalised before the call");
        Assert.DoesNotContain(body.GetRange(clear + 1, call - clear - 1), l => l.Contains("br label %for.", StringComparison.Ordinal));   // same iteration
        var check = Find(body, call, "icmp slt i32", ", 0");
        var assign = Find(body, check, "@\"System._UStrAsg\"(ptr %", "Last");
        Assert.True(Find(body, call, "@\"System._SafecallError\"(i32") < assign);
        var next = Find(body, assign, "br label %for.");
        Assert.True(next > assign);
    }

    [Fact]
    [Trait("Req", "CMP-1204")]
    public void FailedCallLeavesDestinationUnchanged()
    {
        var body = Body("Value");

        // both calls receive a compiler temporary, never the destination; the destination is stored only after the check passed
        var from = 0;
        foreach (var destination in new[] { "%Local.addr", "%V" })
        {
            var call = Find(body, from, " i32 %", "ptr %$tmp", ")");
            Assert.DoesNotContain(destination + ")", body[call], StringComparison.Ordinal);
            var check = Find(body, call, "icmp slt i32", ", 0");
            var fail = Find(body, check, "@\"System._SafecallError\"(i32");
            var unreachable = Find(body, fail, "unreachable");
            var store = Find(body, call, "store i32 %", destination);
            Assert.True(store > unreachable, $"the store to {destination} follows the failure block");
            Assert.DoesNotContain(body.GetRange(call, unreachable - call), l => l.Contains("store i32", StringComparison.Ordinal) && l.Contains(destination, StringComparison.Ordinal));
            from = store;
        }
    }
}
