using FreexComp.Debugger.Expressions;
using FreexComp.Debugger.Tests.TestSupport;

namespace FreexComp.Debugger.Tests;

/// <summary>
/// The expression evaluator (Pascal values §9.2 … §9.7) over a fake debuggee: identifier resolution, designators,
/// every operator row, every builtin, typecasts, error messages and the worked examples.
/// Beds: the demo program stopped in <c>Run</c> on line 71, and the "zoo" with one variable per type family.
/// </summary>
public sealed class ExpressionEvaluatorTests
{
    private static Task<string> Demo(string expression) => EvalBed.Demo().ResultAsync(expression);

    private static Task<string> Zoo(string expression) => ZooBed.Test().ResultAsync(expression);

    private static Task<string> ZooError(string expression) => ZooBed.Test().ErrorAsync(expression);

    private static string Address(ulong address) => "$" + address.ToString("X16");

    // ---- identifier resolution (§9.2) ----

    [Theory]
    [InlineData("Total", "24 : Integer")]
    [InlineData("total", "24 : Integer")]          // case-insensitive
    [InlineData("TOTAL", "24 : Integer")]
    [InlineData("Ok", "True : Boolean")]
    [InlineData("Ch", "'A' : Char")]
    public async Task Resolve_Local(string expression, string expected)
    {
        Assert.Equal(expected, await Demo(expression));
    }

    [Fact]
    public async Task Resolve_SelfField_AfterLocals()
    {
        var bed = ZooBed.Speak();

        // Rule 2: a field of the static class of Self; "Name" means "Self.Name".
        Assert.Equal("'Rex' : string", await bed.ResultAsync("Name"));
        Assert.Equal("'Rex' : string", await bed.ResultAsync("Self.Name"));
        // Rule 1 wins: the local "Legs" hides the field.
        Assert.Equal("99 : Integer", await bed.ResultAsync("Legs"));
        Assert.Equal("4 : Integer", await bed.ResultAsync("Self.Legs"));
        // "Tricks" is a field of the dynamic class only: not reachable without qualification.
        Assert.Equal("Undeclared identifier 'Tricks'", await bed.ErrorAsync("Tricks"));
        Assert.Equal("9 : Integer", await bed.ResultAsync("Self.Tricks"));
        // The implicit field is a location like any other.
        var name = Assert.IsType<LocationValue>(await bed.ValueAsync("Name"));
        Assert.Equal(ZooBed.Instance + 8, name.Address);
    }

    [Fact]
    public async Task Resolve_Global_OfTheFramesUnit()
    {
        Assert.Equal("5 : Integer", await Zoo("Counter"));
        Assert.Equal("6 : Integer", await ZooBed.Speak().ResultAsync("counter + 1"));
        // The demo frame's unit has no globals; another unit's global is not visible (rule 3).
        Assert.Equal("Undeclared identifier 'ExitCode'", await EvalBed.Demo().ErrorAsync("ExitCode"));
    }

    [Theory]
    [InlineData("clGreen", "clGreen : TColor")]
    [InlineData("CLBLUE", "clBlue : TColor")]
    [InlineData("bgLast", "bgLast : TBig")]
    [InlineData("True", "True : Boolean")]
    [InlineData("false", "False : Boolean")]
    [InlineData("nil", "nil : Pointer")]
    [InlineData("TColor.clBlue", "clBlue : TColor")]
    public async Task Resolve_EnumValue_True_False_Nil(string expression, string expected)
    {
        Assert.Equal(expected, await Zoo(expression));
    }

    [Fact]
    public async Task Resolve_TypeName_OnlyAsCastOrArgument()
    {
        Assert.Equal("4 : Integer", await Zoo("SizeOf(Integer)"));
        Assert.Equal("clBlue : TColor", await Zoo("TColor(2)"));
        Assert.Equal("'Integer' is a type, not a value", await ZooError("Integer"));
        Assert.Equal("Operator '+' not applicable to Integer and Integer", await ZooError("Integer + 1"));
    }

    [Fact]
    public async Task Resolve_Undeclared_And_NotLocatable()
    {
        var bed = EvalBed.Demo();
        bed.Debuggee.Variables.Remove("Total");

        Assert.Equal("Undeclared identifier 'Foo'", await bed.ErrorAsync("Foo"));
        Assert.Equal("Undeclared identifier 'Foo'", await bed.ErrorAsync("Ok and Foo"));
        // Known to the type information, but the adapter cannot locate it.
        Assert.Equal("'Total' is not available", await bed.ErrorAsync("Total"));
        // "Address of" is asked with the declared spelling, in the adapter's frame.
        await bed.EvalAsync("PTOTAL");
        Assert.Contains($"addr {DemoMemory.AdapterFrameId}:PTotal", bed.Debuggee.Requests);
    }

    // ---- designators (§9.4) ----

    [Theory]
    [InlineData("Pt.X", "3 : Integer")]
    [InlineData("pt.y", "4 : Integer")]
    [InlineData("Animal.Name", "'Rex' : string")]
    [InlineData("Animal.Legs", "4 : Integer")]
    [InlineData("Animal.Tricks", "9 : Integer")]             // a field of the dynamic class
    [InlineData("PPt^.X", "3 : Integer")]
    public async Task Designator_Field(string expression, string expected)
    {
        Assert.Equal(expected, await Zoo(expression));
    }

    [Theory]
    [InlineData("NilAnimal.Name", "Access through nil reference")]
    [InlineData("Pt.Z", "Undeclared identifier 'Z'")]
    [InlineData("Animal.Wings", "Undeclared identifier 'Wings'")]
    [InlineData("B.X", "Operator '.' not applicable to Byte")]
    [InlineData("PPt.X", "Operator '.' not applicable to PPoint")]
    public async Task Designator_Field_Errors(string expression, string message)
    {
        Assert.Equal(message, await ZooError(expression));
    }

    [Theory]
    // static arrays: range-checked, any index base and index type
    [InlineData("Ints[0]", "10 : Integer")]
    [InlineData("Ints[3]", "40 : Integer")]
    [InlineData("Ints[B - 198]", "30 : Integer")]
    [InlineData("ByColor[clGreen]", "8 : Byte")]
    [InlineData("ByColor[Col]", "9 : Byte")]
    [InlineData("ByLetter['b']", "200 : Integer")]
    [InlineData("Matrix[1][0]", "5 : Byte")]
    [InlineData("Matrix[1, 0]", "5 : Byte")]                  // A[i, j] = A[i][j]
    [InlineData("Matrix[0][-1]", "1 : Byte")]
    // dynamic arrays and open-array parameters: 0 .. Length - 1
    [InlineData("Dyn[0]", "1 : Integer")]
    [InlineData("Dyn[2]", "3 : Integer")]
    [InlineData("A[2]", "7 : Integer")]
    [InlineData("Pts[1].X", "3 : Integer")]
    // strings: 1 .. Length, a Char value
    [InlineData("Str[1]", "'a' : Char")]
    [InlineData("Str[3]", "'c' : Char")]
    [InlineData("Short[2]", "'i' : Char")]
    [InlineData("('xyz')[2]", "'y' : Char")]
    // typed pointers: element i of SizeOf(T), unchecked
    [InlineData("PI[0]", "20 : Integer")]
    [InlineData("PI[1]", "30 : Integer")]
    [InlineData("PI[-1]", "10 : Integer")]
    [InlineData("PC[1]", "'c' : Char")]
    public async Task Designator_Index(string expression, string expected)
    {
        Assert.Equal(expected, await Zoo(expression));
    }

    [Theory]
    [InlineData("Ints[4]", "Index 4 out of bounds (0..3)")]
    [InlineData("Ints[-1]", "Index -1 out of bounds (0..3)")]
    [InlineData("Matrix[0][2]", "Index 2 out of bounds (-1..1)")]
    [InlineData("ByLetter['d']", "Index 100 out of bounds (97..99)")]
    [InlineData("Dyn[3]", "Index 3 out of bounds (0..2)")]
    [InlineData("NilDyn[0]", "Index 0 out of bounds (0..-1)")]
    [InlineData("A[3]", "Index 3 out of bounds (0..2)")]
    [InlineData("Str[0]", "Index 0 out of bounds (1..3)")]
    [InlineData("Str[4]", "Index 4 out of bounds (1..3)")]
    [InlineData("Empty[1]", "Index 1 out of bounds (1..0)")]
    [InlineData("NilPI[0]", "Access through nil reference")]
    [InlineData("Pt[0]", "Operator '[]' not applicable to TPoint and Integer")]
    [InlineData("Ints[1.5]", "Operator '[]' not applicable to array[0..3] of Integer and Double")]
    [InlineData("Untyped[0]", "Operator '[]' not applicable to Pointer and Integer")]
    public async Task Designator_Index_Errors(string expression, string message)
    {
        Assert.Equal(message, await ZooError(expression));
    }

    [Theory]
    [InlineData("PI^", "20 : Integer")]
    [InlineData("PPI^^", "20 : Integer")]
    [InlineData("PPt^.Y", "4 : Integer")]
    [InlineData("Animal^.Legs", "4 : Integer")]               // on a class instance "^" is a no-op
    [InlineData("(@Pt)^.Y", "4 : Integer")]
    [InlineData("(@Ints[2])^", "30 : Integer")]
    public async Task Designator_Deref(string expression, string expected)
    {
        Assert.Equal(expected, await Zoo(expression));
    }

    [Theory]
    [InlineData("NilPI^", "Access through nil reference")]
    [InlineData("Untyped^", "Cannot dereference an untyped pointer")]
    [InlineData("Pt^", "Operator '^' not applicable to TPoint")]
    [InlineData("(1 + 2)^", "Operator '^' not applicable to Integer")]
    public async Task Designator_Deref_Errors(string expression, string message)
    {
        Assert.Equal(message, await ZooError(expression));
    }

    [Fact]
    public async Task Designator_AddressOf()
    {
        var bed = ZooBed.Test();

        Assert.Equal($"{Address(ZooBed.AddressOf("Pt"))} : ^TPoint", await bed.ResultAsync("@Pt"));
        Assert.Equal($"{Address(ZooBed.AddressOf("Pt") + 4)} : ^Integer", await bed.ResultAsync("@Pt.Y"));
        Assert.Equal($"{Address(ZooBed.AddressOf("Ints") + 8)} : ^Integer", await bed.ResultAsync("@Ints[2]"));
        Assert.Equal($"{Address(ZooBed.Instance + 16)} : ^Integer", await bed.ResultAsync("@Animal.Legs"));
        // Not allowed on immediates.
        Assert.Equal("Operator '@' not applicable to Integer", await bed.ErrorAsync("@5"));
        Assert.Equal("Operator '@' not applicable to Integer", await bed.ErrorAsync("@(B + 1)"));
        Assert.Equal("Operator '@' not applicable to Char", await bed.ErrorAsync("@Str[1]"));
    }

    [Theory]
    // A location is read only when an operator needs its value: every scalar kind.
    [InlineData("B + 0", "200 : Integer")]
    [InlineData("Sh + 0", "-5 : Integer")]
    [InlineData("W + 0", "65535 : Integer")]
    [InlineData("C32 + 0", "4000000000 : Int64")]
    [InlineData("L + 0", "-5000000000 : Int64")]
    [InlineData("U + 0", "18446744073709551615 : UInt64")]
    [InlineData("F + 0", "1.5 : Double")]
    [InlineData("D * 2", "4.5 : Double")]
    [InlineData("Cur + 0", "12.5 : Double")]
    [InlineData("not Flag", "False : Boolean")]
    [InlineData("AC + ''", "'x' : string")]
    [InlineData("V + 1", "78 : Integer")]                    // a by-reference parameter
    public async Task Designator_RvalueReads(string expression, string expected)
    {
        Assert.Equal(expected, await Zoo(expression));
    }

    [Fact]
    public async Task Designator_Result_IsALocation()
    {
        var bed = ZooBed.Test();

        var point = await bed.EvalAsync(" PPt ^ ");

        Assert.Equal("(X: 3; Y: 4)", point.Value);
        Assert.Equal(ValueKind.Record, point.Kind);
        Assert.Equal("PPt ^", point.Name);                    // as typed, trimmed
        Assert.Equal("PPt^", point.EvaluateName);             // normalised
        Assert.Equal(ZooBed.AddressOf("Pt"), point.Address);
        Assert.Equal(2, point.ChildCount);
        Assert.Equal(["X", "Y"], (await bed.ChildrenAsync(point.Children!.Value)).Select(v => v.Name));
        Assert.Equal(["PPt^.X", "PPt^.Y"], (await bed.ChildrenAsync(point.Children.Value)).Select(v => v.EvaluateName));
        Assert.True((await bed.EvalAsync("Pt.X")).CanModify);
        Assert.False((await bed.EvalAsync("Pt")).CanModify);
        Assert.False((await bed.EvalAsync("Pt.X + 0")).CanModify);
    }

    // ---- operators (§9.4, DBG-181) ----

    [Theory]
    // + - * on Int: 64-bit wrap-around, no overflow error
    [InlineData("2 + 3", "5 : Integer")]
    [InlineData("2 - 5", "-3 : Integer")]
    [InlineData("6 * 7", "42 : Integer")]
    [InlineData("2147483647 + 1", "2147483648 : Int64")]
    [InlineData("9223372036854775807 + 1", "-9223372036854775808 : Int64")]
    [InlineData("U + 1", "0 : Integer")]
    [InlineData("L * 2", "-10000000000 : Int64")]
    // + - * / mixed: Float; "/" is always Float
    [InlineData("1.5 + 2", "3.5 : Double")]
    [InlineData("2 * 1.5", "3 : Double")]
    [InlineData("D - 1", "1.25 : Double")]
    [InlineData("1 / 2", "0.5 : Double")]
    [InlineData("4 / 2", "2 : Double")]
    [InlineData("B / 8", "25 : Double")]
    // div mod: truncating toward zero; the sign of mod is the sign of the dividend
    [InlineData("7 div 2", "3 : Integer")]
    [InlineData("(-7) div 2", "-3 : Integer")]
    [InlineData("7 div (-2)", "-3 : Integer")]
    [InlineData("7 mod 3", "1 : Integer")]
    [InlineData("(-7) mod 3", "-1 : Integer")]
    [InlineData("7 mod (-3)", "1 : Integer")]
    [InlineData("U div 2", "9223372036854775807 : UInt64")]      // unsigned stays unsigned
    // shl shr: on 64 bits, shr logical
    [InlineData("1 shl 4", "16 : Integer")]
    [InlineData("256 shr 4", "16 : Integer")]
    [InlineData("1 shl 63", "-9223372036854775808 : Int64")]
    [InlineData("(-1) shr 60", "15 : Integer")]
    // unary - +
    [InlineData("-B", "-200 : Integer")]
    [InlineData("+B", "200 : Integer")]
    [InlineData("-D", "-2.25 : Double")]
    [InlineData("-(-5)", "5 : Integer")]
    // and or xor not on Bool
    [InlineData("True and False", "False : Boolean")]
    [InlineData("True or False", "True : Boolean")]
    [InlineData("True xor True", "False : Boolean")]
    [InlineData("Flag xor False", "True : Boolean")]
    [InlineData("not True", "False : Boolean")]
    // and or xor not on Int: bitwise
    [InlineData("12 and 10", "8 : Integer")]
    [InlineData("12 or 3", "15 : Integer")]
    [InlineData("12 xor 10", "6 : Integer")]
    [InlineData("not 0", "-1 : Integer")]
    [InlineData("B and $0F", "8 : Integer")]
    public async Task Operator_Arithmetic_Logical(string expression, string expected)
    {
        Assert.Equal(expected, await Zoo(expression));
    }

    [Theory]
    // = <> : Int/Float mixed; Bool; Char; Enum; Str (and Char with Str); Ptr/Nil; instance references; Set
    [InlineData("1 = 1", "True")]
    [InlineData("1 = 1.0", "True")]
    [InlineData("1 <> 2", "True")]
    [InlineData("D = 2.25", "True")]
    [InlineData("U = 18446744073709551615", "True")]
    [InlineData("U = -1", "False")]                           // the unsigned value, not its bits
    [InlineData("Flag = True", "True")]
    [InlineData("True <> True", "False")]
    [InlineData("WC = 'é'", "True")]
    [InlineData("AC <> 'x'", "False")]
    [InlineData("Col = clBlue", "True")]
    [InlineData("Col <> clRed", "True")]
    [InlineData("Str = 'abc'", "True")]
    [InlineData("Str = 'ABC'", "False")]                      // case-sensitive
    [InlineData("Str <> 'abd'", "True")]
    [InlineData("Empty = ''", "True")]
    [InlineData("Str = 'a'", "False")]                        // Char with Str
    [InlineData("'a' = Str[1]", "True")]
    [InlineData("NilPI = nil", "True")]
    [InlineData("PI <> nil", "True")]
    [InlineData("nil = nil", "True")]
    [InlineData("PPt = @Pt", "True")]
    [InlineData("PI = @Ints[1]", "True")]
    [InlineData("PI = @Ints[2]", "False")]
    [InlineData("Untyped = PPt", "True")]
    [InlineData("Animal = Animal", "True")]
    [InlineData("Animal <> NilAnimal", "True")]
    [InlineData("NilAnimal = nil", "True")]
    [InlineData("Cols = [clRed, clBlue]", "True")]
    [InlineData("Cols <> []", "True")]
    [InlineData("Bytes = [1, 2, 3, 7]", "True")]
    [InlineData("[] = []", "True")]
    // < <= > >= : Int/Float; Char; Enum; Str; on sets <= and >= mean subset and superset
    [InlineData("1 < 2", "True")]
    [InlineData("2 <= 2", "True")]
    [InlineData("3 > 2.5", "True")]
    [InlineData("2 >= 3", "False")]
    [InlineData("L < U", "True")]
    [InlineData("U > 0", "True")]
    [InlineData("'a' < 'b'", "True")]
    [InlineData("WC >= 'z'", "True")]
    [InlineData("Col > clRed", "True")]
    [InlineData("Col <= clGreen", "False")]
    [InlineData("'abc' < 'abd'", "True")]
    [InlineData("'Z' < 'a'", "True")]                         // by code unit
    [InlineData("Str >= 'abc'", "True")]
    [InlineData("Str > 'abcd'", "False")]
    [InlineData("[clRed] <= Cols", "True")]
    [InlineData("Cols <= [clRed]", "False")]
    [InlineData("Cols >= [clRed, clGreen]", "False")]
    [InlineData("Bytes >= [1..3]", "True")]
    [InlineData("[] <= Cols", "True")]
    // in
    [InlineData("clRed in Cols", "True")]
    [InlineData("clGreen in Cols", "False")]
    [InlineData("3 in Bytes", "True")]
    [InlineData("4 in [1..3]", "False")]
    [InlineData("300 in Bytes", "False")]
    [InlineData("'q' in ['a'..'z']", "True")]
    [InlineData("Col in [clBlue]", "True")]
    public async Task Operator_Relational(string expression, string expected)
    {
        Assert.Equal(expected + " : Boolean", await Zoo(expression));
    }

    [Theory]
    // + on Str/Char: concatenation
    [InlineData("'ab' + 'cd'", "'abcd' : string")]
    [InlineData("Str + '!'", "'abc!' : string")]
    [InlineData("'a' + 'b'", "'ab' : string")]
    [InlineData("WC + Str", "'éabc' : string")]
    [InlineData("Str + Short + Empty", "'abchi' : string")]
    [InlineData("'it' + '''' + 's'", "'it''s' : string")]
    // + - * on sets of the same base: union, difference, intersection
    [InlineData("Cols + [clGreen]", "[clRed, clGreen, clBlue] : set of TColor")]
    [InlineData("Cols - [clRed]", "[clBlue] : set of TColor")]
    [InlineData("Cols * [clBlue, clGreen]", "[clBlue] : set of TColor")]
    [InlineData("[] + Cols", "[clRed, clBlue] : set of TColor")]
    [InlineData("[1, 2] + [3..5]", "[1..5] : set of Integer")]
    [InlineData("Bytes - [2]", "[1, 3, 7] : set of Byte")]
    [InlineData("Bytes * [2..8]", "[2, 3, 7] : set of Byte")]
    [InlineData("['a'..'c'] - ['b']", "['a', 'c'] : set of Char")]
    [InlineData("[]", "[] : set")]
    public async Task Operator_String_Set(string expression, string expected)
    {
        Assert.Equal(expected, await Zoo(expression));
    }

    [Theory]
    // precedence is the grammar's: not @ > * / div mod and shl shr > + - or xor > relational
    [InlineData("2 + 3 * 4", "14 : Integer")]
    [InlineData("(2 + 3) * 4", "20 : Integer")]
    [InlineData("-2 * 3", "-6 : Integer")]
    [InlineData("10 - 4 - 3", "3 : Integer")]
    [InlineData("2 * 3 = 6", "True : Boolean")]
    [InlineData("1 + 2 = 3", "True : Boolean")]
    [InlineData("not True or True", "True : Boolean")]
    [InlineData("not (True or True)", "False : Boolean")]
    [InlineData("1 or 2 and 3", "3 : Integer")]
    [InlineData("(B > 100) and (Sh < 0)", "True : Boolean")]
    [InlineData("8 shr 1 + 1", "5 : Integer")]
    public async Task Operator_Precedence(string expression, string expected)
    {
        Assert.Equal(expected, await Zoo(expression));
    }

    [Theory]
    [InlineData("1 / 0", "Division by zero")]
    [InlineData("1.5 / 0.0", "Division by zero")]
    [InlineData("5 div 0", "Division by zero")]
    [InlineData("5 mod 0", "Division by zero")]
    [InlineData("B div (Sh + 5)", "Division by zero")]
    // both sides are always evaluated
    [InlineData("False and (1 div 0 = 1)", "Division by zero")]
    [InlineData("True or (NilPI^ = 1)", "Access through nil reference")]
    // type errors
    [InlineData("True + 1", "Operator '+' not applicable to Boolean and Integer")]
    [InlineData("'a' * 2", "Operator '*' not applicable to Char and Integer")]
    [InlineData("Str - 'a'", "Operator '-' not applicable to string and Char")]
    [InlineData("Pt + 1", "Operator '+' not applicable to TPoint and Integer")]
    [InlineData("Ints = Ints", "Operator '=' not applicable to array[0..3] of Integer and array[0..3] of Integer")]
    [InlineData("1 and True", "Operator 'and' not applicable to Integer and Boolean")]
    [InlineData("1.5 div 2", "Operator 'div' not applicable to Double and Integer")]
    [InlineData("1.5 shl 2", "Operator 'shl' not applicable to Double and Integer")]
    [InlineData("Col = bgLast", "Operator '=' not applicable to TColor and TBig")]
    [InlineData("Col = 2", "Operator '=' not applicable to TColor and Integer")]
    [InlineData("Col < bgLast", "Operator '<' not applicable to TColor and TBig")]
    [InlineData("Flag < True", "Operator '<' not applicable to Boolean and Boolean")]
    [InlineData("PI < PI", "Operator '<' not applicable to ^Integer and ^Integer")]
    [InlineData("PI = 0", "Operator '=' not applicable to ^Integer and Integer")]
    [InlineData("Cols < Cols", "Operator '<' not applicable to set of TColor and set of TColor")]
    [InlineData("Cols + [1]", "Operator '+' not applicable to set of TColor and set of Integer")]
    [InlineData("Cols = Bytes", "Operator '=' not applicable to set of TColor and set of Byte")]
    [InlineData("1 in Cols", "Operator 'in' not applicable to Integer and set of TColor")]
    [InlineData("clRed in Bytes", "Operator 'in' not applicable to TColor and set of Byte")]
    [InlineData("1 in 2", "Operator 'in' not applicable to Integer and Integer")]
    [InlineData("not 'a'", "Operator 'not' not applicable to Char")]
    [InlineData("-True", "Operator '-' not applicable to Boolean")]
    [InlineData("-Str", "Operator '-' not applicable to string")]
    [InlineData("[1, 'a']", "Set elements must have the same type")]
    [InlineData("[256]", "Set element out of range")]
    [InlineData("[1.5]", "Operator '[]' not applicable to Double")]
    public async Task Operator_Errors(string expression, string message)
    {
        Assert.Equal(message, await ZooError(expression));
    }

    // ---- builtins (§9.5, DBG-182) ----

    [Theory]
    [InlineData("Length(Str)", "3 : Integer")]
    [InlineData("length(Empty)", "0 : Integer")]
    [InlineData("Length(Short)", "2 : Integer")]
    [InlineData("Length('hello')", "5 : Integer")]
    [InlineData("Length('a')", "1 : Integer")]
    [InlineData("Length(Str + '!')", "4 : Integer")]
    [InlineData("Length(Dyn)", "3 : Integer")]
    [InlineData("Length(NilDyn)", "0 : Integer")]
    [InlineData("Length(Ints)", "4 : Integer")]
    [InlineData("Length(Matrix[0])", "3 : Integer")]
    [InlineData("Length(A)", "3 : Integer")]
    public async Task Builtin_Length(string expression, string expected)
    {
        Assert.Equal(expected, await Zoo(expression));
    }

    [Theory]
    // an array value
    [InlineData("High(Dyn)", "2 : Integer")]
    [InlineData("Low(Dyn)", "0 : Integer")]
    [InlineData("High(NilDyn)", "-1 : Integer")]
    [InlineData("High(Ints)", "3 : Integer")]
    [InlineData("Low(Matrix[0])", "-1 : Integer")]
    [InlineData("High(ByColor)", "clBlue : TColor")]
    [InlineData("Low(ByLetter)", "'a' : Char")]
    [InlineData("High(A)", "2 : Integer")]
    [InlineData("High(Str)", "3 : Integer")]
    // an ordinal type name
    [InlineData("High(Integer)", "2147483647 : Integer")]
    [InlineData("Low(Integer)", "-2147483648 : Integer")]
    [InlineData("High(Byte)", "255 : Integer")]
    [InlineData("Low(ShortInt)", "-128 : Integer")]
    [InlineData("High(Word)", "65535 : Integer")]
    [InlineData("High(Cardinal)", "4294967295 : Int64")]
    [InlineData("Low(Int64)", "-9223372036854775808 : Int64")]
    [InlineData("High(UInt64)", "18446744073709551615 : UInt64")]
    [InlineData("Low(TColor)", "clRed : TColor")]
    [InlineData("High(TColor)", "clBlue : TColor")]
    [InlineData("High(Boolean)", "True : Boolean")]
    [InlineData("High(Char)", "#65535 : Char")]
    [InlineData("High(AnsiChar)", "'ÿ' : Char")]
    // an ordinal value
    [InlineData("High(B)", "255 : Integer")]
    [InlineData("Low(Sh)", "-128 : Integer")]
    [InlineData("High(Col)", "clBlue : TColor")]
    [InlineData("High(5)", "2147483647 : Integer")]
    [InlineData("Low(Flag)", "False : Boolean")]
    public async Task Builtin_High_Low(string expression, string expected)
    {
        Assert.Equal(expected, await Zoo(expression));
    }

    [Theory]
    [InlineData("Ord(Col)", "2 : Integer")]
    [InlineData("Ord(Big)", "300 : Integer")]
    [InlineData("Ord('A')", "65 : Integer")]
    [InlineData("Ord(WC)", "233 : Integer")]
    [InlineData("Ord(True)", "1 : Integer")]
    [InlineData("Ord(5)", "5 : Integer")]
    [InlineData("Chr(65)", "'A' : Char")]
    [InlineData("Chr(13)", "#13 : Char")]
    [InlineData("Chr(B + 33)", "'é' : Char")]
    [InlineData("Abs(-5)", "5 : Integer")]
    [InlineData("Abs(Sh)", "5 : Integer")]
    [InlineData("Abs(-2.5)", "2.5 : Double")]
    [InlineData("Abs(D)", "2.25 : Double")]
    [InlineData("Odd(3)", "True : Boolean")]
    [InlineData("Odd(B)", "False : Boolean")]
    [InlineData("Succ(1)", "2 : Integer")]
    [InlineData("Pred(B)", "199 : Integer")]
    [InlineData("Succ('a')", "'b' : Char")]
    [InlineData("Pred(Col)", "clGreen : TColor")]
    [InlineData("Succ(clBlue)", "TColor(3) : TColor")]        // no range check
    public async Task Builtin_Ordinal(string expression, string expected)
    {
        Assert.Equal(expected, await Zoo(expression));
    }

    [Theory]
    [InlineData("SizeOf(B)", "1 : Integer")]
    [InlineData("SizeOf(Pt)", "8 : Integer")]
    [InlineData("SizeOf(Matrix)", "6 : Integer")]
    [InlineData("SizeOf(Matrix[0])", "3 : Integer")]
    [InlineData("SizeOf(Integer)", "4 : Integer")]
    [InlineData("SizeOf(TPoint)", "8 : Integer")]
    [InlineData("SizeOf(TAnimal)", "8 : Integer")]            // a class type: pointer size
    [InlineData("SizeOf(Animal)", "8 : Integer")]
    [InlineData("SizeOf(string)", "8 : Integer")]
    [InlineData("Assigned(PI)", "True : Boolean")]
    [InlineData("Assigned(NilPI)", "False : Boolean")]
    [InlineData("Assigned(Animal)", "True : Boolean")]
    [InlineData("Assigned(NilAnimal)", "False : Boolean")]
    [InlineData("Assigned(Dyn)", "True : Boolean")]
    [InlineData("Assigned(NilDyn)", "False : Boolean")]
    [InlineData("Assigned(Proc)", "True : Boolean")]
    [InlineData("Assigned(Intf)", "False : Boolean")]
    [InlineData("Assigned(Ref)", "True : Boolean")]
    [InlineData("Assigned(nil)", "False : Boolean")]
    [InlineData("Assigned(@Pt)", "True : Boolean")]
    public async Task Builtin_SizeOf_Assigned(string expression, string expected)
    {
        Assert.Equal(expected, await Zoo(expression));
    }

    [Theory]
    [InlineData("Length(B)", "'Length' is not applicable to Byte")]
    [InlineData("Length(5)", "'Length' is not applicable to Integer")]
    [InlineData("High(Pt)", "'High' is not applicable to TPoint")]
    [InlineData("Low(1.5)", "'Low' is not applicable to Double")]
    [InlineData("High(TPoint)", "'High' is not applicable to TPoint")]
    [InlineData("Ord(1.5)", "'Ord' is not applicable to Double")]
    [InlineData("Ord(Str)", "'Ord' is not applicable to string")]
    [InlineData("Chr('a')", "'Chr' is not applicable to Char")]
    [InlineData("Chr(65536)", "Value out of range for Char")]
    [InlineData("Chr(-1)", "Value out of range for Char")]
    [InlineData("Abs(True)", "'Abs' is not applicable to Boolean")]
    [InlineData("Odd(1.5)", "'Odd' is not applicable to Double")]
    [InlineData("Succ(1.5)", "'Succ' is not applicable to Double")]
    [InlineData("SizeOf(5)", "'SizeOf' is not applicable to Integer")]
    [InlineData("Assigned(B)", "'Assigned' is not applicable to Byte")]
    [InlineData("Length(Str, 1)", "Wrong number of arguments for 'Length'")]
    [InlineData("Length()", "Wrong number of arguments for 'Length'")]
    [InlineData("Odd(1, 2)", "Wrong number of arguments for 'Odd'")]
    public async Task Builtin_Errors(string expression, string message)
    {
        Assert.Equal(message, await ZooError(expression));
    }

    // ---- typecasts (§9.4) ----

    [Theory]
    // ordinal → integer / enum / char / boolean types: a value
    [InlineData("Integer(Col)", "2 : Integer")]
    [InlineData("integer('A')", "65 : Integer")]
    [InlineData("Integer(True)", "1 : Integer")]
    [InlineData("Byte(300)", "44 : Integer")]
    [InlineData("ShortInt(B)", "-56 : Integer")]
    [InlineData("Cardinal(-1)", "4294967295 : Int64")]
    [InlineData("UInt64(-1)", "18446744073709551615 : UInt64")]
    [InlineData("Int64(B)", "200 : Integer")]
    [InlineData("TColor(1)", "clGreen : TColor")]
    [InlineData("TColor(7)", "TColor(7) : TColor")]
    [InlineData("TBig(Col)", "TBig(2) : TBig")]
    [InlineData("Char(65)", "'A' : Char")]
    [InlineData("Boolean(0)", "False : Boolean")]
    [InlineData("Boolean(B)", "True : Boolean")]
    [InlineData("Integer(Col) + 1", "3 : Integer")]
    // pointer / integer → pointer type
    [InlineData("PPoint(Untyped)^.X", "3 : Integer")]
    [InlineData("PPoint(@Pt)^.Y", "4 : Integer")]
    [InlineData("PPoint(0)", "nil : ^TPoint")]
    [InlineData("PPoint(nil)", "nil : ^TPoint")]
    [InlineData("Pointer(16)", "$0000000000000010 : Pointer")]
    // class instance / pointer / integer → class type, unchecked
    [InlineData("TDog(Animal).Tricks", "9 : Integer")]
    [InlineData("TObject(Animal) = Animal", "True : Boolean")]
    [InlineData("TAnimal(360448).Legs", "4 : Integer")]       // the instance address as a number
    [InlineData("TAnimal(360448)", "TAnimal($0000000000058000) : TAnimal")]
    [InlineData("TAnimal(0)", "nil : Pointer")]
    // storage of the same size, reinterpreted
    [InlineData("TPoint(L).Y", "-2 : Integer")]
    [InlineData("Double(L) = D", "False : Boolean")]
    public async Task Cast_Values(string expression, string expected)
    {
        Assert.Equal(expected, await Zoo(expression));
    }

    [Fact]
    public async Task Cast_OfALocation_KeepsTheStorage()
    {
        var bed = ZooBed.Test();

        var typed = await bed.EvalAsync("PPoint(Untyped)");
        var dog = await bed.EvalAsync("TDog(Animal)");

        // A pointer-like location stays a location: expandable, with an EvaluateName that evaluates to the same storage.
        Assert.Equal($"{Address(ZooBed.AddressOf("Pt"))} : PPoint", typed.Value + " : " + typed.TypeName);
        Assert.Equal(ZooBed.AddressOf("Untyped"), typed.Address);
        Assert.Equal("PPoint(Untyped)", typed.EvaluateName);
        Assert.Equal(["^ = (X: 3; Y: 4)"], (await bed.ChildrenAsync(typed.Children!.Value)).Select(v => v.Name + " = " + v.Value));
        Assert.Equal($"TDog({Address(ZooBed.Instance)}) : TDog", dog.Value + " : " + dog.TypeName);
        Assert.Equal(["Name", "Legs", "Tricks"], (await bed.ChildrenAsync(dog.Children!.Value)).Select(v => v.Name));
        Assert.Equal("TDog(Animal).Tricks", (await bed.ChildrenAsync(dog.Children.Value))[2].EvaluateName);
    }

    [Theory]
    [InlineData("Integer(1.5)", "Invalid typecast")]
    [InlineData("Integer(Str)", "Invalid typecast")]
    [InlineData("Integer(Pt)", "Invalid typecast")]
    [InlineData("TColor(D)", "Invalid typecast")]
    [InlineData("PPoint(1.5)", "Invalid typecast")]
    [InlineData("TAnimal(Str)", "Invalid typecast")]
    [InlineData("TPoint(B)", "Invalid typecast")]              // size-changing cast of a location
    [InlineData("TPoint(5)", "Invalid typecast")]
    [InlineData("Double(B)", "Invalid typecast")]
    [InlineData("TPoint(1, 2)", "Wrong number of arguments for 'TPoint'")]
    [InlineData("TFoo(1)", "Function calls are not supported in expressions")]
    [InlineData("Animal.Speak()", "Function calls are not supported in expressions")]
    public async Task Cast_Errors(string expression, string message)
    {
        Assert.Equal(message, await ZooError(expression));
    }

    // ---- error messages ----

    [Fact]
    public async Task Errors_UnreadableMemory()
    {
        var bed = ZooBed.Test();
        bed.Debuggee.Memory.PutPointer(ZooBed.AddressOf("PI"), 0x99990000);

        // Needed by an operator: the evaluation fails. As the result itself: an error node, not an exception (DBG-152).
        Assert.Equal("<unreadable memory at $0000000099990000>", await bed.ErrorAsync("PI^ + 1"));
        var node = await bed.EvalAsync("PI^");
        Assert.Equal(ValueKind.Error, node.Kind);
        Assert.Equal("<unreadable memory at $0000000099990000>", node.Value);
        Assert.Null(node.EvaluateName);
    }

    [Fact]
    public async Task Errors_StringTooLong()
    {
        var bed = ZooBed.Test();
        bed.Debuggee.Memory.PutInt32(ZooBed.Heap + 0x220 - 12, 600_000);     // "Str" claims more than 1 MiB of UTF-16
        bed.Debuggee.Memory.Map(ZooBed.Heap + 0x220, 4096);

        Assert.Equal("String too long to evaluate", await bed.ErrorAsync("Str = 'abc'"));
        Assert.Equal("String too long to evaluate", await bed.ErrorAsync("Length(Str)"));
        // Shown by itself it is an ordinary (truncated) value.
        Assert.StartsWith("'abc'", (await bed.EvalAsync("Str")).Value);
    }

    [Fact]
    public async Task Errors_Codes_And_NoSideEffects()
    {
        var bed = EvalBed.Demo();

        var syntax = await Assert.ThrowsAsync<DebuggerException>(() => bed.EvalAsync("Total +"));
        Assert.Equal(DebugErrorCode.ExpressionSyntax, syntax.Code);
        Assert.Equal("Expression expected at column 8", syntax.Message);
        var rejected = await Assert.ThrowsAsync<DebuggerException>(() => bed.EvalAsync("Pet as TDog"));
        Assert.Equal(DebugErrorCode.ExpressionSyntax, rejected.Code);
        Assert.Equal("Format suffix ',1' is not supported", await bed.ErrorAsync("PTotal,1"));     // card DBG-807
        Assert.Equal("Function calls are not supported in expressions", await bed.ErrorAsync("Factorial(3)"));
        Assert.Equal("Index 3 out of bounds (0..2)", await bed.ErrorAsync("Nums[3]"));

        // Evaluation only locates root variables and reads memory.
        Assert.All(bed.Debuggee.Requests, request => Assert.StartsWith("addr ", request));
    }

    [Fact]
    public async Task Errors_Condition_MustBeBoolean()
    {
        var bed = EvalBed.Demo();
        var ct = TestContext.Current.CancellationToken;

        Assert.True(await bed.Values.EvaluateConditionAsync("Total = 24", bed.Frame, ct));
        Assert.False(await bed.Values.EvaluateConditionAsync("Ok and (Total > 100)", bed.Frame, ct));
        var notBoolean = await Assert.ThrowsAsync<DebuggerException>(() => bed.Values.EvaluateConditionAsync("Total", bed.Frame, ct));
        Assert.Equal("Boolean expression expected", notBoolean.Message);
        // A condition takes no format suffix.
        var suffix = await Assert.ThrowsAsync<DebuggerException>(() => bed.Values.EvaluateConditionAsync("Ok,x", bed.Frame, ct));
        Assert.Equal("Unexpected ',' at column 3", suffix.Message);
    }

    // ---- result presentation and the worked examples (§9.6, §9.7) ----

    [Theory]
    [InlineData("Total", "24", "Integer")]
    [InlineData("Total * 2 + 1", "49", "Integer")]
    [InlineData("Total,x", "$00000018", "Integer")]
    [InlineData("S", "'Héllo'", "string")]
    [InlineData("Length(S)", "5", "Integer")]
    [InlineData("S[2]", "'é'", "Char")]
    [InlineData("S + '!'", "'Héllo!'", "string")]
    [InlineData("Nums", "(10, 20, 30)", "array of Integer")]
    [InlineData("Nums[1]", "20", "Integer")]
    [InlineData("High(Nums)", "2", "Integer")]
    [InlineData("P", "(X: 3; Y: 4)", "TPoint")]
    [InlineData("P.X + P.Y", "7", "Integer")]
    [InlineData("Color", "clGreen", "TColor")]
    [InlineData("Ord(Color)", "1", "Integer")]
    [InlineData("Colors", "[clRed, clBlue]", "TColors")]
    [InlineData("clRed in Colors", "True", "Boolean")]
    [InlineData("Colors + [clGreen]", "[clRed, clGreen, clBlue]", "set of TColor")]
    [InlineData("Pet.Legs + Total", "28", "Integer")]
    [InlineData("Pet.Name", "'Rex'", "string")]
    [InlineData("PTotal^", "24", "Integer")]
    [InlineData("PTotal = @Total", "True", "Boolean")]
    [InlineData("Ok and (Ch = 'A')", "True", "Boolean")]
    public async Task WorkedExamples(string expression, string value, string typeName)
    {
        var result = await EvalBed.Demo().EvalAsync(expression);

        Assert.Equal(value, result.Value);
        Assert.Equal(typeName, result.TypeName);
        Assert.Equal(expression, result.Name);
    }

    [Theory]
    [InlineData("Nums[3]", DebugErrorCode.ExpressionEvaluation, "Index 3 out of bounds (0..2)")]
    [InlineData("Foo", DebugErrorCode.ExpressionEvaluation, "Undeclared identifier 'Foo'")]
    [InlineData("Total +", DebugErrorCode.ExpressionSyntax, "Expression expected at column 8")]
    public async Task WorkedExamples_Errors(string expression, DebugErrorCode code, string message)
    {
        var exception = await Assert.ThrowsAsync<DebuggerException>(() => EvalBed.Demo().EvalAsync(expression));

        Assert.Equal(code, exception.Code);
        Assert.Equal(message, exception.Message);
    }

    [Theory]
    // TypeName of an immediate: Integer if it fits 32 bits signed, else Int64 (UInt64 if flagged), Double, …
    [InlineData("2147483647", "2147483647 : Integer")]
    [InlineData("-2147483648", "-2147483648 : Integer")]
    [InlineData("2147483648", "2147483648 : Int64")]
    [InlineData("18446744073709551615", "18446744073709551615 : UInt64")]
    [InlineData("1.0", "1 : Double")]
    [InlineData("1e100", "1E100 : Double")]
    [InlineData("'x'", "'x' : Char")]
    [InlineData("''", "'' : string")]
    [InlineData("#13#10", "#13#10 : string")]
    // the format suffix: hexadecimal integers for this result, or decimal
    [InlineData("255,x", "$000000FF : Integer")]
    [InlineData("-1,h", "$FFFFFFFF : Integer")]
    [InlineData("4294967296,X", "$0000000100000000 : Int64")]
    [InlineData("B,x", "$C8 : Byte")]
    [InlineData("Ints,x", "($0000000A, $00000014, $0000001E, $00000028) : array[0..3] of Integer")]
    [InlineData("Str,x", "'abc' : string")]
    [InlineData("B,d", "200 : Byte")]
    public async Task Presentation_Immediates_And_FormatSuffix(string expression, string expected)
    {
        Assert.Equal(expected, await Zoo(expression));
    }

    [Fact]
    public async Task Presentation_DecimalSuffix_OverridesTheHexOption_LongStringsAreCut()
    {
        var hex = ZooBed.Test(new ValueFormatOptions { HexIntegers = true, MaxStringChars = 4 });

        Assert.Equal("$C8 : Byte", await hex.ResultAsync("B"));
        Assert.Equal("200 : Byte", await hex.ResultAsync("B,d"));
        Assert.Equal("$00000005 : Integer", await hex.ResultAsync("2 + 3"));
        // An immediate string follows the display limit like a string read from memory (§4.2).
        Assert.Equal("'abca'… (Length: 6) : string", await hex.ResultAsync("Str + Str"));
        var immediate = await hex.EvalAsync("2 + 3");
        Assert.Null(immediate.Children);
        Assert.Null(immediate.EvaluateName);
        Assert.Null(immediate.Address);
        Assert.False(immediate.CanModify);
    }
}
