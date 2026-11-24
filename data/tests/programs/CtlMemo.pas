// covers: DAT-220, DAT-221, DAT-222, DAT-223, DAT-224, DAT-225, DAT-226, DAT-238, DAT-239, DAT-406
program CtlMemo;
{ TDBMemo on a shown form: the text of string and memo fields with line breaks normalised for display, other
  field types read-only, automatic edit mode and its rejection, Enter as a line break, write-back without an
  added line break, focus-loss validation, Escape, the read-only state, design time and lifetime. }
uses
  Windows, Messages, SysUtils, Classes, Controls, Forms, StdCtrls, DB, DBCtrls, DataTestUtils, TestDataSet,
  CtlTestUtils;

type
  TDesignRoot = class(TComponent)
  public
    constructor Create(AOwner: TComponent); override;
  end;

  THandlers = class
  public
    procedure Validate(Sender: TField);
  end;

constructor TDesignRoot.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  SetDesigning(True);
end;

procedure THandlers.Validate(Sender: TField);
begin
  if Sender.AsString = 'bad' then
    raise Exception.Create('bad is not allowed');
end;

{ id, name (10), note (memo); rows 1 n1 'line1<LF>line2', 2 n2 'a<CR><LF>b', 3 NULL NULL; not opened }
function NewDS(AOwner: TComponent): TTestDataSet;
begin
  Result := TTestDataSet.Create(AOwner);
  Result.AddDef('id', ftInteger);
  Result.AddDef('name', ftString, 10);
  Result.AddDef('note', ftMemo);
  Result.AddRow([IntValue(1), StringValue('n1'), StringValue('line1'#10'line2')]);
  Result.AddRow([IntValue(2), StringValue('n2'), StringValue('a'#13#10'b')]);
  Result.AddRow([IntValue(3), NullValue, NullValue]);
end;

function NewMemo(F: TForm; S: TDataSource; const FieldName: string; ATop: Integer): TDBMemo;
begin
  Result := TDBMemo.Create(F);
  Result.SetBounds(10, ATop, 200, 80);
  Result.Parent := F;
  Result.DataSource := S;
  Result.DataField := FieldName;
end;

procedure CaretToEnd(M: TDBMemo);
var
  N: Integer;
begin
  N := Length(M.Text);
  SendMessageW(M.Handle, EM_SETSEL, WPARAM(N), LPARAM(N));
end;

procedure TestDisplay(F: TForm);
var
  DS: TTestDataSet;
  S: TDataSource;
  M: TDBMemo;
  C: TControl;
begin
  Section('defaults and display');
  DS := NewDS(nil);
  S := TDataSource.Create(nil);
  S.DataSet := DS;
  M := TDBMemo.Create(F);
  M.Parent := F;
  C := M;
  Check(C is TCustomMemo, 'a memo control');
  CheckEqInt(M.Width, 185, 'Width');
  CheckEqInt(M.Height, 89, 'Height');
  CheckEqBool(M.ReadOnly, False, 'ReadOnly');
  Check(M.DataSource = nil, 'DataSource');
  Check(M.Field = nil, 'Field');
  M.Name := 'NoteMemo';
  CheckEq(M.Text, '', 'the name is not shown at run time');
  CheckEqBool(NativeReadOnly(M.Handle), True, 'not linked: read-only');
  M.DataSource := S;
  M.DataField := 'note';
  CheckEq(M.Text, '', 'closed dataset: empty');
  DS.Open;
  Check(M.Field = DS.FieldByName('note'), 'Field');
  CheckEq(Visible(M.Text), 'line1\r\nline2', 'a lone line feed is shown as a line break');
  CheckEq(Visible(DS.FieldByName('note').AsString), 'line1\nline2', 'the field keeps what is stored');
  CheckEqBool(NativeReadOnly(M.Handle), False, 'a memo field is editable');
  DS.Next;
  CheckEq(Visible(M.Text), 'a\r\nb', 'a stored line break stays');
  DS.Next;
  CheckEq(M.Text, '', 'a null value: empty');
  DS.First;
  DS.Edit;
  DS.FieldByName('note').AsString := 'x'#13'y'#10#13'z';
  CheckEq(Visible(M.Text), 'x\r\ny\r\n\r\nz', 'lone carriage returns too');
  DS.Cancel;
  M.DataField := 'name';
  CheckEq(M.Text, 'n1', 'a string field');
  CheckEqBool(NativeReadOnly(M.Handle), False, 'a string field is editable');
  M.DataField := 'id';
  CheckEq(M.Text, '1', 'another field type: its display text');
  CheckEqBool(NativeReadOnly(M.Handle), True, 'another field type: read-only');
  TypeText(M.Handle, '5');
  CheckEq(M.Text, '1', 'typing changes nothing');
  CheckEq(StateName(DS.State), 'dsBrowse', 'no edit');
  M.DataField := 'nope';
  CheckEq(M.Text, '', 'an unknown field: empty');
  M.DataField := 'note';
  DS.Close;
  CheckEq(M.Text, '', 'Close: empty');
  M.Free;
  S.Free;
  DS.Free;
end;

procedure TestModify(F: TForm);
var
  DS: TTestDataSet;
  S: TDataSource;
  M: TDBMemo;
begin
  Section('auto-edit and write-back');
  DS := NewDS(nil);
  S := TDataSource.Create(nil);
  S.DataSet := DS;
  DS.Open;
  M := NewMemo(F, S, 'note', 10);
  TakeBeeps;
  M.SetFocus;
  CaretToEnd(M);
  TypeText(M.Handle, '!');
  CheckEq(Visible(M.Text), 'line1\r\nline2!', 'typed');
  CheckEq(StateName(DS.State), 'dsEdit', 'the first keystroke starts the edit');
  CheckEq(Visible(DS.FieldByName('note').AsString), 'line1\nline2', 'not written yet');
  SendMessageW(M.Handle, WM_CHAR, VK_RETURN, 1);
  TypeText(M.Handle, 'line3');
  CheckEq(Visible(M.Text), 'line1\r\nline2!\r\nline3', 'Enter inserts a line break');
  CheckEq(StateName(DS.State), 'dsEdit', 'Enter does not post');
  CheckEqInt(M.Perform(CM_WANTSPECIALKEY, VK_RETURN, 0), 1, 'Enter belongs to the memo');
  DS.Post;
  CheckEq(Visible(DS.FieldByName('note').AsString), 'line1\r\nline2!\r\nline3', 'Post writes the text with line breaks, nothing added');
  CheckEq(StateName(DS.State), 'dsBrowse', 'posted');

  SendMessageW(M.Handle, EM_SETSEL, 0, -1);
  SendMessageW(M.Handle, WM_CHAR, VK_BACK, 1);
  CheckEq(M.Text, '', 'emptied');
  DS.Next;
  DS.Prior;
  CheckEqBool(DS.FieldByName('note').IsNull, False, 'an empty memo stores an empty string');
  CheckEq(DS.FieldByName('note').AsString, '', 'which reads empty');
  TypeText(M.Handle, 'abc');
  DS.Post;
  CheckEq(Visible(DS.FieldByName('note').AsString), 'abc', 'one line: no trailing line break');
  CheckEqInt(TakeBeeps, 0, 'no beep so far');

  S.AutoEdit := False;
  CaretToEnd(M);
  TypeText(M.Handle, 'd');
  SendMessageW(M.Handle, WM_CHAR, VK_RETURN, 1);
  SendMessageW(M.Handle, WM_CHAR, VK_BACK, 1);
  PressKey(M.Handle, VK_DELETE);
  CheckEq(M.Text, 'abc', 'AutoEdit off: every attempt is swallowed');
  CheckEq(StateName(DS.State), 'dsBrowse', 'AutoEdit off: no edit');
  CheckEqInt(TakeBeeps, 4, 'AutoEdit off: a beep each');
  S.AutoEdit := True;
  M.Text := 'code';
  CheckEq(StateName(DS.State), 'dsBrowse', 'Text assigned by code: no edit');
  DS.Next;
  DS.Prior;
  CheckEq(M.Text, 'abc', 'the control reloads');
  M.Free;
  S.Free;
  DS.Free;
end;

procedure TestFocusAndEscape(F: TForm);
var
  DS: TTestDataSet;
  S: TDataSource;
  M: TDBMemo;
  E: TDBEdit;
  H: THandlers;
begin
  Section('focus loss');
  DS := NewDS(nil);
  S := TDataSource.Create(nil);
  S.DataSet := DS;
  DS.Open;
  M := NewMemo(F, S, 'note', 10);
  E := TDBEdit.Create(F);
  E.SetBounds(10, 100, 150, 23);
  E.Parent := F;
  E.DataSource := S;
  E.DataField := 'name';
  H := THandlers.Create;
  DS.FieldByName('note').OnValidate := H.Validate;
  M.SetFocus;
  CaretToEnd(M);
  TypeText(M.Handle, '?');
  CheckEq(FocusError(E), '', 'leaving with a valid text');
  CheckEq(Visible(DS.FieldByName('note').AsString), 'line1\r\nline2?', 'the text was written');
  CheckEq(StateName(DS.State), 'dsEdit', 'not posted');
  M.SetFocus;
  SendMessageW(M.Handle, EM_SETSEL, 0, -1);
  TypeText(M.Handle, 'bad');
  CheckEq(FocusError(E), 'Exception: bad is not allowed', 'a rejected text raises at focus loss');
  Check(F.ActiveControl = M, 'the memo took the focus back');
  CheckEq(M.Text, 'bad', 'and keeps the text');

  Section('Escape');
  CheckEqInt(M.Perform(CM_WANTSPECIALKEY, VK_ESCAPE, 0), 1, 'pending: Escape is wanted');
  PressKey(M.Handle, VK_ESCAPE);
  CheckEq(Visible(M.Text), 'line1\r\nline2?', 'Escape restores the field value');
  CheckEq(StateName(DS.State), 'dsEdit', 'the record stays in edit mode');
  CheckEqInt(M.Perform(CM_WANTSPECIALKEY, VK_ESCAPE, 0), 0, 'nothing pending: Escape is not wanted');
  CheckEq(FocusError(E), '', 'the focus can leave');
  DS.Cancel;
  CheckEq(Visible(M.Text), 'line1\r\nline2', 'Cancel restores the stored text');
  H.Free;
  M.Free;
  E.Free;
  S.Free;
  DS.Free;
end;

procedure TestReadOnlyAndLifetime(F: TForm);
var
  DS: TTestDataSet;
  S: TDataSource;
  Box: TComponent;
  M: TDBMemo;
begin
  Section('read-only state');
  DS := NewDS(nil);
  Box := TComponent.Create(nil);
  S := TDataSource.Create(Box);
  S.DataSet := DS;
  M := NewMemo(F, S, 'note', 10);
  CheckEqBool(NativeReadOnly(M.Handle), True, 'closed dataset: read-only');
  DS.Open;
  CheckEqBool(NativeReadOnly(M.Handle), False, 'open: editable');
  M.ReadOnly := True;
  CheckEqBool(NativeReadOnly(M.Handle), True, 'ReadOnly: read-only');
  TypeText(M.Handle, 'x');
  CheckEq(StateName(DS.State), 'dsBrowse', 'ReadOnly: no edit');
  CheckEq(Visible(M.Text), 'line1\r\nline2', 'ReadOnly: no change');
  M.ReadOnly := False;
  CheckEqBool(NativeReadOnly(M.Handle), False, 'ReadOnly off: editable');
  DS.Close;
  DS.DriverReadOnly := True;
  DS.Open;
  CheckEqBool(NativeReadOnly(M.Handle), True, 'a dataset that cannot be modified: read-only');
  CheckEqBool(M.Enabled, True, 'never disabled');

  Section('lifetime');
  Box.Free;
  Check(M.DataSource = nil, 'destroying the source clears DataSource');
  CheckEq(M.Text, '', 'and the text');
  M.Free;
  DS.Free;
end;

procedure TestDesignTime;
var
  Root: TDesignRoot;
  DS: TTestDataSet;
  S: TDataSource;
  M: TDBMemo;
begin
  Section('design time');
  Root := TDesignRoot.Create(nil);
  DS := NewDS(nil);
  S := TDataSource.Create(nil);
  S.DataSet := DS;
  M := TDBMemo.Create(Root);
  M.Name := 'DBMemo1';
  CheckEq(M.Text, 'DBMemo1', 'shows its name');
  M.DataSource := S;
  M.DataField := 'note';
  DS.Open;
  CheckEq(M.Text, 'DBMemo1', 'also with an open dataset');
  Root.Free;
  S.Free;
  DS.Free;
end;

var
  F: TForm;
begin
  Application.Initialize;
  HookBeeps;
  F := NewForm;
  try
    TestDisplay(F);
    TestModify(F);
    TestFocusAndEscape(F);
    TestReadOnlyAndLifetime(F);
  finally
    F.Free;
  end;
  TestDesignTime;
  WriteLn('failed checks: ', FailedChecks);
end.
