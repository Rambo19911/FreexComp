using FreexComp.Debugger.Tests.TestSupport;
using FreexComp.Debugger.Values;
using T = FreexComp.Debugger.Tests.TestSupport.ValueTestBed;

namespace FreexComp.Debugger.Tests;

/// <summary>Presentation rules: the nested form inside summaries, type names, modifiability (Pascal values §8, DBG-154).</summary>
public sealed class ValueFormatterTests
{
    private const ulong NamePayload = 0x21000, TagsPayload = 0x22000, OnePayload = 0x23000, TwoPayload = 0x24000;

    /// <summary>A <c>TPet</c> record at <see cref="T.At"/>: Name 'Rex', Pos (3, 4), Tags ('one', 'two').</summary>
    private static T PetBed(ValueFormatOptions? options = null)
    {
        var bed = new T(options);
        bed.Memory.PutPointer(T.At, NamePayload).PutUnicodeString(NamePayload, "Rex")
            .PutInt32(T.At + 8, 3).PutInt32(T.At + 12, 4)
            .PutPointer(T.At + 16, TagsPayload).PutInt64(TagsPayload - 8, 2)
            .PutPointer(TagsPayload, OnePayload).PutUnicodeString(OnePayload, "one")
            .PutPointer(TagsPayload + 8, TwoPayload).PutUnicodeString(TwoPayload, "two");
        return bed;
    }

    [Fact]
    [Trait("Req", "DBG-174")]
    public async Task NestedForm_RecordWithRecordAndArray()
    {
        var bed = PetBed();

        var node = await bed.DecodeAsync(T.TPet, T.At, "Pet");

        // Leaves as standalone; a nested record or array is only "(…)": never deeper than one structured level.
        Assert.Equal("(Name: 'Rex'; Pos: (…); Tags: (…))", bed.Format(node));
        // Expanded, every child is a node of its own with its full summary.
        Assert.Equal(["Name = 'Rex'", "Pos = (X: 3; Y: 4)", "Tags = ('one', 'two')"], await bed.ChildLinesAsync(node));
        Assert.Equal("Pet.Tags[1]", (await bed.ChildrenAsync((await bed.ChildrenAsync(node))[2]))[1].EvaluateName);
    }

    [Fact]
    [Trait("Req", "DBG-174")]
    public async Task NestedForm_ArrayOfRecords()
    {
        const ulong payload = 0x21000;
        var bed = new T();
        bed.Memory.PutPointer(T.At, payload).PutInt64(payload - 8, 3).Map(payload, 24);

        var node = await bed.DecodeAsync(T.DynPoints);

        Assert.Equal("((…), (…), (…))", bed.Format(node));
        // The nested records were not read for the summary: the variable and the length only.
        Assert.Equal(2, bed.Memory.Reads.Count);
    }

    [Fact]
    [Trait("Req", "DBG-174")]
    public async Task NestedForm_ArrayOfString()
    {
        var bed = PetBed();

        var node = await bed.DecodeAsync(T.DynStrings, T.At + 16, "Tags");

        Assert.Equal("('one', 'two')", bed.Format(node));
        Assert.Equal("array of string", node.TypeName);
    }

    [Fact]
    [Trait("Req", "DBG-174")]
    public async Task NestedForm_StringLimitedTo32Characters()
    {
        var bed = PetBed();
        var letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmn";   // 40 characters
        bed.Memory.PutUnicodeString(NamePayload, letters).PutUnicodeString(OnePayload, letters[..32]).PutUnicodeString(TwoPayload, letters[..33]);

        var pet = await bed.DecodeAsync(T.TPet, T.At, "Pet");
        var tags = await bed.DecodeAsync(T.DynStrings, T.At + 16, "Tags");

        // 32 characters, then an ellipsis, without the length suffix.
        Assert.Equal("(Name: 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdef'…; Pos: (…); Tags: (…))", bed.Format(pet));
        Assert.Equal("('ABCDEFGHIJKLMNOPQRSTUVWXYZabcdef', 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdef'…)", bed.Format(tags));
        // Standalone the same string is shown in full.
        Assert.Equal("Name = '" + letters + "'", (await bed.ChildLinesAsync(pet))[0]);

        // A smaller display limit also limits nested strings; the length is still not appended.
        var small = PetBed(new ValueFormatOptions { MaxStringChars = 2 });
        Assert.Equal("(Name: 'Re'…; Pos: (…); Tags: (…))", small.Format(await small.DecodeAsync(T.TPet)));
        Assert.Equal("Name = 'Re'… (Length: 3)", (await small.ChildLinesAsync(await small.DecodeAsync(T.TPet)))[0]);
    }

    [Fact]
    [Trait("Req", "DBG-174")]
    public async Task NestedForm_LeavesAndInstances()
    {
        const ulong instance = 0x1F3A2B41C60, vmt = 0x7FF6A1B30000, total = 0x30000, nums = 0x31000;
        var bed = new T();
        bed.Memory.PutPointer(T.At, instance).PutPointer(instance, vmt).PutVmt(vmt, "TDog", 0, 0x7FF6A1B31000)
            .PutPointer(T.At + 8, total)
            .Put(T.At + 16, 1, 0x05)
            .PutPointer(T.At + 24, nums).PutIntArray(nums, 1, 2)
            .PutInt64(T.At + 32, 0x3FF8000000000001);

        var node = await bed.DecodeAsync(T.TOwner, T.At, "Owner");

        // Class instances as Class($ADDR), pointers, enums, sets, Booleans and floats exactly as standalone;
        // a record without fields is "()".
        Assert.Equal(
            "(Pet: TDog($000001F3A2B41C60); Next: $0000000000030000; Kind: clGreen; Flags: [clRed, clBlue]; Nums: (…); None: (); "
            + "Ok: True; Rate: 1.5000000000000002)",
            bed.Format(node));

        // nil instance, nil pointer, empty array.
        var empty = new T();
        empty.Memory.Map(T.At, 40);
        Assert.Equal(
            "(Pet: nil; Next: nil; Kind: clRed; Flags: []; Nums: (); None: (); Ok: False; Rate: 0)",
            empty.Format(await empty.DecodeAsync(T.TOwner)));
    }

    [Fact]
    [Trait("Req", "DBG-176")]
    public async Task CanModify_ScalarsAndPointersWithAddress_WhenMemoryIsWritable()
    {
        var bed = new T();
        bed.Memory.Map(T.At, 64).PutInt64(T.At + 32, 0);
        (int Type, bool Modifiable)[] cases =
        [
            (T.Integer, true), (T.Double, true), (T.Currency, true), (T.Boolean, true), (T.Char, true), (T.TColor, true),
            (T.PInteger, true), (T.Pointer, true),
            (T.TColors, false), (T.String, false), (T.ShortString10, false), (T.PChar, false), (T.StaticInts, false), (T.DynInts, false),
            (T.TPoint, false), (T.TAnimal, false), (T.TAnimalClass, false), (T.IInterface, false), (T.TProc, false), (T.TNotify, false),
            (T.TSmall, false),
        ];

        foreach (var (type, modifiable) in cases)
        {
            var node = await bed.DecodeAsync(type);
            Assert.True(modifiable == bed.Info(node).CanModify, $"type {type}");
            Assert.False(bed.Info(node, canWriteMemory: false).CanModify, $"type {type} without writable memory");
        }
    }

    [Theory]
    [Trait("Req", "DBG-154")]
    [InlineData(T.Integer, "Integer")]
    [InlineData(T.TColors, "TColors")]
    [InlineData(T.SetOfByte, "set of Byte")]
    [InlineData(T.SetOfAnsiChar, "set of AnsiChar")]
    [InlineData(T.ShortString10, "string[10]")]
    [InlineData(T.StaticInts, "array[0..3] of Integer")]
    [InlineData(T.DynInts, "array of Integer")]
    [InlineData(T.OpenInts, "array of Integer")]
    [InlineData(T.ByColor, "array[clRed..clBlue] of Byte")]
    [InlineData(T.ByLetter, "array['a'..'c'] of Integer")]
    [InlineData(T.MatrixRow, "array[-1..1] of Byte")]
    [InlineData(T.Matrix, "TMatrix")]
    [InlineData(T.DynStrings, "array of string")]
    [InlineData(T.DynDynInts, "array of array of Integer")]
    [InlineData(T.PInteger, "^Integer")]
    [InlineData(T.PPInteger, "^^Integer")]
    [InlineData(T.Pointer, "Pointer")]
    [InlineData(T.PPoint, "PPoint")]
    [InlineData(T.AnonymousClassRef, "class of TAnimal")]
    [InlineData(T.TAnimalClass, "TAnimalClass")]
    [InlineData(T.AnonymousProc, "procedure")]
    [InlineData(T.AnonymousMethod, "procedure of object")]
    [InlineData(T.TNotify, "TNotify")]
    public void TypeName_DeclaredOrSynthesised(int type, string expected)
    {
        Assert.Equal(expected, ValueFormatter.TypeName(new T().Type(type)));
    }
}
