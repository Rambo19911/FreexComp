using FreexComp.Compiler.Lowering;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Lowering;

/// <summary>Bound tree → lowered tree (architecture.md §3.2, CMP-116): loops, <c>for</c> (CMP-321), <c>case</c> (CMP-322), intrinsics (CMP-412).</summary>
[Trait("Phase", "2")]
public sealed class LoweringTests
{
    private static LoweredUnit Lower(string text)
    {
        var (result, _) = Bind(text);
        Assert.Empty(Format(result.Diagnostics));
        return Lowerer.Lower(result);
    }

    /// <summary>The program block of <c>program T; var {globals}; begin {statements} end.</c>, without the outer block.</summary>
    private static string MainBody(string globals, string statements)
    {
        var body = Assert.Single(Lower($"program T; var {globals}; begin {statements} end.").Bodies);
        return string.Join(" ", body.Body.Statements.Select(LoweredDump.Of));
    }

    private static LoweredBody Routine(LoweredUnit unit, string name) => unit.Routines.Single(r => r.Routine!.Name == name);

    /// <summary>The statements of routine <paramref name="name"/> in <c>program T; {declarations} begin end.</c>.</summary>
    private static string Body(string declarations, string name = "F") =>
        string.Join(" ", Routine(Lower($"program T; {declarations} begin end."), name).Body.Statements.Select(LoweredDump.Of));

    private static string Locals(LoweredBody body) => string.Join(" ", body.Locals.Select(l => $"{l.Name}:{l.Type.Name}"));

    // ------------------------------------------------------------------ UnicodeString (CMP-324, CMP-325, C-060)

    private static string Cleanup(LoweredBody body) => body.Cleanup is { } cleanup ? LoweredDump.Of(cleanup) : "none";

    /// <summary>
    /// CMP-325 rules 1–3: managed locals zeroed first, value parameters add a reference, <c>out</c> parameters are cleared; the
    /// cleanup scope finalises value parameters then locals (not <c>const</c>/<c>var</c>/<c>out</c> parameters).
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-325")]
    [Trait("Req", "ABI-030")]
    [Trait("Req", "ABI-031")]
    public void ManagedLifetimes_ZeroAddRefAndCleanup()
    {
        var body = Routine(Lower("program T; procedure F(a: string; const b: string; var c: string; out d: string); var s: string; begin s := a end; begin end."), "F");

        Assert.Equal("(zero (addr s)) (call _UStrAddRef a) (call _UStrClr (addr d)) (call _UStrAsg (addr s) a)",
            string.Join(" ", body.Body.Statements.Select(LoweredDump.Of)));
        Assert.Equal("(block (call _UStrClr (addr a)) (call _UStrClr (addr s)))", Cleanup(body));
        Assert.Equal("s:string", Locals(body));
    }

    /// <summary>C-063: AnsiStrings use the <c>_AStr*</c> family for the same rules; a short string is no managed storage.</summary>
    [Fact]
    [Trait("Req", "CMP-325")]
    [Trait("Req", "ABI-031")]
    public void ManagedLifetimes_AnsiStringsUseTheAStrFamily()
    {
        var body = Routine(Lower("program T; procedure F(a: AnsiString; out d: AnsiString; h: ShortString); var s: AnsiString; begin s := a; d := s end; begin end."), "F");

        Assert.Equal("(zero (addr s)) (call _AStrAddRef a) (call _AStrClr (addr d)) (call _AStrAsg (addr s) a) (call _AStrAsg (addr d) s)",
            string.Join(" ", body.Body.Statements.Select(LoweredDump.Of)));
        Assert.Equal("(block (call _AStrClr (addr a)) (call _AStrClr (addr s)))", Cleanup(body));
    }

    /// <summary>
    /// C-064 (CMP-325, CMP-330): dynamic arrays reuse the lifetime rules with the <c>_DynArray*</c> family and their type info;
    /// <c>SetLength</c> fills a dims array; <c>a := nil</c> clears; elements are <c>elem</c> nodes, checked under <c>{$R+}</c>.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-325")]
    [Trait("Req", "CMP-330")]
    [Trait("Req", "CMP-331")]
    public void DynamicArrays_UseTheDynArrayFamily()
    {
        var body = Routine(Lower("program T; type TA = array of Integer; TG = array of TA; procedure F(a: TA; const c: TA); var g: TG; " +
            "begin SetLength(g, 2, 3); g[1] := a; a := nil; {$R+} a[0] := Length(c) end; begin end."), "F");

        Assert.Equal(
            "(zero (addr g)) (call _DynArrayAddRef a) " +
            "(block (:= ([] $tmp1 0:Int64) (conv NativeInt 2:Integer)) (:= ([] $tmp1 1:Int64) (conv NativeInt 3:Integer)) " +
            "(call _DynArraySetLen (addr g) (typeinfo TG) 2:NativeInt (addr $tmp1))) " +
            "(call _DynArrayAsg (addr (elem g (conv Int64 1:Integer))) a (typeinfo TA)) (call _DynArrayClr (addr a) (typeinfo TA)) " +
            "(:= (elem! a (conv Int64 0:Integer)) (arraylength c))",
            string.Join(" ", body.Body.Statements.Select(LoweredDump.Of)));
        Assert.Equal("(block (call _DynArrayClr (addr a) (typeinfo TA)) (call _DynArrayClr (addr g) (typeinfo TG)))", Cleanup(body));
    }

    /// <summary>
    /// C-065 (CMP-325 rules 1–3 and 6, ABI-080): managed records and static arrays are zeroed, assigned with <c>_CopyRecord</c>,
    /// finalised with <c>_Finalize</c>; an 8-byte value parameter gets the callee's <c>_AddRef</c>, a larger one is the caller's
    /// copy (made with <c>_CopyRecord</c> into a temporary the caller finalises) and is not counted by the callee.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-325")]
    [Trait("Req", "ABI-080")]
    public void ManagedRecords_UseCopyRecordAddRefAndFinalize()
    {
        var unit = Lower("program T; type TR = record s: string; i: Int64 end; TS = record s: string end; TA = array[1..2] of TR; " +
                         "procedure G(r: TR; s: TS); begin end; " +
                         "procedure F(r: TR; s: TS); var a: TA; begin a[1] := r; G(a[2], s) end; begin end.");
        var f = Routine(unit, "F");

        Assert.Equal(
            "(zero (addr a)) (zero (addr $tmp1)) " +
            "(call _AddRef (addr s) (typeinfo TS)) " +
            "(call _CopyRecord (addr ([] a 0:Int64)) (addr r) (typeinfo TR)) " +
            "(call G (seq (call _CopyRecord (addr $tmp1) (addr ([] a 1:Int64)) (typeinfo TR)) (addr $tmp1)) s)",
            string.Join(" ", f.Body.Statements.Select(LoweredDump.Of)));
        Assert.Equal("(block (call _Finalize (addr s) (typeinfo TS)) (call _Finalize (addr a) (typeinfo TA)) (call _Finalize (addr $tmp1) (typeinfo TR)))", Cleanup(f));
        Assert.Equal("(block (call _Finalize (addr s) (typeinfo TS)))", Cleanup(Routine(unit, "G")));
    }

    /// <summary>A routine without managed parameters, locals or temporaries gets no cleanup scope; neither does a <c>const</c>-only one.</summary>
    [Fact]
    [Trait("Req", "CMP-325")]
    public void ManagedLifetimes_NoneWithoutManagedStorage()
    {
        var unit = Lower("program T; procedure F(i: Integer); begin end; procedure G(const s: string); begin end; begin end.");

        Assert.Equal("none", Cleanup(Routine(unit, "F")));
        Assert.Equal("none", Cleanup(Routine(unit, "G")));
        Assert.Empty(Routine(unit, "G").Body.Statements);
    }

    /// <summary>
    /// CMP-324 / CMP-325 rule 4: <c>_UStrAsg</c> for values, <c>_UStrFromWChar</c> for a character, a call writes into a local the
    /// arguments do not mention, else into a temporary assigned afterwards; a discarded result still gets a temporary; the
    /// function's own <c>Result</c> is never a hidden-Result target (it is the caller's storage).
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-324")]
    [Trait("Req", "CMP-325")]
    [Trait("Req", "ABI-032")]
    public void ManagedAssignments_HelpersAndHiddenResultTargets()
    {
        var unit = Lower("program T; function G(x: string): string; begin Result := G(x) end; " +
                         "procedure F(c: Char); var s, t: string; begin s := 'ab'; s := c; t := G(s); s := G(s); G(t) end; begin end.");
        var f = Routine(unit, "F");
        var g = Routine(unit, "G");

        Assert.Equal(
            "(zero (addr s)) (zero (addr t)) (zero (addr $tmp1)) (zero (addr $tmp2)) " +
            "(call _UStrAsg (addr s) 'ab') (call _UStrFromWChar (addr s) c) (block (:= t (call G:string s))) " +
            "(block (:= $tmp1 (call G:string s)) (call _UStrAsg (addr s) $tmp1)) (block (:= $tmp2 (call G:string t)))",
            string.Join(" ", f.Body.Statements.Select(LoweredDump.Of)));
        Assert.Equal("(block (call _UStrClr (addr s)) (call _UStrClr (addr t)) (call _UStrClr (addr $tmp1)) (call _UStrClr (addr $tmp2)))", Cleanup(f));
        Assert.Equal(
            "(zero (addr $tmp1)) (call _UStrAddRef x) (block (:= $tmp1 (call G:string x)) (call _UStrAsg (addr Result) $tmp1))",
            string.Join(" ", g.Body.Statements.Select(LoweredDump.Of)));
        Assert.Equal("(block (call _UStrClr (addr x)) (call _UStrClr (addr $tmp1)))", Cleanup(g));
    }

    /// <summary>CMP-325 rule 4: a local captured by a nested routine never receives a result directly.</summary>
    [Fact]
    [Trait("Req", "CMP-325")]
    public void ManagedAssignments_CapturedLocalGoesThroughATemporary() =>
        Assert.Equal(
            "(zero (addr s)) (zero (addr $tmp1)) (block (:= $tmp1 (call G:string)) (call _UStrAsg (addr s) $tmp1)) (call N)",
            Body("function G: string; begin Result := 'g' end; procedure F; var s: string; procedure N; begin s := '' end; begin s := G; N end;"));

    /// <summary>
    /// CMP-325 rule 4 (codegen.md lead note C-060/C-061, from C-062): a local whose address is taken (<c>@s</c>, <c>Addr(s)</c>,
    /// also of one of its characters) never receives a result directly — the callee could reach it through the pointer.
    /// </summary>
    [Theory]
    [Trait("Req", "CMP-325")]
    [InlineData("p := @s")]
    [InlineData("p := Addr(s)")]
    [InlineData("p := @s[1]")]
    public void ManagedAssignments_AddressTakenLocalGoesThroughATemporary(string addressOf) =>
        Assert.StartsWith(
            "(zero (addr s)) (zero (addr $tmp1)) (block (:= $tmp1 (call G:string)) (call _UStrAsg (addr s) $tmp1)) (:= p ",
            Body("function G: string; begin Result := 'g' end; procedure F(var p: Pointer); var s: string; begin s := G; " + addressOf + " end;"));

    /// <summary>ABI-023/024, CMP-331: <c>Length</c> and <c>High</c> of a string value, a character at <c>i − 1</c>; checked under <c>{$R+}</c>.</summary>
    [Fact]
    [Trait("Req", "ABI-023")]
    [Trait("Req", "ABI-024")]
    [Trait("Req", "CMP-331")]
    public void StringLengthAndCharacters() =>
        Assert.Equal(
            "(:= @i (length @s)) (:= @i (length @s)) (:= @c (char @s (Subtract:Int64 (conv Int64 @i) 1:Int64))) (:= @c (char! @s 1:Int64))",
            MainBody("s: string; i: Integer; c: Char", "i := Length(s); i := High(s); c := s[i]; {$R+} c := s[2] {$R-}"));

    /// <summary>
    /// CMP-324 (C-061): two operands → <c>_UStrCat</c> into the target, more → a <c>[n x ptr]</c> array and <c>_UStrCatN</c> (a char
    /// part through a managed temporary); <c>=</c>/<c>&lt;&gt;</c> → <c>_UStrEq</c>, against <c>''</c> a nil test, ordering →
    /// <c>_UStrCmp</c> against 0; a character as an assignment target is a string element (made unique by the emitter).
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-324")]
    [Trait("Req", "LANG-243")]
    [Trait("Req", "LANG-244")]
    public void StringOperators_LowerToTheCmp324Helpers() =>
        Assert.Equal(
            "(zero (addr $tmp1)) (call _UStrCat (addr @s) @s @t) " +
            "(block (:= ([] $tmp2 0:Int64) @s) (:= ([] $tmp2 1:Int64) 'x') (:= ([] $tmp2 2:Int64) (seq (call _UStrFromWChar (addr $tmp1) @c) $tmp1)) " +
            "(call _UStrCatN (addr @s) (addr $tmp2) 3:Integer)) " +
            "(:= @f (call _UStrEq:Boolean @s @t)) (:= @f (Not:Boolean (call _UStrEq:Boolean @s @t))) (:= @f (Equal @s '')) (:= @f (NotEqual @s '')) " +
            "(:= @f (Less (call _UStrCmp:Integer @s @t) 0:Integer)) (:= (char @s (Subtract:Int64 (conv Int64 @i) 1:Int64)) @c)",
            MainBody("s, t: string; c: Char; f: Boolean; i: Integer",
                "s := s + t; s := s + 'x' + c; f := s = t; f := s <> t; f := s = ''; f := '' <> s; f := s < t; s[i] := c"));

    /// <summary>A concatenation used as a value is built in a managed temporary (CMP-325 rule 5), finalised at exit.</summary>
    [Fact]
    [Trait("Req", "CMP-325")]
    public void Concatenation_AsAValueUsesATemporary()
    {
        var body = Routine(Lower("program T; function F(const a, b: string): Integer; begin Result := Length(a + b) end; begin end."), "F");

        Assert.Equal("(zero (addr $tmp1)) (:= Result (length (seq (call _UStrCat (addr $tmp1) a b) $tmp1)))",
            string.Join(" ", body.Body.Statements.Select(LoweredDump.Of)));
        Assert.Equal("(block (call _UStrClr (addr $tmp1)))", Cleanup(body));
    }

    /// <summary>
    /// C-062 (CMP-412): the string procedures call their rtl-abi.md §9.3/§9.10 helpers with the variable's address; <c>Copy</c> is
    /// built into the target (rule 4 style, <c>Count</c> omitted = <c>MaxInt</c>), <c>Pos</c> defaults <c>Offset</c> to 1, <c>Str</c>
    /// passes the <c>Write</c> widths (CMP-410: reals 23/−1 without width), <c>UpCase</c> is inline.
    /// </summary>
    [Theory]
    [Trait("Req", "CMP-412")]
    [InlineData("procedure F(var s: string; n: Integer)", "SetLength(s, n)", "(call _UStrSetLen (addr s) n)")]
    [InlineData("procedure F(var s: string; i, n: Integer)", "Delete(s, i, n)", "(call _UStrDelete (addr s) i n)")]
    [InlineData("procedure F(const t: string; var s: string; i: Integer)", "Insert(t, s, i)", "(call _UStrInsert t (addr s) i)")]
    [InlineData("function F(const s: string; i, n: Integer): string", "Result := Copy(s, i, n)", "(call _UStrCopy (addr Result) s i n)")]
    [InlineData("function F(const s: string; i: Integer): string", "Result := Copy(s, i)", "(call _UStrCopy (addr Result) s i 2147483647:Integer)")]
    [InlineData("function F(const s, t: string): Integer", "Result := Pos(t, s)", "(:= Result (call _UStrPos:Integer t s 1:Integer))")]
    [InlineData("function F(const s, t: string; i: Integer): Integer", "Result := Pos(t, s, i)", "(:= Result (call _UStrPos:Integer t s i))")]
    [InlineData("procedure F(var s: string; i: Integer)", "Str(i, s)", "(call _StrInt (addr s) (conv Int64 i) 0:Integer)")]
    [InlineData("procedure F(var s: string; i: Cardinal)", "Str(i:4, s)", "(call _StrUInt (addr s) (conv UInt64 i) 4:Integer)")]
    [InlineData("procedure F(var s: string; d: Double)", "Str(d, s)", "(call _StrFloat (addr s) d 23:Integer -1:Integer)")]
    [InlineData("procedure F(var s: string; d: Single; w: Integer)", "Str(d:w, s)", "(call _StrFloat (addr s) (conv Double d) w -1:Integer)")]
    [InlineData("procedure F(var s: string; d: Double)", "Str(d:8:2, s)", "(call _StrFloat (addr s) d 8:Integer 2:Integer)")]
    [InlineData("function F(c: Char): Char", "Result := UpCase(c)",
        "(:= Result (let $v1 c (select (And:Boolean (GreaterOrEqual $v1 'a':Char) (LessOrEqual $v1 'z':Char)) (conv Char (Subtract:Integer (conv Integer $v1) 32:Integer)) $v1)))")]
    public void StringIntrinsics_CallTheirHelpers(string heading, string statement, string expected) =>
        Assert.Equal(expected, Body($"{heading}; begin {statement} end;"));

    /// <summary>
    /// C-062, CMP-412 / LANG-950: <c>Val</c> reads <c>_ValInt</c>/<c>_ValUInt</c>/<c>_ValFloat</c> with the code in an <c>Integer</c>
    /// temporary; a variable narrower than the helper's result is range-tested (out of range: code := Length(s), value 0).
    /// </summary>
    [Theory]
    [Trait("Req", "CMP-412")]
    [Trait("Req", "LANG-950")]
    [InlineData("v: Int64", "(:= $tmp2 (call _ValInt:Int64 s (addr $tmp1))) (:= v $tmp2) (:= code $tmp1)")]
    [InlineData("v: UInt64", "(:= $tmp2 (call _ValUInt:UInt64 s (addr $tmp1))) (:= v $tmp2) (:= code $tmp1)")]
    [InlineData("v: Double", "(:= $tmp2 (call _ValFloat:Double s (addr $tmp1))) (:= v $tmp2) (:= code $tmp1)")]
    [InlineData("v: Single", "(:= $tmp2 (call _ValFloat:Double s (addr $tmp1))) (:= v (conv Single $tmp2)) (:= code $tmp1)")]
    [InlineData("v: Byte",
        "(:= $tmp2 (call _ValInt:Int64 s (addr $tmp1))) " +
        "(if (And:Boolean (Equal $tmp1 0:Integer) (Or:Boolean (Less $tmp2 0:Int64) (Greater $tmp2 255:Int64))) (block (:= $tmp1 (length s)) (:= $tmp2 0:Int64))) " +
        "(:= v (conv Byte $tmp2)) (:= code $tmp1)")]
    [InlineData("v: Integer",
        "(:= $tmp2 (call _ValInt:Int64 s (addr $tmp1))) " +
        "(if (And:Boolean (Equal $tmp1 0:Integer) (Or:Boolean (Less $tmp2 -2147483648:Int64) (Greater $tmp2 2147483647:Int64))) (block (:= $tmp1 (length s)) (:= $tmp2 0:Int64))) " +
        "(:= v (conv Integer $tmp2)) (:= code $tmp1)")]
    public void Val_ReadsThroughTheHelpers(string variable, string expected) =>
        Assert.Equal("(block " + expected + ")", Body($"procedure F(const s: string; var {variable}; var code: Integer); begin Val(s, v, code) end;"));

    /// <summary>C-062: a string-valued <c>Val</c> source that needs a temporary is evaluated once (its value is reused for <c>Length</c>).</summary>
    [Fact]
    [Trait("Req", "LANG-950")]
    public void Val_EvaluatesTheSourceOnce() =>
        Assert.Equal(
            "(zero (addr $tmp1)) (block (call _UStrCat (addr $tmp1) s s) (:= $tmp3 (call _ValInt:Int64 $tmp1 (addr $tmp2))) " +
            "(if (And:Boolean (Equal $tmp2 0:Integer) (Or:Boolean (Less $tmp3 -32768:Int64) (Greater $tmp3 32767:Int64))) (block (:= $tmp2 (length $tmp1)) (:= $tmp3 0:Int64))) " +
            "(:= v (conv SmallInt $tmp3)) (:= code (conv Word $tmp2)))",
            Body("procedure F(const s: string; var v: SmallInt; var code: Word); begin Val(s + s, v, code) end;"));

    /// <summary>C-060: a string-valued <c>Assert</c> message is passed as its value; a character goes through a managed temporary.</summary>
    [Fact]
    [Trait("Req", "CMP-412")]
    public void Assert_StringValueMessage() =>
        Assert.Matches(
            @"^\(zero \(addr \$tmp1\)\) \(if \(Not:Boolean @f\) \(block \(call _Assert @s '[^']+' 1:Integer\) \(unreachable\)\)\) " +
            @"\(if \(Not:Boolean @f\) \(block \(call _Assert \(seq \(call _UStrFromWChar \(addr \$tmp1\) @c\) \$tmp1\) '[^']+' 1:Integer\) \(unreachable\)\)\)$",
            MainBody("f: Boolean; s: string; c: Char", "Assert(f, s); Assert(f, c)"));

    /// <summary>The program block's temporaries are zeroed and finalised like a routine's; globals are not (rule 7: the unit's $final, C-071).</summary>
    [Fact]
    [Trait("Req", "CMP-325")]
    public void ProgramBlock_FinalisesItsTemporariesOnly()
    {
        var body = Assert.Single(Lower("program T; function G: string; begin Result := 'g' end; var s: string; begin s := G; Writeln(G) end.").Bodies);

        Assert.StartsWith("(zero (addr $tmp1)) (zero (addr $tmp2)) (block (:= $tmp1 (call G:string)) (call _UStrAsg (addr @s) $tmp1))",
            string.Join(" ", body.Body.Statements.Select(LoweredDump.Of)));
        Assert.Equal("(block (call _UStrClr (addr $tmp1)) (call _UStrClr (addr $tmp2)))", Cleanup(body));
    }

    // ------------------------------------------------------------------ static arrays (CMP-308, CMP-331) and big sets (CMP-336)

    /// <summary>CMP-308: the index minus Low in Int64; constant indexes folded; under {$R+} checked against the bounds (CMP-331).</summary>
    [Fact]
    [Trait("Req", "CMP-308")]
    [Trait("Req", "CMP-331")]
    public void ArrayIndex_IsZeroBasedInt64AndRangeCheckedUnderR() =>
        Assert.Equal(
            "(:= ([] @a (Subtract:Int64 (conv Int64 @i) 1:Int64)) ([] @a 2:Int64)) " +
            "(:= ([] ([] @m (conv Int64 @c)) (conv Int64 (range 0..9 @i))) 0:Byte)",
            MainBody("a: array[1..10] of Integer; m: array[Boolean, 0..9] of Byte; i: Integer; c: Boolean",
                "a[i] := a[3]; {$R+} m[c, i] := 0"));

    /// <summary>CMP-307: a field access lowers to <see cref="LField"/> of the lowered record; records are assigned as a whole.</summary>
    [Fact]
    [Trait("Req", "CMP-307")]
    public void FieldAccess_LowersToFieldOfTheRecordVariable() =>
        Assert.Equal(
            "(:= (. ([] (. @r V) 0:Int64) X) (. (. @r P) Y)) (:= (. @r P) @q)",
            Lower("type TP = record X, Y: Integer end; TR = record P: TP; V: array[1..2] of TP end; " +
                  "var r: TR; q: TP; begin r.V[1].X := r.P.Y; r.P := q end.").Bodies.Single().Body.Statements
                .Select(LoweredDump.Of).Aggregate((a, b) => a + " " + b));

    /// <summary>CMP-336: a big-set constructor fills a zeroed temporary; <c>in</c> tests the bit behind an <c>x &lt;u 256</c> guard.</summary>
    [Fact]
    [Trait("Req", "CMP-336")]
    public void BigSet_ConstructorAndIn()
    {
        var text = MainBody("s: set of Char; c: Char; b: Boolean", "s := [c, 'a']; b := c in s");

        Assert.Equal(
            "(:= @s (seq (block (zero (addr $tmp1)) (if (let $v1 (conv Cardinal (conv Integer @c)) (Less $v1 256:Cardinal)) (setbit (addr $tmp1) $v1)) " +
            "(setbit (addr $tmp1) 97:Integer)) $tmp1)) " +
            "(:= @b (let $v2 (conv Cardinal (conv Integer @c)) (let $v3 (addr @s) (ConditionalAnd:Boolean (Less $v2 256:Cardinal) (bit? $v3 $v2)))))",
            text);
    }

    // ------------------------------------------------------------------ typecasts (LANG-290) and Assert (C-057)

    /// <summary>
    /// LANG-290: an ordinal becomes a pointer after extension to 64 bits by its own signedness, a pointer an ordinal by
    /// <c>ptrtoint</c>; a variable typecast is a view of the variable's storage, also as an assignment target.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-290")]
    [Trait("Req", "CMP-313")]
    public void Typecasts_ValueCastsConvertVariableTypecastsView() =>
        Assert.Equal(
            "(:= @p (conv Pointer (conv Int64 @i))) (:= @p (conv Pointer (conv UInt64 @b))) (:= @p (conv Pointer @q)) " +
            "(:= @n (conv NativeInt @p)) (:= @i (view Integer @r)) (:= (view Integer @c) 5:Integer) (:= (. (view TR @i) A) 1:Integer)",
            string.Join(" ", Lower("program T; type TR = record A: Integer end; var p: Pointer; q: PChar; i: Integer; b: Byte; c: Cardinal; " +
                    "n: NativeInt; r: TR; begin p := Pointer(i); p := Pointer(b); p := Pointer(q); n := NativeInt(p); i := Integer(r); " +
                    "Integer(c) := 5; TR(i).A := 1 end.")
                .Bodies.Single().Body.Statements.Select(LoweredDump.Of)));

    /// <summary>
    /// intrinsics.md: <c>Assert(c[, m])</c> under <c>{$C+}</c> is <c>if not c then _Assert(m or nil, file name, line)</c> (noreturn,
    /// ABI-103); under <c>{$C-}</c> it generates nothing.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-412")]
    public void Assert_IsAConditionalNoReturnCallUnlessAssertionsAreOff() =>
        Assert.Equal(
            "(if (Not:Boolean @f) (block (call _Assert 'm' 'test.pas' 1:Integer) (unreachable))) (block) " +
            "(if (Not:Boolean (Greater @i 0:Integer)) (block (call _Assert nil:Pointer 'test.pas' 1:Integer) (unreachable)))",
            MainBody("f: Boolean; i: Integer", "Assert(f, 'm'); {$C-} Assert(f) {$C+}; Assert(i > 0)"));

    /// <summary>
    /// C-176 (intrinsics.md, rtl-abi.md: a count ≤ 0 does nothing): a variable count is evaluated once into a temporary and
    /// guards the <c>llvm.memset</c>/<c>llvm.memmove</c> with <c>count &gt; 0</c>; a positive constant count needs no guard, a
    /// constant count ≤ 0 generates nothing.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-412")]
    public void MoveFill_NonPositiveCountDoesNothing() =>
        Assert.Equal(
            "(block (:= $tmp1 (conv NativeInt @i)) (if (Greater $tmp1 0:NativeInt) (memset @a $tmp1 (conv Byte 7:Integer)))) " +
            "(block (:= $tmp2 @n) (if (Greater $tmp2 0:NativeInt) (memmove @a @b $tmp2))) " +
            "(memset @a 4:NativeInt (conv Byte 0:Integer)) (memmove @a @b 2:NativeInt) (block) (block)",
            MainBody("a, b: array[0..3] of Byte; i: Integer; n: NativeInt",
                "FillChar(a, i, 7); Move(a, b, n); FillChar(a, 4, 0); Move(a, b, 2); FillChar(a, 0, 1); Move(a, b, -3)"));

    // ------------------------------------------------------------------ for (CMP-321, LANG-361)

    [Fact]
    [Trait("Req", "CMP-321")]
    public void For_To_LowersToTestedLoopWithIncrementAfterTheEqualityTest()
    {
        var unit = Lower("program T; var i, s: Integer; begin for i := 1 to 10 do s := s + i end.");
        var main = Assert.Single(unit.Bodies);

        Assert.Equal(
            "(block (:= $tmp1 1:Integer) (:= $tmp2 10:Integer) (if (LessOrEqual $tmp1 $tmp2) (block (:= @i $tmp1) " +
            "(loop for.1 (:= @s (Add:Integer @s @i)) (block (if (Equal @i $tmp2) (break for.1)) (:= @i (Add:Integer @i 1:Integer)))))))",
            LoweredDump.Of(Assert.Single(main.Body.Statements)));
        Assert.Equal("$tmp1:Integer $tmp2:Integer", Locals(main));
        Assert.Null(main.Routine);
    }

    [Fact]
    [Trait("Req", "CMP-321")]
    public void For_DownTo_TestsGreaterOrEqualAndDecrements() =>
        Assert.Equal(
            "(block (:= $tmp1 10:Integer) (:= $tmp2 @n) (if (GreaterOrEqual $tmp1 $tmp2) (block (:= @i $tmp1) " +
            "(loop for.1 (block) (block (if (Equal @i $tmp2) (break for.1)) (:= @i (Subtract:Integer @i 1:Integer)))))))",
            MainBody("i, n: Integer", "for i := 10 downto n do begin end"));

    /// <summary>Byte variable: the step is computed in the computation type and truncated back (no overflow at High).</summary>
    [Fact]
    [Trait("Req", "CMP-321")]
    public void For_ByteVariable_StepsInTheComputationType()
    {
        var unit = Lower("program T; procedure P; var b: Byte; begin for b := 0 to 255 do end; begin end.");
        var p = Routine(unit, "P");

        Assert.Equal(
            "(block (:= $tmp1 0:Byte) (:= $tmp2 255:Byte) (if (LessOrEqual $tmp1 $tmp2) (block (:= b $tmp1) (loop for.1 (block) " +
            "(block (if (Equal b $tmp2) (break for.1)) (:= b (conv Byte (Add:Integer (conv Integer b) 1:Integer))))))))",
            LoweredDump.Of(Assert.Single(p.Body.Statements)));
        Assert.Equal("b:Byte $tmp1:Byte $tmp2:Byte", Locals(p));
    }

    // ------------------------------------------------------------------ Read/Readln, Eof/Eoln (CMP-411; C-066)

    /// <summary>
    /// CMP-411: one helper per variable — <c>_ReadInt</c> stored directly for Int64, through a checked temporary (runtime error
    /// 106 outside the variable's range) for narrower types, <c>_ReadUInt</c> for UInt64, <c>_ReadFloat</c> converted for
    /// Single, <c>_ReadWChar</c>, <c>_ReadUStr(T, @s)</c> — then <c>_ReadLn</c>, <c>_ReadEnd</c> and <c>_IOCheck</c> under
    /// <c>{$I+}</c>; the file is <c>Input</c> or the given <c>Text</c> variable.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-411")]
    [Trait("Req", "LANG-942")]
    public void Readln_HelperPerVariableThenLnEndAndIOCheck() =>
        Assert.Equal(
            "(block (:= @q (call _ReadInt:Int64 (addr @System.Input))) " +
            "(:= $tmp1 (call _ReadInt:Int64 (addr @System.Input))) " +
            "(if (ConditionalOr:Boolean (Less $tmp1 0:Int64) (Greater $tmp1 255:Int64)) (block (call _RunError 106:Integer) (unreachable))) " +
            "(:= @b (conv Byte $tmp1)) " +
            "(:= @u (call _ReadUInt:UInt64 (addr @System.Input))) (:= @g (conv Single (call _ReadFloat:Double (addr @System.Input)))) " +
            "(:= @c (call _ReadWChar:Char (addr @System.Input))) (call _ReadUStr (addr @System.Input) (addr @s)) " +
            "(call _ReadLn (addr @System.Input)) (call _ReadEnd (addr @System.Input)) (call _IOCheck)) " +
            "(block (call _ReadEnd (addr @t)))",
            MainBody("q: Int64; b: Byte; u: UInt64; g: Single; c: Char; s: string; t: Text", "Readln(q, b, u, g, c, s); {$I-} Read(t)"));

    [Fact]
    [Trait("Req", "LANG-946")]
    public void EofEoln_CallTheirHelpers() =>
        Assert.Equal(
            "(:= @f (call _Eof:Boolean (addr @System.Input))) (:= @f (call _Eoln:Boolean (addr @t)))",
            MainBody("f: Boolean; t: Text", "f := Eof; f := Eoln(t)"));

    /// <summary>
    /// C-182 / ABI-132: in <c>{$I+}</c> state a call statement whose callee is a <c>System</c> routine with a first parameter
    /// <c>var … : Text</c> is followed by <c>_IOCheck</c> (procedures and discarded function results alike); not under
    /// <c>{$I-}</c>, not for a <c>Text</c> parameter in another position and not for the program's own routines.
    /// </summary>
    [Fact]
    [Trait("Req", "ABI-132")]
    public void TextRoutineCall_IsFollowedByIOCheck()
    {
        var types = new FreexComp.Compiler.Types.BuiltInTypes();
        var system = Compiler.Binding.Binder.BindCompilationUnit(Compiler.Syntax.SyntaxTree.Parse(SourceText.From(@"C:\p\System.pas", """
            unit System;
            interface
            var Input, Output: Text;
            procedure Reset(var F: Text);
            function Probe(var F: Text): Integer;
            procedure Other(I: Integer; var F: Text);
            implementation
            procedure Reset(var F: Text); begin end;
            function Probe(var F: Text): Integer; begin Result := 0 end;
            procedure Other(I: Integer; var F: Text); begin end;
            end.
            """)), types);
        Assert.Empty(Format(system.Diagnostics));
        var tree = Compiler.Syntax.SyntaxTree.Parse(SourceText.From(TestPath, """
            program T;
            var F: Text;
            procedure Own(var F: Text); begin end;
            begin Reset(F); Probe(F); Other(1, F); Own(F); {$I-} Reset(F) end.
            """));
        var result = Compiler.Binding.Binder.BindCompilationUnit(tree, types, system);
        Assert.Empty(Format(result.Diagnostics));
        var body = Assert.Single(Lowerer.Lower(result).Bodies);
        Assert.Equal(
            "(block (call Reset (addr @F)) (call _IOCheck)) (block (call Probe (addr @F)) (call _IOCheck)) (call Other 1:Integer (addr @F)) " +
            "(call Own (addr @F)) (call Reset (addr @F))",
            string.Join(" ", body.Body.Statements.Select(LoweredDump.Of)));
    }

    // ------------------------------------------------------------------ Write/Writeln (CMP-410; C-040)

    [Fact]
    [Trait("Req", "CMP-410")]
    public void Writeln_ToOutput_HelperPerArgumentThenLnEndAndIOCheck() =>
        Assert.Equal(
            "(block (call _WriteUStr (addr @System.Output) 'ab' 0:Integer) (call _WriteInt (addr @System.Output) (conv Int64 @i) @i) " +
            "(call _WriteLn (addr @System.Output)) (call _WriteEnd (addr @System.Output)) (call _IOCheck)) " +
            "(block (call _WriteLn (addr @System.Output)) (call _WriteEnd (addr @System.Output)))",
            MainBody("i: Integer", "Writeln('ab', i:i); {$I-} Writeln()"));

    /// <summary>
    /// C-059 / CMP-410: a real goes to <c>_WriteFloat(T, v as Double, w, d)</c> — <c>Write(x)</c> passes 23 and −1,
    /// <c>Write(x:w)</c> w and −1, <c>Write(x:w:d)</c> w and d.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-410")]
    public void Writeln_Reals_WriteFloatWithWidthAndDecimals() =>
        Assert.Equal(
            "(block (call _WriteFloat (addr @System.Output) @d 23:Integer -1:Integer) (call _WriteEnd (addr @System.Output))) " +
            "(block (call _WriteFloat (addr @System.Output) (conv Double @s) @i -1:Integer) (call _WriteEnd (addr @System.Output))) " +
            "(block (call _WriteFloat (addr @System.Output) @d 0:Integer @i) (call _WriteLn (addr @System.Output)) (call _WriteEnd (addr @System.Output)))",
            MainBody("d: Double; s: Single; i: Integer", "{$I-} Write(d); Write(s:i); Writeln(d:0:i)"));

    // ------------------------------------------------------------------ checks (CMP-312, 314; C-039)

    [Fact]
    [Trait("Req", "CMP-314")]
    public void Checks_ForVariableUnderBothSwitches_BoundsCheckedStepNot()
    {
        var unit = Lower("program T; {$R+}{$Q+} procedure P(n: Integer); var b: Byte; begin for b := 1 to n do end; begin end.");

        Assert.Equal(
            "(block (:= $tmp1 1:Byte) (:= $tmp2 (conv Byte (range 0..255 n))) (if (LessOrEqual $tmp1 $tmp2) (block (:= b $tmp1) (loop for.1 (block) " +
            "(block (if (Equal b $tmp2) (break for.1)) (:= b (conv Byte (Add:Integer (conv Integer b) 1:Integer))))))))",
            LoweredDump.Of(Assert.Single(Routine(unit, "P").Body.Statements)));
    }

    [Fact]
    [Trait("Req", "CMP-314")]
    public void Checks_RangeBoundsAreIntersectedWithTheSourceType() =>
        Assert.Equal(
            "(:= Result (conv Integer (range 0..2147483647 c))) (:= Result (conv Integer c)) (:= Result (conv Integer b))",
            Body("{$R+} function F(c: Cardinal; b: Byte): Integer; begin Result := c; Result := Integer(c); Result := b end;"));

    [Fact]
    [Trait("Req", "CMP-312")]
    public void Checks_IncUnderQPlus_IsAnOverflowCheckedAdd_ChecksOff_Plain()
    {
        Assert.Equal("(:= i (Add!:Integer i 1:Integer)) (:= i (Negate!:Integer i))",
            Body("{$Q+} procedure F(var i: Integer); begin Inc(i); i := -i end;"));
        Assert.Equal("(:= i (Add:Integer i 1:Integer)) (:= i (Negate:Integer i))",
            Body("procedure F(var i: Integer); begin Inc(i); i := -i end;"));
    }

    [Fact]
    [Trait("Req", "LANG-363")]
    public void For_BreakAndContinue_TargetTheLoop_ContinueRunsTheStep() =>
        Assert.Contains(
            "(loop for.1 (block (if (Equal @i 2:Integer) (continue for.1)) (if (Equal @i 3:Integer) (break for.1))) (block (if (Equal @i $tmp2)",
            MainBody("i: Integer", "for i := 1 to 3 do begin if i = 2 then Continue; if i = 3 then Break end"));

    [Fact]
    [Trait("Req", "LANG-363")]
    public void For_Nested_HaveOwnLabelsAndTemporaries_BreakLeavesTheInnerLoop()
    {
        var text = MainBody("i, j: Integer", "for i := 1 to 2 do for j := i to 3 do Break");

        Assert.Contains("(:= $tmp3 @i) (:= $tmp4 3:Integer) (if (LessOrEqual $tmp3 $tmp4) (block (:= @j $tmp3) (loop for.2 (break for.2)", text);
        Assert.Contains("(if (Equal @i $tmp2) (break for.1))", text);
    }

    [Fact]
    [Trait("Req", "CMP-321")]
    public void For_CharAndBooleanVariables()
    {
        var text = MainBody("c: Char; f: Boolean", "for c := 'a' to 'z' do; for f := False to True do");

        Assert.Contains("(:= @c (conv Char (Add:Integer (conv Integer @c) 1:Integer)))", text);
        Assert.Contains("(:= $tmp3 False:Boolean) (:= $tmp4 True:Boolean)", text);
        Assert.Contains("(:= @f (conv Boolean (Add:Integer (conv Integer @f) 1:Integer)))", text);
    }

    // ------------------------------------------------------------------ while, repeat (LANG-360, CMP-319)

    [Fact]
    [Trait("Req", "CMP-319")]
    public void While_IsALoopWhoseBodyTestsFirst() =>
        Assert.Equal(
            "(loop while.1 (if (Less @i 3:Integer) (block (if (Equal @i 1:Integer) (continue while.1)) (:= @i (Add:Integer @i 1:Integer))) (break while.1)))",
            MainBody("i: Integer", "while i < 3 do begin if i = 1 then Continue; i := i + 1 end"));

    [Fact]
    [Trait("Req", "CMP-319")]
    public void Repeat_TestsInTheStep_SoContinueReachesTheTest() =>
        Assert.Equal(
            "(loop repeat.1 (block (:= @i (Add:Integer @i 1:Integer)) (continue repeat.1)) (if (GreaterOrEqual @i 3:Integer) (break repeat.1)))",
            MainBody("i: Integer", "repeat i := i + 1; Continue until i >= 3"));

    // ------------------------------------------------------------------ case (CMP-322, LANG-355…357)

    [Fact]
    [Trait("Req", "CMP-322")]
    public void Case_ValuesAndSmallRangesAreListed_LargeRangesKept_ElseIsTheDefault() =>
        Assert.Equal(
            "(switch @i (case [1] [] (:= @s 1:Integer)) (case [2 4 5 6] [] (:= @s 2:Integer)) (case [] [10..1000] (:= @s 3:Integer)) " +
            "(default (block (:= @s 0:Integer))))",
            MainBody("i, s: Integer", "case i of 1: s := 1; 2, 4..6: s := 2; 10..1000: s := 3 else s := 0 end"));

    [Theory]
    [InlineData("1..256", 256, 0)]
    [InlineData("1..257", 0, 1)]
    [InlineData("-5..5, 300..301", 13, 0)]
    [Trait("Req", "CMP-322")]
    public void Case_RangesUpTo256ValuesAreExpanded(string labels, int values, int ranges)
    {
        var unit = Lower($"program T; var i: Integer; begin case i of {labels}: i := 0 end end.");
        var section = Assert.Single(Assert.IsType<LSwitch>(Assert.Single(unit.Bodies).Body.Statements[0]).Sections);

        Assert.Equal((values, ranges), (section.Values.Length, section.Ranges.Length));
    }

    [Fact]
    [Trait("Req", "CMP-322")]
    public void Case_CharSelector_IsWidenedToInteger_NoElseNoDefault() =>
        Assert.Equal(
            "(switch (conv Integer @c) (case [97] [] (:= @c 'b':Char)) (case [120 121 122] [] (:= @c 'a':Char)))",
            MainBody("c: Char", "case c of 'a': c := 'b'; 'x'..'z': c := 'a' end"));

    [Theory]
    [InlineData("Byte", "(conv Integer @x)")]
    [InlineData("SmallInt", "(conv Integer @x)")]
    [InlineData("Integer", "@x")]
    [InlineData("Cardinal", "@x")]
    [InlineData("Int64", "@x")]
    [InlineData("UInt64", "@x")]
    [Trait("Req", "CMP-322")]
    public void Case_SelectorInItsComputationType(string type, string selector) =>
        Assert.StartsWith($"(switch {selector} (case [5]", MainBody("x: " + type, "case x of 5: x := 1 end"));

    [Fact]
    [Trait("Req", "CMP-322")]
    public void Case_BooleanSelector_LabelsAreOrdinals() =>
        Assert.Equal(
            "(switch (conv Integer @f) (case [1] [] (:= @f False:Boolean)) (case [0] [] (:= @f True:Boolean)))",
            MainBody("f: Boolean", "case f of True: f := False; False: f := True end"));

    // ------------------------------------------------------------------ intrinsics (CMP-412, intrinsics.md §1)

    [Theory]
    [InlineData("x: Integer", "Succ(x)", "(Add:Integer x 1:Integer)")]
    [InlineData("x: Integer", "Pred(x)", "(Subtract:Integer x 1:Integer)")]
    [InlineData("x: Int64", "Succ(x)", "(conv Integer (Add:Int64 x 1:Int64))")]
    [InlineData("x: Byte", "Pred(x)", "(conv Integer (conv Byte (Subtract:Integer (conv Integer x) 1:Integer)))")]
    [InlineData("x: Char", "Ord(x)", "(conv Integer x)")]
    [InlineData("x: Integer", "Ord(x)", "x")]
    [InlineData("x: Boolean", "Ord(x)", "(conv Integer x)")]
    [InlineData("x: Integer", "Ord(Chr(x))", "(conv Integer (conv Char x))")]
    [InlineData("x: Integer", "Ord(Odd(x))", "(conv Integer (NotEqual (And:Integer x 1:Integer) 0:Integer))")]
    [InlineData("x: Integer", "Abs(x)", "(let $v1 x (select (Less $v1 0:Integer) (Negate:Integer $v1) $v1))")]
    [InlineData("x: Byte", "Abs(x)", "(let $v1 (conv Integer x) (select (Less $v1 0:Integer) (Negate:Integer $v1) $v1))")]
    [InlineData("x: Integer", "Sqr(x)", "(let $v1 x (Multiply:Integer $v1 $v1))")]
    [InlineData("x: Integer", "Lo(x)", "(conv Integer (conv Byte x))")]
    [InlineData("x: Word", "Hi(x)", "(conv Integer (conv Byte (Shr:Integer (conv Integer x) 8:Integer)))")]
    [InlineData("x: Int64", "Hi(x)", "(conv Integer (conv Byte (Shr:Int64 x 8:Int64)))")]
    [InlineData("x: Integer", "Swap(x)",
        "(conv Integer (conv Word (let $v1 x (Or:Integer (Shl:Integer (And:Integer $v1 255:Integer) 8:Integer) (And:Integer (Shr:Integer $v1 8:Integer) 255:Integer)))))")]
    [InlineData("x: Integer", "Succ(5) + SizeOf(Int64) + High(Byte)", "269:Integer")]
    [Trait("Req", "CMP-412")]
    public void Intrinsic_ValueFunctions(string parameter, string expression, string expected) =>
        Assert.Equal($"(:= Result {expected})", Body($"function F({parameter}): Integer; begin Result := {expression} end;"));

    [Theory]
    [InlineData("x: Double", "Abs(x)", "(Abs:Double x)")]
    [InlineData("x: Double", "Sqr(x)", "(let $v1 x (Multiply:Double $v1 $v1))")]
    [Trait("Req", "CMP-412")]
    public void Intrinsic_RealAbsAndSqr(string parameter, string expression, string expected) =>
        Assert.Equal($"(:= Result {expected})", Body($"function F({parameter}): Double; begin Result := {expression} end;"));

    /// <summary>C-059 / CMP-412: <c>Trunc</c>/<c>Round</c>/<c>Int</c>/<c>Frac</c> call their RTL helpers (rtl-abi.md §9.10) with a Double; <c>Sqrt</c> is <c>llvm.sqrt.f64</c>.</summary>
    [Theory]
    [InlineData("Trunc(x)", "Int64", "(call _Trunc:Int64 x)")]
    [InlineData("Round(s)", "Int64", "(call _Round:Int64 (conv Double s))")]
    [InlineData("Int(x)", "Double", "(call _Int:Double x)")]
    [InlineData("Frac(i)", "Double", "(call _Frac:Double (conv Double i))")]
    [InlineData("Sqrt(x)", "Double", "(Sqrt:Double x)")]
    [Trait("Req", "CMP-412")]
    public void Intrinsic_RealFunctions(string expression, string resultType, string expected) =>
        Assert.Equal($"(:= Result {expected})",
            Body($"function F(x: Double; s: Single; i: Integer): {resultType}; begin Result := {expression} end;"));

    [Fact]
    [Trait("Req", "CMP-412")]
    public void Intrinsic_TempsAreNumberedPerBody() =>
        Assert.Equal(
            "(:= Result (Add:Integer (let $v1 x (Multiply:Integer $v1 $v1)) (let $v2 x (select (Less $v2 0:Integer) (Negate:Integer $v2) $v2))))",
            Body("function F(x: Integer): Integer; begin Result := Sqr(x) + Abs(x) end;"));

    [Theory]
    [InlineData("var x: Integer", "Inc(x)", "(:= x (Add:Integer x 1:Integer))")]
    [InlineData("var x: Integer", "Dec(x, 5)", "(:= x (Subtract:Integer x 5:Integer))")]
    [InlineData("var x: Byte", "Inc(x, 3)", "(:= x (conv Byte (Add:Integer (conv Integer x) 3:Integer)))")]
    [InlineData("var x: Int64; n: Integer", "Dec(x, n)", "(:= x (Subtract:Int64 x (conv Int64 n)))")]
    [InlineData("var x: Byte; n: Int64", "Inc(x, n)", "(:= x (conv Byte (Add:Integer (conv Integer x) (conv Integer n))))")]
    [InlineData("var x: Char", "Inc(x)", "(:= x (conv Char (Add:Integer (conv Integer x) 1:Integer)))")]
    [InlineData("var x: Cardinal", "Dec(x)", "(:= x (Subtract:Cardinal x 1:Cardinal))")]
    [Trait("Req", "CMP-412")]
    public void Intrinsic_IncDec_AssignInline(string parameter, string statement, string expected) =>
        Assert.Equal(expected, Body($"procedure F({parameter}); begin {statement} end;"));

    [Theory]
    [InlineData("RunError", "(block (call _RunError 0:Integer) (unreachable))")]
    [InlineData("RunError(204)", "(block (call _RunError 204:Integer) (unreachable))")]
    [InlineData("Halt", "(block (call _Halt 0:Integer) (unreachable))")]
    [InlineData("Halt(3)", "(block (call _Halt 3:Integer) (unreachable))")]
    [Trait("Req", "CMP-356")]
    public void Intrinsic_HaltAndRunError_CallNoReturnHelpers(string statement, string expected)
    {
        var body = Assert.Single(Lower($"program T; begin {statement} end.").Bodies);
        var call = Assert.IsType<LCallStatement>(Assert.IsType<LBlock>(body.Body.Statements[0]).Statements[0]);

        Assert.Equal(expected, LoweredDump.Of(body.Body.Statements[0]));
        var helper = Assert.IsType<LHelperCallee>(call.Callee).Helper;
        Assert.True(helper.IsNoReturn);
        Assert.Null(helper.ReturnType);
        Assert.Equal(["Integer"], helper.ParameterTypes.Select(t => t.Name));
        Assert.Equal(new Compiler.Emit.LinkName("System." + helper.Name, true), helper.LinkName);
    }

    // ------------------------------------------------------------------ routines, calls, exit (CMP-310)

    [Fact]
    [Trait("Req", "CMP-310")]
    public void Exit_WithValue_AssignsResultThenReturns() =>
        Assert.Equal(
            "(if (Less n 0:Integer) (block (:= Result 7:Integer) (return))) (:= Result n) (return)",
            Body("function F(n: Integer): Integer; begin if n < 0 then Exit(7); Result := n; Exit end;"));

    [Fact]
    [Trait("Req", "CMP-310")]
    public void Locals_AreResultThenDeclaredLocalsThenTemporaries_ParametersAreNotLocals()
    {
        var unit = Lower("program T; function F(n: Integer): Int64; var a, b: Byte; begin for a := 1 to 2 do b := a; Result := b end; begin end.");
        var f = Routine(unit, "F");

        Assert.Equal("Result:Int64 a:Byte b:Byte $tmp1:Byte $tmp2:Byte", Locals(f));
        Assert.All(f.Locals.Take(3), l => Assert.NotNull(l.Variable));
        Assert.All(f.Locals.Skip(3), l => Assert.Null(l.Variable));
    }

    [Fact]
    [Trait("Req", "CMP-310")]
    public void Calls_VarAndOutArgumentsPassTheirAddress()
    {
        var unit = Lower("program T; var i, s: Integer; procedure P(var a: Integer; out o: Integer; b: Byte; const c: Integer); begin a := b; o := c end; " +
            "function G(x: Integer): Integer; begin Result := x end; begin P(i, s, 2, G(i)); G(3) end.");

        Assert.Equal("(call P (addr @i) (addr @s) 2:Byte (call G:Integer @i)) (call G 3:Integer)",
            string.Join(" ", Assert.Single(unit.Bodies).Body.Statements.Select(LoweredDump.Of)));
        Assert.Equal("(:= a (conv Integer b)) (:= o c)", string.Join(" ", Routine(unit, "P").Body.Statements.Select(LoweredDump.Of)));
    }

    [Fact]
    public void Expressions_ConversionsOperatorsAndShortCircuit() =>
        Assert.Equal(
            "(if (ConditionalAnd:Boolean (Greater @i 0:Integer) (Not:Boolean @f)) (:= @q (Multiply:Int64 (conv Int64 (Negate:Integer @i)) @q)) (:= @f True:Boolean))",
            MainBody("i: Integer; q: Int64; f: Boolean", "if (i > 0) and not f then q := (-i) * q else f := True"));

    [Fact]
    public void UnitBodies_InitializationAndFinalizationAreLowered()
    {
        var unit = Lower("unit U; interface var X: Integer; implementation initialization X := 1; finalization X := 0; end.");

        Assert.Equal(["(:= @X 1:Integer)", "(:= @X 0:Integer)"], unit.Bodies.Select(b => string.Join(" ", b.Body.Statements.Select(LoweredDump.Of))));
        Assert.Equal("U", unit.Unit.Name);
    }

    /// <summary>
    /// CMP-345: a variable of an enclosing routine lowers to <see cref="LOuterRef"/>; the owner lists its captured variables in
    /// its <see cref="LoweredFrame"/> (scope order).
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-345")]
    public void CapturedVariables_LowerToOuterReferences_AndTheOwnerGetsAFrame()
    {
        var unit = Lower("program T; procedure Outer(p: Integer); var n, m: Integer; procedure Inner; begin n := p end; begin Inner end; begin end.");
        var outer = Routine(unit, "Outer");
        var inner = Routine(unit, "Inner");

        Assert.Equal("(:= ^n ^p)", LoweredDump.Of(Assert.Single(inner.Body.Statements)));
        Assert.Equal(["p", "n"], outer.Frame!.Variables.Select(v => v.Name));
        Assert.Null(inner.Frame);
        Assert.Equal("(call Inner)", LoweredDump.Of(Assert.Single(outer.Body.Statements)));
    }

    /// <summary>A nested routine that itself has nested routines gets a frame (field 0 only) so the static link chain can be walked.</summary>
    [Fact]
    [Trait("Req", "CMP-345")]
    public void IntermediateNestedRoutine_GetsALinkOnlyFrame_TopLevelWithoutCapturesGetsNone()
    {
        var unit = Lower("program T; procedure A; procedure B; procedure C; begin end; begin C end; begin B end; begin end.");

        Assert.Null(Routine(unit, "A").Frame);
        Assert.Empty(Routine(unit, "B").Frame!.Variables);
        Assert.Null(Routine(unit, "C").Frame);
    }

    /// <summary>
    /// C-069: the default file of <c>Write</c> is always the bound <c>System.Output</c> (LANG-910) — a unit bound without a
    /// <c>System</c> no longer gets a synthetic <c>System.Output</c> reference.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-910")]
    public void Write_WithoutABoundSystem_IsRejected()
    {
        var tree = Compiler.Syntax.SyntaxTree.Parse(SourceText.From(TestPath, "program T; begin Writeln(1) end."));
        var result = Compiler.Binding.Binder.BindCompilationUnit(tree, new FreexComp.Compiler.Types.BuiltInTypes());
        Assert.Empty(Format(result.Diagnostics));
        Assert.Contains("System.Output", Assert.Throws<InvalidOperationException>(() => Lowerer.Lower(result)).Message);
    }
}
