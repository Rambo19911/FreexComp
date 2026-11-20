// covers: UIF-620, UIF-621, UIF-622, UIF-623, UIF-250
// needs: desktop
program Ctl_Edit;
{ U-054, controls-standard.md section 2: TEdit defaults (121 x 23, TabStop, AutoSize, clWindow); a sub-classed
  system EDIT with the listed style bits, each "-> recreate" property re-creating the window and keeping its text;
  state assigned before the handle (text, selection clamped to the text, limit, cue banner, modified flag) pushed
  natively; native changes read back through the properties; OnChange on EN_CHANGE for user edits and changing
  assignments only, never while loading or re-creating; AutoSelect; Enter/Escape not consumed (UIF-622); intrinsic
  height rules I2/I3 (layout.md UIF-250, E37); streaming of every published property kind; leak check.
  UIF-623 (no EM_SETMARGINS) has no observable assertion beyond the native default. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, TestKit;

type
  TKeyBox = class(TCustomControl)
  protected
    procedure WndProc(var Message: TMessage); override;
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TLog = class
  public
    Text: string;
    procedure Changed(Sender: TObject);
  end;

  TEditForm = class(TForm)
  published
    Auto: TEdit;
    Other: TEdit;
    procedure OtherChange(Sender: TObject);
  end;

  TGetGuiResources = function(hProcess: THandle; uiFlags: DWORD): DWORD; stdcall;

{$FORM Ctl_Edit.form}

const
  GR_GDIOBJECTS = 0;
  GR_USEROBJECTS = 1;

var
  Log: TLog;

constructor TKeyBox.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  SetBounds(200, 0, 20, 20);
end;

procedure TKeyBox.WndProc(var Message: TMessage);
begin
  if Message.Msg = CM_DIALOGKEY then
  begin
    Log.Text := Log.Text + 'dialogkey:' + IntToStr(Message.WParam) + ';';
    Message.Result := 1;
  end
  else
    inherited WndProc(Message);
end;

procedure TLog.Changed(Sender: TObject);
begin
  Text := Text + 'change;';
end;

procedure TEditForm.OtherChange(Sender: TObject);
begin
  Log.Text := Log.Text + 'streamed change;';
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

function NativeText(H: HWND): string;
var
  Buf: array[0..127] of WideChar;
  N: Integer;
begin
  N := Integer(SendMessageW(H, WM_GETTEXT, 128, LPARAM(@Buf[0])));
  SetLength(Result, N);
  if N > 0 then
    Move(Buf[0], PChar(Result)^, N * SizeOf(Char));
end;

function CueBanner(H: HWND): string;
var
  Buf: array[0..127] of WideChar;
begin
  FillChar(Buf, SizeOf(Buf), 0);
  SendMessageW(H, EM_GETCUEBANNER, WPARAM(@Buf[0]), 128);
  Result := string(PChar(@Buf[0]));
end;

function Style(E: TWinControl): NativeInt;
begin
  Result := GetWindowLongPtrW(E.Handle, GWL_STYLE);
end;

function HasStyle(E: TWinControl; Bits: NativeInt): Boolean;
begin
  Result := (Style(E) and Bits) = Bits;
end;

function NativeSel(E: TWinControl): string;
var
  A, B: DWORD;
begin
  A := 0;
  B := 0;
  SendMessageW(E.Handle, EM_GETSEL, WPARAM(@A), LPARAM(@B));
  Result := Format('%d..%d', [A, B]);
end;

function BoundsText(C: TControl): string;
begin
  Result := Format('%d,%d,%d,%d', [C.Left, C.Top, C.Width, C.Height]);
end;

{ GDI + USER objects of the process (GetGuiResources is not imported by Windows: bound at run time) }
function GuiObjects: Integer;
var
  GetGuiResources: TGetGuiResources;
begin
  GetGuiResources := TGetGuiResources(GetProcAddress(GetModuleHandleW('user32.dll'), 'GetGuiResources'));
  Result := Integer(GetGuiResources(GetCurrentProcess, GR_GDIOBJECTS))
    + Integer(GetGuiResources(GetCurrentProcess, GR_USEROBJECTS));
end;

procedure Press(C: TWinControl; Key: Word);
begin
  PostMessageW(C.Handle, WM_KEYDOWN, Key, 1);
  Application.ProcessMessages;
end;

procedure Run;
var
  F: TForm;
  E: TEdit;
  K: TKeyBox;
  SF: TEditForm;
  H: HWND;
  I, Before, S11, S3: Integer;
begin
  Application.Initialize;
  Log := TLog.Create;
  F := TForm.CreateNew(nil);
  try
    F.ClientWidth := 400;
    F.ClientHeight := 300;
    E := TEdit.Create(F);

    Section('defaults');
    CheckEqI(121, E.Width, 'Width 121');
    CheckEqI(23, E.Height, 'Height 23 (rule I2 at 96 DPI, font -12)');
    CheckEqB(True, E.Alignment = taLeftJustify, 'Alignment');
    CheckEqB(True, E.AutoSelect, 'AutoSelect');
    CheckEqB(True, E.AutoSize, 'AutoSize');
    CheckEqB(True, E.BorderStyle = bsSingle, 'BorderStyle bsSingle');
    CheckEqB(True, E.CharCase = ecNormal, 'CharCase');
    CheckEqB(True, E.Color = clWindow, 'Color clWindow');
    CheckEqB(True, E.HideSelection, 'HideSelection');
    CheckEqI(0, E.MaxLength, 'MaxLength');
    CheckEqB(False, E.NumbersOnly, 'NumbersOnly');
    CheckEqB(False, E.ParentColor, 'ParentColor False');
    CheckEqB(True, E.ParentFont, 'ParentFont');
    CheckEqB(True, E.PasswordChar = #0, 'PasswordChar #0');
    CheckEqB(False, E.ReadOnly, 'ReadOnly');
    CheckEqI(-1, E.TabOrder, 'TabOrder');
    CheckEqB(True, E.TabStop, 'TabStop True');
    CheckEqS('', E.Text, 'Text');
    CheckEqS('', E.TextHint, 'TextHint');
    Check(GetClass('TEdit') <> nil, 'registered');

    Section('handle-less state (UIF-621)');
    E.Text := 'abcdef';
    E.SelStart := 1;
    E.SelLength := 3;
    CheckEqI(1, E.SelStart, 'SelStart field');
    CheckEqI(3, E.SelLength, 'SelLength field');
    CheckEqS('bcd', E.SelText, 'SelText from the fields');
    E.SelStart := 10;
    CheckEqI(6, E.SelStart, 'SelStart clamped to the text length');
    E.SelStart := 2;
    E.SelLength := 10;
    CheckEqI(4, E.SelLength, 'SelLength clamped to the text length');
    E.SelLength := 2;
    E.MaxLength := 20;
    E.TextHint := 'hint';
    E.Modified := True;
    CheckEqB(True, E.Modified, 'Modified field');
    CheckEqB(False, E.CanUndo, 'CanUndo without a handle');
    E.Parent := F;
    H := E.Handle;
    CheckEqS('Edit', ClassOf(H), 'system class EDIT');
    CheckEqS('abcdef', NativeText(H), 'text pushed');
    CheckEqS('2..4', NativeSel(E), 'selection pushed');
    CheckEqI(20, SendMessageW(H, EM_GETLIMITTEXT, 0, 0), 'MaxLength pushed (EM_LIMITTEXT)');
    CheckEqS('hint', CueBanner(H), 'TextHint pushed (EM_SETCUEBANNER)');
    CheckEqB(True, SendMessageW(H, EM_GETMODIFY, 0, 0) <> 0, 'Modified pushed');

    Section('window');
    CheckEqB(True, HasStyle(E, ES_AUTOHSCROLL), 'ES_AUTOHSCROLL');
    CheckEqB(True, HasStyle(E, WS_TABSTOP), 'WS_TABSTOP');
    CheckEqB(True, (Style(E) and (ES_CENTER or ES_RIGHT)) = 0, 'ES_LEFT');
    CheckEqB(True, (GetWindowLongPtrW(H, GWL_EXSTYLE) and WS_EX_CLIENTEDGE) <> 0, 'WS_EX_CLIENTEDGE');
    CheckEqB(False, HasStyle(E, ES_NOHIDESEL), 'no ES_NOHIDESEL');
    Log.Text := '';
    E.OnChange := Log.Changed;
    E.Alignment := taCenter;
    Check(E.Handle <> H, 'Alignment recreates');
    CheckEqB(True, HasStyle(E, ES_CENTER), 'ES_CENTER');
    CheckEqS('abcdef', NativeText(E.Handle), 'text kept');
    CheckEqS('2..4', NativeSel(E), 'selection kept');
    E.Alignment := taRightJustify;
    CheckEqB(True, HasStyle(E, ES_RIGHT), 'ES_RIGHT');
    E.Alignment := taLeftJustify;
    H := E.Handle;
    E.BorderStyle := bsNone;
    Check(E.Handle <> H, 'BorderStyle recreates');
    CheckEqB(True, (GetWindowLongPtrW(E.Handle, GWL_EXSTYLE) and WS_EX_CLIENTEDGE) = 0, 'bsNone: no WS_EX_CLIENTEDGE');
    E.BorderStyle := bsSingle;
    H := E.Handle;
    E.HideSelection := False;
    Check(E.Handle <> H, 'HideSelection recreates');
    CheckEqB(True, HasStyle(E, ES_NOHIDESEL), 'ES_NOHIDESEL');
    E.HideSelection := True;
    H := E.Handle;
    E.NumbersOnly := True;
    Check(E.Handle <> H, 'NumbersOnly recreates');
    CheckEqB(True, HasStyle(E, ES_NUMBER), 'ES_NUMBER');
    E.NumbersOnly := False;
    CheckEqS('', Log.Text, 'no OnChange while re-creating');
    H := E.Handle;
    E.PasswordChar := '*';
    Check(E.Handle <> H, 'PasswordChar #0 -> * recreates');
    CheckEqB(True, HasStyle(E, ES_PASSWORD), 'ES_PASSWORD');
    CheckEqI(Ord('*'), SendMessageW(E.Handle, EM_GETPASSWORDCHAR, 0, 0), 'EM_SETPASSWORDCHAR *');
    H := E.Handle;
    E.PasswordChar := '#';
    Check(E.Handle = H, 'PasswordChar * -> # keeps the window');
    CheckEqI(Ord('#'), SendMessageW(E.Handle, EM_GETPASSWORDCHAR, 0, 0), 'EM_SETPASSWORDCHAR #');
    E.PasswordChar := #0;
    CheckEqB(False, HasStyle(E, ES_PASSWORD), 'PasswordChar #0: no ES_PASSWORD');
    H := E.Handle;
    E.ReadOnly := True;
    Check(E.Handle = H, 'ReadOnly keeps the window');
    CheckEqB(True, HasStyle(E, ES_READONLY), 'EM_SETREADONLY');
    E.ReadOnly := False;
    CheckEqB(False, HasStyle(E, ES_READONLY), 'EM_SETREADONLY False');
    E.MaxLength := 3;
    CheckEqI(3, SendMessageW(E.Handle, EM_GETLIMITTEXT, 0, 0), 'MaxLength with a handle');
    E.MaxLength := 0;
    E.TextHint := 'other';
    CheckEqS('other', CueBanner(E.Handle), 'TextHint with a handle');
    H := E.Handle;
    E.CharCase := ecUpperCase;
    Check(E.Handle <> H, 'CharCase recreates');
    CheckEqB(True, HasStyle(E, ES_UPPERCASE), 'ES_UPPERCASE');
    CheckEqS('ABCDEF', E.Text, 'CharCase converts the text');
    E.CharCase := ecLowerCase;
    CheckEqB(True, HasStyle(E, ES_LOWERCASE), 'ES_LOWERCASE');
    CheckEqS('abcdef', E.Text, 'lower case');
    E.CharCase := ecNormal;

    Section('native -> object');
    SetWindowTextW(E.Handle, 'native');
    CheckEqS('native', E.Text, 'Text reads the window');
    SendMessageW(E.Handle, EM_SETSEL, 1, 4);
    CheckEqI(1, E.SelStart, 'SelStart from EM_GETSEL');
    CheckEqI(3, E.SelLength, 'SelLength from EM_GETSEL');
    CheckEqS('ati', E.SelText, 'SelText');
    SendMessageW(E.Handle, EM_SETMODIFY, 1, 0);
    CheckEqB(True, E.Modified, 'Modified from EM_GETMODIFY');
    E.Modified := False;
    CheckEqB(False, SendMessageW(E.Handle, EM_GETMODIFY, 0, 0) <> 0, 'Modified := False');
    E.SelText := 'XY';
    CheckEqS('nXYve', E.Text, 'SelText replaces (EM_REPLACESEL)');
    CheckEqB(True, E.CanUndo, 'CanUndo after EM_REPLACESEL');
    E.Undo;
    CheckEqS('native', E.Text, 'Undo');
    E.SelectAll;
    CheckEqS('0..6', NativeSel(E), 'SelectAll');
    SendMessageW(E.Handle, EM_SETSEL, 0, 2);
    E.ClearSelection;
    CheckEqS('tive', E.Text, 'ClearSelection');
    SendMessageW(E.Handle, EM_SETSEL, 1, 3);
    E.DestroyHandle;
    CheckEqS('tive', E.Text, 'text saved when the window goes');
    CheckEqI(1, E.SelStart, 'SelStart saved');
    CheckEqI(2, E.SelLength, 'SelLength saved');
    E.SelText := 'Q';
    CheckEqS('tQe', E.Text, 'SelText without a handle');
    CheckEqI(2, E.SelStart, 'caret after the inserted text');
    E.HandleNeeded;
    CheckEqS('2..2', NativeSel(E), 'selection pushed again');

    Section('OnChange (UIF-620)');
    Log.Text := '';
    E.Text := 'one';
    CheckEqS('change;', Log.Text, 'assignment that changes the text');
    Log.Text := '';
    E.Text := 'one';
    CheckEqS('', Log.Text, 'same text: no OnChange');
    SendMessageW(E.Handle, EM_SETSEL, 3, 3);
    SendMessageW(E.Handle, WM_CHAR, Ord('z'), 1);
    CheckEqS('onez', E.Text, 'typed character');
    CheckEqS('change;', Log.Text, 'user edit fires OnChange');
    Log.Text := '';
    E.Clear;
    CheckEqS('', E.Text, 'Clear');
    CheckEqS('change;', Log.Text, 'Clear fires OnChange');
    E.DestroyHandle;
    Log.Text := '';
    E.Text := 'handle-less';
    CheckEqS('change;', Log.Text, 'assignment without a handle');
    Log.Text := '';
    E.HandleNeeded;
    CheckEqS('', Log.Text, 'no OnChange in CreateWnd');
    E.OnChange := nil;

    Section('AutoSelect, Enter and Escape (UIF-622)');
    K := TKeyBox.Create(F);
    K.Parent := F;
    F.Show;
    E.Text := 'select me';
    E.SelStart := 0;
    K.SetFocus;
    E.SetFocus;
    CheckEqS('0..9', NativeSel(E), 'AutoSelect: all selected on entry');
    E.AutoSelect := False;
    E.SelStart := 2;
    K.SetFocus;
    E.SetFocus;
    CheckEqS('2..2', NativeSel(E), 'AutoSelect False: selection kept');
    Log.Text := '';
    Press(E, VK_RETURN);
    CheckEqS('dialogkey:13;', Log.Text, 'Enter reaches the form''s dialog keys');
    Log.Text := '';
    Press(E, VK_ESCAPE);
    CheckEqS('dialogkey:27;', Log.Text, 'Escape reaches the form''s dialog keys');

    Section('intrinsic height (layout.md UIF-250, E37)');
    E.Free;
    E := TEdit.Create(nil);
    try
      E.Font.Height := -16;
      E.SetBounds(10, 10, 121, 40);
      CheckEqS('10,10,121,27', BoundsText(E), 'E37: rule I2, 16 + 11');
      E.Font.Height := -20;
      CheckEqI(31, E.Height, 'font change re-applies I2');
      E.BorderStyle := bsNone;
      CheckEqI(23, E.Height, 'rule I3: 20 + 3');
      E.AutoSize := False;
      E.Height := 40;
      CheckEqI(40, E.Height, 'AutoSize False: no intrinsic height');
      E.AutoSize := True;
      CheckEqI(23, E.Height, 'AutoSize True re-applies I3');
      E.BorderStyle := bsSingle;
      E.Font.Height := -12;
      E.Parent := F;
      S11 := LayoutMulDiv(11, F.PixelsPerInch, 96);
      S3 := LayoutMulDiv(3, F.PixelsPerInch, 96);
      CheckEqI(12 + S11, E.Height, 'on a form: s(11) at the form''s DPI');
      E.BorderStyle := bsNone;
      CheckEqI(12 + S3, E.Height, 'on a form: s(3) at the form''s DPI');
    finally
      E.Free;
    end;

    Section('streaming');
    Log.Text := '';
    SF := TEditForm.Create(nil);
    try
      CheckEqS('', Log.Text, 'no OnChange while loading');
      S11 := LayoutMulDiv(11, SF.PixelsPerInch, 96);
      S3 := LayoutMulDiv(3, SF.PixelsPerInch, 96);
      CheckEqS(Format('10,10,121,%d', [16 + S11]), BoundsText(SF.Auto), 'E37 loaded: stored height replaced');
      CheckEqI(-16, SF.Auto.Font.Height, 'Font.Height');
      CheckEqB(False, SF.Auto.ParentFont, 'ParentFont False');
      CheckEqB(True, SF.Auto.Alignment = taRightJustify, 'Alignment');
      CheckEqB(False, SF.Auto.AutoSelect, 'AutoSelect');
      CheckEqB(False, SF.Other.AutoSize, 'AutoSize False');
      CheckEqI(40, SF.Other.Height, 'AutoSize False: stored height kept');
      CheckEqB(True, SF.Other.BorderStyle = bsNone, 'BorderStyle');
      CheckEqB(True, SF.Other.CharCase = ecLowerCase, 'CharCase');
      CheckEqB(True, SF.Other.Color = clYellow, 'Color');
      CheckEqB(False, SF.Other.HideSelection, 'HideSelection');
      CheckEqI(8, SF.Other.MaxLength, 'MaxLength');
      CheckEqB(True, SF.Other.NumbersOnly, 'NumbersOnly');
      CheckEqB(True, SF.Other.PasswordChar = '*', 'PasswordChar');
      CheckEqB(True, SF.Other.ReadOnly, 'ReadOnly');
      CheckEqB(False, SF.Other.TabStop, 'TabStop False');
      CheckEqS('abc', SF.Other.Text, 'Text');
      CheckEqS('hint', SF.Other.TextHint, 'TextHint');
      Check(Assigned(SF.Other.OnChange), 'OnChange bound');
      SF.Other.Text := 'def';
      CheckEqS('streamed change;', Log.Text, 'OnChange handler of the form');
    finally
      SF.Free;
    end;

    Section('leak');
    Before := GuiObjects;
    for I := 1 to 100 do
    begin
      E := TEdit.Create(nil);
      E.Text := 'Leak';
      E.Parent := F;
      E.HandleNeeded;
      E.Free;
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
