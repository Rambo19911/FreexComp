// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1469, UIF-1482
// phase: 8
// needs: desktop
// opc: --app-type gui
program Img_ImageListAssignFailure;
{ U-169, U-171, phase8/image-list.md UIF-1482 for Assign: with the creation of a native list failing (the
  protected seam makes ImageList_Create return 0), Assign to a list with a handle raises EOutOfResources and
  leaves the target exactly as before the call: Width, Height, Count, the other copied properties, the pixels of
  the store, the overlay table, the Handle value, the native list and its background colour; no Change fires.
  Both for a source of another format and of the same format. Without a handle Assign needs no native list and
  succeeds; with the seam off Assign to a list with a handle succeeds with one Change and a native list of the
  copied cells with the source's background colour. Built as a GUI program (common controls of version 6, as a
  real application). }
uses
  Windows, Messages, SysUtils, Classes, CommCtrl, Graphics, Controls, Forms, ImgList, TestKit, WicTestKit,
  ImgTestKit;

type
  TSeamImageList = class(TImageList)
  public
    class procedure FailNativeCreate(Value: Boolean);
  end;

class procedure TSeamImageList.FailNativeCreate(Value: Boolean);
begin
  FFailNativeCreate := Value;
end;

function Cell(Color: TColor; W, H: Integer): TBitmap;
begin
  Result := TBitmap.Create;
  Result.SetSize(W, H);
  Result.Canvas.Brush.Color := Color;
  Result.Canvas.FillRect(Rect(0, 0, W, H));
end;

function Letter(P: Cardinal): string;
begin
  case P and $FFFFFF of
    $FF0000: Result := 'R';
    $00FF00: Result := 'G';
    $0000FF: Result := 'B';
    $00FFFF: Result := 'C';
    $FF00FF: Result := 'M';
  else
    Result := '?';
  end;
end;

procedure AddColors(L: TImageList; const Colors: string);
var
  I: Integer;
  B: TBitmap;
  C: TColor;
begin
  for I := 1 to Length(Colors) do
  begin
    case Colors[I] of
      'R': C := clRed;
      'G': C := clLime;
      'B': C := clBlue;
      'C': C := clAqua;
    else
      C := clFuchsia;
    end;
    B := Cell(C, L.Width, L.Height);
    try
      L.Add(B, nil);
    finally
      B.Free;
    end;
  end;
end;

{ the colours of the cells in the store, read at pixel (5, 5) }
function StoreOrder(L: TCustomImageList): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to L.Count - 1 do
    Result := Result + Letter(CellQuad(L, I, 5, 5));
end;

{ the colours of the cells of the native list, drawn one by one; the native count first }
function NativeOrder(H: HIMAGELIST): string;
var
  I: Integer;
  S: TBitmap;
begin
  Result := IntToStr(ImageList_GetImageCount(H)) + ':';
  S := Surface(16, 16, 0);
  try
    for I := 0 to ImageList_GetImageCount(H) - 1 do
    begin
      ImageList_Draw(H, I, S.Canvas.Handle, 0, 0, ILD_NORMAL);
      Result := Result + Letter(Quad(S, 5, 5));
    end;
  finally
    S.Free;
  end;
end;

{ cell 0 drawn with overlay 1: the overlay's colour when the table maps it, else cell 0's }
function OverlayDrawn(L: TCustomImageList): string;
var
  S: TBitmap;
begin
  S := Surface(16, 16, 0);
  try
    L.DrawOverlay(S.Canvas, 0, 0, 0, 1);
    Result := Letter(Quad(S, 5, 5));
  finally
    S.Free;
  end;
end;

var
  Changes: TChangeCounter;

{ a 16 x 16 masked target with cells R, G, overlay 1 = cell 1, BkColor clBlue, AllocBy 5 and a handle }
function NewTarget: TSeamImageList;
begin
  Result := TSeamImageList.Create(nil);
  Result.OnChange := Changes.Changed;
  Result.BkColor := clBlue;
  Result.AllocBy := 5;
  AddColors(Result, 'RG');
  Result.Overlay(1, 1);
  Check(Result.Handle <> 0, 'a handle before Assign');
end;

{ Assign with the seam on; the exception as 'Class: Message', or 'none' }
function FailedAssign(Target, Source: TCustomImageList): string;
begin
  Result := 'none';
  TSeamImageList.FailNativeCreate(True);
  try
    try
      Target.Assign(Source);
    except
      on E: Exception do
        Result := E.ClassName + ': ' + E.Message;
    end;
  finally
    TSeamImageList.FailNativeCreate(False);
  end;
end;

procedure CheckUnchanged(M: TSeamImageList; H: HIMAGELIST);
begin
  CheckEqI(0, Changes.Count, 'no Change');
  CheckEqI(16, M.Width, 'Width');
  CheckEqI(16, M.Height, 'Height');
  CheckEqI(2, M.Count, 'Count');
  CheckEqB(True, M.Masked, 'Masked');
  Check(M.ColorDepth = cd32Bit, 'ColorDepth');
  CheckEqI(clBlue, M.BkColor, 'BkColor');
  CheckEqI(5, M.AllocBy, 'AllocBy');
  Check(M.DrawingStyle = dsNormal, 'DrawingStyle');
  CheckEqI(clNone, M.BlendColor, 'BlendColor');
  CheckEqS('RG', StoreOrder(M), 'the pixels of the store');
  CheckEqB(True, M.HandleAllocated, 'the handle is kept');
  Check(M.Handle = H, 'the Handle value');
  CheckEqS('2:RG', NativeOrder(M.Handle), 'the native list');
  CheckEqI(ColorToRGB(clBlue), ImageList_GetBkColor(M.Handle), 'the native background colour');
  CheckEqS('G', OverlayDrawn(M), 'the overlay table');
end;

procedure CheckOtherFormat;
var
  L: TImageList;
  M: TSeamImageList;
  H: HIMAGELIST;
begin
  Section('Assign from another format fails natively (UIF-1482)');
  L := TImageList.Create(nil);
  M := NewTarget;
  try
    L.SetSize(24, 20);
    L.Masked := False;
    L.BkColor := clRed;
    L.DrawingStyle := dsSelected;
    L.BlendColor := clLime;
    L.AllocBy := 7;
    L.ColorDepth := cd24Bit;
    AddColors(L, 'CMB');
    H := M.Handle;
    Changes.Count := 0;
    CheckEqS('EOutOfResources: Out of system resources', FailedAssign(M, L), 'EOutOfResources');
    CheckUnchanged(M, H);
    CheckEqB(False, L.HandleAllocated, 'no source handle');
    CheckEqS('CMB', StoreOrder(L), 'the source is unchanged');
  finally
    L.Free;
    M.Free;
  end;
end;

procedure CheckSameFormat;
var
  L: TImageList;
  M: TSeamImageList;
  H: HIMAGELIST;
begin
  Section('Assign from the same format fails natively (UIF-1482)');
  L := TImageList.Create(nil);
  M := NewTarget;
  try
    AddColors(L, 'CMB');
    H := M.Handle;
    Changes.Count := 0;
    CheckEqS('EOutOfResources: Out of system resources', FailedAssign(M, L), 'EOutOfResources');
    CheckUnchanged(M, H);
  finally
    L.Free;
    M.Free;
  end;
end;

procedure CheckNoHandle;
var
  L: TImageList;
  M: TSeamImageList;
begin
  Section('Assign without a handle needs no native list');
  L := TImageList.Create(nil);
  M := TSeamImageList.Create(nil);
  M.OnChange := Changes.Changed;
  try
    L.SetSize(24, 20);
    AddColors(L, 'CM');
    Changes.Count := 0;
    CheckEqS('none', FailedAssign(M, L), 'no exception');
    CheckEqI(1, Changes.Count, 'one Change');
    CheckEqI(24, M.Width, 'Width copied');
    CheckEqS('CM', StoreOrder(M), 'cells copied');
    CheckEqB(False, M.HandleAllocated, 'still no handle');
  finally
    L.Free;
    M.Free;
  end;
end;

procedure CheckSuccess;
var
  L: TImageList;
  M: TSeamImageList;
begin
  Section('Assign with the seam off');
  L := TImageList.Create(nil);
  M := NewTarget;
  try
    L.SetSize(24, 20);
    L.BkColor := clRed;
    AddColors(L, 'CMB');
    L.Overlay(2, 1);
    Changes.Count := 0;
    M.Assign(L);
    CheckEqI(1, Changes.Count, 'one Change');
    CheckEqI(24, M.Width, 'Width');
    CheckEqI(20, M.Height, 'Height');
    CheckEqI(4, M.AllocBy, 'AllocBy');
    CheckEqS('CMB', StoreOrder(M), 'the cells');
    CheckEqB(True, M.HandleAllocated, 'a handle');
    CheckEqS('3:CMB', NativeOrder(M.Handle), 'the native list');
    CheckEqI(ColorToRGB(clRed), ImageList_GetBkColor(M.Handle), 'the native background colour');
    CheckEqS('B', OverlayDrawn(M), 'the overlay table');
  finally
    L.Free;
    M.Free;
  end;
end;

begin
  Application.Initialize;
  Changes := TChangeCounter.Create;
  CheckOtherFormat;
  CheckSameFormat;
  CheckNoHandle;
  CheckSuccess;
  Changes.Free;
  Done;
end.
