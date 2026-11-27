namespace FreexComp.Compiler.Tests.Emit;

/// <summary>
/// C-391 (dynamic-array-operators.md CMP-1418, LANG-277…279): <c>Insert</c> element form → the converted value in a temporary of
/// the element type and <c>_DynArrayInsert(@A, TI, Index, @tmp, 1)</c> (a managed temporary cleared after the call); array form →
/// the source in a counted temporary (a constructor built there) and <c>_DynArrayInsert(@A, TI, Index, src, Length(src))</c>;
/// <c>Delete</c> → <c>_DynArrayDelete(@A, TI, Index, Count)</c>; the integers as <c>iptr</c>; <c>Concat(A)</c> is the one-source
/// <c>_DynArrayCat</c> chain of CMP-1417.
/// </summary>
public sealed partial class IrSnapshotTests
{
    internal const string DynArrayInsertDeleteSource = """
        unit M;
        interface
        type
          TInts = array of Integer;
          TStrs = array of string;
        procedure Edit(var A: TInts; const B: TInts; I: Integer; S: SmallInt);
        procedure EditStrs(var A: TStrs; const W: string);
        function Copied(const A: TInts): TInts;
        implementation
        procedure Edit(var A: TInts; const B: TInts; I: Integer; S: SmallInt);
        begin
          Insert(I, A, S);
          Insert(B, A, 0);
          Insert([I, 2], A, Length(A));
          Insert(nil, A, 1);
          Delete(A, I, S);
        end;
        procedure EditStrs(var A: TStrs; const W: string);
        begin
          Insert(W + '!', A, 0);
          Insert(A, A, 1);
          Delete(A, 0, 1);
        end;
        function Copied(const A: TInts): TInts;
        begin
          Result := Concat(A);
        end;
        end.
        """;

    /// <summary>The modules of this file by snapshot name.</summary>
    internal static readonly IReadOnlyDictionary<string, string> DynArrayInsertDeleteSources = new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
        ["DynArrayInsertDelete"] = DynArrayInsertDeleteSource,
    };

    /// <summary>
    /// CMP-1418: the element form with an integer and with a managed (string) temporary cleared after the call; the array form
    /// from a parameter, a constructor and <c>A</c> itself (each through a counted temporary), <c>nil</c> as a null source of
    /// length 0; <c>Delete</c> with sign-extended integers; <c>Concat(A)</c> as a one-source <c>_DynArrayCat</c>.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1418")]
    [Trait("Req", "LANG-277")]
    [Trait("Req", "LANG-278")]
    [Trait("Req", "LANG-279")]
    public void DynArrayInsertDelete() => Snapshot.Verify("DynArrayInsertDelete", Emit(DynArrayInsertDeleteSource));
}

/// <summary>The modules of <see cref="IrSnapshotTests.DynArrayInsertDeleteSources"/> are accepted by the pinned clang (C-391).</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Native")]
[Trait("Category", "Toolchain")]
public sealed class DynArrayInsertDeleteToolchainTests
{
    public static TheoryData<string, string> Modules()
    {
        var data = new TheoryData<string, string>();
        foreach (var optimisation in new[] { "-O0", "-O2" })
        {
            foreach (var name in IrSnapshotTests.DynArrayInsertDeleteSources.Keys)
            {
                data.Add(name, optimisation);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Modules))]
    [Trait("Req", "CMP-1418")]
    public async Task DynArrayInsertDeleteModule_CompilesWithPinnedClang(string name, string optimisation)
    {
        var text = IrSnapshotTests.Emit(IrSnapshotTests.DynArrayInsertDeleteSources[name]);

        var (exitCode, output) = await Clang.CompileToObjectAsync(text, optimisation);

        Assert.True(exitCode == 0, "clang failed:\n" + output);
    }
}
