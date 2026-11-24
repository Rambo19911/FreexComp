using FreexComp.Compiler.Emit;
using FreexComp.Compiler.Lowering;
using FreexComp.Compiler.Tests.Binding;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Emit;

/// <summary>
/// C-304 (com-interfaces.md CMP-1209, rtl-abi.md ABI-205): the constant call descriptor of a dispatch call —
/// <c>&lt;{ i32 DispId, i16 Flags, i16 ResultType, i16 ArgCount, [ArgCount x i16] ArgTypes }&gt;</c>, alignment 4.
/// </summary>
[Trait("Phase", "8")]
public sealed class DispCallDescriptorTests
{
    private static string Emit(string source)
    {
        var result = DispInterfaceBinderTests.BindWithDispatch(source);
        Assert.Empty(Format(result.Diagnostics));
        var writer = new LlvmTextWriter();
        IrEmitter.EmitUnit(Lowerer.Lower(result), writer);
        return writer.GetText();
    }

    private static string[] Descriptors(string ir) =>
        [.. ir.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith("@\"M.$disp.", StringComparison.Ordinal))];

    /// <summary>
    /// Every type of LANG-695 as a by-value argument, a <c>var</c> argument (code + <c>$4000</c>), a function result and a property
    /// (get: flags 2 with the result code; put: flags 4, or 8 for an interface type, with the value as the only argument).
    /// </summary>
    [Theory]
    [Trait("Req", "CMP-1209")]
    [Trait("Req", "ABI-205")]
    [Trait("Req", "LANG-695")]
    [InlineData("SmallInt", 2)]
    [InlineData("Integer", 3)]
    [InlineData("TColor", 3)]
    [InlineData("Single", 4)]
    [InlineData("Double", 5)]
    [InlineData("Currency", 6)]
    [InlineData("TDateTime", 7)]
    [InlineData("Boolean", 0x20B)]
    [InlineData("WordBool", 11)]
    [InlineData("IDispatch", 9)]
    [InlineData("IChild", 9)]
    [InlineData("IOtherDisp", 9)]
    [InlineData("IInterface", 13)]
    [InlineData("IPlain", 13)]
    [InlineData("ShortInt", 16)]
    [InlineData("Byte", 17)]
    [InlineData("Word", 18)]
    [InlineData("Cardinal", 19)]
    [InlineData("Int64", 20)]
    [InlineData("UInt64", 21)]
    [InlineData("string", 0x100)]
    public void EncodingPerType(string type, int code)
    {
        var ir = Emit($$"""
            unit M;
            interface
            type
              TColor = (Red, Green, Blue);
              IPlain = interface procedure P; end;
              IChild = interface(IDispatch) end;
              IOtherDisp = dispinterface end;
              IDisp = dispinterface
                procedure Take(A: {{type}}; var B: {{type}}); dispid 9;
                function Give: {{type}}; dispid -10;
                property Prop: {{type}} dispid 11;
              end;
            procedure Run(const D: IDisp);
            implementation
            procedure Run(const D: IDisp);
            var
              V: {{type}};
            begin
              D.Take(V, V);
              V := D.Give;
              V := D.Prop;
              D.Prop := V;
            end;
            end.
            """);

        var put = type is "IDispatch" or "IChild" or "IOtherDisp" or "IInterface" or "IPlain" ? 8 : 4;
        Assert.Equal(
            [
                $"@\"M.$disp.1\" = private unnamed_addr constant <{{ i32, i16, i16, i16, [2 x i16] }}> <{{ i32 9, i16 1, i16 0, i16 2, [2 x i16] [i16 {code}, i16 {code | 0x4000}] }}>, align 4",
                $"@\"M.$disp.2\" = private unnamed_addr constant <{{ i32, i16, i16, i16 }}> <{{ i32 -10, i16 1, i16 {code}, i16 0 }}>, align 4",
                $"@\"M.$disp.3\" = private unnamed_addr constant <{{ i32, i16, i16, i16 }}> <{{ i32 11, i16 2, i16 {code}, i16 0 }}>, align 4",
                $"@\"M.$disp.4\" = private unnamed_addr constant <{{ i32, i16, i16, i16, [1 x i16] }}> <{{ i32 11, i16 {put}, i16 0, i16 1, [1 x i16] [i16 {code}] }}>, align 4",
            ],
            Descriptors(ir));
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(ir, """(call|invoke) void @"System\._DispCall"\(""").Count);
    }

    /// <summary>CMP-1209: identical descriptors are one constant; the helper is declared only by a module with a dispatch call (CMP-1206).</summary>
    [Fact]
    [Trait("Req", "CMP-1209")]
    [Trait("Req", "CMP-1206")]
    public void IdenticalDescriptorsShareOneConstant_HelperOnlyWhenUsed()
    {
        const string Types = """
            unit M;
            interface
            type
              IDisp = dispinterface
                procedure Ping(N: Integer); dispid 1;
                property Count: Integer dispid 2;
              end;
            procedure Run(const D: IDisp);
            implementation

            """;
        var ir = Emit(Types + "procedure Run(const D: IDisp); begin D.Ping(1); D.Ping(2); D.Count := D.Count; D.Ping(D.Count) end; end.");
        Assert.Equal(3, Descriptors(ir).Length);
        Assert.Contains("declare void @\"System._DispCall\"(ptr, ptr, ptr, ptr)", ir, StringComparison.Ordinal);

        var unused = Emit(Types + "procedure Run(const D: IDisp); begin end; end.");
        Assert.Empty(Descriptors(unused));
        Assert.DoesNotContain("_DispCall", unused, StringComparison.Ordinal);
        Assert.DoesNotContain("IDisp.$imt", unused, StringComparison.Ordinal);      // ABI-204: no method table
    }
}
