namespace FreexComp.Compiler.Tests.Emit;

/// <summary>
/// ABI-080…082 aggregate lowering (C-055): records, static arrays and big sets of size 1, 2, 4, 8 travel as <c>iN</c>; other
/// sizes by <c>ptr</c> (value: a copy the caller makes, <c>const</c>: the original); such results through a hidden
/// <c>ptr Result</c> after the static link. Snapshot <c>AggregateAbi.ll</c> (also in the toolchain theory).
/// </summary>
[Trait("Phase", "2")]
[Trait("Kind", "Snapshot")]
public sealed class AbiLoweringTests
{
    internal const string Source = """
        unit M;
        interface
        type
          TSmall = record A, B: Word; end;
          TPoint = record X, Y: Integer; end;
          TTriple = record X, Y, Z: Integer; end;
          TBytes3 = array[0..2] of Byte;
          TPacked = packed record B: Byte; T: TTriple; P: TPoint; end;
          TLetters = set of Char;
        var
          G: TTriple;
          GP: TPacked;
        function Small(s: TSmall): TSmall;
        function Point(p: TPoint; const q: TPoint): TPoint;
        function Triple(t: TTriple; const c: TTriple; var v: TTriple): TTriple;
        function Bytes(b: TBytes3): TBytes3;
        function Letters(const s: TLetters): TLetters;
        procedure Use;
        implementation
        function Small(s: TSmall): TSmall;
        begin
          Result := s;
        end;
        function Point(p: TPoint; const q: TPoint): TPoint;
        begin
          Result.X := p.X + q.X;
          Result.Y := p.Y;
        end;
        function Triple(t: TTriple; const c: TTriple; var v: TTriple): TTriple;
          procedure Bump;
          begin
            t.X := c.Y;
            Result := t;
          end;
        begin
          Bump;
          v := c;
        end;
        function Bytes(b: TBytes3): TBytes3;
        begin
          Result := b;
        end;
        function Letters(const s: TLetters): TLetters;
        begin
          Result := s + ['a'];
        end;
        procedure Use;
        var
          t: TTriple;
          p: TPoint;
          l: TLetters;
        begin
          p := Point(p, Point(p, p));
          t := Triple(G, G, t);
          Triple(t, GP.T, t);
          p := Point(GP.P, GP.P);
          l := Letters(['x']);
          G.X := Triple(t, t, t).Y;
        end;
        end.
        """;

    [Fact]
    [Trait("Req", "ABI-080")]
    [Trait("Req", "ABI-081")]
    [Trait("Req", "ABI-082")]
    public void AggregateAbi() => Snapshot.Verify("AggregateAbi", IrSnapshotTests.Emit(Source));

    [Fact]
    [Trait("Req", "ABI-080")]
    [Trait("Req", "ABI-081")]
    public void SmallAggregates_AreIntegersOfTheirSize()
    {
        var text = IrSnapshotTests.Emit(Source);

        Assert.Contains("define i32 @\"M.Small\"(i32 %s) #0 {", text);
        Assert.Contains("define i64 @\"M.Point\"(i64 %p, i64 %q) #0 {", text);
        Assert.Contains("  store i64 %p, ptr %p.addr, align 4\n", text);                  // spilled into the record's alloca
        Assert.Contains("  %p.addr = alloca %\"M.TPoint\", align 4\n", text);
        var point = Body(text, "M.Point");
        Assert.Matches(@"  %t\d+ = load i64, ptr %Result.addr, align 4\n  ret i64 %t\d+\n", point);
    }

    [Fact]
    [Trait("Req", "ABI-080")]
    [Trait("Req", "ABI-081")]
    [Trait("Req", "ABI-082")]
    public void LargeAggregates_GoByAddress_ResultsThroughAHiddenPointer()
    {
        var text = IrSnapshotTests.Emit(Source);

        Assert.Contains("define void @\"M.Triple\"(ptr %Result, ptr %t, ptr %c, ptr %v) #0 {", text);
        Assert.Contains("define void @\"M.Bytes\"(ptr %Result, ptr %b) #0 {", text);
        Assert.Contains("define void @\"M.Letters\"(ptr %Result, ptr %s) #0 {", text);
        // nested routine: static link first, then the hidden result (ABI-082); captured by-address variables keep pointers
        Assert.Contains("%\"M.Triple.$frame\" = type { ptr, ptr, ptr, ptr }\n", text);
        var triple = Body(text, "M.Triple");
        Assert.DoesNotContain("alloca %\"M.TTriple\"", triple);
        Assert.Contains("  ret void\n", triple);
    }

    [Fact]
    [Trait("Req", "ABI-080")]
    [Trait("Req", "ABI-081")]
    public void Callers_CopyValueArguments_PassConstOriginals_AndProvideResultStorage()
    {
        var use = Body(IrSnapshotTests.Emit(Source), "M.Use");

        // value TTriple: copied into a temporary first; const TTriple: the global itself; var: the variable
        Assert.Matches(@"call void @llvm\.memcpy\.p0\.p0\.i64\(ptr align 4 %\$tmp\d+\.addr, ptr align 4 @""M\.G"", i64 12, i1 false\)", use);
        Assert.Matches(@"call void @""M\.Triple""\(ptr %\$tmp\d+\.addr, ptr %\$tmp\d+\.addr, ptr @""M\.G"", ptr %t\.addr\)", use);
        // a const argument inside a packed record is copied (it may be less aligned than its type, CMP-309)
        Assert.DoesNotMatch(@"call void @""M\.Triple""\([^\n]*ptr %t\d+, ptr %t\.addr\)", use);
        // small aggregates: loaded as iN at the access alignment (align 1 in the packed record)
        Assert.Matches(@"load i64, ptr %t\d+, align 1\n", use);
        Assert.Matches(@"%t\d+ = call i64 @""M\.Point""\(i64 %t\d+, i64 %t\d+\)\n  store i64 %t\d+, ptr %\$tmp\d+\.addr, align 4\n", use);
        // a constant big set passed const is the private constant
        Assert.Contains("call void @\"M.Letters\"(ptr %$tmp", use);
        Assert.Contains(", ptr @.set.1)", use);                               // ['x'] (.set.0 is ['a'])
    }

    private static string Body(string text, string name)
    {
        var start = text.IndexOf($"@\"{name}\"(", StringComparison.Ordinal);
        Assert.True(start >= 0, name + " not found");
        return text[start..(text.IndexOf("\n}\n", start, StringComparison.Ordinal) + 2)];
    }
}
