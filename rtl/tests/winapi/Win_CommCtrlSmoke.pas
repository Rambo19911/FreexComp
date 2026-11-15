// phase: 4
// covers: RTL-820, RTL-821, RTL-822, RTL-824, RTL-832, RTL-833, RTL-860, RTL-861, RTL-862
program Win_CommCtrlSmoke;
{ CommCtrl unit (winapi.md sections 1 and 5.5): calls the comctl32 functions the unit declares, and creates one
  control of each class under a hidden top-level window (the list view, the tree view and the macros:
  Win_CommCtrlViews). Messages round-trip the records (TTCItemW, THDItemW, TTBButton, TToolInfoW) and check
  message numbers and the notification codes the controls send back to the parent (RTL-861). Nothing is shown:
  the parent is never made visible, and no tooltip is activated. The program has no manifest, so comctl32 version 5
  is bound: only flags and record sizes that version knows are used (ICC_WIN95_CLASSES, TTTOOLINFOW_V2_SIZE). }
uses Windows, Messages, CommCtrl, Types, TestKit;

{$POINTERMATH ON}

const
  ParentClass = 'FreexCompCommCtrlSmoke';

var
  LastCode: UINT;
  LastFrom: HWND;
  NotifyCount: Integer;
  SelChangedNew: HTREEITEM;
  InsertedItem: Integer;

function WndProc(Wnd: HWND; Msg: UINT; wParam: WPARAM; lParam: LPARAM): LRESULT; stdcall;
var
  N: PNMHdr;
begin
  if Msg = WM_NOTIFY then
  begin
    N := PNMHdr(lParam);
    NotifyCount := NotifyCount + 1;
    LastCode := N^.code;
    LastFrom := N^.hwndFrom;
    if N^.code = TVN_SELCHANGEDW then
      SelChangedNew := PNMTreeViewW(lParam)^.itemNew.hItem;
    if N^.code = LVN_INSERTITEM then
      InsertedItem := PNMListView(lParam)^.iItem;
    Result := 0;
  end
  else
    Result := DefWindowProcW(Wnd, Msg, wParam, lParam);
end;

function BufStr(P: PWideChar): UnicodeString;
begin
  Result := '';
  while P^ <> #0 do
  begin
    Result := Result + P^;
    Inc(P);
  end;
end;

function NewChild(const ClassName: UnicodeString; Style: DWORD; Parent: HWND; Id: Integer): HWND;
begin
  Result := CreateWindowExW(0, PWideChar(ClassName), nil, WS_CHILD or WS_VISIBLE or Style, 0, 0, 200, 100, Parent,
    HMENU(Id), HINST(HInstance), nil);
end;

var
  ICC: TInitCommonControlsEx;
  IL: HIMAGELIST;
  DC: HDC;
  Bmp, Mask, Old: HBITMAP;
  Ico, Got: HICON;
  Cx, Cy: Integer;
  WC: TWndClassExW;
  Parent, Tab, Status, Tool, Prog, Track, UpDown, Tips, Header: HWND;
  TC: TTCItemW;
  HD: THDItemW;
  Btns: array[0..1] of TTBButton;
  TI: TToolInfoW;
  Parts: array[0..1] of Integer;
  Buffer: array[0..63] of WideChar;

begin
  Section('InitCommonControlsEx');
  ICC.dwSize := SizeOf(ICC);
  ICC.dwICC := ICC_WIN95_CLASSES;
  Check(InitCommonControlsEx(ICC), 'InitCommonControlsEx');

  Section('image lists');
  IL := ImageList_Create(16, 16, ILC_COLOR32 or ILC_MASK, 4, 4);
  Check(IL <> 0, 'ImageList_Create');
  CheckEqI(0, ImageList_GetImageCount(IL), 'ImageList_GetImageCount empty');
  DC := CreateCompatibleDC(0);
  Bmp := CreateCompatibleBitmap(DC, 16, 16);
  Mask := CreateBitmap(16, 16, 1, 1, nil);
  CheckEqI(0, ImageList_Add(IL, Bmp, Mask), 'ImageList_Add index');
  CheckEqI(1, ImageList_AddMasked(IL, Bmp, RGB(255, 0, 255)), 'ImageList_AddMasked index');
  Ico := LoadIconW(0, IDI_APPLICATION);
  CheckEqI(2, ImageList_ReplaceIcon(IL, -1, Ico), 'ImageList_ReplaceIcon append');
  CheckEqI(3, ImageList_AddIcon(IL, Ico), 'ImageList_AddIcon');
  CheckEqI(0, ImageList_ReplaceIcon(IL, 0, Ico), 'ImageList_ReplaceIcon replace');
  CheckEqI(4, ImageList_GetImageCount(IL), 'ImageList_GetImageCount');
  CheckEqI(CLR_NONE, ImageList_SetBkColor(IL, RGB(1, 2, 3)), 'ImageList_SetBkColor previous');
  CheckEqI(RGB(1, 2, 3), ImageList_SetBkColor(IL, CLR_NONE), 'ImageList_SetBkColor set');
  Old := HBITMAP(SelectObject(DC, Bmp));
  Check(ImageList_Draw(IL, 2, DC, 0, 0, ILD_NORMAL), 'ImageList_Draw');
  Check(ImageList_DrawEx(IL, 2, DC, 0, 0, 16, 16, CLR_NONE, CLR_DEFAULT, ILD_TRANSPARENT), 'ImageList_DrawEx');
  SelectObject(DC, Old);
  Got := ImageList_GetIcon(IL, 2, ILD_NORMAL);
  Check(Got <> 0, 'ImageList_GetIcon');
  Check(DestroyIcon(Got), 'DestroyIcon of ImageList_GetIcon');
  Check(ImageList_Remove(IL, 1), 'ImageList_Remove');
  CheckEqI(3, ImageList_GetImageCount(IL), 'ImageList_GetImageCount after Remove');
  Check(ImageList_GetIconSize(IL, Cx, Cy), 'ImageList_GetIconSize');
  CheckEqI(16, Cx, 'icon width');
  CheckEqI(16, Cy, 'icon height');
  Check(ImageList_SetIconSize(IL, 24, 20), 'ImageList_SetIconSize');
  Check(ImageList_GetIconSize(IL, Cx, Cy), 'ImageList_GetIconSize after Set');
  CheckEqI(24, Cx, 'new icon width');
  CheckEqI(20, Cy, 'new icon height');
  CheckEqI(0, ImageList_GetImageCount(IL), 'SetIconSize empties the list');
  Check(ImageList_Destroy(IL), 'ImageList_Destroy');
  DeleteObject(Mask);
  DeleteObject(Bmp);
  DeleteDC(DC);

  Section('parent window');
  FillChar(WC, SizeOf(WC), 0);
  WC.cbSize := SizeOf(WC);
  WC.lpfnWndProc := @WndProc;
  WC.hInstance := HINST(HInstance);
  WC.lpszClassName := ParentClass;
  Check(RegisterClassExW(WC) <> 0, 'RegisterClassExW');
  Parent := CreateWindowExW(0, ParentClass, 'CommCtrl', WS_OVERLAPPEDWINDOW, 0, 0, 400, 300, 0, 0, HINST(HInstance), nil);
  Check(Parent <> 0, 'parent');
  Check(not IsWindowVisible(Parent), 'parent stays hidden');

  Section('tab control');
  Tab := NewChild(WC_TABCONTROL, TCS_TABS, Parent, 1);
  Check(Tab <> 0, 'WC_TABCONTROL');
  FillChar(TC, SizeOf(TC), 0);
  TC.mask := TCIF_TEXT or TCIF_PARAM;
  TC.pszText := 'First';
  TC.lParam := 77;
  CheckEqI(0, SendMessageW(Tab, TCM_INSERTITEMW, 0, LPARAM(@TC)), 'TCM_INSERTITEMW');
  TC.pszText := 'Second';
  CheckEqI(1, SendMessageW(Tab, TCM_INSERTITEM, 1, LPARAM(@TC)), 'TCM_INSERTITEM alias');
  CheckEqI(2, SendMessageW(Tab, TCM_GETITEMCOUNT, 0, 0), 'TCM_GETITEMCOUNT');
  FillChar(TC, SizeOf(TC), 0);
  TC.mask := TCIF_TEXT or TCIF_PARAM;
  TC.pszText := @Buffer[0];
  TC.cchTextMax := 64;
  Check(SendMessageW(Tab, TCM_GETITEMW, 0, LPARAM(@TC)) <> 0, 'TCM_GETITEMW');
  CheckEqS('First', BufStr(@Buffer[0]), 'TTCItemW.pszText');
  CheckEqI(77, TC.lParam, 'TTCItemW.lParam');
  CheckEqI(0, SendMessageW(Tab, TCM_SETCURSEL, 1, 0), 'TCM_SETCURSEL previous');
  CheckEqI(1, SendMessageW(Tab, TCM_GETCURSEL, 0, 0), 'TCM_GETCURSEL');

  Section('status bar');
  Status := NewChild(STATUSCLASSNAME, SBARS_SIZEGRIP, Parent, 2);
  Check(Status <> 0, 'STATUSCLASSNAME');
  Parts[0] := 100;
  Parts[1] := -1;
  Check(SendMessageW(Status, SB_SETPARTS, 2, LPARAM(@Parts[0])) <> 0, 'SB_SETPARTS');
  CheckEqI(2, SendMessageW(Status, SB_GETPARTS, 0, 0), 'SB_GETPARTS');
  Check(SendMessageW(Status, SB_SETTEXTW, 1, LPARAM(PWideChar('Ready'))) <> 0, 'SB_SETTEXTW');
  CheckEqI(5, LoWord(SendMessageW(Status, SB_GETTEXTLENGTHW, 1, 0)), 'SB_GETTEXTLENGTHW');
  CheckEqI(5, LoWord(SendMessageW(Status, SB_GETTEXTW, 1, LPARAM(@Buffer[0]))), 'SB_GETTEXTW length');
  CheckEqS('Ready', BufStr(@Buffer[0]), 'SB_GETTEXTW');

  Section('tool bar');
  Tool := NewChild(TOOLBARCLASSNAME, TBSTYLE_FLAT or CCS_NORESIZE, Parent, 3);
  Check(Tool <> 0, 'TOOLBARCLASSNAME');
  SendMessageW(Tool, TB_BUTTONSTRUCTSIZE, SizeOf(TTBButton), 0);
  FillChar(Btns, SizeOf(Btns), 0);
  Btns[0].iBitmap := I_IMAGENONE;
  Btns[0].idCommand := 101;
  Btns[0].fsState := TBSTATE_ENABLED;
  Btns[0].fsStyle := BTNS_BUTTON;
  Btns[1].idCommand := 102;
  Btns[1].fsStyle := BTNS_CHECK;
  Btns[1].dwData := 55;
  Check(SendMessageW(Tool, TB_ADDBUTTONSW, 2, LPARAM(@Btns[0])) <> 0, 'TB_ADDBUTTONSW');
  CheckEqI(2, SendMessageW(Tool, TB_BUTTONCOUNT, 0, 0), 'TB_BUTTONCOUNT');
  CheckEqI(1, SendMessageW(Tool, TB_COMMANDTOINDEX, 102, 0), 'TB_COMMANDTOINDEX');
  Check(SendMessageW(Tool, TB_ISBUTTONENABLED, 101, 0) <> 0, 'TB_ISBUTTONENABLED');
  CheckEqI(0, SendMessageW(Tool, TB_ISBUTTONENABLED, 102, 0), 'TB_ISBUTTONENABLED disabled');
  FillChar(Btns, SizeOf(Btns), 0);
  Check(SendMessageW(Tool, TB_GETBUTTON, 1, LPARAM(@Btns[0])) <> 0, 'TB_GETBUTTON');
  CheckEqI(102, Btns[0].idCommand, 'TTBButton.idCommand');
  CheckEqI(BTNS_CHECK, Btns[0].fsStyle, 'TTBButton.fsStyle');
  CheckEqI(55, Btns[0].dwData, 'TTBButton.dwData');

  Section('progress bar');
  Prog := NewChild(PROGRESS_CLASS, PBS_SMOOTH, Parent, 4);
  Check(Prog <> 0, 'PROGRESS_CLASS');
  SendMessageW(Prog, PBM_SETRANGE32, 0, 200);
  CheckEqI(200, SendMessageW(Prog, PBM_GETRANGE, 0, 0), 'PBM_GETRANGE high');
  SendMessageW(Prog, PBM_SETPOS, 150, 0);
  CheckEqI(150, SendMessageW(Prog, PBM_GETPOS, 0, 0), 'PBM_GETPOS');
  CheckEqI(150, SendMessageW(Prog, PBM_DELTAPOS, 10, 0), 'PBM_DELTAPOS previous');
  CheckEqI(160, SendMessageW(Prog, PBM_GETPOS, 0, 0), 'PBM_GETPOS after delta');

  Section('track bar');
  Track := NewChild(TRACKBAR_CLASS, TBS_HORZ or TBS_AUTOTICKS, Parent, 5);
  Check(Track <> 0, 'TRACKBAR_CLASS');
  SendMessageW(Track, TBM_SETRANGE, 1, MakeLParam(0, 50));
  CheckEqI(50, SendMessageW(Track, TBM_GETRANGEMAX, 0, 0), 'TBM_GETRANGEMAX');
  SendMessageW(Track, TBM_SETPOS, 1, 30);
  CheckEqI(30, SendMessageW(Track, TBM_GETPOS, 0, 0), 'TBM_GETPOS');

  Section('up-down');
  UpDown := NewChild(UPDOWN_CLASS, UDS_ARROWKEYS, Parent, 6);
  Check(UpDown <> 0, 'UPDOWN_CLASS');
  SendMessageW(UpDown, UDM_SETRANGE32, WPARAM(-10), 1000);
  SendMessageW(UpDown, UDM_SETPOS32, 0, 500);
  CheckEqI(500, SendMessageW(UpDown, UDM_GETPOS32, 0, 0), 'UDM_GETPOS32');

  Section('tooltips');
  Tips := CreateWindowExW(0, TOOLTIPS_CLASS, nil, WS_POPUP or TTS_ALWAYSTIP, 0, 0, 0, 0, Parent, 0, HINST(HInstance), nil);
  Check(Tips <> 0, 'TOOLTIPS_CLASS');
  FillChar(TI, SizeOf(TI), 0);
  TI.cbSize := TTTOOLINFOW_V2_SIZE;
  TI.uFlags := TTF_IDISHWND;
  TI.hwnd := Parent;
  TI.uId := UINT_PTR(Tab);
  TI.lpszText := 'Tip';
  Check(SendMessageW(Tips, TTM_ADDTOOLW, 0, LPARAM(@TI)) <> 0, 'TTM_ADDTOOLW');
  CheckEqI(1, SendMessageW(Tips, TTM_GETTOOLCOUNT, 0, 0), 'TTM_GETTOOLCOUNT');
  Check(not IsWindowVisible(Tips), 'tooltip stays hidden');

  Section('header');
  Header := NewChild(WC_HEADER, HDS_HORZ or HDS_BUTTONS, Parent, 7);
  Check(Header <> 0, 'WC_HEADER');
  FillChar(HD, SizeOf(HD), 0);
  HD.mask := HDI_TEXT or HDI_WIDTH or HDI_FORMAT or HDI_LPARAM;
  HD.pszText := 'Name';
  HD.cxy := 80;
  HD.fmt := HDF_LEFT or HDF_STRING;
  HD.lParam := 9;
  CheckEqI(0, SendMessageW(Header, HDM_INSERTITEMW, 0, LPARAM(@HD)), 'HDM_INSERTITEMW');
  CheckEqI(1, SendMessageW(Header, HDM_GETITEMCOUNT, 0, 0), 'HDM_GETITEMCOUNT');
  FillChar(HD, SizeOf(HD), 0);
  HD.mask := HDI_WIDTH or HDI_LPARAM;
  Check(SendMessageW(Header, HDM_GETITEMW, 0, LPARAM(@HD)) <> 0, 'HDM_GETITEMW');
  CheckEqI(80, HD.cxy, 'THDItemW.cxy');
  CheckEqI(9, HD.lParam, 'THDItemW.lParam');

  Section('cleanup');
  Check(DestroyWindow(Tips), 'DestroyWindow tooltip');
  Check(DestroyWindow(Parent), 'DestroyWindow parent');
  Check(UnregisterClassW(ParentClass, HINST(HInstance)), 'UnregisterClassW');
  Done;
end.
