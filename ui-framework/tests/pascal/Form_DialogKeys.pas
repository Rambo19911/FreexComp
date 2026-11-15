// covers: UIF-115
// needs: desktop
program Form_DialogKeys;
{ U-045, core.md 8.4: Application.ProcessMessage pre-processes WM_KEYDOWN for windows of a form: IsShortCut first;
  Tab, arrows, Return, Escape are vetoed by CM_WANTSPECIALKEY or the WM_GETDLGCODE answer of the target control
  (DLGC_WANTALLKEYS, DLGC_WANTTAB for Tab, DLGC_WANTARROWS for arrows); otherwise CM_DIALOGKEY to the form: Tab
  selects the next tab stop, arrows the next sibling (wrapping), Return/Escape are offered to the controls of the
  form (nested ones too). A consumed key never reaches OnKeyDown. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, TestKit;

type
  TKeyBox = class(TCustomControl)
  protected
    procedure WndProc(var Message: TMessage); override;
  public
    DlgCode: LRESULT;
    WantSpecial: Word;
    DialogKey: Word;
    constructor Create(AOwner: TComponent); override;
  published
    property TabStop;
  end;

  TShortForm = class(TForm)
  public
    function IsShortCut(var Message: TWMKey): Boolean; override;
  end;

  TLog = class
  public
    Text: string;
    procedure KeyDown(Sender: TObject; var Key: Word; Shift: TShiftState);
  end;

var
  Log: TLog;

constructor TKeyBox.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  SetBounds(0, 0, 40, 20);
  TabStop := True;
  OnKeyDown := Log.KeyDown;
end;

procedure TKeyBox.WndProc(var Message: TMessage);
begin
  case Message.Msg of
    WM_GETDLGCODE:
      Message.Result := DlgCode;
    CM_WANTSPECIALKEY:
      Message.Result := Ord((WantSpecial <> 0) and (Message.WParam = WantSpecial));
    CM_DIALOGKEY:
      if (DialogKey <> 0) and (Message.WParam = DialogKey) then
      begin
        Log.Text := Log.Text + 'dialogkey:' + Name + ';';
        Message.Result := 1;
      end;
  else
    inherited WndProc(Message);
  end;
end;

function TShortForm.IsShortCut(var Message: TWMKey): Boolean;
begin
  Result := Message.CharCode = VK_F5;
  if Result then
    Log.Text := Log.Text + 'shortcut;';
end;

procedure TLog.KeyDown(Sender: TObject; var Key: Word; Shift: TShiftState);
begin
  Text := Text + 'keydown:' + TComponent(Sender).Name + ':' + IntToStr(Key) + ';';
end;

function Box(const Name: string; Parent: TWinControl): TKeyBox;
begin
  Result := TKeyBox.Create(Parent);
  Result.Name := Name;
  Result.Parent := Parent;
end;

procedure Press(C: TWinControl; Key: Word);
begin
  Log.Text := '';
  PostMessageW(C.Handle, WM_KEYDOWN, Key, 1);
  Application.ProcessMessages;
end;

function ActiveName(F: TForm): string;
begin
  if F.ActiveControl = nil then
    Result := 'nil'
  else
    Result := F.ActiveControl.Name;
end;

procedure Run;
var
  F: TShortForm;
  B1, B2, B3, P, Q1, Q2: TKeyBox;
begin
  Application.Initialize;
  Log := TLog.Create;
  F := TShortForm.CreateNew(nil);
  try
    B1 := Box('B1', F);
    B2 := Box('B2', F);
    B3 := Box('B3', F);
    P := Box('P', F);
    P.TabStop := False;
    Q1 := Box('Q1', P);
    Q2 := Box('Q2', P);
    F.Show;
    B1.SetFocus;

    Section('Tab (CM_DIALOGKEY)');
    Press(B1, VK_TAB);
    CheckEqS('B2', ActiveName(F), 'Tab: next tab stop');
    CheckEqS('', Log.Text, 'consumed: no OnKeyDown');
    B3.SetFocus;
    Press(B3, VK_TAB);
    CheckEqS('Q1', ActiveName(F), 'Tab: into the panel (depth-first)');
    Press(Q1, VK_TAB);
    CheckEqS('Q2', ActiveName(F), 'Tab: inside the panel');
    Press(Q2, VK_TAB);
    CheckEqS('B1', ActiveName(F), 'Tab: wraps around');

    Section('arrows');
    Q1.SetFocus;
    Press(Q1, VK_RIGHT);
    CheckEqS('Q2', ActiveName(F), 'Right: next sibling');
    Press(Q2, VK_DOWN);
    CheckEqS('Q1', ActiveName(F), 'Down: siblings only, wrapping');
    Press(Q1, VK_LEFT);
    CheckEqS('Q2', ActiveName(F), 'Left: previous sibling, wrapping');
    B2.SetFocus;
    Press(B2, VK_UP);
    CheckEqS('B1', ActiveName(F), 'Up: previous sibling');
    CheckEqS('', Log.Text, 'consumed: no OnKeyDown');

    Section('special-key veto');
    B1.DlgCode := DLGC_WANTTAB;
    Press(B1, VK_TAB);
    CheckEqS('keydown:B1:9;', Log.Text, 'DLGC_WANTTAB: Tab delivered');
    CheckEqS('B1', ActiveName(F), 'focus kept');
    Press(B1, VK_RIGHT);
    CheckEqS('B2', ActiveName(F), 'DLGC_WANTTAB does not veto arrows');
    B1.SetFocus;
    B1.DlgCode := DLGC_WANTARROWS;
    Press(B1, VK_RIGHT);
    CheckEqS('keydown:B1:39;', Log.Text, 'DLGC_WANTARROWS: arrow delivered');
    Press(B1, VK_TAB);
    CheckEqS('B2', ActiveName(F), 'DLGC_WANTARROWS does not veto Tab');
    B1.SetFocus;
    B1.DlgCode := DLGC_WANTALLKEYS;
    Press(B1, VK_TAB);
    CheckEqS('keydown:B1:9;', Log.Text, 'DLGC_WANTALLKEYS: Tab delivered');
    Press(B1, VK_ESCAPE);
    CheckEqS('keydown:B1:27;', Log.Text, 'DLGC_WANTALLKEYS: Escape delivered');
    B1.DlgCode := 0;
    B1.WantSpecial := VK_RETURN;
    B3.DialogKey := VK_RETURN;
    Press(B1, VK_RETURN);
    CheckEqS('keydown:B1:13;', Log.Text, 'CM_WANTSPECIALKEY: Return delivered, not offered to the form');
    B1.WantSpecial := 0;

    Section('Return and Escape');
    Press(B1, VK_RETURN);
    CheckEqS('dialogkey:B3;', Log.Text, 'Return: a control of the form takes it, consumed');
    B3.DialogKey := 0;
    Press(B1, VK_RETURN);
    CheckEqS('keydown:B1:13;', Log.Text, 'Return: nobody takes it, delivered');
    Q2.DialogKey := VK_ESCAPE;
    Press(B1, VK_ESCAPE);
    CheckEqS('dialogkey:Q2;', Log.Text, 'Escape: offered to nested controls too');
    Q2.DialogKey := 0;

    Section('other keys and shortcuts');
    Press(B1, Ord('A'));
    CheckEqS('keydown:B1:65;', Log.Text, 'ordinary key delivered');
    Press(B1, VK_F5);
    CheckEqS('shortcut;', Log.Text, 'IsShortCut first, consumed');
  finally
    F.Free;
  end;
  Log.Free;
end;

begin
  Run;
  Done;
end.
