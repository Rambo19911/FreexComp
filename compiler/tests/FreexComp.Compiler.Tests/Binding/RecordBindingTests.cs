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

    /// <summary>
    /// C-065: a managed field makes a record managed, a managed element a static array (also nested); plain records and arrays
    /// stay unmanaged. All bind without diagnostics, as dynamic arrays of them do.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-325")]
    public void ManagedFieldsAndElements_MakeTheTypeManaged()
    {
        var (result, _) = Bind("type TR = record s: string; i: Integer end; TP = record x: Integer end; TN = record r: TR end; " +
                               "TA = array[1..2] of string; TB = array[0..1, 0..1] of TR; TC = array[1..2] of TP; TD = array of TN; " +
                               "var a: TA; begin end.");

        Assert.Empty(result.Diagnostics);
        bool Managed(string name) => ((TypeSymbol)result.Declarations.Scope.Lookup(name)!).IsManaged;
        Assert.True(Managed("TR"));
        Assert.False(Managed("TP"));
        Assert.True(Managed("TN"));
        Assert.True(Managed("TA"));
        Assert.True(Managed("TB"));
        Assert.False(Managed("TC"));
        Assert.True(Managed("TD"));
    }

    /// <summary>Features of later tasks report E2900: record methods (M2.6); typed constants of variant records with fields without a byte image (C-198).</summary>
    [Theory]
    [InlineData("type T = record a: Pointer; case b: Byte of 0: (c: Byte) end; const K: T = (a: nil); begin end.", "E2900(1,76): Feature not supported: typed constant of a variant record with a field of type Pointer")]
    [InlineData("type T = record f: Text end; begin end.", "E2900(1,20): Feature not supported: record field of type Text")]
    public void LaterFeatures_AreE2900(string text, string expected) => Assert.Equal([expected], BindDiagnostics(text));

    /// <summary>C-108, LANG-261: the tag (when named) and every variant's fields are ordinary fields, all in one name space.</summary>
    [Fact]
    [Trait("Req", "LANG-261")]
    public void VariantFields_AreOrdinaryFields()
    {
        var (result, _) = Bind("""
            type T = record x: Integer; case Tag: Byte of 0, 1: (a: Integer); 2: (b: Double; c: Char) end;
            var r: T; d: Double;
            begin r.Tag := 2; r.b := 1.5; d := r.b; r.a := r.x end.
            """);
        Assert.Empty(Format(result.Diagnostics));
        var record = Assert.IsType<RecordTypeSymbol>(result.Declarations.Scope.Lookup("T"));
        Assert.Equal(["x:Integer@0", "Tag:Byte@4", "a:Integer@8", "b:Double@8", "c:Char@16"], record.Fields.Select(f => $"{f.Name}:{f.Type.Name}@{f.Offset}"));
        Assert.Equal((24L, 8, false), (record.Size, record.Alignment, record.IsManaged));
    }

    /// <summary>
    /// C-108, LANG-261 / ABI-061 errors: managed variant fields (E2160), labels of another type (E2010) or out of the tag's range
    /// (E2012), a non-ordinal tag type (E2013), a repeated field name (E2004). A managed fixed part is allowed.
    /// </summary>
    [Theory]
    [Trait("Req", "LANG-261")]
    [Trait("Req", "ABI-061")]
    [InlineData("type T = record case Byte of 0: (s: string) end; begin end.", "E2160(1,37): Managed type 'string' is not allowed in a variant part")]
    [InlineData("type T = record case Byte of 'x': (i: Integer) end; begin end.", "E2010(1,30): Incompatible types: 'Char' and 'Byte'")]
    [InlineData("type T = record case Byte of 300: (i: Integer) end; begin end.", "E2012(1,30): Constant value 300 is out of range for type 'Byte'")]
    [InlineData("type T = record case Double of 0: (i: Integer) end; begin end.", "E2013(1,22): Ordinal type required")]
    [InlineData("type T = record i: Byte; case Byte of 0: (i: Integer) end; begin end.", "E2004(1,43): Identifier redeclared: 'i'")]
    [InlineData("type T = record case Byte of 0: (a: Byte; case Byte of 1: (a: Word)) end; begin end.", "E2004(1,60): Identifier redeclared: 'a'")]
    public void VariantPart_Errors(string text, string expected) => Assert.Equal([expected], BindDiagnostics(text));

    /// <summary>
    /// C-198 (b): a typed constant of a variant record lists fields in declaration order and may skip fields (the other variants);
    /// a field overlapping one given before is E2037; a field that has no byte image (string, pointer) stays E2900.
    /// </summary>
    [Theory]
    [Trait("Req", "LANG-261")]
    [Trait("Req", "LANG-344")]
    [InlineData("const K: T = (x: 1; Tag: 2; b: 1.5; c: 'z');", null)]
    [InlineData("const K: T = (x: 1; a: 7);", null)]
    [InlineData("const K: T = (a: 7; b: 1.5);", "E2037(2,21): Unknown record field 'b' in constant")]
    [InlineData("const K: T = (b: 1.5; x: 1);", "E2037(2,23): Unknown record field 'x' in constant")]
    [InlineData("const K: TS = (s: 'a');", "E2900(2,15): Feature not supported: typed constant of a variant record with a field of type string")]
    public void VariantTypedConstant(string constant, string? expected)
    {
        var text = "type T = record x: Integer; case Tag: Byte of 0, 1: (a: Integer); 2: (b: Double; c: Char) end; " +
                   "TS = record s: string; case Byte of 0: (i: Integer) end;\n" + constant + "\nbegin end.";
        Assert.Equal(expected is null ? [] : [expected], BindDiagnostics(text));
    }

    /// <summary>C-198 (c): a label repeated in one variant part is E2004 at the second label (nested parts have their own labels).</summary>
    [Theory]
    [Trait("Req", "LANG-261")]
    [InlineData("type T = record case Byte of 0: (a: Byte); 1, 0: (b: Word) end; begin end.", "E2004(1,47): Identifier redeclared: '0'")]
    [InlineData("type T = record case Byte of 0: (a: Byte; case Byte of 0: (c: Word)); 1: (b: Word) end; begin end.", null)]
    public void DuplicateVariantLabel(string text, string? expected) =>
        Assert.Equal(expected is null ? [] : [expected], BindDiagnostics(text));

    [Fact]
    [Trait("Req", "LANG-261")]
    public void VariantPart_ManagedFixedPartIsAllowed() =>
        Assert.Empty(BindDiagnostics("type T = record s: string; case Byte of 0: (i: Integer) end; begin end."));
}
