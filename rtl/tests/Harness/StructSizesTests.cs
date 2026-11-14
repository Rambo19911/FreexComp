using System.Text;

namespace FreexComp.Rtl.Tests.Harness;

/// <summary>
/// RTL-835 (winapi.md §4): <c>SizeOf</c> of the Win32 records, and field offsets of the marked rows, as the compiler lays
/// them out. The table is turned into a TestKit program that is compiled against <c>rtl/src</c> and the binding units and
/// run through the golden runner. Rows are added with the units that declare the records (kernel32 rows: R-035).
/// </summary>
[Trait("Phase", "2")]
[Trait("Kind", "Golden")]
[Trait("Category", "Toolchain")]
[Trait("Category", "Integration")]
public sealed class StructSizesTests
{
    /// <summary>One record: expected <c>SizeOf</c> and (field, offset) pairs.</summary>
    public sealed record Row(string Name, int Size, params (string Field, int Offset)[] Offsets);

    /// <summary>The units the generated program uses (besides TestKit).</summary>
    public static readonly string[] Units = ["Types", "Windows"];

    /// <summary>
    /// winapi.md §4 table: the Types rows (RTL-600, R-038) and the kernel32 rows, plus the kernel32 records of the Windows unit
    /// that the table does not list yet.
    /// </summary>
    public static readonly Row[] Rows =
    [
        new("TPoint", 8, ("Y", 4)),
        new("TRect", 16, ("Top", 4), ("Right", 8), ("Bottom", 12)),
        new("TSize", 8, ("cy", 4)),
        new("TSystemTime", 16, ("wDayOfWeek", 4), ("wMilliseconds", 14)),
        new("TFileTime", 8, ("dwHighDateTime", 4)),
        new("TWin32FindDataW", 592, ("ftCreationTime", 4), ("nFileSizeHigh", 28), ("cFileName", 44), ("cAlternateFileName", 564)),
        new("TWin32FileAttributeData", 36, ("ftLastWriteTime", 20), ("nFileSizeLow", 32)),
        new("TRTLCriticalSection", 40, ("LockCount", 8), ("OwningThread", 16), ("SpinCount", 32)),
        new("TExceptionRecord", 152, ("ExceptionRecord", 8), ("ExceptionAddress", 16), ("NumberParameters", 24), ("ExceptionInformation", 32)),
        new("TTimeZoneInformation", 172, ("StandardDate", 68), ("StandardBias", 84), ("DaylightDate", 152), ("DaylightBias", 168)),
        new("TSecurityAttributes", 24, ("lpSecurityDescriptor", 8), ("bInheritHandle", 16)),
        new("TOverlapped", 32, ("Offset", 16), ("OffsetHigh", 20), ("hEvent", 24)),
        new("TSystemInfo", 48, ("dwPageSize", 4), ("lpMinimumApplicationAddress", 8), ("dwActiveProcessorMask", 24), ("dwNumberOfProcessors", 32), ("wProcessorLevel", 44)),
        new("TOSVersionInfoW", 276, ("dwPlatformId", 16), ("szCSDVersion", 20)),
        new("TOSVersionInfoExW", 284, ("wServicePackMajor", 276), ("wSuiteMask", 280), ("wProductType", 282), ("wReserved", 283)),
    ];

    [Fact]
    [Trait("Req", "RTL-835")]
    [Trait("Req", "RTL-830")]
    [Trait("Req", "RTL-833")]
    public async Task StructSizes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", "StructSizes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var (program, expected) = Generate(Path.Combine(RtlSetup.RepositoryRoot, "rtl", "winapi", "src"));
            var source = Path.Combine(dir, "StructSizes.pas");
            File.WriteAllText(source, program);
            File.WriteAllText(Path.Combine(dir, "StructSizes.out"), expected);

            var failure = await GoldenRunner.RunAsync(new RtlCase("winapi/StructSizes", source), RtlSetup.Default, TestContext.Current.CancellationToken);

            Assert.True(failure is null, failure);
        }
        finally
        {
            GoldenRunner.TryDelete(dir);
        }
    }

    /// <summary>The program (one <c>CheckEqI</c> per size and offset) and its expected output.</summary>
    public static (string Program, string Expected) Generate(string bindingDirectory)
    {
        var program = new StringBuilder();
        var expected = new StringBuilder();
        var checks = 0;
        program.Append("// opc: -U ").Append(bindingDirectory).Append('\n');
        program.Append("program StructSizes;\nuses ").Append(string.Join(", ", Units)).Append(", TestKit;\n\n");
        for (var i = 0; i < Rows.Length; i++)
        {
            var row = Rows[i];
            program.Append("procedure Check").Append(i).Append(";\n");
            if (row.Offsets.Length > 0)
            {
                program.Append("var\n  R: ").Append(row.Name).Append(";\n");
            }

            program.Append("begin\n");
            program.Append("  CheckEqI(").Append(row.Size).Append(", SizeOf(").Append(row.Name).Append("), '").Append(row.Name).Append("');\n");
            expected.Append("ok ").Append(row.Name).Append('\n');
            checks++;
            foreach (var (field, offset) in row.Offsets)
            {
                var name = row.Name + "." + field;
                program.Append("  CheckEqI(").Append(offset).Append(", NativeInt(NativeUInt(@R.").Append(field).Append(") - NativeUInt(@R)), '")
                    .Append(name).Append("');\n");
                expected.Append("ok ").Append(name).Append('\n');
                checks++;
            }

            program.Append("end;\n\n");
        }

        program.Append("begin\n");
        for (var i = 0; i < Rows.Length; i++)
        {
            program.Append("  Check").Append(i).Append(";\n");
        }

        program.Append("  Done;\nend.\n");
        expected.Append("# passed ").Append(checks).Append(" failed 0\n");
        return (program.ToString(), expected.ToString());
    }
}
