namespace FreexComp.Compiler.Tests.Emit;

/// <summary>
/// C-387 (inline-variables.md LANG-577, CMP-1410…1412): a <c>for var</c> variable an anonymous method captures has one instance
/// per iteration — the loop's block frame <c>$Block&lt;m&gt;</c>, made at the start of every iteration and released with the
/// loop; a loop whose variable no anonymous method captures compiles as before.
/// </summary>
public sealed partial class IrSnapshotTests
{
    /// <summary>A <c>System</c> with what closures need (CMP-380): the root class, <c>IInterface</c> and <c>TInterfacedObject</c>.</summary>
    internal const string ClosureSystemSource = """
        unit System;
        interface
        type
          TObject = class constructor Create; end;
          IInterface = interface end;
          TInterfacedObject = class(TObject, IInterface) end;
        var Input, Output: Text;
        implementation
        constructor TObject.Create; begin end;
        end.
        """;

    internal const string ForVarCapturedCountedSource = """
        unit M;
        interface
        type TF = reference to function: Integer;
        procedure Fill(N: Integer);
        implementation
        var Fs: array[0..9] of TF;
        procedure Fill(N: Integer);
        begin
          for var I := 0 to N do
          begin
            Fs[I] := function: Integer begin Result := I * 10 end;
            if I = 5 then
              Break;
          end;
        end;
        end.
        """;

    internal const string ForVarCapturedForInSource = """
        unit M;
        interface
        type TF = reference to function: Integer;
        procedure Fill(const Values: array of Integer);
        implementation
        var Fs: array[0..9] of TF;
        procedure Fill(const Values: array of Integer);
        var N: Integer;
        begin
          N := 0;
          for var X in Values do
          begin
            Fs[N] := function: Integer begin Result := X end;
            Inc(N);
          end;
        end;
        end.
        """;

    /// <summary>The routine has a closure (it captures <c>Total</c>), but neither loop variable is captured.</summary>
    internal const string ForVarNotCapturedSource = """
        unit M;
        interface
        type TF = reference to function: Integer;
        function Run(const A: array of Integer; P: Integer): TF;
        implementation
        function Run(const A: array of Integer; P: Integer): TF;
        var Total: Integer;
        begin
          Total := 0;
          for var I := 1 to P do
            Total := Total + I;
          for var X in A do
            Total := Total + X;
          Result := function: Integer begin Result := Total + P end;
        end;
        end.
        """;

    /// <summary>The modules of this file by snapshot name (bound against <see cref="ClosureSystemSource"/>).</summary>
    internal static readonly IReadOnlyDictionary<string, string> ForVarSources = new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
        ["ForVarCapturedCounted"] = ForVarCapturedCountedSource,
        ["ForVarCapturedForIn"] = ForVarCapturedForInSource,
        ["ForVarNotCaptured"] = ForVarNotCapturedSource,
    };

    /// <summary>
    /// CMP-1410: the hidden <c>$for&lt;m&gt;</c> drives the loop (start, end test, increment); every iteration first makes a new
    /// <c>$Block&lt;m&gt;</c> into <c>$block&lt;m&gt;</c>/<c>$block&lt;m&gt;Ref</c> and stores <c>$for&lt;m&gt;</c> into its field
    /// <c>I</c>; the loop's region releases <c>$block&lt;m&gt;Ref</c> on the normal path, on <c>Break</c> and in the cleanup funclet.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-577")]
    [Trait("Req", "CMP-1410")]
    public void ForVarCapturedCounted() =>
        Snapshot.Verify("ForVarCapturedCounted", EmitAgainst(ClosureSystemSource, ForVarCapturedCountedSource));

    /// <summary>CMP-1411: the hidden index stays in the routine; each iteration makes a new frame, then stores the element into its field.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-577")]
    [Trait("Req", "CMP-1411")]
    public void ForVarCapturedForIn() =>
        Snapshot.Verify("ForVarCapturedForIn", EmitAgainst(ClosureSystemSource, ForVarCapturedForInSource));

    /// <summary>CMP-1412: loops whose variables are not captured — in a routine that has a closure — are byte-identical to before C-387.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1412")]
    public void ForVarNotCaptured_Unchanged() =>
        Snapshot.Verify("ForVarNotCaptured", EmitAgainst(ClosureSystemSource, ForVarNotCapturedSource));
}

/// <summary>The modules of <see cref="IrSnapshotTests.ForVarSources"/> are accepted by the pinned clang (C-387).</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Native")]
[Trait("Category", "Toolchain")]
public sealed class ForVarSnapshotToolchainTests
{
    public static TheoryData<string, string> Modules()
    {
        var data = new TheoryData<string, string>();
        foreach (var optimisation in new[] { "-O0", "-O2" })
        {
            foreach (var name in IrSnapshotTests.ForVarSources.Keys)
            {
                data.Add(name, optimisation);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Modules))]
    [Trait("Req", "CMP-1410")]
    [Trait("Req", "CMP-1411")]
    [Trait("Req", "CMP-1412")]
    public async Task ForVarModule_CompilesWithPinnedClang(string name, string optimisation)
    {
        var text = IrSnapshotTests.EmitAgainst(IrSnapshotTests.ClosureSystemSource, IrSnapshotTests.ForVarSources[name]);

        var (exitCode, output) = await Clang.CompileToObjectAsync(text, optimisation);

        Assert.True(exitCode == 0, "clang failed:\n" + output);
    }
}
