// covers: UIF-710, UIF-711
// needs: desktop
program Ctl_ScrollBar;
{ U-063, controls-standard.md section 11: TScrollBar defaults (121 x 17, TabStop); a sub-classed system SCROLLBAR
  with SBS_HORZ / SBS_VERT, Kind re-creating the window; range, page and position pushed with SetScrollInfo; the
  position clamp Min ... Max - max(PageSize - 1, 0); SetParams and its range error; OnChange on every position
  change and never for a range change that keeps the position; the user's scroll codes through OnScroll, which may
  alter the new position; a stored range applied after loading; leak check. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, TestKit, KeyStateKit;

type
  TLog = class
  public
    Text: string;
    Forced: Integer;
    procedure Changed(Sender: TObject);
    procedure Scrolled(Sender: TObject; ScrollCode: TScrollCode; var ScrollPos: Integer);
  end;

  TScrollForm = class(TForm)
  published
    Bar: TScrollBar;
    procedure BarChange(Sender: TObject);
  end;

{$FORM Ctl_ScrollBar.form}

var
  Log: TLog;

procedure TLog.Changed(Sender: TObject);
begin
  Text := Text + 'change:' + IntToStr(TScrollBar(Sender).Position) + ';';
end;

procedure TLog.Scrolled(Sender: TObject; ScrollCode: TScrollCode; var ScrollPos: Integer);
begin
  Text := Text + 'scroll:' + IntToStr(Ord(ScrollCode)) + ',' + IntToStr(ScrollPos) + ';';
  if Forced <> 0 then
    ScrollPos := Forced;
end;

procedure TScrollForm.BarChange(Sender: TObject);
begin
  Log.Text := Log.Text + 'form;';
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

{ min,max,page,pos of the native control }
function NativeInfo(C: TWinControl): string;
var
  Info: TScrollInfo;
begin
  FillChar(Info, SizeOf(Info), 0);
  Info.cbSize := SizeOf(Info);
  Info.fMask := SIF_RANGE or SIF_PAGE or SIF_POS;
  GetScrollInfo(C.Handle, SB_CTL, Info);
  Result := Format('%d,%d,%d,%d', [Info.nMin, Info.nMax, Integer(Info.nPage), Info.nPos]);
end;

function RangeError(S: TScrollBar; APos, AMin, AMax: Integer): string;
begin
  Result := 'none';
  try
    S.SetParams(APos, AMin, AMax);
  except
    on E: EInvalidOperation do
      Result := E.Message;
  end;
end;

procedure Key(C: TWinControl; VK: Word);
begin
  SendKeyMessage(C.Handle, WM_KEYDOWN, VK, 1);
end;

{ GDI + USER objects of the process }
function GuiObjects: Integer;
begin
  Result := Integer(GetGuiResources(GetCurrentProcess, GR_GDIOBJECTS))
    + Integer(GetGuiResources(GetCurrentProcess, GR_USEROBJECTS));
end;

procedure Run;
var
  F: TForm;
  S: TScrollBar;
  SF: TScrollForm;
  H: HWND;
  I, Before: Integer;
begin
  Application.Initialize;
  Log := TLog.Create;
  F := TForm.CreateNew(nil);
  try
    F.ClientWidth := 300;
    F.ClientHeight := 200;
    S := TScrollBar.Create(F);

    Section('defaults');
    CheckEqI(121, S.Width, 'Width 121');
    CheckEqI(17, S.Height, 'Height 17');
    CheckEqB(True, S.Kind = sbHorizontal, 'Kind sbHorizontal');
    CheckEqI(1, S.LargeChange, 'LargeChange 1');
    CheckEqI(100, S.Max, 'Max 100');
    CheckEqI(0, S.Min, 'Min 0');
    CheckEqI(0, S.PageSize, 'PageSize 0');
    CheckEqI(0, S.Position, 'Position 0');
    CheckEqI(1, S.SmallChange, 'SmallChange 1');
    CheckEqI(-1, S.TabOrder, 'TabOrder');
    CheckEqB(True, S.TabStop, 'TabStop True');
    Check(GetClass('TScrollBar') <> nil, 'registered');

    Section('position and range (UIF-711)');
    S.OnChange := Log.Changed;
    S.OnScroll := Log.Scrolled;
    Log.Text := '';
    S.Position := 150;
    CheckEqI(100, S.Position, 'clamped to Max');
    CheckEqS('change:100;', Log.Text, 'OnChange for code');
    Log.Text := '';
    S.Position := 100;
    CheckEqS('', Log.Text, 'equal position: no OnChange');
    S.PageSize := 10;
    CheckEqI(91, S.Position, 'PageSize 10: Max - 9');
    CheckEqS('change:91;', Log.Text, 'clamping by the page fires OnChange');
    Log.Text := '';
    S.Max := 200;
    CheckEqI(91, S.Position, 'range change keeps the position');
    CheckEqS('', Log.Text, 'range change without a position change: no OnChange');
    S.Min := 95;
    CheckEqI(95, S.Position, 'Min above the position');
    CheckEqS('change:95;', Log.Text, 'clamping by Min fires OnChange');
    S.SetParams(50, 0, 100);
    CheckEqS('0,100,50', Format('%d,%d,%d', [S.Min, S.Max, S.Position]), 'SetParams');
    CheckEqS('Scrollbar property out of range', RangeError(S, 1, 10, 5), 'SetParams: Max < Min');
    CheckEqS('0,100,50', Format('%d,%d,%d', [S.Min, S.Max, S.Position]), 'rejected: unchanged');
    CheckEqS('Scrollbar property out of range', RangeError(S, 1, 101, S.Max), 'Min above Max');
    S.LargeChange := 0;
    CheckEqI(1, S.LargeChange, 'LargeChange at least 1');
    S.LargeChange := 40000;
    CheckEqI(32767, S.LargeChange, 'LargeChange at most 32767');
    S.LargeChange := 20;
    S.SmallChange := 5;

    Section('window');
    S.Parent := F;
    H := S.Handle;
    CheckEqS('ScrollBar', ClassOf(H), 'system class SCROLLBAR');
    CheckEqB(False, (GetWindowLongPtrW(H, GWL_STYLE) and SBS_VERT) <> 0, 'SBS_HORZ');
    CheckEqS('0,100,10,50', NativeInfo(S), 'range, page and position pushed');
    S.Position := 60;
    CheckEqS('0,100,10,60', NativeInfo(S), 'position with a handle');
    S.Kind := sbVertical;
    Check(S.Handle <> H, 'Kind recreates');
    CheckEqB(True, (GetWindowLongPtrW(S.Handle, GWL_STYLE) and SBS_VERT) <> 0, 'SBS_VERT');
    CheckEqS('0,100,10,60', NativeInfo(S), 'state kept');
    CheckEqI(121, S.Width, 'at run time Kind does not swap the bounds');
    S.Kind := sbHorizontal;

    Section('user (UIF-710)');
    Log.Text := '';
    Key(S, VK_RIGHT);
    CheckEqS('scroll:1,65;change:65;', Log.Text, 'SB_LINEDOWN: + SmallChange');
    Log.Text := '';
    Key(S, VK_LEFT);
    CheckEqS('scroll:0,60;change:60;', Log.Text, 'SB_LINEUP: - SmallChange');
    Log.Text := '';
    Key(S, VK_NEXT);
    CheckEqS('scroll:3,80;change:80;', Log.Text, 'SB_PAGEDOWN: + LargeChange');
    Log.Text := '';
    Key(S, VK_PRIOR);
    CheckEqS('scroll:2,60;change:60;', Log.Text, 'SB_PAGEUP: - LargeChange');
    Log.Text := '';
    Key(S, VK_END);
    CheckEqS('scroll:7,91;change:91;', Log.Text, 'SB_BOTTOM: Max, clamped by the page');
    CheckEqS('0,100,10,91', NativeInfo(S), 'the native position follows');
    Log.Text := '';
    Key(S, VK_RIGHT);
    CheckEqS('scroll:1,91;', Log.Text, 'at the end: clamped, no OnChange');
    Log.Text := '';
    Key(S, VK_HOME);
    CheckEqS('scroll:6,0;change:0;', Log.Text, 'SB_TOP: Min');
    Log.Text := '';
    S.Perform(CN_HSCROLL, SB_ENDSCROLL, LPARAM(S.Handle));
    CheckEqS('scroll:8,0;', Log.Text, 'SB_ENDSCROLL: the position stays');
    Log.Forced := 1000;
    Log.Text := '';
    Key(S, VK_RIGHT);
    CheckEqS('scroll:1,5;change:91;', Log.Text, 'OnScroll may alter the position, clamped again');
    Log.Forced := 0;
    S.Kind := sbVertical;
    Log.Text := '';
    Key(S, VK_UP);
    CheckEqS('scroll:0,86;change:86;', Log.Text, 'vertical: CN_VSCROLL');

    Section('streaming');
    Log.Text := '';
    SF := TScrollForm.Create(nil);
    try
      CheckEqS('', Log.Text, 'no OnChange while loading');
      CheckEqS('-50,-10,5,-20', Format('%d,%d,%d,%d', [SF.Bar.Min, SF.Bar.Max, SF.Bar.PageSize, SF.Bar.Position]),
        'a range below zero stored Max before Min');
      CheckEqB(True, SF.Bar.Kind = sbVertical, 'Kind');
      CheckEqI(10, SF.Bar.LargeChange, 'LargeChange');
      CheckEqI(2, SF.Bar.SmallChange, 'SmallChange');
      CheckEqB(False, SF.Bar.TabStop, 'TabStop');
      CheckEqS('10,10,17,121', Format('%d,%d,%d,%d', [SF.Bar.Left, SF.Bar.Top, SF.Bar.Width, SF.Bar.Height]),
        'stored bounds');
      CheckEqS('-50,-10,5,-20', NativeInfo(SF.Bar), 'pushed to the window');
      SF.Bar.Position := -30;
      CheckEqS('form;', Log.Text, 'OnChange handler of the form');
    finally
      SF.Free;
    end;

    Section('leak');
    Before := GuiObjects;
    for I := 1 to 100 do
    begin
      S := TScrollBar.Create(nil);
      S.Parent := F;
      S.HandleNeeded;
      S.Free;
    end;
    Check(GuiObjects - Before <= 2, 'GUI objects after 100 instances with handles');
  finally
    F.Free;
    Log.Free;
  end;
end;

begin
  Run;
  Done;
end.
