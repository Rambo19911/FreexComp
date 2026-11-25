// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1374, UIF-1375, UIF-1376, UIF-1377
// phase: 8
program Act_StdFile;
{ U-153, standard-actions.md section 5: the file actions of unit StdActns. TFileOpen owns a TOpenDialog,
  TFileSaveAs a TSaveDialog (public Dialog, set up in code); every component is a target, so the actions are
  enabled. Execute calls BeforeExecute, runs the dialog, then OnAccept (the handler reads Dialog.FileName) or
  OnCancel. The dialog is replaced by Dialogs.CommonDialogHook; without the hook the non-interactive mode writes
  the [Dialog] line and the dialog is cancelled. TFileExit terminates the application without a main form and
  closes the main form otherwise (OnCloseQuery may refuse). An assigned OnExecute replaces the built-in behaviour
  of a standard action. Constructors set no texts. Needs no desktop: no window is created (WM_QUIT is looked for
  in the queue of the thread and removed, so the application is never terminated). }
uses
  Windows, Messages, SysUtils, Classes, Controls, Forms, StdCtrls, Dialogs, CommDlg, ActnList, StdActns, TestKit;

type
  TMainF = class(TForm)
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TLog = class
  public
    Text: string;
    AllowClose: Boolean;
    procedure Before(Sender: TObject);
    procedure Accept(Sender: TObject);
    procedure Cancel(Sender: TObject);
    procedure Exec(Sender: TObject);
    procedure CloseQuery(Sender: TObject; var CanClose: Boolean);
    procedure Closed(Sender: TObject; var Action: TCloseAction);
  end;

var
  Log: TLog;
  HookCalls: Integer;
  HookClass, HookFilter, HookTitle: string;
  AnswerOk: Boolean;
  AnswerFile: string;

constructor TMainF.Create(AOwner: TComponent);
begin
  CreateNew(AOwner);
end;

function DialogOf(Sender: TObject): TOpenDialog;
begin
  if Sender is TFileOpen then
    Result := TFileOpen(Sender).Dialog
  else
    Result := TFileSaveAs(Sender).Dialog;
end;

procedure TLog.Before(Sender: TObject);
begin
  Text := Text + 'before:' + TComponent(Sender).Name + ';';
end;

procedure TLog.Accept(Sender: TObject);
begin
  Text := Text + 'accept:' + DialogOf(Sender).FileName + ';';
end;

procedure TLog.Cancel(Sender: TObject);
begin
  Text := Text + 'cancel:' + TComponent(Sender).Name + ';';
end;

procedure TLog.Exec(Sender: TObject);
begin
  Text := Text + 'exec:' + TComponent(Sender).Name + ';';
end;

procedure TLog.CloseQuery(Sender: TObject; var CanClose: Boolean);
begin
  Text := Text + 'query;';
  CanClose := AllowClose;
end;

procedure TLog.Closed(Sender: TObject; var Action: TCloseAction);
begin
  Text := Text + 'close;';
  Action := caHide;
end;

function TestHook(Dialog: TCommonDialog; ParentWnd: HWND; Data: Pointer; var ExtendedError: DWORD): Boolean;
var
  O: POpenFilenameW;
  I: Integer;
begin
  Inc(HookCalls);
  HookClass := Dialog.ClassName;
  O := POpenFilenameW(Data);
  HookFilter := '';
  if O^.lpstrFilter <> nil then
    HookFilter := O^.lpstrFilter;                            { the first part, up to the first #0 }
  HookTitle := '';
  if O^.lpstrTitle <> nil then
    HookTitle := O^.lpstrTitle;
  Log.Text := Log.Text + 'dialog;';
  Result := AnswerOk;
  ExtendedError := 0;
  if AnswerOk then
  begin
    for I := 1 to Length(AnswerFile) do
      O^.lpstrFile[I - 1] := AnswerFile[I];
    O^.lpstrFile[Length(AnswerFile)] := #0;
    O^.lpstrFile[Length(AnswerFile) + 1] := #0;
  end;
end;

procedure Answer(Ok: Boolean; const AFile: string);
begin
  AnswerOk := Ok;
  AnswerFile := AFile;
  HookCalls := 0;
  HookClass := '';
  Log.Text := '';
end;

{ a WM_QUIT waiting in the queue of the thread (Application.Terminate posts it); removed }
function QuitPosted: Boolean;
var
  Msg: TMsg;
begin
  Result := PeekMessageW(Msg, 0, WM_QUIT, WM_QUIT, PM_REMOVE);
end;

procedure Run;
var
  F: TForm;
  MainF: TMainF;
  List: TActionList;
  Open: TFileOpen;
  SaveAs: TFileSaveAs;
  ExitAct: TFileExit;
  SelAll: TEditSelectAll;
  E: TEdit;
begin
  Log := TLog.Create;
  F := TForm.CreateNew(nil);
  try
    List := TActionList.Create(F);
    Open := TFileOpen.Create(F);
    Open.Name := 'FileOpen1';
    Open.ActionList := List;
    SaveAs := TFileSaveAs.Create(F);
    SaveAs.Name := 'FileSaveAs1';
    SaveAs.ActionList := List;
    ExitAct := TFileExit.Create(F);
    ExitAct.Name := 'FileExit1';
    ExitAct.ActionList := List;

    Section('after the constructor (UIF-1374)');
    Check(Open is TAction, 'TFileOpen is a TAction');
    CheckEqS('TOpenDialog', Open.Dialog.ClassName, 'TFileOpen owns a TOpenDialog');
    CheckEqS('TSaveDialog', SaveAs.Dialog.ClassName, 'TFileSaveAs owns a TSaveDialog');
    Check(Open.Dialog.Owner = Open, 'the owner of the dialog is the action');
    Check(SaveAs.Dialog.Owner = SaveAs, 'also for save');
    Check(Open.Dialog = Open.Dialog, 'every read gives the same dialog');
    CheckEqS('', Open.Caption + SaveAs.Caption + ExitAct.Caption, 'no captions');
    CheckEqS('', Open.Hint + SaveAs.Hint + ExitAct.Hint, 'no hints');
    CheckEqI(0, Open.ShortCut + SaveAs.ShortCut + ExitAct.ShortCut, 'no shortcuts');
    CheckEqB(True, Open.DisableIfNoHandler and SaveAs.DisableIfNoHandler and ExitAct.DisableIfNoHandler,
      'DisableIfNoHandler set by every class');
    Check((GetClass('TFileOpen') = TFileOpen) and (GetClass('TFileSaveAs') = TFileSaveAs)
      and (GetClass('TFileExit') = TFileExit), 'the classes are registered');

    Section('targets (UIF-1375, UIF-1376)');
    CheckEqB(True, Open.HandlesTarget(F), 'TFileOpen: a form is a target');
    CheckEqB(True, Open.HandlesTarget(Application), 'TFileOpen: the application is a target');
    CheckEqB(True, SaveAs.HandlesTarget(List), 'TFileSaveAs: any component');
    CheckEqB(True, ExitAct.HandlesTarget(Application), 'TFileExit: the application');
    Check(Application.MainForm = nil, 'no main form');
    Open.Enabled := False;
    SaveAs.Enabled := False;
    ExitAct.Enabled := False;
    CheckEqB(False, Open.Update, 'Update: no update handler ran');
    SaveAs.Update;
    ExitAct.Update;
    CheckEqB(True, Open.Enabled, 'TFileOpen: enabled by its target');
    CheckEqB(True, SaveAs.Enabled, 'TFileSaveAs: enabled by its target');
    CheckEqB(True, ExitAct.Enabled, 'TFileExit: enabled by its target');

    Section('non-interactive, no hook (UIF-1375, UIF-852)');
    CheckEqB(True, Application.NonInteractive, 'NonInteractive (FXC_UI_NONINTERACTIVE=1)');
    Open.BeforeExecute := Log.Before;
    Open.OnAccept := Log.Accept;
    Open.OnCancel := Log.Cancel;
    SaveAs.BeforeExecute := Log.Before;
    SaveAs.OnAccept := Log.Accept;
    SaveAs.OnCancel := Log.Cancel;
    Log.Text := '';
    CheckEqB(True, Open.Execute, 'TFileOpen.Execute: handled');
    CheckEqS('before:FileOpen1;cancel:FileOpen1;', Log.Text, 'BeforeExecute, the [Dialog] line, OnCancel');
    Log.Text := '';
    CheckEqB(True, SaveAs.Execute, 'TFileSaveAs.Execute: handled');
    CheckEqS('before:FileSaveAs1;cancel:FileSaveAs1;', Log.Text, 'BeforeExecute, the [Dialog] line, OnCancel');

    Section('the hook accepts (UIF-1375)');
    CommonDialogHook := TestHook;
    Open.Dialog.Filter := 'Text files|*.txt';
    Open.Dialog.Title := 'Pick';
    Answer(True, 'C:\data\picked.txt');
    CheckEqB(True, Open.Execute, 'TFileOpen.Execute');
    CheckEqS('before:FileOpen1;dialog;accept:C:\data\picked.txt;', Log.Text,
      'BeforeExecute, the dialog, OnAccept with Dialog.FileName');
    CheckEqI(1, HookCalls, 'the dialog ran once');
    CheckEqS('TOpenDialog', HookClass, 'an open dialog');
    CheckEqS('Text files', HookFilter, 'Dialog.Filter set in code reached the dialog');
    CheckEqS('Pick', HookTitle, 'and Dialog.Title');
    CheckEqS('C:\data\picked.txt', Open.Dialog.FileName, 'Dialog.FileName after the action');
    CheckEqI(1, Open.Dialog.Files.Count, 'Dialog.Files');
    Answer(True, 'C:\data\out.txt');
    CheckEqB(True, SaveAs.Execute, 'TFileSaveAs.Execute');
    CheckEqS('before:FileSaveAs1;dialog;accept:C:\data\out.txt;', Log.Text, 'the same order');
    CheckEqS('TSaveDialog', HookClass, 'a save dialog');

    Section('the hook cancels (UIF-1375)');
    Answer(False, '');
    CheckEqB(True, Open.Execute, 'TFileOpen.Execute: handled also when cancelled');
    CheckEqS('before:FileOpen1;dialog;cancel:FileOpen1;', Log.Text, 'BeforeExecute, the dialog, OnCancel');
    CheckEqS('C:\data\picked.txt', Open.Dialog.FileName, 'FileName unchanged');
    Answer(False, '');
    CheckEqB(True, SaveAs.Execute, 'TFileSaveAs.Execute');
    CheckEqS('before:FileSaveAs1;dialog;cancel:FileSaveAs1;', Log.Text, 'the same order');

    Section('no events assigned (UIF-1375)');
    Open.BeforeExecute := nil;
    Open.OnAccept := nil;
    Open.OnCancel := nil;
    Answer(True, 'C:\x.txt');
    CheckEqB(True, Open.Execute, 'accepted');
    CheckEqS('dialog;', Log.Text, 'only the dialog');
    CheckEqS('C:\x.txt', Open.Dialog.FileName, 'FileName set');
    Answer(False, '');
    CheckEqB(True, Open.Execute, 'cancelled');
    CheckEqS('dialog;', Log.Text, 'only the dialog');

    Section('OnExecute replaces the built-in behaviour (UIF-1377)');
    Open.BeforeExecute := Log.Before;
    Open.OnExecute := Log.Exec;
    Answer(True, 'C:\y.txt');
    CheckEqB(True, Open.Execute, 'TFileOpen.Execute');
    CheckEqS('exec:FileOpen1;', Log.Text, 'the handler only: no BeforeExecute, no dialog');
    CheckEqI(0, HookCalls, 'the dialog did not run');
    Open.OnExecute := nil;
    E := TEdit.Create(F);
    E.Parent := F;
    E.Text := 'text';
    E.SelStart := 1;
    E.SelLength := 0;
    SelAll := TEditSelectAll.Create(F);
    SelAll.Name := 'EditSelectAll1';
    SelAll.Control := E;
    SelAll.OnExecute := Log.Exec;
    Log.Text := '';
    CheckEqB(True, SelAll.Execute, 'TEditSelectAll.Execute');
    CheckEqS('exec:EditSelectAll1;', Log.Text, 'the handler ran');
    CheckEqI(0, E.SelLength, 'nothing selected');
    SelAll.OnExecute := nil;
    CheckEqB(True, SelAll.Execute, 'without the handler');
    CheckEqI(4, E.SelLength, 'the text is selected');
    CommonDialogHook := nil;

    Section('TFileExit without a main form (UIF-1376)');
    CheckEqB(False, QuitPosted, 'no WM_QUIT is waiting');
    CheckEqB(True, ExitAct.Execute, 'Execute');
    CheckEqB(True, QuitPosted, 'Application.Terminate was called');
    ExitAct.OnExecute := Log.Exec;
    Log.Text := '';
    CheckEqB(True, ExitAct.Execute, 'with OnExecute (UIF-1377)');
    CheckEqS('exec:FileExit1;', Log.Text, 'the handler ran');
    CheckEqB(False, QuitPosted, 'and nothing else');
    ExitAct.OnExecute := nil;

    Section('TFileExit with a main form (UIF-1376)');
    Application.CreateForm(TMainF, MainF);
    Check(Application.MainForm = MainF, 'the main form');
    MainF.OnCloseQuery := Log.CloseQuery;
    MainF.OnClose := Log.Closed;
    Log.AllowClose := False;
    Log.Text := '';
    CheckEqB(True, ExitAct.Execute, 'Execute, OnCloseQuery refuses');
    CheckEqS('query;', Log.Text, 'asked, not closed');
    CheckEqB(False, QuitPosted, 'the application goes on');
    Log.AllowClose := True;
    Log.Text := '';
    CheckEqB(True, ExitAct.Execute, 'Execute, OnCloseQuery agrees');
    CheckEqS('query;close;', Log.Text, 'asked and closed');
    CheckEqB(True, QuitPosted, 'closing the main form terminates the application');
    CheckEqB(False, Application.Terminated, 'the program removed every WM_QUIT');
  finally
    CommonDialogHook := nil;
    F.Free;
    Log.Free;
  end;
end;

begin
  Application.Initialize;
  Run;
  Done;
end.
