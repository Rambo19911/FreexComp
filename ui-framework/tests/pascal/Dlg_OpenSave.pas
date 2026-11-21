// covers: UIF-850, UIF-851, UIF-852, UIF-860, UIF-861, UIF-862, UIF-863, UIF-864
// needs: desktop
program Dlg_OpenSave;
{ U-093, controls-standard.md 28 and 28.1: TCommonDialog, TOpenDialog, TSaveDialog.
  Non-interactive mode (the harness sets FXC_UI_NONINTERACTIVE=1): Execute shows nothing, writes
  '[Dialog] <ClassName>.Execute' to stderr (checked by Dlg_OpenSave.err.txt) and returns False.
  Everything else is observed through Dialogs.CommonDialogHook, which replaces the comdlg32 call (and the stderr line)
  while assigned: it receives the prepared OPENFILENAMEW, records it, may write a result into it as the system dialog
  would, and returns the result and the extended error. No real dialog window is ever opened. }
uses
  Windows, Messages, Types, SysUtils, Classes, Controls, Forms, CommDlg, Dialogs, UIConsts, TestKit;

type
  TDlgForm = class(TForm)
  published
    OpenDialog1: TOpenDialog;
    SaveDialog1: TSaveDialog;
  end;

{$FORM Dlg_OpenSave.form}

var
  { what the hook saw }
  HookCalls: Integer;
  HookClass: string;
  HookOwner: HWND;
  HookSize: DWORD;
  HookStructOwner: HWND;
  HookFilter, HookFile, HookInitialDir, HookTitle, HookDefExt: string;
  HookFilterIndex, HookMaxFile, HookFlags: DWORD;
  HookOtherEnabled: Boolean;
  { what the hook answers }
  AnswerOk: Boolean;
  AnswerError: DWORD;
  AnswerFile: string;                                       { '|' stands for #0 }
  AnswerFilterIndex: DWORD;
  AnswerFlags: DWORD;                                       { the output flags on success }
  OtherForm: TForm;

{ the multi-string up to and including its double terminator, each #0 shown as '|'; '(nil)' for nil }
function MultiText(P: PWideChar): string;
begin
  if P = nil then
  begin
    Result := '(nil)';
    Exit;
  end;
  Result := '';
  repeat
    while P^ <> #0 do
    begin
      Result := Result + P^;
      Inc(P);
    end;
    Result := Result + '|';
    Inc(P);
  until P^ = #0;
  Result := Result + '|';
end;

function Str(P: PWideChar): string;
begin
  if P = nil then
    Result := '(nil)'
  else
    Result := P;
end;

function TestHook(Dialog: TCommonDialog; ParentWnd: HWND; Data: Pointer; var ExtendedError: DWORD): Boolean;
var
  O: POpenFilenameW;
  I: Integer;
begin
  Inc(HookCalls);
  HookClass := Dialog.ClassName;
  HookOwner := ParentWnd;
  O := POpenFilenameW(Data);
  HookSize := O^.lStructSize;
  HookStructOwner := O^.hwndOwner;
  HookFilter := MultiText(O^.lpstrFilter);
  HookFile := Str(O^.lpstrFile);
  HookMaxFile := O^.nMaxFile;
  HookInitialDir := Str(O^.lpstrInitialDir);
  HookTitle := Str(O^.lpstrTitle);
  HookDefExt := Str(O^.lpstrDefExt);
  HookFilterIndex := O^.nFilterIndex;
  HookFlags := O^.Flags;
  HookOtherEnabled := (OtherForm <> nil) and IsWindowEnabled(OtherForm.Handle);
  Result := AnswerOk;
  ExtendedError := AnswerError;
  if AnswerOk then
  begin
    for I := 1 to Length(AnswerFile) do
      if AnswerFile[I] = '|' then
        O^.lpstrFile[I - 1] := #0
      else
        O^.lpstrFile[I - 1] := AnswerFile[I];
    O^.lpstrFile[Length(AnswerFile)] := #0;
    O^.lpstrFile[Length(AnswerFile) + 1] := #0;
    O^.nFilterIndex := AnswerFilterIndex;
    O^.Flags := (O^.Flags and not DWORD(OFN_READONLY or OFN_EXTENSIONDIFFERENT)) or AnswerFlags;
  end;
end;

procedure Answer(Ok: Boolean; const AFile: string; AFilterIndex, AFlags, AError: DWORD);
begin
  AnswerOk := Ok;
  AnswerFile := AFile;
  AnswerFilterIndex := AFilterIndex;
  AnswerFlags := AFlags;
  AnswerError := AError;
  HookCalls := 0;
end;

function FilesText(L: TStrings): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to L.Count - 1 do
    Result := Result + L[I] + ';';
end;

const
  BaseFlags = OFN_HIDEREADONLY or OFN_ENABLESIZING or OFN_EXPLORER or OFN_NOCHANGEDIR;

var
  F: TForm;
  O: TOpenDialog;
  S: TSaveDialog;
  SF: TDlgForm;
  Expected: HWND;
  Msg: string;
begin
  Application.Initialize;
  F := TForm.CreateNew(nil);
  OtherForm := TForm.CreateNew(nil);
  try
    O := TOpenDialog.Create(F);
    S := TSaveDialog.Create(F);

    Section('defaults (28.1)');
    CheckEqS('', O.DefaultExt, 'DefaultExt');
    CheckEqS('', O.FileName, 'FileName');
    CheckEqS('', O.Filter, 'Filter');
    CheckEqI(1, O.FilterIndex, 'FilterIndex 1');
    CheckEqS('', O.InitialDir, 'InitialDir');
    CheckEqB(True, O.Options = [ofHideReadOnly, ofEnableSizing], 'Options [ofHideReadOnly, ofEnableSizing]');
    CheckEqS('', O.Title, 'Title');
    CheckEqI(0, O.Files.Count, 'no Files');
    Check(O is TCommonDialog, 'a TCommonDialog');
    Check(S is TOpenDialog, 'TSaveDialog is a TOpenDialog');
    CheckEqB(True, S.Options = [ofHideReadOnly, ofEnableSizing], 'TSaveDialog: the same Options default (UIF-864)');

    Section('non-interactive (UIF-852)');
    CheckEqB(True, Application.NonInteractive, 'NonInteractive (FXC_UI_NONINTERACTIVE=1)');
    O.FileName := 'keep.txt';
    CheckEqB(False, O.Execute, 'TOpenDialog.Execute: False');
    CheckEqB(False, S.Execute(0), 'TSaveDialog.Execute(0): False');
    CheckEqS('keep.txt', O.FileName, 'nothing changed');

    CommonDialogHook := TestHook;

    Section('the OPENFILENAMEW (UIF-860, UIF-861)');
    O.Filter := 'Text files (*.txt)|*.txt|All files (*.*)|*.*';
    O.FilterIndex := 2;
    O.InitialDir := 'C:\data';
    O.Title := 'Pick one';
    O.DefaultExt := 'txt';
    O.FileName := 'a.txt';
    F.HandleNeeded;
    Answer(False, '', 0, 0, 0);
    CheckEqB(False, O.Execute(F.Handle), 'cancelled: False');
    CheckEqI(1, HookCalls, 'the hook replaced the dialog once');
    CheckEqS('TOpenDialog', HookClass, 'with the dialog');
    CheckEqI(152, HookSize, 'lStructSize 152');
    CheckEqB(True, HookOwner = F.Handle, 'the owner passed to the hook');
    CheckEqB(True, HookStructOwner = F.Handle, 'hwndOwner = ParentWnd');
    CheckEqS('Text files (*.txt)|*.txt|All files (*.*)|*.*||', HookFilter, 'filter: | to #0, double terminator');
    CheckEqI(2, HookFilterIndex, 'nFilterIndex');
    CheckEqS('a.txt', HookFile, 'file buffer pre-filled with FileName');
    CheckEqI(32768, HookMaxFile, 'file buffer of 32 768 characters');
    CheckEqS('C:\data', HookInitialDir, 'lpstrInitialDir');
    CheckEqS('Pick one', HookTitle, 'lpstrTitle');
    CheckEqS('txt', HookDefExt, 'lpstrDefExt');
    CheckEqI(BaseFlags, HookFlags, 'default Options plus OFN_EXPLORER and OFN_NOCHANGEDIR');
    CheckEqS('a.txt', O.FileName, 'cancel keeps FileName');
    CheckEqI(2, O.FilterIndex, 'and FilterIndex');

    O.Filter := '';
    O.InitialDir := '';
    O.Title := '';
    O.DefaultExt := '';
    O.FileName := '';
    Answer(False, '', 0, 0, 0);
    O.Execute(0);
    CheckEqS('(nil)', HookFilter, 'empty Filter: nil');
    CheckEqS('(nil)', HookInitialDir, 'empty InitialDir: nil');
    CheckEqS('(nil)', HookTitle, 'empty Title: nil');
    CheckEqS('(nil)', HookDefExt, 'empty DefaultExt: nil');
    CheckEqS('', HookFile, 'empty file buffer');
    CheckEqB(True, HookStructOwner = 0, 'Execute(0): no owner');

    Section('Options to OFN flags (28.1 table)');
    O.Options := [ofReadOnly, ofOverwritePrompt, ofHideReadOnly, ofNoChangeDir, ofShowHelp, ofNoValidate,
      ofAllowMultiSelect, ofExtensionDifferent, ofPathMustExist, ofFileMustExist, ofCreatePrompt, ofShareAware,
      ofNoReadOnlyReturn, ofNoTestFileCreate, ofNoNetworkButton, ofNoDereferenceLinks, ofEnableSizing,
      ofDontAddToRecent, ofForceShowHidden, ofOldStyleDialog, ofNoLongNames, ofEnableIncludeNotify];
    Answer(False, '', 0, 0, 0);
    O.Execute(0);
    CheckEqI(OFN_READONLY or OFN_OVERWRITEPROMPT or OFN_HIDEREADONLY or OFN_NOCHANGEDIR or OFN_NOVALIDATE
      or OFN_ALLOWMULTISELECT or OFN_PATHMUSTEXIST or OFN_FILEMUSTEXIST or OFN_CREATEPROMPT or OFN_SHAREAWARE
      or OFN_NOREADONLYRETURN or OFN_NOTESTFILECREATE or OFN_NODEREFERENCELINKS or OFN_ENABLESIZING
      or OFN_DONTADDTORECENT or OFN_FORCESHOWHIDDEN or OFN_EXPLORER, HookFlags,
      'every option: its flag; ignored and output-only options add none');
    O.Options := [];
    Answer(False, '', 0, 0, 0);
    O.Execute(0);
    CheckEqI(OFN_EXPLORER or OFN_NOCHANGEDIR, HookFlags, 'no options: OFN_EXPLORER and OFN_NOCHANGEDIR always');
    O.Options := [ofHideReadOnly, ofEnableSizing];

    Section('a single file chosen (UIF-862)');
    O.Filter := 'Text files (*.txt)|*.txt|All files (*.*)|*.*';
    O.FileName := 'a.txt';
    Answer(True, 'C:\data\b.txt', 1, OFN_READONLY or OFN_EXTENSIONDIFFERENT, 0);
    CheckEqB(True, O.Execute(0), 'Execute True');
    CheckEqS('C:\data\b.txt', O.FileName, 'FileName = the chosen path');
    CheckEqS('C:\data\b.txt;', FilesText(O.Files), 'Files: one entry');
    CheckEqI(1, O.FilterIndex, 'FilterIndex updated');
    CheckEqB(True, ofReadOnly in O.Options, 'ofReadOnly: the state of the check box');
    CheckEqB(True, ofExtensionDifferent in O.Options, 'ofExtensionDifferent reported');
    Answer(True, 'C:\data\c.txt', 2, 0, 0);
    O.Execute(0);
    CheckEqB(False, ofReadOnly in O.Options, 'ofReadOnly cleared when the box is unchecked');
    CheckEqB(False, ofExtensionDifferent in O.Options, 'ofExtensionDifferent cleared');
    CheckEqS('C:\data\c.txt;', FilesText(O.Files), 'Files replaced');

    Section('multi-select (UIF-862)');
    O.Options := O.Options + [ofAllowMultiSelect];
    Answer(True, 'C:\dir|one.txt|two.txt|', 1, 0, 0);
    CheckEqB(True, O.Execute(0), 'Execute True');
    CheckEqS('C:\dir\one.txt;C:\dir\two.txt;', FilesText(O.Files), 'directory + each name');
    CheckEqS('C:\dir\one.txt', O.FileName, 'FileName = Files[0]');
    Answer(True, 'C:\|x.txt|y.txt|', 1, 0, 0);
    O.Execute(0);
    CheckEqS('C:\x.txt;C:\y.txt;', FilesText(O.Files), 'a root directory: one separator');
    Answer(True, 'C:\dir\only.txt|', 1, 0, 0);
    O.Execute(0);
    CheckEqS('C:\dir\only.txt;', FilesText(O.Files), 'one file chosen: a single full path');
    CheckEqS('C:\dir\only.txt', O.FileName, 'FileName');
    O.Options := O.Options - [ofAllowMultiSelect];

    Section('errors (UIF-851)');
    Answer(False, '', 0, 0, FNERR_INVALIDFILENAME);
    Msg := '';
    try
      O.Execute(0);
    except
      on E: EInvalidOperation do
        Msg := E.Message;
    end;
    CheckEqS('Common dialog error 0x3002', Msg, 'a non-zero extended error raises EInvalidOperation');
    Answer(False, '', 0, 0, CDERR_STRUCTSIZE);
    Msg := '';
    try
      O.Execute(0);
    except
      on E: EInvalidOperation do
        Msg := E.Message;
    end;
    CheckEqS('Common dialog error 0x0001', Msg, 'four hex digits');

    Section('owner and task windows (UIF-850)');
    OtherForm.Show;
    Answer(False, '', 0, 0, 0);
    O.Execute(0);
    CheckEqB(False, HookOtherEnabled, 'the other visible form is disabled while the dialog runs');
    CheckEqB(True, IsWindowEnabled(OtherForm.Handle), 'and enabled again afterwards');
    Expected := 0;
    if (Screen.ActiveForm <> nil) and Screen.ActiveForm.HandleAllocated and IsWindowVisible(Screen.ActiveForm.Handle) then
      Expected := Screen.ActiveForm.Handle;
    O.Execute;
    CheckEqB(True, HookStructOwner = Expected, 'Execute: the active form''s handle, or 0');
    Answer(False, '', 0, 0, FNERR_INVALIDFILENAME);
    try
      O.Execute(0);
    except
      on EInvalidOperation do
        ;
    end;
    CheckEqB(True, IsWindowEnabled(OtherForm.Handle), 'enabled again after an error too');
    OtherForm.Hide;

    Section('TSaveDialog (UIF-863, UIF-864)');
    S.Options := S.Options + [ofOverwritePrompt];
    S.DefaultExt := 'dat';
    Answer(True, 'C:\out\saved.dat', 1, 0, 0);
    CheckEqB(True, S.Execute(0), 'Execute True');
    CheckEqS('TSaveDialog', HookClass, 'the hook receives the save dialog');
    CheckEqI(BaseFlags or OFN_OVERWRITEPROMPT, HookFlags, 'OFN_OVERWRITEPROMPT');
    CheckEqS('dat', HookDefExt, 'DefaultExt');
    CheckEqS('C:\out\saved.dat', S.FileName, 'FileName');

    Section('streaming');
    SF := TDlgForm.Create(nil);
    try
      CheckEqS('Data files (*.dat)|*.dat', SF.OpenDialog1.Filter, 'Filter');
      CheckEqB(True, SF.OpenDialog1.Options = [ofHideReadOnly, ofAllowMultiSelect, ofEnableSizing], 'Options');
      CheckEqI(2, SF.OpenDialog1.FilterIndex, 'FilterIndex');
      CheckEqS('Save as', SF.SaveDialog1.Title, 'Title');
      CheckEqS('dat', SF.SaveDialog1.DefaultExt, 'DefaultExt');
      CheckEqS('C:\work', SF.SaveDialog1.InitialDir, 'InitialDir');
      CheckEqS('start.dat', SF.SaveDialog1.FileName, 'FileName');
    finally
      SF.Free;
    end;
    CommonDialogHook := nil;
  finally
    OtherForm.Free;
    F.Free;
  end;
  Done;
end.
