// covers: UIF-085
// needs: desktop
program Core_KeyEvents;
{ U-032, core.md 7.9: WM_KEYDOWN/WM_SYSKEYDOWN fire OnKeyDown, WM_KEYUP/WM_SYSKEYUP OnKeyUp, WM_CHAR OnKeyPress;
  Key := 0 consumes the key (no default processing) and swallows the following WM_CHAR (no OnKeyPress, nothing
  typed); a key that is not consumed re-arms nothing; #0 consumes a character and a changed character replaces it
  before default processing; ssAlt comes from bit 29 of LParam. The top of the parent chain previews keys first
  when its PreviewKeys hook says so (forms: KeyPreview) and may consume them. }
uses
  Windows, Messages, Types, SysUtils, Classes, Controls, TestKit;

type
  TLog = class
  public
    Text: string;
    ConsumeKey: Word;                                       { key the handlers set to 0 }
    ConsumeIn: TObject;                                     { only in this control's handler }
    procedure KeyDown(Sender: TObject; var Key: Word; Shift: TShiftState);
    procedure KeyUp(Sender: TObject; var Key: Word; Shift: TShiftState);
    procedure KeyPress(Sender: TObject; var Key: Char);
  end;

  TTestWin = class(TWinControl)
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TTopWin = class(TTestWin)
  protected
    procedure CreateParams(var Params: TCreateParams); override;
    function PreviewKeys: Boolean; override;
  public
    Preview: Boolean;
  end;

  TTestEdit = class(TTestWin)
  protected
    procedure CreateParams(var Params: TCreateParams); override;
  end;

var
  Log: TLog;

function ShiftText(Shift: TShiftState): string;
begin
  Result := '';
  if ssAlt in Shift then
    Result := 'a';
end;

procedure TLog.KeyDown(Sender: TObject; var Key: Word; Shift: TShiftState);
begin
  Text := Text + TComponent(Sender).Name + '.Down(' + IntToStr(Key) + ShiftText(Shift) + ');';
  if (Key = ConsumeKey) and (Sender = ConsumeIn) then
    Key := 0;
end;

procedure TLog.KeyUp(Sender: TObject; var Key: Word; Shift: TShiftState);
begin
  Text := Text + TComponent(Sender).Name + '.Up(' + IntToStr(Key) + ShiftText(Shift) + ');';
  if (Key = ConsumeKey) and (Sender = ConsumeIn) then
    Key := 0;
end;

procedure TLog.KeyPress(Sender: TObject; var Key: Char);
begin
  Text := Text + TComponent(Sender).Name + '.Press(' + Key + ');';
  if Sender = ConsumeIn then
    if Key = 'x' then
      Key := #0
    else if Key = 'a' then
      Key := 'b';
end;

constructor TTestWin.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  ControlStyle := ControlStyle + [csAcceptsControls];
  OnKeyDown := Log.KeyDown;
  OnKeyUp := Log.KeyUp;
  OnKeyPress := Log.KeyPress;
end;

procedure TTopWin.CreateParams(var Params: TCreateParams);
begin
  inherited CreateParams(Params);
  Params.Style := WS_POPUP or WS_CLIPCHILDREN;
  Params.WndParent := 0;
end;

function TTopWin.PreviewKeys: Boolean;
begin
  Result := Preview;
end;

procedure TTestEdit.CreateParams(var Params: TCreateParams);
begin
  inherited CreateParams(Params);
  Params.SubclassOf := 'EDIT';
end;

function Send(C: TWinControl; Msg: Cardinal; WParam: WPARAM; LParam: LPARAM): string;
begin
  Log.Text := '';
  SendMessageW(C.Handle, Msg, WParam, LParam);
  Result := Log.Text;
end;

function EditText(E: TWinControl): string;
var
  Buffer: array[0..63] of Char;
begin
  GetWindowTextW(E.Handle, @Buffer[0], 64);
  Result := string(PChar(@Buffer[0]));
end;

procedure Run;
var
  Root: TTopWin;
  W: TTestWin;
  E: TTestEdit;
begin
  Log := TLog.Create;
  Root := TTopWin.Create(nil);
  try
    Root.Name := 'Root';
    Root.SetBounds(100, 100, 300, 200);
    W := TTestWin.Create(Root);
    W.Name := 'W';
    W.SetBounds(10, 10, 50, 30);
    W.Parent := Root;
    E := TTestEdit.Create(Root);
    E.Name := 'E';
    E.SetBounds(10, 60, 120, 24);
    E.Parent := Root;
    Root.HandleNeeded;

    Section('events (UIF-085)');
    CheckEqS('W.Down(65);', Send(W, WM_KEYDOWN, 65, 1), 'WM_KEYDOWN fires OnKeyDown');
    CheckEqS('W.Up(65);', Send(W, WM_KEYUP, 65, LPARAM($C0000001)), 'WM_KEYUP fires OnKeyUp');
    CheckEqS('W.Press(q);', Send(W, WM_CHAR, Ord('q'), 1), 'WM_CHAR fires OnKeyPress');
    CheckEqS('W.Down(66a);', Send(W, WM_SYSKEYDOWN, 66, LPARAM($20000001)), 'WM_SYSKEYDOWN, ssAlt from bit 29');
    CheckEqS('W.Up(66a);', Send(W, WM_SYSKEYUP, 66, LPARAM($E0000001)), 'WM_SYSKEYUP');

    Section('character handling');
    Log.ConsumeIn := E;
    CheckEqS('E.Press(c);', Send(E, WM_CHAR, Ord('c'), 1), 'plain character');
    CheckEqS('c', EditText(E), 'typed by the native control');
    CheckEqS('E.Press(x);', Send(E, WM_CHAR, Ord('x'), 1), '#0 consumes');
    CheckEqS('c', EditText(E), 'nothing typed');
    CheckEqS('E.Press(a);', Send(E, WM_CHAR, Ord('a'), 1), 'changed character');
    CheckEqS('cb', EditText(E), 'replacement typed');

    Section('Key := 0 swallows the next WM_CHAR');
    Log.ConsumeKey := Ord('Q');
    CheckEqS('E.Down(81);', Send(E, WM_KEYDOWN, Ord('Q'), 1), 'key consumed');
    CheckEqS('', Send(E, WM_CHAR, Ord('q'), 1), 'following WM_CHAR swallowed: no OnKeyPress');
    CheckEqS('cb', EditText(E), 'nothing typed');
    CheckEqS('E.Press(d);', Send(E, WM_CHAR, Ord('d'), 1), 'only one character swallowed');
    CheckEqS('cbd', EditText(E), 'next character typed');
    CheckEqS('E.Down(81);', Send(E, WM_KEYDOWN, Ord('Q'), 1), 'consumed again');
    CheckEqS('E.Down(69);', Send(E, WM_KEYDOWN, Ord('E'), 1), 'a key that is not consumed');
    CheckEqS('E.Press(e);', Send(E, WM_CHAR, Ord('e'), 1), 'clears the pending swallow');
    CheckEqS('E.Up(81);', Send(E, WM_KEYUP, Ord('Q'), LPARAM($C0000001)), 'key up consumed too');

    Section('preview by the top of the chain');
    Root.Preview := True;
    Log.ConsumeIn := nil;
    CheckEqS('Root.Down(70);W.Down(70);', Send(W, WM_KEYDOWN, 70, 1), 'preview first');
    CheckEqS('Root.Press(f);W.Press(f);', Send(W, WM_CHAR, Ord('f'), 1), 'characters too');
    CheckEqS('Root.Up(70);W.Up(70);', Send(W, WM_KEYUP, 70, LPARAM($C0000001)), 'key up too');
    CheckEqS('Root.Down(71);', Send(Root, WM_KEYDOWN, 71, 1), 'the top itself: once');
    Log.ConsumeIn := Root;
    Log.ConsumeKey := 72;
    CheckEqS('Root.Down(72);', Send(W, WM_KEYDOWN, 72, 1), 'consumed by the preview');
    CheckEqS('', Send(W, WM_CHAR, Ord('h'), 1), 'its character swallowed');
    CheckEqS('Root.Press(x);', Send(W, WM_CHAR, Ord('x'), 1), 'character consumed by the preview');
    Root.Preview := False;
    Log.ConsumeIn := nil;
    CheckEqS('W.Down(70);', Send(W, WM_KEYDOWN, 70, 1), 'no preview when the hook says no');
  finally
    Root.Free;
    Log.Free;
  end;
end;

begin
  Run;
  Done;
end.
