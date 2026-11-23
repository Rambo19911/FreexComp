using FreexComp.Debugger.Tests.TestSupport;
using FreexComp.Debugger.Values.TypeInfo;

namespace FreexComp.Debugger.Tests;

/// <summary>Reader of the type-information file (interface document "Pascal debug-info sidecar": schema, version, DBG-134).</summary>
public sealed class PdiLoaderTests
{
    private const string Demo = @"C:\work\DbgDemo\DbgDemo.pas";

    [Fact]
    public void Load_DbgDemo()
    {
        var pdi = PdiFixture.LoadDbgDemo();

        Assert.Equal("DbgDemo", pdi.Program);
        Assert.Equal("codeview", pdi.Container);
        Assert.Equal(
            new PdiAbi(8, "System._Raise", 0xE0504153, "FMessage",
                new PdiUnicodeStringAbi(-12, 4, false, 2),
                new PdiAnsiStringAbi(-12, 4, -8, 2),
                new PdiDynArrayAbi(-8, 8),
                new PdiVmtAbi(16, PdiClassNameKind.PointerToUnicodeString, 0, PdiParentKind.PointerToVmt)),
            pdi.Abi);

        Assert.Equal(
            ["1 TAnimal Class 8", "2 TObject Class 8", "3 string String 8", "4 Integer Integer 4", "5 TDog Class 8", "6 Char Char 2",
             "7 Boolean Boolean 1", "8 TColor Enum 1", "9 TColors Set 1", "10 TPoint Record 8", "11  DynArray 8", "12  Pointer 8", "13 Pointer Pointer 8"],
            pdi.Types.Select(t => $"{t.Id} {t.Name} {t.Kind} {t.Size}"));
        PdiType Type(int id) => pdi.Types.Single(t => t.Id == id);

        Assert.True(Type(4).Signed);
        Assert.Equal(PdiStringFlavor.Unicode, Type(3).Flavor);
        Assert.Equal([new PdiEnumValue("clRed", 0), new PdiEnumValue("clGreen", 1), new PdiEnumValue("clBlue", 2)], Type(8).Values);
        Assert.Equal("DbgDemo", Type(8).Unit);
        Assert.Null(Type(4).Unit);
        Assert.Same(Type(8), Type(9).Base);
        Assert.Equal(0, Type(9).BitBase);
        Assert.Equal(["X:Integer@0", "Y:Integer@4"], Type(10).Fields.Select(f => $"{f.Name}:{f.Type.Name}@{f.Offset}"));
        Assert.Same(Type(4), Type(11).Element);
        Assert.Same(Type(4), Type(12).To);
        Assert.Null(Type(13).To);

        // Classes: own fields only; the ancestor chain is linked.
        var animal = Type(1);
        Assert.Equal(24, animal.InstanceSize);
        Assert.Equal(["Name:string@8:public", "Legs:Integer@16:public"], animal.Fields.Select(f => $"{f.Name}:{f.Type.Name}@{f.Offset}:{f.Visibility}"));
        Assert.Same(Type(2), animal.Ancestor);
        Assert.Null(Type(2).Ancestor);
        Assert.Same(animal, Type(5).Ancestor);
        Assert.Empty(Type(5).Fields);
        Assert.Equal(["Name", "Legs"], Type(5).AllFields().Select(f => f.Name));
        Assert.True(Type(5).IsOrDescendsFrom(Type(2)));
        Assert.False(animal.IsOrDescendsFrom(Type(5)));

        Assert.Equal(["DbgDemo", "System"], pdi.Units.Select(u => u.Name));
        var unit = pdi.Units[0];
        Assert.Equal(Demo, unit.File);
        Assert.Empty(unit.Globals);
        Assert.Equal(
            ["DbgDemo|$main|1|76|78", "TAnimal.Create|DbgDemo.TAnimal.Create|19|20|23", "TAnimal.Describe|DbgDemo.TAnimal.Describe|25|26|28",
             "TDog.Describe|DbgDemo.TDog.Describe|30|31|33", "Factorial|DbgDemo.Factorial|35|36|41", "Run|DbgDemo.Run|43|55|74"],
            unit.Routines.Select(r => $"{r.DisplayName}|{r.LinkageName}|{r.Line}|{r.BodyLine}|{r.EndLine}"));
        Assert.All(unit.Routines, r => Assert.Equal(Demo, r.File));
        Assert.All(unit.Routines, r => Assert.Same(unit, r.Unit));

        var create = unit.Routines[1];
        Assert.Same(animal, create.Self);
        Assert.Equal(["Self:Self:TAnimal", "AName:Param:string", "ALegs:Param:Integer"], create.Variables.Select(v => $"{v.Name}:{v.Kind}:{v.Type.Name}"));
        var factorial = unit.Routines[4];
        Assert.Null(factorial.Self);
        Assert.Null(factorial.Parent);
        Assert.Equal(["N:Param:Integer", "Result:Result:Integer"], factorial.Variables.Select(v => $"{v.Name}:{v.Kind}:{v.Type.Name}"));
        Assert.Equal(
            ["I:4", "Total:4", "Ch:6", "Ok:7", "Color:8", "Colors:9", "S:3", "P:10", "Nums:11", "Pet:1", "PTotal:12"],
            unit.Routines[5].Variables.Select(v => $"{v.Name}:{v.Type.Id}"));
        Assert.All(unit.Routines[5].Variables, v => Assert.Equal(PdiVariableKind.Local, v.Kind));

        Assert.Equal(new PdiGlobal("ExitCode", Type(4), "System.ExitCode"), Assert.Single(pdi.Units[1].Globals));
    }

    [Fact]
    public void Load_AllKindsAndOptionalMembers()
    {
        var pdi = PdiFixture.WithTypes("""
            { "id": 1, "name": "TSub", "unit": "U", "kind": "integer", "size": 1, "signed": false, "low": 1, "high": 9 },
            { "id": 2, "name": "Currency", "kind": "float", "size": 8, "format": "currency" },
            { "id": 3, "name": "", "kind": "string", "size": 11, "flavor": "short", "maxLength": 10 },
            { "id": 4, "name": "", "kind": "staticArray", "size": 3, "element": 1, "low": -1, "count": 3, "indexType": 1 },
            { "id": 5, "name": "", "kind": "openArray", "size": 8, "element": 1 },
            { "id": 6, "name": "TClass", "kind": "classRef", "size": 8, "of": 7 },
            { "id": 7, "name": "TObject", "unit": "System", "kind": "class", "size": 8, "ancestor": null, "instanceSize": 8, "fields": [] },
            { "id": 8, "name": "IInterface", "kind": "interface", "size": 8 },
            { "id": 9, "name": "TNotify", "kind": "procedural", "size": 16, "method": true },
            { "id": 10, "name": "Text", "kind": "opaque", "size": 624 },
            { "id": 11, "name": "TMine", "unit": "U", "kind": "integer", "size": 1, "signed": false, "aliasOf": 1, "futureMember": [1, 2] },
            { "id": 12, "name": "TFuture", "kind": "somethingNew", "size": 3 }
            """);
        PdiType Type(int id) => pdi.Types.Single(t => t.Id == id);

        Assert.Equal((false, 1L, 9L), (Type(1).Signed, Type(1).Low, Type(1).High));
        Assert.Equal(PdiFloatFormat.Currency, Type(2).FloatFormat);
        Assert.Equal((PdiStringFlavor.Short, 10), (Type(3).Flavor, Type(3).MaxLength));
        Assert.Equal((-1L, 3L), (Type(4).ArrayLow, Type(4).Count));
        Assert.Same(Type(1), Type(4).Element);
        Assert.Same(Type(1), Type(4).IndexType);
        Assert.Equal(PdiTypeKind.OpenArray, Type(5).Kind);
        Assert.Same(Type(7), Type(6).Of);
        Assert.Equal(PdiTypeKind.Interface, Type(8).Kind);
        Assert.True(Type(9).Method);
        Assert.Equal(PdiTypeKind.Opaque, Type(10).Kind);
        // Unknown members are ignored; an alias entry points at its target.
        Assert.Same(Type(1), Type(11).AliasOf);
        // A kind this reader does not know is shown as raw bytes.
        Assert.Equal(PdiTypeKind.Opaque, Type(12).Kind);
    }

    [Fact]
    public void Version2_Invalid()
    {
        var path = PdiFixture.WriteTemp("DbgDemo.pdi", PdiFixture.DbgDemoText.Replace("\"formatVersion\": 1", "\"formatVersion\": 2", StringComparison.Ordinal));
        try
        {
            var exception = Assert.Throws<DebuggerException>(() => PdiLoader.Load(path));

            Assert.Equal(DebugErrorCode.TypeInfoInvalid, exception.Code);
            Assert.Equal($"Type information file is invalid: {path}: unsupported formatVersion 2", exception.Message);
        }
        finally
        {
            PdiFixture.DeleteDirectoryOf(path);
        }
    }

    [Fact]
    [Trait("Req", "DBG-403")]
    public void Missing_NotAnError()
    {
        var path = PdiFixture.WriteTemp("Other.txt", "");
        try
        {
            Assert.Null(PdiLoader.Load(Path.Combine(Path.GetDirectoryName(path)!, "DbgDemo.pdi")));
            Assert.Null(PdiLoader.Load(Path.Combine(Path.GetDirectoryName(path)!, "NoSuchDirectory", "DbgDemo.pdi")));
        }
        finally
        {
            PdiFixture.DeleteDirectoryOf(path);
        }
    }

    private const string Head = "{ \"formatVersion\": 1, \"abi\": @ABI, ";

    [Theory]
    [InlineData("", "malformed JSON")]
    [InlineData("{ \"formatVersion\": 1, ", "malformed JSON")]
    [InlineData("[]", "the root is not an object")]
    [InlineData("{}", "the file lacks 'formatVersion'")]
    [InlineData("{ \"formatVersion\": \"1\" }", "'formatVersion' of the file is not an integer")]
    [InlineData("{ \"formatVersion\": 0 }", "unsupported formatVersion 0")]
    [InlineData("{ \"formatVersion\": 1 }", "the file lacks 'abi'")]
    [InlineData(Head + "\"units\": [] }", "the file lacks 'types'")]
    [InlineData(Head + "\"types\": [] }", "the file lacks 'units'")]
    [InlineData("{ \"formatVersion\": 1, \"abi\": { \"pointerSize\": 8 }, \"types\": [], \"units\": [] }", "abi lacks 'unicodeString'")]
    [InlineData(Head + "\"types\": [ { \"id\": 1, \"name\": \"A\", \"kind\": \"integer\", \"size\": 4 } ], \"units\": [] }", "type 1 lacks 'signed'")]
    [InlineData(Head + "\"types\": [ { \"id\": 1, \"name\": \"A\", \"kind\": \"pointer\", \"size\": 8, \"to\": 7 } ], \"units\": [] }", "type 1 refers to unknown type id 7")]
    [InlineData(Head + "\"types\": [ { \"id\": 1, \"kind\": \"opaque\", \"size\": 8 }, { \"id\": 1, \"kind\": \"opaque\", \"size\": 8 } ], \"units\": [] }", "type id 1 is not a unique positive integer")]
    [InlineData(Head + "\"types\": [ { \"id\": 1, \"kind\": \"float\", \"size\": 8, \"format\": \"extended\" } ], \"units\": [] }", "type 1 has the unknown float format 'extended'")]
    [InlineData(Head + "\"types\": [ { \"id\": 1, \"kind\": \"record\", \"size\": 8, \"fields\": [ { \"name\": \"F\", \"type\": 2, \"offset\": 0 } ] } ], \"units\": [] }", "type 1 refers to unknown type id 2")]
    [InlineData(Head + "\"types\": [], \"units\": [ { \"name\": \"U\", \"routines\": [ { \"displayName\": \"R\", \"file\": \"C:\\\\u.pas\", \"line\": 1, \"endLine\": 2, \"vars\": [ { \"name\": \"V\", \"kind\": \"local\", \"type\": 3 } ] } ] } ] }", "routine R refers to unknown type id 3")]
    public void Malformed_TypeInfoInvalid(string text, string detail)
    {
        text = text.Replace("@ABI", PdiFixture.Abi, StringComparison.Ordinal);

        var exception = Assert.Throws<DebuggerException>(() => PdiFixture.Parse(text));

        Assert.Equal(DebugErrorCode.TypeInfoInvalid, exception.Code);
        Assert.StartsWith("Type information file is invalid: test.pdi: " + detail, exception.Message);
    }

    [Fact]
    [Trait("Req", "DBG-134")]
    public void FindRoutine_Nested_PicksInnermost()
    {
        var pdi = PdiFixture.Parse($$"""
            { "formatVersion": 1, "abi": {{PdiFixture.Abi}}, "types": [], "units": [ { "name": "U", "file": "C:\\src\\U.pas", "routines": [
                { "displayName": "Outer", "linkageName": "U.Outer", "file": "C:\\src\\U.pas", "line": 10, "bodyLine": 30, "endLine": 40, "self": null, "vars": [] },
                { "displayName": "Inner", "linkageName": "U.Outer.Inner", "parent": "U.Outer", "file": "C:\\src\\U.pas", "line": 12, "bodyLine": 13, "endLine": 20, "self": null, "vars": [] },
                { "displayName": "Innermost", "linkageName": "U.Outer.Inner.Innermost", "parent": "U.Outer.Inner", "file": "C:\\src\\U.pas", "line": 14, "bodyLine": 15, "endLine": 16, "self": null, "vars": [] },
                { "displayName": "Next", "linkageName": "U.Next", "file": "C:\\src\\U.pas", "line": 42, "bodyLine": 43, "endLine": 44, "self": null, "vars": [] },
                { "displayName": "Included", "linkageName": "U.Included", "file": "C:\\src\\Part.inc", "line": 12, "bodyLine": 13, "endLine": 14, "self": null, "vars": [] } ] } ] }
            """);
        string? At(string path, int line) => pdi.FindRoutine(path, line)?.DisplayName;

        Assert.Null(At(@"C:\src\U.pas", 9));
        Assert.Equal("Outer", At(@"C:\src\U.pas", 10));
        Assert.Equal("Outer", At(@"C:\src\U.pas", 11));
        Assert.Equal("Inner", At(@"C:\src\U.pas", 12));
        Assert.Equal("Innermost", At(@"C:\src\U.pas", 14));
        Assert.Equal("Innermost", At(@"C:\src\U.pas", 16));
        Assert.Equal("Inner", At(@"C:\src\U.pas", 17));
        Assert.Equal("Inner", At(@"C:\src\U.pas", 20));
        Assert.Equal("Outer", At(@"C:\src\U.pas", 21));
        Assert.Equal("Outer", At(@"C:\src\U.pas", 40));
        Assert.Null(At(@"C:\src\U.pas", 41));
        Assert.Equal("Next", At(@"C:\src\U.pas", 44));
        // The path is compared case-insensitively and by the routine's own file (include files).
        Assert.Equal("Inner", At(@"c:\SRC\u.PAS", 13));
        Assert.Equal("Included", At(@"C:\src\Part.inc", 13));
        Assert.Null(At(@"C:\src\Other.pas", 13));
        Assert.Equal("U.Outer", pdi.FindRoutine(@"C:\src\U.pas", 13)!.Parent);

        // The demo program: its main block spans the whole file; every routine inside wins over it.
        var demo = PdiFixture.LoadDbgDemo();
        Assert.Equal("Factorial", demo.FindRoutine(Demo, 40)!.DisplayName);
        Assert.Equal("Run", demo.FindRoutine(Demo, 71)!.DisplayName);
        Assert.Equal("DbgDemo", demo.FindRoutine(Demo, 77)!.DisplayName);
        Assert.Equal("DbgDemo", demo.FindRoutine(Demo, 3)!.DisplayName);
        Assert.Null(demo.FindRoutine(Demo, 79));
    }
}
