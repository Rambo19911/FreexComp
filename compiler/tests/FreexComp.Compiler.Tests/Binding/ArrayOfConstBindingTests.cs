using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>C-107: <c>array of const</c> parameters (routines.md LANG-433, rtl-abi.md §9.10 <c>TVarRec</c>).</summary>
[Trait("Phase", "2")]
public sealed class ArrayOfConstBindingTests
{
    /// <summary>The <c>TVarRec</c> of rtl-abi.md §9.10, declared here as <c>System</c> declares it.</summary>
    private const string Prelude = """
        program T;
        const vtInteger = 0; vtBoolean = 1; vtChar = 2; vtExtended = 3; vtString = 4; vtPointer = 5; vtPChar = 6;
          vtObject = 7; vtClass = 8; vtWideChar = 9; vtPWideChar = 10; vtAnsiString = 11; vtInterface = 14; vtInt64 = 16;
          vtUnicodeString = 17;
        type
          TVarRec = record
            case VType: Byte of
              vtInteger: (VInteger: Integer);
              vtBoolean: (VBoolean: Boolean);
              vtPointer: (VPointer: Pointer);
          end;
          TR = record x: Integer end;
        procedure P(const A: array of const); var i: Integer; begin i := A[0].VType + High(A) end;
        var r: TR; s: string; d: Double;

        """;

    /// <summary>LANG-433: the parameter's type; elements keep their own types (the lowering maps them to <c>VType</c>s).</summary>
    [Fact]
    [Trait("Req", "LANG-433")]
    public void Constructor_KeepsTheElementTypes()
    {
        var (result, _) = Bind(Prelude + "begin P([1, 'x', s, d, True, nil, 5000000000]); P([]) end.");
        Assert.Empty(Format(result.Diagnostics));
        var p = result.Routines.Single(r => r.Routine.Name == "P").Routine;
        var type = Assert.IsType<OpenArrayTypeSymbol>(p.Parameters[0].Type);
        Assert.Equal(("array of const", true, "TVarRec", true), (type.Name, type.IsArrayOfConst, type.ElementType.Name, p.Parameters[0].IsOpenArray));
        Assert.Equal(
            "(block (call P (array array of const 1:Integer 'x':Char s:string d:Double True:Boolean nil:nil 5000000000:Int64)) (call P (array array of const)))",
            BoundDump.Of(result.Bodies[^1]));
    }

    /// <summary>
    /// C-204 (lead decision 2026-11-14, LANG-433 vs ABI §9.10): the static type decides — every expression of a 64-bit integer type
    /// (variables, typed constant casts) is <c>vtInt64</c>; other integer constants are <c>vtInteger</c> when they fit Integer.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-433")]
    public void Int64VariableIsVtInt64()
    {
        var (result, _) = Bind(Prelude + "var i64: Int64; u64: UInt64; c: Cardinal; w: Word; " +
            "begin P([i64, u64, i64 + 1, Int64(5), UInt64(7), c, w, 5, 5000000000, High(Cardinal), -1]) end.");
        Assert.Empty(Format(result.Diagnostics));
        var call = Assert.IsType<FreexComp.Compiler.Binding.BoundCallStatement>(Assert.Single(result.Bodies[^1].Statements));
        var elements = Assert.IsType<FreexComp.Compiler.Binding.BoundArrayConstructor>(call.Arguments[0]).Elements;
        Assert.Equal([16, 16, 16, 16, 16, 0, 0, 0, 16, 16, 0], elements.Select(e => VarRecs.VTypeOf(e.Type, e.ConstantValue)!.Value));
    }

    /// <summary>LANG-433: no other kinds of elements (E2010 at the element), no single value, no <c>var</c> constructor.</summary>
    [Theory]
    [Trait("Req", "LANG-433")]
    [InlineData("P([r])", "E2010(15,10): Incompatible types: 'TR' and 'array of const'")]
    [InlineData("P(1)", "E2010(15,9): Incompatible types: 'Integer' and 'array of const'")]
    public void Elements_Errors(string statement, string expected) => Assert.Equal([expected], BindDiagnostics(Prelude + "begin " + statement + " end."));

    /// <summary>Without a <c>TVarRec</c> in scope (no System) the parameter type cannot be built: E2003 for the missing name.</summary>
    [Fact]
    [Trait("Req", "LANG-433")]
    public void WithoutTVarRec_IsE2003() =>
        Assert.Equal(["E2003(1,22): Undeclared identifier 'TVarRec'"], BindDiagnostics("procedure P(const A: array of const); begin end; begin end."));
}
