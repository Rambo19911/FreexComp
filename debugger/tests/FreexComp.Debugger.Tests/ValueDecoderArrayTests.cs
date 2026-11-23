using System.Text;
using FreexComp.Debugger.Tests.TestSupport;
using T = FreexComp.Debugger.Tests.TestSupport.ValueTestBed;

namespace FreexComp.Debugger.Tests;

/// <summary>Static, dynamic and open arrays, character arrays, paging (Pascal values §5: DBG-162 … DBG-165).</summary>
public sealed class ValueDecoderArrayTests
{
    private const ulong Payload = 0x21000;

    private static string Describe(VariableInfo info) =>
        $"{info.Name} = {info.Value} : {info.TypeName} {info.Kind} children={info.ChildCount} modify={info.CanModify} eval={info.EvaluateName} at={info.Address:X}";

    [Fact]
    [Trait("Req", "DBG-162")]
    public async Task Static_SummaryAndChildren()
    {
        var bed = new T();
        bed.Memory.PutInt32(T.At, 10).PutInt32(T.At + 4, 20).PutInt32(T.At + 8, 30).PutInt32(T.At + 12, -40);

        var node = await bed.DecodeAsync(T.StaticInts, T.At, "A");

        // The elements lie at the variable's own address; the anonymous type gets its Pascal spelling.
        var info = bed.Info(node);
        Assert.Equal("A = (10, 20, 30, -40) : array[0..3] of Integer StaticArray children=4 modify=False eval=A at=20000", Describe(info));
        Assert.Equal(new VariableHandle(1, 1), info.Children);
        Assert.Equal(
        [
            "[0] = 10 : Integer Integer children=0 modify=True eval=A[0] at=20000",
            "[1] = 20 : Integer Integer children=0 modify=True eval=A[1] at=20004",
            "[2] = 30 : Integer Integer children=0 modify=True eval=A[2] at=20008",
            "[3] = -40 : Integer Integer children=0 modify=True eval=A[3] at=2000C",
        ], (await bed.ChildrenAsync(node)).Select(child => Describe(bed.Info(child))));
    }

    [Fact]
    [Trait("Req", "DBG-162")]
    public async Task Static_IndexType_EnumAndChar()
    {
        var bed = new T();
        bed.Memory.PutHex(T.At, "0A141E").PutInt32(0x20100, 1).PutInt32(0x20104, 2).PutInt32(0x20108, 3);

        var byColor = await bed.DecodeAsync(T.ByColor, T.At, "ByColor");
        var byLetter = await bed.DecodeAsync(T.ByLetter, 0x20100, "ByLetter");

        // The index is rendered by the rule of its type, in the child name and in the expression.
        Assert.Equal("array[clRed..clBlue] of Byte", byColor.TypeName);
        Assert.Equal("(10, 20, 30)", bed.Format(byColor));
        Assert.Equal(
            ["[clRed]|ByColor[clRed]|10", "[clGreen]|ByColor[clGreen]|20", "[clBlue]|ByColor[clBlue]|30"],
            (await bed.ChildrenAsync(byColor)).Select(child => $"{child.Name}|{child.EvaluateName}|{bed.Format(child)}"));
        Assert.Equal("array['a'..'c'] of Integer", byLetter.TypeName);
        Assert.Equal(
            ["['a']|ByLetter['a']|1", "['b']|ByLetter['b']|2", "['c']|ByLetter['c']|3"],
            (await bed.ChildrenAsync(byLetter)).Select(child => $"{child.Name}|{child.EvaluateName}|{bed.Format(child)}"));
    }

    [Fact]
    [Trait("Req", "DBG-162")]
    public async Task Static_MultiDimensional_NestedArrays()
    {
        var bed = new T();
        bed.Memory.PutHex(T.At, "010203 040506");

        var matrix = await bed.DecodeAsync(T.Matrix, T.At, "M");

        // Inside a summary a structured element is only "(…)" (DBG-174).
        Assert.Equal("((…), (…))", bed.Format(matrix));
        Assert.Equal("TMatrix", matrix.TypeName);
        var rows = await bed.ChildrenAsync(matrix);
        Assert.Equal(["[0] = (1, 2, 3)", "[1] = (4, 5, 6)"], rows.Select(row => row.Name + " = " + bed.Format(row)));
        Assert.Equal("array[-1..1] of Byte", rows[1].TypeName);
        // The row's own low bound is -1.
        Assert.Equal(
            ["[-1]|M[1][-1]|4|20003", "[0]|M[1][0]|5|20004", "[1]|M[1][1]|6|20005"],
            (await bed.ChildrenAsync(rows[1])).Select(cell => $"{cell.Name}|{cell.EvaluateName}|{bed.Format(cell)}|{cell.Address:X}"));
    }

    [Fact]
    [Trait("Req", "DBG-163")]
    public async Task Dynamic_SummaryAndChildren()
    {
        var bed = new T();
        bed.Memory.PutPointer(T.At, Payload).PutIntArray(Payload, 10, 20, 30);

        var node = await bed.DecodeAsync(T.DynInts, T.At, "Nums");

        // The node's address is the variable's; the elements lie at the payload.
        Assert.Equal("Nums = (10, 20, 30) : array of Integer DynamicArray children=3 modify=False eval=Nums at=20000", Describe(bed.Info(node)));
        Assert.Equal(
        [
            "[0] = 10 : Integer Integer children=0 modify=True eval=Nums[0] at=21000",
            "[1] = 20 : Integer Integer children=0 modify=True eval=Nums[1] at=21004",
            "[2] = 30 : Integer Integer children=0 modify=True eval=Nums[2] at=21008",
        ], (await bed.ChildrenAsync(node)).Select(child => Describe(bed.Info(child))));
    }

    [Fact]
    [Trait("Req", "DBG-163")]
    public async Task Dynamic_Nil_IsEmpty()
    {
        var bed = new T();
        bed.Memory.PutPointer(T.At, 0);

        var node = await bed.DecodeAsync(T.DynInts, T.At, "Nums");

        var info = bed.Info(node);
        Assert.Equal("Nums = () : array of Integer DynamicArray children=0 modify=False eval=Nums at=20000", Describe(info));
        Assert.Null(info.Children);
        Assert.Empty(await bed.ChildrenAsync(node));
        Assert.Equal(["20000+8"], bed.Memory.Reads);
    }

    [Theory]
    [Trait("Req", "DBG-163")]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    // More than 2^40 bytes of payload (4-byte elements).
    [InlineData((1L << 38) + 1)]
    [InlineData(long.MaxValue)]
    public async Task Dynamic_InvalidLength_ErrorNode(long length)
    {
        var bed = new T();
        bed.Memory.PutPointer(T.At, Payload).PutInt64(Payload - 8, length);

        var node = await bed.DecodeAsync(T.DynInts, T.At, "Nums");

        Assert.Equal(
            new VariableInfo("Nums", "<invalid array at $0000000000021000>", "array of Integer", ValueKind.Error, null, 0, false, null, T.At),
            bed.Info(node));
    }

    [Fact]
    [Trait("Req", "DBG-152")]
    public async Task Dynamic_Unreadable()
    {
        // The length field cannot be read: the array is an error node.
        var bed = new T();
        bed.Memory.PutPointer(T.At, Payload);
        var dangling = await bed.DecodeAsync(T.DynInts);
        Assert.Equal(ValueKind.Error, dangling.Kind);
        Assert.Equal("<unreadable memory at $0000000000020FF8>", bed.Format(dangling));

        // One element cannot be read: only that element is an error, in the summary and as a child.
        bed = new T();
        bed.Memory.PutPointer(T.At, Payload).PutIntArray(Payload, 10, 20, 30);
        bed.Memory.Remove(Payload + 8, 4);
        var node = await bed.DecodeAsync(T.DynInts);
        Assert.Equal(ValueKind.DynamicArray, node.Kind);
        Assert.Equal("(10, 20, <unreadable memory at $0000000000021008>)", bed.Format(node));
        var children = await bed.ChildrenAsync(node);
        Assert.Equal([ValueKind.Integer, ValueKind.Integer, ValueKind.Error], children.Select(child => child.Kind));
        Assert.Null(bed.Info(children[2]).EvaluateName);
    }

    [Fact]
    [Trait("Req", "DBG-165")]
    public async Task Summary_Elided_WithLength()
    {
        var bed = new T();
        bed.Memory.PutPointer(T.At, Payload).PutIntArray(Payload, [.. Enumerable.Range(1, 100)]);

        var node = await bed.DecodeAsync(T.DynInts);

        // The first MaxSummaryElements elements, an ellipsis and the length.
        Assert.Equal("(1, 2, 3, 4, 5, 6, 7, 8, …) (Length: 100)", bed.Format(node));
        Assert.Equal(100, node.ChildCount);
        // The summary read the variable, the length and eight elements: nothing else.
        Assert.Equal(10, bed.Memory.Reads.Count);

        // Exactly the limit: complete.
        var eight = new T();
        eight.Memory.PutPointer(T.At, Payload).PutIntArray(Payload, 1, 2, 3, 4, 5, 6, 7, 8);
        Assert.Equal("(1, 2, 3, 4, 5, 6, 7, 8)", eight.Format(await eight.DecodeAsync(T.DynInts)));

        // The limit and the hexadecimal option come from the format options.
        var small = new T(new ValueFormatOptions { MaxSummaryElements = 2, HexIntegers = true });
        small.Memory.PutPointer(T.At, Payload).PutIntArray(Payload, 10, 20, 30);
        Assert.Equal("($0000000A, $00000014, …) (Length: 3)", small.Format(await small.DecodeAsync(T.DynInts)));
    }

    [Fact]
    [Trait("Req", "DBG-165")]
    [Trait("Req", "DBG-411")]
    public async Task Children_Paged()
    {
        var bed = new T(new ValueFormatOptions { ChildPageSize = 10 });
        bed.Memory.PutPointer(T.At, Payload).PutIntArray(Payload, [.. Enumerable.Range(1, 100)]);
        var node = await bed.DecodeAsync(T.DynInts, T.At, "Nums");
        bed.Memory.Reads.Clear();

        // count 0: one page.
        var firstPage = await bed.ChildLinesAsync(node);
        Assert.Equal(10, firstPage.Length);
        Assert.Equal("[0] = 1", firstPage[0]);
        Assert.Equal("[9] = 10", firstPage[^1]);
        // Children are decoded only when asked for: ten elements were read.
        Assert.Equal(10, bed.Memory.Reads.Count);

        Assert.Equal(["[10] = 11", "[11] = 12", "[12] = 13"], await bed.ChildLinesAsync(node, 10, 3));
        // An explicit count may exceed the page size; the end of the array limits it.
        Assert.Equal(25, (await bed.ChildrenAsync(node, 50, 25)).Count);
        Assert.Equal(["[95] = 96", "[96] = 97", "[97] = 98", "[98] = 99", "[99] = 100"], await bed.ChildLinesAsync(node, 95, 10));
        Assert.Equal(["[99] = 100"], await bed.ChildLinesAsync(node, 99));
        Assert.Empty(await bed.ChildrenAsync(node, 100));
        Assert.Empty(await bed.ChildrenAsync(node, 1000, 5));

        // The default page is 100 children.
        var standard = new T();
        standard.Memory.PutPointer(T.At, Payload).PutIntArray(Payload, [.. Enumerable.Range(1, 150)]);
        var big = await standard.DecodeAsync(T.DynInts);
        Assert.Equal(100, (await standard.ChildrenAsync(big)).Count);
        Assert.Equal(50, (await standard.ChildrenAsync(big, 100)).Count);
    }

    [Fact]
    [Trait("Req", "DBG-164")]
    public async Task Open_CountFromHiddenHigh()
    {
        var bed = new T();
        bed.Memory.PutPointer(T.At, Payload).PutInt32(Payload, 10).PutInt32(Payload + 4, 20).PutInt32(Payload + 8, 30).PutInt32(Payload + 12, 99);

        // High = 2: three elements, whatever lies behind them.
        var node = await bed.DecodeAsync(T.OpenInts, T.At, "A", openArrayHigh: 2);
        Assert.Equal("A = (10, 20, 30) : array of Integer DynamicArray children=3 modify=False eval=A at=20000", Describe(bed.Info(node)));
        Assert.Equal(["[0] = 10", "[1] = 20", "[2] = 30"], await bed.ChildLinesAsync(node));
        Assert.Equal("A[2]", (await bed.ChildrenAsync(node))[2].EvaluateName);

        // High = -1: empty.
        var empty = await bed.DecodeAsync(T.OpenInts, T.At, "A", openArrayHigh: -1);
        Assert.Equal("()", bed.Format(empty));
        Assert.Equal(0, empty.ChildCount);

        // Without the hidden variable the array cannot be shown.
        var unknown = await bed.DecodeAsync(T.OpenInts, T.At, "A");
        Assert.Equal(new VariableInfo("A", "<not available>", "array of Integer", ValueKind.Error, null, 0, false, null, T.At), bed.Info(unknown));
    }

    [Fact]
    [Trait("Req", "DBG-165")]
    public async Task CharArray_ShownAsText_ChildrenAreElements()
    {
        var bed = new T();
        bed.Memory.Put(T.At, Encoding.Unicode.GetBytes("Hi'\0junk"));

        var node = await bed.DecodeAsync(T.StaticChars, T.At, "Buf");

        // The units up to (not including) the first #0, as a string literal.
        Assert.Equal("Buf = 'Hi''' : array[0..7] of Char StaticArray children=8 modify=False eval=Buf at=20000", Describe(bed.Info(node)));
        var children = await bed.ChildrenAsync(node);
        Assert.Equal(
            ["[0] = 'H'", "[1] = 'i'", "[2] = ''''", "[3] = #0", "[4] = 'j'", "[5] = 'u'", "[6] = 'n'", "[7] = 'k'"],
            children.Select(child => child.Name + " = " + bed.Format(child)));
        Assert.Equal("Buf[2] Char 20004", $"{children[2].EvaluateName} {children[2].TypeName} {children[2].Address:X}");

        // No #0: the whole array.
        var full = new T();
        full.Memory.Put(T.At, Encoding.Unicode.GetBytes("ABCDEFGH"));
        Assert.Equal("'ABCDEFGH'", full.Format(await full.DecodeAsync(T.StaticChars)));
        // Starting with #0: empty text.
        var blank = new T();
        blank.Memory.Map(T.At, 16);
        Assert.Equal("''", blank.Format(await blank.DecodeAsync(T.StaticChars)));
    }

    [Fact]
    [Trait("Req", "DBG-165")]
    public async Task CharArray_DynamicAnsiAndTruncated()
    {
        // array of Char.
        var bed = new T();
        bed.Memory.PutPointer(T.At, Payload).PutInt64(Payload - 8, 5).Put(Payload, Encoding.Unicode.GetBytes("a\tb\0c"));
        var dynamic = await bed.DecodeAsync(T.DynChars, T.At, "D");
        Assert.Equal("D = 'a'#9'b' : array of Char DynamicArray children=5 modify=False eval=D at=20000", Describe(bed.Info(dynamic)));
        Assert.Equal("[4] = 'c'", (await bed.ChildLinesAsync(dynamic))[4]);

        // array[1..4] of AnsiChar: 8-bit units.
        bed = new T();
        bed.Memory.PutHex(T.At, "4FE94B00");
        var ansi = await bed.DecodeAsync(T.StaticAnsiChars, T.At, "S");
        Assert.Equal("S = 'OéK' : array[1..4] of AnsiChar StaticArray children=4 modify=False eval=S at=20000", Describe(bed.Info(ansi)));
        Assert.Equal(["[1] = 'O'", "[2] = #233", "[3] = 'K'", "[4] = #0"], await bed.ChildLinesAsync(ansi));

        // Longer than the display limit without a #0: cut, with the element count.
        bed = new T(new ValueFormatOptions { MaxStringChars = 4 });
        bed.Memory.Put(T.At, Encoding.Unicode.GetBytes("ABCDEFGH"));
        Assert.Equal("'ABCD'… (Length: 8)", bed.Format(await bed.DecodeAsync(T.StaticChars)));
        // A #0 inside the shown part: not cut.
        bed = new T(new ValueFormatOptions { MaxStringChars = 4 });
        bed.Memory.Put(T.At, Encoding.Unicode.GetBytes("AB\0DEFGH"));
        Assert.Equal("'AB'", bed.Format(await bed.DecodeAsync(T.StaticChars)));

        // Empty array of Char.
        bed = new T();
        bed.Memory.PutPointer(T.At, 0);
        Assert.Equal("''", bed.Format(await bed.DecodeAsync(T.DynChars)));
    }

    [Fact]
    [Trait("Req", "DBG-152")]
    public async Task Static_ElementUnreadable_OnlyThatElementIsAnError()
    {
        var bed = new T();
        bed.Memory.PutInt32(T.At, 10).PutInt32(T.At + 4, 20).PutInt32(T.At + 12, 40);

        var node = await bed.DecodeAsync(T.StaticInts, T.At, "A");

        Assert.Equal("(10, 20, <unreadable memory at $0000000000020008>, 40)", bed.Format(node));
        Assert.Equal(
            ["[0] = 10", "[1] = 20", "[2] = <unreadable memory at $0000000000020008>", "[3] = 40"],
            await bed.ChildLinesAsync(node));
    }
}
