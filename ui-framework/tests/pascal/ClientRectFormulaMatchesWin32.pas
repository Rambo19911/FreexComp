// covers: UIF-215, UIF-216
// needs: desktop
program ClientRectFormulaMatchesWin32;
{ U-071, layout.md section 3.1, testing.md section 3.3: for every container class the client size computed by
  formula from fields (UIF-215) equals what Win32 GetClientRect reports for the window at the form's DPI, and the
  client origin (border thickness, UIF-216, device pixels at every DPI) equals the offset of the native client area
  inside the window rectangle. Covered: every container of the UIF-215 table: the form, TPanel (bsNone, bsSingle),
  TGroupBox, TScrollBox (bsSingle, bsNone; no bar, horizontal bar, vertical bar, both bars), TPageControl, TTabSheet,
  TToolBar and TStatusBar (U-129). The scroll-bar thickness B of the formula is also
  compared with the system metric of the form's DPI. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, ExtCtrls, ComCtrls, TestKit;

function FormulaStr(C: TControl): string;
begin
  Result := Format('%dx%d', [C.ClientWidth, C.ClientHeight]);
end;

function NativeStr(C: TWinControl): string;
var
  R: TRect;
begin
  GetClientRect(C.Handle, R);
  Result := Format('%dx%d', [R.Right - R.Left, R.Bottom - R.Top]);
end;

{ offset of the native client area's top-left inside the window rectangle }
function NativeOrigin(C: TWinControl): string;
var
  W: TRect;
  P: TPoint;
begin
  GetWindowRect(C.Handle, W);
  P := Point(0, 0);
  ClientToScreen(C.Handle, P);
  Result := Format('(%d, %d)', [P.X - W.Left, P.Y - W.Top]);
end;

procedure Compare(C: TWinControl; const Origin, Name: string);
begin
  CheckEqS(NativeStr(C), FormulaStr(C), Name + ': client size');
  CheckEqS(Origin, NativeOrigin(C), Name + ': client origin');
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
  P: TPanel;
  G: TGroupBox;
  S: TScrollBox;
  Content: TPanel;
  PC: TPageControl;
  Sheet: TTabSheet;
  Bar: TToolBar;
  Status: TStatusBar;
  DPI, B, D: Integer;
  Ok: Boolean;
begin
  Application.Initialize;
  F := TForm.CreateNew(nil);
  try
    F.SetBounds(40, 40, 600, 500);
    F.ClientWidth := 560;
    F.ClientHeight := 420;
    F.HandleNeeded;
    DPI := F.PixelsPerInch;
    B := LayoutMulDiv(17, DPI, 96);

    Section('scroll-bar thickness');
    CheckEqI(GetSystemMetricsForDpi(SM_CXVSCROLL, DPI), B, 'vertical bar = MulDiv(17, DPI, 96)');
    CheckEqI(GetSystemMetricsForDpi(SM_CYHSCROLL, DPI), B, 'horizontal bar = MulDiv(17, DPI, 96)');
    CheckEqI(GetDpiForWindow(F.Handle), DPI, 'formula DPI = window DPI');
    Ok := True;
    D := 96;
    while D <= 480 do
    begin
      if (GetSystemMetricsForDpi(SM_CXVSCROLL, D) <> LayoutMulDiv(17, D, 96))
        or (GetSystemMetricsForDpi(SM_CYHSCROLL, D) <> LayoutMulDiv(17, D, 96)) then
      begin
        Writeln('# differs at DPI ', D);
        Ok := False;
      end;
      D := D + 12;
    end;
    Check(Ok, 'system metric = MulDiv(17, DPI, 96) for every DPI 96..480 step 12');

    Section('form');
    CheckEqS(NativeStr(F), FormulaStr(F), 'form: client size');

    Section('TPanel');
    P := NewPanel(F, 0, 0, 120, 80);
    P.BevelOuter := bvNone;
    Compare(P, '(0, 0)', 'bsNone');
    P.BorderStyle := bsSingle;
    Compare(P, '(1, 1)', 'bsSingle');
    P.SetBounds(0, 0, 1, 1);
    Compare(P, '(1, 1)', 'bsSingle, smaller than its border');
    P.BevelOuter := bvRaised;
    P.BorderWidth := 3;
    P.SetBounds(0, 0, 90, 70);
    Compare(P, '(1, 1)', 'bsSingle with bevel and border width (align rect only)');

    Section('TGroupBox');
    G := TGroupBox.Create(F);
    G.SetBounds(130, 0, 150, 90);
    G.Caption := 'Group';
    G.Parent := F;
    Compare(G, '(0, 0)', 'group box');

    Section('TScrollBox bsSingle');
    S := TScrollBox.Create(F);
    S.SetBounds(0, 100, 200, 150);
    S.Parent := F;
    Compare(S, '(2, 2)', 'no bar');
    Content := NewPanel(S, 0, 0, 400, 20);
    CheckEqB(True, S.HorzScrollBar.IsScrollBarVisible and not S.VertScrollBar.IsScrollBarVisible, 'H only shown');
    Compare(S, '(2, 2)', 'horizontal bar');
    Content.SetBounds(0, 0, 20, 400);
    CheckEqB(True, S.VertScrollBar.IsScrollBarVisible and not S.HorzScrollBar.IsScrollBarVisible, 'V only shown');
    Compare(S, '(2, 2)', 'vertical bar');
    Content.SetBounds(0, 0, 400, 400);
    CheckEqB(True, S.VertScrollBar.IsScrollBarVisible and S.HorzScrollBar.IsScrollBarVisible, 'both shown');
    Compare(S, '(2, 2)', 'both bars');

    Section('TScrollBox bsNone');
    S.BorderStyle := bsNone;
    Compare(S, '(0, 0)', 'both bars');
    Content.SetBounds(0, 0, 400, 20);
    Compare(S, '(0, 0)', 'horizontal bar');
    Content.SetBounds(0, 0, 20, 400);
    Compare(S, '(0, 0)', 'vertical bar');
    Content.SetBounds(0, 0, 20, 20);
    CheckEqB(False, S.VertScrollBar.IsScrollBarVisible or S.HorzScrollBar.IsScrollBarVisible, 'none shown');
    Compare(S, '(0, 0)', 'no bar');

    Section('TPageControl and TTabSheet');
    PC := TPageControl.Create(F);
    PC.SetBounds(210, 100, 220, 160);
    PC.Parent := F;
    Sheet := TTabSheet.Create(F);
    Sheet.Caption := 'Page';
    Sheet.PageControl := PC;
    PC.ActivePage := Sheet;
    Compare(PC, '(0, 0)', 'page control');
    Compare(Sheet, '(0, 0)', 'tab sheet');

    Section('TToolBar');
    Bar := TToolBar.Create(F);
    Bar.Parent := F;
    Compare(Bar, '(0, 0)', 'tool bar');

    Section('TStatusBar');
    Status := TStatusBar.Create(F);
    Status.Parent := F;
    Compare(Status, '(0, 0)', 'status bar');
  finally
    F.Free;
  end;
end;

begin
  Run;
  Done;
end.
