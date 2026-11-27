// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1492, UIF-1494, UIF-1495, UIF-1497, UIF-1498, UIF-1499, UIF-1500, UIF-1501, UIF-1503
// phase: 8
// needs: desktop
// opc: --app-type gui
program Ctl_TabGeometry;
{ U-172, phase8/tab-control.md section 4: the tab strip geometry the framework computes by formula (RowCount, the
  ScrollOpposite split, DisplayRect, the tab lengths) against the native control (comctl32 version 6, a GUI program)
  for every corpus configuration built in code with a handle (E27, E41 ... E46, E48 switched on one window, E49),
  tsFlatButtons top and bottom, left and right with 12 tabs, the empty control, ScrollOpposite with each of the three
  rows selected, button styles at bottom/left/right, and automatic lengths (with and without a 16 x 16 image list,
  image index 0 on two tabs; a 20-pixel font): RowCount = TCM_GETROWCOUNT; DisplayRect = TCM_ADJUSTRECT (False,
  (0, 0, W, H)) for tsTabs at every position and for the button styles at the top; for the button styles at
  bottom/left/right DisplayRect is the top result of the same control mirrored to that side, overlaps no
  TCM_GETITEMRECT and keeps the distance of the top (4 scaled) to the nearest tab; every formula tab length equals
  the TCM_GETITEMRECT length (rows of automatic lengths are not stretched with RaggedRight). The style bits of
  UIF-1492 are read back with GetWindowLongPtrW; after WM_THEMECHANGED (a new tab control has no theme handle before
  it, RTL-1147) GetWindowTheme is 0 for bottom/left/right (UIF-1494) and non-zero exactly when IsAppThemed for top
  tabs. U-176: tsFlatButtons keeps 10 pixels between tabs at every position (bottom 4 x 60 in W 260 gives 2 rows,
  left/right 12 tabs in H 193 give 6 columns) and automatic lengths process mnemonic prefixes ('&Mnemonic',
  'Mne&monic' measure as 'Mnemonic', 'a&&bcdefghijk' as 'a&bcdefghijk'). Constants at the form's DPI; the output
  names the checks only, so it is the same at every DPI. }
uses
  Windows, Messages, Types, SysUtils, Classes, CommCtrl, Graphics, Controls, Forms, ImgList, ComCtrls, UxTheme,
  TestKit;

type
  TTabAccess = class(TCustomTabControl);

var
  F: TForm;
  IL: TImageList;

function RectText(const R: TRect): string;
begin
  Result := Format('%d,%d,%d,%d', [R.Left, R.Top, R.Right, R.Bottom]);
end;

function Book(W, H, N, TW: Integer; Pos: TTabPosition; St: TTabStyle; ML, SO, Ragged: Boolean;
  const Captions: array of string): TPageControl;
var
  I: Integer;
  S: TTabSheet;
begin
  Result := TPageControl.Create(F);
  Result.Font.Height := -12;
  Result.SetBounds(0, 0, W, H);
  Result.TabWidth := TW;
  Result.TabPosition := Pos;
  Result.Style := St;
  Result.MultiLine := ML;
  Result.ScrollOpposite := SO;
  Result.RaggedRight := Ragged;
  for I := 0 to N - 1 do
  begin
    S := TTabSheet.Create(F);
    if I <= High(Captions) then
      S.Caption := Captions[I];
    S.PageControl := Result;
  end;
  Result.Parent := F;
  Result.HandleNeeded;
end;

function Native(PC: TCustomTabControl): TRect;
begin
  Result := Rect(0, 0, PC.Width, PC.Height);
  SendMessageW(PC.Handle, TCM_ADJUSTRECT, 0, LPARAM(@Result));
end;

function Along(PC: TPageControl; const R: TRect): Integer;
begin
  if PC.TabPosition in [tpLeft, tpRight] then
    Result := R.Bottom - R.Top
  else
    Result := R.Right - R.Left;
end;

{ the distance from DisplayRect to the nearest tab rectangle, on the strip side }
function Gap(PC: TPageControl): Integer;
var
  I, E: Integer;
  D, T: TRect;
begin
  D := PC.DisplayRect;
  Result := MaxInt;
  for I := 0 to PC.PageCount - 1 do
  begin
    T := PC.TabRect(I);
    case PC.TabPosition of
      tpBottom: E := T.Top - D.Bottom;
      tpLeft: E := D.Left - T.Right;
      tpRight: E := T.Left - D.Right;
    else
      E := D.Top - T.Bottom;
    end;
    if E < Result then
      Result := E;
  end;
end;

function Overlaps(PC: TPageControl): Integer;
var
  I: Integer;
  X, T: TRect;
begin
  Result := 0;
  for I := 0 to PC.PageCount - 1 do
  begin
    T := PC.TabRect(I);
    if IntersectRect(X, T, PC.DisplayRect) then
      Inc(Result);
  end;
end;

{ UIF-1492, computed here from the properties }
procedure CheckStyle(const Name: string; PC: TPageControl);
const
  Mask = TCS_BUTTONS or TCS_FLATBUTTONS or TCS_MULTILINE or TCS_BOTTOM or TCS_VERTICAL or TCS_SCROLLOPPOSITE
    or TCS_RAGGEDRIGHT or TCS_FIXEDWIDTH;
var
  Expected: NativeInt;
  ML: Boolean;
begin
  Expected := 0;
  ML := PC.MultiLine or (PC.TabPosition in [tpLeft, tpRight]);
  case PC.Style of
    tsButtons: Expected := TCS_BUTTONS;
    tsFlatButtons: Expected := TCS_BUTTONS or TCS_FLATBUTTONS;
  end;
  if ML then
    Expected := Expected or TCS_MULTILINE;
  case PC.TabPosition of
    tpBottom: Expected := Expected or TCS_BOTTOM;
    tpLeft: Expected := Expected or TCS_VERTICAL;
    tpRight: Expected := Expected or TCS_VERTICAL or TCS_RIGHT;
  end;
  if PC.ScrollOpposite and (PC.Style = tsTabs) and ML then
    Expected := Expected or TCS_SCROLLOPPOSITE;
  if PC.RaggedRight then
    Expected := Expected or TCS_RAGGEDRIGHT;
  if PC.TabWidth > 0 then
    Expected := Expected or TCS_FIXEDWIDTH;
  CheckEqS(IntToHex(Expected, 4), IntToHex(GetWindowLongPtrW(PC.Handle, GWL_STYLE) and Mask, 4), Name + ': style bits');
end;

{ UIF-1494, RTL-1147 }
procedure CheckTheme(const Name: string; PC: TPageControl);
begin
  SendMessageW(PC.Handle, WM_THEMECHANGED, 0, 0);
  if PC.TabPosition <> tpTop then
    CheckEqB(False, GetWindowTheme(PC.Handle) <> 0, Name + ': visual styles off')
  else if PC.Style = tsTabs then
    CheckEqB(IsAppThemed, GetWindowTheme(PC.Handle) <> 0, Name + ': visual styles as the program');
end;

procedure CheckLengths(const Name: string; PC: TPageControl);
var
  I: Integer;
  Formula, Measured: string;
begin
  Formula := '';
  Measured := '';
  for I := 0 to PC.PageCount - 1 do
  begin
    Formula := Formula + IntToStr(TTabAccess(PC).TabLength(I)) + ' ';
    Measured := Measured + IntToStr(Along(PC, PC.TabRect(I))) + ' ';
  end;
  CheckEqS(Measured, Formula, Name + ': tab lengths = TCM_GETITEMRECT');
end;

{ the top result Ref of the same tabs mirrored to the side of PC }
function Mirrored(PC, Ref: TPageControl): TRect;
var
  R: TRect;
begin
  R := Native(Ref);
  case PC.TabPosition of
    tpBottom: Result := Rect(R.Left, PC.Height - R.Bottom, R.Right, PC.Height - R.Top);
    tpLeft: Result := Rect(R.Top, R.Left, R.Bottom, R.Right);
    tpRight: Result := Rect(PC.Width - R.Bottom, R.Left, PC.Width - R.Top, R.Right);
  else
    Result := R;
  end;
end;

procedure CheckAll(const Name: string; PC: TPageControl);
var
  Ref: TPageControl;
  W, H: Integer;
begin
  CheckStyle(Name, PC);
  CheckTheme(Name, PC);
  CheckEqI(SendMessageW(PC.Handle, TCM_GETROWCOUNT, 0, 0), PC.RowCount, Name + ': RowCount = TCM_GETROWCOUNT');
  if (PC.Style = tsTabs) or (PC.TabPosition = tpTop) then
    CheckEqS(RectText(Native(PC)), RectText(PC.DisplayRect), Name + ': DisplayRect = TCM_ADJUSTRECT')
  else
  begin
    if PC.TabPosition = tpBottom then
    begin
      W := PC.Width;
      H := PC.Height;
    end
    else
    begin
      W := PC.Height;
      H := PC.Width;
    end;
    Ref := Book(W, H, PC.PageCount, PC.TabWidth, tpTop, PC.Style, True, False, False, []);
    try
      CheckEqS(RectText(Mirrored(PC, Ref)), RectText(PC.DisplayRect), Name + ': DisplayRect = the top result mirrored');
      CheckEqI(0, Overlaps(PC), Name + ': no tab rectangle overlaps DisplayRect');
      CheckEqI(Gap(Ref), Gap(PC), Name + ': distance to the nearest tab as at the top');
      CheckEqI(LayoutMulDiv(4, F.PixelsPerInch, 96), Gap(PC), Name + ': that distance is 4 scaled');
    finally
      Ref.Free;
    end;
  end;
  CheckLengths(Name, PC);
end;

procedure Probe(const Name: string; PC: TPageControl);
begin
  try
    CheckAll(Name, PC);
  finally
    PC.Free;
  end;
end;

procedure Run;
var
  PC: TPageControl;
  B: TBitmap;
  I: Integer;
begin
  Application.Initialize;
  F := TForm.CreateNew(nil);
  try
    F.ClientWidth := 900;
    F.ClientHeight := 700;
    F.HandleNeeded;                                          { IsAppThemed is read after the first window (RTL-1147) }
    IL := TImageList.Create(F);
    B := TBitmap.Create;
    try
      B.SetSize(16, 16);
      IL.Add(B, nil);
    finally
      B.Free;
    end;

    Section('corpus configurations (UIF-1520)');
    Probe('E27 top', Book(289, 193, 1, 0, tpTop, tsTabs, False, False, False, []));
    Probe('E41 bottom', Book(289, 193, 1, 0, tpBottom, tsTabs, False, False, False, []));
    Probe('E42 left', Book(289, 193, 3, 60, tpLeft, tsTabs, False, False, False, []));
    Probe('E43 right', Book(289, 193, 3, 60, tpRight, tsTabs, False, False, False, []));
    Probe('E44 two rows', Book(289, 193, 6, 60, tpTop, tsTabs, True, False, False, []));
    Probe('E45 three rows', Book(289, 193, 6, 100, tpTop, tsTabs, True, False, False, []));
    Probe('E46 buttons', Book(289, 193, 6, 100, tpTop, tsButtons, True, False, False, []));
    PC := Book(289, 193, 1, 0, tpTop, tsTabs, False, False, False, []);
    try
      CheckAll('E48 top', PC);
      PC.TabPosition := tpRight;
      CheckAll('E48 right', PC);
      PC.TabPosition := tpBottom;
      CheckAll('E48 bottom', PC);
    finally
      PC.Free;
    end;
    PC := Book(289, 193, 6, 100, tpTop, tsTabs, True, True, False, []);
    try
      PC.ActivePageIndex := 2;
      CheckEqI(1, TTabAccess(PC).OppositeRows, 'E49: K = 1');
      CheckAll('E49 scroll opposite', PC);
    finally
      PC.Free;
    end;

    Section('ScrollOpposite: each row selected (UIF-1500)');
    PC := Book(289, 193, 6, 100, tpTop, tsTabs, True, True, False, []);
    try
      for I := 0 to 2 do
      begin
        PC.ActivePageIndex := 2 * I;
        CheckEqI(I, TTabAccess(PC).OppositeRows, Format('row %d selected: K = %d', [I, I]));
        CheckAll(Format('row %d selected', [I]), PC);
      end;
    finally
      PC.Free;
    end;
    PC := Book(289, 193, 6, 100, tpTop, tsButtons, True, True, False, []);
    try
      PC.ActivePageIndex := 4;
      CheckEqI(0, TTabAccess(PC).OppositeRows, 'buttons: ScrollOpposite not in effect, K = 0');
      CheckAll('buttons with ScrollOpposite', PC);
    finally
      PC.Free;
    end;

    Section('positions, styles, rows');
    Probe('flat buttons top', Book(289, 193, 6, 60, tpTop, tsFlatButtons, True, False, False, []));
    Probe('flat buttons bottom', Book(289, 193, 6, 60, tpBottom, tsFlatButtons, True, False, False, []));
    Probe('left 12 tabs', Book(289, 193, 12, 60, tpLeft, tsTabs, False, False, False, []));
    Probe('right 12 tabs', Book(289, 193, 12, 60, tpRight, tsTabs, False, False, False, []));
    Probe('bottom 3 rows', Book(289, 193, 6, 100, tpBottom, tsTabs, True, False, False, []));
    Probe('buttons bottom', Book(289, 193, 6, 100, tpBottom, tsButtons, True, False, False, []));
    Probe('buttons left', Book(289, 193, 12, 60, tpLeft, tsButtons, False, False, False, []));
    Probe('buttons right', Book(289, 193, 12, 60, tpRight, tsButtons, False, False, False, []));
    Probe('empty', Book(289, 193, 0, 0, tpTop, tsTabs, False, False, False, []));

    Section('tsFlatButtons along spacing 10 at every position (U-176, UIF-1497)');
    PC := Book(260, 193, 4, 60, tpBottom, tsFlatButtons, True, False, False, []);
    try
      CheckEqI(2, PC.RowCount, 'flat buttons bottom 4 x 60, W 260: 2 rows');
      CheckAll('flat buttons bottom W 260', PC);
    finally
      PC.Free;
    end;
    PC := Book(289, 193, 12, 60, tpLeft, tsFlatButtons, False, False, False, []);
    try
      CheckEqI(6, PC.RowCount, 'flat buttons left 12 tabs, H 193: 6 columns');
      CheckAll('flat buttons left 12 tabs', PC);
    finally
      PC.Free;
    end;
    PC := Book(289, 193, 12, 60, tpRight, tsFlatButtons, False, False, False, []);
    try
      CheckEqI(6, PC.RowCount, 'flat buttons right 12 tabs, H 193: 6 columns');
      CheckAll('flat buttons right 12 tabs', PC);
    finally
      PC.Free;
    end;

    Section('automatic lengths (UIF-1495, UIF-1498)');
    Probe('single line', Book(400, 193, 4, 0, tpTop, tsTabs, False, False, False, ['i', 'Alpha', 'Settings and more', 'Mnemonic']));
    Probe('two rows', Book(230, 193, 4, 0, tpTop, tsTabs, True, False, True, ['i', 'Alpha', 'Settings and more', 'Mnemonic']));
    PC := Book(230, 193, 4, 0, tpTop, tsTabs, True, False, True, ['i', 'Alpha', 'Settings and more', 'Mnemonic']);
    try
      PC.Images := IL;
      PC.Pages[0].ImageIndex := 0;
      PC.Pages[2].ImageIndex := 0;
      CheckEqI(2, PC.RowCount, 'with images: 2 rows');
      CheckAll('two rows with images', PC);
    finally
      PC.Free;
    end;
    PC := Book(400, 193, 2, 0, tpTop, tsTabs, False, False, False, ['i', 'Settings and more']);
    try
      PC.Font.Height := -20;
      CheckAll('20-pixel font, minimum length sent', PC);
    finally
      PC.Free;
    end;
    Section('automatic lengths with mnemonic prefixes (U-176, UIF-1498)');
    PC := Book(400, 193, 4, 0, tpTop, tsTabs, False, False, False, ['Mnemonic', '&Mnemonic', 'Mne&monic', 'a&&bcdefghijk']);
    try
      CheckEqI(TTabAccess(PC).TabLength(0), TTabAccess(PC).TabLength(1), '&Mnemonic measures as Mnemonic');
      CheckEqI(TTabAccess(PC).TabLength(0), TTabAccess(PC).TabLength(2), 'Mne&monic measures as Mnemonic');
      CheckAll('mnemonic prefixes', PC);
    finally
      PC.Free;
    end;
  finally
    F.Free;
  end;
end;

begin
  Run;
  Done;
end.
