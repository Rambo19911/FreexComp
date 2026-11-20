// covers: UIF-013, UIF-470, UIF-475, UIF-476, UIF-477, UIF-478
// needs: desktop
program Dlg_MessageBoxes;
{ U-080, application-forms.md 9: Application.MessageBox, ShowMessage, ShowMessageFmt, MessageDlg.
  Non-interactive mode (the harness sets FXC_UI_NONINTERACTIVE=1): no window; one stderr line per call
  ('[MessageBox] <caption>: <text>', '[ShowMessage] <text>', '[MessageDlg] <text>', line breaks as \n; checked by
  Dlg_MessageBoxes.err.txt) and the default button's result (UIF-476).
  The interactive path (owner window, MB_TASKMODAL, the MessageDlg flag and caption mapping) is observed through
  Forms.MessageBoxHook, which replaces the Win32 box (and the stderr line) while assigned, so no real blocking
  box is ever shown. }
uses
  Windows, Messages, Types, SysUtils, Classes, Controls, Forms, Dialogs, UIConsts, TestKit;

var
  HookWnd: HWND;
  HookText, HookCaption: string;
  HookFlags: Longint;
  HookResult: Integer;
  HookCalls: Integer;

function TestHook(Wnd: HWND; Text, Caption: PChar; Flags: Longint): Integer;
begin
  Inc(HookCalls);
  HookWnd := Wnd;
  HookText := Text;
  HookCaption := Caption;
  HookFlags := Flags;
  Result := HookResult;
end;

procedure NonInteractive;
var
  Msg: string;
begin
  Section('non-interactive default results (UIF-470, UIF-476)');
  CheckEqB(True, Application.NonInteractive, 'NonInteractive (FXC_UI_NONINTERACTIVE=1)');
  CheckEqI(IDOK, Application.MessageBox('first line'#13#10'second line', 'Box caption', MB_OK), 'MB_OK: OK');
  CheckEqI(IDOK, Application.MessageBox('t', 'c', MB_OKCANCEL), 'MB_OKCANCEL: OK');
  CheckEqI(IDCANCEL, Application.MessageBox('t', 'c', MB_OKCANCEL or MB_DEFBUTTON2), 'MB_OKCANCEL, 2nd: Cancel');
  CheckEqI(IDABORT, Application.MessageBox('t', 'c', MB_ABORTRETRYIGNORE), 'MB_ABORTRETRYIGNORE: Abort');
  CheckEqI(IDRETRY, Application.MessageBox('t', 'c', MB_ABORTRETRYIGNORE or MB_DEFBUTTON2), '2nd: Retry');
  CheckEqI(IDIGNORE, Application.MessageBox('t', 'c', MB_ABORTRETRYIGNORE or MB_DEFBUTTON3), '3rd: Ignore');
  CheckEqI(IDYES, Application.MessageBox('t', 'c', MB_YESNOCANCEL), 'MB_YESNOCANCEL: Yes');
  CheckEqI(IDNO, Application.MessageBox('t', 'c', MB_YESNOCANCEL or MB_DEFBUTTON2), '2nd: No');
  CheckEqI(IDCANCEL, Application.MessageBox('t', 'c', MB_YESNOCANCEL or MB_DEFBUTTON3), '3rd: Cancel');
  CheckEqI(IDYES, Application.MessageBox('t', 'c', MB_YESNO or MB_ICONQUESTION), 'MB_YESNO: Yes (icon ignored)');
  CheckEqI(IDNO, Application.MessageBox('t', 'c', MB_YESNO or MB_DEFBUTTON2), 'MB_YESNO, 2nd: No');
  CheckEqI(IDRETRY, Application.MessageBox('t', 'c', MB_RETRYCANCEL), 'MB_RETRYCANCEL: Retry');
  CheckEqI(IDCANCEL, Application.MessageBox('t', 'c', MB_RETRYCANCEL or MB_DEFBUTTON2), '2nd: Cancel');
  CheckEqI(IDCANCEL, Application.MessageBox('t', 'c', MB_CANCELTRYCONTINUE), 'MB_CANCELTRYCONTINUE: Cancel');
  CheckEqI(IDTRYAGAIN, Application.MessageBox('t', 'c', MB_CANCELTRYCONTINUE or MB_DEFBUTTON2), '2nd: Try Again');
  CheckEqI(IDCONTINUE, Application.MessageBox('t', 'c', MB_CANCELTRYCONTINUE or MB_DEFBUTTON3), '3rd: Continue');
  CheckEqI(IDOK, Application.MessageBox('t', 'c', MB_OK or MB_DEFBUTTON4), 'default beyond the buttons: first');

  Section('ShowMessage and MessageDlg, non-interactive (UIF-470, UIF-477, UIF-478)');
  ShowMessage('hello world');
  ShowMessageFmt('%d items', [3]);
  CheckEqI(mrOk, MessageDlg('plain', mtInformation, [mbOK], 0), '[mbOK]: mrOk');
  CheckEqI(mrOk, MessageDlg('empty set', mtWarning, [], 0), '[]: mrOk');
  CheckEqI(mrOk, MessageDlg('close', mtError, [mbClose], 0), '[mbClose]: mrOk');
  CheckEqI(mrOk, MessageDlg('ok cancel', mtConfirmation, mbOKCancel, 0), 'mbOKCancel: mrOk');
  CheckEqI(mrYes, MessageDlg('yes no', mtConfirmation, mbYesNo, 0), 'mbYesNo: mrYes');
  CheckEqI(mrYes, MessageDlg('ync', mtConfirmation, mbYesNoCancel, 0), 'mbYesNoCancel: mrYes');
  CheckEqI(mrRetry, MessageDlg('rc', mtError, [mbRetry, mbCancel], 0), '[mbRetry, mbCancel]: mrRetry');
  CheckEqI(mrAbort, MessageDlg('ari', mtError, mbAbortRetryIgnore, 0), 'mbAbortRetryIgnore: mrAbort');
  CheckEqI(mrYes, MessageDlg('help'#10'two', mtCustom, [mbYes, mbNo, mbHelp], 0), 'mbHelp removed: mrYes');
  Msg := '';
  try
    MessageDlg('bad', mtWarning, [mbYes], 0);
  except
    on E: EInvalidOperation do
      Msg := E.Message;
  end;
  CheckEqS(SUnsupportedButtons, Msg, 'other sets: EInvalidOperation');
  Msg := '';
  try
    MessageDlg('bad', mtWarning, [mbYes, mbNo, mbAll], 0);
  except
    on E: EInvalidOperation do
      Msg := E.Message;
  end;
  CheckEqS('Unsupported button combination', Msg, '[mbYes, mbNo, mbAll]: unsupported');
  CheckEqB(True, mbYesNo = [mbYes, mbNo], 'constant mbYesNo');
  CheckEqB(True, mbYesNoCancel = [mbYes, mbNo, mbCancel], 'constant mbYesNoCancel');
  CheckEqB(True, mbOKCancel = [mbOK, mbCancel], 'constant mbOKCancel');
  CheckEqB(True, mbAbortRetryIgnore = [mbAbort, mbRetry, mbIgnore], 'constant mbAbortRetryIgnore');
end;

procedure Hooked;
var
  F: TForm;
begin
  Section('Application.MessageBox through the hook (UIF-475)');
  MessageBoxHook := TestHook;
  HookResult := IDNO;
  HookCalls := 0;
  CheckEqI(IDNO, Application.MessageBox('body', 'title', MB_YESNO or MB_ICONQUESTION), 'the box''s result');
  CheckEqI(1, HookCalls, 'one box');
  CheckEqS('body', HookText, 'text');
  CheckEqS('title', HookCaption, 'caption');
  CheckEqB(True, Screen.ActiveForm = nil, 'no active form');
  CheckEqI(0, HookWnd, 'no active form: owner 0');
  CheckEqI(MB_YESNO or MB_ICONQUESTION or MB_TASKMODAL, HookFlags, 'no active form: MB_TASKMODAL added');
  F := TForm.CreateNew(nil);
  try
    F.SetBounds(50, 50, 200, 100);
    F.Visible := True;
    F.Perform(WM_ACTIVATE, WA_ACTIVE, 0);
    CheckEqB(True, Screen.ActiveForm = F, 'active form');
    Application.MessageBox('body', 'title', MB_OK);
    CheckEqB(True, HookWnd = F.Handle, 'visible active form: its handle is the owner');
    CheckEqI(MB_OK, HookFlags, 'flags unchanged');
    F.Visible := False;
    Application.MessageBox('body', 'title', MB_OK);
    CheckEqI(0, HookWnd, 'hidden active form: owner 0');
    CheckEqI(MB_TASKMODAL, HookFlags, 'hidden active form: MB_TASKMODAL');
    F.Perform(WM_ACTIVATE, WA_INACTIVE, 0);
  finally
    F.Free;
  end;

  Section('ShowMessage through the hook (UIF-477)');
  Application.Title := 'My App';
  HookResult := IDOK;
  ShowMessage('shown');
  CheckEqS('shown', HookText, 'text');
  CheckEqS('My App', HookCaption, 'caption Application.Title');
  CheckEqI(MB_OK or MB_TASKMODAL, HookFlags, 'MB_OK');
  ShowMessageFmt('%s=%d', ['x', 5]);
  CheckEqS('x=5', HookText, 'ShowMessageFmt formats');

  Section('MessageDlg mapping through the hook (UIF-478)');
  HookResult := IDCANCEL;
  CheckEqI(mrCancel, MessageDlg('w', mtWarning, mbOKCancel, 0), 'result = Win32 id = mr constant');
  CheckEqS('w', HookText, 'text');
  CheckEqS(SMsgDlgWarning, HookCaption, 'mtWarning: caption Warning');
  CheckEqI(MB_OKCANCEL or MB_ICONWARNING or MB_TASKMODAL, HookFlags, 'mtWarning, OK/Cancel');
  MessageDlg('e', mtError, [mbRetry, mbCancel], 0);
  CheckEqS('Error', HookCaption, 'mtError: caption Error');
  CheckEqI(MB_RETRYCANCEL or MB_ICONERROR or MB_TASKMODAL, HookFlags, 'mtError, Retry/Cancel');
  MessageDlg('i', mtInformation, [mbOK], 0);
  CheckEqS('Information', HookCaption, 'mtInformation: caption Information');
  CheckEqI(MB_OK or MB_ICONINFORMATION or MB_TASKMODAL, HookFlags, 'mtInformation, OK');
  MessageDlg('q', mtConfirmation, mbYesNoCancel, 0);
  CheckEqS('Confirm', HookCaption, 'mtConfirmation: caption Confirm');
  CheckEqI(MB_YESNOCANCEL or MB_ICONQUESTION or MB_TASKMODAL, HookFlags, 'mtConfirmation, Yes/No/Cancel');
  MessageDlg('c', mtCustom, [mbYes, mbNo, mbHelp], 0);
  CheckEqS('My App', HookCaption, 'mtCustom: caption Application.Title');
  CheckEqI(MB_YESNO or MB_TASKMODAL, HookFlags, 'mtCustom: no icon; mbHelp removed');
  MessageDlg('a', mtWarning, mbAbortRetryIgnore, 0);
  CheckEqI(MB_ABORTRETRYIGNORE or MB_ICONWARNING or MB_TASKMODAL, HookFlags, 'Abort/Retry/Ignore');
  MessageDlg('k', mtWarning, [mbClose], 0);
  CheckEqI(MB_OK or MB_ICONWARNING or MB_TASKMODAL, HookFlags, '[mbClose]: MB_OK');
  HookCalls := 0;
  try
    MessageDlg('bad', mtWarning, [mbNo], 0);
  except
    on E: EInvalidOperation do
      ;
  end;
  CheckEqI(0, HookCalls, 'unsupported set: no box');
  MessageBoxHook := nil;
end;

begin
  Application.Initialize;
  NonInteractive;
  Hooked;
  Done;
end.
