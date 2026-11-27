// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1492, UIF-1494, UIF-1495, UIF-1497, UIF-1498, UIF-1499, UIF-1500, UIF-1501, UIF-1503, UIF-1520
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
  'Mne&monic' measure as 'Mnemonic', 'a&&bcdefghijk' as 'a&bcdefghijk'). U-179 (finding UIF-1521): with automatic
  lengths and RaggedRight = False the system balances the rows before it stretches them; the rows of the formula
  (RowOfTab) are the rows of TCM_GETITEMRECT, RowCount = TCM_GETROWCOUNT and DisplayRect = TCM_ADJUSTRECT for the
  measured case (8 captions, W 280: 4 + 4, not the greedy 5 + 3), over ranges of extents for the three styles at the
  top, tsTabs at the bottom, on the left and on the right, with images, and with ScrollOpposite for every selected tab
  (two and three rows; top, bottom, left, right); a row that the balancing empties (button styles) keeps its place; a
  fixed TabWidth and RaggedRight = True keep the greedy rows, OwnerDraw changes nothing. Constants at the form's DPI;
  the output names the checks only, so it is the same at every DPI. }
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

{ U-179: the rows as lists of tab indexes ('0 1 2 3 | 4 5 6 7'); Key(I) tells the rows apart }
function RowsText(PC: TPageControl; Native: Boolean): string;
var
  I, Key, Last, Rows: Integer;
  R: TRect;
begin
  Result := '';
  Last := 0;
  for I := 0 to PC.PageCount - 1 do
  begin
    if Native then
    begin
      R := PC.TabRect(I);
      if PC.TabPosition in [tpLeft, tpRight] then
        Key := R.Left
      else
        Key := R.Top;
    end
    else
      Key := TTabAccess(PC).RowOfTab(I, Rows);
    if I > 0 then
    begin
      if Key <> Last then
        Result := Result + ' |';
      Result := Result + ' ';
    end;
    Result := Result + IntToStr(I);
    Last := Key;
  end;
end;

{ U-179: what differs from the native control: the rows (TCM_GETITEMRECT), RowCount (TCM_GETROWCOUNT) and - for
  tsTabs and for top tabs - DisplayRect (TCM_ADJUSTRECT); '' when nothing does }
function Differences(PC: TPageControl): string;
begin
  Result := '';
  if RowsText(PC, True) <> RowsText(PC, False) then
    Result := Result + Format(' rows %s, native %s;', [RowsText(PC, False), RowsText(PC, True)]);
  if PC.RowCount <> SendMessageW(PC.Handle, TCM_GETROWCOUNT, 0, 0) then
    Result := Result + Format(' RowCount %d, native %d;', [PC.RowCount,
      SendMessageW(PC.Handle, TCM_GETROWCOUNT, 0, 0)]);
  if ((PC.Style = tsTabs) or (PC.TabPosition = tpTop)) and (RectText(Native(PC)) <> RectText(PC.DisplayRect)) then
    Result := Result + Format(' DisplayRect %s, native %s;', [RectText(PC.DisplayRect), RectText(Native(PC))]);
end;

{ U-179: multi-line books with automatic lengths at every extent L0, L0 + Step ... L1 (the width, or the height of
  vertical tabs; 600 across, so that every row fits); one check: nothing differs from the native control at any extent }
procedure Sweep(const Name: string; Pos: TTabPosition; St: TTabStyle; Ragged, Images: Boolean;
  const Captions: array of string; L0, L1, Step: Integer);
var
  L, I, Several: Integer;
  PC: TPageControl;
  D, All: string;
begin
  All := '';
  Several := 0;
  L := L0;
  while L <= L1 do
  begin
    if Pos in [tpLeft, tpRight] then
      PC := Book(600, L, Length(Captions), 0, Pos, St, True, False, Ragged, Captions)
    else
      PC := Book(L, 600, Length(Captions), 0, Pos, St, True, False, Ragged, Captions);
    try
      if Images then
      begin
        PC.Images := IL;
        for I := 0 to PC.PageCount - 1 do
          if Odd(I) then
            PC.Pages[I].ImageIndex := -1
          else
            PC.Pages[I].ImageIndex := 0;
      end;
      if PC.RowCount > 1 then
        Inc(Several);
      D := Differences(PC);
      if D <> '' then
        All := All + Format(' [%d]%s', [L, D]);
    finally
      PC.Free;
    end;
    L := L + Step;
  end;
  Check(Several > 0, Name + ': several rows in the range');
  CheckEqS('', All, Name + ': rows, RowCount and DisplayRect as the native control at every extent');
end;

{ U-179, UIF-1500: ScrollOpposite with every tab selected in turn: K is the row of the selected tab among the
  balanced rows, so DisplayRect = TCM_ADJUSTRECT }
procedure SweepSelection(const Name: string; W, H: Integer; Pos: TTabPosition; const Captions: array of string;
  const ExpectedRows: string);
var
  I, Rows: Integer;
  PC: TPageControl;
  All: string;
begin
  PC := Book(W, H, Length(Captions), 0, Pos, tsTabs, True, True, False, Captions);
  try
    CheckEqS(ExpectedRows, RowsText(PC, False), Name + ': rows');
    All := '';
    for I := 0 to PC.PageCount - 1 do
    begin
      PC.ActivePageIndex := I;
      if TTabAccess(PC).OppositeRows <> TTabAccess(PC).RowOfTab(I, Rows) then
        All := All + Format(' [tab %d] K %d;', [I, TTabAccess(PC).OppositeRows]);
      if Differences(PC) <> '' then
        All := All + Format(' [tab %d]%s', [I, Differences(PC)]);
    end;
    CheckEqS('', All, Name + ': with every tab selected K = its row, rows and DisplayRect as the native control');
  finally
    PC.Free;
  end;
end;

const
  { the captions of finding UIF-1521 }
  Eight: array[0..7] of string = ('&General', 'Options', 'Advanced', 'Colours', 'Fonts', 'Keys', 'Paths', 'Tools');
  Mixed: array[0..9] of string = ('i', 'Alpha', 'Settings and more', 'Mnemonic', 'A very long caption indeed', 'x',
    'Medium one', 'Zed', 'Environment variables', 'ok');
  { lengths of the minimum only: the same at every position }
  Short: array[0..8] of string = ('a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i');
  Thirteen: array[0..12] of string = ('a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm');
  { one long tab among short ones: the balancing of the button styles can empty its row }
  Lonely: array[0..4] of string = ('a', 'b', 'c', 'Wxxxxxxxxxx', 'e');

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

    Section('balanced rows: automatic lengths, RaggedRight = False (U-179, UIF-1521, UIF-1499)');
    PC := Book(280, 193, 8, 0, tpTop, tsTabs, True, False, False, Eight);
    try
      CheckEqS('0 1 2 3 | 4 5 6 7', RowsText(PC, False), '8 captions, W 280: rows 4 + 4');
      CheckEqS('', Differences(PC), '8 captions, W 280: rows, RowCount and DisplayRect as the native control');
      CheckStyle('8 captions, W 280', PC);
    finally
      PC.Free;
    end;
    PC := Book(280, 193, 8, 0, tpTop, tsTabs, True, False, True, Eight);
    try
      CheckEqS('0 1 2 3 4 | 5 6 7', RowsText(PC, False), 'RaggedRight = True, W 280: the greedy rows 5 + 3');
      CheckEqS('', Differences(PC), 'RaggedRight = True, W 280: rows, RowCount and DisplayRect as the native control');
    finally
      PC.Free;
    end;
    PC := Book(330, 193, 7, 60, tpTop, tsTabs, True, False, False, Eight);
    try
      CheckEqS('0 1 2 3 4 | 5 6', RowsText(PC, False), 'TabWidth 60, W 330: the greedy rows 5 + 2');
      CheckEqS('', Differences(PC), 'TabWidth 60, W 330: rows, RowCount and DisplayRect as the native control');
    finally
      PC.Free;
    end;
    PC := Book(280, 193, 8, 0, tpTop, tsTabs, True, False, False, Eight);
    try
      PC.OwnerDraw := True;
      CheckEqS('0 1 2 3 | 4 5 6 7', RowsText(PC, False), 'OwnerDraw, W 280: rows 4 + 4');
      CheckEqS('', Differences(PC), 'OwnerDraw, W 280: rows, RowCount and DisplayRect as the native control');
    finally
      PC.Free;
    end;
    PC := Book(202, 193, 8, 0, tpTop, tsTabs, True, False, False, Eight);
    try
      CheckEqS('0 1 | 2 3 4 | 5 6 7', RowsText(PC, False), '8 captions, W 202: rows 2 + 3 + 3 (greedy 3 + 4 + 1)');
      CheckEqS('', Differences(PC), '8 captions, W 202: rows, RowCount and DisplayRect as the native control');
    finally
      PC.Free;
    end;
    PC := Book(252, 193, 8, 0, tpTop, tsTabs, True, False, False, Eight);
    try
      CheckEqS('0 1 2 | 3 4 5 6 7', RowsText(PC, False), '8 captions, W 252: rows 3 + 5 (the last row takes a tab)');
      CheckEqS('', Differences(PC), '8 captions, W 252: rows, RowCount and DisplayRect as the native control');
    finally
      PC.Free;
    end;
    PC := Book(384, 193, 13, 0, tpTop, tsTabs, True, False, False, Thirteen);
    try
      CheckEqS('0 1 2 3 4 5 6 | 7 8 9 10 11 12', RowsText(PC, False),
        '13 equal tabs, W 384: rows 7 + 6 (8 x the difference = W)');
      CheckEqS('', Differences(PC), '13 equal tabs, W 384: rows, RowCount and DisplayRect as the native control');
    finally
      PC.Free;
    end;
    PC := Book(383, 193, 13, 0, tpTop, tsTabs, True, False, False, Thirteen);
    try
      CheckEqS('0 1 2 3 4 5 | 6 7 8 9 10 11 12', RowsText(PC, False), '13 equal tabs, W 383: rows 6 + 7');
      CheckEqS('', Differences(PC), '13 equal tabs, W 383: rows, RowCount and DisplayRect as the native control');
    finally
      PC.Free;
    end;

    Section('balanced rows over ranges of extents (U-179)');
    Sweep('tsTabs top, 8 captions, W 100 ... 430', tpTop, tsTabs, False, False, Eight, 100, 430, 3);
    Sweep('tsTabs top, 10 mixed captions, W 110 ... 800', tpTop, tsTabs, False, False, Mixed, 110, 800, 7);
    Sweep('tsTabs top, 13 equal tabs, W 100 ... 640', tpTop, tsTabs, False, False, Thirteen, 100, 640, 4);
    Sweep('tsTabs top with images, W 110 ... 900', tpTop, tsTabs, False, True, Mixed, 110, 900, 7);
    Sweep('tsTabs bottom, 10 mixed captions, W 110 ... 800', tpBottom, tsTabs, False, False, Mixed, 110, 800, 9);
    Sweep('tsButtons top, 10 mixed captions, W 110 ... 820', tpTop, tsButtons, False, False, Mixed, 110, 820, 7);
    Sweep('tsFlatButtons top, 10 mixed captions, W 110 ... 880', tpTop, tsFlatButtons, False, False, Mixed, 110, 880,
      7);
    Sweep('tsButtons bottom, 13 equal tabs, W 100 ... 680', tpBottom, tsButtons, False, False, Thirteen, 100, 680, 9);
    Sweep('tsTabs left, 9 short captions, H 100 ... 440', tpLeft, tsTabs, False, False, Short, 100, 440, 5);
    Sweep('tsTabs right, 9 short captions, H 100 ... 440', tpRight, tsTabs, False, False, Short, 100, 440, 5);
    Sweep('tsButtons left, 13 equal tabs, H 100 ... 680', tpLeft, tsButtons, False, False, Thirteen, 100, 680, 9);
    Sweep('RaggedRight = True, 10 mixed captions, W 110 ... 800', tpTop, tsTabs, True, False, Mixed, 110, 800, 9);

    Section('a row emptied by the balancing keeps its place (U-179, button styles)');
    Sweep('tsButtons top, one long tab, W 118 ... 130', tpTop, tsButtons, False, False, Lonely, 118, 130, 1);
    Sweep('tsFlatButtons top, one long tab, W 124 ... 140', tpTop, tsFlatButtons, False, False, Lonely, 124, 140, 1);
    PC := Book(125, 193, 5, 0, tpTop, tsButtons, True, False, False, Lonely);
    try
      CheckEqI(4, PC.RowCount, 'tsButtons, one long tab, W 125: 4 rows');
      CheckEqS('0 1 | 2 | 3 4', RowsText(PC, False),
        'tsButtons, one long tab, W 125: the long tab moved to the last row');
      CheckEqI(3, TTabAccess(PC).RowOfTab(3, I),
        'tsButtons, one long tab, W 125: row 2 is empty, the last row is row 3');
    finally
      PC.Free;
    end;

    Section('ScrollOpposite with balanced rows (U-179, UIF-1500, UIF-1501)');
    SweepSelection('top, W 280', 280, 193, tpTop, Eight, '0 1 2 3 | 4 5 6 7');
    SweepSelection('top, W 200, three rows', 200, 193, tpTop, Eight, '0 1 2 | 3 4 | 5 6 7');
    SweepSelection('top, W 252', 252, 193, tpTop, Eight, '0 1 2 | 3 4 5 6 7');
    SweepSelection('bottom, W 280', 280, 193, tpBottom, Eight, '0 1 2 3 | 4 5 6 7');
    SweepSelection('left, H 345', 289, 345, tpLeft, Short, '0 1 2 3 4 | 5 6 7 8');
    SweepSelection('right, H 250', 289, 250, tpRight, Short, '0 1 2 3 | 4 5 6 7 8');
    PC := Book(280, 193, 8, 0, tpTop, tsTabs, True, True, False, Eight);
    try
      PC.ActivePageIndex := 4;
      CheckEqI(1, TTabAccess(PC).OppositeRows, 'W 280, tab 4 selected: K = 1');
      CheckEqS(RectText(Native(PC)), RectText(PC.DisplayRect), 'W 280, tab 4 selected: DisplayRect = TCM_ADJUSTRECT');
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
