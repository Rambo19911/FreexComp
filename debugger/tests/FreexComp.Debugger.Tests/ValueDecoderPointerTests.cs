using FreexComp.Debugger.Tests.TestSupport;
using T = FreexComp.Debugger.Tests.TestSupport.ValueTestBed;

namespace FreexComp.Debugger.Tests;

/// <summary>Pointers, class references, interfaces, procedural values and opaque types (Pascal values §7.2: DBG-171 … DBG-173).</summary>
public sealed class ValueDecoderPointerTests
{
    private const ulong Target = 0xA3C01FF7B4;

    private static string Describe(VariableInfo info) =>
        $"{info.Name} = {info.Value} : {info.TypeName} {info.Kind} children={info.ChildCount} modify={info.CanModify} eval={info.EvaluateName} at={info.Address:X}";

    [Fact]
    [Trait("Req", "DBG-172")]
    public async Task TypedPointer_AddressAndTargetChild()
    {
        var bed = new T();
        bed.Memory.PutPointer(T.At, Target).PutInt32(Target, 24);

        var node = await bed.DecodeAsync(T.PInteger, T.At, "PTotal");

        var info = bed.Info(node);
        Assert.Equal("PTotal = $000000A3C01FF7B4 : ^Integer Pointer children=1 modify=True eval=PTotal at=20000", Describe(info));
        Assert.Equal(new VariableHandle(1, 1), info.Children);
        // The target is not read until the child is asked for.
        Assert.Equal(["20000+8"], bed.Memory.Reads);
        var child = Assert.Single(await bed.ChildrenAsync(node));
        Assert.Equal("^ = 24 : Integer Integer children=0 modify=True eval=PTotal^ at=A3C01FF7B4", Describe(bed.Info(child)));
    }

    [Fact]
    [Trait("Req", "DBG-172")]
    public async Task TypedPointer_Nil_And_UnreadableTarget()
    {
        var bed = new T();
        bed.Memory.PutPointer(T.At, 0);
        var nil = await bed.DecodeAsync(T.PInteger, T.At, "P");
        Assert.Equal(new VariableInfo("P", "nil", "^Integer", ValueKind.Pointer, null, 0, true, "P", T.At), bed.Info(nil));
        Assert.Empty(await bed.ChildrenAsync(nil));

        // A dangling pointer still shows its address; its target is an error child.
        bed = new T();
        bed.Memory.PutPointer(T.At, Target);
        var dangling = await bed.DecodeAsync(T.PInteger, T.At, "P");
        Assert.Equal("$000000A3C01FF7B4", bed.Format(dangling));
        var child = Assert.Single(await bed.ChildrenAsync(dangling));
        Assert.Equal(
            new VariableInfo("^", "<unreadable memory at $000000A3C01FF7B4>", "Integer", ValueKind.Error, null, 0, false, null, Target),
            bed.Info(child));
    }

    [Fact]
    [Trait("Req", "DBG-172")]
    public async Task TypedPointer_ToRecordAndToPointer()
    {
        var bed = new T();
        bed.Memory.PutPointer(T.At, Target).PutInt32(Target, 3).PutInt32(Target + 4, 4);

        var node = await bed.DecodeAsync(T.PPoint, T.At, "PP");

        Assert.Equal("PP = $000000A3C01FF7B4 : PPoint Pointer children=1 modify=True eval=PP at=20000", Describe(bed.Info(node)));
        var record = Assert.Single(await bed.ChildrenAsync(node));
        Assert.Equal("^ = (X: 3; Y: 4) : TPoint Record children=2 modify=False eval=PP^ at=A3C01FF7B4", Describe(bed.Info(record)));
        Assert.Equal(["PP^.X", "PP^.Y"], (await bed.ChildrenAsync(record)).Select(field => field.EvaluateName));

        // A pointer to a pointer: one level per expansion.
        bed = new T();
        bed.Memory.PutPointer(T.At, 0x30000).PutPointer(0x30000, Target).PutInt32(Target, 24);
        var outer = await bed.DecodeAsync(T.PPInteger, T.At, "Q");
        Assert.Equal("^^Integer", outer.TypeName);
        var inner = Assert.Single(await bed.ChildrenAsync(outer));
        Assert.Equal("^ = $000000A3C01FF7B4 : ^Integer Pointer children=1 modify=True eval=Q^ at=30000", Describe(bed.Info(inner)));
        Assert.Equal("Q^^", Assert.Single(await bed.ChildrenAsync(inner)).EvaluateName);
    }

    [Fact]
    [Trait("Req", "DBG-172")]
    public async Task UntypedPointer_NoChildren()
    {
        var bed = new T();
        bed.Memory.PutPointer(T.At, Target).PutPointer(T.At + 8, 0).PutInt32(Target, 24);

        var node = await bed.DecodeAsync(T.Pointer, T.At, "Raw");
        var nil = await bed.DecodeAsync(T.Pointer, T.At + 8, "Raw");

        Assert.Equal(new VariableInfo("Raw", "$000000A3C01FF7B4", "Pointer", ValueKind.Pointer, null, 0, true, "Raw", T.At), bed.Info(node));
        Assert.Empty(await bed.ChildrenAsync(node));
        Assert.Equal("nil", bed.Format(nil));
        // Unchanged by the hexadecimal option: addresses are always "$" and 16 digits.
        var hex = new T(new ValueFormatOptions { HexIntegers = true });
        hex.Memory.PutPointer(T.At, Target);
        Assert.Equal("$000000A3C01FF7B4", hex.Format(await hex.DecodeAsync(T.Pointer)));
    }

    [Fact]
    [Trait("Req", "DBG-171")]
    public async Task ClassReference_ClassNameFromVmt()
    {
        const ulong vmt = 0x7FF6A1B30000;
        var bed = new T();
        bed.Memory.PutPointer(T.At, vmt).PutVmt(vmt, "TDog", 0, 0x7FF6A1B38000).PutPointer(T.At + 8, 0).PutPointer(T.At + 16, 0x1234567890);

        var named = await bed.DecodeAsync(T.TAnimalClass, T.At, "Cls");
        var anonymous = await bed.DecodeAsync(T.AnonymousClassRef, T.At, "Cls");
        var nil = await bed.DecodeAsync(T.TAnimalClass, T.At + 8, "Cls");
        var dangling = await bed.DecodeAsync(T.TAnimalClass, T.At + 16, "Cls");

        // The class name read from the VMT, also for a class the type information does not list.
        Assert.Equal(new VariableInfo("Cls", "TDog", "TAnimalClass", ValueKind.ClassReference, null, 0, false, "Cls", T.At), bed.Info(named));
        Assert.Equal("Cls = TDog : class of TAnimal ClassReference children=0 modify=False eval=Cls at=20000", Describe(bed.Info(anonymous)));
        Assert.Equal("nil", bed.Format(nil));
        Assert.Equal(ValueKind.ClassReference, nil.Kind);
        Assert.Equal(ValueKind.Error, dangling.Kind);
        Assert.Equal("<unreadable memory at $0000001234567890>", bed.Format(dangling));
    }

    [Fact]
    [Trait("Req", "DBG-173")]
    public async Task Interface_AddressOrNil()
    {
        var bed = new T();
        bed.Memory.PutPointer(T.At, Target).PutPointer(T.At + 8, 0);

        var node = await bed.DecodeAsync(T.IInterface, T.At, "Intf");

        Assert.Equal(new VariableInfo("Intf", "$000000A3C01FF7B4", "IInterface", ValueKind.Interface, null, 0, false, "Intf", T.At), bed.Info(node));
        Assert.Empty(await bed.ChildrenAsync(node));
        Assert.Equal("nil", bed.Format(await bed.DecodeAsync(T.IInterface, T.At + 8)));
    }

    [Fact]
    public async Task Procedural_PlainAndMethod()
    {
        const ulong code = 0x7FF6A1B21234, data = 0x1F3A2B41C60;
        var bed = new T();
        bed.Memory.PutPointer(T.At, code).PutPointer(T.At + 8, data).Map(T.At + 16, 32).PutPointer(T.At + 48, 0).PutPointer(T.At + 56, data);

        // A plain procedure value is its code address.
        var plain = await bed.DecodeAsync(T.TProc, T.At, "Proc");
        Assert.Equal(new VariableInfo("Proc", "$00007FF6A1B21234", "TProc", ValueKind.Procedural, null, 0, false, "Proc", T.At), bed.Info(plain));
        Assert.Equal("procedure", (await bed.DecodeAsync(T.AnonymousProc)).TypeName);
        Assert.Equal("nil", bed.Format(await bed.DecodeAsync(T.TProc, T.At + 16)));

        // A method pointer is a code pointer followed by a data pointer.
        var method = await bed.DecodeAsync(T.TNotify, T.At, "OnClick");
        Assert.Equal(
            new VariableInfo("OnClick", "(Code: $00007FF6A1B21234; Data: $000001F3A2B41C60)", "TNotify", ValueKind.Procedural, null, 0, false, "OnClick", T.At),
            bed.Info(method));
        Assert.Equal("procedure of object", (await bed.DecodeAsync(T.AnonymousMethod)).TypeName);
        // nil only when both are zero.
        Assert.Equal("nil", bed.Format(await bed.DecodeAsync(T.TNotify, T.At + 16)));
        Assert.Equal("(Code: $0000000000000000; Data: $000001F3A2B41C60)", bed.Format(await bed.DecodeAsync(T.TNotify, T.At + 48)));
        // Both pointers are read: 16 bytes.
        Assert.Contains("20000+16", bed.Memory.Reads);
    }

    [Fact]
    public async Task Opaque_HexBytes()
    {
        var bed = new T();
        bed.Memory.PutHex(T.At, "1F00A0").PutHex(0x30000, "000102030405060708090A0B0C0D0E0F10111213");

        var small = await bed.DecodeAsync(T.TSmall, T.At, "Small");
        var large = await bed.DecodeAsync(T.Text, 0x30000, "Output");

        // Up to 16 bytes as hexadecimal pairs; an ellipsis when the type is larger. No expression: a raw node.
        Assert.Equal(new VariableInfo("Small", "{1F 00 A0}", "TSmall", ValueKind.Raw, null, 0, false, null, T.At), bed.Info(small));
        Assert.Equal(
            new VariableInfo("Output", "{00 01 02 03 04 05 06 07 08 09 0A 0B 0C 0D 0E 0F …}", "Text", ValueKind.Raw, null, 0, false, null, 0x30000),
            bed.Info(large));
        Assert.Empty(await bed.ChildrenAsync(large));
        // Only the shown bytes are read, not the whole 624-byte variable.
        Assert.Equal(["20000+3", "30000+17"], bed.Memory.Reads);
    }

    [Theory]
    [Trait("Req", "DBG-152")]
    [InlineData(T.PInteger)]
    [InlineData(T.Pointer)]
    [InlineData(T.TAnimalClass)]
    [InlineData(T.IInterface)]
    [InlineData(T.TProc)]
    [InlineData(T.TNotify)]
    [InlineData(T.TSmall)]
    [InlineData(T.TAnimal)]
    public async Task Variable_Unreadable_ErrorNode(int type)
    {
        var bed = new T();
        bed.Memory.Put(T.At, 1, 2);

        var node = await bed.DecodeAsync(type, T.At, "V");

        Assert.Equal(ValueKind.Error, node.Kind);
        Assert.Equal("<unreadable memory at $0000000000020000>", bed.Format(node));
        Assert.Null(bed.Info(node).Children);
    }
}
