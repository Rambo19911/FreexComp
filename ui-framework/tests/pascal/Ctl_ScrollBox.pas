// covers: UIF-215, UIF-218, UIF-234, UIF-290, UIF-291, UIF-292, UIF-293, UIF-750, UIF-751, UIF-752
// needs: desktop
program Ctl_ScrollBox;
{ U-070, controls-standard.md section 17, layout.md section 11: TScrollBox defaults (185 x 41, AutoScroll,
  bsSingle with WS_EX_CLIENTEDGE, TControlScrollBar Increment 8 / Range 0 / Tracking False / Visible True); the
  client formula (border 2 px per side, bar thickness MulDiv(17, DPI, 96)); worked example E32 in dump mode (96 DPI)
  and at run time; the fixed bar procedure with its cross checks; aligned children excluded from the range,
  akRight/akBottom children contributing Left/Top + minimum size; scrolling by Position, WM_HSCROLL/WM_VSCROLL
  (line, page, thumb with and without Tracking, top, bottom) and the mouse wheel, moving every child, aligned
  ones included; native bars ranged with SetScrollInfo; a shrinking range pulls the position back; unscrolled
  anchor baselines (UIF-234); AutoScroll False with Range and Visible False; ScrollInView, also on focus; scaling;
  streaming of the sub-properties. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, ExtCtrls, TestKit;

type
  TScrollForm = class(TForm)
  published
    Box: TScrollBox;
    N: TPanel;
    B: TPanel;
    Plain: TScrollBox;
  end;

{$FORM Ctl_ScrollBox.form}

function BoundsStr(C: TControl): string;
begin
  Result := Format('%d,%d,%d,%d', [C.Left, C.Top, C.Width, C.Height]);
end;

function ClientStr(C: TControl): string;
begin
  Result := Format('%dx%d', [C.ClientWidth, C.ClientHeight]);
end;

function NativeInfo(C: TWinControl; Bar: Integer): string;
var
  Info: TScrollInfo;
begin
  FillChar(Info, SizeOf(Info), 0);
  Info.cbSize := SizeOf(Info);
  Info.fMask := SIF_ALL;
  GetScrollInfo(C.Handle, Bar, Info);
  Result := Format('%d..%d page %d pos %d', [Info.nMin, Info.nMax, Info.nPage, Info.nPos]);
end;

function Styled(C: TWinControl; Bit: NativeInt): Boolean;
begin
  Result := (GetWindowLongPtrW(C.Handle, GWL_STYLE) and Bit) <> 0;
end;

procedure Scroll(C: TWinControl; Msg: Cardinal; Code, Pos: Integer);
begin
  SendMessageW(C.Handle, Msg, MakeWParam(Word(Code), Word(Pos)), 0);
end;

function NewPanel(AParent: TWinControl; L, T, W, H: Integer): TPanel;
begin
  Result := TPanel.Create(AParent);
  Result.SetBounds(L, T, W, H);
  Result.Parent := AParent;
end;

procedure Run;
var
  F: TForm;
  SF: TScrollForm;
  S: TScrollBox;
  P, A, Q: TPanel;
  E: TEdit;
  Bar: Integer;
begin
  Application.Initialize;

  Section('E32 in dump mode (96 DPI)');
  LayoutDumpMode := True;
  try
    SF := TScrollForm.Create(nil);
    try
      CheckEqS('196x79', ClientStr(SF.Box), 'client 196 x 79');
      CheckEqB(True, SF.Box.HorzScrollBar.IsScrollBarVisible, 'RangeH 300 > 196: HBar');
      CheckEqB(False, SF.Box.VertScrollBar.IsScrollBarVisible, 'RangeV 50 <= 96 - 17: no VBar');
      CheckEqS('0,59,196,20', BoundsStr(SF.B), 'B (0, 59, 196, 20)');
      CheckEqS('0,0,300,50', BoundsStr(SF.N), 'N unchanged');
      SF.Box.HorzScrollBar.Position := 50;
      CheckEqI(0, SF.Box.HorzScrollBar.Position, 'dump mode: positions stay 0');
    finally
      SF.Free;
    end;
  finally
    LayoutDumpMode := False;
  end;

  F := TForm.CreateNew(nil);
  try
    F.ClientWidth := 500;
    F.ClientHeight := 400;
    S := TScrollBox.Create(F);
    Bar := LayoutMulDiv(17, F.PixelsPerInch, 96);

    Section('defaults');
    CheckEqS('185,41', Format('%d,%d', [S.Width, S.Height]), 'size 185 x 41');
    CheckEqB(True, S.AutoScroll, 'AutoScroll True');
    CheckEqB(True, S.BorderStyle = bsSingle, 'BorderStyle bsSingle');
    CheckEqB(True, S.Color = clBtnFace, 'Color clBtnFace');
    CheckEqB(True, S.ParentColor, 'ParentColor True');
    CheckEqB(False, S.TabStop, 'TabStop False');
    CheckEqB(True, csAcceptsControls in S.ControlStyle, 'csAcceptsControls');
    CheckEqI(8, S.HorzScrollBar.Increment, 'Increment 8');
    CheckEqI(0, S.HorzScrollBar.Range, 'Range 0');
    CheckEqB(False, S.HorzScrollBar.Tracking, 'Tracking False');
    CheckEqB(True, S.VertScrollBar.Visible, 'Visible True');
    CheckEqB(True, S.HorzScrollBar.Kind = sbHorizontal, 'Kind sbHorizontal');
    CheckEqB(True, S.VertScrollBar.Kind = sbVertical, 'Kind sbVertical');
    CheckEqI(0, S.VertScrollBar.Position, 'Position 0');
    Check(GetClass('TScrollBox') <> nil, 'registered');

    Section('client formula (UIF-215)');
    S.SetBounds(0, 0, 200, 100);
    S.Parent := F;
    CheckEqS('196x96', ClientStr(S), 'bsSingle, no bars: 2 px per side');
    Check((GetWindowLongPtrW(S.Handle, GWL_EXSTYLE) and WS_EX_CLIENTEDGE) <> 0, 'WS_EX_CLIENTEDGE');
    S.BorderStyle := bsNone;
    CheckEqS('200x100', ClientStr(S), 'bsNone: the whole control');
    Check((GetWindowLongPtrW(S.Handle, GWL_EXSTYLE) and WS_EX_CLIENTEDGE) = 0, 'bsNone: no client edge');
    S.BorderStyle := bsSingle;

    Section('E32 at run time');
    P := NewPanel(S, 0, 0, 300, 50);
    Q := TPanel.Create(S);
    Q.Align := alBottom;
    Q.Height := 20;
    Q.Parent := S;
    CheckEqS(Format('196x%d', [96 - Bar]), ClientStr(S), 'client 196 x (96 - B)');
    CheckEqS(Format('0,%d,196,20', [96 - Bar - 20]), BoundsStr(Q), 'alBottom child above the bar');
    CheckEqB(True, S.HorzScrollBar.IsScrollBarVisible, 'HBar');
    CheckEqB(False, S.VertScrollBar.IsScrollBarVisible, 'no VBar');
    CheckEqB(True, Styled(S, WS_HSCROLL), 'native horizontal bar shown');
    CheckEqB(False, Styled(S, WS_VSCROLL), 'native vertical bar hidden');
    CheckEqS(Format('0..299 page 196 pos 0', []), NativeInfo(S, SB_HORZ), 'SetScrollInfo: range and page');

    Section('bar procedure (UIF-291)');
    P.Height := 96 - Bar + 1;
    CheckEqB(True, S.VertScrollBar.IsScrollBarVisible, 'RangeV > H0 - B with HBar: VBar too');
    CheckEqS(Format('%dx%d', [196 - Bar, 96 - Bar]), ClientStr(S), 'both bars: client');
    P.Height := 50;
    P.Width := 196;
    CheckEqB(False, S.HorzScrollBar.IsScrollBarVisible, 'RangeH = W0: no HBar');
    CheckEqS('196x96', ClientStr(S), 'no bars: full client');
    P.Width := 196 - Bar + 1;
    P.Height := 97;
    CheckEqB(True, S.VertScrollBar.IsScrollBarVisible, 'RangeV > H0: VBar');
    CheckEqB(True, S.HorzScrollBar.IsScrollBarVisible, 'RangeH > W0 - B with VBar: HBar too');
    P.Height := 50;
    P.Width := 300;
    Q.Align := alClient;
    CheckEqB(True, S.HorzScrollBar.IsScrollBarVisible, 'alClient child does not count');
    P.Visible := False;
    CheckEqB(False, S.HorzScrollBar.IsScrollBarVisible, 'invisible child does not count');
    P.Visible := True;
    Q.Align := alBottom;

    Section('anchored children (UIF-292)');
    A := NewPanel(S, 150, 0, 400, 20);
    A.Anchors := [akTop, akRight];
    A.Constraints.MinWidth := 30;
    P.Width := 100;
    CheckEqB(False, S.HorzScrollBar.IsScrollBarVisible, 'akRight child: Left + MinWidth = 180 <= 196');
    A.Constraints.MinWidth := 60;
    S.Realign;                                               { a constraint change alone is no layout trigger }
    CheckEqB(True, S.HorzScrollBar.IsScrollBarVisible, 'akRight child: Left + MinWidth = 210 > 196');
    A.Free;
    P.Width := 300;

    Section('scrolling (UIF-293, UIF-750)');
    S.HorzScrollBar.Position := 50;
    CheckEqI(50, S.HorzScrollBar.Position, 'Position 50');
    CheckEqI(-50, P.Left, 'alNone child moved');
    CheckEqI(-50, Q.Left, 'aligned child moved');
    CheckEqS(Format('0..299 page 196 pos 50', []), NativeInfo(S, SB_HORZ), 'native position');
    S.Realign;
    CheckEqI(-50, Q.Left, 'a layout pass keeps the aligned child scrolled');
    CheckEqI(-50, P.Left, 'a layout pass keeps the alNone child');
    S.HorzScrollBar.Position := 1000;
    CheckEqI(104, S.HorzScrollBar.Position, 'clamped to Range - client width');
    S.HorzScrollBar.Position := -5;
    CheckEqI(0, S.HorzScrollBar.Position, 'clamped to 0');
    CheckEqI(0, P.Left, 'back at 0');
    Scroll(S, WM_HSCROLL, SB_LINEDOWN, 0);
    CheckEqI(8, S.HorzScrollBar.Position, 'line: Increment');
    Scroll(S, WM_HSCROLL, SB_PAGEDOWN, 0);
    CheckEqI(104, S.HorzScrollBar.Position, 'page: 8 + (196 - 8), clamped to the maximum 104');
    Scroll(S, WM_HSCROLL, SB_LINEUP, 0);
    CheckEqI(96, S.HorzScrollBar.Position, 'line up');
    Scroll(S, WM_HSCROLL, SB_PAGEUP, 0);
    CheckEqI(0, S.HorzScrollBar.Position, 'page up clamped');
    Scroll(S, WM_HSCROLL, SB_THUMBTRACK, 40);
    CheckEqI(0, S.HorzScrollBar.Position, 'thumb track without Tracking: no change');
    Scroll(S, WM_HSCROLL, SB_THUMBPOSITION, 40);
    CheckEqI(40, S.HorzScrollBar.Position, 'thumb position');
    S.HorzScrollBar.Tracking := True;
    Scroll(S, WM_HSCROLL, SB_THUMBTRACK, 60);
    CheckEqI(60, S.HorzScrollBar.Position, 'thumb track with Tracking');
    Scroll(S, WM_HSCROLL, SB_BOTTOM, 0);
    CheckEqI(104, S.HorzScrollBar.Position, 'bottom: maximum');
    Scroll(S, WM_HSCROLL, SB_TOP, 0);
    CheckEqI(0, S.HorzScrollBar.Position, 'top');

    Section('range shrinks');
    S.HorzScrollBar.Position := 104;
    P.Width := 250;
    CheckEqI(54, S.HorzScrollBar.Position, 'smaller range: position pulled back');
    CheckEqI(-54, P.Left, 'child follows');
    P.Width := 300;
    S.HorzScrollBar.Position := 0;

    Section('unscrolled baselines (UIF-234)');
    A := NewPanel(S, 150, 0, 20, 20);
    A.Anchors := [akTop, akRight];
    S.HorzScrollBar.Position := 50;
    CheckEqI(100, A.Left, 'scrolled');
    S.Width := 224;
    CheckEqI(150 + 24 - 50, A.Left, 'anchors from the unscrolled baseline, offset by the position');
    S.Width := 200;
    CheckEqI(100, A.Left, 'and back');
    S.HorzScrollBar.Position := 0;
    CheckEqI(150, A.Left, 'unscrolled again');
    A.Free;

    Section('mouse wheel (UIF-752)');
    P.Height := 400;
    SendMessageW(S.Handle, WM_MOUSEWHEEL, MakeWParam(0, Word(-120)), 0);
    CheckEqI(LayoutMulDiv(48, F.PixelsPerInch, 96), S.VertScrollBar.Position, 'one notch down: 3 x 16 logical px');
    SendMessageW(S.Handle, WM_MOUSEWHEEL, MakeWParam(0, Word(240)), 0);
    CheckEqI(0, S.VertScrollBar.Position, 'two notches up: clamped at 0');
    Scroll(S, WM_VSCROLL, SB_LINEDOWN, 0);
    CheckEqI(8, S.VertScrollBar.Position, 'WM_VSCROLL line');
    CheckEqI(-8, P.Top, 'vertical scroll moves the child');
    S.VertScrollBar.Position := 0;

    Section('ScrollInView (UIF-751)');
    Q.Align := alNone;
    Q.SetBounds(0, 300, 50, 20);
    S.ScrollInView(Q);
    CheckEqI(320 - S.ClientHeight, S.VertScrollBar.Position, 'below: the bottom edge into view');
    CheckEqI(S.ClientHeight - 20, Q.Top, 'child at the bottom of the client area');
    S.ScrollInView(P);
    CheckEqI(0, S.VertScrollBar.Position, 'above: the top edge into view');
    E := TEdit.Create(F);
    E.SetBounds(220, 200, 60, 23);
    E.Parent := S;
    F.Show;
    E.SetFocus;
    Check(E.Top >= 0, 'focused child scrolled in: top');
    Check(E.Top + E.Height <= S.ClientHeight, 'focused child scrolled in: bottom');
    Check(E.Left + E.Width <= S.ClientWidth, 'focused child scrolled in: right');
    F.Hide;

    Section('AutoScroll False');
    S.HorzScrollBar.Position := 0;
    S.VertScrollBar.Position := 0;
    S.AutoScroll := False;
    CheckEqB(False, S.HorzScrollBar.IsScrollBarVisible, 'no Range: no bars');
    S.HorzScrollBar.Range := 400;
    CheckEqB(True, S.HorzScrollBar.IsScrollBarVisible, 'Range 400 > 196: HBar');
    S.HorzScrollBar.Position := 1000;
    CheckEqI(400 - 196, S.HorzScrollBar.Position, 'clamped to Range - client width');
    S.HorzScrollBar.Visible := False;
    CheckEqB(False, S.HorzScrollBar.IsScrollBarVisible, 'Visible False: never shown');
    CheckEqS('196x96', ClientStr(S), 'Visible False: no space taken');
    S.HorzScrollBar.Position := 10;
    CheckEqI(10, S.HorzScrollBar.Position, 'Visible False: still scrolls programmatically');

    Section('scaling');
    F.ScaleBy(2, 1);
    CheckEqI(16, S.HorzScrollBar.Increment, 'Increment scaled');
    CheckEqI(800, S.HorzScrollBar.Range, 'Range scaled');
  finally
    F.Free;
  end;

  Section('streaming');
  SF := TScrollForm.Create(nil);
  try
    CheckEqB(True, SF.Box.AutoScroll, 'Box: AutoScroll default');
    CheckEqB(True, SF.Box.HorzScrollBar.IsScrollBarVisible, 'Box: HBar after loading');
    CheckEqB(False, SF.Plain.AutoScroll, 'AutoScroll False');
    CheckEqB(True, SF.Plain.BorderStyle = bsNone, 'BorderStyle bsNone');
    CheckEqI(4, SF.Plain.HorzScrollBar.Increment, 'HorzScrollBar.Increment');
    CheckEqI(500, SF.Plain.HorzScrollBar.Range, 'HorzScrollBar.Range');
    CheckEqB(True, SF.Plain.HorzScrollBar.Tracking, 'HorzScrollBar.Tracking');
    CheckEqI(300, SF.Plain.VertScrollBar.Range, 'VertScrollBar.Range');
    CheckEqB(False, SF.Plain.VertScrollBar.Visible, 'VertScrollBar.Visible');
    CheckEqB(True, SF.Plain.TabStop, 'TabStop');
    CheckEqB(True, SF.Plain.HorzScrollBar.IsScrollBarVisible, 'Range 500 > 200: HBar');
    CheckEqB(False, SF.Plain.VertScrollBar.IsScrollBarVisible, 'Visible False: no VBar');
    CheckEqS(Format('200x%d', [100 - LayoutMulDiv(17, SF.PixelsPerInch, 96)]), ClientStr(SF.Plain),
      'client of the loaded box');
  finally
    SF.Free;
  end;
end;

begin
  Run;
  Done;
end.
