using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>
/// C-221 (LANG-442, 480, 481): during overload resolution a method or routine name is a method-pointer or procedural value for the
/// candidates whose parameter there has such a type, as for a routine that is not overloaded.
/// </summary>
[Trait("Phase", "2")]
[Trait("Req", "LANG-442")]
public sealed class OverloadMethodPointerArgumentTests
{
    private const string Declarations = """
        program P;
        type
          TM = procedure of object;
          TP = procedure;
          TB = class end;
          TC = class(TB)
            procedure Run;
            function Get: TB;
            procedure Two(M: TM); overload;
            procedure Two(A: TB; M: TM); overload;
            procedure Two(A: TB); overload;
          end;
        procedure TC.Run; begin end;
        function TC.Get: TB; begin Result := nil end;
        procedure TC.Two(M: TM); begin end;
        procedure TC.Two(A: TB; M: TM); begin end;
        procedure TC.Two(A: TB); begin end;
        procedure Plain; begin end;
        procedure Call(P: TP); overload; begin end;
        procedure Call(N: Integer); overload; begin end;
        var C: TC;
        begin

        """;

    [Theory]
    [InlineData("C.Two(C.Run);")]
    [InlineData("C.Two(nil, C.Run);")]
    [InlineData("C.Two(C.Get, C.Run);")]
    [InlineData("C.Two(C.Get);")]                       // a parameterless function is still called for the TB overload
    [InlineData("Call(Plain);")]
    [InlineData("Call(1);")]
    public void MethodOrRoutineName_IsAValue_ForAProceduralParameter(string statement) =>
        Assert.Empty(BindDiagnostics(Declarations + statement + "\nend."));

    /// <summary>C-223: the probe sees the receivers of enclosing <c>with</c> statements (a method named through <c>with C do</c>).</summary>
    [Theory]
    [InlineData("with C do Two(Run);")]
    [InlineData("with C do Two(Get, Run);")]
    [InlineData("with C do C.Two(Get);")]
    public void InsideWith(string statement) =>
        Assert.Empty(BindDiagnostics(Declarations + statement + "\nend."));
}