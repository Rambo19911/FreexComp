using System.Text;
using FreexComp.Debugger.Values.TypeInfo;

namespace FreexComp.Debugger.Tests.TestSupport;

/// <summary>The hand-maintained type-information fixture of the demo program and helpers for variants of it.</summary>
internal static class PdiFixture
{
    public static string DbgDemoPath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "pdi", "DbgDemo.pdi");

    public static string DbgDemoText => File.ReadAllText(DbgDemoPath, Encoding.UTF8);

    public static PdiFile LoadDbgDemo() => PdiLoader.Load(DbgDemoPath) ?? throw new FileNotFoundException(DbgDemoPath);

    public static PdiFile Parse(string json) => PdiLoader.Parse(Encoding.UTF8.GetBytes(json), "test.pdi");

    /// <summary>The <c>abi</c> block of the fixture, as JSON text.</summary>
    public const string Abi = """
        { "pointerSize": 8, "raiseHookSymbol": "System._Raise", "pascalExceptionCode": 3763355987, "exceptionMessageField": "FMessage",
          "unicodeString": { "lengthOffset": -12, "lengthSize": 4, "lengthUnit": "chars", "charSize": 2 },
          "ansiString": { "lengthOffset": -12, "lengthSize": 4, "codePageOffset": -8, "codePageSize": 2 },
          "dynArray": { "lengthOffset": -8, "lengthSize": 8 },
          "vmt": { "classNameOffset": 16, "classNameKind": "pointerToUnicodeString", "parentOffset": 0, "parentKind": "pointerToVmt" },
          "set": { "bitOrder": "lsb0" } }
        """;

    /// <summary>A file with the given <c>types</c> entries, no units, and (unless given) the fixture's layout constants.</summary>
    public static PdiFile WithTypes(string types, string abi = Abi) =>
        Parse($$"""{ "formatVersion": 1, "program": "T", "container": "codeview", "abi": {{abi}}, "types": [ {{types}} ], "units": [] }""");

    /// <summary>Writes text into a fresh temp directory (deleted by the caller through <see cref="DeleteDirectoryOf"/>).</summary>
    public static string WriteTemp(string fileName, string text)
    {
        var directory = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    public static void DeleteDirectoryOf(string path) => Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
}
