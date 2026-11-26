// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1478
// phase: 8
// needs: desktop
// opc: --app-type gui
program Ctl_ImageListClients;
{ U-166, phase8/image-list.md UIF-1478: a form with a tool bar (an enabled and a disabled button showing the
  half-transparent cell), a list view and a tree view on one image list. The tool bar painted into a memory
  bitmap over clBtnFace (WM_PRINTCLIENT) gives the source-over blend of the cell for the enabled button and the
  grey, halved conversion for the disabled one (within 1); the list view draws the cell through the native list
  (blend over clWindow). After a middle Insert and a Move both views still report the list's unchanged Handle
  (LVM_GETIMAGELIST, TVM_GETIMAGELIST) and their items still give the image index of their ImageIndex
  (LVM_GETITEMW / TVM_GETITEMW, the callback answered by the framework). Built as a GUI program (common controls
  of version 6). }
uses
  Windows, Messages, SysUtils, Classes, CommCtrl, Graphics, Controls, Forms, ComCtrls, ImgList, TestKit,
  WicTestKit, ImgTestKit;

function SolidCell(V: Cardinal): TBitmap;
begin
  Result := CornerCell(V, V);
end;

procedure AddSolid(L: TImageList; V: Cardinal);
var
  B: TBitmap;
begin
  B := SolidCell(V);
  try
    L.Add(B, nil);
  finally
    B.Free;
  end;
end;

{ the client area of a window printed into a pf32bit bitmap filled with Background first }
function Printed(W: TWinControl; Background: TColor): TBitmap;
begin
  Result := Surface(W.ClientWidth, W.ClientHeight, RGBOf(Background));
  SendMessageW(W.Handle, WM_PRINTCLIENT, WPARAM(Result.Canvas.Handle), PRF_CLIENT);
end;

function ListImage(LV: TListView; Index: Integer): Integer;
var
  It: TLVItemW;
begin
  FillChar(It, SizeOf(It), 0);
  It.mask := LVIF_IMAGE;
  It.iItem := Index;
  SendMessageW(LV.Handle, LVM_GETITEMW, 0, LPARAM(@It));
  Result := It.iImage;
end;

function TreeImage(N: TTreeNode): Integer;
var
  It: TTVItemW;
begin
  FillChar(It, SizeOf(It), 0);
  It.mask := TVIF_HANDLE or TVIF_IMAGE;
  It.hItem := N.ItemId;
  SendMessageW(N.TreeView.Handle, TVM_GETITEMW, 0, LPARAM(@It));
  Result := It.iImage;
end;

var
  F: TForm;
  L: TImageList;
  Bar: TToolBar;
  On_, Off: TToolButton;
  LV: TListView;
  TV: TTreeView;
  A, B: TListItem;
  NA, NB: TTreeNode;
  P: TBitmap;
  H: HIMAGELIST;
  R: TRect;
  Face, Window: Cardinal;

procedure CheckViews(const Name: string);
begin
  Check(L.Handle = H, Name + ': Handle unchanged');
  Check(HIMAGELIST(SendMessageW(LV.Handle, LVM_GETIMAGELIST, LVSIL_SMALL, 0)) = H, Name + ': list view list');
  Check(HIMAGELIST(SendMessageW(TV.Handle, TVM_GETIMAGELIST, TVSIL_NORMAL, 0)) = H, Name + ': tree view list');
  CheckEqI(A.ImageIndex, ListImage(LV, 0), Name + ': list item 0 image index');
  CheckEqI(B.ImageIndex, ListImage(LV, 1), Name + ': list item 1 image index');
  CheckEqI(NA.ImageIndex, TreeImage(NA), Name + ': tree node 0 image index');
  CheckEqI(NB.ImageIndex, TreeImage(NB), Name + ': tree node 1 image index');
end;

begin
  Application.Initialize;
  Face := RGBOf(clBtnFace);
  Window := RGBOf(clWindow);
  F := TForm.CreateNew(nil);
  try
    F.ClientWidth := 400;
    F.ClientHeight := 300;
    L := TImageList.Create(F);
    AddSolid(L, BlockBlue);                                 { 0: blue with alpha 128 }
    AddSolid(L, BlockRed);                                  { 1: opaque red }
    AddSolid(L, Cardinal($FF00FF00));                       { 2: opaque lime }

    Bar := TToolBar.Create(F);
    Bar.Parent := F;
    On_ := TToolButton.Create(F);
    On_.Parent := Bar;
    On_.ImageIndex := 0;
    Off := TToolButton.Create(F);
    Off.Parent := Bar;
    Off.ImageIndex := 0;
    Off.Enabled := False;
    Bar.Images := L;

    LV := TListView.Create(F);
    LV.Parent := F;
    LV.SetBounds(0, 60, 200, 200);
    LV.ViewStyle := vsSmallIcon;
    LV.SmallImages := L;
    A := LV.Items.Add;
    A.Caption := 'a';
    A.ImageIndex := 0;
    B := LV.Items.Add;
    B.Caption := 'b';
    B.ImageIndex := 2;

    TV := TTreeView.Create(F);
    TV.Parent := F;
    TV.SetBounds(200, 60, 200, 200);
    TV.Images := L;
    NA := TV.Items.Add(nil, 'a');
    NA.ImageIndex := 1;
    NB := TV.Items.Add(nil, 'b');
    NB.ImageIndex := 2;
    H := L.Handle;

    Section('tool bar draws through Draw (UIF-1475, UIF-1477)');
    P := Printed(Bar, clBtnFace);
    try
      CheckRGB(Blend(BlockBlue, Face), Quad(P, On_.Left + On_.Width div 2, On_.Top + On_.Height div 2), 1,
        'enabled button: the cell blended over clBtnFace');
      CheckRGB(Blend(DisabledPixel(BlockBlue), Face), Quad(P, Off.Left + Off.Width div 2, Off.Top + Off.Height div 2),
        1, 'disabled button: grey and halved over clBtnFace');
    finally
      P.Free;
    end;

    Section('list view and tree view draw through the native list (UIF-1466)');
    FillChar(R, SizeOf(R), 0);
    R.Left := LVIR_ICON;
    SendMessageW(LV.Handle, LVM_GETITEMRECT, 0, LPARAM(@R));
    P := Printed(LV, clWindow);
    try
      CheckRGB(Blend(BlockBlue, Window), Quad(P, (R.Left + R.Right) div 2, (R.Top + R.Bottom) div 2), 1,
        'list view item 0: the cell blended over clWindow');
    finally
      P.Free;
    end;
    CheckViews('before the edits');

    Section('the clients keep the handle through edits (UIF-1467, UIF-1478)');
    AddSolid(L, BlockRed);
    L.Delete(3);
    CheckViews('after Add and Delete');
    P := SolidCell(BlockClear);
    try
      L.Insert(1, P, nil);
    finally
      P.Free;
    end;
    CheckEqI(4, L.Count, 'middle Insert');
    CheckViews('after a middle Insert');
    L.Move(0, 3);
    CheckViews('after Move');
  finally
    F.Free;
  end;
  Done;
end.
