// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1483
program Img_NoLeak;
{ U-166, phase8/image-list.md UIF-1483: on one image list with a handle and an overlay, 1 000 cycles of Insert (in
  the middle: a rebuild of the native list), Replace, ReplaceIcon, Move, Delete, GetIcon (the icon freed),
  GetAlphaBitmap, Draw of an alpha cell enabled and disabled and DrawOverlay enabled and disabled, after a warm-up
  of 10 cycles. Prints the GDI and USER object counts (GetGuiResources) and the count of live allocations of the
  process after the warm-up and at the end, the number of cycles whose results were as expected and the elapsed
  time of the 1 000 cycles in milliseconds. ImageListLeakTests compiles and runs this program and compares the
  counts. }
uses
  Windows, SysUtils, Classes, Graphics, ImgList;

var
  List: TImageList;
  Alpha, Opaque, Target: TBitmap;
  Icon: TIcon;
  Good: Integer;

{ a pf32bit 16 x 16 bitmap of one canonical pixel value }
function Solid(V: Cardinal): TBitmap;
var
  X, Y: Integer;
begin
  Result := TBitmap.Create;
  Result.PixelFormat := pf32bit;
  Result.SetSize(16, 16);
  for Y := 0 to 15 do
    for X := 0 to 15 do
      PCardinal(NativeUInt(Result.ScanLine[Y]) + NativeUInt(X * 4))^ := V;
end;

procedure Cycle;
var
  Got: TIcon;
  B: TBitmap;
  Ok: Boolean;
begin
  List.Insert(1, Alpha, nil);
  List.Replace(1, Opaque, nil);
  List.ReplaceIcon(1, Icon);
  List.Move(1, 0);
  List.Delete(0);
  Got := TIcon.Create;
  try
    List.GetIcon(0, Got);
    Ok := Got.Handle <> 0;
  finally
    Got.Free;
  end;
  B := TBitmap.Create;
  try
    Ok := Ok and List.GetAlphaBitmap(1, B) and (B.Width = 16);
  finally
    B.Free;
  end;
  List.Draw(Target.Canvas, 0, 0, 0);
  List.Draw(Target.Canvas, 0, 0, 0, False);
  List.DrawOverlay(Target.Canvas, 0, 0, 0, 0);
  List.DrawOverlay(Target.Canvas, 0, 0, 0, 0, False);
  if Ok and (List.Count = 2) then
    Inc(Good);
end;

procedure PrintCounts(const Name: string);
begin
  Writeln(Name, '.gdi=', GetGuiResources(GetCurrentProcess, GR_GDIOBJECTS));
  Writeln(Name, '.user=', GetGuiResources(GetCurrentProcess, GR_USEROBJECTS));
  Writeln(Name, '.allocs=', _LiveAllocCount);
end;

var
  I: Integer;
  Start: UInt64;
begin
  List := TImageList.Create(nil);
  Alpha := Solid(Cardinal($80000080));
  Opaque := Solid(Cardinal($FFFF0000));
  Target := Solid(Cardinal($00FFFFFF));
  Icon := TIcon.Create;
  List.Add(Alpha, nil);
  List.Add(Alpha, nil);
  List.GetIcon(0, Icon);
  if (List.Handle = 0) or not List.Overlay(1, 0) then
  begin
    Writeln('setup failed');
    Halt(2);
  end;
  for I := 1 to 10 do
    Cycle;
  Good := 0;
  PrintCounts('warm');
  Start := GetTickCount64;
  for I := 1 to 1000 do
    Cycle;
  Start := GetTickCount64 - Start;
  PrintCounts('end');
  Writeln('cycles=', Good);
  Writeln('ms=', Start);
  List.Free;
  Alpha.Free;
  Opaque.Free;
  Target.Free;
  Icon.Free;
end.
