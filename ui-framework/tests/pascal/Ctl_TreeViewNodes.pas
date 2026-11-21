// covers: UIF-830, UIF-831, UIF-833
// needs: desktop
program Ctl_TreeViewNodes;
{ U-088, controls-standard.md section 26 (properties, styles, the node object model). Defaults (121 x 97, a tab
  stop, clWindow, Indent 19), the sub-classed SysTreeView32 window and its styles, the recreating properties, the
  colours, the image list, Indent (scaled); TTreeNodes / TTreeNode without a window (their own tree: Add, AddFirst,
  AddChild, AddChildFirst, Insert, pre-order Item[] and Count, Level, Index, AbsoluteIndex and the navigation
  functions); the window created from the objects in pre-order with callback text and images, lParam = the node and
  cChildren set explicitly, updated when children come and go (UIF-830); expanded and selected state restored on
  creation and saved when the window is destroyed, also when the window changed it (UIF-831); deleting a node
  deletes its descendants first, deepest first (UIF-833); the helpers (GetNodeAt, TopItem, DisplayRect, MakeVisible,
  FullExpand, FullCollapse); a leak check. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, ImgList, CommCtrl, ComCtrls, TestKit;

type
  TWinAccess = class(TWinControl);

  { logs the native deletions }
  TLogTree = class(TTreeView)
  protected
    procedure WndProc(var Message: TMessage); override;
  public
    Deleted: string;
  end;

procedure TLogTree.WndProc(var Message: TMessage);
begin
  if (Message.Msg = CN_NOTIFY) and (PNMHdr(Message.LParam)^.code = TVN_DELETEITEMW) then
    Deleted := Deleted + TTreeNode(Pointer(PNMTreeViewW(Message.LParam)^.itemOld.lParam)).Text + ';';
  inherited WndProc(Message);
end;

function ClassOf(H: HWND): string;
var
  Buf: array[0..127] of WideChar;
  N: Integer;
begin
  N := GetClassNameW(H, @Buf[0], 128);
  SetLength(Result, N);
  if N > 0 then
    Move(Buf[0], PChar(Result)^, N * SizeOf(Char));
end;

function HasStyle(C: TWinControl; Bits: NativeInt): Boolean;
begin
  Result := (GetWindowLongPtrW(C.Handle, GWL_STYLE) and Bits) = Bits;
end;

function HasExStyle(C: TWinControl; Bits: NativeInt): Boolean;
begin
  Result := (GetWindowLongPtrW(C.Handle, GWL_EXSTYLE) and Bits) = Bits;
end;

{ the object tree as 'R1(A(A1,A2),B),R2(C),R3' }
function NodeList(N: TTreeNode): string;
begin
  Result := '';
  while N <> nil do
  begin
    if Result <> '' then
      Result := Result + ',';
    Result := Result + N.Text;
    if N.HasChildren then
      Result := Result + '(' + NodeList(N.GetFirstChild) + ')';
    N := N.GetNextSibling;
  end;
end;

function Objects(T: TTreeView): string;
begin
  Result := NodeList(T.Items.GetFirstNode);
end;

function Next(T: TTreeView; H: HTREEITEM; Code: Integer): HTREEITEM;
begin
  Result := HTREEITEM(SendMessageW(T.Handle, TVM_GETNEXTITEM, WPARAM(Code), LPARAM(H)));
end;

function NativeItem(T: TTreeView; H: HTREEITEM; Mask: UINT): TTVItemW;
begin
  FillChar(Result, SizeOf(Result), 0);
  Result.mask := Mask or TVIF_HANDLE;
  Result.hItem := H;
  Result.stateMask := TVIS_EXPANDED or TVIS_SELECTED;
  SendMessageW(T.Handle, TVM_GETITEMW, 0, LPARAM(@Result));
end;

function NativeNode(T: TTreeView; H: HTREEITEM): TTreeNode;
begin
  Result := TTreeNode(Pointer(NativeItem(T, H, TVIF_PARAM).lParam));
end;

{ the native tree as text of the nodes in lParam, with '+' after an expanded item and '*' after a selected one }
function NativeList(T: TTreeView; H: HTREEITEM): string;
var
  It: TTVItemW;
begin
  Result := '';
  while H <> 0 do
  begin
    if Result <> '' then
      Result := Result + ',';
    It := NativeItem(T, H, TVIF_PARAM or TVIF_STATE or TVIF_CHILDREN);
    Result := Result + TTreeNode(Pointer(It.lParam)).Text;
    if (It.state and TVIS_EXPANDED) <> 0 then
      Result := Result + '+';
    if (It.state and TVIS_SELECTED) <> 0 then
      Result := Result + '*';
    if It.cChildren <> 0 then
      Result := Result + '(' + NativeList(T, Next(T, H, TVGN_CHILD)) + ')';
    H := Next(T, H, TVGN_NEXT);
  end;
end;

function Native(T: TTreeView): string;
begin
  Result := NativeList(T, Next(T, 0, TVGN_ROOT));
end;

function NativeText(T: TTreeView; N: TTreeNode): string;
var
  It: TTVItemW;
  Buf: array[0..63] of WideChar;
begin
  FillChar(It, SizeOf(It), 0);
  It.mask := TVIF_TEXT or TVIF_HANDLE;
  It.hItem := N.ItemId;
  It.pszText := @Buf[0];
  It.cchTextMax := 64;
  Buf[0] := #0;
  SendMessageW(T.Handle, TVM_GETITEMW, 0, LPARAM(@It));
  Result := PChar(It.pszText);
end;

function Children(T: TTreeView; N: TTreeNode): Integer;
begin
  Result := NativeItem(T, N.ItemId, TVIF_CHILDREN).cChildren;
end;

{ the text, image and selected image the control answers in TVN_GETDISPINFOW for Node }
function DispInfo(T: TTreeView; N: TTreeNode): string;
var
  D: TTVDispInfoW;
  Buf: array[0..63] of WideChar;
begin
  FillChar(D, SizeOf(D), 0);
  D.hdr.hwndFrom := T.Handle;
  D.hdr.code := TVN_GETDISPINFOW;
  D.item.mask := TVIF_TEXT or TVIF_IMAGE or TVIF_SELECTEDIMAGE;
  D.item.hItem := N.ItemId;
  D.item.lParam := LPARAM(Pointer(N));
  D.item.pszText := @Buf[0];
  D.item.cchTextMax := 64;
  Buf[0] := #0;
  SendMessageW(T.Parent.Handle, WM_NOTIFY, 0, LPARAM(@D));
  Result := string(PChar(@Buf[0])) + '/' + IntToStr(D.item.iImage) + '/' + IntToStr(D.item.iSelectedImage);
end;

function GuiObjects: Integer;
begin
  Result := Integer(GetGuiResources(GetCurrentProcess, GR_GDIOBJECTS))
    + Integer(GetGuiResources(GetCurrentProcess, GR_USEROBJECTS));
end;

procedure Defaults;
var
  T: TTreeView;
begin
  Section('defaults');
  T := TTreeView.Create(nil);
  try
    CheckEqI(121, T.Width, 'Width 121');
    CheckEqI(97, T.Height, 'Height 97');
    CheckEqB(True, T.BorderStyle = bsSingle, 'BorderStyle bsSingle');
    CheckEqB(True, T.Color = clWindow, 'Color clWindow');
    CheckEqB(True, T.HideSelection, 'HideSelection');
    CheckEqB(True, T.Images = nil, 'Images nil');
    CheckEqI(19, T.Indent, 'Indent 19');
    CheckEqB(False, T.ParentColor, 'ParentColor False');
    CheckEqB(True, T.ParentFont, 'ParentFont');
    CheckEqB(False, T.ReadOnly, 'ReadOnly');
    CheckEqB(False, T.RowSelect, 'RowSelect');
    CheckEqB(True, T.ShowButtons, 'ShowButtons');
    CheckEqB(True, T.ShowLines, 'ShowLines');
    CheckEqB(True, T.ShowRoot, 'ShowRoot');
    CheckEqB(True, T.TabStop, 'TabStop');
    CheckEqB(False, csAcceptsControls in T.ControlStyle, 'does not accept children');
    CheckEqI(0, T.Items.Count, 'no nodes');
    CheckEqB(True, T.Items.Owner = T, 'Items.Owner');
    CheckEqB(True, T.Selected = nil, 'Selected nil');
    Check(GetClass('TTreeView') <> nil, 'TTreeView registered');
  finally
    T.Free;
  end;
end;

procedure Run;
var
  F: TForm;
  T: TLogTree;
  T2: TTreeView;
  R1, R2, R3, A, A1, A2, B, C, N: TTreeNode;
  IL: TImageList;
  H: HWND;
  R: TRect;
  I, Before: Integer;
begin
  Application.Initialize;
  F := TForm.CreateNew(nil);
  try
    F.ClientWidth := 640;
    F.ClientHeight := 400;
    Defaults;

    Section('nodes without a window');
    T := TLogTree.Create(F);
    T.SetBounds(8, 8, 300, 300);
    R2 := T.Items.Add(nil, 'R2');
    R1 := T.Items.AddFirst(R2, 'R1');
    R3 := T.Items.Add(R1, 'R3');
    B := T.Items.AddChild(R1, 'B');
    A := T.Items.Insert(B, 'A');
    A2 := T.Items.AddChild(A, 'A2');
    A1 := T.Items.AddChildFirst(A, 'A1');
    C := T.Items.AddChild(R2, 'C');
    CheckEqS('R1(A(A1,A2),B),R2(C),R3', Objects(T), 'the object tree');
    CheckEqI(8, T.Items.Count, 'Count: all nodes');
    CheckEqS('A2', T.Items[3].Text, 'Item[3] in pre-order');
    CheckEqS('R3', T.Items[7].Text, 'Item[7] in pre-order');
    CheckEqI(2, A1.Level, 'Level');
    CheckEqI(0, R1.Level, 'root Level 0');
    CheckEqI(1, A2.Index, 'Index among siblings');
    CheckEqI(2, R3.Index, 'root Index');
    CheckEqI(4, B.AbsoluteIndex, 'AbsoluteIndex');
    CheckEqB(True, A1.Parent = A, 'Parent');
    CheckEqB(True, R1.Parent = nil, 'a root has no parent');
    CheckEqI(2, R1.Count, 'Count of children');
    CheckEqB(True, R1.Item[1] = B, 'Item[1] of a node');
    CheckEqB(True, R1.HasChildren and not R3.HasChildren, 'HasChildren');
    CheckEqB(True, T.Items.GetFirstNode = R1, 'GetFirstNode');
    CheckEqB(True, A.GetFirstChild = A1, 'GetFirstChild');
    CheckEqB(True, A.GetLastChild = A2, 'GetLastChild');
    CheckEqB(True, R3.GetFirstChild = nil, 'GetFirstChild of a leaf');
    CheckEqB(True, A.GetNextSibling = B, 'GetNextSibling');
    CheckEqB(True, B.GetNextSibling = nil, 'no next sibling');
    CheckEqB(True, B.GetPrevSibling = A, 'GetPrevSibling');
    CheckEqB(True, A2.GetNext = B, 'GetNext climbs');
    CheckEqB(True, R2.GetPrev = B, 'GetPrev descends');
    CheckEqB(True, R1.GetPrev = nil, 'nothing before the first node');
    CheckEqB(True, R3.GetNext = nil, 'nothing after the last node');
    CheckEqI(-1, C.ImageIndex, 'ImageIndex -1');
    CheckEqI(-1, C.SelectedIndex, 'SelectedIndex -1');
    CheckEqB(True, C.Data = nil, 'Data nil');
    CheckEqB(True, C.ItemId = 0, 'no ItemId without a window');
    CheckEqB(True, C.TreeView = T, 'TreeView');
    CheckEqB(True, C.Owner = T.Items, 'Owner');
    A.ImageIndex := 2;
    A.SelectedIndex := 3;
    B.ImageIndex := 4;
    R1.Expanded := True;
    A.Expanded := True;
    T.Selected := A2;
    CheckEqB(True, R1.Expanded and A.Expanded and not R2.Expanded, 'Expanded recorded');
    CheckEqB(True, T.Selected = A2, 'Selected recorded');
    CheckEqB(True, A2.Selected and not A1.Selected, 'node Selected');

    Section('the window is created (UIF-830, UIF-831)');
    T.Parent := F;
    H := T.Handle;
    CheckEqS('SysTreeView32', ClassOf(H), 'window class SysTreeView32');
    CheckEqI(8, SendMessageW(H, TVM_GETCOUNT, 0, 0), 'eight native items');
    CheckEqS('R1+(A+(A1,A2*),B),R2(C),R3', Native(T), 'pre-order, expanded and selected state restored');
    CheckEqB(True, A.ItemId <> 0, 'ItemId');
    CheckEqB(True, NativeNode(T, A.ItemId) = A, 'lParam is the node');
    CheckEqB(True, HTREEITEM(SendMessageW(H, TVM_GETNEXTITEM, TVGN_CARET, 0)) = A2.ItemId, 'the caret item');
    CheckEqI(1, Children(T, A), 'cChildren 1');
    CheckEqI(0, Children(T, A1), 'cChildren 0');
    CheckEqS('B', NativeText(T, B), 'the native text');

    Section('callback text and images (UIF-830)');
    CheckEqS('A/2/3', DispInfo(T, A), 'text, image and selected image');
    CheckEqS('B/4/4', DispInfo(T, B), 'SelectedIndex -1: the image');
    CheckEqS('C/-1/-1', DispInfo(T, C), 'no images');
    C.Text := 'Cee';
    C.ImageIndex := 1;
    CheckEqS('Cee/1/1', DispInfo(T, C), 'changes are read from the node');
    CheckEqS('Cee', NativeText(T, C), 'the native text follows the node');
    Check(C.DisplayRect(True).Right = 0, 'C is hidden in a collapsed parent');
    R2.Expanded := True;
    R := C.DisplayRect(True);
    Check(R.Right > R.Left, 'DisplayRect(True) of a visible node');

    Section('styles');
    Check(HasStyle(T, TVS_HASBUTTONS or TVS_HASLINES or TVS_LINESATROOT or TVS_EDITLABELS), 'buttons, lines, root, edit');
    CheckEqB(False, HasStyle(T, TVS_SHOWSELALWAYS), 'no TVS_SHOWSELALWAYS (HideSelection)');
    CheckEqB(False, HasStyle(T, TVS_FULLROWSELECT), 'no TVS_FULLROWSELECT');
    Check(HasStyle(T, WS_TABSTOP), 'WS_TABSTOP');
    Check(HasExStyle(T, WS_EX_CLIENTEDGE), 'WS_EX_CLIENTEDGE');
    CheckEqI(19, SendMessageW(H, TVM_GETINDENT, 0, 0), 'TVM_GETINDENT 19');
    T.ShowButtons := False;
    CheckEqB(True, T.Handle <> H, 'ShowButtons recreates the window');
    CheckEqB(False, HasStyle(T, TVS_HASBUTTONS), 'no TVS_HASBUTTONS');
    CheckEqS('R1+(A+(A1,A2*),B),R2+(Cee),R3', Native(T), 'the tree and its state are kept');
    T.ShowButtons := True;
    H := T.Handle;
    T.ShowLines := False;
    CheckEqB(True, T.Handle <> H, 'ShowLines recreates the window');
    CheckEqB(False, HasStyle(T, TVS_HASLINES), 'no TVS_HASLINES');
    H := T.Handle;
    T.RowSelect := True;
    CheckEqB(True, T.Handle <> H, 'RowSelect recreates the window');
    Check(HasStyle(T, TVS_FULLROWSELECT), 'TVS_FULLROWSELECT');
    T.RowSelect := False;
    T.ShowLines := True;
    H := T.Handle;
    T.ShowRoot := False;
    CheckEqB(True, T.Handle <> H, 'ShowRoot recreates the window');
    CheckEqB(False, HasStyle(T, TVS_LINESATROOT), 'no TVS_LINESATROOT');
    T.ShowRoot := True;
    H := T.Handle;
    T.HideSelection := False;
    CheckEqB(True, T.Handle <> H, 'HideSelection recreates the window');
    Check(HasStyle(T, TVS_SHOWSELALWAYS), 'TVS_SHOWSELALWAYS');
    H := T.Handle;
    T.ReadOnly := True;
    CheckEqB(True, T.Handle <> H, 'ReadOnly recreates the window');
    CheckEqB(False, HasStyle(T, TVS_EDITLABELS), 'no TVS_EDITLABELS');
    T.ReadOnly := False;
    H := T.Handle;
    T.BorderStyle := bsNone;
    CheckEqB(True, T.Handle <> H, 'BorderStyle recreates the window');
    CheckEqB(False, HasExStyle(T, WS_EX_CLIENTEDGE), 'bsNone: no WS_EX_CLIENTEDGE');
    T.BorderStyle := bsSingle;
    CheckEqS('R1+(A+(A1,A2*),B),R2+(Cee),R3', Native(T), 'still the same tree');

    Section('colours, images, indent');
    T.Color := clYellow;
    CheckEqI(ColorToRGB(clYellow), SendMessageW(T.Handle, TVM_GETBKCOLOR, 0, 0), 'TVM_SETBKCOLOR');
    T.Font.Color := clBlue;
    CheckEqI(ColorToRGB(clBlue), SendMessageW(T.Handle, TVM_GETTEXTCOLOR, 0, 0), 'TVM_SETTEXTCOLOR');
    TWinAccess(T).RecreateWnd;
    CheckEqI(ColorToRGB(clYellow), SendMessageW(T.Handle, TVM_GETBKCOLOR, 0, 0), 'background kept');
    CheckEqI(ColorToRGB(clBlue), SendMessageW(T.Handle, TVM_GETTEXTCOLOR, 0, 0), 'text colour kept');
    T.Color := clWindow;
    IL := TImageList.Create(F);
    T.Images := IL;
    CheckEqB(True, HIMAGELIST(SendMessageW(T.Handle, TVM_GETIMAGELIST, TVSIL_NORMAL, 0)) = IL.Handle, 'TVSIL_NORMAL');
    TWinAccess(T).RecreateWnd;
    CheckEqB(True, HIMAGELIST(SendMessageW(T.Handle, TVM_GETIMAGELIST, TVSIL_NORMAL, 0)) = IL.Handle,
      'image list kept');
    IL.Free;
    CheckEqB(True, T.Images = nil, 'a freed list clears Images');
    CheckEqI(0, SendMessageW(T.Handle, TVM_GETIMAGELIST, TVSIL_NORMAL, 0), 'and the native list');
    T.Indent := 30;
    CheckEqI(30, SendMessageW(T.Handle, TVM_GETINDENT, 0, 0), 'Indent sent');
    T.ScaleBy(2, 1);
    CheckEqI(60, T.Indent, 'Indent scaled');
    CheckEqI(60, SendMessageW(T.Handle, TVM_GETINDENT, 0, 0), 'scaled Indent sent');
    T.ScaleBy(1, 2);
    T.Indent := 19;
    TWinAccess(T).RecreateWnd;
    CheckEqI(19, SendMessageW(T.Handle, TVM_GETINDENT, 0, 0), 'Indent kept on recreate');

    Section('the state the window changed is saved (UIF-831)');
    SendMessageW(T.Handle, TVM_EXPAND, TVE_COLLAPSE, LPARAM(A.ItemId));
    SendMessageW(T.Handle, TVM_SELECTITEM, TVGN_CARET, LPARAM(B.ItemId));
    CheckEqB(False, A.Expanded, 'Expanded follows the window');
    CheckEqB(True, T.Selected = B, 'Selected follows the window');
    TWinAccess(T).RecreateWnd;
    CheckEqS('R1+(A(A1,A2),B*),R2+(Cee),R3', Native(T), 'saved and restored');
    CheckEqB(True, B.Selected, 'B Selected');
    T.Selected := nil;
    CheckEqB(True, T.Selected = nil, 'Selected := nil clears');
    CheckEqI(0, SendMessageW(T.Handle, TVM_GETNEXTITEM, TVGN_CARET, 0), 'no caret item');

    Section('nodes added and removed with a window (UIF-830)');
    N := T.Items.AddChild(R3, 'D');
    CheckEqI(1, Children(T, R3), 'the new parent gets cChildren 1');
    T.Items.AddChildFirst(R3, 'D0');
    T.Items.Insert(R2, 'Q');
    T.Items.AddFirst(A1, 'A0');
    CheckEqS('R1+(A(A0,A1,A2),B),Q,R2+(Cee),R3(D0,D)', Native(T), 'native positions');
    CheckEqS('R1(A(A0,A1,A2),B),Q,R2(Cee),R3(D0,D)', Objects(T), 'object positions');
    CheckEqI(12, T.Items.Count, 'Count');
    CheckEqI(12, SendMessageW(T.Handle, TVM_GETCOUNT, 0, 0), 'native count');
    R3.DeleteChildren;
    CheckEqI(0, Children(T, R3), 'DeleteChildren: cChildren 0');
    CheckEqB(False, R3.HasChildren, 'no children');
    CheckEqI(10, T.Items.Count, 'Count after DeleteChildren');
    T.Items.Delete(T.Items[6]);
    CheckEqS('R1(A(A0,A1,A2),B),R2(Cee),R3', Objects(T), 'Items.Delete');

    Section('deleting deletes the descendants first (UIF-833)');
    T.Deleted := '';
    A.Delete;
    CheckEqS('A0;A1;A2;A;', T.Deleted, 'the children first, then the node');
    CheckEqS('R1(B),R2(Cee),R3', Objects(T), 'objects');
    CheckEqS('R1+(B),R2+(Cee),R3', Native(T), 'native');
    T.Deleted := '';
    R1.Free;
    CheckEqS('B;R1;', T.Deleted, 'Free deletes the same way');
    T.Deleted := '';
    T.Items.Clear;
    CheckEqS('Cee;R2;R3;', T.Deleted, 'Clear');
    CheckEqI(0, T.Items.Count, 'Count 0');
    CheckEqI(0, SendMessageW(T.Handle, TVM_GETCOUNT, 0, 0), 'native count 0');

    Section('helpers');
    T.Items.BeginUpdate;
    try
      for I := 1 to 30 do
      begin
        N := T.Items.Add(nil, 'n' + IntToStr(I));
        T.Items.AddChild(N, 'c' + IntToStr(I));
      end;
    finally
      T.Items.EndUpdate;
    end;
    CheckEqI(60, SendMessageW(T.Handle, TVM_GETCOUNT, 0, 0), 'added inside BeginUpdate');
    N := T.Items.GetFirstNode;
    CheckEqB(True, T.TopItem = N, 'TopItem');
    R := N.GetNextSibling.DisplayRect(True);
    CheckEqB(True, T.GetNodeAt((R.Left + R.Right) div 2, (R.Top + R.Bottom) div 2) = N.GetNextSibling, 'GetNodeAt');
    CheckEqB(True, T.GetNodeAt(R.Right + 150, R.Top + 1) = nil, 'GetNodeAt right of the text: nil');
    T.FullExpand;
    Check(N.Expanded and T.Items[59].Parent.Expanded, 'FullExpand');
    T.Items[59].MakeVisible;
    CheckEqB(True, T.TopItem <> N, 'MakeVisible scrolls');
    Check(T.Items[59].DisplayRect(False).Bottom <= T.ClientHeight, 'the node is visible');
    T.FullCollapse;
    CheckEqB(False, N.Expanded or T.Items[59].Parent.Expanded, 'FullCollapse');

    Section('destroying');
    T.Free;
    T2 := TTreeView.Create(F);
    T2.Parent := F;
    T2.Items.AddChild(T2.Items.Add(nil, 'x'), 'y');
    T2.Free;
    Check(True, 'a tree view with nodes and a window is freed');

    Section('leak');
    Before := GuiObjects;
    for I := 1 to 100 do
    begin
      T2 := TTreeView.Create(nil);
      N := T2.Items.Add(nil, 'a');
      T2.Items.AddChild(N, 'b');
      N.Expanded := True;
      T2.Parent := F;
      T2.HandleNeeded;
      T2.Free;
    end;
    Check(GuiObjects - Before <= 2, 'GUI objects after 100 instances with handles');
  finally
    F.Free;
  end;
end;

begin
  Run;
  Done;
end.
