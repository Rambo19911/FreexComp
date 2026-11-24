// covers: DAT-216, DAT-220, DAT-221, DAT-222, DAT-223, DAT-224, DAT-225, DAT-226, DAT-230, DAT-231, DAT-232, DAT-233, DAT-406
program CtlEdit;
{ TDBEdit on a shown form, driven with keyboard messages: display of the edit and display texts, automatic edit
  mode at the first modification and its rejection, write-back at Post, at navigation and at focus loss, focus
  kept after a failed write, Escape, Enter, the native read-only state, the input limit of string fields, the
  focus request of a field and the lifetime of the data source. }
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
    Changes: Integer;
    procedure GetText(Sender: TField; var Text: string; DisplayText: Boolean);
    procedure Validate(Sender: TField);
    procedure Change(Sender: TObject);
  end;

constructor TDesignRoot.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  SetDesigning(True);
end;

procedure THandlers.GetText(Sender: TField; var Text: string; DisplayText: Boolean);
begin
  if DisplayText then
    Text := '<' + Sender.AsString + '>'
  else
    Text := Sender.AsString;
end;

procedure THandlers.Validate(Sender: TField);
begin
  if Sender.AsString = 'bad' then
    raise Exception.Create('bad is not allowed');
end;

procedure THandlers.Change(Sender: TObject);
begin
  Inc(Changes);
end;

{ id, name (10), flag, data (blob); rows 1 n1 True bytes, 2 n2 False NULL, 3 NULL NULL NULL; not opened }
function NewDS(AOwner: TComponent): TTestDataSet;
var
  B: TBytes;
begin
  Result := TTestDataSet.Create(AOwner);
  Result.AddDef('id', ftInteger);
  Result.AddDef('name', ftString, 10);
  Result.AddDef('flag', ftBoolean);
  Result.AddDef('data', ftBlob);
  SetLength(B, 2);
  B[0] := 1;
  B[1] := 2;
  Result.AddRow([IntValue(1), StringValue('n1'), IntValue(1), BytesValue(B)]);
  Result.AddRow([IntValue(2), StringValue('n2'), IntValue(0), NullValue]);
  Result.AddRow([IntValue(3), NullValue, NullValue, NullValue]);
end;

function NewEdit(F: TForm; S: TDataSource; const FieldName: string; ATop: Integer): TDBEdit;
begin
  Result := TDBEdit.Create(F);
  Result.SetBounds(10, ATop, 150, 23);
  Result.Parent := F;
  Result.DataSource := S;
  Result.DataField := FieldName;
end;

procedure TestDefaults(F: TForm);
var
  E: TDBEdit;
  C: TControl;
begin
  Section('defaults');
  E := TDBEdit.Create(F);
  E.Parent := F;
  C := E;
  Check(C is TCustomEdit, 'an edit control');
  CheckEqInt(E.Width, 121, 'Width');
  CheckEqInt(E.Height, 23, 'Height');
  CheckEq(E.Text, '', 'Text is empty');
  CheckEqBool(E.ReadOnly, False, 'ReadOnly');
  CheckEqBool(E.TabStop, True, 'TabStop');
  CheckEqInt(E.MaxLength, 0, 'MaxLength');
  Check(E.DataSource = nil, 'DataSource');
  CheckEq(E.DataField, '', 'DataField');
  Check(E.Field = nil, 'Field');
  E.Name := 'NameEdit';
  CheckEq(E.Text, '', 'the name is not shown at run time');
  CheckEqBool(NativeReadOnly(E.Handle), True, 'not linked: the native control is read-only');
  CheckEqBool(E.Enabled, True, 'but never disabled');
  E.Free;
end;

procedure TestDisplay(F: TForm);
var
  DS: TTestDataSet;
  S: TDataSource;
  E, Other: TDBEdit;
  H: THandlers;
begin
  Section('display');
  DS := NewDS(nil);
  S := TDataSource.Create(nil);
  S.DataSet := DS;
  E := NewEdit(F, S, 'name', 10);
  Other := NewEdit(F, S, 'id', 40);
  CheckEq(E.Text, '', 'closed dataset: empty');
  Check(E.Field = nil, 'closed dataset: no field');
  DS.Open;
  CheckEq(E.Text, 'n1', 'Open: the value of the current row');
  CheckEq(WinText(E.Handle), 'n1', 'in the native control too');
  Check(E.Field = DS.FieldByName('name'), 'Field');
  CheckEq(Other.Text, '1', 'an integer field');
  DS.Next;
  CheckEq(E.Text, 'n2', 'Next');
  DS.Last;
  CheckEq(E.Text, '', 'a null value: empty');
  DS.First;
  DS.Edit;
  DS.FieldByName('name').AsString := 'app';
  CheckEq(E.Text, 'app', 'a field write by the application shows at once');
  DS.Cancel;
  CheckEq(E.Text, 'n1', 'Cancel restores');
  E.DataField := 'flag';
  CheckEq(E.Text, DS.FieldByName('flag').DisplayText, 'DataField changed');
  E.DataField := 'nope';
  CheckEq(E.Text, '', 'an unknown field: empty, no exception');
  Check(E.Field = nil, 'an unknown field: Field is nil');
  E.DataField := 'NAME';
  CheckEq(E.Text, 'n1', 'resolved again, without regard to case');
  S.Enabled := False;
  CheckEq(E.Text, '', 'a disabled source: empty');
  S.Enabled := True;
  CheckEq(E.Text, 'n1', 'enabled again');

  H := THandlers.Create;
  DS.FieldByName('name').OnGetText := H.GetText;
  DS.Next;
  CheckEq(E.Text, '<n2>', 'not focused: the display text');
  E.SetFocus;
  Check(F.ActiveControl = E, 'the edit is the active control');
  CheckEq(E.Text, 'n2', 'focused: the edit text');
  DS.Prior;
  CheckEq(E.Text, 'n1', 'focused: the edit text after a scroll');
  Other.SetFocus;
  CheckEq(E.Text, '<n1>', 'focus left: the display text again');
  DS.FieldByName('name').OnGetText := nil;
  DS.Next;
  DS.Prior;
  H.Free;

  E.DataField := 'data';
  CheckEq(E.Text, '(Blob)', 'a blob field shows (Blob)');
  E.SetFocus;
  CheckEq(E.Text, '(Blob)', 'also while focused');
  CheckEqBool(NativeReadOnly(E.Handle), True, 'a blob field is not editable');
  TypeText(E.Handle, 'x');
  CheckEq(E.Text, '(Blob)', 'typing changes nothing');
  CheckEq(StateName(DS.State), 'dsBrowse', 'and does not start an edit');
  Other.SetFocus;
  DS.Close;
  CheckEq(E.Text, '', 'Close: empty');
  E.Free;
  Other.Free;
  S.Free;
  DS.Free;
end;

procedure TestModify(F: TForm);
var
  DS: TTestDataSet;
  S: TDataSource;
  E, N: TDBEdit;
  H: THandlers;
  Err: string;
begin
  Section('auto-edit and write-back');
  DS := NewDS(nil);
  S := TDataSource.Create(nil);
  S.DataSet := DS;
  DS.Open;
  E := NewEdit(F, S, 'name', 10);
  N := NewEdit(F, S, 'id', 40);
  H := THandlers.Create;
  E.OnChange := H.Change;
  TakeBeeps;
  E.SetFocus;
  SendMessageW(E.Handle, EM_SETSEL, 2, 2);
  TypeText(E.Handle, 'x');
  CheckEq(E.Text, 'n1x', 'the typed character is in the control');
  CheckEq(StateName(DS.State), 'dsEdit', 'the first keystroke starts the edit');
  CheckEq(DS.FieldByName('name').AsString, 'n1', 'the field is not written yet');
  CheckEqBool(DS.Modified, False, 'the dataset is not modified yet');
  Check(H.Changes > 0, 'OnChange fired');
  TypeText(E.Handle, 'y');
  CheckEq(E.Text, 'n1xy', 'further typing continues at the caret');
  DS.Post;
  CheckEq(DS.FieldByName('name').AsString, 'n1xy', 'Post writes the text to the field');
  CheckEq(StateName(DS.State), 'dsBrowse', 'posted');
  CheckEq(DS.StoredText, '[1|n1xy|1|bytes(2)] [2|n2|0|NULL] [3|NULL|NULL|NULL]', 'stored');
  CheckEq(E.Text, 'n1xy', 'the control shows the stored value');

  SendMessageW(E.Handle, EM_SETSEL, 4, 4);
  SendMessageW(E.Handle, WM_CHAR, VK_BACK, 1);
  CheckEq(E.Text, 'n1x', 'Backspace');
  CheckEq(StateName(DS.State), 'dsEdit', 'Backspace starts the edit');
  DS.Next;
  CheckEq(StateName(DS.State), 'dsBrowse', 'navigation posts');
  CheckEq(DS.StoredText, '[1|n1x|1|bytes(2)] [2|n2|0|NULL] [3|NULL|NULL|NULL]', 'the text was written before the post decision');
  CheckEq(E.Text, 'n2', 'the next row');

  SendMessageW(E.Handle, EM_SETSEL, 0, 0);
  PressKey(E.Handle, VK_DELETE);
  CheckEq(E.Text, '2', 'Delete key');
  CheckEq(StateName(DS.State), 'dsEdit', 'the Delete key starts the edit');
  DS.Cancel;
  CheckEq(E.Text, 'n2', 'Cancel of the dataset restores the control');
  CheckEq(DS.FieldByName('name').AsString, 'n2', 'and nothing was written');

  SendMessageW(E.Handle, EM_SETSEL, 0, 1);
  SendMessageW(E.Handle, WM_CLEAR, 0, 0);
  CheckEq(E.Text, '2', 'WM_CLEAR removes the selection');
  CheckEq(StateName(DS.State), 'dsEdit', 'and starts the edit');
  DS.Cancel;
  CheckEqInt(TakeBeeps, 0, 'no beep so far');

  S.AutoEdit := False;
  SendMessageW(E.Handle, EM_SETSEL, 2, 2);
  TypeText(E.Handle, 'z');
  CheckEq(E.Text, 'n2', 'AutoEdit off: the keystroke is swallowed');
  CheckEq(StateName(DS.State), 'dsBrowse', 'AutoEdit off: no edit');
  CheckEqInt(TakeBeeps, 1, 'AutoEdit off: beep');
  SendMessageW(E.Handle, WM_CHAR, VK_BACK, 1);
  CheckEq(E.Text, 'n2', 'AutoEdit off: Backspace rejected');
  SendMessageW(E.Handle, EM_SETSEL, 0, 0);
  PressKey(E.Handle, VK_DELETE);
  CheckEq(E.Text, 'n2', 'AutoEdit off: Delete rejected');
  SendMessageW(E.Handle, EM_SETSEL, 0, 2);
  SendMessageW(E.Handle, WM_PASTE, 0, 0);
  CheckEq(E.Text, 'n2', 'AutoEdit off: paste rejected');
  SendMessageW(E.Handle, WM_CUT, 0, 0);
  CheckEq(E.Text, 'n2', 'AutoEdit off: cut rejected');
  SendMessageW(E.Handle, WM_CLEAR, 0, 0);
  CheckEq(E.Text, 'n2', 'AutoEdit off: clear rejected');
  SendMessageW(E.Handle, WM_UNDO, 0, 0);
  CheckEq(E.Text, 'n2', 'AutoEdit off: undo rejected');
  CheckEqInt(TakeBeeps, 6, 'a beep for every rejected attempt');
  CheckEqBool(NativeReadOnly(E.Handle), False, 'AutoEdit off: the native control is not read-only');
  S.Edit;
  SendMessageW(E.Handle, EM_SETSEL, 2, 2);
  TypeText(E.Handle, 'z');
  CheckEq(E.Text, 'n2z', 'after Edit by the application typing works');
  CheckEqInt(TakeBeeps, 0, 'no beep');
  DS.Post;
  CheckEq(DS.FieldByName('name').AsString, 'n2z', 'written at Post');
  S.AutoEdit := True;

  E.Text := 'code';
  CheckEq(StateName(DS.State), 'dsBrowse', 'Text assigned by code: no edit');
  DS.Prior;
  DS.Next;
  CheckEq(DS.FieldByName('name').AsString, 'n2z', 'and nothing is written');
  CheckEq(E.Text, 'n2z', 'the control reloads');

  Section('empty text');
  SendMessageW(E.Handle, EM_SETSEL, 0, -1);
  SendMessageW(E.Handle, WM_CHAR, VK_BACK, 1);
  CheckEq(E.Text, '', 'emptied');
  DS.Post;
  CheckEqBool(DS.FieldByName('name').IsNull, False, 'a string field stores an empty string');
  CheckEq(DS.FieldByName('name').AsString, '', 'which reads empty');
  N.SetFocus;
  SendMessageW(N.Handle, EM_SETSEL, 0, -1);
  SendMessageW(N.Handle, WM_CHAR, VK_BACK, 1);
  DS.Post;
  CheckEqBool(DS.FieldByName('id').IsNull, True, 'an integer field stores null');

  Section('a text the field rejects');
  TypeText(N.Handle, '12x');
  CheckEq(N.Text, '12x', 'typed');
  Err := '';
  try
    DS.Post;
  except
    on Ex: Exception do
      Err := Ex.ClassName;
  end;
  CheckEq(Err, 'EDatabaseError', 'Post raises the conversion error');
  CheckEq(StateName(DS.State), 'dsEdit', 'the dataset stays in edit mode');
  CheckEq(N.Text, '12x', 'the control keeps the text');
  SendMessageW(N.Handle, WM_CHAR, VK_BACK, 1);
  DS.Post;
  CheckEq(DS.FieldByName('id').AsString, '12', 'corrected and posted');
  CheckEq(N.Text, '12', 'shown');
  H.Free;
  E.Free;
  N.Free;
  S.Free;
  DS.Free;
end;

procedure TestFocusLoss(F: TForm);
var
  DS: TTestDataSet;
  S: TDataSource;
  E, N: TDBEdit;
  H: THandlers;
  Err: string;
begin
  Section('focus loss');
  DS := NewDS(nil);
  S := TDataSource.Create(nil);
  S.DataSet := DS;
  DS.Open;
  E := NewEdit(F, S, 'name', 10);
  N := NewEdit(F, S, 'id', 40);
  H := THandlers.Create;
  DS.FieldByName('name').OnValidate := H.Validate;
  E.SetFocus;
  SendMessageW(E.Handle, EM_SETSEL, 2, 2);
  TypeText(E.Handle, 'a');
  CheckEq(DS.FieldByName('name').AsString, 'n1', 'before leaving: not written');
  CheckEq(FocusError(N), '', 'leaving with a valid text');
  Check(F.ActiveControl = N, 'the focus moved');
  CheckEq(DS.FieldByName('name').AsString, 'n1a', 'the text was written to the field');
  CheckEq(StateName(DS.State), 'dsEdit', 'the record is not posted by the focus change');
  CheckEqBool(DS.Modified, True, 'the dataset is modified');

  SendMessageW(N.Handle, EM_SETSEL, 0, -1);
  TypeText(N.Handle, 'abc');
  Err := FocusError(E);
  Check(Pos('EDatabaseError', Err) = 1, 'leaving with a text the field cannot take raises');
  Check(F.ActiveControl = N, 'the control took the focus back');
  CheckEq(N.Text, 'abc', 'and keeps the text');
  CheckEq(DS.FieldByName('id').AsString, '1', 'the field is unchanged');
  Err := FocusError(E);
  Check(Pos('EDatabaseError', Err) = 1, 'a second attempt raises again');
  PressKey(N.Handle, VK_ESCAPE);
  CheckEq(N.Text, '1', 'Escape restores the field value');
  CheckEq(FocusError(E), '', 'now the focus can leave');
  Check(F.ActiveControl = E, 'the focus moved');

  SendMessageW(E.Handle, EM_SETSEL, 0, -1);
  TypeText(E.Handle, 'bad');
  Err := FocusError(N);
  CheckEq(Err, 'Exception: bad is not allowed', 'OnValidate rejects the text at focus loss');
  Check(F.ActiveControl = E, 'the focus stays');
  CheckEq(E.Text, 'bad', 'the text stays');
  CheckEq(DS.FieldByName('name').AsString, 'n1a', 'the field keeps its value');
  TypeText(E.Handle, 'ge');
  CheckEq(FocusError(N), '', 'a corrected text can leave');
  CheckEq(DS.FieldByName('name').AsString, 'badge', 'and is written');
  DS.Cancel;
  CheckEq(E.Text, 'n1', 'Cancel restores');
  N.SetFocus;
  E.SetFocus;
  CheckEq(StateName(DS.State), 'dsBrowse', 'focus changes without a modification do nothing');
  H.Free;
  E.Free;
  N.Free;
  S.Free;
  DS.Free;
end;

procedure TestKeys(F: TForm);
var
  DS: TTestDataSet;
  S: TDataSource;
  E: TDBEdit;
begin
  Section('Escape');
  DS := NewDS(nil);
  S := TDataSource.Create(nil);
  S.DataSet := DS;
  DS.Open;
  E := NewEdit(F, S, 'name', 10);
  E.SetFocus;
  CheckEqInt(E.Perform(CM_WANTSPECIALKEY, VK_ESCAPE, 0), 0, 'nothing pending: Escape is not wanted');
  PressKey(E.Handle, VK_ESCAPE);
  CheckEq(E.Text, 'n1', 'nothing pending: nothing happens');
  SendMessageW(E.Handle, EM_SETSEL, 2, 2);
  TypeText(E.Handle, 'q');
  CheckEqInt(E.Perform(CM_WANTSPECIALKEY, VK_ESCAPE, 0), 1, 'pending: Escape is wanted by the control');
  PressKey(E.Handle, VK_ESCAPE);
  CheckEq(E.Text, 'n1', 'pending: Escape restores the field value');
  CheckEq(StateName(DS.State), 'dsEdit', 'the record stays in edit mode');
  CheckEqInt(E.Perform(CM_WANTSPECIALKEY, VK_ESCAPE, 0), 0, 'after the reset Escape is not wanted');
  DS.Post;
  CheckEq(DS.FieldByName('name').AsString, 'n1', 'nothing was written');

  Section('Enter');
  SendMessageW(E.Handle, EM_SETSEL, 2, 2);
  TypeText(E.Handle, 'r');
  CheckEqInt(E.Perform(CM_WANTSPECIALKEY, VK_RETURN, 0), 0, 'Enter is never wanted');
  PressKey(E.Handle, VK_RETURN);
  SendMessageW(E.Handle, WM_CHAR, VK_RETURN, 1);
  CheckEq(E.Text, 'n1r', 'Enter leaves the text');
  CheckEq(StateName(DS.State), 'dsEdit', 'Enter does not post');
  CheckEq(DS.FieldByName('name').AsString, 'n1', 'and does not write');
  DS.Cancel;
  E.Free;
  S.Free;
  DS.Free;
end;

procedure TestReadOnly(F: TForm);
var
  DS: TTestDataSet;
  S: TDataSource;
  E: TDBEdit;
begin
  Section('read-only state');
  DS := NewDS(nil);
  S := TDataSource.Create(nil);
  E := NewEdit(F, S, 'name', 10);
  CheckEqBool(NativeReadOnly(E.Handle), True, 'no dataset: read-only');
  S.DataSet := DS;
  CheckEqBool(NativeReadOnly(E.Handle), True, 'closed dataset: read-only');
  DS.Open;
  CheckEqBool(NativeReadOnly(E.Handle), False, 'open dataset: editable');
  E.ReadOnly := True;
  CheckEqBool(E.ReadOnly, True, 'ReadOnly reads back');
  CheckEqBool(NativeReadOnly(E.Handle), True, 'ReadOnly: the native control is read-only');
  TypeText(E.Handle, 'x');
  CheckEq(E.Text, 'n1', 'ReadOnly: typing changes nothing');
  CheckEq(StateName(DS.State), 'dsBrowse', 'ReadOnly: no edit');
  E.ReadOnly := False;
  CheckEqBool(NativeReadOnly(E.Handle), False, 'ReadOnly off: editable again');
  DS.FieldByName('name').ReadOnly := True;
  DS.Next;
  CheckEqBool(NativeReadOnly(E.Handle), True, 'a read-only field: read-only at the next data change');
  DS.FieldByName('name').ReadOnly := False;
  DS.Prior;
  CheckEqBool(NativeReadOnly(E.Handle), False, 'field writable again');
  E.DataField := 'nope';
  CheckEqBool(NativeReadOnly(E.Handle), True, 'an unknown field: read-only');
  E.DataField := 'name';
  CheckEqBool(NativeReadOnly(E.Handle), False, 'resolved: editable');
  S.Enabled := False;
  CheckEqBool(NativeReadOnly(E.Handle), True, 'a disabled source: read-only');
  S.Enabled := True;
  DS.Close;
  CheckEqBool(NativeReadOnly(E.Handle), True, 'closed: read-only');
  DS.DriverReadOnly := True;
  DS.Open;
  CheckEqBool(NativeReadOnly(E.Handle), True, 'a dataset that cannot be modified: read-only');
  DS.Close;
  DS.DriverReadOnly := False;
  DS.FixedField := 'name';
  DS.Open;
  CheckEqBool(NativeReadOnly(E.Handle), True, 'a column that cannot be updated: read-only');
  CheckEqBool(E.Enabled, True, 'the control is never disabled by the link');
  E.Free;
  S.Free;
  DS.Free;
end;

procedure TestMaxLength(F: TForm);
var
  DS: TTestDataSet;
  S: TDataSource;
  E: TDBEdit;
begin
  Section('input limit');
  DS := NewDS(nil);
  S := TDataSource.Create(nil);
  S.DataSet := DS;
  E := NewEdit(F, S, 'name', 10);
  DS.Open;
  CheckEqInt(SendMessageW(E.Handle, EM_GETLIMITTEXT, 0, 0), 10, 'a string field of size 10 limits the input to 10');
  CheckEqInt(E.MaxLength, 0, 'MaxLength stays 0');
  E.SetFocus;
  SendMessageW(E.Handle, EM_SETSEL, 0, -1);
  TypeText(E.Handle, '123456789012');
  CheckEq(E.Text, '1234567890', 'typing stops at the limit');
  DS.Cancel;
  E.DataField := 'id';
  Check(SendMessageW(E.Handle, EM_GETLIMITTEXT, 0, 0) > 10, 'an integer field: no limit from the field');
  E.DataField := 'name';
  CheckEqInt(SendMessageW(E.Handle, EM_GETLIMITTEXT, 0, 0), 10, 'the string field again');
  E.MaxLength := 4;
  CheckEqInt(SendMessageW(E.Handle, EM_GETLIMITTEXT, 0, 0), 4, 'MaxLength set: it wins');
  DS.Next;
  CheckEqInt(SendMessageW(E.Handle, EM_GETLIMITTEXT, 0, 0), 4, 'also after a data change');
  DS.Close;
  DS.Open;
  CheckEqInt(SendMessageW(E.Handle, EM_GETLIMITTEXT, 0, 0), 4, 'and after a reopen');
  E.Free;
  S.Free;
  DS.Free;
end;

procedure TestFocusControl(F: TForm);
var
  DS: TTestDataSet;
  S: TDataSource;
  E, N: TDBEdit;
begin
  Section('focus request of a field');
  DS := NewDS(nil);
  S := TDataSource.Create(nil);
  S.DataSet := DS;
  DS.Open;
  E := NewEdit(F, S, 'name', 10);
  N := NewEdit(F, S, 'id', 40);
  E.SetFocus;
  DS.FieldByName('id').FocusControl;
  Check(F.ActiveControl = N, 'the control of the field takes the focus');
  DS.FieldByName('name').FocusControl;
  Check(F.ActiveControl = E, 'and the other one');
  DS.FieldByName('flag').FocusControl;
  Check(F.ActiveControl = E, 'a field without a control: nothing');
  N.Visible := False;
  DS.FieldByName('id').FocusControl;
  Check(F.ActiveControl = E, 'a control that cannot take the focus: nothing');
  E.Free;
  N.Free;
  S.Free;
  DS.Free;
end;

procedure TestLifetime(F: TForm);
var
  DS: TTestDataSet;
  S: TDataSource;
  Box: TComponent;
  E: TDBEdit;
begin
  Section('lifetime');
  DS := NewDS(nil);
  DS.Open;
  Box := TComponent.Create(nil);
  S := TDataSource.Create(Box);
  S.DataSet := DS;
  E := NewEdit(F, S, 'name', 10);
  CheckEq(E.Text, 'n1', 'linked');
  Box.Free;
  Check(E.DataSource = nil, 'destroying the source clears DataSource');
  CheckEq(E.Text, '', 'and the text');
  CheckEqBool(NativeReadOnly(E.Handle), True, 'read-only again');
  S := TDataSource.Create(nil);
  S.DataSet := DS;
  E.DataSource := S;
  CheckEq(E.Text, 'n1', 'attached to a new source');
  E.SetFocus;
  SendMessageW(E.Handle, EM_SETSEL, 2, 2);
  TypeText(E.Handle, 'k');
  E.Free;
  CheckEq(StateName(DS.State), 'dsEdit', 'destroying the control leaves the dataset alone');
  DS.Cancel;
  DS.Next;
  CheckEq(DS.FieldByName('name').AsString, 'n2', 'the dataset works without the control');
  S.Free;
  DS.Free;
end;

procedure TestDesignTime;
var
  Root: TDesignRoot;
  DS: TTestDataSet;
  S: TDataSource;
  E: TDBEdit;
begin
  Section('design time');
  Root := TDesignRoot.Create(nil);
  DS := NewDS(nil);
  S := TDataSource.Create(nil);
  S.DataSet := DS;
  E := TDBEdit.Create(Root);
  E.Name := 'DBEdit1';
  CheckEq(E.Text, 'DBEdit1', 'shows its name');
  E.DataSource := S;
  E.DataField := 'name';
  DS.Open;
  CheckEq(E.Text, 'DBEdit1', 'also with an open dataset');
  E.Name := 'NameEdit';
  CheckEq(E.Text, 'NameEdit', 'follows a new name');
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
    TestDefaults(F);
    TestDisplay(F);
    TestModify(F);
    TestFocusLoss(F);
    TestKeys(F);
    TestReadOnly(F);
    TestMaxLength(F);
    TestFocusControl(F);
    TestLifetime(F);
  finally
    F.Free;
  end;
  TestDesignTime;
  WriteLn('failed checks: ', FailedChecks);
end.
