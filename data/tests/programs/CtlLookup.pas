// covers: DAT-220, DAT-221, DAT-225, DAT-226, DAT-250, DAT-251, DAT-252, DAT-253, DAT-406
program CtlLookup;
{ TDBLookupComboBox on a shown form: the list built from the list dataset without moving its cursor and rebuilt
  when its data changes, display of the item of the stored key, the null text, a selection written at once,
  clearing with Delete and Backspace, rejection and the read-only state, and the lifetime of both sources. }
uses
  Windows, Messages, SysUtils, Classes, Controls, Forms, StdCtrls, DB, DBCtrls, DataTestUtils, TestDataSet,
  CtlTestUtils;

type
  THandlers = class
  public
    Reads: Integer;
    Clicks: Integer;
    procedure GetText(Sender: TField; var Text: string; DisplayText: Boolean);
    procedure Click(Sender: TObject);
  end;

procedure THandlers.GetText(Sender: TField; var Text: string; DisplayText: Boolean);
begin
  Inc(Reads);
  Text := '<' + Sender.AsString + '>';
end;

procedure THandlers.Click(Sender: TObject);
begin
  Inc(Clicks);
end;

{ code, title (20); rows 10 ten, 20 twenty, 30 thirty; not opened }
function NewList(AOwner: TComponent): TTestDataSet;
begin
  Result := TTestDataSet.Create(AOwner);
  Result.AddDef('code', ftInteger);
  Result.AddDef('title', ftString, 20);
  Result.AddRow([IntValue(10), StringValue('ten')]);
  Result.AddRow([IntValue(20), StringValue('twenty')]);
  Result.AddRow([IntValue(30), StringValue('thirty')]);
end;

{ id, ref, must (required); rows 1 20 10, 2 NULL 10, 3 99 10, 4 10 10; not opened }
function NewData(AOwner: TComponent): TTestDataSet;
begin
  Result := TTestDataSet.Create(AOwner);
  Result.AddDef('id', ftInteger);
  Result.AddDef('ref', ftInteger);
  Result.AddDef('must', ftInteger, 0, True);
  Result.AddRow([IntValue(1), IntValue(20), IntValue(10)]);
  Result.AddRow([IntValue(2), NullValue, IntValue(10)]);
  Result.AddRow([IntValue(3), IntValue(99), IntValue(10)]);
  Result.AddRow([IntValue(4), IntValue(10), IntValue(10)]);
end;

function ItemsText(C: TDBLookupComboBox): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to C.Items.Count - 1 do
    Result := Result + C.Items[I] + ';';
end;

function Sel(C: TDBLookupComboBox): Integer;
begin
  Result := Integer(SendMessageW(C.Handle, CB_GETCURSEL, 0, 0));
end;

procedure TestList(F: TForm);
var
  LDS: TTestDataSet;
  LS: TDataSource;
  C: TDBLookupComboBox;
  K: TControl;
  H: THandlers;
begin
  Section('defaults and the list');
  LDS := NewList(nil);
  LS := TDataSource.Create(nil);
  LS.DataSet := LDS;
  C := TDBLookupComboBox.Create(F);
  C.SetBounds(10, 10, 150, 23);
  C.Parent := F;
  K := C;
  Check(K is TCustomComboBox, 'a combo box control');
  CheckEqInt(C.DropDownRows, 7, 'DropDownRows');
  CheckEq(C.NullValueText, '', 'NullValueText');
  CheckEqBool(C.ReadOnly, False, 'ReadOnly');
  CheckEq(C.KeyValue, '', 'KeyValue');
  CheckEq(C.Text, '', 'Text');
  Check((GetWindowLongPtrW(C.Handle, GWL_STYLE) and 3) = CBS_DROPDOWNLIST, 'the native style is the drop-down list');
  C.DropDownRows := 5;
  CheckEqInt(SendMessageW(C.Handle, CB_GETMINVISIBLE, 0, 0), 5, 'DropDownRows reaches the native control');
  C.ListSource := LS;
  C.KeyField := 'code';
  C.ListField := 'title';
  CheckEq(ItemsText(C), '', 'list dataset closed: empty list');
  LDS.Open;
  CheckEq(ItemsText(C), 'ten;twenty;thirty;', 'list dataset open: one item per row');
  CheckEqInt(LDS.RecNo, 1, 'the cursor of the list dataset did not move');
  LDS.Next;
  C.ListField := '';
  CheckEq(ItemsText(C), '10;20;30;', 'no ListField: the key field is shown');
  CheckEqInt(LDS.RecNo, 2, 'built from another cursor position, which stays');
  C.ListField := 'title';
  CheckEq(ItemsText(C), 'ten;twenty;thirty;', 'ListField again');

  H := THandlers.Create;
  LDS.FieldByName('title').OnGetText := H.GetText;
  LDS.Refresh;
  CheckEq(ItemsText(C), '<ten>;<twenty>;<thirty>;', 'a refresh rebuilds the list from the display texts');
  H.Reads := 0;
  LDS.First;
  LDS.Last;
  LDS.Prior;
  CheckEqInt(H.Reads, 0, 'scrolling the list dataset does not rebuild the list');
  LDS.FieldByName('title').OnGetText := nil;
  LDS.Append;
  CheckEq(ItemsText(C), 'ten;twenty;thirty;;', 'a row being inserted is in the list, still empty');
  LDS.FieldByName('code').AsInteger := 40;
  LDS.FieldByName('title').AsString := 'forty';
  CheckEq(ItemsText(C), 'ten;twenty;thirty;;', 'field writes in the list dataset do not rebuild the list');
  LDS.Post;
  CheckEq(ItemsText(C), 'ten;twenty;thirty;forty;', 'a posted row is in the list');
  LDS.Delete;
  CheckEq(ItemsText(C), 'ten;twenty;thirty;', 'a deleted row is gone');
  C.KeyField := 'nope';
  CheckEq(ItemsText(C), '', 'an unknown key field: empty list');
  C.KeyField := 'code';
  LS.Enabled := False;
  CheckEq(ItemsText(C), '', 'a disabled list source: empty list');
  LS.Enabled := True;
  CheckEq(ItemsText(C), 'ten;twenty;thirty;', 'enabled again');
  LDS.Close;
  CheckEq(ItemsText(C), '', 'list dataset closed: empty list');
  H.Free;
  C.Free;
  LS.Free;
  LDS.Free;
end;

procedure TestDisplayAndSelect(F: TForm);
var
  LDS, DS: TTestDataSet;
  LS, S: TDataSource;
  LBox, Box: TComponent;
  C: TDBLookupComboBox;
  H: THandlers;
begin
  Section('display');
  LDS := NewList(nil);
  LBox := TComponent.Create(nil);
  LS := TDataSource.Create(LBox);
  LS.DataSet := LDS;
  LDS.Open;
  LDS.Next;
  DS := NewData(nil);
  Box := TComponent.Create(nil);
  S := TDataSource.Create(Box);
  S.DataSet := DS;
  C := TDBLookupComboBox.Create(F);
  C.SetBounds(10, 10, 150, 23);
  C.Parent := F;
  C.ListSource := LS;
  C.KeyField := 'code';
  C.ListField := 'title';
  C.NullValueText := '(none)';
  C.DataSource := S;
  C.DataField := 'ref';
  H := THandlers.Create;
  C.OnClick := H.Click;
  CheckEq(C.Text, '', 'data dataset closed: empty');
  CheckEqInt(Sel(C), -1, 'no selection');
  DS.Open;
  Check(C.Field = DS.FieldByName('ref'), 'Field');
  CheckEq(C.Text, 'twenty', 'the item of the stored key');
  CheckEqInt(Sel(C), 1, 'is selected');
  CheckEq(C.KeyValue, '20', 'KeyValue');
  DS.Next;
  CheckEq(C.Text, '(none)', 'a null key: NullValueText');
  CheckEqInt(Sel(C), -1, 'null: no selection');
  CheckEq(C.KeyValue, '', 'null: KeyValue is empty');
  DS.Next;
  CheckEq(C.Text, '', 'a key that is not in the list: empty');
  CheckEqInt(Sel(C), -1, 'no selection');
  CheckEq(C.KeyValue, '99', 'KeyValue is the stored key');
  DS.Next;
  CheckEq(C.Text, 'ten', 'the last row');
  DS.First;
  LDS.Close;
  CheckEq(C.Text, '', 'list dataset closed: empty display');
  LDS.Open;
  CheckEq(C.Text, 'twenty', 'list dataset open again: the item is shown again');
  LDS.Next;

  Section('selection');
  TakeBeeps;
  SendMessageW(C.Handle, WM_KEYDOWN, VK_DOWN, 0);
  CheckEq(C.Text, 'thirty', 'Down selects the next item');
  CheckEq(StateName(DS.State), 'dsEdit', 'the selection starts the edit');
  CheckEq(DS.FieldByName('ref').AsString, '30', 'the key is written at once');
  CheckEq(C.KeyValue, '30', 'KeyValue');
  CheckEqInt(H.Clicks, 1, 'OnClick');
  DS.Post;
  CheckEq(DS.StoredText, '[1|30|10] [2|NULL|10] [3|99|10] [4|10|10]', 'stored');
  CheckEqInt(LDS.RecNo, 2, 'the list dataset was not scrolled');
  CheckEq(StateName(LDS.State), 'dsBrowse', 'nor edited');
  CheckEq(LDS.StoredText, '[10|ten] [20|twenty] [30|thirty]', 'nor changed');

  Section('clearing');
  PressKey(C.Handle, VK_DELETE);
  CheckEqBool(DS.FieldByName('ref').IsNull, True, 'Delete on the closed control clears the field');
  CheckEq(C.Text, '(none)', 'the null text is shown');
  CheckEq(StateName(DS.State), 'dsEdit', 'in edit mode');
  DS.Cancel;
  CheckEq(C.Text, 'thirty', 'Cancel restores');
  PressKey(C.Handle, VK_BACK);
  CheckEqBool(DS.FieldByName('ref').IsNull, True, 'Backspace clears too');
  DS.Cancel;
  C.DataField := 'must';
  CheckEq(C.Text, 'ten', 'a required field');
  PressKey(C.Handle, VK_DELETE);
  CheckEq(DS.FieldByName('must').AsString, '10', 'a required field is not cleared');
  CheckEq(StateName(DS.State), 'dsBrowse', 'and no edit starts');
  C.DataField := 'ref';
  CheckEqInt(TakeBeeps, 0, 'no beep so far');

  Section('rejected and ignored changes');
  H.Clicks := 0;
  S.AutoEdit := False;
  SendMessageW(C.Handle, WM_KEYDOWN, VK_UP, 0);
  CheckEq(C.Text, 'thirty', 'AutoEdit off: the selection is taken back');
  CheckEq(StateName(DS.State), 'dsBrowse', 'AutoEdit off: no edit');
  CheckEqInt(TakeBeeps, 1, 'AutoEdit off: beep');
  PressKey(C.Handle, VK_DELETE);
  CheckEq(DS.FieldByName('ref').AsString, '30', 'AutoEdit off: Delete rejected');
  CheckEqInt(TakeBeeps, 1, 'AutoEdit off: beep');
  S.AutoEdit := True;
  C.ReadOnly := True;
  SendMessageW(C.Handle, WM_KEYDOWN, VK_UP, 0);
  PressKey(C.Handle, VK_DELETE);
  CheckEq(C.Text, 'thirty', 'ReadOnly: changes are ignored');
  CheckEq(StateName(DS.State), 'dsBrowse', 'ReadOnly: no edit');
  CheckEqInt(TakeBeeps, 0, 'ReadOnly: no beep');
  C.ReadOnly := False;
  CheckEqInt(H.Clicks, 0, 'no OnClick for rejected changes');
  CheckEqBool(C.Enabled, True, 'never disabled');

  Section('lifetime');
  LBox.Free;
  Check(C.ListSource = nil, 'destroying the list source clears ListSource');
  CheckEq(ItemsText(C), '', 'and the list');
  CheckEq(C.Text, '', 'nothing is shown');
  CheckEq(C.KeyValue, '30', 'the key is still the stored one');
  Box.Free;
  Check(C.DataSource = nil, 'destroying the data source clears DataSource');
  CheckEq(C.KeyValue, '', 'no key');
  H.Free;
  C.Free;
  DS.Free;
  LDS.Free;
end;

var
  F: TForm;
begin
  Application.Initialize;
  HookBeeps;
  F := NewForm;
  try
    TestList(F);
    TestDisplayAndSelect(F);
  finally
    F.Free;
  end;
  WriteLn('failed checks: ', FailedChecks);
end.
