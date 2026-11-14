using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>Pointers (task C-056: LANG-206, 208, 247, 265…268; intrinsics New/Dispose, GetMem/FreeMem, FillChar/Move).</summary>
[Trait("Phase", "2")]
public sealed class PointerBindingTests
{
    [Fact]
    [Trait("Req", "LANG-208")]
    [Trait("Req", "LANG-265")]
    public void ForwardPointer_IsCompletedAtTheEndOfTheTypeSection()
    {
        var (result, _) = Bind("type PNode = ^TNode; TNode = record Next: PNode; V: Integer end; begin end.");
        Assert.Empty(result.Diagnostics);

        var pointer = Assert.IsType<PointerTypeSymbol>(result.Declarations.Scope.Lookup("PNode"));
        var record = Assert.IsType<RecordTypeSymbol>(result.Declarations.Scope.Lookup("TNode"));
        Assert.Same(record, pointer.PointedType);
        Assert.Same(pointer, record.Fields[0].Type);
        Assert.Equal((8L, 8), (pointer.Size, pointer.Alignment));
    }

    /// <summary>C-179: <c>^string</c> (reserved word) and <c>^AnsiString</c> are typed pointers to the string types.</summary>
    [Fact]
    [Trait("Req", "LANG-265")]
    public void PointerToString()
    {
        var (result, _) = Bind("""
            type PStr = ^string; PAStr = ^AnsiString;
            var s: string; a: AnsiString; p: PStr; q: PAStr; v: ^string;
            begin p := @s; p^ := 'x'; s := p^ + 'y'; v := p; q := @a; q^ := 'z' end.
            """);
        Assert.Empty(Format(result.Diagnostics));

        var pointer = Assert.IsType<PointerTypeSymbol>(result.Declarations.Scope.Lookup("PStr"));
        Assert.Same(result.Types.String, pointer.PointedType);
        var ansi = Assert.IsType<PointerTypeSymbol>(result.Declarations.Scope.Lookup("PAStr"));
        Assert.Same(result.Types.AnsiString, ansi.PointedType);
    }

    [Theory]
    [Trait("Req", "LANG-206")]
    [Trait("Req", "LANG-265")]
    [Trait("Req", "LANG-266")]
    [Trait("Req", "LANG-267")]
    [InlineData("type PInt = ^Integer; var p: PInt; q: ^Integer; begin p := q; q := p end.")]
    [InlineData("type P = ^Integer; procedure F(var x: P); begin end; var q: ^Integer; begin F(q) end.")]
    [InlineData("var p: ^Integer; u: Pointer; begin u := p; p := u; p := nil; u := nil end.")]
    [InlineData("var i: Integer; p: ^Integer; begin p := @i; p^ := 1; i := p^ + 1; Inc(p^) end.")]
    [InlineData("type TR = record X: Integer end; var r: TR; p: ^TR; begin p := @r; p^.X := 1; p.X := p.X + 1 end.")]
    [InlineData("type TA = array[0..3] of Byte; var a: TA; p: ^TA; begin p := @a; p[1] := p^[2] end.")]
    [InlineData("var p, q: ^Integer; b: Boolean; begin b := (p = q) or (p <> nil) or Assigned(p) end.")]
    [InlineData("var c: PChar; n: NativeInt; b: Boolean; begin c := c + 1; c := 2 + c; c := c - 1; n := c - c; b := c < c; c[0] := 'x' end.")]
    [InlineData("{$POINTERMATH ON} var p: ^Integer; u: Pointer; n: NativeInt; begin p := p + 1; n := p - p; u := u + 1; p[2] := 0 end.")]
    [InlineData("var p: ^Integer; begin Inc(p); Dec(p, 2) end.")]
    [InlineData("procedure P; begin end; var u: Pointer; begin u := @P; u := Addr(u); u := Ptr(4096) end.")]
    [InlineData("var p: ^Integer; begin New(p); Dispose(p); GetMem(p, 8); ReallocMem(p, 16); FreeMem(p) end.")]
    [InlineData("var a: array[0..3] of Byte; u: Pointer; begin FillChar(a, SizeOf(a), 0); FillChar(u^, 4, 'x'); Move(a, u^, 4); Move(a[0], a[1], 2) end.")]
    [InlineData("var G: Integer; const C: ^Integer = @G; T: array[0..1] of Pointer = (@G, nil); N: Pointer = nil; begin end.")]
    [InlineData("procedure P; begin end; const C: Pointer = @P; begin end.")]
    public void PointerConstructs_Bind(string text)
    {
        Assert.Equal([], BindDiagnostics(text));
    }

    [Theory]
    [Trait("Req", "LANG-208")]
    [Trait("Req", "LANG-247")]
    [Trait("Req", "LANG-265")]
    [Trait("Req", "LANG-267")]
    [Trait("Req", "LANG-268")]
    [InlineData("type P = ^TMissing; begin end.", "E2015(1,11): Unsatisfied forward declaration of type 'TMissing'")]
    [InlineData("type P = ^T; type T = Integer; begin end.", "E2015(1,11): Unsatisfied forward declaration of type 'T'")]
    [InlineData("var p: ^Integer; q: ^Byte; begin p := q end.", "E2010(1,39): Incompatible types: '^Byte' and '^Integer'")]
    [InlineData("var i: Integer; begin i := i^ end.", "E2018(1,28): Pointer type required")]
    [InlineData("var u: Pointer; i: Integer; begin i := u^ end.", "E2018(1,40): Pointer type required")]
    [InlineData("var p: ^Integer; begin p := @1 end.", "E2033(1,30): Cannot take the address of this expression")]
    [InlineData("var p: ^Integer; begin p := p + 1 end.", "E2011(1,31): Operator '+' not applicable to operand type(s) '^Integer' and 'Integer'")]
    [InlineData("var p, q: ^Integer; b: Boolean; begin b := p < q end.", "E2011(1,46): Operator '<' not applicable to operand type(s) '^Integer' and '^Integer'")]
    [InlineData("var u: Pointer; begin Inc(u) end.", "E2013(1,27): Ordinal type required")]
    [InlineData("var u: Pointer; begin New(u) end.", "E2018(1,27): Pointer type required")]
    [InlineData("var i: Integer; begin GetMem(i, 4) end.", "E2010(1,30): Incompatible types: 'Integer' and 'Pointer'")]
    [InlineData("var a: array[0..1] of Byte; begin FillChar(a, 2, 1.5) end.", "E2010(1,50): Incompatible types: 'Double' and 'Byte'")]
    [InlineData("var a: array[0..1] of Byte; begin Move(a, 1, 2) end.", "E2033(1,43): Cannot take the address of this expression")]
    [InlineData("var i: Integer; const C: Pointer = @i + 1; begin end.", "E2050(1,36): Constant expression expected")]
    [InlineData("procedure P; var L: Integer; const C: Pointer = @L; begin end; begin end.", "E2033(1,50): Cannot take the address of this expression")]
    public void PointerErrors(string text, string expected)
    {
        Assert.Equal([expected], BindDiagnostics(text));
    }

    [Fact]
    [Trait("Req", "LANG-266")]
    public void Nil_HasThePointerCompatibleNilType()
    {
        var (result, _) = Bind("const N = nil; var p: ^Integer; begin p := N end.");
        Assert.Empty(result.Diagnostics);

        var constant = Assert.IsAssignableFrom<FreexComp.Compiler.Symbols.ConstantSymbol>(result.Declarations.Scope.Lookup("N"));
        Assert.Null(constant.Value!.Value);
        Assert.Equal("nil", constant.Value.Type.Name);
        Assert.Equal("nil", constant.Value.ToDisplayString());
    }

    [Fact]
    [Trait("Req", "LANG-265")]
    public void AddressConstant_HoldsTheGlobal()
    {
        var (result, _) = Bind("var G: Integer; const C: ^Integer = @G; begin end.");
        Assert.Empty(result.Diagnostics);

        var constant = Assert.IsAssignableFrom<FreexComp.Compiler.Symbols.ConstantSymbol>(result.Declarations.Scope.Lookup("C"));
        var value = Assert.IsType<AddressConstant>(constant.Value!.Value);
        Assert.Same(result.Declarations.Scope.Lookup("G"), value.Target);
        Assert.Equal("@G", constant.Value.ToDisplayString());
    }
}
