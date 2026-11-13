using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Emit;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Emit;

/// <summary>Link names of codegen.md §12 (CMP-360…363) for the entities that exist in M2.1.</summary>
[Trait("Phase", "2")]
[Trait("Req", "CMP-360")]
public sealed class ManglerTests
{
    private static BindingResult BindOk(string text)
    {
        var (result, _) = Bind(text);
        Assert.Empty(Format(result.Diagnostics));
        return result;
    }

    private static IEnumerable<Symbol> Members(BindingResult result) => result.Unit.InterfaceMembers.Concat(result.Unit.ImplementationMembers);

    private static RoutineSymbol Routine(BindingResult result, string name) =>
        result.Routines.Select(b => b.Routine).Concat(Members(result).OfType<RoutineSymbol>()).First(r => r.Name == name);

    private const string UnitU = "unit U; interface procedure F; var Counter: Integer; implementation procedure F; begin end; end.";

    [Fact]
    public void GlobalRoutine_IsUnitDotName_Quoted()
    {
        var name = Mangler.Routine(Routine(BindOk(UnitU), "F"));

        Assert.Equal(new LinkName("U.F", AlwaysQuote: true), name);
    }

    [Fact]
    public void GlobalVariable_IsUnitDotName()
    {
        var counter = Members(BindOk(UnitU)).OfType<VariableSymbol>().Single();

        Assert.Equal(new LinkName("U.Counter", AlwaysQuote: true), Mangler.GlobalVariable(counter));
    }

    [Fact]
    public void UnitInitAndFinal()
    {
        var unit = BindOk(UnitU).Unit;

        Assert.Equal((new LinkName("U.$init", true), new LinkName("U.$final", true)), (Mangler.UnitInit(unit), Mangler.UnitFinal(unit)));
    }

    [Fact]
    public void Overloaded_AppendsTheSignature_WithModePrefixes()
    {
        var result = BindOk("unit U; interface function Max(a, b: Integer): Integer; overload; " +
            "procedure Put(const s: string; var b: Byte; out c: Char); overload; procedure Put; overload; implementation " +
            "function Max(a, b: Integer): Integer; begin Result := a end; procedure Put(const s: string; var b: Byte; out c: Char); begin end; " +
            "procedure Put; begin end; end.");

        Assert.Equal(
            ["U.Max$Integer,Integer", "U.Put$c:string,v:Byte,o:Char", "U.Put$"],
            result.Unit.InterfaceMembers.OfType<RoutineSymbol>().Select(r => Mangler.Routine(r).Text));
    }

    /// <summary>CMP-361: aliases resolve to the canonical built-in name.</summary>
    [Theory]
    [InlineData("LongInt", "Integer")]
    [InlineData("LongWord", "Cardinal")]
    [InlineData("Real", "Double")]
    [InlineData("Extended", "Double")]
    [InlineData("UnicodeString", "string")]
    [InlineData("WideChar", "Char")]
    [InlineData("PWideChar", "PChar")]
    [InlineData("TextFile", "Text")]
    [Trait("Req", "CMP-361")]
    public void Signature_UsesCanonicalBuiltInNames(string alias, string canonical)
    {
        var result = BindOk($"program T; procedure P(x: {alias}); overload; begin end; begin end.");

        Assert.Equal("T.P$" + canonical, Mangler.Routine(Routine(result, "P")).Text);
    }

    [Fact]
    public void NestedRoutine_IsParentLinkNameDotName()
    {
        var result = BindOk("program T; procedure Outer; procedure Inner; procedure Deep; begin end; begin Deep end; begin Inner end; begin end.");

        Assert.Equal(["T.Outer", "T.Outer.Inner", "T.Outer.Inner.Deep"], result.Routines.Select(b => Mangler.Routine(b.Routine).Text));
    }

    [Fact]
    public void FrameRecordType_IsRoutineLinkNameDotFrame()
    {
        var result = BindOk("program T; procedure Outer; var n: Integer; procedure Inner; begin n := 1 end; begin Inner end; begin end.");

        Assert.Equal(new LinkName("T.Outer.$frame", true), Mangler.FrameType(Routine(result, "Outer")));
    }

    [Fact]
    public void External_IsTheImportName_NotForcedToBeQuoted()
    {
        var result = BindOk("program T; procedure Sleep(ms: Cardinal); stdcall; external 'kernel32.dll'; " +
            "function Tick: Cardinal; external 'kernel32' name 'GetTickCount'; begin Sleep(Tick) end.");

        Assert.Equal(new LinkName("Sleep", AlwaysQuote: false), Mangler.Routine(Routine(result, "Sleep")));
        Assert.Equal(new LinkName("GetTickCount", AlwaysQuote: false), Mangler.Routine(Routine(result, "Tick")));
    }

    [Fact]
    public void PublicName_IsTheGivenName()
    {
        var result = BindOk("program T; procedure P; public name 'memset'; begin end; begin end.");

        Assert.Equal(new LinkName("memset", AlwaysQuote: false), Mangler.Routine(Routine(result, "P")));
    }

    [Fact]
    public void StringLiteral_IsPrivateDotStrN() =>
        Assert.Equal((new LinkName(".str.0", false), new LinkName(".str.12", false)), (Mangler.StringLiteral(0), Mangler.StringLiteral(12)));

    /// <summary>The symbol's own <see cref="RoutineSymbol.LinkName"/> is the mangler's text.</summary>
    [Fact]
    public void RoutineSymbolLinkName_IsTheManglersText()
    {
        var result = BindOk("program T; procedure P(x: LongInt); overload; begin end; procedure Q; external 'k.dll' name 'Z'; begin end.");

        Assert.All(Members(result).OfType<RoutineSymbol>(), r => Assert.Equal(Mangler.Routine(r).Text, r.LinkName));
    }

    /// <summary>CMP-363: the declaration's casing, never a use site's, for routines, variables, units and type names.</summary>
    [Fact]
    [Trait("Req", "CMP-363")]
    public void DeclaredCasing_NotUseSiteCasing()
    {
        var result = BindOk("program MyProg; var Total: integer; procedure DoIt(n: INTEGER); overload; begin TOTAL := n end; " +
            "begin DOIT(1); doit(2) end.");

        var calls = result.Bodies[0].Statements.Cast<BoundCallStatement>().Select(c => Mangler.Routine(c.Routine).Text);
        Assert.Equal(["MyProg.DoIt$Integer", "MyProg.DoIt$Integer"], calls);
        var target = Assert.IsType<BoundAssignmentStatement>(Assert.Single(result.Routines).Body.Statements[0]).Target;
        Assert.Equal("MyProg.Total", Mangler.GlobalVariable(Assert.IsType<BoundVariableExpression>(target).Variable).Text);
    }

    [Fact]
    [Trait("Req", "CMP-361")]
    public void TypeName_BuiltInsAreCanonical()
    {
        var types = new BuiltInTypes();

        Assert.Equal(["Integer", "string", "Double", "Pointer", "AnsiString", "ShortString"],
            new[] { types.Integer, types.String, types.Double, types.Pointer, types.AnsiString, types.ShortString }.Select(Mangler.TypeName));
    }
}
