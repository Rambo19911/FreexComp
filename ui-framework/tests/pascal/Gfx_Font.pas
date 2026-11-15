// covers: UIF-505, UIF-506, UIF-507, UIF-510, UIF-511, UIF-512, UIF-513
// opc: -U ..\..\src
program Gfx_Font;
{ U-004, graphics.md section 2: TGraphicsObject handle rules on TFont - lazy handle, change order (detach,
  OnChange while the old handle still exists, then the old handle is deleted), no event for an unchanged value,
  Assign fires one OnChange, Handle := external font reads attributes back; TFont defaults and DefFontData;
  LOGFONT built from the fields; Height 0 rule; Size <-> Height at 96 and 144 PPI. }
uses
  Windows, SysUtils, Classes, Graphics, TestKit;

type
  TRecorder = class
    Font: TFont;
    Watched: HGDIOBJ;
    Log: string;
    procedure FontChanged(Sender: TObject);
  end;

function Alive(H: HGDIOBJ): Boolean;
var
  LF: TLogFontW;
begin
  Result := GetObjectW(H, SizeOf(LF), @LF) <> 0;
end;

function YesNo(B: Boolean): string;
begin
  if B then
    Result := 'y'
  else
    Result := 'n';
end;

procedure TRecorder.FontChanged(Sender: TObject);
begin
  Log := Log + 'change(allocated=' + YesNo(Font.HandleAllocated) + ' old=' + YesNo(Alive(Watched)) + ');';
end;

function FaceName(const LF: TLogFontW): string;
var
  I: Integer;
begin
  Result := '';
  I := 0;
  while (I < 32) and (LF.lfFaceName[I] <> #0) do
  begin
    Result := Result + LF.lfFaceName[I];
    Inc(I);
  end;
end;

procedure CheckDefaults;
var
  F: TFont;
begin
  Section('defaults');
  CheckEqI(-12, DefFontData.Height, 'DefFontData.Height');
  CheckEqS('Segoe UI', DefFontData.Name, 'DefFontData.Name');
  CheckEqI(DEFAULT_CHARSET, DefFontData.Charset, 'DefFontData.Charset');
  Check(DefFontData.Style = [], 'DefFontData.Style');
  Check(DefFontData.Pitch = fpDefault, 'DefFontData.Pitch');
  F := TFont.Create;
  try
    CheckEqI(1, F.Charset, 'Charset');
    CheckEqI(clWindowText, F.Color, 'Color');
    CheckEqI(-12, F.Height, 'Height');
    CheckEqS('Segoe UI', F.Name, 'Name');
    CheckEqI(0, F.Orientation, 'Orientation');
    Check(F.Pitch = fpDefault, 'Pitch');
    Check(F.Quality = fqDefault, 'Quality');
    Check(F.Style = [], 'Style');
    CheckEqI(9, F.Size, 'Size');
    CheckEqI(96, F.PixelsPerInch, 'PixelsPerInch');
    CheckEqB(False, F.HandleAllocated, 'no handle before first read');
  finally
    F.Free;
  end;
end;

procedure CheckHandleCreation;
var
  F: TFont;
  H: HFONT;
  LF: TLogFontW;
begin
  Section('handle creation (UIF-511)');
  F := TFont.Create;
  try
    F.Name := 'Consolas';
    F.Height := -20;
    F.Style := [fsBold, fsItalic, fsUnderline, fsStrikeOut];
    F.Charset := 0;
    F.Quality := fqClearType;
    F.Pitch := fpFixed;
    F.Orientation := 900;
    H := F.Handle;
    Check(H <> 0, 'handle created on read');
    CheckEqB(True, F.HandleAllocated, 'HandleAllocated after read');
    CheckEqB(True, F.Handle = H, 'handle cached');
    FillChar(LF, SizeOf(LF), 0);
    CheckEqI(SizeOf(LF), GetObjectW(H, SizeOf(LF), @LF), 'GetObjectW size');
    CheckEqI(-20, LF.lfHeight, 'lfHeight');
    CheckEqI(FW_BOLD, LF.lfWeight, 'lfWeight bold');
    CheckEqI(1, LF.lfItalic, 'lfItalic');
    CheckEqI(1, LF.lfUnderline, 'lfUnderline');
    CheckEqI(1, LF.lfStrikeOut, 'lfStrikeOut');
    CheckEqI(0, LF.lfCharSet, 'lfCharSet');
    CheckEqI(5, LF.lfQuality, 'lfQuality CLEARTYPE_QUALITY');
    CheckEqI(FIXED_PITCH, LF.lfPitchAndFamily and 3, 'lfPitchAndFamily');
    CheckEqI(900, LF.lfEscapement, 'lfEscapement');
    CheckEqI(900, LF.lfOrientation, 'lfOrientation');
    CheckEqS('Consolas', FaceName(LF), 'lfFaceName');
    F.Style := [];
    GetObjectW(F.Handle, SizeOf(LF), @LF);
    CheckEqI(FW_NORMAL, LF.lfWeight, 'lfWeight normal');
  finally
    F.Free;
  end;
end;

procedure CheckChangeOrder;
var
  F: TFont;
  R: TRecorder;
  Old: HFONT;
begin
  Section('change order (UIF-505)');
  F := TFont.Create;
  R := TRecorder.Create;
  try
    R.Font := F;
    F.OnChange := R.FontChanged;
    Old := F.Handle;
    R.Watched := Old;
    F.Height := -16;
    CheckEqS('change(allocated=n old=y);', R.Log, 'detached, event while the old handle exists');
    CheckEqB(False, Alive(Old), 'old handle deleted after the event');
    CheckEqB(False, F.HandleAllocated, 'new handle not created until read');
    Check(F.Handle <> 0, 'new handle on read');

    R.Log := '';
    R.Watched := F.Handle;
    F.Height := -16;
    F.Name := 'Segoe UI';
    F.Style := [];
    F.Charset := 1;
    F.Color := clWindowText;
    F.Pitch := fpDefault;
    F.Quality := fqDefault;
    F.Orientation := 0;
    CheckEqS('', R.Log, 'unchanged values fire nothing');
    CheckEqB(True, Alive(R.Watched), 'unchanged values keep the handle');

    F.Color := clRed;
    CheckEqS('change(allocated=y old=y);', R.Log, 'Color fires OnChange and keeps the handle');
    CheckEqB(True, Alive(R.Watched), 'handle kept after Color');

    R.Log := '';
    F.Style := [fsBold];
    F.Name := 'Arial';
    F.Charset := 0;
    F.Pitch := fpVariable;
    F.Quality := fqDraft;
    F.Orientation := 10;
    CheckEqS('change(allocated=n old=y);change(allocated=n old=n);change(allocated=n old=n);'
      + 'change(allocated=n old=n);change(allocated=n old=n);change(allocated=n old=n);', R.Log,
      'one event per changed attribute');
    CheckEqB(False, Alive(R.Watched), 'first handle deleted');
  finally
    F.Free;
    R.Free;
  end;
end;

procedure CheckAssignAndExternal;
var
  A, B: TFont;
  R: TRecorder;
  H, Ext: HFONT;
  LF: TLogFontW;
  I: Integer;
  Face: string;
begin
  Section('Assign (UIF-506)');
  A := TFont.Create;
  B := TFont.Create;
  R := TRecorder.Create;
  try
    R.Font := B;
    B.OnChange := R.FontChanged;
    B.Assign(A);
    CheckEqS('', R.Log, 'equal font: no event');
    A.Name := 'Arial';
    A.Height := -30;
    A.Style := [fsItalic];
    A.Color := clBlue;
    A.Charset := 0;
    A.Pitch := fpVariable;
    A.Quality := fqProof;
    A.Orientation := 450;
    H := B.Handle;
    R.Watched := H;
    B.Assign(A);
    CheckEqS('change(allocated=n old=y);', R.Log, 'one event for many attributes');
    CheckEqB(False, Alive(H), 'old handle deleted');
    CheckEqS('Arial', B.Name, 'Name copied');
    CheckEqI(-30, B.Height, 'Height copied');
    Check(B.Style = [fsItalic], 'Style copied');
    CheckEqI(clBlue, B.Color, 'Color copied');
    CheckEqI(0, B.Charset, 'Charset copied');
    Check(B.Pitch = fpVariable, 'Pitch copied');
    Check(B.Quality = fqProof, 'Quality copied');
    CheckEqI(450, B.Orientation, 'Orientation copied');
    Check(B.Handle <> A.Handle, 'handles never shared');

    Section('Handle := external (UIF-507)');
    FillChar(LF, SizeOf(LF), 0);
    LF.lfHeight := -24;
    LF.lfWeight := FW_BOLD;
    LF.lfUnderline := 1;
    LF.lfCharSet := 0;
    LF.lfQuality := 3;
    LF.lfPitchAndFamily := FIXED_PITCH;
    LF.lfEscapement := 0;
    Face := 'Courier New';
    for I := 1 to Length(Face) do
      LF.lfFaceName[I - 1] := Face[I];
    Ext := CreateFontIndirectW(LF);
    R.Log := '';
    H := B.Handle;
    R.Watched := H;
    B.Handle := Ext;
    CheckEqS('change(allocated=y old=y);', R.Log, 'one event, old handle alive during it');
    CheckEqB(False, Alive(H), 'previous handle deleted');
    CheckEqB(True, B.Handle = Ext, 'external handle kept');
    CheckEqI(-24, B.Height, 'Height read back');
    Check(B.Style = [fsBold, fsUnderline], 'Style read back');
    CheckEqS('Courier New', B.Name, 'Name read back');
    Check(B.Quality = fqNonAntialiased, 'Quality read back');
    Check(B.Pitch = fpFixed, 'Pitch read back');
    CheckEqI(0, B.Orientation, 'Orientation read back');
  finally
    A.Free;
    B.Free;
    R.Free;
  end;
  CheckEqB(False, Alive(Ext), 'owned external handle deleted with the font');
end;

procedure CheckSizeHeight;
var
  F: TFont;
begin
  Section('Size and Height (UIF-512, UIF-513)');
  F := TFont.Create;
  try
    F.Size := 12;
    CheckEqI(-16, F.Height, 'Size 12 at 96 PPI');
    CheckEqI(12, F.Size, 'Size read back at 96');
    F.PixelsPerInch := 144;
    CheckEqI(-16, F.Height, 'PixelsPerInch does not change Height');
    CheckEqI(8, F.Size, 'Height -16 at 144 PPI is 8 pt');
    F.Size := 9;
    CheckEqI(-18, F.Height, 'Size 9 at 144 PPI');
    CheckEqI(9, F.Size, 'Size read back at 144');
    F.Height := 0;
    CheckEqI(-18, F.Height, 'Height 0 = default scaled to 144 PPI');
    F.PixelsPerInch := 96;
    F.Height := 0;
    CheckEqI(-12, F.Height, 'Height 0 = default at 96 PPI');
    F.Height := 20;
    CheckEqI(20, F.Height, 'positive cell height kept');
    CheckEqI(-15, F.Size, 'Size of a positive height');
    F.Name := '0123456789012345678901234567890123456789';
    CheckEqI(31, Length(F.Name), 'Name at most 31 characters');
  finally
    F.Free;
  end;
end;

begin
  CheckDefaults;
  CheckHandleCreation;
  CheckChangeOrder;
  CheckAssignAndExternal;
  CheckSizeHeight;
  Done;
end.
