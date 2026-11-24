// covers: DAT-220, DAT-221, DAT-222, DAT-225, DAT-226, DAT-242, DAT-243, DAT-406
program CtlCheck;
{ TDBCheckBox on a shown form: the state from boolean and text fields, the click sequence with and without the
  grayed state, the immediate write-back, the rejection without AutoEdit, ignored clicks in the read-only
  state, OnClick for user clicks only and a write the field rejects. }
uses
  Windows, Messages, SysUtils, Classes, Controls, Forms, StdCtrls, DB, DBCtrls, DataTestUtils, TestDataSet,
  CtlTestUtils;

type
  THandlers = class
  public
    Clicks: Integer;
    procedure Click(Sender: TObject);
    procedure Validate(Sender: TField);
  end;

procedure THandlers.Click(Sender: TObject);
begin
  Inc(Clicks);
end;

procedure THandlers.Validate(Sender: TField);
begin
  raise Exception.Create('no change allowed');
end;

function StateText(C: TDBCheckBox): string;
begin
  case C.State of
    cbUnchecked: Result := 'unchecked';
    cbChecked: Result := 'checked';
  else
    Result := 'grayed';
  end;
end;

procedure ClickBox(C: TDBCheckBox);
begin
  ClickControl(C);
end;

{ id, flag, yn (5); rows 1 True Y, 2 False no, 3 NULL NULL, 4 True maybe; not opened }
function NewDS(AOwner: TComponent): TTestDataSet;
begin
  Result := TTestDataSet.Create(AOwner);
  Result.AddDef('id', ftInteger);
  Result.AddDef('flag', ftBoolean);
  Result.AddDef('yn', ftString, 5);
  Result.AddRow([IntValue(1), IntValue(1), StringValue('Y')]);
  Result.AddRow([IntValue(2), IntValue(0), StringValue('no')]);
  Result.AddRow([IntValue(3), NullValue, NullValue]);
  Result.AddRow([IntValue(4), IntValue(1), StringValue('maybe')]);
end;

function NewBox(F: TForm; S: TDataSource; const FieldName: string): TDBCheckBox;
begin
  Result := TDBCheckBox.Create(F);
  Result.SetBounds(10, 10, 120, 17);
  Result.Parent := F;
  Result.Caption := 'Flag';
  Result.DataSource := S;
  Result.DataField := FieldName;
end;

procedure TestState(F: TForm);
var
  DS: TTestDataSet;
  S: TDataSource;
  C: TDBCheckBox;
  K: TControl;
begin
  Section('defaults and state');
  DS := NewDS(nil);
  S := TDataSource.Create(nil);
  S.DataSet := DS;
  C := TDBCheckBox.Create(F);
  C.Parent := F;
  K := C;
  Check(K is TCustomCheckBox, 'a check box control');
  CheckEq(C.ValueChecked, 'True', 'ValueChecked');
  CheckEq(C.ValueUnchecked, 'False', 'ValueUnchecked');
  CheckEqBool(C.AllowGrayed, False, 'AllowGrayed');
  CheckEqBool(C.ReadOnly, False, 'ReadOnly');
  CheckEqBool(C.TabStop, True, 'TabStop');
  CheckEq(StateText(C), 'unchecked', 'State');
  Check(C.Field = nil, 'Field');
  C.DataSource := S;
  C.DataField := 'flag';
  CheckEq(StateText(C), 'unchecked', 'closed dataset: unchecked');
  DS.Open;
  Check(C.Field = DS.FieldByName('flag'), 'Field');
  CheckEq(StateText(C), 'checked', 'boolean True');
  CheckEqBool(C.Checked, True, 'Checked');
  CheckEqInt(SendMessageW(C.Handle, BM_GETCHECK, 0, 0), 1, 'the native state');
  DS.Next;
  CheckEq(StateText(C), 'unchecked', 'boolean False');
  DS.Next;
  CheckEq(StateText(C), 'grayed', 'null: grayed');
  DS.First;

  C.DataField := 'yn';
  CheckEq(StateText(C), 'grayed', 'a text that is in neither list: grayed');
  C.ValueChecked := 'yes;y';
  C.ValueUnchecked := 'N;No';
  DS.Next;
  DS.Prior;
  CheckEq(StateText(C), 'checked', 'the text matches an alternative of ValueChecked, without regard to case');
  DS.Next;
  CheckEq(StateText(C), 'unchecked', 'the text matches an alternative of ValueUnchecked');
  DS.Next;
  CheckEq(StateText(C), 'grayed', 'null: grayed');
  DS.Next;
  CheckEq(StateText(C), 'grayed', 'another text: grayed');
  C.DataField := 'nope';
  CheckEq(StateText(C), 'unchecked', 'an unknown field: unchecked');
  C.DataField := 'flag';
  CheckEq(StateText(C), 'checked', 'the boolean field again');
  DS.Close;
  CheckEq(StateText(C), 'unchecked', 'Close: unchecked');
  C.Free;
  S.Free;
  DS.Free;
end;

procedure TestClick(F: TForm);
var
  DS: TTestDataSet;
  S: TDataSource;
  C: TDBCheckBox;
  H: THandlers;
  Err: string;
begin
  Section('click');
  DS := NewDS(nil);
  S := TDataSource.Create(nil);
  S.DataSet := DS;
  DS.Open;
  C := NewBox(F, S, 'flag');
  H := THandlers.Create;
  C.OnClick := H.Click;
  TakeBeeps;
  DS.Next;
  DS.Prior;
  CheckEqInt(H.Clicks, 0, 'no OnClick for state changes that come from the data');
  ClickBox(C);
  CheckEq(StateText(C), 'unchecked', 'checked -> unchecked');
  CheckEq(StateName(DS.State), 'dsEdit', 'the click starts the edit');
  CheckEqBool(DS.FieldByName('flag').AsBoolean, False, 'the field is written at once');
  CheckEqBool(DS.Modified, True, 'the dataset is modified');
  CheckEqInt(H.Clicks, 1, 'OnClick once');
  ClickBox(C);
  CheckEq(StateText(C), 'checked', 'unchecked -> checked');
  CheckEqBool(DS.FieldByName('flag').AsBoolean, True, 'written');
  DS.Post;
  CheckEq(DS.StoredText, '[1|1|Y] [2|0|no] [3|NULL|NULL] [4|1|maybe]', 'stored');

  C.AllowGrayed := True;
  ClickBox(C);
  CheckEq(StateText(C), 'grayed', 'AllowGrayed: checked -> grayed');
  CheckEqBool(DS.FieldByName('flag').IsNull, True, 'grayed clears the field');
  ClickBox(C);
  CheckEq(StateText(C), 'unchecked', 'grayed -> unchecked');
  CheckEqBool(DS.FieldByName('flag').IsNull, False, 'a value again');
  CheckEqBool(DS.FieldByName('flag').AsBoolean, False, 'False');
  DS.Cancel;
  CheckEq(StateText(C), 'checked', 'Cancel restores the state');
  C.AllowGrayed := False;

  DS.Last;
  DS.Prior;
  CheckEq(StateText(C), 'grayed', 'a null row');
  ClickBox(C);
  CheckEq(StateText(C), 'unchecked', 'grayed -> unchecked');
  CheckEqBool(DS.FieldByName('flag').IsNull, False, 'the null field gets a value');
  DS.Cancel;
  DS.First;


  Section('text field');
  C.DataField := 'yn';
  C.ValueChecked := 'Y;yes';
  C.ValueUnchecked := 'N;no';
  CheckEq(StateText(C), 'checked', 'Y');
  ClickBox(C);
  CheckEq(DS.FieldByName('yn').AsString, 'N', 'unchecked writes the first alternative of ValueUnchecked');
  ClickBox(C);
  CheckEq(DS.FieldByName('yn').AsString, 'Y', 'checked writes the first alternative of ValueChecked');
  C.AllowGrayed := True;
  ClickBox(C);
  CheckEqBool(DS.FieldByName('yn').IsNull, True, 'grayed clears the field');
  C.AllowGrayed := False;
  DS.Cancel;
  C.DataField := 'flag';

  Section('rejected and ignored clicks');
  H.Clicks := 0;
  CheckEqInt(TakeBeeps, 0, 'no beep so far');
  S.AutoEdit := False;
  ClickBox(C);
  CheckEq(StateText(C), 'checked', 'AutoEdit off: the state stays');
  CheckEq(StateName(DS.State), 'dsBrowse', 'AutoEdit off: no edit');
  CheckEqInt(TakeBeeps, 1, 'AutoEdit off: beep');
  CheckEqInt(H.Clicks, 0, 'AutoEdit off: no OnClick');
  DS.Edit;
  ClickBox(C);
  CheckEq(StateText(C), 'unchecked', 'after Edit by the application the click works');
  DS.Cancel;
  S.AutoEdit := True;
  H.Clicks := 0;
  C.ReadOnly := True;
  ClickBox(C);
  CheckEq(StateText(C), 'checked', 'ReadOnly: the click is ignored');
  CheckEq(StateName(DS.State), 'dsBrowse', 'ReadOnly: no edit');
  CheckEqInt(TakeBeeps, 0, 'ReadOnly: no beep');
  C.ReadOnly := False;
  DS.FieldByName('flag').ReadOnly := True;
  ClickBox(C);
  CheckEq(StateText(C), 'checked', 'a read-only field: ignored');
  DS.FieldByName('flag').ReadOnly := False;
  DS.Close;
  ClickBox(C);
  CheckEq(StateText(C), 'unchecked', 'closed dataset: ignored');
  CheckEqInt(H.Clicks, 0, 'no OnClick for ignored clicks');
  CheckEqBool(C.Enabled, True, 'never disabled');
  DS.Open;

  Section('assignment by code');
  C.Checked := False;
  CheckEq(StateText(C), 'unchecked', 'Checked assigned');
  CheckEq(StateName(DS.State), 'dsBrowse', 'no edit');
  CheckEqBool(DS.FieldByName('flag').AsBoolean, True, 'the field is not written');
  DS.Next;
  DS.Prior;
  CheckEq(StateText(C), 'checked', 'the control reloads');

  Section('a write the field rejects');
  DS.FieldByName('flag').OnValidate := H.Validate;
  H.Clicks := 0;
  Err := '';
  try
    ClickBox(C);
  except
    on E: Exception do
      Err := E.Message;
  end;
  CheckEq(Err, 'no change allowed', 'the error of the field leaves the click');
  CheckEqInt(H.Clicks, 0, 'no OnClick');
  CheckEq(StateText(C), 'checked', 'the state is restored');
  CheckEqBool(DS.FieldByName('flag').AsBoolean, True, 'the field keeps its value');
  DS.FieldByName('flag').OnValidate := nil;
  DS.Cancel;
  H.Free;
  C.Free;
  S.Free;
  DS.Free;
end;

procedure TestLifetime(F: TForm);
var
  DS: TTestDataSet;
  S: TDataSource;
  Box: TComponent;
  C: TDBCheckBox;
begin
  Section('lifetime');
  DS := NewDS(nil);
  DS.Open;
  Box := TComponent.Create(nil);
  S := TDataSource.Create(Box);
  S.DataSet := DS;
  C := NewBox(F, S, 'flag');
  CheckEq(StateText(C), 'checked', 'linked');
  Box.Free;
  Check(C.DataSource = nil, 'destroying the source clears DataSource');
  CheckEq(StateText(C), 'unchecked', 'and the state');
  C.Free;
  DS.Free;
end;

var
  F: TForm;
begin
  Application.Initialize;
  HookBeeps;
  F := NewForm;
  try
    TestState(F);
    TestClick(F);
    TestLifetime(F);
  finally
    F.Free;
  end;
  WriteLn('failed checks: ', FailedChecks);
end.
