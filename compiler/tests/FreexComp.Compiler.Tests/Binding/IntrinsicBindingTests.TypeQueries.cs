using System.Numerics;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>
/// C-416: the type queries <c>IsManagedType</c>, <c>HasWeakRef</c>, <c>GetTypeKind</c> and <c>IsConstValue</c> (intrinsics.md
/// LANG-972…975) — folded to constants in constant declarations and in bodies alike.
/// </summary>
public sealed partial class IntrinsicBindingTests
{
    private const string QueryTypes = """
        program T;
        type
          TEnum = (eA, eB, eC);
          TEnumSub = eA..eB;
          TIntSub = 1..10;
          TBigSub = 0..$FFFFFFFFFF;
          TSmall64 = Int64(1)..Int64(10);
          TIntHelper = record helper for Integer end;
          TWideSub = 'a'..'z';
          TAnsiSub = AnsiChar('a')..AnsiChar('z');
          TSet = set of TEnum;
          TByteSet = set of Byte;
          TCls = class end;
          TClsRef = class of TCls;
          TMeth = procedure(Sender: TCls) of object;
          TProc = procedure;
          TFunc = function(X: Integer): Integer;
          TRef = reference to procedure;
          IIntf = interface end;
          TShort = string[10];
          TRec = record A: Integer; B: TCls; P: Pointer; end;
          TRecS = record A: Integer; S: string; end;
          TRecDeep = record A: Integer; R: array[0..1] of TRecS; end;
          TRecI = record I: IIntf; end;
          TRecShort = record S: TShort; end;
          TArr = array[0..3] of Integer;
          TArrS = array[0..1] of string;
          TArrRec = array[0..1, 0..1] of TRecS;
          TDyn = array of Integer;
          PInt = ^Integer;
          PRec = ^TRecS;
          TBox<X> = record V: X; end;
          TOuter = class
          public
            type
              TInner = record S: string; end;
              TNestedEnum = (nA, nB);
          end;
          TMyInt = type Integer;
          TMyStr = type string;

        """;

    /// <summary>
    /// LANG-973, 974: one row per line of the two value tables — the type, <c>IsManagedType</c>, <c>HasWeakRef</c> and the ordinal of
    /// <c>GetTypeKind</c> — as a constant declaration and as a statement (the same constants).
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-972")]
    [Trait("Req", "LANG-973")]
    [Trait("Req", "LANG-974")]
    [InlineData("Integer", false, false, 1)]                    // integer types up to 32 bits
    [InlineData("ShortInt", false, false, 1)]
    [InlineData("SmallInt", false, false, 1)]
    [InlineData("LongInt", false, false, 1)]
    [InlineData("Byte", false, false, 1)]
    [InlineData("Word", false, false, 1)]
    [InlineData("Cardinal", false, false, 1)]
    [InlineData("LongWord", false, false, 1)]
    [InlineData("TIntSub", false, false, 1)]
    [InlineData("TMyInt", false, false, 1)]
    [InlineData("TSmall64", false, false, 1)]                   // the host of a subrange is the smallest type holding its bounds
    [InlineData("AnsiChar", false, false, 2)]
    [InlineData("TAnsiSub", false, false, 2)]
    [InlineData("TEnum", false, false, 3)]
    [InlineData("TEnumSub", false, false, 3)]
    [InlineData("TOuter.TNestedEnum", false, false, 3)]
    [InlineData("Boolean", false, false, 3)]
    [InlineData("ByteBool", false, false, 3)]
    [InlineData("WordBool", false, false, 3)]
    [InlineData("LongBool", false, false, 3)]
    [InlineData("Single", false, false, 4)]
    [InlineData("Double", false, false, 4)]
    [InlineData("Real", false, false, 4)]
    [InlineData("Extended", false, false, 4)]
    [InlineData("Currency", false, false, 4)]
    [InlineData("TSet", false, false, 5)]
    [InlineData("TByteSet", false, false, 5)]
    [InlineData("TCls", false, false, 6)]
    [InlineData("TOuter", false, false, 6)]
    [InlineData("TMeth", false, false, 7)]
    [InlineData("Char", false, false, 8)]
    [InlineData("WideChar", false, false, 8)]
    [InlineData("TWideSub", false, false, 8)]
    [InlineData("string", true, false, 9)]
    [InlineData("UnicodeString", true, false, 9)]
    [InlineData("TMyStr", true, false, 9)]
    [InlineData("AnsiString", true, false, 10)]
    [InlineData("UTF8String", true, false, 10)]
    [InlineData("RawByteString", true, false, 10)]
    [InlineData("ShortString", false, false, 11)]
    [InlineData("TShort", false, false, 11)]
    [InlineData("TRec", false, false, 12)]
    [InlineData("TRecS", true, false, 12)]
    [InlineData("TRecDeep", true, false, 12)]
    [InlineData("TRecI", true, false, 12)]
    [InlineData("TRecShort", false, false, 12)]
    [InlineData("TBox<Integer>", false, false, 12)]
    [InlineData("TBox<string>", true, false, 12)]
    [InlineData("TOuter.TInner", true, false, 12)]
    [InlineData("T.TRecS", true, false, 12)]
    [InlineData("TArr", false, false, 13)]
    [InlineData("TArrS", true, false, 13)]
    [InlineData("TArrRec", true, false, 13)]
    [InlineData("TDyn", true, false, 14)]
    [InlineData("IIntf", true, false, 15)]
    [InlineData("TRef", true, false, 15)]                       // a method reference is an interface (ABI-097)
    [InlineData("Int64", false, false, 16)]
    [InlineData("UInt64", false, false, 16)]
    [InlineData("NativeInt", false, false, 16)]                 // 64 bits on x64
    [InlineData("NativeUInt", false, false, 16)]
    [InlineData("TBigSub", false, false, 16)]                   // bounds that need 64 bits
    [InlineData("Pointer", false, false, 17)]
    [InlineData("PInt", false, false, 17)]
    [InlineData("PRec", false, false, 17)]
    [InlineData("PChar", false, false, 17)]
    [InlineData("PAnsiChar", false, false, 17)]
    [InlineData("TClsRef", false, false, 18)]
    [InlineData("TProc", false, false, 19)]
    [InlineData("TFunc", false, false, 19)]
    [InlineData("Text", false, false, 0)]
    [InlineData("TIntHelper", false, false, 0)]                 // a helper has no kind of its own
    public void TypeQueries_Values(string type, bool managed, bool weak, int kind)
    {
        var result = BindWithTypeKinds(QueryTypes + $"""
            const
              CM = IsManagedType({type});
              CW = HasWeakRef({type});
              CK = GetTypeKind({type});
            var
              M, W: Boolean;
              K: TTypeKind;
            begin
              M := IsManagedType({type});
              W := HasWeakRef({type});
              K := GetTypeKind({type});
            end.
            """);

        Assert.Equal([], Format(result.Diagnostics).Where(d => !d.StartsWith('H')));
        ConstantSymbol Constant(string name) => Assert.IsType<ConstantSymbol>(result.Scope.Lookup(name));
        Assert.Equal((managed, "Boolean"), ((bool)Constant("CM").Value!.Value!, Constant("CM").Type.Name));
        Assert.Equal((weak, "Boolean"), ((bool)Constant("CW").Value!.Value!, Constant("CW").Type.Name));
        Assert.Equal((new BigInteger(kind), "TTypeKind"), ((BigInteger)Constant("CK").Value!.Value!, Constant("CK").Type.Name));
        Assert.Equal("System", Constant("CK").Type.ContainingUnit!.Name);
        Assert.Equal(
            $"(block (:= M:Boolean {managed}:Boolean) (:= W:Boolean {weak}:Boolean) (:= K:TTypeKind {kind}:TTypeKind))",
            BoundDump.Of(Assert.Single(result.Bodies)));
    }

    /// <summary>LANG-974: the pointer-sized integers of a 32-bit target are 32 bits wide, so their kind is <c>tkInteger</c> there.</summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-974")]
    [InlineData("NativeInt", 1)]
    [InlineData("NativeUInt", 1)]
    [InlineData("Int64", 16)]
    public void TypeQueries_NativeIntegers_OnA32BitTarget(string type, int kind)
    {
        var types = new BuiltInTypes(Targets.TargetTestSupport.Fake32);
        var system = Binder.BindCompilationUnit(SyntaxTree.Parse(SourceText.From(@"C:\p\System.pas", TypeKindSystemSource)), types);
        var result = Binder.BindCompilationUnit(
            SyntaxTree.Parse(SourceText.From(TestPath, $"program T; const CK = GetTypeKind({type}); begin end.")), types, system);

        Assert.Empty(Format(result.Diagnostics));
        Assert.Equal(new BigInteger(kind), Assert.IsType<ConstantSymbol>(result.Scope.Lookup("CK")).Value!.Value);
    }

    /// <summary>
    /// LANG-972: one argument, a type for the first three; an intrinsic is no procedural value — its address is E2910, its bare name
    /// is a call without arguments (E2110) whatever the target, as for every function-like intrinsic.
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-972")]
    [InlineData("f := IsManagedType(i)", "E2002(1,202): 'i' is not a type identifier")]
    [InlineData("f := HasWeakRef(i + 1)", "E2002(1,199): 'i + 1' is not a type identifier")]
    [InlineData("i := Ord(GetTypeKind(s))", "E2002(1,204): 's' is not a type identifier")]
    [InlineData("f := IsManagedType(5)", "E2002(1,202): '5' is not a type identifier")]
    [InlineData("f := IsManagedType(Nope)", "E2003(1,202): Undeclared identifier 'Nope'")]
    [InlineData("f := IsManagedType()", "E2110(1,188): Not enough actual parameters for 'IsManagedType'")]
    [InlineData("f := HasWeakRef", "E2110(1,188): Not enough actual parameters for 'HasWeakRef'")]
    [InlineData("f := IsConstValue()", "E2110(1,188): Not enough actual parameters for 'IsConstValue'")]
    [InlineData("f := IsManagedType(Integer, Byte)", "E2111(1,211): Too many actual parameters for 'IsManagedType'")]
    [InlineData("i := Ord(GetTypeKind(Integer, Byte))", "E2111(1,213): Too many actual parameters for 'GetTypeKind'")]
    [InlineData("f := IsConstValue(1, 2)", "E2111(1,204): Too many actual parameters for 'IsConstValue'")]
    [InlineData("h := IsManagedType", "E2110(1,188): Not enough actual parameters for 'IsManagedType'")]   // a procedural target: no value either
    [InlineData("p := @IsManagedType", "E2910(1,189): Intrinsic routine 'IsManagedType' cannot be used as a procedural value")]
    [InlineData("p := Addr(GetTypeKind)", "E2910(1,193): Intrinsic routine 'GetTypeKind' cannot be used as a procedural value")]
    [InlineData("p := @IsConstValue", "E2910(1,189): Intrinsic routine 'IsConstValue' cannot be used as a procedural value")]
    public void TypeQueries_OperandErrors(string statement, string expected)
    {
        var result = BindWithTypeKinds("program T; type TQuery = function: Boolean; " + Vars + "p: Pointer; h: TQuery; begin " + statement + " end.");

        Assert.Equal([expected], Format(result.Diagnostics).Where(d => !d.StartsWith('H')));
    }

    /// <summary>LANG-972: the same operand rules in a constant declaration (the constant folder).</summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-972")]
    [InlineData("IsManagedType(i)", "E2002(1,145): 'i' is not a type identifier")]
    [InlineData("GetTypeKind(1 + 2)", "E2002(1,143): '1 + 2' is not a type identifier")]
    [InlineData("HasWeakRef(Nope)", "E2003(1,142): Undeclared identifier 'Nope'")]
    [InlineData("IsManagedType()", "E2110(1,131): Not enough actual parameters for 'IsManagedType'")]
    [InlineData("IsConstValue", "E2110(1,131): Not enough actual parameters for 'IsConstValue'")]
    [InlineData("GetTypeKind(Integer, Byte)", "E2111(1,152): Too many actual parameters for 'GetTypeKind'")]
    public void TypeQueries_OperandErrors_InConstants(string expression, string expected)
    {
        var result = BindWithTypeKinds("program T; " + Vars + "const K = " + expression + "; begin end.");

        Assert.Equal([expected], Format(result.Diagnostics));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-972")]
    public void TypeQueries_UserDeclarationHidesTheIntrinsic()
    {
        var result = BindWithTypeKinds(
            "program T; var f: Boolean; i: Integer; function IsManagedType(X: Integer): Boolean; begin Result := X > 0 end; "
            + "begin f := IsManagedType(i) end.");

        Assert.Empty(Format(result.Diagnostics));
        Assert.Equal("(block (:= f:Boolean (call IsManagedType:Boolean i:Integer)))", BoundDump.Of(Assert.Single(result.Bodies)));
    }

    /// <summary>LANG-974: the result type is <c>System.TTypeKind</c>; a <c>System</c> without it (and a namesake elsewhere) is E2003.</summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-974")]
    [InlineData("program T; var i: Integer; begin i := Ord(GetTypeKind(Integer)) end.", "E2003(1,43): Undeclared identifier 'TTypeKind'")]
    [InlineData("program T; const K = GetTypeKind(Integer); begin end.", "E2003(1,22): Undeclared identifier 'TTypeKind'")]
    [InlineData("program T; type TTypeKind = (tkA, tkB); const K = GetTypeKind(Integer); begin end.", "E2003(1,51): Undeclared identifier 'TTypeKind'")]
    public void GetTypeKind_WithoutTTypeKind_E2003(string program, string expected) =>
        Assert.Equal([expected], BindDiagnostics(program).Where(d => !d.StartsWith('H')));

    private const string ConstValueProgram = """
        program T;
        type
          TEnum = (eA, eB);
          TRec = record A: Integer; end;
        const
          C = 5;
          CS = 'abc';
          TC: Integer = 7;
        var
          V: Integer;
          R: TRec;
          B: Boolean;
        function F(X: Integer): Integer;
        begin
          Result := X;
        end;
        function G: Integer;
        begin
          Result := 1;
        end;
        {DECL}
        procedure Q(A: Integer; const S: string); inline;
        begin
          B := IsConstValue({X});
        end;
        begin
          Q(1, 'x');
        end.
        """;

    /// <summary>
    /// LANG-975: <c>True</c> exactly for constant expressions; variables, fields, typed constants, calls and every parameter (also of
    /// an <c>inline</c> routine) are <c>False</c>; a type name is E2009. Rows without a parameter are checked as a constant
    /// declaration too.
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-975")]
    [InlineData("5", "True")]
    [InlineData("-5 + 2 * 3", "True")]
    [InlineData("1.5", "True")]
    [InlineData("'abc'", "True")]
    [InlineData("C", "True")]
    [InlineData("CS", "True")]
    [InlineData("C + 1", "True")]
    [InlineData("SizeOf(Integer)", "True")]
    [InlineData("High(Byte)", "True")]
    [InlineData("eB", "True")]
    [InlineData("TEnum.eA", "True")]
    [InlineData("nil", "True")]
    [InlineData("True", "True")]
    [InlineData("[1, 2]", "True")]
    [InlineData("Ord('a')", "True")]
    [InlineData("IsManagedType(string)", "True")]
    [InlineData("GetTypeKind(Integer)", "True")]
    [InlineData("IsConstValue(V)", "True")]
    [InlineData("V", "False")]
    [InlineData("V + 1", "False")]
    [InlineData("R.A", "False")]
    [InlineData("TC", "False")]
    [InlineData("F(1)", "False")]
    [InlineData("G", "False")]
    [InlineData("SizeOf(V) + V", "False")]
    [InlineData("A", "False")]
    [InlineData("S", "False")]
    [InlineData("A + 1", "False")]
    [InlineData("Integer", "E2009: Variable or constant expected but type 'Integer' found")]
    [InlineData("string", "E2009: Variable or constant expected but type 'string' found")]
    [InlineData("TRec", "E2009: Variable or constant expected but type 'TRec' found")]
    [InlineData("Nope", "E2003: Undeclared identifier 'Nope'")]
    [InlineData("V + Nope", "E2003: Undeclared identifier 'Nope'")]
    public void IsConstValue_Rows(string expression, string expected)
    {
        static string[] Codes(BindingResult result) =>
            [.. result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Code}: {d.Message}")];

        var inBody = BindWithTypeKinds(ConstValueProgram.Replace("{DECL}", "").Replace("{X}", expression));
        var isError = expected.StartsWith('E');
        Assert.Equal(isError ? [expected] : [], Codes(inBody));
        if (!isError)
        {
            // the operand is never evaluated: the statement assigns the constant, with no call and no read left
            var body = inBody.Routines.Single(r => r.Routine.Name == "Q").Body;
            Assert.Equal($"(block (:= B:Boolean {expected}:Boolean))", BoundDump.Of(body));
        }

        if (expression is "A" or "S" or "A + 1")
        {
            return;                                                             // parameters exist in the routine only
        }

        var asConstant = BindWithTypeKinds(ConstValueProgram.Replace("{DECL}", $"const K = IsConstValue({expression});").Replace("{X}", "0"));
        Assert.Equal(isError ? [expected] : [], Codes(asConstant));
        if (!isError)
        {
            var constant = Assert.IsType<ConstantSymbol>(asConstant.Scope.Lookup("K"));
            Assert.Equal((expected, "Boolean"), (constant.Value!.ToDisplayString(), constant.Type.Name));
        }
    }

    /// <summary>
    /// LANG-975 (LANG-903): the operand is bound and never evaluated — a <c>var</c> parameter (which no anonymous method may capture,
    /// E2720) and an outer local named only there are not captured, and no closure field is made for them.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-975")]
    public void IsConstValue_OperandIsNotCaptured()
    {
        var result = BindWithStubSystem(SyntaxTree.Parse(SourceText.From(TestPath, """
            program T;
            type
              TProc = reference to procedure;
            var
              B: Boolean;
            procedure Q(var X: Integer);
            var
              L: Integer;
              R: TProc;
            begin
              L := 1;
              R := procedure
                begin
                  B := IsConstValue(X) or IsConstValue(L + 1);
                end;
              R();
            end;
            begin
            end.
            """)), AnonymousMethodBindingTests.SystemSource);

        Assert.Equal([], Format(result.Diagnostics).Where(d => !d.StartsWith('H')));
        Assert.All(result.Routines, r => Assert.Empty(r.CapturedVariables));
        var anonymous = result.Routines.Single(r => r.Routine.Name == "Q").AnonymousBodies.Single();
        Assert.Equal("(block (:= B:Boolean False:Boolean))", BoundDump.Of(anonymous.Body));
    }
}
