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
    [InlineData("var i: Integer; begin Byte(i) := 1 end.", "E2900(1,23): Feature not supported: variable typecast")]
    public void Ordinals_Assignment_Targets(string text, string expected) => Assert.Equal([expected], BindDiagnostics(text));

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
