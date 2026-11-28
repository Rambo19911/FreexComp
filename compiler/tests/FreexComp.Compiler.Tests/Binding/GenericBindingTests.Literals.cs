using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>
/// C-426: untyped literal arguments adapt in the inference of a generic method call (LANG-979) — a constant of integer, real,
/// character or string type does not fix a type parameter that an argument with a type of its own fixes; it is then checked against
/// the parameter of the instance like the argument of any call. Literals alone keep their natural types. In a set of generic and
/// non-generic methods (LANG-980) the adapted instance costs what a non-generic method with those parameter types costs.
/// </summary>
public sealed partial class GenericBindingTests
{
    /// <summary>
    /// LANG-979: the type parameter comes from the argument that has a type of its own, whatever the order; the literal next to it is
    /// converted to the parameter type of the instance — no diagnostic.
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-979")]
    // the examples of the decision
    [InlineData("N := TUtil.IndexOf(Names, 'x')", "IndexOf<string>")]
    [InlineData("B := TUtil.Pick(B, 1)", "Pick<Byte>")]
    [InlineData("D := TUtil.Pick(D, 1)", "Pick<Double>")]
    [InlineData("S := TUtil.Pick(S, 'b')", "Pick<string>")]
    [InlineData("Ptr := TUtil.Pick(Ptr, nil)", "Pick<^Integer>")]
    [InlineData("CmpObject := TUtil.Pick(CmpObject, nil)", "Pick<TIntComparer>")]
    // the order of the arguments does not matter
    [InlineData("B := TUtil.Pick(1, B)", "Pick<Byte>")]
    [InlineData("S := TUtil.Pick('b', S)", "Pick<string>")]
    [InlineData("D := TUtil.Pick(1, D)", "Pick<Double>")]
    [InlineData("CmpObject := TUtil.Pick(nil, CmpObject)", "Pick<TIntComparer>")]
    // every kind of literal, and constant expressions made of them
    [InlineData("L := TUtil.Pick(L, 1)", "Pick<Int64>")]
    [InlineData("Sg := TUtil.Pick(Sg, 1.5)", "Pick<Single>")]
    [InlineData("Sg := TUtil.Pick(Sg, 1)", "Pick<Single>")]
    [InlineData("Sh := TUtil.Pick(Sh, 'bc')", "Pick<ShortString>")]
    [InlineData("S := TUtil.Pick(S, #13)", "Pick<string>")]
    [InlineData("B := TUtil.Pick(B, 1 + 2)", "Pick<Byte>")]
    [InlineData("B := TUtil.Pick(B, (1))", "Pick<Byte>")]
    [InlineData("D := TUtil.Pick(D, -(2 * 3))", "Pick<Double>")]
    [InlineData("S := TUtil.Pick(S, 'a' + 'b')", "Pick<string>")]
    // an untyped named constant is a literal (LANG-343: it has the type of its expression and is replaced by its value)
    [InlineData("B := TUtil.Pick(B, Five)", "Pick<Byte>")]
    [InlineData("B := TUtil.Pick(Five + 1, B)", "Pick<Byte>")]
    [InlineData("Sg := TUtil.Pick(Sg, Half)", "Pick<Single>")]
    [InlineData("S := TUtil.Pick(S, Letter)", "Pick<string>")]
    // a constant typecast or intrinsic call is a constant like any other: its natural type was Integer before, whatever it is written as
    [InlineData("B := TUtil.Pick(B, High(Byte))", "Pick<Byte>")]
    [InlineData("B := TUtil.Pick(B, SizeOf(Integer))", "Pick<Byte>")]
    [InlineData("L := TUtil.Pick(L, Byte(1))", "Pick<Int64>")]
    [InlineData("S := TUtil.Pick(S, Chr(65))", "Pick<string>")]
    // through the other parameter forms: var, open array, dynamic array, an instance of a generic type
    [InlineData("TUtil.Fill(1, B)", "Fill<Integer, Byte>")]
    [InlineData("N := TUtil.IndexOf(Bytes, 1)", "IndexOf<Byte>")]
    [InlineData("N := TUtil.IndexOf(Names, Letter)", "IndexOf<string>")]
    [InlineData("D := TUtil.FirstOr(List, 1)", "FirstOr<Double>")]
    [InlineData("S := TUtil.Max('a', 'b', StrCmp)", "Max<string>")]
    [InlineData("D := TUtil.MaxBy(D, 1, function(const A, B: Double): Integer begin Result := 0 end)", "MaxBy<Double>")]
    // a bracket constructor at an open-array parameter: one made of literals only adapts, one with a variable fixes the type
    [InlineData("N := TUtil.IndexOf([1, 2], B)", "IndexOf<Byte>")]
    [InlineData("N := TUtil.IndexOf([Five, 2], B)", "IndexOf<Byte>")]
    [InlineData("N := TUtil.IndexOf(['a', 'b'], S)", "IndexOf<string>")]
    [InlineData("N := TUtil.IndexOf([B, B], 1)", "IndexOf<Byte>")]
    [InlineData("N := TUtil.IndexOf([B, 1], 2)", "IndexOf<Integer>")]            // elements of two natural types fix nothing (as before)
    // literals are the only source: their natural types, as before
    [InlineData("Ch := TUtil.Pick('a', 'b')", "Pick<Char>")]
    [InlineData("N := TUtil.Pick(1, 2)", "Pick<Integer>")]
    [InlineData("N := TUtil.Pick(Five, 2)", "Pick<Integer>")]
    [InlineData("N := TUtil.Pick(Byte(1), High(Byte))", "Pick<Integer>")]
    [InlineData("D := TUtil.Pick(1.5, 2.5)", "Pick<Double>")]
    [InlineData("S := TUtil.Pick('ab', 'cd')", "Pick<string>")]
    [InlineData("L := TUtil.Pick(5000000000, 6000000000)", "Pick<Int64>")]
    [InlineData("N := TUtil.Sum([1, 2, 3])", "Sum<Integer>")]
    [InlineData("N := TUtil.IndexOf([1, 2], 3)", "IndexOf<Integer>")]
    [InlineData("N := TUtil.Opt(5)", "Opt<Integer>")]
    public void Inference_LiteralsAdapt(string call, string expected)
    {
        var result = BindInference(call);

        Assert.Equal([], Format(result.Diagnostics).Where(d => !d.StartsWith('H')));
        Assert.Equal(expected, CalledInstance(result));
    }

    /// <summary>
    /// LANG-979: a literal that does not fit the parameter type the other argument gave is reported as in any call — at the literal,
    /// with the diagnostic of the conversion; no E2711 is added and the call is not bound further.
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-979")]
    // the two rows that were conflicts (E2711) in Inference_Conflicts_AreE2711 before C-426
    [InlineData("N := TUtil.IndexOf(Names, 3)", "E2010(29): Incompatible types: 'Integer' and 'string'")]
    [InlineData("N := TUtil.Max(3, 7, StrCmp)", "E2010(18): Incompatible types: 'Integer' and 'string'|E2010(21): Incompatible types: 'Integer' and 'string'")]
    [InlineData("S := TUtil.Pick(S, 1)", "E2010(22): Incompatible types: 'Integer' and 'string'")]
    [InlineData("N := TUtil.Pick(I, 'x')", "E2010(22): Incompatible types: 'Char' and 'Integer'")]
    [InlineData("N := TUtil.Pick('xy', I)", "E2010(19): Incompatible types: 'string' and 'Integer'")]
    [InlineData("N := TUtil.Pick(I, 2.5)", "E2010(22): Incompatible types: 'Double' and 'Integer'")]
    [InlineData("B := TUtil.Pick(B, 300)", "E2012(22): Constant value 300 is out of range for type 'Byte'")]
    [InlineData("N := TUtil.IndexOf([1, 'a'], S)", "E2010(23): Incompatible types: 'Integer' and 'string'")]
    public void Inference_LiteralsAdapt_MisfitIsItsOwnDiagnostic(string call, string expected) =>
        Assert.Equal(
            expected.Split('|').Select(d => d.Replace("(", $"({Line("{CALL}")},", StringComparison.Ordinal)),
            Format(BindInference(call).Diagnostics));

    private const string LiteralOverloadProgram = """
        program P;
        type
          TLit = class
            class procedure Show(I: Integer); overload; static;
            class procedure Show(const S: string); overload; static;
            class procedure Show<T>(const V: T); overload; static;
            class procedure Two(A, B: Byte); overload; static;
            class procedure Two<T>(const A, B: T); overload; static;
            class procedure Mix(A: Double; B: Integer); overload; static;
            class procedure Mix<T>(const A, B: T); overload; static;
            class procedure Wide(A, B: Double); overload; static;
            class procedure Wide<T>(const A, B: T); overload; static;
            class procedure Text(const A, B: string); overload; static;
            class procedure Text<T>(const A, B: T); overload; static;
          end;
        class procedure TLit.Show(I: Integer); begin end;
        class procedure TLit.Show(const S: string); begin end;
        class procedure TLit.Show<T>(const V: T); begin end;
        class procedure TLit.Two(A, B: Byte); begin end;
        class procedure TLit.Two<T>(const A, B: T); begin end;
        class procedure TLit.Mix(A: Double; B: Integer); begin end;
        class procedure TLit.Mix<T>(const A, B: T); begin end;
        class procedure TLit.Wide(A, B: Double); begin end;
        class procedure TLit.Wide<T>(const A, B: T); begin end;
        class procedure TLit.Text(const A, B: string); begin end;
        class procedure TLit.Text<T>(const A, B: T); begin end;
        var
          I: Integer;
          B: Byte;
          D: Double;
          S: string;
        begin
          {CALL};
        end.
        """;

    /// <summary>
    /// LANG-980 rule 4 with adapting literals: the instance the literal adapts to is one more candidate, and its literal argument costs
    /// what it costs at a non-generic parameter of that type — so a non-generic method that matches equally well or better is still
    /// chosen; the instance wins only where it is the cheaper match (LANG-442).
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-979")]
    [Trait("Req", "LANG-980")]
    [InlineData("TLit.Show(5)", "Show(Integer)")]                        // Show<Integer> costs the same: the non-generic one
    [InlineData("TLit.Show('xy')", "Show(string)")]                      // Show<string> costs the same
    [InlineData("TLit.Show('x')", "Show<Char>")]                         // a character: exact for Show<Char>, a conversion for string
    [InlineData("TLit.Show(B)", "Show<Byte>")]
    [InlineData("TLit.Show(S)", "Show(string)")]
    [InlineData("TLit.Two(B, 1)", "Two(Byte, Byte)")]                    // Two<Byte> by the adapted literal: the same costs
    [InlineData("TLit.Two(1, B)", "Two(Byte, Byte)")]
    [InlineData("TLit.Two(I, 1)", "Two<Integer>")]                       // no narrowing of I
    [InlineData("TLit.Mix(D, 1)", "Mix(Double, Integer)")]               // Mix<Double> converts the literal: the exact method is better
    [InlineData("TLit.Text(S, 'b')", "Text(string, string)")]            // Text<string> by the adapted literal: the same costs
    [InlineData("TLit.Text('a', 'b')", "Text<Char>")]                    // literals alone: their natural type, exact
    [InlineData("TLit.Wide(D, 1)", "Wide(Double, Double)")]
    [InlineData("TLit.Wide(B, 1)", "Wide<Byte>")]                        // cheaper than two conversions to Double (before C-426: not inferable)
    public void Inference_LiteralsAdapt_MixedOverloads(string call, string expected)
    {
        var tree = SyntaxTree.Parse(SourceText.From(TestPath, LiteralOverloadProgram.Replace("{CALL}", call)));
        Assert.Empty(tree.Diagnostics);
        var result = BindWithStubSystem(tree, AnonymousMethodBindingTests.SystemSource);

        Assert.Equal([], Format(result.Diagnostics).Where(d => !d.StartsWith('H')));
        Assert.Equal(expected, result.Bodies[0].Statements.OfType<BoundCallStatement>().Single().Routine switch
        {
            MethodSymbol { GenericMethodDefinition: not null } method => method.Name + "<" + string.Join(", ", method.GenericMethodArguments.Select(a => a.Name)) + ">",
            { } routine => routine.Name + "(" + string.Join(", ", routine.Parameters.Select(p => p.Type.Name)) + ")",
            null => null,
        });
    }
}
