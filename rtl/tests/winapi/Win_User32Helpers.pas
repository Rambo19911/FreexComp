// phase: 4
// covers: RTL-820, RTL-822, RTL-823, RTL-860, RTL-862
program Win_User32Helpers;
{ Windows unit, user32 part (winapi.md section 5.3): the helpers added for the UI framework (task R-204) that
  Win_User32Smoke does not call, kept apart because that program may not grow (RTL-905): PtInRect (a TPoint
  passed by value), CopyIcon, and the edit style ES_AUTOHSCROLL as an EDIT control keeps it. }
uses Types, Windows, TestKit;

var
  R: TRect;
  Icon, Copy: HICON;
  Edit: HWND;

begin
  Section('PtInRect');
  R := Rect(0, 0, 40, 30);
  { Windows is listed after Types, so the unqualified name is the Win32 function; Types.PtInRect is the RTL one }
  CheckEqB(True, PtInRect(R, Point(39, 29)), 'PtInRect inside');
  CheckEqB(True, PtInRect(R, Point(0, 0)), 'PtInRect top left included');
  CheckEqB(False, PtInRect(R, Point(40, 0)), 'PtInRect right edge excluded');
  CheckEqB(False, PtInRect(R, Point(0, 30)), 'PtInRect bottom edge excluded');
  CheckEqB(False, PtInRect(R, Point(5, -1)), 'PtInRect above');
  CheckEqB(Types.PtInRect(R, Point(20, 10)), PtInRect(R, Point(20, 10)), 'agrees with Types.PtInRect');

  Section('CopyIcon');
  Icon := LoadIconW(0, IDI_APPLICATION);
  Check(Icon <> 0, 'LoadIconW');
  Copy := CopyIcon(Icon);
  Check((Copy <> 0) and (Copy <> Icon), 'CopyIcon');
  Check(DestroyIcon(Copy), 'DestroyIcon of the copy');

  Section('edit styles');
  CheckEqI($0080, ES_AUTOHSCROLL, 'ES_AUTOHSCROLL value');
  Edit := CreateWindowExW(0, 'EDIT', '', WS_POPUP or ES_AUTOHSCROLL, 0, 0, 80, 20, 0, 0, HINST(HInstance), nil);
  Check(Edit <> 0, 'CreateWindowExW EDIT');
  Check((GetWindowLongPtrW(Edit, GWL_STYLE) and ES_AUTOHSCROLL) <> 0, 'EDIT keeps ES_AUTOHSCROLL');
  Check(DestroyWindow(Edit), 'DestroyWindow EDIT');
  Done;
end.
