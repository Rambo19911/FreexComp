// covers: UIF-660, UIF-661, UIF-662, UIF-115
// needs: desktop
program Ctl_RadioButton;
{ U-058, controls-standard.md section 6: TRadioButton defaults (113 x 17, TabStop False); a sub-classed system BUTTON
  with BS_RADIOBUTTON (never the AUTO variant), BS_LEFTTEXT and BS_MULTILINE; the group (same Parent): checking
  unchecks the others silently and fires OnClick of the checked one, unchecking by code fires nothing; TabStop
  follows Checked; a native click, the accelerator and arrow-key navigation between siblings check the button;
  while loading several checked buttons of one group leave the last one in creation order checked; leak check. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, TestKit, KeyStateKit;

type
  TLog = class
  public
    Text: string;
    procedure Clicked(Sender: TObject);
  end;

  TRadioForm = class(TForm)
  published
    First: TRadioButton;
    Second: TRadioButton;
    Third: TRadioButton;
    Box: TGroupBox;
    Inner1: TRadioButton;
    Inner2: TRadioButton;
    procedure RadioClick(Sender: TObject);
  end;

{$FORM Ctl_RadioButton.form}

var
  Log: TLog;

procedure TLog.Clicked(Sender: TObject);
begin
  Text := Text + TComponent(Sender).Name + ';';
end;

procedure TRadioForm.RadioClick(Sender: TObject);
begin
  Log.Text := Log.Text + 'form:' + TComponent(Sender).Name + ';';
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

function Style(C: TWinControl): NativeInt;
begin
  Result := GetWindowLongPtrW(C.Handle, GWL_STYLE);
end;

function NativeCheck(C: TWinControl): Integer;
begin
  Result := Integer(SendMessageW(C.Handle, BM_GETCHECK, 0, 0));
end;

{ the names of the checked buttons among the controls of P }
function CheckedOf(P: TWinControl): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to P.ControlCount - 1 do
    if (P.Controls[I] is TRadioButton) and TRadioButton(P.Controls[I]).Checked then
      Result := Result + P.Controls[I].Name + ';';
end;

{ GDI + USER objects of the process }
function GuiObjects: Integer;
begin
  Result := Integer(GetGuiResources(GetCurrentProcess, GR_GDIOBJECTS))
    + Integer(GetGuiResources(GetCurrentProcess, GR_USEROBJECTS));
end;

function NewRadio(F: TForm; const AName: string; ATop: Integer): TRadioButton;
begin
  Result := TRadioButton.Create(F);
  Result.Name := AName;
  Result.Caption := AName;
  Result.Top := ATop;
  Result.OnClick := Log.Clicked;
end;

procedure Run;
var
  F: TForm;
  A, B, C, R: TRadioButton;
  Other: TRadioButton;
  G: TGroupBox;
  RF: TRadioForm;
  H: HWND;
  I, Before: Integer;
begin
  Application.Initialize;
  Log := TLog.Create;
  F := TForm.CreateNew(nil);
  try
    F.ClientWidth := 300;
    F.ClientHeight := 200;
    R := TRadioButton.Create(F);

    Section('defaults');
    CheckEqI(113, R.Width, 'Width 113');
    CheckEqI(17, R.Height, 'Height 17');
    CheckEqB(True, R.Alignment = taRightJustify, 'Alignment taRightJustify');
    CheckEqS('', R.Caption, 'Caption');
    CheckEqB(False, R.Checked, 'Checked');
    CheckEqB(True, R.ParentFont, 'ParentFont');
    CheckEqI(-1, R.TabOrder, 'TabOrder');
    CheckEqB(False, R.TabStop, 'TabStop False');
    CheckEqB(False, R.WordWrap, 'WordWrap');
    Check(GetClass('TRadioButton') <> nil, 'registered');
    R.Free;

    Section('window');
    A := NewRadio(F, 'A', 10);
    B := NewRadio(F, 'B', 30);
    C := NewRadio(F, 'C', 50);
    A.Parent := F;
    B.Parent := F;
    C.Parent := F;
    H := A.Handle;
    CheckEqS('Button', ClassOf(H), 'system class BUTTON');
    CheckEqI(BS_RADIOBUTTON, Integer(Style(A) and BS_TYPEMASK), 'BS_RADIOBUTTON (not auto)');
    CheckEqB(False, (Style(A) and BS_LEFTTEXT) <> 0, 'taRightJustify: no BS_LEFTTEXT');
    A.Checked := True;
    A.Alignment := taLeftJustify;
    Check(A.Handle <> H, 'Alignment recreates');
    CheckEqB(True, (Style(A) and BS_LEFTTEXT) <> 0, 'taLeftJustify: BS_LEFTTEXT');
    CheckEqI(BST_CHECKED, NativeCheck(A), 'Checked kept over the recreation');
    A.Alignment := taRightJustify;
    A.WordWrap := True;
    CheckEqB(True, (Style(A) and BS_MULTILINE) <> 0, 'BS_MULTILINE');
    A.WordWrap := False;

    Section('group (UIF-660)');
    A.Checked := False;
    Log.Text := '';
    CheckEqS('', CheckedOf(F), 'none checked');
    B.Checked := True;
    CheckEqS('B;', Log.Text, 'checking fires OnClick of the checked button');
    CheckEqS('B;', CheckedOf(F), 'B checked');
    Log.Text := '';
    C.Checked := True;
    CheckEqS('C;', Log.Text, 'the unchecked sibling fires nothing');
    CheckEqS('C;', CheckedOf(F), 'C checked, B unchecked');
    CheckEqI(BST_UNCHECKED, NativeCheck(B), 'B unchecked natively');
    CheckEqI(BST_CHECKED, NativeCheck(C), 'C checked natively');
    Log.Text := '';
    C.Checked := True;
    CheckEqS('', Log.Text, 'equal value: no OnClick');
    C.Checked := False;
    CheckEqS('', Log.Text, 'unchecking by code: no OnClick');
    CheckEqS('', CheckedOf(F), 'none checked again');
    G := TGroupBox.Create(F);
    G.SetBounds(130, 10, 150, 100);
    G.Parent := F;
    Other := NewRadio(F, 'Other', 20);
    Other.Parent := G;
    Other.Checked := True;
    A.Checked := True;
    CheckEqB(True, Other.Checked, 'another parent is another group');
    CheckEqS('A;', CheckedOf(F), 'A checked on the form');

    Section('TabStop follows Checked');
    CheckEqB(True, A.TabStop, 'checked: TabStop True');
    CheckEqB(False, B.TabStop, 'unchecked: TabStop False');
    B.TabStop := True;
    CheckEqB(True, B.TabStop, 'the setter is honoured');
    B.Checked := True;
    CheckEqB(False, A.TabStop, 'unchecked by the group: TabStop False');
    B.Checked := False;
    CheckEqB(False, B.TabStop, 'unchecked by code: TabStop False');

    Section('user (UIF-661)');
    A.Checked := True;
    Log.Text := '';
    SendMessageW(B.Handle, BM_CLICK, 0, 0);
    CheckEqS('B;', Log.Text, 'native click checks, OnClick once');
    CheckEqS('B;', CheckedOf(F), 'A unchecked by the click on B');
    CheckEqI(BST_UNCHECKED, NativeCheck(A), 'A unchecked natively');
    Log.Text := '';
    SendMessageW(B.Handle, BM_CLICK, 0, 0);
    CheckEqS('', Log.Text, 'click on the checked button: no OnClick');
    CheckEqI(BST_CHECKED, NativeCheck(B), 'stays checked');
    C.Caption := '&Cee';
    F.Show;
    Log.Text := '';
    CheckEqI(0, C.Perform(CM_DIALOGCHAR, Ord('x'), 0), 'other character: not consumed');
    CheckEqI(1, C.Perform(CM_DIALOGCHAR, Ord('c'), 0), 'accelerator consumed');
    CheckEqS('C;', Log.Text, 'accelerator checks');
    CheckEqB(True, F.ActiveControl = C, 'accelerator focuses');

    Section('arrow navigation (UIF-115)');
    A.SetFocus;
    Log.Text := '';
    SetKeyState(False, False);
    CheckEqI(1, F.Perform(CM_DIALOGKEY, VK_DOWN, 0), 'Down consumed');
    CheckEqB(True, F.ActiveControl = B, 'Down: next sibling focused');
    CheckEqS('B;', CheckedOf(F), 'Down: the focused button is checked');
    CheckEqS('B;', Log.Text, 'Down: OnClick');
    SetKeyState(False, False);
    F.Perform(CM_DIALOGKEY, VK_RIGHT, 0);
    CheckEqB(True, F.ActiveControl = C, 'Right: next sibling');
    CheckEqS('C;', CheckedOf(F), 'Right: checked');
    SetKeyState(False, False);
    F.Perform(CM_DIALOGKEY, VK_DOWN, 0);
    CheckEqB(True, F.ActiveControl = G, 'Down: siblings only, the group box is next');
    CheckEqS('C;', CheckedOf(F), 'a non-radio sibling: nothing checked');
    CheckEqB(True, Other.Checked, 'inside the group box: unchanged');
    C.SetFocus;
    SetKeyState(False, False);
    F.Perform(CM_DIALOGKEY, VK_UP, 0);
    CheckEqB(True, F.ActiveControl = B, 'Up: previous sibling');
    SetKeyState(False, False);
    F.Perform(CM_DIALOGKEY, VK_LEFT, 0);
    CheckEqB(True, F.ActiveControl = A, 'Left: previous sibling');
    CheckEqS('A;', CheckedOf(F), 'Left: checked');
    SetKeyState(False, False);
    F.Perform(CM_DIALOGKEY, VK_UP, 0);
    CheckEqB(True, F.ActiveControl = G, 'Up from the first: wraps to the last sibling');
    Other.SetFocus;
    Log.Text := '';
    SetKeyState(False, False);
    F.Perform(CM_DIALOGKEY, VK_DOWN, 0);
    CheckEqB(True, F.ActiveControl = Other, 'only child: stays');
    CheckEqS('', Log.Text, 'stays: no OnClick');
    F.Hide;

    Section('loading (UIF-662)');
    Log.Text := '';
    RF := TRadioForm.Create(nil);
    try
      CheckEqS('', Log.Text, 'no OnClick while loading');
      CheckEqS('Third;', CheckedOf(RF), 'the last checked in creation order wins');
      CheckEqS('Inner2;', CheckedOf(RF.Box), 'per group');
      CheckEqB(True, RF.Third.TabStop, 'TabStop of the winner');
      CheckEqB(False, RF.First.TabStop, 'TabStop of the loser');
      CheckEqB(True, RF.Third.Alignment = taLeftJustify, 'Alignment');
      CheckEqS('Third', RF.Third.Caption, 'Caption');
      CheckEqI(-15, RF.Third.Font.Height, 'Font.Height');
      CheckEqB(False, RF.Third.ParentFont, 'ParentFont False');
      CheckEqB(True, RF.Third.WordWrap, 'WordWrap');
      CheckEqI(BST_UNCHECKED, NativeCheck(RF.First), 'loser unchecked natively');
      CheckEqI(BST_CHECKED, NativeCheck(RF.Third), 'winner checked natively');
      RF.Second.Checked := True;
      CheckEqS('form:Second;', Log.Text, 'OnClick handler of the form');
      CheckEqS('Second;', CheckedOf(RF), 'group works after loading');
    finally
      RF.Free;
    end;

    Section('leak');
    Before := GuiObjects;
    for I := 1 to 100 do
    begin
      R := TRadioButton.Create(nil);
      R.Caption := 'Leak';
      R.Parent := F;
      R.Checked := True;
      R.HandleNeeded;
      R.Free;
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
