using FreexComp.Debugger.Tests.TestSupport;
using T = FreexComp.Debugger.Tests.TestSupport.ValueTestBed;

namespace FreexComp.Debugger.Tests;

/// <summary>Class instances: dynamic type, inherited fields, nil, invalid instances (Pascal values §7.1: DBG-167 … DBG-170).</summary>
public sealed class ValueDecoderClassTests
{
    private const ulong Instance = 0x1F3A2B41C60;
    private const ulong DogVmt = 0x7FF6A1B30000, AnimalVmt = 0x7FF6A1B30100, ObjectVmt = 0x7FF6A1B30200, OtherVmt = 0x7FF6A1B30300;
    private const ulong Names = 0x7FF6A1B38000, RexPayload = 0x1F3A2B50000;

    private static string Describe(VariableInfo info) =>
        $"{info.Name} = {info.Value} : {info.TypeName} {info.Kind} children={info.ChildCount} modify={info.CanModify} eval={info.EvaluateName} at={info.Address:X}";

    /// <summary>
    /// A variable at <see cref="T.At"/> pointing to an instance (Name 'Rex', Legs 4, Tricks 7) whose VMT is
    /// <see cref="DogVmt"/>; the VMT chain TDog → TAnimal → TObject with their class names.
    /// </summary>
    private static T Bed(string className = "TDog", string? abi = null)
    {
        var bed = new T(abi: abi);
        bed.Memory.PutPointer(T.At, Instance)
            .PutPointer(Instance, DogVmt).PutPointer(Instance + 8, RexPayload).PutInt32(Instance + 16, 4).PutInt32(Instance + 20, 7)
            .PutUnicodeString(RexPayload, "Rex")
            .PutVmt(DogVmt, className, AnimalVmt, Names)
            .PutVmt(AnimalVmt, "TAnimal", ObjectVmt, Names + 0x100)
            .PutVmt(ObjectVmt, "TObject", 0, Names + 0x200);
        return bed;
    }

    [Fact]
    [Trait("Req", "DBG-168")]
    [Trait("Req", "DBG-169")]
    [Trait("Req", "DBG-170")]
    public async Task DynamicType_Resolved()
    {
        var bed = Bed();

        // The variable is declared as TAnimal; the instance is a TDog.
        var node = await bed.DecodeAsync(T.TAnimal, T.At, "Pet");

        var info = bed.Info(node);
        Assert.Equal("Pet = TDog($000001F3A2B41C60) : TDog ClassInstance children=3 modify=False eval=Pet at=20000", Describe(info));
        Assert.Equal(new VariableHandle(1, 1), info.Children);
        Assert.Same(bed.Type(T.TDog), node.Type);
        // All fields of the dynamic class, ancestor fields first, read at the instance.
        Assert.Equal(
        [
            "Name = 'Rex' : string String children=0 modify=False eval=Pet.Name at=1F3A2B41C68",
            "Legs = 4 : Integer Integer children=0 modify=True eval=Pet.Legs at=1F3A2B41C70",
            "Tricks = 7 : Integer Integer children=0 modify=True eval=Pet.Tricks at=1F3A2B41C74",
        ], (await bed.ChildrenAsync(node)).Select(child => Describe(bed.Info(child))));

        // The class name is matched case-insensitively.
        var upper = Bed("TDOG");
        var sameClass = await upper.DecodeAsync(T.TAnimal, T.At, "Pet");
        Assert.Same(upper.Type(T.TDog), sameClass.Type);

        // The static type itself.
        var animal = Bed("TAnimal");
        var plain = await animal.DecodeAsync(T.TAnimal, T.At, "Pet");
        Assert.Equal("TAnimal($000001F3A2B41C60)", animal.Format(plain));
        Assert.Equal(["Name = 'Rex'", "Legs = 4"], await animal.ChildLinesAsync(plain));
    }

    [Fact]
    [Trait("Req", "DBG-170")]
    public async Task Value_ReadsNoField()
    {
        var bed = Bed();

        await bed.DecodeAsync(T.TAnimal, T.At, "Pet");

        // The variable, the VMT pointer, the class-name pointer, the name's length and its units: no field.
        Assert.Equal(["20000+8", "1F3A2B41C60+8", "7FF6A1B30010+8", "7FF6A1B37FF4+4", "7FF6A1B38000+8"], bed.Memory.Reads);
    }

    [Fact]
    [Trait("Req", "DBG-168")]
    public async Task SeveralClassesOfOneName_PreferTheDescendantOfTheStaticType()
    {
        var bed = Bed();

        // Declared as TAnimal: the TDog that descends from TAnimal, although another unit's TDog exists.
        Assert.Same(bed.Type(T.TDog), (await bed.DecodeAsync(T.TAnimal)).Type);
        // Declared as TObject: both descend from it; the first in file order.
        Assert.Same(bed.Type(T.TDog), (await bed.DecodeAsync(T.TObject)).Type);
        // Declared as the other unit's TDog: that one.
        var other = await bed.DecodeAsync(T.OtherDog, T.At, "D");
        Assert.Same(bed.Type(T.OtherDog), other.Type);
        Assert.Equal(["D.Bark"], (await bed.ChildrenAsync(other)).Select(child => child.EvaluateName));
    }

    [Fact]
    [Trait("Req", "DBG-168")]
    public async Task UnknownClass_WalksAncestors()
    {
        // The instance's class was built without type information; its parent TDog is known.
        var bed = Bed();
        bed.Memory.PutPointer(Instance, OtherVmt).PutVmt(OtherVmt, "TPoodle", DogVmt, Names + 0x300);

        var node = await bed.DecodeAsync(T.TAnimal, T.At, "Pet");

        // The real class name is shown; the fields are those of the nearest known ancestor.
        Assert.Equal("Pet = TPoodle($000001F3A2B41C60) : TPoodle ClassInstance children=3 modify=False eval=Pet at=20000", Describe(bed.Info(node)));
        Assert.Same(bed.Type(T.TDog), node.Type);
        Assert.Equal(["Name = 'Rex'", "Legs = 4", "Tricks = 7"], await bed.ChildLinesAsync(node));

        // Two unknown levels: the walk continues up to TAnimal.
        bed = Bed("TMutt");
        bed.Memory.PutPointer(Instance, OtherVmt).PutVmt(OtherVmt, "TPoodle", DogVmt, Names + 0x300);
        node = await bed.DecodeAsync(T.TObject, T.At, "Pet");
        Assert.Equal("TPoodle($000001F3A2B41C60)", bed.Format(node));
        Assert.Same(bed.Type(T.TAnimal), node.Type);
        Assert.Equal(["Name = 'Rex'", "Legs = 4"], await bed.ChildLinesAsync(node));

        // An alias entry is not a class of its own: its name is looked for among the ancestors.
        bed = Bed("TPuppy");
        node = await bed.DecodeAsync(T.TAnimal, T.At, "Pet");
        Assert.Equal("TPuppy($000001F3A2B41C60)", bed.Format(node));
        Assert.Same(bed.Type(T.TAnimal), node.Type);
    }

    [Fact]
    [Trait("Req", "DBG-167")]
    public async Task Nil()
    {
        var bed = new T();
        bed.Memory.PutPointer(T.At, 0);

        var node = await bed.DecodeAsync(T.TAnimal, T.At, "Pet");

        // A leaf of the static class.
        Assert.Equal(new VariableInfo("Pet", "nil", "TAnimal", ValueKind.ClassInstance, null, 0, false, "Pet", T.At), bed.Info(node));
        Assert.Empty(await bed.ChildrenAsync(node));
        Assert.Equal(["20000+8"], bed.Memory.Reads);
    }

    [Fact]
    [Trait("Req", "DBG-168")]
    public async Task InvalidVmt()
    {
        // The instance pointer is garbage: nothing readable there.
        var bed = new T();
        bed.Memory.PutPointer(T.At, Instance);
        var node = await bed.DecodeAsync(T.TAnimal, T.At, "Pet");
        Assert.Equal(
            "Pet = TAnimal($000001F3A2B41C60) {invalid instance} : TAnimal ClassInstance children=2 modify=False eval=Pet at=20000",
            Describe(bed.Info(node)));
        // The static type's fields are offered; each is an error of its own.
        Assert.Equal(
            ["Name = <unreadable memory at $000001F3A2B41C68>", "Legs = <unreadable memory at $000001F3A2B41C70>"],
            await bed.ChildLinesAsync(node));

        // The VMT pointer is readable but points nowhere.
        bed = Bed();
        bed.Memory.PutPointer(Instance, 0x1234567890);
        node = await bed.DecodeAsync(T.TAnimal, T.At, "Pet");
        Assert.Equal("TAnimal($000001F3A2B41C60) {invalid instance}", bed.Format(node));
        Assert.Equal(["Name = 'Rex'", "Legs = 4"], await bed.ChildLinesAsync(node));

        // No class of the chain is known.
        bed = Bed();
        bed.Memory.PutPointer(Instance, OtherVmt).PutVmt(OtherVmt, "TAlien", 0, Names + 0x300);
        node = await bed.DecodeAsync(T.TAnimal, T.At, "Pet");
        Assert.Equal("TAnimal($000001F3A2B41C60) {invalid instance}", bed.Format(node));
        Assert.True(node.InvalidInstance);

        // A parent chain that never ends is given up.
        bed = Bed();
        bed.Memory.PutPointer(Instance, OtherVmt).PutVmt(OtherVmt, "TLoop", OtherVmt, Names + 0x300);
        node = await bed.DecodeAsync(T.TAnimal, T.At, "Pet");
        Assert.Equal("TAnimal($000001F3A2B41C60) {invalid instance}", bed.Format(node));
    }

    [Fact]
    [Trait("Req", "DBG-168")]
    public async Task ClassName_OtherVmtLayouts()
    {
        // Pointer to a length-prefixed UTF-8 name at VMT+24; the parent through a pointer to a pointer at VMT+8.
        var abi = PdiFixture.Abi.Replace(
            "\"classNameOffset\": 16, \"classNameKind\": \"pointerToUnicodeString\", \"parentOffset\": 0, \"parentKind\": \"pointerToVmt\"",
            "\"classNameOffset\": 24, \"classNameKind\": \"pointerToShortString\", \"parentOffset\": 8, \"parentKind\": \"pointerToPointerToVmt\"",
            StringComparison.Ordinal);
        var bed = new T(abi: abi);
        bed.Memory.PutPointer(T.At, Instance).PutPointer(Instance, OtherVmt).Map(Instance + 8, 16)
            .PutPointer(OtherVmt + 24, Names).Put(Names, 7, (byte)'T', (byte)'P', (byte)'o', (byte)'o', (byte)'d', (byte)'l', (byte)'e')
            .PutPointer(OtherVmt + 8, Names + 0x40).PutPointer(Names + 0x40, DogVmt)
            .PutPointer(DogVmt + 24, Names + 0x80).Put(Names + 0x80, 4, (byte)'T', (byte)'D', (byte)'o', (byte)'g');

        var node = await bed.DecodeAsync(T.TAnimal, T.At, "Pet");

        Assert.Equal("TPoodle($000001F3A2B41C60)", bed.Format(node));
        Assert.Same(bed.Type(T.TDog), node.Type);

        // The name inline in the VMT.
        abi = PdiFixture.Abi.Replace(
            "\"classNameOffset\": 16, \"classNameKind\": \"pointerToUnicodeString\"", "\"classNameOffset\": 32, \"classNameKind\": \"inlineShortString\"",
            StringComparison.Ordinal);
        bed = new T(abi: abi);
        bed.Memory.PutPointer(T.At, Instance).PutPointer(Instance, OtherVmt).Map(Instance + 8, 16)
            .Put(OtherVmt + 32, 4, (byte)'T', (byte)'D', (byte)'o', (byte)'g');
        Assert.Equal("TDog($000001F3A2B41C60)", bed.Format(await bed.DecodeAsync(T.TAnimal)));
    }
}
