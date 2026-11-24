// phase: 7
// covers: UIF-880, UIF-881, UIF-882, UIF-883, UIF-884, UIF-885, UIF-886
// needs: desktop
program Hooks_DataAware;
{ U-115, U-116, controls-standard.md section 31: what a descendant control of another unit relies on.
  UIF-880: the TCustom... bases declare every published member of TLabel, TEdit, TMemo, TCheckBox and TComboBox as
  protected (descendants here re-publish all of them, or none and keep the defaults); a TCustomControl that paints
  itself and has a scroll bar of its own. UIF-881: Change, run-time ReadOnly, Modified. UIF-882: EditCanModify before
  a character, Backspace, Delete, paste, cut, clear and undo; never for code, never while ReadOnly. UIF-883: Toggle.
  UIF-884: the combo box hooks, EditCanModify for the edit part and for a user selection (taken back when refused).
  UIF-885: the key methods before the events, CM_WANTSPECIALKEY for Escape and Enter, DoEnter / DoExit and an
  exception raised in DoExit. UIF-886: unnamed helper child controls.
  The clipboard is never read or written: paste and cut are only sent while they are refused. }
uses
  Windows, Messages, Types, SysUtils, Classes, TypInfo, Graphics, Controls, Forms, StdCtrls, TestKit;

type
  TLog = class
  public
    Text: string;
    Swallow: Boolean;
    procedure Add(const S: string);
    procedure Changed(Sender: TObject);
    procedure Clicked(Sender: TObject);
    procedure Selected(Sender: TObject);
    procedure Dropped(Sender: TObject);
    procedure Closed(Sender: TObject);
    procedure KeyDown(Sender: TObject; var Key: Word; Shift: TShiftState);
    procedure KeyUp(Sender: TObject; var Key: Word; Shift: TShiftState);
    procedure KeyPress(Sender: TObject; var Key: Char);
    procedure Entered(Sender: TObject);
    procedure Exited(Sender: TObject);
    procedure Exc(Sender: TObject; E: Exception);
  end;

  { every published member of TLabel }
  THookLabel = class(TCustomLabel)
  published
    property Alignment;
    property AutoSize;
    property Caption;
    property Color;
    property FocusControl;
    property Font;
    property Layout;
    property ParentColor;
    property ParentFont;
    property ShowAccelChar;
    property Transparent;
    property WordWrap;
    property OnClick;
    property OnContextPopup;
    property OnDblClick;
    property OnMouseDown;
    property OnMouseEnter;
    property OnMouseLeave;
    property OnMouseMove;
    property OnMouseUp;
  end;

  { every published member of TEdit; the hooks of UIF-881, UIF-882 and UIF-885 }
  THookEdit = class(TCustomEdit)
  protected
    procedure Change; override;
    function EditCanModify: Boolean; override;
    procedure KeyDown(var Key: Word; Shift: TShiftState); override;
    procedure KeyUp(var Key: Word; Shift: TShiftState); override;
    procedure KeyPress(var Key: Char); override;
    procedure DoEnter; override;
    procedure DoExit; override;
    procedure WndProc(var Message: TMessage); override;
  public
    Allow: Boolean;
    Asked: Integer;
    Silent: Boolean;                                         { EditCanModify sets RejectSilently }
    Quiet: Boolean;                                          { Change does not call inherited }
    EatDown: Boolean;                                        { KeyDown sets Key := 0 }
    EatPress: Boolean;                                       { KeyPress sets Key := #0 }
    WantEscape: Boolean;
    WantReturn: Boolean;
    FailExit: Boolean;                                       { DoExit raises }
    TakeBack: Boolean;                                       { ... after taking the focus back }
  published
    property Alignment;
    property AutoSelect;
    property AutoSize;
    property BorderStyle;
    property CharCase;
    property Color;
    property Font;
    property HideSelection;
    property MaxLength;
    property NumbersOnly;
    property ParentColor;
    property ParentFont;
    property PasswordChar;
    property ReadOnly;
    property TabOrder;
    property TabStop;
    property Text;
    property TextHint;
    property OnChange;
    property OnClick;
    property OnContextPopup;
    property OnDblClick;
    property OnEnter;
    property OnExit;
    property OnKeyDown;
    property OnKeyPress;
    property OnKeyUp;
    property OnMouseDown;
    property OnMouseEnter;
    property OnMouseLeave;
    property OnMouseMove;
    property OnMouseUp;
  end;

  { publishes nothing: every property keeps its default }
  TBareEdit = class(TCustomEdit)
  end;

  { every published member of TMemo }
  THookMemo = class(TCustomMemo)
  protected
    procedure Change; override;
    function EditCanModify: Boolean; override;
  public
    Allow: Boolean;
    Asked: Integer;
    Silent: Boolean;
  published
    property Alignment;
    property BorderStyle;
    property Color;
    property Font;
    property HideSelection;
    property Lines;
    property MaxLength;
    property ParentColor;
    property ParentFont;
    property ReadOnly;
    property ScrollBars;
    property TabOrder;
    property TabStop;
    property WantReturns;
    property WantTabs;
    property WordWrap;
    property OnChange;
    property OnClick;
    property OnContextPopup;
    property OnDblClick;
    property OnEnter;
    property OnExit;
    property OnKeyDown;
    property OnKeyPress;
    property OnKeyUp;
    property OnMouseDown;
    property OnMouseEnter;
    property OnMouseLeave;
    property OnMouseMove;
    property OnMouseUp;
  end;

  { every published member of TCheckBox; UIF-883 }
  THookCheck = class(TCustomCheckBox)
  protected
    procedure Toggle; override;
  public
    Veto: Boolean;
  published
    property Action;
    property Alignment;
    property AllowGrayed;
    property Caption;
    property Checked;
    property Font;
    property ParentFont;
    property State;
    property TabOrder;
    property TabStop;
    property WordWrap;
    property OnClick;
    property OnContextPopup;
    property OnDblClick;
    property OnEnter;
    property OnExit;
    property OnKeyDown;
    property OnKeyPress;
    property OnKeyUp;
    property OnMouseDown;
    property OnMouseEnter;
    property OnMouseLeave;
    property OnMouseMove;
    property OnMouseUp;
  end;

  { every published member of TComboBox; UIF-884 }
  THookCombo = class(TCustomComboBox)
  protected
    procedure Change; override;
    procedure Select; override;
    procedure DropDown; override;
    procedure CloseUp; override;
    function EditCanModify: Boolean; override;
  public
    Allow: Boolean;
    Asked: Integer;
    Silent: Boolean;                                         { EditCanModify sets RejectSilently }
    Trace: Boolean;                                          { EditCanModify and Change log IsSelecting }
    function Selecting: Boolean;
  published
    property CharCase;
    property Color;
    property DropDownCount;
    property Font;
    property Items;
    property ItemIndex;
    property MaxLength;
    property ParentColor;
    property ParentFont;
    property Sorted;
    property Style;
    property TabOrder;
    property TabStop;
    property Text;
    property TextHint;
    property OnChange;
    property OnClick;
    property OnCloseUp;
    property OnContextPopup;
    property OnDblClick;
    property OnDropDown;
    property OnEnter;
    property OnExit;
    property OnKeyDown;
    property OnKeyPress;
    property OnKeyUp;
    property OnMouseDown;
    property OnMouseEnter;
    property OnMouseLeave;
    property OnMouseMove;
    property OnMouseUp;
    property OnSelect;
  end;

  TBareCombo = class(TCustomComboBox)
  end;

  { UIF-880: a wholly owner-drawn control with a scroll bar of its own }
  TScroller = class(TCustomControl)
  protected
    procedure CreateParams(var Params: TCreateParams); override;
    procedure CreateWnd; override;
    procedure Paint; override;
    procedure WndProc(var Message: TMessage); override;
  public
    Painted: Integer;
    Scrolls: string;
    HasCanvas: Boolean;
  end;

  { UIF-886: a control with a helper child of its own }
  THost = class(TCustomControl)
  public
    Helper: TButton;
    constructor Create(AOwner: TComponent); override;
  end;

  TButtons = class
  public
    procedure DefaultClick(Sender: TObject);
    procedure CancelClick(Sender: TObject);
  end;

var
  Log: TLog;
  Beeps: Integer;

procedure CountBeep;
begin
  Inc(Beeps);
end;

{ ---------------------------------------------------------------- TLog }

procedure TLog.Add(const S: string);
begin
  Text := Text + S + ';';
end;

procedure TLog.Changed(Sender: TObject);
begin
  Add('OnChange');
end;

procedure TLog.Clicked(Sender: TObject);
begin
  Add('OnClick');
end;

procedure TLog.Selected(Sender: TObject);
begin
  Add('OnSelect');
end;

procedure TLog.Dropped(Sender: TObject);
begin
  Add('OnDropDown');
end;

procedure TLog.Closed(Sender: TObject);
begin
  Add('OnCloseUp');
end;

procedure TLog.KeyDown(Sender: TObject; var Key: Word; Shift: TShiftState);
begin
  Add('OnKeyDown:' + IntToStr(Key));
  if Swallow then
    Key := 0;
end;

procedure TLog.KeyUp(Sender: TObject; var Key: Word; Shift: TShiftState);
begin
  Add('OnKeyUp:' + IntToStr(Key));
end;

procedure TLog.KeyPress(Sender: TObject; var Key: Char);
begin
  Add('OnKeyPress:' + Key);
  if Swallow then
    Key := #0;
end;

procedure TLog.Entered(Sender: TObject);
begin
  Add('OnEnter');
end;

procedure TLog.Exited(Sender: TObject);
begin
  Add('OnExit');
end;

procedure TLog.Exc(Sender: TObject; E: Exception);
begin
  Add('exception:' + E.ClassName + ':' + E.Message);
end;

{ ---------------------------------------------------------------- the descendants }

procedure THookEdit.Change;
begin
  Log.Add('Change');
  if not Quiet then
    inherited Change;
end;

function THookEdit.EditCanModify: Boolean;
begin
  Inc(Asked);
  if Silent then
    RejectSilently := True;
  Result := Allow;
end;

procedure THookEdit.KeyDown(var Key: Word; Shift: TShiftState);
begin
  Log.Add('KeyDown:' + IntToStr(Key));
  inherited KeyDown(Key, Shift);
  if EatDown then
    Key := 0;
end;

procedure THookEdit.KeyUp(var Key: Word; Shift: TShiftState);
begin
  Log.Add('KeyUp:' + IntToStr(Key));
  inherited KeyUp(Key, Shift);
end;

procedure THookEdit.KeyPress(var Key: Char);
begin
  Log.Add('KeyPress:' + Key);
  inherited KeyPress(Key);
  if EatPress then
    Key := #0;
end;

procedure THookEdit.DoEnter;
begin
  Log.Add('DoEnter');
  inherited DoEnter;
end;

procedure THookEdit.DoExit;
begin
  Log.Add('DoExit');
  if FailExit then
  begin
    if TakeBack then
      SetFocus;
    raise EInvalidOperation.Create('not valid');
  end;
  inherited DoExit;
end;

procedure THookEdit.WndProc(var Message: TMessage);
begin
  if (Message.Msg = CM_WANTSPECIALKEY)
    and (((Message.WParam = VK_ESCAPE) and WantEscape) or ((Message.WParam = VK_RETURN) and WantReturn)) then
  begin
    Message.Result := 1;
    Exit;
  end;
  inherited WndProc(Message);
end;

procedure THookMemo.Change;
begin
  Log.Add('Change');
  inherited Change;
end;

function THookMemo.EditCanModify: Boolean;
begin
  Inc(Asked);
  if Silent then
    RejectSilently := True;
  Result := Allow;
end;

procedure THookCheck.Toggle;
begin
  Log.Add('Toggle');
  if not Veto then
    inherited Toggle;
end;

function THookCombo.Selecting: Boolean;
begin
  Result := IsSelecting;
end;

procedure THookCombo.Change;
begin
  if Trace then
    Log.Add('selecting=' + BoolToStr(IsSelecting, True));
  Log.Add('Change');
  inherited Change;
end;

procedure THookCombo.Select;
begin
  Log.Add('Select');
  inherited Select;
end;

procedure THookCombo.DropDown;
begin
  Log.Add('DropDown');
  inherited DropDown;
end;

procedure THookCombo.CloseUp;
begin
  Log.Add('CloseUp');
  inherited CloseUp;
end;

function THookCombo.EditCanModify: Boolean;
begin
  Inc(Asked);
  if Trace then
    Log.Add('ask,selecting=' + BoolToStr(IsSelecting, True));
  if Silent then
    RejectSilently := True;
  Result := Allow;
end;

procedure TScroller.CreateParams(var Params: TCreateParams);
begin
  inherited CreateParams(Params);
  Params.Style := Params.Style or WS_VSCROLL;
end;

procedure TScroller.CreateWnd;
var
  SI: TScrollInfo;
begin
  inherited CreateWnd;
  FillChar(SI, SizeOf(SI), 0);
  SI.cbSize := SizeOf(SI);
  SI.fMask := SIF_ALL;
  SI.nMin := 0;
  SI.nMax := 99;
  SI.nPage := 10;
  SI.nPos := 20;
  SetScrollInfo(Handle, SB_VERT, SI, True);
end;

procedure TScroller.Paint;
begin
  Inc(Painted);
  HasCanvas := Canvas.Handle <> 0;
  Canvas.Brush.Color := clWhite;
  Canvas.FillRect(ClientRect);
end;

procedure TScroller.WndProc(var Message: TMessage);
begin
  if Message.Msg = WM_VSCROLL then
    Scrolls := Scrolls + IntToStr(LoWord(Message.WParam)) + ';';
  inherited WndProc(Message);
end;

constructor THost.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  SetBounds(0, 0, 100, 40);
  Helper := TButton.Create(Self);
  Helper.SetBounds(60, 4, 30, 20);
  Helper.Parent := Self;
end;

procedure TButtons.DefaultClick(Sender: TObject);
begin
  Log.Add('default');
end;

procedure TButtons.CancelClick(Sender: TObject);
begin
  Log.Add('cancel');
end;

{ ---------------------------------------------------------------- helpers }

function StyleOf(H: HWND): NativeInt;
begin
  Result := GetWindowLongPtrW(H, GWL_STYLE);
end;

procedure Press(C: TWinControl; Key: Word);
begin
  Log.Text := '';
  PostMessageW(C.Handle, WM_KEYDOWN, Key, 1);
  Application.ProcessMessages;
end;

{ the user clicks into C }
procedure Click(C: TWinControl);
begin
  SendMessageW(C.Handle, WM_LBUTTONDOWN, MK_LBUTTON, $00050005);
  SendMessageW(C.Handle, WM_LBUTTONUP, 0, $00050005);
end;

{ the exception of C.SetFocus as 'Class:Message', '' without one }
function FocusError(C: TWinControl): string;
begin
  Result := '';
  try
    C.SetFocus;
  except
    on E: Exception do
      Result := E.ClassName + ':' + E.Message;
  end;
end;

procedure Reset(E: THookEdit; const S: string; Allow: Boolean);
begin
  E.Text := S;
  E.Allow := Allow;
  E.Asked := 0;
  Beeps := 0;
  Log.Text := '';
end;

procedure ResetCombo(C: THookCombo; Index: Integer; Allow: Boolean);
begin
  C.ItemIndex := Index;
  C.Allow := Allow;
  C.Asked := 0;
  Beeps := 0;
  Log.Text := '';
end;

function Published(AClass: TClass; const Names: string): Boolean;
var
  L: TStringList;
  I: Integer;
begin
  Result := True;
  L := TStringList.Create;
  try
    L.CommaText := Names;
    for I := 0 to L.Count - 1 do
      if not IsPublishedProp(AClass, L[I]) then
        Result := False;
  finally
    L.Free;
  end;
end;

{ ---------------------------------------------------------------- sections }

procedure TestBases(F: TForm);
var
  L: THookLabel;
  E: THookEdit;
  B: TBareEdit;
  BC: TBareCombo;
  M: THookMemo;
  K: THookCheck;
  C: THookCombo;
  S: TScroller;
  SI: TScrollInfo;
  S11: Integer;
begin
  Section('bases declare, concrete classes publish (UIF-880)');
  S11 := LayoutMulDiv(11, F.PixelsPerInch, 96);
  L := THookLabel.Create(F);
  CheckEqB(True, (L.Alignment = taLeftJustify) and L.AutoSize and (L.Layout = tlTop) and L.ShowAccelChar
    and L.Transparent and not L.WordWrap and (L.FocusControl = nil), 'label defaults');
  CheckEqB(True, Published(THookLabel, 'Alignment,AutoSize,Caption,Color,FocusControl,Font,Layout,ParentColor,'
    + 'ParentFont,ShowAccelChar,Transparent,WordWrap,OnClick,OnDblClick,OnMouseDown'), 'label members published');
  E := THookEdit.Create(F);
  CheckEqB(True, (E.Alignment = taLeftJustify) and E.AutoSelect and E.AutoSize and (E.BorderStyle = bsSingle)
    and (E.CharCase = ecNormal) and E.HideSelection and (E.MaxLength = 0) and not E.NumbersOnly
    and (E.PasswordChar = #0) and not E.ReadOnly and (E.TextHint = '') and E.TabStop and (E.Color = clWindow),
    'edit defaults');
  CheckEqB(True, Published(THookEdit, 'Alignment,AutoSelect,AutoSize,BorderStyle,CharCase,Color,Font,'
    + 'HideSelection,MaxLength,NumbersOnly,ParentColor,ParentFont,PasswordChar,ReadOnly,TabOrder,TabStop,Text,'
    + 'TextHint,OnChange,OnKeyDown,OnKeyPress,OnKeyUp,OnEnter,OnExit'), 'edit members published');
  B := TBareEdit.Create(F);
  B.Parent := F;
  CheckEqB(False, IsPublishedProp(TBareEdit, 'MaxLength') or IsPublishedProp(TBareEdit, 'OnChange')
    or IsPublishedProp(TBareEdit, 'BorderStyle'), 'a descendant publishes only what it names');
  CheckEqI(Abs(B.Font.Height) + S11, B.Height, 'an unpublished AutoSize / BorderStyle keeps its default (rule I2)');
  BC := TBareCombo.Create(F);
  BC.Parent := F;
  CheckEqI(Abs(BC.Font.Height) + S11, BC.Height, 'an unpublished Style keeps its default (rule I1)');
  M := THookMemo.Create(F);
  CheckEqB(True, (M.ScrollBars = ssNone) and M.WantReturns and not M.WantTabs and M.WordWrap
    and (M.Lines.Count = 0), 'memo defaults');
  CheckEqB(True, Published(THookMemo, 'Alignment,BorderStyle,Color,Font,HideSelection,Lines,MaxLength,ReadOnly,'
    + 'ScrollBars,WantReturns,WantTabs,WordWrap,OnChange'), 'memo members published');
  K := THookCheck.Create(F);
  CheckEqB(True, (K.Alignment = taRightJustify) and not K.AllowGrayed and not K.Checked
    and (K.State = cbUnchecked) and not K.WordWrap, 'check box defaults');
  CheckEqB(True, Published(THookCheck, 'Action,Alignment,AllowGrayed,Caption,Checked,Font,ParentFont,State,'
    + 'TabOrder,TabStop,WordWrap,OnClick'), 'check box members published');
  C := THookCombo.Create(F);
  CheckEqB(True, (C.CharCase = ecNormal) and (C.DropDownCount = 8) and (C.ItemIndex = -1) and (C.MaxLength = 0)
    and not C.Sorted and (C.Style = csDropDown) and (C.TextHint = ''), 'combo box defaults');
  CheckEqB(True, Published(THookCombo, 'CharCase,Color,DropDownCount,Font,Items,ItemIndex,MaxLength,Sorted,'
    + 'Style,Text,TextHint,OnChange,OnCloseUp,OnDropDown,OnSelect'), 'combo box members published');
  L.Free;
  E.Free;
  B.Free;
  BC.Free;
  M.Free;
  K.Free;
  C.Free;

  S := TScroller.Create(F);
  S.SetBounds(300, 200, 80, 60);
  S.Parent := F;
  S.HandleNeeded;
  CheckEqB(True, (StyleOf(S.Handle) and WS_VSCROLL) <> 0, 'owner-drawn control: WS_VSCROLL from CreateParams');
  FillChar(SI, SizeOf(SI), 0);
  SI.cbSize := SizeOf(SI);
  SI.fMask := SIF_ALL;
  GetScrollInfo(S.Handle, SB_VERT, SI);
  CheckEqI(20, SI.nPos, 'SetScrollInfo');
  SendMessageW(S.Handle, WM_VSCROLL, SB_LINEDOWN, 0);
  CheckEqS('1;', S.Scrolls, 'WM_VSCROLL arrives in WndProc');
  S.Invalidate;
  UpdateWindow(S.Handle);
  Check(S.Painted > 0, 'Paint');
  CheckEqB(True, S.HasCanvas, 'Canvas');
  S.Free;
end;

procedure TestEdit(F: TForm);
var
  E: THookEdit;
  M: THookMemo;
  H: HWND;
  I: Integer;
begin
  Section('Change, ReadOnly, Modified (UIF-881)');
  E := THookEdit.Create(F);
  E.SetBounds(10, 10, 150, 23);
  E.Parent := F;
  E.OnChange := Log.Changed;
  Log.Text := '';
  E.Text := 'one';
  CheckEqS('Change;OnChange;', Log.Text, 'handle-less assignment: Change, which fires OnChange');
  H := E.Handle;
  Log.Text := '';
  E.Text := 'two';
  CheckEqS('Change;OnChange;', Log.Text, 'assignment that changes the text');
  Log.Text := '';
  E.Text := 'two';
  CheckEqS('', Log.Text, 'same text: no Change');
  E.Allow := True;
  E.SelStart := 3;
  Log.Text := '';
  SendMessageW(H, WM_CHAR, Ord('x'), 1);
  CheckEqS('KeyPress:x;Change;OnChange;', Log.Text, 'user edit');
  CheckEqB(True, E.Modified, 'Modified after a user edit');
  E.Modified := False;
  CheckEqB(False, E.Modified, 'Modified := False');
  E.Quiet := True;
  Log.Text := '';
  E.Text := 'three';
  CheckEqS('Change;', Log.Text, 'an override that does not call inherited: no OnChange');
  E.Quiet := False;
  for I := 1 to 3 do
  begin
    E.ReadOnly := True;
    CheckEqB(True, (StyleOf(E.Handle) and ES_READONLY) <> 0, 'ReadOnly := True (EM_SETREADONLY)');
    E.Asked := 0;
    Log.Text := '';
    SendMessageW(E.Handle, WM_CHAR, Ord('y'), 1);
    CheckEqS('three', E.Text, 'read-only: the character is discarded');
    CheckEqI(0, E.Asked, 'read-only: EditCanModify is not asked');
    E.ReadOnly := False;
    CheckEqB(False, (StyleOf(E.Handle) and ES_READONLY) <> 0, 'ReadOnly := False');
  end;
  Check(E.Handle = H, 'the window was never recreated');
  M := THookMemo.Create(F);
  M.SetBounds(10, 40, 150, 60);
  M.Parent := F;
  M.OnChange := Log.Changed;
  M.HandleNeeded;
  Log.Text := '';
  M.Text := 'memo';
  CheckEqS('Change;OnChange;', Log.Text, 'memo: Change inherited from the edit base');
  M.ReadOnly := True;
  CheckEqB(True, (StyleOf(M.Handle) and ES_READONLY) <> 0, 'memo: ReadOnly at run time');
  M.ReadOnly := False;

  Section('EditCanModify (UIF-882)');
  RejectBeepHook := CountBeep;
  Reset(E, 'abc', False);
  E.SelStart := 3;
  SendMessageW(H, WM_CHAR, Ord('x'), 1);
  CheckEqS('abc', E.Text, 'refused character: discarded');
  CheckEqI(1, E.Asked, 'asked once');
  CheckEqI(1, Beeps, 'one beep');
  CheckEqS('KeyPress:x;', Log.Text, 'KeyPress ran before, no Change');
  Reset(E, 'abc', False);
  E.SelStart := 3;
  SendMessageW(H, WM_CHAR, 8, 1);
  CheckEqS('abc', E.Text, 'refused Backspace');
  CheckEqI(1, E.Asked, 'asked once');
  Reset(E, 'abc', False);
  E.SelStart := 0;
  SendMessageW(H, WM_KEYDOWN, VK_DELETE, 1);
  CheckEqS('abc', E.Text, 'refused Delete key');
  CheckEqI(1, E.Asked, 'asked once');
  CheckEqI(1, Beeps, 'one beep');
  Reset(E, 'abc', False);
  E.SelectAll;
  SendMessageW(H, WM_PASTE, 0, 0);
  CheckEqS('abc', E.Text, 'refused WM_PASTE');
  CheckEqI(1, E.Asked, 'asked once');
  SendMessageW(H, WM_CUT, 0, 0);
  CheckEqS('abc', E.Text, 'refused WM_CUT');
  CheckEqI(2, E.Asked, 'asked once');
  SendMessageW(H, WM_CLEAR, 0, 0);
  CheckEqS('abc', E.Text, 'refused WM_CLEAR');
  CheckEqI(3, E.Asked, 'asked once');
  E.PasteFromClipboard;
  E.CutToClipboard;
  CheckEqS('abc', E.Text, 'PasteFromClipboard / CutToClipboard are the user''s paste and cut');
  CheckEqI(5, E.Asked, 'asked once each');
  CheckEqI(5, Beeps, 'a beep for every refusal');
  CheckEqS('', Log.Text, 'nothing changed');
  { undo: a typed character, then the undo refused, then allowed }
  Reset(E, 'abc', True);
  E.SelStart := 3;
  SendMessageW(H, WM_CHAR, Ord('d'), 1);
  CheckEqS('abcd', E.Text, 'allowed character: applied');
  CheckEqI(1, E.Asked, 'asked once');
  CheckEqI(0, Beeps, 'no beep');
  CheckEqS('KeyPress:d;Change;OnChange;', Log.Text, 'KeyPress, then the change');
  E.Allow := False;
  SendMessageW(H, WM_UNDO, 0, 0);
  CheckEqS('abcd', E.Text, 'refused WM_UNDO');
  CheckEqI(2, E.Asked, 'asked once');
  SendMessageW(H, WM_CHAR, 26, 1);
  CheckEqS('abcd', E.Text, 'refused Ctrl+Z (the native control sends itself EM_UNDO)');
  CheckEqI(3, E.Asked, 'asked once');
  E.Undo;
  CheckEqS('abcd', E.Text, 'refused Undo');
  CheckEqI(4, E.Asked, 'asked once');
  SendMessageW(H, WM_CHAR, 22, 1);
  CheckEqS('abcd', E.Text, 'refused Ctrl+V (the native control sends itself WM_PASTE)');
  CheckEqI(5, E.Asked, 'asked once');
  E.SelectAll;
  SendMessageW(H, WM_CHAR, 24, 1);
  CheckEqS('abcd', E.Text, 'refused Ctrl+X (the native control sends itself WM_CUT)');
  CheckEqI(6, E.Asked, 'asked once');
  E.Allow := True;
  SendMessageW(H, WM_UNDO, 0, 0);
  CheckEqS('abc', E.Text, 'allowed WM_UNDO');
  CheckEqI(7, E.Asked, 'asked once');
  Reset(E, 'abc', True);
  E.SelStart := 3;
  SendMessageW(H, WM_CHAR, 8, 1);
  CheckEqS('ab', E.Text, 'allowed Backspace');
  E.SelStart := 0;
  SendMessageW(H, WM_KEYDOWN, VK_DELETE, 1);
  CheckEqS('b', E.Text, 'allowed Delete key');
  E.SelectAll;
  SendMessageW(H, WM_CLEAR, 0, 0);
  CheckEqS('', E.Text, 'allowed WM_CLEAR');
  CheckEqI(3, E.Asked, 'asked once per message');
  CheckEqI(0, Beeps, 'no beep');
  Reset(E, 'abc', False);
  SendMessageW(H, WM_KEYDOWN, VK_RIGHT, 1);
  SendMessageW(H, WM_KEYDOWN, VK_HOME, 1);
  SendMessageW(H, WM_CHAR, 27, 1);
  SendMessageW(H, WM_KEYUP, VK_DELETE, 1);
  CheckEqI(0, E.Asked, 'keys that do not modify the text do not ask');
  Reset(E, 'abc', False);
  E.EatPress := True;
  SendMessageW(H, WM_CHAR, Ord('x'), 1);
  E.EatPress := False;
  E.EatDown := True;
  SendMessageW(H, WM_KEYDOWN, VK_DELETE, 1);
  E.EatDown := False;
  CheckEqI(0, E.Asked, 'a key swallowed in KeyPress / KeyDown does not ask');
  CheckEqI(0, Beeps, 'and does not beep');
  Reset(E, 'abc', False);
  E.Text := 'code';
  E.SelStart := 0;
  E.SelLength := 1;
  E.SelText := 'm';
  CheckEqS('mode', E.Text, 'Text := and SelText := are applied');
  E.ClearSelection;
  E.Clear;
  CheckEqS('', E.Text, 'Clear');
  CheckEqI(0, E.Asked, 'code never asks');
  CheckEqI(0, Beeps, 'code never beeps');
  { U-140: a refusal without the beep }
  Reset(E, 'abc', False);
  E.Silent := True;
  E.SelStart := 3;
  SendMessageW(H, WM_KEYDOWN, VK_DELETE, 1);
  SendMessageW(H, WM_CHAR, Ord('x'), 1);
  E.SelectAll;
  SendMessageW(H, WM_PASTE, 0, 0);
  SendMessageW(H, WM_CUT, 0, 0);
  CheckEqS('abc', E.Text, 'RejectSilently: the input is still discarded');
  CheckEqI(4, E.Asked, 'RejectSilently: asked once each');
  CheckEqI(0, Beeps, 'RejectSilently: no beep');
  E.Silent := False;
  SendMessageW(H, WM_CHAR, Ord('x'), 1);
  CheckEqI(1, Beeps, 'RejectSilently lasts for the one question: the next refusal beeps');
  E.Silent := True;
  E.Allow := True;
  E.SelStart := 3;
  SendMessageW(H, WM_CHAR, Ord('d'), 1);
  CheckEqS('abcd', E.Text, 'RejectSilently with True: the input is applied');
  E.Silent := False;
  E.Allow := False;
  SendMessageW(H, WM_CHAR, Ord('x'), 1);
  CheckEqI(2, Beeps, 'RejectSilently set while allowing does not silence a later refusal');
  M.Allow := False;
  M.Silent := True;
  M.Text := 'line';
  Beeps := 0;
  SendMessageW(M.Handle, WM_CHAR, Ord('x'), 1);
  CheckEqS('line', M.Text, 'memo: RejectSilently discards');
  CheckEqI(0, Beeps, 'memo: RejectSilently, no beep');
  M.Silent := False;
  { memo: a line break and a tab modify the text }
  M.Allow := False;
  M.Asked := 0;
  M.Text := 'line';
  M.SelStart := 4;
  SendMessageW(M.Handle, WM_CHAR, 13, 1);
  SendMessageW(M.Handle, WM_CHAR, 9, 1);
  SendMessageW(M.Handle, WM_CHAR, Ord('x'), 1);
  CheckEqS('line', M.Text, 'memo: refused line break, tab and character');
  CheckEqI(3, M.Asked, 'memo: asked once each');
  M.Allow := True;
  SendMessageW(M.Handle, WM_CHAR, 13, 1);
  CheckEqI(2, M.Lines.Count, 'memo: allowed line break');
  M.ReadOnly := True;
  M.Asked := 0;
  SendMessageW(M.Handle, WM_CHAR, Ord('x'), 1);
  SendMessageW(M.Handle, WM_CLEAR, 0, 0);
  CheckEqI(0, M.Asked, 'memo: read-only does not ask');
  M.Free;
  E.Free;
end;

{ F is not shown: BM_CLICK needs no active window then }
procedure TestCheckBox(F: TForm);
var
  K: THookCheck;
begin
  Section('Toggle (UIF-883)');
  K := THookCheck.Create(F);
  K.SetBounds(10, 110, 150, 17);
  K.Caption := 'Chec&k';
  K.Parent := F;
  K.OnClick := Log.Clicked;
  K.HandleNeeded;
  Log.Text := '';
  SendMessageW(K.Handle, BM_CLICK, 0, 0);
  CheckEqS('Toggle;OnClick;', Log.Text, 'user click: Toggle, which assigns State');
  CheckEqB(True, K.Checked, 'checked');
  SendMessageW(K.Handle, BM_CLICK, 0, 0);
  CheckEqB(False, K.Checked, 'unchecked');
  K.AllowGrayed := True;
  SendMessageW(K.Handle, BM_CLICK, 0, 0);
  SendMessageW(K.Handle, BM_CLICK, 0, 0);
  CheckEqB(True, K.State = cbGrayed, 'the base computes the next state (AllowGrayed)');
  K.Veto := True;
  Log.Text := '';
  SendMessageW(K.Handle, BM_CLICK, 0, 0);
  CheckEqS('Toggle;', Log.Text, 'an override that does not call inherited: no change, no OnClick');
  CheckEqB(True, K.State = cbGrayed, 'state kept');
  CheckEqI(BST_INDETERMINATE, SendMessageW(K.Handle, BM_GETCHECK, 0, 0), 'native state kept');
  Log.Text := '';
  K.Checked := True;
  K.State := cbUnchecked;
  CheckEqS('OnClick;OnClick;', Log.Text, 'Checked := / State := do not call Toggle');
  K.Free;
end;

{ F is shown: the accelerator needs a control that can take the focus }
procedure TestCheckBoxAccel(F: TForm);
var
  K: THookCheck;
begin
  K := THookCheck.Create(F);
  K.SetBounds(10, 110, 150, 17);
  K.Caption := 'Chec&k';
  K.Parent := F;
  K.OnClick := Log.Clicked;
  Log.Text := '';
  CheckEqI(1, K.Perform(CM_DIALOGCHAR, Ord('k'), 0), 'accelerator consumed');
  CheckEqS('Toggle;OnClick;', Log.Text, 'accelerator: Toggle');
  CheckEqB(True, K.Checked, 'checked');
  K.Veto := True;
  Log.Text := '';
  K.Perform(CM_DIALOGCHAR, Ord('k'), 0);
  CheckEqS('Toggle;', Log.Text, 'accelerator: the override decides');
  CheckEqB(True, K.Checked, 'state kept');
  K.Free;
end;

procedure TestComboBox(F: TForm);
var
  C: THookCombo;
  Edit: HWND;
begin
  Section('combo box hooks (UIF-884)');
  C := THookCombo.Create(F);
  C.SetBounds(10, 140, 150, 23);
  C.Parent := F;
  C.Items.Add('a');
  C.Items.Add('b');
  C.Items.Add('c');
  C.OnChange := Log.Changed;
  C.OnSelect := Log.Selected;
  C.OnClick := Log.Clicked;
  C.OnDropDown := Log.Dropped;
  C.OnCloseUp := Log.Closed;
  Edit := FindWindowExW(C.Handle, 0, 'Edit', nil);
  Check(Edit <> 0, 'the edit part');
  ResetCombo(C, 0, True);
  SendMessageW(Edit, WM_KEYDOWN, VK_DOWN, 1);
  CheckEqS('Change;OnChange;Select;OnSelect;OnClick;', Log.Text, 'user selection: Change, Select, then OnClick');
  CheckEqI(1, C.ItemIndex, 'selected');
  CheckEqS('b', C.Text, 'text of the item');
  CheckEqI(1, C.Asked, 'asked once');
  Log.Text := '';
  C.DroppedDown := True;
  CheckEqS('DropDown;OnDropDown;', Log.Text, 'DropDown');
  Log.Text := '';
  C.DroppedDown := False;
  CheckEqS('CloseUp;OnCloseUp;', Log.Text, 'CloseUp');
  ResetCombo(C, 1, True);
  C.SelStart := 1;
  SendMessageW(Edit, WM_CHAR, Ord('x'), 1);
  CheckEqS('bx', C.Text, 'allowed character in the edit part');
  CheckEqS('Change;OnChange;', Log.Text, 'editing: Change');
  CheckEqI(1, C.Asked, 'asked once');
  CheckEqI(0, Beeps, 'no beep');

  ResetCombo(C, 0, False);
  C.SelStart := 1;
  SendMessageW(Edit, WM_CHAR, Ord('x'), 1);
  CheckEqS('a', C.Text, 'refused character: discarded');
  CheckEqI(1, C.Asked, 'asked once');
  CheckEqI(1, Beeps, 'one beep');
  C.SelStart := 1;
  SendMessageW(Edit, WM_CHAR, 8, 1);
  C.SelStart := 0;
  SendMessageW(Edit, WM_KEYDOWN, VK_DELETE, 1);
  SendMessageW(Edit, EM_SETSEL, 0, -1);
  SendMessageW(Edit, WM_PASTE, 0, 0);
  SendMessageW(Edit, WM_CUT, 0, 0);
  SendMessageW(Edit, WM_CLEAR, 0, 0);
  SendMessageW(Edit, WM_UNDO, 0, 0);
  SendMessageW(Edit, EM_UNDO, 0, 0);
  CheckEqS('a', C.Text, 'refused Backspace, Delete, paste, cut, clear and undo');
  CheckEqI(8, C.Asked, 'asked once each');
  CheckEqI(8, Beeps, 'a beep each');
  CheckEqS('', Log.Text, 'no event');
  CheckEqI(0, C.ItemIndex, 'selection kept');

  ResetCombo(C, 0, False);
  SendMessageW(Edit, WM_KEYDOWN, VK_DOWN, 1);
  CheckEqI(0, C.ItemIndex, 'refused selection: ItemIndex restored');
  CheckEqS('a', C.Text, 'text restored');
  CheckEqS('', Log.Text, 'no event');
  CheckEqI(1, C.Asked, 'asked once');
  CheckEqI(1, Beeps, 'one beep');
  Application.ProcessMessages;
  CheckEqI(0, C.ItemIndex, 'still restored after the message queue is empty');
  CheckEqS('a', C.Text, 'text too');
  { a typed text that is no item }
  ResetCombo(C, -1, False);
  C.Text := 'zz';
  CheckEqI(-1, C.ItemIndex, 'a text that is no item');
  SendMessageW(Edit, WM_KEYDOWN, VK_DOWN, 1);
  Application.ProcessMessages;
  CheckEqI(-1, C.ItemIndex, 'refused selection: no item again');
  CheckEqS('zz', C.Text, 'the typed text again');
  CheckEqS('', Log.Text, 'no event');
  { refused in the dropped list }
  ResetCombo(C, 1, False);
  C.DroppedDown := True;
  Log.Text := '';
  SendMessageW(Edit, WM_KEYDOWN, VK_DOWN, 1);
  SendMessageW(Edit, WM_KEYDOWN, VK_DOWN, 1);
  CheckEqS('', Log.Text, 'dropped list: refused, no event');
  C.DroppedDown := False;
  Application.ProcessMessages;
  CheckEqI(1, C.ItemIndex, 'dropped list: ItemIndex restored');
  CheckEqS('b', C.Text, 'dropped list: text restored');
  { code }
  ResetCombo(C, 0, False);
  C.ItemIndex := 2;
  C.Text := 'code';
  CheckEqS('code', C.Text, 'ItemIndex := / Text := are applied');
  CheckEqI(0, C.Asked, 'code never asks');
  CheckEqS('', Log.Text, 'code fires nothing');

  { U-140: a refusal without the beep; a selection told from typing }
  ResetCombo(C, 0, False);
  C.Silent := True;
  C.SelStart := 1;
  SendMessageW(Edit, WM_CHAR, Ord('x'), 1);
  SendMessageW(Edit, WM_PASTE, 0, 0);
  CheckEqS('a', C.Text, 'RejectSilently: typing is still discarded');
  SendMessageW(Edit, WM_KEYDOWN, VK_DOWN, 1);
  Application.ProcessMessages;
  CheckEqI(0, C.ItemIndex, 'RejectSilently: the selection is still taken back');
  CheckEqS('a', C.Text, 'RejectSilently: text restored');
  CheckEqI(3, C.Asked, 'RejectSilently: asked once each');
  CheckEqI(0, Beeps, 'RejectSilently: no beep');
  CheckEqS('', Log.Text, 'RejectSilently: no event');
  C.Silent := False;
  SendMessageW(Edit, WM_KEYDOWN, VK_DOWN, 1);
  Application.ProcessMessages;
  CheckEqI(1, Beeps, 'RejectSilently lasts for the one question: the next refusal beeps');
  C.Trace := True;
  ResetCombo(C, 0, True);
  CheckEqB(False, C.Selecting, 'IsSelecting: False at rest');
  SendMessageW(Edit, WM_KEYDOWN, VK_DOWN, 1);
  CheckEqS('ask,selecting=True;selecting=True;Change;OnChange;Select;OnSelect;OnClick;', Log.Text,
    'IsSelecting: True in EditCanModify and Change of a user selection');
  CheckEqB(False, C.Selecting, 'IsSelecting: False after the selection');
  Log.Text := '';
  C.SelStart := 1;
  SendMessageW(Edit, WM_CHAR, Ord('x'), 1);
  CheckEqS('ask,selecting=False;selecting=False;Change;OnChange;', Log.Text,
    'IsSelecting: False in EditCanModify and Change of typing');
  ResetCombo(C, 0, False);
  SendMessageW(Edit, WM_KEYDOWN, VK_DOWN, 1);
  Application.ProcessMessages;
  CheckEqS('ask,selecting=True;', Log.Text, 'IsSelecting: True in the question of a refused selection');
  CheckEqB(False, C.Selecting, 'IsSelecting: False after the refusal');
  Log.Text := '';
  C.ItemIndex := 2;
  C.Text := 'code';
  CheckEqS('', Log.Text, 'IsSelecting: code asks nothing');
  C.Trace := False;

  { the keys of the edit part take the key path of the combo box (UIF-885) }
  C.OnKeyDown := Log.KeyDown;
  C.OnKeyPress := Log.KeyPress;
  C.OnKeyUp := Log.KeyUp;
  ResetCombo(C, 0, True);
  SendMessageW(Edit, WM_KEYDOWN, Ord('Q'), 1);
  SendMessageW(Edit, WM_CHAR, Ord('q'), 1);
  SendMessageW(Edit, WM_KEYUP, Ord('Q'), 1);
  CheckEqS('OnKeyDown:81;OnKeyPress:q;Change;OnChange;OnKeyUp:81;', Log.Text, 'key events of the edit part');
  ResetCombo(C, 0, False);
  Log.Swallow := True;
  SendMessageW(Edit, WM_CHAR, Ord('q'), 1);
  SendMessageW(Edit, WM_KEYDOWN, VK_DOWN, 1);
  Log.Swallow := False;
  CheckEqS('a', C.Text, 'a swallowed key does not reach the edit part');
  CheckEqI(0, C.ItemIndex, 'nor the list');
  CheckEqI(0, C.Asked, 'and does not ask');
  C.OnKeyDown := nil;
  C.OnKeyPress := nil;
  C.OnKeyUp := nil;

  { list style: the selection only }
  C.Style := csDropDownList;
  Check(FindWindowExW(C.Handle, 0, 'Edit', nil) = 0, 'list style: no edit part');
  ResetCombo(C, 0, False);
  SendMessageW(C.Handle, WM_KEYDOWN, VK_DOWN, 1);
  CheckEqI(0, C.ItemIndex, 'list style: refused selection restored');
  CheckEqS('a', C.Text, 'list style: text restored');
  CheckEqS('', Log.Text, 'list style: no event');
  CheckEqI(1, Beeps, 'list style: one beep');
  ResetCombo(C, 0, True);
  SendMessageW(C.Handle, WM_KEYDOWN, VK_DOWN, 1);
  CheckEqI(1, C.ItemIndex, 'list style: allowed selection');
  CheckEqS('Change;OnChange;Select;OnSelect;OnClick;', Log.Text, 'list style: the same order');
  C.Trace := True;
  C.Silent := True;
  ResetCombo(C, 0, False);
  SendMessageW(C.Handle, WM_KEYDOWN, VK_DOWN, 1);
  CheckEqS('ask,selecting=True;', Log.Text, 'list style: IsSelecting in the question');
  CheckEqI(0, C.ItemIndex, 'list style: RejectSilently, selection restored');
  CheckEqI(0, Beeps, 'list style: RejectSilently, no beep');
  C.Trace := False;
  C.Silent := False;
  C.Style := csDropDown;
  Edit := FindWindowExW(C.Handle, 0, 'Edit', nil);
  ResetCombo(C, 0, False);
  SendMessageW(Edit, WM_CHAR, Ord('x'), 1);
  CheckEqI(1, C.Asked, 'a recreated window: the new edit part asks');
  C.Free;
end;

procedure TestKeysAndFocus(F: TForm);
var
  E: THookEdit;
  Other: TEdit;
  OK, Cancel: TButton;
  B: TButtons;
begin
  Section('keys, Escape and Enter (UIF-885)');
  B := TButtons.Create;
  E := THookEdit.Create(F);
  E.Name := 'Hook';
  E.SetBounds(200, 10, 150, 23);
  E.Parent := F;
  E.Text := '';
  E.Allow := True;
  Other := TEdit.Create(F);
  Other.Name := 'Other';
  Other.SetBounds(200, 40, 150, 23);
  Other.Parent := F;
  OK := TButton.Create(F);
  OK.SetBounds(200, 70, 60, 25);
  OK.Default := True;
  OK.OnClick := B.DefaultClick;
  OK.Parent := F;
  Cancel := TButton.Create(F);
  Cancel.SetBounds(270, 70, 60, 25);
  Cancel.Cancel := True;
  Cancel.OnClick := B.CancelClick;
  Cancel.Parent := F;
  E.OnKeyDown := Log.KeyDown;
  E.OnKeyPress := Log.KeyPress;
  E.OnKeyUp := Log.KeyUp;
  E.OnEnter := Log.Entered;
  E.OnExit := Log.Exited;
  Other.SetFocus;
  Log.Text := '';
  E.SetFocus;
  CheckEqS('DoEnter;OnEnter;', Log.Text, 'DoEnter when the focus enters');
  Log.Text := '';
  SendMessageW(E.Handle, WM_KEYDOWN, Ord('A'), 1);
  SendMessageW(E.Handle, WM_CHAR, Ord('a'), 1);
  SendMessageW(E.Handle, WM_KEYUP, Ord('A'), 1);
  CheckEqS('KeyDown:65;OnKeyDown:65;KeyPress:a;OnKeyPress:a;Change;KeyUp:65;OnKeyUp:65;', Log.Text,
    'the key methods run before the events');
  CheckEqS('a', E.Text, 'the character was applied');
  E.EatDown := True;
  SendMessageW(E.Handle, WM_KEYDOWN, Ord('B'), 1);
  SendMessageW(E.Handle, WM_CHAR, Ord('b'), 1);
  E.EatDown := False;
  CheckEqS('a', E.Text, 'Key := 0 in KeyDown swallows the key and its character');
  E.EatPress := True;
  SendMessageW(E.Handle, WM_CHAR, Ord('c'), 1);
  E.EatPress := False;
  CheckEqS('a', E.Text, 'Key := #0 in KeyPress swallows the character');
  Press(E, VK_ESCAPE);
  CheckEqS('cancel;', Log.Text, 'Escape not wanted: the cancel button');
  Press(E, VK_RETURN);
  CheckEqS('default;', Log.Text, 'Enter not wanted: the default button');
  E.WantEscape := True;
  Press(E, VK_ESCAPE);
  CheckEqS('KeyDown:27;OnKeyDown:27;KeyPress:'#27';OnKeyPress:'#27';', Log.Text,
    'Escape wanted (CM_WANTSPECIALKEY): the control gets the key');
  Press(E, VK_RETURN);
  CheckEqS('default;', Log.Text, 'Enter still not wanted');
  E.WantEscape := False;
  E.WantReturn := True;
  Press(E, VK_RETURN);
  CheckEqS('KeyDown:13;OnKeyDown:13;KeyPress:'#13';OnKeyPress:'#13';', Log.Text,
    'Enter wanted: the control gets the key');
  Press(E, VK_ESCAPE);
  CheckEqS('cancel;', Log.Text, 'Escape not wanted again');
  E.WantReturn := False;

  Section('DoEnter, DoExit (UIF-885)');
  Log.Text := '';
  Other.SetFocus;
  CheckEqS('DoExit;OnExit;', Log.Text, 'DoExit when the focus leaves');
  Check(F.ActiveControl = Other, 'the focus moved');
  E.SetFocus;
  Application.OnException := Log.Exc;
  E.FailExit := True;
  { the user moves the focus with the mouse: the exception goes to the application handler }
  Log.Text := '';
  Click(Other);
  CheckEqS('DoExit;exception:EInvalidOperation:not valid;', Log.Text,
    'an exception raised in DoExit reaches the application handler');
  Check(F.ActiveControl = Other, 'after the focus change');
  Check(GetFocus = Other.Handle, 'natively too');
  E.FailExit := False;
  E.SetFocus;
  E.FailExit := True;
  E.TakeBack := True;
  Log.Text := '';
  Click(Other);
  CheckEqS('DoExit;exception:EInvalidOperation:not valid;', Log.Text,
    'a control that takes the focus back before raising');
  Check(F.ActiveControl = E, 'has the focus');
  Check(GetFocus = E.Handle, 'natively too');
  { code moves the focus: the exception is the caller's }
  Log.Text := '';
  CheckEqS('EInvalidOperation:not valid', FocusError(Other), 'SetFocus by code raises the exception of DoExit');
  CheckEqS('DoExit;', Log.Text, 'not reported twice');
  Check(F.ActiveControl = E, 'the control that took the focus back has it');
  E.TakeBack := False;
  E.FailExit := False;
  Other.SetFocus;
  Log.Text := '';
  Log.Text := '';
  E.SetFocus;
  CheckEqS('DoEnter;OnEnter;', Log.Text, 'the focus comes back as usual');
  Other.SetFocus;
  CheckEqS('DoEnter;OnEnter;DoExit;OnExit;', Log.Text, 'and leaves as usual');
  Application.OnException := nil;
  E.Free;
  Other.Free;
  OK.Free;
  Cancel.Free;
  B.Free;
end;

procedure TestHelpers(F: TForm);
var
  H: THost;
  S: string;
begin
  Section('helper child controls (UIF-886)');
  H := THost.Create(F);
  H.Name := 'Host';
  H.Parent := F;
  Check(H.Helper.Owner = H, 'Owner = the control');
  Check(H.Helper.Parent = H, 'Parent = the control');
  CheckEqS('', H.Helper.Name, 'empty Name');
  H.HandleNeeded;
  Check(GetParent(H.Helper.Handle) = H.Handle, 'a child window of the control');
  S := LayoutDumpText(F);
  Check(Pos('"name": "Host"', S) > 0, 'the control is in the layout dump');
  Check(Pos('TButton', S) = 0, 'its helper is not');
  Check(Pos('"controls": []', Copy(S, Pos('"name": "Host"', S), MaxInt)) > 0, 'the control has no listed children');
  Check(F.FindComponent('Host') = H, 'the control is a named component of the form');
  CheckEqI(0, F.ComponentCount - 1, 'the helper is not a component of the form');
  H.Free;
end;

procedure Run;
var
  F, G: TForm;
begin
  Application.Initialize;
  Log := TLog.Create;
  RejectBeepHook := CountBeep;
  F := TForm.CreateNew(nil);
  try
    F.Name := 'HookForm';
    F.ClientWidth := 400;
    F.ClientHeight := 300;
    F.Show;
    G := TForm.CreateNew(nil);
    try
      TestCheckBox(G);
    finally
      G.Free;
    end;
    TestCheckBoxAccel(F);
    TestBases(F);
    TestEdit(F);
    TestComboBox(F);
    TestKeysAndFocus(F);
    TestHelpers(F);
  finally
    F.Free;
    Log.Free;
  end;
end;

begin
  Run;
  Done;
end.
