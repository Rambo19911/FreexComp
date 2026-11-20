using System.Text;

namespace FreexComp.Rtl.Tests.Harness;

/// <summary>
/// RTL-824 / RTL-850 (winapi.md §3, §6; task R-185): the control window styles of the Windows unit (<c>BS_</c>, <c>ES_</c>,
/// <c>LBS_</c>, <c>CBS_</c>, <c>SS_</c>, <c>SBS_</c>) and the control messages and notification codes of the Messages unit
/// (<c>EM_</c>/<c>EN_</c>, <c>BM_</c>/<c>BN_</c>/<c>BST_</c>, <c>LB_</c>/<c>LBN_</c>, <c>CB_</c>/<c>CBN_</c>, <c>STM_</c>/<c>STN_</c>,
/// <c>SBM_</c>) have the values of the Win32 reference. Like <see cref="StructSizesTests"/>, the table is turned into a TestKit
/// program (one <c>CheckEqI</c> per constant) that is compiled and run through the golden runner.
/// </summary>
[Trait("Phase", "4")]
[Trait("Kind", "Golden")]
[Trait("Category", "Toolchain")]
[Trait("Category", "Integration")]
public sealed class ConstantValuesTests
{
    /// <summary>One constant and its documented value.</summary>
    public sealed record Row(string Name, long Value);

    /// <summary>Control window styles (winuser.h: Button Styles, Edit Control Styles, List Box Styles, Combo Box Styles, Static Control Styles, Scroll Bar Control Styles).</summary>
    public static readonly Row[] StyleRows =
    [
        // button styles
        new("BS_PUSHBUTTON", 0x0000), new("BS_DEFPUSHBUTTON", 0x0001), new("BS_CHECKBOX", 0x0002), new("BS_AUTOCHECKBOX", 0x0003),
        new("BS_RADIOBUTTON", 0x0004), new("BS_3STATE", 0x0005), new("BS_AUTO3STATE", 0x0006), new("BS_GROUPBOX", 0x0007),
        new("BS_USERBUTTON", 0x0008), new("BS_AUTORADIOBUTTON", 0x0009), new("BS_PUSHBOX", 0x000A), new("BS_OWNERDRAW", 0x000B),
        new("BS_TYPEMASK", 0x000F), new("BS_LEFTTEXT", 0x0020), new("BS_TEXT", 0x0000), new("BS_ICON", 0x0040), new("BS_BITMAP", 0x0080),
        new("BS_LEFT", 0x0100), new("BS_RIGHT", 0x0200), new("BS_CENTER", 0x0300), new("BS_TOP", 0x0400), new("BS_BOTTOM", 0x0800),
        new("BS_VCENTER", 0x0C00), new("BS_PUSHLIKE", 0x1000), new("BS_MULTILINE", 0x2000), new("BS_NOTIFY", 0x4000),
        new("BS_FLAT", 0x8000), new("BS_RIGHTBUTTON", 0x0020),

        // edit control styles
        new("ES_LEFT", 0x0000), new("ES_CENTER", 0x0001), new("ES_RIGHT", 0x0002), new("ES_MULTILINE", 0x0004),
        new("ES_UPPERCASE", 0x0008), new("ES_LOWERCASE", 0x0010), new("ES_PASSWORD", 0x0020), new("ES_AUTOVSCROLL", 0x0040),
        new("ES_AUTOHSCROLL", 0x0080), new("ES_NOHIDESEL", 0x0100), new("ES_OEMCONVERT", 0x0400), new("ES_READONLY", 0x0800),
        new("ES_WANTRETURN", 0x1000), new("ES_NUMBER", 0x2000),

        // list box styles
        new("LBS_NOTIFY", 0x0001), new("LBS_SORT", 0x0002), new("LBS_NOREDRAW", 0x0004), new("LBS_MULTIPLESEL", 0x0008),
        new("LBS_OWNERDRAWFIXED", 0x0010), new("LBS_OWNERDRAWVARIABLE", 0x0020), new("LBS_HASSTRINGS", 0x0040),
        new("LBS_USETABSTOPS", 0x0080), new("LBS_NOINTEGRALHEIGHT", 0x0100), new("LBS_MULTICOLUMN", 0x0200),
        new("LBS_WANTKEYBOARDINPUT", 0x0400), new("LBS_EXTENDEDSEL", 0x0800), new("LBS_DISABLENOSCROLL", 0x1000),
        new("LBS_NODATA", 0x2000), new("LBS_NOSEL", 0x4000), new("LBS_COMBOBOX", 0x8000), new("LBS_STANDARD", 0x00A00003),

        // combo box styles
        new("CBS_SIMPLE", 0x0001), new("CBS_DROPDOWN", 0x0002), new("CBS_DROPDOWNLIST", 0x0003), new("CBS_OWNERDRAWFIXED", 0x0010),
        new("CBS_OWNERDRAWVARIABLE", 0x0020), new("CBS_AUTOHSCROLL", 0x0040), new("CBS_OEMCONVERT", 0x0080), new("CBS_SORT", 0x0100),
        new("CBS_HASSTRINGS", 0x0200), new("CBS_NOINTEGRALHEIGHT", 0x0400), new("CBS_DISABLENOSCROLL", 0x0800),
        new("CBS_UPPERCASE", 0x2000), new("CBS_LOWERCASE", 0x4000),

        // static control styles
        new("SS_LEFT", 0x0000), new("SS_CENTER", 0x0001), new("SS_RIGHT", 0x0002), new("SS_ICON", 0x0003), new("SS_BLACKRECT", 0x0004),
        new("SS_GRAYRECT", 0x0005), new("SS_WHITERECT", 0x0006), new("SS_BLACKFRAME", 0x0007), new("SS_GRAYFRAME", 0x0008),
        new("SS_WHITEFRAME", 0x0009), new("SS_USERITEM", 0x000A), new("SS_SIMPLE", 0x000B), new("SS_LEFTNOWORDWRAP", 0x000C),
        new("SS_OWNERDRAW", 0x000D), new("SS_BITMAP", 0x000E), new("SS_ENHMETAFILE", 0x000F), new("SS_ETCHEDHORZ", 0x0010),
        new("SS_ETCHEDVERT", 0x0011), new("SS_ETCHEDFRAME", 0x0012), new("SS_TYPEMASK", 0x001F), new("SS_REALSIZECONTROL", 0x0040),
        new("SS_NOPREFIX", 0x0080), new("SS_NOTIFY", 0x0100), new("SS_CENTERIMAGE", 0x0200), new("SS_RIGHTJUST", 0x0400),
        new("SS_REALSIZEIMAGE", 0x0800), new("SS_SUNKEN", 0x1000), new("SS_EDITCONTROL", 0x2000), new("SS_ENDELLIPSIS", 0x4000),
        new("SS_PATHELLIPSIS", 0x8000), new("SS_WORDELLIPSIS", 0xC000), new("SS_ELLIPSISMASK", 0xC000),

        // scroll bar control styles
        new("SBS_HORZ", 0x0000), new("SBS_VERT", 0x0001), new("SBS_TOPALIGN", 0x0002), new("SBS_LEFTALIGN", 0x0002),
        new("SBS_BOTTOMALIGN", 0x0004), new("SBS_RIGHTALIGN", 0x0004), new("SBS_SIZEBOXTOPLEFTALIGN", 0x0002),
        new("SBS_SIZEBOXBOTTOMRIGHTALIGN", 0x0004), new("SBS_SIZEBOX", 0x0008), new("SBS_SIZEGRIP", 0x0010),
    ];

    /// <summary>Control messages and notification codes of the Messages unit (Edit, Button, List Box, Combo Box, Static and Scroll Bar control reference).</summary>
    public static readonly Row[] MessageRows =
    [
        // edit control messages and notifications
        new("EM_GETSEL", 0x00B0), new("EM_SETSEL", 0x00B1), new("EM_GETRECT", 0x00B2), new("EM_SETRECT", 0x00B3), new("EM_SETRECTNP", 0x00B4),
        new("EM_SCROLL", 0x00B5), new("EM_LINESCROLL", 0x00B6), new("EM_SCROLLCARET", 0x00B7), new("EM_GETMODIFY", 0x00B8),
        new("EM_SETMODIFY", 0x00B9), new("EM_GETLINECOUNT", 0x00BA), new("EM_LINEINDEX", 0x00BB), new("EM_SETHANDLE", 0x00BC),
        new("EM_GETHANDLE", 0x00BD), new("EM_GETTHUMB", 0x00BE), new("EM_LINELENGTH", 0x00C1), new("EM_REPLACESEL", 0x00C2),
        new("EM_GETLINE", 0x00C4), new("EM_LIMITTEXT", 0x00C5), new("EM_SETLIMITTEXT", 0x00C5), new("EM_CANUNDO", 0x00C6),
        new("EM_UNDO", 0x00C7), new("EM_FMTLINES", 0x00C8), new("EM_LINEFROMCHAR", 0x00C9), new("EM_SETTABSTOPS", 0x00CB),
        new("EM_SETPASSWORDCHAR", 0x00CC), new("EM_EMPTYUNDOBUFFER", 0x00CD), new("EM_GETFIRSTVISIBLELINE", 0x00CE),
        new("EM_SETREADONLY", 0x00CF), new("EM_SETWORDBREAKPROC", 0x00D0), new("EM_GETWORDBREAKPROC", 0x00D1),
        new("EM_GETPASSWORDCHAR", 0x00D2), new("EM_SETMARGINS", 0x00D3), new("EM_GETMARGINS", 0x00D4), new("EM_GETLIMITTEXT", 0x00D5),
        new("EM_POSFROMCHAR", 0x00D6), new("EM_CHARFROMPOS", 0x00D7), new("EM_SETIMESTATUS", 0x00D8), new("EM_GETIMESTATUS", 0x00D9),
        new("ECM_FIRST", 0x1500), new("EM_SETCUEBANNER", 0x1501), new("EM_GETCUEBANNER", 0x1502),
        new("EC_LEFTMARGIN", 0x0001), new("EC_RIGHTMARGIN", 0x0002), new("EC_USEFONTINFO", 0xFFFF),
        new("EN_SETFOCUS", 0x0100), new("EN_KILLFOCUS", 0x0200), new("EN_CHANGE", 0x0300), new("EN_UPDATE", 0x0400),
        new("EN_ERRSPACE", 0x0500), new("EN_MAXTEXT", 0x0501), new("EN_HSCROLL", 0x0601), new("EN_VSCROLL", 0x0602),
        new("EN_ALIGN_LTR_EC", 0x0700), new("EN_ALIGN_RTL_EC", 0x0701),

        // button messages, notifications and states
        new("BM_GETCHECK", 0x00F0), new("BM_SETCHECK", 0x00F1), new("BM_GETSTATE", 0x00F2), new("BM_SETSTATE", 0x00F3),
        new("BM_SETSTYLE", 0x00F4), new("BM_CLICK", 0x00F5), new("BM_GETIMAGE", 0x00F6), new("BM_SETIMAGE", 0x00F7),
        new("BM_SETDONTCLICK", 0x00F8),
        new("BN_CLICKED", 0), new("BN_PAINT", 1), new("BN_HILITE", 2), new("BN_UNHILITE", 3), new("BN_DISABLE", 4),
        new("BN_DOUBLECLICKED", 5), new("BN_PUSHED", 2), new("BN_UNPUSHED", 3), new("BN_DBLCLK", 5), new("BN_SETFOCUS", 6),
        new("BN_KILLFOCUS", 7),
        new("BST_UNCHECKED", 0x0000), new("BST_CHECKED", 0x0001), new("BST_INDETERMINATE", 0x0002), new("BST_PUSHED", 0x0004),
        new("BST_FOCUS", 0x0008),

        // list box messages and notifications
        new("LB_OKAY", 0), new("LB_ERR", -1), new("LB_ERRSPACE", -2),
        new("LB_ADDSTRING", 0x0180), new("LB_INSERTSTRING", 0x0181), new("LB_DELETESTRING", 0x0182), new("LB_SELITEMRANGEEX", 0x0183),
        new("LB_RESETCONTENT", 0x0184), new("LB_SETSEL", 0x0185), new("LB_SETCURSEL", 0x0186), new("LB_GETSEL", 0x0187),
        new("LB_GETCURSEL", 0x0188), new("LB_GETTEXT", 0x0189), new("LB_GETTEXTLEN", 0x018A), new("LB_GETCOUNT", 0x018B),
        new("LB_SELECTSTRING", 0x018C), new("LB_DIR", 0x018D), new("LB_GETTOPINDEX", 0x018E), new("LB_FINDSTRING", 0x018F),
        new("LB_GETSELCOUNT", 0x0190), new("LB_GETSELITEMS", 0x0191), new("LB_SETTABSTOPS", 0x0192),
        new("LB_GETHORIZONTALEXTENT", 0x0193), new("LB_SETHORIZONTALEXTENT", 0x0194), new("LB_SETCOLUMNWIDTH", 0x0195),
        new("LB_ADDFILE", 0x0196), new("LB_SETTOPINDEX", 0x0197), new("LB_GETITEMRECT", 0x0198), new("LB_GETITEMDATA", 0x0199),
        new("LB_SETITEMDATA", 0x019A), new("LB_SELITEMRANGE", 0x019B), new("LB_SETANCHORINDEX", 0x019C),
        new("LB_GETANCHORINDEX", 0x019D), new("LB_SETCARETINDEX", 0x019E), new("LB_GETCARETINDEX", 0x019F),
        new("LB_SETITEMHEIGHT", 0x01A0), new("LB_GETITEMHEIGHT", 0x01A1), new("LB_FINDSTRINGEXACT", 0x01A2),
        new("LB_SETLOCALE", 0x01A5), new("LB_GETLOCALE", 0x01A6), new("LB_SETCOUNT", 0x01A7), new("LB_INITSTORAGE", 0x01A8),
        new("LB_ITEMFROMPOINT", 0x01A9), new("LB_GETLISTBOXINFO", 0x01B2),
        new("LBN_ERRSPACE", -2), new("LBN_SELCHANGE", 1), new("LBN_DBLCLK", 2), new("LBN_SELCANCEL", 3), new("LBN_SETFOCUS", 4),
        new("LBN_KILLFOCUS", 5),

        // combo box messages and notifications
        new("CB_OKAY", 0), new("CB_ERR", -1), new("CB_ERRSPACE", -2),
        new("CB_GETEDITSEL", 0x0140), new("CB_LIMITTEXT", 0x0141), new("CB_SETEDITSEL", 0x0142), new("CB_ADDSTRING", 0x0143),
        new("CB_DELETESTRING", 0x0144), new("CB_DIR", 0x0145), new("CB_GETCOUNT", 0x0146), new("CB_GETCURSEL", 0x0147),
        new("CB_GETLBTEXT", 0x0148), new("CB_GETLBTEXTLEN", 0x0149), new("CB_INSERTSTRING", 0x014A), new("CB_RESETCONTENT", 0x014B),
        new("CB_FINDSTRING", 0x014C), new("CB_SELECTSTRING", 0x014D), new("CB_SETCURSEL", 0x014E), new("CB_SHOWDROPDOWN", 0x014F),
        new("CB_GETITEMDATA", 0x0150), new("CB_SETITEMDATA", 0x0151), new("CB_GETDROPPEDCONTROLRECT", 0x0152),
        new("CB_SETITEMHEIGHT", 0x0153), new("CB_GETITEMHEIGHT", 0x0154), new("CB_SETEXTENDEDUI", 0x0155),
        new("CB_GETEXTENDEDUI", 0x0156), new("CB_GETDROPPEDSTATE", 0x0157), new("CB_FINDSTRINGEXACT", 0x0158),
        new("CB_SETLOCALE", 0x0159), new("CB_GETLOCALE", 0x015A), new("CB_GETTOPINDEX", 0x015B), new("CB_SETTOPINDEX", 0x015C),
        new("CB_GETHORIZONTALEXTENT", 0x015D), new("CB_SETHORIZONTALEXTENT", 0x015E), new("CB_GETDROPPEDWIDTH", 0x015F),
        new("CB_SETDROPPEDWIDTH", 0x0160), new("CB_INITSTORAGE", 0x0161), new("CB_GETCOMBOBOXINFO", 0x0164),
        new("CBM_FIRST", 0x1700), new("CB_SETMINVISIBLE", 0x1701), new("CB_GETMINVISIBLE", 0x1702), new("CB_SETCUEBANNER", 0x1703),
        new("CB_GETCUEBANNER", 0x1704),
        new("CBN_ERRSPACE", -1), new("CBN_SELCHANGE", 1), new("CBN_DBLCLK", 2), new("CBN_SETFOCUS", 3), new("CBN_KILLFOCUS", 4),
        new("CBN_EDITCHANGE", 5), new("CBN_EDITUPDATE", 6), new("CBN_DROPDOWN", 7), new("CBN_CLOSEUP", 8), new("CBN_SELENDOK", 9),
        new("CBN_SELENDCANCEL", 10),

        // static control messages and notifications
        new("STM_SETICON", 0x0170), new("STM_GETICON", 0x0171), new("STM_SETIMAGE", 0x0172), new("STM_GETIMAGE", 0x0173),
        new("STN_CLICKED", 0), new("STN_DBLCLK", 1), new("STN_ENABLE", 2), new("STN_DISABLE", 3),

        // scroll bar control messages
        new("SBM_SETPOS", 0x00E0), new("SBM_GETPOS", 0x00E1), new("SBM_SETRANGE", 0x00E2), new("SBM_GETRANGE", 0x00E3),
        new("SBM_ENABLE_ARROWS", 0x00E4), new("SBM_SETRANGEREDRAW", 0x00E6), new("SBM_SETSCROLLINFO", 0x00E9),
        new("SBM_GETSCROLLINFO", 0x00EA), new("SBM_GETSCROLLBARINFO", 0x00EB),
    ];

    [Fact]
    [Trait("Req", "RTL-824")]
    public Task ControlStyleConstants() => RunAsync("ControlStyleConstants", ["Windows"], StyleRows);

    [Fact]
    [Trait("Req", "RTL-824")]
    [Trait("Req", "RTL-850")]
    public Task ControlMessageConstants() => RunAsync("ControlMessageConstants", ["Windows", "Messages"], MessageRows);

    /// <summary>Generates the program of <paramref name="rows"/> as <c>winapi/&lt;name&gt;</c> and runs it through the golden runner.</summary>
    private static async Task RunAsync(string name, string[] units, Row[] rows)
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

    /// <summary>The program (one <c>CheckEqI</c> per constant) and its expected output.</summary>
    public static (string Program, string Expected) Generate(string name, string[] units, Row[] rows)
    {
        var program = new StringBuilder();
        var expected = new StringBuilder();
        program.Append("program ").Append(name).Append(";\nuses ").Append(string.Join(", ", units)).Append(", TestKit;\n\nbegin\n");
        foreach (var row in rows)
        {
            program.Append("  CheckEqI(").Append(row.Value).Append(", ").Append(row.Name).Append(", '").Append(row.Name).Append("');\n");
            expected.Append("ok ").Append(row.Name).Append('\n');
        }

        program.Append("  Done;\nend.\n");
        expected.Append("# passed ").Append(rows.Length).Append(" failed 0\n");
        return (program.ToString(), expected.ToString());
    }
}
