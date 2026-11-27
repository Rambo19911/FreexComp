using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>C-384: the inferred type of <c>var X := e</c> and the initialisers that cannot be inferred (inline-variables.md LANG-573, 574).</summary>
public sealed partial class VarBindingTests
{
    private const string ObjDeclarations =
        "type TObj = class procedure Proc; function Fn(A: Integer): Integer; function Count: Integer; end; "
        + "procedure TObj.Proc; begin end; function TObj.Fn(A: Integer): Integer; begin Result := A end; "
        + "function TObj.Count: Integer; begin Result := 3 end; var Obj: TObj;";

    /// <summary>
    /// The type of <c>X</c> in <c>var X := <paramref name="initializer"/>;</c> (line 5, column 7) inside a procedure after the one-line
    /// <paramref name="declarations"/>, compiled against the real <c>System</c>, and the errors (hints and warnings left out).
    /// </summary>
    private static (string? Type, string[] Errors) Infer(string declarations, string initializer)
    {
        var program = $"program P;\n{declarations}\nprocedure Run;\nbegin\n  var X := {initializer};\nend;\nbegin\n  Run;\nend.";
        var fs = new InMemoryFileSystem().Add(@"C:\p\P.pas", program).AddSystem();
        var options = new CompilationOptions { MainFile = @"C:\p\P.pas", RtlDirectory = @"C:\rtl", ObjectDirectory = @"C:\p\obj" };
        var model = Compilation.Create(options, [], fs).GetSemanticModel(@"C:\p\P.pas");
        var declaration = model.SyntaxTree.Root.DescendantNodes().OfType<InlineVarStatementSyntax>().Single();
        var symbol = model.GetDeclaredSymbol(declaration.Identifiers[0].Identifier) as VariableSymbol;
        return (symbol?.Type.Name, [.. Format(model.GetDiagnostics()).Where(d => d.StartsWith('E'))]);
    }

    private static void AssertInfers(string declarations, string initializer, string expected)
    {
        var (type, errors) = Infer(declarations, initializer);
        Assert.Empty(errors);
        Assert.Equal(expected, type);
    }

    /// <summary>LANG-573 rule 4: a subrange-typed initialiser gives the subrange's base type; a named small integer type stays.</summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-573")]
    [InlineData("type TDigit = 0..9; var D: TDigit;", "D", "Integer")]
    [InlineData("type TDigit = 0..9;", "Low(TDigit)", "Integer")]
    [InlineData("type TSmall = -5..5; var S: TSmall;", "S", "Integer")]
    [InlineData("type TBig = 0..4000000000; var B: TBig;", "B", "Cardinal")]
    [InlineData("type THuge = 0..5000000000; var H: THuge;", "H", "UInt64")]           // stored as UInt64 (LANG-212), computed as it
    [InlineData("type TColor = (Red, Green, Blue); TWarm = Red..Green; var W: TWarm;", "W", "TColor")]
    [InlineData("type TLower = 'a'..'z'; var L: TLower;", "L", "Char")]
    [InlineData("var Bt: Byte;", "Bt", "Byte")]
    [InlineData("var W: Word;", "W", "Word")]
    public void InlineInference_SubrangeWidensToBase(string declarations, string initializer, string expected) =>
        AssertInfers(declarations, initializer, expected);

    /// <summary>LANG-573 rules 1 and 3: a constant expression with an explicit type keeps it; untyped integer constants follow the literal rule.</summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-573")]
    [InlineData("", "Byte(5)", "Byte")]
    [InlineData("", "Int64(5)", "Int64")]
    [InlineData("", "Word(70000 - 69999)", "Word")]
    [InlineData("var U: UInt64;", "High(U)", "UInt64")]
    [InlineData("const N = 10;", "N", "Integer")]
    [InlineData("const N = 10;", "N * 2 + 1", "Integer")]
    [InlineData("const B = Byte(7);", "B", "Byte")]
    [InlineData("", "5", "Integer")]
    [InlineData("", "3000000000", "Int64")]
    [InlineData("", "18446744073709551615", "UInt64")]
    [InlineData("type TColor = (Red, Green, Blue);", "Green", "TColor")]
    [InlineData("", "'x'", "Char")]
    [InlineData("", "'text'", "string")]
    [InlineData("", "2.5", "Double")]
    [InlineData("", "True", "Boolean")]
    public void InlineInference_TypedConstantKeepsItsType(string declarations, string initializer, string expected) =>
        AssertInfers(declarations, initializer, expected);

    /// <summary>
    /// LANG-574: <c>nil</c>, set and array constructors, anonymous-method literals and routine names that are not called are E2075 at
    /// the identifier, and the declaration is skipped: the local has the error type, so its uses report nothing further.
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-574")]
    [InlineData("", "nil")]
    [InlineData("", "[]")]
    [InlineData("", "[1, 2]")]
    [InlineData("", "procedure begin end")]
    [InlineData("", "function: Integer begin Result := 1 end")]
    [InlineData("procedure SomeProc; begin end;", "SomeProc")]
    [InlineData(ObjDeclarations, "Obj.Proc")]
    [InlineData("function NeedsArg(A: Integer): Integer; begin Result := A end;", "NeedsArg")]
    [InlineData(ObjDeclarations, "Obj.Fn")]
    [InlineData("function Ov(A: Integer): Integer; overload; begin Result := A end; function Ov(const S: string): Integer; overload; begin Result := 0 end;", "Ov")]
    public void InlineInference_NotInferable_E2075(string declarations, string initializer)
    {
        var (type, errors) = Infer(declarations, initializer);
        Assert.Equal(["E2075(5,7): Cannot infer the type of 'X'"], errors);
        Assert.Equal("<error>", type);
    }

    /// <summary>LANG-574 rule 3, last sentence: a routine callable without arguments is a call and infers its result type; <c>@Routine</c> is a pointer.</summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-574")]
    [InlineData("function Five: Integer; begin Result := 5 end;", "Five", "Integer")]
    [InlineData("function Name: string; begin Result := 'n' end;", "Name", "string")]
    [InlineData(ObjDeclarations, "Obj.Count", "Integer")]
    [InlineData("function Ov(A: Integer): Integer; overload; begin Result := A end; function Ov: Int64; overload; begin Result := 0 end;", "Ov", "Int64")]
    [InlineData("procedure SomeProc; begin end;", "@SomeProc", "Pointer")]
    public void InlineInference_ParameterlessFunctionIsCalled(string declarations, string initializer, string expected) =>
        AssertInfers(declarations, initializer, expected);
}
