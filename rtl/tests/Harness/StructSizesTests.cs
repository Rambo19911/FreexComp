using System.Text;
using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

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
    public static readonly string[] Units = ["Types", "Windows", "CommCtrl", "CommDlg", "ShellAPI", "ActiveX", "RichEdit", "Wincodec"];

    /// <summary>
    /// winapi.md §4 table: the Types rows (RTL-600, R-038), the kernel32 rows, the user32 rows (R-109), the gdi32 rows (R-110), the comctl32 rows
    /// (R-112) and the comdlg32 / shell32 rows (R-113), plus every other record of the binding units (R-114, enforced by
    /// <see cref="StructSizes_CoversEveryBindingRecord"/>). Offsets of procedural-type fields are not listed: <c>@R.Proc</c> is the
    /// field's value, not its address.
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
        new("TActCtxW", 56, ("dwFlags", 4), ("lpSource", 8), ("wProcessorArchitecture", 16), ("wLangId", 18), ("lpAssemblyDirectory", 24), ("lpResourceName", 32), ("lpApplicationName", 40), ("hModule", 48)),

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

        // comctl32 rows (R-112)
        new("TInitCommonControlsEx", 8, ("dwICC", 4)),
        new("TNMCustomDraw", 80, ("dwDrawStage", 24), ("hdc", 32), ("rc", 40), ("dwItemSpec", 56), ("uItemState", 64), ("lItemlParam", 72)),
        new("TTCItemW", 40, ("pszText", 16), ("cchTextMax", 24), ("iImage", 28), ("lParam", 32)),
        new("TTCHitTestInfo", 12, ("flags", 8)),
        new("TTBButton", 32, ("fsState", 8), ("fsStyle", 9), ("bReserved", 10), ("dwData", 16), ("iString", 24)),
        new("TTBAddBitmap", 16, ("nID", 8)),
        new("TTBButtonInfoW", 48, ("fsState", 16), ("cx", 18), ("lParam", 24), ("pszText", 32), ("cchText", 40)),
        new("TNMToolBarW", 96, ("iItem", 24), ("tbButton", 32), ("cchText", 64), ("pszText", 72), ("rcButton", 80)),
        new("TPBRange", 8, ("iHigh", 4)),
        new("TUDAccel", 8, ("nInc", 4)),
        new("TNMUpDown", 32, ("iPos", 24), ("iDelta", 28)),
        new("TToolInfoW", 72, ("hwnd", 8), ("uId", 16), ("rect", 24), ("hinst", 40), ("lpszText", 48), ("lParam", 56), ("lpReserved", 64)),
        new("TNMTTDispInfoW", 216, ("lpszText", 24), ("szText", 32), ("hinst", 192), ("uFlags", 200), ("lParam", 208)),
        new("THDItemW", 72, ("pszText", 8), ("hbm", 16), ("cchTextMax", 24), ("fmt", 28), ("lParam", 32), ("iImage", 40), ("iOrder", 44),
            ("&type", 48), ("pvFilter", 56), ("state", 64)),
        new("THDLayout", 16, ("pwpos", 8)),
        new("THDHitTestInfo", 16, ("flags", 8), ("iItem", 12)),
        new("TNMHeaderW", 40, ("iItem", 24), ("iButton", 28), ("pitem", 32)),
        new("TLVItemW", 88, ("iSubItem", 8), ("state", 12), ("stateMask", 16), ("pszText", 24), ("cchTextMax", 32), ("iImage", 36),
            ("lParam", 40), ("iIndent", 48), ("iGroupId", 52), ("cColumns", 56), ("puColumns", 64), ("piColFmt", 72), ("iGroup", 80)),
        new("TLVColumnW", 56, ("pszText", 16), ("cchTextMax", 24), ("iSubItem", 28), ("iOrder", 36), ("cxIdeal", 48)),
        new("TNMListView", 64, ("iItem", 24), ("uNewState", 32), ("uChanged", 40), ("ptAction", 44), ("lParam", 56)),
        new("TNMItemActivate", 72, ("ptAction", 44), ("lParam", 56), ("uKeyFlags", 64)),
        new("TLVDispInfoW", 112, ("item", 24)),
        new("TLVHitTestInfo", 24, ("flags", 8), ("iItem", 12), ("iSubItem", 16), ("iGroup", 20)),
        new("TLVFindInfoW", 40, ("psz", 8), ("lParam", 16), ("pt", 24), ("vkDirection", 32)),
        new("TNMLVCustomDraw", 136, ("clrText", 80), ("iSubItem", 88), ("dwItemType", 92), ("clrFace", 96), ("iStateId", 112), ("rcText", 116),
            ("uAlign", 132)),
        new("TTVItemW", 56, ("hItem", 8), ("state", 16), ("stateMask", 20), ("pszText", 24), ("cchTextMax", 32), ("iImage", 36),
            ("iSelectedImage", 40), ("cChildren", 44), ("lParam", 48)),
        new("TTVItemExW", 80, ("iIntegral", 56), ("uStateEx", 60), ("hwnd", 64), ("iExpandedImage", 72), ("iReserved", 76)),
        new("TTVInsertStructW", 96, ("hInsertAfter", 8), ("item", 16), ("itemexPadding", 72)),
        new("TNMTreeViewW", 152, ("action", 24), ("itemOld", 32), ("itemNew", 88), ("ptDrag", 144)),
        new("TTVDispInfoW", 80, ("item", 24)),
        new("TTVHitTestInfo", 24, ("flags", 8), ("hItem", 16)),
        new("TTVSortCB", 24, ("lParam", 16)),
        new("TNMTVCustomDraw", 96, ("clrText", 80), ("clrTextBk", 84), ("iLevel", 88)),

        // keyboard notifications, 1-byte packed (R-186)
        new("TNMLVKeyDown", 30, ("hdr", 0), ("wVKey", 24), ("flags", 26)),
        new("TNMTVKeyDown", 30, ("hdr", 0), ("wVKey", 24), ("flags", 26)),
        new("TNMTCKeyDown", 30, ("hdr", 0), ("wVKey", 24), ("flags", 26)),

        // comdlg32 and shell32 rows (R-113)
        new("TOpenFilenameW", 152, ("hwndOwner", 8), ("hInstance", 16), ("lpstrFilter", 24), ("nMaxCustFilter", 40), ("nFilterIndex", 44),
            ("lpstrFile", 48), ("nMaxFile", 56), ("lpstrFileTitle", 64), ("nMaxFileTitle", 72), ("lpstrInitialDir", 80), ("lpstrTitle", 88),
            ("Flags", 96), ("nFileOffset", 100), ("nFileExtension", 102), ("lpstrDefExt", 104), ("lCustData", 112), ("lpTemplateName", 128),
            ("pvReserved", 136), ("dwReserved", 144), ("FlagsEx", 148)),
        new("TChooseColorW", 72, ("rgbResult", 24), ("lpCustColors", 32), ("Flags", 40), ("lCustData", 48), ("lpTemplateName", 64)),
        new("TChooseFontW", 104, ("hDC", 16), ("lpLogFont", 24), ("iPointSize", 32), ("Flags", 36), ("rgbColors", 40), ("lCustData", 48),
            ("lpTemplateName", 64), ("hInstance", 72), ("lpszStyle", 80), ("nFontType", 88), ("nSizeMin", 92), ("nSizeMax", 96)),
        new("TDropFiles", 20, ("pt", 4), ("fNC", 12), ("fWide", 16)),

        // oleaut32 rows (R-230; com-support.md RTL-1006)
        new("TVariantArg", 24, ("wReserved1", 2), ("lVal", 8), ("bstrVal", 8), ("byRef", 8)),
        new("TDispParams", 24, ("rgdispidNamedArgs", 8), ("cArgs", 16), ("cNamedArgs", 20)),
        new("TExcepInfo", 64, ("bstrSource", 8), ("bstrHelpFile", 24), ("dwHelpContext", 32), ("pvReserved", 40), ("pfnDeferredFillIn", 48), ("scode", 56)),

        // rich edit rows (R-235; RTL-1023, RTL-1024): 4-byte packing, so pointer-sized members are not aligned to 8
        new("TCharRange", 8, ("cpMax", 4)),
        new("TTextRangeW", 16, ("lpstrText", 8)),
        new("TFindTextExW", 24, ("lpstrText", 8), ("chrgText", 16)),
        new("TCharFormat2W", 116, ("crTextColor", 20), ("bCharSet", 24), ("szFaceName", 26), ("wWeight", 90), ("crBackColor", 96), ("sStyle", 108), ("bUnderlineColor", 115)),
        new("TParaFormat2", 188, ("wNumbering", 8), ("dxStartIndent", 12), ("wAlignment", 24), ("cTabCount", 26), ("rgxTabs", 28), ("dySpaceBefore", 156), ("sStyle", 168), ("wBorders", 186)),
        new("TEditStream", 20, ("dwError", 8)),
        new("TGetTextLengthEx", 8, ("codepage", 4)),
        new("TGetTextEx", 28, ("codepage", 8), ("lpDefaultChar", 12), ("lpUsedDefChar", 20)),
        new("TSetTextEx", 8, ("codepage", 4)),
        new("TENLink", 52, ("msg", 24), ("wParam", 28), ("lParam", 36), ("chrg", 44)),
        new("TSelChange", 36, ("chrg", 24), ("seltyp", 32)),
        new("TENProtected", 52, ("msg", 24), ("wParam", 28), ("lParam", 36), ("chrg", 44)),

        // image codec rows (R-237; RTL-1031, RTL-1033, RTL-1035)
        new("TStatStg", 80, ("cbSize", 16), ("mtime", 24), ("atime", 40), ("grfMode", 48), ("clsid", 56), ("grfStateBits", 72), ("reserved", 76)),
        new("TPropVariant", 24, ("wReserved1", 2), ("uiVal", 8), ("ulVal", 8), ("pwszVal", 8)),
        new("TWICRect", 16, ("Y", 4), ("Width", 8), ("Height", 12)),
        new("TBlendFunction", 4, ("BlendFlags", 1), ("SourceConstantAlpha", 2), ("AlphaFormat", 3)),
    ];

    [Fact]
    [Trait("Req", "RTL-835")]
    [Trait("Req", "RTL-830")]
    [Trait("Req", "RTL-833")]
    public Task StructSizes() => RunAsync("StructSizes", Units, Rows);

    /// <summary>
    /// RTL-835 completeness (R-114): every record type declared in the interface of a binding unit
    /// (<c>rtl/winapi/src</c>) has a row here or in <see cref="MessageRecordSizesTests.Rows"/>, so a new record cannot
    /// ship without its size check. Pointer and alias declarations (<c>PFoo = ^TFoo</c>, <c>FOO = TFoo</c>) are not records.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Req", "RTL-835")]
    public void StructSizes_CoversEveryBindingRecord()
    {
        var dir = Path.Combine(RtlSetup.RepositoryRoot, "rtl", "winapi", "src");
        var covered = Rows.Concat(MessageRecordSizesTests.Rows).Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        foreach (var path in Directory.GetFiles(dir, "*.pas").OrderBy(f => f, StringComparer.Ordinal))
        {
            var tree = SyntaxTree.Parse(SourceText.From(path, File.ReadAllText(path)), null, PhysicalFileSystem.Instance);
            var root = Assert.IsType<UnitSyntax>(tree.Root);
            missing.AddRange(root.InterfaceSection.DescendantNodes().OfType<TypeDeclarationSyntax>()
                .Where(t => t.Type is RecordTypeSyntax)
                .Select(t => (string)t.Identifier.Value!)
                .Where(name => !covered.Contains(name))
                .Select(name => $"{Path.GetFileName(path)}: {name}"));
        }

        Assert.True(missing.Count == 0, "records without a StructSizes row:\n" + string.Join("\n", missing));
    }

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
