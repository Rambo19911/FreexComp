using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>Records without variant parts (task C-053: LANG-260, 263, ABI-060…064).</summary>
[Trait("Phase", "2")]
public sealed class RecordBindingTests
{
    [Fact]
    [Trait("Req", "LANG-260")]
    [Trait("Req", "ABI-060")]
    public void RecordType_FieldsInDeclarationOrder()
    {
        var (result, _) = Bind("type TPoint = record X, Y: Integer; Tag: Char end; begin end.");
        Assert.Empty(result.Diagnostics);

        var record = Assert.IsType<RecordTypeSymbol>(result.Declarations.Scope.Lookup("TPoint"));
        Assert.Equal((TypeKind.Record, 12L, 4, false), (record.TypeKind, record.Size, record.Alignment, record.IsPacked));
        Assert.Equal(["X:Integer@0", "Y:Integer@4", "Tag:Char@8"], record.Fields.Select(f => $"{f.Name}:{f.Type.Name}@{f.Offset}"));
        Assert.All(record.Fields, f => Assert.Same(record, f.ContainingSymbol));
        Assert.Equal(record.Fields, record.Members);
        Assert.Same(record.Fields[1], record.GetMembers("y").Single());
        Assert.Equal(SymbolKind.Field, record.Fields[0].Kind);
    }

    [Fact]
    [Trait("Req", "LANG-260")]
    public void FieldAccess_IsAVariableOfTheFieldType()
    {
        var (result, _) = Bind("""
            type
              TP = record X, Y: Integer end;
              TR = record A, B: TP; V: array[1..2] of TP end;
            var r: TR; i: Integer;
            begin
              r.A.X := 1;
              r.V[i].Y := r.B.X;
              Inc(r.A.Y);
              r.B := r.A;
            end.
            """);
        Assert.Empty(result.Diagnostics);

        Assert.Equal(
            "(block (:= (. (. r:TR A):TP X):Integer 1:Integer) " +
            "(:= (. ([] (. r:TR V):array[1..2] of TP i:Integer):TP Y):Integer (. (. r:TR B):TP X):Integer) " +
            "(Inc (. (. r:TR A):TP Y):Integer) " +
            "(:= (. r:TR B):TP (. r:TR A):TP))", BoundDump.Of(result.Bodies[0]));
    }

    [Theory]
    [Trait("Req", "LANG-260")]
    [InlineData("type T = record a: Byte end; var x, y: T; begin x := y end.")]
    [InlineData("type T = record a: Byte end; var x: T; procedure P(var r: T); begin r.a := 1 end; begin P(x) end.")]
    [InlineData("type T = record a: Integer end; var x: T; procedure P(var i: Integer); begin end; begin P(x.a) end.")]
    [InlineData("type T = record a: Integer end; procedure P(const r: T); var i: Integer; begin i := r.a end; begin end.")]
    [InlineData("type T = record a: array[1..3] of Byte end; var x: T; begin x.a[2] := x.a[3]; x.a[1] := High(x.a) end.")]
    [InlineData("type T = record b: Boolean; s: set of 0..7 end; var x: T; begin x.b := 3 in x.s end.")]
    [InlineData("type T = record a: Integer end; const N = SizeOf(T); var x: T; begin x.a := SizeOf(x) + SizeOf(x.a) + N end.")]
    [InlineData("type TColor = (Red, Green); TR = record Red: Integer end; var x: TR; c: TColor; begin x.Red := 1; c := Red end.")]
    [InlineData("type T = record end; var x, y: T; begin x := y end.")]
    public void Compatible(string text) => Assert.Empty(BindDiagnostics(text));

    [Theory]
    [Trait("Req", "LANG-260")]
    [Trait("Req", "LANG-207")]
    [InlineData("type T = record a: Integer end; var x: T; begin x.b := 0 end.", "E2005(1,51): 'T' does not contain a member named 'b'")]
    [InlineData("var i: Integer; begin i.x := 0 end.", "E2006(1,23): 'i' is not a unit, record, class or interface; cannot qualify with '.'")]
    [InlineData("type A = record x: Byte end; B = record x: Byte end; var va: A; vb: B; begin va := vb end.",
        "E2010(1,84): Incompatible types: 'B' and 'A'")]
    [InlineData("var a: record x: Byte end; b: record x: Byte end; begin a := b end.",
        "E2010(1,62): Incompatible types: 'record x: Byte end' and 'record x: Byte end'")]
    [InlineData("type T = record a: Integer end; var x, y: T; b: Boolean; begin b := x = y end.",
        "E2011(1,71): Operator '=' not applicable to operand type(s) 'T' and 'T'")]
    [InlineData("type T = record a, b, a: Integer end; begin end.", "E2004(1,23): Identifier redeclared: 'a'")]
    [InlineData("type T = record a: Integer end; procedure P(const r: T); begin r.a := 1 end; begin end.",
        "E2053(1,64): Left side cannot be assigned to")]
    [InlineData("type T = record a: Integer end; var x: T; i: Integer; begin i := x end.", "E2010(1,66): Incompatible types: 'T' and 'Integer'")]
    public void Incompatible(string text, string expected) => Assert.Equal([expected], BindDiagnostics(text));

    /// <summary>Features of later tasks report E2900: managed fields (C-065), variant parts (C-108), record methods (M2.6).</summary>
    [Theory]
    [InlineData("type T = record s: string end; begin end.", "E2900(1,20): Feature not supported: record with managed fields")]
    [InlineData("type T = record a: Integer; case b: Byte of 0: (c: Byte) end; begin end.", "E2900(1,29): Feature not supported: variant part")]
    [InlineData("type T = record f: Text end; begin end.", "E2900(1,20): Feature not supported: record field of type Text")]
    public void LaterFeatures_AreE2900(string text, string expected) => Assert.Equal([expected], BindDiagnostics(text));
}
