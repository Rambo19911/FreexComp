using System.Reflection;
using System.Runtime.InteropServices;
using FreexComp.Ide.TestHelper;

namespace FreexComp.Ide.Tests.Platform;

/// <summary>
/// I-395 (G-107 finding): the key records the console helpers write with <c>WriteConsoleInputW</c> carry their character
/// as one UTF-16 unit. Without <c>CharSet.Unicode</c> the marshaller writes it as one ANSI byte and a non-ASCII
/// character arrives as <c>?</c>.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Unit")]
public sealed class ConsoleHelperRecordTests
{
    [Theory]
    [InlineData(typeof(ConsoleKeyHelper))]
    [InlineData(typeof(ConsoleInputHelper))]
    public void KeyRecord_CarriesTheCharacterAsUtf16(Type helper)
    {
        ArgumentNullException.ThrowIfNull(helper);
        var native = helper.GetNestedType("Native", BindingFlags.NonPublic)!;
        var recordType = native.GetNestedType("INPUT_RECORD", BindingFlags.Public)!;
        var record = Activator.CreateInstance(recordType)!;
        recordType.GetField("Char")!.SetValue(record, '✓');

        var size = Marshal.SizeOf(recordType);
        var memory = Marshal.AllocHGlobal(size);
        try
        {
            for (var i = 0; i < size; i++)
                Marshal.WriteByte(memory, i, 0);
            Marshal.StructureToPtr(record, memory, fDeleteOld: false);

            Assert.Equal(20, size);
            Assert.Equal(0x2713, Marshal.ReadInt16(memory, 14)); // KEY_EVENT_RECORD.uChar at offset 14 of INPUT_RECORD
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }
    }
}
