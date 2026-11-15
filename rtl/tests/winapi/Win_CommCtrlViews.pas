// phase: 4
// covers: RTL-820, RTL-821, RTL-822, RTL-824, RTL-832, RTL-833, RTL-860, RTL-861, RTL-862
program Win_CommCtrlViews;
{ CommCtrl unit (winapi.md sections 1 and 5.5), continued from Win_CommCtrlSmoke: a list view and a tree view under
  a hidden top-level window (common controls initialised and the parent created without checks; those calls are
  checked in Win_CommCtrlSmoke), and the macros. Messages round-trip the records (TLVColumnW, TLVItemW,
  TTVInsertStructW / TTVItemW) and check message numbers and the notification codes the controls send back to
  the parent (RTL-861). Nothing is shown: the parent is never made visible. The program has no manifest, so
  comctl32 version 5 is bound (ICC_WIN95_CLASSES). }
uses Windows, Messages, CommCtrl, Types, TestKit;

{$POINTERMATH ON}

const
  ParentClass = 'FreexCompCommCtrlViews';

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
  WC: TWndClassExW;
  Parent, List, Tree: HWND;
  LC: TLVColumnW;
  LI: TLVItemW;
  TVI: TTVInsertStructW;
  TV: TTVItemW;
  Root, Child: HTREEITEM;
  Buffer: array[0..63] of WideChar;
  R: TRect;

begin
  ICC.dwSize := SizeOf(ICC);
  ICC.dwICC := ICC_WIN95_CLASSES;
  InitCommonControlsEx(ICC);
  FillChar(WC, SizeOf(WC), 0);
  WC.cbSize := SizeOf(WC);
  WC.lpfnWndProc := @WndProc;
  WC.hInstance := HINST(HInstance);
  WC.lpszClassName := ParentClass;
  RegisterClassExW(WC);
  Parent := CreateWindowExW(0, ParentClass, 'CommCtrl', WS_OVERLAPPEDWINDOW, 0, 0, 400, 300, 0, 0, HINST(HInstance), nil);

  Section('list view');
  List := NewChild(WC_LISTVIEW, LVS_REPORT or LVS_SHOWSELALWAYS, Parent, 8);
  Check(List <> 0, 'WC_LISTVIEW');
  FillChar(LC, SizeOf(LC), 0);
  LC.mask := LVCF_TEXT or LVCF_WIDTH or LVCF_SUBITEM;
  LC.cx := 120;
  LC.pszText := 'Col';
  CheckEqI(0, SendMessageW(List, LVM_INSERTCOLUMNW, 0, LPARAM(@LC)), 'LVM_INSERTCOLUMNW');
  CheckEqI(120, SendMessageW(List, LVM_GETCOLUMNWIDTH, 0, 0), 'LVM_GETCOLUMNWIDTH');
  LC.iSubItem := 1;
  LC.cx := 60;
  CheckEqI(1, SendMessageW(List, LVM_INSERTCOLUMN, 1, LPARAM(@LC)), 'LVM_INSERTCOLUMN alias');
  FillChar(LC, SizeOf(LC), 0);
  LC.mask := LVCF_WIDTH or LVCF_SUBITEM;
  Check(SendMessageW(List, LVM_GETCOLUMNW, 1, LPARAM(@LC)) <> 0, 'LVM_GETCOLUMNW');
  CheckEqI(60, LC.cx, 'TLVColumnW.cx');
  CheckEqI(1, LC.iSubItem, 'TLVColumnW.iSubItem');
  NotifyCount := 0;
  InsertedItem := -1;
  FillChar(LI, SizeOf(LI), 0);
  LI.mask := LVIF_TEXT or LVIF_PARAM;
  LI.iItem := 0;
  LI.pszText := 'Alpha';
  LI.lParam := 1234;
  CheckEqI(0, SendMessageW(List, LVM_INSERTITEMW, 0, LPARAM(@LI)), 'LVM_INSERTITEMW');
  CheckEqI(LVN_INSERTITEM, LastCode, 'LVN_INSERTITEM sent to the parent');
  Check(LastFrom = List, 'LVN_INSERTITEM hwndFrom');
  CheckEqI(0, InsertedItem, 'TNMListView.iItem');
  LI.iItem := 1;
  LI.pszText := 'Beta';
  CheckEqI(1, SendMessageW(List, LVM_INSERTITEM, 0, LPARAM(@LI)), 'LVM_INSERTITEM alias');
  CheckEqI(2, SendMessageW(List, LVM_GETITEMCOUNT, 0, 0), 'LVM_GETITEMCOUNT');
  FillChar(LI, SizeOf(LI), 0);
  LI.iSubItem := 1;
  LI.pszText := 'Sub';
  Check(SendMessageW(List, LVM_SETITEMTEXTW, 1, LPARAM(@LI)) <> 0, 'LVM_SETITEMTEXTW');
  FillChar(LI, SizeOf(LI), 0);
  LI.iSubItem := 1;
  LI.pszText := @Buffer[0];
  LI.cchTextMax := 64;
  CheckEqI(3, SendMessageW(List, LVM_GETITEMTEXTW, 1, LPARAM(@LI)), 'LVM_GETITEMTEXTW length');
  CheckEqS('Sub', BufStr(@Buffer[0]), 'LVM_GETITEMTEXTW');
  FillChar(LI, SizeOf(LI), 0);
  LI.mask := LVIF_TEXT or LVIF_PARAM;
  LI.iItem := 1;
  LI.pszText := @Buffer[0];
  LI.cchTextMax := 64;
  Check(SendMessageW(List, LVM_GETITEMW, 0, LPARAM(@LI)) <> 0, 'LVM_GETITEMW');
  CheckEqS('Beta', BufStr(@Buffer[0]), 'TLVItemW.pszText');
  CheckEqI(1234, LI.lParam, 'TLVItemW.lParam');
  FillChar(LI, SizeOf(LI), 0);
  LI.stateMask := LVIS_SELECTED;
  LI.state := LVIS_SELECTED;
  Check(SendMessageW(List, LVM_SETITEMSTATE, 1, LPARAM(@LI)) <> 0, 'LVM_SETITEMSTATE');
  CheckEqI(LVN_ITEMCHANGED, LastCode, 'LVN_ITEMCHANGED sent to the parent');
  CheckEqI(1, SendMessageW(List, LVM_GETSELECTEDCOUNT, 0, 0), 'LVM_GETSELECTEDCOUNT');
  CheckEqI(1, SendMessageW(List, LVM_GETNEXTITEM, WPARAM(-1), LVNI_SELECTED), 'LVM_GETNEXTITEM selected');
  SendMessageW(List, LVM_SETEXTENDEDLISTVIEWSTYLE, LVS_EX_FULLROWSELECT, LVS_EX_FULLROWSELECT);
  CheckEqI(LVS_EX_FULLROWSELECT, SendMessageW(List, LVM_GETEXTENDEDLISTVIEWSTYLE, 0, 0) and LVS_EX_FULLROWSELECT,
    'LVM_GETEXTENDEDLISTVIEWSTYLE');
  Check(SendMessageW(List, LVM_SETBKCOLOR, 0, RGB(1, 2, 3)) <> 0, 'LVM_SETBKCOLOR');
  CheckEqI(RGB(1, 2, 3), SendMessageW(List, LVM_GETBKCOLOR, 0, 0), 'LVM_GETBKCOLOR');
  R.Left := LVIR_BOUNDS;
  Check(SendMessageW(List, LVM_GETITEMRECT, 0, LPARAM(@R)) <> 0, 'LVM_GETITEMRECT');
  Check(R.Right > R.Left, 'LVM_GETITEMRECT width');
  Check(SendMessageW(List, LVM_DELETEITEM, 0, 0) <> 0, 'LVM_DELETEITEM');
  CheckEqI(LVN_DELETEITEM, LastCode, 'LVN_DELETEITEM sent to the parent');
  CheckEqI(1, SendMessageW(List, LVM_GETITEMCOUNT, 0, 0), 'LVM_GETITEMCOUNT after delete');

  Section('tree view');
  Tree := NewChild(WC_TREEVIEW, TVS_HASLINES or TVS_HASBUTTONS or TVS_LINESATROOT, Parent, 9);
  Check(Tree <> 0, 'WC_TREEVIEW');
  FillChar(TVI, SizeOf(TVI), 0);
  TVI.hParent := TVI_ROOT;
  TVI.hInsertAfter := TVI_LAST;
  TVI.item.mask := TVIF_TEXT or TVIF_PARAM;
  TVI.item.pszText := 'Root';
  TVI.item.lParam := 42;
  Root := HTREEITEM(SendMessageW(Tree, TVM_INSERTITEMW, 0, LPARAM(@TVI)));
  Check(Root <> 0, 'TVM_INSERTITEMW');
  TVI.hParent := Root;
  TVI.hInsertAfter := TVI_FIRST;
  TVI.item.pszText := 'Child';
  TVI.item.lParam := 43;
  Child := HTREEITEM(SendMessageW(Tree, TVM_INSERTITEM, 0, LPARAM(@TVI)));
  Check(Child <> 0, 'TVM_INSERTITEM alias');
  CheckEqI(2, SendMessageW(Tree, TVM_GETCOUNT, 0, 0), 'TVM_GETCOUNT');
  Check(HTREEITEM(SendMessageW(Tree, TVM_GETNEXTITEM, TVGN_ROOT, 0)) = Root, 'TVM_GETNEXTITEM TVGN_ROOT');
  Check(HTREEITEM(SendMessageW(Tree, TVM_GETNEXTITEM, TVGN_CHILD, LPARAM(Root))) = Child, 'TVM_GETNEXTITEM TVGN_CHILD');
  Check(HTREEITEM(SendMessageW(Tree, TVM_GETNEXTITEM, TVGN_PARENT, LPARAM(Child))) = Root, 'TVM_GETNEXTITEM TVGN_PARENT');
  FillChar(TV, SizeOf(TV), 0);
  TV.mask := TVIF_TEXT or TVIF_PARAM or TVIF_HANDLE;
  TV.hItem := Child;
  TV.pszText := @Buffer[0];
  TV.cchTextMax := 64;
  Check(SendMessageW(Tree, TVM_GETITEMW, 0, LPARAM(@TV)) <> 0, 'TVM_GETITEMW');
  CheckEqS('Child', BufStr(@Buffer[0]), 'TTVItemW.pszText');
  CheckEqI(43, TV.lParam, 'TTVItemW.lParam');
  Check(SendMessageW(Tree, TVM_EXPAND, TVE_EXPAND, LPARAM(Root)) <> 0, 'TVM_EXPAND');
  CheckEqI(TVIS_EXPANDED, SendMessageW(Tree, TVM_GETITEMSTATE, WPARAM(Root), TVIS_EXPANDED) and TVIS_EXPANDED,
    'TVM_GETITEMSTATE expanded');
  SelChangedNew := 0;
  Check(SendMessageW(Tree, TVM_SELECTITEM, TVGN_CARET, LPARAM(Child)) <> 0, 'TVM_SELECTITEM');
  Check(SelChangedNew = Child, 'TVN_SELCHANGEDW sent to the parent with itemNew');
  Check(HTREEITEM(SendMessageW(Tree, TVM_GETNEXTITEM, TVGN_CARET, 0)) = Child, 'TVM_GETNEXTITEM TVGN_CARET');
  Check(SendMessageW(Tree, TVM_DELETEITEM, 0, LPARAM(Child)) <> 0, 'TVM_DELETEITEM');
  CheckEqI(1, SendMessageW(Tree, TVM_GETCOUNT, 0, 0), 'TVM_GETCOUNT after delete');

  Section('macros');
  CheckEqI($2000, IndexToStateImageMask(2), 'IndexToStateImageMask');
  CheckEqI($300, IndexToOverlayMask(3), 'IndexToOverlayMask');
  CheckEqI(-1, NativeInt(LPSTR_TEXTCALLBACKW), 'LPSTR_TEXTCALLBACKW');
  DestroyWindow(Parent);
  UnregisterClassW(ParentClass, HINST(HInstance));
  Done;
end.
