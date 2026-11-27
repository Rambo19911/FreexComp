using FreexComp.Compiler.Emit;
using FreexComp.Compiler.Lowering;
using FreexComp.Compiler.Syntax;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Emit;

/// <summary>C-416 (CMP-1474): a type query is a constant in the IR and emits nothing for the type it names.</summary>
public sealed partial class IrSnapshotTests
{
    private const string TypeQueryModule = """
        unit M;
        interface
        type
          TEnum = (eA, eB);
          TRec = record S: string; N: Integer; end;
          TDyn = array of TRec;
          TSet = set of TEnum;
        function Q: Integer;
        implementation
        function Q: Integer;
        var
          B: Boolean;
          K: TTypeKind;
        begin
          B := IsManagedType(TRec);
          B := HasWeakRef(TDyn);
          B := IsConstValue(SizeOf(TSet));
          K := GetTypeKind(TEnum);
          Result := Ord(GetTypeKind(string)) + Ord(K) + Ord(B);
        {EXTRA}end;
        end.

        """;

    private static string EmitWithTypeKinds(string source)
    {
        var tree = SyntaxTree.Parse(SourceText.From(@"C:\p\M.pas", source));
        Assert.Empty(tree.Diagnostics);
        var result = BindWithStubSystem(tree, TypeKindSystemSource);
        Assert.Empty(Format(result.Diagnostics));
        var writer = new LlvmTextWriter();
        IrEmitter.EmitUnit(Lowerer.Lower(result), writer);
        return writer.GetText();
    }

    /// <summary>
    /// A module that only queries its types has no RTTI block for any of them and no trace of the queries: the statements store the
    /// constants. <c>TypeInfo</c> of the same type (the control) does emit the block.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1474")]
    public void TypeQueries_EmitNoRtti()
    {
        var text = EmitWithTypeKinds(TypeQueryModule.Replace("{EXTRA}", ""));

        Assert.DoesNotContain("$rtti", text, StringComparison.Ordinal);
        Assert.DoesNotContain("$names", text, StringComparison.Ordinal);
        Assert.DoesNotContain("IsManagedType", text, StringComparison.Ordinal);
        Assert.DoesNotContain("HasWeakRef", text, StringComparison.Ordinal);
        Assert.DoesNotContain("IsConstValue", text, StringComparison.Ordinal);
        Assert.DoesNotContain("GetTypeKind", text, StringComparison.Ordinal);
        Assert.DoesNotContain("call ", text, StringComparison.Ordinal);
        Assert.Contains(
            "  store i8 1, ptr %B.addr, align 1\n" +                              // IsManagedType(TRec)
            "  store i8 0, ptr %B.addr, align 1\n" +                              // HasWeakRef(TDyn)
            "  store i8 1, ptr %B.addr, align 1\n" +                              // IsConstValue(SizeOf(TSet))
            "  store i8 3, ptr %K.addr, align 1\n",                               // tkEnumeration
            text, StringComparison.Ordinal);

        var control = EmitWithTypeKinds(TypeQueryModule.Replace("{EXTRA}", "  if TypeInfo(TRec) = nil then Result := 0;\n"));
        Assert.Contains("@\"M.TRec.$rtti\" = linkonce_odr constant { i8, [7 x i8], ptr, i64, i64, ptr } { i8 12,", control, StringComparison.Ordinal);
    }
}
