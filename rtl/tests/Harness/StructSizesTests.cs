using System.Text;

namespace FreexComp.Rtl.Tests.Harness;

/// <summary>
/// RTL-835 (winapi.md §4): <c>SizeOf</c> of the Win32 records, and field offsets of the marked rows, as the compiler lays
/// them out. The table is turned into a TestKit program that is compiled against <c>rtl/src</c> (opc finds the binding units in <c>rtl/winapi/src</c> itself, C-184) and
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
    /// winapi.md §4 table: the Types rows (RTL-600, R-038), the kernel32 rows, the user32 rows (R-109) and the gdi32 rows (R-110), plus the kernel32
    /// records of the Windows unit that the table does not list yet.
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

        // user32 rows (R-109)
        new("TMsg", 48, ("message", 8), ("wParam", 16), ("lParam", 24), ("time", 32), ("pt", 36), ("lPrivate", 44)),
        new("TWndClassExW", 80, ("lpfnWndProc", 8), ("cbClsExtra", 16), ("hInstance", 24), ("lpszClassName", 64), ("hIconSm", 72)),
        new("TCreateStructW", 80, ("hInstance", 8), ("cy", 32), ("x", 44), ("style", 48), ("lpszName", 56), ("dwExStyle", 72)),
        new("TPaintStruct", 72, ("fErase", 8), ("rcPaint", 12), ("fRestore", 28), ("rgbReserved", 36)),
        new("TWindowPos", 40, ("hwndInsertAfter", 8), ("x", 16), ("cy", 28), ("flags", 32)),
        new("TMinMaxInfo", 40, ("ptMaxTrackSize", 32)),
        new("TNMHdr", 24, ("idFrom", 8), ("code", 16)),
        new("TScrollInfo", 28, ("nPos", 20), ("nTrackPos", 24)),
        new("TMenuItemInfoW", 80, ("wID", 16), ("hSubMenu", 24), ("dwItemData", 48), ("dwTypeData", 56), ("cch", 64), ("hbmpItem", 72)),
        new("TLogFontW", 92, ("lfItalic", 20), ("lfPitchAndFamily", 27), ("lfFaceName", 28)),
        new("TNonClientMetricsW", 504, ("lfCaptionFont", 24), ("iSmCaptionWidth", 116), ("lfSmCaptionFont", 124), ("iMenuWidth", 216),
            ("lfMenuFont", 224), ("lfStatusFont", 316), ("lfMessageFont", 408), ("iPaddedBorderWidth", 500)),
        new("TDrawItemStruct", 64, ("hwndItem", 24), ("hDC", 32), ("rcItem", 40), ("itemData", 56)),
        new("TMeasureItemStruct", 32, ("itemHeight", 16), ("itemData", 24)),
        new("TWindowPlacement", 44, ("ptMinPosition", 12), ("rcNormalPosition", 28)),
        new("TMonitorInfo", 40, ("rcMonitor", 4), ("rcWork", 20), ("dwFlags", 36)),
        new("TTrackMouseEvent", 24, ("hwndTrack", 8), ("dwHoverTime", 16)),
        new("TIconInfo", 32, ("xHotspot", 4), ("hbmMask", 16), ("hbmColor", 24)),
        new("TAccel", 6, ("key", 2), ("cmd", 4)),

        // gdi32 rows (R-110)
        new("TTextMetricW", 60, ("tmFirstChar", 44), ("tmItalic", 52), ("tmCharSet", 56)),
        new("TBitmapInfoHeader", 40, ("biPlanes", 12), ("biCompression", 16), ("biClrImportant", 36)),
        new("TRGBQuad", 4, ("rgbRed", 2)),
        new("TBitmapInfo", 44, ("bmiColors", 40)),
        new("TBitmapRec", 32, ("bmPlanes", 16), ("bmBits", 24)),
        new("TBitmapFileHeader", 14, ("bfSize", 2), ("bfReserved1", 6), ("bfOffBits", 10)),
        new("TLogBrush", 16, ("lbHatch", 8)),
    ];

    [Fact]
    [Trait("Req", "RTL-835")]
    [Trait("Req", "RTL-830")]
    [Trait("Req", "RTL-833")]
    public Task StructSizes() => RunAsync("StructSizes", Units, Rows);

    /// <summary>Generates the program of <paramref name="rows"/> as <c>winapi/&lt;name&gt;</c> and runs it through the golden runner.</summary>
    internal static async Task RunAsync(string name, string[] units, Row[] rows)
    {
        var dir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var (program, expected) = Generate(name, units, rows);
            var source = Path.Combine(dir, name + ".pas");
            File.WriteAllText(source, program);
            File.WriteAllText(Path.Combine(dir, name + ".out"), expected);

            var failure = await GoldenRunner.RunAsync(new RtlCase("winapi/" + name, source), RtlSetup.Default, TestContext.Current.CancellationToken);

            Assert.True(failure is null, failure);
        }
        finally
        {
            GoldenRunner.TryDelete(dir);
        }
    }

    /// <summary>The program (one <c>CheckEqI</c> per size and offset) and its expected output.</summary>
    public static (string Program, string Expected) Generate(string name, string[] units, Row[] rows)
    {
        var program = new StringBuilder();
        var expected = new StringBuilder();
        var checks = 0;
        program.Append("program ").Append(name).Append(";\nuses ").Append(string.Join(", ", units)).Append(", TestKit;\n\n");
        for (var i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
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
                var label = row.Name + "." + field;
                program.Append("  CheckEqI(").Append(offset).Append(", NativeInt(NativeUInt(@R.").Append(field).Append(") - NativeUInt(@R)), '")
                    .Append(label).Append("');\n");
                expected.Append("ok ").Append(label).Append('\n');
                checks++;
            }

            program.Append("end;\n\n");
        }

        program.Append("begin\n");
        for (var i = 0; i < rows.Length; i++)
        {
            program.Append("  Check").Append(i).Append(";\n");
        }

        program.Append("  Done;\nend.\n");
        expected.Append("# passed ").Append(checks).Append(" failed 0\n");
        return (program.ToString(), expected.ToString());
    }
}
