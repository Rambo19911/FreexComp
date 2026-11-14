using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>Typing of integer, Boolean and char expressions (types.md LANG-220…227, expressions-statements.md LANG-300…306).</summary>
[Trait("Phase", "2")]
[Trait("Req", "LANG-221")]
public sealed class ExpressionTypingTests
{
    private const string Variables =
        "var sh: ShortInt; b: Byte; si: SmallInt; w: Word; i: Integer; c: Cardinal; i64: Int64; u: UInt64; " +
        "ni: NativeInt; nu: NativeUInt; flag: Boolean; bb: ByteBool; ch: Char; ac: AnsiChar; d: Double; s: Single; " +
        "str: string; const K = 10;";

    /// <summary>
    /// Binds <paramref name="expression"/> as the right side of <c>X := …</c> on line 2 (column 6 = first character of
    /// the expression) of a program declaring <see cref="Variables"/>; returns the bound dump and the diagnostics.
    /// </summary>
    private static (string Dump, string[] Diagnostics) BindExpression(string expression, string prefix = "")
    {
        var tree = SyntaxTree.Parse(SourceText.From(TestPath, $"{prefix}program P; {Variables} begin\nX := {expression};\nend."));
        Assert.Empty(tree.Diagnostics);
        var types = new BuiltInTypes();
        var declarations = DeclarationBinder.BindCompilationUnit(tree, types);
        Assert.Empty(declarations.Diagnostics);
        var assignment = tree.Root.DescendantNodes().OfType<AssignmentStatementSyntax>().First();
        var diagnostics = new DiagnosticBag();
        var bound = new ExpressionBinder(types, declarations.Scope, diagnostics).BindExpression(assignment.Value);
        return (BoundDump.Of(bound), Format(diagnostics.ToImmutable()));
    }

    /// <summary>
    /// C-208 (LANG-221): a constant declared as a typecast to a 64-bit or unsigned type keeps that type, so <c>Integer op C</c>
    /// is computed in the wider type (<c>N * Int64(86400000)</c> is an Int64 product, not a 32-bit one): the LANG-222 adaptation of a constant to the other operand's computation type never narrows a 64-bit constant to 32 bits.
    /// </summary>
    [Theory]
    [InlineData("i * C64", "Int64")]
    [InlineData("i + C64", "Int64")]
    [InlineData("i - C64", "Int64")]
    [InlineData("i div C64", "Int64")]
    [InlineData("C64 * i", "Int64")]
    [InlineData("c * CU64", "UInt64")]
    [InlineData("w + CU32", "Integer")]       // a 32-bit constant that fits still adapts (LANG-222)
    public void TypecastConstant_KeepsItsType(string expression, string type)
    {
        var dump = TypedWithConstants(expression);
        Assert.StartsWith("(", dump);
        Assert.Contains(":" + type, dump[..dump.IndexOf(' ', StringComparison.Ordinal)]);
    }

    [Fact]
    public void TypecastConstant_SizeOfIsTheTypesSize() =>
        Assert.Equal("8:Integer", TypedWithConstants("SizeOf(C64)"));

    private static string TypedWithConstants(string expression)
    {
        var tree = SyntaxTree.Parse(SourceText.From(TestPath,
            $"program P; const C64 = Int64(86400000); CU64 = UInt64(3); CU32 = Cardinal(7); {Variables} begin\nX := {expression};\nend."));
        Assert.Empty(tree.Diagnostics);
        var types = new BuiltInTypes();
        var declarations = DeclarationBinder.BindCompilationUnit(tree, types);
        Assert.Empty(declarations.Diagnostics);
        var assignment = tree.Root.DescendantNodes().OfType<AssignmentStatementSyntax>().First();
        var diagnostics = new DiagnosticBag();
        var bound = new ExpressionBinder(types, declarations.Scope, diagnostics).BindExpression(assignment.Value);
        Assert.Empty(Format(diagnostics.ToImmutable()));
        return BoundDump.Of(bound);
    }

    private static string Typed(string expression, string prefix = "")
    {
        var (dump, diagnostics) = BindExpression(expression, prefix);
        Assert.Empty(diagnostics);
        return dump;
    }

    private static string TypeOf(string expression)
    {
        var dump = Typed(expression);
        return dump[(dump.IndexOf(':', StringComparison.Ordinal) + 1)..dump.IndexOf(' ', StringComparison.Ordinal)];
    }

    // ------------------------------------------------------------------ LANG-220…222: computation types and the result table

    [Theory]
    [InlineData("b + b", "(Add:Integer (conv Integer b:Byte) (conv Integer b:Byte))")]
    [InlineData("i + i", "(Add:Integer i:Integer i:Integer)")]
    [InlineData("i + c", "(Add:Int64 (conv Int64 i:Integer) (conv Int64 c:Cardinal))")]
    [InlineData("w * c", "(Multiply:Int64 (conv Int64 w:Word) (conv Int64 c:Cardinal))")]     // Word computes as Integer
    public void Operands_AreWidenedToTheComputationType(string expression, string expected) => Assert.Equal(expected, Typed(expression));

    /// <summary>LANG-221 table: one variable of each computation type on each side.</summary>
    [Theory]
    [InlineData("i", "i", "Integer", false)]
    [InlineData("i", "c", "Int64", false)]
    [InlineData("i", "i64", "Int64", false)]
    [InlineData("i", "u", "UInt64", true)]
    [InlineData("c", "i", "Int64", false)]
    [InlineData("c", "c", "Cardinal", false)]
    [InlineData("c", "i64", "Int64", false)]
    [InlineData("c", "u", "UInt64", false)]
    [InlineData("i64", "i", "Int64", false)]
    [InlineData("i64", "c", "Int64", false)]
    [InlineData("i64", "i64", "Int64", false)]
    [InlineData("i64", "u", "UInt64", true)]
    [InlineData("u", "i", "UInt64", true)]
    [InlineData("u", "c", "UInt64", false)]
    [InlineData("u", "i64", "UInt64", true)]
    [InlineData("u", "u", "UInt64", false)]
    public void ResultTable(string left, string right, string expected, bool warns)
    {
        foreach (var op in new[] { "+", "-", "*", "div", "mod", "and", "or", "xor" })
        {
            var (dump, diagnostics) = BindExpression($"{left} {op} {right}");
            Assert.Equal((op, expected), (op, dump[(dump.IndexOf(':', StringComparison.Ordinal) + 1)..dump.IndexOf(' ', StringComparison.Ordinal)]));
            var column = 6 + left.Length + 1;
            Assert.Equal(
                warns ? [$"W1010(2,{column}): Mixing signed and unsigned 64-bit operands"] : [],
                diagnostics);
        }
    }

    [Theory]
    [Trait("Req", "LANG-222")]
    [InlineData("sh + si", "Integer")]
    [InlineData("w + w", "Integer")]
    [InlineData("ni + ni", "Int64")]
    [InlineData("nu + nu", "UInt64")]
    [InlineData("ni + i", "Int64")]
    [InlineData("nu + c", "UInt64")]
    public void SmallAndNativeTypes_MapToComputationTypes(string expression, string expected) => Assert.Equal(expected, TypeOf(expression));

    [Theory]
    [Trait("Req", "LANG-222")]
    [InlineData("c + 1", "(Add:Cardinal c:Cardinal 1:Cardinal)")]
    [InlineData("1 + c", "(Add:Cardinal 1:Cardinal c:Cardinal)")]
    [InlineData("u + 1", "(Add:UInt64 u:UInt64 1:UInt64)")]
    [InlineData("b + 1", "(Add:Integer (conv Integer b:Byte) 1:Integer)")]
    [InlineData("c + K", "(Add:Cardinal c:Cardinal 10:Cardinal)")]
    [InlineData("i + 3000000000", "(Add:Int64 (conv Int64 i:Integer) 3000000000:Int64)")]
    [InlineData("c + (-1)", "(Add:Int64 (conv Int64 c:Cardinal) -1:Int64)")]
    [InlineData("i64 + 1", "(Add:Int64 i64:Int64 1:Int64)")]
    public void IntegerConstant_TakesTheOtherOperandsComputationType_WhenItFits(string expression, string expected) =>
        Assert.Equal(expected, Typed(expression));

    [Theory]
    [InlineData("u + (-1)", "W1010(2,8): Mixing signed and unsigned 64-bit operands")]
    [InlineData("u * i", "W1010(2,8): Mixing signed and unsigned 64-bit operands")]
    public void NegativeOrNonConstantSignedOperand_WithUInt64_Warns(string expression, string expected) =>
        Assert.Equal([expected], BindExpression(expression).Diagnostics);

    [Fact]
    [Trait("Req", "LANG-341")]
    public void ConstantOperands_AreFolded()
    {
        Assert.Equal("3:Integer", Typed("1 + 2"));
        Assert.Equal("2147483648:Int64", Typed("MaxInt + 1"));
        Assert.Equal("20:Integer", Typed("K * 2"));
        Assert.Equal("True:Boolean", Typed("K > 2"));
    }

    [Fact]
    [Trait("Req", "LANG-300")]
    public void LeadingSign_AppliesToTheWholeFirstTerm()
    {
        Assert.Equal("-1:Integer", Typed("-1 and $FF"));
        Assert.Equal("(And:Integer (Negate:Integer i:Integer) 255:Integer)", Typed("(-i) and $FF"));
        Assert.Equal("(Negate:Integer (And:Integer i:Integer 255:Integer))", Typed("-i and $FF"));
    }

    // ------------------------------------------------------------------ LANG-224…226: shifts, division

    [Theory]
    [Trait("Req", "LANG-224")]
    [InlineData("b shl 1", "(Shl:Integer (conv Integer b:Byte) 1:Integer)")]
    [InlineData("c shr 1", "(Shr:Cardinal c:Cardinal 1:Cardinal)")]
    [InlineData("i64 shl i", "(Shl:Int64 i64:Int64 (conv Int64 i:Integer))")]
    [InlineData("i shl i64", "(Shl:Integer i:Integer (conv Integer i64:Int64))")]
    [InlineData("1 shl i", "(Shl:Integer 1:Integer i:Integer)")]
    [InlineData("Int64(1) shl 33", "8589934592:Int64")]
    [InlineData("1 shl 33", "2:Integer")]
    public void Shift_HasTheLeftOperandsComputationType(string expression, string expected) => Assert.Equal(expected, Typed(expression));

    [Theory]
    [Trait("Req", "LANG-225")]
    [InlineData("i div 2", "(IntegerDivide:Integer i:Integer 2:Integer)")]
    [InlineData("c mod i", "(Modulo:Int64 (conv Int64 c:Cardinal) (conv Int64 i:Integer))")]
    [InlineData("i div 0", "(IntegerDivide:Integer i:Integer 0:Integer)")]
    public void IntegerDivision(string expression, string expected) => Assert.Equal(expected, Typed(expression));

    [Theory]
    [Trait("Req", "LANG-226")]
    [InlineData("i / i", "(Divide:Double (conv Double i:Integer) (conv Double i:Integer))")]
    [InlineData("s / s", "(Divide:Single s:Single s:Single)")]
    [InlineData("d / 2", "(Divide:Double d:Double 2.0:Double)")]
    [InlineData("7 / 2", "3.5:Double")]
    public void Slash_IsRealDivision(string expression, string expected) => Assert.Equal(expected, Typed(expression));

    [Theory]
    [Trait("Req", "LANG-230")]
    [InlineData("d + i", "(Add:Double d:Double (conv Double i:Integer))")]
    [InlineData("s * s", "(Multiply:Single s:Single s:Single)")]
    [InlineData("s + 1", "(Add:Single s:Single 1.0:Single)")]
    [InlineData("s + i", "(Add:Double (conv Double s:Single) (conv Double i:Integer))")]
    [InlineData("s - d", "(Subtract:Double (conv Double s:Single) d:Double)")]
    public void RealMixing(string expression, string expected) => Assert.Equal(expected, Typed(expression));

    /// <summary>
    /// C-058: LANG-230 (an integer constant keeps a Single operation only when Single holds it exactly; real constants are
    /// Double), LANG-232 (comparisons in the operation type), LANG-233 (constants folded in Double).
    /// </summary>
    [Theory]
    [Trait("Req", "LANG-230")]
    [Trait("Req", "LANG-232")]
    [Trait("Req", "LANG-233")]
    [InlineData("s + 16777216", "(Add:Single s:Single 16777216.0:Single)")]
    [InlineData("s + 16777217", "(Add:Double (conv Double s:Single) 16777217.0:Double)")]
    [InlineData("s < 1", "(Less:Boolean s:Single 1.0:Single)")]
    [InlineData("s = 0.5", "(Equal:Boolean (conv Double s:Single) 0.5:Double)")]
    [InlineData("d <> d", "(NotEqual:Boolean d:Double d:Double)")]
    [InlineData("u / 2", "(Divide:Double (conv Double u:UInt64) 2.0:Double)")]
    [InlineData("-s", "(Negate:Single s:Single)")]
    [InlineData("i64 * 1.5", "(Multiply:Double (conv Double i64:Int64) 1.5:Double)")]
    [InlineData("0.1 + 0.2", "0.30000000000000004:Double")]
    [InlineData("1 / 3 * 3", "1.0:Double")]
    [InlineData("-Pi < -3", "True:Boolean")]
    public void Reals_Typing(string expression, string expected) => Assert.Equal(expected, Typed(expression));

    // ------------------------------------------------------------------ LANG-227: unary operators

    [Theory]
    [Trait("Req", "LANG-227")]
    [InlineData("-b", "(Negate:Integer (conv Integer b:Byte))")]
    [InlineData("-c", "(Negate:Int64 (conv Int64 c:Cardinal))")]
    [InlineData("-i64", "(Negate:Int64 i64:Int64)")]
    [InlineData("+w", "(Identity:Integer (conv Integer w:Word))")]
    [InlineData("-d", "(Negate:Double d:Double)")]
    [InlineData("not b", "(Not:Byte b:Byte)")]
    [InlineData("not w", "(Not:Word w:Word)")]
    [InlineData("not i", "(Not:Integer i:Integer)")]
    [InlineData("not c", "(Not:Cardinal c:Cardinal)")]
    [InlineData("not ni", "(Not:Int64 (conv Int64 ni:NativeInt))")]
    [InlineData("not flag", "(Not:Boolean flag:Boolean)")]
    [InlineData("not bb", "(Not:ByteBool bb:ByteBool)")]
    public void Unary(string expression, string expected) => Assert.Equal(expected, Typed(expression));

    [Fact]
    [Trait("Req", "LANG-227")]
    public void NegatingUInt64_Warns()
    {
        var (dump, diagnostics) = BindExpression("-u");
        Assert.Equal("(Negate:UInt64 u:UInt64)", dump);
        Assert.Equal(["W1010(2,6): Mixing signed and unsigned 64-bit operands"], diagnostics);
    }

    // ------------------------------------------------------------------ LANG-301…305: Boolean and relational operators

    [Theory]
    [Trait("Req", "LANG-301")]
    [InlineData("i < c", "(Less:Boolean (conv Int64 i:Integer) (conv Int64 c:Cardinal))")]
    [InlineData("b = 1", "(Equal:Boolean (conv Integer b:Byte) 1:Integer)")]
    [InlineData("c >= 0", "(GreaterOrEqual:Boolean c:Cardinal 0:Cardinal)")]
    [InlineData("ch = 'a'", "(Equal:Boolean ch:Char 'a':Char)")]
    [InlineData("ac <> 'a'", "(NotEqual:Boolean ac:AnsiChar 'a':AnsiChar)")]
    [InlineData("ch <= ch", "(LessOrEqual:Boolean ch:Char ch:Char)")]
    [InlineData("flag > bb", "(Greater:Boolean flag:Boolean (conv Boolean bb:ByteBool))")]
    [InlineData("d < i", "(Less:Boolean d:Double (conv Double i:Integer))")]
    public void Relational_GivesBoolean(string expression, string expected) => Assert.Equal(expected, Typed(expression));

    [Theory]
    [Trait("Req", "LANG-302")]
    [InlineData("i64 < u", "(Less:Boolean i64:Int64 u:UInt64)")]
    [InlineData("i = u", "(Equal:Boolean (conv Int64 i:Integer) u:UInt64)")]
    [InlineData("u > (-1)", "(Greater:Boolean u:UInt64 -1:Int64)")]
    public void SignedUnsigned64Comparison_KeepsEachSidesSignedness_WithoutWarning(string expression, string expected) =>
        Assert.Equal(expected, Typed(expression));

    [Theory]
    [Trait("Req", "LANG-305")]
    [InlineData("flag and (i > 0)", "(ConditionalAnd:Boolean flag:Boolean (Greater:Boolean i:Integer 0:Integer))")]
    [InlineData("flag or bb", "(ConditionalOr:Boolean flag:Boolean (conv Boolean bb:ByteBool))")]
    [InlineData("flag xor flag", "(Xor:Boolean flag:Boolean flag:Boolean)")]
    [InlineData("flag and True", "(ConditionalAnd:Boolean flag:Boolean True:Boolean)")]
    public void BooleanOperators_ShortCircuitByDefault(string expression, string expected) => Assert.Equal(expected, Typed(expression));

    [Fact]
    [Trait("Req", "LANG-305")]
    public void BooleanOperators_UnderBPlus_EvaluateCompletely()
    {
        Assert.Equal("(And:Boolean flag:Boolean flag:Boolean)", Typed("flag and flag", "{$B+}"));
        Assert.Equal("(Or:Boolean flag:Boolean flag:Boolean)", Typed("flag or flag", "{$B+}"));
    }

    // ------------------------------------------------------------------ literals, names, typecasts

    [Theory]
    [InlineData("'a'", "'a':Char")]
    [InlineData("'ab'", "'ab':string")]
    [InlineData("True", "True:Boolean")]
    [InlineData("$FFFFFFFF", "4294967295:Cardinal")]
    [InlineData("ch", "ch:Char")]
    [InlineData("(i)", "i:Integer")]
    public void Factors(string expression, string expected) => Assert.Equal(expected, Typed(expression));

    [Theory]
    [Trait("Req", "LANG-290")]
    [InlineData("Byte(i)", "(cast Byte i:Integer)")]
    [InlineData("Integer(flag)", "(cast Integer flag:Boolean)")]
    [InlineData("Char(w)", "(cast Char w:Word)")]
    [InlineData("Double(i)", "(cast Double i:Integer)")]
    [InlineData("Double(5)", "(cast Double 5:Integer)")]            // integer -> real casts are not constant (LANG-340)
    [InlineData("Byte(300)", "44:Byte")]
    [InlineData("Int64(i) shl 33", "(Shl:Int64 (cast Int64 i:Integer) 33:Int64)")]
    public void OrdinalTypecasts(string expression, string expected) => Assert.Equal(expected, Typed(expression));

    // ------------------------------------------------------------------ errors (E2011, E2009, E2040, cascades)

    [Theory]
    [Trait("Req", "LANG-301")]
    [InlineData("flag + 1", "E2011(2,11): Operator '+' not applicable to operand type(s) 'Boolean' and 'Integer'")]
    [InlineData("ch + 1", "E2011(2,9): Operator '+' not applicable to operand type(s) 'Char' and 'Integer'")]
    [InlineData("i and flag", "E2011(2,8): Operator 'and' not applicable to operand type(s) 'Integer' and 'Boolean'")]
    [InlineData("-flag", "E2011(2,6): Operator '-' not applicable to operand type(s) 'Boolean'")]
    [InlineData("not ch", "E2011(2,6): Operator 'not' not applicable to operand type(s) 'Char'")]
    [InlineData("ch = ac", "E2011(2,9): Operator '=' not applicable to operand type(s) 'Char' and 'AnsiChar'")]
    [InlineData("i = flag", "E2011(2,8): Operator '=' not applicable to operand type(s) 'Integer' and 'Boolean'")]
    [InlineData("d div 2", "E2011(2,8): Operator 'div' not applicable to operand type(s) 'Double' and 'Integer'")]
    [InlineData("i shl d", "E2011(2,8): Operator 'shl' not applicable to operand type(s) 'Integer' and 'Double'")]
    [InlineData("flag < 'a'", "E2011(2,11): Operator '<' not applicable to operand type(s) 'Boolean' and 'Char'")]
    [InlineData("Integer + 1", "E2009(2,6): Variable or constant expected but type 'Integer' found")]
    [InlineData("Byte(d)", "E2040(2,6): Invalid typecast from 'Double' to 'Byte'")]
    [InlineData("1 div 0", "E2052(2,8): Division by zero in constant expression")]
    [InlineData("i + Missing", "E2003(2,10): Undeclared identifier 'Missing'")]
    [InlineData("Byte(i:2)", "E2915(2,12): Width specifiers are only allowed in Write, Writeln and Str")]
    public void Errors(string expression, string expected) => Assert.Equal([expected], BindExpression(expression).Diagnostics);

    [Theory]
    [Trait("Req", "CMP-135")]
    [InlineData("(Missing + 1) * flag")]
    [InlineData("-Missing")]
    [InlineData("not (i + Missing)")]
    [InlineData("Byte(Missing) = ch")]
    public void ErrorOperands_DoNotCascade(string expression)
    {
        var (dump, diagnostics) = BindExpression(expression);
        Assert.StartsWith("E2003(", Assert.Single(diagnostics), StringComparison.Ordinal);
        Assert.Equal("<error>", dump);
    }

    /// <summary>C-061 (LANG-243/244): the former M2.2 placeholders are typed string operations now.</summary>
    [Theory]
    [Trait("Req", "LANG-243")]
    [Trait("Req", "LANG-244")]
    [InlineData("str + str", "(Add:string str:string str:string)")]
    [InlineData("ch + ch", "(Add:string (conv string ch:Char) (conv string ch:Char))")]
    [InlineData("str = 'a'", "(Equal:Boolean str:string 'a':string)")]
    public void StringOperators_AreTyped(string expression, string expected) => Assert.Equal(expected, Typed(expression));

    /// <summary>
    /// LANG-243/244 (C-063): byte strings of one kind stay byte strings (a constant takes their kind), mixed kinds go through
    /// UnicodeString; a non-string operand is E2011.
    /// </summary>
    [Theory]
    [Trait("Req", "LANG-243")]
    [Trait("Req", "LANG-244")]
    [InlineData("str + ansi", "")]
    [InlineData("ansi = str", "")]
    [InlineData("ansi + 1", "E2011(2,11): Operator '+' not applicable to operand type(s) 'AnsiString' and 'Integer'")]
    public void StringOperators_OfOtherStringKinds(string expression, string expected) =>
        Assert.Equal(expected.Length == 0 ? [] : [expected], BindExpressionWithAnsi(expression));

    private static string[] BindExpressionWithAnsi(string expression)
    {
        var tree = SyntaxTree.Parse(SourceText.From(TestPath, $"program P; {Variables} var ansi: AnsiString; begin\nX := {expression};\nend."));
        var types = new BuiltInTypes();
        var declarations = DeclarationBinder.BindCompilationUnit(tree, types);
        var assignment = tree.Root.DescendantNodes().OfType<AssignmentStatementSyntax>().First();
        var diagnostics = new DiagnosticBag();
        new ExpressionBinder(types, declarations.Scope, diagnostics).BindExpression(assignment.Value);
        return Format(diagnostics.ToImmutable());
    }
}
