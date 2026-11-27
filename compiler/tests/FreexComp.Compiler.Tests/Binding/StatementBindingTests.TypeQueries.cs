using FreexComp.Compiler.Binding;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>C-417: type-query conditions outside generics (intrinsics.md LANG-977 rules 1, 2, 4, 6, 7).</summary>
public sealed partial class StatementBindingTests
{
    private static string TypeKindBody(string body, string declarations = "")
    {
        var result = BindWithTypeKinds($"program P; {Variables}{declarations}begin {body} end.");
        Assert.Empty(Format(result.Diagnostics));
        return BoundDump.Of(Assert.Single(result.Bodies));
    }

    /// <summary>
    /// LANG-977 rule 4: in a body that is not generic the statement a type-query condition does not take is bound and checked like
    /// every statement — an undeclared identifier in it is E2003 — and is kept apart from the one that is compiled.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-977")]
    public void TypeQueryCondition_OutsideGenerics_IsStillChecked()
    {
        Assert.Equal(
            ["E2003(1,116): Undeclared identifier 'Missing'"],
            Diagnostics("if IsManagedType(Integer) then Missing := 1 else i := 2"));
        Assert.Equal(
            ["E2003(1,127): Undeclared identifier 'Missing'"],
            Diagnostics("if IsManagedType(string) then i := 2 else Missing := 1"));
        Assert.Equal(
            ["E2010(1,114): Incompatible types: 'string' and 'Integer'"],
            Diagnostics("if IsConstValue(i) then i := 'text'"));

        // rule 1: the kept statement, and the checked one that is not compiled
        Assert.Equal(
            "(block (pruned False:Boolean (:= i:Integer 2:Integer) (checked (:= i:Integer 1:Integer))))",
            TypeKindBody("if IsManagedType(Integer) then i := 1 else i := 2"));
        Assert.Equal(
            "(block (pruned True:Boolean (:= i:Integer 1:Integer) (checked (:= i:Integer 2:Integer))))",
            TypeKindBody("if not IsManagedType(Integer) and (SizeOf(Integer) = 4) then i := 1 else i := 2"));
        Assert.Equal("(block (pruned False:Boolean (checked (:= i:Integer 1:Integer))))", TypeKindBody("if HasWeakRef(string) then i := 1"));
        Assert.Equal(
            "(block (pruned True:Boolean (:= j:Integer 1:Integer)))",
            TypeKindBody("if GetTypeKind(Integer) in [tkInteger, tkInt64] then j := 1"));
        Assert.Equal(
            "(block (pruned True:Boolean (:= i:Integer 1:Integer) (checked (:= i:Integer 2:Integer))))",
            TypeKindBody("if Wide then i := 1 else i := 2", "const Wide = GetTypeKind(Int64) = tkInt64; "));
        Assert.Equal(
            "(block (pruned False:Boolean (:= i:Integer 2:Integer) (checked (:= i:Integer 1:Integer))))",
            TypeKindBody("if not P.Wide then i := 1 else i := 2", "const Wide = GetTypeKind(Int64) = tkInt64; "));       // qualified by the unit
        Assert.Equal(
            "(block (pruned True:Boolean (:= i:Integer 1:Integer) (checked (:= i:Integer 2:Integer))))",
            TypeKindBody("if TBox.Managed then i := 1 else i := 2", "type TBox = class public const Managed = IsManagedType(string); end; "));
        Assert.Equal(
            "(block (pruned 9:TTypeKind [1, 9] (:= i:Integer 2:Integer) (checked (:= i:Integer 1:Integer) (block (:= i:Integer 3:Integer)))))",
            TypeKindBody("case GetTypeKind(string) of tkInteger: i := 1; tkUString: i := 2; else i := 3; end"));
        Assert.Equal(
            "(block (pruned 4:TTypeKind [1] (block (:= i:Integer 3:Integer)) (checked (:= i:Integer 1:Integer))))",
            TypeKindBody("case GetTypeKind(Double) of tkInteger: i := 1; else i := 3; end"));

        // rule 7: no type query, or not constant: the statement as before
        Assert.Equal(
            "(block (if True:Boolean (:= i:Integer 1:Integer) (:= i:Integer 2:Integer)))",
            TypeKindBody("if SizeOf(Integer) = 4 then i := 1 else i := 2"));
        Assert.Equal(
            "(block (if (ConditionalAnd:Boolean True:Boolean flag:Boolean) (:= i:Integer 1:Integer)))",
            TypeKindBody("if IsManagedType(string) and flag then i := 1"));
        Assert.Equal("(block (while False:Boolean (:= i:Integer 1:Integer)))", TypeKindBody("while IsManagedType(Integer) do i := 1"));
        Assert.Equal(
            "(block (case 1:Integer (arm [1] (:= i:Integer 1:Integer))))",
            TypeKindBody("case SizeOf(Byte) of 1: i := 1; end"));
    }

    /// <summary>
    /// LANG-977 rules 5 and 6 outside generics: flow analysis works on the kept statement, and a local that only the statement not
    /// taken names is neither "never used" nor a dead store.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-977")]
    public void TypeQueryCondition_OutsideGenerics_FlowWorksOnWhatIsKept()
    {
        string[] Flow(string body)
        {
            var result = BindWithTypeKinds($"program P; function F(p: Integer): Integer; var a, b, unused: Integer; begin {body} end; begin Writeln(F(1)) end.");
            Assert.Empty(Format(result.Diagnostics));
            return [.. FlowAnalyzer.Analyze(result).Select(d => $"{d.Code} {d.Message}")];
        }

        // a is assigned in kept code and read only in the statement not taken; b lives only there
        Assert.Equal(
            ["H2001 Variable 'unused' is declared but never used"],
            Flow("a := p; Result := 0; if IsManagedType(Integer) then begin b := a; Result := b; end;"));
        // the kept statement assigns Result on every path; the other one would not
        Assert.Equal(
            ["H2001 Variable 'a' is declared but never used", "H2001 Variable 'b' is declared but never used", "H2001 Variable 'unused' is declared but never used"],
            Flow("if IsManagedType(string) then Result := p else p := 2;"));
        Assert.Equal(
            ["H2001 Variable 'a' is declared but never used", "H2001 Variable 'b' is declared but never used",
             "H2001 Variable 'unused' is declared but never used", "W1042 Return value of function 'F' might be undefined"],
            Flow("if IsManagedType(Integer) then Result := p else p := 2;"));
        // a is assigned only in the statement not taken: for the kept code it is read before any assignment (rule 6)
        Assert.Equal(
            ["H2001 Variable 'b' is declared but never used", "H2001 Variable 'unused' is declared but never used", "W1041 Variable 'a' might not have been initialized"],
            Flow("if IsManagedType(Integer) then a := 1; Result := a;"));

        // a private member that only the statement not taken names was bound there: no H2003
        var members = BindWithTypeKinds("""
            program P;
            type
              TBox = class
              private
                FOnlyThere: Integer;
                FNowhere: Integer;
              public
                procedure Run;
              end;
            procedure TBox.Run;
            begin
              if IsManagedType(Integer) then
                FOnlyThere := 1;
            end;
            begin
            end.
            """);
        Assert.Empty(Format(members.Diagnostics));
        Assert.Equal(["H2003 Private symbol 'FNowhere' is declared but never used"], FlowAnalyzer.Analyze(members).Select(d => $"{d.Code} {d.Message}"));
    }

    /// <summary>LANG-977 rule 2 with LANG-380: no jump from outside into the statement a type-query condition does not take (E2072).</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-977")]
    public void TypeQueryCondition_GotoIntoThePrunedStatement_IsE2072()
    {
        string[] Jump(string body) =>
            [.. Format(BindWithTypeKinds($"program P; procedure R; label L; var i: Integer; begin {body} end; begin R end.").Diagnostics)
                .Where(d => !d.StartsWith('H')).Select(d => d[..5])];

        Assert.Equal(["E2072"], Jump("goto L; if IsManagedType(Integer) then begin L: i := 1; end;"));
        Assert.Equal([], Jump("goto L; if IsManagedType(string) then begin L: i := 1; end;"));                   // the kept statement
        Assert.Equal([], Jump("if IsManagedType(Integer) then begin goto L; i := 1; end; L: i := 2;"));            // out of it: never compiled
        Assert.Equal([], Jump("if IsManagedType(Integer) then begin goto L; L: i := 1; end;"));                    // inside it
    }
}
