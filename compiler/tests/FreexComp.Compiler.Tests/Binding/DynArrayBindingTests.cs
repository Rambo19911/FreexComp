using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>C-064: dynamic arrays (types.md LANG-206, LANG-257; intrinsics.md SetLength/Copy/Length/High/Low).</summary>
[Trait("Phase", "2")]
public sealed class DynArrayBindingTests
{
    private const string Types = "type TInts = array of Integer; TGrid = array of TInts; ";

    private const string Vars = "var a, b: TInts; g: TGrid; c: array of Integer; s: array of string; i: Integer; f: Boolean; ";

    private static string Body(string statements)
    {
        var (result, _) = Bind("program T; " + Types + Vars + "begin " + statements + " end.");
        Assert.Empty(Format(result.Diagnostics));
        return BoundDump.Of(Assert.Single(result.Bodies));
    }

    private static string[] Diagnostics(string statements) => BindDiagnostics("program T; " + Types + Vars + "begin " + statements + " end.");

    /// <summary>LANG-257: elements are variables (0-based, any integer index), <c>g[i, j]</c> = <c>g[i][j]</c>; assignment of the same type and <c>nil</c>.</summary>
    [Fact]
    [Trait("Req", "LANG-257")]
    public void Elements_AreVariables() =>
        Assert.Equal(
            "(block (:= ([] a:TInts i:Integer):Integer 1:Integer) (:= i:Integer ([] ([] g:TGrid 1:Integer):TInts 2:Integer):Integer) " +
            "(:= ([] s:array of string 0:Integer):string ([] s:array of string 0:Integer):string) (:= b:TInts a:TInts) (:= a:TInts nil:TInts))",
            Body("a[i] := 1; i := g[1, 2]; s[0] := s[0]; b := a; a := nil"));

    /// <summary>Length/High at run time, Low constant 0; SetLength with one length per nesting level; Copy with 1–3 arguments.</summary>
    [Fact]
    [Trait("Req", "LANG-257")]
    public void Intrinsics_LengthSetLengthCopy() =>
        Assert.Equal(
            "(block (:= i:Integer (Length:Integer a:TInts)) (:= i:Integer (High:Integer g:TGrid)) (:= i:Integer 0:Integer) " +
            "(SetLength a:TInts 3:Integer) (SetLength g:TGrid 2:Integer i:Integer) (:= b:TInts (Copy:TInts a:TInts)) " +
            "(:= b:TInts (Copy:TInts a:TInts (conv NativeInt i:Integer))) (:= b:TInts (Copy:TInts a:TInts 1:NativeInt 2:NativeInt)))",
            Body("i := Length(a); i := High(g); i := Low(a); SetLength(a, 3); SetLength(g, 2, i); b := Copy(a); b := Copy(a, i); b := Copy(a, 1, 2)"));

    /// <summary>LANG-257: <c>=</c>/<c>&lt;&gt;</c> compare references of one type or with <c>nil</c>.</summary>
    [Fact]
    [Trait("Req", "LANG-257")]
    public void Comparison_OfReferences() =>
        Assert.Equal("(block (:= f:Boolean (Equal:Boolean a:TInts b:TInts)) (:= f:Boolean (NotEqual:Boolean a:TInts nil:nil)) (:= f:Boolean (Equal:Boolean nil:nil g:TGrid)))",
            Body("f := a = b; f := a <> nil; f := nil = g"));

    /// <summary>LANG-206 (distinct anonymous types), LANG-257 errors, intrinsics.md argument counts, LANG-430 (open array parameters, M2.5).</summary>
    [Theory]
    [Trait("Req", "LANG-206")]
    [Trait("Req", "LANG-257")]
    [InlineData("a := c", "E2010(1,170): Incompatible types: 'array of Integer' and 'TInts'")]
    [InlineData("f := a < b", "E2011(1,172): Operator '<' not applicable to operand type(s) 'TInts' and 'TInts'")]
    [InlineData("f := a = c", "E2011(1,172): Operator '=' not applicable to operand type(s) 'TInts' and 'array of Integer'")]
    [InlineData("i := a[f]", "E2010(1,172): Incompatible types: 'Boolean' and 'Integer'")]
    [InlineData("SetLength(a, 1, 2)", "E2111(1,181): Too many actual parameters for 'SetLength'")]
    [InlineData("SetLength(g, 1, 2, 3)", "E2111(1,184): Too many actual parameters for 'SetLength'")]
    [InlineData("i := Length(TInts)", "E2911(1,170): 'Length' cannot be applied to this type")]
    public void Errors(string statement, string expected) => Assert.Equal([expected], Diagnostics(statement));

    /// <summary>
    /// C-067, LANG-259 / LANG-280 rule 12: <c>[e1, …]</c> in a dynamic-array context (assignment, value parameter of the only
    /// candidate, <c>Exit</c>/<c>Result</c>) builds an array of the target type; elements convert to the element type and nest;
    /// <c>[]</c> is <c>nil</c>.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-259")]
    public void ArrayConstructor_TakesTheDynamicArrayType() =>
        Assert.Equal(
            "(block (:= a:TInts (array TInts 1:Integer 2:Integer i:Integer)) (:= g:TGrid (array TGrid (array TInts 1:Integer) nil:TInts)) " +
            "(:= s:array of string (array array of string 'x':string 'y':string)) (:= a:TInts nil:TInts))",
            Body("a := [1, 2, i]; g := [[1], []]; s := ['x', 'y']; a := []"));

    [Fact]
    [Trait("Req", "LANG-259")]
    public void ArrayConstructor_AsValueParameterAndResult()
    {
        var (result, _) = Bind("program T; " + Types + """
            function F(const x: TInts; y: TInts): TInts; begin Result := [Length(x)]; Exit([2, 3]) end;
            var a: TInts; i: Integer; begin a := F([1], [Byte(i)]) end.
            """);
        Assert.Empty(Format(result.Diagnostics));
        Assert.Equal("(block (:= a:TInts (call F:TInts (array TInts 1:Integer) (array TInts (conv Integer (cast Byte i:Integer))))))", BoundDump.Of(result.Bodies[^1]));
    }

    [Theory]
    [Trait("Req", "LANG-259")]
    [InlineData("a := [1, 'x']", "E2010(1,174): Incompatible types: 'Char' and 'Integer'")]
    [InlineData("a := [1..3]", "E2010(1,170): Incompatible types: 'set of 0..3' and 'TInts'")]
    [InlineData("s := [1]", "E2010(1,171): Incompatible types: 'Integer' and 'string'")]
    public void ArrayConstructor_Errors(string statement, string expected) => Assert.Equal([expected], Diagnostics(statement));

    /// <summary>intrinsics.md §1 (C-067): <c>Assigned(a)</c> of a dynamic array tests the reference against <c>nil</c>.</summary>
    [Fact]
    [Trait("Req", "LANG-257")]
    public void Assigned_OfADynamicArray() =>
        Assert.Equal("(block (:= f:Boolean (Assigned:Boolean (cast Pointer a:TInts))) (:= f:Boolean (Assigned:Boolean (cast Pointer ([] g:TGrid 0:Integer):TInts))))",
            Body("f := Assigned(a); f := Assigned(g[0])"));

    // ---- C-388 (F10-024 part 1): constructor contexts, D.Create, casts, overloads (LANG-270…273) ----

    private const string Types8 = "type TInts = array of Integer; TGrid = array of TInts; TArray<T> = array of T; TByteSet = set of Byte; ";

    private const string Vars8 = "var a: TInts; g: TGrid; t: TArray<Integer>; st: TByteSet; i: Integer; ";

    private static string Body8(string declarations, string statements)
    {
        var (result, _) = Bind("program T; " + Types8 + declarations + Vars8 + "begin " + statements + " end.");
        Assert.Empty(Format(result.Diagnostics));
        return BoundDump.Of(result.Bodies[^1]);
    }

    private static string[] Diagnostics8(string declarations, string statements) =>
        BindDiagnostics("program T; " + Types8 + declarations + Vars8 + "begin " + statements + " end.");

    /// <summary>LANG-271: <c>D.Create(e1, …)</c> of a named dynamic-array type (also a generic instance) is the constructor value.</summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-271")]
    [InlineData("a := TInts.Create(1, 2)", "(block (:= a:TInts (array TInts 1:Integer 2:Integer)))")]
    [InlineData("a := TInts.Create", "(block (:= a:TInts nil:TInts))")]
    [InlineData("a := TInts.Create()", "(block (:= a:TInts nil:TInts))")]
    [InlineData("t := TArray<Integer>.Create(3)", "(block (:= t:TArray<Integer> (array TArray<Integer> 3:Integer)))")]
    [InlineData("g := TGrid.Create([1], [])", "(block (:= g:TGrid (array TGrid (array TInts 1:Integer) nil:TInts)))")]
    [InlineData("var x := TInts.Create(1); a := x", "(block (locals x:TInts (block (:= x:TInts (array TInts 1:Integer)) (:= a:TInts x:TInts))))")]
    [InlineData("i := Length(TInts.Create(1, i))", "(block (:= i:Integer (Length:Integer (array TInts 1:Integer i:Integer))))")]
    public void CreateForm(string statements, string expected) => Assert.Equal(expected, Body8("", statements));

    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-271")]
    [InlineData("a := TInts.Create(1, 'x')", "E2010(1,212): Incompatible types: 'Char' and 'Integer'")]
    [InlineData("g := TGrid.Create(1)", "E2010(1,209): Incompatible types: 'Integer' and 'TInts'")]
    public void CreateForm_ElementMismatch_E2010(string statement, string expected) => Assert.Equal([expected], Diagnostics8("", statement));

    /// <summary>LANG-272: a cast of a bracket constructor to a dynamic-array type builds that array, to a set type that set.</summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-272")]
    [InlineData("a := TInts([1, 2])", "(block (:= a:TInts (array TInts 1:Integer 2:Integer)))")]
    [InlineData("var x := TInts([i]); a := x", "(block (locals x:TInts (block (:= x:TInts (array TInts i:Integer)) (:= a:TInts x:TInts))))")]
    [InlineData("st := TByteSet([1, 2])", "(block (:= st:TByteSet 6:TByteSet))")]
    public void Cast_BindsTheConstructor(string statements, string expected) => Assert.Equal(expected, Body8("", statements));

    /// <summary>LANG-270: a constructor for a field, a property (field and setter access), an array element and a typed inline variable.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-270")]
    public void Contexts_PropertyFieldElementInlineVar() =>
        Assert.Equal(
            "(block (:= (. r:TRec F):TInts (array TInts 1:Integer)) (:= (. o:TObj FA):TInts (array TInts 2:Integer)) " +
            "(call SetB (array TInts 3:Integer)) (:= ([] g:TGrid 0:Integer):TInts (array TInts 4:Integer)) " +
            "(locals v:TInts (block (:= v:TInts (array TInts 5:Integer i:Integer)))))",
            Body8("""
                type TRec = record F: TInts; end;
                TObj = class FA: TInts; procedure SetB(const V: TInts); property A: TInts read FA write FA; property B: TInts write SetB; end;
                procedure TObj.SetB(const V: TInts); begin end;
                var r: TRec; o: TObj;
                """, "r.F := [1]; o.A := [2]; o.B := [3]; g[0] := [4]; var v: TInts := [5, i]"));

    private const string Overloads = """
        procedure P(I: Integer); overload; begin end;
        procedure P(const A: TInts); overload; begin end;
        procedure Q(S: TByteSet); overload; begin end;
        procedure Q(const A: TInts); overload; begin end;
        procedure R(const A: array of Integer); overload; begin end;
        procedure R(S: TByteSet); overload; begin end;
        """;

    private static string CalledParameter(string call)
    {
        var (result, _) = Bind("program T; " + Types8 + Overloads + Vars8 + "begin " + call + " end.");
        Assert.Empty(Format(result.Diagnostics));
        return BoundDump.Of(result.Bodies[^1]);
    }

    /// <summary>LANG-273: a constructor argument is applicable to a dynamic-array parameter of any candidate (cost 3).</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-273")]
    public void OverloadedCall_DynArrayCandidate() =>
        Assert.Equal("(block (call P (array TInts 1:Integer 2:Integer)) (call P 1:Integer))", CalledParameter("P([1, 2]); P(1)"));

    /// <summary>LANG-273: a set candidate and a dynamic-array candidate both taking the constructor → E2043 at the argument.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-273")]
    public void OverloadedCall_SetAndArray_E2043()
    {
        var diagnostics = BindDiagnostics("program T; " + Types8 + Overloads + Vars8 + "begin Q([1, 2]) end.");
        var expected = "E2043: Ambiguous overloaded call to 'Q': the array constructor can be 'TByteSet' or 'TInts'; cast it to one of them";
        Assert.Equal([expected], diagnostics.Select(d => d[..5] + d[d.IndexOf(':')..]));
    }

    /// <summary>LANG-272/273: a cast decides between the set and the dynamic-array overload.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-273")]
    public void OverloadedCall_CastDecides()
    {
        var dump = CalledParameter("Q(TInts([1, 2])); Q(TByteSet([1, 2])); Q(TInts.Create(3))");
        Assert.Equal(
            "(block (call Q (array TInts 1:Integer 2:Integer)) (call Q 6:TByteSet) (call Q (array TInts 3:Integer)))",
            dump);
    }

    /// <summary>LANG-273, decision 3: an open array versus a set overload keeps the set meaning of the constructor (unchanged).</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-273")]
    public void OverloadedCall_OpenArrayVsSet_Unchanged() =>
        Assert.Equal("(block (call R 6:TByteSet))", CalledParameter("R([1, 2])"));

    /// <summary>
    /// LANG-273: an open-array and a dynamic-array candidate both take the constructor at cost 3 — ranked as before, so a tie is E2122;
    /// a constructor that fits only the dynamic-array element type picks that candidate; one that fits no form reports its set error.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-273")]
    public void OverloadedCall_OpenArrayVsDynArray_RankedByCost()
    {
        const string declarations = """
            type TStrs = array of string;
            procedure W(const A: array of Integer); overload; begin end;
            procedure W(const A: TInts); overload; begin end;
            procedure V(I: Integer); overload; begin end;
            procedure V(const A: TStrs); overload; begin end;
            """;
        Assert.Equal(["E2122"], Diagnostics8(declarations, "W([1, 2])").Select(d => d[..5]));
        Assert.Equal("(block (call V (array TStrs 'a':string 'bc':string)))", Body8(declarations, "V(['a', 'bc'])"));
        Assert.Equal(["E2013"], Diagnostics8(declarations, "V([1.5])").Select(d => d[..5]));      // no form fits: bound as a set, as before
    }
}
