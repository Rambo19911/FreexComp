// covers: UIF-120
// needs: desktop
program Form_Accelerators;
{ U-046, core.md 8.5: IsAccel compares the character after a single '&' with AnsiUpperCase ('&&' is a literal
  ampersand); WM_SYSCHAR, and WM_CHAR for a control that does not want characters (no DLGC_WANTCHARS /
  DLGC_WANTALLKEYS), are offered to the form's visible, enabled controls as CM_DIALOGCHAR, depth-first; a control
  that answers consumes the character (no OnKeyPress); hidden, disabled controls and children of disabled parents are
  skipped; nothing answering means normal delivery. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, TestKit, KeyStateKit;

type
  TAccelBox = class(TCustomControl)
  protected
    procedure WndProc(var Message: TMessage); override;
  public
    DlgCode: LRESULT;
    constructor Create(AOwner: TComponent); override;
  published
    property Caption;
  end;

  TLog = class
  public
    Text: string;
    procedure KeyPress(Sender: TObject; var Key: Char);
  end;

var
  Log: TLog;

constructor TAccelBox.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  SetBounds(0, 0, 40, 20);
  OnKeyPress := Log.KeyPress;
end;

procedure TAccelBox.WndProc(var Message: TMessage);
begin
  case Message.Msg of
    WM_GETDLGCODE:
      Message.Result := DlgCode;
    CM_DIALOGCHAR:
      if IsAccel(Word(Message.WParam), Caption) then
      begin
        Log.Text := Log.Text + 'accel:' + Name + ';';
        Message.Result := 1;
      end;
    WM_SYSCHAR:
      begin
        Log.Text := Log.Text + 'syschar:' + Name + ';';
        inherited WndProc(Message);
      end;
  else
    inherited WndProc(Message);
  end;
end;

procedure TLog.KeyPress(Sender: TObject; var Key: Char);
begin
  Text := Text + 'keypress:' + TComponent(Sender).Name + ':' + Key + ';';
end;

function Box(const Name, Caption: string; Parent: TWinControl): TAccelBox;
begin
  Result := TAccelBox.Create(Parent);
  Result.Name := Name;
  Result.Caption := Caption;
  Result.Parent := Parent;
end;

procedure Send(C: TWinControl; Msg: UINT; Ch: Char);
begin
  Log.Text := '';
  PostKeyMessage(C.Handle, Msg, Ord(Ch), 1);
  Application.ProcessMessages;
end;

procedure Run;
var
  F: TForm;
  Target, A1, P, B1, H, D, Q, E: TAccelBox;
begin
  Section('IsAccel');
  CheckEqB(True, IsAccel(Ord('S'), '&Save'), 'upper case');
  CheckEqB(True, IsAccel(Ord('s'), '&Save'), 'lower case character');
  CheckEqB(True, IsAccel(Ord('v'), 'Sa&Ve'), 'case-insensitive caption');
  CheckEqB(False, IsAccel(Ord('A'), 'Save &&As'), '&& is a literal ampersand');
  CheckEqB(True, IsAccel(Ord('A'), 'Save && &As'), 'accelerator after a literal ampersand');
  CheckEqB(False, IsAccel(Ord('S'), 'Save'), 'no ampersand');
  CheckEqB(False, IsAccel(Ord('S'), 'Save&'), 'trailing ampersand');
  CheckEqB(True, IsAccel(Ord('Ä'), '&äh'), 'AnsiUpperCase');

  Application.Initialize;
  Log := TLog.Create;
  F := TForm.CreateNew(nil);
  try
    Target := Box('Target', 'Target', F);
    A1 := Box('A1', '&Alpha', F);
    P := Box('P', 'Panel', F);
    B1 := Box('B1', '&Beta', P);
    H := Box('H', '&Hidden', F);
    H.Visible := False;
    D := Box('D', '&Delta', F);
    D.Enabled := False;
    Q := Box('Q', 'Off', F);
    Q.Enabled := False;
    E := Box('E', '&Echo', Q);
    F.Show;

    Section('CM_DIALOGCHAR (UIF-120)');
    Send(Target, WM_SYSCHAR, 'a');
    CheckEqS('accel:A1;', Log.Text, 'Alt+character: the control with the accelerator, consumed');
    Send(Target, WM_SYSCHAR, 'b');
    CheckEqS('accel:B1;', Log.Text, 'nested control found depth-first');
    Send(Target, WM_CHAR, 'a');
    CheckEqS('accel:A1;', Log.Text, 'WM_CHAR on a control without DLGC_WANTCHARS: offered, consumed');
    Send(Target, WM_CHAR, 'x');
    CheckEqS('keypress:Target:x;', Log.Text, 'nobody answers: delivered');
    Send(Target, WM_SYSCHAR, 'h');
    CheckEqS('syschar:Target;', Log.Text, 'hidden control skipped');
    Send(Target, WM_SYSCHAR, 'd');
    CheckEqS('syschar:Target;', Log.Text, 'disabled control skipped');
    Send(Target, WM_SYSCHAR, 'e');
    CheckEqS('syschar:Target;', Log.Text, 'child of a disabled parent skipped');
    Target.DlgCode := DLGC_WANTCHARS;
    Send(Target, WM_CHAR, 'a');
    CheckEqS('keypress:Target:a;', Log.Text, 'DLGC_WANTCHARS: WM_CHAR delivered');
    Send(Target, WM_SYSCHAR, 'a');
    CheckEqS('accel:A1;', Log.Text, 'WM_SYSCHAR offered regardless');
    Target.DlgCode := DLGC_WANTALLKEYS;
    Send(Target, WM_CHAR, 'b');
    CheckEqS('keypress:Target:b;', Log.Text, 'DLGC_WANTALLKEYS: WM_CHAR delivered');
  finally
    F.Free;
  end;
  Log.Free;
end;

begin
  Run;
  Done;
end.
