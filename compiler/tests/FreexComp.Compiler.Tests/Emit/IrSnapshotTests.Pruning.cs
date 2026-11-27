using System.Text.RegularExpressions;

namespace FreexComp.Compiler.Tests.Emit;

/// <summary>C-417 (CMP-1475): a type-query condition emits the kept statement only — no test, no branch, nothing of the other part.</summary>
public sealed partial class IrSnapshotTests
{
    /// <summary>The text of the one definition whose <c>define</c> line contains <paramref name="name"/>, up to its closing brace.</summary>
    private static string Definition(string module, string name)
    {
        var definitions = Regex.Matches(module, @"^define [^\n]*\n(?:[^\n]*\n)*?\}\n", RegexOptions.Multiline)
            .Select(m => m.Value).Where(d => d[..d.IndexOf('\n')].Contains(name, StringComparison.Ordinal)).ToList();
        return Assert.Single(definitions);
    }

    /// <summary>
    /// Card F10-039, acceptance 3: one generic source, two instances — the instance for <c>Integer</c> holds the raw move only (no call of
    /// the managed copy, no conditional branch, and the module has no RTTI block of <c>Integer</c>, which only the pruned statement
    /// names); the instance for <c>string</c> holds the call and no code of the move.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1475")]
    [Trait("Req", "LANG-977")]
    public void ManagedBranchPruned()
    {
        var text = EmitWithTypeKinds("""
            unit M;
            interface
            type
              TMover<T> = class
                class procedure Assign(var Dst: T; const Src: T);
              end;
            procedure CopyManaged(Dst, Src, Info: Pointer);
            procedure Run;
            implementation
            procedure CopyManaged(Dst, Src, Info: Pointer);
            begin
            end;
            class procedure TMover<T>.Assign(var Dst: T; const Src: T);
            begin
              if IsManagedType(T) then
                CopyManaged(@Dst, @Src, TypeInfo(T))
              else
                Move(Src, Dst, SizeOf(T));
            end;
            procedure Run;
            var
              I, J: Integer;
              S, U: string;
            begin
              J := 1;
              TMover<Integer>.Assign(I, J);
              TMover<string>.Assign(S, U);
            end;
            end.

            """);

        var forInteger = Definition(text, "TMover<Integer>.Assign");
        Assert.DoesNotContain("CopyManaged", forInteger, StringComparison.Ordinal);
        Assert.DoesNotContain("br i1", forInteger, StringComparison.Ordinal);
        Assert.Contains("@llvm.memmove", forInteger, StringComparison.Ordinal);
        Assert.DoesNotContain("Integer.$rtti", text, StringComparison.Ordinal);

        var forString = Definition(text, "TMover<string>.Assign");
        Assert.Contains("call void @\"M.CopyManaged\"(", forString, StringComparison.Ordinal);
        Assert.Contains("string.$rtti", forString, StringComparison.Ordinal);
        Assert.DoesNotContain("br i1", forString, StringComparison.Ordinal);
        Assert.DoesNotContain("memmove", forString, StringComparison.Ordinal);
    }

    /// <summary>
    /// CMP-1475 outside generics: the statement an <c>if</c> or <c>case</c> on a type query takes is all the code there is — no
    /// comparison, no <c>br</c>, no <c>switch</c>, nothing of the other parts (they were checked, LANG-977 rule 4).
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1475")]
    public void TypeQueryCondition_NonGeneric_KeepsOneBranch()
    {
        var text = EmitWithTypeKinds("""
            unit M;
            interface
            type
              TEnum = (eA, eB);
              TRec = record S: string; N: Integer; end;
            function Q(P: Integer): Integer;
            implementation
            function Q(P: Integer): Integer;
            const
              Plain = not IsManagedType(TEnum);
            begin
              if IsManagedType(TRec) then
                Result := P + 11
              else
                Result := P - 22;
              case GetTypeKind(TEnum) of
                tkInteger: Result := Result * 33;
                tkEnumeration: Result := Result * 44;
              else
                Result := 55;
              end;
              if HasWeakRef(TRec) then
                Result := 66;
              if Plain then
                Result := Result + 77;
            end;
            end.

            """);

        var q = Definition(text, "@\"M.Q\"");
        Assert.DoesNotContain("br i1", q, StringComparison.Ordinal);
        Assert.DoesNotContain("switch", q, StringComparison.Ordinal);
        Assert.DoesNotContain("icmp", q, StringComparison.Ordinal);
        Assert.Equal(["11", "44", "77"], Regex.Matches(q, @"\b(11|22|33|44|55|66|77)\b").Select(m => m.Value));
    }

    /// <summary>
    /// CMP-1475 under <c>-g</c>: the line of the <c>if</c> keyword of a type-query condition has no instruction of its own — every
    /// instruction of the body belongs to the line of the kept statement or to the routine's own lines.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1475")]
    public void TypeQueryCondition_UnderDebug_TheIfLineHasNoInstruction()
    {
        var text = DebugInfoTests.EmitWithDebugInfo(
            "unit M;\ninterface\nfunction Q(P: Integer): Integer;\nimplementation\nfunction Q(P: Integer): Integer;\nbegin\n" +
            "  if IsManagedType(Integer) then\n    Result := P + 11\n  else\n    Result := P - 22;\nend;\nend.\n");

        var lines = Regex.Matches(text, @"= !DILocation\(line: (\d+),").Select(m => int.Parse(m.Groups[1].Value)).Distinct().Order().ToList();
        Assert.Contains(10, lines);                                               // the kept statement
        Assert.DoesNotContain(7, lines);                                          // the 'if' line
        Assert.DoesNotContain(8, lines);                                          // the statement that is not compiled
        Assert.DoesNotContain("br i1", text, StringComparison.Ordinal);
    }
}
