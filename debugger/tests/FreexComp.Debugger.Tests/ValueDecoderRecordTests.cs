using FreexComp.Debugger.Tests.TestSupport;
using T = FreexComp.Debugger.Tests.TestSupport.ValueTestBed;

namespace FreexComp.Debugger.Tests;

/// <summary>Records (Pascal values §6, DBG-166).</summary>
[Trait("Req", "DBG-166")]
public sealed class ValueDecoderRecordTests
{
    private static string Describe(VariableInfo info) =>
        $"{info.Name} = {info.Value} : {info.TypeName} {info.Kind} children={info.ChildCount} modify={info.CanModify} eval={info.EvaluateName} at={info.Address:X}";

    [Fact]
    public async Task Record_SummaryAndFields()
    {
        var bed = new T();
        bed.Memory.PutInt32(T.At, 3).PutInt32(T.At + 4, 4);

        var node = await bed.DecodeAsync(T.TPoint, T.At, "P");

        var info = bed.Info(node);
        Assert.Equal("P = (X: 3; Y: 4) : TPoint Record children=2 modify=False eval=P at=20000", Describe(info));
        Assert.Equal(new VariableHandle(1, 1), info.Children);
        // Fields in declaration order, each at its offset.
        Assert.Equal(
        [
            "X = 3 : Integer Integer children=0 modify=True eval=P.X at=20000",
            "Y = 4 : Integer Integer children=0 modify=True eval=P.Y at=20004",
        ], (await bed.ChildrenAsync(node)).Select(child => Describe(bed.Info(child))));
    }

    [Fact]
    public async Task Record_WithoutFields()
    {
        var bed = new T();

        var node = await bed.DecodeAsync(T.TEmpty, T.At, "E");

        var info = bed.Info(node);
        Assert.Equal("E = () : TEmpty Record children=0 modify=False eval=E at=20000", Describe(info));
        Assert.Null(info.Children);
        Assert.Empty(await bed.ChildrenAsync(node));
        // Nothing is read for a record without fields.
        Assert.Empty(bed.Memory.Reads);
    }

    [Fact]
    public async Task Record_VariantFields_EachAtItsOwnOffset()
    {
        var bed = new T();
        bed.Memory.PutHex(T.At, "07 000000 34120100");

        var node = await bed.DecodeAsync(T.TVariant, T.At, "V");

        // Overlapping fields are all shown.
        Assert.Equal("(Tag: 7; AsInt: 70196; AsWord: 4660)", bed.Format(node));
        Assert.Equal(
            ["Tag|V.Tag|20000", "AsInt|V.AsInt|20004", "AsWord|V.AsWord|20004"],
            (await bed.ChildrenAsync(node)).Select(child => $"{child.Name}|{child.EvaluateName}|{child.Address:X}"));
    }

    [Fact]
    public async Task Record_Summary_ElidedAfterMaxSummaryElements()
    {
        var bed = new T();
        bed.Memory.PutHex(T.At, "0102030405060708090A");

        var node = await bed.DecodeAsync(T.TWide, T.At, "W");

        Assert.Equal("(F1: 1; F2: 2; F3: 3; F4: 4; F5: 5; F6: 6; F7: 7; F8: 8; …)", bed.Format(node));
        Assert.Equal(10, node.ChildCount);
        // The summary reads only the fields it shows; all fields are children.
        Assert.Equal(8, bed.Memory.Reads.Count);
        Assert.Equal("F10 = 10", (await bed.ChildLinesAsync(node))[9]);
        Assert.Equal(["F9 = 9", "F10 = 10"], await bed.ChildLinesAsync(node, 8));

        var small = new T(new ValueFormatOptions { MaxSummaryElements = 1, HexIntegers = true });
        small.Memory.PutInt32(T.At, 3).PutInt32(T.At + 4, 4);
        Assert.Equal("(X: $00000003; …)", small.Format(await small.DecodeAsync(T.TPoint)));
    }

    [Fact]
    [Trait("Req", "DBG-152")]
    public async Task Record_FieldUnreadable_OnlyThatFieldIsAnError()
    {
        var bed = new T();
        bed.Memory.PutInt32(T.At + 4, 4);

        var node = await bed.DecodeAsync(T.TPoint, T.At, "P");

        Assert.Equal(ValueKind.Record, node.Kind);
        Assert.Equal("(X: <unreadable memory at $0000000000020000>; Y: 4)", bed.Format(node));
        var children = await bed.ChildrenAsync(node);
        Assert.Equal([ValueKind.Error, ValueKind.Integer], children.Select(child => child.Kind));
        Assert.Equal("Integer", children[0].TypeName);
    }

    [Fact]
    public async Task Record_InsideArray_FieldExpressions()
    {
        const ulong payload = 0x21000;
        var bed = new T();
        bed.Memory.PutPointer(T.At, payload).PutInt64(payload - 8, 2)
            .PutInt32(payload, 1).PutInt32(payload + 4, 2).PutInt32(payload + 8, 3).PutInt32(payload + 12, 4);

        var node = await bed.DecodeAsync(T.DynPoints, T.At, "Pts");

        Assert.Equal("array of TPoint", node.TypeName);
        var second = (await bed.ChildrenAsync(node))[1];
        Assert.Equal("[1] = (X: 3; Y: 4) : TPoint Record children=2 modify=False eval=Pts[1] at=21008", Describe(bed.Info(second)));
        Assert.Equal(["Pts[1].X", "Pts[1].Y"], (await bed.ChildrenAsync(second)).Select(child => child.EvaluateName));
    }
}
