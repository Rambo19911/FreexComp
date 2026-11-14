namespace FreexComp.Rtl.Tests.Harness;

/// <summary>
/// RTL-851 (winapi.md §6): every cracked message record of the Messages unit overlays <c>TMessage</c> exactly — size 32,
/// <c>Msg</c> at 0, <c>Result</c> at 24 — and its parameter fields sit in the WParam (8) or LParam (16) slot.
/// </summary>
[Trait("Phase", "4")]
[Trait("Kind", "Golden")]
[Trait("Category", "Toolchain")]
[Trait("Category", "Integration")]
public sealed class MessageRecordSizesTests
{
    public static readonly string[] Units = ["Windows", "Messages"];

    private static StructSizesTests.Row Cracked(string name, params (string Field, int Offset)[] fields) =>
        new(name, 32, [("Msg", 0), .. fields, ("Result", 24)]);

    /// <summary>TMessage and the required set of RTL-851 (aliases such as <c>TWMChar = TWMKey</c> are listed under their own name).</summary>
    public static readonly StructSizesTests.Row[] Rows =
    [
        Cracked("TMessage", ("WParam", 8), ("LParam", 16)),
        Cracked("TWMSize", ("SizeType", 8), ("Width", 16), ("Height", 18)),
        Cracked("TWMMove", ("XPos", 16), ("YPos", 18)),
        Cracked("TWMCommand", ("ItemID", 8), ("NotifyCode", 10), ("Ctl", 16)),
        Cracked("TWMNotify", ("IDCtrl", 8), ("NMHdr", 16)),
        Cracked("TWMPaint", ("DC", 8)),
        Cracked("TWMEraseBkgnd", ("DC", 8)),
        Cracked("TWMKey", ("CharCode", 8), ("KeyData", 16)),
        Cracked("TWMChar", ("CharCode", 8), ("KeyData", 16)),
        Cracked("TWMMouse", ("Keys", 8), ("XPos", 16), ("YPos", 18)),
        Cracked("TWMMouseWheel", ("Keys", 8), ("WheelDelta", 10), ("XPos", 16), ("YPos", 18)),
        Cracked("TWMSetFocus", ("FocusedWnd", 8)),
        Cracked("TWMKillFocus", ("FocusedWnd", 8)),
        Cracked("TWMActivate", ("Active", 8), ("Minimized", 10), ("ActiveWindow", 16)),
        Cracked("TWMShowWindow", ("Show", 8), ("Status", 16)),
        Cracked("TWMWindowPosChanged", ("WindowPos", 16)),
        Cracked("TWMGetMinMaxInfo", ("MinMaxInfo", 16)),
        Cracked("TWMSetCursor", ("CursorWnd", 8), ("HitTest", 16), ("MouseMsg", 18)),
        Cracked("TWMTimer", ("TimerID", 8), ("TimerProc", 16)),
        Cracked("TWMHScroll", ("ScrollCode", 8), ("Pos", 10), ("ScrollBar", 16)),
        Cracked("TWMVScroll", ("ScrollCode", 8), ("Pos", 10), ("ScrollBar", 16)),
        Cracked("TWMClose"),
        Cracked("TWMSysCommand", ("CmdType", 8), ("XPos", 16), ("YPos", 18)),
        Cracked("TWMNCHitTest", ("XPos", 16), ("YPos", 18)),
        Cracked("TWMDrawItem", ("Ctl", 8), ("DrawItemStruct", 16)),
        Cracked("TWMMeasureItem", ("IDCtl", 8), ("MeasureItemStruct", 16)),
        Cracked("TWMDpiChanged", ("XDpi", 8), ("YDpi", 10), ("SuggestedRect", 16)),
        Cracked("TWMContextMenu", ("hWnd", 8), ("XPos", 16), ("YPos", 18)),
        Cracked("TWMGetDlgCode", ("MsgPtr", 16)),
        Cracked("TWMSetText", ("Text", 16)),
        Cracked("TWMSetFont", ("Font", 8), ("Redraw", 16)),

        // the shared records behind the aliases (TWMClose = TWMNoParams, TWMWindowPosChanged = TWMWindowPosMsg, TWMHScroll = TWMScroll)
        Cracked("TWMNoParams", ("Unused", 8)),
        Cracked("TWMWindowPosMsg", ("WindowPos", 16)),
        Cracked("TWMScroll", ("ScrollCode", 8), ("Pos", 10), ("ScrollBar", 16)),
    ];

    [Fact]
    [Trait("Req", "RTL-851")]
    public Task MessageRecordSizes() => StructSizesTests.RunAsync("MessageRecordSizes", Units, Rows);
}
