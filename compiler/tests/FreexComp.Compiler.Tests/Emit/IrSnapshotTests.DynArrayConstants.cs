namespace FreexComp.Compiler.Tests.Emit;

/// <summary>
/// C-389 (dynamic-array-operators.md CMP-1416, LANG-274): typed dynamic-array constants are static blocks — header RefCount −1,
/// Capacity 0, Length n, then the elements (string elements as literal references, nested arrays as the address of element 0 of
/// their own block) — in private read-only data; the constant itself is a read-only pointer global (external in the interface,
/// internal otherwise; <c>null</c> for <c>[]</c>); an inline constant's global is defined by the module that uses it.
/// </summary>
public sealed partial class IrSnapshotTests
{
    internal const string DynArrayConstantSource = """
        unit M;
        interface
        type
          TInts = array of Integer;
          TGrid = array of TInts;
          TPair = record N: Integer; S: string end;
        const
          Primes: TInts = [2, 3, 5, 7];
          None: TInts = [];
          Grid: TGrid = [[1], [2, 3], []];
        function Second: Integer;
        implementation
        const
          Names: array of string = ['ab', ''];
          Pairs: array of TPair = [(N: 1; S: 'x'), (N: 2)];
          Reals: array of Double = [1.5];
        function Second: Integer;
        const
          Local: TInts = [10, 20];
        var
          B: TInts;
        begin
          B := Primes;
          Result := Primes[1] + Length(Grid[1]) + Local[1] + Length(Names[0]) + Pairs[0].N + Trunc(Reals[0]);
          for var X in Primes do
            Inc(Result, X);
          begin
            const C: TInts = [4];
            Inc(Result, C[0]);
          end;
          begin
            const C: TInts = [5, 6];
            Inc(Result, C[1]);
          end;
        end;
        end.
        """;

    /// <summary>The modules of this file by snapshot name.</summary>
    internal static readonly IReadOnlyDictionary<string, string> DynArrayConstantSources = new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
        ["DynArrayConstant"] = DynArrayConstantSource,
    };

    /// <summary>
    /// CMP-1416: one <c>.dynarr.N</c> block per non-empty constructor (<c>&lt;{ i32 -1, i32 0, i64 n, [n x T] }&gt;</c>, align 16,
    /// <c>private unnamed_addr constant</c>); <c>@"M.Primes"</c> holds the GEP to element 0, <c>@"M.None"</c> null; a nested block
    /// before the block holding its address; strings as <c>@.str.N</c> payload references; the two inline constants <c>C</c> as
    /// <c>@"M.Second.C"</c> and <c>@"M.Second.C.1"</c>; reading is a load, assigning <c>_DynArrayAsg</c> (the RTL copies a static source).
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1416")]
    [Trait("Req", "LANG-274")]
    public void DynArrayConstant() => Snapshot.Verify("DynArrayConstant", Emit(DynArrayConstantSource));
}

/// <summary>The modules of <see cref="IrSnapshotTests.DynArrayConstantSources"/> are accepted by the pinned clang (C-389).</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Native")]
[Trait("Category", "Toolchain")]
public sealed class DynArrayConstantToolchainTests
{
    public static TheoryData<string, string> Modules()
    {
        var data = new TheoryData<string, string>();
        foreach (var optimisation in new[] { "-O0", "-O2" })
        {
            foreach (var name in IrSnapshotTests.DynArrayConstantSources.Keys)
            {
                data.Add(name, optimisation);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Modules))]
    [Trait("Req", "CMP-1416")]
    public async Task DynArrayConstantModule_CompilesWithPinnedClang(string name, string optimisation)
    {
        var text = IrSnapshotTests.Emit(IrSnapshotTests.DynArrayConstantSources[name]);

        var (exitCode, output) = await Clang.CompileToObjectAsync(text, optimisation);

        Assert.True(exitCode == 0, "clang failed:\n" + output);
    }
}
