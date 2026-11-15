// covers: UIF-540, UIF-541, UIF-542, UIF-543, UIF-547
// opc: -U ..\..\src
program Gfx_BitmapBasics;
{ U-008, graphics.md section 4.1: TBitmap storage is a top-down DIB section (pf32bit default, pf24bit
  allowed, other formats raise); SetSize/Width/Height keep the overlapping pixels and fill new ones white
  (alpha 0); 0 x n is empty without handle; negative sizes raise; Canvas draws into the DIB through a
  memory DC that survives until a resize, and its OnChange marks the bitmap Modified; ScanLine rows (row 0 =
  top, stride rule, B G R [A] order); ReleaseHandle gives the DIB away; Assign makes a deep copy. }
uses
  Windows, SysUtils, Classes, Graphics, TestKit;

type
  TBytes4 = array[0..3] of Byte;
  PBytes4 = ^TBytes4;

  TRecorder = class
    Count: Integer;
    procedure Changed(Sender: TObject);
  end;

procedure TRecorder.Changed(Sender: TObject);
begin
  Inc(Count);
end;

{ the bytes of pixel X in row Y, in memory order }
function PixelBytes(B: TBitmap; X, Y, BytesPerPixel: Integer): string;
var
  P: PByte;
  I: Integer;
begin
  P := PByte(B.ScanLine[Y]);
  Inc(P, X * BytesPerPixel);
  Result := '';
  for I := 0 to BytesPerPixel - 1 do
  begin
    if I > 0 then
      Result := Result + ' ';
    Result := Result + IntToHex(P^, 2);
    Inc(P);
  end;
end;

function Stride(B: TBitmap): Integer;
begin
  Result := NativeInt(B.ScanLine[1]) - NativeInt(B.ScanLine[0]);
end;

procedure CheckDefaults;
var
  B: TBitmap;
begin
  Section('defaults');
  B := TBitmap.Create;
  try
    CheckEqB(True, B.Empty, 'Empty');
    CheckEqI(0, B.Width, 'Width');
    CheckEqI(0, B.Height, 'Height');
    Check(B.PixelFormat = pf32bit, 'PixelFormat pf32bit');
    CheckEqI(0, B.Handle, 'no handle');
    CheckEqB(False, B.Modified, 'Modified');
    CheckEqB(False, B.Transparent, 'Transparent');
  finally
    B.Free;
  end;
end;

procedure CheckStorage;
var
  B: TBitmap;
  BM: TBitmapRec;
  R: TRecorder;
begin
  Section('storage (UIF-540, UIF-541)');
  B := TBitmap.Create;
  R := TRecorder.Create;
  try
    B.OnChange := R.Changed;
    B.SetSize(5, 2);
    CheckEqI(1, R.Count, 'SetSize fires OnChange once');
    CheckEqB(False, B.Empty, 'not empty');
    Check(B.Handle <> 0, 'DIB created');
    FillChar(BM, SizeOf(BM), 0);
    CheckEqI(SizeOf(BM), GetObjectW(B.Handle, SizeOf(BM), @BM), 'GetObjectW BITMAP');
    CheckEqI(5, BM.bmWidth, 'bmWidth');
    CheckEqI(2, BM.bmHeight, 'bmHeight');
    CheckEqI(32, BM.bmBitsPixel, 'bmBitsPixel');
    Check(BM.bmBits <> nil, 'a DIB section (bits accessible)');
    CheckEqS('FF FF FF 00', PixelBytes(B, 0, 0, 4), 'new pixels white, alpha 0');
    CheckEqS('FF FF FF 00', PixelBytes(B, 4, 1, 4), 'last pixel white');
    CheckEqI(20, Stride(B), '32-bit stride for width 5');
    B.SetSize(5, 2);
    CheckEqI(1, R.Count, 'same size: no event');

    Section('Canvas (UIF-542)');
    B.Modified := False;
    B.Canvas.Pixels[0, 0] := clLime;
    B.Canvas.Pixels[3, 1] := TColor($332211);
    CheckEqB(True, B.Modified, 'Canvas drawing marks Modified');
    CheckEqI(3, R.Count, 'each Canvas change fires OnChange');
    CheckEqS('00 FF 00', Copy(PixelBytes(B, 0, 0, 4), 1, 8), 'row 0 is the top row, B G R order');
    CheckEqS('33 22 11', Copy(PixelBytes(B, 3, 1, 4), 1, 8), 'pixel (3,1) B G R');
    CheckEqI(clLime, B.Canvas.Pixels[0, 0], 'Canvas reads the DIB');

    Section('resize keeps the overlap');
    B.SetSize(6, 3);
    CheckEqI(6, B.Width, 'Width');
    CheckEqI(3, B.Height, 'Height');
    CheckEqS('00 FF 00', Copy(PixelBytes(B, 0, 0, 4), 1, 8), 'kept pixel (0,0)');
    CheckEqS('33 22 11', Copy(PixelBytes(B, 3, 1, 4), 1, 8), 'kept pixel (3,1)');
    CheckEqS('FF FF FF 00', PixelBytes(B, 5, 0, 4), 'new column white');
    CheckEqS('FF FF FF 00', PixelBytes(B, 0, 2, 4), 'new row white');
    B.Canvas.Pixels[5, 2] := clBlue;
    CheckEqI(clBlue, B.Canvas.Pixels[5, 2], 'Canvas works after resize');
    B.SetSize(2, 1);
    CheckEqS('00 FF 00', Copy(PixelBytes(B, 0, 0, 4), 1, 8), 'shrink keeps the top-left');
    B.Width := 4;
    CheckEqI(4, B.Width, 'Width setter');
    CheckEqI(1, B.Height, 'Width setter keeps Height');
    B.Height := 3;
    CheckEqI(3, B.Height, 'Height setter');
    CheckEqS('00 FF 00', Copy(PixelBytes(B, 0, 0, 4), 1, 8), 'Width/Height setters keep pixels');

    Section('empty and invalid sizes');
    B.SetSize(0, 5);
    CheckEqB(True, B.Empty, '0 x n is empty');
    CheckEqI(0, B.Handle, 'empty has no handle');
    CheckEqI(0, B.Width, 'empty Width');
    B.SetSize(5, 0);
    CheckEqB(True, B.Empty, 'n x 0 is empty');
    Expect('EInvalidGraphicOperation', 'Invalid bitmap size');
    try
      B.SetSize(-1, 2);
      Check(False, 'negative size raises');
    except
      on E: Exception do
        CheckRaises('negative size raises', E.ClassName, E.Message);
    end;
    Expect('EInvalidGraphicOperation', 'Invalid bitmap size');
    try
      B.Height := -3;
      Check(False, 'negative Height raises');
    except
      on E: Exception do
        CheckRaises('negative Height raises', E.ClassName, E.Message);
    end;
  finally
    B.Free;
    R.Free;
  end;
end;

procedure CheckPixelFormat;
var
  B: TBitmap;
  BM: TBitmapRec;
begin
  Section('PixelFormat');
  B := TBitmap.Create;
  try
    B.SetSize(5, 2);
    B.Canvas.Pixels[1, 1] := TColor($665544);
    B.PixelFormat := pf24bit;
    Check(B.PixelFormat = pf24bit, 'pf24bit accepted');
    GetObjectW(B.Handle, SizeOf(BM), @BM);
    CheckEqI(24, BM.bmBitsPixel, '24-bit DIB');
    CheckEqI(16, Stride(B), '24-bit stride for width 5');
    CheckEqS('66 55 44', PixelBytes(B, 1, 1, 3), 'pixel converted to 24-bit');
    CheckEqS('FF FF FF', PixelBytes(B, 4, 1, 3), 'white converted to 24-bit');
    B.SetSize(7, 2);
    CheckEqI(24, Stride(B), '24-bit stride for width 7');
    CheckEqS('66 55 44', PixelBytes(B, 1, 1, 3), '24-bit resize keeps pixels');
    B.PixelFormat := pf32bit;
    CheckEqS('66 55 44 00', PixelBytes(B, 1, 1, 4), 'back to 32-bit, alpha 0');
    Expect('EInvalidGraphicOperation', 'Unsupported pixel format');
    try
      B.PixelFormat := pf8bit;
      Check(False, 'pf8bit raises');
    except
      on E: Exception do
        CheckRaises('pf8bit raises', E.ClassName, E.Message);
    end;
    Check(B.PixelFormat = pf32bit, 'format unchanged after the error');
  finally
    B.Free;
  end;
end;

procedure CheckScanLineAndRelease;
var
  B: TBitmap;
  H: HBITMAP;
  BM: TBitmapRec;
begin
  Section('ScanLine and ReleaseHandle (UIF-543)');
  B := TBitmap.Create;
  try
    B.SetSize(3, 3);
    PBytes4(B.ScanLine[2])^[0] := $10;
    PBytes4(B.ScanLine[2])^[1] := $20;
    PBytes4(B.ScanLine[2])^[2] := $30;
    CheckEqI($102030, B.Canvas.Pixels[0, 2], 'ScanLine writes reach the canvas (B G R)');
    Expect('EInvalidGraphicOperation', 'Value out of range (3)');
    try
      Check(B.ScanLine[3] = nil, 'no pointer');
      Check(False, 'ScanLine out of range raises');
    except
      on E: Exception do
        CheckRaises('ScanLine out of range raises', E.ClassName, E.Message);
    end;
    H := B.ReleaseHandle;
    Check(H <> 0, 'ReleaseHandle returns the DIB');
    CheckEqB(True, B.Empty, 'empty after ReleaseHandle');
    CheckEqI(0, B.Handle, 'no handle after ReleaseHandle');
    CheckEqI(SizeOf(BM), GetObjectW(H, SizeOf(BM), @BM), 'released DIB still valid');
    CheckEqI(3, BM.bmWidth, 'released DIB width');
  finally
    B.Free;
  end;
  CheckEqI(SizeOf(BM), GetObjectW(H, SizeOf(BM), @BM), 'released DIB survives the bitmap');
  DeleteObject(H);
end;

procedure CheckAssign;
var
  A, B: TBitmap;
begin
  Section('Assign (UIF-547)');
  A := TBitmap.Create;
  B := TBitmap.Create;
  try
    A.SetSize(4, 2);
    A.PixelFormat := pf24bit;
    A.Canvas.Pixels[2, 1] := clRed;
    B.Assign(A);
    CheckEqI(4, B.Width, 'Width copied');
    CheckEqI(2, B.Height, 'Height copied');
    Check(B.PixelFormat = pf24bit, 'PixelFormat copied');
    Check(B.Handle <> A.Handle, 'new DIB');
    CheckEqI(clRed, B.Canvas.Pixels[2, 1], 'pixels copied');
    A.Canvas.Pixels[2, 1] := clBlue;
    CheckEqI(clRed, B.Canvas.Pixels[2, 1], 'deep copy');
    B.Assign(nil);
    CheckEqB(True, B.Empty, 'Assign(nil) clears');
  finally
    A.Free;
    B.Free;
  end;
end;

begin
  CheckDefaults;
  CheckStorage;
  CheckPixelFormat;
  CheckScanLineAndRelease;
  CheckAssign;
  Done;
end.
