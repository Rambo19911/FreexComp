// covers: UIF-070, UIF-071
// opc: -U ..\..\src
// needs: desktop
program Core_Reflection;
{ U-031, core.md 7.5: the parent forwards WM_COMMAND (LParam <> 0), WM_HSCROLL/WM_VSCROLL (LParam <> 0), WM_NOTIFY
  (hwndFrom), WM_DRAWITEM (hwndItem) and WM_MEASUREITEM (control id; WParam <> 0) to the child object as CN_BASE + Msg
  and returns its result; without a target (LParam 0, foreign window, WParam 0) default processing applies, and
  scroll messages with LParam 0 reach the parent's own WMHScroll/WMVScroll. Child windows carry distinct non-zero
  control ids. A native button's own BN_CLICKED arrives as CN_COMMAND; WM_CTLCOLOR* answers with the child's brush. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, TestKit;

type
  TTestWin = class(TWinControl)
  protected
    procedure WndProc(var Message: TMessage); override;
    procedure WMHScroll(var Message: TWMScroll); override;
    procedure WMVScroll(var Message: TWMScroll); override;
  public
    constructor Create(AOwner: TComponent); override;
    property Color;
  end;

  TTopWin = class(TTestWin)
  protected
    procedure CreateParams(var Params: TCreateParams); override;
  end;

  TTestButton = class(TTestWin)
  protected
    procedure CreateParams(var Params: TCreateParams); override;
  end;

var
  Log: string;

constructor TTestWin.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  ControlStyle := ControlStyle + [csAcceptsControls];
end;

procedure TTestWin.WndProc(var Message: TMessage);
var
  Item: PMeasureItemStruct;
begin
  case Message.Msg of
    CN_COMMAND:
      begin
        Log := Log + Name + '.CN_COMMAND(' + IntToStr(HiWord(Message.WParam)) + ');';
        Message.Result := 11;
      end;
    CN_HSCROLL:
      begin
        Log := Log + Name + '.CN_HSCROLL(' + IntToStr(LoWord(Message.WParam)) + ');';
        Message.Result := 12;
      end;
    CN_VSCROLL:
      begin
        Log := Log + Name + '.CN_VSCROLL(' + IntToStr(LoWord(Message.WParam)) + ');';
        Message.Result := 13;
      end;
    CN_NOTIFY:
      begin
        Log := Log + Name + '.CN_NOTIFY(' + IntToStr(PNMHdr(Message.LParam)^.code) + ');';
        Message.Result := 14;
      end;
    CN_DRAWITEM:
      begin
        Log := Log + Name + '.CN_DRAWITEM(' + IntToStr(PDrawItemStruct(Message.LParam)^.itemID) + ');';
        Message.Result := 15;
      end;
    CN_MEASUREITEM:
      begin
        Item := PMeasureItemStruct(Message.LParam);
        Log := Log + Name + '.CN_MEASUREITEM(' + IntToStr(Item^.itemID) + ');';
        Item^.itemHeight := 33;
        Message.Result := 16;
      end;
  else
    inherited WndProc(Message);
  end;
end;

procedure TTestWin.WMHScroll(var Message: TWMScroll);
begin
  Log := Log + Name + '.WMHScroll;';
  inherited WMHScroll(Message);
end;

procedure TTestWin.WMVScroll(var Message: TWMScroll);
begin
  Log := Log + Name + '.WMVScroll;';
  inherited WMVScroll(Message);
end;

procedure TTopWin.CreateParams(var Params: TCreateParams);
begin
  inherited CreateParams(Params);
  Params.Style := WS_POPUP or WS_CLIPCHILDREN;
  Params.WndParent := 0;
end;

procedure TTestButton.CreateParams(var Params: TCreateParams);
begin
  inherited CreateParams(Params);
  Params.SubclassOf := 'BUTTON';
end;

function Send(Wnd: HWND; Msg: Cardinal; WParam: WPARAM; LParam: LPARAM; var R: LRESULT): string;
begin
  Log := '';
  R := SendMessageW(Wnd, Msg, WParam, LParam);
  Result := Log;
end;

procedure Run;
var
  Root: TTopWin;
  A, B: TTestWin;
  Btn: TTestButton;
  Foreign: HWND;
  R: LRESULT;
  Hdr: TNMHdr;
  Draw: TDrawItemStruct;
  Measure: TMeasureItemStruct;
  DC: HDC;
begin
  Root := TTopWin.Create(nil);
  try
    Root.Name := 'Root';
    Root.SetBounds(100, 100, 300, 200);
    A := TTestWin.Create(Root);
    A.Name := 'A';
    A.SetBounds(10, 10, 50, 30);
    A.Parent := Root;
    B := TTestWin.Create(Root);
    B.Name := 'B';
    B.SetBounds(70, 10, 50, 30);
    B.Parent := Root;
    Btn := TTestButton.Create(Root);
    Btn.Name := 'Btn';
    Btn.SetBounds(10, 60, 80, 25);
    Btn.Parent := Root;
    Root.HandleNeeded;
    Foreign := CreateWindowExW(0, 'STATIC', 'x', WS_CHILD, 0, 0, 10, 10, Root.Handle, 0, HINST(HInstance), nil);

    Section('control ids');
    CheckEqB(True, GetDlgCtrlID(A.Handle) <> 0, 'child window id non-zero');
    CheckEqB(True, GetDlgCtrlID(A.Handle) <> GetDlgCtrlID(B.Handle), 'sibling ids differ');

    Section('WM_COMMAND (UIF-070)');
    CheckEqS('A.CN_COMMAND(7);', Send(Root.Handle, WM_COMMAND, MakeWParam(GetDlgCtrlID(A.Handle), 7), A.Handle, R),
      'reflected to the child of LParam');
    CheckEqI(11, R, 'child result returned');
    CheckEqS('', Send(Root.Handle, WM_COMMAND, MakeWParam(5, 0), 0, R), 'LParam 0: not reflected');
    CheckEqS('', Send(Root.Handle, WM_COMMAND, MakeWParam(GetDlgCtrlID(Foreign), 1), Foreign, R),
      'foreign window: default processing');
    CheckEqS('Btn.CN_COMMAND(0);', Send(Btn.Handle, BM_CLICK, 0, 0, R), 'native BN_CLICKED arrives as CN_COMMAND');

    Section('scroll');
    CheckEqS('B.CN_HSCROLL(1);', Send(Root.Handle, WM_HSCROLL, SB_LINERIGHT, B.Handle, R), 'WM_HSCROLL reflected');
    CheckEqI(12, R, 'WM_HSCROLL result');
    CheckEqS('B.CN_VSCROLL(1);', Send(Root.Handle, WM_VSCROLL, SB_LINEDOWN, B.Handle, R), 'WM_VSCROLL reflected');
    CheckEqI(13, R, 'WM_VSCROLL result');
    CheckEqS('Root.WMHScroll;', Send(Root.Handle, WM_HSCROLL, SB_LINERIGHT, 0, R), 'LParam 0: own WMHScroll');
    CheckEqS('Root.WMVScroll;', Send(Root.Handle, WM_VSCROLL, SB_LINEDOWN, 0, R), 'LParam 0: own WMVScroll');

    Section('WM_NOTIFY');
    Hdr.hwndFrom := B.Handle;
    Hdr.idFrom := UINT_PTR(GetDlgCtrlID(B.Handle));
    Hdr.code := 123;
    CheckEqS('B.CN_NOTIFY(123);', Send(Root.Handle, WM_NOTIFY, Hdr.idFrom, LPARAM(@Hdr), R), 'reflected to hwndFrom');
    CheckEqI(14, R, 'WM_NOTIFY result');
    Hdr.hwndFrom := Foreign;
    CheckEqS('', Send(Root.Handle, WM_NOTIFY, 1, LPARAM(@Hdr), R), 'foreign sender: default');

    Section('owner draw');
    FillChar(Draw, SizeOf(Draw), 0);
    Draw.CtlType := ODT_BUTTON;
    Draw.CtlID := GetDlgCtrlID(A.Handle);
    Draw.itemID := 4;
    Draw.itemAction := ODA_DRAWENTIRE;
    Draw.hwndItem := A.Handle;
    CheckEqS('A.CN_DRAWITEM(4);', Send(Root.Handle, WM_DRAWITEM, Draw.CtlID, LPARAM(@Draw), R),
      'WM_DRAWITEM reflected to hwndItem');
    CheckEqI(15, R, 'WM_DRAWITEM result');
    CheckEqS('', Send(Root.Handle, WM_DRAWITEM, 0, LPARAM(@Draw), R), 'WParam 0 (menu): not reflected');
    FillChar(Measure, SizeOf(Measure), 0);
    Measure.CtlID := GetDlgCtrlID(B.Handle);
    Measure.itemID := 2;
    CheckEqS('B.CN_MEASUREITEM(2);', Send(Root.Handle, WM_MEASUREITEM, Measure.CtlID, LPARAM(@Measure), R),
      'WM_MEASUREITEM reflected by control id');
    CheckEqI(33, Measure.itemHeight, 'child filled the structure');
    CheckEqI(16, R, 'WM_MEASUREITEM result');

    Section('WM_CTLCOLOR* (UIF-071)');
    A.Color := clRed;
    DC := GetDC(Root.Handle);
    try
      CheckEqB(True, SendMessageW(Root.Handle, WM_CTLCOLORSTATIC, WPARAM(DC), A.Handle) = LRESULT(A.Brush.Handle),
        'child brush returned');
      CheckEqI(Integer(ColorToRGB(clRed)), Integer(GetBkColor(DC)), 'background colour set');
    finally
      ReleaseDC(Root.Handle, DC);
    end;
    DestroyWindow(Foreign);
  finally
    Root.Free;
  end;
end;

begin
  Run;
  Done;
end.
