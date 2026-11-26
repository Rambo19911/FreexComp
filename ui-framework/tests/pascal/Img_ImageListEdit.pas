// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1460, UIF-1462, UIF-1463, UIF-1467, UIF-1468, UIF-1469, UIF-1470
// phase: 8
// needs: desktop
// opc: --app-type gui
program Img_ImageListEdit;
{ U-165, phase8/image-list.md sections 4 and 5: the store as the single source of truth; opaque sources with a
  mask, a mask colour and Masked = False; the cells of a wide, a narrow and a short source; Insert at the front,
  in the middle and at the end with and without a handle, the native list following the store; Replace with a
  strip; Move both ways and onto itself; every index and size error; CreateSize, SetSize, the AllocBy floor;
  AddImage, AddImages (also Self and nil) and the size mismatch; Assign; Change counts per call and per nested
  update bracket, none for a failure; one Handle value through all edits, still reported by a list view and a tree
  view. Built as a GUI program (common controls of version 6, as a real application). }
uses
  Windows, Messages, SysUtils, Classes, CommCtrl, Graphics, Controls, Forms, ComCtrls, ImgList, TestKit,
  WicTestKit, ImgTestKit;

{ a 16 x 16 opaque cell of one colour (GDI drawing: alpha 0, an opaque source) }
function Solid(Color: TColor; W, H: Integer): TBitmap;
begin
  Result := TBitmap.Create;
  Result.SetSize(W, H);
  Result.Canvas.Brush.Color := Color;
  Result.Canvas.FillRect(Rect(0, 0, W, H));
end;

function Cell(Color: TColor): TBitmap;
begin
  Result := Solid(Color, 16, 16);
end;

function Letter(P: Cardinal): string;
begin
  case P and $FFFFFF of
    $FF0000: Result := 'R';
    $00FF00: Result := 'G';
    $0000FF: Result := 'B';
    $FFFF00: Result := 'Y';
    $00FFFF: Result := 'C';
    $FF00FF: Result := 'M';
  else
    Result := '?';
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
function NativeOrder(L: TCustomImageList): string;
var
  I: Integer;
  S: TBitmap;
begin
  Result := IntToStr(ImageList_GetImageCount(L.Handle)) + ':';
  S := Surface(16, 16, 0);
  try
    for I := 0 to ImageList_GetImageCount(L.Handle) - 1 do
    begin
      ImageList_Draw(L.Handle, I, S.Canvas.Handle, 0, 0, ILD_NORMAL);
      Result := Result + Letter(Quad(S, 5, 5));
    end;
  finally
    S.Free;
  end;
end;

procedure AddColors(L: TCustomImageList; const Colors: string);
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
      'Y': C := clYellow;
      'C': C := clAqua;
    else
      C := clFuchsia;
    end;
    B := Cell(C);
    try
      L.Add(B, nil);
    finally
      B.Free;
    end;
  end;
end;

var
  Changes: TChangeCounter;

function NewList: TImageList;
begin
  Result := TImageList.Create(nil);
  Result.OnChange := Changes.Changed;
end;

{ the exception an edit raises as 'Class: Message', or 'none' }
function Attempt(L, Other: TImageList; Op: Integer): string;
var
  B, E: TBitmap;
  Icon: TIcon;
begin
  Result := 'none';
  B := Cell(clYellow);
  E := TBitmap.Create;
  Icon := TIcon.Create;
  try
    try
      case Op of
        0: L.Insert(-1, B, nil);
        1: L.Insert(L.Count + 1, B, nil);
        2: L.InsertMasked(L.Count + 1, B, clRed);
        3: L.InsertIcon(-1, Icon);
        4: L.Replace(L.Count, B, nil);
        5: L.ReplaceMasked(-1, B, clRed);
        6: L.ReplaceIcon(L.Count, Icon);
        7: L.Move(-1, 0);
        8: L.Move(0, L.Count);
        9: L.Delete(L.Count);
        10: L.AddImage(Other, Other.Count);
        11: L.Add(nil, nil);
        12: L.Add(E, nil);
        13: L.AddMasked(nil, clRed);
        14: L.Insert(0, nil, nil);
        15: L.InsertMasked(0, E, clRed);
        16: L.Replace(0, nil, nil);
        17: L.ReplaceMasked(0, E, clRed);
        18: L.AddIcon(nil);
        19: L.AddIcon(Icon);
        20: L.InsertIcon(0, Icon);
        21: L.ReplaceIcon(0, nil);
        22: L.AddImage(Other, 0);
        23: L.AddImages(Other);
      end;
    except
      on X: Exception do
        Result := X.ClassName + ': ' + X.Message;
    end;
  finally
    B.Free;
    E.Free;
    Icon.Free;
  end;
end;

procedure CheckInsert(WithHandle: Boolean);
var
  L: TImageList;
  B: TBitmap;
  H: HIMAGELIST;
  Suffix: string;
begin
  if WithHandle then
    Suffix := ' (with handle)'
  else
    Suffix := ' (no handle)';
  Section('Insert' + Suffix);
  L := NewList;
  try
    AddColors(L, 'RGB');
    H := 0;
    if WithHandle then
      H := L.Handle;
    CheckEqB(WithHandle, L.HandleAllocated, 'handle only when read');
    CheckEqI(3, L.Count, 'Count from the store');
    Changes.Count := 0;
    B := Cell(clYellow);
    try
      L.Insert(0, B, nil);
    finally
      B.Free;
    end;
    CheckEqS('YRGB', StoreOrder(L), 'insert at the front');
    CheckEqI(1, Changes.Count, 'one Change');
    B := Solid(clAqua, 32, 16);
    try
      L.Insert(2, B, nil);
    finally
      B.Free;
    end;
    CheckEqS('YRCCGB', StoreOrder(L), 'a 2-cell strip inserted in the middle, in order');
    CheckEqI(2, Changes.Count, 'one Change per Insert');
    B := Cell(clFuchsia);
    try
      L.Insert(L.Count, B, nil);
    finally
      B.Free;
    end;
    CheckEqS('YRCCGBM', StoreOrder(L), 'insert at the end');
    CheckEqB(WithHandle, L.HandleAllocated, 'edits do not create a handle');
    if WithHandle then
      Check(L.Handle = H, 'Handle value unchanged');
    CheckEqS('7:YRCCGBM', NativeOrder(L), 'the native list follows the store');
  finally
    L.Free;
  end;
end;

procedure CheckReplaceMove;
var
  L: TImageList;
  B: TBitmap;
  Icon: TIcon;
  H: HIMAGELIST;
begin
  Section('Replace, Move');
  L := NewList;
  try
    AddColors(L, 'RGBY');
    H := L.Handle;
    Changes.Count := 0;
    B := TBitmap.Create;
    try
      B.SetSize(32, 16);
      B.Canvas.Brush.Color := clAqua;
      B.Canvas.FillRect(Rect(0, 0, 16, 16));
      B.Canvas.Brush.Color := clFuchsia;
      B.Canvas.FillRect(Rect(16, 0, 32, 16));
      L.Replace(1, B, nil);
      CheckEqS('RCBY', StoreOrder(L), 'Replace with a 2-cell strip uses cell 0');
      CheckEqI(4, L.Count, 'Replace keeps Count');
      CheckEqI(1, Changes.Count, 'Replace: one Change');
      L.ReplaceMasked(2, B, clAqua);
      CheckEqS('00000000', QuadText(CellQuad(L, 2, 5, 5)), 'ReplaceMasked: the mask colour is transparent');
    finally
      B.Free;
    end;
    Icon := TIcon.Create;
    try
      L.GetIcon(0, Icon);
      L.ReplaceIcon(2, Icon);
    finally
      Icon.Free;
    end;
    CheckEqS('RCRY', StoreOrder(L), 'ReplaceIcon');
    CheckEqS('4:RCRY', NativeOrder(L), 'native list after Replace');
    AddColors(L, 'G');
    L.Delete(2);
    CheckEqS('RCYG', StoreOrder(L), 'Delete');
    Changes.Count := 0;
    L.Move(0, 2);
    CheckEqS('CYRG', StoreOrder(L), 'Move forward');
    L.Move(3, 0);
    CheckEqS('GCYR', StoreOrder(L), 'Move backward');
    CheckEqI(2, Changes.Count, 'one Change per Move');
    L.Move(1, 1);
    CheckEqS('GCYR', StoreOrder(L), 'Move onto itself changes nothing');
    CheckEqI(2, Changes.Count, 'and fires nothing');
    CheckEqS('4:GCYR', NativeOrder(L), 'native list after Move');
    Check(L.Handle = H, 'Handle value unchanged');
  finally
    L.Free;
  end;
end;

procedure CheckErrors;
const
  Index = 'EInvalidOperation: Invalid ImageList Index';
  Size = 'EInvalidOperation: Invalid image size';
  Mismatch = 'EInvalidOperation: Image list sizes do not match';
var
  L, Other: TImageList;
  I: Integer;
begin
  Section('index and size errors (UIF-1468, UIF-1482)');
  L := NewList;
  Other := TImageList.Create(nil);
  try
    AddColors(L, 'RGB');
    AddColors(Other, 'Y');
    Check(L.Handle <> 0, 'a handle');
    Changes.Count := 0;
    for I := 0 to 10 do
      CheckEqS(Index, Attempt(L, Other, I), 'index error ' + IntToStr(I));
    for I := 11 to 21 do
      CheckEqS(Size, Attempt(L, Other, I), 'size error ' + IntToStr(I));
    CheckEqS('RGB', StoreOrder(L), 'nothing changed');
    CheckEqS('3:RGB', NativeOrder(L), 'native list unchanged');
    CheckEqI(0, Changes.Count, 'no Change for a failure');
    Other.Width := 24;
    AddColors(Other, 'Y');
    CheckEqS(Mismatch, Attempt(L, Other, 22), 'AddImage: size mismatch');
    CheckEqS(Mismatch, Attempt(L, Other, 23), 'AddImages: size mismatch');
    CheckEqI(0, Changes.Count, 'still no Change');
  finally
    L.Free;
    Other.Free;
  end;
end;

procedure CheckMasks;
var
  L: TImageList;
  B, M: TBitmap;
begin
  Section('opaque sources: mask, mask colour, Masked = False (UIF-1462)');
  L := NewList;
  B := Cell(clRed);
  M := Solid(clBlack, 8, 8);
  try
    B.Canvas.Pixels[5, 6] := clBlue;
    M.Canvas.Pixels[3, 3] := RGB(0, 1, 0);
    L.Add(B, M);
    CheckEqS('00000000', QuadText(CellQuad(L, 0, 3, 3)), 'a mask pixel that is not black is transparent');
    CheckEqS('FFFF0000', QuadText(CellQuad(L, 0, 2, 3)), 'a black mask pixel is opaque');
    CheckEqS('FFFF0000', QuadText(CellQuad(L, 0, 12, 12)), 'outside the mask counts as black');
    L.AddMasked(B, clBlue);
    CheckEqS('00000000', QuadText(CellQuad(L, 1, 5, 6)), 'the mask colour is transparent');
    CheckEqS('FFFF0000', QuadText(CellQuad(L, 1, 5, 5)), 'other colours are opaque');
    L.AddMasked(B, RGB(0, 0, 254));
    CheckEqS('FF0000FF', QuadText(CellQuad(L, 2, 5, 6)), 'only the exact colour is transparent');
    CheckEqI(clBlue, B.Canvas.Pixels[5, 6], 'the source is not modified');
    CheckEqI(RGB(0, 1, 0), M.Canvas.Pixels[3, 3], 'the mask is not modified');
    L.Masked := False;
    CheckEqI(0, L.Count, 'Masked empties the list');
    L.Add(B, M);
    L.AddMasked(B, clBlue);
    CheckEqS('FFFF0000', QuadText(CellQuad(L, 0, 3, 3)), 'Masked = False ignores the mask');
    CheckEqS('FF0000FF', QuadText(CellQuad(L, 1, 5, 6)), 'Masked = False ignores the mask colour');
    CheckEqS('2:RR', NativeOrder(L), 'unmasked native list');
  finally
    L.Free;
    B.Free;
    M.Free;
  end;
end;

procedure CheckCellCount;
var
  L: TImageList;
  B: TBitmap;
begin
  Section('cells of a source (UIF-1463)');
  L := NewList;
  try
    B := Solid(clRed, 40, 16);
    try
      CheckEqI(0, L.Add(B, nil), 'Add returns the first new index');
      CheckEqI(2, L.Count, '40 pixels wide: 2 cells, the rest ignored');
    finally
      B.Free;
    end;
    B := Solid(clLime, 10, 8);
    try
      CheckEqI(2, L.Add(B, nil), 'a narrow source: one cell');
      CheckEqI(3, L.Count, 'Count 3');
      CheckEqS('FF00FF00', QuadText(CellQuad(L, 2, 9, 7)), 'inside the source');
      CheckEqS('00000000', QuadText(CellQuad(L, 2, 10, 0)), 'right of the source: transparent');
      CheckEqS('00000000', QuadText(CellQuad(L, 2, 0, 8)), 'below the source: transparent');
    finally
      B.Free;
    end;
  finally
    L.Free;
  end;
end;

procedure CheckSizes;
var
  L: TImageList;
begin
  Section('CreateSize, SetSize, AllocBy (UIF-1468)');
  L := TImageList.CreateSize(24, 20);
  try
    CheckEqI(24, L.Width, 'CreateSize width');
    CheckEqI(20, L.Height, 'CreateSize height');
    Check(L.Owner = nil, 'CreateSize: no owner');
    CheckEqB(True, L.Masked, 'CreateSize: other defaults');
    CheckEqI(4, L.AllocBy, 'AllocBy default 4');
    L.OnChange := Changes.Changed;
    Changes.Count := 0;
    AddColors(L, 'RG');
    L.SetSize(24, 20);
    CheckEqI(2, L.Count, 'SetSize to the same size keeps the cells');
    CheckEqI(2, Changes.Count, 'and fires nothing');
    L.SetSize(32, 32);
    CheckEqI(32, L.Width, 'SetSize width');
    CheckEqI(32, L.Height, 'SetSize height');
    CheckEqI(0, L.Count, 'SetSize empties the list');
    CheckEqI(3, Changes.Count, 'SetSize: one Change');
    L.AllocBy := 0;
    CheckEqI(1, L.AllocBy, 'AllocBy 0 is stored as 1');
    L.AllocBy := -5;
    CheckEqI(1, L.AllocBy, 'AllocBy -5 is stored as 1');
    L.AllocBy := 10;
    CheckEqI(10, L.AllocBy, 'AllocBy 10');
    CheckEqI(5, Changes.Count, 'AllocBy changes fire Change');
    AddColors(L, 'B');
    CheckEqS('1:B', NativeOrder(L), 'a handle with AllocBy 10');
  finally
    L.Free;
  end;
end;

procedure CheckCopies;
var
  L, M: TImageList;
begin
  Section('AddImage, AddImages, Assign (UIF-1469)');
  L := NewList;
  M := TImageList.Create(nil);
  try
    AddColors(L, 'R');
    AddColors(M, 'GBY');
    Check(L.Handle <> 0, 'a handle');
    Changes.Count := 0;
    CheckEqI(1, L.AddImage(M, 1), 'AddImage returns the new index');
    CheckEqS('RB', StoreOrder(L), 'AddImage copies the cell');
    L.AddImages(M);
    CheckEqS('RBGBY', StoreOrder(L), 'AddImages appends all cells in order');
    CheckEqI(2, Changes.Count, 'one Change each');
    L.AddImages(L);
    CheckEqS('RBGBYRBGBY', StoreOrder(L), 'AddImages(Self) doubles the list');
    L.AddImages(nil);
    CheckEqI(3, Changes.Count, 'AddImages(nil) does nothing');
    CheckEqS('10:RBGBYRBGBY', NativeOrder(L), 'native list after the copies');
    CheckEqS('GBY', StoreOrder(M), 'the source list is unchanged');
  finally
    L.Free;
    M.Free;
  end;

  L := TImageList.Create(nil);
  M := NewList;
  try
    L.SetSize(24, 20);
    L.Masked := False;
    L.BkColor := clRed;
    L.DrawingStyle := dsSelected;
    L.AllocBy := 7;
    L.ColorDepth := cd24Bit;
    AddColors(L, 'CM');
    AddColors(M, 'R');
    Check(M.Handle <> 0, 'a handle before Assign');
    Changes.Count := 0;
    M.Assign(L);
    CheckEqI(1, Changes.Count, 'Assign: one Change');
    CheckEqI(24, M.Width, 'Assign: Width');
    CheckEqI(20, M.Height, 'Assign: Height');
    CheckEqB(False, M.Masked, 'Assign: Masked');
    CheckEqI(clRed, M.BkColor, 'Assign: BkColor');
    Check(M.DrawingStyle = dsSelected, 'Assign: DrawingStyle');
    CheckEqI(7, M.AllocBy, 'Assign: AllocBy');
    Check(M.ColorDepth = cd24Bit, 'Assign: ColorDepth');
    CheckEqS('CM', StoreOrder(M), 'Assign: the cells');
    CheckEqB(False, L.HandleAllocated, 'Assign does not create the source handle');
    Check(M.Handle <> 0, 'a handle of its own');
    CheckEqS('2:CM', NativeOrder(M), 'Assign: the native list');
    try
      M.Assign(nil);
      Check(False, 'Assign(nil) raises');
    except
      on E: Exception do
        CheckEqS('EConvertError: Cannot assign nil to a TImageList', E.ClassName + ': ' + E.Message,
          'Assign(nil): the conversion error');
    end;
    CheckEqI(1, Changes.Count, 'no Change for a failed Assign');
  finally
    L.Free;
    M.Free;
  end;
end;

procedure CheckBrackets;
var
  L: TImageList;
begin
  Section('BeginUpdate, EndUpdate (UIF-1470)');
  L := NewList;
  try
    Changes.Count := 0;
    L.EndUpdate;
    CheckEqI(0, Changes.Count, 'EndUpdate without BeginUpdate does nothing');
    L.BeginUpdate;
    L.BeginUpdate;
    AddColors(L, 'RG');
    L.Delete(0);
    L.EndUpdate;
    CheckEqI(0, Changes.Count, 'inner EndUpdate: nothing');
    L.EndUpdate;
    CheckEqI(1, Changes.Count, 'outer EndUpdate: one Change');
    L.BeginUpdate;
    L.EndUpdate;
    CheckEqI(1, Changes.Count, 'an empty bracket fires nothing');
    AddColors(L, 'B');
    CheckEqI(2, Changes.Count, 'after the bracket each edit fires again');
    L.BeginUpdate;
    try
      L.Delete(9);
    except
      on E: Exception do
        CheckEqS('Invalid ImageList Index', E.Message, 'Delete(9) inside a bracket raises');
    end;
    L.EndUpdate;
    CheckEqI(2, Changes.Count, 'a failure inside a bracket fires nothing');
  finally
    L.Free;
  end;
end;

procedure CheckClients;
var
  F: TForm;
  LV: TListView;
  TV: TTreeView;
  L: TImageList;
  H: HIMAGELIST;
  B: TBitmap;

  procedure Same(const Name: string);
  begin
    Check(L.Handle = H, Name + ': Handle unchanged');
    Check(HIMAGELIST(SendMessageW(LV.Handle, LVM_GETIMAGELIST, LVSIL_SMALL, 0)) = H, Name + ': list view');
    Check(HIMAGELIST(SendMessageW(TV.Handle, TVM_GETIMAGELIST, TVSIL_NORMAL, 0)) = H, Name + ': tree view');
  end;

begin
  Section('one handle for the clients (UIF-1467)');
  F := TForm.CreateNew(nil);
  L := TImageList.Create(F);
  try
    AddColors(L, 'RGB');
    LV := TListView.Create(F);
    LV.Parent := F;
    TV := TTreeView.Create(F);
    TV.Parent := F;
    LV.SmallImages := L;
    TV.Images := L;
    H := L.Handle;
    Same('start');
    B := Cell(clYellow);
    try
      L.Insert(1, B, nil);
      Same('middle Insert');
      L.Replace(0, B, nil);
      Same('Replace');
    finally
      B.Free;
    end;
    L.Move(0, 3);
    Same('Move');
    L.Delete(1);
    Same('Delete');
    L.Clear;
    Same('Clear');
    AddColors(L, 'C');
    Same('Add');
    CheckEqS('1:C', NativeOrder(L), 'native list at the end');
  finally
    F.Free;
  end;
end;

begin
  Application.Initialize;
  Changes := TChangeCounter.Create;
  CheckInsert(False);
  CheckInsert(True);
  CheckReplaceMove;
  CheckErrors;
  CheckMasks;
  CheckCellCount;
  CheckSizes;
  CheckCopies;
  CheckBrackets;
  CheckClients;
  Changes.Free;
  Done;
end.
