using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>Assignment compatibility of ordinal (and real) types: types.md LANG-280 rules 1–4, 6; LANG-215, 231, 262; LANG-350, 351.</summary>
[Trait("Phase", "2")]
[Trait("Req", "LANG-280")]
public sealed class ConversionTests
{
    private static readonly BuiltInTypes Types = new();

    private static TypeSymbol Type(string name) => Assert.IsAssignableFrom<TypeSymbol>(Types.Scope.Lookup(name));

    [Theory]
    [InlineData("Integer", "Integer", ConversionKind.Identity)]
    [InlineData("LongInt", "Integer", ConversionKind.Identity)]
    [InlineData("Byte", "Int64", ConversionKind.ImplicitNumeric)]
    [InlineData("UInt64", "ShortInt", ConversionKind.ImplicitNumeric)]
    [InlineData("Integer", "Cardinal", ConversionKind.ImplicitNumeric)]
    [InlineData("NativeInt", "Int64", ConversionKind.ImplicitNumeric)]
    [InlineData("Int64", "NativeUInt", ConversionKind.ImplicitNumeric)]
    [InlineData("Char", "WideChar", ConversionKind.Identity)]
    [InlineData("Char", "AnsiChar", ConversionKind.None)]
    [InlineData("AnsiChar", "Char", ConversionKind.ImplicitNumeric)]
    [InlineData("Boolean", "ByteBool", ConversionKind.ImplicitNumeric)]
    [InlineData("LongBool", "Boolean", ConversionKind.ImplicitNumeric)]
    [InlineData("WordBool", "LongBool", ConversionKind.ImplicitNumeric)]
    [InlineData("Integer", "Boolean", ConversionKind.None)]
    [InlineData("Boolean", "Integer", ConversionKind.None)]
    [InlineData("Integer", "Char", ConversionKind.None)]
    [InlineData("Char", "Integer", ConversionKind.None)]
    [InlineData("Char", "Boolean", ConversionKind.None)]
    [InlineData("Integer", "Double", ConversionKind.ImplicitNumeric)]
    [InlineData("Int64", "Single", ConversionKind.ImplicitNumeric)]
    [InlineData("Single", "Double", ConversionKind.ImplicitNumeric)]
    [InlineData("Double", "Single", ConversionKind.ImplicitNumeric)]
    [InlineData("Double", "Integer", ConversionKind.None)]
    [InlineData("Integer", "string", ConversionKind.None)]
    public void Ordinals_Classification(string from, string to, ConversionKind expected)
    {
        var conversion = new Conversions().Classify(Type(from), Type(to));

        Assert.Equal((expected, expected != ConversionKind.None, expected != ConversionKind.None), (conversion.Kind, conversion.IsImplicit, conversion.Exists));
    }

    [Fact]
    [Trait("Req", "CMP-135")]
    public void Ordinals_ErrorType_IsCompatibleWithEverything()
    {
        var conversions = new Conversions();

        Assert.True(conversions.Classify(Types.Error, Types.Char).Exists);
        Assert.True(conversions.Classify(Types.Boolean, Types.Error).Exists);
    }

    // ------------------------------------------------------------------ assignment statements (LANG-350, 351)

    private const string Variables =
        "var b: Byte; i: Integer; c: Cardinal; i64: Int64; flag: Boolean; bb: ByteBool; ch: Char; ac: AnsiChar; d: Double; s: Single;";

    /// <summary>The bound value of the first assignment of <c>begin {statement} end.</c>.</summary>
    private static string AssignedValue(string statement)
    {
        var (result, _) = Bind($"{Variables} begin {statement} end.");
        Assert.Equal([], Format(result.Diagnostics));
        var assignment = Assert.IsType<BoundAssignmentStatement>(Assert.Single(Assert.Single(result.Bodies).Statements));
        Assert.Same(assignment.Target.Type, assignment.Value.Type);
        return BoundDump.Of(assignment.Value);
    }

    [Theory]
    [Trait("Req", "LANG-351")]
    [InlineData("b := i", "(conv Byte i:Integer)")]
    [InlineData("i := b", "(conv Integer b:Byte)")]
    [InlineData("c := i", "(conv Cardinal i:Integer)")]
    [InlineData("i := i + 1", "(Add:Integer i:Integer 1:Integer)")]
    [InlineData("b := 200", "200:Byte")]
    [InlineData("i64 := c * c", "(conv Int64 (Multiply:Cardinal c:Cardinal c:Cardinal))")]
    [InlineData("flag := bb", "(conv Boolean bb:ByteBool)")]
    [InlineData("bb := True", "True:ByteBool")]
    [InlineData("flag := i > 0", "(Greater:Boolean i:Integer 0:Integer)")]
    [InlineData("ch := ac", "(conv Char ac:AnsiChar)")]
    [InlineData("ac := 'a'", "'a':AnsiChar")]
    [InlineData("ch := #$263A", "'☺':Char")]
    [InlineData("d := i", "(conv Double i:Integer)")]
    [InlineData("s := 1", "1.0:Single")]
    [InlineData("d := s", "(conv Double s:Single)")]
    public void Ordinals_Assignment_ConvertsTheValueToTheTargetType(string statement, string expected) =>
        Assert.Equal(expected, AssignedValue(statement));

    [Fact]
    public void Ordinals_Assignment_BindsTargetAndValue()
    {
        var (result, _) = Bind($"{Variables} begin i := 1; ; b := 2 end.");

        var statements = Assert.Single(result.Bodies).Statements;
        Assert.Equal(2, statements.Length);
        var first = Assert.IsType<BoundAssignmentStatement>(statements[0]);
        Assert.Equal("i", Assert.IsType<BoundVariableExpression>(first.Target).Variable.Name);
    }

    [Theory]
    [InlineData("b := 256", "E2012(1,{0}): Constant value 256 is out of range for type 'Byte'")]
    [InlineData("b := -1", "E2012(1,{0}): Constant value -1 is out of range for type 'Byte'")]
    [InlineData("c := -1", "E2012(1,{0}): Constant value -1 is out of range for type 'Cardinal'")]
    [InlineData("i := 2147483648", "E2012(1,{0}): Constant value 2147483648 is out of range for type 'Integer'")]
    [InlineData("i := flag", "E2010(1,{0}): Incompatible types: 'Boolean' and 'Integer'")]
    [InlineData("flag := 1", "E2010(1,{0}): Incompatible types: 'Integer' and 'Boolean'")]
    [InlineData("i := ch", "E2010(1,{0}): Incompatible types: 'Char' and 'Integer'")]
    [InlineData("ch := 65", "E2010(1,{0}): Incompatible types: 'Integer' and 'Char'")]
    [InlineData("ac := ch", "E2010(1,{0}): Incompatible types: 'Char' and 'AnsiChar'")]
    [InlineData("ac := #$100", "E2010(1,{0}): Incompatible types: 'Char' and 'AnsiChar'")]
    [InlineData("i := d", "E2010(1,{0}): Incompatible types: 'Double' and 'Integer'")]
    [InlineData("i := 1.5", "E2010(1,{0}): Incompatible types: 'Double' and 'Integer'")]
    [InlineData("ac := #200", "W1021(1,{0}): Non-ASCII character in ANSI literal")]
    [InlineData("s := 1e39", "E2012(1,{0}): Constant value 1E+39 is out of range for type 'Single'")]       // C-058: no silent infinity
    [InlineData("s := -3.5e38", "E2012(1,{0}): Constant value -3.5E+38 is out of range for type 'Single'")]
    public void Ordinals_Assignment_Errors(string statement, string expected)
    {
        var prefix = $"{Variables} begin ";
        var column = prefix.Length + statement.IndexOf(":=", StringComparison.Ordinal) + 4;   // first character of the value
        Assert.Equal([string.Format(System.Globalization.CultureInfo.InvariantCulture, expected, column)], BindDiagnostics($"{prefix}{statement} end."));
    }

    [Theory]
    [Trait("Req", "LANG-350")]
    [InlineData("const K = 1; begin K := 2 end.", "E2053(1,20): Left side cannot be assigned to")]
    [InlineData("begin True := False end.", "E2053(1,7): Left side cannot be assigned to")]
    [InlineData("var i: Integer; begin Integer := 1 end.", "E2009(1,23): Variable or constant expected but type 'Integer' found")]
    [InlineData("var i: Integer; begin Missing := i end.", "E2003(1,23): Undeclared identifier 'Missing'")]
    [InlineData("var i: Integer; begin i := Missing end.", "E2003(1,28): Undeclared identifier 'Missing'")]
    [InlineData("var x: Missing; i: Integer; begin x := i; i := x end.", "E2003(1,8): Undeclared identifier 'Missing'")]
    [InlineData("var i: Integer; begin Byte(i) := 1 end.", "E2041(1,23): Variable typecast not allowed for managed types or types of different size")]
    [InlineData("var i: Integer; begin Integer(5) := 1 end.", "E2053(1,23): Left side cannot be assigned to")]
    public void Ordinals_Assignment_Targets(string text, string expected) => Assert.Equal([expected], BindDiagnostics(text));

    // ------------------------------------------------------------------ the LANG-280 matrix beyond ordinals (rules 5, 8, 9)

    private const string MatrixDeclarations = """
        type
          PInteger = ^Integer;
          PInteger2 = ^Integer;
          PByte = ^Byte;
          TColor = (Red, Green, Blue);
          TWarm = Red..Green;
          TFruit = (Apple, Pear);
          TSmall = set of 0..7;
          TColors = set of TColor;
          TWide = set of Byte;
          TMy = type Integer;
          TMyChar = type Char;
          TMyColor = type TColor;
          TAlias = Integer;
        """;

    private static TypeSymbol Declared(BindingResult result, string name) => Assert.IsAssignableFrom<TypeSymbol>(result.Scope.Lookup(name));

    [Theory]
    [Trait("Req", "LANG-205")]
    [InlineData("TWarm", "TColor", ConversionKind.ImplicitNumeric)]       // rule 5
    [InlineData("TColor", "TFruit", ConversionKind.None)]
    [InlineData("TSmall", "TWide", ConversionKind.ImplicitNumeric)]       // rule 8
    [InlineData("TColors", "TSmall", ConversionKind.None)]
    [InlineData("PInteger", "PInteger2", ConversionKind.ImplicitReference)]  // rule 9 (LANG-206/267)
    [InlineData("PInteger", "Pointer", ConversionKind.ImplicitReference)]
    [InlineData("Pointer", "PByte", ConversionKind.ImplicitReference)]
    [InlineData("PInteger", "PByte", ConversionKind.None)]
    [InlineData("PChar", "PByte", ConversionKind.None)]
    [InlineData("TMy", "Integer", ConversionKind.ImplicitNumeric)]        // LANG-205: distinct, assignment-compatible both ways
    [InlineData("Integer", "TMy", ConversionKind.ImplicitNumeric)]
    [InlineData("TMy", "Double", ConversionKind.ImplicitNumeric)]
    [InlineData("TMyChar", "Char", ConversionKind.ImplicitNumeric)]
    [InlineData("Char", "TMyChar", ConversionKind.ImplicitNumeric)]
    [InlineData("TMyColor", "TColor", ConversionKind.ImplicitNumeric)]
    [InlineData("TMyChar", "AnsiChar", ConversionKind.None)]
    [InlineData("TAlias", "Integer", ConversionKind.Identity)]
    public void Matrix_Classification(string from, string to, ConversionKind expected)
    {
        var (result, _) = Bind($"{MatrixDeclarations} begin end.");
        Assert.Empty(result.Diagnostics);

        var conversion = new Conversions().Classify(Declared(result, from), Declared(result, to));

        Assert.Equal((expected, expected != ConversionKind.None), (conversion.Kind, conversion.Exists));
    }

    /// <summary>LANG-205: a distinct type has the representation of its source but its own identity (name, <c>var</c> matching).</summary>
    [Fact]
    [Trait("Req", "LANG-205")]
    public void DistinctType_IsANewTypeWithTheSameRepresentation()
    {
        var (result, _) = Bind($"{MatrixDeclarations} begin end.");

        var distinct = Declared(result, "TMy");
        Assert.NotSame(Types.Integer, distinct);
        Assert.Equal(("TMy", 4L, 4), (distinct.Name, distinct.Size, distinct.Alignment));
        Assert.Same(Declared(result, "Integer"), Declared(result, "TAlias"));
        Assert.Equal("Integer", Declared(result, "TAlias").Name);
        Assert.Equal(2L, Declared(result, "TMyChar").Size);
        Assert.Equal(Declared(result, "TColor").Size, Declared(result, "TMyColor").Size);
    }

    [Theory]
    [Trait("Req", "LANG-205")]
    [Trait("Req", "LANG-281")]
    [InlineData("TAlias", "")]
    [InlineData("TMy", "E2010(3,62): Incompatible types: 'TMy' and 'Integer'")]
    public void DistinctType_DoesNotMatchAVarParameter(string type, string expected)
    {
        var text = $"type TAlias = Integer; TMy = type Integer;\nprocedure P(var X: Integer); begin end;\nvar v: {type.PadRight(6)}; i: Integer; begin i := v; v := i + 1; P(i); P(v) end.";

        Assert.Equal(expected.Length == 0 ? [] : [expected], BindDiagnostics(text));
    }

    [Theory]
    [Trait("Req", "LANG-205")]
    [InlineData("type R = record end; T = type R;", "E2900(1,31): Feature not supported: distinct type of a record")]
    [InlineData("type T = type 1..5; var x: T; begin x := 3; end.", "")]
    public void DistinctType_OfStructuredTypes(string text, string expected)
    {
        var source = text.EndsWith('.') ? text : text + " begin end.";
        Assert.Equal(expected.Length == 0 ? [] : [expected], BindDiagnostics(source));
    }

    /// <summary>
    /// C-192: LANG-280 has no implicit class reference → <c>Pointer</c> conversion (types.md lead note); the explicit
    /// <c>Pointer(C)</c> typecast (LANG-290) and nil stay allowed.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-280")]
    [Trait("Req", "LANG-290")]
    public void ClassRefToPointer_IsE2010()
    {
        const string Types = "type TA = class end; TAClass = class of TA;\nprocedure Take(P: Pointer); begin end;\n";

        Assert.Equal(["E2010(3,49): Incompatible types: 'TAClass' and 'Pointer'"],
            BindDiagnostics(Types + "var C: TAClass; P: Pointer; begin C := TA; P := C; end."));
        Assert.Equal(["E2010(3,40): Incompatible types: 'TAClass' and 'Pointer'"],
            BindDiagnostics(Types + "var C: TAClass; P: Pointer; begin Take(C); end."));
        Assert.Empty(BindDiagnostics(Types + "var C: TAClass; P: Pointer; begin C := nil; P := Pointer(C); Take(Pointer(TA)); end."));
    }

    // ------------------------------------------------------------------ typecasts (LANG-290)

    private const string CastTypes = """
        type
          PInteger = ^Integer;
          TColor = (Red, Green, Blue);
          TSet = set of 0..7;
          TRec4 = record A: Integer; end;
          TRec8 = record A, B: Integer; end;
          TArr4 = array[0..3] of Byte;
        """;

    private static readonly string[] CastTargets =
        ["Integer", "Byte", "Cardinal", "Char", "AnsiChar", "Boolean", "TColor", "Pointer", "PChar", "PInteger", "NativeInt", "UInt64", "Int64",
         "Double", "Single", "TRec4", "TRec8", "TSet", "TArr4", "string"];

    private static string CastDeclarations =>
        CastTypes + "\nvar\n  i: Integer; b: Byte; c: Cardinal; i64: Int64; n: NativeInt; u: NativeUInt; ch: Char; ac: AnsiChar; f: Boolean;\n" +
        "  d: Double; s: Single; p: Pointer; pc: PChar; pi: PInteger; e: TColor; st: TSet; r4: TRec4; r8: TRec8; a4: TArr4; str: string;\n" +
        string.Concat(CastTargets.Select(t => $"  x{t}: {t};\n"));

    /// <summary>The value of <c>x&lt;T&gt; := T(…)</c> as a bound dump, or its diagnostics (<c>CODE: message</c>).</summary>
    private static string Cast(string cast)
    {
        var type = cast[..cast.IndexOf('(', StringComparison.Ordinal)];
        var (result, _) = Bind($"{CastDeclarations} begin x{type} := {cast} end.");
        if (!result.Diagnostics.IsEmpty)
        {
            return string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}"));
        }

        return BoundDump.Of(Assert.IsType<BoundAssignmentStatement>(Assert.Single(Assert.Single(result.Bodies).Statements)).Value);
    }

    /// <summary>LANG-290, one row per table line: value casts <c>(cast …)</c>, variable typecasts <c>(vcast …)</c>, E2040, E2041.</summary>
    [Theory]
    [Trait("Req", "LANG-290")]
    // ordinal -> ordinal: value-preserving or truncating/extending (row 1)
    [InlineData("Integer(c)", "(cast Integer c:Cardinal)")]
    [InlineData("Byte(i)", "(cast Byte i:Integer)")]
    [InlineData("Char(i)", "(cast Char i:Integer)")]
    [InlineData("Integer(ch)", "(cast Integer ch:Char)")]
    [InlineData("Boolean(b)", "(cast Boolean b:Byte)")]
    [InlineData("TColor(i)", "(cast TColor i:Integer)")]
    [InlineData("AnsiChar(ch)", "(cast AnsiChar ch:Char)")]
    [InlineData("Byte(300)", "44:Byte")]
    // ordinal -> pointer (row 2)
    [InlineData("Pointer(i)", "(cast Pointer i:Integer)")]
    [InlineData("PChar(i64)", "(cast PChar i64:Int64)")]
    [InlineData("Pointer(e)", "(cast Pointer e:TColor)")]
    // pointer -> pointer-sized ordinal (row 3)
    [InlineData("NativeInt(p)", "(cast NativeInt p:Pointer)")]
    [InlineData("UInt64(pc)", "(cast UInt64 pc:PChar)")]
    [InlineData("Int64(pi)", "(cast Int64 pi:PInteger)")]
    [InlineData("Integer(p)", "E2041: Variable typecast not allowed for managed types or types of different size")]
    [InlineData("Integer(nil)", "E2040: Invalid typecast from 'nil' to 'Integer'")]
    // pointer -> pointer (row 4)
    [InlineData("PInteger(p)", "(cast PInteger p:Pointer)")]
    [InlineData("Pointer(pc)", "(cast Pointer pc:PChar)")]
    [InlineData("PChar(pi)", "(cast PChar pi:PInteger)")]
    [InlineData("PChar(nil)", "nil:PChar")]
    // reals (row 6): integer -> real converts; real -> ordinal is E2040
    [InlineData("Double(i)", "(cast Double i:Integer)")]
    [InlineData("Single(i64)", "(cast Single i64:Int64)")]
    [InlineData("Double(s)", "(cast Double s:Single)")]
    [InlineData("Integer(d)", "E2040: Invalid typecast from 'Double' to 'Integer'")]
    [InlineData("Int64(d)", "E2040: Invalid typecast from 'Double' to 'Int64'")]
    [InlineData("TRec8(d)", "E2040: Invalid typecast from 'Double' to 'TRec8'")]
    // strings (rows 7, 8, C-063): pointer to the data
    [InlineData("PChar(str)", "(cast PChar str:string)")]
    // variable typecasts (row 9): same size, not managed
    [InlineData("TRec4(i)", "(vcast TRec4 i:Integer)")]
    [InlineData("Integer(r4)", "(vcast Integer r4:TRec4)")]
    [InlineData("TRec8(i64)", "(vcast TRec8 i64:Int64)")]
    [InlineData("Int64(r8)", "(vcast Int64 r8:TRec8)")]
    [InlineData("TSet(b)", "(vcast TSet b:Byte)")]
    [InlineData("Byte(st)", "(vcast Byte st:TSet)")]
    [InlineData("TArr4(c)", "(vcast TArr4 c:Cardinal)")]
    [InlineData("Cardinal(a4)", "(vcast Cardinal a4:TArr4)")]
    [InlineData("Pointer(r8)", "(vcast Pointer r8:TRec8)")]
    [InlineData("TRec8(i)", "E2041: Variable typecast not allowed for managed types or types of different size")]
    [InlineData("TSet(i)", "E2041: Variable typecast not allowed for managed types or types of different size")]
    [InlineData("TRec8(str)", "E2041: Variable typecast not allowed for managed types or types of different size")]
    [InlineData("TRec4(5)", "E2040: Invalid typecast from 'Integer' to 'TRec4'")]
    [InlineData("TRec4(i + 1)", "E2040: Invalid typecast from 'Integer' to 'TRec4'")]
    [InlineData("TSet(Red)", "E2040: Invalid typecast from 'TColor' to 'TSet'")]
    public void Typecasts_Matrix(string cast, string expected) => Assert.Equal(expected, Cast(cast));

    /// <summary>LANG-290 row 9: as an assignment target or a <c>var</c> argument a same-size cast of a variable is a variable typecast.</summary>
    [Theory]
    [Trait("Req", "LANG-290")]
    [InlineData("Integer(c) := -1", "(:= (vcast Integer c:Cardinal) -1:Integer)")]
    [InlineData("Byte(ac) := 65", "(:= (vcast Byte ac:AnsiChar) 65:Byte)")]
    [InlineData("TRec4(i).A := 3", "(:= (. (vcast TRec4 i:Integer) A):Integer 3:Integer)")]
    [InlineData("TRec4(i) := r4", "(:= (vcast TRec4 i:Integer) r4:TRec4)")]
    [InlineData("Inc(Integer(c), 2)", "(Inc (vcast Integer c:Cardinal) 2:Integer)")]
    [InlineData("Bump(Integer(c))", "(call Bump (vcast Integer c:Cardinal))")]
    public void Typecasts_VariableContext(string statement, string expected)
    {
        var (result, _) = Bind($"{CastDeclarations} procedure Bump(var X: Integer); begin end; begin {statement} end.");

        Assert.Empty(Format(result.Diagnostics));
        Assert.Equal(expected, BoundDump.Of(Assert.Single(Assert.Single(result.Bodies).Statements)));
    }

    /// <summary>
    /// LANG-290 rows 3 and 4 (C-178): a dynamic array or a long string cast to a pointer type or a pointer-sized ordinal
    /// reinterprets the reference (no reference counting); it is a value cast, not a variable typecast (no E2041).
    /// </summary>
    [Theory]
    [Trait("Req", "LANG-290")]
    [InlineData("Pointer(da)", "(cast Pointer da:TDyn)")]
    [InlineData("NativeInt(da)", "(cast NativeInt da:TDyn)")]
    [InlineData("NativeUInt(da)", "(cast NativeUInt da:TDyn)")]
    [InlineData("Int64(da)", "(cast Int64 da:TDyn)")]
    [InlineData("UInt64(da)", "(cast UInt64 da:TDyn)")]
    [InlineData("PInteger(da)", "(cast PInteger da:TDyn)")]
    [InlineData("PChar(da)", "(cast PChar da:TDyn)")]
    [InlineData("Pointer(str)", "(cast Pointer str:string)")]
    [InlineData("NativeInt(str)", "(cast NativeInt str:string)")]
    [InlineData("NativeUInt(str)", "(cast NativeUInt str:string)")]
    [InlineData("Pointer(ans)", "(cast Pointer ans:AnsiString)")]
    [InlineData("NativeInt(ans)", "(cast NativeInt ans:AnsiString)")]
    [InlineData("Integer(da)", "E2041: Variable typecast not allowed for managed types or types of different size")]
    [InlineData("Integer(str)", "E2041: Variable typecast not allowed for managed types or types of different size")]
    public void ManagedValueCastToPointer(string cast, string expected)
    {
        var target = cast[..cast.IndexOf('(', StringComparison.Ordinal)];
        var (result, _) = Bind($$"""
            type PInteger = ^Integer; TDyn = array of Integer;
            var da: TDyn; str: string; ans: AnsiString; x: {{target}};
            begin x := {{cast}} end.
            """);
        if (!result.Diagnostics.IsEmpty)
        {
            Assert.Equal(expected, string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
            return;
        }

        Assert.Equal(expected, BoundDump.Of(Assert.IsType<BoundAssignmentStatement>(Assert.Single(Assert.Single(result.Bodies).Statements)).Value));
    }

    // ------------------------------------------------------------------ global initialisers (LANG-522)

    [Fact]
    [Trait("Req", "LANG-522")]
    public void Ordinals_Initializer_IsConvertedToTheVariableType()
    {
        var (result, _) = Bind("var X: Byte = 200; D: Double = 2; F: ByteBool = True; begin end.");

        Assert.Empty(result.Diagnostics);
        var x = Assert.IsType<VariableSymbol>(result.Scope.Lookup("X")).Initializer!;
        Assert.Equal(("Byte", "200"), (x.Type.Name, x.ToDisplayString()));
        var d = Assert.IsType<VariableSymbol>(result.Scope.Lookup("D")).Initializer!;
        Assert.Equal(("Double", "2.0"), (d.Type.Name, d.ToDisplayString()));
        Assert.Equal("ByteBool", Assert.IsType<VariableSymbol>(result.Scope.Lookup("F")).Initializer!.Type.Name);
    }

    [Theory]
    [Trait("Req", "LANG-522")]
    [InlineData("var X: Byte = 300; begin end.", "E2012(1,15): Constant value 300 is out of range for type 'Byte'")]
    [InlineData("var X: Boolean = 0; begin end.", "E2010(1,18): Incompatible types: 'Integer' and 'Boolean'")]
    [InlineData("var X: Integer = 'a'; begin end.", "E2010(1,18): Incompatible types: 'Char' and 'Integer'")]
    public void Ordinals_Initializer_Errors(string text, string expected) => Assert.Equal([expected], BindDiagnostics(text));
}
