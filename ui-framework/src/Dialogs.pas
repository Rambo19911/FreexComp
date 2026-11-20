{ Dialogs - message boxes and simple dialogs (application-forms.md section 9). }
unit Dialogs;

interface

uses
  Windows, Messages, Types, SysUtils, Classes, Controls, Forms, StdCtrls, UIConsts;

type
  TMsgDlgType = (mtWarning, mtError, mtInformation, mtConfirmation, mtCustom);
  TMsgDlgBtn = (mbYes, mbNo, mbOK, mbCancel, mbAbort, mbRetry, mbIgnore, mbAll, mbNoToAll, mbYesToAll, mbHelp,
    mbClose);
  TMsgDlgButtons = set of TMsgDlgBtn;

const
  mbYesNo = [mbYes, mbNo];
  mbYesNoCancel = [mbYes, mbNo, mbCancel];
  mbOKCancel = [mbOK, mbCancel];
  mbAbortRetryIgnore = [mbAbort, mbRetry, mbIgnore];

type
  { receives the built InputQuery form instead of ShowModal and returns the modal result }
  TInputQueryHook = function(Form: TForm): Integer;

var
  { while assigned, InputQuery / InputBox build their form and call it instead of ShowModal, in both modes, and
    write no stderr line (tests, tools) }
  InputQueryHook: TInputQueryHook;

{ UIF-477 }
procedure ShowMessage(const Msg: string);
procedure ShowMessageFmt(const Msg: string; const Params: array of const);
{ UIF-478 }
function MessageDlg(const Msg: string; DlgType: TMsgDlgType; Buttons: TMsgDlgButtons; HelpCtx: Longint): Integer;
{ UIF-479 }
function InputQuery(const ACaption, APrompt: string; var Value: string): Boolean;
function InputBox(const ACaption, APrompt, ADefault: string): string;

implementation

procedure ShowMessage(const Msg: string);
begin
  FrameworkMessageBox(Msg, Application.Title, MB_OK, '[ShowMessage]', Msg);
end;

procedure ShowMessageFmt(const Msg: string; const Params: array of const);
begin
  ShowMessage(Format(Msg, Params));
end;

function MessageDlg(const Msg: string; DlgType: TMsgDlgType; Buttons: TMsgDlgButtons; HelpCtx: Longint): Integer;
var
  Flags: Longint;
  Caption: string;
begin
  Buttons := Buttons - [mbHelp];
  if (Buttons = []) or (Buttons = [mbOK]) or (Buttons = [mbClose]) then
    Flags := MB_OK
  else if Buttons = [mbOK, mbCancel] then
    Flags := MB_OKCANCEL
  else if Buttons = [mbYes, mbNo] then
    Flags := MB_YESNO
  else if Buttons = [mbYes, mbNo, mbCancel] then
    Flags := MB_YESNOCANCEL
  else if Buttons = [mbRetry, mbCancel] then
    Flags := MB_RETRYCANCEL
  else if Buttons = [mbAbort, mbRetry, mbIgnore] then
    Flags := MB_ABORTRETRYIGNORE
  else
    raise EInvalidOperation.Create(SUnsupportedButtons);
  case DlgType of
    mtWarning:
      begin
        Flags := Flags or MB_ICONWARNING;
        Caption := SMsgDlgWarning;
      end;
    mtError:
      begin
        Flags := Flags or MB_ICONERROR;
        Caption := SMsgDlgError;
      end;
    mtInformation:
      begin
        Flags := Flags or MB_ICONINFORMATION;
        Caption := SMsgDlgInformation;
      end;
    mtConfirmation:
      begin
        Flags := Flags or MB_ICONQUESTION;
        Caption := SMsgDlgConfirm;
      end;
  else
    Caption := Application.Title;
  end;
  Result := FrameworkMessageBox(Msg, Caption, Flags, '[MessageDlg]', Msg);
end;

function InputQuery(const ACaption, APrompt: string; var Value: string): Boolean;
var
  F: TForm;
  L: TLabel;
  E: TEdit;
  B: TButton;
  R: Integer;
begin
  Result := False;
  if Application.NonInteractive and not Assigned(InputQueryHook) then
  begin
    NonInteractiveLine('[InputQuery]', ACaption + ': ' + APrompt);
    Exit;
  end;
  F := TForm.CreateNew(nil);
  try
    F.BorderStyle := bsDialog;
    F.Position := poScreenCenter;
    F.Caption := ACaption;
    F.ClientWidth := 320;
    F.ClientHeight := 97;
    L := TLabel.Create(F);
    L.AutoSize := True;
    L.Left := 8;
    L.Top := 8;
    L.Caption := APrompt;
    L.Parent := F;
    E := TEdit.Create(F);
    E.Parent := F;
    E.SetBounds(8, 28, 304, 23);
    E.Text := Value;
    E.TabOrder := 0;
    E.SelectAll;
    B := TButton.Create(F);
    B.Parent := F;
    B.SetBounds(164, 64, 75, 25);
    B.Caption := SOKButton;
    B.Default := True;
    B.ModalResult := mrOk;
    B.TabOrder := 1;
    B := TButton.Create(F);
    B.Parent := F;
    B.SetBounds(245, 64, 75, 25);
    B.Caption := SCancelButton;
    B.Cancel := True;
    B.ModalResult := mrCancel;
    B.TabOrder := 2;
    F.ScaleForCurrentDpi;
    if Assigned(InputQueryHook) then
      R := InputQueryHook(F)
    else
      R := F.ShowModal;
    if R = mrOk then
    begin
      Value := E.Text;
      Result := True;
    end;
  finally
    F.Free;
  end;
end;

function InputBox(const ACaption, APrompt, ADefault: string): string;
begin
  Result := ADefault;
  InputQuery(ACaption, APrompt, Result);
end;

end.
