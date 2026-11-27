namespace FreexComp.Compiler.Tests.Emit;

/// <summary>
/// C-390 (dynamic-array-operators.md CMP-1417, LANG-275, 276): a chain of <c>+</c> on dynamic arrays is one <c>_DynArrayCat(var Dest,
/// TI, n, Sources)</c>: the operands left to right into a stack <c>[n x ptr]</c> (a variable by its value, a constructor built in a
/// temporary released after the call); the assignment target is <c>Dest</c> (its address taken once, before the operands, unless it
/// is a plain variable), elsewhere a fresh temporary is.
/// </summary>
public sealed partial class IrSnapshotTests
{
    internal const string DynArrayConcatAssignSource = """
        unit M;
        interface
        type
          TInts = array of Integer;
          TGrid = array of TInts;
        procedure Append(var A: TInts; const B: TInts; X: Integer);
        function Build(N: Integer): TInts;
        procedure Grow(var G: TGrid; const B: TInts);
        implementation
        procedure Append(var A: TInts; const B: TInts; X: Integer);
        begin
          A := A + B + [X];
        end;
        function Build(N: Integer): TInts;
        var
          I: Integer;
        begin
          for I := 1 to N do
            Result := Result + [I];
        end;
        procedure Grow(var G: TGrid; const B: TInts);
        begin
          G[0] := G[0] + B;
        end;
        end.
        """;

    internal const string DynArrayConcatValueSource = """
        unit M;
        interface
        type
          TInts = array of Integer;
        function Total(const A: TInts): Integer;
        function Twice(const A: TInts): Integer;
        implementation
        function Total(const A: TInts): Integer;
        var
          X: Integer;
        begin
          Result := 0;
          for X in A do
            Inc(Result, X);
        end;
        function Twice(const A: TInts): Integer;
        begin
          Result := Total(A + A) + Length(nil + A + []);
        end;
        end.
        """;

    /// <summary>The modules of this file by snapshot name.</summary>
    internal static readonly IReadOnlyDictionary<string, string> DynArrayConcatSources = new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
        ["DynArrayConcatAssign"] = DynArrayConcatAssignSource,
        ["DynArrayConcatValue"] = DynArrayConcatValueSource,
    };

    /// <summary>
    /// CMP-1417: <c>A := A + B + [X]</c> is one <c>_DynArrayCat</c> with <c>Dest</c> = the parameter <c>A</c>, <c>Sources</c> of
    /// three (<c>A</c> itself first, so the run time may append in place), the constructor temporary cleared after the call; the
    /// loop <c>Result := Result + [I]</c> one call per iteration; <c>G[0] := G[0] + B</c> takes the element's address once, first.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1417")]
    [Trait("Req", "LANG-276")]
    public void DynArrayConcatAssign() => Snapshot.Verify("DynArrayConcatAssign", Emit(DynArrayConcatAssignSource));

    /// <summary>
    /// CMP-1417: in argument position the chain writes into a fresh temporary <c>Dest</c> that is then the value; <c>nil</c> and
    /// <c>[]</c> operands are <c>null</c> sources.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1417")]
    [Trait("Req", "LANG-276")]
    public void DynArrayConcatValue() => Snapshot.Verify("DynArrayConcatValue", Emit(DynArrayConcatValueSource));
}

/// <summary>The modules of <see cref="IrSnapshotTests.DynArrayConcatSources"/> are accepted by the pinned clang (C-390).</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Native")]
[Trait("Category", "Toolchain")]
public sealed class DynArrayConcatToolchainTests
{
    public static TheoryData<string, string> Modules()
    {
        var data = new TheoryData<string, string>();
        foreach (var optimisation in new[] { "-O0", "-O2" })
        {
            foreach (var name in IrSnapshotTests.DynArrayConcatSources.Keys)
            {
                data.Add(name, optimisation);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Modules))]
    [Trait("Req", "CMP-1417")]
    public async Task DynArrayConcatModule_CompilesWithPinnedClang(string name, string optimisation)
    {
        var text = IrSnapshotTests.Emit(IrSnapshotTests.DynArrayConcatSources[name]);

        var (exitCode, output) = await Clang.CompileToObjectAsync(text, optimisation);

        Assert.True(exitCode == 0, "clang failed:\n" + output);
    }
}
