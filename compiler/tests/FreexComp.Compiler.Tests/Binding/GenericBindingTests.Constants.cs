using System.Numerics;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>
/// C-353: generic instance names in constant expressions (generics.md LANG-783, LANG-784 items 5 and 6) — constant declarations,
/// typed-constant initialisers, array bounds, case labels and default parameter values give the value of the statement.
/// </summary>
public sealed partial class GenericBindingTests
{
    private const string ConstantProgram = """
        program P;
        type
          TR<T> = record
            A: T;
            B: Byte;
          end;
          TK<T> = class
          public
            const X = 7;
            type
              TEn = (eA, eB, eC);
              TSub = 2..5;
          end;
          TD = class(TK<Integer>)
          end;
          TOuter = class
          public
            type
              TInner<T> = record
                V: T;
                W: T;
              end;
          end;
          TDerivedOuter = class(TOuter)
          end;
        const
          C = {E};
          TC: Integer = {E};
        type
          TArr = array[0..{E}] of Byte;
        procedure Q(N: Integer = {E});
        begin
        end;
        var
          N: Integer;
        begin
          N := {E};
          case N of
            {E}: N := 0;
          end;
        end.
        """;

    /// <summary>LANG-784 items 5 and 6: each expression folds to the same value in every constant position and in the statement.</summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-784")]
    [Trait("Req", "LANG-780")]
    [InlineData("SizeOf(TR<Int64>)", 16)]
    [InlineData("SizeOf(P.TR<Int64>)", 16)]
    [InlineData("SizeOf(TR<Byte>)", 2)]
    [InlineData("TK<Integer>.X", 7)]
    [InlineData("P.TK<Integer>.X", 7)]
    [InlineData("TD.X", 7)]
    [InlineData("Ord(High(TK<Integer>.TEn))", 2)]
    [InlineData("Ord(Low(P.TK<Integer>.TEn))", 0)]
    [InlineData("High(TK<Byte>.TSub)", 5)]
    [InlineData("Low(TD.TSub)", 2)]
    [InlineData("SizeOf(TK<Integer>.TEn)", 1)]
    [InlineData("SizeOf(TOuter.TInner<Word>)", 4)]
    [InlineData("SizeOf(P.TOuter.TInner<Byte>)", 2)]
    [InlineData("SizeOf(TDerivedOuter.TInner<Int64>)", 16)]
    [InlineData("TK<Integer>.X * 2 + SizeOf(TR<Byte>)", 16)]
    public void ConstantExpressions_GenericNames(string expression, int expected)
    {
        var (result, _) = Bind(ConstantProgram.Replace("{E}", expression));

        Assert.Equal([], Format(result.Diagnostics).Where(d => !d.StartsWith('H') && !d.StartsWith('W')));
        var constant = Assert.IsType<ConstantSymbol>(result.Scope.LookupLocal("C"));
        Assert.Equal(new BigInteger(expected), constant.Value!.Value);
        var array = Assert.IsType<ArrayTypeSymbol>(result.Scope.LookupLocal("TArr"));
        Assert.Equal(new BigInteger(expected), array.High);
        var routine = Assert.IsAssignableFrom<RoutineSymbol>(result.Scope.LookupLocal("Q"));
        Assert.Equal(new BigInteger(expected), routine.Parameters.Single().DefaultValue!.Value);
    }

    /// <summary>
    /// LANG-783: an instance of a forward generic named in a constant expression before the completion (nested constants and array
    /// bounds of the same type section) is E2007. Method headings are bound after the section, so their default values see it complete.
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-783")]
    [InlineData("TB = class const S = SizeOf(TA<Integer>); end;", "E2007(1,81): Type 'TA<Integer>' is not yet completely defined")]
    [InlineData("TB = class const S = TA<Integer>.X; end;", "E2007(1,74): Type 'TA<Integer>' is not yet completely defined")]
    [InlineData("TB = record const S = Ord(High(TA<Integer>.TSub)); end;", "E2007(1,84): Type 'TA<Integer>' is not yet completely defined")]
    [InlineData("TArr = array[0..TA<Integer>.X] of Byte;", "E2007(1,69): Type 'TA<Integer>' is not yet completely defined")]
    [InlineData("TArr = array[0..SizeOf(TA<Integer>)] of Byte;", "E2007(1,76): Type 'TA<Integer>' is not yet completely defined")]
    public void ForwardGeneric_ConstantInitialiser_E2007(string between, string expected) =>
        Assert.Equal([expected], BindDiagnostics(
            "program P; type TObject = class end; TA<T> = class; " + between + " TA<T> = class const X = 1; type TSub = 1..3; end; begin end.")
            .Where(d => !d.StartsWith('H')));
}
