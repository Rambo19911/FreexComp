// covers: DAT-256, DAT-257, DAT-258, DAT-259, DAT-406, DAT-407
program CtlNavigator;
{ TDBNavigator on a shown form: the button layout, the hints, the enabling table in every dataset situation,
  the action of every button, BeforeAction and OnClick, the delete confirmation through the message-box hook,
  mouse clicks and the lifetime of the data source. Nothing is compared by pixels. }
uses
  Windows, Messages, SysUtils, Classes, Controls, Forms, DB, DBCtrls, DataTestUtils, TestDataSet, CtlTestUtils;

const
  BtnNames: array[TNavigateBtn] of string = ('nbFirst', 'nbPrior', 'nbNext', 'nbLast', 'nbInsert', 'nbDelete',
    'nbEdit', 'nbPost', 'nbCancel', 'nbRefresh');
  BtnLetters = 'FPNLIDEOCR';

type
  THandlers = class
  public
    Log: string;
    AbortBefore: Boolean;
    procedure Before(Sender: TObject; Button: TNavigateBtn);
    procedure Click(Sender: TObject; Button: TNavigateBtn);
  end;

var
  BoxTexts: string;
  BoxAnswer: Integer;

procedure THandlers.Before(Sender: TObject; Button: TNavigateBtn);
begin
  Log := Log + 'before:' + BtnNames[Button] + ';';
  if AbortBefore then
    Abort;
end;

procedure THandlers.Click(Sender: TObject; Button: TNavigateBtn);
begin
  Log := Log + 'click:' + BtnNames[Button] + ';';
end;

function AnswerBox(const Text: string): Integer;
begin
  BoxTexts := BoxTexts + Text + ';';
  Result := BoxAnswer;
end;

{ one letter per enabled button (First Prior Next Last Insert Delete Edit pOst Cancel Refresh), '-' when disabled }
function Enabling(N: TDBNavigator): string;
var
  B: TNavigateBtn;
begin
  Result := '';
  for B := nbFirst to nbRefresh do
    if N.ButtonEnabled(B) then
      Result := Result + BtnLetters[Ord(B) + 1]
    else
      Result := Result + '-';
end;

function RectText(const R: TRect): string;
begin
  Result := Format('%d,%d,%d,%d', [R.Left, R.Top, R.Right, R.Bottom]);
end;

function ClickError(N: TDBNavigator; B: TNavigateBtn): string;
begin
  Result := '';
  try
    N.BtnClick(B);
  except
    on E: Exception do
      Result := E.ClassName;
  end;
end;

procedure MouseClick(N: TDBNavigator; B: TNavigateBtn);
var
  R: TRect;
  P: LPARAM;
begin
  R := N.ButtonRect(B);
  P := MakeLParam(Word((R.Left + R.Right) div 2), Word((R.Top + R.Bottom) div 2));
  SendMessageW(N.Handle, WM_LBUTTONDOWN, MK_LBUTTON, P);
  SendMessageW(N.Handle, WM_LBUTTONUP, 0, P);
end;

{ id, name (10); Count rows 1 n1, 2 n2, ...; not opened }
function NewDS(Count: Integer): TTestDataSet;
var
  I: Integer;
begin
  Result := TTestDataSet.Create(nil);
  Result.AddDef('id', ftInteger);
  Result.AddDef('name', ftString, 10);
  for I := 1 to Count do
    Result.AddRow([IntValue(I), StringValue('n' + IntToStr(I))]);
end;

procedure TestLayout(F: TForm);
var
  N: TDBNavigator;
  C: TControl;
  B: TNavigateBtn;
  S: string;
begin
  Section('defaults and layout');
  N := TDBNavigator.Create(F);
  N.Parent := F;
  C := N;
  Check(C is TCustomControl, 'a custom control');
  CheckEqInt(N.Width, 240, 'Width');
  CheckEqInt(N.Height, 25, 'Height');
  CheckEqBool(N.TabStop, False, 'TabStop');
  CheckEqBool(N.ConfirmDelete, True, 'ConfirmDelete');
  Check(N.VisibleButtons = [nbFirst, nbPrior, nbNext, nbLast, nbInsert, nbDelete, nbEdit, nbPost, nbCancel,
    nbRefresh], 'all buttons are visible');
  Check(N.DataSource = nil, 'DataSource');
  CheckEqInt(N.Hints.Count, 0, 'no hint overrides');
  CheckEq(RectText(N.ButtonRect(nbFirst)), '0,0,24,25', 'ten buttons share 240: the first');
  CheckEq(RectText(N.ButtonRect(nbPrior)), '24,0,48,25', 'the second');
  CheckEq(RectText(N.ButtonRect(nbRefresh)), '216,0,240,25', 'the last');
  N.Width := 247;
  CheckEq(RectText(N.ButtonRect(nbCancel)), '192,0,216,25', 'width 247: 24 each');
  CheckEq(RectText(N.ButtonRect(nbRefresh)), '216,0,247,25', 'the last button takes the remainder');
  N.VisibleButtons := [nbFirst, nbNext, nbPost];
  N.SetBounds(N.Left, N.Top, 100, 30);
  CheckEq(RectText(N.ButtonRect(nbFirst)), '0,0,33,30', 'three buttons share 100, full height');
  CheckEq(RectText(N.ButtonRect(nbNext)), '33,0,66,30', 'in enumeration order');
  CheckEq(RectText(N.ButtonRect(nbPost)), '66,0,100,30', 'the last with the remainder');
  CheckEq(RectText(N.ButtonRect(nbPrior)), '0,0,0,0', 'a hidden button has no rectangle');
  N.VisibleButtons := [];
  CheckEq(RectText(N.ButtonRect(nbFirst)), '0,0,0,0', 'no visible button');
  N.Update;
  Check(True, 'paints without buttons');

  Section('hints');
  S := '';
  for B := nbFirst to nbRefresh do
    S := S + N.ButtonHint(B) + ';';
  CheckEq(S, 'First record;Prior record;Next record;Last record;Insert record;Delete record;Edit record;'
    + 'Post edit;Cancel edit;Refresh data;', 'the default hints');
  N.Hints.Add('Go to start');
  N.Hints.Add('');
  N.Hints.Add('Forward');
  CheckEq(N.ButtonHint(nbFirst), 'Go to start', 'an override');
  CheckEq(N.ButtonHint(nbPrior), 'Prior record', 'an empty override line: the default');
  CheckEq(N.ButtonHint(nbNext), 'Forward', 'the third line is for the third button');
  CheckEq(N.ButtonHint(nbLast), 'Last record', 'no line: the default');
  N.Free;
end;

procedure TestEnabling(F: TForm);
var
  DS, Empty: TTestDataSet;
  S: TDataSource;
  N: TDBNavigator;
begin
  Section('enabling');
  DS := NewDS(3);
  Empty := NewDS(0);
  S := TDataSource.Create(nil);
  N := TDBNavigator.Create(F);
  N.Parent := F;
  CheckEq(Enabling(N), '----------', 'no data source');
  N.DataSource := S;
  CheckEq(Enabling(N), '----------', 'no dataset');
  S.DataSet := DS;
  CheckEq(Enabling(N), '----------', 'closed dataset');
  DS.Open;
  CheckEq(Enabling(N), '--NLIDE--R', 'open, at the first row (Bof)');
  DS.Next;
  CheckEq(Enabling(N), 'FPNLIDE--R', 'a middle row');
  DS.Last;
  CheckEq(Enabling(N), 'FP--IDE--R', 'after Last (Eof)');
  DS.Prior;
  CheckEq(Enabling(N), 'FPNLIDE--R', 'back in the middle');
  DS.Edit;
  CheckEq(Enabling(N), 'FPNLID-OCR', 'dsEdit');
  DS.Cancel;
  DS.Insert;
  CheckEq(Enabling(N), 'FPNLI--OCR', 'dsInsert');
  DS.Cancel;
  N.Update;
  Check(True, 'paints with enabled and disabled buttons');
  S.Enabled := False;
  CheckEq(Enabling(N), '----------', 'a disabled data source');
  S.Enabled := True;
  N.Enabled := False;
  CheckEq(Enabling(N), '----------', 'the control disabled');
  N.Enabled := True;
  CheckEq(Enabling(N), 'FPNLIDE--R', 'enabled again');
  DS.Close;
  DS.DriverReadOnly := True;
  DS.Open;
  DS.Next;
  CheckEq(Enabling(N), 'FPNL-----R', 'a dataset that cannot be modified');
  DS.Close;
  DS.DriverReadOnly := False;
  S.DataSet := Empty;
  Empty.Open;
  CheckEq(Enabling(N), '----I-E--R', 'an empty dataset');
  Empty.Insert;
  CheckEq(Enabling(N), 'FPNLI--OCR', 'inserting into an empty dataset: neither Bof nor Eof');
  Empty.Cancel;
  N.Free;
  S.Free;
  Empty.Free;
  DS.Free;
end;

procedure TestActions(F: TForm);
var
  DS: TTestDataSet;
  S: TDataSource;
  Box: TComponent;
  N: TDBNavigator;
  H: THandlers;
begin
  Section('actions');
  DS := NewDS(3);
  Box := TComponent.Create(nil);
  S := TDataSource.Create(Box);
  S.DataSet := DS;
  DS.Open;
  N := TDBNavigator.Create(F);
  N.Parent := F;
  N.DataSource := S;
  N.ConfirmDelete := False;
  H := THandlers.Create;
  N.BeforeAction := H.Before;
  N.OnClick := H.Click;
  N.BtnClick(nbNext);
  CheckEqInt(DS.RecNo, 2, 'nbNext');
  CheckEq(H.Log, 'before:nbNext;click:nbNext;', 'BeforeAction, the action, OnClick');
  N.BtnClick(nbLast);
  CheckEqInt(DS.RecNo, 3, 'nbLast');
  N.BtnClick(nbPrior);
  CheckEqInt(DS.RecNo, 2, 'nbPrior');
  N.BtnClick(nbFirst);
  CheckEqInt(DS.RecNo, 1, 'nbFirst');
  N.BtnClick(nbEdit);
  CheckEq(StateName(DS.State), 'dsEdit', 'nbEdit');
  DS.FieldByName('name').AsString := 'changed';
  N.BtnClick(nbPost);
  CheckEq(StateName(DS.State), 'dsBrowse', 'nbPost');
  CheckEq(DS.StoredText, '[1|changed] [2|n2] [3|n3]', 'posted');
  N.BtnClick(nbInsert);
  CheckEq(StateName(DS.State), 'dsInsert', 'nbInsert');
  N.BtnClick(nbCancel);
  CheckEq(StateName(DS.State), 'dsBrowse', 'nbCancel');
  CheckEqInt(DS.RecordCount, 3, 'nothing inserted');
  ClearLog;
  DS.HookEvents;
  N.BtnClick(nbRefresh);
  Check(Pos('BeforeRefresh', TakeLog) > 0, 'nbRefresh');
  N.BtnClick(nbDelete);
  CheckEq(DS.StoredText, '[2|n2] [3|n3]', 'nbDelete without confirmation');

  Section('disabled buttons');
  N.BtnClick(nbFirst);
  H.Log := '';
  N.BtnClick(nbPost);
  N.BtnClick(nbCancel);
  N.BtnClick(nbPrior);
  CheckEq(H.Log, '', 'a call for a disabled button does nothing');
  CheckEq(StateName(DS.State), 'dsBrowse', 'no state change');

  Section('BeforeAction');
  H.AbortBefore := True;
  CheckEq(ClickError(N, nbNext), 'EAbort', 'Abort in BeforeAction leaves BtnClick');
  CheckEqInt(DS.RecNo, 1, 'the action did not run');
  CheckEq(H.Log, 'before:nbNext;', 'OnClick did not fire');
  H.AbortBefore := False;

  Section('delete confirmation');
  N.ConfirmDelete := True;
  MessageBoxHook := AnswerBox;
  BoxAnswer := IDCANCEL;
  H.Log := '';
  N.BtnClick(nbDelete);
  CheckEq(BoxTexts, 'Delete record?;', 'the confirmation is asked');
  CheckEq(DS.StoredText, '[2|n2] [3|n3]', 'Cancel: nothing deleted');
  CheckEq(H.Log, 'before:nbDelete;', 'Cancel: no OnClick');
  BoxAnswer := IDOK;
  BoxTexts := '';
  H.Log := '';
  N.BtnClick(nbDelete);
  CheckEq(BoxTexts, 'Delete record?;', 'asked again');
  CheckEq(DS.StoredText, '[3|n3]', 'OK: deleted');
  CheckEq(H.Log, 'before:nbDelete;click:nbDelete;', 'OK: OnClick');
  N.ConfirmDelete := False;
  BoxTexts := '';
  N.BtnClick(nbInsert);
  DS.FieldByName('id').AsInteger := 9;
  N.BtnClick(nbPost);
  N.BtnClick(nbDelete);
  CheckEq(BoxTexts, '', 'ConfirmDelete off: not asked');
  CheckEqInt(DS.RecordCount, 1, 'deleted');
  MessageBoxHook := nil;

  Section('mouse');
  N.BtnClick(nbInsert);
  DS.FieldByName('id').AsInteger := 5;
  N.BtnClick(nbPost);
  N.BtnClick(nbFirst);
  H.Log := '';
  MouseClick(N, nbNext);
  CheckEqInt(DS.RecNo, 2, 'a click on an enabled button runs its action');
  CheckEq(H.Log, 'before:nbNext;click:nbNext;', 'with both events');
  H.Log := '';
  MouseClick(N, nbPost);
  CheckEq(H.Log, '', 'a click on a disabled button does nothing');
  MouseClick(N, nbEdit);
  CheckEq(StateName(DS.State), 'dsEdit', 'nbEdit by mouse');
  MouseClick(N, nbCancel);
  CheckEq(StateName(DS.State), 'dsBrowse', 'nbCancel by mouse');
  N.VisibleButtons := [nbFirst, nbPrior];
  MouseClick(N, nbPrior);
  CheckEqInt(DS.RecNo, 1, 'the click finds the button in a reduced set');

  Section('lifetime');
  CheckEq(Enabling(N), 'FPNLIDE--R', 'linked');
  Box.Free;
  Check(N.DataSource = nil, 'destroying the source clears DataSource');
  CheckEq(Enabling(N), '----------', 'all buttons disabled');
  H.Free;
  N.Free;
  DS.Free;
end;

var
  F: TForm;
begin
  Application.Initialize;
  HookBeeps;
  F := NewForm;
  try
    TestLayout(F);
    TestEnabling(F);
    TestActions(F);
  finally
    F.Free;
  end;
  WriteLn('failed checks: ', FailedChecks);
end.
