// covers: UIF-650, UIF-651, UIF-652
// needs: desktop
program Ctl_CheckBox;
{ U-057, controls-standard.md section 5: TCheckBox defaults (97 x 17, TabStop); a sub-classed system BUTTON with
  BS_CHECKBOX or BS_3STATE (never the AUTO variants) and BS_LEFTTEXT, Alignment and AllowGrayed re-creating the
  window and keeping the state; State pushed with BM_SETCHECK before and after the handle; a native click and the
  accelerator toggle (unchecked -> checked -> grayed if AllowGrayed -> unchecked) through the virtual Toggle;
  OnClick on every State change, by the user or by code, never for an equal value or while loading; Checked and
  State streamed alone or together; leak check. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, TestKit;

type
  TLog = class
  public
    Text: string;
    procedure Clicked(Sender: TObject);
  end;

  TVetoBox = class(TCheckBox)
  protected
    procedure Toggle; override;
  public
    Veto: Boolean;
  end;

  TCheckForm = class(TForm)
  published
    ByChecked: TCheckBox;
    ByState: TCheckBox;
    Both: TCheckBox;
    procedure BoxClick(Sender: TObject);
  end;

{$FORM Ctl_CheckBox.form}

var
  Log: TLog;

procedure TLog.Clicked(Sender: TObject);
begin
  Text := Text + 'click:' + IntToStr(Ord(TCheckBox(Sender).State)) + ';';
end;

procedure TVetoBox.Toggle;
begin
  Log.Text := Log.Text + 'toggle;';
  if not Veto then
    inherited Toggle;
end;

procedure TCheckForm.BoxClick(Sender: TObject);
begin
  Log.Text := Log.Text + 'streamed click;';
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

function Kind(C: TWinControl): Integer;
begin
  Result := Integer(GetWindowLongPtrW(C.Handle, GWL_STYLE) and BS_TYPEMASK);
end;

function LeftText(C: TWinControl): Boolean;
begin
  Result := (GetWindowLongPtrW(C.Handle, GWL_STYLE) and BS_LEFTTEXT) <> 0;
end;

function NativeCheck(C: TWinControl): Integer;
begin
  Result := Integer(SendMessageW(C.Handle, BM_GETCHECK, 0, 0));
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
  C: TCheckBox;
  V: TVetoBox;
  SF: TCheckForm;
  H: HWND;
  I, Before: Integer;
begin
  Application.Initialize;
  Log := TLog.Create;
  F := TForm.CreateNew(nil);
  try
    F.ClientWidth := 300;
    F.ClientHeight := 200;
    C := TCheckBox.Create(F);
    C.Name := 'C';

    Section('defaults');
    CheckEqI(97, C.Width, 'Width 97');
    CheckEqI(17, C.Height, 'Height 17');
    CheckEqB(True, C.Alignment = taRightJustify, 'Alignment taRightJustify');
    CheckEqB(False, C.AllowGrayed, 'AllowGrayed');
    CheckEqS('', C.Caption, 'Caption');
    CheckEqB(False, C.Checked, 'Checked');
    CheckEqB(True, C.ParentFont, 'ParentFont');
    CheckEqB(True, C.State = cbUnchecked, 'State cbUnchecked');
    CheckEqI(-1, C.TabOrder, 'TabOrder');
    CheckEqB(True, C.TabStop, 'TabStop True');
    CheckEqB(False, C.WordWrap, 'WordWrap');
    Check(GetClass('TCheckBox') <> nil, 'registered');

    Section('handle-less state, window');
    C.OnClick := Log.Clicked;
    Log.Text := '';
    C.Checked := True;
    CheckEqS('click:1;', Log.Text, 'Checked by code without a handle fires OnClick');
    C.Caption := 'Box';
    C.Parent := F;
    H := C.Handle;
    CheckEqS('Button', ClassOf(H), 'system class BUTTON');
    CheckEqI(BS_CHECKBOX, Kind(C), 'BS_CHECKBOX (not auto)');
    CheckEqB(False, LeftText(C), 'taRightJustify: no BS_LEFTTEXT');
    CheckEqI(BST_CHECKED, NativeCheck(C), 'state pushed');
    C.Alignment := taLeftJustify;
    Check(C.Handle <> H, 'Alignment recreates');
    CheckEqB(True, LeftText(C), 'taLeftJustify: BS_LEFTTEXT');
    CheckEqI(BST_CHECKED, NativeCheck(C), 'state kept');
    C.Alignment := taRightJustify;
    H := C.Handle;
    C.AllowGrayed := True;
    Check(C.Handle <> H, 'AllowGrayed recreates');
    CheckEqI(BS_3STATE, Kind(C), 'BS_3STATE (not auto)');
    Log.Text := '';
    C.State := cbGrayed;
    CheckEqI(BST_INDETERMINATE, NativeCheck(C), 'cbGrayed: BST_INDETERMINATE');
    CheckEqB(False, C.Checked, 'grayed is not Checked');
    CheckEqS('click:2;', Log.Text, 'State by code fires OnClick');
    Log.Text := '';
    C.State := cbGrayed;
    CheckEqS('', Log.Text, 'equal State: no OnClick');
    C.Checked := False;
    CheckEqB(True, C.State = cbUnchecked, 'Checked False -> cbUnchecked');
    CheckEqI(BST_UNCHECKED, NativeCheck(C), 'unchecked pushed');
    C.WordWrap := True;
    CheckEqB(True, (GetWindowLongPtrW(C.Handle, GWL_STYLE) and BS_MULTILINE) <> 0, 'BS_MULTILINE');
    C.WordWrap := False;

    Section('Toggle (UIF-650, UIF-651)');
    Log.Text := '';
    SendMessageW(C.Handle, BM_CLICK, 0, 0);
    CheckEqS('click:1;', Log.Text, 'click: unchecked -> checked, OnClick once');
    SendMessageW(C.Handle, BM_CLICK, 0, 0);
    CheckEqS('click:1;click:2;', Log.Text, 'AllowGrayed: checked -> grayed');
    SendMessageW(C.Handle, BM_CLICK, 0, 0);
    CheckEqS('click:1;click:2;click:0;', Log.Text, 'grayed -> unchecked');
    C.AllowGrayed := False;
    C.Checked := True;
    Log.Text := '';
    SendMessageW(C.Handle, BM_CLICK, 0, 0);
    CheckEqS('click:0;', Log.Text, 'no AllowGrayed: checked -> unchecked');
    CheckEqI(BST_UNCHECKED, NativeCheck(C), 'the native check follows');
    C.Caption := '&Box';
    F.Show;
    Log.Text := '';
    CheckEqI(0, C.Perform(CM_DIALOGCHAR, Ord('x'), 0), 'other character: not consumed');
    CheckEqI(1, C.Perform(CM_DIALOGCHAR, Ord('b'), 0), 'accelerator consumed');
    CheckEqS('click:1;', Log.Text, 'accelerator toggles');
    CheckEqB(True, F.ActiveControl = C, 'accelerator focuses');
    V := TVetoBox.Create(F);
    V.Parent := F;
    V.Top := 30;
    V.OnClick := Log.Clicked;
    Log.Text := '';
    SendMessageW(V.Handle, BM_CLICK, 0, 0);
    CheckEqS('toggle;click:1;', Log.Text, 'user click goes through Toggle');
    V.Veto := True;
    Log.Text := '';
    SendMessageW(V.Handle, BM_CLICK, 0, 0);
    CheckEqS('toggle;', Log.Text, 'Toggle override vetoes');
    CheckEqB(True, V.Checked, 'vetoed: unchanged');
    CheckEqI(BST_CHECKED, NativeCheck(V), 'vetoed: native check unchanged');
    Log.Text := '';
    V.Checked := False;
    CheckEqS('click:0;', Log.Text, 'code does not call Toggle');
    F.Hide;

    Section('streaming (UIF-652)');
    Log.Text := '';
    SF := TCheckForm.Create(nil);
    try
      CheckEqS('', Log.Text, 'no OnClick while loading');
      CheckEqB(True, SF.ByChecked.Checked, 'Checked alone');
      CheckEqB(True, SF.ByState.State = cbGrayed, 'State alone');
      CheckEqB(True, SF.ByState.AllowGrayed, 'AllowGrayed');
      CheckEqB(True, SF.Both.Checked and (SF.Both.State = cbChecked), 'both');
      CheckEqB(True, SF.Both.Alignment = taLeftJustify, 'Alignment');
      CheckEqS('Both', SF.Both.Caption, 'Caption');
      CheckEqI(-15, SF.Both.Font.Height, 'Font.Height');
      CheckEqB(False, SF.Both.ParentFont, 'ParentFont False');
      CheckEqB(False, SF.Both.TabStop, 'TabStop False');
      CheckEqB(True, SF.Both.WordWrap, 'WordWrap');
      Check(Assigned(SF.Both.OnClick), 'OnClick bound');
      SF.Both.Checked := False;
      CheckEqS('streamed click;', Log.Text, 'OnClick handler of the form');
    finally
      SF.Free;
    end;

    Section('leak');
    Before := GuiObjects;
    for I := 1 to 100 do
    begin
      C := TCheckBox.Create(nil);
      C.Caption := 'Leak';
      C.Parent := F;
      C.HandleNeeded;
      C.Free;
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
