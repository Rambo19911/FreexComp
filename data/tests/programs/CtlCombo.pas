// covers: DAT-220, DAT-221, DAT-222, DAT-223, DAT-224, DAT-225, DAT-226, DAT-246, DAT-247, DAT-406
program CtlCombo;
{ TDBComboBox on a shown form, in both styles: display of the field text, a selection that starts the edit and
  is written at once, typed text written at Post or focus loss, the rejection without AutoEdit, ignored
  changes in the read-only state, Escape and the lifetime of the data source. }
uses
  Windows, Messages, SysUtils, Classes, Controls, Forms, StdCtrls, DB, DBCtrls, DataTestUtils, TestDataSet,
  CtlTestUtils;

type
  THandlers = class
  public
    Log: string;
    procedure Change(Sender: TObject);
    procedure Click(Sender: TObject);
    procedure Validate(Sender: TField);
  end;

procedure THandlers.Change(Sender: TObject);
begin
  Log := Log + 'change;';
end;

procedure THandlers.Click(Sender: TObject);
begin
  Log := Log + 'click;';
end;

procedure THandlers.Validate(Sender: TField);
begin
  if Sender.AsString = 'bad' then
    raise Exception.Create('bad is not allowed');
end;

{ id, name (10); rows 1 red, 2 green, 3 NULL, 4 other; not opened }
function NewDS(AOwner: TComponent): TTestDataSet;
begin
  Result := TTestDataSet.Create(AOwner);
  Result.AddDef('id', ftInteger);
  Result.AddDef('name', ftString, 10);
  Result.AddRow([IntValue(1), StringValue('red')]);
  Result.AddRow([IntValue(2), StringValue('green')]);
  Result.AddRow([IntValue(3), NullValue]);
  Result.AddRow([IntValue(4), StringValue('other')]);
end;

function NewCombo(F: TForm; S: TDataSource; AStyle: TComboBoxStyle): TDBComboBox;
begin
  Result := TDBComboBox.Create(F);
  Result.SetBounds(10, 10, 150, 23);
  Result.Parent := F;
  Result.Style := AStyle;
  Result.Items.Add('red');
  Result.Items.Add('green');
  Result.Items.Add('blue');
  Result.DataSource := S;
  Result.DataField := 'name';
end;

{ the edit window inside a combo box of the drop-down style }
function InnerEdit(C: TDBComboBox): HWND;
begin
  Result := GetWindow(C.Handle, GW_CHILD);
end;

procedure TestDropDown(F: TForm);
var
  DS: TTestDataSet;
  S: TDataSource;
  C: TDBComboBox;
  E: TDBEdit;
  H: THandlers;
  K: TControl;
  Ed: HWND;
begin
  Section('drop-down style: display');
  DS := NewDS(nil);
  S := TDataSource.Create(nil);
  S.DataSet := DS;
  C := TDBComboBox.Create(F);
  C.Parent := F;
  K := C;
  Check(K is TCustomComboBox, 'a combo box control');
  Check(C.Style = csDropDown, 'Style is csDropDown by default');
  CheckEqBool(C.ReadOnly, False, 'ReadOnly');
  Check(C.DataSource = nil, 'DataSource');
  Check(C.Field = nil, 'Field');
  C.Free;
  C := NewCombo(F, S, csDropDown);
  E := TDBEdit.Create(F);
  E.SetBounds(10, 50, 150, 23);
  E.Parent := F;
  E.DataSource := S;
  E.DataField := 'id';
  H := THandlers.Create;
  C.OnChange := H.Change;
  C.OnClick := H.Click;
  Ed := InnerEdit(C);
  CheckEq(C.Text, '', 'closed dataset: empty');
  DS.Open;
  Check(C.Field = DS.FieldByName('name'), 'Field');
  CheckEq(C.Text, 'red', 'Open: the field text');
  CheckEq(WinText(Ed), 'red', 'in the edit part');
  DS.Next;
  CheckEq(C.Text, 'green', 'Next');
  DS.Next;
  CheckEq(C.Text, '', 'a null value: empty');
  DS.Next;
  CheckEq(C.Text, 'other', 'a text that is not in the list is shown');
  DS.First;
  CheckEq(H.Log, '', 'no events for changes that come from the data');

  Section('drop-down style: selection');
  TakeBeeps;
  C.SetFocus;
  SendMessageW(Ed, WM_KEYDOWN, VK_DOWN, 0);
  CheckEq(C.Text, 'green', 'Down selects the next item');
  CheckEq(StateName(DS.State), 'dsEdit', 'the selection starts the edit');
  CheckEq(DS.FieldByName('name').AsString, 'green', 'and is written at once');
  CheckEq(H.Log, 'change;click;', 'OnChange and OnClick');
  DS.Post;
  CheckEq(DS.StoredText, '[1|green] [2|green] [3|NULL] [4|other]', 'stored');

  Section('drop-down style: typing');
  H.Log := '';
  SendMessageW(Ed, EM_SETSEL, 5, 5);
  TypeText(Ed, 'x');
  CheckEq(C.Text, 'greenx', 'typed');
  CheckEq(StateName(DS.State), 'dsEdit', 'typing starts the edit');
  CheckEq(DS.FieldByName('name').AsString, 'green', 'typed text is not written yet');
  CheckEq(H.Log, 'change;', 'OnChange');
  DS.Post;
  CheckEq(DS.FieldByName('name').AsString, 'greenx', 'Post writes the typed text');
  SendMessageW(Ed, EM_SETSEL, 6, 6);
  TypeText(Ed, 'y');
  CheckEq(FocusError(E), '', 'the focus leaves');
  CheckEq(DS.FieldByName('name').AsString, 'greenxy', 'focus loss writes the typed text');
  CheckEq(StateName(DS.State), 'dsEdit', 'without posting');
  DS.Cancel;
  CheckEq(C.Text, 'greenx', 'Cancel restores');

  DS.FieldByName('name').OnValidate := H.Validate;
  C.SetFocus;
  SendMessageW(Ed, EM_SETSEL, 0, -1);
  TypeText(Ed, 'bad');
  CheckEq(FocusError(E), 'Exception: bad is not allowed', 'a rejected text raises at focus loss');
  Check(F.ActiveControl = C, 'the combo box took the focus back');
  CheckEq(C.Text, 'bad', 'and keeps the text');

  Section('drop-down style: Escape');
  CheckEqInt(C.Perform(CM_WANTSPECIALKEY, VK_ESCAPE, 0), 1, 'pending: Escape is consumed');
  PressKey(Ed, VK_ESCAPE);
  CheckEq(C.Text, 'greenx', 'and restores the field value');
  CheckEq(StateName(DS.State), 'dsEdit', 'the record stays in edit mode');
  CheckEqInt(C.Perform(CM_WANTSPECIALKEY, VK_ESCAPE, 0), 0, 'nothing pending: Escape is not consumed');
  CheckEqInt(C.Perform(CM_WANTSPECIALKEY, VK_RETURN, 0), 0, 'Enter is not consumed');
  DS.Cancel;
  DS.FieldByName('name').OnValidate := nil;
  CheckEqInt(TakeBeeps, 0, 'no beep so far');

  Section('drop-down style: rejected and ignored changes');
  H.Log := '';
  S.AutoEdit := False;
  SendMessageW(Ed, EM_SETSEL, 6, 6);
  TypeText(Ed, 'z');
  CheckEq(C.Text, 'greenx', 'AutoEdit off: the typed character is taken back');
  CheckEq(StateName(DS.State), 'dsBrowse', 'AutoEdit off: no edit');
  CheckEqInt(TakeBeeps, 1, 'AutoEdit off: beep');
  SendMessageW(Ed, WM_KEYDOWN, VK_DOWN, 0);
  Application.ProcessMessages;
  CheckEq(C.Text, 'greenx', 'AutoEdit off: the selection is taken back');
  CheckEq(StateName(DS.State), 'dsBrowse', 'AutoEdit off: still no edit');
  CheckEqInt(TakeBeeps, 1, 'AutoEdit off: beep');
  CheckEq(H.Log, '', 'no events for rejected changes');
  S.AutoEdit := True;
  C.ReadOnly := True;
  TypeText(Ed, 'z');
  SendMessageW(Ed, WM_KEYDOWN, VK_DOWN, 0);
  Application.ProcessMessages;
  CheckEq(C.Text, 'greenx', 'ReadOnly: changes are ignored');
  CheckEq(StateName(DS.State), 'dsBrowse', 'ReadOnly: no edit');
  CheckEqInt(TakeBeeps, 0, 'ReadOnly: no beep');
  C.ReadOnly := False;
  S.Enabled := False;
  TypeText(Ed, 'z');
  CheckEq(C.Text, '', 'inactive link: changes are ignored');
  CheckEqBool(C.Enabled, True, 'never disabled');
  S.Enabled := True;
  CheckEq(C.Text, 'greenx', 'active again');

  C.Text := 'code';
  CheckEq(StateName(DS.State), 'dsBrowse', 'Text assigned by code: no edit');
  DS.Next;
  DS.Prior;
  CheckEq(C.Text, 'greenx', 'the control reloads');
  CheckEq(H.Log, '', 'no events');
  H.Free;
  E.Free;
  C.Free;
  S.Free;
  DS.Free;
end;

procedure TestList(F: TForm);
var
  DS: TTestDataSet;
  S: TDataSource;
  Box: TComponent;
  C: TDBComboBox;
  H: THandlers;
begin
  Section('list style: display');
  DS := NewDS(nil);
  Box := TComponent.Create(nil);
  S := TDataSource.Create(Box);
  S.DataSet := DS;
  C := NewCombo(F, S, csDropDownList);
  H := THandlers.Create;
  C.OnChange := H.Change;
  C.OnClick := H.Click;
  CheckEqInt(C.ItemIndex, -1, 'closed dataset: no selection');
  DS.Open;
  CheckEqInt(C.ItemIndex, 0, 'the item equal to the field text');
  CheckEq(C.Text, 'red', 'Text');
  DS.Next;
  CheckEqInt(C.ItemIndex, 1, 'Next');
  DS.Next;
  CheckEqInt(C.ItemIndex, -1, 'a null value: no selection');
  DS.Next;
  CheckEqInt(C.ItemIndex, -1, 'a text that is not in the list: no selection');
  CheckEq(C.Text, '', 'empty display');
  DS.Edit;
  DS.FieldByName('name').AsString := 'BLUE';
  CheckEqInt(C.ItemIndex, 2, 'the comparison ignores case');
  DS.Cancel;
  DS.First;
  CheckEq(H.Log, '', 'no events for changes that come from the data');

  Section('list style: selection');
  TakeBeeps;
  SendMessageW(C.Handle, WM_KEYDOWN, VK_DOWN, 0);
  CheckEqInt(C.ItemIndex, 1, 'Down selects the next item');
  CheckEq(StateName(DS.State), 'dsEdit', 'the selection starts the edit');
  CheckEq(DS.FieldByName('name').AsString, 'green', 'and is written at once');
  CheckEq(H.Log, 'change;click;', 'OnChange and OnClick');
  SendMessageW(C.Handle, WM_CHAR, Ord('b'), 1);
  CheckEq(DS.FieldByName('name').AsString, 'blue', 'a typed letter selects and writes');
  DS.Post;
  CheckEq(DS.StoredText, '[1|blue] [2|green] [3|NULL] [4|other]', 'stored');
  CheckEqInt(TakeBeeps, 0, 'no beep so far');

  Section('list style: rejected and ignored changes');
  H.Log := '';
  S.AutoEdit := False;
  SendMessageW(C.Handle, WM_KEYDOWN, VK_UP, 0);
  CheckEqInt(C.ItemIndex, 2, 'AutoEdit off: the selection is taken back');
  CheckEq(StateName(DS.State), 'dsBrowse', 'AutoEdit off: no edit');
  CheckEqInt(TakeBeeps, 1, 'AutoEdit off: beep');
  S.AutoEdit := True;
  C.ReadOnly := True;
  SendMessageW(C.Handle, WM_KEYDOWN, VK_UP, 0);
  CheckEqInt(C.ItemIndex, 2, 'ReadOnly: the selection change is ignored');
  CheckEq(StateName(DS.State), 'dsBrowse', 'ReadOnly: no edit');
  CheckEqInt(TakeBeeps, 0, 'ReadOnly: no beep');
  C.ReadOnly := False;
  DS.FieldByName('name').ReadOnly := True;
  SendMessageW(C.Handle, WM_KEYDOWN, VK_UP, 0);
  CheckEqInt(C.ItemIndex, 2, 'a read-only field: ignored');
  DS.FieldByName('name').ReadOnly := False;
  CheckEq(H.Log, '', 'no events for rejected changes');
  C.ItemIndex := 0;
  CheckEq(StateName(DS.State), 'dsBrowse', 'ItemIndex assigned by code: no edit');
  CheckEq(DS.FieldByName('name').AsString, 'blue', 'and nothing is written');
  DS.Next;
  DS.Prior;
  CheckEqInt(C.ItemIndex, 2, 'the control reloads');

  Section('lifetime');
  Box.Free;
  Check(C.DataSource = nil, 'destroying the source clears DataSource');
  CheckEqInt(C.ItemIndex, -1, 'and the selection');
  SendMessageW(C.Handle, WM_KEYDOWN, VK_DOWN, 0);
  CheckEqInt(C.ItemIndex, -1, 'not linked: selection changes are ignored');
  H.Free;
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
    TestDropDown(F);
    TestList(F);
  finally
    F.Free;
  end;
  WriteLn('failed checks: ', FailedChecks);
end.
